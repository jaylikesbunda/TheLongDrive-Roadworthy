using System;
using System.Collections.Generic;
using TldMp.Experimental;

// Uses only detached native save-record objects. No scene or save file is used.
internal static class ExperimentalSavedIdsGameTests
{
    static int assertions;
    static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception("Native SavedIds invariant failed: " + message);
    }

    static itemDataClass NewGroup(uint first, uint second)
    {
        var data = new itemDataClass();
        data.items.Add(new save_item { id = first });
        data.items.Add(new save_item { id = second });
        return data;
    }

    public static int Main()
    {
        Dictionary<uint, uint> map;
        var data = NewGroup(7, 12);
        Check(SavedIds.TryPlan(data, 20, 21, delegate(uint id) { return false; }, out map), "valid native save items plan");
        Check(map.Count == 2 && map[7] == 20 && map[12] == 21, "stable contiguous native ID map");
        Check(data.items[0].id == 7 && data.items[1].id == 12, "planning leaves native save_item IDs untouched");

        data.attachable.Add(new save_attachable {
            id = 12,
            attachType = (int)save_attachable.attachTypeEmu.toItem,
            parentid = 7
        });
        data.physLocks.Add(new save_physicslock {
            id = 7,
            IDs = new uint[] { 12 },
            poss = new vector3[] { new vector3() },
            rots = new vector3[] { new vector3() }
        });
        Check(SavedIds.ItemReferencesWithinGroup(data, map), "native attachment and physics-lock references are closed");
        Check(SavedIds.ComponentOwnersWithinGroup(data, map), "native component-owner lists accept group IDs");
        Check(data.attachable[0].id == 12 && data.attachable[0].parentid == 7 &&
            data.physLocks[0].id == 7 && data.physLocks[0].IDs[0] == 12, "reference checks do not remap records");

        data.attachable[0].attachType = (int)save_attachable.attachTypeEmu.toPOI;
        data.attachable[0].parentid = 900;
        Check(SavedIds.ItemReferencesWithinGroup(data, map), "external native POI parent remains valid when outside group IDs");
        data.attachable[0].parentid = 7;
        Check(!SavedIds.ItemReferencesWithinGroup(data, map), "non-item parent colliding with a group ID is rejected before native remap");
        data.attachable[0].attachType = (int)save_attachable.attachTypeEmu.toItem;
        data.attachable[0].parentid = 7;

        data.car.Add(new save_carscript { id = 99 });
        Check(!SavedIds.ComponentOwnersWithinGroup(data, map), "unmapped native car component owner rejected");
        data.car.Clear();

        data.attachable[0].parentid = 99;
        Check(!SavedIds.ItemReferencesWithinGroup(data, map), "missing native toItem parent rejected");
        data.attachable[0].parentid = 7;
        data.physLocks[0].IDs = new uint[] { 99 };
        Check(!SavedIds.ItemReferencesWithinGroup(data, map), "missing native physics-lock item reference rejected");
        data.physLocks[0].IDs = null;
        Check(!SavedIds.ItemReferencesWithinGroup(data, map), "null native physics-lock ID array rejected");

        var duplicate = NewGroup(7, 7);
        Check(!SavedIds.TryPlan(duplicate, 20, 21, delegate(uint id) { return false; }, out map) && map == null,
            "duplicate native saved IDs rejected");
        var zero = NewGroup(0, 12);
        Check(!SavedIds.TryPlan(zero, 20, 21, delegate(uint id) { return false; }, out map) && map == null,
            "zero native saved ID rejected");
        var occupied = NewGroup(7, 12);
        Check(!SavedIds.TryPlan(occupied, 20, 21, delegate(uint id) { return id == 21; }, out map) && map == null,
            "occupied target ID rejected");
        var shortRange = NewGroup(7, 12);
        Check(!SavedIds.TryPlan(shortRange, 20, 20, delegate(uint id) { return false; }, out map) && map == null,
            "too-small target range rejected");

        var remapped = NewGroup(7, 12);
        remapped.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem });
        remapped.physLocks.Add(new save_physicslock { id = 7, IDs = new uint[] { 12 }, poss = new vector3[1], rots = new vector3[1] });
        Check(SavedIds.TryValidateIds(remapped, 20, 21, delegate(uint id) { return false; }, out map),
            "detached graph passes preflight");
        savedatascript.RearrangeID(remapped, 20);
        Check(remapped.items[0].id == 20 && remapped.items[1].id == 21 &&
            remapped.attachable[0].id == 21 && remapped.attachable[0].parentid == 20 &&
            remapped.physLocks[0].id == 20 && remapped.physLocks[0].IDs[0] == 21,
            "native remapper updates detached item, attachment and storage IDs");
        var original = NewGroup(7, 12);
        original.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem });
        original.physLocks.Add(new save_physicslock { id = 7, IDs = new uint[] { 12 }, poss = new vector3[1], rots = new vector3[1] });
        var copy = new itemDataClass(); copy.Copy(original);
        Check(!object.ReferenceEquals(copy.items[0], original.items[0]) && !object.ReferenceEquals(copy.physLocks[0].IDs, original.physLocks[0].IDs), "native clone separates item and storage array instances");
        savedatascript.RearrangeID(copy, 20);
        Check(original.items[0].id == 7 && original.attachable[0].parentid == 7 && original.physLocks[0].IDs[0] == 12,
            "remapping the detached copy leaves original graph unchanged");
        itemDataClass prepared;
        Check(DetachedRemap.TryPrepare(original, 20, 21, delegate(uint id) { return false; }, out prepared), "isolated detached remap prepared");
        Check(prepared.items[0].id == 20 && prepared.attachable[0].parentid == 20 && prepared.physLocks[0].IDs[0] == 21 &&
            original.items[0].id == 7 && original.physLocks[0].IDs[0] == 12, "prepared copy remaps references without changing source");
        var emptyLock = NewGroup(7, 12);
        emptyLock.physLocks.Add(new save_physicslock { id = 7, IDs = new uint[0], poss = new vector3[0], rots = new vector3[0] });
        Check(!SavedIds.TryValidateIds(emptyLock, 20, 21, delegate(uint id) { return false; }, out map), "preflight rejects empty lock that native copy would normalize");
        Check(!DetachedRemap.TryPrepare(emptyLock, 20, 21, delegate(uint id) { return false; }, out prepared) && prepared == null,
            "invalid detached copy is never published");
        var weaponGroup = NewGroup(7, 12);
        weaponGroup.weapon.Add(new save_weapon { id = 7 });
        Check(DetachedRemap.TryPrepare(weaponGroup, 20, 21, delegate(uint id) { return false; }, out prepared) &&
            prepared.weapon[0].bbgun == null && prepared.weapon[0].revolver == null,
            "detached remap preserves absent weapon components");
        var weaponCopy = weaponGroup.weapon[0].Copy();
        Check(weaponGroup.weapon[0].revolver == null && weaponCopy.revolver != null && weaponCopy.revolver.bullets == null,
            "native weapon copy creates a revolver record from a null source");
        weaponGroup.weapon[0].revolver = new save_revolver { id = 7, bullets = new bool[] { true } };
        Check(DetachedRemap.TryPrepare(weaponGroup, 20, 21, delegate(uint id) { return false; }, out prepared) &&
            prepared.weapon[0].revolver.id == 20 && prepared.weapon[0].revolver.bullets[0] &&
            weaponGroup.weapon[0].revolver.id == 7, "detached remap preserves ammunition and rekeys nested revolver");
        weaponGroup.weapon[0].revolver.bullets = new bool[0];
        Check(!DetachedRemap.TryPrepare(weaponGroup, 20, 21, delegate(uint id) { return false; }, out prepared),
            "empty revolver ammunition is rejected before native copy");
        weaponGroup.weapon[0].revolver.bullets = new bool[] { true };
        weaponGroup.weapon[0].revolver.id = 12;
        Check(!DetachedRemap.TryPrepare(weaponGroup, 20, 21, delegate(uint id) { return false; }, out prepared),
            "mismatched nested weapon owner rejected");
        weaponGroup.weapon[0].revolver.id = 7;
        var weaponDetached = new itemDataClass(); weaponDetached.Copy(weaponGroup);
        savedatascript.RearrangeID(weaponDetached, 20);
        Check(weaponDetached.weapon[0].id == 20 && weaponDetached.weapon[0].revolver.id == 7,
            "native remapper leaves embedded revolver ID unchanged");
        var conditionGroup = NewGroup(7, 12);
        conditionGroup.partconditions.Add(new save_partcondition { id = 7, color = null });
        Check(!DetachedRemap.TryPrepare(conditionGroup, 20, 21, delegate(uint id) { return false; }, out prepared),
            "part-condition with absent nested color is rejected");
        Check(conditionGroup.partconditions[0].Copy().color != null, "native part-condition copy creates a color from null");
        conditionGroup.partconditions[0].color = new save_color { id = 7, ind = 0, r = 0.75f, a = 1f };
        Check(DetachedRemap.TryPrepare(conditionGroup, 20, 21, delegate(uint id) { return false; }, out prepared), "valid nested part color accepted");
        Check(prepared.partconditions[0].id == 20 && prepared.partconditions[0].color.id == 20 &&
            prepared.partconditions[0].color.r == 0.75f && conditionGroup.partconditions[0].color.id == 7,
            "embedded part color ID remapped and source preserved");
        conditionGroup.partconditions[0].color.id = 12;
        Check(!DetachedRemap.TryPrepare(conditionGroup, 20, 21, delegate(uint id) { return false; }, out prepared), "part color owner mismatch rejected");
        conditionGroup.partconditions[0].color.id = 7; conditionGroup.partconditions[0].color.ind = 1;
        Check(!DetachedRemap.TryPrepare(conditionGroup, 20, 21, delegate(uint id) { return false; }, out prepared), "part color slot mismatch rejected");
        var instruments = NewGroup(7, 12);
        instruments.meter.Add(new save_meterscript { id = 7,
            mutatok = new save_meterscript.save_meter[] { new save_meterscript.save_meter { id = 7, tipus = "speed", value = 42 } },
            szamlapok = new save_meterscript.save_meter[0] });
        instruments.outfit.Add(new save_outfit { id = 12, segments = new save_outfitsegment[0] });
        Check(DetachedRemap.TryPrepare(instruments, 20, 21, delegate(uint id) { return false; }, out prepared), "valid nested meter and outfit records accepted");
        Check(prepared.meter[0].id == 20 && prepared.meter[0].mutatok[0].id == 20 && prepared.meter[0].mutatok[0].value == 42 &&
            prepared.meter[0].szamlapok != null && prepared.meter[0].szamlapok.Length == 0 &&
            prepared.outfit[0].segments != null && prepared.outfit[0].segments.Length == 0 && instruments.meter[0].mutatok[0].id == 7,
            "nested meter owner remapped and empty arrays preserved without modifying source");
        instruments.meter[0].szamlapok = new save_meterscript.save_meter[] { new save_meterscript.save_meter { id = 0, value = 3 } };
        Check(DetachedRemap.TryPrepare(instruments, 20, 21, delegate(uint id) { return false; }, out prepared) &&
            prepared.meter[0].szamlapok[0].id == 0 && prepared.meter[0].szamlapok[0].value == 3,
            "ownerless meter dial stays ownerless");
        instruments.meter[0].mutatok[0].id = 999;
        Check(!DetachedRemap.TryPrepare(instruments, 20, 21, delegate(uint id) { return false; }, out prepared), "foreign meter owner rejected");
        instruments.meter[0].mutatok[0] = null;
        Check(!DetachedRemap.TryPrepare(instruments, 20, 21, delegate(uint id) { return false; }, out prepared), "null meter entry rejected");
        instruments.meter[0].mutatok = null; instruments.outfit[0].segments = new save_outfitsegment[] { null };
        Check(!DetachedRemap.TryPrepare(instruments, 20, 21, delegate(uint id) { return false; }, out prepared), "null outfit segment rejected");
        var tankGroup = NewGroup(7, 12);
        tankGroup.tanks.Add(new save_tankscript { id = 7, amount = new float[] { 2 }, type = new int[0] });
        Check(!DetachedRemap.TryPrepare(tankGroup, 20, 21, delegate(uint id) { return false; }, out prepared), "mismatched tank arrays rejected");
        tankGroup.tanks[0].type = new int[] { 1 };
        Check(DetachedRemap.TryPrepare(tankGroup, 20, 21, delegate(uint id) { return false; }, out prepared) &&
            prepared.tanks[0].id == 20 && prepared.tanks[0].amount[0] == 2 && prepared.tanks[0].type[0] == 1,
            "valid tank contents survive detached remap");
        var carGroup = NewGroup(7, 12);
        carGroup.car.Add(new save_carscript { id = 7, steer = 0.25f, ignition = true, gear = 3, distance = 123, blood = true });
        carGroup.engine.Add(new save_engine { id = 7, rpm = 1200, temp = 70 });
        carGroup.tanks.Add(new save_tankscript { id = 7, amount = new float[] { 2 }, type = new int[] { 1 } });
        carGroup.partconditions.Add(new save_partcondition { id = 12, ind = 0, state = 3, color = new save_color { id = 12, ind = 0, r = 0.5f, a = 1 } });
        Check(DetachedRemap.TryPrepare(carGroup, 20, 21, delegate(uint id) { return false; }, out prepared), "vehicle component fixture accepted");
        Check(prepared.car[0].id == 20 && prepared.car[0].steer == 0.25f && prepared.car[0].ignition && prepared.car[0].gear == 3 && prepared.car[0].blood &&
            prepared.engine[0].rpm == 1200 && prepared.engine[0].temp == 70 && prepared.tanks[0].amount[0] == 2 &&
            prepared.partconditions[0].id == 21 && prepared.partconditions[0].color.id == 21 && prepared.partconditions[0].color.r == 0.5f,
            "vehicle component fixture retains state and remapped identities");
        var shapes = NewGroup(7, 12);
        shapes.ragdollpos.Add(new save_ragdollpos { id = 7, childs = new posRotd[0] });
        shapes.busdoorscript.Add(new save_busdoors { id = 7, Bopened = new bool[0], Bsigned = new bool[0] });
        shapes.randomScales.Add(new save_randomScale { id = 7, v = new vector3[0] });
        Check(DetachedRemap.TryPrepare(shapes, 20, 21, delegate(uint id) { return false; }, out prepared) &&
            prepared.ragdollpos[0].childs != null && prepared.ragdollpos[0].childs.Length == 0 &&
            prepared.busdoorscript[0].Bopened != null && prepared.busdoorscript[0].Bsigned != null &&
            prepared.randomScales[0].v != null, "empty native arrays stay non-null on detached copy");
        shapes.busdoorscript[0].Bsigned = new bool[1];
        Check(!DetachedRemap.TryPrepare(shapes, 20, 21, delegate(uint id) { return false; }, out prepared), "mismatched bus door arrays rejected");
        shapes.busdoorscript[0].Bsigned = new bool[0]; shapes.ragdollpos[0].childs = null;
        Check(!DetachedRemap.TryPrepare(shapes, 20, 21, delegate(uint id) { return false; }, out prepared), "null ragdoll positions rejected");
        shapes.ragdollpos[0].childs = new posRotd[0]; shapes.randomScales[0].v = null;
        Check(!DetachedRemap.TryPrepare(shapes, 20, 21, delegate(uint id) { return false; }, out prepared), "null random scales rejected before prefab validation");
        var emptyCopy = new itemDataClass(); emptyCopy.Copy(emptyLock);
        Check(emptyCopy.physLocks[0].IDs == null, "native copy normalizes empty storage IDs to null");
        var collision = NewGroup(7, 12);
        collision.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toPOI });
        Check(SavedIds.TryPlan(collision, 20, 21, delegate(uint id) { return false; }, out map) &&
            !SavedIds.ItemReferencesWithinGroup(collision, map), "preflight rejects non-item parent collision");
        savedatascript.RearrangeID(collision, 20);
        Check(collision.attachable[0].parentid == 20, "native remapper rewrites colliding non-item parent ID");
        var covered = new HashSet<string>(new string[] { "items", "partconditions", "ammo", "weapon", "tanks", "usable", "meter", "car", "engine", "attachable", "nyul", "ragdollpos", "busdoorscript", "colors", "slideOnCollider", "rendszam", "randoms", "door_rots", "ginlamp", "wear", "food", "outfit", "randomScales", "newai", "painting", "physLocks" });
        foreach (var field in typeof(itemDataClass).GetFields())
        {
            Type type = field.FieldType;
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(List<>)) continue;
            Type element = type.GetGenericArguments()[0];
            if (typeof(iid).IsAssignableFrom(element) || typeof(iid2).IsAssignableFrom(element))
                Check(covered.Remove(field.Name), "unexpected native save component list " + field.Name);
        }
        Check(covered.Count == 0, "expected native component lists remain present");
        var allComponents = NewGroup(7, 12);
        foreach (var field in typeof(itemDataClass).GetFields())
        {
            Type listType = field.FieldType;
            if (field.Name == "items" || !listType.IsGenericType || listType.GetGenericTypeDefinition() != typeof(List<>)) continue;
            Type element = listType.GetGenericArguments()[0];
            if (!typeof(iid).IsAssignableFrom(element) && !typeof(iid2).IsAssignableFrom(element)) continue;
            object record = Activator.CreateInstance(element);
            if (record is iid) ((iid)record).id = 7; else ((iid2)record).id = 7;
            var storage = record as save_physicslock;
            if (storage != null) { storage.IDs = new uint[] { 12 }; storage.poss = new vector3[1]; storage.rots = new vector3[1]; }
            var part = record as save_partcondition;
            if (part != null) part.color = new save_color { id = part.id, ind = part.ind };
            var ragdoll = record as save_ragdollpos;
            if (ragdoll != null) ragdoll.childs = new posRotd[0];
            var doors = record as save_busdoors;
            if (doors != null) { doors.Bopened = new bool[0]; doors.Bsigned = new bool[0]; }
            var scale = record as save_randomScale;
            if (scale != null) scale.v = new vector3[0];
            ((System.Collections.IList)field.GetValue(allComponents)).Add(record);
        }
        Check(SavedIds.TryValidateIds(allComponents, 20, 21, delegate(uint id) { return false; }, out map), "all native component owner lists pass preflight");
        savedatascript.RearrangeID(allComponents, 20);
        foreach (var field in typeof(itemDataClass).GetFields())
        {
            Type listType = field.FieldType;
            if (field.Name == "items" || !listType.IsGenericType || listType.GetGenericTypeDefinition() != typeof(List<>)) continue;
            Type element = listType.GetGenericArguments()[0];
            if (!typeof(iid).IsAssignableFrom(element) && !typeof(iid2).IsAssignableFrom(element)) continue;
            object record = ((System.Collections.IList)field.GetValue(allComponents))[0];
            uint id = record is iid ? ((iid)record).id : ((iid2)record).id;
            Check(id == 20, "native remapper updates component " + field.Name);
        }

        // Nested owner IDs that native RearrangeID does not recurse into.
        var nested = NewGroup(7, 12);
        nested.weapon.Add(new save_weapon { id = 7, revolver = new save_revolver { id = 7, bullets = new bool[] { true } } });
        nested.partconditions.Add(new save_partcondition { id = 12, ind = 0, color = new save_color { id = 12, ind = 0 } });
        nested.meter.Add(new save_meterscript { id = 7,
            mutatok = new save_meterscript.save_meter[] { new save_meterscript.save_meter { id = 7 } },
            szamlapok = new save_meterscript.save_meter[0] });
        Check(SavedIds.NestedReferencesSafe(nested), "nested weapon, part colour and meter owners agree with the item");
        nested.weapon[0].revolver.id = 12;
        Check(!SavedIds.NestedReferencesSafe(nested), "nested revolver owner mismatch rejected");
        nested.weapon[0].revolver.id = 7;
        nested.meter[0].mutatok[0].id = 999;
        Check(!SavedIds.NestedReferencesSafe(nested), "foreign meter owner rejected");
        nested.meter[0].mutatok[0].id = 7;
        nested.partconditions[0].color.ind = 1;
        Check(!SavedIds.NestedReferencesSafe(nested), "part colour slot mismatch rejected");
        nested.partconditions[0].color.ind = 0;

        // Slot plausibility using the native slot indices.
        Check(SavedIds.SlotIndicesPlausible(nested), "unique native owner slots accepted");
        nested.tanks.Add(new save_tankscript { id = 7, ind = 0, amount = new float[] { 1 }, type = new int[] { 1 } });
        nested.tanks.Add(new save_tankscript { id = 7, ind = 0, amount = new float[] { 1 }, type = new int[] { 1 } });
        Check(!SavedIds.SlotIndicesPlausible(nested), "duplicate native tank slot rejected");
        nested.tanks[1].ind = 1;
        Check(SavedIds.SlotIndicesPlausible(nested), "distinct native tank slots accepted");
        nested.tanks[1].ind = -1;
        Check(!SavedIds.SlotIndicesPlausible(nested), "negative native tank slot rejected");
        nested.tanks[1].ind = 1;
        nested.randoms.Add(new save_randomType { id = 12, ind = 0 });
        nested.randoms.Add(new save_randomType { id = 12, ind = 0 });
        Check(!SavedIds.SlotIndicesPlausible(nested), "duplicate native random slot rejected");

        // Ambiguous and cyclic native attachment graphs.
        var cyclic = NewGroup(7, 12);
        cyclic.attachable.Add(new save_attachable { id = 7, parentid = 12, attachType = (int)save_attachable.attachTypeEmu.toItem });
        cyclic.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem });
        Check(SavedIds.TryPlan(cyclic, 20, 21, delegate(uint id) { return false; }, out map) &&
            !SavedIds.ItemReferencesWithinGroup(cyclic, map), "cyclic native attachment chain rejected");
        Check(!SavedIds.TryValidateIds(cyclic, 20, 21, delegate(uint id) { return false; }, out map) && map == null,
            "whole preflight rejects a cyclic native attachment chain");
        var selfParent = NewGroup(7, 12);
        selfParent.attachable.Add(new save_attachable { id = 12, parentid = 12, attachType = (int)save_attachable.attachTypeEmu.toItem });
        Dictionary<uint, uint> selfMap;
        Check(SavedIds.TryPlan(selfParent, 20, 21, delegate(uint id) { return false; }, out selfMap) &&
            !SavedIds.ItemReferencesWithinGroup(selfParent, selfMap), "self-parent native attachment rejected");
        var duplicateChild = NewGroup(7, 12);
        duplicateChild.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem });
        duplicateChild.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem });
        Check(SavedIds.TryPlan(duplicateChild, 20, 21, delegate(uint id) { return false; }, out map) &&
            !SavedIds.ItemReferencesWithinGroup(duplicateChild, map), "duplicate native attachment record rejected");
        var badIndex = NewGroup(7, 12);
        badIndex.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem,
            indexType = (int)save_attachable.indexTypeEmu.slot + 1 });
        Check(SavedIds.TryPlan(badIndex, 20, 21, delegate(uint id) { return false; }, out map) &&
            !SavedIds.ItemReferencesWithinGroup(badIndex, map), "out-of-enum attachment index type rejected");
        var badType = NewGroup(7, 12);
        badType.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toWorld + 1 });
        Check(SavedIds.TryPlan(badType, 20, 21, delegate(uint id) { return false; }, out map) &&
            !SavedIds.ItemReferencesWithinGroup(badType, map), "out-of-enum attachment type rejected");

        // Documented unverified surface stays explicit.
        Check(SavedIds.Unverified.Length > 0 && PrefabChecklist.Unverified.Length > 0, "unverified conditions are documented");

        // Prefab-aware checklist against the installed prefab database.
        var prefabs = PrefabChecklist.Load();
        Check(prefabs.Available && prefabs.KnownPrefabs > 100, "installed itemdatabase prefab enum is readable");
        Check(prefabs.IsItemPrefab(1) && !prefabs.IsItemPrefab(0) && !prefabs.IsItemPrefab(int.MaxValue),
            "prefab ids resolve through itemdatabase.i");
        Check(prefabs.IsVehiclePrefab(1) && !prefabs.IsVehiclePrefab(78) && prefabs.IsVehiclePrefab(173),
            "vehicle prefabs are classified by name");
        List<string> unverified;
        var prefabGroup = NewGroup(7, 12);
        prefabGroup.items[0].prefabID = 1;
        prefabGroup.items[1].prefabID = 78;
        prefabGroup.car.Add(new save_carscript { id = 7 });
        prefabGroup.tanks.Add(new save_tankscript { id = 7, amount = new float[] { 2 }, type = new int[] { 1 } });
        Check(prefabs.TryEvaluate(prefabGroup, out unverified) && unverified.Count == PrefabChecklist.Unverified.Length,
            "vehicle components on vehicle prefabs accepted");
        prefabGroup.busdoorscript.Add(new save_busdoors { id = 7, Bopened = new bool[0], Bsigned = new bool[0] });
        Check(!prefabs.TryEvaluate(prefabGroup, out unverified) && unverified != null, "bus door component on a car prefab rejected");
        prefabGroup.busdoorscript.Clear();
        prefabGroup.items[1].prefabID = 0;
        Check(!prefabs.TryEvaluate(prefabGroup, out unverified), "unset prefab id rejected");
        prefabGroup.items[1].prefabID = 999999;
        Check(!prefabs.TryEvaluate(prefabGroup, out unverified), "unknown prefab id rejected");
        prefabGroup.items[1].prefabID = 78;
        prefabGroup.car.Clear();
        prefabGroup.tanks.Clear();
        prefabGroup.car.Add(new save_carscript { id = 12 });
        Check(!prefabs.TryEvaluate(prefabGroup, out unverified), "car component on a non-vehicle prefab rejected");

        // Lease-routed detached remap with the prefab checklist.
        var heldIds = new HashSet<uint>();
        var heldTokens = new Dictionary<uint, Guid>();
        Func<uint, bool> isOccupied = delegate(uint id) { return heldIds.Contains(id); };
        Func<uint, uint, Guid, bool> tryReserve = delegate(uint first, uint count, Guid token)
        {
            for (uint id = first; id < first + count; id++) if (heldIds.Contains(id)) return false;
            for (uint id = first; id < first + count; id++) { heldIds.Add(id); heldTokens[id] = token; }
            return true;
        };
        Func<uint, uint, Guid, bool> tryRelease = delegate(uint first, uint count, Guid token)
        {
            Guid stored;
            if (!heldTokens.TryGetValue(first, out stored) || stored != token) return false;
            for (uint id = first; id < first + count; id++) { heldIds.Remove(id); heldTokens.Remove(id); }
            return true;
        };
        IdRangeLease lease;
        Check(IdRangeLease.TryAcquire(20, 2, isOccupied, tryReserve, tryRelease, out lease), "allocator-issued lease acquired");
        var leased = NewGroup(7, 12);
        itemDataClass leasedCopy;
        leased.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem });
        leased.physLocks.Add(new save_physicslock { id = 7, IDs = new uint[] { 12 }, poss = new vector3[1], rots = new vector3[1] });
        Check(DetachedRemap.TryPrepare(leased, lease, out leasedCopy), "lease-routed detached remap");
        Check(leasedCopy.items[0].id == 20 && leasedCopy.items[1].id == 21 && leasedCopy.physLocks[0].IDs[0] == 21,
            "lease-routed copy carries the reserved IDs");
        Check(!DetachedRemap.TryPrepare(leased, lease, prefabs, out leasedCopy), "prefab checklist fails closed on unset prefab ids");
        leased.items[0].prefabID = 1;
        leased.items[1].prefabID = 1;
        Check(DetachedRemap.TryPrepare(leased, lease, prefabs, out leasedCopy), "prefab-checked detached remap");
        Check(leasedCopy.items[1].id == 21 && leasedCopy.attachable[0].parentid == 20, "prefab-checked copy remaps references");
        lease.Dispose();
        Check(heldIds.Count == 0 && !DetachedRemap.TryPrepare(leased, lease, out leasedCopy) && leasedCopy == null,
            "released lease cannot prepare a detached copy");

        Console.WriteLine("PASS: " + assertions + " native saved-ID assertions using actual game record types; no game state or save file accessed.");
        return 0;
    }
}
