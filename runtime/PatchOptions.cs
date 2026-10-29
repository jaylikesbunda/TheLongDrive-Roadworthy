using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TldMp
{
    internal enum GraphicsPreset
    {
        Vanilla = 0,
        Balanced = 1,
        Performance = 2,
        Custom = 3
    }

    internal enum ShadowLevel
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    // Values a preset writes. Vanilla applies nothing at all.
    internal sealed class PresetValues
    {
        internal string Name;
        internal bool Applies;
        internal int AntiAliasing;
        internal int PixelLightCount;
        internal float ShadowDistance;
        internal int ShadowCascades;
        internal bool RealtimeReflectionProbes;
        internal bool MirrorOptimizations;
        internal ShadowLevel DefaultShadow;
    }

    // Values a shadow level writes. Shadows uses UnityEngine.ShadowQuality:
    // 0 = Disable, 1 = HardOnly, 2 = All. Resolution uses UnityEngine.ShadowResolution:
    // 0 = Low, 1 = Medium, 2 = High, 3 = VeryHigh.
    internal sealed class ShadowValues
    {
        internal string Name;
        internal int Shadows;
        internal float ShadowDistance;
        internal int ShadowCascades;
        internal int ShadowResolution;
    }

    // Roadworthy graphics options, stored outside settingsscript.S so the game's
    // serialized settings keep their exact shape. A missing or corrupt file falls
    // back to Balanced (today's values) and Medium shadows.
    internal static class PatchOptions
    {
        internal const string FileName = "roadworthy-options.txt";
        internal const string PresetKey = "preset";
        internal const string ShadowKey = "shadow";
        internal const string SmoothKey = "smoothcars";
        internal const string RabbitsKey = "rabbits";
        internal const string SandstormsKey = "sandstorms";
        internal const string HighRateKey = "highrate";

        internal static readonly PresetValues[] Presets =
        {
            new PresetValues { Name = "Vanilla", Applies = false, DefaultShadow = ShadowLevel.Off },
            new PresetValues
            {
                Name = "Balanced", Applies = true, AntiAliasing = 2, PixelLightCount = 4,
                ShadowDistance = 80f, ShadowCascades = 2, RealtimeReflectionProbes = false,
                MirrorOptimizations = false, DefaultShadow = ShadowLevel.Medium
            },
            new PresetValues
            {
                Name = "Performance", Applies = true, AntiAliasing = 0, PixelLightCount = 2,
                ShadowDistance = 50f, ShadowCascades = 1, RealtimeReflectionProbes = false,
                MirrorOptimizations = true, DefaultShadow = ShadowLevel.Low
            },
            new PresetValues
            {
                Name = "Custom", Applies = true, AntiAliasing = 2, PixelLightCount = 4,
                ShadowDistance = 80f, ShadowCascades = 2, RealtimeReflectionProbes = false,
                MirrorOptimizations = false, DefaultShadow = ShadowLevel.Medium
            }
        };

        internal static readonly ShadowValues[] Shadows =
        {
            new ShadowValues { Name = "Off", Shadows = 0, ShadowDistance = 0f, ShadowCascades = 1, ShadowResolution = 0 },
            new ShadowValues { Name = "Low", Shadows = 1, ShadowDistance = 40f, ShadowCascades = 1, ShadowResolution = 0 },
            new ShadowValues { Name = "Medium", Shadows = 2, ShadowDistance = 80f, ShadowCascades = 2, ShadowResolution = 1 },
            new ShadowValues { Name = "High", Shadows = 2, ShadowDistance = 150f, ShadowCascades = 4, ShadowResolution = 2 }
        };

        internal static GraphicsPreset Preset = GraphicsPreset.Balanced;
        internal static ShadowLevel Shadow = ShadowLevel.Medium;
        internal static bool SmoothCars = true;
        // Vanilla ships both disabled (rabbit spawner never started, no sandstorm
        // spawner in the scene); Roadworthy turns them on unless switched off here.
        internal static bool Rabbits = true;
        internal static bool Sandstorms = true;
        // Multiplayer: players 30 Hz and passenger cars up to 60 Hz (off = 20 Hz cars,
        // native player rate).
        internal static bool HighRate = true;
        static bool loaded;

        internal static string[] PresetNames
        {
            get { return new string[] { Presets[0].Name, Presets[1].Name, Presets[2].Name, Presets[3].Name }; }
        }

        internal static string[] ShadowNames
        {
            get { return new string[] { Shadows[0].Name, Shadows[1].Name, Shadows[2].Name, Shadows[3].Name }; }
        }

        internal static PresetValues Values(GraphicsPreset preset)
        {
            int index = (int)preset;
            if (index < 0 || index >= Presets.Length) index = (int)GraphicsPreset.Balanced;
            return Presets[index];
        }

        internal static ShadowValues ShadowValuesFor(ShadowLevel level)
        {
            int index = (int)level;
            if (index < 0 || index >= Shadows.Length) index = (int)ShadowLevel.Medium;
            return Shadows[index];
        }

        internal static ShadowLevel DefaultShadow(GraphicsPreset preset)
        {
            return Values(preset).DefaultShadow;
        }

        // Changing the shadow level makes the preset Custom; changing the preset
        // installs that preset's default shadow level. Both only change the stored
        // choice, never the game's own draw distance, grass or mirror toggles.
        internal static void SelectPreset(GraphicsPreset preset)
        {
            Preset = preset;
            Shadow = DefaultShadow(preset);
        }

        internal static void SelectShadow(ShadowLevel level)
        {
            Shadow = level;
            Preset = GraphicsPreset.Custom;
        }

        internal static void ResetToDefaults()
        {
            Preset = GraphicsPreset.Balanced;
            Shadow = ShadowLevel.Medium;
            SmoothCars = true;
            Rabbits = true;
            Sandstorms = true;
            HighRate = true;
            loaded = true;
        }

        internal static string DefaultPath()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (String.IsNullOrEmpty(documents))
            {
                string profile = Environment.GetEnvironmentVariable("USERPROFILE");
                if (String.IsNullOrEmpty(profile)) return null;
                documents = Path.Combine(profile, "Documents");
            }
            return Path.Combine(Path.Combine(documents, "TheLongDrive"), FileName);
        }

        internal static void Load()
        {
            if (loaded) return;
            loaded = true;
            ResetToDefaults();
            string path = null;
            string text = null;
            try
            {
                path = DefaultPath();
                if (path != null && File.Exists(path)) text = File.ReadAllText(path);
            }
            catch (Exception)
            {
                text = null;
            }
            Parse(text);
        }

        // Reload from disk on the next ApplyGraphicsPreset call.
        internal static void Invalidate()
        {
            loaded = false;
        }

        internal static string Format()
        {
            var text = new StringBuilder();
            text.Append(PresetKey).Append('=').Append(PresetName(Preset)).Append('\n');
            text.Append(ShadowKey).Append('=').Append(ShadowName(Shadow)).Append('\n');
            text.Append(SmoothKey).Append('=').Append(SmoothCars ? "on" : "off").Append('\n');
            text.Append(RabbitsKey).Append('=').Append(Rabbits ? "on" : "off").Append('\n');
            text.Append(SandstormsKey).Append('=').Append(Sandstorms ? "on" : "off").Append('\n');
            text.Append(HighRateKey).Append('=').Append(HighRate ? "on" : "off").Append('\n');
            return text.ToString();
        }

        internal static string PresetName(GraphicsPreset preset)
        {
            return Values(preset).Name.ToLowerInvariant();
        }

        internal static string ShadowName(ShadowLevel level)
        {
            return ShadowValuesFor(level).Name.ToLowerInvariant();
        }

        internal static void Parse(string text)
        {
            Preset = GraphicsPreset.Balanced;
            Shadow = ShadowLevel.Medium;
            SmoothCars = true;
            Rabbits = true;
            Sandstorms = true;
            HighRate = true;
            if (String.IsNullOrEmpty(text)) return;
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals).Trim().ToLowerInvariant();
                string value = line.Substring(equals + 1).Trim().ToLowerInvariant();
                if (key == PresetKey)
                {
                    GraphicsPreset preset;
                    if (TryPreset(value, out preset)) Preset = preset;
                }
                else if (key == ShadowKey)
                {
                    ShadowLevel level;
                    if (TryShadow(value, out level)) Shadow = level;
                }
                else if (key == SmoothKey)
                {
                    if (value == "on" || value == "true" || value == "1") SmoothCars = true;
                    else if (value == "off" || value == "false" || value == "0") SmoothCars = false;
                }
                else if (key == RabbitsKey)
                {
                    bool on;
                    if (TryOnOff(value, out on)) Rabbits = on;
                }
                else if (key == SandstormsKey)
                {
                    bool on;
                    if (TryOnOff(value, out on)) Sandstorms = on;
                }
                else if (key == HighRateKey)
                {
                    bool on;
                    if (TryOnOff(value, out on)) HighRate = on;
                }
            }
        }

        internal static bool TryOnOff(string value, out bool on)
        {
            on = value == "on" || value == "true" || value == "1";
            return on || value == "off" || value == "false" || value == "0";
        }

        internal static bool TryPreset(string value, out GraphicsPreset preset)
        {
            preset = GraphicsPreset.Balanced;
            for (int i = 0; i < Presets.Length; i++)
            {
                if (String.Equals(Presets[i].Name, value, StringComparison.OrdinalIgnoreCase))
                {
                    preset = (GraphicsPreset)i;
                    return true;
                }
            }
            return false;
        }

        internal static bool TryShadow(string value, out ShadowLevel level)
        {
            level = ShadowLevel.Medium;
            for (int i = 0; i < Shadows.Length; i++)
            {
                if (String.Equals(Shadows[i].Name, value, StringComparison.OrdinalIgnoreCase))
                {
                    level = (ShadowLevel)i;
                    return true;
                }
            }
            return false;
        }

        internal static bool Save()
        {
            try
            {
                string path = DefaultPath();
                if (path == null) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, Format(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
