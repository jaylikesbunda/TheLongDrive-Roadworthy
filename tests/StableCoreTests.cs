using System;
using System.Collections.Generic;
using TldMp;

// Surviving core state machine coverage: handshake, ID blocks, world snapshot
// and journal barrier, map generation transfer, deferred map work, liveness and
// native byte relay. Ownership/lease/grant tests were removed with that
// architecture (archive/OwnershipCoreTests.cs).
public static class StableCoreTests
{
    sealed class Packet {public ulong From,To;public byte[] Bytes;public bool Reliable;}
    sealed class Lab
    {
        public Dictionary<ulong,StableNode> Nodes=new Dictionary<ulong,StableNode>();public List<Packet> Queue=new List<Packet>();
        public Dictionary<ulong,List<byte[]>> Native=new Dictionary<ulong,List<byte[]>>();
        public Dictionary<ulong,int> Applied=new Dictionary<ulong,int>();
        public double Now;
        public Lab()
        {
            foreach(ulong id in new ulong[]{10,20,30})Add(id,"same-build");
            Nodes[10].SnapshotRequested=delegate(ulong id){Nodes[10].BeginSnapshot(id,new byte[]{1,2,3});};
        }
        public StableNode Add(ulong id,string build)
        {
            var n=new StableNode(id,10,999,id+123,build,100);Nodes[id]=n;Applied[id]=0;Native[id]=new List<byte[]>();
            n.Send=delegate(ulong target,byte[] bytes,bool reliable)
            {
                Queue.Add(new Packet {From=id,To=target,Bytes=bytes,Reliable=reliable});
                if(bytes[0]!=254 && target!=0)Native[target].Add((byte[])bytes.Clone());
            };
            n.ApplySnapshot=delegate(byte[] data){if(data.Length!=3 || data[0]!=1)throw new Exception("bad snapshot");Applied[id]++;};
            return n;
        }
        public void Pump(){int guard=0;while(Queue.Count>0){if(++guard>20000)throw new Exception("loop");var p=Queue[0];Queue.RemoveAt(0);if(Nodes.ContainsKey(p.To))Nodes[p.To].Receive(p.From,p.Bytes,Now);}}
        public void Connect(){foreach(var n in Nodes.Values)n.Tick(Now);Pump();Nodes[20].RequestSnapshot();Nodes[30].RequestSnapshot();Pump();}
        public void Tick(double time){Now=time;foreach(var n in Nodes.Values)n.Tick(time);Pump();}
    }
    static int checks;
    static void Check(bool ok,string what){checks++;if(!ok)throw new Exception("FAIL: "+what);}
    static byte[] Motion(int value){var b=new byte[49];b[0]=39;Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,1,4);return b;}
    static byte[] Control(int value){var b=new byte[5];b[0]=37;Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,1,4);return b;}
    public static int Main()
    {
        try
        {
            var lab=new Lab();lab.Connect();foreach(var n in lab.Nodes.Values)Check(n.Ready,"all peers ready after snapshot ACK barrier");
            Check(lab.Applied[20]==1 && lab.Applied[30]==1,"both clients applied hash-checked snapshot");
            lab.Tick(2);lab.Nodes[30].RequestSnapshot();var request=lab.Queue[0];lab.Queue.Clear();lab.Nodes[10].Receive(request.From,request.Bytes,2);
            lab.Nodes[10].PublishNative(Motion(1));lab.Nodes[10].PublishNative(Motion(2));
            Check(lab.Nodes[10].Peers[30].Journal.Count==2,"native updates journaled while transfer pending");
            lab.Pump();
            Check(lab.Nodes[30].Ready && lab.Applied[30]==2,"rejoin reaches ready after complete snapshot and journal");
            var native=lab.Native[30];Check(native.Count>=2 && BitConverter.ToInt32(native[native.Count-2],1)==1 && BitConverter.ToInt32(native[native.Count-1],1)==2,"join journal replays native bytes in order");
            var ids=new HashSet<uint>();foreach(var n in lab.Nodes.Values)for(int i=0;i<1000;i++)Check(ids.Add(n.AllocateID(delegate(uint id){return id==105;})),"reserved IDs never overlap");
            Check((lab.Nodes[10].FirstID>0 && lab.Nodes[10].LastID>=lab.Nodes[10].FirstID),"host reserved an ID block");
            Check((lab.Nodes[20].FirstID>0 && lab.Nodes[20].LastID>=lab.Nodes[20].FirstID),"peer received a reserved ID block");
            Check(lab.Nodes[10].FirstID!=lab.Nodes[20].FirstID,"peer blocks are disjoint");
            var mismatch=new Lab();mismatch.Add(30,"different-content");mismatch.Connect();Check(!mismatch.Nodes[30].Compatible && !mismatch.Nodes[30].Ready,"content mismatch blocks joining");
            var corrupt=new Lab();corrupt.Nodes[20].Tick(0);corrupt.Pump();corrupt.Nodes[20].RequestSnapshot();var r=corrupt.Queue[0];corrupt.Queue.Clear();corrupt.Nodes[10].Receive(r.From,r.Bytes,0);
            for(int i=0;i<corrupt.Queue.Count;i++){var m=StableMessage.Decode(corrupt.Queue[i].Bytes);if(m.Op==StableOp.Part){m.Data[0]^=0x10;corrupt.Queue[i].Bytes=m.Encode();}}
            corrupt.Pump();Check(corrupt.Applied[20]==0 && !corrupt.Nodes[20].Ready,"corrupted snapshot rejected before any apply");
            // Decode rejects truncation at every byte and oversized/unknown envelopes.
            var replayHello=new StableMessage {Op=StableOp.Hello,Lobby=999,Session=123,Origin=20,Data=System.Text.Encoding.UTF8.GetBytes("same-build")};
            var encoded=replayHello.Encode();for(int i=0;i<encoded.Length;i++){var b=new byte[i];Buffer.BlockCopy(encoded,0,b,0,i);Check(StableMessage.Decode(b)==null,"truncated envelope rejected");}
            Check(StableMessage.Decode(new byte[60001])==null,"oversized envelope rejected");
            MapTransfers();MapRequestBackpressure();DeferredMapWork();NativeRelay();Liveness();CargoPosePolicyChecks();CarBloodRelay();
            Console.WriteLine("PASS: "+checks+" reduced-core checks with THREE independent peer state machines: handshake, ID blocks, snapshot/journal/hash barrier, native journal ordering, map transfers, deferred map work, liveness and compatibility.");
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void CarBloodRelay()
    {
        var lab=new Lab();lab.Connect();
        var received=new Dictionary<ulong,List<string>>();
        foreach(var pair in lab.Nodes)
        {
            ulong id=pair.Key;received[id]=new List<string>();
            pair.Value.CarBloodReceived=delegate(uint car,bool dirty){received[id].Add(car+":"+dirty);};
        }
        Check(lab.Nodes[20].PublishCarBlood(42,true),"ready client publishes blood state");lab.Pump();
        Check(received[10].Count==1 && received[10][0]=="42:True","host applies client blood");
        Check(received[30].Count==1 && received[30][0]=="42:True","host relays blood to other client");
        Check(received[20].Count==0,"publishing client does not receive its own echo");
        Check(lab.Nodes[10].PublishCarBlood(42,false),"host publishes clean state");lab.Pump();
        Check(received[20][received[20].Count-1]=="42:False" && received[30][received[30].Count-1]=="42:False","clean state reaches clients");
        lab.Tick(2);
        var order=new List<string>();
        lab.Nodes[20].ApplySnapshot=delegate(byte[] bytes){order.Add("snapshot");};
        lab.Nodes[20].CarBloodReceived=delegate(uint id,bool dirty){order.Add("blood:"+id+":"+dirty);};
        lab.Nodes[20].RequestSnapshot();
        Check(lab.Queue.Count==1,"resnapshot request queued");
        var request=lab.Queue[0];lab.Queue.RemoveAt(0);lab.Nodes[10].Receive(request.From,request.Bytes,lab.Now);
        Check(lab.Nodes[10].Peers[20].Syncing,"resnapshot is in progress");
        lab.Nodes[10].PublishCarBlood(42,true);
        Check(lab.Nodes[10].Peers[20].Journal.Count>0,"blood update is journaled during world transfer");
        lab.Pump();
        Check(order.Count==2 && order[0]=="snapshot" && order[1]=="blood:42:True","blood state follows snapshot during join");
        Check(!lab.Nodes[20].PublishCarBlood(0,true),"invalid item ID is refused");
        var bad=new StableMessage {Op=StableOp.CarBlood,Lobby=999,Session=lab.Nodes[20].Session,Origin=20,Data=new byte[]{42,0,0,0,2}};
        int count=received[10].Count;lab.Nodes[10].Receive(20,bad.Encode(),lab.Now);lab.Pump();
        Check(received[10].Count==count,"invalid blood state is ignored");
    }

    static void NativeRelay()
    {
        var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
        host.PublishNative(Motion(7));
        Check(lab.Native[20].Count==1 && lab.Native[30].Count==1,"host broadcasts native bytes to every ready peer");
        Check(lab.Native[20][0][0]==39 && BitConverter.ToInt32(lab.Native[20][0],1)==7,"native payload is byte-identical");
        lab.Pump();
        client.PublishNative(Control(9));
        Check(lab.Queue.Count==1,"client sends native bytes only to the host");
        lab.Pump();
        Check(lab.Native[10].Count>0 && lab.Native[10][lab.Native[10].Count-1][0]==37,"client native bytes reach the host unchanged");
    }
    static void CargoPosePolicyChecks()
    {
        Check(CargoPosePolicy.Classify(false,false)==CargoPoseDisposition.World,"a free item keeps the native world pose");
        Check(CargoPosePolicy.Classify(true,false)==CargoPoseDisposition.LocalStorage,"a mount-locked item is re-expressed as the native parent-local pose");
        Check(CargoPosePolicy.Classify(false,true)==CargoPoseDisposition.Suppress,"an attached item suppresses the world pose");
        Check(CargoPosePolicy.Classify(true,true)==CargoPoseDisposition.LocalStorage,"native physlock state is decisive when both flags are set");
        Check(CargoPosePolicy.Classify(false,true,true)==CargoPoseDisposition.World,"an item-data reply keeps an attached part's world pose so the asker can create it");
        Check(CargoPosePolicy.Classify(true,false,true)==CargoPoseDisposition.World,"an item-data reply keeps a stored item's world pose so the asker can create it");
        Check(CargoPosePolicy.Classify(false,true,false)==CargoPoseDisposition.Suppress && CargoPosePolicy.Classify(true,false,false)==CargoPoseDisposition.LocalStorage,"ordinary sends keep the cargo protection");
    }
    static void Liveness()
    {
        var lab=new Lab();lab.Connect();lab.Tick(1);
        lab.Nodes[20].Tick(30);
        Check(lab.Nodes[20].Error!=null,"a client that hears no host heartbeat stops");
        var host=new Lab();host.Connect();
        host.Now=30;host.Nodes[10].Tick(30);
        Check(!host.Nodes[10].Peers.ContainsKey(20),"an idle peer is removed after PeerTimeout");
        // A retired session cannot resurrect itself through a stale Hello.
        var retire=new Lab();retire.Connect();retire.Nodes[10].RemovePeer(20);
        var stale=new StableMessage {Op=StableOp.Hello,Lobby=999,Session=retire.Nodes[20].Session,Origin=20,Data=System.Text.Encoding.UTF8.GetBytes("same-build")};
        retire.Nodes[10].Receive(20,stale.Encode(),1);Check(!retire.Nodes[10].Peers.ContainsKey(20),"retired peer session cannot rejoin with the same session id");
    }
    static void MapTransfers()
    {
        var lab=new Lab();lab.Connect();var terrain=new byte[140000];new Random(617).NextBytes(terrain);int applied=0,generated=0;
        lab.Nodes[10].MapRequested=delegate(ulong peer,string key){Check(peer==20 && key=="r|0|0|0|0","host receives bounded generation request");generated++;return terrain;};
        lab.Nodes[20].MapApplied=delegate(string key,byte[] data){Check(StableNode.Hash(data)==StableNode.Hash(terrain),"host generation result reassembled exactly");applied++;};
        lab.Nodes[20].RequestMap("r|0|0|0|0");lab.Pump();Check(applied==1,"large generation manifest applied once after full checksum");
        lab.Tick(4.1);lab.Nodes[20].RequestMap("r|0|0|0|0");var request=lab.Queue[0];lab.Queue.Clear();lab.Nodes[10].Receive(request.From,request.Bytes,4.1);lab.Nodes[10].Receive(request.From,request.Bytes,4.1);
        Check(generated==2 && lab.Queue.Count>0,"host suppresses a duplicate map request during the reply window");
        for(int i=0;i<lab.Queue.Count;i++){var m=StableMessage.Decode(lab.Queue[i].Bytes);if(m.Op==StableOp.MapPart && m.Sequence>0){m.Data[0]^=1;lab.Queue[i].Bytes=m.Encode();break;}}
        lab.Pump();Check(applied==1,"corrupted generated geometry rejected before apply");
        lab.Tick(9.2);lab.Nodes[20].RequestMap("r|0|0|0|0");lab.Pump();Check(applied==2,"corrupt map transfer retries after its no-progress timeout and converges");

        var window=new Lab();window.Connect();int requested=0,appliedWindow=0;byte[] small=new byte[]{8,1,2};
        window.Nodes[10].MapRequested=delegate(ulong peer,string key){requested++;return small;};
        window.Nodes[20].MapApplied=delegate(string key,byte[] data){appliedWindow++;};
        for(int i=0;i<12;i++)window.Nodes[20].RequestMap("r|0|0|"+i+"|0");
        Check(window.Queue.Count==12,"map requests use the full concurrent join window");
        window.Pump();Check(requested==12 && appliedWindow==12,"windowed map requests drain and free slots after application");
    }
    static void MapRequestBackpressure()
    {
        var joining=new Lab();joining.Nodes[20].CanEnterWorld=delegate{return false;};joining.Connect();
        Check(!joining.Nodes[20].Ready && joining.Nodes[10].Peers[20].Syncing,"map backpressure case is still before the snapshot ACK barrier");
        int generated=0,applied=0;var chunk=new byte[120000];new Random(441).NextBytes(chunk);
        joining.Nodes[10].MapRequested=delegate(ulong peer,string key){generated++;return chunk;};
        joining.Nodes[20].MapApplied=delegate(string key,byte[] data){applied++;};
        var keys=new List<string>();for(int i=0;i<4;i++){string key="r|0|0|"+i+"|0";keys.Add(key);joining.Nodes[20].RequestMap(key);}
        var requests=new List<Packet>(joining.Queue);joining.Queue.Clear();
        foreach(var request in requests)joining.Nodes[10].Receive(request.From,request.Bytes,0);
        var replies=new List<Packet>(joining.Queue);joining.Queue.Clear();
        Check(generated==4 && replies.Count>4,"the host generated one four-key response window before ACK");
        joining.Tick(1.2);
        for(int i=0;i<keys.Count;i++)joining.Nodes[20].RequestMap(keys[i]);
        Check(joining.Queue.Count==0,"one-second calls do not retransmit map requests while responses are in flight");
        for(int i=0;i<requests.Count;i++)joining.Nodes[10].Receive(requests[i].From,requests[i].Bytes,1.2);
        Check(generated==4 && joining.Queue.Count==0,"host suppresses duplicate generation/replies for recent peer/key requests");
        foreach(var reply in replies)joining.Nodes[20].Receive(reply.From,reply.Bytes,1.2);
        Check(applied==4 && !joining.Nodes[20].Ready && joining.Nodes[10].Peers[20].Syncing,"map responses apply while the peer remains behind the ACK/Live barrier");

        var progress=new Lab();progress.Connect();var large=new byte[120000];new Random(442).NextBytes(large);int generatedProgress=0,appliedProgress=0;
        progress.Nodes[10].MapRequested=delegate(ulong peer,string key){generatedProgress++;return large;};
        progress.Nodes[20].MapApplied=delegate(string key,byte[] data){appliedProgress++;};
        const string progressKey="r|0|0|9|0";progress.Nodes[20].RequestMap(progressKey);var firstRequest=progress.Queue[0];progress.Queue.Clear();
        progress.Nodes[10].Receive(firstRequest.From,firstRequest.Bytes,0);var progressReplies=new List<Packet>(progress.Queue);progress.Queue.Clear();
        Check(progressReplies.Count==3,"large test chunk was split into multiple transfer parts");
        progress.Nodes[20].Receive(progressReplies[0].From,progressReplies[0].Bytes,4.9);
        progress.Tick(9.8);progress.Nodes[20].RequestMap(progressKey);
        Check(progress.Queue.Count==0,"accepted part progress restarts the no-progress retry clock");
        progress.Tick(10.0);progress.Nodes[20].RequestMap(progressKey);
        Check(progress.Queue.Count==1,"a stalled partial transfer becomes retryable after five seconds without progress");
        progress.Pump();Check(generatedProgress==2 && appliedProgress==1,"stalled partial transfer retries and converges after a fresh response");
    }
    static void DeferredMapWork()
    {
        var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
        host.DeferMapWork=client.DeferMapWork=true;
        var generated=new List<string>();var applied=new List<string>();
        host.MapRequested=delegate(ulong peer,string key){generated.Add(key);return new byte[]{1,2,3};};
        client.MapApplied=delegate(string key,byte[] data){applied.Add(key);};
        client.RequestMap("o|0|0|0|0");client.RequestMap("r|0|0|0|0");client.RequestMap("p|0|0|0|0");
        var duplicate=lab.Queue[0];lab.Queue.Add(duplicate);lab.Pump();
        Check(generated.Count==0 && applied.Count==0,"receive only queues native map work");
        host.Tick(.1);lab.Pump();
        Check(generated.Count==3 && generated[0].StartsWith("r|") && generated[1].StartsWith("p|") && generated[2].StartsWith("o|"),
            "one host tick serves the queued prerequisites in collision-first order");
        Check(applied.Count==0,"completed native map application waits for the client frame boundary");
        client.CancelMap("r|0|0|0|0");client.RequestMap("r|0|0|0|0");
        client.Tick(.1);lab.Pump();
        Check(applied.Count==1 && applied[0].StartsWith("p|"),"cancelled completed job is skipped; the next world prerequisite applies");
        host.Tick(.2);lab.Pump();client.Tick(.2);lab.Pump();
        Check(generated.Count==3 && applied.Count==2 && applied[1].StartsWith("o|"),
            "decoration work follows world prerequisites and the canceled cell stays unapplied");
        host.Tick(.4);lab.Pump();
        Check(generated.Count==3,"duplicate queued requests are generated only once");
        client.RequestMap("o|0|0|1|0");lab.Pump();host.RemovePeer(20);host.Tick(.5);lab.Pump();
        Check(generated.Count==3,"retired peers cannot run queued native map work");

        var batch=new Lab();batch.Connect();host=batch.Nodes[10];client=batch.Nodes[20];client.DeferMapWork=true;
        int count=0;host.MapRequested=delegate{return new byte[]{1};};client.MapApplied=delegate{count++;};
        client.RequestMap("o|0|0|0|0");client.RequestMap("o|0|0|1|0");batch.Pump();
        Check(count==0,"multiple complete responses perform no native work in receive");
        client.Tick(.1);batch.Pump();Check(count==1,"client applies at most one completed manifest per tick");
        client.Tick(.2);batch.Pump();Check(count==2,"queued client manifests converge over subsequent frames");
        client.RequestMap("o|0|0|2|0");batch.Pump();
        client.MapApplied=delegate{throw new InvalidOperationException("deferred fixture failure");};
        client.Tick(.3);Check(client.Error!=null && !client.Ready,"deferred apply failure preserves the join safety barrier");
    }
}
