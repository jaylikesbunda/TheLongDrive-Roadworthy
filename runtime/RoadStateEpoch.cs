using System;
using System.Threading;

namespace TldMp
{
    // Global road-state epoch. Each writer of the state that roadGenScript.RoadsNear,
    // RoadsDoneAtPlace, poiGenScript.AlignPosToRoad and road terrain height read bumps
    // this counter. A memo may reuse a value only while Current is unchanged, and it may
    // store a value only when Current was unchanged across the call that produced it.
    // That producing call then generated nothing, so a later call at the same epoch reads
    // the same state, returns the same list in the same order, and generates nothing.
    //
    // Writers and their bumps:
    //   roadGenScript.UpdDotChunk   adds a chunk and its dots          RoadStateEpoch via StableGameBridge.RoadDotChunkAdding (cell absent)
    //   roadGenScript.UpdChunks     removes chunks                     StableGameBridge.RoadChunksRemoving (chunksToRemove non-empty)
    //   roadGenScript.ConnectDots   adds roadClass entries (ConnectWithRoad)  StableGameBridge.RoadConnectionsStarting
    //   roadClass.Generate          generated, roadElements, bonePos, UpdInnerChunk  StableGameBridge.RoadGenerated
    //   roadGenScript.Restart       generator lifecycle (FStart, map load)  StableGameBridge.RoadStateReset
    //   StableMap road restore      replaces chunk, dots, roads, inner chunks  StableMap (direct Bump)
    // Not read by these functions, so not bumped: connectivity flags, chunksNeeded,
    // innerChunkGenerated (RoadChunkPolicy), road object lifecycle (RoadFarSkip).
    public static class RoadStateEpoch
    {
        static long epoch;

        // Test-only mutation switch. When set, Current is constant, so every memo entry
        // looks valid. The POI cache tests use it to prove that the epoch is needed.
        public static bool MutationIgnoreEpochForTest;

        public static long Current
        {
            get { return MutationIgnoreEpochForTest ? 0L : Interlocked.Read(ref epoch); }
        }

        public static void Bump()
        {
            Interlocked.Increment(ref epoch);
        }

        public static bool UnchangedSince(long before)
        {
            return Current == before;
        }
    }
}
