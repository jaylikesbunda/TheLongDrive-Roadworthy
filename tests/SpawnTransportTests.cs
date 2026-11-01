using System;
using System.Collections.Generic;
using System.Net;
using Steamworks;
using TldMp;

public static class SpawnTransportTests
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static byte[] Payload(int seed, int length)
    {
        var bytes=new byte[length];var rand=new Random(seed);rand.NextBytes(bytes);return bytes;
    }

    sealed class SimPeer : IDisposable
    {
        public readonly ulong Id;
        public readonly SteamSdrTransport Socket;
        public readonly StableNode Node;
        public double Now;
        public SimPeer(ulong id)
        {
            Id=id;Simulation.Current=id;
            Socket=new SteamSdrTransport();Socket.AcceptPeer=delegate(ulong p){return p==30-Id;};
            Node=new StableNode(id,10,999,id+111,"spawn-transport",100);
            Node.Send=delegate(ulong peer,byte[] b,bool reliable){Socket.Send(peer,b,b.Length>1024?NetDeliveryLane.Bulk:NetDeliveryLane.Critical,reliable);};
            Node.Notice=delegate(string s){};
            Node.CanEnterWorld=delegate{return true;};
            Node.ApplySnapshot=delegate(byte[] b){};
            Node.SnapshotRequested=delegate(ulong p){Node.BeginSnapshot(p,new byte[]{1,2,3});};
        }
        public void Poll()
        {
            Simulation.Current=Id;
            Socket.Poll(delegate(ulong from,byte[] bytes){Node.Receive(from,bytes,Now);},256);
            Node.Tick(Now);
        }
        public void Dispose(){Simulation.Current=Id;Socket.Dispose();}
    }

    static void SimPumpImpaired(Random rand,double drop)
    {
        Simulation.Pump();
        foreach(var s in new List<Simulation.Socket>(Simulation.Sockets.Values))
        {
            var items=new List<IntPtr>();
            while(s.Messages.Count>0)items.Add(s.Messages.Dequeue());
            for(int i=items.Count-1;i>0;i--){int j=rand.Next(i+1);var t=items[i];items[i]=items[j];items[j]=t;}
            foreach(var m in items)
            {
                if(rand.NextDouble()<drop){SteamNetworkingMessage_t.Release(m);continue;}
                s.Messages.Enqueue(m);
            }
        }
    }

    static void SteamPairTransfer(Random rand,double drop,int length)
    {
        using(var host=new SimPeer(10))using(var client=new SimPeer(20))
        {
            Simulation.Current=10;host.Socket.Listen(0);
            Simulation.Current=20;client.Socket.Connect(10,0);
            SimPumpImpaired(rand,0);
            for(int i=0;i<20 && (!host.Node.Peers.ContainsKey(20) || !client.Node.Ready);i++)
            {SimPumpImpaired(rand,0);host.Poll();client.Poll();if(i==2)client.Node.RequestSnapshot();}
            var hPeer=host.Node.Peers[20];
            Check(hPeer!=null && client.Node.Ready,"steam pair handshake");
            int mutations=0,acks=0;
            host.Node.SpawnReceived=delegate(ulong src,ulong seq,byte[] data){mutations++;return true;};
            client.Node.SpawnAcknowledged=delegate(ulong seq){acks++;};
            var payload=Payload(7,length);
            Check(client.Node.SendSpawn(41,payload),"spawn send accepted");
            int guard=0;
            while(acks==0 && ++guard<200)
            {
                SimPumpImpaired(rand,drop);
                host.Poll();client.Poll();
                if(guard%20==0)client.Node.SendSpawn(41,payload);
            }
            Check(mutations==1,"transfer completed through loss/jitter exactly once");
            Check(acks==1,"final SpawnAcknowledged fired exactly once");
        }
    }

    static void SteamDeterministicLoss()
    {
        var rand=new Random(99);
        using(var host=new SimPeer(10))using(var client=new SimPeer(20))
        {
            Simulation.Current=10;host.Socket.Listen(0);
            Simulation.Current=20;client.Socket.Connect(10,0);
            for(int i=0;i<20 && (!host.Node.Peers.ContainsKey(20) || !client.Node.Ready);i++)
            {Simulation.Pump();host.Poll();client.Poll();if(i==2)client.Node.RequestSnapshot();}
            Check(host.Node.Peers.ContainsKey(20) && client.Node.Ready,"deterministic join");
            int mutations=0,acks=0;
            host.Node.SpawnReceived=delegate(ulong src,ulong seq,byte[] data){mutations++;return true;};
            client.Node.SpawnAcknowledged=delegate(ulong seq){acks++;};
            var payload=Payload(5,120000);
            Check(client.Node.SendSpawn(43,payload),"chunked send");
            SimPumpDrop(1);
            host.Poll();client.Poll();
            Check(mutations==0,"dropped first chunk stalls mutation");
            for(int i=0;i<200 && mutations==0;i++){Simulation.Pump();client.Node.SendSpawn(43,payload);SimPumpImpaired(rand,0.05);host.Poll();client.Poll();}
            Check(mutations==1,"chunk transfer recovered from first-chunk drop");
            for(int i=0;i<200 && acks==0;i++){client.Node.SendSpawn(43,payload);SimPumpImpaired(rand,0.05);host.Poll();client.Poll();}
            Check(acks>=1,"final ack delivered after recovery");
            Check(mutations==1,"no second mutation across retries");
        }
    }

    static void SimPumpDrop(int count)
    {
        Simulation.Pump();
        int dropped=0;
        foreach(var s in new List<Simulation.Socket>(Simulation.Sockets.Values))
        {
            while(dropped<count && s.Messages.Count>0){SteamNetworkingMessage_t.Release(s.Messages.Dequeue());dropped++;}
        }
    }

    static void LoopbackPairTransfer()
    {
        int port=34000+new Random().Next(2000);
        var hostCfg=LoopbackConfig.For(true,"127.0.0.1",port);
        var clientCfg=LoopbackConfig.For(false,"127.0.0.1",port);
        var profile=new LoopbackNetworkProfile(200,10.0,7,50);
        var hostT=new LoopbackTransport(hostCfg,profile);
        var clientT=new LoopbackTransport(clientCfg,profile);
        Check(hostT.Start() && clientT.Start(),"loopback transports started");
        var hostLog=new System.Text.StringBuilder();var clientLog=new System.Text.StringBuilder();
        var host=new StableNode(hostCfg.Own,hostCfg.HostIdentity,hostCfg.Lobby,111,"spawn-transport",100);
        var guest=new StableNode(clientCfg.Own,clientCfg.HostIdentity,clientCfg.Lobby,222,"spawn-transport",100);
        host.Send=delegate(ulong peer,byte[] bytes,bool reliable){hostT.Send(peer,bytes,reliable);};
        guest.Send=delegate(ulong peer,byte[] bytes,bool reliable){clientT.Send(peer,bytes,reliable);};
        var opHost=new Dictionary<int,int>();var opGuest=new Dictionary<int,int>();
        host.Notice=delegate(string s){Append(hostLog,"N:"+s);};guest.Notice=delegate(string s){Append(clientLog,"N:"+s);};
        host.Trace=delegate(string s){Append(hostLog,"T:"+s);};guest.Trace=delegate(string s){Append(clientLog,"T:"+s);};
        host.SnapshotRequested=delegate(ulong peer){host.BeginSnapshot(peer,new byte[]{1,2,3});};
        guest.CanEnterWorld=delegate{return true;};
        guest.ApplySnapshot=delegate(byte[] bytes){guest.HostWorldLoaded=true;};
        int mutations=0,acks=0,motionRx=0;
        host.SpawnReceived=delegate(ulong src,ulong seq,byte[] data){mutations++;return true;};
        guest.SpawnAcknowledged=delegate(ulong seq){acks++;};
        var payload=Payload(9,120000);
        var motion=new byte[32];motion[0]=64;new Random(3).NextBytes(motion);motion[0]=64;
        var clock0=System.Diagnostics.Stopwatch.StartNew();
        bool snapshotAsked=false,sent=false;
        double lastSend=-10;
        string failure=null;
        try
        {
            while(clock0.Elapsed.TotalSeconds<45)
            {
                double now=clock0.Elapsed.TotalSeconds;
                LoopbackEvent e;
                while(hostT.TryDequeue(out e))
                {
                    if(e.Bytes!=null && e.Bytes.Length>0)
                    {
                        if(e.Bytes[0]==64){motionRx++;continue;}
                        if(e.Bytes[0]==64||e.Bytes[0]==65){motionRx++;continue;}
                        host.Receive(e.From,e.Bytes,now);
                    }
                }
                while(clientT.TryDequeue(out e))
                {
                    if(e.Bytes!=null && e.Bytes.Length>0)guest.Receive(e.From,e.Bytes,now);
                }
                host.Tick(now);guest.Tick(now);
                if(guest.Compatible && !guest.Ready && !snapshotAsked){guest.RequestSnapshot();snapshotAsked=true;}
                if(guest.Ready && now-lastSend>=2.0 && acks==0)
                {
                    lastSend=now;
                    if(guest.SendSpawn(51,payload))sent=true;
                }
                if(acks>0)break;
                System.Threading.Thread.Sleep(5);
            }
        }
        finally{hostT.Stop();clientT.Stop();}
        if(acks==0)
        {
            failure="loopback full join+chunk transfer failed: sent="+sent+" snapshotAsked="+snapshotAsked+
                " hostPeers="+host.Peers.Count+" guestCompatible="+guest.Compatible+" guestReady="+guest.Ready+
                " guestJoin="+guest.JoinStatus+" guestErr="+guest.Error+" hostErr="+host.Error+" mutations="+mutations+" acks="+acks+
                " hostTrace["+hostLog+"] guestTrace["+clientLog+"]";
            throw new Exception(failure);
        }
        Check(mutations==1,"real TCP transfer committed exactly once");
        Check(acks==1,"real TCP final ack once");
        Check(sent,"real TCP spawn emitted");
    }

    static void Append(System.Text.StringBuilder sb,string s)
    {
        if(sb.Length>4000)return;
        if(sb.Length>0)sb.Append('|');
        sb.Append(s);
    }

    static void ReconnectCollision()
    {
        var lab=new TransferLab();lab.Connect();
        var host=lab.Nodes[10];var client=lab.Nodes[20];
        int mutations=0;
        host.SpawnReceived=delegate(ulong src,ulong seq,byte[] data){mutations++;return true;};
        Check(client.SendSpawn(61,new byte[]{1,2,3}),"small send");
        lab.Pump();
        Check(mutations==1,"committed once");
        var old=host.Peers[20];
        var oldAck=new StableMessage{Op=StableOp.SpawnChunkAck,Lobby=999,Session=old.Session,Origin=20,Epoch=old.Session,Sequence=61,Data=BitConverter.GetBytes(9999)};
        host.Receive(20,oldAck.Encode(),lab.Now);
        var oldChunk=new StableMessage{Op=StableOp.SpawnChunk,Lobby=999,Session=old.Session,Origin=20,Epoch=old.Session,Sequence=61,
            Data=Concat(BitConverter.GetBytes(120000),BitConverter.GetBytes(0),new byte[32],new byte[48000])};
        host.Receive(20,oldChunk.Encode(),lab.Now);
        Check(mutations==1,"stale-session messages never re-applied");
        host.RemovePeer(20);
        lab.Reconnect(20);
        Check(mutations==1,"reconnect does not replay");
        var np=host.Peers[20];
        Check(np.Session!=old.Session,"new session replaces retired");
        var fresh=Payload(21,70000);
        var freshClient=lab.Nodes[20];        Check(lab.Nodes[20].SendSpawn(63,fresh),"fresh session new seq accepted");
        lab.Pump();
        for(int i=0;i<50 && mutations<2;i++){lab.Nodes[20].SendSpawn(63,fresh);lab.Pump();}
        Check(mutations==2,"fresh session transfer applies");
        var ghostAck=new StableMessage{Op=StableOp.SpawnAck,Lobby=999,Session=old.Session,Origin=10,Epoch=old.Session,Sequence=63,Data=new byte[0]};
        lab.Nodes[20].Receive(10,ghostAck.Encode(),lab.Now);
        var ghostChunkAck=new StableMessage{Op=StableOp.SpawnChunkAck,Lobby=999,Session=host.Session,Origin=10,Epoch=old.Session,Sequence=64,Data=BitConverter.GetBytes(48000)};
        lab.Nodes[20].Receive(10,ghostChunkAck.Encode(),lab.Now);
        Check(mutations==2,"ghost session acks cannot settle new transactions");
        host.RetirePeersForWorldReplacement();
        Check(host.Peers.Count==0,"world replacement retires all peer sessions");
        int before=host.SpawnIncomingHigh;
        var staleChunk=new StableMessage{Op=StableOp.SpawnChunk,Lobby=999,Session=old.Session,Origin=20,Epoch=old.Session,Sequence=61,
            Data=Concat(BitConverter.GetBytes(120000),BitConverter.GetBytes(0),new byte[32],new byte[48000])};
        host.Receive(20,staleChunk.Encode(),lab.Now);
        Check(mutations==2 && host.SpawnIncomingHigh==before,"stale chunk after retire never allocated");
    }

    static byte[] Concat(params byte[][] parts)
    {
        int total=0;foreach(var p in parts)total+=p.Length;
        var result=new byte[total];int at=0;
        foreach(var p in parts){Buffer.BlockCopy(p,0,result,at,p.Length);at+=p.Length;}
        return result;
    }

    sealed class TransferLab
    {
        public readonly Dictionary<ulong,StableNode> Nodes=new Dictionary<ulong,StableNode>();
        public double Now;
        public void Connect()
        {
            Nodes[10]=Make(10);Nodes[20]=Make(20);
            foreach(var n in Nodes.Values)n.Tick(Now);Pump();Nodes[20].RequestSnapshot();Pump();
        }
        ulong sessionSeed=111;
        StableNode Make(ulong id)
        {
            var n=new StableNode(id,10,999,id+sessionSeed,"spawn-transport",100);
            n.Send=delegate(ulong peer,byte[] b,bool reliable){Nodes[peer].Receive(id,b,Now);};
            n.Notice=delegate(string s){};
            n.SnapshotRequested=delegate(ulong p){n.BeginSnapshot(p,new byte[]{1,2,3});};
            n.CanEnterWorld=delegate{return true;};
            n.ApplySnapshot=delegate(byte[] b){n.HostWorldLoaded=true;};
            return n;
        }
        public void Pump(){foreach(var n in Nodes.Values)n.Tick(Now);}
        public void Reconnect(ulong id)
        {
            sessionSeed+=777;
            Nodes[id]=Make(id);Now+=1;
            for(int i=0;i<10 && !Nodes[10].Peers.ContainsKey(id);i++){Nodes[id].Tick(Now);Pump();}
            Pump();Nodes[id].RequestSnapshot();Pump();
            Check(Nodes[10].Peers.ContainsKey(id),"reconnected peer session established");
        }
    }

    public static int Main()
    {
        try
        {
            var rand=new Random(42);
            SteamPairTransfer(rand,0.15,60000);
            SteamPairTransfer(rand,0.15,120000);
            SteamDeterministicLoss();
            LoopbackPairTransfer();
            ReconnectCollision();
            Console.WriteLine("PASS: spawn transport "+checks+" checks (SteamSocketSimulation + impaired LoopbackTransport, reconnect collision).");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
