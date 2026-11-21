using TLDLoader;
using UnityEngine;
using HarmonyLib;

// Built against the stable branch's Assembly-CSharp (what community mods target), never shipped
// to players. Every member here differs on the beta, one per translation rule:
//  - mainscript.godmode / ChGodMode: moved to the developer settings (helpers)
//  - mainscript.chatWasOpen / TankInc(): removed (stand-in storage / stub)
//  - mainscript.HpCh: removed shortcut (helper doing what the stable body did)
//  - mainscript.Spawn(GameObject): returned void, now returns the object (adapted call)
//  - mainscript.load: save loading flag (helper)
//  - fpscontroller.RightHandRelease(): gained an argument (default passed)
//  - mainscript.mainWorld / doublecoord: the world object (helper) and its coordinate struct (Vector3d)
//  - TerrainGenerator / roadclass: deleted types (stand-ins), roads filled from the beta's
public class RoadworthyStableApi : Mod
{
    public override string ID { get { return "RoadworthyStableApi"; } }
    public override string Name { get { return "Roadworthy stable API check"; } }
    public override string Author { get { return "Roadworthy"; } }
    public override string Version { get { return "1.0.0"; } }
    public override bool LoadInMenu { get { return true; } }

    public override void OnMenuLoad()
    {
        var menu = mainmenuscript.mainmenu;
        if (menu == null || menu.SaveScreenObj == null || menu.SettingsScreenObj == null)
            throw new System.InvalidOperationException("Stable menu screen references did not resolve.");

        menu.saveScreen.Open(false);
        if (!menu.SaveScreenObj.activeSelf)
            throw new System.InvalidOperationException("Stable save screen did not show the open beta screen.");
        menu.saveScreen.PressedBack();
        menu.Settings.Open();
        if (menu.SaveScreenObj.activeSelf || !menu.SettingsScreenObj.activeSelf)
            throw new System.InvalidOperationException("Stable menu screen visibility did not track the beta settings screen.");
        menu.Settings.PBack();
        if (menu.SettingsScreenObj.activeSelf)
            throw new System.InvalidOperationException("Stable settings screen stayed open after closing.");

        Debug.Log("[RW stable API] menu screen adapters passed.");
        if (System.Environment.GetEnvironmentVariable("RW_TEST_STABLE_MENU_START") == "1")
        {
            menu.DFMS.seed = -1752327066;
            menu.PressedLoadScene("SceneNewRandom");
        }
    }

    public static bool Handbrake(save_carscript car) { return car.bhandbrake; }
    public static int StableSaveField(save_prefab prefab) { return prefab.categoryID; }

    public static GameObject SpawnSavedPrefab(savedatascript save, Vector3 position)
    {
        var prefab = new save_prefab(0, 333, 100, 2, -30, 0, 90, 0);
        if (prefab.transform.pos.x != 100 || prefab.transform.rot.y != 90)
            throw new System.InvalidOperationException("Stable save constructor did not preserve its coordinates.");
        return save.SpawnItem(prefab, position, 0);
    }

    public static save_usablescript CaptureUsable(usablescript usable) { return new save_usablescript(usable); }
    public static save_painting CapturePainting(paintingScript painting) { return new save_painting(painting); }
    public static save_newai CaptureAi(newAiScript ai) { return new save_newai(ai); }

    public static object RoadGroups(roadBuildingScript roads)
    {
        return roads.groups;
    }

    public static int InventorySlot(pickupable item) { return item.inIninventory; }
    public static float RadioVolume(radioscript radio) { return radio.v; }

    public static System.Collections.Generic.List<partslotscript> Slots(pickupable item, System.Collections.Generic.List<partslotscript> value)
    {
        item.slots = value;
        return item.slots;
    }

    bool pendingSaveCheck;
    attachablescript recoveringPart;
    Rigidbody recoveryTarget;
    GameObject recoveryRoot;
    ConfigurableJoint originalJoint;
    Collider recoveryPartCollider, recoveryTargetCollider;

    static void VerifyPartAdapters()
    {
        var host = new GameObject("Stable API attachment fixture");
        host.SetActive(false);
        var slot = host.AddComponent<partslotscript>();
        var child = new GameObject("Stable API part fixture");
        child.transform.SetParent(host.transform);
        var part = child.AddComponent<partscript>();
        var attachment = child.GetComponent<attachablescript>();
        var saved = child.AddComponent<tosaveitemscript>();
        var pick = host.AddComponent<pickupable>();
        part.p = pick;
        if (part.p != pick || (pickupable)saved.GetType().GetField("P").GetValue(saved) != pick)
            throw new System.InvalidOperationException("Stable part pickup assignment did not reach the native save component.");
        part.p = null;
        if (part.p != null || (pickupable)saved.GetType().GetField("P").GetValue(saved) != null)
            throw new System.InvalidOperationException("Stable part pickup could not be cleared.");
        slot.part = part;
        if (slot.part != part) throw new System.InvalidOperationException("Stable slot assignment did not reach the beta slot.");
        slot.part = null;
        if (slot.part != null) throw new System.InvalidOperationException("Stable slot clear did not reach the beta slot.");
        slot.enableSpawn = true;
        if (slot.spawnChance != 0f)
            throw new System.InvalidOperationException("Enabling a stable slot bypassed its default spawn chance.");
        slot.spawnChance = 0.25f;
        slot.randomChance = 0.5f;
        if (slot.spawnChance != 0.25f || slot.randomChance != 0.5f)
            throw new System.InvalidOperationException("Stable slot chances did not round trip.");
        attachment.defParent = host.transform;
        attachment.snapYtoNormal = true;
        if (attachment.defParent != host.transform || !attachment.snapYtoNormal)
            throw new System.InvalidOperationException("Stable attachment settings did not round trip.");
        var target = host.AddComponent<attachTargetscript>();
        target.target = host.transform;
        attachment.attachAtStartTarget = target;
        if (attachment.attachAtStartTarget != target)
            throw new System.InvalidOperationException("Stable startup target did not bind to the beta slot.");
        attachment.ConnectCars = true;
        attachment.originalTarget = target.transform;
        attachment.reatachPoint = target.transform;
        attachment.reatach = true;
        if (!attachment.ConnectCars || attachment.parentAtAttach || attachment.originalTarget != target.transform ||
            !attachment.reatach || attachment.reatachPoint != target.transform)
            throw new System.InvalidOperationException("Stable tow configuration did not reach the joint path.");
        attachment.reatach = false;
        var car = host.AddComponent<carscript>();
        var seat = child.AddComponent<seatscript>();
        car.driverSeat = seat;
        if (car.driverSeat != seat || seat.Car != car)
            throw new System.InvalidOperationException("Stable driver seat did not reach beta's seat configuration.");
        car.KmhObj = child;
        if (car.KmhObj != child) throw new System.InvalidOperationException("Stable speed display did not bind.");
        Object.Destroy(host);
        Debug.Log("[RW stable API] part adapters passed.");
    }

    static void VerifyChangedOverloads(mainscript main, savedatascript save)
    {
        var fixture = new GameObject("Stable API overload fixture");
        fixture.SetActive(false);
        try
        {
            var light = fixture.AddComponent<headlightscript>();
            light.states = new System.Collections.Generic.List<headlightscript.lightState> {
                new headlightscript.lightState(), new headlightscript.lightState(), new headlightscript.lightState()
            };
            light.switchToState = 1;
            light.enabled = false;
            light.SwitchState();
            if (light.switchToState != 2 || !light.enabled)
                throw new System.InvalidOperationException("Stable headlight switch did not advance.");
            light.SwitchState();
            if (light.switchToState != 0)
                throw new System.InvalidOperationException("Stable headlight switch did not wrap.");
            var collider = fixture.AddComponent<BoxCollider>();
            var layer = fixture.AddComponent<layerScript>();
            var pickup = fixture.AddComponent<pickupable>();
            layer.p = pickup;
            layer.FStart();
            fixture.layer = 2;
            layer.FStart();
            if (layer.p != pickup || layer.col != collider || !layer.started || layer.defLayer != 2)
                throw new System.InvalidOperationException("Stable layer setup lost its pickup or ignored a second initialization.");
            var menuDataType = typeof(mainscript).Assembly.GetType("DataFromMenuScript", true);
            object menuData = menuDataType.GetField("s").GetValue(null);
            int seed = (int)menuDataType.GetField("seed").GetValue(menuData);
            save.properties.seed = int.MinValue;
            save.properties.date = "";
            save.RefreshProperties();
            if (save.properties.seed != seed || string.IsNullOrEmpty(save.properties.date))
                throw new System.InvalidOperationException("Stable save refresh did not update the live save properties (expected seed " +
                    seed + ", actual " + save.properties.seed + ", date '" + save.properties.date + "').");
            main.MirrorsUpdate(main.player.transform.position);
            main.MirrorsUpdate(main.player.transform.position, false);
        }
        finally
        {
            Object.Destroy(fixture);
        }
        Debug.Log("[RW stable API] changed overloads passed.");
    }

    static void VerifyWorldAndComponentAdapters(mainscript main, savedatascript save)
    {
        var settings = new gamemodesettingsscript.gamemodesettings();
        settings.homesickMode = 2;
        var mapField = settings.GetType().GetField("map");
        var map = mapField.GetValue(settings);
        if (settings.homesickMode != 2 || (int)map.GetType().GetField("homesickMode").GetValue(map) != 2)
            throw new System.InvalidOperationException("Stable map mode did not update the beta map settings.");

        var instance = new GameObject("Stable API component fixture");
        instance.SetActive(false);
        var saved = instance.AddComponent<tosaveitemscript>();
        var ammo = instance.AddComponent<ammoscript>();
        saved.ammo = ammo;
        ammo.ammoRmin = 1;
        ammo.ammoRmax = 1;
        ammo.max = 10;
        var entry = new newRandomStuffSpawnScript.item { fr1 = 4, fr2 = 5 };
        saved.RandomSpawned(entry);
        if (ammo.GetAmmo() != 4)
            throw new System.InvalidOperationException("Stable ammo range did not reach native item spawning.");
        var target = instance.AddComponent<attachTargetscript>();
        target.target = instance.transform;
        saved.attachTargets = new[] { target };
        var nativeSlots = (System.Collections.IList)saved.GetType().GetField("partslotscripts").GetValue(saved);
        if (saved.attachTargets[0] != target || nativeSlots.Count != 1)
            throw new System.InvalidOperationException("Stable attachment targets did not join the native slot list.");
        saved.attachTargets = new attachTargetscript[0];
        if (saved.attachTargets.Length != 0 || nativeSlots.Count != 1)
            throw new System.InvalidOperationException("Stable attachment target update changed a native slot index.");

        var usable = instance.AddComponent<usablescript>();
        usable.turnable = true;
        var cap = instance.AddComponent<tankcapscript>();
        cap.canToggle = false;
        cap.openVolume = 0.4f;
        cap.closedRot = new Vector3(0, 10, 0);
        var nativeUsable = cap.GetType().GetField("usable");
        if (nativeUsable.GetValue(cap) != null)
            throw new System.InvalidOperationException("Stable sound and rotation settings created a toggle on a disabled cap.");
        nativeUsable.SetValue(cap, usable);
        cap.canToggle = false;
        if (usable.turnable || cap.canToggle || cap.openVolume != 0.4f || cap.closedRot.y != 10)
            throw new System.InvalidOperationException("Stable cap settings did not reach the beta cap.");
        var open = new GameObject("Legacy cap indicator");
        open.transform.SetParent(instance.transform);
        open.SetActive(false);
        cap.GOpen = new System.Collections.Generic.List<GameObject> { open };
        cap.valve = 1f;
        cap.UpdCap(false);
        if (!open.activeSelf) throw new System.InvalidOperationException("Stable cap refresh did not update its indicator.");
        var cabin = instance.AddComponent<interiorLightScript>();
        cabin.ISwitches = new[] { usable };
        cabin.ILight = new[] { open };
        var runtime = cabin.GetType().Assembly;
        var interior = runtime.GetType("TldMp.TldLoaderBridge", true).GetMethod("SetSaveInteriorLights");
        interior.Invoke(null, new object[] { saved, new System.Collections.Generic.List<interiorLightScript> { cabin } });
        var lightList = instance.GetComponent(runtime.GetType("TldMp.TldStableInteriorList", true));
        var refresh = lightList.GetType().GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        usable.turned = false;
        refresh.Invoke(lightList, null);
        if (open.activeSelf) throw new System.InvalidOperationException("Saved interior switch did not turn the light off.");
        usable.turned = true;
        refresh.Invoke(lightList, null);
        if (!open.activeSelf) throw new System.InvalidOperationException("Saved interior switch did not turn the light on.");
        Object.Destroy(instance);

        var headlight = new headlightscript.lightState();
        var material = new Material(Shader.Find("Standard"));
        material.SetColor("_EmissionColor", Color.red);
        headlight.M = material;
        if (headlight.M != material)
            throw new System.InvalidOperationException("Stable headlight material did not reach a beta renderer group.");
        Object.Destroy(material);

        var road = save.mainRoadScript;
        var offsetty = road.GetType().GetField("offsetty");
        float oldOffset = road.roadAboveTerrain;
        road.roadAboveTerrain = oldOffset + 0.25f;
        if ((float)offsetty.GetValue(road) != oldOffset + 0.25f)
            throw new System.InvalidOperationException("Stable road elevation did not reach beta's road generator.");
        road.roadAboveTerrain = oldOffset;
        if (main.terrainGenerationSettings.objGeneration.objTypes.Length == 0 ||
            main.terrainGenerationSettings.roadBuildingGeneration.parent == null)
            throw new System.InvalidOperationException("Stable world catalogs did not expose beta prefabs.");
        var terrainSettings = main.terrainGenerationSettings;
        if (terrainSettings.Terrain.tileSize <= 0 || terrainSettings.terranGenerator == null ||
            terrainSettings.terranGenerator.allterrains == null || terrainSettings.terranGenerator.allterrains.Count == 0)
            throw new System.InvalidOperationException("Stable terrain settings did not expose the beta terrain tiles.");
        if (terrainSettings.importantObjs == null || terrainSettings.importantObjs.Count == 0 ||
            terrainSettings.world == null || !terrainSettings.started)
            throw new System.InvalidOperationException("Stable terrain settings did not expose the beta world anchors.");
        var graphics = new settingsscript.setting();
        graphics.BFarTerrain = true;
        if (!graphics.BFarTerrain) throw new System.InvalidOperationException("Stable distant terrain did not enable beta draw distance.");
        graphics.BFarTerrain = false;
        if (graphics.BFarTerrain) throw new System.InvalidOperationException("Stable distant terrain did not disable beta draw distance.");
        var piece = road.roads[0];
        var shift = main.mainWorld.coord;
        var center = new Vector3((float)(piece.posX + shift.x), (float)(piece.posY + shift.y), (float)(piece.posZ + shift.z));
        var probe = center + Vector3.right * 18f;
        var projected = road.OneCoord(probe);
        if ((projected - probe).sqrMagnitude < 0.01f || Vector3.Distance(projected, center) > 40f)
            throw new System.InvalidOperationException("Stable road query did not return the nearby beta road.");
        float oldLength = road.length, oldScale = road.scale;
        doublecoord oldDouble = road.doublePos;
        road.length = 100f;
        road.scale = 0.5f;
        road.doublePos = new doublecoord { x = 20, y = 0.1, z = 0 };
        if (road.length != 100f || road.scale != 0.5f || road.doublePos.x != 20 || road.doublePos.y != 0.1)
            throw new System.InvalidOperationException("Stable road controls did not keep their writes.");
        road.length = oldLength;
        road.scale = oldScale;
        road.doublePos = oldDouble;
        Debug.Log("[RW stable API] world and component adapters passed.");
    }

    static void VerifyTerrainMarkers(mainscript main)
    {
        var root = new GameObject("Stable API terrain marker fixture");
        var marker = root.AddComponent<terrainHeightAlignToBuildingScript>();
        var pad = new GameObject("Pad");
        pad.transform.SetParent(root.transform);
        pad.transform.position = new Vector3(25f, 500f, 40f);
        pad.transform.localScale = Vector3.one * 8f;
        marker.helpPosCoords = new[] { pad.transform };
        marker.range2 = new[] { 3f };
        marker.FStart(true);
        if (pad.activeSelf || marker.helpPosList.Count != 1 || marker.helpPosList[0].range.y != 144f)
            throw new System.InvalidOperationException("Stable terrain marker did not build its range and hide the marker.");

        var runtime = System.Array.Find(System.AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "Tld.Multiplayer");
        var bridge = runtime.GetType("TldMp.TldStableTerrain", true);
        bridge.GetMethod("Tick", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { true });
        var game = typeof(mainscript).Assembly;
        var nativeQuery = game.GetType("poiGenScript", true).GetMethod("TerrainAlignsValue");
        var emptyPois = System.Activator.CreateInstance(nativeQuery.GetParameters()[1].ParameterType);
        var shift = main.mainWorld.coord;
        var position = new doublecoord { x = 25f - shift.x, y = 0, z = 40f - shift.z };
        var result = nativeQuery.Invoke(null, new object[] { position, emptyPois });
        double height = System.Convert.ToDouble(result.GetType().GetField("height").GetValue(result));
        if (System.Math.Abs(height - (500f - shift.y)) > 0.01)
            throw new System.InvalidOperationException("Stable terrain marker did not affect the beta terrain query.");
        Object.Destroy(root);
        Debug.Log("[RW stable API] terrain markers passed.");
    }

    static void VerifyTerrainHoles(mainscript main)
    {
        var menuType = typeof(mainscript).Assembly.GetType("menuhandler", true);
        var menu = menuType.GetField("s").GetValue(null);
        var map = menuType.GetField("currentMainMap").GetValue(menu);
        var generators = (System.Collections.IList)map.GetType().GetField("poiGens").GetValue(map);
        System.Collections.IList quads = null;
        foreach (var generator in generators)
        {
            var chunks = (System.Collections.IDictionary)generator.GetType().GetField("chunks").GetValue(generator);
            foreach (var chunk in chunks.Values)
            {
                var pois = (System.Collections.IDictionary)chunk.GetType().GetField("pois").GetValue(chunk);
                foreach (var poi in pois.Values)
                {
                    quads = (System.Collections.IList)poi.GetType().GetField("terrainHoleQuads").GetValue(poi);
                    break;
                }
                if (quads != null) break;
            }
            if (quads != null) break;
        }
        if (quads == null) throw new System.InvalidOperationException("No native POI hole list was available.");
        var quadType = quads.GetType().GetGenericArguments()[0];
        var corners = new[] { Vector3.zero, Vector3.right * 4, Vector3.forward * 6, new Vector3(4, 0, 6) };
        var quad = System.Activator.CreateInstance(quadType, new object[] { new doublecoord { x = 12345, z = 67890 }, Vector3.zero, corners });
        quads.Add(quad);
        try
        {
            if (!main.holes.Exists(hole => hole.pos.x == 12347 && hole.pos.z == 67893))
                throw new System.InvalidOperationException("Stable terrain holes did not include the native quad.");
        }
        finally { quads.Remove(quad); }
        Debug.Log("[RW stable API] terrain holes passed.");
    }

    void StartJointRecovery(GameObject book)
    {
        book.GetComponent<tosaveitemscript>().FStart();
        recoveryRoot = new GameObject("Stable API hitch recovery target");
        recoveryRoot.transform.position = book.transform.position;
        recoveryTarget = recoveryRoot.AddComponent<Rigidbody>();
        recoveryTarget.isKinematic = true;
        recoveryTargetCollider = recoveryRoot.AddComponent<BoxCollider>();
        recoveryTargetCollider.isTrigger = true;
        var point = new GameObject("attachPoint").transform;
        point.SetParent(recoveryRoot.transform, false);
        book.AddComponent<partscript>();
        recoveringPart = book.GetComponent<attachablescript>();
        recoveringPart.ConnectCars = true;
        recoveringPart.targetPosRot = false;
        recoveringPart.lockRotation = true;
        if (recoveringPart.targetPosRot)
            throw new System.InvalidOperationException("Stable rotation locking enabled the beta snap flag.");
        var colliderObject = new GameObject("Stable API custom part collider");
        colliderObject.transform.SetParent(book.transform, false);
        recoveryPartCollider = colliderObject.AddComponent<BoxCollider>();
        recoveryPartCollider.isTrigger = true;
        var colliders = new System.Collections.Generic.List<Collider>();
        recoveringPart.C = colliders;
        recoveringPart.C.Add(recoveryPartCollider);
        if (!object.ReferenceEquals(recoveringPart.C, colliders) || colliders.Count != 1)
            throw new System.InvalidOperationException("Stable attachment collider list lost an in-place edit.");
        recoveryPartCollider.enabled = false;
        recoveringPart.Attach(point, recoveryTargetCollider);
        originalJoint = recoveringPart.J;
        if (originalJoint == null || originalJoint.connectedBody != recoveryTarget || recoveringPart.reatach)
            throw new System.InvalidOperationException("Could not initialize the native hitch recovery fixture.");
        VerifyJointMotion(originalJoint, ConfigurableJointMotion.Locked);
        if (!recoveryPartCollider.enabled || !Physics.GetIgnoreCollision(recoveryPartCollider, recoveryTargetCollider))
            throw new System.InvalidOperationException("Native mounting did not apply the custom attachment collider.");

        // Beta's Update detaches a joint whose connected body was lost; stable's recovery
        // path re-attaches it. Exercise that path directly so the result does not depend on
        // frame order or on the book prefab being simulated.
        recoveringPart.Detach();
        if (Physics.GetIgnoreCollision(recoveryPartCollider, recoveryTargetCollider))
            throw new System.InvalidOperationException("Native detachment did not restore the custom collider pair.");
        recoveringPart.Attach(point, recoveryTargetCollider);
        if (recoveringPart.J == null || recoveringPart.J == originalJoint || recoveringPart.J.connectedBody != recoveryTarget)
            throw new System.InvalidOperationException("Stable re-attach did not create a fresh native joint.");
        VerifyJointMotion(recoveringPart.J, ConfigurableJointMotion.Locked);
        recoveringPart.lockRotation = false;
        VerifyJointMotion(recoveringPart.J, ConfigurableJointMotion.Free);
        recoveringPart.gameObject.GetComponent<tosaveitemscript>().Claim(false);
        recoveringPart.Detach();
        if (Physics.GetIgnoreCollision(recoveryPartCollider, recoveryTargetCollider))
            throw new System.InvalidOperationException("Stable detach did not restore the custom collider pair.");
        Object.Destroy(book);
        Object.Destroy(recoveryRoot);
        recoveringPart = null;
        Debug.Log("[RW stable API] joint recovery passed.");
    }

    static void VerifyJointMotion(ConfigurableJoint joint, ConfigurableJointMotion expected)
    {
        if (joint.angularXMotion != expected || joint.angularYMotion != expected || joint.angularZMotion != expected)
            throw new System.InvalidOperationException("The translated rotation lock did not reach all native joint axes.");
    }

    static void VerifyHarmonyAdapters(savedatascript save)
    {
        var harmony = new Harmony("roadworthy.stable-api-fixture");
        var game = typeof(mainscript).Assembly;
        var terrain = game.GetType("terrainGenSettings", true).GetMethod("GetFirstHeight");
        var terrainArgs = new object[] { new doublecoord { x = 112, z = 238 } };
        var baseline = (double)terrain.Invoke(null, terrainArgs);
        harmony.PatchAll(typeof(RoadworthyStableApi).Assembly);
        if ((double)terrain.Invoke(null, terrainArgs) != baseline)
            throw new System.InvalidOperationException("Inactive terrain hooks changed the beta height.");
        WorldTweaker.Harmony.GetTerrainHeight_Postfix.Enabled = true;
        WorldTweaker.Harmony.GetTerrainHeightWorld_Postfix.Enabled = true;
        var adjusted = (double)terrain.Invoke(null, terrainArgs);
        WorldTweaker.Harmony.GetTerrainHeight_Postfix.Enabled = false;
        WorldTweaker.Harmony.GetTerrainHeightWorld_Postfix.Enabled = false;
        if (System.Math.Abs(adjusted - (baseline + 5)) > 0.01)
            throw new System.InvalidOperationException("Stable terrain postfixes did not adjust beta's height.");

        var poi = new GameObject("Stable building spawn fixture");
        try
        {
            var nativePoi = poi.AddComponent(game.GetType("poiScript", true));
            var spawn = nativePoi.GetType().GetMethod("SpawnStuff");
            var recordType = spawn.GetParameters()[0].ParameterType;
            var record = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(recordType);
            recordType.GetField("id").SetValue(record, 42);
            recordType.GetField("spawnedFixed").SetValue(record, true);
            var spawnArgs = new[] { record };
            spawn.Invoke(nativePoi, spawnArgs);
            spawn.Invoke(nativePoi, spawnArgs);
            if (TheLongWho.Harmony.Patch_Building_SpawnStuff.Calls != 1 ||
                TheLongWho.Harmony.Patch_Building_SpawnStuff.BuildingId != 42)
                throw new System.InvalidOperationException("Stable building callback did not run once before native item spawning.");
        }
        finally { Object.Destroy(poi); }

        var nativeSave = typeof(savedatascript).GetMethod("SaveToData");
        var saveUi = typeof(newSaveScreenScript).GetMethod("Save");
        var saveItem = typeof(savedatascript).GetMethod("SaveToDictionary");
        var start = game.GetType("menuhandler", true).GetMethod("PressedStart");
        foreach (var target in new[] { nativeSave, saveUi, saveItem, start })
        {
            var patches = Harmony.GetPatchInfo(target);
            if (patches == null || !System.Linq.Enumerable.Contains(patches.Owners, harmony.Id))
                throw new System.InvalidOperationException("Stable Harmony patch did not attach to " + target.Name);
        }

        var copy = System.Activator.CreateInstance(save.data.GetType(), new object[] { save.data });
        nativeSave.Invoke(save, new[] { copy });
        if (Amt.PrepareSaveDataPatch.Calls == 0 || AskJimmyEquippedAccessoryVanillaSaveFilterPatch.Calls == 0)
            throw new System.InvalidOperationException("Stable save callbacks did not run during native serialization.");

        typeof(Amt.SavePatch).GetMethod("Postfix", new[] { typeof(string) }).Invoke(null, new object[] { "Fixture" });
        if (Amt.SavePatch.Path != "TLDsaveData_Fixture")
            throw new System.InvalidOperationException("Stable save callback did not get the native file prefix.");

        if (itemdatabase.d.ReturnItem(37001, 0) != WorldTweaker.Harmony.Patch_itemdatabase_ReturnItem.Marker)
            throw new System.InvalidOperationException("Stable item lookup did not reach the retargeted Harmony callback.");
        Object.Destroy(WorldTweaker.Harmony.Patch_itemdatabase_ReturnItem.Marker);
        Debug.Log("[RW stable API] Harmony adapters passed.");
    }

    static void VerifyRemoveFromMemory(savedatascript save)
    {
        var keep = SpawnSavedPrefab(save, mainscript.M.player.transform.position + Vector3.right * 4f);
        var drop = SpawnSavedPrefab(save, mainscript.M.player.transform.position + Vector3.right * 6f);
        var keepSave = keep == null ? null : keep.GetComponent<tosaveitemscript>();
        var dropSave = drop == null ? null : drop.GetComponent<tosaveitemscript>();
        if (keepSave == null || dropSave == null)
            throw new System.InvalidOperationException("The removeFromMemory fixture did not spawn save components.");
        keepSave.FStart();
        dropSave.FStart();
        if (keepSave.idInSave == 0 || dropSave.idInSave == 0)
            throw new System.InvalidOperationException("The removeFromMemory fixture items were not registered.");

        dropSave.removeFromMemory = true;
        if (!dropSave.removeFromMemory)
            throw new System.InvalidOperationException("removeFromMemory did not stay set while the item was alive.");

        // Stable only consults the flag when the object is destroyed. A save made while the
        // item is still alive still contains it.
        var copy = System.Activator.CreateInstance(save.data.GetType(), new object[] { save.data });
        // The save-data copy constructor shares itemData; use a fresh container so this
        // probe does not mutate the live save.
        var itemDataField = copy.GetType().GetField("itemData");
        itemDataField.SetValue(copy, System.Activator.CreateInstance(itemDataField.FieldType));
        typeof(savedatascript).GetMethod("SaveToData").Invoke(save, new[] { copy });
        if (!SaveItemsContain(copy, dropSave.idInSave))
            throw new System.InvalidOperationException("A live removeFromMemory item disappeared from the save.");

        dropSave.removeFromMemory = false;
        if (dropSave.removeFromMemory)
            throw new System.InvalidOperationException("removeFromMemory did not clear.");
        dropSave.removeFromMemory = true;

        // Remove() calls PreDestroy; the beta would append the far record unless the flag is honoured.
        int farBefore = SaveItemCount(save.data, dropSave.idInSave);
        typeof(tosaveitemscript).GetMethod("Remove").Invoke(dropSave, null);
        if (SaveItemCount(save.data, dropSave.idInSave) != farBefore)
            throw new System.InvalidOperationException("The far save gained a record for a removeFromMemory item after PreDestroy.");
        Object.Destroy(drop);
        Object.Destroy(keep);
        Debug.Log("[RW stable API] removeFromMemory lifecycle passed.");
    }

    static bool SaveItemsContain(object data, int id) { return SaveItemCount(data, id) > 0; }

    static int SaveItemCount(object data, int id)
    {
        if (data == null) return 0;
        var itemData = data.GetType().GetField("itemData");
        if (itemData == null) return 0;
        var value = itemData.GetValue(data);
        if (value == null) return 0;
        var items = value.GetType().GetField("items").GetValue(value) as System.Collections.IList;
        if (items == null) return 0;
        int count = 0;
        foreach (var item in items)
        {
            var idField = item.GetType().GetField("id");
            if (idField != null && (uint)idField.GetValue(item) == unchecked((uint)id)) count++;
        }
        return count;
    }

    static void VerifyLegacyPersistence(savedatascript save)
    {
        var far = save.data.farStuff;
        var worldTweaker = new save_prefab(0, 0, 0, 0, 0, 0, 0, 0);
        worldTweaker.rendszam = new save_rendszam { S = "roadlength=100" };
        far[12345] = worldTweaker;
        var cruiser = new save_prefab(0, 0, 0, 0, 0, 0, 0, 0);
        cruiser.meter = new save_meterscript
        {
            mutatok = new[] { new save_meterscript.save_meter { tipus = "gauge", value = 42f } },
            szamlapok = new save_meterscript.save_meter[0]
        };
        far[23456] = cruiser;
        var categoryRecord = new save_prefab(37002, 0, 0, 0, 0, 0, 0, 0);
        far[34567] = categoryRecord;

        var screen = save.saveScreen;
        var saveMethod = typeof(newSaveScreenScript).GetMethod("Save", new[] { typeof(savedata), typeof(Texture2D), typeof(string), typeof(bool) });
        if (screen == null || saveMethod == null)
            throw new System.InvalidOperationException("The legacy sidecar fixture could not reach the native save.");
        saveMethod.Invoke(screen, new object[] { save.data, new Texture2D(1, 1), "RoadworthyLegacy", false });

        far.Clear();
        var runtimeAssembly = System.Array.Find(System.AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "Tld.Multiplayer");
        var legacy = runtimeAssembly.GetType("TldMp.TldLegacySave", true);
        legacy.GetMethod("BeforeLoad", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { "RoadworthyLegacy" });
        legacy.GetMethod("AfterLoadPending", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, null);
        var loaded = savedatascript.d.data.farStuff;
        if (!loaded.ContainsKey(12345) || loaded[12345].rendszam == null || loaded[12345].rendszam.S != "roadlength=100")
            throw new System.InvalidOperationException("A WorldTweaker legacy record did not survive save and reload.");
        if (!loaded.ContainsKey(23456) || loaded[23456].meter == null || loaded[23456].meter.mutatok == null ||
            loaded[23456].meter.mutatok.Length != 1 || loaded[23456].meter.mutatok[0].value != 42f)
            throw new System.InvalidOperationException("A Company Cruiser gauge legacy record did not survive save and reload.");
        if (!loaded.ContainsKey(34567) || loaded[34567].categoryID != 37002)
            throw new System.InvalidOperationException("A legacy item category did not survive save and reload.");
        Debug.Log("[RW stable API] legacy sidecar passed.");
    }

    static void VerifyItemCategory()
    {
        var prefab = new GameObject("Legacy category fixture");
        try
        {
            var saved = prefab.AddComponent<tosaveitemscript>();
            saved.id = 0;
            saved.category = 37002;
            if (saved.category != 37002)
                throw new System.InvalidOperationException("Stable item category did not round trip.");
            var returned = itemdatabase.d.ReturnItem(37002, 0);
            if (returned != prefab)
                throw new System.InvalidOperationException("Stable category lookup did not return the registered prefab.");
            int id = saved.id;
            if (id < 4000 || itemdatabase.d.ReturnItem(id, 0) != prefab)
                throw new System.InvalidOperationException("The category prefab was not registered in itemdatabase (id " + id + ").");
        }
        finally { Object.Destroy(prefab); }
        Debug.Log("[RW stable API] item category passed.");
    }

    public override void Update()
    {
        if (!pendingSaveCheck || Time.timeScale <= 0f) return;
        var main = mainscript.M;
        var save = savedatascript.d;
        if (main == null || main.player == null || save == null || save.data == null) return;
        var records = save.data.nearStuff;
        if (records == null || records.Count == 0) return;
        if (!save.buildings.Exists(building => building != null && building.startHouse)) return;
        pendingSaveCheck = false;
        VerifyPartAdapters();
        VerifyChangedOverloads(main, save);
        VerifyWorldAndComponentAdapters(main, save);
        VerifyTerrainMarkers(main);
        VerifyTerrainHoles(main);
        Debug.Log("[RW stable API] building adapter passed.");
        var book = SpawnSavedPrefab(save, main.player.transform.position + Vector3.up * 2f);
        if (book == null || !book.name.StartsWith("Book", System.StringComparison.OrdinalIgnoreCase))
            throw new System.InvalidOperationException("Stable save spawn did not return the stock book.");
        StartJointRecovery(book);
        VerifyHarmonyAdapters(save);
        VerifyRemoveFromMemory(save);
        VerifyLegacyPersistence(save);
        VerifyItemCategory();
        Debug.Log("[RW stable API] save adapter passed: " + records.Count + " records, constructor and book spawn.");
    }

    public override void OnLoad()
    {
        var main = mainscript.M;
        if (main == null) return;
        if (mainmenuscript.mainmenu.Settings == null)
            throw new System.InvalidOperationException("Stable menu Settings did not resolve to the beta settings.");
        if (main.load || main.loaded || main.DFMS != null)
            throw new System.InvalidOperationException("A new world exposed the stable save-load flags.");
        var distance = main.DDS;
        var nativeSettings = typeof(settingsscript).GetField("S").GetValue(mainmenuscript.mainmenu.Settings);
        var nativeDistance = nativeSettings.GetType().GetField("FDrawDistance");
        float originalDistance = distance.drawDistance;
        try
        {
            distance.drawDistance = originalDistance + 50;
            if (!object.ReferenceEquals(distance, main.DDS) ||
                (float)nativeDistance.GetValue(nativeSettings) != originalDistance + 50)
                throw new System.InvalidOperationException("Stable draw distance did not update the beta setting.");
        }
        finally
        {
            distance.drawDistance = originalDistance;
            nativeDistance.SetValue(nativeSettings, originalDistance);
        }
        pendingSaveCheck = true;
        bool wasGod = main.godmode;
        main.ChGodMode(!wasGod);
        main.godmode = wasGod;
        bool chat = main.chatWasOpen;
        main.chatWasOpen = !chat;
        main.TankInc();
        main.HpCh(1f);
        if (!main.load) main.Spawn(new GameObject("Roadworthy stable API probe"));
        if (main.player != null) main.player.RightHandRelease();
        if (main.mainWorld != null)
        {
            doublecoord origin = main.mainWorld.coord;
            Debug.Log("[RW stable API] world origin " + origin.x + "," + origin.y + "," + origin.z);
        }
        var generator = TerrainGenerator.TG;
        var roadScript = savedatascript.d != null ? savedatascript.d.mainRoadScript : null;
        if (generator != null && roadScript != null && roadScript.roads != null && roadScript.roads.Length > 0)
            Debug.Log("[RW stable API] roads: " + roadScript.roads.Length + ", first at " + roadScript.roads[0].posX + ", segment " + roadScript.roadSegmentLength);
        Debug.Log("[RW stable API] ran: godmode " + main.godmode);
    }
}

namespace TheLongWho.Harmony
{
    [HarmonyPatch(typeof(buildingscript), "SpawnStuff")]
    public static class Patch_Building_SpawnStuff
    {
        public static int Calls;
        public static int BuildingId;

        public static void Prefix(buildingscript __instance)
        {
            if (__instance.itemsSpawned)
                throw new System.InvalidOperationException("Stable building event ran after items were already spawned.");
            Calls++;
            BuildingId = __instance.tipus;
        }
    }
}

namespace Amt
{
    [HarmonyPatch(typeof(savedatascript), "RefreshData")]
    public static class PrepareSaveDataPatch
    {
        public static int Calls;
        public static void Postfix() { Calls++; }
    }

    [HarmonyPatch(typeof(newSaveScreenScript), "Save")]
    public static class SavePatch
    {
        public static string Path;
        public static void Postfix(string ___saveDataPreName, string ___savedName) { Path = ___saveDataPreName + ___savedName; }
    }
}

[HarmonyPatch(typeof(savedatascript), "AddToStuff")]
public static class AskJimmyEquippedAccessoryVanillaSaveFilterPatch
{
    public static int Calls;
    public static bool Prefix(tosaveitemscript _tosave, System.Collections.Generic.Dictionary<int, save_prefab> _dict)
    {
        if (_tosave != null) Calls++;
        return true;
    }
}

namespace WorldTweaker.Harmony
{
    [HarmonyPatch(typeof(TerrainGenerator), "GetTerrainHeight", new[] { typeof(Vector2), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class GetTerrainHeight_Postfix
    {
        public static bool Enabled;
        public static void Postfix(ref float __result) { if (Enabled) __result += 2; }
    }

    [HarmonyPatch(typeof(TerrainGenerator), "GetTerrainHeightWorld", new[] { typeof(Vector2), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class GetTerrainHeightWorld_Postfix
    {
        public static bool Enabled;
        public static void Postfix(ref float __result) { if (Enabled) __result += 3; }
    }

    [HarmonyPatch(typeof(mainmenuscript), "PressedLoadScene")]
    public static class Patch_MainMenuScript_FStart
    {
        public static bool Prefix(mainmenuscript __instance, string s) { return __instance != null; }
    }

    [HarmonyPatch(typeof(itemdatabase), "ReturnItem")]
    public static class Patch_itemdatabase_ReturnItem
    {
        public static GameObject Marker;
        public static void Postfix(itemdatabase __instance, ref GameObject __result, int category, int id)
        {
            if (category == 37001 && id == 0) __result = Marker = new GameObject("Legacy Harmony item fixture");
        }
    }
}

