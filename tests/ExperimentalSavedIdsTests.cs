using System;
using System.Collections.Generic;
using TldMp.Experimental;

// Stubs of the native save record shapes. Field names mirror the installed game
// types so the offline suite exercises the same references as the real ones.
public interface iid { uint id { get; set; } }
public interface iid2 { uint id { get; set; } }
public struct vector3 { }
public struct posRotd { }
public class save_item : iid { public uint id { get; set; } public int prefabID; }
public class save_attachable : iid
{
    public enum attachTypeEmu { none, toItem, toPOI, toObj, toWorld }
    public enum indexTypeEmu { none, collider, slot }
    public uint id { get; set; }
    public uint parentid;
    public int attachType;
    public int indexType;
    public int index;
    public int poigenid;
}
public class save_color : iid2 { public uint id { get; set; } public int ind; }
public class save_partcondition : iid2 { public uint id { get; set; } public int ind; public save_color color; }
public class save_ammo : iid { public uint id { get; set; } public int count; }
public class save_bbgun : iid { public uint id { get; set; } }
public class save_revolver : iid { public uint id { get; set; } public bool[] bullets; }
public class save_weapon : iid { public uint id { get; set; } public save_bbgun bbgun; public save_revolver revolver; }
public class save_tankscript : iid2 { public uint id { get; set; } public int ind; public float[] amount; public int[] type; }
public class save_usablescript : iid2 { public uint id { get; set; } public int ind; }
public class save_meterscript : iid
{
    public class save_meter : iid { public uint id { get; set; } }
    public uint id { get; set; }
    public save_meter[] mutatok;
    public save_meter[] szamlapok;
}
public class save_carscript : iid { public uint id { get; set; } }
public class save_engine : iid { public uint id { get; set; } }
public class save_nyulscript : iid { public uint id { get; set; } }
public class save_ragdollpos : iid { public uint id { get; set; } public posRotd[] childs; }
public class save_busdoors : iid { public uint id { get; set; } public bool[] Bopened; public bool[] Bsigned; }
public class save_slideOnCollider : iid { public uint id { get; set; } }
public class save_rendszam : iid { public uint id { get; set; } public string S; }
public class save_randomType : iid2 { public uint id { get; set; } public int ind; }
public class save_door_rot : iid2 { public uint id { get; set; } public int ind; }
public class save_ginlamp : iid { public uint id { get; set; } }
public class save_wear : iid { public uint id { get; set; } }
public class save_food : iid { public uint id { get; set; } public int index; }
public class save_outfitsegment { }
public class save_outfit : iid { public uint id { get; set; } public save_outfitsegment[] segments; }
public class save_randomScale : iid2 { public uint id { get; set; } public int ind; public vector3[] v; }
public class save_newai : iid { public uint id { get; set; } }
public class save_painting : iid { public uint id { get; set; } public int index; }
public class save_physicslock : iid2 { public uint id { get; set; } public int ind; public uint[] IDs; public vector3[] poss, rots; }
public class itemDataClass
{
    public List<save_item> items = new List<save_item>();
    public List<save_partcondition> partconditions = new List<save_partcondition>();
    public List<save_ammo> ammo = new List<save_ammo>();
    public List<save_weapon> weapon = new List<save_weapon>();
    public List<save_tankscript> tanks = new List<save_tankscript>();
    public List<save_usablescript> usable = new List<save_usablescript>();
    public List<save_meterscript> meter = new List<save_meterscript>();
    public List<save_carscript> car = new List<save_carscript>();
    public List<save_engine> engine = new List<save_engine>();
    public List<save_attachable> attachable = new List<save_attachable>();
    public List<save_nyulscript> nyul = new List<save_nyulscript>();
    public List<save_ragdollpos> ragdollpos = new List<save_ragdollpos>();
    public List<save_busdoors> busdoorscript = new List<save_busdoors>();
    public List<save_color> colors = new List<save_color>();
    public List<save_slideOnCollider> slideOnCollider = new List<save_slideOnCollider>();
    public List<save_rendszam> rendszam = new List<save_rendszam>();
    public List<save_randomType> randoms = new List<save_randomType>();
    public List<save_door_rot> door_rots = new List<save_door_rot>();
    public List<save_ginlamp> ginlamp = new List<save_ginlamp>();
    public List<save_wear> wear = new List<save_wear>();
    public List<save_food> food = new List<save_food>();
    public List<save_outfit> outfit = new List<save_outfit>();
    public List<save_randomScale> randomScales = new List<save_randomScale>();
    public List<save_newai> newai = new List<save_newai>();
    public List<save_painting> painting = new List<save_painting>();
    public List<save_physicslock> physLocks = new List<save_physicslock>();
}

internal static class ExperimentalSavedIdsTests
{
    static int checks;
    static void Check(bool ok)
    {
        checks++;
        if (!ok) throw new Exception("Saved ID planning invariant failed at assertion " + checks);
    }
    static Func<uint, bool> Clear() { return delegate(uint id) { return false; }; }

    static itemDataClass Group(uint first, uint second)
    {
        var data = new itemDataClass();
        data.items.Add(new save_item { id = first });
        data.items.Add(new save_item { id = second });
        return data;
    }

    static itemDataClass Graph()
    {
        var data = Group(7, 12);
        data.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem });
        data.physLocks.Add(new save_physicslock { id = 7, IDs = new uint[] { 12 }, poss = new vector3[1], rots = new vector3[1] });
        return data;
    }

    public static int Main()
    {
        Dictionary<uint, uint> map;
        var data = Graph();
        Check(SavedIds.TryPlan(data, 20, 21, Clear(), out map));
        Check(map[7] == 20 && map[12] == 21 && data.items[0].id == 7 && data.items[1].id == 12);
        Check(SavedIds.ItemReferencesWithinGroup(data, map));
        Check(SavedIds.StorageArraysSafe(data));
        Check(SavedIds.NestedReferencesSafe(data));
        Check(SavedIds.SlotIndicesPlausible(data));
        Check(SavedIds.TryValidateIds(data, 20, 21, Clear(), out map) && map[7] == 20);
        Check(SavedIds.Unverified.Length > 0);

        // Storage arrays must keep IDs/poss/rots aligned and non-empty.
        data.physLocks[0].rots = new vector3[0]; Check(!SavedIds.StorageArraysSafe(data));
        data.physLocks[0].rots = new vector3[1];
        data.physLocks[0].IDs = new uint[0]; data.physLocks[0].poss = new vector3[0]; data.physLocks[0].rots = new vector3[0];
        Check(!SavedIds.StorageArraysSafe(data));
        Check(!SavedIds.TryValidateIds(data, 20, 21, Clear(), out map) && map == null);
        data.physLocks[0].IDs = new uint[] { 12 }; data.physLocks[0].poss = new vector3[1]; data.physLocks[0].rots = new vector3[1];
        Check(SavedIds.TryPlan(data, 20, 21, Clear(), out map));

        // Component owners must be group IDs.
        Check(SavedIds.ComponentOwnersWithinGroup(data, map));
        data.car.Add(new save_carscript { id = 999 });
        Check(!SavedIds.ComponentOwnersWithinGroup(data, map));
        data.car.Clear(); data.usable = null;
        Check(!SavedIds.ComponentOwnersWithinGroup(data, map));
        data.usable = new List<save_usablescript>();

        // References outside the group are rejected, except explicit externals.
        data.physLocks[0].IDs = new uint[] { 999 };
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.physLocks[0].IDs = null;
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.physLocks[0].IDs = new uint[] { 12 };
        data.attachable[0].parentid = 999;
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.attachable[0].attachType = (int)save_attachable.attachTypeEmu.none;
        data.attachable[0].parentid = 7;
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.attachable[0].attachType = (int)save_attachable.attachTypeEmu.toItem;
        data.attachable[0].parentid = 7;

        // Attachment record types and indices come from the native enums.
        data.attachable[0].attachType = (int)save_attachable.attachTypeEmu.toWorld + 1;
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.attachable[0].attachType = (int)save_attachable.attachTypeEmu.toItem;
        data.attachable[0].indexType = (int)save_attachable.indexTypeEmu.slot + 1;
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.attachable[0].indexType = (int)save_attachable.indexTypeEmu.collider;
        data.attachable[0].index = -1;
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.attachable[0].index = 0;
        Check(SavedIds.ItemReferencesWithinGroup(data, map));

        // Ambiguous and cyclic attachment graphs are rejected.
        data.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem });
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.attachable.RemoveAt(1);
        data.attachable[0].parentid = 12;
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.attachable[0].parentid = 7;
        data.attachable.Clear();
        data.attachable.Add(new save_attachable { id = 7, parentid = 12, attachType = (int)save_attachable.attachTypeEmu.toItem });
        data.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem });
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        Check(!SavedIds.TryValidateIds(data, 20, 21, Clear(), out map) && map == null);
        data.attachable.Clear();
        data.attachable.Add(new save_attachable { id = 12, parentid = 7, attachType = (int)save_attachable.attachTypeEmu.toItem });
        Check(SavedIds.TryPlan(data, 20, 21, Clear(), out map));

        // Storage ambiguity: no self-reference, no child claimed twice.
        data.physLocks[0].IDs = new uint[] { 7 };
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.physLocks[0].IDs = new uint[] { 12 };
        data.physLocks.Add(new save_physicslock { id = 7, ind = 1, IDs = new uint[] { 12 }, poss = new vector3[1], rots = new vector3[1] });
        Check(!SavedIds.ItemReferencesWithinGroup(data, map));
        data.physLocks.RemoveAt(1);

        // Slot plausibility: duplicate owner/slot pairs and negative slots.
        Check(SavedIds.SlotIndicesPlausible(data));
        data.partconditions.Add(new save_partcondition { id = 12, ind = 0, color = new save_color { id = 12, ind = 0 } });
        data.partconditions.Add(new save_partcondition { id = 12, ind = 0, color = new save_color { id = 12, ind = 0 } });
        Check(!SavedIds.SlotIndicesPlausible(data));
        data.partconditions[1].ind = 1;
        data.partconditions[1].color.ind = 1;
        Check(SavedIds.SlotIndicesPlausible(data));
        data.partconditions[1].ind = -1;
        Check(!SavedIds.SlotIndicesPlausible(data));
        data.partconditions.RemoveAt(1);
        data.car.Add(new save_carscript { id = 7 });
        data.car.Add(new save_carscript { id = 7 });
        Check(!SavedIds.SlotIndicesPlausible(data));
        data.car.RemoveAt(1);

        // Nested records that RearrangeID does not recurse into.
        data.weapon.Add(new save_weapon { id = 7 });
        Check(SavedIds.NestedReferencesSafe(data));
        data.weapon[0].revolver = new save_revolver { id = 12 };
        Check(!SavedIds.NestedReferencesSafe(data));
        data.weapon[0].revolver.id = 7;
        Check(SavedIds.NestedReferencesSafe(data));
        data.weapon[0].bbgun = new save_bbgun { id = 12 };
        Check(!SavedIds.NestedReferencesSafe(data));
        data.weapon[0].bbgun = null;
        data.partconditions[0].color.id = 7;
        Check(!SavedIds.NestedReferencesSafe(data));
        data.partconditions[0].color.id = 12; data.partconditions[0].color.ind = 1;
        Check(!SavedIds.NestedReferencesSafe(data));
        data.partconditions[0].color.ind = 0;
        Check(SavedIds.NestedReferencesSafe(data));
        data.partconditions[0].color = null;
        Check(!SavedIds.NestedReferencesSafe(data));
        data.partconditions.Clear();
        data.meter.Add(new save_meterscript { id = 7, mutatok = new save_meterscript.save_meter[] { new save_meterscript.save_meter { id = 7 } }, szamlapok = new save_meterscript.save_meter[0] });
        Check(SavedIds.NestedReferencesSafe(data));
        data.meter[0].mutatok[0].id = 999;
        Check(!SavedIds.NestedReferencesSafe(data));
        data.meter[0].mutatok[0].id = 7;
        data.meter[0].mutatok[0] = null;
        Check(!SavedIds.NestedReferencesSafe(data));
        data.meter.Clear();
        data.tanks.Add(new save_tankscript { id = 7, amount = new float[] { 2 }, type = new int[0] });
        Check(!SavedIds.NestedReferencesSafe(data));
        data.tanks[0].type = new int[] { 1 };
        Check(SavedIds.NestedReferencesSafe(data));
        data.tanks.Clear();
        data.ragdollpos.Add(new save_ragdollpos { id = 7 });
        Check(!SavedIds.NestedReferencesSafe(data));
        data.ragdollpos[0].childs = new posRotd[0];
        Check(SavedIds.NestedReferencesSafe(data));
        data.ragdollpos.Clear();
        data.busdoorscript.Add(new save_busdoors { id = 7, Bopened = new bool[0], Bsigned = new bool[1] });
        Check(!SavedIds.NestedReferencesSafe(data));
        data.busdoorscript.Clear();
        data.randomScales.Add(new save_randomScale { id = 7 });
        Check(!SavedIds.NestedReferencesSafe(data));
        data.randomScales.Clear();
        data.outfit.Add(new save_outfit { id = 7, segments = new save_outfitsegment[] { null } });
        Check(!SavedIds.NestedReferencesSafe(data));
        data.outfit.Clear();
        Check(SavedIds.TryValidateIds(data, 20, 21, Clear(), out map));

        // Prefab-aware checklist fails closed without the installed database.
        var prefab = PrefabChecklist.Load();
        Check(!prefab.Available && prefab.KnownPrefabs == 0);
        Check(!prefab.IsItemPrefab(1) && !prefab.IsVehiclePrefab(1));
        List<string> unverified;
        Check(!prefab.TryEvaluate(data, out unverified) && unverified != null && unverified.Count > PrefabChecklist.Unverified.Length);
        Check(PrefabChecklist.Unverified.Length > 0);
        Check(PrefabChecklist.IsVehicleName("car01") && PrefabChecklist.IsVehicleName("bus02Tank") &&
            !PrefabChecklist.IsVehicleName("bbgun") && PrefabChecklist.IsBusName("bus02") && !PrefabChecklist.IsBusName("car01") &&
            PrefabChecklist.IsTankName("bus02Tank") && !PrefabChecklist.IsTankName("bbgun"));

        data.items[1] = null;
        Check(!SavedIds.TryPlan(data, 20, 21, Clear(), out map) && map == null);
        Console.WriteLine("PASS: native saved ID planning is read-only");
        return 0;
    }
}
