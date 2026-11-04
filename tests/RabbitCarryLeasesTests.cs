using System;
using System.Collections.Generic;
using TldMp;

static class RabbitCarryLeasesTests
{
    static int checks;
    static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
    static bool Reserve(RabbitCarryLeases ledger,ulong peer,ulong session,ulong nonce,uint item,double now,out RabbitCarryLeases.Lease lease)
    {
        lease=null;
        return ledger.BeginRequest(peer,session,nonce,now)&&ledger.TryReserve(peer,session,nonce,item,0,new object(),new object(),new object(),now,out lease);
    }
    static int Main()
    {
        var l=new RabbitCarryLeases();RabbitCarryLeases.Lease a,b;
        Check(!Reserve(l,1,10,1,100,0,out a),"unknown peer denied");
        Check(l.Observe(1,10,true)==null,"initial observation");
        Check(Reserve(l,1,10,1,100,0,out a),"first reservation");
        Check(a.Token>0&&a.Request==1&&a.Item==100&&a.Session==10,"lease identity");
        Check(l.TryGetPeer(1,out b)&&ReferenceEquals(a,b)&&l.TryGetItem(100,out b)&&ReferenceEquals(a,b),"both indexes");
        Check(!Reserve(l,1,10,2,101,0,out b),"one lease per peer");
        l.Observe(2,20,true);
        Check(!Reserve(l,2,20,1,100,0,out b),"one lease per rabbit");
        Check(l.Release(1,10,100,a.Token+1)==null,"wrong token cannot release");
        Check(!l.Refresh(1,11,100,a.Token,1),"wrong session cannot refresh");
        Check(!l.Refresh(1,10,100,a.Token,double.NaN),"nonfinite time rejected");
        Check(l.Refresh(1,10,100,a.Token,2),"heartbeat refresh");
        Check(!l.Refresh(1,10,100,a.Token,1),"clock rollback cannot refresh");
        var expired=new List<RabbitCarryLeases.Lease>();l.Expire(4.99,expired);
        Check(expired.Count==0,"refreshed lease survives before deadline");
        Check(!l.Refresh(1,10,100,a.Token,5),"expired lease cannot revive");
        l.Expire(5,expired);Check(expired.Count==1&&ReferenceEquals(expired[0],a),"expiry returns physical cleanup target");
        Check(!l.TryGetItem(100,out b)&&!l.TryGetPeer(1,out b),"expiry removes both indexes");
        Check(!Reserve(l,1,10,2,101,5,out b),"failed exclusive attempt consumed nonce");
        Check(!Reserve(l,2,20,1,100,5,out b),"failed contested attempt consumed nonce");
        Check(Reserve(l,1,10,3,101,5,out b)&&b.Token>a.Token,"tokens increase");
        Check(ReferenceEquals(l.Observe(1,10,false),b),"readiness loss returns lease");
        l.Observe(1,10,true);
        Check(!Reserve(l,1,10,3,101,6,out a),"same session retains watermark after readiness change");
        Check(Reserve(l,1,10,4,101,6,out a),"new nonce accepted");
        Check(ReferenceEquals(l.Disconnect(1),a),"disconnect returns lease");
        l.Observe(1,10,true);
        Check(!Reserve(l,1,10,4,101,7,out b),"same session retains watermark after disconnect");
        Check(Reserve(l,1,10,5,101,7,out a),"same session advances");
        Check(ReferenceEquals(l.Observe(1,11,true),a),"new session revokes old lease");
        Check(Reserve(l,1,11,1,101,8,out b)&&b.Token>a.Token,"new session resets nonce but not token");
        Check(l.Release(1,10,101,a.Token)==null,"old session cannot release replacement");
        Check(ReferenceEquals(l.Release(1,11,101,b.Token),b),"matching release returns lease");
        Check(!Reserve(l,1,11,2,102,-1,out a),"negative time rejected");
        Check(Reserve(l,1,11,2,102,9,out a),"invalid time did not consume nonce");
        expired.Clear();l.ReleaseAll(expired);
        Check(expired.Count==1&&ReferenceEquals(expired[0],a),"teardown returns leases");
        l.Observe(1,11,true);
        Check(!Reserve(l,1,11,2,102,10,out b),"teardown preserves replay watermark");
        Check(Reserve(l,1,11,3,102,10,out b),"fresh request after teardown");
        l.Observe(2,20,true);
        Check(l.BeginRequest(2,20,2,10),"request accepted before geometry validation");
        Check(!l.BeginRequest(2,20,2,11),"failed geometry cannot replay request");
        Check(!l.TryReserve(2,20,2,103,0,new object(),new object(),new object(),13,out a),"pending request expires");
        Check(!l.TryReserve(2,20,2,103,0,new object(),new object(),new object(),11,out a),"expired reservation cannot revive");
        Check(l.BeginRequest(2,20,3,14),"next request accepted");
        l.CancelRequest(2,20,3);
        Check(!l.TryReserve(2,20,3,103,0,new object(),new object(),new object(),14,out a),"cancelled native validation cannot reserve");
        Check(!l.BeginRequest(2,20,3,14),"cancel retains watermark");
        Console.WriteLine("PASS "+checks+" rabbit carry lease checks");return 0;
    }
}
