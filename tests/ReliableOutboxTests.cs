using System;
using System.Collections.Generic;
using TldMp;

public static class ReliableOutboxTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    public static int Main()
    {
        try
        {
            bool accepting=false;var accepted=new List<int>();var errors=new List<ulong>();
            var q=new ReliableOutbox(delegate(ulong peer,byte[] bytes,bool reliable){if(reliable&&!accepting)return false;accepted.Add(bytes[0]);return true;},delegate(ulong peer,string error){errors.Add(peer);});
            var first=new byte[]{1};Check(q.Send(20,first,true,0),"Refused reliable send retained.");first[0]=99;
            q.Send(20,new byte[]{2},true,0);Check(q.Send(20,new byte[]{3},false,0) && q.PendingPackets==2 && accepted.Count==1 && accepted[0]==3,"Unreliable motion proceeds independently while reliable packets are blocked.");
            q.Flush(.1);Check(accepted.Count==1 && q.PendingPackets==2,"Refused reliable head cannot be skipped or block the independent lane.");
            accepting=true;q.Flush(.2);Check(accepted.Count==3 && accepted[1]==1 && accepted[2]==2 && q.PendingBytes==0,"Reliable retry is FIFO and owns a copy of mutable payloads.");

            errors.Clear();accepted.Clear();q=new ReliableOutbox(delegate(ulong peer,byte[] bytes,bool reliable){if(peer==20)return false;accepted.Add(bytes[0]);return true;},delegate(ulong peer,string error){errors.Add(peer);});
            q.Send(20,new byte[]{1},true,0);q.Send(30,new byte[]{2},true,0);q.Flush(21);
            Check(errors.Count==1 && errors[0]==20 && accepted.Count==1 && accepted[0]==2 && q.PendingPackets==0,"A stalled peer times out without blocking a healthy peer.");

            errors.Clear();q=new ReliableOutbox(delegate(ulong peer,byte[] bytes,bool reliable){return false;},delegate(ulong peer,string error){errors.Add(peer);},16,24,8);
            q.Send(30,new byte[8],true,0);q.Send(20,new byte[16],true,0);Check(!q.Send(20,new byte[1],true,0),"Backlog limit enforced.");
            Check(errors.Count==0 && q.PendingBytes==8,"Failure callback deferred outside protocol Send/broadcast enumeration; other peer retained.");
            q.Flush(.1);Check(errors.Count==1 && errors[0]==20,"Overflow failure reported on safe flush boundary.");q.Clear();q.Flush(1);Check(q.PendingPackets==0 && q.PendingBytes==0 && errors.Count==1,"Session clear discards queues and failure records.");

            var random=new Random(97119);var order=new List<int>();bool recover=false;
            q=new ReliableOutbox(delegate(ulong peer,byte[] bytes,bool reliable){if(!recover && random.NextDouble()<.35)return false;order.Add(BitConverter.ToInt32(bytes,0));return true;},delegate(ulong peer,string error){throw new Exception(error);});
            for(int i=0;i<1000;i++){q.Send(20,BitConverter.GetBytes(i),true,i*.01);q.Flush(i*.01);}
            recover=true;for(int tick=0;q.PendingPackets>0 && tick<1000;tick++)q.Flush(10+tick*.01);
            Check(order.Count==1000,"Every reliable message eventually accepted after seeded refusal/recovery.");
            for(int i=0;i<order.Count;i++)Check(order[i]==i,"No reliable loss, reordering or duplication under 35% enqueue refusals.");
            Console.WriteLine("PASS: "+checks+" production outbox assertions: transient refusal, FIFO/immutable payloads, peer isolation, timeout, bounds, deferred failure callback and 1,000-message recovery. Native Steam send results are injected.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
