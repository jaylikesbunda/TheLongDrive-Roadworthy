using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Pure decisions for GrassMarkerCulling (no Unity objects, unit-tested).
    public static class GrassMarkerPolicy
    {
        // Extra distance kept switched on around the grass camera's square, so a
        // marker that moves into view between checks is already on. At 40 m/s and a
        // check cycle under 0.2 s a marker moves about 8 m; 25 m leaves room to spare.
        public const float Margin = 25f;
        // A player move larger than this between frames (teleport, respawn, join)
        // re-checks every marker at once instead of in slices.
        public const float JumpDistance = 20f;
        public const int SliceSize = 350;

        // True while the marker's bounds can reach the grass camera's square
        // (an axis-aligned top-down orthographic view centred on the camera).
        public static bool Needed(float centerX, float centerZ, float extentX, float extentZ,
            float cameraX, float cameraZ, float halfWidth, float halfHeight, float margin)
        {
            if (float.IsNaN(centerX) || float.IsNaN(centerZ) || float.IsNaN(cameraX) || float.IsNaN(cameraZ)) return true;
            return Math.Abs(centerX - cameraX) <= halfWidth + extentX + margin &&
                   Math.Abs(centerZ - cameraZ) <= halfHeight + extentZ + margin;
        }
    }

    // Grass displacement markers (layer "displacement") are drawn only by the
    // grass camera, a top-down orthographic camera covering a square around the
    // player. Every other camera still walks those renderers each frame while
    // culling, which measured about 1 ms per frame for the ~2,600 markers that
    // loaded road pieces carry. A marker outside the grass camera's square cannot
    // change the displacement texture, so it is switched off with
    // Renderer.forceRenderingOff and switched back on before it can reach the
    // square. The grass camera's output is unchanged.
    //
    // Only road pieces are covered: they hold about 90% of the markers and are
    // created under each road generator's parent, so new pieces are found without
    // searching the scene. Off unless TLD_MP_GRASS_MARKER_CULL=1.
    public static class GrassMarkerCulling
    {
        // Off by default: grass showed through roads with it on. TLD_MP_GRASS_MARKER_CULL=1 turns it on.
        static readonly bool DisabledByEnvironment = Environment.GetEnvironmentVariable("TLD_MP_GRASS_MARKER_CULL") != "1";
        // Developer A/B switch (SpawnProbe); off restores every marker.
        public static bool Paused;
        static bool Disabled { get { return DisabledByEnvironment || Paused; } }

        // Road pieces never move relative to their generator's parent (the
        // world's floating origin moves that parent), so each marker's bounds are
        // read once, kept relative to the parent, and every later check is plain
        // arithmetic. The engine is only touched when a marker changes state.
        sealed class Marker
        {
            internal Renderer R;
            internal Transform Parent;
            internal float X, Z, ExtentX, ExtentZ;
            internal bool Off;
        }
        static readonly Dictionary<Transform, Marker[]> pieces = new Dictionary<Transform, Marker[]>();
        static readonly List<Marker> markers = new List<Marker>();
        static readonly List<Transform> stale = new List<Transform>();
        static readonly List<MeshRenderer> found = new List<MeshRenderer>();
        static int layer = -2, switchedOff;
        static float nextScan;
        static Camera grassCamera;

        public static int Tracked { get { return markers.Count; } }
        public static int SwitchedOff { get { return switchedOff; } }

        public static void Tick()
        {
            if (Disabled) { if (markers.Count > 0) RestoreAll(); return; }
            var main = mainscript.s;
            var map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
            if (main == null || map == null || map.roadGens == null || main.grassCam == null) { Clear(); return; }
            if (grassCamera == null || grassCamera.gameObject != main.grassCam.gameObject) grassCamera = main.grassCam.GetComponent<Camera>();
            if (layer == -2) layer = LayerMask.NameToLayer("displacement");
            // Grass off or camera unusable: put everything back and stand by.
            if (grassCamera == null || layer < 0 || !grassCamera.orthographic || !grassCamera.isActiveAndEnabled || (grassCamera.cullingMask & (1 << layer)) == 0) { RestoreAll(); return; }

            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + .5f; Scan(map); }
            if (markers.Count == 0) return;

            Vector3 at = grassCamera.transform.position;
            float halfHeight = grassCamera.orthographicSize, halfWidth = halfHeight * Mathf.Max(grassCamera.aspect, 1e-3f);
            Transform lastParent = null; float cx = 0, cz = 0;
            int off = 0;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m.Parent != lastParent)
                {
                    lastParent = m.Parent;
                    if (lastParent == null) continue;
                    var p = lastParent.position; cx = at.x - p.x; cz = at.z - p.z;
                }
                bool needed = GrassMarkerPolicy.Needed(m.X, m.Z, m.ExtentX, m.ExtentZ, cx, cz, halfWidth, halfHeight, GrassMarkerPolicy.Margin);
                // The cached bounds can be stale: road pieces fill in or rebuild their
                // displacement meshes after the first scan. Read the real bounds before
                // switching a marker off, and re-check switched-off markers in turn, so a
                // stale cache can only ever leave a marker on (grass never shows through
                // a road under the player).
                bool recheck = m.Off && i % RecheckSlices == recheckSlice;
                if ((!m.Off && !needed) || recheck)
                {
                    if (m.R == null) continue; // destroyed; dropped at the next scan
                    Refresh(m);
                    needed = GrassMarkerPolicy.Needed(m.X, m.Z, m.ExtentX, m.ExtentZ, cx, cz, halfWidth, halfHeight, GrassMarkerPolicy.Margin);
                }
                if (m.Off == needed)
                {
                    if (m.R == null) continue; // destroyed; dropped at the next scan
                    m.R.forceRenderingOff = !needed;
                    m.Off = !needed;
                }
                if (m.Off) off++;
            }
            recheckSlice = (recheckSlice + 1) % RecheckSlices;
            switchedOff = off;
        }

        // Off markers are re-read one slice per frame: every marker within about a second.
        const int RecheckSlices = 60;
        static int recheckSlice;

        static void Refresh(Marker m)
        {
            if (m.R == null || m.Parent == null) return;
            Bounds b = m.R.bounds;
            Vector3 origin = m.Parent.position;
            m.X = b.center.x - origin.x; m.Z = b.center.z - origin.z;
            m.ExtentX = b.extents.x; m.ExtentZ = b.extents.z;
        }

        // New road pieces are added to their parent; destroyed pieces drop out.
        // Only unknown pieces are searched, and their markers' bounds read once.
        static void Scan(mapSettingScript map)
        {
            stale.Clear();
            foreach (var pair in pieces) if (pair.Key == null) stale.Add(pair.Key);
            if (stale.Count > 0)
            {
                for (int i = 0; i < stale.Count; i++) pieces.Remove(stale[i]);
                markers.RemoveAll(m => m.R == null || m.Parent == null);
            }
            for (int g = 0; g < map.roadGens.Count; g++)
            {
                var gen = map.roadGens[g];
                if (gen == null || gen.parent == null) continue;
                var parent = gen.parent;
                Vector3 origin = parent.position;
                for (int c = 0; c < parent.childCount; c++)
                {
                    var piece = parent.GetChild(c);
                    if (piece == null || pieces.ContainsKey(piece)) continue;
                    found.Clear();
                    piece.GetComponentsInChildren(true, found);
                    var list = new List<Marker>();
                    foreach (var r in found)
                    {
                        // Inactive markers have no reliable bounds; they stay with Unity.
                        if (r == null || r.gameObject.layer != layer || r.forceRenderingOff || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                        Bounds b = r.bounds;
                        list.Add(new Marker { R = r, Parent = parent, X = b.center.x - origin.x, Z = b.center.z - origin.z, ExtentX = b.extents.x, ExtentZ = b.extents.z });
                    }
                    found.Clear();
                    var array = list.ToArray();
                    pieces[piece] = array;
                    markers.AddRange(array);
                }
            }
        }

        static void RestoreAll()
        {
            for (int i = 0; i < markers.Count; i++) if (markers[i].Off && markers[i].R != null) markers[i].R.forceRenderingOff = false;
            markers.Clear(); pieces.Clear(); switchedOff = 0;
        }

        public static void Clear()
        {
            RestoreAll(); grassCamera = null; nextScan = 0f;
        }
    }
}