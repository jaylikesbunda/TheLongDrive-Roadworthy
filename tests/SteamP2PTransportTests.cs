using System;
using System.Collections.Generic;
using System.Threading;
using Steamworks;
using TldMp;

// Production SteamP2PTransport against the simulated ISteamNetworking v006 boundary.
public static class SteamP2PTransportTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}

    sealed class Side
    {
        public ulong Id;public SteamP2PTransport T;public bool Member=true;public ulong Other;
        public readonly List<ulong> Connected=new List<ulong>();public readonly List<string> Disconnected=new List<string>();
        public readonly List<byte[]> Received=new List<byte[]>();
        public Side(ulong id)
        {
            Id=id;Simulation.Current=id;T=new SteamP2PTransport();
            T.AcceptPeer=delegate(ulong p){return Member && p==Other;};
            T.PeerConnected+=delegate(ulong p){Connected.Add(p);};
            T.PeerDisconnected+=delegate(ulong p,int r,string d){Disconnected.Add(p+":"+r);};
        }
        public void Poll(){Simulation.Current=Id;T.Poll(delegate(ulong from,byte[] b){Received.Add(b);});}
    }
    static void Pump(Side a,Side b,int frames,int sleepMs)
    {
        for(int i=0;i<frames;i++){a.Poll();P2PSimulation.Pump();b.Poll();P2PSimulation.Pump();if(sleepMs>0)Thread.Sleep(sleepMs);}
    }
    static void Pair(out Side host,out Side guest)
    {
        Simulation.Callbacks.Clear();P2PSimulation.Reset();
        host=new Side(101);guest=new Side(202);host.Other=guest.Id;guest.Other=host.Id;
        Simulation.Current=host.Id;host.T.Listen(0);
    }

    public static int Main()
    {
        try
        {
            SteamP2PTransport.HelloSeconds=0.02;SteamP2PTransport.KeepAliveSeconds=0.05;
            SteamP2PTransport.ConnectTimeoutSeconds=0.6;SteamP2PTransport.IdleTimeoutSeconds=0.4;SteamP2PTransport.PendingMemberSeconds=0.3;
            Side host,guest;

            // 1. Normal join, data both ways, unreliable fallback for large packets.
            Pair(out host,out guest);
            Simulation.Current=guest.Id;Check(guest.T.Connect(host.Id,0),"guest connect returned false");
            Check(guest.T.HasConnection(host.Id),"guest has no pending connection after Connect");
            Simulation.Current=guest.Id;Check(!guest.T.Send(host.Id,new byte[]{9},NetDeliveryLane.Critical,true),"data sent before the handshake completed");
            Pump(host,guest,20,2);
            Check(host.Connected.Count==1 && host.Connected[0]==guest.Id,"host did not see the guest connect");
            Check(guest.Connected.Count==1 && guest.Connected[0]==host.Id,"guest did not see the host connect");
            Check(P2PSimulation.Requests>=1,"the host never received a P2P session request");
            Simulation.Current=guest.Id;Check(guest.T.Send(host.Id,new byte[]{254,1,2,3},NetDeliveryLane.Critical,true),"guest reliable send refused");
            Simulation.Current=host.Id;Check(host.T.Send(guest.Id,new byte[]{8,7},NetDeliveryLane.Motion,false),"host unreliable send refused");
            var big=new byte[5000];for(int i=0;i<big.Length;i++)big[i]=(byte)i;
            int reliableBefore=P2PSimulation.ReliableSends;
            Simulation.Current=host.Id;Check(host.T.Send(guest.Id,big,NetDeliveryLane.Motion,false),"large unreliable send refused");
            Check(P2PSimulation.ReliableSends==reliableBefore+1 && P2PSimulation.Oversized==0,"a >1200 byte unreliable packet was not upgraded to reliable");
            Pump(host,guest,3,0);
            Check(host.Received.Count==1 && host.Received[0].Length==4 && host.Received[0][0]==254,"host did not receive the guest's protocol bytes unchanged");
            Check(guest.Received.Count==2 && guest.Received[0][0]==8 && guest.Received[1].Length==5000 && guest.Received[1][4999]==unchecked((byte)4999),"guest did not receive both host packets unchanged");
            string health=host.T.TakeHealthSummary();
            Check(health.Contains("transport=p2p-v006") && health.Contains("session{"),"health summary misses transport or session state: "+health);

            // 2. Clean close: the other side is told.
            Simulation.Current=guest.Id;Check(guest.T.Close(host.Id,"test close"),"close returned false");
            Pump(host,guest,3,0);
            Check(host.Disconnected.Count==1 && host.Disconnected[0].StartsWith(guest.Id+":"),"host was not told the guest closed");
            Check(!guest.T.HasConnection(host.Id),"guest still tracks a closed peer");

            // 3. Lobby list lag: host's member check fails at first, then succeeds.
            Pair(out host,out guest);host.Member=false;
            Simulation.Current=guest.Id;guest.T.Connect(host.Id,0);
            Pump(host,guest,5,2);
            Check(host.Connected.Count==0 && guest.Connected.Count==0,"a non-member was admitted before the lobby list showed it");
            host.Member=true;
            Pump(host,guest,20,2);
            Check(host.Connected.Count==1 && guest.Connected.Count==1,"held session was not admitted once the lobby list showed the guest");

            // 4. Never a member: guest gives up with a disconnect, host never connects.
            Pair(out host,out guest);host.Member=false;
            Simulation.Current=guest.Id;guest.T.Connect(host.Id,0);
            Pump(host,guest,60,15);
            Check(host.Connected.Count==0,"host admitted a peer that never became a lobby member");
            Check(guest.Connected.Count==0 && guest.Disconnected.Count==1,"guest did not report a timed-out connect");
            Check(!guest.T.HasConnection(host.Id),"guest still tracks a timed-out connect, so the bridge could not retry");

            // 5. Peer vanishes without Bye: both sides time out.
            Pair(out host,out guest);
            Simulation.Current=guest.Id;guest.T.Connect(host.Id,0);
            Pump(host,guest,20,2);
            Check(host.Connected.Count==1 && guest.Connected.Count==1,"setup for idle timeout failed");
            P2PSimulation.Offline.Add(guest.Id);
            Pump(host,guest,60,15);
            Check(host.Disconnected.Count==1,"host did not time out a silent guest");

            // 6. Packets from an unknown sender on the data channel are dropped.
            Pair(out host,out guest);
            Simulation.Current=guest.Id;SteamNetworking.SendP2PPacket(new CSteamID(host.Id),new byte[]{1,2},2,EP2PSend.k_EP2PSendReliable,SteamP2PTransport.DataChannel);
            Simulation.Current=host.Id;SteamNetworking.AcceptP2PSessionWithUser(new CSteamID(guest.Id));
            host.Poll();
            Check(host.Received.Count==0,"data from a peer without a handshake reached the protocol");

            // 7. Dispose sends Bye and stops callbacks.
            Pair(out host,out guest);
            Simulation.Current=guest.Id;guest.T.Connect(host.Id,0);Pump(host,guest,20,2);
            Simulation.Current=guest.Id;guest.T.Dispose();
            host.Poll();
            Check(host.Disconnected.Count==1,"host was not told when the guest's transport was disposed");

            Console.WriteLine("PASS: "+checks+" Steam P2P v006 transport checks (handshake, data both ways, large-unreliable upgrade, close, lobby-list lag, non-member timeout, idle timeout, unknown sender, dispose).");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
