using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Pure decisions for SmallShadowCulling (no Unity objects, unit-tested).
    public static class SmallShadowPolicy
    {
        // Within this distance of the view a cobweb casts shadows as before.
        // Past it the shadow falls in the third cascade or further (about 5 cm
        // per shadow texel at 100 m shadow distance), where a web's threads are
        // already a blur. The gap to OffDistance stops flicker at the edge.
        public const float OnDistance = 20f, OffDistance = 24f;

        // Returns the new "casting" state.
        public static bool Casts(bool castingNow, float distanceSquared)
        {
            if (float.IsNaN(distanceSquared)) return true;
            return castingNow ? !(distanceSquared > OffDistance * OffDistance) : distanceSquared <= OnDistance * OnDistance;
        }

        public static bool IsTarget(string materialName)
        {
            return materialName != null && materialName.StartsWith("cobweb", StringComparison.Ordinal);
        }
    }

    // Cobwebs (about 950 in a start-house area) each cast a sun shadow, and
    // the shadow pass measured about 1-1.5 ms of main thread for them. Their
    // shadows only matter near the player; past OnDistance they are switched
    // off and switched back on before the player gets close. Cobwebs sit in
    // POIs and never move relative to their POI, so their positions are read
    // once and kept relative to the POI root (which moves with the floating
    // origin). Cobwebs under a rigidbody are left alone.
    // TLD_MP_SMALL_SHADOW_CULL=0 turns this off.
    public static class SmallShadowCulling
    {
        static readonly bool DisabledByEnvironment = Environment.GetEnvironmentVariable("TLD_MP_SMALL_SHADOW_CULL") == "0";
        // Developer A/B switch (SpawnProbe); paused restores every shadow.
        public static bool Paused;
        static bool Disabled { get { return DisabledByEnvironment || Paused; } }

        sealed class Caster
        {
            internal Renderer R;
            internal Transform Root;
            internal float X, Y, Z;
            internal UnityEngine.Rendering.ShadowCastingMode Mode;
            internal bool Casting = true;
        }
        static readonly List<Caster> casters = new List<Caster>();
        static readonly HashSet<int> knownRoots = new HashSet<int>();
        static readonly List<MeshRenderer> found = new List<MeshRenderer>();
        static float nextScan;
        static int switchedOff;

        public static string Summary() { return "smallShadows{casters=" + casters.Count + " off=" + switchedOff + "}"; }

        public static void Tick()
        {
            if (Disabled) { if (casters.Count > 0) RestoreAll(); return; }
            var main = mainscript.s;
            var view = main == null || main.player == null || main.player.Cam == null ? null : main.player.Cam.transform;
            if (view == null) return;
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1f; Scan(); }
            if (casters.Count == 0) return;
            Vector3 at = view.position;
            Transform lastRoot = null; Vector3 local = Vector3.zero;
            int off = 0;
            for (int i = 0; i < casters.Count; i++)
            {
                var c = casters[i];
                if (c.Root != lastRoot)
                {
                    lastRoot = c.Root;
                    if (lastRoot == null) continue;
                    local = at - lastRoot.position;
                }
                float dx = c.X - local.x, dy = c.Y - local.y, dz = c.Z - local.z;
                bool casts = SmallShadowPolicy.Casts(c.Casting, dx * dx + dy * dy + dz * dz);
                if (casts != c.Casting)
                {
                    if (c.R == null) continue;
                    c.R.shadowCastingMode = casts ? c.Mode : UnityEngine.Rendering.ShadowCastingMode.Off;
                    c.Casting = casts;
                }
                if (!c.Casting) off++;
            }
            switchedOff = off;
        }

        static void Scan()
        {
            int before = casters.Count;
            casters.RemoveAll(c => c.R == null || c.Root == null);
            if (casters.Count != before) { knownRoots.Clear(); foreach (var c in casters) knownRoots.Add(c.Root.GetInstanceID()); }
            var map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
            if (map == null || map.poiGens == null) return;
            for (int g = 0; g < map.poiGens.Count; g++)
            {
                var gen = map.poiGens[g];
                if (gen == null) continue;
                if (gen.fixPois != null) for (int i = 0; i < gen.fixPois.Count; i++) Poi(gen.fixPois[i]);
                if (gen.chunks == null) continue;
                foreach (var chunk in gen.chunks.Values)
                    if (chunk != null && chunk.pois != null)
                        foreach (var poi in chunk.pois.Values) if (poi != null) Poi(poi.pobj);
            }
        }

        static void Poi(poiScript poi)
        {
            if (poi == null || !poi.started || !knownRoots.Add(poi.GetInstanceID())) return;
            var root = poi.transform;
            Vector3 origin = root.position;
            found.Clear();
            poi.GetComponentsInChildren(true, found);
            foreach (var r in found)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
                var m = r.sharedMaterial;
                if (m == null || !SmallShadowPolicy.IsTarget(m.name)) continue;
                if (r.GetComponentInParent<Rigidbody>() != null) continue;
                Vector3 p = r.bounds.center - origin;
                casters.Add(new Caster { R = r, Root = root, X = p.x, Y = p.y, Z = p.z, Mode = r.shadowCastingMode });
            }
            found.Clear();
        }

        static void RestoreAll()
        {
            for (int i = 0; i < casters.Count; i++) if (!casters[i].Casting && casters[i].R != null) casters[i].R.shadowCastingMode = casters[i].Mode;
            casters.Clear(); knownRoots.Clear(); switchedOff = 0; nextScan = 0f;
        }

        public static void Clear() { RestoreAll(); }
    }
}
