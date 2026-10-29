using System;
using System.Collections.Generic;

namespace TldMp
{
    // syncScript.UpdItemParent runs for every received item/car pose and, for
    // each remote player, calls mpplayerscript.IgnoreCollision(item, false):
    // GetComponentsInChildren<Collider>() on the whole item plus one
    // Physics.IgnoreCollision per (item collider, player collider) pair. For a car
    // that is hundreds of native calls per received pose, almost always restoring
    // pairs that are already not ignored.
    //
    // A restore can be skipped exactly when, since the last full restore of the
    // same (item, player) pair: the item's active collider set is identical, the
    // player's collider array is identical, and no ignore=true call for that
    // player has been made. Every ignore=true against a remote player's colliders
    // goes through StableGameBridge.IgnoreCollisionSafe (UpdItemParent's calls are
    // routed there), and colliders that are new, re-activated or re-enabled start
    // out not ignored, so the skipped calls could only have been no-ops.
    // Unity-free so it can be tested offline.
    internal sealed class CollisionRestoreCache
    {
        internal const int MaxEntries = 4096;

        sealed class Entry
        {
            internal int[] Item;
            internal int[] Player;
            internal int PlayerGeneration;
        }

        readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
        readonly Dictionary<int, int> playerGeneration = new Dictionary<int, int>();

        internal long Skipped, Restored, PairsAvoided, Invalidations;

        static long Key(int itemId, int playerId) { return ((long)itemId << 32) ^ (uint)playerId; }

        int Generation(int playerId)
        {
            int g;
            return playerGeneration.TryGetValue(playerId, out g) ? g : 0;
        }

        // True when the native restore would only repeat a completed restore.
        internal bool CanSkip(int itemId, int playerId, int[] itemColliders, int itemCount, int[] playerColliders, int playerCount)
        {
            Entry e;
            if (!entries.TryGetValue(Key(itemId, playerId), out e)) return false;
            if (e.PlayerGeneration != Generation(playerId)) return false;
            if (!Same(e.Item, itemColliders, itemCount) || !Same(e.Player, playerColliders, playerCount)) return false;
            Skipped++;
            PairsAvoided += (long)itemCount * playerCount;
            return true;
        }

        // Records a completed native restore of every pair in these exact sets.
        internal void RecordRestore(int itemId, int playerId, int[] itemColliders, int itemCount, int[] playerColliders, int playerCount)
        {
            Restored++;
            if (entries.Count >= MaxEntries) entries.Clear();
            var e = new Entry();
            e.Item = Copy(itemColliders, itemCount);
            e.Player = Copy(playerColliders, playerCount);
            e.PlayerGeneration = Generation(playerId);
            entries[Key(itemId, playerId)] = e;
        }

        // Any ignore=true for this player invalidates every cached pair with it.
        internal void PlayerIgnored(int playerId)
        {
            Invalidations++;
            playerGeneration[playerId] = Generation(playerId) + 1;
        }

        internal void Clear()
        {
            entries.Clear();
            playerGeneration.Clear();
        }

        internal int Count { get { return entries.Count; } }

        static bool Same(int[] cached, int[] current, int count)
        {
            if (cached == null || current == null || cached.Length != count || count > current.Length) return false;
            for (int i = 0; i < count; i++) if (cached[i] != current[i]) return false;
            return true;
        }

        static int[] Copy(int[] source, int count)
        {
            var copy = new int[count];
            Array.Copy(source, copy, count);
            return copy;
        }
    }
}
