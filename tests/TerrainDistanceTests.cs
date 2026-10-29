using System;
using TldMp;

// Compile with runtime/TerrainDistancePolicy.cs. This directly exercises the
// production radius policy used before terrainGenScript.Restart/FStart.
public static class TerrainDistanceTests
{
    sealed class DrawSetting
    {
        public float Radius;
        public float TileSize;
        public int Resolution;
    }

    sealed class TerrainMock
    {
        public DrawSetting Min = new DrawSetting { Radius = 1000f, TileSize = 250f, Resolution = 64 };
        public DrawSetting Max = new DrawSetting { Radius = 25000f, TileSize = 1000f, Resolution = 256 };
        public DrawSetting SavedMin;
        public DrawSetting SavedMax;
        public readonly TerrainDistancePolicy.ClampState Clamp = new TerrainDistancePolicy.ClampState();
        public float LastDrawDistance;
        public float EffectiveRadius;
        public int Restarts;

        public void Upd(float requested)
        {
            var plan = TerrainDistancePolicy.Prepare(Min.Radius, Max.Radius, requested, LastDrawDistance, Clamp);
            if (plan.KeepCurrentClamp) { NativeUpd(requested); return; }
            if (plan.RestoreOriginal)
            {
                Min = SavedMin;
                Max = SavedMax;
                SavedMin = null;
                SavedMax = null;
            }
            if (plan.Clamp)
            {
                if (SavedMin == null) SavedMin = new DrawSetting { Radius = Min.Radius, TileSize = Min.TileSize, Resolution = Min.Resolution };
                if (SavedMax == null) SavedMax = new DrawSetting { Radius = Max.Radius, TileSize = Max.TileSize, Resolution = Max.Resolution };
                Min = new DrawSetting { Radius = plan.Min, TileSize = Min.TileSize, Resolution = Min.Resolution };
                Max = new DrawSetting { Radius = plan.Max, TileSize = Max.TileSize, Resolution = Max.Resolution };
            }
            if (plan.ForceRestart) LastDrawDistance = requested - 1f;
            NativeUpd(requested);
        }

        void NativeUpd(float requested)
        {
            if (LastDrawDistance != requested)
            {
                Restarts++;
                EffectiveRadius = TerrainDistancePolicy.EffectiveRadius(Min.Radius, Max.Radius,
                    TerrainDistancePolicy.SliderLerp(requested));
                LastDrawDistance = requested;
            }
        }
    }

    static void Equal(float expected, float actual, string message)
    {
        if (Math.Abs(expected - actual) > .001f) throw new Exception(message + ": " + actual + " != " + expected);
    }
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    public static int Main()
    {
        try
        {
            Equal(0f, TerrainDistancePolicy.SliderLerp(0f), "slider lower clamp");
            Equal(0f, TerrainDistancePolicy.SliderLerp(500f), "native slider minimum");
            Equal(.5f, TerrainDistancePolicy.SliderLerp(12750f), "native slider midpoint");
            Equal(1f, TerrainDistancePolicy.SliderLerp(25000f), "native slider maximum");
            Equal(1f, TerrainDistancePolicy.SliderLerp(30000f), "slider upper clamp");

            Equal(1000f, TerrainDistancePolicy.EffectiveRadius(1000f, 10000f, 0f), "minimum radius");
            Equal(5500f, TerrainDistancePolicy.EffectiveRadius(1000f, 10000f, .5f), "interpolated radius");
            Equal(10000f, TerrainDistancePolicy.EffectiveRadius(1000f, 10000f, 1f), "maximum radius");
            Equal(250f, TerrainDistancePolicy.EffectiveRadius(200f, 301f, .5f), "Unity midpoint rounding to even");
            Check(!TerrainDistancePolicy.NeedsClamp(1000f, 1000f), "already-correct terrain should not rebuild");
            Check(!TerrainDistancePolicy.NeedsClamp(1000f, 1000.4f), "radius below fractional horizon should not rebuild");
            Check(TerrainDistancePolicy.NeedsClamp(1001f, 1000f), "oversized native radius must be capped");

            // Replay the runtime's pre-Upd decision and native Restart boundary.
            // The requested range must be installed before each single restart;
            // an unchanged setting must not invalidate again.
            var scene = new TerrainMock();
            scene.Upd(1000f);
            Equal(1f, scene.Restarts, "low setting should cause exactly one startup correction restart");
            Equal(1000f, scene.EffectiveRadius, "low setting effective scene radius");
            Equal(1000f, scene.Min.Radius, "low setting min endpoint");
            Equal(1000f, scene.Max.Radius, "low setting max endpoint");

            scene.Upd(8000f);
            Equal(2f, scene.Restarts, "raising the setting should restart once");
            Equal(8000f, scene.EffectiveRadius, "raised setting effective scene radius");
            scene.Upd(8000f);
            Equal(2f, scene.Restarts, "same setting must not restart");

            scene.Upd(3000f);
            Equal(3f, scene.Restarts, "lowering the setting should restart once");
            Equal(3000f, scene.EffectiveRadius, "lowered setting effective scene radius");
            Equal(250f, scene.Min.TileSize, "min tile size must remain authored");
            Equal(1000f, scene.Max.TileSize, "max tile size must remain authored");
            Equal(64f, scene.Min.Resolution, "min resolution must remain authored");
            Equal(256f, scene.Max.Resolution, "max resolution must remain authored");

            Console.WriteLine("PASS: native slider policy and mocked low/high/same/lower terrain restart transitions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
