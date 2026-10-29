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

        // The latest attachment's parent gates the replay.
        present.Add(10);
        Check(q.Hold(10,500,1,Attach(10,1,500,1),3) && q.TakeReady(exists,4)==null,"replayed before parent exists");
        present.Add(500);Check(q.TakeReady(exists,5).Count==1,"not replayed once parent exists");

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
        Console.WriteLine("PASS: "+checks+" held item-message assertions; state that beats its item is replayed in arrival order once the item and parent exist, newer state flushes older first, and the queue is bounded.");
    }
}
