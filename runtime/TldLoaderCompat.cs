using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using GeneszSteamP2PBase;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TldMp
{
    // Runs TLDLoader 3.x (Documents\TheLongDrive\Mods) on the beta game.
    // TLDLoader.dll is rewritten in place by TldLoaderTranslator, with an exact
    // backup, and every mod is rewritten into a cache just before TLDLoader loads
    // it. Nothing in the beta starts TLDLoader, so Roadworthy starts it on the
    // main menu and reports menu/world changes to it (the beta is one scene).
    // TLD_MP_TLDLOADER=0 or the settings toggle turns all of this off and puts
    // the original TLDLoader.dll back.
    //
    // The methods named in TldLoaderCompatPolicy.ForField and SceneName/TranslateMod
    // are called from the translated assemblies: keep them public and their
    // signatures fixed.
    public static class TldLoaderBridge
    {
        const string Tag = "[TLD MP mods] ";
        enum Phase { Waiting, Running, Off }
        static Phase phase;
        static int menuFrames;
        static bool inWorld, worldReady, saveLoad;
        static bool sceneChanged, sceneSaveLoad;
        // TLDLoader's UI bundle (Mods\Assets\TLDLoader_Core\core.unity3d) is missing: TLDLoader
        // cannot show its loading screen, so Roadworthy runs the mods' OnLoad itself.
        static bool coreMissing, fallbackLoaded;
        static Assembly loader;
        static Type modLoaderType, modType;
        static double nextLobbyCheck;
        static ulong warnedLobby;
        static string localList;

        // Per mod file name: game members it uses that the beta lacks.
        internal static readonly Dictionary<string, int> MissingByFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal static readonly List<string> TranslationFailures = new List<string>();
        internal static bool Running { get { return phase == Phase.Running; } }
        internal static bool Installed { get { return File.Exists(Path.Combine(ManagedDirectory, "TLDLoader.dll")); } }

        internal static string ManagedDirectory { get { return Path.GetDirectoryName(typeof(TldLoaderBridge).Assembly.Location); } }
        // Called from the loader's rewritten Documents-based paths as well as
        // Roadworthy's own mod code. All of these belong to the same profile.
        public static string LoaderDataRoot()
        {
            return StableGameBridge.RedirectSaveRoot().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        internal static string ModsDirectory { get { return Path.Combine(LoaderDataRoot(), "Mods"); } }

        internal static string CacheDirectory
        {
            get { return Path.Combine(LoaderDataRoot(), "Roadworthy", "TldLoaderCache"); }
        }

        // ---- Called from translated TLDLoader / mod code ----

        // mainscript.M: the public game had it only inside a loaded world.
        public static mainscript MainScript() { return worldReady ? mainscript.s : null; }
        // gameObject.AddComponent<partscript>() in mod code. The beta only lets a part look for
        // slots when isPart is set (attachablescript.Scan), and its fields come from FStart.
        public static attachablescript ModAddPart(GameObject target)
        {
            var part = target.AddComponent<attachablescript>();
            part.isPart = true;
            // The public game's Craft always snapped a part to its slot; the beta snaps only parts
            // with targetPosRot (set in vanilla prefabs), so a mod's part mounted at whatever angle
            // it was held at.
            part.targetPosRot = true;
            // The public game's Craft also parented the part to the slot. The beta parents only parts
            // with parentAtAttach and otherwise hangs them on a joint: the battery dangled upside
            // down, and Car Batteries (which looks for a parent) never saw it mounted.
            part.parentAtAttach = true;
            try { part.FStart(); } catch (Exception e) { Debug.LogWarning(Tag + "part set-up on " + target.name + " failed: " + e.Message); }
            TldModParts.Adopt(part);
            return part;
        }

        // gameObject.AddComponent<partslotscript>() in mod code. The player finds slots with
        // fpscontroller.attachLayer and parts with mainscript.scanLayer; a new object sits on
        // the default layer, which may be in neither.
        public static partslotscript ModAddSlot(GameObject target)
        {
            var slot = target.AddComponent<partslotscript>();
            try
            {
                var main = mainscript.s;
                int mask = main == null ? 0 : (int)main.scanLayer;
                int attach = main == null || main.player == null ? 0 : (int)main.player.attachLayer;
                int layer = TldLoaderCompatPolicy.SlotLayer(target.layer, mask, attach);
                if (layer != target.layer) target.layer = layer;
            }
            catch (Exception e) { Debug.LogWarning(Tag + "slot set-up on " + target.name + " failed: " + e.Message); }
            TldModParts.TrackSlot(slot);
            return slot;
        }

        // mainscript.M in mod code: it existed from the start of world loading, before DbLoad.
        public static mainscript ModMainScript() { return inWorld || worldReady ? mainscript.s : null; }
        // mainscript.M.DFMS
        public static DataFromMenuScript DataFromMenu(mainscript main) { return main != null && saveLoad ? DataFromMenuScript.s : null; }
        // menuhandler.Settings
        public static settingsscript MenuSettings(menuhandler menu) { return settingsscript.s; }
        public static void MenuPressedLoadScene(menuhandler menu, string scene)
        {
            if (menu == null) return;
            switch (scene)
            {
                case "SceneNewRandom":
                    menu.PressedStart();
                    break;
                case "SceneMenu":
                    menu.LoadMainMenu(true);
                    break;
                default:
                    var data = DataFromMenuScript.s;
                    if (data != null) data.ResetStuff();
                    SceneManager.LoadScene(scene);
                    break;
            }
        }
        // newSaveScreenScript.Open(bool): the beta derives the flag from DataFromMenu.
        public static void OpenSaveScreen(newSaveScreenScript screen, bool inMainMenu)
        {
            if (screen == null) return;
            screen.Open();
            // The flag is private on the beta; reflection keeps stable's explicit choice.
            var field = typeof(newSaveScreenScript).GetField("inMainMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null) field.SetValue(screen, inMainMenu);
        }
        // mainscript.loaded
        public static bool WorldLoaded(mainscript main) { return saveLoad && worldReady && main != null; }
        // mainscript.load: the world is still loading.
        public static bool WorldLoading(mainscript main) { return main != null && saveLoad && !worldReady; }
        // mainscript.mainWorld: mods convert positions with global = unity - mainWorld.coord.
        // A hidden visszarako (inactive, so it never runs) whose coord follows the beta's origin.
        static visszarako mainWorld;
        public static visszarako MainWorld(mainscript main)
        {
            if (!worldReady || main == null) return null;
            if (mainWorld == null)
            {
                var holder = new GameObject("Roadworthy TLDLoader main world");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
                mainWorld = holder.AddComponent<visszarako>();
                mainWorld.dont = true;
            }
            Vector3d origin = mainscript.GlobalFromUnityPos(Vector3.zero);
            mainWorld.coord = new Vector3d(-origin.x, -origin.y, -origin.z);
            return mainWorld;
        }
        // ---- The stable world generator's data, filled from the beta's (stand-ins in TldStableShells.cs) ----

        public static roadGenScript MainRoadScript(savedatascript save) { return TldStableWorld.RoadGen(); }
        public static List<StableShells.buildingscript> SaveBuildings(savedatascript save) { return TldStableWorld.Buildings(); }
        public static StableShells.buildingscript BuildingBeforeItemSpawn(poiScript poi, poiGenScript.poiClass record)
        {
            if (!syncScript.IsHostOrSingle() || record.spawnedItems) return null;
            var map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
            poiGenScript generator = null;
            if (map != null && map.poiGens != null && poi.poigenid >= 0 && poi.poigenid < map.poiGens.Count)
                generator = map.poiGens[poi.poigenid];
            return TldStableWorld.Building(poi, record, generator);
        }
        public static StableShells.roadclass[] StableRoads(roadGenScript gen) { return TldStableWorld.Roads(gen); }
        public static visszarako RoadWorld2(roadGenScript gen) { return MainWorld(mainscript.s); }
        public static float RoadSegmentLength(roadGenScript gen) { TldStableWorld.Roads(gen); return TldStableWorld.SegmentLength; }
        public static float RoadBoneLength(roadGenScript gen) { TldStableWorld.Roads(gen); return TldStableWorld.BoneLength; }
        public static int RoadBoneNum(roadGenScript gen) { TldStableWorld.Roads(gen); return TldStableWorld.BoneCount; }
        // The beta's road meshes sit on the bone points; stable mods add this above them.
        public static float RoadWidth(roadGenScript gen) { return TldStableWorld.Width; }
        public static GameObject RoadPrefab(roadGenScript gen)
        {
            if (itemdatabase.s == null || gen == null) return null;
            var player = mainscript.s == null ? null : mainscript.s.player;
            var position = player == null ? Vector3d.zero : mainscript.GlobalFromUnityPos(player.transform.position);
            var settings = gen.terrainSettings;
            int biome = settings == null ? 0 : settings.GetBiomeIntNoSnow(position);
            bool snow = settings != null && settings.GetBiomeInt(position) == 1;
            return itemdatabase.s.Road(itemdatabase.s.RoadID(biome, snow, roadGenScript.roadTypeEmu.mainRoad));
        }
        public static Vector3 RoadPointNear(roadGenScript gen, Vector3 position)
        {
            if (gen == null || mainscript.s == null) return position;
            Vector3d global = mainscript.GlobalFromUnityPos(position);
            Vector3d nearest;
            var roads = gen.RoadsNear(global, true, poiGenScript.alignToRoadEmu.anyRoad);
            return roadGenScript.NearestPointOnRoadBone(global, roads, out nearest) == null
                ? position : mainscript.UnityPosFromGlobal(nearest);
        }

        public static DataFromMenuScript MenuDataFromMenu(menuhandler menu) { return DataFromMenuScript.s; }
        public static void SetMenuDataFromMenu(menuhandler menu, DataFromMenuScript data) { DataFromMenuScript.s = data; }
        public static StableShells.TerrainGenerationSettings MainTerrainSettings(mainscript main) { return TldStableWorld.Settings(); }
        public static List<StableShells.digholescript2> MainTerrainHoles(mainscript main) { return TldStableWorld.Holes(); }
        sealed class FarTerrainDistance { public float Value; }
        static readonly ConditionalWeakTable<settingsscript.setting, FarTerrainDistance> farTerrainDistances =
            new ConditionalWeakTable<settingsscript.setting, FarTerrainDistance>();
        public static bool FarTerrain(settingsscript.setting settings) { return settings.GetDistantTerrain(); }
        public static void SetFarTerrain(settingsscript.setting settings, bool enabled)
        {
            if (enabled == settings.GetDistantTerrain()) return;
            var distance = farTerrainDistances.GetOrCreateValue(settings);
            if (enabled)
                settings.FDrawDistance = distance.Value > 500f ? distance.Value : 25000f;
            else
            {
                distance.Value = settings.FDrawDistance;
                settings.FDrawDistance = 500f;
            }
        }
        public static Transform[] TerrainAlignMarkers(terrainHeightAlignToBuildingScript marker) { return TldStableTerrain.For(marker).Markers; }
        public static void SetTerrainAlignMarkers(terrainHeightAlignToBuildingScript marker, Transform[] value)
        {
            var group = TldStableTerrain.For(marker);
            group.Markers = value ?? new Transform[0];
            group.HasLegacyMarkers = true;
        }
        public static float[] TerrainAlignRanges(terrainHeightAlignToBuildingScript marker) { return TldStableTerrain.For(marker).Ranges; }
        public static void SetTerrainAlignRanges(terrainHeightAlignToBuildingScript marker, float[] value) { TldStableTerrain.For(marker).Ranges = value ?? new float[0]; }
        public static List<StableShells.terrainHeightAlignToBuildingScript_helpPos> TerrainAlignPoints(terrainHeightAlignToBuildingScript marker)
        {
            return TldStableTerrain.For(marker).Points;
        }
        public static void SetTerrainAlignPoints(terrainHeightAlignToBuildingScript marker, List<StableShells.terrainHeightAlignToBuildingScript_helpPos> value)
        {
            var group = TldStableTerrain.For(marker);
            group.Points = value ?? new List<StableShells.terrainHeightAlignToBuildingScript_helpPos>();
            group.Publish();
        }
        public static bool TerrainAlignStarted(terrainHeightAlignToBuildingScript marker) { return TldStableTerrain.For(marker).Started; }
        public static void SetTerrainAlignStarted(terrainHeightAlignToBuildingScript marker, bool value) { TldStableTerrain.For(marker).Started = value; }
        public static void StartTerrainAlign(terrainHeightAlignToBuildingScript marker, bool force)
        {
            var group = TldStableTerrain.For(marker);
            if (!force && group.Started) return;
            if (group.HasLegacyMarkers) group.Start(true);
            else
            {
                if (force) group.Start(true);
                marker.FStart();
            }
        }
        internal static bool NativeTerrainAlignStart(terrainHeightAlignToBuildingScript marker)
        {
            if (!Running) return true;
            var group = TldStableTerrain.For(marker);
            group.Start(false);
            return !group.HasLegacyMarkers;
        }
        public static StableShells.settingsscript_DrawDistanceSetting MainDrawDistance(mainscript main) { return TldStableWorld.DrawDistance(); }
        public static gamemodesettingsscript.gamemodesettings MainGameModeSettings(mainscript main) { return gamemodesettingsscript.s == null ? null : gamemodesettingsscript.s.S; }
        public static int GameModeHomesick(gamemodesettingsscript.gamemodesettings settings) { return settings.map.homesickMode; }
        public static void SetGameModeHomesick(gamemodesettingsscript.gamemodesettings settings, int mode) { settings.map.homesickMode = mode; }
        public static settingsscript MainSettings(mainscript main) { return settingsscript.s; }
        // pickupable.isPhyslocked / physLockParentID / mountStuff.Lock
        public static bool PickupIsPhyslocked(pickupable pick) { return pick != null && pick.physlock != null; }
        public static int PickupPhysLockParent(pickupable pick)
        {
            return pick == null || pick.physlock == null || pick.physlock.parentTosave == null ? 0 : unchecked((int)pick.physlock.parentTosave.idInSave);
        }
        // Stable's bool was "skip the timer"; the beta's means "ignore the rigidbody", so it stays false.
        public static void MountLock(mountStuff mount, pickupable pick, bool ignoreTime) { if (mount != null) mount.Store(pick, false); }
        internal static bool WorldIsReady { get { return worldReady; } }

        // mainscript.multi
        public static bool MainMulti(mainscript main) { return DataFromMenuScript.s != null && DataFromMenuScript.s.multi; }
        // mainscript.skystuffs
        public static Transform[] MainSkyStuffs(mainscript main) { return napszakvaltakozas.s == null ? new Transform[0] : napszakvaltakozas.s.skystuffs; }
        // pickupable.mpPickedup
        public static bool PickupMpPickedUp(pickupable pick) { return pick != null && pick.tosave != null && pick.tosave.mppickedup; }
        public static void SetPickupMpPickedUp(pickupable pick, bool value) { if (pick != null && pick.tosave != null) pick.tosave.mppickedup = value; }
        // savedatascript.properties
        public static saveproperties SaveProperties(savedatascript save) { return save == null || save.data == null ? null : save.data.properties; }
        // fpscontroller.M
        public static mainscript PlayerMain(fpscontroller player) { return ModMainScript(); }

        // Game members a mod uses that the beta removed (TldLoaderTranslator.Degrade): fields keep
        // a value per object, methods do nothing. Each is logged once.
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, object>> shadows =
            new System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, object>>();
        static readonly Dictionary<string, object> staticShadows = new Dictionary<string, object>(StringComparer.Ordinal);
        static readonly HashSet<string> degradedLogged = new HashSet<string>(StringComparer.Ordinal);

        static Dictionary<string, object> ShadowsOf(object owner) { return owner == null ? staticShadows : shadows.GetOrCreateValue(owner); }

        static void LogDegraded(string key, string what)
        {
            lock (degradedLogged) if (!degradedLogged.Add(key)) return;
            Debug.LogWarning(Tag + "a mod uses " + key + ", which this game version does not have; " + what + ".");
        }

        public static T ShadowGet<T>(object owner, string key)
        {
            LogDegraded(key, "it reads the value the mod last stored there (else the default)");
            object value;
            lock (staticShadows) return ShadowsOf(owner).TryGetValue(key, out value) && value is T ? (T)value : default(T);
        }

        public static void ShadowSet<T>(object owner, T value, string key)
        {
            LogDegraded(key, "the mod's value is kept but the game ignores it");
            lock (staticShadows) ShadowsOf(owner)[key] = value;
        }

        public static void ShadowSetStatic<T>(T value, string key) { ShadowSet<T>(null, value, key); }

        public static void MissingCall(string key) { LogDegraded(key, "the call does nothing"); }

        // mainscript's survival shortcuts (factors as on the public game).
        static survivalscript Survival(mainscript main) { return main == null || main.player == null ? null : main.player.survival; }
        public static void MainHpCh(mainscript main, float value) { var s = Survival(main); if (s != null) s.HealInstant(value * 0.5f, false); }
        public static void MainFoodCh(mainscript main, float value) { var s = Survival(main); if (s != null) s.food += value * 0.1f; }
        public static void MainWaterCh(mainscript main, float value) { var s = Survival(main); if (s != null) s.water += value * 0.3f; }
        public static void MainShitCh(mainscript main, float value) { var s = Survival(main); if (s != null) s.shit += value * 0.1f; }
        public static void MainPissCh(mainscript main, float value)
        {
            var piss = main == null || main.player == null ? null : main.player.piss;
            if (piss != null && piss.Tank != null && piss.Tank.F != null) piss.Tank.F.ChangeWithoutMix(value * 0.2f);
        }
        // mainscript.isServerOrNoMulti(): single player, or the host.
        public static bool MainIsServerOrNoMulti(mainscript main)
        {
            try { var hood = networkingScript.s == null ? null : networkingScript.s.hood; return hood == null || !hood.isConnected || hood.isHost; }
            catch (Exception) { return true; }
        }
        public static void MainPressedExit(mainscript main) { if (menuhandler.s != null) menuhandler.s.PressedExit(); }
        public static void MainPressedRestart(mainscript main) { if (menuhandler.s != null) menuhandler.s.PressedRestart(); }
        // mainscript.world: a stand-in root kept where the public game's world
        // transform would be, so (unity position - world.position) is the global position.
        static Transform worldRoot;
        public static Transform WorldRoot(mainscript main)
        {
            if (!worldReady || main == null) return null;
            if (worldRoot == null)
            {
                var holder = new GameObject("Roadworthy TLDLoader world root");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                worldRoot = holder.transform;
            }
            Vector3d origin = mainscript.GlobalFromUnityPos(Vector3.zero);
            worldRoot.position = new Vector3((float)-origin.x, (float)-origin.y, (float)-origin.z);
            return worldRoot;
        }
        // mainscript.napszak
        public static napszakvaltakozas DayCycle(mainscript main) { return napszakvaltakozas.s; }

        // savedatascript.toSaveStuff: live items by id. A snapshot rebuilt at most once a
        // frame; writing to it changes nothing in the game.
        static Dictionary<int, tosaveitemscript> saveItems;
        static int saveItemsFrame = -1;
        public static Dictionary<int, tosaveitemscript> SaveItems(savedatascript save)
        {
            if (saveItems != null && saveItemsFrame == Time.frameCount) return saveItems;
            var result = new Dictionary<int, tosaveitemscript>();
            if (save != null && save.items != null)
                foreach (var pair in save.items) result[unchecked((int)pair.Key)] = pair.Value;
            saveItemsFrame = Time.frameCount;
            return saveItems = result;
        }

        // A stable-shaped view of the item records used by the beta save.
        static Dictionary<int, StableShells.save_prefab> savedIds;
        static savedata savedIdsSource;
        static int savedIdsFrame = -1;
        public static Dictionary<int, StableShells.save_prefab> SavedIds(savedata data)
        {
            if (savedIds != null && savedIdsFrame == Time.frameCount && ReferenceEquals(savedIdsSource, data)) return savedIds;
            var result = new Dictionary<int, StableShells.save_prefab>();
            if (data != null && data.itemData != null)
            {
                if (data.itemData.items != null)
                    foreach (var item in data.itemData.items)
                        if (item != null)
                            result[unchecked((int)item.id)] = new StableShells.save_prefab
                            { prefabID = item.prefabID, transform = item.transform, buried = item.buried };
                if (data.itemData.rendszam != null)
                    foreach (var plate in data.itemData.rendszam)
                    {
                        StableShells.save_prefab item;
                        if (plate != null && result.TryGetValue(unchecked((int)plate.id), out item))
                            item.rendszam = new save_rendszam(plate);
                    }
            }
            var save = savedatascript.s;
            var main = mainscript.s;
            if (save != null && save.items != null && main != null && ReferenceEquals(save.data, data))
                foreach (var pair in save.items)
                {
                    var live = pair.Value;
                    if (live == null) continue;
                    int id = unchecked((int)pair.Key);
                    StableShells.save_prefab item;
                    if (!result.TryGetValue(id, out item)) result[id] = item = new StableShells.save_prefab();
                    var anchor = live.useThisForDistance != null ? live.useThisForDistance : live.transform;
                    item.prefabID = live.id;
                    item.transform = new posRotd(anchor.position - main.visszarakva, live.transform.eulerAngles);
                    item.buried = live.buried;
                    item.inIninventory = PickupInventorySlot(live.P);
                    item.rendszam = live.rendszam == null ? null : new save_rendszam(live.rendszam, pair.Key);
                }
            savedIdsSource = data;
            savedIdsFrame = Time.frameCount;
            return savedIds = result;
        }

        // Stable sidecar dictionaries. Unlike SavedIds these are live: Add/indexer/Remove
        // writes are kept and are written beside the native save.
        public static Dictionary<int, StableShells.save_prefab> LegacyNear(savedata data) { return TldLegacySave.Near(data); }
        public static Dictionary<int, StableShells.save_prefab> LegacyFar(savedata data) { return TldLegacySave.Far(data); }
        public static void SetLegacyNear(savedata data, Dictionary<int, StableShells.save_prefab> value) { TldLegacySave.SetNear(data, value); }
        public static void SetLegacyFar(savedata data, Dictionary<int, StableShells.save_prefab> value) { TldLegacySave.SetFar(data, value); }

        public static GameObject SpawnStableSavedItem(savedatascript save, StableShells.save_prefab item, int key)
        {
            return item == null || mainscript.s == null ? null :
                SpawnStableSavedItem(save, item, mainscript.UnityPosFromGlobal(item.transform.pos), key);
        }

        public static GameObject SpawnStableSavedItem(savedatascript save, StableShells.save_prefab item, Vector3 position, int key)
        {
            var db = itemdatabase.s;
            if (save == null || item == null || db == null) return null;
            // Stable building slot 18 was the mansion and slot 17 was the removed village.
            int poi = item.prefabID == 18 ? (int)itemdatabase.b.mansion : item.prefabID;
            var legacy = TldLegacyCategories.Find(item.categoryID, item.prefabID);
            var prefab = legacy != null ? legacy : item.categoryID == 0 ? db.Item(item.prefabID) :
                item.categoryID == 1 && item.prefabID != 17 && db.pois != null && poi >= 0 && poi < db.pois.Length ?
                    db.pois[poi] : db.Item(0);
            if (prefab == null) prefab = db.Item(0);
            return prefab == null ? null : UnityEngine.Object.Instantiate(prefab, position, Quaternion.Euler(item.transform.rot.Load()));
        }

        // tosaveitemscript.removeFromMemory: the public game kept the component and read the flag
        // when the object was destroyed. Setting it never destroyed anything. The beta has no
        // field, so keep the flag per component and let PreDestroy drop the far record.
        sealed class LegacyRemoval { public bool Remove; }
        static readonly ConditionalWeakTable<tosaveitemscript, LegacyRemoval> legacyRemovals =
            new ConditionalWeakTable<tosaveitemscript, LegacyRemoval>();

        public static bool RemoveFromMemory(tosaveitemscript save)
        {
            if (save == null) return false;
            LegacyRemoval state;
            return legacyRemovals.TryGetValue(save, out state) && state.Remove;
        }

        public static void SetRemoveFromMemory(tosaveitemscript save, bool remove)
        {
            if (save == null) return;
            legacyRemovals.GetOrCreateValue(save).Remove = remove;
        }

        // Called by the beta's AddToFarStuff before it stores the record. Stable's flag
        // suppressed that storage (and the chunk entry) while PreDestroy still released locks.
        public static bool LegacyAllowFarStuff(tosaveitemscript save)
        {
            bool allow = save == null || !RemoveFromMemory(save);
            if (!allow) Debug.Log(Tag + "removeFromMemory suppressed the far record for " + save.name + " (id " + save.idInSave + ")");
            return allow;
        }

        // pickupable.attach
        public static attachablescript PickupAttach(pickupable pick)
        {
            if (pick == null) return null;
            if (pick.tosave != null && pick.tosave.attachable != null) return pick.tosave.attachable;
            return pick.GetComponent<attachablescript>();
        }
        public static void SetPickupAttach(pickupable pick, attachablescript attach)
        {
            if (pick != null && pick.tosave != null && attach != null) pick.tosave.attachable = attach;
            TldModParts.Adopt(attach);
        }

        public static int PickupInventorySlot(pickupable pick)
        {
            return pick == null || mainscript.s == null ? 0 : pick.inInventory();
        }

        public static bool PickupMountGuard(pickupable pick) { return pick != null && pick.CheckDontMountTillPickup(); }
        public static void SetPickupMountGuard(pickupable pick, bool guarded)
        {
            if (pick == null) return;
            if (guarded) pick.SetDontMountTillPickup();
            else pick.ReSetDontMountTillPickup();
        }

        static tosaveitemscript PickupSave(pickupable pick)
        {
            return pick == null ? null : pick.tosave != null ? pick.tosave : pick.GetComponent<tosaveitemscript>();
        }

        public static List<partslotscript> PickupSlots(pickupable pick)
        {
            if (pick == null) return null;
            var save = PickupSave(pick);
            return save == null ? ShadowGet<List<partslotscript>>(pick, "pickupable::slots") : save.partslotscripts;
        }

        public static void SetPickupSlots(pickupable pick, List<partslotscript> slots)
        {
            if (pick == null) return;
            var save = PickupSave(pick);
            if (save == null) ShadowSet(pick, slots, "pickupable::slots");
            else save.partslotscripts = slots;
        }

        public static UnityEngine.UI.Toggle AutoShiftToggle(settingsscript settings)
        {
            // The beta settings menu uses this control when reading BAutoShift.
            var controls = settings == null || settings.tabControls == null ? null : settings.tabControls.toggles;
            return controls == null || controls.Length <= 1 || controls[1] == null ? null : controls[1].toggle;
        }

        public static UnityEngine.UI.Toggle AutoShiftToggle(menuhandler menu)
        {
            return menu == null ? null : AutoShiftToggle(settingsscript.s);
        }

        public static float RadioVolume(radioscript radio)
        {
            // The beta's radio mixer and on/off logic both read this knob.
            return radio == null || radio.Bvolume == null ? 0f : radio.Bvolume.tunedLerp;
        }

        public static save_usablescript CaptureUsable(usablescript usable)
        {
            return new save_usablescript(usable, usable.tosaveid, usable.usableid);
        }

        public static save_painting CapturePainting(paintingScript painting)
        {
            var owner = painting.GetComponentInParent<tosaveitemscript>();
            return new save_painting(painting, owner == null ? 0u : owner.idInSave);
        }

        public static save_newai CaptureAi(newAiScript ai)
        {
            return new save_newai(ai, ai.tosave == null ? 0u : ai.tosave.idInSave);
        }

        // pickupable.cols / cols2: arrays on the public game, lists on the beta.
        public static layerScript[] PickupCols(pickupable pick) { return pick == null || pick.cols == null ? new layerScript[0] : pick.cols.ToArray(); }
        public static layerScript[] PickupCols2(pickupable pick) { return pick == null || pick.cols2 == null ? new layerScript[0] : pick.cols2.ToArray(); }
        public static void SetPickupCols(pickupable pick, layerScript[] cols) { if (pick != null) pick.cols = StartLayers(pick, cols); }
        public static void SetPickupCols2(pickupable pick, layerScript[] cols) { if (pick != null) pick.cols2 = StartLayers(pick, cols); }

        // The public game's layerScript set itself up; the beta's needs FStart(pickupable, index)
        // for its collider, or picking the item up throws halfway and the player cannot let go.
        static List<layerScript> StartLayers(pickupable pick, layerScript[] cols)
        {
            var list = cols == null ? new List<layerScript>() : new List<layerScript>(cols);
            for (int i = 0; i < list.Count; i++)
            {
                var layer = list[i];
                if (layer == null) continue;
                try { layer.FStart(pick, i); } catch (Exception e) { Debug.LogWarning(Tag + "collider set-up on " + pick.name + " failed: " + e.Message); }
            }
            list.RemoveAll(layer => layer == null || layer.col == null);
            return list;
        }

        // Stable C contains colliders on the part. Beta C is an unrelated scan buffer.
        public static List<Collider> AttachColliders(attachablescript attach)
        {
            if (attach == null) return null;
            var state = TldStableAttach.Get(attach, true);
            if (state.Colliders == null) state.Colliders = new List<Collider>();
            return state.Colliders;
        }
        public static void SetAttachColliders(attachablescript attach, List<Collider> colliders)
        {
            if (attach == null) return;
            TldStableAttach.Get(attach, true).Colliders = colliders;
            TldModParts.Adopt(attach);
        }

        public static bool AttachLockedRotation(attachablescript attach)
        {
            var state = attach == null ? null : TldStableAttach.Get(attach, false);
            return state != null && state.LockRotation;
        }

        public static void SetAttachLockedRotation(attachablescript attach, bool locked)
        {
            if (attach == null) return;
            var state = TldStableAttach.Get(attach, true);
            state.LockRotation = locked;
            state.ApplyJointLock(attach.attached ? attach.J : null);
        }

        internal static void NativeAttachmentMounted(attachablescript attach)
        {
            var state = TldStableAttach.Get(attach, false);
            if (state == null) return;
            state.ApplyJointLock(attach.parentAtAttach ? null : attach.J);
            state.ClearCollisionPairs();
            if (state.Colliders == null) return;
            state.IgnoredTarget = attach.attachedC;
            foreach (var collider in state.Colliders)
            {
                if (collider == null) continue;
                collider.enabled = true;
                if (state.IgnoredTarget == null || collider == state.IgnoredTarget || !collider.gameObject.activeInHierarchy) continue;
                Physics.IgnoreCollision(collider, state.IgnoredTarget, true);
                state.IgnoredColliders.Add(collider);
            }
        }

        internal static void NativeAttachmentDetached(attachablescript attach)
        {
            var state = TldStableAttach.Get(attach, false);
            if (state == null) return;
            state.LockedJoint = null;
            state.ClearCollisionPairs();
        }

        // attachablescript.targetTosave
        public static tosaveitemscript AttachTarget(attachablescript attach)
        {
            if (attach == null) return null;
            if (attach.attached) return attach.AttachedToTosave();
            var record = TldStableAttach.Get(attach, false);
            return record == null ? null : record.Target;
        }
        public static void SetAttachTarget(attachablescript attach, tosaveitemscript target) { if (attach != null) TldStableAttach.Get(attach, true).Target = target; }

        // Stable's attachment save fields: live from the beta's attachment while attached, else what
        // the mod stored (it stores them, then calls Load to mount the part again).
        public static bool AttachHasTarget(attachablescript attach)
        {
            if (attach == null) return false;
            if (attach.attached) return attach.AttachedToTosave() != null;
            var record = TldStableAttach.Get(attach, false);
            return record != null && record.Has;
        }
        public static void SetAttachHasTarget(attachablescript attach, bool value) { if (attach != null) TldStableAttach.Get(attach, true).Has = value; }
        public static bool AttachIsCollider(attachablescript attach)
        {
            if (attach == null) return false;
            if (attach.attached) return attach.attachedC != null;
            var record = TldStableAttach.Get(attach, false);
            return record == null || record.IsCollider;
        }
        public static void SetAttachIsCollider(attachablescript attach, bool value) { if (attach != null) TldStableAttach.Get(attach, true).IsCollider = value; }
        public static int AttachColliderId(attachablescript attach)
        {
            if (attach == null) return -1;
            var target = attach.attached ? attach.AttachedToTosave() : null;
            if (target != null && attach.attachedC != null) return target.GetColliderIndex(attach.attachedC);
            if (target != null && attach.slot != null && attach.slot.attachTarget)
                return Array.IndexOf(SaveAttachTargets(target), StableAttachTarget(attach.slot));
            var record = TldStableAttach.Get(attach, false);
            return record == null ? -1 : record.ColliderId;
        }
        public static void SetAttachColliderId(attachablescript attach, int value) { if (attach != null) TldStableAttach.Get(attach, true).ColliderId = value; }
        public static Vector3 AttachPointLocalPos(attachablescript attach)
        {
            if (attach == null) return Vector3.zero;
            var target = attach.attached ? attach.AttachedToTosave() : null;
            if (target != null && attach.point != null) return target.transform.InverseTransformPoint(attach.point.position);
            var record = TldStableAttach.Get(attach, false);
            return record == null ? Vector3.zero : record.PointLocalPos;
        }
        public static void SetAttachPointLocalPos(attachablescript attach, Vector3 value) { if (attach != null) TldStableAttach.Get(attach, true).PointLocalPos = value; }

        // attachablescript.Load(pointLocalPos): mount to the stored item, on the stored collider when
        // there is one (as the beta's own save loader does), else on the item itself.
        public static void AttachLoad(attachablescript attach, Vector3 pointLocalPos)
        {
            if (attach == null) return;
            var record = TldStableAttach.Get(attach, false);
            var target = record == null ? null : record.Target;
            if (target == null) return;
            Adopt(attach);
            var world = target.transform.TransformPoint(pointLocalPos);
            if (record.Has && record.ColliderId >= 0)
            {
                if (record.IsCollider)
                {
                    var collider = target.GetColliderFromIndex(record.ColliderId);
                    if (collider != null) { attach.Attach(attach.NewPoint(collider.transform, world, Quaternion.identity), collider); return; }
                }
                else
                {
                    var targets = SaveAttachTargets(target);
                    if (record.ColliderId < targets.Length)
                    {
                        var slot = ResolveAttachTarget(targets[record.ColliderId]);
                        if (slot != null) { slot.Craft(attach); return; }
                    }
                }
            }
            attach.Attach(attach.NewPoint(target.transform, world, Quaternion.identity));
        }
        static void Adopt(attachablescript attach) { TldModParts.Adopt(attach); }
        // attachablescript.Refresh(): stable recomputed its save fields; the beta keeps them live.
        public static void AttachRefresh(attachablescript attach) { }

        // Fuel caps (stable moved TCap between TClosed and TOpen and switched GOpen/GClosed).
        public static Transform CapMoving(tankcapscript cap) { var s = TldStableCap.For(cap, false); return s != null && s.TCap != null ? s.TCap : cap != null && cap.usable != null ? cap.usable.TDoor : null; }
        public static Transform CapOpen(tankcapscript cap) { var s = TldStableCap.For(cap, false); return s != null && s.TOpen != null ? s.TOpen : cap != null && cap.usable != null ? cap.usable.TendPos : null; }
        public static Transform CapClosed(tankcapscript cap) { var s = TldStableCap.For(cap, false); return s != null && s.TClosed != null ? s.TClosed : cap != null && cap.usable != null ? cap.usable.TstartPos : null; }
        public static List<GameObject> CapOpenObjects(tankcapscript cap) { var s = TldStableCap.For(cap, false); return s != null && s.GOpen != null ? s.GOpen : new List<GameObject>(); }
        public static List<GameObject> CapClosedObjects(tankcapscript cap) { var s = TldStableCap.For(cap, false); return s != null && s.GClosed != null ? s.GClosed : new List<GameObject>(); }
        public static void SetCapMoving(tankcapscript cap, Transform value) { var s = TldStableCap.For(cap, true); if (s != null) s.TCap = value; }
        public static void SetCapOpen(tankcapscript cap, Transform value) { var s = TldStableCap.For(cap, true); if (s != null) s.TOpen = value; }
        public static void SetCapClosed(tankcapscript cap, Transform value) { var s = TldStableCap.For(cap, true); if (s != null) s.TClosed = value; }
        public static void SetCapOpenObjects(tankcapscript cap, List<GameObject> value) { var s = TldStableCap.For(cap, true); if (s != null) s.GOpen = value; }
        public static void SetCapClosedObjects(tankcapscript cap, List<GameObject> value) { var s = TldStableCap.For(cap, true); if (s != null) s.GClosed = value; }
        public static AudioClip CapOpenSound(tankcapscript cap) { return cap.openSound; }
        public static void SetCapOpenSound(tankcapscript cap, AudioClip value) { cap.openSound = value; TldStableCap.For(cap, true, false); }
        public static AudioClip CapCloseSound(tankcapscript cap) { return cap.closeSound; }
        public static void SetCapCloseSound(tankcapscript cap, AudioClip value) { cap.closeSound = value; TldStableCap.For(cap, true, false); }
        public static float CapOpenVolume(tankcapscript cap) { return cap.openVolume; }
        public static void SetCapOpenVolume(tankcapscript cap, float value) { cap.openVolume = value; TldStableCap.For(cap, true, false); }
        public static float CapCloseVolume(tankcapscript cap) { return cap.closeVolume; }
        public static void SetCapCloseVolume(tankcapscript cap, float value) { cap.closeVolume = value; TldStableCap.For(cap, true, false); }
        public static AudioClip CapTuneSound(tankcapscript cap) { return cap.tekerSound; }
        public static void SetCapTuneSound(tankcapscript cap, AudioClip value) { cap.tekerSound = value; TldStableCap.For(cap, true, false); }
        public static bool CapCanToggle(tankcapscript cap) { return cap.usable == null ? cap.canToggle : cap.usable.turnable; }
        public static void SetCapCanToggle(tankcapscript cap, bool value)
        {
            cap.canToggle = value;
            TldStableCap.For(cap, true, value).ToggleConfigured = true;
            if (cap.usable != null) cap.usable.turnable = value;
        }
        public static Vector3 CapClosedRotation(tankcapscript cap) { return cap.closedRot; }
        public static void SetCapClosedRotation(tankcapscript cap, Vector3 value)
        {
            cap.closedRot = value;
            TldStableCap.For(cap, true, false).LegacyRotation = true;
        }
        public static Vector3 CapOpenRotation(tankcapscript cap) { return cap.openRot; }
        public static void SetCapOpenRotation(tankcapscript cap, Vector3 value)
        {
            cap.openRot = value;
            TldStableCap.For(cap, true, false).LegacyRotation = true;
        }
        public static void RefreshCap(tankcapscript cap, bool sync)
        {
            if (cap == null) return;
            TldStableCap.For(cap, true, false).Refresh(true);
            if (sync && cap.usable != null && syncScript.IsConnected()) cap.usable.SyncMulti(true);
        }

        public static void SetCarLights(carscript car, int value) { if (car != null && car.lightUsable != null) car.lightUsable.UpdTurnState(value, true); }
        // carscript.driverSeat: the beta marks the driver's seat on the seat itself.
        public static seatscript CarDriverSeat(carscript car)
        {
            if (car == null) return null;
            seatscript secondary = null;
            foreach (var seat in car.GetComponentsInChildren<seatscript>(true))
            {
                if (seat == null) continue;
                if (seat.driverSeat0) return seat;
                if (seat.driverSeat1 && secondary == null) secondary = seat;
            }
            return secondary;
        }

        public static void SetCarDriverSeat(carscript car, seatscript seat)
        {
            if (car == null) return;
            foreach (var previous in car.GetComponentsInChildren<seatscript>(true))
                if (previous != null && previous != seat) previous.driverSeat0 = false;
            if (seat == null) return;
            seat.driverSeat0 = true;
            seat.Car = car;
        }

        static StableShells.interiorLightScript CarInterior(carscript car)
        {
            var lights = car.GetComponent<StableShells.interiorLightScript>();
            if (lights != null) return lights;
            lights = car.gameObject.AddComponent<StableShells.interiorLightScript>();
            lights.ISwitches = new usablescript[0];
            lights.ILight = new GameObject[0];
            lights.ILamp = new Renderer[0];
            return lights;
        }

        public static GameObject[] CarInteriorLights(carscript car) { return CarInterior(car).ILight; }
        public static void SetCarInteriorLights(carscript car, GameObject[] value) { CarInterior(car).ILight = value; }
        public static Renderer[] CarInteriorLamps(carscript car) { return CarInterior(car).ILamp; }
        public static void SetCarInteriorLamps(carscript car, Renderer[] value) { CarInterior(car).ILamp = value; }
        public static Material CarInteriorLampOn(carscript car) { return CarInterior(car).ILampOn; }
        public static void SetCarInteriorLampOn(carscript car, Material value) { CarInterior(car).ILampOn = value; }
        public static Material CarInteriorLampOff(carscript car) { return CarInterior(car).ILampOff; }
        public static void SetCarInteriorLampOff(carscript car, Material value) { CarInterior(car).ILampOff = value; }
        public static usablescript[] CarInteriorSwitches(carscript car) { return CarInterior(car).ISwitches; }
        public static void SetCarInteriorSwitches(carscript car, usablescript[] value) { CarInterior(car).ISwitches = value; }
        public static void RefreshCarInterior(carscript car)
        {
            if (car != null) RefreshLegacyInterior(car.GetComponent<StableShells.interiorLightScript>());
        }

        public static List<StableShells.interiorLightScript> SaveInteriorLights(tosaveitemscript item)
        {
            return SavedInterior(item).Lights;
        }

        public static void SetSaveInteriorLights(tosaveitemscript item, List<StableShells.interiorLightScript> value)
        {
            SavedInterior(item).Lights = value;
        }

        static TldStableInteriorList SavedInterior(tosaveitemscript item)
        {
            var lights = item.GetComponent<TldStableInteriorList>();
            if (lights != null) return lights;
            lights = item.gameObject.AddComponent<TldStableInteriorList>();
            lights.Lights = new List<StableShells.interiorLightScript>(item.GetComponentsInChildren<StableShells.interiorLightScript>(true));
            return lights;
        }

        internal static void RefreshLegacyInterior(StableShells.interiorLightScript lights)
        {
            if (lights == null || lights.ISwitches == null || lights.ISwitches.Length == 0) return;
            bool on = false;
            foreach (var control in lights.ISwitches)
                if (control != null && (control.turned || control.currentTurnState != 0)) { on = true; break; }
            if (lights.ILight != null)
                foreach (var light in lights.ILight)
                    if (light != null && light.activeSelf != on) light.SetActive(on);
            var material = on ? lights.ILampOn : lights.ILampOff;
            if (material != null && lights.ILamp != null)
                foreach (var lamp in lights.ILamp)
                    if (lamp != null && lamp.sharedMaterial != material) lamp.sharedMaterial = material;
        }
        // partslotscript.enableSpawn / spawn_def: the beta spawns its proper part.
        public static bool SlotEnableSpawn(partslotscript slot) { return slot != null && slot.enableSpawnProperPart; }
        public static void SetSlotEnableSpawn(partslotscript slot, bool value)
        {
            if (slot == null) return;
            slotSpawnOdds.GetOrCreateValue(slot);
            slot.enableSpawnProperPart = value;
        }
        public static int SlotSpawnDef(partslotscript slot)
        {
            if (slot == null || slot.properPart == null) return 0;
            var save = slot.properPart.GetComponent<tosaveitemscript>();
            return save == null ? 0 : save.id;
        }
        public static void SetSlotSpawnDef(partslotscript slot, int id) { if (slot != null && itemdatabase.s != null) slot.properPart = itemdatabase.s.Item(id); }

        sealed class SlotSpawnOdds
        {
            public float Spawn;
            public float Random;
        }

        static readonly ConditionalWeakTable<partslotscript, SlotSpawnOdds> slotSpawnOdds =
            new ConditionalWeakTable<partslotscript, SlotSpawnOdds>();

        public static float SlotSpawnChance(partslotscript slot)
        {
            SlotSpawnOdds odds;
            return slotSpawnOdds.TryGetValue(slot, out odds) ? odds.Spawn : 1f;
        }

        public static void SetSlotSpawnChance(partslotscript slot, float value)
        {
            slotSpawnOdds.GetOrCreateValue(slot).Spawn = value;
        }

        public static float SlotRandomChance(partslotscript slot)
        {
            SlotSpawnOdds odds;
            return slotSpawnOdds.TryGetValue(slot, out odds) ? odds.Random : 0f;
        }

        public static void SetSlotRandomChance(partslotscript slot, float value)
        {
            slotSpawnOdds.GetOrCreateValue(slot).Random = value;
        }

        internal static bool NativeSlotSpawn(partslotscript slot)
        {
            SlotSpawnOdds odds;
            if (!slotSpawnOdds.TryGetValue(slot, out odds)) return true;

            if (slot.randomTypeToStart != null)
            {
                slot.randomTypeToStart.FStart();
                partslotscript.StartPart(slot.part());
            }
            if (slot.enableSpawnProperPart && !slot.hasPart() && UnityEngine.Random.value < odds.Spawn)
            {
                GameObject prefab = slot.properPart;
                if (UnityEngine.Random.value < odds.Random && slot.randomParts != null)
                {
                    float total = 0f;
                    foreach (var candidate in slot.randomParts)
                        if (candidate != null && candidate.prefab != null && candidate.chance > 0f) total += candidate.chance;
                    float draw = UnityEngine.Random.Range(0f, total);
                    foreach (var candidate in slot.randomParts)
                    {
                        if (candidate == null || candidate.prefab == null || candidate.chance <= 0f) continue;
                        draw -= candidate.chance;
                        if (draw > 0f) continue;
                        prefab = candidate.prefab;
                        break;
                    }
                }
                var saved = prefab == null ? null : prefab.GetComponent<tosaveitemscript>();
                GameObject registered;
                if (saved != null && itemdatabase.s != null && itemdatabase.s.Item(saved.id, out registered))
                    UnityEngine.Object.Instantiate(registered, slot.T);
            }
            slot.FStart2();
            return false;
        }
        // God mode and the speed readout moved to the developer settings.
        // devSettings is a struct: read it, or copy, change and store it back.
        public static bool MainGodMode(mainscript main) { return kaposztaleves.s != null && kaposztaleves.s.settings.god; }
        public static void SetMainGodMode(mainscript main, bool value)
        {
            if (kaposztaleves.s == null) return;
            var settings = kaposztaleves.s.settings; settings.god = value; kaposztaleves.s.settings = settings;
        }
        public static void MainChGodMode(mainscript main, bool value) { SetMainGodMode(main, value); }
        public static bool MainShowKmh(mainscript main) { return kaposztaleves.s != null && kaposztaleves.s.settings.kmh; }
        public static void SetMainShowKmh(mainscript main, bool value)
        {
            if (kaposztaleves.s == null) return;
            var settings = kaposztaleves.s.settings; settings.kmh = value; kaposztaleves.s.settings = settings;
        }
        public static void MainChKmh(mainscript main, bool value) { SetMainShowKmh(main, value); }
        public static Transform PlayerBody(fpscontroller player) { return player == null ? null : player.BodyRot != null ? player.BodyRot : player.transform; }
        public static void PickupDrop(pickupable pick, bool unused) { if (pick != null) pick.Drop(); }
        public static savedatascript PlaceSave(itemPlaceRemoveScript place) { return savedatascript.s; }
        public static StableShells.TerrainGenerationSettings PlaceSettings(itemPlaceRemoveScript place) { return StableShells.TerrainGenerationSettings.staticReference; }
        sealed class PlaceAttempts { public int Count; }
        static readonly ConditionalWeakTable<itemPlaceRemoveScript, PlaceAttempts> placeAttempts =
            new ConditionalWeakTable<itemPlaceRemoveScript, PlaceAttempts>();

        public static int PlaceAttemptCount(itemPlaceRemoveScript place)
        {
            PlaceAttempts attempts;
            return placeAttempts.TryGetValue(place, out attempts) ? attempts.Count : 0;
        }

        public static void SetPlaceAttemptCount(itemPlaceRemoveScript place, int count)
        {
            placeAttempts.GetOrCreateValue(place).Count = count;
        }

        internal static void CountPlaceAttempt(itemPlaceRemoveScript place)
        {
            if (Running) placeAttempts.GetOrCreateValue(place).Count++;
        }

        internal static void ResetPlaceAttempts(itemPlaceRemoveScript place)
        {
            if (Running) SetPlaceAttemptCount(place, 0);
        }
        public static gamemodesettingsscript.gamemodesettings GameModeDefaults(gamemodesettingsscript modes) { return modes == null ? null : modes.S; }

        // tosaveitemscript.category: the first category table that lists the item (or what a mod set).
        static Dictionary<int, int> categoryOf;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<tosaveitemscript, object> categorySet = new System.Runtime.CompilerServices.ConditionalWeakTable<tosaveitemscript, object>();
        public static int ItemCategory(tosaveitemscript save)
        {
            if (save == null) return 0;
            object set;
            if (categorySet.TryGetValue(save, out set)) return (int)set;
            var db = itemdatabase.s;
            if (categoryOf == null && db != null && db.categories != null)
            {
                categoryOf = new Dictionary<int, int>();
                for (int k = 0; k < db.categories.Length; k++)
                    if (db.categories[k] != null && db.categories[k].items != null)
                        foreach (int id in db.categories[k].items) if (!categoryOf.ContainsKey(id)) categoryOf[id] = k;
            }
            int category;
            return categoryOf != null && categoryOf.TryGetValue(save.id, out category) ? category : 0;
        }
        public static void SetItemCategory(tosaveitemscript save, int value)
        {
            if (save == null) return;
            categorySet.Remove(save);
            categorySet.Add(save, value);
            TldLegacyCategories.Register(value, save.id, save.gameObject);
        }
        // roadGenScript.roadList / scale: the beta's placed road pieces; stable's scale was 1.
        static roadscript[] roadPieces = new roadscript[0];
        static float roadPiecesAt = -10f;
        public static roadscript[] RoadList(roadGenScript gen)
        {
            if (Time.realtimeSinceStartup - roadPiecesAt < 1f) return roadPieces;
            roadPiecesAt = Time.realtimeSinceStartup;
            return roadPieces = worldReady ? UnityEngine.Object.FindObjectsOfType<roadscript>() : new roadscript[0];
        }
        public static float RoadScale(roadGenScript gen) { return RoadOptionsFor(gen).Scale; }
        public static void SetRoadScale(roadGenScript gen, float value) { RoadOptionsFor(gen).Scale = value; }
        public static float RoadLength(roadGenScript gen) { return RoadOptionsFor(gen).Length; }
        public static void SetRoadLength(roadGenScript gen, float value) { RoadOptionsFor(gen).Length = value; }
        public static Vector3d RoadDoublePos(roadGenScript gen) { return RoadOptionsFor(gen).DoublePos; }
        public static void SetRoadDoublePos(roadGenScript gen, Vector3d value) { RoadOptionsFor(gen).DoublePos = value; }
        sealed class RoadOptionState { public float Scale = 1f; public float Length; public Vector3d DoublePos; }
        static readonly ConditionalWeakTable<roadGenScript, RoadOptionState> roadOptions =
            new ConditionalWeakTable<roadGenScript, RoadOptionState>();
        static RoadOptionState RoadOptionsFor(roadGenScript gen)
        {
            return gen == null ? new RoadOptionState() : roadOptions.GetOrCreateValue(gen);
        }


        internal static void CaptureAttachOriginalParent(attachablescript attach)
        {
            if (Running && !attach.started) SetAttachOriginalParent(attach, attach.transform.parent);
        }

        public static Transform AttachOriginalParent(attachablescript attach)
        {
            var state = TldStableAttach.Get(attach, false);
            return state != null && state.HasOriginalParent ? state.OriginalParent : attach.transform.parent;
        }

        public static void SetAttachOriginalParent(attachablescript attach, Transform parent)
        {
            var state = TldStableAttach.Get(attach, true);
            state.OriginalParent = parent;
            state.HasOriginalParent = true;
        }

        public static bool AttachConnectCars(attachablescript attach)
        {
            var state = TldStableAttach.Get(attach, false);
            if (state != null && state.ConnectCars.HasValue) return state.ConnectCars.Value;
            if (attach.utanfuto != null) return true;
            var grip = attach.tosave == null ? null : attach.tosave.attachgrip;
            return grip != null && grip.Grip == attach.gameObject;
        }

        public static void SetAttachConnectCars(attachablescript attach, bool connect)
        {
            TldStableAttach.Get(attach, true).ConnectCars = connect;
            if (connect && !attach.attached) attach.parentAtAttach = false;
        }

        internal static bool UsesStockTowOwnership(attachablescript attach)
        {
            var state = TldStableAttach.Get(attach, false);
            return state == null || state.ConnectCars != false;
        }

        public static Transform AttachOriginalTarget(attachablescript attach)
        {
            var state = TldStableAttach.Get(attach, false);
            if (state != null && state.HasOriginalTarget) return state.OriginalTarget;
            return AttachConnectCars(attach) && attach.attached ? attach.point : null;
        }

        public static void SetAttachOriginalTarget(attachablescript attach, Transform target)
        {
            var state = TldStableAttach.Get(attach, true);
            state.OriginalTarget = target;
            state.HasOriginalTarget = true;
        }

        public static bool AttachReattaching(attachablescript attach)
        {
            var state = TldStableAttach.Get(attach, true);
            state.RecoverJoint = true;
            return state.Reattaching;
        }

        public static void SetAttachReattaching(attachablescript attach, bool value)
        {
            var state = TldStableAttach.Get(attach, true);
            state.RecoverJoint = true;
            state.Reattaching = value;
            state.AutomaticRecovery = false;
            state.ReattachAt = Time.time + 0.01f;
            if (!value) state.ReattachPoint = null;
        }

        public static Transform AttachReattachPoint(attachablescript attach)
        {
            return TldStableAttach.Get(attach, true).ReattachPoint;
        }

        public static void SetAttachReattachPoint(attachablescript attach, Transform point)
        {
            TldStableAttach.Get(attach, true).ReattachPoint = point;
        }

        internal static bool NativeAttachmentUpdate(attachablescript attach)
        {
            if (!Running || attach.parentAtAttach) return true;
            var state = TldStableAttach.Get(attach, false);
            if (state == null || !state.RecoverJoint) return true;
            if (!state.Reattaching)
            {
                if (!attach.attached || attach.point == null || (attach.J != null && attach.J.connectedBody != null)) return true;
                state.ReattachPoint = attach.point;
                if (state.OriginalTarget != null && AttachConnectCars(attach)) state.ReattachPoint = state.OriginalTarget;
                state.ReattachAt = Time.time + 0.01f;
                state.Reattaching = true;
                state.AutomaticRecovery = true;
                return false;
            }
            if (Time.time < state.ReattachAt) return false;

            var point = state.ReattachPoint;
            state.Reattaching = false;
            state.ReattachPoint = null;
            if (state.AutomaticRecovery && !attach.attached) return true;
            if (point == null || attach.tosave == null || TrailerHitch.P(attach.tosave) == null) return true;
            if (point != attach.point)
            {
                var parent = point.parent;
                point = attach.NewPoint(parent, point.position, point.rotation);
            }
            if (syncScript.IsConnected() && !attach.tosave.ThisOneInCharge()) attach.dontSendMulti = true;
            attach.Attach(point);
            return false;
        }

        public static bool AttachSnapUp(attachablescript attach)
        {
            var state = TldStableAttach.Get(attach, false);
            return state != null && state.SnapUp;
        }

        public static void SetAttachSnapUp(attachablescript attach, bool snap)
        {
            TldStableAttach.Get(attach, true).SnapUp = snap;
        }

        public static StableShells.attachTargetscript AttachStartupTarget(attachablescript attach)
        {
            return StableAttachTarget(attach.attachAtStartTarget);
        }

        public static void SetAttachStartupTarget(attachablescript attach, StableShells.attachTargetscript target)
        {
            attach.attachAtStartTarget = ResolveAttachTarget(target);
        }

        static partslotscript ResolveAttachTarget(StableShells.attachTargetscript target)
        {
            if (target == null) return null;
            var slot = target.GetComponent<partslotscript>();
            if (slot == null)
            {
                slot = ModAddSlot(target.gameObject);
                slot.attachTarget = true;
            }
            slot.T = target.target != null ? target.target : target.transform;
            slot.tosaveitem = target.tosaveitem;
            slot.tipus = target.tipus;
            slot.dontRemoveAttachedWhenLookingAtThis = target.dontRemoveAttachedWhenLookingAtThis;
            return slot;
        }

        public static StableShells.attachTargetscript AttachCurrentTarget(attachablescript attach)
        {
            if (!attach.attached) return null;
            return StableAttachTarget(attach.slot);
        }

        public static void SetAttachCurrentTarget(attachablescript attach, StableShells.attachTargetscript target)
        {
            attach.slot = target == null ? null : target.GetComponent<partslotscript>();
        }

        static StableShells.attachTargetscript StableAttachTarget(partslotscript slot)
        {
            if (slot == null) return null;
            var target = slot.GetComponent<StableShells.attachTargetscript>();
            if (target == null) target = slot.gameObject.AddComponent<StableShells.attachTargetscript>();
            target.target = slot.T != null ? slot.T : slot.transform;
            target.tosaveitem = slot.tosaveitem;
            target.attachs = slot.parts;
            target.tipus = slot.tipus;
            target.dontRemoveAttachedWhenLookingAtThis = slot.dontRemoveAttachedWhenLookingAtThis;
            return target;
        }

        sealed class AttachmentTargets { public StableShells.attachTargetscript[] Ordered; }
        static readonly ConditionalWeakTable<tosaveitemscript, AttachmentTargets> attachmentTargets =
            new ConditionalWeakTable<tosaveitemscript, AttachmentTargets>();

        public static StableShells.attachTargetscript[] SaveAttachTargets(tosaveitemscript item)
        {
            AttachmentTargets targets;
            if (attachmentTargets.TryGetValue(item, out targets))
                return targets.Ordered ?? new StableShells.attachTargetscript[0];
            var result = new List<StableShells.attachTargetscript>();
            if (item.partslotscripts != null)
                foreach (var slot in item.partslotscripts)
                    if (slot != null && slot.attachTarget) result.Add(StableAttachTarget(slot));
            return result.ToArray();
        }

        public static void SetSaveAttachTargets(tosaveitemscript item, StableShells.attachTargetscript[] targets)
        {
            attachmentTargets.GetOrCreateValue(item).Ordered = targets;
            if (targets == null) return;
            foreach (var target in targets)
            {
                if (target == null) continue;
                target.tosaveitem = item;
                var slot = ResolveAttachTarget(target);
                TldModParts.Register(slot, true);
            }
        }

        internal static bool NativeRayAttach(attachablescript attach, RaycastHit hit)
        {
            if (!Running || !AttachSnapUp(attach) || hit.collider == null) return true;
            Transform point = attach.NewPoint(hit.collider.transform, hit.point + hit.normal * attach.offsetback, Quaternion.identity);
            OrientStableAttachPoint(attach, point);
            attach.attachedC = hit.collider;
            attach.Attach(point);
            return false;
        }

        static void OrientStableAttachPoint(attachablescript attach, Transform point)
        {
            point.rotation = attach.transform.rotation;
            point.up = Vector3.up;
        }

        // attachablescript.Attach(Transform point, RaycastHit hit)
        public static void AttachAt(attachablescript attach, Transform point, RaycastHit hit)
        {
            if (attach == null) return;
            if (AttachSnapUp(attach)) OrientStableAttachPoint(attach, point);
            attach.Attach(point, hit.collider);
        }

        // carscript.lampak: non-zero while any headlight is on.
        public static int CarLights(carscript car)
        {
            if (car == null || car.headlights == null) return 0;
            foreach (var slot in car.headlights)
            {
                if (slot == null) continue;
                var light = slot.GetComponentInChildren<headlightscript>();
                if (light != null && light.currentState > 0) return 1;
            }
            return 0;
        }

        // mainscript.seed: the world's seed.
        public static int WorldSeed(mainscript main)
        {
            var save = savedatascript.s;
            return save != null && save.data != null && save.data.properties != null ? save.data.properties.seed : 0;
        }

        // The public game's partscript is the beta's attachablescript.
        public static pickupable PartPickup(attachablescript part)
        {
            if (part == null) return null;
            var state = TldStableAttach.Get(part, false);
            if (state != null && state.HasPartPickup) return state.PartPickup;
            if (part.tosave != null && part.tosave.P != null) return part.tosave.P;
            return part.GetComponent<pickupable>();
        }
        public static attachablescript PickupPart(pickupable pick) { return PickupAttach(pick); }
        public static attachablescript SavePart(tosaveitemscript save) { return save == null ? null : save.attachable != null ? save.attachable : save.GetComponent<attachablescript>(); }
        // Stores wire up a part a mod builds itself: keep the beta's tosave.attachable link.
        public static void SetPartPickup(attachablescript part, pickupable pick)
        {
            if (part == null) return;
            var state = TldStableAttach.Get(part, true);
            state.HasPartPickup = true;
            state.PartPickup = pick;
            TldModParts.Adopt(part);
        }
        public static void SetPickupPart(pickupable pick, attachablescript part) { SetPickupAttach(pick, part); }
        public static void SetSavePart(tosaveitemscript save, attachablescript part) { if (save != null && part != null) save.attachable = part; TldModParts.Adopt(part); }
        public static attachablescript SlotPart(partslotscript slot) { return slot == null ? null : slot.part(); }
        public static void SetSlotPart(partslotscript slot, attachablescript part)
        {
            if (slot == null) return;
            if (slot.parts == null) slot.parts = new List<attachablescript>();
            var current = slot.part();
            if (current == part) return;

            // Stable's assignment only changed the slot's reference. Craft and UnCraft also
            // move objects and send network messages, so leave those to the calling mod.
            if (current != null) slot.parts.RemoveAt(0);
            if (part == null) return;
            slot.parts.Remove(part);
            slot.parts.Insert(0, part);
        }
        public static void SlotCraft(partslotscript slot, attachablescript part, bool instant)
        {
            // Mods re-mount on load as well; mounting twice would list the part twice.
            if (slot == null || part == null || (slot.part() == part && part.slot == slot)) return;
            TldModParts.Adopt(part);
            if (part.tosave != null) TldModParts.SyncColliders(part.tosave.P);
            slot.Craft(part);
        }

        // partslotscript.FStart(): on the public game it only set the slot up. The beta's also
        // spawns and mounts a starting part, which crashed on a mod's half-built part.
        public static void SlotFStart(partslotscript slot)
        {
            if (slot != null && slot.T == null) slot.T = slot.transform;
        }

        // headlightscript.lightState: L/R/Ls/Rs were single lights and renderers.
        public static Light StateLight(headlightscript.lightState state)
        {
            if (state == null || state.lights == null) return null;
            foreach (var group in state.lights) if (group != null && group.L != null) foreach (var light in group.L) if (light != null) return light;
            return null;
        }
        public static Renderer StateRenderer(headlightscript.lightState state)
        {
            if (state == null || state.renderers == null) return null;
            foreach (var group in state.renderers) if (group != null && group.R != null) foreach (var renderer in group.R) if (renderer != null) return renderer;
            return null;
        }
        public static List<Light> StateLights(headlightscript.lightState state)
        {
            var result = new List<Light>();
            if (state != null && state.lights != null)
                foreach (var group in state.lights) if (group != null && group.L != null) result.AddRange(group.L);
            return result;
        }
        public static List<Renderer> StateRenderers(headlightscript.lightState state)
        {
            var result = new List<Renderer>();
            if (state != null && state.renderers != null)
                foreach (var group in state.renderers) if (group != null && group.R != null) result.AddRange(group.R);
            return result;
        }

        public static Material StateMaterial(headlightscript.lightState state)
        {
            if (state == null || state.renderers == null) return null;
            foreach (var group in state.renderers)
                if (group != null && group.M != null) return group.M;
            return null;
        }

        public static void SetStateMaterial(headlightscript.lightState state, Material material)
        {
            if (state == null) return;
            if (state.renderers == null) state.renderers = new List<headlightscript.rendererClass>();
            if (state.renderers.Count == 0) state.renderers.Add(new headlightscript.rendererClass { R = new List<Renderer>() });
            foreach (var group in state.renderers)
            {
                if (group == null) continue;
                group.M = material;
                Color emission = material != null && material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
                group.defMIntensity = emission.maxColorComponent;
                group.defMColor = group.defMIntensity > 0f ? emission / group.defMIntensity : emission;
            }
        }

        sealed class SpawnAmmoRange { public float Min, Max; }
        static readonly ConditionalWeakTable<newRandomStuffSpawnScript.item, SpawnAmmoRange> spawnAmmoRanges =
            new ConditionalWeakTable<newRandomStuffSpawnScript.item, SpawnAmmoRange>();

        public static float SpawnAmmoMin(newRandomStuffSpawnScript.item item)
        {
            SpawnAmmoRange range;
            return spawnAmmoRanges.TryGetValue(item, out range) ? range.Min : 0f;
        }

        public static float SpawnAmmoMax(newRandomStuffSpawnScript.item item)
        {
            SpawnAmmoRange range;
            return spawnAmmoRanges.TryGetValue(item, out range) ? range.Max : 0f;
        }

        public static void SetSpawnAmmoMin(newRandomStuffSpawnScript.item item, float value) { spawnAmmoRanges.GetOrCreateValue(item).Min = value; }
        public static void SetSpawnAmmoMax(newRandomStuffSpawnScript.item item, float value) { spawnAmmoRanges.GetOrCreateValue(item).Max = value; }

        internal static void ApplySpawnAmmo(tosaveitemscript saved, newRandomStuffSpawnScript.item item)
        {
            SpawnAmmoRange range;
            if (saved.ammo == null || !spawnAmmoRanges.TryGetValue(item, out range) ||
                range.Min == range.Max || saved.GetComponent<sprayscript>() != null) return;
            int amount = UnityEngine.Random.Range((int)range.Min, (int)range.Max);
            if (amount <= saved.ammo.max) saved.ammo.SetIntAmmo(amount);
        }

        // ---- Mods creating saved world items ----
        // In multiplayer the host owns the world: a guest's mod spawner would add a second
        // copy of each item the host's spawner made (and the save ids would clash). On a
        // guest such an item is discarded before it is registered; the host's copy reaches
        // the guest through normal sync. Single player and the host are unchanged.
        static readonly HashSet<int> discarded = new HashSet<int>();
        static long discardedCount;
        internal static bool MultiplayerGuest()
        {
            try
            {
                var hood = networkingScript.s == null ? null : networkingScript.s.hood;
                return hood != null && hood.isConnected && !hood.isHost;
            }
            catch (Exception) { return false; }
        }
        static void Discard(tosaveitemscript save)
        {
            var go = save.gameObject;
            if (discarded.Count > 4096) discarded.Clear();
            discarded.Add(go.GetInstanceID());
            if (discardedCount++ % 50 == 0)
                Debug.Log(Tag + "multiplayer guest: a mod tried to create the saved item " + go.name + "; the host's copy is used instead (" + discardedCount + " so far).");
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
        }
        // tosaveitemscript.FStart(int id) (the beta takes uint)
        public static void ModRegisterItem(tosaveitemscript save, int id)
        {
            if (save == null) return;
            if (MultiplayerGuest()) { Discard(save); return; }
            save.FStart(unchecked((uint)id));
        }
        public static void ModRegisterItem(tosaveitemscript save, uint id) { ModRegisterItem(save, unchecked((int)id)); }
        // tosaveitemscript.FStart()
        public static void ModRegisterNew(tosaveitemscript save)
        {
            if (save == null) return;
            if (MultiplayerGuest()) { Discard(save); return; }
            save.FStart();
        }
        // mainscript.PostSpawn(GameObject): registers the object's tosaveitemscript too.
        public static void ModPostSpawn(mainscript main, GameObject go)
        {
            if (go == null) return;
            if (discarded.Remove(go.GetInstanceID())) return;
            if (MultiplayerGuest())
            {
                var save = go.GetComponent<tosaveitemscript>();
                if (save != null) { Discard(save); discarded.Remove(go.GetInstanceID()); return; }
            }
            var target = main != null ? main : mainscript.s;
            if (target != null) target.PostSpawn(go);
        }

        // itemdatabase.ReturnItem(index, 0)
        // The public game's ReturnItem(a, b) took the item id second: TLDLoader's box patch calls
        // ReturnItem(0, id). Reading the first number made every box hold item 0 (the error cube).
        public static GameObject ReturnItem(itemdatabase database, int first, int second)
        {
            var prefab = TldLegacyCategories.Find(first, second);
            if (prefab != null) return prefab;
            var db = database != null ? database : itemdatabase.s;
            return db == null ? null : db.Item(TldLoaderCompatPolicy.ReturnItemIndex(first, second));
        }

        public static void ReleaseLegacyItem(tosaveitemscript item, bool claim)
        {
            if (claim || item == null || !item.started || item.useThisForPos == null ||
                !syncScript.IsConnected() || !item.ThisOneInCharge()) return;
            item.ForcePosUpd(sendPosIfHasParent: true, forceGlobal: false, sendEvenIfNotInteracting: false);
            item.ForceAttachableUpd();
        }
        // tankscript.SetTankId(saveId, tankIndex) became SetID(uint, int).
        public static void SetTankId(tankscript tank, int saveId, int index)
        {
            if (tank != null) tank.SetID(unchecked((uint)saveId), index);
        }
        // breakablescript.noGib
        public static bool BreakableNoGib(breakablescript breakable) { return breakable != null && breakable.Gib == null && breakable.reducedGib == null; }
        public static void SetBreakableNoGib(breakablescript breakable, bool noGib)
        {
            if (breakable != null && noGib) { breakable.Gib = null; breakable.reducedGib = null; }
        }
        // itemdatabase.buildings: the beta's building prefabs.
        public static GameObject[] BuildingPrefabs(itemdatabase database)
        {
            var db = database != null ? database : itemdatabase.s;
            return db == null || db.pois == null ? new GameObject[0] : db.pois;
        }

        // T[] in the mod, List<T> on the beta (and the reverse). Field found by its beta name.
        static readonly Dictionary<string, FieldInfo> collectionFields = new Dictionary<string, FieldInfo>();
        static FieldInfo CollectionField(object owner, string name)
        {
            if (owner == null) return null;
            var type = owner.GetType();
            string key = type.FullName + "::" + name;
            FieldInfo field;
            if (collectionFields.TryGetValue(key, out field)) return field;
            for (var t = type; t != null && field == null; t = t.BaseType)
                field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            collectionFields[key] = field;
            return field;
        }
        public static T[] ListFieldAsArray<T>(object owner, string name)
        {
            var field = CollectionField(owner, name);
            var list = field == null ? null : field.GetValue(owner) as List<T>;
            return list == null ? new T[0] : list.ToArray();
        }
        public static void SetListFieldFromArray<T>(object owner, T[] value, string name)
        {
            var field = CollectionField(owner, name);
            if (field != null) field.SetValue(owner, value == null ? new List<T>() : new List<T>(value));
        }
        public static List<T> ArrayFieldAsList<T>(object owner, string name)
        {
            var field = CollectionField(owner, name);
            var array = field == null ? null : field.GetValue(owner) as T[];
            return array == null ? new List<T>() : new List<T>(array);
        }
        public static void SetArrayFieldFromList<T>(object owner, List<T> value, string name)
        {
            var field = CollectionField(owner, name);
            if (field != null) field.SetValue(owner, value == null ? new T[0] : value.ToArray());
        }

        // ---- TLDLoader item framework (ItemDatabase / ItemRegistrationBuilder) ----

        // ItemDatabase.MakeSaveable(mod, id, prefab): a beta save component whose prefab id
        // is the item's stable modded id (TldLoaderCompatPolicy.ModItemId).
        public static tosaveitemscript ModMakeSaveable(object database, string modId, int itemId, GameObject prefab)
        {
            if (prefab == null) return null;
            var save = prefab.GetComponent<tosaveitemscript>();
            if (save == null) save = prefab.AddComponent<tosaveitemscript>();
            TldModItems.FillEmptyCollections(save);
            int id = TldModItems.IdFor(modId, itemId);
            if (id > 0) save.id = id;
            save.RB = prefab.GetComponent<Rigidbody>();
            var attach = prefab.GetComponent<attachablescript>();
            if (attach != null) save.attachable = attach;
            var pick = prefab.GetComponent<pickupable>();
            if (pick != null && save.P == null) save.P = pick;
            return save;
        }

        // End of ItemDatabase.RegisterItem(mod, prefab, id): the prefab goes into
        // itemdatabase.items at its stable id, so the beta saves, loads and spawns it.
        public static void ModRegisterPrefab(string modId, int itemId, GameObject prefab)
        {
            try { TldModItems.Register(modId, itemId, prefab); }
            catch (Exception e) { Debug.LogWarning(Tag + "could not register item " + modId + "#" + itemId + ": " + e.Message); }
        }

        // ItemRegistrationBuilder.SpawnAt(chance, targets): add the item to the building
        // prefabs' random item spawners next to the listed vanilla items.
        static MethodInfo addSpawnToBuilding;
        public static void ModSpawnAt(object builder, int chance, GameObject[] targets)
        {
            if (builder == null || targets == null || targets.Length == 0) return;
            if (addSpawnToBuilding == null)
                addSpawnToBuilding = builder.GetType().GetMethod("AddSpawnToBuilding", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (addSpawnToBuilding == null) { Debug.LogWarning(Tag + "TLDLoader has no AddSpawnToBuilding; SpawnAt is skipped."); return; }
            int added = 0;
            foreach (var building in BuildingPrefabs(itemdatabase.s))
            {
                if (building == null) continue;
                try { addSpawnToBuilding.Invoke(builder, new object[] { building, chance, targets }); added++; }
                catch (Exception e) { Debug.LogWarning(Tag + "SpawnAt failed for building " + building.name + ": " + Unwrap(e)); }
            }
        }

        // headlightscript.SwitchState(int) became SwitchState(int, usablescript).
        public static void HeadlightSwitchState(headlightscript light, int state)
        {
            if (light != null) light.SwitchState(state, null);
        }

        public static void HeadlightSwitchStateNext(headlightscript light)
        {
            if (light == null || light.states == null || light.states.Count == 0) return;
            int next = light.switchToState + 1;
            light.SwitchState(next < light.states.Count ? next : 0, null);
            light.enabled = true;
        }

        public static void LayerFStart(layerScript layer)
        {
            if (layer == null || layer.GetComponent<Collider>() == null) return;
            layer.started = false;
            layer.FStart(layer.p, layer.index);
        }

        public static void RefreshPropertiesData(savedatascript save)
        {
            var properties = SaveProperties(save);
            if (properties != null && DataFromMenuScript.s != null && mainscript.s != null &&
                menuhandler.s != null && menuhandler.s.currentMainMap != null)
                save.RefreshProperties(properties);
        }

        public static void MirrorsUpdateMain(mainscript main, Vector3 position)
        {
            MirrorsUpdateDepth(main, position, false);
        }

        public static void MirrorsUpdateDepth(mainscript main, Vector3 position, bool depth)
        {
            if (main != null && main.player != null && main.player.Cam != null)
                main.MirrorsUpdate(position, main.player.Cam, depth);
        }

        public static void PlayerRightHandRelease(fpscontroller player)
        {
            if (player != null) player.RightHandRelease(true);
        }

        public static void MainPressedEscape(mainscript main)
        {
            if (main == null) return;
            bool mainMenu = DataFromMenuScript.s != null && DataFromMenuScript.s.mainmenu;
            main.PressedEscape(!mainMenu && main.player != null, mainMenu);
        }

        public static void MainTakeScreenshot(mainscript main)
        {
            if (main != null && main.player != null) main.TakeScreenShot(true);
        }

        public static void RoadFStart(roadGenScript road)
        {
            if (road == null) return;
            var map = road.mapSetting;
            if (map == null && menuhandler.s != null) map = menuhandler.s.currentMainMap;
            if (map != null) road.FStart(map);
        }

        public static void PickupFStart(pickupable pickup)
        {
            if (pickup != null) pickup.FStart(pickup.tosave);
        }

        public static void UsableUpdateTurnState(usablescript usable)
        {
            if (usable != null) usable.UpdTurnState(false);
        }

        public static void UsableSetTurnState(usablescript usable, int state)
        {
            if (usable != null) usable.UpdTurnState(state, false);
        }

        // Scene.name of the active scene, as the public game's two scenes.
        public static string SceneName(ref Scene scene)
        {
            string name = scene.name;
            try { if (scene.handle == SceneManager.GetActiveScene().handle) return TldLoaderCompatPolicy.SceneName(inWorld); }
            catch (Exception) { }
            return name;
        }

        // First statement of TLDLoader.ModLoader.LoadDLL(file).
        public static string TranslateMod(string file)
        {
            try { return TldLoaderHost.TranslateMod(file); }
            catch (Exception e)
            {
                lock (TranslationFailures) TranslationFailures.Add(Path.GetFileName(file));
                Debug.LogWarning(Tag + Path.GetFileName(file) + ": could not be translated (" + e.GetType().Name + ": " + e.Message + "); TLDLoader gets the original file.");
                return file;
            }
        }

        // ---- Driven by StableGameBridge.Driver.Update ----

        static void CaptureSceneLoad(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            var data = DataFromMenuScript.s;
            sceneChanged = true;
            sceneSaveLoad = data != null && data.load;
        }

        internal static void Tick()
        {
            if (phase == Phase.Off) return;
            var data = DataFromMenuScript.s;
            bool onMenu = data == null || data.mainmenu;
            if (phase == Phase.Waiting)
            {
                PatchOptions.Load();
                if (!TldLoaderCompatPolicy.Enabled(Environment.GetEnvironmentVariable(TldLoaderCompatPolicy.EnvironmentVariable), PatchOptions.TldLoaderMods))
                {
                    phase = Phase.Off;
                    if (Installed) TryRestore();
                    return;
                }
                if (!onMenu || menuhandler.s == null) { menuFrames = 0; return; }
                if (++menuFrames < 30) return;
                Start();
                return;
            }
            bool replacedWorld = sceneChanged && inWorld && !onMenu;
            if (inWorld && (onMenu || replacedWorld))
            {
                inWorld = false; worldReady = false; saveLoad = false; fallbackLoaded = false;
                TldModParts.Clear();
                UnloadMods();
                Call("SceneChanged", default(Scene), default(Scene));
                TldStableWorld.ResetDrawDistance();
                TldStableWorld.Tick(false);
                TldStableTerrain.Tick(false);
                Debug.Log(Tag + "world left: mods unloaded, TLDLoader told SceneMenu.");
            }
            // The menu clears its flag before the scene-loading coroutine runs.
            if (!inWorld && !onMenu && !sceneChanged) return;
            worldReady = !onMenu && StableGameBridge.WorldScene();
            // Before the save's items are placed, and whenever the save data changes.
            if (!onMenu) { try { TldModItems.ProtectMissing(); } catch (Exception e) { Debug.LogWarning(Tag + "modded item check failed: " + e.Message); } }
            var step = TldLoaderCompatPolicy.Step(inWorld, onMenu);
            if (step == TldLoaderCompatPolicy.Transition.EnterWorld)
            {
                inWorld = true;
                // Beta clears load when map setup finishes, before the loader's OnLoad.
                saveLoad = sceneSaveLoad;
                try { TldModDatabase.BeforeDbLoad(); } catch (Exception e) { Debug.LogWarning(Tag + "item database reset failed: " + e.Message); }
                TldModParts.Clear();
                Call("dbInit");
                TldModMaterials.Fix(true);
                Call("SceneChanged", default(Scene), default(Scene));
                fallbackLoaded = false;
                // Without its loading screen TLDLoader's own OnLoad pass stops at the first line.
                if (coreMissing) SetField("requiresOnLoad", false);
                Debug.Log(Tag + "world loading: TLDLoader told SceneNewRandom; mods run OnLoad once the world is ready.");
            }
            sceneChanged = false;
            if (inWorld) { try { TldModParts.Tick(); } catch (Exception e) { Debug.LogWarning(Tag + "mod part upkeep failed: " + e.Message); } }
            TldStableWorld.SyncDrawDistance();
            try { TldStableWorld.Tick(worldReady); } catch (Exception e) { Debug.LogWarning(Tag + "stable world stand-ins failed: " + e.Message); }
            TldStableTerrain.Tick(worldReady);
            try { TldModMaterials.Fix(false); } catch (Exception e) { Debug.LogWarning(Tag + "mod material check failed: " + e.Message); }
            if (coreMissing && inWorld && worldReady && !fallbackLoaded)
            {
                fallbackLoaded = true;
                LoadModsWithoutCore();
            }
            TickLobby();
        }

        // What TLDLoader's LoadMods coroutine does, minus its loading screen.
        static void LoadModsWithoutCore()
        {
            var onLoad = modType == null ? null : modType.GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var property = modLoaderType == null ? null : modLoaderType.GetProperty("LoadedMods", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var mods = property == null ? null : property.GetValue(null, null) as IEnumerable;
            if (onLoad == null || mods == null) return;
            int count = 0, failed = 0;
            foreach (object mod in mods)
            {
                if (mod == null || mod.GetType().Assembly == loader) continue;
                string id = Get(mod, "ID") as string;
                if (id == null || id.StartsWith("TLDLoader_", StringComparison.Ordinal)) continue;
                object disabled = Get(mod, "isDisabled");
                if (disabled is bool && (bool)disabled) continue;
                try { onLoad.Invoke(mod, null); count++; }
                catch (Exception e) { failed++; Debug.LogError(Tag + "mod " + id + " threw in OnLoad: " + Unwrap(e)); }
            }
            SetField("requiresOnLoad", false);
            SetField("allModsLoaded", true);
            Debug.Log(Tag + "ran OnLoad for " + count + " mod(s)" + (failed > 0 ? " (" + failed + " threw)" : "") + " without TLDLoader's loading screen.");
        }

        static void SetField(string name, object value)
        {
            var field = modLoaderType == null ? null : modLoaderType.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) { Debug.LogWarning(Tag + "TLDLoader has no field " + name + "."); return; }
            try { field.SetValue(null, value); } catch (Exception e) { Debug.LogWarning(Tag + "could not set TLDLoader." + name + ": " + Unwrap(e)); }
        }

        // TLDLoader's own SetActive calls; its panels from core.unity3d are null without the file.
        public static void GameObjectSetActive(GameObject target, bool value)
        {
            if (target != null) target.SetActive(value);
        }

        // TLDLoader's menu-panel Animator calls; the panel is null when core.unity3d is missing.
        public static void AnimatorSetBool(Animator animator, string name, bool value)
        {
            if (animator != null) animator.SetBool(name, value);
        }

        static void TryRestore()
        {
            try { if (EmbeddedCecil.Install()) TldLoaderHost.RestoreOriginal(ManagedDirectory); }
            catch (Exception e) { Debug.LogWarning(Tag + "could not put the original TLDLoader.dll back: " + e.Message); }
        }

        static void Start()
        {
            phase = Phase.Off;
            string managed = ManagedDirectory;
            string dll = Path.Combine(managed, "TLDLoader.dll");
            if (!File.Exists(dll)) return;
            try
            {
                if (!EmbeddedCecil.Install()) { Debug.LogWarning(Tag + "Mono.Cecil is not available; TLDLoader mods stay off."); return; }
                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                    if (loaded.GetName().Name == "TLDLoader") { Debug.LogWarning(Tag + "TLDLoader was already loaded by something else; leaving it alone."); return; }
                if (!TldLoaderHost.EnsureLoader(managed)) return;
                loader = Assembly.LoadFrom(dll);
                modLoaderType = loader.GetType("TLDLoader.ModLoader", true);
                modType = loader.GetType("TLDLoader.Mod", true);
                phase = Phase.Running;
                Call("InitMainMenu");
                var list = LoadedMods();
                Debug.Log(Tag + "TLDLoader " + Field("Version") + " started on the beta: " + list.Count + " mod(s) loaded" +
                    (list.Count == 0 ? "" : ": " + string.Join(", ", Describe(list).ToArray())) + ".");
                coreMissing = Field("loading") == null;
                if (coreMissing)
                {
                    string note = "TLDLoader's menu files are missing (Mods\\Assets\\TLDLoader_Core\\core.unity3d), so its loading screen and menu button are off. " +
                        "Mods still run, and M opens TLDLoader's mod menu.";
                    Debug.LogWarning(Tag + note);
                    TldModNotice.Show(note);
                }
                SceneManager.sceneLoaded += CaptureSceneLoad;
            }
            catch (Exception e)
            {
                phase = Phase.Off;
                Debug.LogError(Tag + "TLDLoader did not start: " + Unwrap(e));
            }
        }

        static object Call(string name, params object[] args)
        {
            if (modLoaderType == null) return null;
            try
            {
                var method = modLoaderType.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) { Debug.LogWarning(Tag + "TLDLoader has no " + name + "."); return null; }
                return method.Invoke(null, args);
            }
            catch (Exception e) { Debug.LogError(Tag + "TLDLoader " + name + " failed: " + Unwrap(e)); return null; }
        }

        static object Field(string name)
        {
            var field = modLoaderType == null ? null : modLoaderType.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(null);
        }

        static string Unwrap(Exception e)
        {
            while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            return e.GetType().Name + ": " + e.Message;
        }

        internal sealed class ModInfo { public string Id, Name, Version, File; public bool Disabled; }

        static object Get(object target, string property)
        {
            if (target == null) return null;
            var p = target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) return p.GetValue(target, null);
            var f = target.GetType().GetField(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(target);
        }

        // Player mods (TLDLoader's own core and console mods are left out).
        internal static List<ModInfo> LoadedMods()
        {
            var result = new List<ModInfo>();
            if (phase != Phase.Running || modLoaderType == null) return result;
            try
            {
                var property = modLoaderType.GetProperty("LoadedMods", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var mods = property == null ? null : property.GetValue(null, null) as IEnumerable;
                if (mods == null) return result;
                foreach (object mod in mods)
                {
                    if (mod == null) continue;
                    string id = Get(mod, "ID") as string;
                    if (id == null || id.StartsWith("TLDLoader", StringComparison.OrdinalIgnoreCase)) continue;
                    if (mod.GetType().Assembly == loader) continue;
                    object disabled = Get(mod, "isDisabled");
                    result.Add(new ModInfo
                    {
                        Id = id, Name = Get(mod, "Name") as string ?? id, Version = Get(mod, "Version") as string ?? "",
                        File = mod.GetType().Assembly.GetName().Name + ".dll", Disabled = disabled is bool && (bool)disabled,
                    });
                }
            }
            catch (Exception e) { Debug.LogWarning(Tag + "could not read TLDLoader's mod list: " + Unwrap(e)); }
            return result;
        }

        internal static List<string> InvalidMods()
        {
            var list = Field("InvalidMods") as IEnumerable;
            var result = new List<string>();
            if (list != null) foreach (object item in list) if (item != null) result.Add(item.ToString());
            return result;
        }

        static List<string> Describe(List<ModInfo> mods)
        {
            var rows = new List<string>();
            foreach (var mod in mods) rows.Add(mod.Name + " " + mod.Version + (mod.Disabled ? " (disabled)" : ""));
            return rows;
        }

        // The beta keeps the scene, so mods get OnUnload when the world goes away.
        static void UnloadMods()
        {
            var unload = modType == null ? null : modType.GetMethod("OnUnload", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var property = modLoaderType == null ? null : modLoaderType.GetProperty("LoadedMods", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var mods = property == null ? null : property.GetValue(null, null) as IEnumerable;
            if (unload == null || mods == null) return;
            foreach (object mod in mods)
            {
                if (mod == null || mod.GetType().Assembly == loader) continue;
                object disabled = Get(mod, "isDisabled");
                if (disabled is bool && (bool)disabled) continue;
                try { unload.Invoke(mod, null); }
                catch (Exception e) { Debug.LogWarning(Tag + "mod " + Get(mod, "ID") + " threw in OnUnload: " + Unwrap(e)); }
            }
        }

        // ---- Lobby: publish our mod list as host, warn (never block) as a guest ----

        const string LobbyKey = "roadworthy_tldmods";

        static void TickLobby()
        {
            if (Time.realtimeSinceStartup < nextLobbyCheck || Loopback.Active) return;
            nextLobbyCheck = Time.realtimeSinceStartup + 2;
            try
            {
                var hood = networkingScript.s == null ? null : networkingScript.s.hood;
                if (hood == null || !hood.isConnected) { warnedLobby = 0; return; }
                var lobby = hood.ConnectedLobbyID;
                if (!lobby.IsValid()) return;
                localList = LocalList();
                if (hood.isHost)
                {
                    if (SteamMatchmaking.GetLobbyData(lobby, LobbyKey) != localList) SteamMatchmaking.SetLobbyData(lobby, LobbyKey, localList);
                    return;
                }
                if (warnedLobby == (ulong)lobby) return;
                warnedLobby = (ulong)lobby;
                string note = TldLoaderCompatPolicy.DescribeDifference(localList, SteamMatchmaking.GetLobbyData(lobby, LobbyKey));
                if (note == null) return;
                Debug.LogWarning(Tag + note.Replace("\n", " "));
                TldModNotice.Show(note);
            }
            catch (Exception e) { Debug.LogWarning(Tag + "lobby mod check failed: " + e.Message); }
        }

        static string LocalList()
        {
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (var mod in LoadedMods()) if (!mod.Disabled) pairs.Add(new KeyValuePair<string, string>(mod.Id, mod.Version));
            return TldLoaderCompatPolicy.EncodeModList(pairs);
        }
    }

    // Prefab ids of items registered through TLDLoader's item framework.
    //  - The id is hashed from "mod ID#item id" (TldLoaderCompatPolicy.ModItemId), so two
    //    players with the same mods agree and the save does not depend on load order.
    //  - Ids are remembered per machine (Documents\TheLongDrive\Roadworthy\mod-item-ids.txt)
    //    and never handed to another item, even after its mod is removed.
    //  - A saved item whose mod is not loaded gets a placeholder prefab (the game's error
    //    item) that keeps its id, so it shows as an error object but is saved unchanged
    //    and comes back when the mod is installed again. Without that, the beta would put
    //    its own error item in the save and lose the item's id.
    internal static class TldModItems
    {
        const string Tag = "[TLD MP mods] ";
        static Dictionary<string, int> remembered;
        static readonly Dictionary<int, string> holders = new Dictionary<int, string>();
        static readonly HashSet<int> registeredThisSession = new HashSet<int>();
        static readonly Dictionary<int, GameObject> placeholders = new Dictionary<int, GameObject>();
        static Transform prefabHolder;
        static bool baseChecked, disabled;

        static string RegistryPath
        {
            get { return Path.Combine(TldLoaderBridge.LoaderDataRoot(), "Roadworthy", "mod-item-ids.txt"); }
        }

        static void LoadRegistry()
        {
            if (remembered != null) return;
            remembered = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(RegistryPath))
                    foreach (string line in File.ReadAllLines(RegistryPath))
                    {
                        int eq = line.LastIndexOf('=');
                        int id;
                        if (eq <= 0 || !int.TryParse(line.Substring(eq + 1), out id) || !TldLoaderCompatPolicy.IsModItemId(id)) continue;
                        string key = line.Substring(0, eq);
                        if (holders.ContainsKey(id)) continue;
                        remembered[key] = id; holders[id] = key;
                    }
            }
            catch (Exception e) { Debug.LogWarning(Tag + "could not read " + RegistryPath + ": " + e.Message); }
        }

        static void SaveRegistry()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath));
                var lines = new List<string> { "# Roadworthy: save ids of TLDLoader modded items (mod ID#item id=prefab id). Do not edit." };
                foreach (var pair in remembered) lines.Add(pair.Key + "=" + pair.Value);
                File.WriteAllLines(RegistryPath, lines.ToArray());
            }
            catch (Exception e) { Debug.LogWarning(Tag + "could not write " + RegistryPath + ": " + e.Message); }
        }

        static bool CheckBase()
        {
            if (baseChecked) return !disabled;
            var db = itemdatabase.s;
            if (db == null || db.items == null) return false;
            baseChecked = true;
            if (db.items.Length > TldLoaderCompatPolicy.ModItemBase && TldModItemsVanillaCount(db) > TldLoaderCompatPolicy.ModItemBase)
            {
                disabled = true;
                Debug.LogError(Tag + "the game now has more than " + TldLoaderCompatPolicy.ModItemBase + " items; modded TLDLoader items are not registered so they cannot clash with game items. Roadworthy needs an update.");
            }
            return !disabled;
        }

        static int TldModItemsVanillaCount(itemdatabase db)
        {
            return Enum.GetNames(typeof(itemdatabase.i)).Length;
        }

        internal static int IdFor(string modId, int itemId)
        {
            LoadRegistry();
            string key = TldLoaderCompatPolicy.ModItemKey(modId, itemId);
            int known;
            int id = TldLoaderCompatPolicy.ModItemId(key, remembered.TryGetValue(key, out known) ? known : -1,
                delegate(int candidate) { string holder; return holders.TryGetValue(candidate, out holder) ? holder : null; });
            if (id < 0) { Debug.LogError(Tag + "no free modded item id left for " + key + "."); return -1; }
            if (!remembered.TryGetValue(key, out known) || known != id)
            {
                remembered[key] = id; holders[id] = key;
                SaveRegistry();
            }
            return id;
        }

        static void EnsureSlot(itemdatabase db, int id)
        {
            if (db.items.Length <= id)
            {
                var items = db.items;
                Array.Resize(ref items, id + 1);
                db.items = items;
            }
        }

        internal static void Register(string modId, int itemId, GameObject prefab)
        {
            if (prefab == null || !CheckBase()) return;
            int id = IdFor(modId, itemId);
            if (id < 0) return;
            var db = itemdatabase.s;
            EnsureSlot(db, id);
            GameObject placeholder;
            if (placeholders.TryGetValue(id, out placeholder)) { placeholders.Remove(id); if (placeholder != null) UnityEngine.Object.Destroy(placeholder); }
            db.items[id] = prefab;
            var save = prefab.GetComponent<tosaveitemscript>();
            if (save != null) save.id = id;
            if (registeredThisSession.Add(id))
                Debug.Log(Tag + "modded item " + TldLoaderCompatPolicy.ModItemKey(modId, itemId) + " (" + prefab.name + ") has save id " + id + ".");
        }

        // Newly added tosaveitemscripts have no inspector-filled lists; the beta expects them.
        internal static void FillEmptyCollections(tosaveitemscript save)
        {
            foreach (var field in typeof(tosaveitemscript).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.GetValue(save) != null) continue;
                var type = field.FieldType;
                try
                {
                    if (type.IsArray) field.SetValue(save, Array.CreateInstance(type.GetElementType(), 0));
                    else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) field.SetValue(save, Activator.CreateInstance(type));
                }
                catch (Exception) { }
            }
        }

        // Saved modded items whose prefab is not registered get a placeholder that keeps the id.
        static savedata scannedData;
        static int scannedCount = -1;
        internal static void ProtectMissing()
        {
            var db = itemdatabase.s;
            var save = savedatascript.s;
            if (db == null || db.items == null || save == null || save.data == null || save.data.itemData == null || save.data.itemData.items == null) return;
            var items = save.data.itemData.items;
            if (ReferenceEquals(scannedData, save.data) && scannedCount == items.Count) return;
            scannedData = save.data; scannedCount = items.Count;
            if (!CheckBase()) return;
            var made = new List<string>();
            foreach (var item in items)
            {
                if (item == null || !TldLoaderCompatPolicy.IsModItemId(item.prefabID)) continue;
                int id = item.prefabID;
                if (db.items.Length > id && db.items[id] != null) continue;
                if (MakePlaceholder(db, id)) { string key; made.Add(id + (holders.TryGetValue(id, out key) ? " (" + key + ")" : "")); }
            }
            if (made.Count > 0)
                Debug.LogWarning(Tag + made.Count + " saved item type(s) come from a TLDLoader mod that is not loaded: " + string.Join(", ", made.ToArray()) + ". They show as error items and are kept in the save until the mod is back.");
        }

        // The database went back to its pre-DbLoad state: placeholders left it, mods register again.
        internal static void DatabaseRestored()
        {
            foreach (var placeholder in placeholders.Values) if (placeholder != null) UnityEngine.Object.Destroy(placeholder);
            placeholders.Clear();
            scannedData = null; scannedCount = -1;
        }

        static bool MakePlaceholder(itemdatabase db, int id)
        {
            if (db.gerror == null) return false;
            if (prefabHolder == null)
            {
                var holder = new GameObject("Roadworthy modded item placeholders");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
                prefabHolder = holder.transform;
            }
            var copy = UnityEngine.Object.Instantiate(db.gerror, prefabHolder, false);
            copy.name = db.gerror.name;
            var save = copy.GetComponent<tosaveitemscript>();
            if (save == null) { save = copy.AddComponent<tosaveitemscript>(); FillEmptyCollections(save); }
            save.id = id;
            EnsureSlot(db, id);
            db.items[id] = copy;
            placeholders[id] = copy;
            return true;
        }
    }

    // Parts and slots that mods build at run time, made to behave like the beta's own:
    //  - a part's T/RB/tosave point at its own object (mods copy another item's attachablescript,
    //    which keeps that item's references, so held parts drove the wrong rigidbody);
    //  - a slot joins its item's partslotscripts with a slot id, so a mounted part saves its
    //    slot index like any built-in slot;
    //  - a saved mount is applied once the mod has added the part (the beta loads mounts while
    //    the item spawns, before mods add their parts) and the parent and its slot exist.
    internal static class TldModParts
    {
        const string Tag = "[TLD MP mods] ";
        const float GiveUpSeconds = 60f;
        sealed class PendingSlot { public partslotscript Slot; public float Since; }
        sealed class PendingMount { public attachablescript Part; public save_attachable Record; public float Since; }
        static readonly List<PendingSlot> slots = new List<PendingSlot>();
        static readonly List<PendingMount> mounts = new List<PendingMount>();
        static readonly HashSet<int> adopted = new HashSet<int>();
        static int restored, lost, reported;
        const int ReportLimit = 12;

        internal static void Clear()
        {
            slots.Clear(); mounts.Clear(); adopted.Clear(); resyncs.Clear();
            restored = 0; lost = 0; reported = 0;
        }

        // The first few run-time parts and slots of each world are logged, so a mod whose parts
        // silently do nothing can be followed in Player.log.
        static void Report(string message)
        {
            if (reported >= ReportLimit) return;
            reported++;
            Debug.Log(Tag + message + (reported == ReportLimit ? " (further part/slot lines this world are not logged)" : ""));
        }

        static string Owner(Component component)
        {
            var root = component == null ? null : component.transform.root;
            return root == null ? "?" : root.name;
        }

        internal static void Adopt(attachablescript part)
        {
            if (part == null) return;
            var go = part.gameObject;
            var own = go.transform;
            if (part.T == null || (part.T != own && !part.T.IsChildOf(own))) part.T = own;
            if (part.RB == null || part.RB.gameObject != go) part.RB = go.GetComponent<Rigidbody>();
            var save = go.GetComponent<tosaveitemscript>();
            if (part.tosave == null || part.tosave.gameObject != go) part.tosave = save;
            // The beta's Craft marks tosave.P as crafted; mods set the public game's fields instead.
            if (part.tosave != null)
            {
                var pickup = TldStableAttach.Get(part, false);
                if (pickup != null && pickup.HasPartPickup)
                    part.tosave.P = pickup.PartPickup != null ? pickup.PartPickup : go.GetComponent<pickupable>();
                else if (part.tosave.P == null)
                    part.tosave.P = go.GetComponent<pickupable>();
            }
            if (part.tosave != null) { SyncColliders(part.tosave.P); QueueResync(part.tosave.P); }
            if (!part.attached && part.slot != null && part.slot.part() != part) part.slot = null;
            if (save == null || !adopted.Add(part.GetInstanceID()) || part.attached) return;
            var record = SavedMount(save.idInSave);
            if (record != null) mounts.Add(new PendingMount { Part = part, Record = record, Since = Time.realtimeSinceStartup });
            Report("mod part " + go.name + " (save id " + save.idInSave + ") on " + Owner(part) + ": " + (save.P == null ? "no pickup" : save.P.cols.Count + " collider(s)") +
                (record != null ? ", saved mount queued" : ""));
        }

        // Vanilla items list every collider they use in pickupable.cols; the beta switches exactly
        // those on and off when the item is held, dropped or mounted. Mods hide an item's own
        // colliders and add a model with new ones, so the list was backwards: mounting switched the
        // hidden colliders back on inside the car and left the new ones unmanaged, and the car was
        // thrown. The list becomes the colliders the item really uses.
        // The player's look ray finds an item through the hit collider's layerScript.p. Mods merge
        // other items in as visuals (Nitrous puts a compass inside its gauge and destroys the
        // compass's pickupable), leaving colliders whose layerScript points at a pickupable that is
        // gone: the item could not be picked up. Such layerScripts are re-pointed at this item.
        // Destroy runs at the end of the frame, so items are synced again a frame and a moment later.
        internal static void SyncColliders(pickupable pick)
        {
            if (pick == null) return;
            if (pick.cols == null) pick.cols = new List<layerScript>();
            pick.cols.RemoveAll(layer => layer == null || layer.col == null || !layer.col.enabled || layer.p != pick);
            var listed = new HashSet<Collider>();
            foreach (var layer in pick.cols) listed.Add(layer.col);
            foreach (var collider in pick.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || !collider.enabled || collider.isTrigger || listed.Contains(collider)) continue;
                if (collider.GetComponentInParent<pickupable>() != pick) continue;
                var layer = collider.GetComponent<layerScript>();
                if (layer == null) layer = collider.gameObject.AddComponent<layerScript>();
                if (layer.p != pick) layer.started = false;
                try { layer.FStart(pick, pick.cols.Count); } catch (Exception) { continue; }
                if (layer.col != collider || layer.p != pick) continue;
                pick.cols.Add(layer);
                listed.Add(collider);
            }
        }

        sealed class PendingSync { public pickupable Pick; public int Frame; public float Time; }
        static readonly List<PendingSync> resyncs = new List<PendingSync>();

        static void QueueResync(pickupable pick)
        {
            if (pick == null) return;
            resyncs.Add(new PendingSync { Pick = pick, Frame = UnityEngine.Time.frameCount + 1, Time = 0f });
            resyncs.Add(new PendingSync { Pick = pick, Frame = 0, Time = UnityEngine.Time.realtimeSinceStartup + 2f });
        }

        static void TickResyncs()
        {
            for (int i = resyncs.Count - 1; i >= 0; i--)
            {
                var pending = resyncs[i];
                if (pending.Pick == null) { resyncs.RemoveAt(i); continue; }
                if (pending.Frame > 0 ? UnityEngine.Time.frameCount < pending.Frame : UnityEngine.Time.realtimeSinceStartup < pending.Time) continue;
                resyncs.RemoveAt(i);
                try { SyncColliders(pending.Pick); } catch (Exception) { }
            }
        }

        static save_attachable SavedMount(uint id)
        {
            if (id == 0) return null;
            var data = savedatascript.s == null || savedatascript.s.data == null ? null : savedatascript.s.data.itemData;
            if (data == null || data.attachable == null) return null;
            foreach (var record in data.attachable)
                if (record != null && record.id == id && record.attachType != 0) return record;
            return null;
        }

        internal static void TrackSlot(partslotscript slot)
        {
            if (slot != null) slots.Add(new PendingSlot { Slot = slot, Since = Time.realtimeSinceStartup });
        }

        internal static void Tick()
        {
            if (resyncs.Count > 0) TickResyncs();
            if (slots.Count == 0 && mounts.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                var pending = slots[i];
                bool expired = now - pending.Since > GiveUpSeconds;
                if (pending.Slot == null || Register(pending.Slot, expired) || expired) slots.RemoveAt(i);
            }
            for (int i = mounts.Count - 1; i >= 0; i--)
            {
                var pending = mounts[i];
                if (pending.Part == null || pending.Part.attached) { mounts.RemoveAt(i); continue; }
                if (!Ready(pending.Record))
                {
                    if (now - pending.Since > GiveUpSeconds) { lost++; mounts.RemoveAt(i); Debug.LogWarning(Tag + pending.Part.name + ": its saved mount could not be restored (the item it was mounted on did not load)."); }
                    continue;
                }
                mounts.RemoveAt(i);
                try { pending.Record.Load(pending.Part); restored++; }
                catch (Exception e) { lost++; Debug.LogWarning(Tag + pending.Part.name + ": restoring its saved mount failed: " + e.GetType().Name + " " + e.Message); }
                if (mounts.Count == 0 && restored > 0) Debug.Log(Tag + "restored " + restored + " saved mount(s) of parts that mods add at run time.");
            }
        }

        // A slot joins its item's slot list once it is parented under the item and the item has
        // its save id (or the wait ran out).
        internal static bool Register(partslotscript slot, bool force)
        {
            var owner = slot.tosaveitem != null ? slot.tosaveitem : slot.GetComponentInParent<tosaveitemscript>();
            if (owner == null || (owner.idInSave == 0 && !force)) return false;
            if (owner.partslotscripts == null) owner.partslotscripts = new List<partslotscript>();
            int index = owner.partslotscripts.IndexOf(slot);
            if (index < 0) { owner.partslotscripts.Add(slot); index = owner.partslotscripts.Count - 1; }
            if (slot.T == null) slot.T = slot.transform;
            slot.tosaveitem = owner;
            slot.tosaveid = owner.idInSave;
            slot.id = index;
            slot.setid = true;
            Report("mod slot " + slot.name + " joined " + owner.name + " as slot " + index + " (" + string.Join("/", slot.tipus ?? new string[0]) + ")" + (owner.idInSave == 0 ? " before it had a save id" : ""));
            return true;
        }

        static bool Ready(save_attachable record)
        {
            if (record.attachType != 1) return true;
            var places = itemPlaceRemoveScript.s;
            tosaveitemscript parent;
            if (places == null || !places.GetOne(record.parentid, out parent) || parent == null) return false;
            if (record.indexType != 2 || record.index < 0) return true;
            return parent.partslotscripts != null && record.index < parent.partslotscripts.Count;
        }
    }

    // Stable's attachment save fields a mod stored, until it calls Load (TldLoaderBridge.AttachLoad).
    internal sealed class TldStableAttach
    {
        internal tosaveitemscript Target;
        internal List<Collider> Colliders;
        internal bool HasPartPickup;
        internal pickupable PartPickup;
        internal Collider IgnoredTarget;
        internal readonly List<Collider> IgnoredColliders = new List<Collider>();
        internal bool LockRotation;
        internal ConfigurableJoint LockedJoint;
        ConfigurableJointMotion previousX, previousY, previousZ;
        internal bool Has, IsCollider = true;
        internal int ColliderId = -1;
        internal Vector3 PointLocalPos;
        internal Transform OriginalParent;
        internal bool HasOriginalParent, SnapUp;
        internal bool? ConnectCars;
        internal Transform OriginalTarget, ReattachPoint;
        internal bool HasOriginalTarget, RecoverJoint, Reattaching, AutomaticRecovery;
        internal float ReattachAt;

        internal void ApplyJointLock(ConfigurableJoint joint)
        {
            if (joint == null) return;
            if (LockRotation)
            {
                if (LockedJoint != joint)
                {
                    LockedJoint = joint;
                    previousX = joint.angularXMotion;
                    previousY = joint.angularYMotion;
                    previousZ = joint.angularZMotion;
                }
                joint.angularXMotion = ConfigurableJointMotion.Locked;
                joint.angularYMotion = ConfigurableJointMotion.Locked;
                joint.angularZMotion = ConfigurableJointMotion.Locked;
            }
            else if (LockedJoint == joint)
            {
                joint.angularXMotion = previousX;
                joint.angularYMotion = previousY;
                joint.angularZMotion = previousZ;
                LockedJoint = null;
            }
        }

        internal void ClearCollisionPairs()
        {
            if (IgnoredTarget != null)
                foreach (var collider in IgnoredColliders)
                    if (collider != null) Physics.IgnoreCollision(collider, IgnoredTarget, false);
            IgnoredColliders.Clear();
            IgnoredTarget = null;
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<attachablescript, TldStableAttach> records =
            new System.Runtime.CompilerServices.ConditionalWeakTable<attachablescript, TldStableAttach>();
        internal static TldStableAttach Get(attachablescript attach, bool create)
        {
            TldStableAttach record;
            if (records.TryGetValue(attach, out record)) return record;
            return create ? records.GetOrCreateValue(attach) : null;
        }
    }

    // Legacy alignment groups become world-space pads. The tile workers read immutable spatial
    // snapshots, so replacing a group's settings never changes a list while a tile reads it.
    internal sealed class TldStableTerrain
    {
        const double CellSize = 256;
        const float DefaultRangeMultiplier = 4f;
        static readonly ConditionalWeakTable<terrainHeightAlignToBuildingScript, TldStableTerrain> groups =
            new ConditionalWeakTable<terrainHeightAlignToBuildingScript, TldStableTerrain>();
        static readonly Dictionary<terrainHeightAlignToBuildingScript, Pad[]> active =
            new Dictionary<terrainHeightAlignToBuildingScript, Pad[]>();

        struct Pad { public Vector3d Center; public double Inner, Outer; }
        sealed class Index
        {
            public readonly Dictionary<Vector2Int, List<Pad>> Cells = new Dictionary<Vector2Int, List<Pad>>();
            public readonly List<Pad> Large = new List<Pad>();
        }
        static volatile Index index;
        static bool dirty;
        static float nextPrune;
        terrainHeightAlignToBuildingScript owner;
        internal bool Started, HasLegacyMarkers;
        internal Transform[] Markers;
        internal float[] Ranges = new float[0];
        internal List<StableShells.terrainHeightAlignToBuildingScript_helpPos> Points =
            new List<StableShells.terrainHeightAlignToBuildingScript_helpPos>();

        internal static TldStableTerrain For(terrainHeightAlignToBuildingScript marker)
        {
            return groups.GetValue(marker, key => new TldStableTerrain { owner = key, Markers = new[] { key.transform } });
        }

        internal void Start(bool force)
        {
            if (Started && !force) return;
            Started = true;
            if (mainscript.s == null) return;
            if (Ranges.Length < Markers.Length) Array.Resize(ref Ranges, Markers.Length);
            Points = new List<StableShells.terrainHeightAlignToBuildingScript_helpPos>(Markers.Length);
            for (int i = 0; i < Markers.Length; i++)
            {
                var marker = Markers[i];
                if (marker == null) continue;
                float inner = Mathf.Abs(marker.localScale.x) * 0.5f;
                float multiplier = Ranges[i] == 0f ? DefaultRangeMultiplier : Mathf.Max(0f, Ranges[i]);
                Ranges[i] = multiplier;
                float outer = inner * multiplier;
                Points.Add(new StableShells.terrainHeightAlignToBuildingScript_helpPos
                {
                    pos = (Vector3)mainscript.GlobalFromUnityPos(marker.position),
                    range = new Vector2(inner * inner, outer * outer)
                });
                if (HasLegacyMarkers && marker != owner.transform) marker.gameObject.SetActive(false);
            }
            if (HasLegacyMarkers) Publish();
        }

        internal void Publish()
        {
            if (!owner.gameObject.scene.isLoaded) return;
            var pads = new List<Pad>(Points.Count);
            foreach (var point in Points)
            {
                if (point == null) continue;
                double outer = Math.Sqrt(Math.Max(point.range.x, point.range.y));
                var center = new Vector3d(point.pos.x, point.pos.y, point.pos.z);
                if (outer > 0 && Finite(outer) && Finite(center.x) && Finite(center.y) && Finite(center.z))
                    pads.Add(new Pad { Center = center, Inner = Math.Sqrt(Math.Max(0f, point.range.x)), Outer = outer });
            }
            active[owner] = pads.ToArray();
            dirty = true;
        }

        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static int Cell(double value)
        {
            return (int)Math.Max(int.MinValue + 1d, Math.Min(int.MaxValue - 1d, Math.Floor(value / CellSize)));
        }

        internal static void Tick(bool worldReady)
        {
            if (!worldReady)
            {
                active.Clear();
                index = null;
                dirty = false;
                return;
            }
            float now = Time.unscaledTime;
            if (active.Count > 0 && now >= nextPrune)
            {
                nextPrune = now + 1f;
                var removed = new List<terrainHeightAlignToBuildingScript>();
                foreach (var pair in active) if (pair.Key == null) removed.Add(pair.Key);
                foreach (var marker in removed) active.Remove(marker);
                dirty |= removed.Count > 0;
            }
            if (!dirty) return;
            var next = new Index();
            foreach (var pads in active.Values)
                foreach (var pad in pads)
                {
                    if (pad.Outer > 1024) { next.Large.Add(pad); continue; }
                    for (int x = Cell(pad.Center.x - pad.Outer); x <= Cell(pad.Center.x + pad.Outer); x++)
                        for (int z = Cell(pad.Center.z - pad.Outer); z <= Cell(pad.Center.z + pad.Outer); z++)
                        {
                            var cell = new Vector2Int(x, z);
                            List<Pad> list;
                            if (!next.Cells.TryGetValue(cell, out list)) next.Cells[cell] = list = new List<Pad>();
                            list.Add(pad);
                        }
                }
            index = next;
            dirty = false;
        }

        internal static poiGenScript.terrainAlignValueClass AlignHeight(poiGenScript.terrainAlignValueClass native, Vector3d position)
        {
            var current = index;
            if (current == null || native.lerp >= 1) return native;
            double height = native.height, weight = native.lerp, closest = double.MaxValue;
            List<Pad> cells;
            if (current.Cells.TryGetValue(new Vector2Int(Cell(position.x), Cell(position.z)), out cells))
                Select(cells, position, ref height, ref weight, ref closest);
            Select(current.Large, position, ref height, ref weight, ref closest);
            return weight > native.lerp ? new poiGenScript.terrainAlignValueClass(height, weight) : native;
        }

        static void Select(List<Pad> pads, Vector3d position, ref double height, ref double weight, ref double closest)
        {
            foreach (var pad in pads)
            {
                double x = pad.Center.x - position.x, z = pad.Center.z - position.z;
                double distance = Math.Sqrt(x * x + z * z);
                if (distance > pad.Outer) continue;
                double blend = pad.Outer <= pad.Inner ? 1 : Mathd.InverseLerp(-1, 1, Mathd.Sin(Mathd.Lerp(-90, 90,
                    Mathd.InverseLerp(pad.Outer, pad.Inner, distance)) * 0.01745329));
                if (blend < weight || (blend == weight && distance >= closest)) continue;
                height = pad.Center.y;
                weight = blend;
                closest = distance;
            }
        }
    }

    internal sealed class TldStableInteriorList : MonoBehaviour
    {
        internal List<StableShells.interiorLightScript> Lights;

        void LateUpdate()
        {
            if (Lights == null) return;
            foreach (var light in Lights) TldLoaderBridge.RefreshLegacyInterior(light);
        }
    }

    // Legacy fuel caps follow their beta usable state or their valve position.
    internal sealed class TldStableCap : MonoBehaviour
    {
        internal tankcapscript Cap;
        internal Transform TCap, TOpen, TClosed;
        internal List<GameObject> GOpen, GClosed;
        internal bool LegacyRotation;
        internal bool ToggleConfigured;
        float lerp;
        float lastValve;
        bool shown, lastOpen;

        internal static TldStableCap For(tankcapscript cap, bool create, bool ensureUsable = true)
        {
            if (cap == null) return null;
            TldStableCap stable = null;
            foreach (var candidate in cap.GetComponents<TldStableCap>())
                if (candidate.Cap == cap) { stable = candidate; break; }
            if (!create) return stable;
            if (stable == null)
            {
                stable = cap.gameObject.AddComponent<TldStableCap>();
                stable.Cap = cap;
            }
            if (ensureUsable && cap.usable == null && (!stable.ToggleConfigured || cap.canToggle))
            {
                var usable = cap.GetComponent<usablescript>();
                if (usable == null)
                {
                    usable = cap.gameObject.AddComponent<usablescript>();
                    usable.turnable = true;
                    usable.maxTurnState = 1;
                }
                cap.usable = usable;
                try { usable.FStart(); } catch (Exception) { }
            }
            return stable;
        }

        bool Open()
        {
            if (Cap == null) return false;
            if (Cap.usable != null && Cap.usable.turnable) return Cap.usable.currentTurnState != 0;
            return Cap.valve > 0f;
        }

        static void Show(List<GameObject> objects, bool value)
        {
            if (objects == null) return;
            foreach (var o in objects) if (o != null) o.SetActive(value);
        }

        void LateUpdate() { Refresh(false); }

        internal void Refresh(bool snap)
        {
            if (Cap == null) return;
            bool open = Open();
            if (shown && Cap.tekerSound != null && lastValve > 0f && lastValve < 1f && lastValve != Cap.valve)
                AudioSource.PlayClipAtPoint(Cap.tekerSound, transform.position, Mathf.Abs(Cap.valve - lastValve));
            lastValve = Cap.valve;
            if (!shown || open != lastOpen)
            {
                // Stable played the cap's own open/close sounds; the beta keeps them but no longer does.
                if (shown)
                {
                    var clip = open ? Cap.openSound : Cap.closeSound;
                    if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, open ? Cap.openVolume : Cap.closeVolume);
                }
                shown = true; lastOpen = open; Show(GOpen, open); Show(GClosed, !open);
            }
            float target = open ? 1f : 0f;
            lerp = snap ? target : Mathf.MoveTowards(lerp, target, Time.deltaTime * 4f);
            if (LegacyRotation && Cap.frot)
            {
                Transform moving = TCap != null ? TCap : Cap.usable == null ? null : Cap.usable.TDoor;
                if (moving != null) moving.localEulerAngles = Vector3.Lerp(Cap.closedRot, Cap.openRot, lerp);
                return;
            }
            if (TCap == null) return;
            if (TOpen != null && TClosed != null)
            {
                TCap.position = Vector3.Lerp(TClosed.position, TOpen.position, lerp);
                TCap.rotation = Quaternion.Lerp(TClosed.rotation, TOpen.rotation, lerp);
            }
            else if (Cap.frot) TCap.localEulerAngles = Vector3.Lerp(Cap.closedRot, Cap.openRot, lerp);
        }
    }

    // The stable world generator's objects that mods reach (TerrainGenerator.TG,
    // TerrainGenerationSettings/TerrainManager.staticReference, roadGenScript.roads, mainscript.DDS),
    // as stand-ins filled from the beta's generators. They exist while a world is loaded, as on stable.
    internal static class TldStableWorld
    {
        static roadGenScript roadGen;
        static GameObject holder;
        static StableShells.roadclass[] roads = new StableShells.roadclass[0];
        static float roadsBuilt = -10f;
        static mapSettingScript buildingMap;
        static mapSettingScript settingsMap;
        static List<StableShells.buildingscript> buildings;
        static float buildingsBuilt;
        static mapSettingScript holesMap;
        static GameObject holesHolder;
        static readonly Dictionary<poiGenScript.quadClass, StableShells.digholescript2> holeViews =
            new Dictionary<poiGenScript.quadClass, StableShells.digholescript2>();
        static List<StableShells.digholescript2> holes;
        static float holesBuilt;

        internal static List<StableShells.digholescript2> Holes()
        {
            var map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
            if (map == null || map.poiGens == null || !TldLoaderBridge.WorldIsReady)
                return new List<StableShells.digholescript2>();
            float now = Time.realtimeSinceStartup;
            if (holes != null && holesMap == map && now - holesBuilt < 0.25f) return holes;
            if (holesHolder == null)
            {
                holesHolder = new GameObject("Roadworthy stable terrain holes");
                holesHolder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holesHolder);
            }

            var found = new HashSet<poiGenScript.quadClass>();
            var result = new List<StableShells.digholescript2>();
            foreach (var generator in map.poiGens)
            {
                if (generator == null) continue;
                foreach (var chunk in generator.chunks.Values)
                    foreach (var poi in chunk.pois.Values)
                        foreach (var quad in poi.terrainHoleQuads)
                        {
                            if (quad == null || quad.corners == null || quad.corners.Length != 4 || !found.Add(quad)) continue;
                            StableShells.digholescript2 view;
                            if (!holeViews.TryGetValue(quad, out view))
                            {
                                view = holesHolder.AddComponent<StableShells.digholescript2>();
                                holeViews.Add(quad, view);
                            }
                            var corners = quad.corners;
                            view.V = (Vector3d[])corners.Clone();
                            view.pos = mainscript.Center(corners);
                            var ordered = new List<Vector3d>(corners);
                            ordered.Sort((left, right) =>
                            {
                                int x = left.x.CompareTo(right.x);
                                return x != 0 ? x : left.z.CompareTo(right.z);
                            });
                            view.bal1 = ordered[0]; view.bal2 = ordered[1];
                            view.jobb1 = ordered[2]; view.jobb2 = ordered[3];
                            result.Add(view);
                        }
            }
            var stale = new List<poiGenScript.quadClass>();
            foreach (var entry in holeViews)
                if (!found.Contains(entry.Key)) stale.Add(entry.Key);
            foreach (var quad in stale)
            {
                UnityEngine.Object.Destroy(holeViews[quad]);
                holeViews.Remove(quad);
            }
            holesMap = map;
            holesBuilt = now;
            return holes = result;
        }

        internal static StableShells.TerrainGenerationSettings Settings()
        {
            Tick(TldLoaderBridge.WorldIsReady);
            return StableShells.TerrainGenerationSettings.staticReference;
        }

        static void UpdateSettings(mapSettingScript map)
        {
            var settings = StableShells.TerrainGenerationSettings.staticReference;
            if (settings == null || map == null || settingsMap == map) return;
            var objects = new List<StableShells.ObjClass>();
            var seen = new HashSet<GameObject>();
            if (map.objGens != null)
                foreach (var generator in map.objGens)
                {
                    if (generator == null || generator.categories == null) continue;
                    foreach (var category in generator.categories)
                    {
                        if (category == null || category.prefabs == null) continue;
                        foreach (var entry in category.prefabs)
                            if (entry != null && entry.prefab != null && seen.Add(entry.prefab))
                                objects.Add(new StableShells.ObjClass { prefab = entry.prefab, chance = entry.chance });
                    }
                }
            settings.objGeneration.objTypes = objects.ToArray();
            if (map.poiGens != null)
                foreach (var generator in map.poiGens)
                {
                    if (generator == null) continue;
                    settings.roadBuildingGeneration.parent = generator.transform;
                    if (generator.genNearRoad) break;
                }
            // Beta terrain is generated by terrainGenScript tiles. Expose the live tile size
            // and placed terrain to stable mods that still read TerrainGenerator/Terrain.
            var terrains = UnityEngine.Object.FindObjectsOfType<terrainGenScript>();
            terrainGenScript mainTerrain = null;
            foreach (var candidate in terrains)
                if (candidate != null && !candidate.isDistantTerrain) { mainTerrain = candidate; break; }
            if (mainTerrain != null)
            {
                settings.Terrain.resolution = mainTerrain.resolution > 0 ? mainTerrain.resolution : 128;
                settings.Terrain.tileSize = mainTerrain.tileSize > 0 ? (int)mainTerrain.tileSize : 128;
                settings.Terrain.drawDistance = (int)mainTerrain.drawDistance;
                settings.Terrain.removeDist = mainTerrain.drawDistance;
                settings.Terrain.terrainNeeded = mainTerrain.terrains == null ? 0 : mainTerrain.terrains.Count;
                settings.started = mainTerrain.isActiveAndEnabled || map.started;
                settings.world = TldLoaderBridge.WorldRoot(mainscript.s);
            }
            var all = new List<terrainscript>(UnityEngine.Object.FindObjectsOfType<terrainscript>());
            if (all.Count == 0 && holder != null)
            {
                var standIn = holder.AddComponent<terrainscript>();
                if (mainTerrain != null)
                {
                    var p = mainTerrain.transform.position;
                    standIn.coord = new Vector2(p.x, p.z);
                }
                all.Add(standIn);
            }
            settings.terranGenerator.allterrains = all;
            var important = new List<StableShells.ImportantObj>();
            foreach (var poi in UnityEngine.Object.FindObjectsOfType<poiScript>())
            {
                if (poi == null) continue;
                important.Add(new StableShells.ImportantObj
                {
                    T = poi.transform,
                    pos = new Vector2(poi.transform.position.x, poi.transform.position.z),
                    distantPos = new Vector2(poi.transform.position.x, poi.transform.position.z),
                    distantPos2 = new Vector2(poi.transform.position.x, poi.transform.position.z)
                });
            }
            settings.importantObjs = important;
            if ((settings.importantObjs == null || settings.importantObjs.Count == 0) && mainTerrain != null)
                settings.importantObjs = new List<StableShells.ImportantObj>
                {
                    new StableShells.ImportantObj { T = mainTerrain.transform,
                        pos = new Vector2(mainTerrain.transform.position.x, mainTerrain.transform.position.z) }
                };
            settings.seed = map.seed;
            settingsMap = map;
        }

        internal static List<StableShells.buildingscript> Buildings()
        {
            var map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
            if (map == null || map.poiGens == null) return new List<StableShells.buildingscript>();
            float now = Time.realtimeSinceStartup;
            if (buildings != null && map == buildingMap && now - buildingsBuilt < 0.25f) return buildings;

            var loaded = new List<StableShells.buildingscript>();
            foreach (var generator in map.poiGens)
            {
                if (generator == null) continue;
                foreach (var chunk in generator.chunks.Values)
                {
                    foreach (var record in chunk.pois.Values)
                    {
                        var poi = record.pobj;
                        if (poi == null || !poi.gameObject.activeInHierarchy) continue;
                        loaded.Add(Building(poi, record, generator));
                    }
                }
            }
            buildingMap = map;
            buildings = loaded;
            buildingsBuilt = now;
            return buildings;
        }

        internal static StableShells.buildingscript Building(poiScript poi, poiGenScript.poiClass record, poiGenScript generator)
        {
            var building = poi.GetComponent<StableShells.buildingscript>();
            if (building == null) building = poi.gameObject.AddComponent<StableShells.buildingscript>();
            building.pos = record.pos;
            building.tipus = record.id;
            building.started = poi.started;
            building.itemsSpawned = record.spawnedItems;
            building.doors = poi.usables;
            building.tanks = poi.tanks;
            building.seats = poi.seats;
            building.startHouse = generator != null && generator.placeStartHouse && record.id == (int)generator.startHouseType &&
                poi.transform.Find("PlayerStart") != null;
            return building;
        }

        internal static roadGenScript RoadGen()
        {
            if (!TldLoaderBridge.WorldIsReady) return null;
            if (roadGen == null) roadGen = UnityEngine.Object.FindObjectOfType<roadGenScript>();
            return roadGen;
        }

        internal static void Tick(bool ready)
        {
            if (!ready)
            {
                if (holesHolder != null)
                {
                    UnityEngine.Object.Destroy(holesHolder);
                    holesHolder = null;
                    holesMap = null;
                    holes = null;
                    holeViews.Clear();
                }
                if (holder != null)
                {
                    UnityEngine.Object.Destroy(holder);
                    holder = null;
                    StableShells.TerrainGenerator.TG = null;
                    StableShells.TerrainGenerationSettings.staticReference = null;
                    StableShells.TerrainManager.staticReference = null;
                    roads = new StableShells.roadclass[0];
                    roadGen = null;
                    buildingMap = null;
                    settingsMap = null;
                    buildings = null;
                }
                return;
            }
            if (holder == null)
            {
                holder = new GameObject("Roadworthy stable world stand-ins");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
                StableShells.TerrainGenerator.TG = holder.AddComponent<StableShells.TerrainGenerator>();
                var settings = holder.AddComponent<StableShells.TerrainGenerationSettings>();
                StableShells.TerrainGenerationSettings.staticReference = settings;
                settings.objGeneration = holder.AddComponent<StableShells.ObjectGenerationScript>();
                settings.objGeneration.objTypes = new StableShells.ObjClass[0];
                settings.desertTowerGeneration = holder.AddComponent<StableShells.ObjectGenerationScript>();
                settings.desertTowerGeneration.objTypes = new StableShells.ObjClass[0];
                settings.mountainGeneration = holder.AddComponent<StableShells.ObjectGenerationScript>();
                settings.mountainGeneration.objTypes = new StableShells.ObjClass[0];
                settings.desertBuildingGeneration = holder.AddComponent<StableShells.ObjectGenerationScript>();
                settings.desertBuildingGeneration.objTypes = new StableShells.ObjClass[0];
                settings.roadBuildingGeneration = holder.AddComponent<StableShells.roadBuildingScript>();
                settings.terranGenerator = StableShells.TerrainGenerator.TG;
                StableShells.TerrainManager.staticReference = holder.AddComponent<StableShells.TerrainManager>();
                StableShells.TerrainGenerator.TG.roads = new List<roadGenScript>();
                StableShells.TerrainGenerator.TG.manager = StableShells.TerrainManager.staticReference;
                StableShells.TerrainManager.staticReference.generator = StableShells.TerrainGenerator.TG;
                StableShells.TerrainManager.staticReference.settings = StableShells.TerrainGenerationSettings.staticReference;
            }
            var list = StableShells.TerrainGenerator.TG.roads;
            var gen = RoadGen();
            if (gen != null && (list.Count != 1 || list[0] != gen)) { list.Clear(); list.Add(gen); }
            if (menuhandler.s != null) UpdateSettings(menuhandler.s.currentMainMap);
        }

        // The beta's roads as the stable roadclass list: one piece per road element, its first bone
        // the origin and the rest as offsets (global = pos + coords[i]), pieces chained end to start
        // from the one nearest the player so walking the array follows the road.
        internal static StableShells.roadclass[] Roads(roadGenScript gen)
        {
            if (Time.realtimeSinceStartup - roadsBuilt < 1f) return roads;
            roadsBuilt = Time.realtimeSinceStartup;
            if (gen == null) gen = RoadGen();
            if (gen == null || gen.chunks == null) return roads = new StableShells.roadclass[0];
            var segments = new List<List<Vector3d>>();
            foreach (var chunk in gen.chunks.Values)
            {
                if (chunk == null || chunk.roads == null) continue;
                foreach (var road in chunk.roads)
                {
                    if (road == null || road.roadElements == null) continue;
                    foreach (var element in road.roadElements)
                        if (element != null && element.bonePos != null && element.bonePos.Count >= 2) segments.Add(new List<Vector3d>(element.bonePos));
                }
            }
            Vector3d player = mainscript.s != null && mainscript.s.player != null ? mainscript.GlobalFromUnityPos(mainscript.s.player.transform.position) : new Vector3d(0, 0, 0);
            Measure(segments);
            var ordered = Chain(segments, player);
            var result = new StableShells.roadclass[ordered.Count];
            for (int i = 0; i < ordered.Count; i++) result[i] = Piece(ordered[i]);
            return roads = result;
        }

        // Averages over the current road pieces (stable kept these as generator settings).
        internal static float SegmentLength = 50f, BoneLength = 5f, Width = 8f;
        internal static int BoneCount = 10;

        static void Measure(List<List<Vector3d>> segments)
        {
            double segment = 0, bone = 0; int bones = 0, gaps = 0;
            foreach (var s in segments)
            {
                segment += Distance(s[0], s[s.Count - 1]);
                bones += s.Count;
                for (int i = 1; i < s.Count; i++) { bone += Distance(s[i - 1], s[i]); gaps++; }
            }
            if (segments.Count == 0) return;
            SegmentLength = (float)(segment / segments.Count);
            BoneCount = Math.Max(2, bones / segments.Count);
            if (gaps > 0) BoneLength = (float)(bone / gaps);
        }

        static StableShells.roadclass Piece(List<Vector3d> points)
        {
            var start = points[0];
            var piece = new StableShells.roadclass { posX = start.x, posY = start.y, posZ = start.z };
            piece.coords = new vector3[points.Count];
            piece.rots = new StableShells.roadbonerot[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                piece.coords[i] = new vector3 { x = (float)(points[i].x - start.x), y = (float)(points[i].y - start.y), z = (float)(points[i].z - start.z) };
                var next = points[Math.Min(i + 1, points.Count - 1)];
                var previous = points[Math.Max(i - 1, 0)];
                var direction = new Vector3((float)(next.x - previous.x), (float)(next.y - previous.y), (float)(next.z - previous.z));
                var euler = direction.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(direction).eulerAngles : Vector3.zero;
                piece.rots[i] = new StableShells.roadbonerot { x = euler.x, y = euler.y, z = euler.z };
            }
            return piece;
        }

        static double Distance(Vector3d a, Vector3d b) { double x = a.x - b.x, y = a.y - b.y, z = a.z - b.z; return Math.Sqrt(x * x + y * y + z * z); }

        // Orders segments into a path: start nearest the player, then repeatedly attach the segment
        // whose end touches the path's ends (reversing it when needed). Unconnected ones follow.
        static List<List<Vector3d>> Chain(List<List<Vector3d>> segments, Vector3d player)
        {
            var path = new List<List<Vector3d>>();
            if (segments.Count == 0) return path;
            var left = new List<List<Vector3d>>(segments);
            int nearest = 0; double best = double.MaxValue;
            for (int i = 0; i < left.Count; i++)
                foreach (var p in left[i]) { double d = Distance(p, player); if (d < best) { best = d; nearest = i; } }
            path.Add(left[nearest]); left.RemoveAt(nearest);
            const double touch = 3.0;
            bool grew = true;
            while (grew && left.Count > 0)
            {
                grew = false;
                var tail = path[path.Count - 1]; var end = tail[tail.Count - 1];
                var head = path[0]; var begin = head[0];
                for (int i = 0; i < left.Count; i++)
                {
                    var s = left[i];
                    if (Distance(s[0], end) < touch) { path.Add(s); }
                    else if (Distance(s[s.Count - 1], end) < touch) { s.Reverse(); path.Add(s); }
                    else if (Distance(s[s.Count - 1], begin) < touch) { path.Insert(0, s); }
                    else if (Distance(s[0], begin) < touch) { s.Reverse(); path.Insert(0, s); }
                    else continue;
                    left.RemoveAt(i); grew = true; break;
                }
            }
            path.AddRange(left);
            return path;
        }

        static StableShells.settingsscript_DrawDistanceSetting drawDistance;
        static float syncedDrawDistance;

        internal static void ResetDrawDistance() { drawDistance = null; }

        internal static void SyncDrawDistance()
        {
            var settings = settingsscript.s == null ? null : settingsscript.s.S;
            if (drawDistance == null || settings == null) return;
            float requested = drawDistance.drawDistance;
            if (requested != syncedDrawDistance && requested > 0f && !float.IsInfinity(requested))
                settings.FDrawDistance = requested;
            syncedDrawDistance = settings.FDrawDistance;
            drawDistance.drawDistance = syncedDrawDistance;
        }

        // Keep the stable settings object alive for mods that hold it or write its fields.
        internal static StableShells.settingsscript_DrawDistanceSetting DrawDistance()
        {
            if (drawDistance != null)
            {
                SyncDrawDistance();
                return drawDistance;
            }
            float draw = settingsscript.s != null && settingsscript.s.S != null ? settingsscript.s.S.FDrawDistance : 1000f;
            var camera = Camera.main;
            var gen = RoadGen();
            float road = gen != null ? gen.roadPlaceDist : draw;
            syncedDrawDistance = draw;
            drawDistance = new StableShells.settingsscript_DrawDistanceSetting
            {
                drawDistance = draw, Cam1FarPlane = camera != null ? camera.farClipPlane : draw, Cam2NearPlane = camera != null ? camera.farClipPlane : draw, Cam2FarPlane = draw,
                TerrainGenDist = draw, TerrainRemoveDist = draw * 1.1f, ObjGenDist = draw, ObjRemoveDist = draw * 1.1f, MountainGenDist = draw, MountainRemoveDist = draw * 1.1f,
                BuildingGenDist = draw, BuildingRemoveDist = draw * 1.1f, RoadGenDist = road, ItemSpawnDist = draw, ItemRemoveDist = draw * 1.1f,
            };
            return drawDistance;
        }
    }

    // Materials whose shader the beta build cannot draw (stripped from the build, or compiled
    // into a mod's asset bundle for the public game's Unity) render as a glowing magenta square.
    // They get the game's shader of the same name, else Standard, else a vanilla item's shader,
    // keeping their textures and colours.
    internal static class TldModMaterials
    {
        static readonly HashSet<int> checkedMaterials = new HashSet<int>();
        static float nextScan;
        static Shader fallback;
        static int fixedCount;

        internal static void Fix(bool now)
        {
            if (!now && Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + 3f;
            if (TldLoaderBridge.LoadedMods().Count == 0) return;
            Material[] materials;
            try { materials = Resources.FindObjectsOfTypeAll<Material>(); } catch (Exception) { return; }
            int before = fixedCount;
            foreach (var material in materials)
            {
                if (material == null || !checkedMaterials.Add(material.GetInstanceID())) continue;
                var shader = material.shader;
                // Only what draws magenta: no shader, or Unity's error shader. Vanilla materials keep theirs.
                if (shader != null && shader.name != "Hidden/InternalErrorShader") continue;
                var replacement = Replacement(shader == null ? null : shader.name);
                if (replacement == null || replacement == shader) continue;
                try { material.shader = replacement; fixedCount++; } catch (Exception) { }
            }
            if (fixedCount > before)
                Debug.Log("[TLD MP mods] gave " + (fixedCount - before) + " mod material(s) a shader this game can draw (" + (fallback == null ? "same-name shader" : fallback.name) + ").");
        }

        static Shader Replacement(string name)
        {
            if (!string.IsNullOrEmpty(name) && name != "Hidden/InternalErrorShader")
            {
                var same = Shader.Find(name);
                if (same != null && same.isSupported) return same;
            }
            if (fallback == null)
            {
                var standard = Shader.Find("Standard");
                if (standard != null && standard.isSupported) fallback = standard;
                var db = itemdatabase.s;
                if (fallback == null && db != null && db.items != null)
                    foreach (var item in db.items)
                    {
                        var renderer = item == null ? null : item.GetComponentInChildren<MeshRenderer>(true);
                        var shared = renderer == null ? null : renderer.sharedMaterial;
                        if (shared != null && shared.shader != null && shared.shader.isSupported) { fallback = shared.shader; break; }
                    }
            }
            return fallback;
        }
    }

    // The beta keeps one scene, so the item database outlives a world. Mods change it in DbLoad
    // (TLDLoader runs DbLoad on every world load): appended items, box spawn tables and building
    // spawn tables would pile up each time (a mod item ended up in every box). Before each DbLoad
    // the database goes back to how it was before the first one.
    internal static class TldModDatabase
    {
        static GameObject[] items;
        static readonly Dictionary<spawnInBoxScript, spawnInBoxScript.boxspawnclass[]> boxes = new Dictionary<spawnInBoxScript, spawnInBoxScript.boxspawnclass[]>();
        static readonly Dictionary<newRandomStuffSpawnScript, newRandomStuffSpawnScript.item[]> buildings = new Dictionary<newRandomStuffSpawnScript, newRandomStuffSpawnScript.item[]>();

        internal static void BeforeDbLoad()
        {
            var db = itemdatabase.s;
            if (db == null || db.items == null) return;
            if (items == null) { Take(db); return; }
            db.items = (GameObject[])items.Clone();
            foreach (var pair in boxes) if (pair.Key != null) pair.Key.spawn = pair.Value;
            foreach (var pair in buildings) if (pair.Key != null) pair.Key.items = pair.Value;
            TldModItems.DatabaseRestored();
        }

        static void Take(itemdatabase db)
        {
            items = (GameObject[])db.items.Clone();
            foreach (var item in items)
            {
                if (item == null) continue;
                foreach (var box in item.GetComponentsInChildren<spawnInBoxScript>(true))
                    if (box != null && !boxes.ContainsKey(box)) boxes[box] = box.spawn;
            }
            foreach (var building in TldLoaderBridge.BuildingPrefabs(db))
            {
                if (building == null) continue;
                foreach (var spawner in building.GetComponentsInChildren<newRandomStuffSpawnScript>(true))
                    if (spawner != null && !buildings.ContainsKey(spawner)) buildings[spawner] = spawner.items;
            }
        }
    }

    // A short, non-blocking note at the top of the screen.
    internal sealed class TldModNotice : MonoBehaviour
    {
        static TldModNotice current;
        string text; float until;
        internal static void Show(string message)
        {
            if (current == null)
            {
                var host = new GameObject("Roadworthy mod notice");
                DontDestroyOnLoad(host);
                current = host.AddComponent<TldModNotice>();
            }
            current.text = message; current.until = Time.realtimeSinceStartup + 15f;
        }
        void OnGUI()
        {
            if (text == null || Time.realtimeSinceStartup > until) return;
            var style = new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 16, alignment = TextAnchor.MiddleCenter };
            float width = Mathf.Min(760f, Screen.width - 40f);
            float height = style.CalcHeight(new GUIContent(text), width) + 16f;
            GUI.Box(new Rect((Screen.width - width) / 2f, 24f, width, height), text, style);
        }
    }

    // What a translation produced, copied out of the translator assembly's Report.
    internal sealed class TranslationReport
    {
        public byte[] Bytes;
        public int Rewrites;
        public bool LoadDllHooked;
        public List<string> StrippedPatches = new List<string>();
        public List<string> Missing = new List<string>();
        public List<string> Degraded = new List<string>();
    }

    // The Mono.Cecil translator lives in its own assembly (resource TldMp.TldLoaderTranslator.dll),
    // loaded only after EmbeddedCecil.Install(). Tld.Multiplayer.dll itself must not reference
    // Mono.Cecil: anything that scans its types before then (Assembly.GetTypes from the game,
    // Unity or another mod) would fail to resolve Cecil, and Mono remembers the failed class for
    // the rest of the session ("Invalid type Resolver for instance field ... resolver").
    internal sealed class TranslatorHandle
    {
        readonly object instance;
        readonly MethodInfo translateLoader, translateMod;

        internal TranslatorHandle(string gamePath, List<string> dirs, AssemblyName runtime)
        {
            var assembly = EmbeddedCecil.LoadTranslator();
            if (assembly == null) throw new InvalidOperationException("the TLDLoader translator is missing from Tld.Multiplayer.dll");
            var type = assembly.GetType("TldMp.TldLoaderTranslator", true);
            instance = Activator.CreateInstance(type, new object[] { gamePath, dirs, runtime.Name, runtime.Version });
            translateLoader = type.GetMethod("TranslateLoader");
            translateMod = type.GetMethod("TranslateMod");
        }

        internal TranslationReport TranslateLoader(byte[] source) { return Copy(Invoke(translateLoader, source)); }
        internal TranslationReport TranslateMod(byte[] source) { return Copy(Invoke(translateMod, source)); }

        object Invoke(MethodInfo method, byte[] source)
        {
            try { return method.Invoke(instance, new object[] { source }); }
            catch (TargetInvocationException e) { if (e.InnerException != null) throw e.InnerException; throw; }
        }

        static TranslationReport Copy(object report)
        {
            var type = report.GetType();
            var result = new TranslationReport
            {
                Bytes = (byte[])type.GetField("Bytes").GetValue(report),
                Rewrites = (int)type.GetField("Rewrites").GetValue(report),
                LoadDllHooked = (bool)type.GetField("LoadDllHooked").GetValue(report),
            };
            result.StrippedPatches.AddRange((IEnumerable<string>)type.GetField("StrippedPatches").GetValue(report));
            foreach (DictionaryEntry pair in (IDictionary)type.GetField("Missing").GetValue(report)) result.Missing.Add((string)pair.Key);
            var degraded = type.GetField("Degraded");
            if (degraded != null) result.Degraded.AddRange((IEnumerable<string>)degraded.GetValue(report));
            return result;
        }
    }

    // File handling around the translator. Its methods are only called after
    // EmbeddedCecil.Install().
    internal static class TldLoaderHost
    {
        const string Tag = "[TLD MP mods] ";
        const string BackupName = "TLDLoader.roadworthy-original.bin";
        const string StateName = "TLDLoader.roadworthy-state.txt";
        static TranslatorHandle translator;
        static string gameSha;

        internal static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        static string GamePath(string managed) { return Path.Combine(managed, "Assembly-CSharp.dll"); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static TranslatorHandle Translator(string managed)
        {
            if (translator != null) return translator;
            if (gameSha == null) gameSha = Sha(File.ReadAllBytes(GamePath(managed)));
            var runtime = typeof(TldLoaderBridge).Assembly.GetName();
            return translator = new TranslatorHandle(GamePath(managed), new List<string> { managed, TldLoaderBridge.ModsDirectory }, runtime);
        }

        static Dictionary<string, string> ReadState(string path)
        {
            var state = new Dictionary<string, string>();
            if (!File.Exists(path)) return state;
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) state[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return state;
        }

        static string Value(Dictionary<string, string> state, string key)
        {
            string value; return state.TryGetValue(key, out value) ? value : null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool EnsureLoader(string managed)
        {
            string dll = Path.Combine(managed, "TLDLoader.dll");
            string backup = Path.Combine(managed, BackupName);
            string statePath = Path.Combine(managed, StateName);
            byte[] current = File.ReadAllBytes(dll);
            string currentSha = Sha(current);
            if (gameSha == null) gameSha = Sha(File.ReadAllBytes(GamePath(managed)));
            var state = ReadState(statePath);
            byte[] backupBytes = File.Exists(backup) ? File.ReadAllBytes(backup) : null;
            string backupSha = backupBytes == null ? null : Sha(backupBytes);
            var plan = TldLoaderCompatPolicy.PlanLoader(currentSha, Value(state, "translated"), Value(state, "key"), backupSha, gameSha);
            if (plan == TldLoaderCompatPolicy.LoaderPlan.UpToDate)
            {
                Debug.Log(Tag + "TLDLoader.dll is already translated for this game build.");
                return true;
            }
            byte[] source;
            if (plan == TldLoaderCompatPolicy.LoaderPlan.FromBackup) source = backupBytes;
            else
            {
                // A new original: keep an exact copy before touching it.
                source = current; backupSha = currentSha;
                File.WriteAllBytes(backup + ".tmp", current);
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(backup + ".tmp", backup);
            }
            var report = Translator(managed).TranslateLoader(source);
            if (!report.LoadDllHooked) throw new InvalidDataException("TLDLoader's LoadDLL hook was not placed.");
            string temp = dll + ".roadworthy-tmp";
            File.WriteAllBytes(temp, report.Bytes);
            File.Copy(temp, dll, true);
            File.Delete(temp);
            File.WriteAllLines(statePath, new[]
            {
                "source=" + backupSha, "translated=" + Sha(report.Bytes), "key=" + TldLoaderCompatPolicy.CacheKey(backupSha, gameSha),
                "note=TLDLoader.dll was translated for the beta by Roadworthy. " + BackupName + " is the exact original.",
            });
            Debug.Log(Tag + "translated TLDLoader.dll for the beta (" + report.Rewrites + " references renamed; original kept as " + BackupName + ").");
            if (report.StrippedPatches.Count > 0) Debug.Log(Tag + "TLDLoader patches left out (their game code is not in the beta): " + string.Join(", ", report.StrippedPatches.ToArray()));
            if (report.Missing.Count > 0)
                Debug.Log(Tag + "TLDLoader still uses " + report.Missing.Count + " game members the beta lacks (its custom-item framework); mods that register items with it will fail: " + Summarize(report, 12));
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void RestoreOriginal(string managed)
        {
            string dll = Path.Combine(managed, "TLDLoader.dll");
            string backup = Path.Combine(managed, BackupName);
            string statePath = Path.Combine(managed, StateName);
            if (!File.Exists(dll) || !File.Exists(statePath)) return;
            var state = ReadState(statePath);
            if (!TldLoaderCompatPolicy.ShouldRestore(Sha(File.ReadAllBytes(dll)), Value(state, "translated"), File.Exists(backup))) return;
            File.Copy(backup, dll, true);
            File.Delete(statePath);
            Debug.Log(Tag + "TLDLoader mods are off: the original TLDLoader.dll is back in place.");
        }

        static string Summarize(TranslationReport report, int max)
        {
            var parts = new List<string>();
            foreach (string name in report.Missing)
            {
                if (parts.Count == max) { parts.Add("+" + (report.Missing.Count - max) + " more"); break; }
                parts.Add(name);
            }
            return string.Join(", ", parts.ToArray());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string TranslateMod(string file)
        {
            string managed = TldLoaderBridge.ManagedDirectory;
            string cache = TldLoaderBridge.CacheDirectory;
            Directory.CreateDirectory(cache);
            // Libraries next to the mod are translated too, so references between
            // mods resolve from the cache folder.
            string folder = Path.GetDirectoryName(file);
            foreach (string sibling in Directory.GetFiles(folder, "*.dll"))
                if (!string.Equals(sibling, file, StringComparison.OrdinalIgnoreCase))
                    try { Cached(managed, cache, sibling, false); }
                    catch (Exception e) { Debug.LogWarning(Tag + Path.GetFileName(sibling) + ": could not be translated: " + e.Message); }
            return Cached(managed, cache, file, true);
        }

        static string Cached(string managed, string cache, string file, bool announce)
        {
            byte[] source = File.ReadAllBytes(file);
            string name = Path.GetFileName(file);
            string target = Path.Combine(cache, name);
            string keyFile = target + ".key";
            if (gameSha == null) gameSha = Sha(File.ReadAllBytes(GamePath(managed)));
            string key = TldLoaderCompatPolicy.CacheKey(Sha(source), gameSha);
            if (File.Exists(target) && File.Exists(keyFile))
            {
                string[] lines = File.ReadAllLines(keyFile);
                if (lines.Length >= 2 && lines[0] == key)
                {
                    int missing; if (int.TryParse(lines[1], out missing)) lock (TldLoaderBridge.MissingByFile) TldLoaderBridge.MissingByFile[name] = missing;
                    return target;
                }
            }
            var report = Translator(managed).TranslateMod(source);
            File.WriteAllBytes(target, report.Bytes);
            File.WriteAllLines(keyFile, new[] { key, report.Missing.Count.ToString() });
            lock (TldLoaderBridge.MissingByFile) TldLoaderBridge.MissingByFile[name] = report.Missing.Count;
            string summary = name + ": translated for the beta (" + report.Rewrites + " references renamed" +
                (report.StrippedPatches.Count > 0 ? "; Harmony patches left out: " + string.Join(", ", report.StrippedPatches.ToArray()) : "") + ").";
            if (report.Missing.Count > 0)
                Debug.LogWarning(Tag + summary + " It uses " + report.Missing.Count + " game member(s) this game version does not have, so parts of it may fail (TLDLoader disables it after repeated errors): " + Summarize(report, 8));
            else if (announce) Debug.Log(Tag + summary);
            if (report.Degraded.Count > 0)
                Debug.Log(Tag + name + ": " + report.Degraded.Count + " game member(s) it uses are gone on this game version and are stood in for (fields keep the mod's values, calls do nothing): " +
                    string.Join(", ", report.Degraded.GetRange(0, Math.Min(8, report.Degraded.Count)).ToArray()) + (report.Degraded.Count > 8 ? ", +" + (report.Degraded.Count - 8) + " more" : "") + ".");
            return target;
        }
    }

    // Mono.Cecil travels inside Tld.Multiplayer.dll (resource TldMp.Mono.Cecil.dll),
    // so players install nothing extra.
    internal static class EmbeddedCecil
    {
        static Assembly cecil;
        static bool installed;
        internal static bool Install()
        {
            if (installed) return cecil != null || Loaded() != null;
            installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            try { return Load() != null; }
            catch (Exception e) { Debug.LogWarning("[TLD MP mods] embedded Mono.Cecil failed to load: " + e.Message); return false; }
        }
        static Assembly Loaded()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if (assembly.GetName().Name == "Mono.Cecil") return assembly;
            return null;
        }
        static Assembly Load()
        {
            if (cecil != null) return cecil;
            var existing = Loaded();
            if (existing != null) return cecil = existing;
            using (var stream = typeof(EmbeddedCecil).Assembly.GetManifestResourceStream("TldMp.Mono.Cecil.dll"))
            {
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                int read = 0; while (read < bytes.Length) { int n = stream.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                return cecil = Assembly.Load(bytes);
            }
        }
        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name == "Mono.Cecil") { try { return Load(); } catch (Exception) { return null; } }
            if (name == TranslatorName) return translator;
            return null;
        }

        internal const string TranslatorName = "TldMp.TldLoaderTranslator";
        static Assembly translator;

        // The Cecil translator assembly (built from TldLoaderTranslator.cs). Call after Install().
        internal static Assembly LoadTranslator()
        {
            if (translator != null) return translator;
            using (var stream = typeof(EmbeddedCecil).Assembly.GetManifestResourceStream(TranslatorName + ".dll"))
            {
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                int read = 0; while (read < bytes.Length) { int n = stream.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                return translator = Assembly.Load(bytes);
            }
        }
    }
}

namespace TldMp.StableShells
{
    public class NAudioPlayer
    {
        public static MemoryStream AudioMemStream(object waveStream)
        {
            var stream = waveStream as Stream;
            var format = stream == null ? null : stream.GetType().GetProperty("WaveFormat");
            if (format == null || format.PropertyType.FullName != "NAudio.Wave.WaveFormat")
                throw new ArgumentException("Expected an NAudio WaveStream.", "waveStream");
            var writerType = format.PropertyType.Assembly.GetType("NAudio.Wave.WaveFileWriter", true);
            using (var output = new MemoryStream())
            {
                using (var writer = (Stream)Activator.CreateInstance(writerType, output, format.GetValue(stream, null)))
                {
                    stream.Position = 0;
                    stream.CopyTo(writer);
                }
                return new MemoryStream(output.ToArray());
            }
        }

        public static AudioClip FromMp3Data(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            var codec = Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "NAudio");
            if (codec == null)
            {
                try { codec = Assembly.Load("NAudio"); }
                catch (FileNotFoundException e) { throw new NotSupportedException("This mod needs NAudio for MP3 decoding.", e); }
            }
            var readerType = codec.GetType("NAudio.Wave.Mp3FileReader", true);
            var conversion = codec.GetType("NAudio.Wave.WaveFormatConversionStream", true);
            var convert = conversion.GetMethod("CreatePcmStream", new[] { codec.GetType("NAudio.Wave.WaveStream", true) });
            if (convert == null) throw new NotSupportedException("This NAudio version has no PCM conversion.");
            using (var input = new MemoryStream(data))
            using (var reader = (Stream)Activator.CreateInstance(readerType, input))
            {
                var pcm = (Stream)convert.Invoke(null, new object[] { reader });
                try
                {
                    using (var encoded = AudioMemStream(pcm))
                    {
                        var decoded = new WAV(encoded.ToArray());
                        var clip = AudioClip.Create("Mod audio", decoded.SampleCount, decoded.ChannelCount, decoded.Frequency, false);
                        clip.SetData(decoded.StereoChannel ?? decoded.LeftChannel, 0);
                        return clip;
                    }
                }
                finally { if (!ReferenceEquals(pcm, reader)) pcm.Dispose(); }
            }
        }
    }

    // The stable WAV API exposes deinterleaved and interleaved 16-bit PCM.
    public class WAV
    {
        public float[] LeftChannel { get; private set; }
        public float[] RightChannel { get; private set; }
        public float[] StereoChannel { get; private set; }
        public int ChannelCount { get; private set; }
        public int SampleCount { get; private set; }
        public int Frequency { get; private set; }

        public WAV() { }

        public WAV(byte[] wav)
        {
            if (wav == null) throw new ArgumentNullException("wav");
            if (wav.Length < 12 || !TagAt(wav, 0, "RIFF") || !TagAt(wav, 8, "WAVE"))
                throw new InvalidDataException("Invalid RIFF WAVE header.");
            int riffSize = bytesToInt(wav, 4);
            if (riffSize < 4 || riffSize > wav.Length - 8)
                throw new InvalidDataException("Truncated WAVE file.");
            int end = riffSize + 8;
            int format = -1, data = -1, dataSize = 0;
            for (int chunk = 12; chunk <= end - 8;)
            {
                int size = bytesToInt(wav, chunk + 4);
                int start = chunk + 8;
                if (size < 0 || size > end - start) throw new InvalidDataException("Truncated WAVE chunk.");
                if (TagAt(wav, chunk, "fmt ") && size >= 16 && format < 0) format = start;
                if (TagAt(wav, chunk, "data") && data < 0) { data = start; dataSize = size; }
                chunk = start + size;
                if ((size & 1) != 0 && chunk < end) chunk++;
            }
            if (format < 0 || data < 0) throw new InvalidDataException("WAVE format or sample data is missing.");
            ChannelCount = UInt16At(wav, format + 2);
            Frequency = bytesToInt(wav, format + 4);
            int stride = UInt16At(wav, format + 12);
            if (UInt16At(wav, format) != 1 || UInt16At(wav, format + 14) != 16 ||
                (ChannelCount != 1 && ChannelCount != 2) || Frequency <= 0 || stride != ChannelCount * 2 || dataSize % stride != 0)
                throw new InvalidDataException("Expected mono or stereo 16-bit PCM WAVE samples.");
            SampleCount = dataSize / stride;
            LeftChannel = new float[SampleCount];
            if (ChannelCount == 2)
            {
                RightChannel = new float[SampleCount];
                StereoChannel = new float[SampleCount * 2];
            }
            for (int frame = 0; frame < SampleCount; frame++)
            {
                int sample = data + frame * stride;
                LeftChannel[frame] = bytesToFloat(wav[sample], wav[sample + 1]);
                if (RightChannel == null) continue;
                RightChannel[frame] = bytesToFloat(wav[sample + 2], wav[sample + 3]);
                StereoChannel[frame * 2] = LeftChannel[frame];
                StereoChannel[frame * 2 + 1] = RightChannel[frame];
            }
        }

        static bool TagAt(byte[] bytes, int offset, string tag)
        {
            for (int i = 0; i < tag.Length; i++) if (bytes[offset + i] != tag[i]) return false;
            return true;
        }

        static int UInt16At(byte[] bytes, int offset) { return bytes[offset] | (bytes[offset + 1] << 8); }
        public static int bytesToInt(byte[] bytes, int offset) { return UInt16At(bytes, offset) | (UInt16At(bytes, offset + 2) << 16); }
        public static float bytesToFloat(byte firstByte, byte secondByte) { return unchecked((short)(firstByte | (secondByte << 8))) / 32768f; }
        public override string ToString() { return ChannelCount + " channel(s), " + SampleCount + " frames, " + Frequency + " Hz"; }
    }

    public partial class save_prefab
    {
        public save_prefab(int categoryID, int prefabID, double px, double py, double pz, float rx, float ry, float rz)
        {
            this.categoryID = categoryID;
            this.prefabID = prefabID;
            transform = new posRotd(new Vector3d(px, py, pz), new Vector3(rx, ry, rz));
        }
    }
}
