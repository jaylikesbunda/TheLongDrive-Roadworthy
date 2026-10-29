using System;
using TldMp;

internal static class PatchOptionsTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        Defaults();
        Tables();
        RoundTrip();
        Corrupt();
        Interplay();
        Version();
        Console.WriteLine("PASS: " + checks + " graphics option/version assertions.");
        return 0;
    }

    static void Defaults()
    {
        PatchOptions.ResetToDefaults();
        Check(PatchOptions.Preset == GraphicsPreset.Balanced, "the default preset is Balanced");
        Check(PatchOptions.Shadow == ShadowLevel.Medium, "the default shadow level is Medium");
        Check(PatchOptions.SmoothCars, "car smoothing defaults on");
        PresetValues balanced = PatchOptions.Values(GraphicsPreset.Balanced);
        Check(balanced.Applies && balanced.AntiAliasing == 2 && balanced.PixelLightCount == 4 &&
            Math.Abs(balanced.ShadowDistance - 80f) < 0.01f && balanced.ShadowCascades == 2 &&
            !balanced.RealtimeReflectionProbes, "Balanced keeps today's values");
    }

    static void Tables()
    {
        Check(!PatchOptions.Values(GraphicsPreset.Vanilla).Applies, "Vanilla applies nothing");
        PresetValues performance = PatchOptions.Values(GraphicsPreset.Performance);
        Check(performance.AntiAliasing == 0 && performance.PixelLightCount == 2 && performance.ShadowCascades == 1 &&
            Math.Abs(performance.ShadowDistance - 50f) < 0.01f && performance.MirrorOptimizations, "Performance values match the contract");
        Check(PatchOptions.Values(GraphicsPreset.Custom).Applies, "Custom applies a preset base");
        Check(PatchOptions.DefaultShadow(GraphicsPreset.Performance) == ShadowLevel.Low, "Performance defaults to Low shadows");
        Check(PatchOptions.DefaultShadow(GraphicsPreset.Balanced) == ShadowLevel.Medium, "Balanced defaults to Medium shadows");
        Check(PatchOptions.DefaultShadow(GraphicsPreset.Vanilla) == ShadowLevel.Off, "Vanilla defaults to Off shadows");

        ShadowValues off = PatchOptions.ShadowValuesFor(ShadowLevel.Off);
        ShadowValues low = PatchOptions.ShadowValuesFor(ShadowLevel.Low);
        ShadowValues medium = PatchOptions.ShadowValuesFor(ShadowLevel.Medium);
        ShadowValues high = PatchOptions.ShadowValuesFor(ShadowLevel.High);
        Check(off.Shadows == 0 && off.ShadowDistance == 0f && off.ShadowCascades == 1, "Off disables shadows");
        Check(low.Shadows == 1 && low.ShadowDistance == 40f && low.ShadowCascades == 1, "Low maps to 40 m and one cascade");
        Check(medium.Shadows == 2 && medium.ShadowDistance == 80f && medium.ShadowCascades == 2, "Medium maps to 80 m and two cascades");
        Check(high.Shadows == 2 && high.ShadowDistance == 150f && high.ShadowCascades == 4, "High maps to 150 m and four cascades");
        Check(off.ShadowResolution == 0 && low.ShadowResolution == 0 && medium.ShadowResolution == 1 && high.ShadowResolution == 2,
            "shadow resolution follows the level");
    }

    static void RoundTrip()
    {
        PatchOptions.Parse("preset=performance\nshadow=high\n");
        Check(PatchOptions.Preset == GraphicsPreset.Performance && PatchOptions.Shadow == ShadowLevel.High, "keys parse case-insensitively");
        string text = PatchOptions.Format();
        PatchOptions.Parse(text);
        Check(PatchOptions.Preset == GraphicsPreset.Performance && PatchOptions.Shadow == ShadowLevel.High, "format/parse round-trips");
        PatchOptions.Parse("# comment\r\n\r\npreset = custom\r\nshadow = off\r\n");
        Check(PatchOptions.Preset == GraphicsPreset.Custom && PatchOptions.Shadow == ShadowLevel.Off, "comments and spacing are tolerated");
        PatchOptions.Parse("preset=custom");
        Check(PatchOptions.Preset == GraphicsPreset.Custom && PatchOptions.Shadow == ShadowLevel.Medium, "a missing shadow keeps the default");
        PatchOptions.Parse("smoothcars=off");
        Check(!PatchOptions.SmoothCars, "car smoothing can be disabled in the options file");
        PatchOptions.Parse("smoothcars=on");
        Check(PatchOptions.SmoothCars, "car smoothing can be enabled in the options file");
    }

    static void Corrupt()
    {
        PatchOptions.Parse(null);
        Check(PatchOptions.Preset == GraphicsPreset.Balanced && PatchOptions.Shadow == ShadowLevel.Medium, "missing text gives defaults");
        PatchOptions.Parse("preset=nonsense\nshadow=impossible\nnoise\n=");
        Check(PatchOptions.Preset == GraphicsPreset.Balanced && PatchOptions.Shadow == ShadowLevel.Medium, "unknown values give defaults");
        PatchOptions.Parse("shadow=HIGH\npreset=PERFORMANCE");
        Check(PatchOptions.Preset == GraphicsPreset.Performance && PatchOptions.Shadow == ShadowLevel.High, "values are case-insensitive");
    }

    static void Interplay()
    {
        PatchOptions.ResetToDefaults();
        PatchOptions.SelectShadow(ShadowLevel.High);
        Check(PatchOptions.Preset == GraphicsPreset.Custom && PatchOptions.Shadow == ShadowLevel.High, "changing shadow selects Custom");
        PatchOptions.SelectPreset(GraphicsPreset.Performance);
        Check(PatchOptions.Preset == GraphicsPreset.Performance && PatchOptions.Shadow == ShadowLevel.Low, "changing preset installs its default shadow");
        PatchOptions.SelectPreset(GraphicsPreset.Balanced);
        Check(PatchOptions.Shadow == ShadowLevel.Medium, "Balanced restores Medium shadows");
    }

    static void Version()
    {
        Check(PatchInfo.Display == "v0.1.0 Roadworthy ", "the label text is v0.1.0 Roadworthy");
        Check(PatchInfo.Version == "0.1.0" && PatchInfo.Name == "Roadworthy", "version and name are split for reuse");
    }
}
