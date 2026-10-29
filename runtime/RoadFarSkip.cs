using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TldMp
{
    // Two exact-output reductions of roadGenScript.UpdPlaceRemove work.
    //
    // Dedupe: GetChunksNeeded concatenates one ring per genAround, so two players
    // in the same area list every road chunk twice and UpdPlaceRemove walks each
    // chunk twice. Every road consumer of chunksNeeded is gated by ContainsKey or
    // a per-chunk flag, so a later identical entry is a no-op. Only entries with
    // identical components are removed; the first occurrence keeps its order.
    //
    // Far skip: a road chunk whose inner cells are all farther than roadPlaceDist
    // from every genAround only runs the far branch, which only destroys live
    // elements. When no element of the chunk is live that branch does nothing,
    // so the chunk can be skipped. The distance test is a lower bound on the
    // native per-cell magnitude (same double arithmetic, monotonic rounding), and
    // the live check walks a superset of the elements the far branch reads.
    internal static class RoadFarSkip
    {
        // Extra distance beyond the proven bound, so a cell near the boundary
        // always takes the native path.
        internal const double Margin = 0.5;

        sealed class Entry
        {
            internal int InnerCount = -1;
            internal double MinX, MaxX, MinZ, MaxZ;
            internal bool Empty;
        }

        sealed class ExactComparer : IEqualityComparer<Vector3d>
        {
            public bool Equals(Vector3d a, Vector3d b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
            public int GetHashCode(Vector3d v)
            {
                unchecked { return ((v.x.GetHashCode() * 397) ^ v.y.GetHashCode()) * 397 ^ v.z.GetHashCode(); }
            }
        }

        static ConditionalWeakTable<roadGenScript.dotChunkClass, Entry> entries =
            new ConditionalWeakTable<roadGenScript.dotChunkClass, Entry>();
        static readonly HashSet<Vector3d> seen = new HashSet<Vector3d>(new ExactComparer());
        static readonly List<Vector3d> centers = new List<Vector3d>();

        internal static long Skipped, Walked, Deduped;

        internal static void Clear()
        {
            entries = new ConditionalWeakTable<roadGenScript.dotChunkClass, Entry>();
        }

        internal static void Dedupe(List<Vector3d> needed)
        {
            if (needed == null || needed.Count < 2) return;
            seen.Clear();
            int write = 0;
            for (int read = 0; read < needed.Count; read++)
            {
                Vector3d value = needed[read];
                if (!seen.Add(value)) continue;
                if (write != read) needed[write] = value;
                write++;
            }
            int removed = needed.Count - write;
            if (removed > 0) { needed.RemoveRange(write, removed); Deduped += removed; }
            seen.Clear();
        }

        internal static bool Skippable(roadGenScript road, roadGenScript.dotChunkClass chunk)
        {
            if (ReferenceEquals(road, null) || chunk == null || chunk.innerChunks == null || chunk.roads == null ||
                ReferenceEquals(road.mapSetting, null) || road.mapSetting.genArounds == null)
            { Walked++; return false; }
            centers.Clear();
            var arounds = road.mapSetting.genArounds;
            for (int i = 0; i < arounds.Count; i++)
            {
                if (arounds[i] == null) { Walked++; return false; }
                centers.Add(mainscript.GlobalFromUnityPos(arounds[i].upos));
            }
            bool skip = Decide(chunk, centers, road.roadPlaceDist);
            if (skip) Skipped++; else Walked++;
            return skip;
        }

        // Unity-free core, exercised directly by the offline tests.
        internal static bool Decide(roadGenScript.dotChunkClass chunk, IList<Vector3d> genCenters, float roadPlaceDist)
        {
            Entry entry = entries.GetOrCreateValue(chunk);
            if (entry.InnerCount != chunk.innerChunks.Count) Bounds(chunk, entry);
            if (entry.InnerCount > 0 && !AllFar(entry, genCenters, roadPlaceDist))
            {
                // The native loop may instantiate here; re-prove emptiness later.
                entry.Empty = false;
                return false;
            }
            if (entry.Empty) return true;
            entry.Empty = NoLiveElements(chunk);
            return entry.Empty;
        }

        static void Bounds(roadGenScript.dotChunkClass chunk, Entry entry)
        {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            bool finite = true;
            foreach (var key in chunk.innerChunks.Keys)
            {
                if (double.IsNaN(key.x) || double.IsNaN(key.z) || double.IsInfinity(key.x) || double.IsInfinity(key.z)) finite = false;
                if (key.x < minX) minX = key.x;
                if (key.x > maxX) maxX = key.x;
                if (key.z < minZ) minZ = key.z;
                if (key.z > maxZ) maxZ = key.z;
            }
            entry.InnerCount = chunk.innerChunks.Count;
            if (!finite)
            {
                // Never prove a chunk far when a cell coordinate is not finite.
                minX = minZ = double.NegativeInfinity;
                maxX = maxZ = double.PositiveInfinity;
            }
            entry.MinX = minX; entry.MaxX = maxX; entry.MinZ = minZ; entry.MaxZ = maxZ;
            entry.Empty = false;
        }

        internal static bool AllFar(double minX, double maxX, double minZ, double maxZ, IList<Vector3d> genCenters, float roadPlaceDist)
        {
            double limit = (double)roadPlaceDist + Margin;
            if (!(limit > 0) || double.IsNaN(limit)) return false;
            for (int i = 0; i < genCenters.Count; i++)
            {
                Vector3d c = genCenters[i];
                if (double.IsNaN(c.x) || double.IsNaN(c.z) || double.IsInfinity(c.x) || double.IsInfinity(c.z)) return false;
                double dx = c.x < minX ? minX - c.x : (c.x > maxX ? c.x - maxX : 0.0);
                double dz = c.z < minZ ? minZ - c.z : (c.z > maxZ ? c.z - maxZ : 0.0);
                if (!(Math.Sqrt(dx * dx + dz * dz) >= limit)) return false;
            }
            return true;
        }

        static bool AllFar(Entry entry, IList<Vector3d> genCenters, float roadPlaceDist)
        {
            return AllFar(entry.MinX, entry.MaxX, entry.MinZ, entry.MaxZ, genCenters, roadPlaceDist);
        }

        static bool NoLiveElements(roadGenScript.dotChunkClass chunk)
        {
            for (int i = 0; i < chunk.roads.Count; i++)
            {
                var road = chunk.roads[i];
                if (road == null || road.roadElements == null) return false;
                for (int j = 0; j < road.roadElements.Count; j++)
                {
                    var element = road.roadElements[j];
                    if (element == null) return false;
                    if (element.obj != null) return false;
                }
            }
            return true;
        }

        internal static string Summary()
        {
            return "roadFarSkip{skipped=" + Skipped + " walked=" + Walked + " deduped=" + Deduped + "}";
        }
    }
}
