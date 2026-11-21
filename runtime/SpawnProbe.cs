using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Profiling;
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
                if (Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_RENDER") == "1") StartCoroutine(RenderStats());
                if (Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_GRASS") == "1") StartCoroutine(GrassFacts());
                float soak;
                if (float.TryParse(Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_SOAK") ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out soak) && soak > 0f)
                {
                    yield return StartCoroutine(Soak(soak));
                    Flush();
                    continue;
                }
                while (Time.realtimeSinceStartup - started < (Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_AB") == "1" ? 170f : Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_RENDER") == "1" ? 45f : 25f))
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

        // Developer render dump: Unity's own draw/batch counters averaged over
        // 240 frames, the render settings that decide batching, and what the
        // visible renderers are, grouped by mesh and material.
        IEnumerator RenderStats()
        {
            for (int i = 0; i < 600; i++) yield return null; // let the world settle
            string[] names = { "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Instanced Batches Count",
                "Instanced Batched Draw Calls Count", "Static Batches Count", "Static Batched Draw Calls Count",
                "Dynamic Batches Count", "Dynamic Batched Draw Calls Count", "Shadow Casters Count", "Triangles Count" };
            var recorders = new ProfilerRecorder[names.Length];
            for (int i = 0; i < names.Length; i++) recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, names[i]);
            var sums = new double[names.Length]; int frames = 0;
            for (int f = 0; f < 240; f++)
            {
                yield return null;
                for (int i = 0; i < names.Length; i++) if (recorders[i].Valid) sums[i] += recorders[i].LastValue;
                frames++;
            }
            var sb = new System.Text.StringBuilder("render counters avg/frame over " + frames + ":");
            for (int i = 0; i < names.Length; i++) { sb.Append(" " + names[i].Replace(" Count", "").Replace(" ", "") + "=" + (recorders[i].Valid ? F(sums[i] / frames) : "n/a")); recorders[i].Dispose(); }
            Log(sb.ToString());
            try { RenderSettingsDump(); } catch (Exception e) { Log("render dump error " + e); }
            Flush();
            if (Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_AB") == "1") yield return StartCoroutine(RenderAb());
        }

        // Developer A/B: each suspect is measured between two fresh baselines and
        // repeated, so slow drift in frame time cannot pass for a saving. Every
        // change is restored right after its window. Nothing is saved or kept.
        IEnumerator Sample(int frames, double[] into)
        {
            var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var render = ProfilerRecorder.StartNew(ProfilerCategory.Render, "CPU Render Thread Frame Time");
            double m = 0, d = 0, dt = 0, rt = 0; int n = 0;
            for (int f = 0; f < frames; f++)
            {
                yield return null;
                if (main.Valid) m += main.LastValue / 1e6; if (draws.Valid) d += draws.LastValue; if (render.Valid) rt += render.LastValue / 1e6;
                dt += Time.unscaledDeltaTime * 1000.0; n++;
            }
            main.Dispose(); draws.Dispose(); render.Dispose();
            into[0] = m / n; into[1] = d / n; into[2] = dt / n;
            if (into.Length > 3) into[3] = rt / n;
        }
        IEnumerator Pair(string name, Action off, Action on)
        {
            var before = new double[3]; var during = new double[3]; var after = new double[3];
            yield return StartCoroutine(Sample(150, before));
            try { off(); } catch (Exception e) { Log("ab " + name + " off failed " + e.Message); yield break; }
            for (int i = 0; i < 20; i++) yield return null;
            yield return StartCoroutine(Sample(150, during));
            try { on(); } catch (Exception e) { Log("ab " + name + " restore failed " + e.Message); }
            for (int i = 0; i < 20; i++) yield return null;
            yield return StartCoroutine(Sample(150, after));
            double baseMain = (before[0] + after[0]) / 2, baseDraws = (before[1] + after[1]) / 2;
            Log("ab " + name + " mainMs base=" + F(baseMain) + " off=" + F(during[0]) + " delta=" + F(during[0] - baseMain) +
                " draws base=" + F(baseDraws) + " off=" + F(during[1]));
        }

        // Developer rendering price list: each suspect is switched off in
        // alternating windows and restored exactly. Measurement only.
        IEnumerator RenderPrice()
        {
            const int cycles = 6;
            // Cameras other than the main view.
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || cam.name == "Camera") continue;
                var c = cam;
                yield return StartCoroutine(Interleaved("camera off " + c.name, cycles, delegate { c.enabled = false; }, delegate { c.enabled = true; }));
                Flush();
            }
            var shadows = QualitySettings.shadows; var cascades = QualitySettings.shadowCascades; var shadowDist = QualitySettings.shadowDistance;
            yield return StartCoroutine(Interleaved("shadows off", cycles, delegate { QualitySettings.shadows = ShadowQuality.Disable; }, delegate { QualitySettings.shadows = shadows; }));
            yield return StartCoroutine(Interleaved("cascades 4->2", cycles, delegate { QualitySettings.shadowCascades = 2; }, delegate { QualitySettings.shadowCascades = cascades; }));
            yield return StartCoroutine(Interleaved("shadow distance 100->50", cycles, delegate { QualitySettings.shadowDistance = 50f; }, delegate { QualitySettings.shadowDistance = shadowDist; }));
            Flush();

            var all = new List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) if (r != null && r.enabled && !r.forceRenderingOff) all.Add(r);
            int displacement = LayerMask.NameToLayer("displacement");
            var groups = new List<KeyValuePair<string, Func<Renderer, bool>>> {
                new KeyValuePair<string, Func<Renderer, bool>>("cobwebs", r => MatName(r).StartsWith("cobweb")),
                new KeyValuePair<string, Func<Renderer, bool>>("impostors", r => r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.Contains("Impostor")),
                new KeyValuePair<string, Func<Renderer, bool>>("rock spheres", r => { var f = r.GetComponent<MeshFilter>(); return f != null && f.sharedMesh != null && f.sharedMesh.name == "Sphere" && MatName(r).Contains("Rock"); }),
                new KeyValuePair<string, Func<Renderer, bool>>("ropes", r => MatName(r).StartsWith("AmplifyRope")),
                new KeyValuePair<string, Func<Renderer, bool>>("not visible now", r => !r.isVisible && r.gameObject.layer != displacement),
            };
            var sb = new System.Text.StringBuilder("renderprice renderers=" + all.Count);
            foreach (var g in groups)
            {
                var list = all.FindAll(r => { try { return g.Value(r); } catch { return false; } });
                int lod = 0; foreach (var r in list) if (r.GetComponentInParent<LODGroup>() != null) lod++;
                sb.Append(' ').Append(g.Key).Append('=').Append(list.Count).Append("(lod ").Append(lod).Append(')');
                yield return StartCoroutine(Interleaved("hide " + g.Key + " x" + list.Count, cycles,
                    delegate { foreach (var r in list) if (r != null) r.forceRenderingOff = true; },
                    delegate { foreach (var r in list) if (r != null) r.forceRenderingOff = false; }));
                var casting = list.FindAll(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
                var modes = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();
                foreach (var r in casting) modes[r] = r.shadowCastingMode;
                if (casting.Count > 0)
                    yield return StartCoroutine(Interleaved("no shadows " + g.Key + " x" + casting.Count, cycles,
                        delegate { foreach (var r in casting) if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; },
                        delegate { foreach (var pair in modes) if (pair.Key != null) pair.Key.shadowCastingMode = pair.Value; }));
                Flush();
            }
            Log(sb.ToString()); Flush();
        }

        static void SceneCount(System.Text.StringBuilder sb, string name)
        {
            var type = typeof(mainscript).Assembly.GetType(name, false);
            if (type == null) return;
            int n = 0;
            foreach (var o in FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var b = o as MonoBehaviour;
                if (b != null && b.gameObject.activeInHierarchy) n++;
            }
            sb.Append(' ').Append(name).Append('=').Append(n);
        }

        // Steadier A/B under background load: alternate short on and off windows
        // many times, compare each off window with the on windows either side,
        // and report the median of those differences (robust to load bursts).
        IEnumerator Interleaved(string name, int cycles, Action off, Action on)
        {
            const int frames = 120, settle = 15;
            var onW = new List<double[]>(); var offW = new List<double[]>();
            for (int c = 0; c <= cycles; c++)
            {
                for (int i = 0; i < settle; i++) yield return null;
                var a = new double[4]; yield return StartCoroutine(Sample(frames, a)); onW.Add(a);
                if (c == cycles) break;
                try { off(); } catch (Exception e) { Log("ab " + name + " off failed " + e.Message); yield break; }
                for (int i = 0; i < settle; i++) yield return null;
                var b = new double[4]; yield return StartCoroutine(Sample(frames, b)); offW.Add(b);
                try { on(); } catch (Exception e) { Log("ab " + name + " restore failed " + e.Message); yield break; }
            }
            var sb = new System.Text.StringBuilder("abx " + name + " cycles=" + offW.Count);
            string[] labels = { "main", "draws", "frame", "render" };
            foreach (int k in new[] { 0, 2, 3, 1 })
            {
                var deltas = new List<double>(); int positive = 0;
                for (int c = 0; c < offW.Count; c++) { double d = offW[c][k] - (onW[c][k] + onW[c + 1][k]) / 2; deltas.Add(d); if (d > 0) positive++; }
                deltas.Sort();
                double median = deltas.Count == 0 ? 0 : (deltas.Count % 2 == 1 ? deltas[deltas.Count / 2] : (deltas[deltas.Count / 2 - 1] + deltas[deltas.Count / 2]) / 2);
                var base_ = new List<double>(); foreach (var w in onW) base_.Add(w[k]); base_.Sort();
                sb.Append(' ').Append(labels[k]).Append("{base=").Append(F(base_[base_.Count / 2])).Append(" delta=").Append(F(median)).Append(" up=").Append(positive).Append('/').Append(deltas.Count).Append('}');
            }
            Log(sb.ToString());
        }
        static List<Renderer> RenderersWhere(Func<Renderer, bool> pick)
        {
            var list = new List<Renderer>();
            foreach (var r in FindObjectsOfType<Renderer>()) if (r != null && pick(r)) list.Add(r);
            return list;
        }
        static string MatName(Renderer r) { return r.sharedMaterial == null ? "" : r.sharedMaterial.name; }

        // Developer soak: stand still for the given seconds. Every 15 s, log the
        // main thread over the last window and what changed in the scene since
        // the previous census, by object type, so slow growth has a name.
        Dictionary<string, int> lastTypes;
        IEnumerator Soak(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            int round = 0;
            while (Time.realtimeSinceStartup < end)
            {
                var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
                var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                var samples = new List<double>(); double d = 0; int n = 0;
                float windowEnd = Time.realtimeSinceStartup + 14f;
                while (Time.realtimeSinceStartup < windowEnd)
                {
                    yield return null;
                    if (main.Valid) samples.Add(main.LastValue / 1e6); if (draws.Valid) d += draws.LastValue; n++;
                }
                main.Dispose(); draws.Dispose();
                samples.Sort();
                double p50 = samples.Count == 0 ? 0 : samples[samples.Count / 2], p95 = samples.Count == 0 ? 0 : samples[(int)(samples.Count * .95)];
                round++;
                try { SoakCensus(round, p50, p95, n == 0 ? 0 : d / n, n); } catch (Exception e) { Log("soak census error " + e.Message); }
                Flush();
            }
            if (Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_RABBITS") == "1") { yield return StartCoroutine(RabbitAb()); Flush(); }
            if (Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_PERFAB") == "1") { yield return StartCoroutine(PerfAb()); Flush(); }
        }

        // Developer A/B for the performance fixes: pause each between fresh baselines.
        IEnumerator PerfAb()
        {
            Log("perfab grass " + GrassMarkerCulling.Tracked + "/" + GrassMarkerCulling.SwitchedOff); Flush();
            yield return StartCoroutine(Interleaved("grass marker culling paused", 8,
                delegate { GrassMarkerCulling.Paused = true; }, delegate { GrassMarkerCulling.Paused = false; }));
            Flush();
            Log("perfab " + SmallShadowCulling.Summary()); Flush();
            yield return StartCoroutine(Interleaved("small shadow culling paused", 10,
                delegate { SmallShadowCulling.Paused = true; }, delegate { SmallShadowCulling.Paused = false; }));
            Log("perfab " + SmallShadowCulling.Summary()); Flush();
            yield return StartCoroutine(Interleaved("all fixes paused", 10,
                delegate { SmallShadowCulling.Paused = true; GrassMarkerCulling.Paused = true; RoadIdleSkip.Paused = true; LensCameraGate.Paused = true; },
                delegate { SmallShadowCulling.Paused = false; GrassMarkerCulling.Paused = false; RoadIdleSkip.Paused = false; LensCameraGate.Paused = false; }));
            Flush();
            for (int round = 1; round <= 0; round++)
            {
                yield return StartCoroutine(Pair("r" + round + " grass marker culling paused",
                    delegate { GrassMarkerCulling.Paused = true; }, delegate { GrassMarkerCulling.Paused = false; }));
                yield return StartCoroutine(Pair("r" + round + " road idle skip paused",
                    delegate { RoadIdleSkip.Paused = true; }, delegate { RoadIdleSkip.Paused = false; }));
                yield return StartCoroutine(Pair("r" + round + " both paused",
                    delegate { GrassMarkerCulling.Paused = true; RoadIdleSkip.Paused = true; },
                    delegate { GrassMarkerCulling.Paused = false; RoadIdleSkip.Paused = false; }));
                Log("perfab r" + round + " " + RoadIdleSkip.Summary() + " grassTracked=" + GrassMarkerCulling.Tracked + " grassOff=" + GrassMarkerCulling.SwitchedOff);
                Flush();
            }
        }

        // Developer survey: switch off every enabled instance of one script type
        // that has a per-frame method, between fresh baselines, for the busiest
        // types. Only instances switched off here are switched back on.
        static readonly HashSet<string> surveySkip = new HashSet<string> {
            "mainscript", "fpscontroller", "mapSettingScript", "savedatascript", "menuhandler", "settingsscript",
            "itemdatabase", "kaposztaleves", "SpawnProbe" };
        IEnumerator TypeSurvey()
        {
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            var byType = new Dictionary<Type, List<MonoBehaviour>>();
            var perFrame = new Dictionary<Type, bool>();
            foreach (var b in FindObjectsOfType<MonoBehaviour>())
            {
                if (b == null || !b.isActiveAndEnabled) continue;
                var t = b.GetType();
                if (t.Namespace == "TldMp" || surveySkip.Contains(t.Name)) continue;
                bool has;
                if (!perFrame.TryGetValue(t, out has))
                {
                    has = false;
                    for (var k = t; k != null && k != typeof(MonoBehaviour); k = k.BaseType)
                        if (k.GetMethod("Update", any, null, Type.EmptyTypes, null) != null || k.GetMethod("LateUpdate", any, null, Type.EmptyTypes, null) != null ||
                            k.GetMethod("FixedUpdate", any, null, Type.EmptyTypes, null) != null) { has = true; break; }
                    perFrame[t] = has;
                }
                if (!has) continue;
                List<MonoBehaviour> list;
                if (!byType.TryGetValue(t, out list)) byType[t] = list = new List<MonoBehaviour>();
                list.Add(b);
            }
            var types = new List<KeyValuePair<Type, List<MonoBehaviour>>>(byType);
            types.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));
            var sb = new System.Text.StringBuilder("survey types=" + types.Count + " top:");
            for (int i = 0; i < types.Count && i < 40; i++) sb.Append(' ').Append(types[i].Key.Name).Append('=').Append(types[i].Value.Count);
            Log(sb.ToString()); Flush();
            for (int i = 0; i < types.Count && i < 30; i++)
            {
                var entry = types[i];
                var switched = new List<MonoBehaviour>();
                yield return StartCoroutine(Pair("type " + entry.Key.Name + " x" + entry.Value.Count,
                    delegate { foreach (var b in entry.Value) if (b != null && b.enabled) { b.enabled = false; switched.Add(b); } },
                    delegate { foreach (var b in switched) if (b != null) b.enabled = true; }));
                Flush();
            }
        }

        // Developer rabbit A/B: what each part of a live rabbit costs the main thread.
        IEnumerator RabbitAb()
        {
            var rabbits = new List<nyulscript>();
            foreach (var r in FindObjectsOfType<nyulscript>()) if (r != null && r.gameObject.activeInHierarchy) rabbits.Add(r);
            var cam = mainscript.s == null || mainscript.s.player == null ? null : mainscript.s.player.Cam;
            foreach (var r in rabbits)
            {
                var anim = r.GetComponentInChildren<Animator>();
                int bodies = 0, kinematic = 0, sleeping = 0; bool visible = false;
                foreach (var rb in r.GetComponentsInChildren<Rigidbody>()) { bodies++; if (rb.isKinematic) kinematic++; if (rb.IsSleeping()) sleeping++; }
                foreach (var rr in r.GetComponentsInChildren<Renderer>()) if (rr.isVisible) visible = true;
                Log("rabbit " + r.name + " dist=" + (cam == null ? "?" : F(Vector3.Distance(cam.transform.position, r.transform.position))) + " visible=" + visible +
                    " animator=" + (anim == null ? "none" : anim.cullingMode + "/" + anim.enabled) + " bodies=" + bodies + " kinematic=" + kinematic + " sleeping=" + sleeping +
                    " behaviours=" + r.GetComponentsInChildren<MonoBehaviour>().Length);
            }
            if (rabbits.Count == 0) { Log("rabbit ab: no rabbits"); yield break; }
            yield return StartCoroutine(Pair("rabbits all inactive (" + rabbits.Count + ")",
                delegate { foreach (var r in rabbits) if (r != null) r.gameObject.SetActive(false); },
                delegate { foreach (var r in rabbits) if (r != null) r.gameObject.SetActive(true); }));
            var anims = new List<Animator>(); var modes = new List<AnimatorCullingMode>();
            foreach (var r in rabbits) foreach (var a in r.GetComponentsInChildren<Animator>()) { anims.Add(a); modes.Add(a.cullingMode); }
            yield return StartCoroutine(Pair("rabbit animators cull offscreen (" + anims.Count + ")",
                delegate { foreach (var a in anims) if (a != null) a.cullingMode = AnimatorCullingMode.CullCompletely; },
                delegate { for (int i = 0; i < anims.Count; i++) if (anims[i] != null) anims[i].cullingMode = modes[i]; }));
            var natives = new List<Behaviour>();
            foreach (var r in rabbits) { foreach (var b in r.GetComponentsInChildren<aiscript>()) natives.Add(b); foreach (var b in r.GetComponentsInChildren<sensoryscript>()) natives.Add(b); }
            yield return StartCoroutine(Pair("rabbit native ai off (" + natives.Count + ")",
                delegate { foreach (var b in natives) if (b != null) b.enabled = false; },
                delegate { foreach (var b in natives) if (b != null) b.enabled = true; }));
            var ours = new List<Behaviour>();
            foreach (var r in rabbits) foreach (var b in r.GetComponentsInChildren<MonoBehaviour>()) if (b != null && b.GetType().Namespace == "TldMp") ours.Add(b);
            yield return StartCoroutine(Pair("rabbit roadworthy scripts off (" + ours.Count + ")",
                delegate { foreach (var b in ours) if (b != null) b.enabled = false; },
                delegate { foreach (var b in ours) if (b != null) b.enabled = true; }));
            var skinned = new List<SkinnedMeshRenderer>();
            foreach (var r in rabbits) foreach (var m in r.GetComponentsInChildren<SkinnedMeshRenderer>()) if (m.enabled) skinned.Add(m);
            yield return StartCoroutine(Pair("rabbit skinned meshes off (" + skinned.Count + ")",
                delegate { foreach (var m in skinned) if (m != null) m.enabled = false; },
                delegate { foreach (var m in skinned) if (m != null) m.enabled = true; }));
        }

        void SoakCensus(int round, double p50, double p95, double draws, int frames)
        {
            var types = new Dictionary<string, int>();
            int behaviours = 0;
            foreach (var b in FindObjectsOfType<MonoBehaviour>())
            {
                if (b == null) continue; behaviours++;
                string t = b.GetType().Name; int c; types.TryGetValue(t, out c); types[t] = c + 1;
            }
            foreach (var c in FindObjectsOfType<Component>())
            {
                if (c == null || c is MonoBehaviour || c is Transform) continue;
                string t = "[" + c.GetType().Name + "]"; int k; types.TryGetValue(t, out k); types[t] = k + 1;
            }
            int gameObjects = FindObjectsOfType<Transform>().Length;
            var player = mainscript.s == null || mainscript.s.player == null ? null : mainscript.s.player.transform;
            var changes = new System.Text.StringBuilder();
            if (lastTypes != null)
            {
                var keys = new List<string>(types.Keys); foreach (var k in lastTypes.Keys) if (!types.ContainsKey(k)) keys.Add(k);
                var deltas = new List<KeyValuePair<string, int>>();
                foreach (var k in keys)
                {
                    int now, before; types.TryGetValue(k, out now); lastTypes.TryGetValue(k, out before);
                    if (now != before) deltas.Add(new KeyValuePair<string, int>(k, now - before));
                }
                deltas.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)); });
                for (int i = 0; i < deltas.Count && i < 14; i++) changes.Append(" " + deltas[i].Key + (deltas[i].Value > 0 ? "+" : "") + deltas[i].Value);
            }
            lastTypes = types;
            Log("soak #" + round + " t=" + F(Time.realtimeSinceStartup) + " mainMs p50=" + F(p50) + " p95=" + F(p95) + " frames=" + frames + " draws=" + F(draws) +
                " gameObjects=" + gameObjects + " behaviours=" + behaviours + " timeOfDay=" + (napszakvaltakozas.s == null ? "?" : F(napszakvaltakozas.s.currentTime)) +
                " player=" + (player == null ? "?" : player.position.ToString("F1")) + " changes:" + changes);
        }

        // Developer facts for grass-marker culling: cameras, layers, marker owners
        // and how long the discovery calls take.
        IEnumerator GrassFacts()
        {
            for (int i = 0; i < 400; i++) yield return null;
            try
            {
                var main = mainscript.s;
                foreach (var c in Camera.allCameras)
                    Log("grass camera " + c.name + " ortho=" + c.orthographic + " size=" + F(c.orthographicSize) + " aspect=" + F(c.aspect) + " far=" + F(c.farClipPlane) +
                        " mask=0x" + c.cullingMask.ToString("X8") + " pos=" + c.transform.position.ToString("F1") + " rt=" + (c.targetTexture == null ? "none" : c.targetTexture.width + "x" + c.targetTexture.height));
                if (main != null && main.grassCam != null)
                {
                    var gc = main.grassCam.GetComponent<Camera>();
                    Log("grass grassCam object=" + main.grassCam.name + " follow=" + (main.grassCam.follow == null ? "null" : main.grassCam.follow.name) + " height=" + F(main.grassCam.followHeight) +
                        " camera=" + (gc == null ? "none" : gc.name + " size=" + F(gc.orthographicSize) + " mask=0x" + gc.cullingMask.ToString("X8")));
                }
                if (main != null && main.Mirrors != null)
                    foreach (var m in main.Mirrors) if (m != null && m.C != null) { Log("grass mirror camera mask=0x" + m.C.cullingMask.ToString("X8") + " far=" + F(m.C.farClipPlane)); break; }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var all = FindObjectsOfType<Renderer>();
                double findSorted = sw.Elapsed.TotalMilliseconds; sw.Reset(); sw.Start();
                var fast = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                double findFast = sw.Elapsed.TotalMilliseconds;
                var layers = new Dictionary<int, int>(); var owners = new Dictionary<string, int>(); var meshes = new Dictionary<string, int>();
                int markers = 0; var cam = main == null || main.player == null ? null : main.player.Cam;
                sw.Reset(); sw.Start();
                Bounds sink = new Bounds();
                foreach (var r in all)
                {
                    if (r == null || r.sharedMaterial == null || r.sharedMaterial.name.IndexOf("GrassDisplacement", StringComparison.Ordinal) < 0) continue;
                    markers++; sink.Encapsulate(r.bounds.center);
                    int k; layers.TryGetValue(r.gameObject.layer, out k); layers[r.gameObject.layer] = k + 1;
                    var t = r.transform; string owner = t.root == null ? t.name : t.root.name; int o; owners.TryGetValue(owner, out o); owners[owner] = o + 1;
                    var mf = r.GetComponent<MeshFilter>(); string mesh = (r.GetType().Name) + ":" + (mf == null || mf.sharedMesh == null ? "?" : mf.sharedMesh.name) + " parent=" + (t.parent == null ? "-" : t.parent.name) + " self=" + t.name;
                    int mm; meshes.TryGetValue(mesh, out mm); meshes[mesh] = mm + 1;
                }
                double walk = sw.Elapsed.TotalMilliseconds;
                Log("grass discovery renderers=" + all.Length + " findSortedMs=" + F(findSorted) + " findUnsortedMs=" + F(findFast) + " (" + fast.Length + ") markers=" + markers + " markerWalkMs=" + F(walk));
                var sb = new System.Text.StringBuilder(); foreach (var kv in layers) sb.Append(" layer" + kv.Key + "(" + LayerMask.LayerToName(kv.Key) + ")=" + kv.Value);
                Log("grass marker layers:" + sb);
                var ol = new List<KeyValuePair<string, int>>(owners); ol.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
                sb = new System.Text.StringBuilder(); for (int i = 0; i < ol.Count && i < 15; i++) sb.Append(" " + ol[i].Value + "x " + ol[i].Key + ";");
                Log("grass marker roots (" + ol.Count + "):" + sb);
                var ml = new List<KeyValuePair<string, int>>(meshes); ml.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
                sb = new System.Text.StringBuilder(); for (int i = 0; i < ml.Count && i < 10; i++) sb.Append(" " + ml[i].Value + "x " + ml[i].Key + ";");
                Log("grass marker shapes:" + sb);
                if (cam != null)
                {
                    int near = 0; foreach (var r in all) if (r != null && r.sharedMaterial != null && r.sharedMaterial.name.IndexOf("GrassDisplacement", StringComparison.Ordinal) >= 0 && (r.bounds.center - cam.transform.position).sqrMagnitude < 50f * 50f) near++;
                    Log("grass markers within 50 m of the player: " + near);
                }
            }
            catch (Exception e) { Log("grass facts error " + e); }
            Flush();
        }

        void Census(string label)
        {
            var cam = mainscript.s == null || mainscript.s.player == null ? null : mainscript.s.player.Cam;
            int total = 0, active = 0, far = 0, farActive = 0, markers = 0, lod = 0;
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (r == null) continue; total++;
                bool on = r.enabled && r.gameObject.activeInHierarchy; if (on) active++;
                if (MatName(r).IndexOf("GrassDisplacement", StringComparison.Ordinal) >= 0) markers++;
                if (cam != null && Mathf.Sqrt(r.bounds.SqrDistance(cam.transform.position)) > cam.farClipPlane) { far++; if (on) farActive++; }
            }
            lod = FindObjectsOfType<LODGroup>().Length;
            Log("census " + label + " t=" + F(Time.realtimeSinceStartup) + " renderers=" + total + " active=" + active + " beyondFarClip=" + far + " beyondFarClipActive=" + farActive +
                " grassMarkers=" + markers + " lodGroups=" + lod + " rigidbodies=" + FindObjectsOfType<Rigidbody>().Length + " lights=" + FindObjectsOfType<Light>().Length +
                " behaviours=" + FindObjectsOfType<MonoBehaviour>().Length);
        }

        IEnumerator RenderAb()
        {
            var cam = mainscript.s == null || mainscript.s.player == null ? null : mainscript.s.player.Cam;
            if (cam == null) yield break;
            Census("start");
            var shadowsBefore = QualitySettings.shadows;
            for (int round = 1; round <= 2; round++)
            {
                // Renderers that can never appear in the main view: beyond its far clip plane.
                List<Renderer> beyond = RenderersWhere(delegate(Renderer r)
                {
                    return r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) &&
                        Mathf.Sqrt(r.bounds.SqrDistance(cam.transform.position)) > cam.farClipPlane;
                });
                yield return StartCoroutine(Pair("r" + round + " beyond far clip off (" + beyond.Count + ")",
                    delegate { foreach (var r in beyond) if (r != null) r.forceRenderingOff = true; },
                    delegate { foreach (var r in beyond) if (r != null) r.forceRenderingOff = false; }));
                var markers = RenderersWhere(delegate(Renderer r) { return r.enabled && !r.forceRenderingOff && MatName(r).IndexOf("GrassDisplacement", StringComparison.Ordinal) >= 0; });
                yield return StartCoroutine(Pair("r" + round + " grass markers off (" + markers.Count + ")",
                    delegate { foreach (var r in markers) if (r != null) r.forceRenderingOff = true; },
                    delegate { foreach (var r in markers) if (r != null) r.forceRenderingOff = false; }));
                var mirrors = new List<Renderer>();
                if (mainscript.s.Mirrors != null) foreach (var mr in mainscript.s.Mirrors) if (mr != null && mr.R != null && mr.R.enabled) mirrors.Add(mr.R);
                yield return StartCoroutine(Pair("r" + round + " mirrors hidden (" + mirrors.Count + ")",
                    delegate { foreach (var r in mirrors) if (r != null) r.enabled = false; },
                    delegate { foreach (var r in mirrors) if (r != null) r.enabled = true; }));
                yield return StartCoroutine(Pair("r" + round + " shadows off",
                    delegate { QualitySettings.shadows = ShadowQuality.Disable; },
                    delegate { QualitySettings.shadows = shadowsBefore; }));
                Census("after round " + round);
            }
            Flush();
        }

        void RenderSettingsDump()
        {
            var cam = mainscript.s == null || mainscript.s.player == null ? null : mainscript.s.player.Cam;
            if (cam == null) { Log("render: no player camera"); return; }
            Log("render settings path=" + cam.actualRenderingPath + " hdr=" + cam.allowHDR + " far=" + F(cam.farClipPlane) +
                " shadows=" + QualitySettings.shadows + " shadowDistance=" + F(QualitySettings.shadowDistance) + " cascades=" + QualitySettings.shadowCascades +
                " pixelLights=" + QualitySettings.pixelLightCount + " lodBias=" + F(QualitySettings.lodBias) + " msaa=" + QualitySettings.antiAliasing +
                " cameras=" + Camera.allCamerasCount);
            int realtimeLights = 0, shadowLights = 0; var lightInfo = new System.Text.StringBuilder();
            foreach (var l in FindObjectsOfType<Light>())
            {
                if (l == null || !l.isActiveAndEnabled) continue;
                realtimeLights++; if (l.shadows != LightShadows.None) shadowLights++;
                if (realtimeLights <= 12) lightInfo.Append(" [" + l.name + " " + l.type + " mode=" + l.renderMode + " shadows=" + l.shadows + " range=" + F(l.range) + " dist=" + F(Vector3.Distance(l.transform.position, cam.transform.position)) + "]");
            }
            Log("render lights active=" + realtimeLights + " withShadows=" + shadowLights + lightInfo);
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var groups = new Dictionary<string, int[]>(); // key -> {count, propBlock, negScale, lightProbe, instancingOn, shadowCasting}
            int visible = 0;
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                if (Vector3.Distance(r.bounds.center, cam.transform.position) > cam.farClipPlane) continue;
                visible++;
                Mesh mesh = null; var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh;
                var mat = r.sharedMaterial;
                string key = (mesh == null ? "?" : mesh.name) + " | " + (mat == null ? "?" : mat.name) + " | " + (mat == null || mat.shader == null ? "?" : mat.shader.name);
                int[] g; if (!groups.TryGetValue(key, out g)) { g = new int[7]; groups[key] = g; }
                g[0]++;
                if (r.HasPropertyBlock()) g[1]++;
                var sc = r.transform.lossyScale; if (sc.x * sc.y * sc.z < 0) g[2]++;
                if (r.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off) g[3]++;
                if (mat != null && mat.enableInstancing) g[4]++;
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) g[5]++;
                if (r.sharedMaterials != null && r.sharedMaterials.Length > 1) g[6]++;
            }
            var list = new List<KeyValuePair<string, int[]>>(groups);
            list.Sort(delegate(KeyValuePair<string, int[]> a, KeyValuePair<string, int[]> b) { return b.Value[0].CompareTo(a.Value[0]); });
            Log("render visible renderers=" + visible + " groups=" + groups.Count);
            for (int i = 0; i < list.Count && i < 30; i++)
            {
                var g = list[i].Value;
                Log("render group n=" + g[0] + " propBlock=" + g[1] + " negScale=" + g[2] + " lightProbe=" + g[3] + " instancing=" + g[4] + " shadow=" + g[5] + " multiMat=" + g[6] + " :: " + list[i].Key);
            }
            // Where the cobweb-material cubes come from.
            var roots = new Dictionary<string, int>(); var sample = new System.Text.StringBuilder();
            foreach (var r in FindObjectsOfType<MeshRenderer>())
            {
                if (r == null || r.sharedMaterial == null || r.sharedMaterial.name.IndexOf("cobweb", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string path = r.name; var t = r.transform.parent; int depth = 0;
                while (t != null && depth < 3) { path = t.name + "/" + path; t = t.parent; depth++; }
                int n; roots.TryGetValue(path, out n); roots[path] = n + 1;
                if (sample.Length < 600) sample.Append(" [" + path + " size=" + r.bounds.size.ToString("F2") + " active=" + r.gameObject.activeInHierarchy + " enabled=" + r.enabled + "]");
            }
            var rl = new List<KeyValuePair<string, int>>(roots);
            rl.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
            var rs = new System.Text.StringBuilder();
            for (int i = 0; i < rl.Count && i < 12; i++) rs.Append(" " + rl[i].Value + "x " + rl[i].Key + ";");
            Log("render cobweb paths:" + rs);
            Log("render cobweb sample:" + sample);
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
