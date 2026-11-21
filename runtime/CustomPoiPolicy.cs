using System;

namespace TldMp
{
    // Pure decisions for CustomPois (no Unity objects, unit-tested).
    // Worlds created with placement version 6 or later (Roadworthy setting "New buildings") swap a share of
    // roadside POIs for Roadworthy's own buildings. The choice depends only on the
    // world seed, the POI position and the vanilla type, so every peer and every
    // reload picks the same building. Changing anything here changes version-6
    // worlds: add a new placement version instead.
    public static class CustomPoiPolicy
    {
        public const int MinPlacementVersion = 6;
        // Share of roadside POIs that become a Roadworthy building: about a third, so the
        // eight new buildings turn up regularly and vanilla ones stay the majority.
        public const double Chance = 0.35;
        // Road half-width plus a verge, added to each building's front extent.
        public const double RoadMargin = 5.5;

        // Any random roadside POI may be swapped, major ones included. Only the start
        // house (placed separately anyway) and unknown types are left alone.
        static readonly string[] Kept = { "error", "kezdohaz" };

        public static bool IsReplaceable(string vanillaName)
        {
            return vanillaName != null && Array.IndexOf(Kept, vanillaName) < 0;
        }

        // Index of the building to use, or -1 to keep the vanilla POI. The index depends on
        // buildingCount, so adding a building reshuffles version-6 worlds.
        public static int Pick(int placementVersion, int worldSeed, double x, double z, string vanillaName, bool alignedToRoad, int buildingCount)
        {
            if (placementVersion < MinPlacementVersion || !alignedToRoad || buildingCount <= 0 || !IsReplaceable(vanillaName)) return -1;
            if (double.IsNaN(x) || double.IsNaN(z) || double.IsInfinity(x) || double.IsInfinity(z)) return -1;
            ulong h = Hash(worldSeed, (long)Math.Round(x), (long)Math.Round(z));
            if ((h & 0xFFFF) / 65536.0 >= Chance) return -1;
            return (int)((h >> 16) % (ulong)buildingCount);
        }

        // Layers the game uses for collision-only or special objects (TagManager): ray 9,
        // driver 10, wheel 11, player 12, interior 13, defsens 14, pickedUp 20, tankcap 22,
        // playerdeath 27. Buildings never go on those or on built-in invisible layers.
        static readonly int[] HiddenLayers = { 1, 2, 4, 5, 8, 9, 10, 11, 12, 13, 14, 20, 21, 22, 25, 26, 27, 28 };

        // The layer to draw and collide a building on, given the vanilla house's wall layer.
        public static int VisibleLayer(int vanillaRenderLayer)
        {
            if (vanillaRenderLayer < 0 || vanillaRenderLayer > 31 || Array.IndexOf(HiddenLayers, vanillaRenderLayer) >= 0) return 0;
            return vanillaRenderLayer;
        }

        // Extra distance to move a building away from the road so its front clears it.
        public static double PushOut(double distanceToRoad, double frontExtent)
        {
            if (double.IsNaN(distanceToRoad) || double.IsNaN(frontExtent)) return 0;
            double need = Math.Max(0, frontExtent) + RoadMargin;
            return distanceToRoad < need ? need - Math.Max(0, distanceToRoad) : 0;
        }

        // Stable 64-bit mix (splitmix64); GetHashCode is not stable across runtimes.
        public static ulong Hash(int seed, long x, long z)
        {
            ulong h = 0x9E3779B97F4A7C15UL ^ (ulong)(uint)seed;
            h = Mix(h ^ (ulong)x);
            h = Mix(h ^ (ulong)z);
            return Mix(h ^ 0x5267574F52544859UL);
        }

        static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
