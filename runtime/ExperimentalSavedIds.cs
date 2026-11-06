using System;
using System.Collections.Generic;
using System.Reflection;

namespace TldMp.Experimental
{
    // Reads native item definitions only. It does not prove prefab semantics,
    // reserve the range, remap references or invoke RearrangeID.
    internal static class SavedIds
    {
        // Development limit; revise only with measured real vehicle groups.
        internal const int MaxGroupItems = 4096;
        // Native save records address prefab colliders/slots/storage slots by
        // index; negative or absurd values cannot be a real prefab address.
        internal const int MaxSlotIndex = 1 << 10;

        // Conditions this offline preflight cannot decide from detached records.
        // Callers must not infer them from a successful TryValidateIds.
        internal static readonly string[] Unverified = new string[]
        {
            "save_attachable.index vs recipient prefab collider/slot count",
            "save_attachable.poigenid/poiid external POI identity",
            "save_physicslock.ind vs recipient mount storage capacity",
            "save_color.ind vs prefab part colour slot assignment",
            "save_meter.mutatok/szamlapok tipus vs prefab dials",
            "save_wear.player and save_food.index wear/food semantics",
            "item both attached and stored in one group",
            "live savedatascript.items/itemChunks keys and component IDs",
            "all non-ID scalar state (transforms, buried, states, amounts)"
        };

        internal static bool TryPlan(itemDataClass data, uint first, uint last, Func<uint, bool> occupied,
            out Dictionary<uint, uint> map)
        {
            map = null;
            if (data == null || data.items == null || data.items.Count == 0 || data.items.Count > MaxGroupItems) return false;
            var ids = new uint[data.items.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                if (data.items[i] == null) return false;
                ids[i] = data.items[i].id;
            }
            return IdRangePreflight.TryMap(ids, first, last, occupied, out map);
        }

        // A convenience preflight, not a transaction or reservation. Caller must
        // exclusively own the detached data until the range has been reserved
        // and the subsequent native remap completes.
        internal static bool TryValidateIds(itemDataClass data, uint first, uint last, Func<uint, bool> occupied,
            out Dictionary<uint, uint> map)
        {
            map = null;
            Dictionary<uint, uint> candidate;
            if (!TryPlan(data, first, last, occupied, out candidate) ||
                !StorageArraysSafe(data) || !ItemReferencesWithinGroup(data, candidate) ||
                !ComponentOwnersWithinGroup(data, candidate) || !NestedReferencesSafe(data) ||
                !SlotIndicesPlausible(data)) return false;
            map = candidate;
            return true;
        }

        // Native Load indexes all arrays by IDs.Length. Native Copy normalizes
        // empty arrays to null, which RearrangeID cannot handle; reject empty
        // lock records until a detached normalization policy is implemented.
        internal static bool StorageArraysSafe(itemDataClass data)
        {
            if (data == null || data.physLocks == null) return false;
            foreach (var storage in data.physLocks)
                if (storage == null || storage.IDs == null || storage.IDs.Length == 0 || storage.poss == null || storage.rots == null ||
                    storage.IDs.Length != storage.poss.Length || storage.IDs.Length != storage.rots.Length) return false;
            return true;
        }

        // Checks every ID reference the native RearrangeID helper rewrites, plus
        // the whole attachment/storage closure. Prefab-side capacity and slot
        // meaning stay unverified (see Unverified and PrefabChecklist).
        internal static bool ItemReferencesWithinGroup(itemDataClass data, Dictionary<uint, uint> map)
        {
            if (data == null || map == null || data.attachable == null || data.physLocks == null) return false;
            var children = new HashSet<uint>();
            var attached = new Dictionary<uint, uint>();
            foreach (var attachment in data.attachable)
            {
                if (attachment == null || !map.ContainsKey(attachment.id)) return false;
                // Two attachment records for one item would make the parent
                // ambiguous after rekeying.
                if (!children.Add(attachment.id)) return false;
                if (attachment.attachType < (int)save_attachable.attachTypeEmu.none ||
                    attachment.attachType > (int)save_attachable.attachTypeEmu.toWorld) return false;
                if (attachment.indexType < (int)save_attachable.indexTypeEmu.none ||
                    attachment.indexType > (int)save_attachable.indexTypeEmu.slot) return false;
                if (attachment.index < 0 || attachment.index > MaxSlotIndex) return false;
                if (attachment.attachType == (int)save_attachable.attachTypeEmu.toItem)
                {
                    if (!map.ContainsKey(attachment.parentid)) return false;
                    if (attachment.parentid == attachment.id) return false;
                    attached.Add(attachment.id, attachment.parentid);
                }
                // RearrangeID rewrites parentid regardless of attachment type.
                // Reject a non-item reference that would be accidentally rekeyed.
                else if (attachment.parentid != 0 && map.ContainsKey(attachment.parentid)) return false;
            }
            // Every toItem chain must terminate: a bounded walk rejects cycles
            // deeper than the attachment set without guessing prefab parents.
            foreach (var pair in attached)
            {
                uint current = pair.Value;
                uint parent;
                int steps = 0;
                while (attached.TryGetValue(current, out parent))
                {
                    current = parent;
                    if (++steps > attached.Count) return false;
                }
            }
            var stored = new HashSet<uint>();
            foreach (var storage in data.physLocks)
            {
                if (storage == null || !map.ContainsKey(storage.id) || storage.IDs == null) return false;
                if (storage.ind < 0 || storage.ind > MaxSlotIndex) return false;
                foreach (uint id in storage.IDs)
                {
                    if (!map.ContainsKey(id)) return false;
                    if (id == storage.id) return false;
                    // A child stored by two lock records is ambiguous.
                    if (!stored.Add(id)) return false;
                }
            }
            return true;
        }

        static bool Records1<T>(List<T> list, Dictionary<uint, uint> ids) where T : iid
        {
            if (list == null) return false;
            foreach (T record in list) if ((object)record == null || !ids.ContainsKey(record.id)) return false;
            return true;
        }
        static bool Records2<T>(List<T> list, Dictionary<uint, uint> ids) where T : iid2
        {
            if (list == null) return false;
            foreach (T record in list) if ((object)record == null || !ids.ContainsKey(record.id)) return false;
            return true;
        }
        // Mirrors the component lists traversed by the native remapper. This
        // only validates owner IDs and non-null lists, not component semantics.
        internal static bool ComponentOwnersWithinGroup(itemDataClass d, Dictionary<uint, uint> ids)
        {
            if (d == null || ids == null) return false;
            return Records1(d.items, ids) && Records2(d.partconditions, ids) && Records1(d.ammo, ids) &&
                Records1(d.weapon, ids) && Records2(d.tanks, ids) && Records2(d.usable, ids) &&
                Records1(d.meter, ids) && Records1(d.car, ids) && Records1(d.engine, ids) &&
                Records1(d.attachable, ids) && Records1(d.nyul, ids) && Records1(d.ragdollpos, ids) &&
                Records1(d.busdoorscript, ids) && Records2(d.colors, ids) && Records1(d.slideOnCollider, ids) &&
                Records1(d.rendszam, ids) && Records2(d.randoms, ids) && Records2(d.door_rots, ids) &&
                Records1(d.ginlamp, ids) && Records1(d.wear, ids) && Records1(d.food, ids) &&
                Records1(d.outfit, ids) && Records2(d.randomScales, ids) && Records1(d.newai, ids) &&
                Records1(d.painting, ids) && Records2(d.physLocks, ids);
        }

        // Nested records that native RearrangeID does not recurse into. Their IDs
        // must already agree with the owning item, otherwise the rekeyed group
        // would carry stale identities (nested weapon/revolver, part colour and
        // meter-owner IDs). Optional nested records must stay consistent when
        // present and must not be silently fabricated by the native copy.
        internal static bool NestedReferencesSafe(itemDataClass d)
        {
            if (d == null) return false;
            if (d.weapon != null)
                foreach (var weapon in d.weapon)
                {
                    if ((object)weapon == null) return false;
                    if (weapon.bbgun != null && weapon.bbgun.id != weapon.id) return false;
                    if (weapon.revolver != null && weapon.revolver.id != weapon.id) return false;
                }
            if (d.partconditions != null)
                foreach (var part in d.partconditions)
                {
                    if ((object)part == null || part.color == null) return false;
                    if (part.color.id != part.id || part.color.ind != part.ind) return false;
                }
            if (d.meter != null)
                foreach (var meter in d.meter)
                    if ((object)meter == null || !MeterOwnersSafe(meter.mutatok, meter.id) ||
                        !MeterOwnersSafe(meter.szamlapok, meter.id)) return false;
            if (d.tanks != null)
                foreach (var tank in d.tanks)
                    if ((object)tank == null) return false;
                    else if (tank.amount != null && tank.amount.Length > 0 &&
                        (tank.type == null || tank.type.Length != tank.amount.Length)) return false;
            if (d.ragdollpos != null)
                foreach (var ragdoll in d.ragdollpos)
                    if ((object)ragdoll == null || ragdoll.childs == null) return false;
            if (d.busdoorscript != null)
                foreach (var doors in d.busdoorscript)
                    if ((object)doors == null || doors.Bopened == null || doors.Bsigned == null ||
                        doors.Bopened.Length != doors.Bsigned.Length) return false;
            if (d.randomScales != null)
                foreach (var scale in d.randomScales)
                    if ((object)scale == null || scale.v == null) return false;
            if (d.outfit != null)
                foreach (var outfit in d.outfit)
                {
                    if ((object)outfit == null) return false;
                    if (outfit.segments == null) continue;
                    foreach (var segment in outfit.segments) if (segment == null) return false;
                }
            return true;
        }

        static bool MeterOwnersSafe(save_meterscript.save_meter[] meters, uint owner)
        {
            if (meters == null) return true;
            foreach (var meter in meters) if (meter == null || (meter.id != 0 && meter.id != owner)) return false;
            return true;
        }

        // Native Load attaches component records to prefab slots by owner ID plus
        // slot index, so duplicate or negative slots and duplicate single-record
        // components are ambiguous after rekeying. Slot counts themselves live in
        // the prefab and remain unverified.
        internal static bool SlotIndicesPlausible(itemDataClass d)
        {
            if (d == null) return false;
            return OwnerUnique1(d.items) && OwnerUnique1(d.ammo) && OwnerUnique1(d.weapon) &&
                OwnerUnique1(d.meter) && OwnerUnique1(d.car) && OwnerUnique1(d.engine) &&
                OwnerUnique1(d.nyul) && OwnerUnique1(d.ragdollpos) && OwnerUnique1(d.busdoorscript) &&
                OwnerUnique1(d.slideOnCollider) && OwnerUnique1(d.rendszam) && OwnerUnique1(d.ginlamp) &&
                OwnerUnique1(d.wear) && OwnerUnique1(d.food) && OwnerUnique1(d.outfit) &&
                OwnerUnique1(d.newai) && OwnerUnique1(d.painting) && OwnerUnique2(d.colors) &&
                OwnerUnique1(d.attachable) &&
                SlotsSafe(d.partconditions, delegate(save_partcondition r) { return r.ind; }) &&
                SlotsSafe(d.tanks, delegate(save_tankscript r) { return r.ind; }) &&
                SlotsSafe(d.usable, delegate(save_usablescript r) { return r.ind; }) &&
                SlotsSafe(d.randoms, delegate(save_randomType r) { return r.ind; }) &&
                SlotsSafe(d.door_rots, delegate(save_door_rot r) { return r.ind; }) &&
                SlotsSafe(d.randomScales, delegate(save_randomScale r) { return r.ind; }) &&
                SlotsSafe(d.physLocks, delegate(save_physicslock r) { return r.ind; }) &&
                IndicesSafe1(d.food, delegate(save_food r) { return r.index; }) &&
                IndicesSafe1(d.painting, delegate(save_painting r) { return r.index; });
        }

        static bool OwnerUnique1<T>(List<T> list) where T : iid
        {
            if (list == null) return false;
            var seen = new HashSet<uint>();
            foreach (T record in list) if ((object)record == null || !seen.Add(record.id)) return false;
            return true;
        }
        static bool OwnerUnique2<T>(List<T> list) where T : iid2
        {
            if (list == null) return false;
            var seen = new HashSet<uint>();
            foreach (T record in list) if ((object)record == null || !seen.Add(record.id)) return false;
            return true;
        }
        static bool SlotsSafe<T>(List<T> list, Func<T, int> slot) where T : iid2
        {
            if (list == null) return false;
            var seen = new HashSet<ulong>();
            foreach (T record in list)
            {
                if ((object)record == null) return false;
                int index = slot(record);
                if (index < 0 || index > MaxSlotIndex) return false;
                if (!seen.Add(((ulong)record.id << 32) | (uint)index)) return false;
            }
            return true;
        }
        static bool IndicesSafe1<T>(List<T> list, Func<T, int> slot) where T : iid
        {
            if (list == null) return false;
            foreach (T record in list)
            {
                if ((object)record == null) return false;
                int index = slot(record);
                if (index < 0 || index > MaxSlotIndex) return false;
            }
            return true;
        }
    }

    // Prefab-aware checklist. It reads installed prefab metadata only: the
    // itemdatabase prefab enum names, which map save_item.prefabID to the
    // prefab the native spawn resolves through itemdatabase.Item(prefabID).
    // Unity is never started, no prefab is instantiated and nothing is written;
    // conditions that need prefab components or capacities are reported through
    // Unverified instead of being guessed.
    internal sealed class PrefabChecklist
    {
        internal const string PrefabTypeName = "itemdatabase";
        internal const string PrefabEnumName = "i";
        // Plausibility bound for one prefab index; not a game limit.
        internal const int MaxPrefabId = 1 << 16;

        internal static readonly string[] Unverified = new string[]
        {
            "save_attachable.index vs prefab collider/slot count",
            "save_physicslock.ind vs prefab mount storage capacity",
            "save_partcondition.ind vs prefab part count",
            "save_color.ind vs prefab colour slot assignment",
            "save_meter.mutatok/szamlapok tipus vs prefab dials",
            "component presence beyond the car/bus/tank name family",
            "save_attachable.poigenid/poiid external POI identity"
        };

        readonly Dictionary<int, string> names = new Dictionary<int, string>();

        internal bool Available { get { return names.Count > 0; } }
        internal int KnownPrefabs { get { return names.Count; } }

        // Read-only reflection over the assembly that defines itemDataClass.
        // Absent or unreadable prefab metadata leaves the catalog empty and every
        // evaluation fails closed instead of guessing prefab semantics.
        internal static PrefabChecklist Load()
        {
            var catalog = new PrefabChecklist();
            try
            {
                Type database = typeof(itemDataClass).Assembly.GetType(PrefabTypeName, false);
                if (database == null) return catalog;
                Type prefabs = database.GetNestedType(PrefabEnumName, BindingFlags.Public | BindingFlags.NonPublic);
                if (prefabs == null || !prefabs.IsEnum) return catalog;
                foreach (string name in Enum.GetNames(prefabs))
                {
                    int id;
                    try { id = Convert.ToInt32(Enum.Parse(prefabs, name)); }
                    catch (Exception) { continue; }
                    if (id <= 0 || id > MaxPrefabId) continue;
                    catalog.names[id] = name;
                }
            }
            catch (Exception) { catalog.names.Clear(); }
            return catalog;
        }

        internal bool TryName(int prefabID, out string name) { return names.TryGetValue(prefabID, out name); }

        // Native itemdatabase.Item(prefabID) requires CheckType: id > 0 and the
        // slot must be populated, so prefab id 0 (the `error` entry) is invalid.
        internal bool IsItemPrefab(int prefabID) { return prefabID > 0 && names.ContainsKey(prefabID); }

        internal bool IsVehiclePrefab(int prefabID)
        {
            string name;
            return names.TryGetValue(prefabID, out name) && IsVehicleName(name);
        }

        internal static bool IsVehicleName(string name)
        {
            return HasPrefix(name, "car") || HasPrefix(name, "bus") || HasPrefix(name, "bike") || HasPrefix(name, "utanfuto");
        }
        internal static bool IsBusName(string name) { return HasPrefix(name, "bus"); }
        internal static bool IsTankName(string name) { return name != null && name.IndexOf("tank", StringComparison.OrdinalIgnoreCase) >= 0; }

        static bool HasPrefix(string name, string prefix)
        {
            return name != null && name.Length >= prefix.Length &&
                string.Compare(name, 0, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        // Returns false when a decidable prefab condition fails; `unverified`
        // always lists what this data set cannot decide offline.
        internal bool TryEvaluate(itemDataClass data, out List<string> unverified)
        {
            unverified = new List<string>(Unverified);
            if (data == null || data.items == null) return false;
            if (!Available) { unverified.Add("prefab database: " + PrefabTypeName + "." + PrefabEnumName + " unavailable"); return false; }
            var prefabByItem = new Dictionary<uint, int>();
            foreach (var item in data.items)
            {
                if (item == null) return false;
                if (!IsItemPrefab(item.prefabID)) return false;
                if (prefabByItem.ContainsKey(item.id)) return false;
                prefabByItem.Add(item.id, item.prefabID);
            }
            if (data.car != null)
                foreach (var car in data.car)
                {
                    int prefab;
                    if ((object)car == null || !prefabByItem.TryGetValue(car.id, out prefab)) return false;
                    if (!IsVehiclePrefab(prefab)) return false;
                }
            if (data.busdoorscript != null)
                foreach (var doors in data.busdoorscript)
                {
                    string name;
                    int prefab;
                    if ((object)doors == null || !prefabByItem.TryGetValue(doors.id, out prefab)) return false;
                    if (!TryName(prefab, out name) || !IsBusName(name)) return false;
                }
            if (data.tanks != null)
                foreach (var tank in data.tanks)
                {
                    string name;
                    int prefab;
                    if ((object)tank == null || !prefabByItem.TryGetValue(tank.id, out prefab)) return false;
                    if (!TryName(prefab, out name)) return false;
                    if (!IsVehicleName(name) && !IsTankName(name)) return false;
                }
            return true;
        }
    }
}
