using System;
using System.Collections.Generic;
using TldMp;

// Stateful protocol regressions. Payload/application callbacks are stand-ins;
// the session, snapshot/journal and receive paths are production code.
public static class StableAuditTests
{
    sealed class Packet {public ulong From,To;public byte[] Bytes;}
    sealed class Lab
    {
        public Dictionary<ulong,StableNode> Nodes=new Dictionary<ulong,StableNode>();
        public List<Packet> Queue=new List<Packet>();public List<byte[]> Native=new List<byte[]>();public int Applied;public double Now;
        public Lab()
        {
            foreach(ulong id in new ulong[]{10,20,30})
            {
                ulong from=id;var n=new StableNode(id,10,999,id+123,"audit",100);Nodes[id]=n;
                n.Send=delegate(ulong to,byte[] bytes,bool reliable){Queue.Add(new Packet {From=from,To=to,Bytes=bytes});if(to==20 && bytes[0]!=254)Native.Add((byte[])bytes.Clone());};
                n.ApplySnapshot=delegate(byte[] bytes){if(from==30)Applied++;};
            }
            Nodes[10].SnapshotRequested=delegate(ulong peer){Nodes[10].BeginSnapshot(peer,new byte[]{1,2,3});};
            foreach(var n in Nodes.Values)n.Tick(0);Pump();Nodes[20].RequestSnapshot();Nodes[30].RequestSnapshot();Pump();
        }
        public void Pump(){int count=0;while(Queue.Count>0){if(++count>10000)throw new Exception("reply loop");var p=Queue[0];Queue.RemoveAt(0);Nodes[p.To].Receive(p.From,p.Bytes,Now);}}
        public void Advance(double now){Now=now;foreach(var n in Nodes.Values)n.Tick(now);Pump();}
        public void UntilLive()
        {
            int guard=0;while(Queue.Count>0)
            {if(++guard>10000)throw new Exception("loop");var p=Queue[0];if(p.To==30 && StableMessage.Decode(p.Bytes).Op==StableOp.Live)return;Queue.RemoveAt(0);Nodes[p.To].Receive(p.From,p.Bytes,Now);}
            throw new Exception("Expected pending Live");
        }
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void NativeDuringJoin()
    {
        var l=new Lab();l.Advance(2);
        bool built=false;l.Nodes[30].CanEnterWorld=delegate{return built;};l.Nodes[30].RequestSnapshot();l.Pump();
        var motion=new byte[49];motion[0]=39;Buffer.BlockCopy(BitConverter.GetBytes((uint)77),0,motion,1,4);
        l.Nodes[10].PublishNative(motion);l.Pump();
        built=true;l.Nodes[30].Tick(2.1);l.Pump();
        Check(l.Nodes[30].Ready,"Join must finish after the world becomes enterable.");
        Check(l.Native.Count==1 && BitConverter.ToUInt32(l.Native[0],1)==77,"A legitimate native change was lost from the join journal.");
    }
    static void SupersedingTransfer()
    {
        var l=new Lab();l.Advance(2);bool built=false;l.Nodes[30].CanEnterWorld=delegate{return built;};
        l.Nodes[30].RequestSnapshot();l.Pump();int before=l.Applied;
        l.Nodes[10].BeginSnapshot(30,new byte[]{4,5,6});
        int at=l.Queue.FindIndex(p=>p.To==30 && StableMessage.Decode(p.Bytes).Op==StableOp.Begin);var begin=l.Queue[at];l.Queue.RemoveAt(at);
        l.Nodes[30].Receive(10,begin.Bytes,2.1);built=true;l.Nodes[30].Tick(2.2);
        Check(!l.Queue.Exists(p=>p.From==30 && StableMessage.Decode(p.Bytes).Op==StableOp.Ack),"Readiness from the previous snapshot acknowledged the new transfer before its bytes arrived.");
        l.Pump();Check(l.Applied==before+1 && l.Nodes[30].Ready,"Superseding transfer must apply and reach live normally.");
    }
    static void OldLiveAfterNewRequest()
    {
        var l=new Lab();l.Advance(2);l.Nodes[30].RequestSnapshot();l.UntilLive();
        var oldLive=l.Queue[0];l.Queue.RemoveAt(0);
        l.Nodes[30].Tick(3.2);l.Nodes[30].RequestSnapshot();
        l.Nodes[30].Receive(10,oldLive.Bytes,3.2);
        Check(!l.Nodes[30].Ready,"Live from the preceding request reopened gameplay while a new snapshot was pending.");
        l.Pump();Check(l.Nodes[30].Ready,"Fresh requested snapshot must complete after ignoring old Live.");
    }
    static void HandshakeTimeout()
    {
        var client=new StableNode(20,10,999,202,"audit",100);
        client.Send=delegate(ulong peer,byte[] bytes,bool reliable){};
        client.Tick(0);client.Tick(31);
        Check(client.Error!=null && !client.Ready,"An unanswered initial handshake stayed pending forever.");
        client=new StableNode(20,10,999,202,"audit",100);client.Send=delegate(ulong peer,byte[] bytes,bool reliable){};
        client.Tick(100);client.Tick(129);Check(client.Error==null,"Pre-handshake time incorrectly consumed the timeout.");
        client.Tick(131);Check(client.Error!=null,"Delayed-start handshake still needs its own deadline.");
    }
    static void StaleRejection()
    {
        var client=new StableNode(20,10,999,303,"audit",100);
        var old=new StableMessage {Op=StableOp.Reject,Lobby=999,Session=101,Origin=10,Epoch=202,Data=System.Text.Encoding.UTF8.GetBytes("old failed join")};
        client.Receive(10,old.Encode(),0);
        Check(client.Error==null,"A rejection addressed to an old client session aborted the fresh join.");
        old.Epoch=303;old.Data=System.Text.Encoding.UTF8.GetBytes("current rejection");client.Receive(10,old.Encode(),0);
        Check(client.Error=="current rejection","A rejection addressed to the current session must still stop joining.");
    }
    public static int Main(string[] args)
    {
        bool handshake=args.Length>0 && args[0]=="--handshake-baseline";
        bool rejection=args.Length>0 && args[0]=="--reject-baseline";
        bool baseline=args.Length>0 && args[0]=="--baseline" || handshake || rejection;int failed=0;
        var tests=rejection?new Dictionary<string,Action>{{"stale rejection from old session",StaleRejection}}:handshake?new Dictionary<string,Action>{{"unanswered initial handshake",HandshakeTimeout}}:new Dictionary<string,Action>{{"native changes during join",NativeDuringJoin},{"superseding world transfer",SupersedingTransfer},{"old Live racing new snapshot request",OldLiveAfterNewRequest},{"unanswered initial handshake",HandshakeTimeout},{"stale rejection from old session",StaleRejection}};
        foreach(var test in tests)
        {
            try{test.Value();Console.WriteLine("PASS: "+test.Key);}
            catch(Exception e){failed++;Console.WriteLine("FAIL: "+test.Key+": "+e.Message);}
        }
        if(baseline){Console.WriteLine("Baseline failures reproduced: "+failed+"/"+tests.Count);return failed==tests.Count?0:1;}
        Console.WriteLine("Snapshot audit failures: "+failed+"/"+tests.Count);return failed==0?0:1;
    }
}
