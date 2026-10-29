using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TldMp
{
    // Opt-in, read-only ground diagnostics. The bridge calls in only when
    // ModDiagnostics.On or loopback is set.
    //  * [TLD MP bus-ground]: one line when a bus stop POI is placed and when it
    //    is spawned, plus a few bounded follow-ups once terrain has had time to
    //    load. Compares the placement height, the alignment-helper heights and
    //    the near-terrain collider under several floor anchors of the real prefab.
    //  * [TLD MP terrain-lod]: while driving, near-collider height vs the distant
    //    meshes' interpolated height at the same global XZ points ahead of the player.
    // Nothing here writes to the game, moves an object, or changes a decision.
    public static class GroundDiagnostics
    {
        const int BusStopId = 5;
        const int MaxBusLines = 400;
        const int MaxLodLines = 240;
        const int MaxPending = 24;
        const int MaxPoiLines = 300;
        const int MaxPlacedChecks = 400;
        const int MaxPoiNearLines = 120;
        const int PoiNearEntries = 8;
        const float PoiNearRange = 150f;
        const float PoiNearMove = 30f;
        const float NearRange = 150f;
        const float PollInterval = .5f;
        const float WaitLimit = 600f;
        const int MaxTilesScanned = 4096;
        const float RayHeight = 1000f;
        const float RayLength = 2000f;
        const float LodInterval = 2f;
        static readonly float[] FollowUps = new float[] { 3f, 10f };
        static readonly float[] LodDistances = new float[] { 0f, 40f, 80f, 120f, 200f, 300f, 450f, 700f };
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        sealed class Pending
        {
            public poiGenScript.poiClass Poi;
            public float Due;
            public int Step;
            public bool Waiting;
            public float Deadline;
        }

        static readonly List<Pending> pending = new List<Pending>();
        static int busLines, poiLines, lodLines, placedChecks, poiNearLines;
        static Vector3d poiNearAt;
        static bool poiNearSeen;
        static float nextLod, nextPoiNear;
        static bool lodHeader;

        static string F(double x) { return x.ToString("F3", Invariant); }
        static string V(Vector3d p) { return F(p.x) + "," + F(p.y) + "," + F(p.z); }

        static bool Emit(string line, ref int counter, int max)
        {
            if (counter >= max) return false;
            counter++;
            Debug.Log(counter == max ? line + " (log limit reached)" : line);
            return true;
        }

        static mapSettingScript Map()
        {
            return menuhandler.s == null ? null : menuhandler.s.currentMainMap;
        }

        static terrainGenScript NearGen(mapSettingScript map)
        {
            if (map == null || map.terrainGen == null) return null;
            for (int i = 0; i < map.terrainGen.Count; i++)
            {
                var g = map.terrainGen[i];
                if (g != null && g.hasCollider && !g.isDistantTerrain && g.terrains != null) return g;
            }
            return null;
        }

        // The tile whose footprint covers the global XZ point, scanning a bounded number of tiles.
        static newTerrainTileScript TileAt(terrainGenScript gen, Vector3d g, out Vector3d key)
        {
            key = default(Vector3d);
            if (gen == null || gen.terrains == null) return null;
            double half = gen.tileSize * 0.5;
            int scanned = 0;
            foreach (var pair in gen.terrains)
            {
                if (++scanned > MaxTilesScanned) break;
                if (Math.Abs(pair.Key.x - g.x) <= half && Math.Abs(pair.Key.z - g.z) <= half && pair.Value != null)
                { key = pair.Key; return pair.Value; }
            }
            return null;
        }

        // Collider height of the near terrain at a global XZ. tile/collider tell why a miss happened.
        static double ColliderY(terrainGenScript gen, Vector3d g, out bool tile, out bool collider)
        {
            tile = collider = false;
            Vector3d key;
            var t = TileAt(gen, g, out key);
            if (t == null) return double.NaN;
            tile = true;
            if (t.mc == null || !t.mc.enabled || t.mc.sharedMesh == null) return double.NaN;
            collider = true;
            Vector3 origin = mainscript.UnityPosFromGlobal(g) + Vector3.up * RayHeight;
            RaycastHit hit;
            if (!t.mc.Raycast(new Ray(origin, Vector3.down), out hit, RayLength)) return double.NaN;
            return mainscript.GlobalFromUnityPos(hit.point).y;
        }

        // Bilinear height of a tile's render mesh at a global XZ (grid vertices, not the exact triangle split).
        static double MeshY(terrainGenScript gen, Vector3d g)
        {
            Vector3d key;
            var t = TileAt(gen, g, out key);
            if (t == null || t.mf == null || t.mf.sharedMesh == null || gen.resolution < 2 || gen.vertexDist <= 0f) return double.NaN;
            try
            {
                Vector3[] v = t.mf.sharedMesh.vertices;
                int res = gen.resolution;
                if (v == null || v.Length < res * res) return double.NaN;
                double half = gen.tileSize * 0.5;
                double fx = Math.Max(0, Math.Min(res - 1.0001, (g.x - key.x + half) / gen.vertexDist));
                double fz = Math.Max(0, Math.Min(res - 1.0001, (g.z - key.z + half) / gen.vertexDist));
                int j = (int)fx, i = (int)fz;
                double ax = fx - j, az = fz - i;
                double y = (v[j + i * res].y * (1 - ax) + v[j + 1 + i * res].y * ax) * (1 - az)
                         + (v[j + (i + 1) * res].y * (1 - ax) + v[j + 1 + (i + 1) * res].y * ax) * az;
                double lx = fx * gen.vertexDist - half, lz = fz * gen.vertexDist - half;
                return mainscript.GlobalFromUnityPos(t.t.TransformPoint(new Vector3((float)lx, (float)y, (float)lz))).y;
            }
            catch { return double.NaN; }
        }

        static string Num(double v) { return double.IsNaN(v) ? "n/a" : F(v); }

        // A spawned haz02 can precede its terrain tile. Keep its bus stop pending
        // until the actual near-terrain collider is ready; only then may its
        // detached item spawners run. Entries are removed when the POI despawns.
        static readonly List<poiScript> busPlacementPending = new List<poiScript>();
        static readonly HashSet<poiScript> busPlacementComplete = new HashSet<poiScript>();
        static float nextBusPlacement;

        static Transform StartingBusMarker(poiScript script)
        {
            if (script == null || script.gameObject.name != "haz02(Clone)") return null;
            var helpers = script.GetComponentsInChildren<terrainHeightAlignToBuildingScript>(true);
            foreach (var helper in helpers)
                if (helper != null && helper.transform.parent != null && helper.transform.parent.name == "Buszmegallo")
                    return helper.transform;
            return null;
        }

        public static void QueueBusPlacement(poiScript script)
        {
            var marker = StartingBusMarker(script);
            if (marker == null || busPlacementComplete.Contains(script)) return;
            if (!busPlacementPending.Contains(script)) busPlacementPending.Add(script);
            TryBusPlacement(script);
        }

        public static bool TryBusPlacement(poiScript script)
        {
            if (script == null) return true;
            if (busPlacementComplete.Contains(script)) return true;
            var marker = StartingBusMarker(script);
            if (marker == null) return true;
            var gen = NearGen(Map());
            var global = mainscript.GlobalFromUnityPos(marker.position);
            bool tile, collider;
            double ground = ColliderY(gen, global, out tile, out collider);
            if (double.IsNaN(ground) || double.IsInfinity(ground)) return false;
            var stop = marker.parent;
            var position = stop.position;
            position.y += (float)(ground - global.y);
            stop.position = position;
            busPlacementComplete.Add(script);
            busPlacementPending.Remove(script);
            Debug.Log("[TLD MP bus-placement] placed marker=" + F(global.y) + " ground=" + F(ground));
            return true;
        }

        public static void TickBusPlacement()
        {
            if (Time.unscaledTime < nextBusPlacement) return;
            nextBusPlacement = Time.unscaledTime + PollInterval;
            for (int i = busPlacementPending.Count - 1; i >= 0; i--)
            {
                var script = busPlacementPending[i];
                if (script == null) busPlacementPending.RemoveAt(i);
                else TryBusPlacement(script);
            }
            busPlacementComplete.RemoveWhere(script => script == null);
        }

        // Hook: poiGenScript.AddPoi(Vector3d, Vector3, int, bool) entry. The position is the height placement used.
        public static void Placed(Vector3d pos, Vector3 rot, int id, bool aligned)
        {
            bool bus = id == BusStopId;
            if (bus ? busLines >= MaxBusLines : (poiLines >= MaxPoiLines || placedChecks >= MaxPlacedChecks)) return;
            try
            {
                if (!bus) placedChecks++;
                var map = Map();
                if (map == null || map.terrainGenSettings == null || map.terrainGenSettings.Count == 0 || map.terrainGenSettings[0] == null) return;
                var s = map.terrainGenSettings[0];
                double baseY = s.Height(pos);
                double roadY = s.Height(pos, mapSettingScript.RoadsNear(pos));
                // Other POI types only when the placement height and natural ground disagree noticeably.
                if (!bus && Math.Abs(roadY - baseY) < 2.0 && Math.Abs(pos.y - baseY) < 2.0) return;
                var sb = new StringBuilder(256);
                sb.Append(bus ? "[TLD MP bus-ground]" : "[TLD MP poi-ground]").Append(" phase=placed frame=").Append(Time.frameCount).Append(" id=").Append(id);
                sb.Append(" logical=").Append(V(pos)).Append(" rotY=").Append(F(rot.y)).Append(" roadAligned=").Append(aligned);
                sb.Append(" baseHeight=").Append(F(baseY)).Append(" roadHeight=").Append(F(roadY));
                if (bus) Emit(sb.ToString(), ref busLines, MaxBusLines); else Emit(sb.ToString(), ref poiLines, MaxPoiLines);
            }
            catch (Exception e) { Debug.Log("[TLD MP bus-ground] placed probe failed: " + e.Message); }
        }

        // Hook: poiScript.FStart(int, Vector3d) entry, called from poiGenScript.Spawn.
        public static void Spawned(poiScript script, int genId, Vector3d posID)
        {
            if (script == null || (busLines >= MaxBusLines && poiLines >= MaxPoiLines)) return;
            try
            {
                var map = Map();
                if (map == null || map.poiGens == null || genId < 0 || genId >= map.poiGens.Count || map.poiGens[genId] == null) return;
                var gen = map.poiGens[genId];
                poiGenScript.poiClass poi = null;
                foreach (var chunk in gen.chunks)
                {
                    if (chunk.Value != null && chunk.Value.pois != null && chunk.Value.pois.TryGetValue(posID, out poi) && poi != null) break;
                    poi = null;
                }
                if (poi == null) return;
                if (poi.id == BusStopId) Capture(poi, "spawn");
                bool queued = false;
                for (int i = 0; i < pending.Count; i++) if (pending[i].Poi == poi) queued = true;
                if (!queued && pending.Count < MaxPending)
                    pending.Add(new Pending { Poi = poi, Due = Time.unscaledTime + PollInterval, Step = 0, Waiting = true, Deadline = Time.unscaledTime + WaitLimit });
            }
            catch (Exception e) { Debug.Log("[TLD MP bus-ground] spawn probe failed: " + e.Message); }
        }

        static void Capture(poiGenScript.poiClass poi, string phase)
        {
            var map = Map();
            var near = NearGen(map);
            var sb = new StringBuilder(640);
            bool bus = poi.id == BusStopId;
            sb.Append(bus ? "[TLD MP bus-ground]" : "[TLD MP poi-ground]").Append(" phase=").Append(phase).Append(" frame=").Append(Time.frameCount);
            sb.Append(" id=").Append(poi.id).Append(" name=").Append(poi.poiName);
            if (poi.pobj != null) sb.Append(" fixed=").Append(poi.pobj.isFixPOI ? 1 : 0);
            sb.Append(" posID=").Append(V(poi.posID)).Append(" logical=").Append(V(poi.pos)).Append(" roadAligned=").Append(poi.alignedToRoad);
            double rootY = double.NaN;
            var pobj = poi.pobj;
            bool spawned = pobj != null;
            sb.Append(" spawned=").Append(spawned ? 1 : 0);
            if (spawned)
            {
                Vector3d root = mainscript.GlobalFromUnityPos(pobj.transform.position);
                rootY = root.y;
                sb.Append(" spawnedPos=").Append(V(root)).Append(" spawnedMinusLogicalY=").Append(F(root.y - poi.pos.y));
            }
            bool tile, coll;
            double c = ColliderY(near, poi.pos, out tile, out coll);
            sb.Append(" nearTile=").Append(tile ? 1 : 0).Append(" nearCollider=").Append(coll ? 1 : 0);
            sb.Append(" logicalColliderY=").Append(Num(c));
            if (map != null && map.terrainGenSettings != null && map.terrainGenSettings.Count > 0 && map.terrainGenSettings[0] != null)
            {
                var ts = map.terrainGenSettings[0];
                var roads = mapSettingScript.RoadsNear(poi.pos);
                sb.Append(" heightNoPoi=").Append(F(ts.Height(poi.pos, roads, null)));
                sb.Append(" heightWithPoi=").Append(F(ts.Height(poi.pos, roads, mapSettingScript.POIsNear(poi.pos, near == null ? 100f : near.tileSize))));
            }
            if (poi.terrainAligns != null)
            {
                for (int i = 0; i < poi.terrainAligns.Count && i < 4; i++)
                {
                    var a = poi.terrainAligns[i];
                    if (a == null) continue;
                    c = ColliderY(near, a.gpos, out tile, out coll);
                    sb.Append(" align").Append(i).Append("=").Append(V(a.gpos)).Append(" radii=").Append(F(a.minRadius)).Append(",").Append(F(a.maxRadius));
                    sb.Append(" align").Append(i).Append("ColliderY=").Append(Num(c)).Append(" align").Append(i).Append("MinusCollider=").Append(Num(a.gpos.y - c));
                    if (i == 0 && map != null && map.terrainGenSettings != null && map.terrainGenSettings.Count > 0 && map.terrainGenSettings[0] != null)
                    {
                        var setting = map.terrainGenSettings[0];
                        var nearby = mapSettingScript.POIsNear(a.gpos, near == null ? 100f : near.tileSize);
                        var roadsAt = mapSettingScript.RoadsNear(a.gpos);
                        var direct = new List<poiGenScript.poiClass>(); direct.Add(poi);
                        sb.Append(" alignLookupContains=").Append(nearby != null && nearby.Contains(poi) ? 1 : 0);
                        sb.Append(" alignRange=").Append(F(poi.maxAlignEffectRange));
                        sb.Append(" alignBase=").Append(F(setting.Height(a.gpos)));
                        sb.Append(" alignDirect=").Append(F(setting.Height(a.gpos, direct)));
                        sb.Append(" alignLookup=").Append(F(setting.Height(a.gpos, nearby)));
                        sb.Append(" alignRoadAndDirect=").Append(F(setting.Height(a.gpos, roadsAt, direct)));
                        sb.Append(" alignRoadAndLookup=").Append(F(setting.Height(a.gpos, roadsAt, nearby)));
                    }
                }
            }
            if (spawned)
            {
                // Real floor anchors: the spawned prefab's own helper transforms and its visual bottom.
                var helpers = pobj.GetComponentsInChildren<terrainHeightAlignToBuildingScript>(true);
                for (int i = 0; helpers != null && i < helpers.Length && i < 4; i++)
                {
                    Vector3d h = mainscript.GlobalFromUnityPos(helpers[i].transform.position);
                    c = ColliderY(near, h, out tile, out coll);
                    sb.Append(" helper").Append(i).Append("=").Append(V(h)).Append(" helper").Append(i).Append("ColliderY=").Append(Num(c)).Append(" helper").Append(i).Append("MinusCollider=").Append(Num(h.y - c));
                }
                var renderers = pobj.GetComponentsInChildren<Renderer>(false);
                bool any = false;
                Bounds b = default(Bounds);
                for (int i = 0; renderers != null && i < renderers.Length && i < 64; i++)
                {
                    if (renderers[i] == null || renderers[i] is ParticleSystemRenderer) continue;
                    if (!any) { b = renderers[i].bounds; any = true; } else b.Encapsulate(renderers[i].bounds);
                }
                if (any)
                {
                    Vector3d bottom = mainscript.GlobalFromUnityPos(new Vector3(b.center.x, b.min.y, b.center.z));
                    c = ColliderY(near, bottom, out tile, out coll);
                    sb.Append(" visualBottom=").Append(V(bottom)).Append(" visualBottomColliderY=").Append(Num(c)).Append(" visualBottomMinusCollider=").Append(Num(bottom.y - c));
                    sb.Append(" visualSize=").Append(F(b.size.x)).Append(",").Append(F(b.size.y)).Append(",").Append(F(b.size.z));
                }
                c = ColliderY(near, mainscript.GlobalFromUnityPos(pobj.transform.position), out tile, out coll);
                sb.Append(" rootColliderY=").Append(Num(c)).Append(" rootMinusCollider=").Append(Num(rootY - c));
            }
            if (bus) Emit(sb.ToString(), ref busLines, MaxBusLines); else Emit(sb.ToString(), ref poiLines, MaxPoiLines);
        }

        // Called from the bridge pulse. Cheap when idle: one time compare.
        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (pending.Count > 0)
            {
                var player = mainscript.s == null ? null : mainscript.s.player;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var p = pending[i];
                    if (now < p.Due) continue;
                    if (p.Poi == null || p.Poi.pobj == null || (p.Poi.id == BusStopId ? busLines >= MaxBusLines : poiLines >= MaxPoiLines) || (p.Waiting && now > p.Deadline)) { pending.RemoveAt(i); continue; }
                    try
                    {
                        if (p.Waiting)
                        {
                            p.Due = now + PollInterval;
                            if (player == null) continue;
                            Vector3d pg = mainscript.GlobalFromUnityPos(player.transform.position);
                            double dx = pg.x - p.Poi.pos.x, dz = pg.z - p.Poi.pos.z;
                            bool tile, coll;
                            bool close = dx * dx + dz * dz <= NearRange * NearRange;
                            ColliderY(NearGen(Map()), p.Poi.pos, out tile, out coll);
                            if (!close && !coll) continue;
                            Capture(p.Poi, "hit dist=" + F(Math.Sqrt(dx * dx + dz * dz)));
                            p.Waiting = false; p.Step = 0; p.Due = now + FollowUps[0];
                            continue;
                        }
                        Capture(p.Poi, "follow+" + FollowUps[p.Step].ToString("F1", Invariant) + "s");
                    }
                    catch (Exception e) { Debug.Log("[TLD MP bus-ground] follow-up failed: " + e.Message); pending.RemoveAt(i); continue; }
                    p.Step++;
                    if (p.Step >= FollowUps.Length) pending.RemoveAt(i); else p.Due = now + FollowUps[p.Step] - FollowUps[p.Step - 1];
                }
            }
            if (now >= nextPoiNear) { nextPoiNear = now + LodInterval; try { PoiNear(); } catch (Exception e) { Debug.Log("[TLD MP poi-near] sample failed: " + e.Message); nextPoiNear = now + 30f; } }
            if (now >= nextLod) { nextLod = now + LodInterval; try { SampleLod(); } catch (Exception e) { Debug.Log("[TLD MP terrain-lod] sample failed: " + e.Message); nextLod = now + 30f; } }
        }

        // The POIs nearest the player, once the player has moved far enough since the last line.
        static void PoiNear()
        {
            if (poiNearLines >= MaxPoiNearLines) return;
            var map = Map();
            var player = mainscript.s == null ? null : mainscript.s.player;
            if (map == null || !map.started || player == null || map.poiGens == null) return;
            Vector3d pg = mainscript.GlobalFromUnityPos(player.transform.position);
            if (poiNearSeen)
            {
                double mx = pg.x - poiNearAt.x, mz = pg.z - poiNearAt.z;
                if (mx * mx + mz * mz < PoiNearMove * PoiNearMove) return;
            }
            poiNearSeen = true;
            poiNearAt = pg;
            var near = NearGen(map);
            var best = new List<poiGenScript.poiClass>();
            var bestD = new List<double>();
            for (int gi = 0; gi < map.poiGens.Count; gi++)
            {
                var gen = map.poiGens[gi];
                if (gen == null || gen.chunks == null) continue;
                foreach (var chunk in gen.chunks)
                {
                    if (chunk.Value == null || chunk.Value.pois == null) continue;
                    foreach (var pair in chunk.Value.pois)
                    {
                        var poi = pair.Value;
                        if (poi == null) continue;
                        double dx = poi.pos.x - pg.x, dz = poi.pos.z - pg.z, d2 = dx * dx + dz * dz;
                        if (d2 > PoiNearRange * PoiNearRange) continue;
                        int at = best.Count;
                        while (at > 0 && bestD[at - 1] > d2) at--;
                        if (at >= PoiNearEntries) continue;
                        best.Insert(at, poi); bestD.Insert(at, d2);
                        if (best.Count > PoiNearEntries) { best.RemoveAt(best.Count - 1); bestD.RemoveAt(bestD.Count - 1); }
                    }
                }
            }
            if (best.Count == 0) return;
            var sb = new StringBuilder(512);
            sb.Append("[TLD MP poi-near] frame=").Append(Time.frameCount).Append(" at=").Append(V(pg));
            for (int i = 0; i < best.Count; i++)
            {
                var poi = best[i];
                bool tile, coll;
                double c = ColliderY(near, poi.pos, out tile, out coll);
                sb.Append(" | ").Append(poi.poiName).Append("#").Append(poi.id).Append(" d=").Append(F(Math.Sqrt(bestD[i])));
                sb.Append(" y=").Append(F(poi.pos.y)).Append(" spawned=").Append(poi.pobj != null ? 1 : 0);
                if (poi.pobj != null) sb.Append(" rootY=").Append(F(mainscript.GlobalFromUnityPos(poi.pobj.transform.position).y)).Append(" fixed=").Append(poi.pobj.isFixPOI ? 1 : 0);
                sb.Append(" colliderY=").Append(Num(c)).Append(" yMinusCollider=").Append(Num(poi.pos.y - c));
            }
            Emit(sb.ToString(), ref poiNearLines, MaxPoiNearLines);
        }

        static void SampleLod()
        {
            if (lodLines >= MaxLodLines) return;
            var map = Map();
            var player = mainscript.s == null ? null : mainscript.s.player;
            if (map == null || !map.started || player == null || map.terrainGen == null) return;
            var near = NearGen(map);
            if (near == null) return;
            Transform body = player.Car != null ? player.Car.transform : player.transform;
            Vector3 fwd = body.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; else fwd.Normalize();
            var far = new List<terrainGenScript>();
            for (int i = 0; i < map.terrainGen.Count; i++)
            {
                var g = map.terrainGen[i];
                if (g != null && g.isDistantTerrain && g.terrains != null) far.Add(g);
            }
            if (!lodHeader)
            {
                lodHeader = true;
                var hs = new StringBuilder("[TLD MP terrain-lod] generators");
                for (int i = 0; i < map.terrainGen.Count; i++)
                {
                    var g = map.terrainGen[i];
                    if (g == null) continue;
                    hs.Append(" ").Append(g.terrainName).Append("{tile=").Append(F(g.tileSize)).Append(",res=").Append(g.resolution)
                      .Append(",vertexDist=").Append(F(g.vertexDist)).Append(",collider=").Append(g.hasCollider ? 1 : 0).Append(",distant=").Append(g.isDistantTerrain ? 1 : 0);
                    if (g.material != null)
                    {
                        string[] props = { "_NearDist", "_FarDist", "_Amount" };
                        foreach (string prop in props) if (g.material.HasProperty(prop)) hs.Append(",").Append(prop).Append("=").Append(F(g.material.GetFloat(prop)));
                    }
                    hs.Append("}");
                }
                Emit(hs.ToString(), ref lodLines, MaxLodLines);
            }
            var s = new StringBuilder(256);
            s.Append("[TLD MP terrain-lod] frame=").Append(Time.frameCount).Append(" at=").Append(V(mainscript.GlobalFromUnityPos(body.position)));
            s.Append(" speed=").Append(F(player.Car != null && player.Car.GetComponent<Rigidbody>() != null ? player.Car.GetComponent<Rigidbody>().velocity.magnitude : 0f));
            var settings = map.terrainGenSettings != null && map.terrainGenSettings.Count > 0 ? map.terrainGenSettings[0] : null;
            for (int d = 0; d < LodDistances.Length; d++)
            {
                Vector3d g = mainscript.GlobalFromUnityPos(body.position + fwd * LodDistances[d]);
                bool tile, coll;
                double ny = ColliderY(near, g, out tile, out coll);
                s.Append(" | d=").Append(F(LodDistances[d])).Append(" base=").Append(settings == null ? "n/a" : F(settings.Height(g)));
                s.Append(" near=").Append(Num(ny));
                for (int i = 0; i < far.Count; i++)
                {
                    double fy = MeshY(far[i], g);
                    s.Append(" ").Append(far[i].terrainName).Append("=").Append(Num(fy));
                    if (i == 0 && !double.IsNaN(fy) && !double.IsNaN(ny)) s.Append("(d").Append(F(fy - ny)).Append(")");
                }
            }
            Emit(s.ToString(), ref lodLines, MaxLodLines);
        }
    }
}
