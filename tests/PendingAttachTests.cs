using System;
using System.Collections.Generic;
using TldMp;
class PendingAttachTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    // Wire layout of a native attachable (type 42) message up to the slot index.
    static byte[] Attach(uint id,int attachType,uint parent,int index)
    {
        var b=new List<byte>{42};b.AddRange(BitConverter.GetBytes(id));b.AddRange(BitConverter.GetBytes(attachType));
        if(attachType==0)return b.ToArray();
        b.AddRange(BitConverter.GetBytes(2));b.AddRange(BitConverter.GetBytes(parent));b.AddRange(BitConverter.GetBytes(-1));
        b.AddRange(new byte[24]);b.AddRange(BitConverter.GetBytes(index));b.AddRange(new byte[24+24+12+12]);
        return b.ToArray();
    }
    // Any per-item state message: type, uint id, payload marker.
    static byte[] State(byte type,uint id,byte marker){var b=new List<byte>{type};b.AddRange(BitConverter.GetBytes(id));b.Add(marker);return b.ToArray();}
    static void Main()
    {
        var present=new HashSet<uint>();Func<uint,bool> exists=present.Contains;
        var q=new PendingItemQueue();

        // What is held: per-item state only.
        foreach(byte t in new byte[]{23,24,25,26,27,28,30,32,33,37,38,40,41,42,48})Check(PendingItemQueue.Holdable(t),"state type not holdable: "+t);
        foreach(byte t in new byte[]{10,16,17,18,19,20,21,22,29,31,36,39,43,44,45,46,47})Check(!PendingItemQueue.Holdable(t),"transient type holdable: "+t);
        Check(PendingItemQueue.Parent(Attach(5,1,77,3))==77 && PendingItemQueue.Parent(Attach(5,0,0,0))==0 && PendingItemQueue.Parent(State(32,5,1))==0,"parent extraction");

        // Recorded bus sequence: two copies of the wheel attach, then its paint,
        // all before the wheel exists. All are held, in arrival order.
        uint bus=2147483999,wheel=2147484012;present.Add(bus);
        Check(q.Hold(wheel,bus,9,Attach(wheel,1,bus,13),0) && q.Hold(wheel,bus,9,Attach(wheel,1,bus,13),0.2) && q.Hold(wheel,0,9,State(32,wheel,7),0.3),"early messages not held");
        Check(q.Count==3 && q.Items==1 && q.TakeReady(exists,1)==null,"replayed before the item exists");
        present.Add(wheel);
        var ready=q.TakeReady(exists,2);
        Check(ready!=null && ready.Count==3 && ready[0].Bytes[0]==42 && ready[2].Bytes[0]==32 && ready[2].Bytes[5]==7 && ready[0].Sender==9,"held messages not replayed in arrival order with their sender");
        Check(q.Count==0 && q.Items==0 && q.Replayed==3,"queue not emptied");

        // Each attachment's parent gates its own replay.
        present.Add(10);
        Check(q.Hold(10,500,1,Attach(10,1,500,1),3) && q.TakeReady(exists,4)==null,"replayed before parent exists");
        present.Add(500);Check(q.TakeReady(exists,5).Count==1,"not replayed once parent exists");

        q.Clear(); present.Add(20); present.Add(600);
        q.Hold(20,600,1,Attach(20,1,600,1),0);q.Hold(20,700,1,Attach(20,1,700,2),0);
        var appliedParents=new List<uint>();var missingParents=new List<uint>();
        Action<PendingItemQueue.Held> recordParent=delegate(PendingItemQueue.Held h){appliedParents.Add(PendingItemQueue.Parent(h.Bytes));};
        q.Drain(exists,1,128,32,delegate{return true;},recordParent,missingParents.Add);
        Check(appliedParents.Count==1 && appliedParents[0]==600 && q.Count==1 && missingParents.Contains(700),"ready older attachment blocked by later missing parent");
        present.Add(700);q.Drain(exists,2,128,32,delegate{return true;},recordParent,missingParents.Add);
        Check(appliedParents.Count==2 && appliedParents[1]==700 && q.Count==0,"later attachment did not wait for its own parent");
        q.Hold(20,800,1,Attach(20,1,800,1),0);q.Hold(20,0,1,Attach(20,0,0,0),0);
        q.Drain(exists,1,128,32,delegate{return true;},recordParent,missingParents.Add);
        Check(q.Count==2 && appliedParents.Count==2 && missingParents.Contains(800),"detach bypassed earlier missing parent");
        q.Clear();

        // A newer message for an item that now exists flushes older held state first.
        Check(q.Hold(11,0,1,State(41,11,1),6) && q.Hold(11,0,1,State(41,11,2),6.1),"fluid states not held");
        present.Add(11);
        Check(q.Has(11),"Has");
        var older=q.Take(11);
        Check(older.Count==2 && older[0].Bytes[5]==1 && older[1].Bytes[5]==2 && !q.Has(11),"Take did not return held state in order");

        // Bounded: per item, overall, and by age.
        for(int i=0;i<PendingItemQueue.PerItemCapacity+3;i++)q.Hold(12,0,1,State(23,12,(byte)i),7);
        Check(q.Count==PendingItemQueue.PerItemCapacity && q.Dropped==3,"per-item cap not enforced");
        Check(q.TakeReady(exists,7+PendingItemQueue.ExpirySeconds)==null && q.Count==0 && q.Expired==PendingItemQueue.PerItemCapacity,"held state did not expire");
        for(uint i=0;i<PendingItemQueue.Capacity+5;i++)q.Hold(100000+i,0,1,State(32,100000+i,0),8);
        Check(q.Count==PendingItemQueue.Capacity && q.Dropped==8,"global cap not enforced");
        q.Clear();Check(q.Count==0 && q.Items==0,"clear");

        // Invalid input and buffer independence.
        Check(!q.Hold(0,0,1,State(32,0,0),0) && !q.Hold(5,0,1,null,0),"invalid hold accepted");
        var buffer=State(32,13,4);q.Hold(13,0,1,buffer,0);buffer[5]=99;present.Add(13);
        Check(q.TakeReady(exists,1)[0].Bytes[5]==4,"held message aliases caller buffer");

        // Regression: the global limit used to be checked only for a new ID.
        q.Clear();
        for(uint i=1;i<=PendingItemQueue.Capacity;i++)q.Hold(i,0,1,State(23,i,1),0);
        Check(!q.Hold(1,0,1,State(23,1,2),0) && q.Count==PendingItemQueue.Capacity,"existing ID bypassed the global message limit");
        q.Remove(1);Check(q.Count==PendingItemQueue.Capacity-1 && q.Bytes==q.Count*6,"deletion did not free its exact byte/message accounting");
        q.Clear();
        var big=new byte[58000];big[0]=23;
        for(uint i=1;i<=64;i++)q.Hold(i,0,1,big,0);
        Check(q.Bytes<=PendingItemQueue.ByteCapacity && q.Count==PendingItemQueue.ByteCapacity/big.Length,"byte budget exceeded");
        q.Clear();

        // A large ready backlog is spread across frames, FIFO per item, and
        // missing entries cannot starve ready entries behind them.
        for(uint i=1;i<=20;i++)for(int j=0;j<16;j++)q.Hold(i,0,1,State(23,i,(byte)j),0);
        var last=new Dictionary<uint,int>();int applied=0;
        Action<PendingItemQueue.Held> replay=delegate(PendingItemQueue.Held h){uint id=BitConverter.ToUInt32(h.Bytes,1);int previous;last.TryGetValue(id,out previous);Check(h.Bytes[5]==(last.ContainsKey(id)?previous+1:0),"per-item FIFO changed across drain frames");last[id]=h.Bytes[5];applied++;};
        for(int frame=0;frame<20 && q.Count>0;frame++)
        {int before=applied;int n=q.Drain(delegate(uint id){return true;},1,128,32,delegate{return true;},replay,null);Check(n<=32 && applied-before==n,"replay exceeded its frame message budget");}
        Check(applied==320 && q.Count==0 && q.Bytes==0,"bounded replay lost messages or accounting");
        q.Hold(100,0,1,State(23,100,0),0);q.Hold(101,0,1,State(23,101,0),0);
        int probes=0;
        q.Drain(delegate(uint id){probes++;return id==101;},1,1,32,delegate{return true;},delegate{applied++;},null);
        Check(probes==1 && q.Count==2,"scan budget ignored");
        q.Drain(delegate(uint id){return id==101;},1,1,32,delegate{return true;},delegate{applied++;},null);
        Check(q.Count==1 && q.Has(100),"missing object starved the next ready object");
        int calls=0;q.Drain(delegate(uint id){return true;},1,128,32,delegate{return calls++==0;},delegate{throw new Exception("handler started beyond time budget");},null);
        Check(q.Count==1,"time budget ignored");q.Clear();

        RecoveryChecks();
        AttachmentChecks();
        Console.WriteLine("PASS: "+checks+" held item-message assertions; state that beats its item is replayed in arrival order once the item and parent exist, newer state flushes older first, and the queue is bounded.");
    }
    static void RecoveryChecks()
    {
        var r=new ItemRecoveryRequests();var sent=new List<uint>();var present=new HashSet<uint>();
        r.Watch(7,0);r.Watch(7,0);
        Check(r.Count==1 && r.Tick(present.Contains,0,2,sent.Add)==1,"first missing definition request not deduplicated");
        Check(r.Tick(present.Contains,.5,2,sent.Add)==0 && r.Tick(present.Contains,1,2,sent.Add)==1,"retry backoff ignored");
        present.Add(7);Check(r.Tick(present.Contains,2,2,sent.Add)==0 && r.Count==0,"recovery continued after creation arrived");
        r.Watch(8,0);r.Remove(8,.1);
        Check(r.Deleted(8,1) && r.Tick(present.Contains,1,2,sent.Add)==0,"deleted item requested back");
        Check(!r.Deleted(8,61),"deletion tombstone never expires");r.Clear();
        r.Watch(9,0);
        foreach(double t in new double[]{0,1,3,7,15,31,47,59})r.Tick(present.Contains,t,2,sent.Add);
        Check(r.Sent==ItemRecoveryRequests.MaxAttempts && r.Exhausted==1,"unresolved definition retries are unbounded");
        r.Tick(present.Contains,61,2,sent.Add);Check(r.Count==0,"failed recovery never retired");
        r.Refresh(7,62);Check(r.Tick(present.Contains,62,2,sent.Add,delegate{return true;})==0,"overflow refresh overtook its older FIFO");
        Check(r.Tick(present.Contains,63,2,sent.Add)==1 && r.Count==0,"overflow did not refresh complete state after drain");
        r.Clear();for(uint i=1;i<=ItemRecoveryRequests.Capacity+10;i++)r.Watch(i,0);
        Check(r.Count==ItemRecoveryRequests.Capacity && r.Tick(delegate{return false;},0,2,sent.Add)==2,"recovery table or per-frame request budget exceeded");
        r.Clear();Check(r.Count==0,"session reset retained recovery/tombstones");
    }
    static void AttachmentChecks()
    {
        byte[] wanted=Attach(5,1,77,3);
        Check(AttachmentReplayPolicy.SameSlot(wanted,1,2,77,0,0,0,0,3),"matching native slot not recognized");
        Check(!AttachmentReplayPolicy.SameSlot(wanted,1,2,78,0,0,0,0,3),"different parent incorrectly treated as already applied");
        Check(!AttachmentReplayPolicy.SameSlot(wanted,1,2,77,0,0,0,0,4),"different slot incorrectly treated as already applied");
        Check(!AttachmentReplayPolicy.SameSlot(wanted,1,1,77,0,0,0,0,3),"collider anchor skipped without checking its pose");
        Check(!AttachmentReplayPolicy.SameSlot(Attach(5,0,0,0),1,2,77,0,0,0,0,3),"detach skipped because the part was attached");
    }
}
