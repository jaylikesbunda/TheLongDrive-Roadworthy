using System;
using System.Collections.Generic;
using TldMp;

// Join-journal regressions: a moving host must not disconnect a peer that is
// still receiving a multi-second world snapshot. Native motion bytes are a
// latest-value stream, so the journal may coalesce/evict them, but never ordered
// reliable native messages.
public static class StableJournalTests
{
    static int checks;
    static void Check(bool ok,string what){checks++;if(!ok)throw new Exception("FAIL: "+what);}
    sealed class Packet {public ulong From,To;public byte[] Bytes;public bool Reliable;}
    sealed class Lab
    {
        public readonly Dictionary<ulong,StableNode> Nodes=new Dictionary<ulong,StableNode>();
        public readonly List<Packet> Queue=new List<Packet>();
        public readonly Dictionary<ulong,List<byte[]>> Native=new Dictionary<ulong,List<byte[]>>();
        public int Rejects;
        public double Now=0;
        public Lab()
        {
            Add(10,"journal");Add(20,"journal");
            Nodes[10].SnapshotRequested=delegate(ulong id){Nodes[10].BeginSnapshot(id,new byte[]{1,2,3});};
        }
        public StableNode Add(ulong id,string build)
        {
            var n=new StableNode(id,10,999,id+123,build,100);Nodes[id]=n;Native[id]=new List<byte[]>();
            n.Send=delegate(ulong target,byte[] bytes,bool reliable)
            {
                var m=StableMessage.Decode(bytes);if(m!=null && m.Op==StableOp.Reject)Rejects++;
                Queue.Add(new Packet {From=id,To=target,Bytes=bytes,Reliable=reliable});
                if(bytes[0]!=254 && target!=0)Native[target].Add((byte[])bytes.Clone());
            };
            n.ApplySnapshot=delegate(byte[] data){if(data.Length==0)throw new Exception("empty snapshot");};
            return n;
        }
        public void Pump()
        {
            int guard=0;while(Queue.Count>0)
            {
                if(++guard>200000)throw new Exception("reply loop");
                var p=Queue[0];Queue.RemoveAt(0);if(Nodes.ContainsKey(p.To))Nodes[p.To].Receive(p.From,p.Bytes,Now);
            }
        }
        public void HandshakeOnly()
        {
            Nodes[20].Tick(Now);Pump();Check(Nodes[20].Compatible,"handshake completed before snapshot");
        }
    }
    static byte[] Motion(int value){var b=new byte[49];b[0]=39;Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,1,4);return b;}
    static byte[] Control(int value){var b=new byte[5];b[0]=37;Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,1,4);return b;}

    static void BoundedJoinJournal()
    {
        var lab=new Lab();lab.HandshakeOnly();
        var host=lab.Nodes[10];
        var world=new byte[1024*1024];new Random(11).NextBytes(world);
        host.BeginSnapshot(20,world);
        Check(host.Peers[20].Syncing && !host.Peers[20].Ready,"snapshot transfer is still in progress");
        const int keys=48,rounds=900;
        var latest=new Dictionary<uint,int>();
        for(int round=0;round<rounds;round++)
        {
            for(int k=0;k<keys;k++)
            {
                uint id=(uint)(100+k);int value=k*100000+round;
                Check(host.PublishNative(MotionFor(id,value)),"host publishes native motion while the peer is joining");
                latest[id]=value;
                Check(host.Peers[20].JournalBytes<=StableNode.MaxJournalBytes,"journal stays bounded at every publish");
            }
        }
        var peer=host.Peers[20];
        Check(peer.JournalBytes<=StableNode.MaxJournalBytes,"journal is under the cap at the end of the join");
        Check(peer.Journal.Count<=keys,"coalescing leaves at most one non-reliable entry per moving object");
        Check(peer.Syncing && !peer.Ready,"peer is kept syncing instead of being disconnected");
        lab.Pump();
        Check(lab.Rejects==0,"journal pressure never emits Reject");
        Check(lab.Nodes[20].Error==null && lab.Nodes[20].Ready,"joining peer reaches Ready after the snapshot and journal replay: "+(lab.Nodes[20].Error??"not ready"));
        var delivered=lab.Native[20];
        Check(delivered.Count==keys,"only the newest coalesced sample per object is replayed");
        foreach(var data in delivered)
        {
            uint id=BitConverter.ToUInt32(data,1);int value=BitConverter.ToInt32(data,5);
            Check(latest.ContainsKey(id) && latest[id]==value,"newest value per object arrived after coalescing");
        }
    }
    static byte[] MotionFor(uint id,int value)
    {
        var b=new byte[49];b[0]=39;Buffer.BlockCopy(BitConverter.GetBytes(id),0,b,1,4);Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,5,4);return b;
    }
    static void ReliableJournalOrder()
    {
        var lab=new Lab();lab.HandshakeOnly();
        var host=lab.Nodes[10];
        host.BeginSnapshot(20,new byte[]{1,2,3});
        var expected=new List<int>();
        for(int i=1;i<=3000;i++)
        {
            Check(host.PublishNative(Control(i)),"reliable native control published during join");
            expected.Add(i);
            Check(host.PublishNative(Motion(i)),"unreliable native motion published during join");
            if(i%15==0)
            {
                var big=new byte[50000];big[0]=39;Buffer.BlockCopy(BitConverter.GetBytes((uint)i),0,big,1,4);
                Check(host.PublishNative(big),"large non-reliable state published during join");
            }
        }
        var peer=host.Peers[20];
        Check(peer.JournalBytes<=StableNode.MaxJournalBytes,"non-reliable eviction keeps the journal bounded");
        var journal=new List<int>();
        foreach(var entry in peer.Journal)
            if(entry.Reliable && entry.Bytes[0]==37)journal.Add(BitConverter.ToInt32(entry.Bytes,1));
        Check(journal.Count==expected.Count,"no reliable journal entry was dropped");
        for(int i=0;i<expected.Count;i++)Check(journal[i]==expected[i],"reliable journal preserves original production order");
        lab.Pump();
        Check(lab.Rejects==0 && lab.Nodes[20].Error==null && lab.Nodes[20].Ready,"peer reaches Ready without rejection after reliable pressure");
        var delivered=new List<int>();
        foreach(var data in lab.Native[20])if(data[0]==37)delivered.Add(BitConverter.ToInt32(data,1));
        Check(delivered.Count==expected.Count,"no reliable message was lost during journal replay");
        for(int i=0;i<expected.Count;i++)Check(delivered[i]==expected[i],"reliable delivery order matches production order");
    }

    public static int Main()
    {
        try
        {
            BoundedJoinJournal();
            ReliableJournalOrder();
            Console.WriteLine("PASS: "+checks+" join-journal assertions: bounded/coalesced non-reliable native motion during a slow snapshot, no Reject/Ready failure, newest value per object, and reliable native bytes replayed in original order.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}

