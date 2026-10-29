using System;

namespace TldMp
{
    // Pure decisions behind the guest's world-streaming bookkeeping. Each pass
    // used to rebuild its full work list every frame; these keep the result and
    // only repeat the work when its inputs change or a short time has passed.

    // Remembers that every manifest around the players was available. The cache
    // is keyed on a signature of the chunks around each player, so moving into a
    // new chunk recomputes at once, and it expires so a retried POI is re-checked.
    public sealed class ManifestReadyCache
    {
        public const double MaxAgeSeconds = 2.0;
        long signature;
        bool valid;
        double expires;

        public bool TryReuse(long currentSignature, double now)
        {
            return valid && currentSignature == signature && now < expires;
        }

        public void StoreReady(long currentSignature, double now)
        {
            signature = currentSignature; valid = true; expires = now + MaxAgeSeconds;
        }

        public void Invalidate() { valid = false; }

        // Order-sensitive 64-bit mix (FNV-1a style) for building a signature.
        public static long Combine(long hash, long value)
        {
            unchecked { return (hash ^ value) * 1099511628211L; }
        }

        public const long Seed = unchecked((long)14695981039346656037UL);
    }

    // Cleanup of cells outside the needed ring only has to run when that ring or
    // the set of generated cells changed, plus a periodic sweep: an object that
    // was destroyed in the meantime becomes evictable without either changing.
    public sealed class EvictGate
    {
        public const double IntervalSeconds = 1.0;
        object list;
        int needed, generated;
        double next;
        bool any;

        public bool ShouldRun(object neededList, int neededCount, int generatedCount, double now)
        {
            bool changed = !any || !ReferenceEquals(list, neededList) || needed != neededCount || generated != generatedCount;
            if (!changed && now < next) return false;
            any = true; list = neededList; needed = neededCount; generated = generatedCount;
            next = now + IntervalSeconds;
            return true;
        }
    }

    // Splits one long scan of a request list into bounded slices per call so no
    // single frame formats and checks thousands of keys.
    public struct ScanStep
    {
        public int Start, End;
        public bool Finished;
    }

    public static class ScanBudget
    {
        public const int KeysPerCall = 48;

        public static ScanStep Next(int cursor, int total, int budget)
        {
            var step = new ScanStep();
            if (total <= 0) { step.Finished = true; return step; }
            if (cursor < 0 || cursor >= total) cursor = 0;
            if (budget < 1) budget = 1;
            long end = (long)cursor + budget;
            step.Start = cursor;
            step.End = end >= total ? total : (int)end;
            step.Finished = step.End >= total;
            return step;
        }
    }
}
