using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Replaces terrainGenScript.CheckRemove only where its input collections are
    // owned by the synchronous main-thread path. True asks the native method to run.
    public static class TerrainMembership
    {
        public static bool CheckRemove(terrainGenScript terrain)
        {
            if (ReferenceEquals(terrain, null) || terrain.terrains == null || terrain.coordsNeeded == null ||
                terrain.coordsToRemove == null ||
                ReferenceEquals(terrain.coordsNeeded, terrain.coordsToRemove) ||
                (!ReferenceEquals(terrain.mapSetting, null) && terrain.mapSetting.useAsync2))
                return true;

            // List<Vector3d>.Contains uses Vector3d.Equals, not Vector3d.operator==.
            // Default EqualityComparer<Vector3d> keeps that exact equality contract,
            // including double.Equals behavior for NaN and signed zero.
            List<Vector3d> removals = terrain.coordsToRemove;
            removals.Clear();
            var needed = new HashSet<Vector3d>(terrain.coordsNeeded);
            foreach (KeyValuePair<Vector3d, newTerrainTileScript> entry in terrain.terrains)
            {
                if (!needed.Contains(entry.Key)) removals.Add(entry.Key);
            }
            return false;
        }
    }
}
