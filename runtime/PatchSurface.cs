using System;
using System.Text;
using TMPro;
using UnityEngine;
using Steamworks;

namespace TldMp
{
    // Runtime entry points called from the patched game assembly. Both are tiny on
    // purpose: the v4 patch pins the single call in TldStable.Configure, and the
    // version label swap pins the call in mainscript.Update / menuhandler.FStart.
    public static partial class StableGameBridge
    {
        static string lastPresetLog;
        static string lastPresetError;

        // Called instead of TextMeshProUGUI.set_text for the top-right label.
        // Application.version itself is never changed.
        public static void SetVersionLabel(TMP_Text label, string text)
        {
            if (label == null) return;
            label.text = PatchInfo.Display;
        }

        public static bool SetLobbyVersion(CSteamID lobby, string key, string version)
        {
            bool result = SteamMatchmaking.SetLobbyData(lobby, key, version);
            if (result) SteamMatchmaking.SetLobbyData(lobby, "roadworthy_version", PatchInfo.Display.Trim());
            return result;
        }

        public static string GetLobbyVersion(CSteamID lobby, string key)
        {
            string roadworthy = SteamMatchmaking.GetLobbyData(lobby, "roadworthy_version");
            return string.IsNullOrEmpty(roadworthy) ? SteamMatchmaking.GetLobbyData(lobby, key) : roadworthy;
        }

        // The vanilla news window reads news.txt once at scene start. Keep the
        // Roadworthy notes on top of that text so the same button shows both.
        // The notes are compiled in from CHANGELOG.md at build time.
        static menuhandler newsStamped;
        static bool newsFailed;
        static string newsNotes;

        internal static string NewsNotes
        {
            get
            {
                if (newsNotes == null)
                    try { newsNotes = Encoding.UTF8.GetString(Convert.FromBase64String(PatchNotes.ChangelogBase64)).Trim(); }
                    catch { newsNotes = ""; }
                return newsNotes;
            }
        }

        public static void TickNewsChangelog()
        {
            try
            {
                var menu = menuhandler.s;
                if (menu == null || menu.newsText == null || newsStamped == menu) return;
                string notes = NewsNotes;
                if (notes.Length == 0) { newsStamped = menu; return; }
                string text = menu.newsText.text;
                if (text != null && text.StartsWith(notes)) { newsStamped = menu; return; }
                menu.newsText.text = string.IsNullOrEmpty(text) ? notes : notes + "\n\n---------- Original game news ----------\n\n" + text;
                if (menu.newsButton != null && !menu.newsButton.activeSelf) menu.newsButton.SetActive(true);
                newsStamped = menu;
            }
            catch (Exception e)
            {
                if (!newsFailed) { newsFailed = true; Debug.LogWarning("[TLD MP news] changelog prepend failed: " + e.GetType().Name); }
            }
        }

        // Called from TldStable.Configure after scene load and before every return
        // of settingsscript.RefreshInMain. Any failure falls back to the Balanced
        // values the IL used to write directly.
        public static void ApplyGraphicsPreset()
        {
            try
            {
                PatchOptions.Load();
                GraphicsPreset preset = PatchOptions.Preset;
                PresetValues values = PatchOptions.Values(preset);
                if (preset == GraphicsPreset.Vanilla)
                {
                    ApplyShadowBudget(null);
                    RenderSwitches.SetMirrorOptimizations(false);
                    LogPreset("vanilla");
                    return;
                }
                QualitySettings.antiAliasing = values.AntiAliasing;
                ApplyShadowBudget(values);
                QualitySettings.realtimeReflectionProbes = values.RealtimeReflectionProbes;
                RenderSwitches.SetMirrorOptimizations(values.MirrorOptimizations);
                if (preset == GraphicsPreset.Custom) ApplyShadow(PatchOptions.ShadowValuesFor(PatchOptions.Shadow));
                LogPreset(PatchOptions.PresetName(preset) + "/" + PatchOptions.ShadowName(PatchOptions.Shadow));
            }
            catch (Exception error)
            {
                ApplyBalanced();
                string message = error.GetType().Name + ": " + error.Message;
                if (lastPresetError != message)
                {
                    lastPresetError = message;
                    Debug.LogWarning("[TLD MP options] preset failed, using Balanced: " + message);
                }
            }
        }

        // The quality level owns the shadow budget: pixel lights, shadow distance
        // and cascades. Only the Performance preset replaces them. Every other
        // preset (and Vanilla) puts back exactly the values Unity had for the
        // active level, so a preset change can never leave the level overridden
        // and a quality-level change is picked up as the new baseline.
        static int shadowBudgetLevel = Int32.MinValue;
        static int shadowBudgetLights, shadowBudgetCascades;
        static float shadowBudgetDistance;
        static void ApplyShadowBudget(PresetValues values)
        {
            int level = QualitySettings.GetQualityLevel();
            if (level != shadowBudgetLevel)
            {
                shadowBudgetLevel = level;
                shadowBudgetLights = QualitySettings.pixelLightCount;
                shadowBudgetDistance = QualitySettings.shadowDistance;
                shadowBudgetCascades = QualitySettings.shadowCascades;
            }
            if (values != null && values.ShadowBudget)
            {
                QualitySettings.pixelLightCount = values.PixelLightCount;
                QualitySettings.shadowDistance = values.ShadowDistance;
                QualitySettings.shadowCascades = values.ShadowCascades;
            }
            else
            {
                QualitySettings.pixelLightCount = shadowBudgetLights;
                QualitySettings.shadowDistance = shadowBudgetDistance;
                QualitySettings.shadowCascades = shadowBudgetCascades;
            }
        }

        static void ApplyShadow(ShadowValues values)
        {
            QualitySettings.shadows = (ShadowQuality)values.Shadows;
            QualitySettings.shadowDistance = values.ShadowDistance;
            QualitySettings.shadowCascades = values.ShadowCascades;
            QualitySettings.shadowResolution = (ShadowResolution)values.ShadowResolution;
        }

        static void ApplyBalanced()
        {
            try
            {
                QualitySettings.antiAliasing = 2;
                QualitySettings.realtimeReflectionProbes = false;
            }
            catch (Exception)
            {
            }
        }

        static void LogPreset(string text)
        {
            if (lastPresetLog == text) return;
            lastPresetLog = text;
            Debug.Log("[TLD MP options] " + text + " file=" + (PatchOptions.DefaultPath() == null ? "?" : PatchOptions.DefaultPath()));
        }
    }
}
