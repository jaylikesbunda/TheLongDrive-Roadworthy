using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TldMp
{
    // Pure decision for RoadIdleSkip (unit-tested without Unity objects).
    public static class RoadIdlePolicy
    {
        // A full pass still runs at least this often, so anything the inputs
        // below do not see (a road piece destroyed by other code) is repaired
        // within this time, as the native per-frame pass would have done.
        public const float MaxIdleSeconds = 1f;
        // Player movement smaller than this keeps the pass idle. Road pieces are
        // placed and removed at roadPlaceDist (hundreds of metres), so a quarter
        // metre cannot change what is in range except at that far edge, and the
        // forced pass above bounds that to one second.
        public const double MoveTolerance = 0.25;

        public static bool PassNeeded(bool havePrevious, bool previousChanged, float secondsSinceFull,
            bool placeDistChanged, bool dataChanged, bool aroundsChanged, bool childrenChanged)
        {
            return Reason(havePrevious, previousChanged, secondsSinceFull, placeDistChanged, dataChanged, aroundsChanged, childrenChanged) != 0;
        }

        // 0 = skip; otherwise the first reason a pass is needed (for diagnostics).
        public static int Reason(bool havePrevious, bool previousChanged, float secondsSinceFull,
            bool placeDistChanged, bool dataChanged, bool aroundsChanged, bool childrenChanged)
        {
            if (!havePrevious) return 1;
            if (previousChanged) return 2;
            if (secondsSinceFull >= MaxIdleSeconds) return 3;
            if (placeDistChanged) return 4;
            if (dataChanged) return 5;
            if (aroundsChanged) return 6;
            if (childrenChanged) return 7;
            return 0;
        }
    }

    // roadGenScript.UpdPlaceRemove runs every frame and walks every road cell of
    // every needed chunk (about 14,000) to place road pieces in range and remove
    // pieces out of range. Its result depends only on: the needed chunks' road
    // cells and elements (written only by roadClass.Generate and a generator
    // restart, counted by DataChanged), the generation centres' positions (the
    // needed chunks follow them), roadPlaceDist, and which pieces exist. Chunks
    // outside the needed set are never read by it, so their churn does not count. Its only effects are creating a piece (a new child of the road
    // parent) or destroying one (StableGameBridge.RoadChunkDestroyed). When none of
    // those changed since a pass that itself changed nothing, the next pass would
    // change nothing either, so it is skipped.
    //
    // Single player only (the multiplayer road path is unchanged). The other road
    // steps run every frame exactly as before, in the native order. Disable with
    // TLD_MP_ROAD_IDLE_SKIP=0.
    public static class RoadIdleSkip
    {
        static readonly bool DisabledByEnvironment = Environment.GetEnvironmentVariable("TLD_MP_ROAD_IDLE_SKIP") == "0";
        // Developer A/B switch (SpawnProbe); paused runs the native method.
        public static bool Paused;
        static bool Disabled { get { return DisabledByEnvironment || Paused; } }
        sealed class State
        {
            internal bool Have, Changed;
            internal float LastFull, PlaceDist;
            internal long Epoch, Destroyed;
            internal int Children;
            internal Vector3d[] Arounds = new Vector3d[0];
        }
        static readonly ConditionalWeakTable<roadGenScript, State> states = new ConditionalWeakTable<roadGenScript, State>();
        static long destroyedPieces, roadData;
        public static long Passes, Skips, Placed, Removed;
        static readonly long[] reasons = new long[8];

        public static void PieceDestroyed() { destroyedPieces++; }
        // roadClass.Generate (cells and elements) or a generator restart.
        public static void DataChanged() { roadData++; }

        public static string Summary()
        {
            return "roadIdleSkip{passes=" + Passes + " skipped=" + Skips + " first=" + reasons[1] + " changed=" + reasons[2] + " timer=" + reasons[3] +
                " distance=" + reasons[4] + " data=" + reasons[5] + " moved=" + reasons[6] + " pieces=" + reasons[7] + " placed=" + Placed + " removed=" + Removed + "}";
        }

        // Called from the roadGenScript.Upd(bool) guard. True lets the native method
        // run unchanged; false means the same steps were run here.
        public static bool Run(roadGenScript road, bool more)
        {
            if (Disabled || road == null || !road.isActiveAndEnabled || road.mapSetting == null || road.mapSetting.genArounds == null) return true;
            if (kaposztaleves.s != null && kaposztaleves.s.settings.bacon) return true; // native debug drawing
            // Native Upd(bool): UpdChunksNeeded(); Upd2(moreFrame & _moreFrame).
            road.UpdChunksNeeded();
            bool frame = road.moreFrame & more;
            road.UpdChunks(frame);
            road.ConnectChunks();
            road.UpdGenRoad(frame);
            var state = states.GetOrCreateValue(road);
            int reason = Needed(road, state);
            if (reason == 0) { Skips++; return false; }
            reasons[reason]++;
            int childrenBefore = road.parent == null ? 0 : road.parent.childCount;
            long destroyedBefore = destroyedPieces;
            road.UpdPlaceRemove(frame);
            Passes++;
            int childrenAfter = road.parent == null ? 0 : road.parent.childCount;
            if (childrenAfter > childrenBefore) Placed += childrenAfter - childrenBefore;
            Removed += destroyedPieces - destroyedBefore;
            state.Changed = childrenAfter != childrenBefore || destroyedPieces != destroyedBefore;
            Record(road, state);
            return false;
        }

        static int Needed(roadGenScript road, State state)
        {
            var arounds = road.mapSetting.genArounds;
            bool aroundsChanged = arounds.Count != state.Arounds.Length;
            for (int i = 0; !aroundsChanged && i < arounds.Count; i++)
            {
                Vector3d now = mainscript.GlobalFromUnityPos(arounds[i].upos), was = state.Arounds[i];
                double dx = now.x - was.x, dy = now.y - was.y, dz = now.z - was.z;
                if (!(dx * dx + dy * dy + dz * dz <= RoadIdlePolicy.MoveTolerance * RoadIdlePolicy.MoveTolerance)) aroundsChanged = true;
            }
            int children = road.parent == null ? 0 : road.parent.childCount;
            return RoadIdlePolicy.Reason(state.Have, state.Changed, Time.unscaledTime - state.LastFull,
                road.roadPlaceDist != state.PlaceDist, roadData != state.Epoch, aroundsChanged,
                children != state.Children || destroyedPieces != state.Destroyed);
        }

        static void Record(roadGenScript road, State state)
        {
            var arounds = road.mapSetting.genArounds;
            if (state.Arounds.Length != arounds.Count) state.Arounds = new Vector3d[arounds.Count];
            for (int i = 0; i < arounds.Count; i++) state.Arounds[i] = mainscript.GlobalFromUnityPos(arounds[i].upos);
            state.Have = true;
            state.LastFull = Time.unscaledTime;
            state.PlaceDist = road.roadPlaceDist;
            state.Epoch = roadData;
            state.Children = road.parent == null ? 0 : road.parent.childCount;
            state.Destroyed = destroyedPieces;
        }

        public static void Clear() { Passes = Skips = Placed = Removed = 0; Array.Clear(reasons, 0, reasons.Length); }
    }
}
