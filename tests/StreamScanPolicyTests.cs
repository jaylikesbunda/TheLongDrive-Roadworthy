using System;
using System.Collections.Generic;
using TldMp;
class StreamScanPolicyTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Main()
    {
        // ---- manifest-ready cache -------------------------------------------------
        var cache=new ManifestReadyCache();
        long here=ManifestReadyCache.Combine(ManifestReadyCache.Combine(ManifestReadyCache.Seed,1),2);
        long moved=ManifestReadyCache.Combine(ManifestReadyCache.Combine(ManifestReadyCache.Seed,1),3);
        long swapped=ManifestReadyCache.Combine(ManifestReadyCache.Combine(ManifestReadyCache.Seed,2),1);
        Check(here!=moved && here!=swapped && ManifestReadyCache.Combine(0,5)!=ManifestReadyCache.Combine(0,6),"signature does not tell inputs apart or ignores order");
        Check(!cache.TryReuse(here,0),"empty cache reused");
        cache.StoreReady(here,10);
        Check(cache.TryReuse(here,10.5),"ready result not reused for the same chunks");
        Check(!cache.TryReuse(moved,10.5),"reused after moving into another chunk");
        Check(!cache.TryReuse(here,10+ManifestReadyCache.MaxAgeSeconds),"ready result never expires");
        cache.StoreReady(here,20);cache.Invalidate();
        Check(!cache.TryReuse(here,20.1),"invalidated cache reused");

        // ---- eviction gate --------------------------------------------------------
        var gate=new EvictGate();
        var ring=new List<int>{1,2,3};
        Check(gate.ShouldRun(ring,3,100,0),"first call must run");
        Check(!gate.ShouldRun(ring,3,100,0.2),"unchanged state ran again within the interval");
        Check(gate.ShouldRun(ring,3,101,0.3),"new generated cell did not trigger cleanup");
        Check(gate.ShouldRun(new List<int>{1,2,3},3,101,0.4),"replaced needed list did not trigger cleanup");
        Check(gate.ShouldRun(ring,2,101,0.5),"shrunk needed list did not trigger cleanup");
        Check(!gate.ShouldRun(ring,2,101,0.9),"ran again before the interval after a change");
        Check(gate.ShouldRun(ring,2,101,0.5+EvictGate.IntervalSeconds),"periodic sweep never runs");

        // ---- scan budget ----------------------------------------------------------
        Check(ScanBudget.Next(0,0,48).Finished && ScanBudget.Next(0,0,48).End==0,"empty list not finished");
        var step=ScanBudget.Next(0,100,48);
        Check(step.Start==0 && step.End==48 && !step.Finished,"first slice");
        step=ScanBudget.Next(step.End,100,48);
        Check(step.Start==48 && step.End==96 && !step.Finished,"second slice");
        step=ScanBudget.Next(step.End,100,48);
        Check(step.Start==96 && step.End==100 && step.Finished,"last slice");
        step=ScanBudget.Next(0,48,48);
        Check(step.End==48 && step.Finished,"exact fit not finished");
        step=ScanBudget.Next(90,40,48);
        Check(step.Start==0 && step.End==40 && step.Finished,"cursor past a shrunk list not restarted");
        step=ScanBudget.Next(-5,10,0);
        Check(step.Start==0 && step.End==1 && !step.Finished,"invalid cursor or budget not clamped");
        // Every key of a long list is visited exactly once per pass.
        var seen=new int[1000];int cursor=0,calls=0;
        while(true){var s=ScanBudget.Next(cursor,seen.Length,ScanBudget.KeysPerCall);for(int i=s.Start;i<s.End;i++)seen[i]++;calls++;if(s.Finished)break;cursor=s.End;}
        bool once=true;foreach(int n in seen)if(n!=1)once=false;
        Check(once && calls==(seen.Length+ScanBudget.KeysPerCall-1)/ScanBudget.KeysPerCall,"a pass did not visit every key exactly once in bounded slices");
        Console.WriteLine("PASS: "+checks+" stream scan policy assertions; ready manifests are reused until the chunks around a player change or two seconds pass, cleanup runs on change or once a second, and long scans are split into bounded slices that visit every key once.");
    }
}
