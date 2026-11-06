using System;
using System.Collections.Generic;

namespace TldMp.Experimental
{
    // Prepares a fresh detached copy only. Caller must hold an exclusive
    // reservation for the entire target ID range and own the source while this
    // runs; occupied must report conflicts, excluding that caller's reservation.
    // This is not an allocator, world spawn, save operation or commit.
    // Only IDs/references are checked: native Copy fidelity for all component
    // values is unproven. Do not spawn or persist its result until verified.
    internal static class DetachedRemap
    {
        internal static bool TryPrepare(itemDataClass source, uint first, uint last, Func<uint, bool> occupied,
            out itemDataClass prepared)
        {
            prepared = null;
            Dictionary<uint, uint> sourceMap;
            if (!SavedIds.TryValidateIds(source, first, last, occupied, out sourceMap)) return false;
            // Native weapon Copy constructs nested records even when absent and
            // maps empty ammunition to null although Load dereferences it.
            foreach (var weapon in source.weapon)
                if (weapon.revolver != null && (weapon.revolver.bullets == null || weapon.revolver.bullets.Length == 0)) return false;
            try
            {
                var copy = new itemDataClass();
                copy.Copy(source);
                for (int i = 0; i < source.weapon.Count; i++)
                {
                    if (source.weapon[i].bbgun == null) copy.weapon[i].bbgun = null;
                    if (source.weapon[i].revolver == null) copy.weapon[i].revolver = null;
                }
                for (int i = 0; i < source.meter.Count; i++)
                {
                    if (source.meter[i].mutatok != null && source.meter[i].mutatok.Length == 0) copy.meter[i].mutatok = new save_meterscript.save_meter[0];
                    if (source.meter[i].szamlapok != null && source.meter[i].szamlapok.Length == 0) copy.meter[i].szamlapok = new save_meterscript.save_meter[0];
                }
                for (int i = 0; i < source.outfit.Count; i++)
                    if (source.outfit[i].segments != null && source.outfit[i].segments.Length == 0) copy.outfit[i].segments = new save_outfitsegment[0];
                for (int i = 0; i < source.ragdollpos.Count; i++)
                    if (source.ragdollpos[i].childs.Length == 0) copy.ragdollpos[i].childs = new posRotd[0];
                for (int i = 0; i < source.busdoorscript.Count; i++)
                {
                    if (source.busdoorscript[i].Bopened.Length == 0) copy.busdoorscript[i].Bopened = new bool[0];
                    if (source.busdoorscript[i].Bsigned.Length == 0) copy.busdoorscript[i].Bsigned = new bool[0];
                }
                for (int i = 0; i < source.randomScales.Count; i++)
                    if (source.randomScales[i].v.Length == 0) copy.randomScales[i].v = new vector3[0];
                Dictionary<uint, uint> copiedMap;
                if (!SavedIds.TryValidateIds(copy, first, last, occupied, out copiedMap) || copiedMap.Count != sourceMap.Count) return false;
                foreach (var pair in sourceMap)
                {
                    uint value;
                    if (!copiedMap.TryGetValue(pair.Key, out value) || value != pair.Value) return false;
                }
                savedatascript.RearrangeID(copy, first);
                // The game remaps the outer part-condition record, but its
                // embedded color record has its own ID and is not traversed.
                foreach (var part in copy.partconditions) part.color.id = part.id;
                foreach (var weapon in copy.weapon)
                {
                    if (weapon.bbgun != null) weapon.bbgun.id = weapon.id;
                    if (weapon.revolver != null) weapon.revolver.id = weapon.id;
                }
                for (int i = 0; i < source.meter.Count; i++)
                {
                    RekeyMeters(copy.meter[i].mutatok, source.meter[i].id, copy.meter[i].id);
                    RekeyMeters(copy.meter[i].szamlapok, source.meter[i].id, copy.meter[i].id);
                }
                Dictionary<uint, uint> after;
                if (!SavedIds.TryValidateIds(copy, first, last, delegate(uint id) { return false; }, out after)) return false;
                for (int i = 0; i < copy.items.Count; i++) if (copy.items[i].id != first + (uint)i) return false;
                prepared = copy; return true;
            }
            catch (Exception) { return false; }
        }

        // Production shape: the allocator-issued lease already owns the whole
        // target range, so no separate occupied callback is needed and a released
        // or foreign lease can never prepare a copy.
        internal static bool TryPrepare(itemDataClass source, IdRangeLease lease, out itemDataClass prepared)
        {
            prepared = null;
            if (lease == null || !lease.IsHeld) return false;
            return TryPrepare(source, lease.First, lease.Last,
                delegate(uint id) { return lease.OccupiedByOther(id); }, out prepared);
        }

        // Same as above plus the prefab-aware checklist. A prefab condition that
        // cannot be decided offline fails closed instead of being guessed.
        internal static bool TryPrepare(itemDataClass source, IdRangeLease lease, PrefabChecklist prefab,
            out itemDataClass prepared)
        {
            prepared = null;
            if (lease == null || !lease.IsHeld || prefab == null) return false;
            List<string> unverified;
            if (!prefab.TryEvaluate(source, out unverified)) return false;
            return TryPrepare(source, lease, out prepared);
        }

        static void RekeyMeters(save_meterscript.save_meter[] meters, uint oldOwner, uint newOwner)
        {
            if (meters == null) return;
            foreach (var meter in meters) if (meter.id == oldOwner) meter.id = newOwner;
        }
    }
}
