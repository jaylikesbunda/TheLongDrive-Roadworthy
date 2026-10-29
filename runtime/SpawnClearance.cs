using System;
using System.Collections.Generic;

namespace TldMp
{
    // Joining clients are held at their spawn point until ground exists
    // (NativeSpawnSafety). This adds a clearance rule: the player's capsule must
    // not overlap anything solid except the ground (terrain tiles, roads) -
    // props, cars, items, building walls, other players. When blocked, a nearby
    // free spot is searched in rings, nearest first. The spot must stay clear for
    // SettleSeconds (items keep streaming in right after joining). If nothing is
    // free for GiveUpSeconds, the hold releases at the native spot as before.
    // Unity-free so it can be tested offline.
    public static class SpawnClearancePolicy
    {
        public const double RingStep = 1.5;
        public const double MaxRadius = 15.0;
        public const double MaxHeightChange = 4.0;
        public const double SettleSeconds = 1.0;
        public const double GiveUpSeconds = 8.0;
        public const double SearchRetrySeconds = 0.5;

        // Candidate offsets (dx, dz), nearest ring first, 8*k points on ring k,
        // starting at angle 0 and going counter-clockwise. Deterministic.
        public static List<double[]> Offsets(double step, double maxRadius)
        {
            var result = new List<double[]>();
            if (!(step > 0) || !(maxRadius > 0)) return result;
            int rings = (int)Math.Floor(maxRadius / step + 1e-9);
            for (int k = 1; k <= rings; k++)
            {
                double r = k * step;
                int points = 8 * k;
                for (int i = 0; i < points; i++)
                {
                    double a = 2.0 * Math.PI * i / points;
                    result.Add(new[] { Math.Cos(a) * r, Math.Sin(a) * r });
                }
            }
            return result;
        }

        // A collider found in the player's capsule blocks the spawn unless it is a
        // trigger, part of the joining player, or ground.
        public static bool Blocks(bool isTrigger, bool isSelf, bool isGround, bool enabled)
        {
            return enabled && !isTrigger && !isSelf && !isGround;
        }

        // A downward hit is usable ground: walkable slope, not a loose physics
        // object (car roof, prop, item), close to the native spawn height.
        public static bool UsableGround(double normalY, bool hasRigidbody, double heightChange)
        {
            return normalY >= 0.45 && !hasRigidbody && Math.Abs(heightChange) <= MaxHeightChange;
        }
    }

    public enum SpawnClearanceAction { Release, Hold, Search, GiveUp }

    // Per-hold clearance state machine.
    public sealed class SpawnClearanceTracker
    {
        double clearSince = -1, blockedSince = -1, lastSearch = -1, firstBlocked = -1;
        bool gaveUp;
        public int Searches, Moves;
        public bool GaveUp { get { return gaveUp; } }

        public void Reset() { clearSince = blockedSince = lastSearch = firstBlocked = -1; gaveUp = false; Searches = Moves = 0; }

        // Called each evaluation while support exists and the world is live.
        public SpawnClearanceAction Evaluate(bool clear, double now)
        {
            // Once given up, the hold is released at the native spot as before.
            if (gaveUp) return SpawnClearanceAction.Release;
            if (clear)
            {
                blockedSince = -1;
                if (clearSince < 0) clearSince = now;
                return now - clearSince >= SpawnClearancePolicy.SettleSeconds ? SpawnClearanceAction.Release : SpawnClearanceAction.Hold;
            }
            clearSince = -1;
            if (blockedSince < 0) blockedSince = now;
            if (firstBlocked < 0) firstBlocked = now;
            // Blocked for GiveUpSeconds at one spot, or 2x that overall across moves.
            if (now - blockedSince >= SpawnClearancePolicy.GiveUpSeconds || now - firstBlocked >= 2 * SpawnClearancePolicy.GiveUpSeconds)
            {
                gaveUp = true;
                return SpawnClearanceAction.GiveUp;
            }
            if (lastSearch < 0 || now - lastSearch >= SpawnClearancePolicy.SearchRetrySeconds)
            {
                lastSearch = now;
                Searches++;
                return SpawnClearanceAction.Search;
            }
            return SpawnClearanceAction.Hold;
        }

        // A free spot was found and the hold moved there; it must settle again.
        public void Moved() { Moves++; clearSince = -1; blockedSince = -1; lastSearch = -1; }
    }
}
