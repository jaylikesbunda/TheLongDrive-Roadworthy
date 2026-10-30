using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Steamworks;

namespace TldMp
{
    // Steam Datagram Relay transport. The caller owns packet/session validation
    // and invokes Poll on the Steam callback/main thread. StableCore reliable
    // journal packets stay on Critical to preserve one ordered protocol stream.
    public sealed class SteamSdrTransport : IDisposable
    {
        readonly Dictionary<ulong,HSteamNetConnection> byPeer=new Dictionary<ulong,HSteamNetConnection>();
        readonly Dictionary<uint,ulong> byHandle=new Dictionary<uint,ulong>();
        readonly IntPtr[] receiveBatch=new IntPtr[64];
        readonly Callback<SteamNetConnectionStatusChangedCallback_t> statusCallback;
        HSteamListenSocket listen=HSteamListenSocket.Invalid;
        bool disposed,legacyLanes;
        int pollPeerCursor;const double ReceiveDispatchBudgetMilliseconds=4.0;
        int healthPeerCursor,receiveBudgetHits,receiveLimitHits,sendRefusals;
        double maxReceiveMs;bool statusUnavailable;
        public event Action<ulong> PeerConnected;
        public event Action<ulong,int,string> PeerDisconnected;
        public event Action<Exception> ReceiveFailure;
        public Action<string> Trace;
        public Func<ulong,bool> AcceptPeer;
        public bool HasConnection(ulong peer){return byPeer.ContainsKey(peer);}
        internal static bool IsIncomingConnection(uint listener,uint eventListener,uint invalid)
        {return listener!=invalid && listener==eventListener;}

        public SteamSdrTransport()
        {
            statusCallback=Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
        }
        public static void ConfigureLanes(HSteamNetConnection connection)
        {
            // Equal priority prevents a reliable control backlog from starving
            // motion. Weights bound bulk share while preserving the common
            // control lane as lane zero. Dependent protocol data stays together.
            var result=SteamNetworkingSockets.ConfigureConnectionLanes(connection,3,new[]{1,1,1},new ushort[]{4,3,1});
            if(result!=EResult.k_EResultOK)throw new InvalidOperationException("Steam failed to configure three SDR traffic lanes: "+result);
        }
        public void Listen(int virtualPort)
        {
            ThrowIfDisposed();if(virtualPort<0||virtualPort>65535)throw new ArgumentOutOfRangeException("virtualPort");
            if(listen.m_HSteamListenSocket!=HSteamListenSocket.Invalid.m_HSteamListenSocket)throw new InvalidOperationException("Already listening.");
            listen=SteamNetworkingSockets.CreateListenSocketP2P(virtualPort,0,null);
            if(listen.m_HSteamListenSocket==HSteamListenSocket.Invalid.m_HSteamListenSocket)throw new InvalidOperationException("Steam P2P listen socket creation failed.");
        }
        public bool Connect(ulong peer,int virtualPort)
        {
            ThrowIfDisposed();if(peer==0||virtualPort<0||virtualPort>65535||byPeer.ContainsKey(peer))return false;
            var identity=new SteamNetworkingIdentity();identity.SetSteamID64(peer);
            HSteamNetConnection connection=SteamNetworkingSockets.ConnectP2P(ref identity,virtualPort,0,null);
            if(Trace!=null)Trace("connect peer="+peer+" handle="+connection.m_HSteamNetConnection);
            if(connection.m_HSteamNetConnection==HSteamNetConnection.Invalid.m_HSteamNetConnection)return false;
            Bind(peer,connection);return true;
        }
        void Bind(ulong peer,HSteamNetConnection connection)
        {
            if(peer==0)throw new InvalidOperationException("Steam identity did not resolve to a Steam ID.");
            HSteamNetConnection prior;if(byPeer.TryGetValue(peer,out prior)&&prior.m_HSteamNetConnection!=connection.m_HSteamNetConnection)
            {byHandle.Remove(prior.m_HSteamNetConnection);SteamNetworkingSockets.CloseConnection(prior,1000,"Replacing duplicate peer connection",false);}
            byPeer[peer]=connection;byHandle[connection.m_HSteamNetConnection]=peer;
        }
        void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t change)
        {
            if(disposed)return;var state=change.m_info.m_eState;ulong peer=change.m_info.m_identityRemote.GetSteamID64();
            if(Trace!=null)Trace("status peer="+peer+" state="+state+" listener="+change.m_info.m_hListenSocket.m_HSteamListenSocket+" localListener="+listen.m_HSteamListenSocket+" reason="+change.m_info.m_eEndReason+" detail="+change.m_info.m_szEndDebug);
            if(state==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting &&
               IsIncomingConnection(listen.m_HSteamListenSocket,change.m_info.m_hListenSocket.m_HSteamListenSocket,HSteamListenSocket.Invalid.m_HSteamListenSocket))
            {
                if(peer==0 || !AcceptRemote(peer)){SteamNetworkingSockets.CloseConnection(change.m_hConn,1002,"Peer is not a current lobby member",false);return;}
                if(SteamNetworkingSockets.AcceptConnection(change.m_hConn)!=EResult.k_EResultOK)
                {SteamNetworkingSockets.CloseConnection(change.m_hConn,1001,"Connection rejected",false);return;}
                Bind(peer,change.m_hConn);return;
            }
            if(state==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                if(peer==0)peer=PeerFor(change.m_hConn);if(peer==0 || !AcceptRemote(peer)){SteamNetworkingSockets.CloseConnection(change.m_hConn,1002,"Peer is not a current lobby member",false);return;}
                HSteamNetConnection existing;if(!byPeer.TryGetValue(peer,out existing))Bind(peer,change.m_hConn);
                else if(existing.m_HSteamNetConnection==change.m_hConn.m_HSteamNetConnection)
                {
                    try
                    {
                        if(!legacyLanes)
                        {
                            try{ConfigureLanes(change.m_hConn);}
                            catch(EntryPointNotFoundException)
                            {
                                // The game's older steam_api64 has no lane API.
                                // Its default ordered lane supports every packet.
                                legacyLanes=true;
                                if(Trace!=null)Trace("lane API unavailable; using default Steam lane");
                            }
                        }
                    }
                    catch(Exception e)
                    {
                        Unbind(peer,change.m_hConn);
                        SteamNetworkingSockets.CloseConnection(change.m_hConn,1003,"Failed to configure SDR traffic lanes",false);
                        var failure=ReceiveFailure;if(failure!=null)failure(e);return;
                    }
                    var connected=PeerConnected;if(connected!=null)connected(peer);
                }
                return;
            }
            if(state==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer||
               state==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally||
               state==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Dead)
            {
                if(peer==0)peer=PeerFor(change.m_hConn);bool current=peer!=0&&Unbind(peer,change.m_hConn);
                var disconnected=PeerDisconnected;if(current&&disconnected!=null)disconnected(peer,change.m_info.m_eEndReason,change.m_info.m_szEndDebug);return;
            }
        }
        bool AcceptRemote(ulong peer){var accept=AcceptPeer;return accept!=null && accept(peer);}
        ulong PeerFor(HSteamNetConnection c){ulong p;return byHandle.TryGetValue(c.m_HSteamNetConnection,out p)?p:0;}
        bool Unbind(ulong peer,HSteamNetConnection c)
        {HSteamNetConnection current;bool removed=byPeer.TryGetValue(peer,out current)&&current.m_HSteamNetConnection==c.m_HSteamNetConnection;if(removed)byPeer.Remove(peer);byHandle.Remove(c.m_HSteamNetConnection);return removed;}

        // Send is admission to Steam's socket queue, not remote delivery.
        // Reliable refusal returns false so StableCore can retain FIFO data.
        public bool Send(ulong peer,byte[] data,NetDeliveryLane lane,bool reliable)
        {
            ThrowIfDisposed();if(data==null||data.Length==0||data.Length>Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend||(int)lane<0||(int)lane>2)return false;
            HSteamNetConnection connection;if(!byPeer.TryGetValue(peer,out connection))return false;
            if(legacyLanes)
            {
                // Use the older byte-buffer API too: do not write the newer
                // message structure (with m_idxLane) into an older allocation.
                var pinned=GCHandle.Alloc(data,GCHandleType.Pinned);
                try
                {
                    long number;
                    int flags=reliable?Constants.k_nSteamNetworkingSend_Reliable:lane==NetDeliveryLane.Motion?Constants.k_nSteamNetworkingSend_UnreliableNoNagle:Constants.k_nSteamNetworkingSend_Unreliable;
                    bool accepted=SteamNetworkingSockets.SendMessageToConnection(connection,pinned.AddrOfPinnedObject(),(uint)data.Length,flags,out number)==EResult.k_EResultOK;
                    if(!accepted)sendRefusals++;return accepted;
                }
                finally{pinned.Free();}
            }
            IntPtr message=SteamNetworkingUtils.AllocateMessage(data.Length);if(message==IntPtr.Zero)return false;
            try
            {
                var native=SteamNetworkingMessage_t.FromIntPtr(message);Marshal.Copy(data,0,native.m_pData,data.Length);
                native.m_conn=connection;native.m_cbSize=data.Length;native.m_idxLane=(ushort)lane;
                // Motion (latest-value poses, live usables) skips Steam's ~5 ms Nagle batching.
                native.m_nFlags=reliable?Constants.k_nSteamNetworkingSend_Reliable:lane==NetDeliveryLane.Motion?Constants.k_nSteamNetworkingSend_UnreliableNoNagle:Constants.k_nSteamNetworkingSend_Unreliable;
                Marshal.StructureToPtr(native,message,false);
                var result=new long[1];SteamNetworkingSockets.SendMessages(1,new[]{message},result);
                message=IntPtr.Zero; // SendMessages consumes the message on every result.
                if(result[0]<=0)sendRefusals++;return result[0]>0;
            }
            finally{if(message!=IntPtr.Zero)SteamNetworkingMessage_t.Release(message);}
        }
        public int Poll(Action<ulong,byte[]> received,int maxMessages=256)
        {
            // Do NOT call SteamNetworkingSockets.RunCallbacks() here. This build
            // obtains the Steamworks SteamNetworkingSockets012 interface and
            // registers connection status through Steamworks.NET Callback<T>,
            // which the game already pumps via SteamAPI.RunCallbacks() (manual
            // dispatch) in SteamManager.Update. Valve documents
            // ISteamNetworkingSockets::RunCallbacks() as only needed for the
            // standalone GameNetworkingSockets queued-function-pointer path, and
            // explicitly not for applications using Steam's callback dispatch.
            // Calling it here would be a second, interface-specific pump that
            // this transport does not use. SteamTransportPumpTests guards this.
            ThrowIfDisposed();long started=Stopwatch.GetTimestamp();if(received==null||maxMessages<=0)return 0;int handled=0;var connections=new List<KeyValuePair<ulong,HSteamNetConnection>>(byPeer);
            int connectionCount=connections.Count;if(connectionCount==0){pollPeerCursor=0;return 0;}
            int cursor=pollPeerCursor%connectionCount;if(cursor<0)cursor+=connectionCount;
            while(handled<maxMessages && InReceiveBudget(started))
            {
                bool madeProgress=false;
                for(int step=0;step<connectionCount&&handled<maxMessages&&InReceiveBudget(started);step++)
                {
                    int index=(cursor+step)%connectionCount;var pair=connections[index];receiveBatch[0]=IntPtr.Zero;
                    int n=SteamNetworkingSockets.ReceiveMessagesOnConnection(pair.Value,receiveBatch,1);
                    if(n<=0)continue;madeProgress=true;int available=Math.Min(n,receiveBatch.Length);
                    for(int i=0;i<available;i++)
                    {
                        IntPtr ptr=receiveBatch[i];receiveBatch[i]=IntPtr.Zero;
                        try
                        {
                            var message=SteamNetworkingMessage_t.FromIntPtr(ptr);
                            if(message.m_cbSize>=1&&message.m_cbSize<=Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend)
                            {
                                var bytes=new byte[message.m_cbSize];Marshal.Copy(message.m_pData,bytes,0,bytes.Length);
                                try{received(pair.Key,bytes);}catch(Exception e){var failure=ReceiveFailure;if(failure!=null)failure(e);}
                            }
                        }
                        finally{SteamNetworkingMessage_t.Release(ptr);handled++;}
                    }
                    // Steam currently honors nMaxMessages=1. If it returns more,
                    // release every native allocation even if dispatch failed.
                    for(int i=available;i<n;i++)
                    {IntPtr extra=receiveBatch[i];receiveBatch[i]=IntPtr.Zero;if(extra!=IntPtr.Zero)SteamNetworkingMessage_t.Release(extra);}
                }
                cursor=(cursor+1)%connectionCount;pollPeerCursor=cursor;if(!madeProgress)break;
            }
            double elapsed=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
            if(elapsed>maxReceiveMs)maxReceiveMs=elapsed;
            if(handled>=maxMessages)receiveLimitHits++;
            if(elapsed>=ReceiveDispatchBudgetMilliseconds)receiveBudgetHits++;
            return handled;
        }
        // Called once per health window, on the main thread. The older textual
        // status API avoids marshalling newer status/lane structs into the game's
        // older Steam runtime. Query at most one peer per window, rotating fairly.
        public string TakeHealthSummary()
        {
            string summary="steam{legacyLane="+legacyLanes+" receiveBudgetHits="+receiveBudgetHits+" receiveLimitHits="+receiveLimitHits+
                " maxReceiveMs="+maxReceiveMs.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" sendRefusals="+sendRefusals+"}";
            receiveBudgetHits=receiveLimitHits=sendRefusals=0;maxReceiveMs=0;
            if(statusUnavailable || byPeer.Count==0)return summary;
            var peers=new List<ulong>(byPeer.Keys);ulong peer=peers[healthPeerCursor++%peers.Count];
            try
            {
                string detail;int result=SteamNetworkingSockets.GetDetailedConnectionStatus(byPeer[peer],out detail,8192);
                if(result>=0 && !String.IsNullOrEmpty(detail))
                {
                    detail=detail.Replace('\r',' ').Replace('\n',' ');
                    if(detail.Length>4096)detail=detail.Substring(0,4096);
                    summary+=" steamStatusPeer="+peer+" status="+detail;
                }
            }
            catch(EntryPointNotFoundException){statusUnavailable=true;summary+=" steamStatus=unsupported";}
            catch(Exception e){statusUnavailable=true;summary+=" steamStatus=unavailable:"+e.GetType().Name;}
            return summary;
        }
        static bool InReceiveBudget(long started)
        {return (Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency<ReceiveDispatchBudgetMilliseconds;}
        public bool Close(ulong peer,string reason)
        {HSteamNetConnection c;if(!byPeer.TryGetValue(peer,out c))return false;Unbind(peer,c);return SteamNetworkingSockets.CloseConnection(c,1000,reason??"Closed",false);}
        public void Dispose()
        {
            if(disposed)return;disposed=true;statusCallback.Dispose();
            foreach(var pair in byPeer)SteamNetworkingSockets.CloseConnection(pair.Value,1000,"Session closed",false);
            byPeer.Clear();byHandle.Clear();if(listen.m_HSteamListenSocket!=HSteamListenSocket.Invalid.m_HSteamListenSocket)SteamNetworkingSockets.CloseListenSocket(listen);
        }
        void ThrowIfDisposed(){if(disposed)throw new ObjectDisposedException("SteamSdrTransport");}
    }
}
