namespace TldMp
{
    // Terrain-alignment helpers inside a POI prefab. Vanilla reads each helper's
    // localPosition, which is relative to its immediate parent. For a helper that
    // sits under a nested child (haz02 > Buszmegallo > HelpPos) that is an offset
    // from the child, not from the POI root, so the alignment lands on the root and
    // the ground under the nested building is never shaped. A helper whose parent
    // is the root (or that has no parent) keeps its exact stock value.
    internal static class TerrainAlignPolicy
    {
        internal static float LookupRange(float current, double dx, double dz, float radius)
        {
            return System.Math.Max(current, (float)(System.Math.Sqrt(dx * dx + dz * dz) + radius));
        }

        internal static bool LevelStartingBusMarker(string rootName, string parentName)
        {
            return rootName == "haz02" && parentName == "Buszmegallo";
        }

        internal static bool UsesRootRelative(bool hasParent, bool parentIsRoot)
        {
            return hasParent && !parentIsRoot;
        }
    }
}
