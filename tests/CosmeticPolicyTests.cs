using System;
using TldMp;
internal static class CosmeticPolicyTests
{
    static int count;
    static void Check(bool value,string message){count++;if(!value)throw new Exception(message);}
    static void Main()
    {
        var limiter=new CosmeticRateLimiter();
        int allowed=0;
        for(int i=0;i<50;i++)if(limiter.Allow(5,10.0+i*0.001))allowed++;
        Check(allowed==CosmeticRateLimiter.MaxPerSecond,"burst is capped at "+CosmeticRateLimiter.MaxPerSecond+" per second");
        Check(limiter.Allow(5,11.0),"budget renews after one second");
        Check(limiter.Allow(6,10.5),"budget is per sender");
        Check(!limiter.Allow(0,10.5) && !limiter.Allow(7,double.NaN) && !limiter.Allow(7,double.PositiveInfinity),"invalid sender or clock is refused");
        Check(limiter.Allow(7,20.0) && limiter.Allow(7,19.0),"clock stepping backwards starts a new window");
        limiter.Remove(7);Check(limiter.Allow(7,19.5),"removed sender starts fresh");
        limiter.Clear();
        Check(limiter.Allow(5,0.0),"clear resets budgets");

        Check(CosmeticPolicy.FoodBite(0,1,false),"first bite replays");
        Check(CosmeticPolicy.FoodBite(3,6,false),"coalesced bites replay once");
        Check(!CosmeticPolicy.FoodBite(3,3,false),"no index change is not a bite");
        Check(!CosmeticPolicy.FoodBite(5,2,false),"index regression is not a bite");
        Check(!CosmeticPolicy.FoodBite(-1,1,false),"unknown prior index is not a bite");
        Check(!CosmeticPolicy.FoodBite(0,1,true),"continuous food does not replay bite sounds");

        Check(CosmeticPolicy.IgnitionClick(false,true,false),"remote ignition on clicks");
        Check(CosmeticPolicy.IgnitionClick(true,false,false),"remote ignition off clicks");
        Check(!CosmeticPolicy.IgnitionClick(false,false,false),"no edge no click");
        Check(!CosmeticPolicy.IgnitionClick(false,true,true),"local driver already hears the click");
        Check(!CosmeticPolicy.IgnitionClick(true,false,true),"local driver ignition off already heard");

        Console.WriteLine("Cosmetic policy: "+count+" checks passed");
    }
}
