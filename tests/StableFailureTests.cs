using System;
using System.Collections.Generic;
using TldMp;

// Protocol failure paths. Payloads here are deliberately synthetic: production
// serializer/validation integration lives in StableWireProductionTests instead.
public static class StableFailureTests
{
    static int checks;
    static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
    sealed class Packet {public ulong From,To;public byte[] Bytes;}
    sealed class Lab
    {
        public StableNode Host,Client;public double Now;public int Applied,Delivered,NativeToClient;
        public readonly List<Packet> Queue=new List<Packet>();
        public Lab()
        {
            Host=new StableNode(10,10,999,101,"build",0);Client=MakeClient(202);
            Host.Send=delegate(ulong to,byte[] b,bool r){Queue.Add(new Packet {From=10,To=to,Bytes=b});if(to==20 && b[0]!=254)NativeToClient++;};
            Host.SnapshotRequested=delegate(ulong peer){Host.BeginSnapshot(peer,new byte[]{1,2,3});};
            Client.Tick(0);Pump();Client.RequestSnapshot();Pump();Check(Client.Ready,"Failure fixture starts joined.");
        }
        public StableNode MakeClient(ulong session)
        {
            var node=new StableNode(20,10,999,session,"build",0);
            node.Send=delegate(ulong to,byte[] b,bool r){Queue.Add(new Packet {From=20,To=to,Bytes=b});};
            node.ApplySnapshot=delegate(byte[] b){Applied++;};node.Deliver=delegate(ulong origin,byte[] b){Delivered++;};return node;
        }
        public void Pump()
        {
            int count=0;while(Queue.Count>0){Check(++count<10000,"No runaway failure-path reply loop.");var p=Queue[0];Queue.RemoveAt(0);if(p.To==10)Host.Receive(p.From,p.Bytes,Now);else Client.Receive(p.From,p.Bytes,Now);}
        }
    }
    public static int Main()
    {
        try
        {
            var l=new Lab();l.Pump();Check(l.Host.Peers.ContainsKey(20) && l.Client.Ready,"Partition fixture starts joined.");
            // A complete partition advances clocks without delivering anything.
            l.Now=6;l.Host.Tick(l.Now);Check(l.Host.Peers.ContainsKey(20),"Host keeps a silent peer inside PeerTimeout.");
            l.Now=21;l.Host.Tick(l.Now);l.Client.Tick(l.Now);Check(!l.Host.Peers.ContainsKey(20),"Timed-out peer removed from host.");
            Check(l.Client.Error!=null && !l.Client.Ready,"Partitioned client stops rather than continuing as healthy.");
            l.Queue.Clear();l.Client=l.MakeClient(303);l.Client.Tick(l.Now);l.Pump();l.Client.RequestSnapshot();l.Pump();
            Check(l.Client.Ready && l.Host.Peers[20].Session==303,"Fresh session reconnects after timeout.");
            uint first=l.Client.FirstID;l.Host.Receive(20,new StableMessage {Op=StableOp.Hello,Lobby=999,Session=202,Origin=20,Data=System.Text.Encoding.UTF8.GetBytes("build")}.Encode(),l.Now);
            Check(l.Host.Peers[20].Session==303 && l.Client.FirstID==first,"Retired pre-partition handshake cannot replace new session.");

            l=new Lab();l.Now=2;l.Client.Tick(2);l.Pump();l.Client.ApplySnapshot=delegate(byte[] b){throw new InvalidOperationException("native apply fixture failure");};
            l.Client.RequestSnapshot();l.Pump();Check(l.Client.Error!=null && !l.Client.Ready && l.Host.Peers[20].Syncing,"Snapshot apply exception cannot acknowledge or enter live mode.");

            l=new Lab();l.Now=2;l.Client.Tick(2);l.Pump();l.Client.RequestSnapshot();
            var request=l.Queue[0];l.Queue.Clear();l.Host.Receive(20,request.Bytes,l.Now);
            var large=new byte[58000];large[0]=37;
            for(int i=0;i<160;i++)l.Host.PublishNative(large);
            Check(l.Host.Peers[20].Syncing && l.Host.Peers[20].Journal.Count==160 && l.Host.Peers[20].JournalBytes>StableNode.MaxJournalBytes,"Reliable journal overflow is retained without dropping ordered traffic or aborting the join.");
            l.Pump();Check(l.Client.Ready && l.Client.Error==null && l.NativeToClient==160,"Join completes and every ordered reliable native update survives journal overflow.");

            l=new Lab();bool refused=false;
            try{l.Host.BeginSnapshot(20,new byte[StableNode.MaxSnapshotBytes+1]);}catch(InvalidOperationException){refused=true;}
            Check(refused && !l.Host.Peers[20].Syncing && l.Queue.Count==0,"Oversized snapshot refused before transfer or peer mutation.");

            l=new Lab();l.Host.MapRequested=delegate(ulong peer,string key){return new byte[]{1,2,3};};
            l.Client.MapApplied=delegate(string key,byte[] b){throw new InvalidOperationException("native map fixture failure");};
            l.Client.RequestMap("r|0|0|0|0");l.Pump();Check(l.Client.Error!=null && !l.Client.Ready,"Generation apply exception stops the client safely.");

            l=new Lab();int applied=l.Applied;var random=new Random(9833);
            for(int i=0;i<10000;i++){var bytes=new byte[random.Next(0,512)];random.NextBytes(bytes);l.Host.Receive(20,bytes,0);l.Client.Receive(10,bytes,0);}
            Check(l.Delivered==0 && l.Applied==applied && l.Client.Ready,"Random unframed transport data causes no gameplay/snapshot side effects.");

            var exhausted=new StableNode(10,10,999,101,"build",UInt32.MaxValue-65536);
            exhausted.AllocateID(delegate(uint id){return false;});
            int messages=0;exhausted.Send=delegate(ulong peer,byte[] b,bool reliable){messages++;};refused=false;
            try{exhausted.Receive(20,new StableMessage {Op=StableOp.Hello,Lobby=999,Session=202,Origin=20,Data=System.Text.Encoding.UTF8.GetBytes("build")}.Encode(),0);}catch(InvalidOperationException){refused=true;}
            Check(refused && exhausted.Peers.Count==0 && messages==0,"Exhausted global ID namespace never emits overlapping allocation or partial welcome.");
            Console.WriteLine("PASS: "+checks+" protocol failure-path assertions: partition/timeout/reconnect, retired session, apply exceptions, journal/snapshot bounds, random receive data and ID exhaustion. Native failures are injected stand-ins.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
