using System;
using System.Collections.Generic;
using TldMp;

// Bounded join stages (A3): per-stage deadlines refreshed only by
// snapshot-stage progress (Begin/Part/End/Ack/map bytes), explicit
// stage+reason errors, WaitingHostLoad reporting while the host is still
// loading natively, and per-peer/transfer coalescing of duplicate snapshot
// requests. Production StableNode code runs here; only the transport and
// world callbacks are stand-ins.
public static class JoinStageBoundTests
{
    static int checks;
    static void Check(bool ok,string what){checks++;if(!ok)throw new Exception("FAIL: "+what);}
    sealed class Packet {public ulong From,To;public byte[] Bytes;}
    sealed class Lab
    {
        public StableNode Host,Client;public double Now;public int SnapshotServes,Applied;
        public readonly List<Packet> Queue=new List<Packet>();
        public byte[] World=new byte[]{1,2,3};
        public bool ServeSnapshots=true,WorldEnterable=true;
        public Lab()
        {
            Host=new StableNode(10,10,999,101,"stage",0);
            Host.Send=delegate(ulong to,byte[] b,bool r){Queue.Add(new Packet {From=10,To=to,Bytes=b});};
            Host.SnapshotRequested=delegate(ulong peer){SnapshotServes++;if(ServeSnapshots)Host.BeginSnapshot(peer,World);};
            Host.MapRequested=delegate(ulong peer,string key){return new byte[]{7,7,7,7};};
            Client=new StableNode(20,10,999,202,"stage",0);
            Client.Send=delegate(ulong to,byte[] b,bool r){Queue.Add(new Packet {From=20,To=to,Bytes=b});};
            Client.ApplySnapshot=delegate(byte[] b){Applied++;};
            Client.CanEnterWorld=delegate{return WorldEnterable;};
            Client.Tick(0);Pump();
            Check(Client.Compatible,"handshake completed before stage tests");
        }
        public void Pump()
        {
            int guard=0;
            while(Queue.Count>0)
            {
                if(++guard>10000)throw new Exception("reply loop");
                var p=Queue[0];Queue.RemoveAt(0);
                if(p.To==10)Host.Receive(p.From,p.Bytes,Now);else Client.Receive(p.From,p.Bytes,Now);
            }
        }
        // Deliver only matching packets; everything else stays queued.
        public void PumpWhere(Func<Packet,bool> filter)
        {
            for(int i=0;i<Queue.Count;)
            {
                var p=Queue[i];
                if(!filter(p)){i++;continue;}
                Queue.RemoveAt(i);
                if(p.To==10)Host.Receive(p.From,p.Bytes,Now);else Client.Receive(p.From,p.Bytes,Now);
            }
        }
        public void Join()
        {
            Client.RequestSnapshot();Pump();
            Check(Client.Ready && Client.Error==null,"baseline join completes: "+(Client.Error??"not ready"));
        }
    }
    static StableOp Op(Packet p){var m=StableMessage.Decode(p.Bytes);return m==null?(StableOp)0:m.Op;}
    static void RequestedStageExpiry()
    {
        var l=new Lab();l.ServeSnapshots=false;l.Client.HostWorldLoaded=true;
        l.Client.RequestSnapshot();
        l.Now=29.5;l.Client.Tick(l.Now);
        Check(l.Client.Error==null,"Requested stage alive before its 30 s deadline.");
        l.Now=30.5;l.Client.Tick(l.Now);
        Check(l.Client.Error!=null && l.Client.Error.Contains("stage Requested") && l.Client.Error.Contains("no snapshot transfer started"),
            "Requested expiry reports explicit stage+reason: "+l.Client.Error);
        Console.WriteLine("PASS: Requested stage deadline expires with explicit stage+reason.");
    }
    static void WaitingHostLoadReport()
    {
        var l=new Lab();l.ServeSnapshots=false;
        l.Client.RequestSnapshot();
        Check(l.Client.JoinStage=="WaitingHostLoad","waiting join reports WaitingHostLoad as its own stage: "+l.Client.JoinStage);
        Check(l.Client.JoinStageReason.Contains("host") && l.Client.JoinStageReason.Contains("load"),"waiting join reports the host-load reason: "+l.Client.JoinStageReason);
        l.Now=20;l.Client.Tick(l.Now);
        Check(l.Client.Error==null,"no join deadline runs while the host is still loading natively");
        l.Client.HostWorldLoaded=true;
        l.Now=49.9;l.Client.Tick(l.Now);
        Check(l.Client.Error==null,"pre-load wait never consumes the bounded Requested deadline");
        l.Now=50.5;l.Client.Tick(l.Now);
        Check(l.Client.Error!=null && l.Client.Error.Contains("stage Requested") && l.Client.Error.Contains("no snapshot transfer started"),
            "once the host world is loaded the stalled request expires with explicit stage+reason: "+l.Client.Error);
        Console.WriteLine("PASS: waiting-for-host-load is its own stage/reason and arms no deadline.");
    }
    static void ReceivingProgressAndHeartbeat()
    {
        var l=new Lab();l.World=new byte[100000];
        l.Now=10;l.Client.Tick(l.Now);l.Client.RequestSnapshot();
        l.PumpWhere(delegate(Packet p){return p.To==10;});
        l.Now=10.1;l.PumpWhere(delegate(Packet p){return p.To==20 && Op(p)==StableOp.Begin;});
        Check(l.Client.JoinStage=="Receiving","Begin moves the join into Receiving: "+l.Client.JoinStage);
        l.Now=20;l.PumpWhere(delegate(Packet p){return p.To==20 && Op(p)==StableOp.Part;});
        l.Now=45;l.Host.Tick(l.Now);
        l.PumpWhere(delegate(Packet p){return p.To==20 && Op(p)==StableOp.Heartbeat;});
        l.Client.Tick(49.9);
        Check(l.Client.Error==null,"Part progress reset the stage deadline (alive 29.9 s after progress).");
        l.Client.Tick(50.5);
        Check(l.Client.Error!=null && l.Client.Error.Contains("stage Receiving") && l.Client.Error.Contains("snapshot transfer stopped"),
            "Receiving expiry reports explicit stage+reason after progress stalled: "+l.Client.Error);
        Check(!l.Client.Error.Contains("heartbeat"),"transport heartbeat kept liveness but never extends the stage deadline.");
        Console.WriteLine("PASS: Receiving stage honors progress reset and ignores heartbeats.");
    }
    static void WaitingWorldMapProgress()
    {
        var l=new Lab();l.Join();
        l.WorldEnterable=false;
        l.Now=10;l.Client.Tick(l.Now);l.Client.RequestSnapshot();l.Pump();
        Check(l.Client.JoinStage=="WaitingWorld","applied snapshot waits in WaitingWorld: "+l.Client.JoinStage);
        l.Now=25;l.Client.RequestMap("r|0|0|0|0");l.Pump();
        l.Client.Tick(54.9);
        Check(l.Client.Error==null,"map bytes reset the WaitingWorld deadline (alive 29.9 s after map progress).");
        l.Client.Tick(55.5);
        Check(l.Client.Error!=null && l.Client.Error.Contains("stage WaitingWorld") && l.Client.Error.Contains("the world never became enterable"),
            "WaitingWorld expiry reports explicit stage+reason: "+l.Client.Error);
        Console.WriteLine("PASS: WaitingWorld stage honors map-byte progress and expires explicitly.");
    }
    static void AwaitingLiveExpiry()
    {
        var l=new Lab();l.Join();
        l.Now=10;l.Client.Tick(l.Now);l.Client.RequestSnapshot();
        l.PumpWhere(delegate(Packet p){return !(p.To==20 && Op(p)==StableOp.Live);});
        Check(l.Client.JoinStage=="AwaitingLive" && !l.Client.Ready,"Ack sent; Live withheld leaves the join in AwaitingLive");
        l.Client.Tick(40.5);
        Check(l.Client.Error!=null && l.Client.Error.Contains("stage AwaitingLive") && l.Client.Error.Contains("Live confirmation never arrived"),
            "AwaitingLive expiry reports explicit stage+reason: "+l.Client.Error);
        Console.WriteLine("PASS: AwaitingLive stage expires explicitly when Live never arrives.");
    }
    static void DuplicateRequestCoalescing()
    {
        var l=new Lab();l.World=new byte[100000];
        l.Client.RequestSnapshot();l.Client.RequestSnapshot();
        int requests=0;foreach(var p in l.Queue)if(p.To==10 && Op(p)==StableOp.Resync)requests++;
        Check(requests==1,"client coalesces duplicate snapshot requests within its retry window");
        l.PumpWhere(delegate(Packet p){return p.To==10;});
        Check(l.SnapshotServes==1,"first request started one transfer");
        l.Now=1.2;l.Client.Tick(l.Now);l.Client.RequestSnapshot();
        l.PumpWhere(delegate(Packet p){return p.To==10;});
        Check(l.SnapshotServes==1,"duplicate request for the in-flight transfer is coalesced per peer/transfer");
        l.Pump();
        Check(l.Client.Ready && l.Client.Error==null,"held transfer still completes normally");
        l.Now=5;l.Client.Tick(l.Now);l.Client.RequestSnapshot();l.Pump();
        Check(l.SnapshotServes==2 && l.Client.Ready,"a request after the transfer completed is a fresh resync");
        Console.WriteLine("PASS: duplicate snapshot requests coalesce per peer/transfer.");
    }
    static void ConstantsUnchanged()
    {
        Check(StableNode.PeerTimeout==20,"PeerTimeout=20 unchanged");
        Check(StableNode.JoinStageTimeout==30,"join stage deadline is the spec's 30 s");
        Console.WriteLine("PASS: liveness constants unchanged (PeerTimeout=20).");
    }
    public static int Main()
    {
        try
        {
            RequestedStageExpiry();WaitingHostLoadReport();ReceivingProgressAndHeartbeat();
            WaitingWorldMapProgress();AwaitingLiveExpiry();DuplicateRequestCoalescing();ConstantsUnchanged();
            Console.WriteLine("PASS: "+checks+" join-stage bounding assertions: per-stage deadlines, progress-only resets, heartbeat neutrality, explicit stage+reason errors, WaitingHostLoad reporting and duplicate request coalescing.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}

