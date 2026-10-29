using System;
using System.Collections.Generic;
using System.Diagnostics;
using TldMp;

internal static class SyncItemSchedulerTests
{
    sealed class Item
    {
        internal readonly int Id;
        internal Item(int id){Id=id;}
    }
    sealed class Trace
    {
        internal readonly List<int> Items=new List<int>();
        internal int Yields;
    }
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static Dictionary<int,Item> Make(int count,bool includeNull=false)
    {
        var items=new Dictionary<int,Item>();
        for(int i=0;i<count;i++)items.Add(i,includeNull && i%5==2?null:new Item(i));
        return items;
    }
    // Literal model of the native foreach's restart/skip behavior in SyncStuff.
    static bool LegacyBatch(Dictionary<int,Item> items,ref uint progress,float batchSize,Action<Item> update)
    {return LegacyBatch(items,ref progress,delegate{return batchSize;},update);}
    static bool LegacyBatch(Dictionary<int,Item> items,ref uint progress,Func<float> batchSize,Action<Item> update)
    {
        if(progress>=items.Count)return false;
        uint num=0,num2=0;
        foreach(KeyValuePair<int,Item> pair in items)
        {
            if(num2<progress){num2++;continue;}
            if(pair.Value!=null)update(pair.Value);
            num2++;num++;progress=num2;
            if((float)num>=batchSize())break;
        }
        return true;
    }
    static Trace ReplayLegacy(Dictionary<int,Item> items,float batchSize,Action<int,Dictionary<int,Item>> afterYield=null)
    {
        var trace=new Trace();uint progress=0;int batch=0;
        while(progress<items.Count)
        {
            LegacyBatch(items,ref progress,batchSize,i=>trace.Items.Add(i.Id));
            trace.Yields++;batch++;
            if(batch==1 && afterYield!=null)afterYield(batch,items);
        }
        return trace;
    }
    static Trace ReplayCached(Dictionary<int,Item> items,float batchSize,Action<int,Dictionary<int,Item>> afterYield=null)
    {
        var trace=new Trace();var scheduler=new SyncItemScheduler<int,Item>();scheduler.BeginPass();int batch=0;
        while(scheduler.HasMore(items.Count))
        {
            Assert(scheduler.ProcessBatch(items,batchSize,i=>{if(i!=null)trace.Items.Add(i.Id);}),"cached scheduler ended a batch early");
            trace.Yields++;batch++;
            if(batch==1 && afterYield!=null)afterYield(batch,items);
        }
        return trace;
    }
    static void Equal(Trace expected,Trace actual,string name)
    {
        Assert(expected.Yields==actual.Yields,name+" yield cadence differs: "+expected.Yields+" vs "+actual.Yields);
        Assert(String.Join(",",expected.Items)==String.Join(",",actual.Items),name+" item order differs");
    }
    static void MutationBetweenBatches(int remove,int add)
    {
        Action<int,Dictionary<int,Item>> mutation=delegate(int batch,Dictionary<int,Item> items)
        {items.Remove(remove);items.Add(add,new Item(add));};
        Equal(ReplayLegacy(Make(23,true),4,mutation),ReplayCached(Make(23,true),4,mutation),"between-batch remove/add");
    }
    static void Main()
    {
        foreach(float batchSize in new[]{-1f,0f,1f,2.5f,7f,Single.NaN,Single.PositiveInfinity})
            Equal(ReplayLegacy(Make(37,true),batchSize),ReplayCached(Make(37,true),batchSize),"batch size "+batchSize);
        MutationBetweenBatches(3,90);

        // Native code reads syncItemCount after each MultiUpd call, so a callback
        // that changes it can alter the current batch immediately.
        var dynamicLegacy=Make(8);var dynamicCached=Make(8);float legacyLimit=1,cachedLimit=1;uint dynamicProgress=0;
        var dynamicOld=new Trace();var dynamicNew=new Trace();
        LegacyBatch(dynamicLegacy,ref dynamicProgress,delegate{return legacyLimit;},delegate(Item i){dynamicOld.Items.Add(i.Id);if(i.Id==0)legacyLimit=100;});
        var dynamicScheduler=new SyncItemScheduler<int,Item>();dynamicScheduler.BeginPass();
        dynamicScheduler.ProcessBatch(dynamicCached,delegate{return cachedLimit;},delegate(Item i){dynamicNew.Items.Add(i.Id);if(i.Id==0)cachedLimit=100;});
        Assert(String.Join(",",dynamicOld.Items)==String.Join(",",dynamicNew.Items) && dynamicOld.Items.Count==dynamicCached.Count,"dynamic batch threshold was not read per item");

        // The save object can be replaced between coroutine yields. Native code
        // re-reads savedatascript.s.items; the cached scheduler must detect identity.
        var oldLegacy=Make(12);var oldCached=Make(12);uint changedProgress=0;
        LegacyBatch(oldLegacy,ref changedProgress,3,i=>{});
        var changedScheduler=new SyncItemScheduler<int,Item>();changedScheduler.BeginPass();
        changedScheduler.ProcessBatch(oldCached,3,i=>{});
        var newLegacy=Make(15);var newCached=Make(15);var legacyChangedTail=new Trace();var cachedChangedTail=new Trace();
        while(changedProgress<newLegacy.Count)LegacyBatch(newLegacy,ref changedProgress,3,i=>legacyChangedTail.Items.Add(i.Id));
        while(changedScheduler.HasMore(newCached.Count))changedScheduler.ProcessBatch(newCached,3,i=>cachedChangedTail.Items.Add(i.Id));
        Assert(String.Join(",",legacyChangedTail.Items)==String.Join(",",cachedChangedTail.Items),"dictionary replacement between batches differs");

        // Mutation by the final callback in a batch is not observed until the next
        // batch in the stock foreach; the optimized cursor must fall back then.
        var legacyItems=Make(8);var cachedItems=Make(8);uint legacyProgress=0;
        LegacyBatch(legacyItems,ref legacyProgress,2,i=>{if(i.Id==1)legacyItems.Add(99,new Item(99));});
        var scheduler=new SyncItemScheduler<int,Item>();scheduler.BeginPass();
        Assert(scheduler.ProcessBatch(cachedItems,2,i=>{if(i.Id==1)cachedItems.Add(99,new Item(99));}),"last-callback batch missing");
        Assert(legacyProgress==scheduler.Progress,"last-callback progress differs");
        var legacyTail=new Trace();while(legacyProgress<legacyItems.Count)LegacyBatch(legacyItems,ref legacyProgress,2,i=>legacyTail.Items.Add(i.Id));
        var cachedTail=new Trace();while(scheduler.HasMore(cachedItems.Count))scheduler.ProcessBatch(cachedItems,2,i=>cachedTail.Items.Add(i.Id));
        Assert(String.Join(",",legacyTail.Items)==String.Join(",",cachedTail.Items),"last-callback mutation fallback differs");

        // Mutation during a non-final batch still fails on the next MoveNext, as the
        // original foreach does. A callback exception is also allowed to propagate.
        bool legacyMutationThrows=false,cachedMutationThrows=false;
        var l=Make(8);var c=Make(8);uint p=0;
        try{LegacyBatch(l,ref p,3,i=>{if(i.Id==0)l.Add(99,new Item(99));});}catch(InvalidOperationException){legacyMutationThrows=true;}
        var s=new SyncItemScheduler<int,Item>();s.BeginPass();
        try{s.ProcessBatch(c,3,i=>{if(i.Id==0)c.Add(99,new Item(99));});}catch(InvalidOperationException){cachedMutationThrows=true;}
        Assert(legacyMutationThrows && cachedMutationThrows,"mid-batch mutation exception semantics differ");
        Exception legacyError=null,cachedError=null;
        try{LegacyBatch(Make(8),ref p,3,i=>{throw new ApplicationException("callback");});}catch(Exception e){legacyError=e;}
        s.BeginPass();try{s.ProcessBatch(Make(8),3,i=>{throw new ApplicationException("callback");});}catch(Exception e){cachedError=e;}
        Assert(legacyError!=null && cachedError!=null && legacyError.GetType()==cachedError.GetType(),"callback exception semantics differ");

        // A synthetic large dictionary quantifies the traversal reduction without
        // treating it as evidence of whole-game FPS impact.
        const int large=30000;var nativeItems=Make(large);var cachedLarge=Make(large);var timer=Stopwatch.StartNew();uint nativeProgress=0;
        while(nativeProgress<nativeItems.Count)LegacyBatch(nativeItems,ref nativeProgress,300,i=>{});
        timer.Stop();long nativeMs=timer.ElapsedMilliseconds;
        var optimized=new SyncItemScheduler<int,Item>();optimized.BeginPass();timer.Restart();
        while(optimized.HasMore(cachedLarge.Count))optimized.ProcessBatch(cachedLarge,300,i=>{});
        timer.Stop();long cachedMs=timer.ElapsedMilliseconds;
        Console.WriteLine("PASS: SyncItemScheduler native-loop replay; synthetic N="+large+" batch=300 legacyMs="+nativeMs+" cachedMs="+cachedMs+" (not a game FPS claim).");
    }
}
