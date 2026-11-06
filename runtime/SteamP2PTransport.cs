using System;
using System.Collections.Generic;
using System.Diagnostics;
using Steamworks;

namespace TldMp
{
    // Steam P2P transport over ISteamNetworking v006: the same API vanilla
    // multiplayer uses. The game ships an older steam_api64.dll (SteamNetworkingSockets
    // v008) than its Steamworks.NET binding requests (v012); ISteamNetworking v006
    // matches on both sides, so this path does not depend on that mismatch.
    // Opt-in fallback (TLD_MP_TRANSPORT=p2p); SDR is the default because the in-game
    // self-test showed the SteamNetworkingSockets calls work on this DLL.
    //
    // Vanilla's hood.ReadMessage is replaced by the bridge (ReadBudgeted), so this
    // transport owns P2P reads. Channels:
    //   DataChannel    - protocol bytes, unchanged (reliable and unreliable)
    //   ControlChannel - one-byte frames: Hello (guest->host), Welcome (host->guest),
    //                    Bye, KeepAlive. A peer is "connected" only after Hello/Welcome,
    //                    which gives the same Connected/Disconnected events as SDR.
    // Steam authenticates the remote Steam ID of every P2P packet.
    public sealed class SteamP2PTransport : ISteamTransport
    {
        internal const int DataChannel=1,ControlChannel=2;
        internal const byte Hello=1,Welcome=2,Bye=3,KeepAlive=4;
        internal const int UnreliableLimit=1200,ReliableLimit=1024*1024;
        // Static (not const) so the simulation tests can shorten them; production never changes them.
        internal static double HelloSeconds=1.0,KeepAliveSeconds=2.0,ConnectTimeoutSeconds=10.0,IdleTimeoutSeconds=20.0,PendingMemberSeconds=15.0;
        const double ReceiveDispatchBudgetMilliseconds=4.0;

        sealed class Peer
        {
            internal ulong Id;internal bool Connected,Outgoing;
            internal long Started,LastReceive,NextHello,NextKeepAlive;
        }
        readonly Dictionary<ulong,Peer> peers=new Dictionary<ulong,Peer>();
        readonly Dictionary<ulong,long> pendingSessions=new Dictionary<ulong,long>();
        readonly Callback<P2PSessionRequest_t> sessionRequest;
        readonly Callback<P2PSessionConnectFail_t> sessionFail;
        readonly byte[] control=new byte[1];
        byte[] buffer=new byte[64*1024];
        bool disposed,listening;
        int receiveBudgetHits,receiveLimitHits,sendRefusals,droppedUnknown,healthCursor;double maxReceiveMs;

        public event Action<ulong> PeerConnected;
        public event Action<ulong,int,string> PeerDisconnected;
        public event Action<Exception> ReceiveFailure;
        public Action<string> Trace { get; set; }
        public Func<ulong,bool> AcceptPeer { get; set; }

        public SteamP2PTransport()
        {
            sessionRequest=Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            sessionFail=Callback<P2PSessionConnectFail_t>.Create(OnSessionFail);
        }
        static long Ticks(double seconds){return (long)(seconds*Stopwatch.Frequency);}
        void Log(string text){var trace=Trace;if(trace!=null)trace(text);}
        bool Allowed(ulong peer){var accept=AcceptPeer;return peer!=0 && accept!=null && accept(peer);}

        public bool HasConnection(ulong peer){return peers.ContainsKey(peer);}
        public void Listen(int virtualPort)
        {
            ThrowIfDisposed();listening=true;
            Log("p2p listening (ISteamNetworking v006, data channel "+DataChannel+", control channel "+ControlChannel+")");
        }
        public bool Connect(ulong peer,int virtualPort)
        {
            ThrowIfDisposed();if(peer==0 || peers.ContainsKey(peer))return false;
            long now=Stopwatch.GetTimestamp();
            peers[peer]=new Peer{Id=peer,Outgoing=true,Started=now,LastReceive=now,NextHello=now};
            Log("p2p connect peer="+peer);
            SendControl(peer,Hello);peers[peer].NextHello=now+Ticks(HelloSeconds);
            return true;
        }
        void OnSessionRequest(P2PSessionRequest_t request)
        {
            if(disposed)return;ulong peer=(ulong)request.m_steamIDRemote;
            if(Allowed(peer)){SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);pendingSessions.Remove(peer);return;}
            // The lobby member list can lag the joiner's first packet: hold, re-check.
            if(peer!=0 && !pendingSessions.ContainsKey(peer))
            {
                pendingSessions[peer]=Stopwatch.GetTimestamp()+Ticks(PendingMemberSeconds);
                Log("p2p holding session from peer="+peer+" until the lobby member list shows it");
            }
        }
        void OnSessionFail(P2PSessionConnectFail_t failure)
        {
            if(disposed)return;ulong peer=(ulong)failure.m_steamIDRemote;
            Log("p2p session failure peer="+peer+" error="+failure.m_eP2PSessionError);
            Drop(peer,4000+failure.m_eP2PSessionError,"Steam P2P session failed (EP2PSessionError "+failure.m_eP2PSessionError+")");
        }
        void Drop(ulong peer,int reason,string detail)
        {
            Peer state;if(!peers.TryGetValue(peer,out state))return;
            peers.Remove(peer);
            var disconnected=PeerDisconnected;if(disconnected!=null)disconnected(peer,reason,detail);
        }
        void MarkConnected(Peer state)
        {
            if(state.Connected)return;state.Connected=true;
            Log("p2p connected peer="+state.Id);
            var connected=PeerConnected;if(connected!=null)connected(state.Id);
        }
        bool SendControl(ulong peer,byte kind)
        {control[0]=kind;return SteamNetworking.SendP2PPacket(new CSteamID(peer),control,1,EP2PSend.k_EP2PSendReliable,ControlChannel);}

        // Send is admission to Steam's queue, not remote delivery (same contract as SDR).
        public bool Send(ulong peer,byte[] data,NetDeliveryLane lane,bool reliable)
        {
            ThrowIfDisposed();if(data==null || data.Length==0 || data.Length>ReliableLimit || (int)lane<0 || (int)lane>2)return false;
            Peer state;if(!peers.TryGetValue(peer,out state) || !state.Connected)return false;
            // Unreliable P2P packets are limited to ~1200 bytes; larger ones go reliable.
            EP2PSend mode=reliable || data.Length>UnreliableLimit ? EP2PSend.k_EP2PSendReliable :
                lane==NetDeliveryLane.Motion ? EP2PSend.k_EP2PSendUnreliableNoDelay : EP2PSend.k_EP2PSendUnreliable;
            bool accepted=SteamNetworking.SendP2PPacket(new CSteamID(peer),data,(uint)data.Length,mode,DataChannel);
            if(!accepted)sendRefusals++;return accepted;
        }

        public int Poll(Action<ulong,byte[]> received,int maxMessages=256)
        {
            ThrowIfDisposed();long started=Stopwatch.GetTimestamp();
            Maintain(started);
            ReadControl();
            if(received==null || maxMessages<=0)return 0;
            int handled=0;uint size;
            while(handled<maxMessages && InReceiveBudget(started) && SteamNetworking.IsP2PPacketAvailable(out size,DataChannel))
            {
                if(size==0 || size>ReliableLimit){Discard(size,DataChannel);continue;}
                if(buffer.Length<size)buffer=new byte[Math.Max(size,(uint)buffer.Length*2)];
                uint read;CSteamID remote;
                if(!SteamNetworking.ReadP2PPacket(buffer,size,out read,out remote,DataChannel))break;
                handled++;ulong peer=(ulong)remote;Peer state;
                if(read==0 || !peers.TryGetValue(peer,out state) || !state.Connected){droppedUnknown++;continue;}
                state.LastReceive=Stopwatch.GetTimestamp();
                var bytes=new byte[read];Buffer.BlockCopy(buffer,0,bytes,0,(int)read);
                try{received(peer,bytes);}catch(Exception e){var failure=ReceiveFailure;if(failure!=null)failure(e);}
            }
            double elapsed=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
            if(elapsed>maxReceiveMs)maxReceiveMs=elapsed;
            if(handled>=maxMessages)receiveLimitHits++;
            if(elapsed>=ReceiveDispatchBudgetMilliseconds)receiveBudgetHits++;
            return handled;
        }
        void Discard(uint size,int channel)
        {
            var scratch=new byte[Math.Max(1u,Math.Min(size,(uint)ReliableLimit))];uint read;CSteamID remote;
            SteamNetworking.ReadP2PPacket(scratch,(uint)scratch.Length,out read,out remote,channel);
        }
        void ReadControl()
        {
            uint size;var frame=new byte[16];int guard=0;
            while(++guard<=256 && SteamNetworking.IsP2PPacketAvailable(out size,ControlChannel))
            {
                if(size>frame.Length){Discard(size,ControlChannel);continue;}
                uint read;CSteamID remote;
                if(!SteamNetworking.ReadP2PPacket(frame,(uint)frame.Length,out read,out remote,ControlChannel))break;
                if(read<1)continue;ulong peer=(ulong)remote;byte kind=frame[0];Peer state;
                peers.TryGetValue(peer,out state);
                if(kind==Hello)
                {
                    if(!listening || !Allowed(peer)){Log("p2p ignoring hello from peer="+peer+(listening?" (not a lobby member)":" (not hosting)"));continue;}
                    if(state==null){long now=Stopwatch.GetTimestamp();state=new Peer{Id=peer,Started=now,LastReceive=now,NextKeepAlive=now+Ticks(KeepAliveSeconds)};peers[peer]=state;}
                    state.LastReceive=Stopwatch.GetTimestamp();
                    SendControl(peer,Welcome);MarkConnected(state);
                }
                else if(kind==Welcome)
                {
                    if(state==null || !state.Outgoing)continue;
                    state.LastReceive=Stopwatch.GetTimestamp();state.NextKeepAlive=state.LastReceive+Ticks(KeepAliveSeconds);
                    MarkConnected(state);
                }
                else if(kind==KeepAlive){if(state!=null)state.LastReceive=Stopwatch.GetTimestamp();}
                else if(kind==Bye){if(state!=null){Log("p2p peer="+peer+" closed the connection");Drop(peer,1000,"Peer closed the connection");}}
            }
        }
        void Maintain(long now)
        {
            if(pendingSessions.Count>0)
                foreach(var pair in new List<KeyValuePair<ulong,long>>(pendingSessions))
                {
                    if(Allowed(pair.Key)){pendingSessions.Remove(pair.Key);SteamNetworking.AcceptP2PSessionWithUser(new CSteamID(pair.Key));Log("p2p lobby member list now shows peer="+pair.Key+"; session accepted");}
                    else if(now>=pair.Value){pendingSessions.Remove(pair.Key);Log("p2p dropping held session from peer="+pair.Key+": not a lobby member");}
                }
            if(peers.Count==0)return;
            foreach(var state in new List<Peer>(peers.Values))
            {
                if(!state.Connected)
                {
                    if(now-state.Started>Ticks(ConnectTimeoutSeconds)){Log("p2p connect timed out peer="+state.Id);Drop(state.Id,4100,"No answer from host within "+ConnectTimeoutSeconds+" s");continue;}
                    if(state.Outgoing && now>=state.NextHello){SendControl(state.Id,Hello);state.NextHello=now+Ticks(HelloSeconds);}
                    continue;
                }
                if(now-state.LastReceive>Ticks(IdleTimeoutSeconds)){Log("p2p peer timed out peer="+state.Id);Drop(state.Id,4101,"No traffic from peer for "+IdleTimeoutSeconds+" s");continue;}
                if(now>=state.NextKeepAlive){SendControl(state.Id,KeepAlive);state.NextKeepAlive=now+Ticks(KeepAliveSeconds);}
            }
        }
        static bool InReceiveBudget(long started)
        {return (Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency<ReceiveDispatchBudgetMilliseconds;}

        public bool Close(ulong peer,string reason)
        {
            Peer state;if(!peers.TryGetValue(peer,out state))return false;
            SendControl(peer,Bye);peers.Remove(peer);
            Log("p2p closed peer="+peer+" reason="+(reason??"Closed"));
            return true;
        }
        public string TakeHealthSummary()
        {
            string summary="steam{transport=p2p-v006 peers="+peers.Count+" receiveBudgetHits="+receiveBudgetHits+" receiveLimitHits="+receiveLimitHits+
                " maxReceiveMs="+maxReceiveMs.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" sendRefusals="+sendRefusals+" droppedUnknown="+droppedUnknown;
            receiveBudgetHits=receiveLimitHits=sendRefusals=droppedUnknown=0;maxReceiveMs=0;
            if(peers.Count>0)
            {
                var ids=new List<ulong>(peers.Keys);ulong peer=ids[healthCursor++%ids.Count];P2PSessionState_t state;
                if(SteamNetworking.GetP2PSessionState(new CSteamID(peer),out state))
                    summary+=" session{peer="+peer+" active="+state.m_bConnectionActive+" connecting="+state.m_bConnecting+" relay="+state.m_bUsingRelay+
                        " error="+state.m_eP2PSessionError+" queuedBytes="+state.m_nBytesQueuedForSend+" queuedPackets="+state.m_nPacketsQueuedForSend+"}";
            }
            return summary+"}";
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var peer in new List<ulong>(peers.Keys))SendControl(peer,Bye);
            peers.Clear();pendingSessions.Clear();sessionRequest.Dispose();sessionFail.Dispose();
        }
        void ThrowIfDisposed(){if(disposed)throw new ObjectDisposedException("SteamP2PTransport");}
    }
}
