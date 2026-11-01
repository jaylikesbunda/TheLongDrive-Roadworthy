using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace TldMp
{
    internal enum SpawnAdmission
    {
        Admitted = 0,
        Duplicate = 1,
        CapacityRefused = 2,
        StaleWorld = 3,
        FailedBlocked = 4,
        MissingDependency = 5,
        MutationActive = 6,
        TerminalSaturated = 7
    }

    internal enum SpawnJobState
    {
        Queued = 0,
        Running = 1,
        Complete = 2,
        Cancelled = 3,
        Failed = 4
    }

    internal struct SpawnJobKey : IEquatable<SpawnJobKey>
    {
        internal long Generation;
        internal int Source;
        internal long Key;
        internal object Owner;

        internal SpawnJobKey(long generation, int source, long key, object owner)
        {
            Generation = generation;
            Source = source;
            Key = key;
            Owner = owner;
        }

        public bool Equals(SpawnJobKey other)
        {
            return Generation == other.Generation && Source == other.Source && Key == other.Key &&
                ReferenceEquals(Owner, other.Owner);
        }

        public override bool Equals(object obj)
        {
            return obj is SpawnJobKey && Equals((SpawnJobKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Generation.GetHashCode();
                hash = (hash * 397) ^ Source;
                hash = (hash * 397) ^ Key.GetHashCode();
                hash = (hash * 397) ^ (Owner == null ? 0 : RuntimeHelpers.GetHashCode(Owner));
                return hash;
            }
        }
    }

    internal sealed class SpawnUnit
    {
        internal readonly Func<bool> Validate;
        internal readonly Action Run;

        internal SpawnUnit(Func<bool> validate, Action run)
        {
            Validate = validate;
            Run = run;
        }
    }

    internal sealed class SpawnJob
    {
        internal SpawnJobKey Identity;
        internal readonly System.Collections.ObjectModel.ReadOnlyCollection<SpawnUnit> Units;
        internal readonly System.Collections.ObjectModel.ReadOnlyCollection<SpawnJobKey> Dependencies;
        internal readonly int Records;
        internal readonly int PlanBytes;
        internal SpawnJobState State;
        internal int NextUnit;
        internal string FailReason;
        internal long AdmittedMs;

        internal SpawnJob(SpawnJobKey identity, List<SpawnUnit> units, List<SpawnJobKey> dependencies, int records, int planBytes)
        {
            Identity = identity;
            Units = new List<SpawnUnit>(units).AsReadOnly();
            Dependencies = dependencies == null ? null : new List<SpawnJobKey>(dependencies).AsReadOnly();
            Records = records;
            PlanBytes = planBytes;
            State = SpawnJobState.Queued;
        }
    }

    internal sealed class SpawnCoordinator
    {
        internal const int MaxAdmittedGroups = 128;
        internal const int MaxItemRecords = 8192;
        internal const int MaxPlanBytes = 16 * 1024 * 1024;
        internal const int MaxTerminalRecords = 256;

        readonly Func<double> nowMs;
        readonly List<SpawnJob> queued = new List<SpawnJob>();
        readonly HashSet<SpawnJobKey> live = new HashSet<SpawnJobKey>();
        readonly LinkedList<SpawnJobKey> terminalOrder = new LinkedList<SpawnJobKey>();
        readonly Dictionary<SpawnJobKey, bool> terminalFailed = new Dictionary<SpawnJobKey, bool>();
        int activeUnits;
        long currentGeneration;
        int failedCurrent;
        bool terminalLatched;

        internal long Generation { get { return currentGeneration; } }
        internal int PendingCount { get { return queued.Count; } }
        internal bool MutationActive { get { return activeUnits > 0; } }
        internal bool HasFailedCurrent { get { return failedCurrent > 0 || terminalLatched; } }
        internal int TerminalCount { get { return terminalFailed.Count; } }
        int frameToken = -1;
        internal double FrameSpentMs;
        internal bool frameHasWork;
        internal int AgedOverruns;
        internal int AtomicOver3ms;

        public void BeginFrame(int token)
        {
            if (frameToken == token) return;
            frameToken = token;
            FrameSpentMs = 0;
            frameHasWork = false;
        }

        internal int AdmittedTotal, CompletedTotal, FailedTotal, CancelledTotal;
        internal int DuplicateTotal, CapacityTotal, StaleTotal, MissingDepTotal, BlockedTotal;
        internal int UnitsRun, Overruns, TerminalEvictions;
        internal int HighWaterGroups, HighWaterRecords, HighWaterBytes;
        internal int LiveRecords, LiveBytes;
        internal string LastFailReason;

        internal SpawnCoordinator(Func<double> nowMilliseconds)
        {
            nowMs = nowMilliseconds;
            currentGeneration = 1;
        }

        internal SpawnAdmission Preflight(SpawnJobKey identity)
        {
            if (activeUnits > 0) return SpawnAdmission.MutationActive;
            if (identity.Generation != currentGeneration) return SpawnAdmission.StaleWorld;
            if (live.Contains(identity)) return SpawnAdmission.Duplicate;
            bool failed;
            if (terminalFailed.TryGetValue(identity, out failed) && failed) return SpawnAdmission.FailedBlocked;
            if (terminalLatched) return SpawnAdmission.TerminalSaturated;
            return SpawnAdmission.Admitted;
        }

        internal SpawnAdmission TryAdmit(SpawnJob job, out string reason)
        {
            reason = null;
            if (job == null || job.Units == null || job.Units.Count == 0)
                throw new ArgumentException("Construction job requires at least one unit.");
            if (job.Records < 0 || job.PlanBytes < 0)
                throw new ArgumentException("Construction job accounting must not be negative.");
            var status = Preflight(job.Identity);
            if (status != SpawnAdmission.Admitted)
            {
                if (status == SpawnAdmission.Duplicate) { DuplicateTotal++; reason = "duplicate admission"; }
                else if (status == SpawnAdmission.StaleWorld) { StaleTotal++; reason = "stale world generation"; }
                else if (status == SpawnAdmission.MutationActive) { BlockedTotal++; reason = "nested admission during active mutation"; }
                else if (status == SpawnAdmission.FailedBlocked) { BlockedTotal++; reason = "identity retained after failed mutation"; }
                else { BlockedTotal++; reason = "terminal failure registry saturated"; }
                return status;
            }
            if (job.Dependencies != null)
            {
                for (int i = 0; i < job.Dependencies.Count; i++)
                {
                    var dep = job.Dependencies[i];
                    bool depFailed;
                    if (dep.Equals(job.Identity) || (!live.Contains(dep) && !(terminalFailed.TryGetValue(dep, out depFailed) && !depFailed)))
                    {
                        MissingDepTotal++;
                        reason = "missing or unusable dependency";
                        return SpawnAdmission.MissingDependency;
                    }
                }
            }
            if (queued.Count >= MaxAdmittedGroups ||
                (long)LiveRecords + job.Records > MaxItemRecords ||
                (long)LiveBytes + job.PlanBytes > MaxPlanBytes)
            {
                CapacityTotal++;
                reason = "coordinator capacity";
                return SpawnAdmission.CapacityRefused;
            }
            job.AdmittedMs = (long)nowMs();
            queued.Add(job);
            live.Add(job.Identity);
            LiveRecords += job.Records;
            LiveBytes += job.PlanBytes;
            AdmittedTotal++;
            if (queued.Count > HighWaterGroups) HighWaterGroups = queued.Count;
            if (LiveRecords > HighWaterRecords) HighWaterRecords = LiveRecords;
            if (LiveBytes > HighWaterBytes) HighWaterBytes = LiveBytes;
            return SpawnAdmission.Admitted;
        }

        internal bool Cancel(SpawnJobKey identity, out string reason)
        {
            reason = null;
            if (activeUnits > 0)
            {
                reason = "cannot cancel during active mutation";
                return false;
            }
            for (int i = 0; i < queued.Count; i++)
            {
                var job = queued[i];
                if (!job.Identity.Equals(identity)) continue;
                if (job.NextUnit > 0 || job.State != SpawnJobState.Queued)
                {
                    reason = "job already mutated";
                    return false;
                }
                job.State = SpawnJobState.Cancelled;
                ReleaseCountersOnly(job);
                CancelledTotal++;
                queued.RemoveAt(i);
                return true;
            }
            reason = "job not admitted";
            return false;
        }

        internal int CancelUnstarted(int source)
        {
            int removed = 0;
            for (int i = queued.Count - 1; i >= 0; i--)
            {
                var job = queued[i];
                if (job.Identity.Source != source || job.NextUnit > 0 || job.State != SpawnJobState.Queued) continue;
                job.State = SpawnJobState.Cancelled;
                ReleaseCountersOnly(job);
                CancelledTotal++;
                queued.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        internal bool ResetWorld(long generation, out string reason)
        {
            reason = null;
            if (activeUnits > 0)
            {
                reason = "world reset during active mutation";
                return false;
            }
            currentGeneration = generation;
            for (int i = queued.Count - 1; i >= 0; i--)
            {
                var job = queued[i];
                job.State = SpawnJobState.Cancelled;
                ReleaseCountersOnly(job);
                queued.RemoveAt(i);
            }
            terminalOrder.Clear();
            terminalFailed.Clear();
            failedCurrent = 0;
            terminalLatched = false;
            return true;
        }

        void ReleaseCountersOnly(SpawnJob job)
        {
            live.Remove(job.Identity);
            LiveRecords -= job.Records;
            LiveBytes -= job.PlanBytes;
        }

        internal bool FailedReason(SpawnJobKey identity, out string reason)
        {
            reason = null;
            bool failed;
            if (terminalFailed.TryGetValue(identity, out failed) && failed)
            {
                reason = LastFailReason ?? "failed construction group";
                return true;
            }
            return false;
        }

        internal bool RunUnadmittedAtomic(SpawnJobKey identity, Action run, out string reason)
        {
            reason = null;
            var status = Preflight(identity);
            if (status != SpawnAdmission.Admitted && status != SpawnAdmission.CapacityRefused)
            {
                reason = "unadmitted atomic refused: " + status;
                return false;
            }
            live.Add(identity);
            activeUnits++;
            double u0 = nowMs();
            try
            {
                run();
            }
            catch (Exception error)
            {
                LastFailReason = "unadmitted atomic threw: " + error.GetType().Name;
                FailedTotal++;
                failedCurrent++;
                RecordTerminal(identity, true);
                throw;
            }
            finally
            {
                activeUnits--;
                live.Remove(identity);
                double unitCost = nowMs() - u0;
                FrameSpentMs += unitCost;
                frameHasWork = true;
                if (unitCost > 3.0) AtomicOver3ms++;
            }
            RecordTerminal(identity, false);
            CompletedTotal++;
            return true;
        }

        bool DependenciesFailed(SpawnJob job)
        {
            if (job.Dependencies == null) return false;
            for (int i = 0; i < job.Dependencies.Count; i++)
            {
                bool failed;
                if (!terminalFailed.TryGetValue(job.Dependencies[i], out failed) || failed) return true;
            }
            return false;
        }

        void StepUnit(SpawnJob job)
        {
            if (job.NextUnit == 0 && DependenciesFailed(job))
            {
                job.FailReason = "dependency failed or cancelled";
                job.State = SpawnJobState.Failed;
                Fail(job);
                return;
            }
            var unit = job.Units[job.NextUnit];
            bool valid;
            try
            {
                valid = unit.Validate == null || unit.Validate();
            }
            catch (Exception error)
            {
                job.FailReason = "validation threw: " + error.GetType().Name;
                job.State = SpawnJobState.Failed;
                Fail(job);
                throw;
            }
            if (!valid)
            {
                if (job.NextUnit > 0)
                {
                    job.FailReason = "validation refused after mutation";
                    job.State = SpawnJobState.Failed;
                    Fail(job);
                    return;
                }
                job.State = SpawnJobState.Cancelled;
                ReleaseCountersOnly(job);
                CancelledTotal++;
                return;
            }
            activeUnits++;
            double u0 = nowMs();
            try
            {
                unit.Run();
            }
            catch (Exception error)
            {
                job.FailReason = "unit threw: " + error.GetType().Name;
                job.State = SpawnJobState.Failed;
                Fail(job);
                throw;
            }
            finally
            {
                activeUnits--;
                double unitCost = nowMs() - u0;
                FrameSpentMs += unitCost;
                frameHasWork = true;
                if (unitCost > 3.0) AtomicOver3ms++;
            }
            job.NextUnit++;
            UnitsRun++;
            if (job.NextUnit >= job.Units.Count)
            {
                job.State = SpawnJobState.Complete;
                Release(job, false);
                CompletedTotal++;
            }
        }

        void Fail(SpawnJob job)
        {
            FailedTotal++;
            failedCurrent++;
            LastFailReason = job.FailReason;
            Release(job, true);
        }

        void RecordTerminal(SpawnJobKey key, bool failed)
        {
            bool prior;
            if (terminalFailed.TryGetValue(key, out prior))
            {
                if (failed && !prior) terminalFailed[key] = true;
                return;
            }
            if (terminalFailed.Count >= MaxTerminalRecords && !TryEvictTerminal())
            {
                terminalLatched = true;
                return;
            }
            terminalFailed[key] = failed;
            terminalOrder.AddLast(key);
            BoundTerminal();
        }

        bool TryEvictTerminal()
        {
            var node = terminalOrder.First;
            while (node != null)
            {
                bool failed;
                if (!terminalFailed.TryGetValue(node.Value, out failed)) { var stale = node; node = node.Next; terminalOrder.Remove(stale); continue; }
                if (failed || PinnedByDependencies(node.Value)) { node = node.Next; continue; }
                var key = node.Value;
                terminalFailed.Remove(key);
                terminalOrder.Remove(node);
                TerminalEvictions++;
                return true;
            }
            return false;
        }

        void Release(SpawnJob job, bool failed)
        {
            live.Remove(job.Identity);
            LiveRecords -= job.Records;
            LiveBytes -= job.PlanBytes;
            RecordTerminal(job.Identity, failed);
        }

        bool PinnedByDependencies(SpawnJobKey key)
        {
            for (int i = 0; i < queued.Count; i++)
            {
                var deps = queued[i].Dependencies;
                if (deps != null && deps.Contains(key)) return true;
            }
            return false;
        }

        void BoundTerminal()
        {
            while (terminalFailed.Count > MaxTerminalRecords)
                if (!TryEvictTerminal())
                {
                    terminalLatched = true;
                    return;
                }
        }

        SpawnJob NextReady()
        {
            for (int pass = 0; pass < queued.Count; pass++)
            {
                var job = queued[pass];
                if (job.Dependencies == null || job.Dependencies.Count == 0) return job;
                bool waiting = false, broken = false;
                for (int i = 0; i < job.Dependencies.Count; i++)
                {
                    var dep = job.Dependencies[i];
                    bool failed;
                    if (!terminalFailed.TryGetValue(dep, out failed))
                    {
                        if (live.Contains(dep)) waiting = true;
                        else broken = true;
                    }
                    else if (failed) broken = true;
                }
                if (broken)
                {
                    job.FailReason = "dependency failed or cancelled";
                    job.State = SpawnJobState.Failed;
                    Fail(job);
                    queued.RemoveAt(pass);
                    pass--;
                    continue;
                }
                if (!waiting && (job.State == SpawnJobState.Queued || job.State == SpawnJobState.Running)) return job;
            }
            return null;
        }

        bool DependenciesSatisfied(SpawnJob job)
        {
            if (job.Dependencies == null || job.Dependencies.Count == 0) return true;
            for (int i = 0; i < job.Dependencies.Count; i++)
            {
                bool failed;
                if (!terminalFailed.TryGetValue(job.Dependencies[i], out failed)) return false;
                if (failed) return false;
            }
            return true;
        }

        SpawnJob OldestReady()
        {
            SpawnJob oldest = null;
            for (int i = 0; i < queued.Count; i++)
            {
                var j = queued[i];
                if (j.State != SpawnJobState.Queued && j.State != SpawnJobState.Running) continue;
                if (!DependenciesSatisfied(j)) continue;
                if (oldest == null || j.AdmittedMs < oldest.AdmittedMs) oldest = j;
            }
            return oldest;
        }

        void ExecuteUnit(SpawnJob job, double budgetMs)
        {
            try
            {
                StepUnit(job);
            }
            catch
            {
                if (job.State != SpawnJobState.Queued && job.State != SpawnJobState.Running) queued.Remove(job);
                if ((frameToken >= 0 && FrameSpentMs > budgetMs) || (frameToken < 0)) Overruns++;
                throw;
            }
            if (job.State != SpawnJobState.Queued && job.State != SpawnJobState.Running) queued.Remove(job);
            if (frameToken >= 0 && FrameSpentMs > budgetMs) Overruns++;
        }

        internal int Drain(double budgetMs)
        {
            if (activeUnits > 0)
                throw new InvalidOperationException("Construction drain during an active mutation.");
            int ran = 0;
            double start = nowMs();
            bool agedOverrun = false;
            while (queued.Count > 0)
            {
                var job = NextReady();
                if (job == null) break;
                if (frameToken >= 0)
                {
                    bool guaranteed = ran == 0 && !frameHasWork;
                    if (!guaranteed && FrameSpentMs >= budgetMs)
                    {
                        var oldest = OldestReady();
                        if (!agedOverrun && oldest != null && nowMs() - oldest.AdmittedMs >= 2000)
                        {
                            agedOverrun = true;
                            AgedOverruns++;
                            ExecuteUnit(oldest, budgetMs);
                            ran++;
                            continue;
                        }
                        break;
                    }
                }
                else if (ran > 0 && nowMs() - start >= budgetMs) break;
                ExecuteUnit(job, budgetMs);
                ran++;
                if (frameToken < 0 && nowMs() - start > budgetMs) Overruns++;
            }
            return ran;
        }

        internal void CompletePendingConstruction(string reason, string scope)
        {
            if (activeUnits > 0)
                throw new InvalidOperationException("CompletePendingConstruction(" + reason + "," + scope + ") inside an active construction mutation.");
            if (HasFailedCurrent)
                throw new InvalidOperationException("CompletePendingConstruction(" + reason + "," + scope + ") blocked by a failed construction group: " + (LastFailReason ?? "failed registry saturated"));
            Drain(double.PositiveInfinity);
            if (HasFailedCurrent)
                throw new InvalidOperationException("CompletePendingConstruction(" + reason + "," + scope + ") produced a failed construction group: " + (LastFailReason ?? "failed registry saturated"));
            if (queued.Count != 0)
                throw new InvalidOperationException("CompletePendingConstruction(" + reason + "," + scope + ") left " + queued.Count.ToString(CultureInfo.InvariantCulture) + " unfinished groups.");
        }

        internal void RequireSettled(string reason)
        {
            if (activeUnits > 0 || queued.Count != 0 || HasFailedCurrent)
                throw new InvalidOperationException("Construction consistency read (" + reason + ") found unfinished construction: pending=" +
                    queued.Count.ToString(CultureInfo.InvariantCulture) + " active=" + activeUnits.ToString(CultureInfo.InvariantCulture) +
                    " failed=" + failedCurrent.ToString(CultureInfo.InvariantCulture));
        }

        void RunJobWithDependencies(SpawnJob job)
        {
            if (job.Dependencies != null)
            {
                for (int i = 0; i < job.Dependencies.Count; i++)
                {
                    var dep = job.Dependencies[i];
                    for (int k = 0; k < queued.Count; k++)
                    {
                        if (!queued[k].Identity.Equals(dep)) continue;
                        var depJob = queued[k];
                        queued.RemoveAt(k);
                        try
                        {
                            RunJobWithDependencies(depJob);
                        }
                        catch
                        {
                            job.FailReason = "dependency threw";
                            job.State = SpawnJobState.Failed;
                            Fail(job);
                            throw;
                        }
                        break;
                    }
                    bool failed;
                    if (!terminalFailed.TryGetValue(dep, out failed) || failed)
                    {
                        job.FailReason = "dependency failed or cancelled";
                        job.State = SpawnJobState.Failed;
                        Fail(job);
                        return;
                    }
                }
            }
            while (job.State == SpawnJobState.Queued || job.State == SpawnJobState.Running)
                StepUnit(job);
        }

        internal void RunSynchronous(SpawnJob job)
        {
            queued.Remove(job);
            RunJobWithDependencies(job);
        }

        internal string TakeSummary()
        {
            string text = "construction{admitted=" + AdmittedTotal.ToString(CultureInfo.InvariantCulture) +
                " completed=" + CompletedTotal.ToString(CultureInfo.InvariantCulture) +
                " failed=" + FailedTotal.ToString(CultureInfo.InvariantCulture) +
                " cancelled=" + CancelledTotal.ToString(CultureInfo.InvariantCulture) +
                " dup=" + DuplicateTotal.ToString(CultureInfo.InvariantCulture) +
                " cap=" + CapacityTotal.ToString(CultureInfo.InvariantCulture) +
                " stale=" + StaleTotal.ToString(CultureInfo.InvariantCulture) +
                " missingDep=" + MissingDepTotal.ToString(CultureInfo.InvariantCulture) +
                " blocked=" + BlockedTotal.ToString(CultureInfo.InvariantCulture) +
                " units=" + UnitsRun.ToString(CultureInfo.InvariantCulture) +
                " overruns=" + Overruns.ToString(CultureInfo.InvariantCulture) +
                " hiGroups=" + HighWaterGroups.ToString(CultureInfo.InvariantCulture) +
                " hiRecords=" + HighWaterRecords.ToString(CultureInfo.InvariantCulture) +
                " hiBytes=" + HighWaterBytes.ToString(CultureInfo.InvariantCulture) +
                " terminalEvict=" + TerminalEvictions.ToString(CultureInfo.InvariantCulture) +
                " frameSpentMs=" + FrameSpentMs.ToString("F3", CultureInfo.InvariantCulture) +
                " atomicOver3ms=" + AtomicOver3ms.ToString(CultureInfo.InvariantCulture) +
                " agedOverruns=" + AgedOverruns.ToString(CultureInfo.InvariantCulture) +
                " failedNow=" + failedCurrent.ToString(CultureInfo.InvariantCulture) +
                (LastFailReason == null ? "" : " lastFail=" + LastFailReason) + "}";
            AdmittedTotal = CompletedTotal = FailedTotal = CancelledTotal = 0;
            DuplicateTotal = CapacityTotal = StaleTotal = MissingDepTotal = BlockedTotal = 0;
            AgedOverruns = AtomicOver3ms = 0;
            UnitsRun = Overruns = TerminalEvictions = 0;
            HighWaterGroups = queued.Count;
            HighWaterRecords = LiveRecords;
            HighWaterBytes = LiveBytes;
            return text;
        }
    }
}
