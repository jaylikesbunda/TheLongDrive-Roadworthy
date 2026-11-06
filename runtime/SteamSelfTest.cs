using System;
using System.IO;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace TldMp
{
    // One-account Steam networking self-test (diagnostics only, read-only for the game).
    // Enabled by TLD_MP_STEAM_SELFTEST=1 or a "tldmp-steam-selftest.on" file next to
    // TheLongDrive.exe. Runs once after SteamManager initialises and logs
    // "[TLD MP steam-selftest]" lines to Player.log. It checks, with no second player:
    //   A  SteamNetworkingSockets socket pair: send/receive both ways (binding v012 vs DLL v008)
    //   B  ISteamNetworking v006 P2P packet to our own Steam ID (the API vanilla uses)
    //   C  SteamNetworkingSockets ConnectP2P to our own Steam ID: status callbacks decode
    //   D  Relay network status
    // If the game crashes or freezes during a step, the last logged step is the culprit.
    internal sealed class SteamSelfTest : MonoBehaviour
    {
        const string Flag="tldmp-steam-selftest.on";
        const int SelfChannel=7,SelfPort=4242;
        int step;float stepStarted,nextLog;ulong own;
        HSteamNetConnection pairA,pairB,self;HSteamListenSocket listen=HSteamListenSocket.Invalid;
        Callback<SteamNetConnectionStatusChangedCallback_t> status;
        bool pairAB,pairBA,selfIncoming,selfConnected,selfEcho;int callbacks;
        string pResult="not run",aResult="not run",cResult="not run",dResult="not run";

        internal static void StartIfRequested(GameObject host)
        {
            bool requested=Environment.GetEnvironmentVariable("TLD_MP_STEAM_SELFTEST")=="1";
            if(!requested)
                try
                {
                    string managed=Path.GetDirectoryName(typeof(SteamSelfTest).Assembly.Location);
                    string game=Directory.GetParent(Directory.GetParent(managed).FullName).FullName;
                    requested=File.Exists(Path.Combine(game,Flag));
                }
                catch{}
            if(requested)host.AddComponent<SteamSelfTest>();
        }
        static void Log(string text){Debug.Log("[TLD MP steam-selftest] "+text);}
        static byte[] Pattern(int seed){var b=new byte[64];for(int i=0;i<b.Length;i++)b[i]=(byte)(seed*31+i*7);return b;}
        static bool Same(byte[] a,byte[] b){if(a==null||b==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        void Next(){step++;stepStarted=Time.realtimeSinceStartup;nextLog=0;}
        float Elapsed{get{return Time.realtimeSinceStartup-stepStarted;}}

        void Update()
        {
            try{Run();}
            catch(Exception e){Log("step "+step+" threw "+e.GetType().Name+": "+e.Message);Finish();}
        }
        void Run()
        {
            switch(step)
            {
                case 0:
                    if(!SteamManager.Initialized || Time.realtimeSinceStartup<5f)return;
                    own=(ulong)SteamUser.GetSteamID();
                    Log("start own="+own+" binding=SteamNetworkingSockets012/Utils004/Networking006; see Player.log for each step");
                    Next();return;
                case 1:
                {
                    Log("A socket pair: creating");
                    var id1=new SteamNetworkingIdentity();id1.SetSteamID64(own);var id2=new SteamNetworkingIdentity();id2.SetSteamID64(own);
                    bool ok=SteamNetworkingSockets.CreateSocketPair(out pairA,out pairB,false,ref id1,ref id2);
                    Log("A CreateSocketPair ok="+ok+" a="+pairA.m_HSteamNetConnection+" b="+pairB.m_HSteamNetConnection);
                    if(!ok){aResult="FAIL CreateSocketPair returned false";Next();Next();return;}
                    Log("A send a->b: "+SendSocket(pairA,Pattern(1))+", b->a: "+SendSocket(pairB,Pattern(2)));
                    Next();return;
                }
                case 2:
                    if(!pairAB)pairAB=ReceiveSocket(pairB,Pattern(1));
                    if(!pairBA)pairBA=ReceiveSocket(pairA,Pattern(2));
                    if((pairAB && pairBA) || Elapsed>3f)
                    {
                        aResult=(pairAB&&pairBA?"PASS":"FAIL")+" a->b="+pairAB+" b->a="+pairBA;Log("A "+aResult);
                        SteamNetworkingSockets.CloseConnection(pairA,0,"self-test",false);SteamNetworkingSockets.CloseConnection(pairB,0,"self-test",false);
                        Next();
                    }
                    return;
                case 3:
                {
                    var data=Pattern(3);bool sent=SteamNetworking.SendP2PPacket(new CSteamID(own),data,(uint)data.Length,EP2PSend.k_EP2PSendReliable,SelfChannel);
                    Log("B P2P v006 send-to-self accepted="+sent);
                    if(!sent){pResult="FAIL SendP2PPacket refused";Next();Next();return;}
                    Next();return;
                }
                case 4:
                {
                    uint size;
                    if(SteamNetworking.IsP2PPacketAvailable(out size,SelfChannel))
                    {
                        var buffer=new byte[size];uint read;CSteamID remote;
                        bool ok=SteamNetworking.ReadP2PPacket(buffer,size,out read,out remote,SelfChannel);
                        bool match=ok && Same(Pattern(3),buffer) && (ulong)remote==own;
                        pResult=(match?"PASS":"FAIL")+" read="+ok+" bytes="+read+" from="+(ulong)remote;Log("B "+pResult);Next();
                    }
                    else if(Elapsed>5f){pResult="INCONCLUSIVE no self-delivery in 5 s (Steam may not loop P2P back to the sender)";Log("B "+pResult);Next();}
                    return;
                }
                case 5:
                {
                    status=Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);
                    listen=SteamNetworkingSockets.CreateListenSocketP2P(SelfPort,0,null);
                    Log("C CreateListenSocketP2P handle="+listen.m_HSteamListenSocket);
                    var identity=new SteamNetworkingIdentity();identity.SetSteamID64(own);
                    self=SteamNetworkingSockets.ConnectP2P(ref identity,SelfPort,0,null);
                    Log("C ConnectP2P(self) handle="+self.m_HSteamNetConnection);
                    Next();return;
                }
                case 6:
                    if(selfConnected && !selfEcho && Elapsed>0.5f){Log("C sending over the self connection: "+SendSocket(self,Pattern(4)));selfEcho=true;}
                    if(Elapsed>15f)
                    {
                        cResult="callbacks="+callbacks+" incoming="+selfIncoming+" connected="+selfConnected+
                            (callbacks==0?" (FAIL: no status callbacks decoded)":"");
                        Log("C "+cResult);
                        if(self.m_HSteamNetConnection!=0)SteamNetworkingSockets.CloseConnection(self,0,"self-test",false);
                        if(listen.m_HSteamListenSocket!=HSteamListenSocket.Invalid.m_HSteamListenSocket)SteamNetworkingSockets.CloseListenSocket(listen);
                        Next();
                    }
                    return;
                case 7:
                    Log("D InitRelayNetworkAccess");SteamNetworkingUtils.InitRelayNetworkAccess();Next();return;
                case 8:
                    if(Time.realtimeSinceStartup>=nextLog || Elapsed>20f)
                    {
                        nextLog=Time.realtimeSinceStartup+2f;SteamRelayNetworkStatus_t relay;
                        var avail=SteamNetworkingUtils.GetRelayNetworkStatus(out relay);
                        Log("D relay status="+avail+" avail="+relay.m_eAvail+" config="+relay.m_eAvailNetworkConfig+" anyRelay="+relay.m_eAvailAnyRelay+" pinging="+relay.m_bPingMeasurementInProgress);
                        if(Elapsed>20f){dResult=avail.ToString();Finish();}
                    }
                    return;
            }
        }
        void OnStatus(SteamNetConnectionStatusChangedCallback_t change)
        {
            callbacks++;var info=change.m_info;ulong remote=info.m_identityRemote.GetSteamID64();
            Log("C status conn="+change.m_hConn.m_HSteamNetConnection+" state="+info.m_eState+" remote="+remote+(remote==own?" (own id ok)":" (UNEXPECTED id)")+
                " listener="+info.m_hListenSocket.m_HSteamListenSocket+" reason="+info.m_eEndReason+" detail="+info.m_szEndDebug);
            if(info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting &&
               info.m_hListenSocket.m_HSteamListenSocket==listen.m_HSteamListenSocket && listen.m_HSteamListenSocket!=0)
            {selfIncoming=true;Log("C AcceptConnection="+SteamNetworkingSockets.AcceptConnection(change.m_hConn));}
            if(info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)selfConnected=true;
        }
        static string SendSocket(HSteamNetConnection connection,byte[] data)
        {
            var pinned=GCHandle.Alloc(data,GCHandleType.Pinned);
            try{long number;return SteamNetworkingSockets.SendMessageToConnection(connection,pinned.AddrOfPinnedObject(),(uint)data.Length,Constants.k_nSteamNetworkingSend_Reliable,out number).ToString();}
            finally{pinned.Free();}
        }
        static bool ReceiveSocket(HSteamNetConnection connection,byte[] expected)
        {
            var batch=new IntPtr[1];int n=SteamNetworkingSockets.ReceiveMessagesOnConnection(connection,batch,1);
            if(n<=0 || batch[0]==IntPtr.Zero)return false;
            try
            {
                var message=SteamNetworkingMessage_t.FromIntPtr(batch[0]);
                if(message.m_cbSize<1 || message.m_cbSize>1<<20){Log("A received implausible size="+message.m_cbSize+" (struct layout mismatch?)");return false;}
                var bytes=new byte[message.m_cbSize];Marshal.Copy(message.m_pData,bytes,0,bytes.Length);
                bool same=Same(bytes,expected);if(!same)Log("A received "+bytes.Length+" bytes that do not match what was sent");
                return same;
            }
            finally{SteamNetworkingMessage_t.Release(batch[0]);}
        }
        void Finish()
        {
            Log("SUMMARY socketPair="+aResult+" | p2pV006="+pResult+" | connectP2PSelf="+cResult+" | relay="+dResult);
            if(status!=null){status.Dispose();status=null;}
            step=99;enabled=false;
        }
    }
}
