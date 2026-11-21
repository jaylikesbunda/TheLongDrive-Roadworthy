using System;
using System.Collections.Generic;
using System.Text;

namespace TldMp
{
    // Pure decisions for running TLDLoader 3.x and its mods on the beta game
    // (no Unity, no Cecil; unit-tested). TldLoaderTranslator applies them with
    // Mono.Cecil and TldLoaderBridge drives the loader from the game's state.
    public static class TldLoaderCompatPolicy
    {
        // Bump when a rule changes so cached translations are rebuilt.
        public const int TranslatorVersion = 16;
        public const string EnvironmentVariable = "TLD_MP_TLDLOADER";
        public const string BridgeNamespace = "TldMp";
        public const string BridgeType = "TldLoaderBridge";
        public const string GameAssembly = "Assembly-CSharp";
        // Steam lobby metadata values are capped at 8 KB; stay well under it.
        public const int LobbyListLimit = 4000;

        // TLD_MP_TLDLOADER=0 always wins; otherwise the settings toggle decides.
        public static bool Enabled(string environment, bool option)
        {
            if (environment != null && environment.Trim() == "0") return false;
            return option;
        }

        public enum RewriteKind { None, RenameField, CallHelper }

        public struct FieldRewrite
        {
            public RewriteKind Kind;
            // RenameField: the field's new name (same declaring type, or the renamed type).
            // CallHelper: a static TldLoaderBridge method that takes the instance (for an
            // instance field) and returns a value of the field's type.
            public string Name;
            // CallHelper: the static method that stores a value (null: stores stay as they are).
            public string SetName;
            public bool Static;
        }

        // Types the public game had that the beta renamed. Applied to every type reference.
        // A dotted result is a core-library type: removed save types the mods only use as
        // opaque dictionary values become System.Object.
        public static string RenamedType(string fullName)
        {
            switch (fullName)
            {
                case "mainmenuscript": return "menuhandler";
                case "save_prefab": return ShellPrefix + "save_prefab";
                // The beta folded parts into attachablescript (same tipus/maintipus).
                case "partscript": return "attachablescript";
                // Stable's coordinate structs have exact beta counterparts (same fields and types).
                case "doublecoord": return "UnityEngine.Vector3d";
                case "doublecoord2": return "UnityEngine.Vector2d";
                case "coord": return "vector3";
                case "posrot": return "posRotd";
            }
            // Every other stable type the beta deleted has a stand-in in Roadworthy's own assembly.
            if (Array.IndexOf(TldStableShellNames.All, fullName) >= 0) return ShellPrefix + fullName.Replace('/', '_');
            return null;
        }

        // A rename into Roadworthy's runtime assembly (stand-ins for deleted stable types).
        public const string ShellPrefix = "[runtime]TldMp.StableShells.";
        public static bool RenamedToRuntime(string renamed) { return renamed != null && renamed.StartsWith("[runtime]", StringComparison.Ordinal); }
        public static string WithoutAssembly(string renamed) { return RenamedToRuntime(renamed) ? renamed.Substring("[runtime]".Length) : renamed; }

        // A field or parameter whose type only changed signedness keeps working when the
        // reference is retyped: both live on the evaluation stack as the same IL type.
        public static bool SameStackType(string a, string b)
        {
            if (a == b) return true;
            return Pair(a, b, "System.Int32", "System.UInt32") || Pair(a, b, "System.Int64", "System.UInt64") ||
                Pair(a, b, "System.Int16", "System.UInt16") || Pair(a, b, "System.Byte", "System.SByte");
        }
        static bool Pair(string a, string b, string x, string y) { return (a == x && b == y) || (a == y && b == x); }

        // Fields of the public game keyed by their declaring type there.
        public static FieldRewrite ForField(string declaringType, string name)
        {
            switch (declaringType + "::" + name)
            {
                // Null on the main menu, as the public game's per-scene singleton was.
                case "mainscript::M": return Helper("MainScript", true);
                case "mainscript::DFMS": return Helper("DataFromMenu", false);
                case "mainmenuscript::Settings":
                case "menuhandler::Settings": return Helper("MenuSettings", false);
                // "the world is loaded" (the beta's own flags differ in meaning).
                case "mainscript::loaded": return Helper("WorldLoaded", false);
                // The public game's floating-origin root: global = unity - world.position.
                case "mainscript::world": return Helper("WorldRoot", false);
                // The public game set load when the game scene started and cleared it once loading
                // finished: "the world is still loading", not "loaded".
                case "mainscript::load": return Helper("WorldLoading", false);
                // Fields that moved to other singletons on the beta (same name and type there).
                case "mainscript::multi": return Helper("MainMulti", false);
                // The public game's floating-origin world object: global = unity - mainWorld.coord.
                case "mainscript::mainWorld": return Helper("MainWorld", false);
                case "mainscript::mainRoadWorld": return Helper("MainWorld", false);
                // The stable world generator's data, filled from the beta's (TldStableWorld).
                case "savedatascript::mainRoadScript": return Helper("MainRoadScript", false);
                case "roadGenScript::roads": return Helper("StableRoads", false);
                case "roadGenScript::world2": return Helper("RoadWorld2", false);
                // Road dimensions mods do arithmetic with, measured from the beta's road points.
                case "roadGenScript::roadSegmentLength": return Helper("RoadSegmentLength", false);
                case "roadGenScript::roadBoneLength": return Helper("RoadBoneLength", false);
                case "roadGenScript::roadBoneNum": return Helper("RoadBoneNum", false);
                case "roadGenScript::roadAboveTerrain": return Rename("offsetty", false);
                case "roadGenScript::width": return Helper("RoadWidth", false);
                case "roadGenScript::prefab": return Helper("RoadPrefab", false);
                case "mainmenuscript::DFMS":
                case "menuhandler::DFMS": return Helper("MenuDataFromMenu", false, "SetMenuDataFromMenu");
                case "mainscript::DDS": return Helper("MainDrawDistance", false);
                case "mainscript::terrainGenerationSettings": return Helper("MainTerrainSettings", false);
                case "mainscript::holes": return Helper("MainTerrainHoles", false);
                case "settingsscript/setting::BFarTerrain": return Helper("FarTerrain", false, "SetFarTerrain");
                case "terrainHeightAlignToBuildingScript::helpPosCoords": return Helper("TerrainAlignMarkers", false, "SetTerrainAlignMarkers");
                case "terrainHeightAlignToBuildingScript::helpPosList": return Helper("TerrainAlignPoints", false, "SetTerrainAlignPoints");
                case "terrainHeightAlignToBuildingScript::range2": return Helper("TerrainAlignRanges", false, "SetTerrainAlignRanges");
                case "terrainHeightAlignToBuildingScript::started": return Helper("TerrainAlignStarted", false, "SetTerrainAlignStarted");
                case "mainscript::GSettings": return Helper("MainGameModeSettings", false);
                case "gamemodesettingsscript/gamemodesettings::homesickMode": return Helper("GameModeHomesick", false, "SetGameModeHomesick");
                case "mainscript::SettingObj": return Helper("MainSettings", false);
                // Physics locks: the beta keeps the lock object on the item.
                case "pickupable::isPhyslocked": return Helper("PickupIsPhyslocked", false);
                case "pickupable::physLockParentID": return Helper("PickupPhysLockParent", false);
                case "mainscript::skystuffs": return Helper("MainSkyStuffs", false);
                case "pickupable::mpPickedup": return Helper("PickupMpPickedUp", false, "SetPickupMpPickedUp");
                case "savedatascript::properties": return Helper("SaveProperties", false);
                case "savedatascript::buildings": return Helper("SaveBuildings", false);
                case "fpscontroller::M": return Helper("PlayerMain", false);
                // Day/night and weather: a singleton of its own on the beta.
                case "mainscript::napszak": return Helper("DayCycle", false);
                // Item mods look up saved items by id. The beta keys them by uint and keeps
                // unspawned items in savedata.itemData; these return read-only snapshots.
                case "savedatascript::toSaveStuff": return Helper("SaveItems", false);
                case "savedata::nearStuff": return Helper("LegacyNear", false, "SetLegacyNear");
                case "savedata::farStuff": return Helper("LegacyFar", false, "SetLegacyFar");
                // "Never save this object": its tosaveitemscript is removed instead.
                case "tosaveitemscript::removeFromMemory": return Helper("RemoveFromMemory", false, "SetRemoveFromMemory");
                case "pickupable::attach": return Helper("PickupAttach", false, "SetPickupAttach");
                case "pickupable::inIninventory": return Helper("PickupInventorySlot", false);
                case "pickupable::slots": return Helper("PickupSlots", false, "SetPickupSlots");
                case "pickupable::dontMountTillPickup": return Helper("PickupMountGuard", false, "SetPickupMountGuard");
                case "settingsscript::TAutoShift": return Helper("AutoShiftToggle", false);
                case "mainmenuscript::TAutoShift": return Helper("AutoShiftToggle", false);
                case "radioscript::v": return Helper("RadioVolume", false);
                case "pickupable::cols": return Helper("PickupCols", false, "SetPickupCols");
                case "pickupable::cols2": return Helper("PickupCols2", false, "SetPickupCols2");
                case "attachablescript::C": return Helper("AttachColliders", false, "SetAttachColliders");
                case "attachablescript::targetTosave": return Helper("AttachTarget", false, "SetAttachTarget");
                // Stable's attachment save fields (AMT saves and restores mounts with them).
                case "attachablescript::hasColliderOrTarget": return Helper("AttachHasTarget", false, "SetAttachHasTarget");
                case "attachablescript::colliderOrTarget": return Helper("AttachIsCollider", false, "SetAttachIsCollider");
                case "attachablescript::colliderOrTargetId": return Helper("AttachColliderId", false, "SetAttachColliderId");
                case "attachablescript::pointLocalPos": return Helper("AttachPointLocalPos", false, "SetAttachPointLocalPos");
                case "attachablescript::defParent": return Helper("AttachOriginalParent", false, "SetAttachOriginalParent");
                case "attachablescript::ConnectCars": return Helper("AttachConnectCars", false, "SetAttachConnectCars");
                case "attachablescript::originalTarget": return Helper("AttachOriginalTarget", false, "SetAttachOriginalTarget");
                case "attachablescript::reatach": return Helper("AttachReattaching", false, "SetAttachReattaching");
                case "attachablescript::reatachPoint": return Helper("AttachReattachPoint", false, "SetAttachReattachPoint");
                case "attachablescript::snapYtoNormal": return Helper("AttachSnapUp", false, "SetAttachSnapUp");
                case "attachablescript::attachAtStartTarget": return Helper("AttachStartupTarget", false, "SetAttachStartupTarget");
                case "attachablescript::target": return Helper("AttachCurrentTarget", false, "SetAttachCurrentTarget");
                // Fuel caps: the beta moves caps through a usablescript (TldStableCap brings back stable's).
                case "tankcapscript::TCap": return Helper("CapMoving", false, "SetCapMoving");
                case "tankcapscript::TOpen": return Helper("CapOpen", false, "SetCapOpen");
                case "tankcapscript::TClosed": return Helper("CapClosed", false, "SetCapClosed");
                case "tankcapscript::GOpen": return Helper("CapOpenObjects", false, "SetCapOpenObjects");
                case "tankcapscript::GClosed": return Helper("CapClosedObjects", false, "SetCapClosedObjects");
                case "tankcapscript::openSound": return Helper("CapOpenSound", false, "SetCapOpenSound");
                case "tankcapscript::closeSound": return Helper("CapCloseSound", false, "SetCapCloseSound");
                case "tankcapscript::openVolume": return Helper("CapOpenVolume", false, "SetCapOpenVolume");
                case "tankcapscript::closeVolume": return Helper("CapCloseVolume", false, "SetCapCloseVolume");
                case "tankcapscript::tekerSound": return Helper("CapTuneSound", false, "SetCapTuneSound");
                case "tankcapscript::canToggle": return Helper("CapCanToggle", false, "SetCapCanToggle");
                case "tankcapscript::closedRot": return Helper("CapClosedRotation", false, "SetCapClosedRotation");
                case "tankcapscript::openRot": return Helper("CapOpenRotation", false, "SetCapOpenRotation");
                case "carscript::driverSeat": return Helper("CarDriverSeat", false, "SetCarDriverSeat");
                case "carscript::ILight": return Helper("CarInteriorLights", false, "SetCarInteriorLights");
                case "carscript::ILamp": return Helper("CarInteriorLamps", false, "SetCarInteriorLamps");
                case "carscript::ILampOn": return Helper("CarInteriorLampOn", false, "SetCarInteriorLampOn");
                case "carscript::ILampOff": return Helper("CarInteriorLampOff", false, "SetCarInteriorLampOff");
                case "carscript::ISwitches": return Helper("CarInteriorSwitches", false, "SetCarInteriorSwitches");
                case "tosaveitemscript::interiorLights": return Helper("SaveInteriorLights", false, "SetSaveInteriorLights");
                case "carscript::KmhObj": return Rename("debugCanvas", false);
                // Stable's "point" attach options are the beta's "slot" ones.
                case "attachablescript::onlyToPoint": return new FieldRewrite { Kind = RewriteKind.RenameField, Name = "onlyToSlot" };
                case "attachablescript::ignorePoints": return new FieldRewrite { Kind = RewriteKind.RenameField, Name = "ignoreSlots" };
                case "attachablescript::lockRotation": return Helper("AttachLockedRotation", false, "SetAttachLockedRotation");
                case "partslotscript::enableSpawn": return Helper("SlotEnableSpawn", false, "SetSlotEnableSpawn");
                case "partslotscript::spawn_def": return Helper("SlotSpawnDef", false, "SetSlotSpawnDef");
                case "partslotscript::spawnChance": return Helper("SlotSpawnChance", false, "SetSlotSpawnChance");
                case "partslotscript::randomChance": return Helper("SlotRandomChance", false, "SetSlotRandomChance");
                case "mainscript::godmode": return Helper("MainGodMode", false, "SetMainGodMode");
                case "mainscript::Showkmh": return Helper("MainShowKmh", false, "SetMainShowKmh");
                case "fpscontroller::Tb": return Helper("PlayerBody", false);
                case "itemPlaceRemoveScript::save": return Helper("PlaceSave", false);
                case "itemPlaceRemoveScript::settings": return Helper("PlaceSettings", false);
                case "itemPlaceRemoveScript::db": return Helper("PlaceAttemptCount", false, "SetPlaceAttemptCount");
                case "gamemodesettingsscript::defS": return Helper("GameModeDefaults", false);
                // Stable kept the item's category on the item; the beta only keeps category tables.
                case "tosaveitemscript::category": return Helper("ItemCategory", false, "SetItemCategory");
                case "roadGenScript::roadList": return Helper("RoadList", false);
                case "roadGenScript::length": return Helper("RoadLength", false, "SetRoadLength");
                case "roadGenScript::defPointRange": return Helper("RoadBoneLength", false);
                case "roadGenScript::scale": return Helper("RoadScale", false, "SetRoadScale");
                case "roadGenScript::doublePos": return Helper("RoadDoublePos", false, "SetRoadDoublePos");
                // Lights on (non-zero) or off; the beta keeps it per headlight.
                case "carscript::lampak": return Helper("CarLights", false, "SetCarLights");
                // No gib object at all on the beta when set.
                case "breakablescript::noGib": return Helper("BreakableNoGib", false, "SetBreakableNoGib");
                // The beta's building prefabs (pois).
                case "itemdatabase::buildings": return Helper("BuildingPrefabs", false);
                case "mainscript::seed": return Helper("WorldSeed", false);
                // Parts (partscript is renamed to attachablescript after these lookups).
                case "partscript::p": return Helper("PartPickup", false, "SetPartPickup");
                case "partscript::tosaveitem": return Rename("tosave", false);
                case "pickupable::part": return Helper("PickupPart", false, "SetPickupPart");
                case "tosaveitemscript::part": return Helper("SavePart", false, "SetSavePart");
                case "tosaveitemscript::attachTargets": return Helper("SaveAttachTargets", false, "SetSaveAttachTargets");
                case "partslotscript::part": return Helper("SlotPart", false, "SetSlotPart");
                // One light/renderer per state on the public game, lists of groups on the beta.
                case "headlightscript/lightState::L": return Helper("StateLight", false);
                case "headlightscript/lightState::R": return Helper("StateRenderer", false);
                case "headlightscript/lightState::Ls": return Helper("StateLights", false);
                case "headlightscript/lightState::Rs": return Helper("StateRenderers", false);
                case "headlightscript/lightState::M": return Helper("StateMaterial", false, "SetStateMaterial");
                case "newRandomStuffSpawnScript/item::fr1": return Helper("SpawnAmmoMin", false, "SetSpawnAmmoMin");
                case "newRandomStuffSpawnScript/item::fr2": return Helper("SpawnAmmoMax", false, "SetSpawnAmmoMax");
                case "itemdatabase::d": return Rename("s", true);
                case "savedatascript::d": return Rename("s", true);
                case "mainmenuscript::mainmenu": return Rename("s", true);
                case "mainmenuscript::Canvas": return Rename("canv2", false);
                case "mainmenuscript::saveScreen": return Rename("saveScreen", false);
                case "mainmenuscript::SaveScreenObj":
                case "menuhandler::SaveScreenObj": return Rename("SaveLoadObject", false);
                case "mainmenuscript::SettingsScreenObj":
                case "menuhandler::SettingsScreenObj": return Rename("SettingsObject", false);
            }
            return new FieldRewrite { Kind = RewriteKind.None };
        }

        // The layer for a slot built at run time: its own when both searches see it, else the
        // lowest layer both see, else the lowest the player's attach ray sees (mounting by hand).
        // A mask of 0 is unknown and ignored.
        public static int SlotLayer(int current, int scanMask, int attachMask)
        {
            int both = scanMask != 0 && attachMask != 0 ? scanMask & attachMask : (scanMask | attachMask);
            if (both == 0) both = attachMask;
            if (both == 0 || (both & (1 << current)) != 0) return current;
            for (int layer = 0; layer < 32; layer++) if ((both & (1 << layer)) != 0) return layer;
            return current;
        }

        // A type rename's target that lives in the .NET core library (everything else stays in
        // the game's own assembly, where the beta defines it).
        public static bool RenamedToCoreLibrary(string renamed)
        {
            return renamed != null && renamed.StartsWith("System.", StringComparison.Ordinal);
        }

        // How a call's arguments line up with a beta overload that has more parameters: for each
        // beta parameter the index of the call's argument it takes, or -1 for "pass the default".
        // Extra parameters at the end are the usual case; otherwise the call's arguments must
        // appear in the beta's list in order, and in exactly one way. Null: no fit.
        public static int[] AlignParameters(string[] stable, string[] beta)
        {
            if (beta.Length < stable.Length) return null;
            var map = new int[beta.Length];
            for (int k = 0; k < map.Length; k++) map[k] = -1;
            bool prefix = true;
            for (int i = 0; i < stable.Length && prefix; i++) prefix = SameStackType(stable[i], beta[i]);
            if (prefix) { for (int i = 0; i < stable.Length; i++) map[i] = i; return map; }
            var left = new int[stable.Length];
            int j = 0;
            for (int i = 0; i < stable.Length; i++)
            {
                while (j < beta.Length && !SameStackType(stable[i], beta[j])) j++;
                if (j == beta.Length) return null;
                left[i] = j++;
            }
            j = beta.Length - 1;
            for (int i = stable.Length - 1; i >= 0; i--)
            {
                while (j >= 0 && !SameStackType(stable[i], beta[j])) j--;
                if (j < 0 || j != left[i]) return null;
                j--;
            }
            for (int i = 0; i < stable.Length; i++) map[left[i]] = i;
            return map;
        }

        // Whether a call compiled against the public game's return type still works against the
        // beta's: the same type (or signedness), or a void call whose new result is dropped.
        public static bool ReturnAdaptable(string stableReturn, string betaReturn)
        {
            return stableReturn == "System.Void" || SameStackType(stableReturn, betaReturn);
        }

        // itemdatabase.ReturnItem(a, b) on the public game: the item id is the second number
        // (TLDLoader calls ReturnItem(0, id)); a lone non-zero first number is taken as the id.
        public static int ReturnItemIndex(int first, int second)
        {
            return second == 0 && first != 0 ? first : second;
        }

        // Whether a Harmony patch argument binds against the target method: Harmony's special
        // names, ___field for a field of the target type, __N for an argument index, else the
        // name of one of the target's parameters.
        public static bool PatchArgumentFits(string name, bool targetStatic, bool targetVoid, string[] targetParameters, Func<string, bool> hasField)
        {
            if (string.IsNullOrEmpty(name)) return false;
            switch (name)
            {
                case "__instance": return !targetStatic;
                case "__result": case "__resultRef": return !targetVoid;
                case "__state": case "__args": case "__originalMethod": case "__runOriginal": case "__exception": return true;
            }
            if (name.StartsWith("___", StringComparison.Ordinal)) return hasField(name.Substring(3));
            int index;
            if (name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(name.Substring(2), out index)) return index >= 0 && index < targetParameters.Length;
            return Array.IndexOf(targetParameters, name) >= 0;
        }

        // gameObject.AddComponent<T>() in mod code that becomes a TldLoaderBridge call taking the
        // GameObject: run-time parts and slots need the beta's set-up (isPart, FStart, slot layer).
        public static string AddComponentHelper(string componentType)
        {
            switch (componentType)
            {
                case "partscript": case "attachablescript": return "ModAddPart";
                case "partslotscript": return "ModAddSlot";
            }
            return null;
        }

        // Instance methods whose signature changed. The key is "type::name(param types)";
        // the result is a static TldLoaderBridge method taking the instance first.
        public static string MethodHelper(string declaringType, string name, string[] parameterTypes)
        {
            switch (declaringType + "::" + name + "(" + string.Join(",", parameterTypes ?? new string[0]) + ")")
            {
                // The beta added the switching usable; null means "from code".
                case "headlightscript::SwitchState(System.Int32)": return "HeadlightSwitchState";
                case "headlightscript::SwitchState()": return "HeadlightSwitchStateNext";
                case "layerScript::FStart()": return "LayerFStart";
                case "savedatascript::RefreshProperties()": return "RefreshPropertiesData";
                case "mainscript::MirrorsUpdate(UnityEngine.Vector3)": return "MirrorsUpdateMain";
                case "mainscript::MirrorsUpdate(UnityEngine.Vector3,System.Boolean)": return "MirrorsUpdateDepth";
                case "fpscontroller::RightHandRelease()": return "PlayerRightHandRelease";
                case "mainscript::PressedEscape()": return "MainPressedEscape";
                case "mainscript::TakeScreenShot()": return "MainTakeScreenshot";
                case "roadGenScript::FStart()": return "RoadFStart";
                case "pickupable::FStart()": return "PickupFStart";
                case "usablescript::UpdTurnState()": return "UsableUpdateTurnState";
                case "usablescript::UpdTurnState(System.Int32)": return "UsableSetTurnState";
                // mainscript's survival shortcuts were removed; the public game's bodies changed
                // the player's survivalscript (and piss tank) with these factors.
                case "mainscript::HpCh(System.Single)": return "MainHpCh";
                case "mainscript::FoodCh(System.Single)": return "MainFoodCh";
                case "mainscript::WaterCh(System.Single)": return "MainWaterCh";
                case "mainscript::ShitCh(System.Single)": return "MainShitCh";
                case "mainscript::PissCh(System.Single)": return "MainPissCh";
                case "mainscript::isServerOrNoMulti()": return "MainIsServerOrNoMulti";
                // Moved to the menu handler.
                case "mainscript::PressedExit()": return "MainPressedExit";
                case "mainscript::PressedRestart()": return "MainPressedRestart";
                case "mainmenuscript::PressedLoadScene(System.String)":
                case "menuhandler::PressedLoadScene(System.String)": return "MenuPressedLoadScene";
                // The beta's Open() takes no "in main menu" argument; it reads DataFromMenu.
                case "newSaveScreenScript::Open(System.Boolean)": return "OpenSaveScreen";
                // Renamed on the beta, same arguments.
                case "mountStuff::Lock(pickupable,System.Boolean)": return "MountLock";
                case "attachablescript::Load(UnityEngine.Vector3)": return "AttachLoad";
                case "attachablescript::Refresh()": return "AttachRefresh";
                case "mainscript::ChGodMode(System.Boolean)": return "MainChGodMode";
                case "mainscript::ChKmh(System.Boolean)": return "MainChKmh";
                case "roadGenScript::OneCoord(UnityEngine.Vector3)": return "RoadPointNear";
                case "terrainHeightAlignToBuildingScript::FStart(System.Boolean)": return "StartTerrainAlign";
                case "tankcapscript::UpdCap(System.Boolean)": return "RefreshCap";
                case "carscript::RefreshInteriorLight()": return "RefreshCarInterior";
                // The beta's bool was multiplayer bookkeeping; stable's Drop(bool) is a plain drop.
                case "pickupable::Drop(System.Boolean)": return "PickupDrop";
                // Attach at a point against the hit collider.
                case "attachablescript::Attach(UnityEngine.Transform,UnityEngine.RaycastHit)": return "AttachAt";
                // The beta's Craft(attachablescript) has no "skip the animation" flag.
                case "partslotscript::Craft(partscript,System.Boolean)": return "SlotCraft";
                // The beta's FStart also spawns and mounts a starting part; the public game's only set up.
                case "partslotscript::FStart()": return "SlotFStart";
                // Mods registering saved world items: the host owns them in multiplayer,
                // so a guest's own copy is discarded (TldLoaderBridge.ModRegisterItem).
                case "tosaveitemscript::FStart(System.Int32)": return "ModRegisterItem";
                // The public game's ReturnItem(index, 0) is the beta's Item(index).
                case "itemdatabase::ReturnItem(System.Int32,System.Int32)": return "ReturnItem";
                case "savedatascript::SpawnItem(save_prefab,UnityEngine.Vector3,System.Int32)": return "SpawnStableSavedItem";
                case "savedatascript::SpawnItem(save_prefab,System.Int32)": return "SpawnStableSavedItem";
                case "save_usablescript::.ctor(usablescript)": return "CaptureUsable";
                case "save_painting::.ctor(paintingScript)": return "CapturePainting";
                case "save_newai::.ctor(newAiScript)": return "CaptureAi";
                case "tankscript::SetTankId(System.Int32,System.Int32)": return "SetTankId";
                case "tosaveitemscript::FStart(System.UInt32)": return "ModRegisterItem";
                case "tosaveitemscript::FStart()": return "ModRegisterNew";
                case "mainscript::PostSpawn(UnityEngine.GameObject)": return "ModPostSpawn";
            }
            return null;
        }

        static FieldRewrite Rename(string name, bool isStatic) { return new FieldRewrite { Kind = RewriteKind.RenameField, Name = name, Static = isStatic }; }
        static FieldRewrite Helper(string name, bool isStatic, string setName = null) { return new FieldRewrite { Kind = RewriteKind.CallHelper, Name = name, SetName = setName, Static = isStatic }; }

        // A load becomes the helper; a store only when the rule has a store helper; an
        // address (ldflda) of a helper field stays as it is.
        public static bool CanApply(FieldRewrite rewrite, bool isLoad, bool isStaticOpcode, bool isStore = false)
        {
            if (rewrite.Kind == RewriteKind.None) return false;
            if (rewrite.Static != isStaticOpcode) return false;
            if (rewrite.Kind == RewriteKind.RenameField) return true;
            return isLoad || (isStore && rewrite.SetName != null);
        }

        // TLDLoader item-framework methods whose bodies are replaced by a call to a
        // TldLoaderBridge method taking the same arguments (null: keep the body).
        public static string ReplacedLoaderMethod(string type, string method)
        {
            switch (type + "::" + method)
            {
                case "TLDLoader.ItemDatabase::MakeSaveable": return "ModMakeSaveable";
                case "TLDLoader.ItemRegistrationBuilder::SpawnAt": return "ModSpawnAt";
            }
            return null;
        }

        // ---- Modded item prefab ids ----
        // A registered item's prefab id is what the save stores (save_item.prefabID) and
        // what itemdatabase.Item(id) returns. It must not depend on load order or on which
        // other mods are installed, and two players with the same mods must agree, so it is
        // hashed from "mod ID#item id" into a fixed range above the vanilla items. A taken
        // slot moves on to the next free one. Remembered ids (per machine) win.
        public const int ModItemBase = 4000;
        public const int ModItemRange = 1024;

        public static string ModItemKey(string modId, int itemId) { return (modId ?? "").Trim() + "#" + itemId; }

        public static uint Fnv1a(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text ?? "") { hash ^= (byte)(c & 0xFF); hash *= 16777619; hash ^= (byte)(c >> 8); hash *= 16777619; }
            return hash;
        }

        public static bool IsModItemId(int prefabId) { return prefabId >= ModItemBase && prefabId < ModItemBase + ModItemRange; }

        // occupant(id): the key holding that id now, or null. remembered: this key's id from an
        // earlier session (or -1). Returns -1 when the whole range is full.
        public static int ModItemId(string key, int remembered, Func<int, string> occupant)
        {
            if (IsModItemId(remembered))
            {
                string holder = occupant(remembered);
                if (holder == null || holder == key) return remembered;
            }
            int start = (int)(Fnv1a(key) % ModItemRange);
            for (int i = 0; i < ModItemRange; i++)
            {
                int id = ModItemBase + (start + i) % ModItemRange;
                string holder = occupant(id);
                if (holder == null || holder == key) return id;
            }
            return -1;
        }

        // A field that is T[] on one side and List<T> on the other (same name, ignoring case).
        public enum CollectionShape { None, ArrayInModListInGame, ListInModArrayInGame }
        public static CollectionShape Shape(string modElementOfArray, string modElementOfList, string gameElementOfArray, string gameElementOfList)
        {
            if (modElementOfArray != null && modElementOfArray == gameElementOfList) return CollectionShape.ArrayInModListInGame;
            if (modElementOfList != null && modElementOfList == gameElementOfArray) return CollectionShape.ListInModArrayInGame;
            return CollectionShape.None;
        }

        // TLDLoader's own Harmony classes that target or use game code the beta lacks.
        static readonly string[] droppedLoaderPatches =
        {
            "Patch_itemdatabase_ReturnItem", "Patch_itemPlaceRemoveScript_PlaceOneItem", "Patch_itemPlaceRemoveScript_RemoveItems",
            "Patch_MainMenu_Discord", "Patch_MainMenu_CopyDiscord", "Patch_SaveScreen_Delete",
        };
        public static bool IsDroppedLoaderPatch(string typeName) { return Array.IndexOf(droppedLoaderPatches, typeName) >= 0; }

        public enum TargetStatus { Unknown, Exists, Missing }

        // A Harmony class is left out of PatchAll when its target is gone, or when
        // its own code uses game members the beta lacks (it would throw inside the
        // game method on every call). An undeterminable target is kept.
        public static bool StripPatch(TargetStatus target, bool bodyUsesMissing, bool listed)
        {
            return listed || target == TargetStatus.Missing || bodyUsesMissing;
        }

        // Harmony's MethodType: Normal=0, Getter=1, Setter=2, Constructor=3, StaticConstructor=4.
        // Returns the metadata name to look for, or null when it cannot be checked.
        public static string PatchedMethodName(string name, int methodType)
        {
            switch (methodType)
            {
                case 0: return name;
                case 1: return name == null ? null : "get_" + name;
                case 2: return name == null ? null : "set_" + name;
                case 3: return ".ctor";
                case 4: return ".cctor";
            }
            return null;
        }

        public static string CacheKey(string sourceSha, string gameSha)
        {
            return "v" + TranslatorVersion + ":" + sourceSha + ":" + gameSha;
        }

        public enum LoaderPlan { NotInstalled, UpToDate, FromBackup, FromCurrent }

        // TLDLoader.dll is rewritten in place. stateTranslated/stateKey record the
        // last write; anything else in its place is a new original (a reinstall or
        // TLDLoader's own updater) and becomes the backup.
        public static LoaderPlan PlanLoader(string currentSha, string stateTranslated, string stateKey, string backupSha, string gameSha)
        {
            if (string.IsNullOrEmpty(currentSha)) return LoaderPlan.NotInstalled;
            bool ours = !string.IsNullOrEmpty(stateTranslated) && string.Equals(currentSha, stateTranslated, StringComparison.OrdinalIgnoreCase);
            if (!ours) return LoaderPlan.FromCurrent;
            // Our output without its backup: never translate it a second time.
            if (string.IsNullOrEmpty(backupSha)) return LoaderPlan.UpToDate;
            return stateKey == CacheKey(backupSha, gameSha) ? LoaderPlan.UpToDate : LoaderPlan.FromBackup;
        }

        // When turned off, put the player's original TLDLoader.dll back.
        public static bool ShouldRestore(string currentSha, string stateTranslated, bool backupExists)
        {
            return backupExists && !string.IsNullOrEmpty(currentSha) &&
                string.Equals(currentSha, stateTranslated, StringComparison.OrdinalIgnoreCase);
        }

        // The beta is one scene with an in-world main menu; TLDLoader expects the
        // public game's two scenes.
        public const string MenuScene = "SceneMenu";
        public const string WorldScene = "SceneNewRandom";
        public static string SceneName(bool inWorld) { return inWorld ? WorldScene : MenuScene; }

        public enum Transition { None, EnterWorld, LeaveWorld }
        public static Transition Step(bool wasInWorld, bool onMainMenu)
        {
            if (!wasInWorld && !onMainMenu) return Transition.EnterWorld;
            if (wasInWorld && onMainMenu) return Transition.LeaveWorld;
            return Transition.None;
        }

        // "id@version;..." sorted, for lobby metadata. ';' and '@' in values are replaced.
        public static string EncodeModList(IEnumerable<KeyValuePair<string, string>> mods)
        {
            var rows = new List<string>();
            foreach (var mod in mods)
            {
                string id = Clean(mod.Key);
                if (id.Length == 0) continue;
                rows.Add(id + "@" + Clean(mod.Value));
            }
            rows.Sort(StringComparer.Ordinal);
            var text = new StringBuilder();
            foreach (string row in rows)
            {
                if (text.Length + row.Length + 1 > LobbyListLimit) break;
                if (text.Length > 0) text.Append(';');
                text.Append(row);
            }
            return text.ToString();
        }

        static string Clean(string value)
        {
            return (value ?? "").Trim().Replace(';', ',').Replace('@', '_');
        }

        public static Dictionary<string, string> DecodeModList(string text)
        {
            var mods = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return mods;
            foreach (string row in text.Split(';'))
            {
                if (row.Length == 0) continue;
                int at = row.LastIndexOf('@');
                string id = at < 0 ? row : row.Substring(0, at);
                if (id.Length > 0) mods[id] = at < 0 ? "" : row.Substring(at + 1);
            }
            return mods;
        }

        // Null when both sides run the same mods; otherwise a short player-facing note.
        // A missing remote value (host without TLDLoader support) counts as no mods.
        public static string DescribeDifference(string local, string host)
        {
            var mine = DecodeModList(local);
            var theirs = DecodeModList(host);
            var onlyHost = new List<string>(); var onlyMine = new List<string>(); var versions = new List<string>();
            foreach (var pair in theirs)
            {
                string version;
                if (!mine.TryGetValue(pair.Key, out version)) onlyHost.Add(pair.Key);
                else if (version != pair.Value) versions.Add(pair.Key + " (host " + pair.Value + ", you " + version + ")");
            }
            foreach (var pair in mine) if (!theirs.ContainsKey(pair.Key)) onlyMine.Add(pair.Key);
            if (onlyHost.Count == 0 && onlyMine.Count == 0 && versions.Count == 0) return null;
            onlyHost.Sort(StringComparer.Ordinal); onlyMine.Sort(StringComparer.Ordinal); versions.Sort(StringComparer.Ordinal);
            var text = new StringBuilder("Your TLDLoader mods differ from the host's. You can still play, but modded things may not match.");
            if (onlyHost.Count > 0) text.Append("\nOnly the host has: ").Append(string.Join(", ", onlyHost.ToArray()));
            if (onlyMine.Count > 0) text.Append("\nOnly you have: ").Append(string.Join(", ", onlyMine.ToArray()));
            if (versions.Count > 0) text.Append("\nDifferent versions: ").Append(string.Join(", ", versions.ToArray()));
            return text.ToString();
        }

        // One settings row per mod.
        public static string ModRowLabel(string name, string version, bool disabled, int missingMembers)
        {
            string label = (string.IsNullOrEmpty(name) ? "?" : name.Trim()) + (string.IsNullOrEmpty(version) ? "" : " " + version.Trim());
            if (disabled) return label + " (disabled)";
            if (missingMembers > 0) return label + " (may fail on this game version)";
            return label;
        }
    }
}
