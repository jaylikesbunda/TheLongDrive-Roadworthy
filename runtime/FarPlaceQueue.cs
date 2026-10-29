using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TldMp
{
    // PlaceStuff used to place every far item in one frame (131-209 ms hitches).
    // The far pass now queues the item id, and the driver places a few ms of
    // queued items per frame. Only timing changes: each id is revalidated against
    // the live world right before PlaceOne runs. Unity-free policy: the caller
    // supplies the clock and the place action, and the queue is bounded.
    internal sealed class FarPlaceQueuePolicy
    {
        internal const int MaxQueued = 4096;

        readonly Queue<uint> order = new Queue<uint>();
        readonly HashSet<uint> pending = new HashSet<uint>();
        internal int Queued, Placed, Dropped, MaxQueue;

        internal int Count { get { return order.Count; } }

        // Returns false for a duplicate (already queued) or an id refused by the cap.
        internal bool Enqueue(uint id)
        {
            if (pending.Contains(id)) return false;
            if (order.Count >= MaxQueued)
            {
                Dropped++;
                return false;
            }
            order.Enqueue(id);
            pending.Add(id);
            Queued++;
            if (order.Count > MaxQueue) MaxQueue = order.Count;
            return true;
        }

        // Places queued ids in FIFO order while elapsedMs() is below budgetMs.
        // At least one id is handled per call when any is queued. place returns
        // true when the id was placed and false when it must be dropped.
        internal int Drain(double budgetMs, Func<double> elapsedMs, Func<uint, bool> place)
        {
            int handled = 0;
            while (order.Count > 0)
            {
                if (handled > 0 && elapsedMs() >= budgetMs) break;
                uint id = order.Dequeue();
                pending.Remove(id);
                if (place(id)) Placed++;
                else Dropped++;
                handled++;
            }
            return handled;
        }

        // Discards everything still queued, for world unload or reset.
        internal void Clear()
        {
            Dropped += order.Count;
            order.Clear();
            pending.Clear();
        }

        // Log fragment for the camera diagnostics line, then resets the window counts.
        internal string TakeSummary()
        {
            string text = "farPlace{queued=" + Queued.ToString(CultureInfo.InvariantCulture) +
                " placed=" + Placed.ToString(CultureInfo.InvariantCulture) +
                " dropped=" + Dropped.ToString(CultureInfo.InvariantCulture) +
                " maxQueue=" + MaxQueue.ToString(CultureInfo.InvariantCulture) + "}";
            Queued = 0;
            Placed = 0;
            Dropped = 0;
            MaxQueue = order.Count;
            return text;
        }
    }

    // Unity-side driver. Holds the policy for the game's far item placement and
    // revalidates each queued id exactly as the synchronous pass would.
    // Only the periodic one-second pass may defer. World load (UpdInstant, then
    // LoadPlayerStuff restores held/worn items) and the construction spawner
    // (clientDontAskOnSpawn) read the placed items right after Upd returns.
    public static class FarPlaceTiming
    {
        public static bool MustPlaceNow(bool featureOff, bool worldStarted, bool loading, bool dontAskOnSpawn, bool hasPlayer)
        {
            return featureOff || !worldStarted || loading || dontAskOnSpawn || !hasPlayer;
        }
    }

    public static class FarPlaceQueue
    {
        internal const double BudgetMs = 3.0;
        static System.Reflection.FieldInfo dontAskField;
        static bool dontAskLookedUp;
        static int featureState;

        // Any doubt (missing state, reflection failure) places synchronously, as vanilla.
        public static bool MustPlaceNow(itemPlaceRemoveScript script)
        {
            if (featureState == 0) featureState = Environment.GetEnvironmentVariable("TLD_MP_FARPLACE_SPREAD") == "0" ? 2 : 1;
            bool dontAsk = true;
            try
            {
                if (!dontAskLookedUp)
                {
                    dontAskLookedUp = true;
                    dontAskField = typeof(itemPlaceRemoveScript).GetField("clientDontAskOnSpawn",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                }
                if (script != null && dontAskField != null) dontAsk = (bool)dontAskField.GetValue(script);
            }
            catch (Exception) { dontAsk = true; }
            var map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
            bool loading = DataFromMenuScript.s == null || DataFromMenuScript.s.load;
            bool hasPlayer = mainscript.s != null && mainscript.s.player != null;
            return FarPlaceTiming.MustPlaceNow(featureState == 2, map != null && map.firstRun, loading, dontAsk, hasPlayer);
        }

        static readonly FarPlaceQueuePolicy policy = new FarPlaceQueuePolicy();
        static readonly System.Diagnostics.Stopwatch drainWatch = new System.Diagnostics.Stopwatch();
        static readonly Func<double> drainElapsed = delegate { return drainWatch.Elapsed.TotalMilliseconds; };
        static bool loggedError;

        public static void Enqueue(uint id)
        {
            policy.Enqueue(id);
        }

        // Called once per frame from the bridge driver.
        public static void Drain()
        {
            if (policy.Count == 0) return;
            if (!WorldReady())
            {
                policy.Clear();
                return;
            }
            drainWatch.Reset();
            drainWatch.Start();
            policy.Drain(BudgetMs, drainElapsed, TryPlace);
            drainWatch.Stop();
        }

        // Places everything queued now, with no budget. Used before a world capture.
        public static void FlushAll()
        {
            if (policy.Count == 0) return;
            if (!WorldReady())
            {
                policy.Clear();
                return;
            }
            policy.Drain(double.PositiveInfinity, drainElapsed, TryPlace);
        }

        public static void Clear()
        {
            policy.Clear();
        }

        public static string TakeSummary()
        {
            return policy.TakeSummary();
        }

        static bool WorldReady()
        {
            return itemPlaceRemoveScript.s != null && menuhandler.s != null &&
                menuhandler.s.currentMainMap != null && savedatascript.s != null;
        }

        // The same checks the synchronous PlaceStuff path depends on. Indices are
        // looked up here, never at enqueue time, because they go stale.
        static bool Valid(uint id, out int index)
        {
            index = -1;
            var script = itemPlaceRemoveScript.s;
            if (script == null) return false;
            if (!savedatascript.IndexOfID(savedatascript.s.data.itemData.items, id, out index)) return false;
            if (savedatascript.s.items.ContainsKey(id)) return false;
            var map = menuhandler.s.currentMainMap;
            var position = savedatascript.s.data.itemData.items[index].transform.pos;
            for (int i = 0; i < map.genArounds.Count; i++)
            {
                if ((mainscript.GlobalFromUnityPos(map.genArounds[i].upos) - position).magnitude < (double)script.itemSpawnDist)
                    return true;
            }
            return false;
        }

        static bool TryPlace(uint id)
        {
            int index;
            if (!Valid(id, out index)) return false;
            try
            {
                itemPlaceRemoveScript.s.PlaceOne(id, index);
                return true;
            }
            catch (Exception e)
            {
                if (!loggedError)
                {
                    loggedError = true;
                    UnityEngine.Debug.Log("[TLD MP far place] PlaceOne failed for id " + id.ToString(CultureInfo.InvariantCulture) +
                        ": " + e.GetType().Name + ": " + e.Message);
                }
                return false;
            }
        }
    }
}
