using System;
using TldMp;

internal static class DrunkPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }
    static bool Near(float a, float b, float tolerance) { return Math.Abs(a - b) < tolerance; }

    public static int Main(string[] args)
    {
        Gain();
        Decay();
        Sober();
        Sway();
        Fov();
        Option();
        Console.WriteLine("PASS: " + checks + " drunk policy assertions; alcohol adds a liter-scaled level clamped to 0..3, one level wears off per five minutes (three levels in fifteen), milk sobers at two levels per liter (half a liter removes one), sway grows as l + l*l degrees up to 12 and speeds up with the level, angles are bounded by their amplitude (roll by 0.95 of it), never go quiet at max level, and are exactly zero when sober, the field of view breathes up to 6 degrees with the level, and the drunk-effects option defaults on and round-trips.");
        return 0;
    }

    static void Gain()
    {
        Check(Near(DrunkPolicy.Add(0f, 0.33f), 0.33f), "a can's worth gives a mild level");
        Check(Near(DrunkPolicy.Add(0.2f, 0f), 0.2f), "zero alcohol changes nothing");
        Check(Near(DrunkPolicy.Add(0.2f, -1f), 0.2f), "negative alcohol changes nothing");
        Check(Near(DrunkPolicy.Add(0.2f, float.NaN), 0.2f), "NaN alcohol changes nothing");
        Check(Near(DrunkPolicy.Add(0.2f, float.PositiveInfinity), 0.2f), "infinite alcohol changes nothing");
        Check(Near(DrunkPolicy.Add(2.9f, 5f), DrunkPolicy.MaxLevel), "heavy drinking caps at the max level");
        Check(Near(DrunkPolicy.Add(1f, 1f), 2f), "a liter adds one level");
        Check(Near(DrunkPolicy.Add(float.NaN, 0.5f), 0.5f), "a corrupt level is treated as sober");
        Check(Near(DrunkPolicy.Clamp(3.7f), 3f) && Near(DrunkPolicy.Clamp(-0.3f), 0f), "levels clamp to 0..3");
        Check(Near(DrunkPolicy.Clamp(2.5f), 2.5f), "a level inside the range is kept");
        Check(Near(DrunkPolicy.Clamp(float.NaN), 0f), "a NaN level clamps to zero");
        Check(DrunkPolicy.MaxLevel == 3f && DrunkPolicy.GainPerLiter == 1f, "max level is 3 and a liter adds one level");
    }

    static void Decay()
    {
        float level = 3f;
        for (int i = 0; i < 300; i++) level = DrunkPolicy.Decay(level, 1f);
        Check(Near(level, 2f, 0.001f), "one level wears off in 300 seconds");
        Check(Near(DrunkPolicy.Decay(3f, 300f), 3f - 1f / DrunkPolicy.DecaySeconds), "one call is capped at one second");
        Check(Near(DrunkPolicy.Decay(3f, 1f), 3f - 1f / DrunkPolicy.DecaySeconds), "one second removes 1/300 of a level");
        Check(DrunkPolicy.Decay(0.001f, 1f) == 0f, "decay never goes below zero");
        Check(Near(DrunkPolicy.Decay(2f, 0f), 2f) && Near(DrunkPolicy.Decay(2f, -2f), 2f), "no time passing changes nothing");
        Check(Near(DrunkPolicy.Decay(2f, float.NaN), 2f), "NaN time changes nothing");
        level = 1f;
        for (int i = 0; i < 300; i++) level = DrunkPolicy.Decay(level, 1f);
        Check(Near(level, 0f, 0.001f), "one liter is gone after five minutes");
        level = DrunkPolicy.MaxLevel;
        for (int i = 0; i < 900; i++) level = DrunkPolicy.Decay(level, 1f);
        Check(Near(level, 0f, 0.001f), "a full three levels is gone after fifteen minutes");
    }

    static void Sober()
    {
        Check(Near(DrunkPolicy.Sober(2f, 0.5f), 1f), "half a liter of milk removes one level");
        Check(Near(DrunkPolicy.Sober(1f, 0.25f), 0.5f), "a quarter liter removes half a level");
        Check(Near(DrunkPolicy.Sober(0.5f, 5f), 0f), "milk clamps at sober");
        Check(Near(DrunkPolicy.Sober(0f, 1f), 0f), "milk on a sober player changes nothing");
        Check(Near(DrunkPolicy.Sober(3f, 0f), 3f), "zero milk changes nothing");
        Check(Near(DrunkPolicy.Sober(2f, -1f), 2f), "negative milk changes nothing");
        Check(Near(DrunkPolicy.Sober(2f, float.NaN), 2f), "NaN milk changes nothing");
        Check(Near(DrunkPolicy.Sober(2f, float.PositiveInfinity), 2f), "infinite milk changes nothing");
        Check(Near(DrunkPolicy.Sober(float.NaN, 0.5f), 0f), "a corrupt level sobers to zero");
        Check(DrunkPolicy.MilkSoberPerLiter == 2f, "a liter of milk removes two levels");
    }

    static void Sway()
    {
        Check(Near(DrunkPolicy.SwayAmplitude(0f), 0f), "sober means no sway");
        Check(Near(DrunkPolicy.SwayAmplitude(0.5f), 0.75f), "half a level sways 0.75 degrees");
        Check(Near(DrunkPolicy.SwayAmplitude(1f), 2f), "one level sways 2 degrees");
        Check(Near(DrunkPolicy.SwayAmplitude(2f), 6f), "two levels sway 6 degrees");
        Check(Near(DrunkPolicy.SwayAmplitude(3f), 12f), "three levels sway 12 degrees");
        Check(Near(DrunkPolicy.SwayAmplitude(5f), 12f), "sway is capped at the maximum");
        Check(Near(DrunkPolicy.SwayAmplitude(float.NaN), 0f), "a NaN level has no sway");
        float prev = -1f;
        for (int i = 0; i <= 30; i++)
        {
            float amplitude = DrunkPolicy.SwayAmplitude(i * 0.1f);
            Check(amplitude > prev, "sway grows strictly with the level");
            prev = amplitude;
        }
        Check(Near(DrunkPolicy.SwaySpeed(0f), 1f), "sober sway runs at 1x");
        Check(Near(DrunkPolicy.SwaySpeed(3f), 1.75f), "max level sway runs at 1.75x");
        Check(Near(DrunkPolicy.SwaySpeed(1f), 1.25f), "one level sways at 1.25x");
        Check(Near(DrunkPolicy.SwaySpeed(float.NaN), 1f), "a NaN level runs at 1x");
        float pitch, yaw, roll;
        DrunkPolicy.Angles(0f, 12.5f, out pitch, out yaw, out roll);
        Check(pitch == 0f && yaw == 0f && roll == 0f, "sober angles are exactly zero");
        float maxSeen = 0f;
        for (int i = 0; i < 4000; i++)
        {
            DrunkPolicy.Angles(3f, i * 0.05f, out pitch, out yaw, out roll);
            Check(Math.Abs(pitch) <= 12f + 0.0001f, "pitch stays within its amplitude");
            Check(Math.Abs(yaw) <= 12f + 0.0001f, "yaw stays within its amplitude");
            Check(Math.Abs(roll) <= 0.95f * 12f + 0.0001f, "roll stays within 0.95 of its amplitude");
            if (Math.Abs(pitch) > maxSeen) maxSeen = Math.Abs(pitch);
        }
        Check(maxSeen > 6f, "the sway actually moves");
        DrunkPolicy.Angles(2f, 3.25f, out pitch, out yaw, out roll);
        float p2, y2, r2;
        DrunkPolicy.Angles(2f, 3.25f, out p2, out y2, out r2);
        Check(pitch == p2 && yaw == y2 && roll == r2, "the same time gives the same sway");
        DrunkPolicy.Angles(2f, float.NaN, out pitch, out yaw, out roll);
        Check(pitch == 0f && yaw == 0f && roll == 0f, "a NaN time gives no sway");

        // Never quiet: at max level, every 2-second window of 0..120 s (sampled every
        // 0.05 s) must reach past 0.4 of the amplitude on pitch or yaw.
        const int step = 1; // sample index, 0.05 s each
        const int samples = 2401; // 0..120 s inclusive
        const int window = 41; // 2.0 s inclusive
        float limit = 0.4f * 12f;
        var peak = new float[samples];
        for (int i = 0; i < samples; i += step)
        {
            DrunkPolicy.Angles(3f, i * 0.05f, out pitch, out yaw, out roll);
            peak[i] = Math.Max(Math.Abs(pitch), Math.Abs(yaw));
        }
        for (int start = 0; start + window <= samples; start++)
        {
            float best = 0f;
            for (int j = start; j < start + window; j++) if (peak[j] > best) best = peak[j];
            Check(best > limit, "the sway is never quiet for a full two seconds at max level");
        }
    }

    static void Fov()
    {
        Check(Near(DrunkPolicy.MaxFovPulse, 6f), "the field of view breathes up to 6 degrees");
        Check(DrunkPolicy.FovAmplitude(0f) == 0f, "sober field of view does not change");
        Check(DrunkPolicy.FovAmplitude(float.NaN) == 0f, "a NaN level leaves the field of view alone");
        Check(Near(DrunkPolicy.FovAmplitude(3f), DrunkPolicy.MaxFovPulse), "max level reaches the full pulse");
        Check(Near(DrunkPolicy.FovAmplitude(9f), DrunkPolicy.MaxFovPulse), "the pulse is capped at max level");
        float prev = -1f;
        for (int i = 0; i <= 30; i++)
        {
            float amplitude = DrunkPolicy.FovAmplitude(i * 0.1f);
            Check(amplitude > prev, "the field of view pulse grows strictly with the level");
            prev = amplitude;
        }
        for (int i = 0; i < 2000; i++)
        {
            float time = i * 0.05f;
            Check(DrunkPolicy.FovOffset(0f, time) == 0f, "sober field of view offset is exactly zero");
            float offset = DrunkPolicy.FovOffset(3f, time);
            Check(Math.Abs(offset) <= DrunkPolicy.FovAmplitude(3f) + 0.0001f, "field of view offset stays within its amplitude");
            Check(Math.Abs(offset) <= DrunkPolicy.MaxFovPulse + 0.0001f, "field of view offset stays within the max pulse");
        }
        Check(DrunkPolicy.FovOffset(3f, float.NaN) == 0f, "a NaN time gives no field of view offset");
        Check(DrunkPolicy.FovOffset(3f, float.PositiveInfinity) == 0f, "an infinite time gives no field of view offset");
    }

    static void Option()
    {
        PatchOptions.ResetToDefaults();
        Check(PatchOptions.DrunkEffects, "drunk effects default on");
        Check(PatchOptions.Format().Contains("drunkeffects=on\n"), "the default is saved as on");
        PatchOptions.Parse("drunkeffects=off\n");
        Check(!PatchOptions.DrunkEffects, "drunk effects can be turned off");
        string saved = PatchOptions.Format();
        Check(saved.Contains("drunkeffects=off\n"), "the off choice is saved");
        PatchOptions.Parse(saved);
        Check(!PatchOptions.DrunkEffects, "the off choice round-trips");
        PatchOptions.Parse("drunkeffects=maybe\n");
        Check(PatchOptions.DrunkEffects, "a bad drunk-effects value keeps the default");
        PatchOptions.Parse("preset=performance\n");
        Check(PatchOptions.DrunkEffects, "a file without the key keeps drunk effects on");
        PatchOptions.ResetToDefaults();
    }
}
