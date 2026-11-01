using System;
using System.Collections.Generic;
using TldMp;

public static class SpawnCoordinatorTests
{
    static int checks;
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); checks++; }

    static SpawnJobKey Key(long generation, long key)
    {
        return new SpawnJobKey(generation, NativeSpawnAdapter.SourceFarPlace, key, null);
    }

    static SpawnJobKey Key(long generation, long key, object owner)
    {
        return new SpawnJobKey(generation, NativeSpawnAdapter.SourcePoi, key, owner);
    }

    static SpawnJob Job(long generation, long key, Action action)
    {
        return new SpawnJob(Key(generation, key), new List<SpawnUnit> { new SpawnUnit(null, action) }, null, 1, 0);
    }

    public static int Main()
    {
        try
        {
            double now = 0;
            Func<double> clock = delegate { return now; };

            var capped = new SpawnCoordinator(clock);
            string reason;
            for (int i = 0; i < SpawnCoordinator.MaxAdmittedGroups; i++)
                Assert(capped.TryAdmit(Job(1, i, delegate { }), out reason) == SpawnAdmission.Admitted, "admit group " + i);
            Assert(capped.TryAdmit(Job(1, 999, delegate { }), out reason) == SpawnAdmission.CapacityRefused, "group cap");
            Assert(capped.HighWaterGroups == SpawnCoordinator.MaxAdmittedGroups, "group high water");

            var records = new SpawnCoordinator(clock);
            Assert(records.TryAdmit(new SpawnJob(Key(1, 1), new List<SpawnUnit> { new SpawnUnit(null, delegate { }) }, null, SpawnCoordinator.MaxItemRecords, 0), out reason) == SpawnAdmission.Admitted, "exact record cap");
            Assert(records.TryAdmit(Job(1, 2, delegate { }), out reason) == SpawnAdmission.CapacityRefused, "record overflow");

            var bytes = new SpawnCoordinator(clock);
            Assert(bytes.TryAdmit(new SpawnJob(Key(1, 1), new List<SpawnUnit> { new SpawnUnit(null, delegate { }) }, null, 0, SpawnCoordinator.MaxPlanBytes), out reason) == SpawnAdmission.Admitted, "exact byte cap");
            Assert(bytes.TryAdmit(new SpawnJob(Key(1, 2), new List<SpawnUnit> { new SpawnUnit(null, delegate { }) }, null, 0, 1), out reason) == SpawnAdmission.CapacityRefused, "byte overflow");

            var duplicates = new SpawnCoordinator(clock);
            int ran = 0;
            Assert(duplicates.TryAdmit(Job(1, 7, delegate { ran++; }), out reason) == SpawnAdmission.Admitted, "first admit");
            Assert(duplicates.TryAdmit(Job(1, 7, delegate { ran += 100; }), out reason) == SpawnAdmission.Duplicate, "duplicate refused");
            duplicates.Drain(double.PositiveInfinity);
            Assert(ran == 1, "duplicate never ran");

            var world = new SpawnCoordinator(clock);
            world.TryAdmit(Job(1, 1, delegate { }), out reason);
            Assert(world.ResetWorld(2, out reason) && reason == null, "reset accepted");
            Assert(world.PendingCount == 0, "unstarted discarded");
            Assert(world.TryAdmit(Job(1, 1, delegate { }), out reason) == SpawnAdmission.StaleWorld, "stale rejected");
            Assert(world.TryAdmit(Job(2, 1, delegate { }), out reason) == SpawnAdmission.Admitted, "new generation admitted");

            var canceller = new SpawnCoordinator(clock);
            var target = Job(1, 5, delegate { });
            canceller.TryAdmit(target, out reason);
            Assert(canceller.Cancel(target.Identity, out reason), "cancel unstarted");
            Assert(canceller.PendingCount == 0 && canceller.CancelledTotal == 1, "cancel recorded");
            var startedJob = new SpawnJob(Key(1, 6), new List<SpawnUnit> {
                new SpawnUnit(null, delegate { }),
                new SpawnUnit(null, delegate { }) }, null, 1, 0);
            canceller.TryAdmit(startedJob, out reason);
            canceller.Drain(0.0);
            Assert(!canceller.Cancel(startedJob.Identity, out reason) && reason != null, "started job cannot cancel");

            var missing = new SpawnCoordinator(clock);
            var orphan = new SpawnJob(Key(1, 1), new List<SpawnUnit> { new SpawnUnit(null, delegate { }) },
                new List<SpawnJobKey> { Key(1, 77) }, 1, 0);
            Assert(missing.TryAdmit(orphan, out reason) == SpawnAdmission.MissingDependency, "missing dep refused");

            var order = new List<long>();
            var linked = new SpawnCoordinator(clock);
            var parent = new SpawnJob(Key(1, 10), new List<SpawnUnit> { new SpawnUnit(null, delegate { order.Add(10); }) }, null, 1, 0);
            Assert(linked.TryAdmit(parent, out reason) == SpawnAdmission.Admitted, "dep admitted");
            var child = new SpawnJob(Key(1, 11), new List<SpawnUnit> { new SpawnUnit(null, delegate { order.Add(11); }) },
                new List<SpawnJobKey> { parent.Identity }, 1, 0);
            Assert(linked.TryAdmit(child, out reason) == SpawnAdmission.Admitted, "dependent admitted");
            linked.Drain(double.PositiveInfinity);
            Assert(order.Count == 2 && order[0] == 10 && order[1] == 11, "dependency order");

            var broken = new SpawnCoordinator(clock);
            var badDep = new SpawnJob(Key(1, 20), new List<SpawnUnit> { new SpawnUnit(null, delegate { throw new InvalidOperationException("boom"); }) }, null, 1, 0);
            var dependent = new SpawnJob(Key(1, 21), new List<SpawnUnit> { new SpawnUnit(null, delegate { }) },
                new List<SpawnJobKey> { badDep.Identity }, 1, 0);
            broken.TryAdmit(badDep, out reason);
            broken.TryAdmit(dependent, out reason);
            bool threw = false;
            try { broken.Drain(double.PositiveInfinity); } catch (InvalidOperationException) { threw = true; }
            Assert(threw && broken.FailedTotal == 1, "failed unit propagated");
            try { broken.Drain(double.PositiveInfinity); } catch (InvalidOperationException) { }
            Assert(dependent.State == SpawnJobState.Failed && dependent.FailReason.StartsWith("dependency"), "dependent failed on dep");

            var partial = new SpawnCoordinator(clock);
            int replays = 0;
            var failing = new SpawnJob(Key(1, 30), new List<SpawnUnit> {
                new SpawnUnit(null, delegate { replays++; }),
                new SpawnUnit(null, delegate { throw new InvalidOperationException("unit2"); }) }, null, 1, 0);
            partial.TryAdmit(failing, out reason);
            try { partial.Drain(double.PositiveInfinity); Assert(false, "expected failure"); }
            catch (InvalidOperationException) { }
            Assert(replays == 1, "first unit ran once");
            Assert(partial.TryAdmit(Job(1, 30, delegate { replays += 100; }), out reason) == SpawnAdmission.FailedBlocked, "failed identity blocked");
            Assert(replays == 1, "failed mutation never replayed");

            var validating = new SpawnCoordinator(clock);
            var vetoed = new SpawnJob(Key(1, 40), new List<SpawnUnit> {
                new SpawnUnit(delegate { return false; }, delegate { Assert(false, "must not run"); }) }, null, 1, 0);
            validating.TryAdmit(vetoed, out reason);
            validating.Drain(double.PositiveInfinity);
            Assert(vetoed.State == SpawnJobState.Cancelled && validating.FailedTotal == 0 && validating.CancelledTotal == 1, "validation cancelled");

            var fences = new SpawnCoordinator(clock);
            var nested = new SpawnJob(Key(1, 50), new List<SpawnUnit> {
                new SpawnUnit(null, delegate {
                    try { fences.CompletePendingConstruction("nested", "test"); Assert(false, "reentrant fence must fail"); }
                    catch (InvalidOperationException) { }
                }) }, null, 1, 0);
            fences.TryAdmit(nested, out reason);
            fences.Drain(double.PositiveInfinity);
            fences.CompletePendingConstruction("save", "test");
            Assert(fences.PendingCount == 0, "fence drained");

            var fifo = new SpawnCoordinator(clock);
            var seen = new List<long>();
            for (int i = 0; i < 4; i++)
            {
                long id = i;
                fifo.TryAdmit(new SpawnJob(Key(1, id), new List<SpawnUnit> { new SpawnUnit(null, delegate { seen.Add(id); now += 4.0; }) }, null, 1, 0), out reason);
            }
            now = 0;
            fifo.Drain(3.0);
            Assert(seen.Count == 1 && fifo.PendingCount == 3, "one unit per budgeted drain");
            Assert(fifo.Overruns == 1, "atomic overrun recorded");
            fifo.Drain(3.0);
            fifo.Drain(3.0);
            fifo.Drain(3.0);
            Assert(seen.Count == 4 && seen[0] == 0 && seen[1] == 1 && seen[2] == 2 && seen[3] == 3, "FIFO order kept");

            var reference = new SpawnCoordinator(clock);
            int sync = 0;
            var syncJob = Job(1, 60, delegate { sync++; });
            Assert(reference.TryAdmit(syncJob, out reason) == SpawnAdmission.Admitted, "reference admitted");
            reference.RunSynchronous(syncJob);
            Assert(sync == 1 && syncJob.State == SpawnJobState.Complete && reference.PendingCount == 0, "synchronous parity");

            var bounded = new SpawnCoordinator(clock);
            for (int i = 0; i < SpawnCoordinator.MaxTerminalRecords + 8; i++)
            {
                var done = Job(1, 1000 + i, delegate { });
                bounded.TryAdmit(done, out reason);
                bounded.Drain(double.PositiveInfinity);
            }
            var failed = new SpawnJob(Key(1, 2000), new List<SpawnUnit> { new SpawnUnit(null, delegate { throw new InvalidOperationException("kept"); }) }, null, 1, 0);
            bounded.TryAdmit(failed, out reason);
            try { bounded.Drain(double.PositiveInfinity); } catch (InvalidOperationException) { }
            Assert(bounded.TryAdmit(Job(1, 2000, delegate { }), out reason) == SpawnAdmission.FailedBlocked, "failed identity survives terminal bound");
            Assert(bounded.TerminalEvictions > 0, "terminal bound evicted completed records");

            var settled = new SpawnCoordinator(clock);
            settled.RequireSettled("empty");
            settled.TryAdmit(Job(1, 1, delegate { }), out reason);
            bool refused = false;
            try { settled.RequireSettled("pending"); } catch (InvalidOperationException) { refused = true; }
            Assert(refused, "settled check refuses pending work");

            var multi=new SpawnCoordinator(clock);
            var order2=new List<int>();
            var units=new List<SpawnUnit>();
            for(int u=0;u<3;u++){int unitId=u;units.Add(new SpawnUnit(null,delegate{order2.Add(unitId);now+=2;}));}
            var multiJob=new SpawnJob(Key(1,50),units,null,3,0);
            Assert(multi.TryAdmit(multiJob,out reason)==SpawnAdmission.Admitted,"multi-unit admitted");
            now=0;
            Assert(multi.Drain(3)==2,"budget stops between units");
            Assert(order2.Count==2 && order2[0]==0 && order2[1]==1,"first two units ran in order");
            Assert(multiJob.NextUnit==2 && multiJob.State==SpawnJobState.Queued,"mid-job state recorded");
            Assert(multi.Drain(3)==1,"remaining unit drains next call");
            Assert(order2.Count==3 && order2[2]==2 && multiJob.State==SpawnJobState.Complete,"multi-unit completes, first never repeats");

            var chain=new SpawnCoordinator(clock);
            var parentUnits=new List<SpawnUnit>{new SpawnUnit(null,delegate{now+=2;}),new SpawnUnit(null,delegate{now+=2;})};
            var childJob=new SpawnJob(Key(1,60),new List<SpawnUnit>{new SpawnUnit(null,delegate{})},new List<SpawnJobKey>{Key(1,59)},1,0);
            var parentJob=new SpawnJob(Key(1,59),parentUnits,null,2,0);
            Assert(chain.TryAdmit(parentJob,out reason)==SpawnAdmission.Admitted && chain.TryAdmit(childJob,out reason)==SpawnAdmission.Admitted,"dep chain admitted");
            now=0;Assert(chain.Drain(3)==2,"dependent waits behind multi-unit parent");
            Assert(parentJob.State==SpawnJobState.Complete && childJob.State==SpawnJobState.Queued,"dependent not run early");
            Assert(chain.Drain(10)==1 && childJob.State==SpawnJobState.Complete,"dependent completes after parent");

            var invalid=new SpawnCoordinator(clock);
            int mutated=0;
            var bad=new SpawnJob(Key(1,70),new List<SpawnUnit>{
                new SpawnUnit(null,delegate{mutated++;}),
                new SpawnUnit(delegate{return false;},delegate{mutated+=100;})},null,2,0);
            invalid.TryAdmit(bad,out reason);
            invalid.Drain(double.PositiveInfinity);
            Assert(mutated==1 && bad.State==SpawnJobState.Failed && invalid.HasFailedCurrent,"validate-false after mutation fails, not cancels");

            var cancels=new SpawnCoordinator(clock);
            var cancelledDep=new SpawnJob(Key(1,80),new List<SpawnUnit>{new SpawnUnit(delegate{return false;},delegate{Assert(false,"cancelled ran");})},null,1,0);
            var orphanJob=new SpawnJob(Key(1,81),new List<SpawnUnit>{new SpawnUnit(null,delegate{Assert(false,"orphan ran");})},new List<SpawnJobKey>{Key(1,80)},1,0);
            cancels.TryAdmit(cancelledDep,out reason);cancels.TryAdmit(orphanJob,out reason);
            cancels.Drain(double.PositiveInfinity);
            Assert(orphanJob.State==SpawnJobState.Failed && orphanJob.FailReason.StartsWith("dependency"),"cancelled dependency fails dependent");

            var saturate=new SpawnCoordinator(clock);
            for(int i=0;i<SpawnCoordinator.MaxTerminalRecords+4;i++)
            {
                var failer=new SpawnJob(Key(1,3000+i),new List<SpawnUnit>{new SpawnUnit(null,delegate{throw new Exception("f");})},null,1,0);
                saturate.TryAdmit(failer,out reason);
                try{saturate.Drain(double.PositiveInfinity);}catch(Exception){}
            }
            Assert(saturate.HasFailedCurrent,"failure registry latched");
            Assert(saturate.Preflight(Key(1,3000))==SpawnAdmission.FailedBlocked,"oldest failure never evicted to allow replay");

            try
            {
                new SpawnCoordinator(clock).TryAdmit(new SpawnJob(Key(1,90),new List<SpawnUnit>{new SpawnUnit(null,delegate{})},null,-1,0),out reason);
                Assert(false,"negative records accepted");
            }
            catch(ArgumentException){checks++;}
            try
            {
                new SpawnCoordinator(clock).TryAdmit(new SpawnJob(Key(1,91),new List<SpawnUnit>{new SpawnUnit(null,delegate{})},null,0,-5),out reason);
                Assert(false,"negative bytes accepted");
            }
            catch(ArgumentException){checks++;}

            var mutate=new SpawnCoordinator(clock);
            var passedUnits=new List<SpawnUnit>{new SpawnUnit(null,delegate{})};
            var passedDeps=new List<SpawnJobKey>();
            var mutableJob=new SpawnJob(Key(1,95),passedUnits,passedDeps,1,0);
            mutate.TryAdmit(mutableJob,out reason);
            passedUnits.Add(new SpawnUnit(null,delegate{Assert(false,"post-admission unit ran");}));
            passedDeps.Add(Key(1,95));
            mutate.Drain(double.PositiveInfinity);
            Assert(mutableJob.State==SpawnJobState.Complete,"admitted plan immune to caller list mutation");

            var owners=new SpawnCoordinator(clock);
            object ownerA=new object(),ownerB=new object();
            Assert(owners.TryAdmit(new SpawnJob(new SpawnJobKey(1,NativeSpawnAdapter.SourcePoi,42,ownerA),new List<SpawnUnit>{new SpawnUnit(null,delegate{})},null,1,0),out reason)==SpawnAdmission.Admitted,"owner A admitted");
            Assert(owners.TryAdmit(new SpawnJob(new SpawnJobKey(1,NativeSpawnAdapter.SourcePoi,42,ownerB),new List<SpawnUnit>{new SpawnUnit(null,delegate{})},null,1,0),out reason)==SpawnAdmission.Admitted,"same coords different owner not deduped");
            Assert(owners.Preflight(new SpawnJobKey(1,NativeSpawnAdapter.SourcePoi,42,ownerA))==SpawnAdmission.Duplicate,"same owner deduped");

            var syncOrder=new SpawnCoordinator(clock);
            var syncSteps=new List<int>();
            var syncDep=new SpawnJob(Key(1,100),new List<SpawnUnit>{new SpawnUnit(null,delegate{syncSteps.Add(1);})},null,1,0);
            var syncMain=new SpawnJob(Key(1,101),new List<SpawnUnit>{new SpawnUnit(null,delegate{syncSteps.Add(2);})},new List<SpawnJobKey>{Key(1,100)},1,0);
            syncOrder.TryAdmit(syncDep,out reason);syncOrder.TryAdmit(syncMain,out reason);
            syncOrder.RunSynchronous(syncMain);
            Assert(syncSteps.Count==2 && syncSteps[0]==1 && syncSteps[1]==2,"synchronous run honors dependency order");

            var syncThrow=new SpawnCoordinator(clock);
            var throwDep=new SpawnJob(Key(1,110),new List<SpawnUnit>{new SpawnUnit(null,delegate{throw new Exception("dep");})},null,1,0);
            var throwParent=new SpawnJob(Key(1,111),new List<SpawnUnit>{new SpawnUnit(null,delegate{Assert(false,"parent ran");})},new List<SpawnJobKey>{Key(1,110)},1,0);
            syncThrow.TryAdmit(throwDep,out reason);syncThrow.TryAdmit(throwParent,out reason);
            try{syncThrow.RunSynchronous(throwParent);Assert(false,"dep throw swallowed");}catch(Exception){checks++;}
            Assert(throwParent.State==SpawnJobState.Failed && syncThrow.FailedTotal>=2,"parent failed with counters released");

            var atomic=new SpawnCoordinator(clock);
            var atomicRan=0;
            var atomicKey=Key(1,120);
            Assert(atomic.RunUnadmittedAtomic(atomicKey,delegate{atomicRan++;},out reason) && atomicRan==1,"unadmitted atomic ran once");
            var atomicFail=Key(1,121);
            try{atomic.RunUnadmittedAtomic(atomicFail,delegate{throw new Exception("cap");},out reason);Assert(false,"atomic throw swallowed");}catch(Exception){checks++;}
            Assert(atomic.HasFailedCurrent && atomic.Preflight(atomicFail)==SpawnAdmission.FailedBlocked,"failed atomic blocks re-admission and fences");

            int pinRan=0;
            var fence=new SpawnCoordinator(clock);
            var fenceFail=new SpawnJob(Key(1,130),new List<SpawnUnit>{new SpawnUnit(null,delegate{throw new Exception("f");})},null,1,0);
            fence.TryAdmit(fenceFail,out reason);
            bool firstThrew=false;
            try{fence.CompletePendingConstruction("save","x");Assert(false,"fence passed failure");}catch(Exception){firstThrew=true;}
            Assert(firstThrew && fence.HasFailedCurrent,"drain failure latched as blocker");
            try{fence.CompletePendingConstruction("save2","x");Assert(false,"second fence passed same failure");}catch(InvalidOperationException){checks++;}
            Assert(fence.ResetWorld(2,out reason),"reset clears failed blockers");
            fence.CompletePendingConstruction("save3","x");
            Assert(fence.Preflight(Key(2,130))==SpawnAdmission.Admitted,"new world admits again");

            var revisit=new SpawnCoordinator(clock);
            var revisitKey=Key(1,140);
            Assert(revisit.RunUnadmittedAtomic(revisitKey,delegate{},out reason),"revisit first success");
            try{revisit.RunUnadmittedAtomic(revisitKey,delegate{throw new Exception("native throw");},out reason);Assert(false,"revisit throw swallowed");}catch(Exception){checks++;}
            Assert(revisit.Preflight(revisitKey)==SpawnAdmission.FailedBlocked,"failure upgrades prior success receipt");
            for(int i=0;i<SpawnCoordinator.MaxTerminalRecords+4;i++)
            {
                var extra=new SpawnJob(Key(1,4000+i),new List<SpawnUnit>{new SpawnUnit(null,delegate{})},null,1,0);
                revisit.TryAdmit(extra,out reason);
                revisit.Drain(double.PositiveInfinity);
            }
            Assert(revisit.Preflight(revisitKey)==SpawnAdmission.FailedBlocked,"failed receipt never evicted by completions");
            Assert(revisit.HasFailedCurrent,"latched failure still blocks fences");

            var dupOrder=new SpawnCoordinator(clock);
            var dupKey=Key(1,150);
            Assert(dupOrder.RunUnadmittedAtomic(dupKey,delegate{},out reason),"first atomic");
            var again=new SpawnJob(dupKey,new List<SpawnUnit>{new SpawnUnit(null,delegate{})},null,1,0);
            Assert(dupOrder.TryAdmit(again,out reason)==SpawnAdmission.Admitted,"prior success receipt allows same-identity job");
            dupOrder.Drain(double.PositiveInfinity);
            Assert(dupOrder.TerminalEvictions>=0,"no missing-key crash on duplicate order node");

            var pin=new SpawnCoordinator(clock);
            var parentDone=new SpawnJob(Key(1,160),new List<SpawnUnit>{new SpawnUnit(null,delegate{})},null,1,0);
            var waitingDep=new SpawnJob(Key(1,161),new List<SpawnUnit>{new SpawnUnit(null,delegate{pinRan++;})},new List<SpawnJobKey>{Key(1,160)},1,0);
            pin.TryAdmit(parentDone,out reason);
            pin.Drain(double.PositiveInfinity);
            pin.TryAdmit(waitingDep,out reason);
            for(int i=0;i<SpawnCoordinator.MaxTerminalRecords+8;i++)
            {
                var filler=new SpawnJob(Key(1,5000+i),new List<SpawnUnit>{new SpawnUnit(null,delegate{})},null,1,0);
                pin.TryAdmit(filler,out reason);
                pin.Drain(double.PositiveInfinity);
            }
            pin.Drain(double.PositiveInfinity);
            Assert(pinRan==1 && waitingDep.State==SpawnJobState.Complete,"pinned parent receipt survives terminal bound");

            var hardcap=new SpawnCoordinator(clock);
            for(int i=0;i<400;i++)
            {
                var failJob=new SpawnJob(Key(1,6000+i),new List<SpawnUnit>{new SpawnUnit(null,delegate{throw new Exception("x");})},null,1,0);
                hardcap.TryAdmit(failJob,out reason);
                try{hardcap.Drain(double.PositiveInfinity);}catch(Exception){}
            }
            Assert(hardcap.TerminalCount<=SpawnCoordinator.MaxTerminalRecords,"terminal registry hard bound "+hardcap.TerminalCount);
            Assert(hardcap.Preflight(Key(1,9000))==SpawnAdmission.TerminalSaturated,"latched unknown preflight saturated");
            Assert(hardcap.Preflight(Key(1,6000))==SpawnAdmission.FailedBlocked,"retained failed key still blocked");

            var shared=new SpawnCoordinator(clock);
            shared.BeginFrame(1);
            string atomicReason;
            Assert(shared.RunUnadmittedAtomic(Key(1,170),delegate{now+=4;},out atomicReason),"atomic sync charged");
            Assert(shared.FrameSpentMs>=3.9 && shared.frameHasWork,"sync atomic charged to frame");
            Assert(shared.Drain(3)==0,"prior sync work blocks far drain same frame");
            shared.BeginFrame(1);
            Assert(shared.Drain(3)==0,"same token never resets frame accounting");
            shared.BeginFrame(2);
            var farDone=0;
            shared.TryAdmit(new SpawnJob(Key(1,171),new List<SpawnUnit>{new SpawnUnit(null,delegate{farDone++;now+=2;})},null,1,0),out reason);
            Assert(shared.Drain(3)==1 && farDone==1,"fresh frame drains far unit");

            var softOver=new SpawnCoordinator(clock);
            softOver.BeginFrame(5);
            Assert(softOver.RunUnadmittedAtomic(Key(1,180),delegate{now+=0.5;},out atomicReason),"small sync");
            var softOrder=new List<int>();
            softOver.TryAdmit(new SpawnJob(Key(1,181),new List<SpawnUnit>{
                new SpawnUnit(null,delegate{softOrder.Add(1);now+=2;}),
                new SpawnUnit(null,delegate{softOrder.Add(2);now+=2;})},null,2,0),out reason);
            Assert(softOver.Drain(3)==2 && softOver.FrameSpentMs>4,"sync 0.5 + two far 2ms = soft overrun charged once");
            Assert(softOrder.Count==2,"both far units ran");

            var starved=new SpawnCoordinator(clock);
            var starvedUnits=new List<SpawnUnit>{new SpawnUnit(null,delegate{now+=2;}),new SpawnUnit(null,delegate{now+=2;})};
            var starvedJob=new SpawnJob(Key(1,190),starvedUnits,null,2,0);
            starved.BeginFrame(10);
            Assert(starved.RunUnadmittedAtomic(Key(1,191),delegate{now+=4;},out atomicReason),"frame10 sync");
            starved.TryAdmit(starvedJob,out reason);
            Assert(starved.Drain(3)==0,"young far job blocked after sync work");
            now+=2100;
            starved.BeginFrame(11);
            Assert(starved.RunUnadmittedAtomic(Key(1,192),delegate{now+=4;},out atomicReason),"frame11 required sync");
            Assert(starved.Drain(3)==1,"aged far job gets one unit despite exhausted budget");
            Assert(starvedJob.NextUnit==1 && starved.AgedOverruns==1,"aged overrun advanced one unit once");

            var fenceFull=new SpawnCoordinator(clock);
            fenceFull.BeginFrame(20);
            fenceFull.RunUnadmittedAtomic(Key(1,200),delegate{now+=9;},out atomicReason);
            var fenceJob=new SpawnJob(Key(1,201),new List<SpawnUnit>{new SpawnUnit(null,delegate{}),new SpawnUnit(null,delegate{})},null,2,0);
            fenceFull.TryAdmit(fenceJob,out reason);
            Assert(fenceFull.Drain(3)==0,"fence-frame budget blocks");
            fenceFull.Drain(double.PositiveInfinity);
            Assert(fenceJob.State==SpawnJobState.Complete,"infinite-budget fence completes regardless");

            Console.WriteLine("PASS: spawn coordinator " + checks + " checks (caps, duplicate/capacity distinction, generation, cancel, dependencies, partial failure, reentrant fence, FIFO budget, synchronous parity, bounded terminals).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
