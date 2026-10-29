using System;
using System.Globalization;
using System.Text;

namespace TldMp
{
    // Bounded, main-thread-only correlation for measured PlayerLoop envelopes.
    // Native observer scopes are nested/inclusive; per-slot self time subtracts
    // only observed descendants and is not a claim about all script work.
    public sealed class SlowFrameDiagnostics
    {
        const int MaxScopeDepth = 64;
        const int MaxNativeReport = 6;

        sealed class FrameRow
        {
            internal int Id = Int32.MinValue;
            internal bool Completed;
            internal int ConfiguredPhaseMask;
            internal bool HasEnvelope;
            internal long EarliestStart, LatestEnd;
            internal int PhaseObservedMask;
            internal int[] PhaseCalls;
            internal long[] PhaseTicks;
            internal long[] NativeTotal, NativeSelf, NativeMax;
            internal int[] NativeCalls;

            internal FrameRow(int phaseCount, int nativeCount)
            {
                PhaseCalls = new int[phaseCount]; PhaseTicks = new long[phaseCount];
                NativeTotal = new long[nativeCount]; NativeSelf = new long[nativeCount];
                NativeMax = new long[nativeCount]; NativeCalls = new int[nativeCount];
            }

            internal void Clear(int id)
            {
                Id = id; Completed = false; ConfiguredPhaseMask = 0; HasEnvelope = false;
                EarliestStart = LatestEnd = 0; PhaseObservedMask = 0;
                Array.Clear(PhaseCalls, 0, PhaseCalls.Length); Array.Clear(PhaseTicks, 0, PhaseTicks.Length);
                Array.Clear(NativeTotal, 0, NativeTotal.Length); Array.Clear(NativeSelf, 0, NativeSelf.Length);
                Array.Clear(NativeMax, 0, NativeMax.Length); Array.Clear(NativeCalls, 0, NativeCalls.Length);
            }
        }

        struct Scope
        {
            internal int Frame, Slot;
            internal long Start, ChildTicks;
        }

        readonly string[] nativeNames, phaseNames;
        readonly FrameRow[] rows;
        readonly Scope[] scopes = new Scope[MaxScopeDepth];
        readonly int[] topIndices, nativeTopIndices;
        readonly long[] nativeTopValues;
        int depth, incompleteScopes, droppedScopes;
        int evictedCompleted, evictedIncomplete;
        bool suppressOverflowFrame;
        int overflowFrame;

        public SlowFrameDiagnostics(string[] nativeNames, string[] phaseNames, int capacity = 256)
        {
            if (nativeNames == null) throw new ArgumentNullException("nativeNames");
            if (phaseNames == null) throw new ArgumentNullException("phaseNames");
            if (phaseNames.Length > 32) throw new ArgumentOutOfRangeException("phaseNames");
            if (capacity < 2) throw new ArgumentOutOfRangeException("capacity");
            this.nativeNames = (string[])nativeNames.Clone(); this.phaseNames = (string[])phaseNames.Clone();
            rows = new FrameRow[capacity];
            for (int i = 0; i < rows.Length; i++) rows[i] = new FrameRow(phaseNames.Length, nativeNames.Length);
            topIndices = new int[capacity]; nativeTopIndices = new int[MaxNativeReport];
            nativeTopValues = new long[MaxNativeReport];
        }

        FrameRow Touch(int frame)
        {
            int index = frame % rows.Length;
            if (index < 0) index += rows.Length;
            FrameRow row = rows[index];
            if (row.Id != frame)
            {
                if (row.Id != Int32.MinValue)
                {
                    if (row.Completed) Increment(ref evictedCompleted);
                    else Increment(ref evictedIncomplete);
                }
                row.Clear(frame);
            }
            return row;
        }

        static void Increment(ref int value) { if (value < Int32.MaxValue) value++; }

        bool Suppressed(int frame)
        {
            if (!suppressOverflowFrame) return false;
            if (frame == overflowFrame) return true;
            if (frame < overflowFrame) return true;
            suppressOverflowFrame = false;
            return false;
        }

        void CheckActiveFrame(int frame)
        {
            if (depth != 0 && scopes[depth - 1].Frame != frame) DiscardScopes();
        }

        public void Begin(int frame, int slot, long ticks)
        {
            if (Suppressed(frame)) { Increment(ref droppedScopes); return; }
            if (slot < 0 || slot >= nativeNames.Length) { Increment(ref droppedScopes); return; }
            CheckActiveFrame(frame);
            if (depth == scopes.Length)
            {
                Increment(ref droppedScopes); DiscardScopes();
                suppressOverflowFrame = true; overflowFrame = frame;
                return;
            }
            Touch(frame);
            scopes[depth].Frame = frame; scopes[depth].Slot = slot;
            scopes[depth].Start = ticks; scopes[depth].ChildTicks = 0; depth++;
        }

        public void End(int frame, int slot, long ticks)
        {
            if (Suppressed(frame)) { Increment(ref droppedScopes); return; }
            if (depth == 0) return;
            Scope current = scopes[depth - 1];
            if (current.Frame != frame || current.Slot != slot || ticks < current.Start)
            {
                DiscardScopes(); Increment(ref droppedScopes);
                return;
            }
            depth--;
            long elapsed = ticks - current.Start;
            FrameRow row = Touch(frame);
            row.NativeTotal[slot] += elapsed;
            long self = elapsed - current.ChildTicks;
            if (self < 0) self = 0;
            row.NativeSelf[slot] += self;
            if (elapsed > row.NativeMax[slot]) row.NativeMax[slot] = elapsed;
            if (row.NativeCalls[slot] < Int32.MaxValue) row.NativeCalls[slot]++;
            if (depth != 0)
            {
                Scope parent = scopes[depth - 1];
                if (parent.Frame == frame) { parent.ChildTicks += elapsed; scopes[depth - 1] = parent; }
                else DiscardScopes();
            }
        }

        void DiscardScopes()
        {
            if (depth > Int32.MaxValue - incompleteScopes) incompleteScopes = Int32.MaxValue;
            else incompleteScopes += depth;
            depth = 0;
        }

        public void AddPhase(int frame, int phase, long startTicks, long endTicks)
        {
            if (phase < 0 || phase >= phaseNames.Length || startTicks < 0 || endTicks < startTicks)
            { Increment(ref droppedScopes); return; }
            CheckActiveFrame(frame);
            FrameRow row = Touch(frame);
            row.PhaseTicks[phase] += endTicks - startTicks;
            if (row.PhaseCalls[phase] < Int32.MaxValue) row.PhaseCalls[phase]++;
            row.PhaseObservedMask |= 1 << phase;
            if (!row.HasEnvelope)
            {
                row.HasEnvelope = true; row.EarliestStart = startTicks; row.LatestEnd = endTicks;
            }
            else
            {
                if (startTicks < row.EarliestStart) row.EarliestStart = startTicks;
                if (endTicks > row.LatestEnd) row.LatestEnd = endTicks;
            }
        }

        public void CompleteFrame(int frame, int configuredPhaseMask)
        {
            // CompleteFrame is called for the previous frame at the start of
            // Update. A newer frame's synchronous native scope is still live.
            if (depth != 0 && scopes[depth - 1].Frame == frame) DiscardScopes();
            FrameRow row = Touch(frame);
            row.ConfiguredPhaseMask = configuredPhaseMask & (phaseNames.Length == 32 ? -1 : ((1 << phaseNames.Length) - 1));
            row.Completed = true;
        }

        public void Reset()
        {
            DiscardScopes();
            for (int i = 0; i < rows.Length; i++) rows[i].Clear(Int32.MinValue);
            incompleteScopes = droppedScopes = evictedCompleted = evictedIncomplete = 0;
            suppressOverflowFrame = false; overflowFrame = 0;
        }

        public string ReportAndReset(long ticksPerSecond, int maxFrames = 3, double thresholdMs = 33.3)
        {
            if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException("ticksPerSecond");
            if (Double.IsNaN(thresholdMs) || Double.IsInfinity(thresholdMs) || thresholdMs < 0)
                throw new ArgumentOutOfRangeException("thresholdMs");
            if (maxFrames < 0) throw new ArgumentOutOfRangeException("maxFrames");
            if (maxFrames > rows.Length) maxFrames = rows.Length;

            int selected = 0;
            double thresholdTicks = thresholdMs * ticksPerSecond / 1000.0;
            for (int i = 0; i < rows.Length; i++)
            {
                FrameRow row = rows[i];
                if (!row.Completed) continue;
                if (row.HasEnvelope && row.LatestEnd - row.EarliestStart >= thresholdTicks && maxFrames > 0)
                {
                    if (selected == maxFrames && !IsBetter(row, rows[topIndices[selected - 1]])) continue;
                    int pos = selected;
                    if (pos >= maxFrames) pos = maxFrames - 1;
                    while (pos > 0 && IsBetter(row, rows[topIndices[pos - 1]]))
                    {
                        if (pos < maxFrames) topIndices[pos] = topIndices[pos - 1];
                        pos--;
                    }
                    if (pos < maxFrames)
                    {
                        topIndices[pos] = i;
                        if (selected < maxFrames) selected++;
                    }
                }
            }

            StringBuilder text = new StringBuilder();
            text.Append("slowFrames{basis=partial observed PlayerLoop envelope; excludes unobserved presentation; threshold=")
                .Append(thresholdMs.ToString("F1", CultureInfo.InvariantCulture)).Append("ms; incompleteScopes=")
                .Append(incompleteScopes).Append(" droppedEvents=").Append(droppedScopes)
                .Append(" evictedCompleted=").Append(evictedCompleted)
                .Append(" evictedIncomplete=").Append(evictedIncomplete);
            double scale = 1000.0 / ticksPerSecond;
            for (int n = 0; n < selected; n++)
            {
                FrameRow row = rows[topIndices[n]];
                text.Append("; #").Append(n + 1).Append(" frame=").Append(row.Id)
                    .Append(" envelopeMs=").Append(((row.LatestEnd - row.EarliestStart) * scale).ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" phases[");
                for (int phase = 0; phase < phaseNames.Length; phase++)
                {
                    if (phase != 0) text.Append(',');
                    text.Append(phaseNames[phase]).Append('=');
                    if ((row.ConfiguredPhaseMask & (1 << phase)) == 0) text.Append("unavailable");
                    else if ((row.PhaseObservedMask & (1 << phase)) == 0 && phase != 1 && phase != 2)
                        text.Append("unavailable(no callback)");
                    else text.Append((row.PhaseTicks[phase] * scale).ToString("F2", CultureInfo.InvariantCulture))
                        .Append("ms/").Append(row.PhaseCalls[phase]);
                }
                text.Append("] nativeTop[");
                AppendNativeTop(text, row, scale);
                text.Append(']');
            }
            text.Append("}");

            // Completed records are consumed even when below threshold. Keep
            // in-progress/uncompleted records so current or late hooks survive.
            for (int i = 0; i < rows.Length; i++) if (rows[i].Completed) rows[i].Clear(Int32.MinValue);
            incompleteScopes = droppedScopes = evictedCompleted = evictedIncomplete = 0;
            return text.ToString();
        }

        static bool IsBetter(FrameRow candidate, FrameRow existing)
        {
            long a = candidate.LatestEnd - candidate.EarliestStart;
            long b = existing.LatestEnd - existing.EarliestStart;
            return a > b || (a == b && candidate.Id < existing.Id);
        }

        void AppendNativeTop(StringBuilder text, FrameRow row, double scale)
        {
            int count = 0;
            for (int slot = 0; slot < nativeNames.Length; slot++)
            {
                if (row.NativeCalls[slot] == 0) continue;
                if (count == MaxNativeReport && row.NativeTotal[slot] <= nativeTopValues[count - 1]) continue;
                int pos = count < MaxNativeReport ? count : MaxNativeReport - 1;
                while (pos > 0 && row.NativeTotal[slot] > nativeTopValues[pos - 1])
                {
                    if (pos < MaxNativeReport)
                    { nativeTopValues[pos] = nativeTopValues[pos - 1]; nativeTopIndices[pos] = nativeTopIndices[pos - 1]; }
                    pos--;
                }
                if (pos < MaxNativeReport)
                {
                    nativeTopValues[pos] = row.NativeTotal[slot]; nativeTopIndices[pos] = slot;
                    if (count < MaxNativeReport) count++;
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (i != 0) text.Append(';');
                int slot = nativeTopIndices[i];
                text.Append(nativeNames[slot] ?? ("slot" + slot)).Append(" total=")
                    .Append((row.NativeTotal[slot] * scale).ToString("F2", CultureInfo.InvariantCulture))
                    .Append("ms selfObs=").Append((row.NativeSelf[slot] * scale).ToString("F2", CultureInfo.InvariantCulture))
                    .Append("ms max=").Append((row.NativeMax[slot] * scale).ToString("F2", CultureInfo.InvariantCulture))
                    .Append("ms calls=").Append(row.NativeCalls[slot]);
            }
        }
    }
}
