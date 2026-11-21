using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace TldMp
{
    // Roadworthy's own roadside buildings (modelled in tools/PoiBuildings/build_pois.py).
    // They are appended to itemdatabase.pois as ordinary POI prefabs, so the native
    // poiGenScript places, streams, saves and syncs them like any vanilla building:
    // terrain is flattened by a terrainHeightAlignToBuildingScript child, and loot
    // comes from newRandomStuffSpawnScript children copied from a fitting vanilla
    // building, run by the host through poiScript.ItemSpawn.
    // Templates are built lazily on the first random POI of a version-6 world.
    public static class CustomPois
    {
        public const string ResourceName = "TldMp.RoadworthyPois.bin";

        sealed class Building
        {
            internal string Name;
            internal float FrontExtent, AlignRadius;
            internal readonly List<KeyValuePair<string, Mesh>> Parts = new List<KeyValuePair<string, Mesh>>();
            internal Mesh Collision;
            internal readonly List<Vector3> Loot = new List<Vector3>();
            internal readonly List<Wreck> Wrecks = new List<Wreck>();
            internal readonly List<Prop> Props = new List<Prop>();
            internal readonly List<Fixture> Fixtures = new List<Fixture>();
            internal readonly List<Prop> Spawns = new List<Prop>();   // Item = vanilla spawner name
            internal readonly List<Door> Doors = new List<Door>();
            internal readonly List<Rect> AreaRects = new List<Rect>();   // x/z footprint of each surface
            internal readonly List<float> AreaHeights = new List<float>();
        }

        // A specific native item or a named vanilla spawn table, depending on the list.
        struct Prop { internal string Item; internal Vector3 Position; internal float Yaw; }

        struct Fixture
        {
            internal string Source, Name;
            internal Vector3 Position, Size, Euler;
            internal bool Collision;
        }

        // A doorway for a real vanilla door: bottom-centre, size, yaw turning +Z to the outside.
        struct Door { internal Vector3 Position; internal float Width, Height, Yaw; }

        // A spot for a real game car body (scrapyard stacks, the hangar's project car).
        struct Wreck { internal Vector3 Position; internal float Yaw, Roll; internal int Column, Level; }

        // Vanilla buildings whose loot tables suit each new building, best first.
        static readonly Dictionary<string, string[]> lootSources = new Dictionary<string, string[]>
        {
            { "rw_chapel", new[] { "oldhouse", "cabin" } },
            { "rw_motel", new[] { "cabin", "oldhouse", "mansion" } },
            { "rw_drivein", new[] { "oldpetrolstation", "muhely", "oldhouse" } },
            { "rw_silos", new[] { "muhely", "oldpetrolstation", "oldhouse" } },
            { "rw_hangar", new[] { "muhely", "oldpetrolstation", "oldhouse" } },
            { "rw_barn", new[] { "cabin", "oldhouse", "muhely" } },
            { "rw_scrapyard", new[] { "muhely", "oldpetrolstation", "oldhouse" } },
            { "rw_railway", new[] { "oldhouse", "muhely", "cabin" } },
        };

        static itemdatabase database;
        static int baseId = -1;
        static List<Building> buildings;
        static bool failed;
        static GameObject holder;
        public static int Registered { get { return buildings == null || baseId < 0 ? 0 : buildings.Count; } }
        public static long Placed;

        public static string Summary() { return "customPois{registered=" + Registered + " placed=" + Placed + (failed ? " failed" : "") + "}"; }

        // Diagnostics: one line per spawned Roadworthy building (capped), so a missing one
        // shows whether it was built, where, and how that compares with the ground there.
        static int spawnLogs;
        sealed class Watched { internal GameObject Go; internal Vector3d PosID; internal bool Near; internal float NextLog; }
        static readonly List<Watched> watched = new List<Watched>();
        static float nextWatch;

        // Every second: report each spawned Roadworthy building once the player is within
        // 150 m (and again every 20 s while near), and when one disappears or is switched off.
        public static void Tick()
        {
            if (watched.Count == 0 || Time.unscaledTime < nextWatch) return;
            nextWatch = Time.unscaledTime + 1f;
            var main = mainscript.s;
            var player = main == null ? null : main.player;
            Vector3 pp = player == null ? Vector3.zero : player.transform.position;
            for (int i = watched.Count - 1; i >= 0; i--)
            {
                var w = watched[i];
                string id = " posID=" + w.PosID.x.ToString("F0") + "," + w.PosID.z.ToString("F0");
                if (w.Go == null) { Debug.Log("[Roadworthy POI] watch: building destroyed" + id); watched.RemoveAt(i); continue; }
                if (!w.Go.activeInHierarchy) { Debug.Log("[Roadworthy POI] watch: " + w.Go.name + " switched off" + id); watched.RemoveAt(i); continue; }
                if (player == null) continue;
                Vector3 p = w.Go.transform.position;
                float d = new Vector2(p.x - pp.x, p.z - pp.z).magnitude;
                if (d > 150f) { w.Near = false; continue; }
                if (w.Near && Time.unscaledTime < w.NextLog) continue;
                w.Near = true; w.NextLog = Time.unscaledTime + 20f;
                var r = w.Go.GetComponent<MeshRenderer>();
                string ground = "none";
                float top = float.NegativeInfinity;
                foreach (var h in Physics.RaycastAll(p + Vector3.up * 500f, Vector3.down, 1000f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider != null && !h.collider.transform.IsChildOf(w.Go.transform) && h.point.y > top) { top = h.point.y; ground = h.point.y.ToString("F1") + " (" + h.collider.name + ")"; }
                Debug.Log("[Roadworthy POI] watch: " + w.Go.name + id + " dist=" + d.ToString("F0") + " building=" + p.ToString("F1") +
                          " player=" + pp.ToString("F1") + " groundAbove/at=" + ground + " visible=" + (r != null && r.isVisible) +
                          " enabled=" + (r != null && r.enabled) + " parent=" + (w.Go.transform.parent == null ? "none" : w.Go.transform.parent.name) +
                          " scale=" + w.Go.transform.lossyScale.ToString("F2"));
            }
        }

        internal static void Spawned(poiScript script, Vector3d posID)
        {
            if (script == null || !script.name.StartsWith("rw_", StringComparison.Ordinal)) return;
            if (watched.Count < 64) watched.Add(new Watched { Go = script.gameObject, PosID = posID });
            if (spawnLogs >= 40) return;
            spawnLogs++;
            var go = script.gameObject;
            var r = go.GetComponent<MeshRenderer>();
            var mf = go.GetComponent<MeshFilter>();
            Vector3 p = go.transform.position;
            string ground = "none";
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 300f, Vector3.down, 600f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider != null && !h.collider.transform.IsChildOf(go.transform) && h.point.y > best) { best = h.point.y; ground = h.point.y.ToString("F1") + " (" + h.collider.name + ")"; }
            var cam = Camera.main;
            ground += " layer=" + go.layer + " drawn=" + (cam == null ? "no camera" : ((cam.cullingMask & (1 << go.layer)) != 0).ToString());
            Debug.Log("[Roadworthy POI] spawned " + go.name + " posID=" + posID.x.ToString("F0") + "," + posID.z.ToString("F0") +
                      " unity=" + p.x.ToString("F1") + "," + p.y.ToString("F1") + "," + p.z.ToString("F1") + " groundHit=" + ground +
                      " active=" + go.activeInHierarchy + " renderer=" + (r == null ? "none" : r.enabled + " bounds=" + r.bounds.size.ToString("F1")) +
                      " verts=" + (mf == null || mf.sharedMesh == null ? 0 : mf.sharedMesh.vertexCount) +
                      " mats=" + (r == null ? 0 : r.sharedMaterials.Length) + " children=" + go.transform.childCount);
        }

        // Decide whether this random roadside POI becomes a Roadworthy building.
        internal static bool Choose(poiGenScript gen, Vector3d pos, int vanillaType, bool aligned, out int id, out double frontExtent)
        {
            id = -1; frontExtent = 0;
            if (gen == null || gen.mapSetting == null) return false;
            string vanilla = Enum.IsDefined(typeof(itemdatabase.b), vanillaType) ? ((itemdatabase.b)vanillaType).ToString() : null;
            // Cheap checks first, so worlds and types that never swap never load the meshes.
            if (CustomPoiPolicy.Pick(WorldPlacementPolicy.Version, gen.mapSetting.seed, pos.x, pos.z, vanilla, aligned, 1) < 0) return false;
            if (!EnsureRegistered()) return false;
            int index = CustomPoiPolicy.Pick(WorldPlacementPolicy.Version, gen.mapSetting.seed, pos.x, pos.z, vanilla, aligned, buildings.Count);
            if (index < 0) return false;
            id = baseId + index;
            frontExtent = buildings[index].FrontExtent;
            Placed++;
            return true;
        }

        static bool EnsureRegistered()
        {
            var db = itemdatabase.s;
            if (db == null || db.pois == null) return false;
            if (db == database && baseId >= 0) return true;
            if (failed && db == database) return false;
            database = db; baseId = -1; failed = false;
            try
            {
                if (buildings == null) buildings = Load();
                if (holder != null) UnityEngine.Object.Destroy(holder);
                holder = new GameObject("RoadworthyPoiTemplates");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
                var materials = MaterialLookup();
                materialsForGrass = materials;
                int layer = BuildingLayer(db);
                var templates = new GameObject[buildings.Count];
                for (int i = 0; i < buildings.Count; i++) templates[i] = Template(db, buildings[i], materials, layer);
                int start = db.pois.Length;
                var grown = new GameObject[start + templates.Length];
                Array.Copy(db.pois, grown, start);
                Array.Copy(templates, 0, grown, start, templates.Length);
                db.pois = grown;
                baseId = start;
                Debug.Log("[Roadworthy POI] registered " + templates.Length + " buildings from id " + start);
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Debug.LogWarning("[Roadworthy POI] buildings unavailable; keeping vanilla POIs: " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        // ---- template construction ----------------------------------------------
        static GameObject Template(itemdatabase db, Building b, Dictionary<string, Material> materials, int layer)
        {
            var root = new GameObject(b.Name);
            root.layer = layer;
            root.transform.SetParent(holder.transform, false);
            var combined = new Mesh { name = b.Name };
            var mats = new Material[b.Parts.Count];
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
            var subs = new List<int[]>();
            for (int i = 0; i < b.Parts.Count; i++)
            {
                var part = b.Parts[i].Value;
                int offset = vertices.Count;
                vertices.AddRange(part.vertices); normals.AddRange(part.normals); uvs.AddRange(part.uv);
                var tris = part.triangles;
                for (int k = 0; k < tris.Length; k++) tris[k] += offset;
                subs.Add(tris);
                mats[i] = Find(materials, b.Parts[i].Key);
            }
            if (vertices.Count > 65000) combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combined.SetVertices(vertices); combined.SetNormals(normals); combined.SetUVs(0, uvs);
            combined.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) combined.SetTriangles(subs[i], i);
            combined.RecalculateBounds(); combined.RecalculateTangents();
            root.AddComponent<MeshFilter>().sharedMesh = combined;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = mats;
            var collider = root.AddComponent<MeshCollider>();
            collider.sharedMesh = b.Collision;

            var poi = root.AddComponent<poiScript>();
            poi.usables = new List<usablescript>(); poi.partslots = new List<partslotscript>();
            poi.tanks = new List<tankscript>(); poi.seats = new List<seatscript>();
            poi.ropes = new List<ropeScript>(); poi.randoms = new List<randomTypeSelector>();
            poi.cols = new List<Collider>();

            AddTerrainAligns(root, b.Collision != null ? b.Collision.bounds : combined.bounds);

            // Like every vanilla POI root: register with the world recentring (visszarako),
            // or the building is left behind at old coordinates when the origin moves.
            CopyRecentring(db, root);
            AddNoObjQuad(root, combined.bounds);

            AddVanillaSpawners(db, b, root);
            AddProps(db, b, root);
            AddNativeSpawns(db, b, root);
            AddFixtures(db, b, root);
            AddDoors(db, b, root, poi);
            AddWrecks(db, b, root, layer, materials);
            return root;
        }

        // ---- wrecks ------------------------------------------------------------
        // Loose spots (a column with one car) get a native spawner, like vanilla POI cars:
        // the host rolls the game's own "kaszni" (bare shell) or "car" (random car with
        // some random parts, repairable) category, and the car is a normal saved item.
        // Stacks are decoration: static copies of real bodies, picked per POI instance
        // from the "kaszni" category with a seed from the POI's position, so every peer
        // builds the same yard (RoadworthyWreckYard, below).
        public const int ShellChance = 55, CarChance = 45;
        static readonly Dictionary<string, List<Wreck>> yardStacks = new Dictionary<string, List<Wreck>>();

        static void AddWrecks(itemdatabase db, Building b, GameObject root, int layer, Dictionary<string, Material> materials)
        {
            if (b.Wrecks.Count == 0) return;
            var perColumn = new Dictionary<int, int>();
            foreach (var w in b.Wrecks) perColumn[w.Column] = perColumn.ContainsKey(w.Column) ? perColumn[w.Column] + 1 : 1;
            var stacks = new List<Wreck>();
            int n = 0;
            foreach (var w in b.Wrecks)
            {
                if (perColumn[w.Column] > 1) { stacks.Add(w); continue; }
                var go = new GameObject("CarSpawn" + n++);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = w.Position + Vector3.up * 0.6f;   // dropped onto the ground, not into it
                go.transform.localRotation = Quaternion.Euler(0f, w.Yaw, 0f);
                var spawner = go.AddComponent<newRandomStuffSpawnScript>();
                spawner.chanceToSpawn = 1f;
                var here = new[] { new newRandomStuffSpawnScript.place { chance = 0, T = go.transform } };
                spawner.items = new[]
                {
                    new newRandomStuffSpawnScript.item { isitem = false, prefab = itemdatabase.k.kaszni.ToString(), chance = ShellChance, places = here },
                    new newRandomStuffSpawnScript.item { isitem = false, prefab = itemdatabase.k.car.ToString(), chance = CarChance, places = here },
                };
            }
            if (stacks.Count == 0) return;
            stacks.Sort((x, y) => x.Column != y.Column ? x.Column.CompareTo(y.Column) : x.Level.CompareTo(y.Level));
            yardStacks[b.Name] = stacks;
            root.AddComponent<RoadworthyWreckYard>();
        }

        // Called from RoadworthyWreckYard.Start on each spawned POI (all peers).
        internal static void BuildYard(GameObject yard)
        {
            string name = yard.name.Replace("(Clone)", "").Trim();
            List<Wreck> stacks;
            if (!yardStacks.TryGetValue(name, out stacks) || itemdatabase.s == null) return;
            var poi = yard.GetComponent<poiScript>();
            ulong seed = poi == null ? 0UL : CustomPoiPolicy.Hash(0x5752, (long)Math.Round(poi.posID.x), (long)Math.Round(poi.posID.z));
            var rnd = new System.Random((int)(seed & 0x7FFFFFFF));
            var tops = new Dictionary<int, float>();
            int n = 0;
            foreach (var w in stacks)
            {
                GameObject prefab = PickShell(itemdatabase.s, rnd);
                if (prefab == null) continue;
                try
                {
                    Bounds bounds;
                    var slot = new GameObject("Wreck" + n++);
                    slot.layer = yard.layer;
                    slot.transform.SetParent(yard.transform, false);
                    var body = BuildBody(prefab, yard.layer, slot.transform, out bounds);
                    if (body == null) { UnityEngine.Object.Destroy(slot); continue; }
                    float baseY = w.Level == 0 || !tops.ContainsKey(w.Column) ? w.Position.y : tops[w.Column];
                    slot.transform.localPosition = new Vector3(w.Position.x, baseY, w.Position.z);
                    slot.transform.localRotation = Quaternion.Euler(0f, w.Yaw, 0f) * Quaternion.Euler(0f, 0f, w.Roll);
                    body.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                    tops[w.Column] = baseY + bounds.size.y * 0.92f;   // stacked shells settle into each other
                }
                catch (Exception e) { Debug.LogWarning("[Roadworthy POI] skipped a stacked wreck (" + prefab.name + "): " + e.Message); }
            }
        }

        // The game's "kaszni" category, weighted like itemdatabase.selectId but with our seed.
        static GameObject PickShell(itemdatabase db, System.Random rnd)
        {
            int id = PickFromCategory(db, (int)itemdatabase.k.kaszni, rnd, 0);
            if (id >= 0 && db.items != null && id < db.items.Length && db.items[id] != null) return db.items[id];
            var fallback = CarBodies(db);
            return fallback.Count == 0 ? null : fallback[rnd.Next(fallback.Count)];
        }

        static int PickFromCategory(itemdatabase db, int category, System.Random rnd, int depth)
        {
            if (db.categories == null || category < 0 || category >= db.categories.Length || depth > 4) return -1;
            var c = db.categories[category];
            if (c == null) return -1;
            int items = c.items == null ? 0 : c.items.Length, subs = c.categories == null ? 0 : c.categories.Length;
            int total = 0;
            for (int i = 0; i < items; i++) total += c.itemchances == null ? 1 : Math.Max(0, c.itemchances[i]);
            int itemTotal = total;
            for (int i = 0; i < subs; i++) total += c.categorychances == null ? 1 : Math.Max(0, c.categorychances[i]);
            if (total <= 0) return -1;
            int roll = rnd.Next(1, total + 1), acc = 0;
            if (roll <= itemTotal)
            {
                for (int i = 0; i < items; i++)
                {
                    acc += c.itemchances == null ? 1 : Math.Max(0, c.itemchances[i]);
                    if (roll <= acc) return c.items[i];
                }
                return -1;
            }
            acc = itemTotal;
            for (int i = 0; i < subs; i++)
            {
                acc += c.categorychances == null ? 1 : Math.Max(0, c.categorychances[i]);
                if (roll <= acc) return PickFromCategory(db, c.categories[i], rnd, depth + 1);
            }
            return -1;
        }

        static List<GameObject> carBodies;
        static List<GameObject> CarBodies(itemdatabase db)
        {
            if (carBodies != null) return carBodies;
            carBodies = new List<GameObject>();
            if (db.items == null) return carBodies;
            foreach (var item in db.items)
            {
                if (item == null) continue;
                var car = item.GetComponent<carscript>();
                if (car == null) continue;
                string name = item.name.ToLowerInvariant();
                if (name.Contains("full") || name.Contains("bike") || name.Contains("bus") || name.Contains("slide")) continue;
                carBodies.Add(item);
            }
            carBodies.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
            return carBodies;
        }

        // A static, rusty copy of a car's body: the body condition's renderers when the
        // prefab is a car, otherwise every mesh in the prefab. Box colliders only.
        static GameObject BuildBody(GameObject prefab, int layer, Transform parent, out Bounds bounds)
        {
            bounds = new Bounds();
            var car = prefab.GetComponent<carscript>();
            var pc = car != null && car.condition != null ? car.condition : prefab.GetComponent<partconditionscript>();
            var renderers = new List<Renderer>();
            if (pc != null && pc.renderers != null && pc.renderers.Count > 0) renderers.AddRange(pc.renderers);
            else renderers.AddRange(prefab.GetComponentsInChildren<MeshRenderer>(false));
            var body = new GameObject("Body_" + prefab.name);
            body.layer = layer;
            body.transform.SetParent(parent, false);
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            bool any = false;
            foreach (var r in renderers)
            {
                var mr = r as MeshRenderer;
                var mf = mr == null ? null : mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var owner = r.GetComponentInParent<partconditionscript>();
                if (owner == null) owner = pc;
                Material rusty = owner == null ? null : (owner.mRusty != null ? owner.mRusty : owner.defRusty);
                var part = new GameObject(r.name);
                part.layer = layer;
                part.transform.SetParent(body.transform, false);
                Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mats = (Material[])mr.sharedMaterials.Clone();
                if (rusty != null)
                    for (int i = 0; i < mats.Length; i++)
                        if (IsConditionMaterial(mats[i], owner) || (mats.Length == 1 && owner == pc)) mats[i] = rusty;
                part.AddComponent<MeshRenderer>().sharedMaterials = mats;
                var box = part.AddComponent<BoxCollider>();
                var mb = mf.sharedMesh.bounds;
                box.center = mb.center; box.size = mb.size;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
                }
            }
            if (!any) { UnityEngine.Object.Destroy(body); return null; }
            return body;
        }

        static bool IsConditionMaterial(Material m, partconditionscript pc)
        {
            return m != null && pc != null && (m == pc.mNew || m == pc.mUsed || m == pc.mMiddle || m == pc.mOld || m == pc.mRusty ||
                                 m == pc.defNew || m == pc.defUsed || m == pc.defMiddle || m == pc.defOld || m == pc.defRusty);
        }

        static void AddLoot(itemdatabase db, Building b, GameObject root)
        {
            var sources = LootSpawners(db, b.Name);
            if (sources.Count == 0 || b.Loot.Count == 0) return;
            for (int i = 0; i < b.Loot.Count; i++)
            {
                var go = new GameObject("Loot" + i);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = b.Loot[i];
                go.transform.localRotation = Quaternion.Euler(0f, i * 73f, 0f);
                CopySpawner(sources[i % sources.Count], go.AddComponent<newRandomStuffSpawnScript>(), go.transform);
            }
        }

        // Ground smoothing. The terrain is fully flattened to the building's height within
        // each terrainHeightAlignToBuildingScript's radius (its localScale.x) and blended
        // back beyond it (poiGenScript.TerrainAlignsValue). One circle cannot cover a long
        // building, so the footprint is covered with a grid of overlapping circles about
        // CellSize apart, each reaching past its cell's corners.
        // Small, tight cells: each circle also blends the ground over a multiple of its own
        // radius, so big circles flattened far beyond the building.
        const float CellSize = 4f, AlignMargin = 0.4f;

        static void AddTerrainAligns(GameObject root, Bounds footprint)
        {
            float minX = footprint.min.x, maxX = footprint.max.x, minZ = footprint.min.z, maxZ = footprint.max.z;
            int nx = Math.Max(1, Mathf.CeilToInt((maxX - minX) / CellSize)), nz = Math.Max(1, Mathf.CeilToInt((maxZ - minZ) / CellSize));
            float cw = (maxX - minX) / nx, cd = (maxZ - minZ) / nz;
            float radius = 0.5f * Mathf.Sqrt(cw * cw + cd * cd) + AlignMargin;
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    var align = new GameObject("TerrainAlign" + i + "_" + k);
                    align.transform.SetParent(root.transform, false);
                    align.transform.localPosition = new Vector3(minX + (i + 0.5f) * cw, 0f, minZ + (k + 0.5f) * cd);
                    align.transform.localScale = new Vector3(radius, 1f, radius);
                    align.AddComponent<terrainHeightAlignToBuildingScript>();
                }
        }

        // visszarako ("put back") on the root, set up like the vanilla old house's. The game
        // shifts everything registered with it whenever it recentres the world origin.
        static void CopyRecentring(itemdatabase db, GameObject root)
        {
            visszarako source = null;
            int id = (int)itemdatabase.b.oldhouse;
            if (id < db.pois.Length && db.pois[id] != null) source = db.pois[id].GetComponent<visszarako>();
            var v = root.AddComponent<visszarako>();
            if (source == null) return;
            foreach (var f in typeof(visszarako).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name == "RB" || f.Name == "coord" || f.Name == "started") continue;
                f.SetValue(v, f.GetValue(source));
            }
        }

        // A hidden quad over the footprint that tells the world generator to keep its
        // procedural props (trees, rocks, bushes) off the building, as vanilla POIs do.
        static void AddNoObjQuad(GameObject root, Bounds local)
        {
            var go = new GameObject("ObjRemoveQuad");
            go.SetActive(false);
            go.transform.SetParent(root.transform, false);
            float x0 = local.min.x - 1f, x1 = local.max.x + 1f, z0 = local.min.z - 1f, z1 = local.max.z + 1f;
            var mesh = new Mesh { name = root.name + "_noObj" };
            mesh.vertices = new[] { new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.enabled = false;
            var quad = go.AddComponent<POIQuadScript>();
            quad.tip = POIQuadScript.quadType.noObj; quad.R = mr; quad.MF = mf;

            // Grass displacement over the same footprint (only the grass camera draws the
            // "displacement" layer), so grass does not grow through the floors.
            int displacement = LayerMask.NameToLayer("displacement");
            Material push;
            if (displacement < 0 || materialsForGrass == null || !materialsForGrass.TryGetValue("GrassDisplacementRed", out push)) return;
            var grass = new GameObject("grassDisplacement");
            grass.layer = displacement;
            grass.transform.SetParent(root.transform, false);
            grass.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            grass.AddComponent<MeshFilter>().sharedMesh = mesh;
            var gr = grass.AddComponent<MeshRenderer>();
            gr.sharedMaterial = push;
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gr.receiveShadows = false;
        }
        static Dictionary<string, Material> materialsForGrass;

        // Fixed items through the same native spawner as loot: always
        // this one item at this spot, spawned once by the host and saved like any item.
        static void AddProps(itemdatabase db, Building b, GameObject root)
        {
            for (int i = 0; i < b.Props.Count; i++)
            {
                var prop = b.Props[i];
                itemdatabase.i id;
                try { id = (itemdatabase.i)Enum.Parse(typeof(itemdatabase.i), prop.Item); }
                catch { Debug.LogWarning("[Roadworthy POI] unknown item " + prop.Item + " in " + b.Name); continue; }
                if (!Enum.IsDefined(typeof(itemdatabase.i), id) || (int)id < 0 ||
                    (int)id >= db.items.Length || db.items[(int)id] == null)
                {
                    Debug.LogWarning("[Roadworthy POI] unavailable item " + prop.Item + " in " + b.Name);
                    continue;
                }
                var go = new GameObject("Prop" + i);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = prop.Position + Vector3.up * ItemDepth(db, (int)id);
                go.transform.localRotation = Quaternion.Euler(0f, prop.Yaw, 0f);
                var spawner = go.AddComponent<newRandomStuffSpawnScript>();
                spawner.chanceToSpawn = 1f;
                spawner.items = new[]
                {
                    new newRandomStuffSpawnScript.item
                    {
                        isitem = true, prefab = prop.Item, chance = 0,
                        places = new[] { new newRandomStuffSpawnScript.place { chance = 0, T = go.transform } }
                    }
                };
            }
        }

        static GameObject FixtureSource(itemdatabase db, Fixture fixture)
        {
            if (fixture.Source == "door") return DoorSource(db);
            GameObject prefab;
            string meshName = fixture.Name;
            if (fixture.Source == "item")
            {
                int separator = meshName.IndexOf('/');
                string itemName = separator < 0 ? meshName : meshName.Substring(0, separator);
                itemdatabase.i item;
                if (!Enum.TryParse(itemName, out item) || (int)item < 0 || (int)item >= db.items.Length) return null;
                prefab = db.items[(int)item];
                if (separator < 0) return prefab;
                meshName = meshName.Substring(separator + 1);
            }
            else
            {
                itemdatabase.b poi;
                if (!Enum.TryParse(fixture.Source, out poi) || (int)poi < 0 || (int)poi >= baseIdOrLength(db)) return null;
                prefab = db.pois[(int)poi];
            }
            if (prefab == null) return null;
            foreach (var child in prefab.GetComponentsInChildren<Transform>(true))
                if (child.name == meshName) return child.gameObject;
            return null;
        }

        static Mesh garageShelf;

        static Mesh StarterGarageShelf(GameObject source)
        {
            if (garageShelf != null) return garageShelf;
            var filter = source.GetComponent<MeshFilter>();
            var mesh = filter == null ? null : filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) return null;

            // The four tiers and their uprights are a single section of the
            // starting garage's combined wood mesh, in its local coordinates.
            var clip = new Bounds();
            clip.SetMinMax(new Vector3(-1.50f, -0.11f, -6.96f), new Vector3(1.44f, 2.02f, -6.59f));
            var positions = mesh.vertices;
            var normals = mesh.normals;
            var uv = mesh.uv;
            var tangents = mesh.tangents;
            var used = new Dictionary<int, int>();
            var clippedPositions = new List<Vector3>();
            var clippedNormals = new List<Vector3>();
            var clippedUv = new List<Vector2>();
            var clippedTangents = new List<Vector4>();
            var submeshes = new List<int>[mesh.subMeshCount];
            for (int submesh = 0; submesh < submeshes.Length; submesh++)
            {
                var triangles = mesh.GetTriangles(submesh);
                var kept = submeshes[submesh] = new List<int>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    if (!clip.Contains(positions[triangles[i]]) ||
                        !clip.Contains(positions[triangles[i + 1]]) ||
                        !clip.Contains(positions[triangles[i + 2]])) continue;
                    for (int vertex = i; vertex < i + 3; vertex++)
                    {
                        int original = triangles[vertex], mapped;
                        if (!used.TryGetValue(original, out mapped))
                        {
                            used.Add(original, mapped = clippedPositions.Count);
                            clippedPositions.Add(positions[original]);
                            if (normals.Length == positions.Length) clippedNormals.Add(normals[original]);
                            if (uv.Length == positions.Length) clippedUv.Add(uv[original]);
                            if (tangents.Length == positions.Length) clippedTangents.Add(tangents[original]);
                        }
                        kept.Add(mapped);
                    }
                }
            }
            if (clippedPositions.Count == 0) return null;
            garageShelf = new Mesh { name = "Vanilla starting garage shelf", subMeshCount = submeshes.Length };
            garageShelf.SetVertices(clippedPositions);
            if (clippedNormals.Count > 0) garageShelf.SetNormals(clippedNormals);
            if (clippedUv.Count > 0) garageShelf.SetUVs(0, clippedUv);
            if (clippedTangents.Count > 0) garageShelf.SetTangents(clippedTangents);
            for (int i = 0; i < submeshes.Length; i++) garageShelf.SetTriangles(submeshes[i], i, false);
            if (clippedNormals.Count == 0) garageShelf.RecalculateNormals();
            garageShelf.RecalculateBounds();
            return garageShelf;
        }

        static void AddFixtures(itemdatabase db, Building b, GameObject root)
        {
            foreach (var fixture in b.Fixtures)
            {
                var source = FixtureSource(db, fixture);
                if (source == null)
                {
                    Debug.LogWarning("[Roadworthy POI] missing fixture " + fixture.Source + "/" + fixture.Name + " in " + b.Name);
                    continue;
                }
                bool isGarageShelf = fixture.Source == "kezdohaz" && fixture.Name == "fa anyagok";
                var shelfMesh = isGarageShelf ? StarterGarageShelf(source) : null;
                if (isGarageShelf && shelfMesh == null)
                {
                    Debug.LogWarning("[Roadworthy POI] starting garage shelf mesh unavailable in " + b.Name);
                    continue;
                }
                var bounds = isGarageShelf ? shelfMesh.bounds : LocalBounds(source, source.transform);
                // Some vanilla door meshes lie flat in prefab space and their hinge
                // transform stands them upright. Fit the leaf flat along +Z in either case.
                var sourcePose = Quaternion.identity;
                var size = bounds.size;
                if (fixture.Source == "door")
                {
                    if (size.x < size.y && size.x < size.z) sourcePose = Quaternion.Euler(0f, 0f, 90f);
                    else if (size.z < size.y) sourcePose = Quaternion.Euler(90f, 0f, 0f);
                    var flat = sourcePose * size;
                    if (Mathf.Abs(flat.x) > Mathf.Abs(flat.z)) sourcePose = Quaternion.Euler(0f, 90f, 0f) * sourcePose;
                }
                var extents = sourcePose * size;
                extents = new Vector3(Mathf.Abs(extents.x), Mathf.Abs(extents.y), Mathf.Abs(extents.z));
                var fit = new GameObject("Fixture_" + source.name);
                fit.layer = root.layer;
                fit.transform.SetParent(root.transform, false);
                fit.transform.localPosition = fixture.Position;
                fit.transform.localRotation = Quaternion.Euler(fixture.Euler);
                fit.transform.localScale = DivideScale(fixture.Size, extents);
                var toSource = source.transform.worldToLocalMatrix;
                foreach (var render in source.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = render.GetComponent<MeshFilter>();
                    if (!render.enabled || filter == null || filter.sharedMesh == null ||
                        render.name.StartsWith("cobweb", StringComparison.OrdinalIgnoreCase)) continue;
                    var mesh = filter.sharedMesh;
                    if (isGarageShelf)
                    {
                        if (render.gameObject != source) continue;
                        mesh = shelfMesh;
                    }
                    var part = new GameObject(render.name);
                    part.layer = root.layer;
                    part.transform.SetParent(fit.transform, false);
                    var relative = toSource * render.transform.localToWorldMatrix;
                    part.transform.localPosition = sourcePose * ((Vector3)relative.GetColumn(3) - bounds.center);
                    part.transform.localRotation = sourcePose * relative.rotation;
                    part.transform.localScale = relative.lossyScale;
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    part.AddComponent<MeshRenderer>().sharedMaterials = render.sharedMaterials;
                    if (fixture.Collision) part.AddComponent<MeshCollider>().sharedMesh = mesh;
                }
            }
        }

        static List<newRandomStuffSpawnScript> LootSpawners(itemdatabase db, string building)
        {
            var found = new List<newRandomStuffSpawnScript>();
            string[] names;
            if (!lootSources.TryGetValue(building, out names)) names = new[] { "oldhouse" };
            foreach (var name in names)
            {
                itemdatabase.b type;
                try { type = (itemdatabase.b)Enum.Parse(typeof(itemdatabase.b), name); } catch { continue; }
                int id = (int)type;
                if (id < 0 || id >= db.pois.Length || db.pois[id] == null) continue;
                foreach (var s in db.pois[id].GetComponentsInChildren<newRandomStuffSpawnScript>(true))
                    if (s != null && !s.dontSpawn && s.items != null && s.items.Length > 0) found.Add(s);
                if (found.Count > 0) break;
            }
            return found;
        }

        // Same loot table and odds as the vanilla spawner, spawning at this marker.
        // ---- item spawning like a vanilla building ------------------------------------
        // A vanilla POI runs its whole set of newRandomStuffSpawnScript children (the
        // cabin has about 30): each picks one of its spots and one weighted item, so a
        // building gets the usual spread of crates, junk, parts, food and furniture. Each
        // Roadworthy building copies the full set of a matching vanilla building (same
        // tables, odds and number of spots per item) and spreads those spots over its own
        // floors, tables and lofts. Vehicle spawners are left out: they need open ground.
        static readonly string[] OutdoorOnly = { "carspawn", "busspawn", "bikespawn", "szankospawn", "1underground" };

        // Vanilla places every spot by hand, so items never pile up. Here spots are spread
        // over the building's own surfaces at least MinSpacing apart, about one per
        // AreaPerSpot square metres, away from the hand-placed props; each copied spawner
        // gets spots of its own, and spawners that no longer fit are left out.
        const float MinSpacing = 0.8f, AreaPerSpot = 1.2f;
        const int MaxSpotsPerItem = 2;

        static void AddVanillaSpawners(itemdatabase db, Building b, GameObject root)
        {
            if (b.AreaRects.Count == 0) return;
            GameObject reference = null;
            string[] names;
            if (!lootSources.TryGetValue(b.Name, out names)) names = new[] { "oldhouse" };
            foreach (var name in names)
            {
                itemdatabase.b type;
                try { type = (itemdatabase.b)Enum.Parse(typeof(itemdatabase.b), name); } catch { continue; }
                int id = (int)type;
                if (id >= 0 && id < db.pois.Length && db.pois[id] != null) { reference = db.pois[id]; break; }
            }
            if (reference == null) return;
            var rnd = new System.Random((int)(CustomPoiPolicy.Hash(b.Name.Length, b.Name[3], b.Name[b.Name.Length - 1]) & 0x7FFFFFFF));
            var pool = SpreadSpots(b, rnd);

            // Vanilla spawners in a stable shuffled order, so a small building gets a fair mix.
            var spawners = new List<newRandomStuffSpawnScript>();
            foreach (var from in reference.GetComponentsInChildren<newRandomStuffSpawnScript>(true))
                if (from != null && !from.dontSpawn && from.items != null && from.items.Length > 0 && Array.IndexOf(OutdoorOnly, SpawnerKey(from.name)) < 0)
                    spawners.Add(from);
            for (int i = spawners.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = spawners[i]; spawners[i] = spawners[j]; spawners[j] = t; }

            int copied = 0, used = 0;
            foreach (var from in spawners)
            {
                if (used >= pool.Count) break;
                var go = new GameObject("Vanilla_" + from.name);
                go.transform.SetParent(root.transform, false);
                var to = go.AddComponent<newRandomStuffSpawnScript>();
                CopySpawnerFields(from, to);
                float lift = SpawnLift(db, from);
                // This spawner's own spots: shared by its items (it spawns one item at a time).
                int want = 1;
                foreach (var it in from.items) if (it != null && it.places != null) want = Math.Max(want, Math.Min(MaxSpotsPerItem, it.places.Length));
                var spots = new List<Transform>();
                for (int k = 0; k < want && used < pool.Count; k++)
                {
                    var spot = new GameObject("Spot").transform;
                    spot.SetParent(go.transform, false);
                    spot.localPosition = pool[used++] + Vector3.up * lift;
                    spot.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                    spots.Add(spot);
                }
                var items = new newRandomStuffSpawnScript.item[from.items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    var dst = CopyItem(from.items[i]);
                    dst.places = new newRandomStuffSpawnScript.place[spots.Count];
                    for (int k = 0; k < spots.Count; k++) dst.places[k] = new newRandomStuffSpawnScript.place { chance = 0, T = spots[k] };
                    items[i] = dst;
                }
                to.items = items;
                copied++;
            }
            Debug.Log("[Roadworthy POI] " + b.Name + ": " + copied + " of " + spawners.Count + " spawners from " + reference.name + " on " + used + " spaced spots");
        }

        // Spots on the building's surfaces, MinSpacing apart and clear of hand-placed props
        // (dart throwing with a fixed seed: same layout every time).
        static List<Vector3> SpreadSpots(Building b, System.Random rnd)
        {
            var taken = new List<Vector3>();
            foreach (var p in b.Spawns) taken.Add(p.Position);
            foreach (var p in b.Props) taken.Add(p.Position);
            foreach (var fixture in b.Fixtures) if (fixture.Collision) taken.Add(fixture.Position);
            var spots = new List<Vector3>();
            for (int a = 0; a < b.AreaRects.Count; a++)
            {
                var r = b.AreaRects[a];
                int target = Math.Max(1, (int)(r.width * r.height / AreaPerSpot));
                int tries = target * 30;
                int made = 0;
                while (made < target && tries-- > 0)
                {
                    var p = new Vector3(r.xMin + (float)rnd.NextDouble() * r.width, b.AreaHeights[a] + 0.15f, r.yMin + (float)rnd.NextDouble() * r.height);
                    bool clear = true;
                    foreach (var q in spots) if (Flat(p, q) < MinSpacing) { clear = false; break; }
                    if (clear) foreach (var q in taken) if (Flat(p, q) < MinSpacing && Mathf.Abs(p.y - q.y) < 1.5f) { clear = false; break; }
                    if (!clear) continue;
                    spots.Add(p); made++;
                }
            }
            // Mix surfaces so the first spawners don't all land in the first room.
            for (int i = spots.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = spots[i]; spots[i] = spots[j]; spots[j] = t; }
            return spots;
        }

        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        // ---- spawn height ---------------------------------------------------------------
        // Vanilla spots are placed by hand at the right height for their items, which are
        // positioned by their pivot (a fridge's is near its middle). Our spots sit on the
        // surface, so each spawner's spots are raised by the most any of its possible items
        // reaches below its own pivot: the biggest item stands on the floor, small ones drop.
        static readonly Dictionary<newRandomStuffSpawnScript, float> liftCache = new Dictionary<newRandomStuffSpawnScript, float>();
        static readonly Dictionary<int, float> prefabDepth = new Dictionary<int, float>();
        const float MaxLift = 2.5f;

        static float SpawnLift(itemdatabase db, newRandomStuffSpawnScript spawner)
        {
            float lift;
            if (liftCache.TryGetValue(spawner, out lift)) return lift;
            lift = 0f;
            if (spawner.items != null)
                foreach (var it in spawner.items)
                {
                    if (it == null) continue;
                    if (it.G != null) { lift = Math.Max(lift, PivotDepth(it.G)); continue; }
                    if (string.IsNullOrEmpty(it.prefab)) continue;
                    try
                    {
                        if (it.isitem) lift = Math.Max(lift, ItemDepth(db, (int)(itemdatabase.i)Enum.Parse(typeof(itemdatabase.i), it.prefab)));
                        else
                        {
                            var ids = new List<int>();
                            CategoryItems(db, (int)(itemdatabase.k)Enum.Parse(typeof(itemdatabase.k), it.prefab), ids, 0);
                            foreach (int id in ids) lift = Math.Max(lift, ItemDepth(db, id));
                        }
                    }
                    catch { }
                }
            lift = Mathf.Min(lift + 0.03f, MaxLift);
            liftCache[spawner] = lift;
            return lift;
        }

        static float ItemDepth(itemdatabase db, int id)
        {
            if (db.items == null || id < 0 || id >= db.items.Length || db.items[id] == null) return 0f;
            float d;
            if (!prefabDepth.TryGetValue(id, out d)) prefabDepth[id] = d = PivotDepth(db.items[id]);
            return d;
        }

        // How far the prefab's meshes reach below its pivot (0 when they are all above it).
        static float PivotDepth(GameObject prefab)
        {
            var lb = LocalBounds(prefab, prefab.transform);
            return lb.size == Vector3.zero ? 0f : Mathf.Max(0f, -lb.min.y);
        }

        static void CategoryItems(itemdatabase db, int category, List<int> ids, int depth)
        {
            if (db.categories == null || category < 0 || category >= db.categories.Length || depth > 4) return;
            var c = db.categories[category];
            if (c == null) return;
            if (c.items != null) ids.AddRange(c.items);
            if (c.categories != null) foreach (int sub in c.categories) CategoryItems(db, sub, ids, depth + 1);
        }

        static void CopySpawnerFields(newRandomStuffSpawnScript from, newRandomStuffSpawnScript to)
        {
            foreach (var f in typeof(newRandomStuffSpawnScript).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name == "items" || f.Name == "spawned" || f.Name == "SPAWN" || f.Name == "debugspawn") continue;
                f.SetValue(to, f.GetValue(from));
            }
        }

        static newRandomStuffSpawnScript.item CopyItem(newRandomStuffSpawnScript.item src)
        {
            var dst = new newRandomStuffSpawnScript.item();
            if (src != null)
                foreach (var f in typeof(newRandomStuffSpawnScript.item).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    f.SetValue(dst, f.GetValue(src));
            return dst;
        }

        // ---- native props: copies of the vanilla POIs' own spawners --------------
        // Vanilla buildings place barrels, TVs, beds, chairs, parts and junk through
        // named newRandomStuffSpawnScript children (BarrelSpawn, tvSpawn, BedSpawn, ...).
        // Each spot names one; it gets a copy of that vanilla spawner (same item table,
        // odds and fluid rules) pointed at the spot, run by poiScript.ItemSpawn on the host.
        static Dictionary<string, List<newRandomStuffSpawnScript>> spawnerCatalog;

        static Dictionary<string, List<newRandomStuffSpawnScript>> SpawnerCatalog(itemdatabase db)
        {
            if (spawnerCatalog != null) return spawnerCatalog;
            spawnerCatalog = new Dictionary<string, List<newRandomStuffSpawnScript>>();
            for (int id = 0; id < db.pois.Length && id < baseIdOrLength(db); id++)
            {
                var prefab = db.pois[id];
                if (prefab == null) continue;
                foreach (var sp in prefab.GetComponentsInChildren<newRandomStuffSpawnScript>(true))
                {
                    if (sp == null || sp.dontSpawn || sp.items == null || sp.items.Length == 0) continue;
                    string key = SpawnerKey(sp.name);
                    List<newRandomStuffSpawnScript> list;
                    if (!spawnerCatalog.TryGetValue(key, out list)) spawnerCatalog[key] = list = new List<newRandomStuffSpawnScript>();
                    list.Add(sp);
                }
            }
            Debug.Log("[Roadworthy POI] vanilla spawner kinds: " + spawnerCatalog.Count);
            return spawnerCatalog;
        }

        static int baseIdOrLength(itemdatabase db) { return baseId >= 0 ? baseId : db.pois.Length; }

        // "MiscSpawn (12)" -> "miscspawn": vanilla names repeat with Unity's copy suffix.
        static string SpawnerKey(string name)
        {
            int cut = name.IndexOf(" (", StringComparison.Ordinal);
            return (cut > 0 ? name.Substring(0, cut) : name).Trim().ToLowerInvariant();
        }

        static readonly HashSet<string> missingKinds = new HashSet<string>();

        static void AddNativeSpawns(itemdatabase db, Building b, GameObject root)
        {
            if (b.Spawns.Count == 0) return;
            var catalog = SpawnerCatalog(db);
            for (int i = 0; i < b.Spawns.Count; i++)
            {
                var spot = b.Spawns[i];
                List<newRandomStuffSpawnScript> sources;
                if (!catalog.TryGetValue(SpawnerKey(spot.Item), out sources) && !catalog.TryGetValue("miscspawn", out sources))
                {
                    if (missingKinds.Add(spot.Item)) Debug.LogWarning("[Roadworthy POI] no vanilla spawner named " + spot.Item);
                    continue;
                }
                var go = new GameObject("Native" + i + "_" + spot.Item);
                go.transform.SetParent(root.transform, false);
                // A stable pick among the vanilla copies of this spawner kind.
                var picked = sources[(int)((uint)(b.Name.Length * 31 + i * 7) % (uint)sources.Count)];
                go.transform.localPosition = spot.Position + Vector3.up * SpawnLift(db, picked);
                go.transform.localRotation = Quaternion.Euler(0f, spot.Yaw, 0f);
                CopySpawner(picked, go.AddComponent<newRandomStuffSpawnScript>(), go.transform);
            }
        }

        // ---- doors: a real vanilla door fitted into each doorway --------------------
        // The door (its usablescript, hinge rotation, sounds) is cloned from a vanilla POI
        // and registered in poiScript.usables, so opening it syncs and saves like vanilla.
        static GameObject doorSource;
        static bool doorSearched;

        static GameObject DoorSource(itemdatabase db)
        {
            if (doorSearched) return doorSource;
            doorSearched = true;
            foreach (var name in new[] { "DoorSzoba", "Door1", "DoorRot", "FrontDoorLeft" })
                for (int id = 0; id < db.pois.Length && id < baseIdOrLength(db); id++)
                {
                    var prefab = db.pois[id];
                    if (prefab == null) continue;
                    foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                        if (t.name == name && t.GetComponentInChildren<usablescript>(true) != null && t.GetComponentInChildren<MeshFilter>(true) != null)
                        {
                            doorSource = t.gameObject;
                            Debug.Log("[Roadworthy POI] doors cloned from " + prefab.name + "/" + name);
                            return doorSource;
                        }
                }
            Debug.LogWarning("[Roadworthy POI] no vanilla door found; doorways stay open");
            return null;
        }

        static void AddDoors(itemdatabase db, Building b, GameObject root, poiScript poi)
        {
            if (b.Doors.Count == 0) return;
            var source = DoorSource(db);
            if (source == null) return;
            for (int i = 0; i < b.Doors.Count; i++)
            {
                var d = b.Doors[i];
                try
                {
                    var holder = new GameObject("Door" + i);
                    holder.transform.SetParent(root.transform, false);
                    holder.transform.localPosition = d.Position;
                    holder.transform.localRotation = Quaternion.Euler(0f, d.Yaw, 0f);
                    var fit = new GameObject("Fit");
                    fit.transform.SetParent(holder.transform, false);
                    var door = UnityEngine.Object.Instantiate(source, fit.transform, false);
                    door.name = source.name;
                    door.transform.localPosition = Vector3.zero;
                    // Keep the door's orientation from its vanilla building (relative to that
                    // building's root, which stands upright): that is what makes its hinge axis
                    // vertical. Only ever turn it about the vertical axis from here.
                    var srcRoot = source.transform.root;
                    Quaternion upright = Quaternion.Inverse(srcRoot.rotation) * source.transform.rotation;
                    door.transform.localRotation = upright;
                    door.transform.localScale = DivideScale(source.transform.lossyScale, srcRoot.lossyScale);
                    // Turn the leaf to run along the wall (+X), hinge at the origin.
                    Bounds lb = LocalBounds(door, fit.transform);
                    if (lb.size.z > lb.size.x) { door.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * upright; lb = LocalBounds(door, fit.transform); }
                    if (Mathf.Abs(lb.max.x) < Mathf.Abs(lb.min.x)) { door.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * door.transform.localRotation; lb = LocalBounds(door, fit.transform); }
                    // Scale to the doorway: same factor across the leaf and its swing plane, so it
                    // turns without skewing; height on its own.
                    float s = d.Width / Mathf.Max(0.05f, lb.size.x), h = d.Height / Mathf.Max(0.05f, lb.size.y);
                    fit.transform.localScale = new Vector3(s, h, s);
                    door.transform.localPosition = new Vector3(-d.Width / (2f * s) - lb.min.x, -lb.min.y, -lb.center.z);
                    foreach (var u in door.GetComponentsInChildren<usablescript>(true)) poi.usables.Add(u);
                }
                catch (Exception e) { Debug.LogWarning("[Roadworthy POI] door " + i + " in " + b.Name + " failed: " + e.Message); }
            }
        }

        static Vector3 DivideScale(Vector3 a, Vector3 b)
        {
            return new Vector3(b.x == 0f ? a.x : a.x / b.x, b.y == 0f ? a.y : a.y / b.y, b.z == 0f ? a.z : a.z / b.z);
        }

        // Bounds of every mesh under go, in the space of `space` (works on inactive objects).
        static Bounds LocalBounds(GameObject go, Transform space)
        {
            var bounds = new Bounds(); bool any = false;
            Matrix4x4 toSpace = space.worldToLocalMatrix;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = toSpace * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var pt = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(pt, Vector3.zero); any = true; } else bounds.Encapsulate(pt);
                }
            }
            return bounds;
        }

        static void CopySpawner(newRandomStuffSpawnScript from, newRandomStuffSpawnScript to, Transform place)
        {
            foreach (var f in typeof(newRandomStuffSpawnScript).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name == "items" || f.Name == "spawned" || f.Name == "SPAWN" || f.Name == "debugspawn") continue;
                f.SetValue(to, f.GetValue(from));
            }
            var items = new newRandomStuffSpawnScript.item[from.items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                var src = from.items[i];
                var dst = new newRandomStuffSpawnScript.item();
                if (src != null)
                    foreach (var f in typeof(newRandomStuffSpawnScript.item).GetFields(BindingFlags.Public | BindingFlags.Instance))
                        f.SetValue(dst, f.GetValue(src));
                dst.places = new[] { new newRandomStuffSpawnScript.place { chance = 0, T = place } };
                items[i] = dst;
            }
            to.items = items;
        }

        // ---- materials and layers ---------------------------------------------
        static Dictionary<string, Material> MaterialLookup()
        {
            var all = new Dictionary<string, Material>();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && !all.ContainsKey(m.name)) all[m.name] = m;
            return all;
        }

        static Material Find(Dictionary<string, Material> all, string name)
        {
            Material m;
            if (all.TryGetValue(name, out m)) return m;
            Debug.LogWarning("[Roadworthy POI] game material missing, using plain grey: " + name);
            var shader = Shader.Find("Standard");
            m = new Material(shader) { name = "rw_" + name, color = new Color(0.6f, 0.6f, 0.6f) };
            all[name] = m;
            return m;
        }

        // The layer the vanilla houses draw their walls on. Their first solid collider can
        // sit on a collision-only layer the camera does not render, so a building put on
        // that layer is solid but invisible. Render and collide on the visible layer, the
        // way most of the game's world geometry does.
        static int BuildingLayer(itemdatabase db)
        {
            int id = (int)itemdatabase.b.oldhouse;
            int render = -1, collide = -1;
            if (id < db.pois.Length && db.pois[id] != null)
            {
                foreach (var r in db.pois[id].GetComponentsInChildren<MeshRenderer>(true))
                    if (r != null && r.GetComponent<MeshFilter>() != null) { render = r.gameObject.layer; break; }
                foreach (var c in db.pois[id].GetComponentsInChildren<Collider>(true))
                    if (c != null && !c.isTrigger) { collide = c.gameObject.layer; break; }
            }
            int layer = CustomPoiPolicy.VisibleLayer(render);
            Debug.Log("[Roadworthy POI] vanilla house layers: render=" + render + " (" + LayerMask.LayerToName(Math.Max(0, render)) + ") collider=" + collide +
                      " (" + LayerMask.LayerToName(Math.Max(0, collide)) + "); using " + layer + " (" + LayerMask.LayerToName(layer) + ")");
            return layer;
        }

        // ---- data -------------------------------------------------------------
        static List<Building> Load()
        {
            using (var stream = typeof(CustomPois).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidDataException("missing embedded " + ResourceName);
                using (var r = new BinaryReader(stream)) return Read(r);
            }
        }

        // Format written by tools/PoiBuildings/build_pois.py (Unity space, Unity winding).
        static List<Building> Read(BinaryReader r)
        {
            string format = new string(r.ReadChars(4));
            if (format != "RWP4" && format != "RWP5") throw new InvalidDataException("bad POI data header");
            int count = Count(r, 64);
            var list = new List<Building>(count);
            for (int n = 0; n < count; n++)
            {
                var b = new Building { Name = Str(r), FrontExtent = r.ReadSingle(), AlignRadius = r.ReadSingle() };
                int parts = Count(r, 64);
                for (int p = 0; p < parts; p++)
                {
                    string mat = Str(r);
                    int nv = Count(r, 1 << 20);
                    var v = new Vector3[nv]; var nn = new Vector3[nv]; var uv = new Vector2[nv];
                    for (int i = 0; i < nv; i++)
                    {
                        v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        nn[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    }
                    var tri = Indices(r, nv);
                    var mesh = new Mesh { name = b.Name + "_" + mat };
                    if (nv > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.vertices = v; mesh.normals = nn; mesh.uv = uv; mesh.triangles = tri;
                    b.Parts.Add(new KeyValuePair<string, Mesh>(mat, mesh));
                }
                int cv = Count(r, 1 << 20);
                var cvs = new Vector3[cv];
                for (int i = 0; i < cv; i++) cvs[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var ctri = Indices(r, cv);
                b.Collision = new Mesh { name = b.Name + "_collision" };
                if (cv > 65000) b.Collision.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                b.Collision.vertices = cvs; b.Collision.triangles = ctri;
                int loot = Count(r, 256);
                for (int i = 0; i < loot; i++) b.Loot.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                int wrecks = Count(r, 64);
                for (int i = 0; i < wrecks; i++)
                    b.Wrecks.Add(new Wreck
                    {
                        Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                        Yaw = r.ReadSingle(), Roll = r.ReadSingle(), Column = r.ReadInt32(), Level = r.ReadInt32()
                    });
                int props = Count(r, 64);
                for (int i = 0; i < props; i++)
                    b.Props.Add(new Prop { Item = Str(r), Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), Yaw = r.ReadSingle() });
                int spawns = Count(r, 128);
                for (int i = 0; i < spawns; i++)
                    b.Spawns.Add(new Prop { Item = Str(r), Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), Yaw = r.ReadSingle() });
                int doors = Count(r, 32);
                for (int i = 0; i < doors; i++)
                    b.Doors.Add(new Door { Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), Width = r.ReadSingle(), Height = r.ReadSingle(), Yaw = r.ReadSingle() });
                int areas = Count(r, 32);
                for (int i = 0; i < areas; i++)
                {
                    float x0 = r.ReadSingle(), z0 = r.ReadSingle(), x1 = r.ReadSingle(), z1 = r.ReadSingle(), y = r.ReadSingle();
                    b.AreaRects.Add(Rect.MinMaxRect(x0, z0, x1, z1)); b.AreaHeights.Add(y);
                }
                if (format == "RWP5")
                {
                    int fixtures = Count(r, 64);
                    for (int i = 0; i < fixtures; i++)
                        b.Fixtures.Add(new Fixture
                        {
                            Source = Str(r), Name = Str(r),
                            Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                            Size = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                            Euler = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                            Collision = r.ReadBoolean()
                        });
                }
                list.Add(b);
            }
            return list;
        }

        static int[] Indices(BinaryReader r, int vertexCount)
        {
            int n = Count(r, 1 << 22);
            if (n % 3 != 0) throw new InvalidDataException("triangle list not a multiple of 3");
            var idx = new int[n];
            for (int i = 0; i < n; i++)
            {
                idx[i] = r.ReadInt32();
                if (idx[i] < 0 || idx[i] >= vertexCount) throw new InvalidDataException("index out of range");
            }
            return idx;
        }

        static int Count(BinaryReader r, int max)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > max) throw new InvalidDataException("bad count " + n);
            return n;
        }

        static string Str(BinaryReader r)
        {
            int n = Count(r, 256);
            return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n));
        }
    }

    // Builds a scrapyard's stacked wrecks when its POI spawns (see CustomPois.BuildYard).
    public sealed class RoadworthyWreckYard : MonoBehaviour
    {
        void Start()
        {
            try { CustomPois.BuildYard(gameObject); }
            catch (Exception e) { Debug.LogWarning("[Roadworthy POI] wreck yard failed: " + e.Message); }
        }
    }
}
