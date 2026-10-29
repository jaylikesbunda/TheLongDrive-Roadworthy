using System;
using UnityEngine;

namespace TldMp
{
    // A/B switches for the mirror render path, read once from the environment:
    //   TLD_MP_MIRROR_OCCLUSION=0  mirror cameras run without occlusion culling
    //   TLD_MP_MIRROR_SHADOWS=0    shadows are off only while a mirror camera renders
    // Defaults change nothing. The shadow window is opened and closed by the
    // existing MirrorsUpdate entry/exit observers; if that exit is ever skipped by
    // an exception, the saved value is restored at the next frame start.
    //
    // Also holds the graphics preset's realtime reflection probes off. The game's
    // own graphics refresh (SetReflections) can re-enable them after world load,
    // and each enabled probe renders a reflection camera every frame.
    internal static class RenderSwitches
    {
        internal const float PresetShadowDistance = 80f;
        internal const int PresetAntiAliasing = 2, PresetPixelLightCount = 4, PresetShadowCascades = 2;

        static int state;
        static bool occlusionOff, shadowsOff, envOcclusionOff, envShadowsOff, presetMirror;
        static bool shadowPending;
        static float shadowSaved;
        internal static long ReflectionHolds;

        internal static bool IsOff(string value) { return value == "0"; }

        static void ReadOnce()
        {
            if (state != 0) return;
            envOcclusionOff = IsOff(Environment.GetEnvironmentVariable("TLD_MP_MIRROR_OCCLUSION"));
            envShadowsOff = IsOff(Environment.GetEnvironmentVariable("TLD_MP_MIRROR_SHADOWS"));
            occlusionOff = envOcclusionOff;
            shadowsOff = envShadowsOff;
            state = 1;
        }

        // The Performance preset turns the two mirror optimizations on. Environment
        // switches can only ever add to them; selecting Vanilla/Balanced restores
        // exactly what the environment asked for. Nothing here changes what exists,
        // only what a mirror camera renders while it is open.
        internal static void SetMirrorOptimizations(bool on)
        {
            ReadOnce();
            if (presetMirror == on) return;
            presetMirror = on;
            occlusionOff = on || envOcclusionOff;
            shadowsOff = on || envShadowsOff;
            if (!shadowsOff) RestoreShadows();
        }

        internal static bool PresetMirrorOptimizations { get { return presetMirror; } }

        // Unity-free: the preset is in effect only when every value it sets still matches.
        internal static bool PresetMatches(int antiAliasing, int pixelLightCount, int shadowCascades, float shadowDistance)
        {
            return antiAliasing == PresetAntiAliasing && pixelLightCount == PresetPixelLightCount &&
                   shadowCascades == PresetShadowCascades && Math.Abs(shadowDistance - PresetShadowDistance) < 0.01f;
        }

        // Unity-free: the reflection hold writes only when probes are on and the preset is in effect.
        internal static bool ShouldHoldReflections(bool worldActive, bool probesOn, bool presetInEffect)
        {
            return worldActive && probesOn && presetInEffect;
        }

        internal static void MirrorsBegin(mainscript main)
        {
            ReadOnce();
            RestoreShadows();
            if (occlusionOff && main != null && main.Mirrors != null)
            {
                for (int i = 0; i < main.Mirrors.Count; i++)
                {
                    var camera = main.Mirrors[i] == null ? null : main.Mirrors[i].C;
                    if (camera != null && camera.useOcclusionCulling) camera.useOcclusionCulling = false;
                }
            }
            if (shadowsOff && !shadowPending)
            {
                shadowSaved = QualitySettings.shadowDistance;
                shadowPending = true;
                QualitySettings.shadowDistance = 0f;
            }
        }

        internal static void MirrorsEnd()
        {
            RestoreShadows();
        }

        internal static void RestoreShadows()
        {
            if (!shadowPending) return;
            shadowPending = false;
            QualitySettings.shadowDistance = shadowSaved;
        }

        // Runs at the start of each main-loop frame, before any mirror window opens.
        internal static void MainFrame()
        {
            RestoreShadows();
            bool worldActive = mainscript.s != null && mainscript.s.player != null;
            // Balanced, Performance and Custom all want realtime reflection probes
            // off; Vanilla applies nothing and leaves the game's own value alone.
            bool presetInEffect = PatchOptions.Values(PatchOptions.Preset).Applies;
            if (!ShouldHoldReflections(worldActive, QualitySettings.realtimeReflectionProbes, presetInEffect)) return;
            QualitySettings.realtimeReflectionProbes = false;
            ReflectionHolds++;
        }

        internal static string Summary()
        {
            ReadOnce();
            return " mirrorSwitches{occlusion=" + (occlusionOff ? "off" : "on") + " shadows=" + (shadowsOff ? "off" : "on") +
                   " reflectionHolds=" + ReflectionHolds + "}";
        }
    }
}
