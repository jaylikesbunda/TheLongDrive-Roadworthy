using System;
using System.Collections.Generic;

namespace TldMp
{
    // Per-sender budget for one-shot cosmetic replays (sounds and particles that a
    // remote player's action causes). Replays past the budget are dropped, never queued.
    internal sealed class CosmeticRateLimiter
    {
        internal const int MaxPerSecond = 20;
        readonly Dictionary<ulong, double> windowStart = new Dictionary<ulong, double>();
        readonly Dictionary<ulong, int> counts = new Dictionary<ulong, int>();

        internal bool Allow(ulong key, double now)
        {
            if (key == 0 || Double.IsNaN(now) || Double.IsInfinity(now)) return false;
            double start;
            if (!windowStart.TryGetValue(key, out start) || now < start || now - start >= 1.0)
            {
                windowStart[key] = now;
                counts[key] = 1;
                return true;
            }
            int count = counts[key];
            if (count >= MaxPerSecond) return false;
            counts[key] = count + 1;
            return true;
        }

        internal void Remove(ulong key)
        {
            windowStart.Remove(key);
            counts.Remove(key);
        }

        internal void Clear()
        {
            windowStart.Clear();
            counts.Clear();
        }
    }

    // Decisions for cosmetic replays. None of these change gameplay state; they only
    // say whether a sound or particle should play on this machine.
    internal static class CosmeticPolicy
    {
        // A bite is a rise in a non-continuous food's bite index. Several rises that
        // arrive in one state update are one bite to the ear, so they replay once.
        internal static bool FoodBite(int before, int after, bool continuous)
        {
            return !continuous && before >= 0 && after > before;
        }

        // The ignition click is the radio-style click on the edge of a car's ignition
        // change. The local driver already hears it from their own input, so the click is
        // replayed only for a car the local player is not driving.
        internal static bool IgnitionClick(bool previous, bool current, bool localDriving)
        {
            return previous != current && !localDriving;
        }
    }
}
