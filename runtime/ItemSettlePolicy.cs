using System.Collections.Generic;
using System.Globalization;

namespace TldMp
{
    // Frozen items that are about to unfreeze (a player came within range) keep
    // falling through terrain or colliders that have not loaded yet. This policy
    // holds such an item frozen until ground exists below it, with a hard 20 s
    // cap per item. Items the native game owns (held, attached, stored or buried)
    // are never held here. Unity-free: the caller supplies the ground result and
    // the clock, and the table is bounded.
    internal sealed class ItemSettlePolicy
    {
        internal const double MaxHoldSeconds = 20.0;
        internal const int MaxTracked = 256;

        readonly Dictionary<int, double> firstHeld = new Dictionary<int, double>();
        internal int Held, Released, Timeouts;

        internal int Tracked { get { return firstHeld.Count; } }

        // Returns true to keep the item frozen this pass, false to let the native
        // unfreeze run. nativeOwned means the native game decides for this item.
        internal bool ShouldHold(int id, bool nativeOwned, bool hasGround, double now)
        {
            if (nativeOwned)
            {
                firstHeld.Remove(id);
                return false;
            }
            double first;
            bool tracked = firstHeld.TryGetValue(id, out first);
            if (hasGround)
            {
                if (tracked)
                {
                    firstHeld.Remove(id);
                    Released++;
                }
                return false;
            }
            if (!tracked)
            {
                if (firstHeld.Count >= MaxTracked) Prune(now);
                // Table still full: refuse to hold rather than track an unbounded set.
                if (firstHeld.Count >= MaxTracked) return false;
                firstHeld[id] = now;
                Held++;
                return true;
            }
            if (now - first >= MaxHoldSeconds)
            {
                firstHeld.Remove(id);
                Timeouts++;
                return false;
            }
            return true;
        }

        // Called when the item is no longer kinematic (or no longer exists).
        internal void Forget(int id)
        {
            firstHeld.Remove(id);
        }

        void Prune(double now)
        {
            var stale = new List<int>();
            foreach (var pair in firstHeld)
                if (now - pair.Value >= MaxHoldSeconds) stale.Add(pair.Key);
            foreach (int id in stale) firstHeld.Remove(id);
        }

        // Log fragment for the camera diagnostics line, then resets the window counts.
        internal string TakeSummary()
        {
            string text = "itemSettle{held=" + Held.ToString(CultureInfo.InvariantCulture) +
                " released=" + Released.ToString(CultureInfo.InvariantCulture) +
                " timeouts=" + Timeouts.ToString(CultureInfo.InvariantCulture) + "}";
            Held = 0;
            Released = 0;
            Timeouts = 0;
            return text;
        }
    }
}
