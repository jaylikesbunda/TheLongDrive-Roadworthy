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
                if (Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_DUMP") == "1") { try { DumpFluids(); VerifyBeer(); } catch (Exception e) { Log("dump error " + e); } }
                if(Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_POI")=="12")yield return CheckPoiProps();
                Flush();
            }
            if (originalBiome >= 0 && gamemodesettingsscript.s != null && gamemodesettingsscript.s.S != null) gamemodesettingsscript.s.S.biome = originalBiome;
            Flush();
            Quit();
        }

        // Developer dump: every item prefab with a tank and its random liquids, and
        // every tank in the scene near the start house with what it holds.
        void DumpFluids()
        {
            var db = itemdatabase.s;
            if (db != null && db.items != null)
                for (int i = 0; i < db.items.Length; i++)
                {
                    var item = db.items[i]; if (item == null) continue;
                    foreach (var t in item.GetComponentsInChildren<tankscript>(true))
                    {
                        var sb = new System.Text.StringBuilder();
                        if (t.randomFluids != null) foreach (var r in t.randomFluids) sb.Append(" " + r.type + ":" + F(r.chance));
                        var def = new System.Text.StringBuilder();
                        if (t.defaultFluid != null) foreach (var d in t.defaultFluid) def.Append(" " + d.type + ":" + F(d.amount));
                        Log("item[" + i + "] " + item.name + " tank=" + t.name + " max=" + F(t.F == null ? -1 : t.F.maxC) + " empty=" + F(t.emptyChance) + " mix=" + F(t.mixChance) + " min=" + F(t.minFluid) + " maxFluid=" + F(t.maxFluid) + " always=" + t.alwaysDefaultFluid + " random{" + sb + " } default{" + def + " }");
                    }
                }
            var map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
            if (map != null && map.poiGens != null)
                foreach (var g in map.poiGens)
                {
                    if (g == null || !g.placeStartHouse || g.chunks == null) continue;
                    foreach (var chunk in g.chunks) foreach (var entry in chunk.Value.pois)
                    {
                        var poi = entry.Value;
                        if (poi == null || poi.id != (int)g.startHouseType || poi.pobj == null) continue;
                        var house = poi.pobj.transform;
                        Log("house root " + house.name + " pos=" + house.position + " rot=" + house.eulerAngles);
                        foreach (var sp in house.GetComponentsInChildren<newRandomStuffSpawnScript>(true))
                        {
                            string path = sp.name; for (var q = sp.transform.parent; q != null && q != house; q = q.parent) path = q.name + "/" + path;
                            var sb = new System.Text.StringBuilder();
                            if (sp.items != null) foreach (var it in sp.items)
                            {
                                sb.Append(" [" + (it.G != null ? "G:" + it.G.name : it.prefab) + " isitem=" + it.isitem + " chance=" + it.chance);
                                if (it.overrideFluidType && it.fluids != null) { sb.Append(" fluids:"); foreach (var f in it.fluids) sb.Append(f.type + "/" + F(f.chance) + ","); }
                                if (it.overrideFluidAmount) sb.Append(" amount=" + F(it.minFluid) + "-" + F(it.maxFluid));
                                if (it.places != null) { sb.Append(" places:"); foreach (var pl in it.places) sb.Append(pl.T == null ? "null;" : house.InverseTransformPoint(pl.T.position).ToString("F2") + ";"); }
                                sb.Append("]");
                            }
                            Log("spawner " + path + " local=" + house.InverseTransformPoint(sp.transform.position).ToString("F2") + " chance=" + F(sp.chanceToSpawn) + " spawned=" + sp.spawned + sb);
                        }
                    }
                }
            var player = mainscript.s == null || mainscript.s.player == null ? null : mainscript.s.player.transform;
            if (player == null) return;
            foreach (var t in FindObjectsOfType<tankscript>())
            {
                if (Vector3.Distance(t.transform.position, player.position) > 80f) continue;
                var sb = new System.Text.StringBuilder();
                if (t.F != null && t.F.fluids != null) foreach (var f in t.F.fluids) sb.Append(" " + f.type + ":" + F(f.amount));
                string path = t.name; for (var p = t.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                Log("scene tank " + path + " max=" + F(t.F == null ? -1 : t.F.maxC) + " holds{" + sb + " } dist=" + F(Vector3.Distance(t.transform.position, player.position)));
            }
        }

        sealed class PropSample
        {
            internal tosaveitemscript Item;
            internal Vector3 Start;
            internal float Floor=float.NaN;
            internal bool Crossed;
        }

        IEnumerator CheckPoiProps()
        {
            var map=menuhandler.s.currentMainMap;poiGenScript.poiClass target=null;float nearest=float.MaxValue;
            foreach(var gen in map.poiGens)foreach(var chunk in gen.chunks)foreach(var pair in chunk.Value.pois)
            {
                var poi=pair.Value;if(poi==null || poi.id!=12)continue;
                float distance=Vector3.Distance(mainscript.UnityPosFromGlobal(poi.pos),mainscript.s.player.transform.position);
                if(distance<nearest){nearest=distance;target=poi;}
            }
            if(target==null){Log("FAIL POI probe: no Vizmu generated seed="+map.seed);yield break;}
            Log("POI probe Vizmu seed="+map.seed+" global="+V(target.pos)+" spawnedItems="+target.spawnedItems);
            var player=mainscript.s.player;
            var destination=mainscript.UnityPosFromGlobal(target.pos)+Vector3.up*2f;
            player.Teleport(destination,Vector3.zero);
            var samples=new Dictionary<int,PropSample>();int crossings=0;float until=Time.realtimeSinceStartup+25f;
            while(Time.realtimeSinceStartup<until)
            {
                var root=target.pobj==null?null:target.pobj.transform;
                if(root!=null)
                {
                    // Keep the player at the destination without altering prop physics.
                    foreach(var pair in savedatascript.s.items)
                    {
                        var item=pair.Value;
                        if(item==null || item.RB==null || Vector3.Distance(item.transform.position,root.position)>60f || item.transform.IsChildOf(player.transform))continue;
                        int key=item.GetInstanceID();PropSample sample;
                        var center=item.RB.worldCenterOfMass;
                        if(!samples.TryGetValue(key,out sample))
                        {
                            sample=new PropSample{Item=item,Start=root.InverseTransformPoint(center)};samples.Add(key,sample);
                            foreach(var hit in Physics.RaycastAll(center+Vector3.up*.1f,Vector3.down,30f,~0,QueryTriggerInteraction.Ignore))
                            {
                                if(hit.collider.attachedRigidbody!=null || !hit.collider.transform.IsChildOf(root) || hit.normal.y<.5f)continue;
                                float y=root.InverseTransformPoint(hit.point).y;
                                if(float.IsNaN(sample.Floor) || y>sample.Floor)sample.Floor=y;
                            }
                            Log("POI prop start "+item.name+" id="+item.idInSave+" local="+sample.Start.ToString("F2")+" floor="+F(sample.Floor)+" kinematic="+item.RB.isKinematic+" mode="+item.RB.collisionDetectionMode);
                        }
                        var current=root.InverseTransformPoint(center);
                        if(!sample.Crossed && !float.IsNaN(sample.Floor) && sample.Start.y>=sample.Floor && current.y<sample.Floor-.3f && new Vector2(current.x-sample.Start.x,current.z-sample.Start.z).magnitude<2f)
                        {
                            sample.Crossed=true;string surface;
                            bool blocked=PropSpawnDiagnostics.HasOriginalSurface(root,current,sample.Floor,out surface);
                            if(blocked)crossings++;
                            Log((blocked?"FAIL POI prop crossed floor ":"POI prop left support edge ")+item.name+" id="+item.idInSave+" start="+sample.Start+" now="+current+" floor="+F(sample.Floor)+" surface="+surface);
                        }
                    }
                }
                yield return null;
            }
            var finalRoot=target.pobj==null?null:target.pobj.transform;
            if(finalRoot!=null)foreach(var sample in samples.Values)
            {
                if(sample.Item==null)continue;
                var current=finalRoot.InverseTransformPoint(sample.Item.RB.worldCenterOfMass);
                Log("POI prop end "+sample.Item.name+" id="+sample.Item.idInSave+" drop="+F(sample.Start.y-current.y)+" local="+current.ToString("F2"));
            }
            Log("POI prop probe finished: tracked="+samples.Count+" floorCrossings="+crossings+" spawnedItems="+target.spawnedItems+" spawnedFixed="+target.spawnedFixed);
            Flush();
        }

        void VerifyBeer()
        {
            var main=mainscript.s;var player=main.player;var survival=player.survival;
            bool label=main.fluids.Length>8 && ls.l.g(main.fluids[8].lname)=="Beer";
            int houseBeerBottles=0,houseAlcoholBottles=0;
            foreach(var item in FindObjectsOfType<tosaveitemscript>())
            {
                if(item.id!=71 || Vector3.Distance(item.transform.position,player.transform.position)>20f)continue;
                foreach(var tank in item.GetComponentsInChildren<tankscript>(true))
                {
                    float beer=BeerLiquid.Amount(tank.F),alcohol=tank.F.GetPercent(mainscript.fluidenum.alcohol)*tank.F.GetAmount();
                    if(beer>.01f)houseBeerBottles++;
                    if(alcohol>.01f)houseAlcoholBottles++;
                    Log("house bottle "+item.name+" id="+item.idInSave+" registered="+item.started+" beer="+F(beer)+" alcohol="+F(alcohol));
                }
            }
            Log((houseBeerBottles>0 && houseAlcoholBottles==0?"PASS":"FAIL")+" house bottle contents: beer="+houseBeerBottles+" alcohol="+houseAlcoholBottles);
            int barrels=0;string capText="";
            foreach(var tank in FindObjectsOfType<tankscript>())
            {
                if(!tank.name.StartsWith("BARREL",StringComparison.Ordinal) || Mathf.Abs(BeerLiquid.Amount(tank.F)-40f)>.1f || Vector3.Distance(tank.transform.position,player.transform.position)>40f)continue;
                barrels++;
                foreach(var cap in tank.GetComponentsInChildren<tankcapscript>(true))
                    if(cap.Tank==tank && !cap.noText)capText=cap.GetCapString().Replace("\n"," | ");
                var save=tank.GetComponent<tosaveitemscript>();
                Log("beer barrel id="+(save==null?"missing":save.idInSave.ToString())+" registered="+(save!=null && save.started)+" label="+capText);
            }
            var randomState=UnityEngine.Random.state;GameObject bottle=null;bool bottleBeer=false;
            try
            {
                bottle=Instantiate(itemdatabase.s.items[71],player.transform.position+Vector3.up*100f,Quaternion.identity);
                var tank=bottle.GetComponentInChildren<tankscript>();
                for(int i=0;i<256 && !bottleBeer;i++){tank.RandomFluid();bottleBeer=BeerLiquid.Amount(tank.F)>0f;}
            }
            finally{if(bottle!=null)Destroy(bottle);UnityEngine.Random.state=randomState;}
            float water=survival.water,queued=survival.toDrink,heal=survival.toHp;
            var bladder=new System.Collections.Generic.List<mainscript.fluid>();
            foreach(var f in player.piss.Tank.F.fluids)bladder.Add(new mainscript.fluid(f));
            bool drink=false;
            try
            {
                DrunkEffects.Clear(survival);
                var source=new mainscript.fluidcontainer{maxC=.5f};source.ChangeOne(.5f,BeerLiquid.Fluid);
                var mouth=new mainscript.fluidcontainer{maxC=1f};
                StableGameBridge.NativeFluidFillAllowed(player,null,null,true);
                StableGameBridge.DrinkTransfer(mouth,source,.5f,true,false,null);
                float hydration=survival.toDrink-queued,drunk=DrunkEffects.Level(survival);
                drink=Mathf.Abs(hydration-.45f)<.0001f && Mathf.Abs(drunk-.1f)<.0001f && BeerLiquid.Amount(source)<.0001f && BeerLiquid.Amount(mouth)<.0001f;
                Log("beer drink hydration="+F(hydration)+" drunk="+F(drunk)+" mouth="+F(BeerLiquid.Amount(mouth)));
            }
            finally
            {
                survival.water=water;survival.toDrink=queued;survival.toHp=heal;
                player.piss.Tank.F.Copy(bladder);DrunkEffects.Clear(survival);
            }
            Log((label && bottleBeer && houseBeerBottles>0 && houseAlcoholBottles==0 && barrels==1 && capText.Contains("Beer") && drink?"PASS":"FAIL")+" beer integration: name="+label+" randomizedBottle="+bottleBeer+" beerBarrels="+barrels+" drinking="+drink);
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
