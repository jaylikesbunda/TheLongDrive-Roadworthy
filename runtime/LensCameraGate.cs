using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Pure decisions for LensCameraGate (no Unity objects, unit-tested).
    public static class LensCameraPolicy
    {
        // While no screen camera can see the lens, its picture is still refreshed
        // this often, so anything the frustum test cannot see (a mirror, a camera
        // added later) shows a recent picture rather than a frozen one.
        public const float HiddenRefreshSeconds = .25f;

        public static bool Render(bool lensSeen, float secondsSinceRender)
        {
            return lensSeen || !(secondsSinceRender < HiddenRefreshSeconds);
        }
    }

    // Binoculars (tavcsoscript) show a live picture on their lenses: a second
    // camera (CamR) renders the whole scene, out to 100 km, into a small texture
    // every frame while the cursor is locked. That costs about 0.5 ms of main
    // thread per pair of binoculars, even when the binoculars are in a drawer
    // behind the player. The picture is only seen on the lens renderers, so the
    // lens camera now renders on frames where a screen camera can see a lens,
    // plus a refresh four times a second otherwise. Looking through the
    // binoculars is unchanged (the game turns the lens camera off then anyway).
    // TLD_MP_LENS_CAMERA_GATE=0 turns this off.
    public static class LensCameraGate
    {
        static readonly bool DisabledByEnvironment = Environment.GetEnvironmentVariable("TLD_MP_LENS_CAMERA_GATE") == "0";
        // Developer A/B switch (SpawnProbe); paused restores every lens camera.
        public static bool Paused;
        static bool Disabled { get { return DisabledByEnvironment || Paused; } }

        sealed class Lens
        {
            internal tavcsoscript Owner;
            internal Camera Camera;
            internal float LastRender = -1000f;
            internal bool Gated;
        }
        static readonly List<Lens> lenses = new List<Lens>();
        static readonly HashSet<int> known = new HashSet<int>();
        static readonly Plane[] planes = new Plane[6];
        static Camera[] screen = new Camera[16];
        static float nextScan;
        public static long Rendered, Held;

        public static int Tracked { get { return lenses.Count; } }
        public static string Summary() { return "lensGate{lenses=" + lenses.Count + " rendered=" + Rendered + " held=" + Held + "}"; }

        public static void Tick()
        {
            if (Disabled) { if (lenses.Count > 0) RestoreAll(); return; }
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1f; Scan(); }
            if (lenses.Count == 0) return;

            int screens = ScreenCameras();
            for (int i = lenses.Count - 1; i >= 0; i--)
            {
                var lens = lenses[i];
                if (lens.Owner == null || lens.Camera == null) { if (lens.Camera != null && lens.Gated) lens.Camera.enabled = true; lenses.RemoveAt(i); continue; }
                // The game switches the lens camera's object on and off itself;
                // only Camera.enabled is ours.
                if (!lens.Camera.gameObject.activeInHierarchy) { if (lens.Gated) { lens.Camera.enabled = true; lens.Gated = false; } continue; }
                bool render = LensCameraPolicy.Render(Seen(lens.Owner, screens), Time.unscaledTime - lens.LastRender);
                if (render) { lens.LastRender = Time.unscaledTime; Rendered++; } else Held++;
                if (lens.Camera.enabled != render) lens.Camera.enabled = render;
                lens.Gated = !render;
            }
        }

        // Lens cameras are found among the enabled cameras (a handful) while the
        // game has them on; once known they stay tracked while gated off.
        static void Scan()
        {
            int count = Camera.allCamerasCount;
            if (screen.Length < count) screen = new Camera[count + 8];
            Camera.GetAllCameras(screen);
            for (int c = 0; c < count; c++)
            {
                var cam = screen[c];
                if (cam == null || cam.targetTexture == null) continue;
                int id = cam.GetInstanceID();
                if (!known.Add(id)) continue;
                var owner = cam.GetComponentInParent<tavcsoscript>();
                if (owner == null || owner.CamR != cam) continue;
                lenses.Add(new Lens { Owner = owner, Camera = cam });
            }
            Array.Clear(screen, 0, count);
        }

        // Enabled cameras that draw to the screen, compacted to the front of the array.
        static int ScreenCameras()
        {
            int count = Camera.allCamerasCount;
            if (screen.Length < count) screen = new Camera[count + 8];
            Camera.GetAllCameras(screen);
            int n = 0;
            for (int c = 0; c < count; c++)
            {
                var cam = screen[c];
                if (cam != null && cam.targetTexture == null) screen[n++] = cam;
            }
            for (int c = n; c < count; c++) screen[c] = null;
            return n;
        }

        static bool Seen(tavcsoscript owner, int screens)
        {
            var renderers = owner.R;
            if (renderers == null) return true;
            for (int c = 0; c < screens; c++)
            {
                var cam = screen[c];
                GeometryUtility.CalculateFrustumPlanes(cam, planes);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if ((cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                    if (GeometryUtility.TestPlanesAABB(planes, r.bounds)) return true;
                }
            }
            return false;
        }

        static void RestoreAll()
        {
            for (int i = 0; i < lenses.Count; i++) if (lenses[i].Camera != null && lenses[i].Gated) lenses[i].Camera.enabled = true;
            lenses.Clear(); known.Clear(); nextScan = 0f;
        }

        public static void Clear() { RestoreAll(); }
    }
}
