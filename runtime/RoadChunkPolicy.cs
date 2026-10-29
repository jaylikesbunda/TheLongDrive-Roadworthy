using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // roadGenScript.UpdChunksToRemove currently marks every chunk that is not in
    // chunksNeeded. UpdChunks then destroys its road objects, removes it, and
    // immediately re-adds the same cell through the -1..1 neighbour loop, which
    // regenerates its dots (System.Random + terrain height) every frame.
    //
    // This policy keeps only the no-op border cells: a cell that UpdChunks will
    // re-add in the same call, has no roads or inner chunks, has no connectivity
    // flags set, and still carries the seed for the current map. Such a cell has
    // no road objects (objects exist only for chunksNeeded cells), and its dots
    // are exactly what UpdDotChunk would deterministically regenerate from the
    // same cell hash and map seed. Anything with data or a stale seed falls back
    // to the native remove/destroy/recreate path.
    internal static class RoadChunkPolicy
    {
        internal static bool Keep(roadGenScript road, List<Vector3d> needed, Vector3d cell)
        {
            if (ReferenceEquals(road, null) || needed == null || needed.Count == 0) return false;
            double step = road.chunkSize;
            if (!(step > 0.0)) return false;
            double limit = step * 1.5;
            bool inside = false, border = false;
            for (int i = 0; i < needed.Count; i++)
            {
                Vector3d n = needed[i];
                if (n.x == cell.x && n.y == cell.y && n.z == cell.z) { inside = true; break; }
                if (Math.Abs(n.x - cell.x) <= limit && Math.Abs(n.z - cell.z) <= limit) border = true;
            }
            if (inside) return true;
            if (!border) return false;
            if (road.chunks == null || ReferenceEquals(road.mapSetting, null)) return false;
            roadGenScript.dotChunkClass chunk;
            if (!road.chunks.TryGetValue(cell, out chunk) || chunk == null) return false;
            if (chunk.roads != null && chunk.roads.Count != 0) return false;
            if (chunk.innerChunks != null && chunk.innerChunks.Count != 0) return false;
            if (chunk.insideConnected || chunk.outsideUConnected || chunk.outsideRConnected || chunk.innerChunkGenerated) return false;
            if (chunk.seed != cell.GetHashCode() + road.mapSetting.seed) return false;
            return true;
        }
    }
}
