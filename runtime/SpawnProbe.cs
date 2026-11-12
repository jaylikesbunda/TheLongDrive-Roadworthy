using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TldMp
{
    // Developer-only new-world spawn probe. Never runs unless TLD_MP_SPAWN_PROBE
    // names an output folder. It starts one new world with the seed in
    // TLD_MP_SPAWN_PROBE_SEED, turns autosave off in memory only (the settings
    // file is not written), records the start house and the player's height
    // against the ground for a short time, then quits. Nothing is saved.
    public sealed class SpawnProbe : MonoBehaviour
    {
        public string output;
        readonly List<string> lines = new List<string>();

        public static void StartIfRequested(GameObject host)
        {
            string dir = Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE");
            if (host == null || string.IsNullOrEmpty(dir) || host.GetComponent<SpawnProbe>() != null) return;
            host.AddComponent<SpawnProbe>().output = dir;
        }

        void Start() { StartCoroutine(Run()); }

        void Log(string s)
        {
            string line = Time.realtimeSinceStartup.ToString("F2", CultureInfo.InvariantCulture) + " " + s;
            lines.Add(line);
            Debug.Log("[spawn-probe] " + line);
        }

        void Flush()
        {
            try { Directory.CreateDirectory(output); File.WriteAllLines(Path.Combine(output, "spawn-probe.log"), lines.ToArray()); }
            catch (Exception e) { Debug.LogError("[spawn-probe] " + e); }
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 120f;
            while ((menuhandler.s == null || DataFromMenuScript.s == null || !DataFromMenuScript.s.mainmenu) && Time.realtimeSinceStartup < deadline) yield return null;
            for (int i = 0; i < 120; i++) yield return null;
            if (menuhandler.s == null || DataFromMenuScript.s == null) { Log("FAIL no menu"); Flush(); Quit(); yield break; }
            // A comma list starts each world in turn in the same game session,
            // returning to the main menu (no save) between them.
            string seedText = Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_SEED") ?? "";
            string[] seeds = seedText.Split(',');
            int originalBiome = gamemodesettingsscript.s != null && gamemodesettingsscript.s.S != null ? gamemodesettingsscript.s.S.biome : -1;
            for (int w = 0; w < seeds.Length; w++)
            {
                int seed;
                if (!int.TryParse(seeds[w].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                if (w > 0)
                {
                    menuhandler.s.PressedMainMenu();
                    deadline = Time.realtimeSinceStartup + 120f;
                    for (int i = 0; i < 30; i++) yield return null;
                    while ((menuhandler.s == null || DataFromMenuScript.s == null || !DataFromMenuScript.s.mainmenu) && Time.realtimeSinceStartup < deadline) yield return null;
                    for (int i = 0; i < 120; i++) yield return null;
                    if (menuhandler.s == null) { Log("FAIL menu did not return"); break; }
                }
                Log("start world=" + (w + 1) + " seed=" + seed + " biome=" + WorldBiomePolicy.Mode);
                // Optional biome per world (0 mixed,1 desert,2 winter,3 grass), set in memory only.
                string[] biomes = (Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_BIOME") ?? "").Split(',');
                int biome;
                if (w < biomes.Length && int.TryParse(biomes[w].Trim(), out biome) && biome >= 0 && biome <= 3 && gamemodesettingsscript.s != null && gamemodesettingsscript.s.S != null) gamemodesettingsscript.s.S.biome = biome;
                var previous = mainscript.s;
                DataFromMenuScript.s.seed = seed;
                menuhandler.s.PressedStart();
                deadline = Time.realtimeSinceStartup + 120f;
                // Test switch: mark the start house as already tried before the game
                // places it, recreating a failed road lookup.
                if (Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_SKIPHOUSE") == "1")
                {
                    bool skipped = false;
                    while (!skipped && Time.realtimeSinceStartup < deadline)
                    {
                        var m = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
                        if (m != null && !ReferenceEquals(mainscript.s, previous) && m.poiGens != null)
                            foreach (var g in m.poiGens)
                                if (g != null && g.placeStartHouse && g.startHouseStarted && mainscript.s != null && mainscript.s.player != null)
                                {
                                    // Undo the placement the way a failed road lookup leaves it:
                                    // started, but no house, and the player left where they were.
                                    Vector3d removedKey = default(Vector3d); Vector3d removedChunk = default(Vector3d); bool found = false;
                                    foreach (var chunk in g.chunks)
                                        foreach (var entry in chunk.Value.pois)
                                            if (!found && entry.Value != null && entry.Value.id == (int)g.startHouseType) { removedChunk = chunk.Key; removedKey = entry.Key; found = true; }
                                    if (found)
                                    {
                                        var poi = g.chunks[removedChunk].pois[removedKey];
                                        if (poi.pobj != null) Destroy(poi.pobj.gameObject);
                                        g.chunks[removedChunk].pois.Remove(removedKey);
                                    }
                                    mainscript.s.player.transform.position += Vector3.up * 30f;
                                    Log("skiphouse: removed house=" + found + ", player raised 30m, loading=" + (menuhandler.s.GLoadText != null && menuhandler.s.GLoadText.activeSelf));
                                    skipped = true;
                                }
                        if (!skipped) yield return null;
                    }
                }
                for (int i = 0; i < 5; i++) yield return null;
                while ((mainscript.s == null || ReferenceEquals(mainscript.s, previous) || mainscript.s.player == null || menuhandler.s == null || menuhandler.s.currentMainMap == null) && Time.realtimeSinceStartup < deadline) yield return null;
                if (mainscript.s == null || mainscript.s.player == null) { Log("FAIL world never loaded"); break; }
                try { if (settingsscript.s != null && settingsscript.s.S != null) settingsscript.s.S.BAutoSave = false; } catch { }
                float started = Time.realtimeSinceStartup;
                bool houseLogged = false;
                while (Time.realtimeSinceStartup - started < 25f)
                {
                    try { Sample(ref houseLogged); } catch (Exception e) { Log("sample error " + e.GetType().Name + " " + e.Message); }
                    yield return new WaitForSecondsRealtime(.5f);
                }
                Flush();
            }
            if (originalBiome >= 0 && gamemodesettingsscript.s != null && gamemodesettingsscript.s.S != null) gamemodesettingsscript.s.S.biome = originalBiome;
            Flush();
            Quit();
        }

        void Quit()
        {
            if (Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_QUIT") == "1") Application.Quit();
        }

        void Sample(ref bool houseLogged)
        {
            var map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
            var player = mainscript.s.player;
            Vector3 unity = player.transform.position;
            Vector3d global = mainscript.GlobalFromUnityPos(unity);
            string house = "none";
            if (map != null && map.poiGens != null)
            {
                foreach (var gen in map.poiGens)
                {
                    if (gen == null || !gen.placeStartHouse) continue;
                    house = "started=" + gen.startHouseStarted + " pos=" + (gen.startHousePos == null ? "null" : "set");
                    if (!houseLogged && gen.startHouseStarted)
                    {
                        houseLogged = true;
                        int count = 0; string found = "missing";
                        foreach (var chunk in gen.chunks)
                        {
                            if (chunk.Value == null || chunk.Value.pois == null) continue;
                            foreach (var entry in chunk.Value.pois)
                            {
                                count++;
                                var poi = entry.Value;
                                if (poi != null && poi.id == (int)gen.startHouseType)
                                    found = "at=" + V(poi.pos) + " pobj=" + (poi.pobj != null) + " active=" + (poi.pobj != null && poi.pobj.gameObject.activeInHierarchy);
                            }
                        }
                        Log("house " + found + " biomeMode=" + WorldBiomePolicy.Mode + " pois=" + count + " type=" + gen.startHouseType + " roadgen=" + (gen.roadgen != null) + " roadAffects=" + gen.roadAffects + " seed=" + (map.seed));
                        if (gen.roadgen != null)
                        {
                            int main = 0, all = 0;
                            foreach (var dc in gen.roadgen.chunks)
                            {
                                if (dc.Value == null || dc.Value.roads == null) continue;
                                foreach (var road in dc.Value.roads) { all++; if (road.roadType == roadGenScript.roadTypeEmu.mainRoad) main++; }
                            }
                            Log("roads chunks=" + gen.roadgen.chunks.Count + " roads=" + all + " main=" + main + " hasMain=" + gen.roadgen.hasMainRoad + " hasSide=" + gen.roadgen.hasSideRoad);
                        }
                    }
                }
            }
            double ground = double.NaN; string groundName = "none";
            var hits = Physics.RaycastAll(unity + Vector3.up * 1000f, Vector3.down, 3000f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i].collider; if (c == null) continue;
                if (player.C != null && Array.IndexOf(player.C, c) >= 0) continue;
                if (c.transform.IsChildOf(player.transform)) continue;
                // Highest surface at or below the player's head, else the highest surface.
                float d = hits[i].point.y <= unity.y + 2f ? unity.y - hits[i].point.y : 10000f + hits[i].point.y;
                if (d < best) { best = d; ground = mainscript.GlobalFromUnityPos(hits[i].point).y; groundName = c.name; }
            }
            var rb = player.RB;
            Log("player at=" + V(global) + " vy=" + (rb == null ? "?" : rb.velocity.y.ToString("F2", CultureInfo.InvariantCulture)) +
                " groundY=" + F(ground) + " gap=" + F(global.y - ground) + " on=" + groundName + " grounded=" + player.lastGrounded +
                " kinematic=" + (rb != null && rb.isKinematic) + " house{" + house + "} loading=" + (menuhandler.s.GLoadText != null && menuhandler.s.GLoadText.activeSelf));
        }

        static string F(double v) { return double.IsNaN(v) ? "NaN" : v.ToString("F2", CultureInfo.InvariantCulture); }
        static string V(Vector3d v) { return F(v.x) + "," + F(v.y) + "," + F(v.z); }
    }
}
