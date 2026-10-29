using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TldMp
{
    // Opt-in (FrameDiagnostics.Enabled) per-camera CPU cost. Read-only: it
    // never changes a camera, renderer or setting.
    internal static class CameraCost
    {
        const double WindowSeconds = 5.0;
        static readonly float[] BandAt = { 45f, 105f, 165f };
        static readonly CameraCostCore core = new CameraCostCore();
        static readonly Dictionary<int, string> labels = new Dictionary<int, string>();
        static bool installed;
        static double windowStart;
        static int bandRuns, labelFrame = -1;

        internal static void Tick()
        {
            if (!FrameDiagnostics.Enabled) return;
            if (!installed)
            {
                installed = true;
                Camera.onPreCull += PreCull;
                Camera.onPostRender += PostRender;
            }
            core.FrameBoundary();
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            if (windowStart == 0) windowStart = now;
            if (now - windowStart >= WindowSeconds)
            {
                try { Log(now - windowStart); } catch (Exception e) { UnityEngine.Debug.Log("[TLD MP cameras] log failed: " + e.GetType().Name); }
                core.ResetWindow(); windowStart = now;
            }
            if (bandRuns < BandAt.Length && Time.realtimeSinceStartup >= BandAt[bandRuns])
            {
                bandRuns++;
                try { LogSettings(); LogBands(); } catch (Exception e) { UnityEngine.Debug.Log("[TLD MP cameras] census failed: " + e.GetType().Name + ": " + e.Message); }
            }
        }

        static void PreCull(Camera camera)
        {
            if (camera == null) return;
            core.Begin(camera.GetInstanceID(), Label(camera), Stopwatch.GetTimestamp());
        }

        static void PostRender(Camera camera)
        {
            if (camera == null) return;
            core.End(camera.GetInstanceID(), Stopwatch.GetTimestamp());
        }

        static string Label(Camera camera)
        {
            if (labelFrame != Time.frameCount && Time.frameCount % 300 == 0) { labels.Clear(); labelFrame = Time.frameCount; }
            int id = camera.GetInstanceID();
            string label;
            if (labels.TryGetValue(id, out label)) return label;
            label = Classify(camera);
            labels[id] = label;
            return label;
        }

        static string Classify(Camera camera)
        {
            var main = mainscript.s;
            if (main != null && main.player != null)
            {
                if (camera == main.player.mainCam) return "main";
                if (camera == main.player.Cam) return "view";
                if (camera == main.player.farCamera) return "far";
            }
            if (main != null && main.Mirrors != null)
                for (int i = 0; i < main.Mirrors.Count; i++)
                    if (main.Mirrors[i] != null && main.Mirrors[i].C == camera) return "mirror";
            var reflections = reflectionMainScript.s;
            if (reflections != null && reflections.reflectionPlanes != null)
                for (int i = 0; i < reflections.reflectionPlanes.Count; i++)
                    if (reflections.reflectionPlanes[i] != null && reflections.reflectionPlanes[i].cam == camera) return "reflectionPlane";
            if (camera.targetTexture != null) return "rt:" + camera.name;
            return "other:" + camera.name;
        }

        static double Ms(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }
        static string F(double v) { return v.ToString("F3", CultureInfo.InvariantCulture); }

        static void Log(double seconds)
        {
            int frames = Math.Max(1, core.Frames);
            var text = new StringBuilder("[TLD MP cameras] t=");
            text.Append(Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture))
                .Append(" window=").Append(seconds.ToString("F1", CultureInfo.InvariantCulture))
                .Append("s frames=").Append(core.Frames);
            foreach (var b in core.Ordered)
            {
                text.Append(' ').Append(b.Label).Append("{renders/frame=").Append((b.Renders / (double)frames).ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" exclusiveMs/frame=").Append(F(Ms(b.Exclusive) / frames))
                    .Append(" inclusiveMs/frame=").Append(F(Ms(b.Inclusive) / frames))
                    .Append(" maxRenderMs=").Append(F(Ms(b.MaxExclusive))).Append('}');
            }
            text.Append(" mirrorSkips{onScreenSkipped=").Append(MirrorOnScreen.Skipped).Append(" rendered=").Append(MirrorOnScreen.Passed).Append('}');
            text.Append(' ').Append(RoadBake.Summary());
            text.Append(" dropped=").Append(core.Dropped).Append(" overflow=").Append(core.Overflow);
            text.Append(' ').Append(StableGameBridge.TakeItemSettleSummary());
            text.Append(' ').Append(StableGameBridge.TakeFarPlaceSummary());
            text.Append(' ').Append(StableGameBridge.TakeTerrainWorkerSummary());
            text.Append(' ').Append(StableGameBridge.TakeCarSmoothSummary());
            text.Append(' ').Append(CargoGhost.Summary());
            text.Append(' ').Append(PlacementSkip.TakeSummary());
            text.Append(' ').Append(StableGameBridge.TakeLiveUsableSummary());
            text.Append(' ').Append(CarCargo.TakeSummary());
            text.Append(" wildlife{creatureWritesSkipped=").Append(WildlifeWeather.CreatureWritesSkipped).Append('}');
            WildlifeWeather.CreatureWritesSkipped = 0;
            MirrorOnScreen.Skipped = 0; MirrorOnScreen.Passed = 0;
            UnityEngine.Debug.Log(text.ToString());
            int interpolated, extrapolated, held;
            RemoteCarSmoothing.TakeSampleCounts(out interpolated, out extrapolated, out held);
            UnityEngine.Debug.Log(NetStats.TakeLine(Time.realtimeSinceStartup, seconds, StableGameBridge.DeliveryCoalesced,
                PlayerLeaveSafety.RestoreCache, interpolated, extrapolated, held));
        }

        static void Describe(StringBuilder text, string name, Camera c)
        {
            if (c == null) { text.Append(' ').Append(name).Append("{missing}"); return; }
            var rt = c.targetTexture;
            int culled = 0;
            float[] distances = c.layerCullDistances;
            if (distances != null) for (int i = 0; i < distances.Length; i++) if (distances[i] > 0) culled++;
            text.Append(' ').Append(name).Append("{mask=0x").Append(c.cullingMask.ToString("X8"))
                .Append(" path=").Append(c.actualRenderingPath)
                .Append(" near=").Append(F(c.nearClipPlane)).Append(" far=").Append(F(c.farClipPlane))
                .Append(" msaa=").Append(c.allowMSAA).Append(" hdr=").Append(c.allowHDR)
                .Append(" occlusion=").Append(c.useOcclusionCulling)
                .Append(" layerCull=").Append(culled)
                .Append(" postProcess=").Append(c.GetComponent("PostProcessLayer") != null)
                .Append(" rt=").Append(rt == null ? "screen" : rt.width + "x" + rt.height + "/aa" + rt.antiAliasing + "/depth" + rt.depth)
                .Append(" enabled=").Append(c.enabled).Append('}');
        }

        static void LogSettings()
        {
            var text = new StringBuilder("[TLD MP cameras] settings t=");
            text.Append(Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture))
                .Append(" shadows=").Append(QualitySettings.shadows)
                .Append(" shadowDistance=").Append(F(QualitySettings.shadowDistance))
                .Append(" cascades=").Append(QualitySettings.shadowCascades)
                .Append(" pixelLights=").Append(QualitySettings.pixelLightCount)
                .Append(" drawDistance=").Append(settingsscript.s == null ? "?" : F(settingsscript.s.S.FDrawDistance))
                .Append(" mirrorDrawDistance=").Append(settingsscript.s == null ? "?" : F(settingsscript.s.S.FMirrorDrawDistance))
                .Append(RenderSwitches.Summary());
            var main = mainscript.s;
            if (main != null && main.player != null)
            {
                Describe(text, "main", main.player.mainCam);
                Describe(text, "view", main.player.Cam);
                Describe(text, "far", main.player.farCamera);
            }
            if (main != null && main.Mirrors != null)
            {
                text.Append(" mirrors=").Append(main.Mirrors.Count);
                for (int i = 0; i < main.Mirrors.Count && i < 3; i++)
                    if (main.Mirrors[i] != null) Describe(text, "mirror" + i, main.Mirrors[i].C);
            }
            UnityEngine.Debug.Log(text.ToString());
        }

        static readonly float[] Bands = { 200f, 500f, 2000f, 5000f, float.PositiveInfinity };
        static readonly string[] BandNames = { "lt200", "200-500", "500-2k", "2k-5k", "gt5k" };

        static void LogBands()
        {
            var main = mainscript.s;
            Camera cam = main != null && main.player != null ? main.player.mainCam : Camera.main;
            if (cam == null) return;
            Vector3 origin = cam.transform.position;
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            var skinned = new int[Bands.Length]; var mesh = new int[Bands.Length]; var other = new int[Bands.Length];
            var baked = new int[Bands.Length]; var ropes = new int[Bands.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                float distance = Vector3.Distance(origin, r.bounds.center);
                int band = 0;
                while (band < Bands.Length - 1 && distance >= Bands[band]) band++;
                if (r is SkinnedMeshRenderer) skinned[band]++;
                else if (r is MeshRenderer) mesh[band]++;
                else other[band]++;
                if (r.gameObject.name == RoadBake.ChildName) baked[band]++;
                var material = r.sharedMaterial;
                if (material != null && material.name.IndexOf("Rope", StringComparison.OrdinalIgnoreCase) >= 0) ropes[band]++;
            }
            var text = new StringBuilder("[TLD MP cameras] bands t=");
            text.Append(Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture)).Append(" renderers=").Append(renderers.Length);
            for (int b = 0; b < Bands.Length; b++)
                text.Append(' ').Append(BandNames[b]).Append("{skinned=").Append(skinned[b]).Append(" mesh=").Append(mesh[b])
                    .Append(" other=").Append(other[b]).Append(" bakedRoads=").Append(baked[b]).Append(" ropes=").Append(ropes[b]).Append('}');
            UnityEngine.Debug.Log(text.ToString());
        }
    }
}
