using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Shared by the synchronous tile path and TerrainTileMesh.  The caller owns
    // the persisted generation-policy decision and supplies its clearance.
    public static class RoadTerrainClearance
    {
        public static double Height(terrainGenSettings setting, Vector3d position,
            List<roadGenScript.roadClass> roads, List<poiGenScript.poiClass> pois,
            bool enabled, double clearance)
        {
            if (!enabled || roads == null || roads.Count == 0)
                return setting.Height(position, roads, pois);
            if (double.IsNaN(clearance) || double.IsInfinity(clearance) || clearance < 0.0)
                throw new ArgumentOutOfRangeException("clearance");

            // Preserve native noise and POI ordering. Apply the native road blend
            // ourselves so the exact centre line is aligned as well.
            double height = setting.Height(position, (List<roadGenScript.roadClass>)null, pois);
            Vector3d nearest;
            roadGenScript.roadClass road = roadGenScript.NearestPointOnRoadBone(position, roads, out nearest);
            if (road == null) return height;
            Vector3d horizontal = nearest;
            horizontal.y = position.y;
            double distance = (horizontal - position).magnitude;
            if (distance == double.MaxValue || double.IsNaN(distance)) return height;
            double weight = Mathd.InverseLerp(-1.0, 1.0, Mathd.Sin(Mathd.Lerp(-90.0, 90.0,
                Mathd.InverseLerp(road.terrainAlignMaxDist, road.terrainAlignMinDist, distance)) * 0.01745329));
            return Mathd.Lerp(height, nearest.y - clearance, weight);
        }

        // Run on the main thread: RoadsNear may generate roads. Include the
        // normal-sampling border, and cover every inner/outer chunk intersected
        // by the tile. Centre first retains native tie ordering where possible.
        public static List<roadGenScript.roadClass> RoadsNearTile(Vector3d centre, float tileSize, float vertexDist)
        {
            var result = new List<roadGenScript.roadClass>();
            var seen = new HashSet<roadGenScript.roadClass>();
            Add(result, seen, mapSettingScript.RoadsNear(centre));
            double extent = Math.Abs((double)tileSize) * 0.5 + Math.Abs((double)vertexDist);
            double step = extent * 2.0;
            var generators = menuhandler.s.currentMainMap.roadGens;
            if (generators != null)
                foreach (roadGenScript generator in generators)
                {
                    if (generator.innerChunkSize > 0 && generator.innerChunkSize < step) step = generator.innerChunkSize;
                    if (generator.chunkSize > 0 && generator.chunkSize < step) step = generator.chunkSize;
                }
            if (!(extent > 0) || !(step > 0) || double.IsInfinity(extent) || double.IsInfinity(step)) return result;
            // Distant tiles can be tens of kilometres across. Do not issue a
            // quadratic number of native lookups: their road geometry is already
            // indexed in loaded chunks, and only generated roads have bones.
            if (2.0 * extent / step > 8.0)
            {
                if (generators != null)
                    foreach (roadGenScript generator in generators)
                        foreach (var chunk in generator.chunks.Values)
                            if (chunk != null && chunk.roads != null)
                                foreach (roadGenScript.roadClass road in chunk.roads)
                                    if (road != null && road.generated && seen.Add(road)) result.Add(road);
                return result;
            }
            int count = (int)Math.Ceiling(2.0 * extent / step);
            if (count < 1) count = 1;
            for (int z = 0; z <= count; z++)
                for (int x = 0; x <= count; x++)
                {
                    var sample = new Vector3d(centre.x - extent + 2.0 * extent * x / count,
                        centre.y, centre.z - extent + 2.0 * extent * z / count);
                    Add(result, seen, mapSettingScript.RoadsNear(sample));
                }
            return result;
        }

        static void Add(List<roadGenScript.roadClass> result, HashSet<roadGenScript.roadClass> seen,
            List<roadGenScript.roadClass> roads)
        {
            if (roads == null) return;
            foreach (roadGenScript.roadClass road in roads)
                if (road != null && seen.Add(road)) result.Add(road);
        }
    }
}
