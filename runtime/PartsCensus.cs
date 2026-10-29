using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TldMp
{
    // Read-only multiplayer diagnostics: every 10 s, each car or POI within 60 m of
    // the local player that has part slots is listed with its filled and empty
    // slots, so host and guest logs can be compared slot by slot. Never changes
    // a slot, part or item.
    internal static class PartsCensus
    {
        internal const float IntervalSeconds = 10f;
        internal const float RadiusMetres = 60f;
        const int MaxOwners = 12, MaxNames = 12;
        static float next;

        sealed class Owner
        {
            internal string Key, Name;
            internal int Slots, Filled;
            internal readonly List<string> Empty = new List<string>();
            internal readonly List<string> Parts = new List<string>();
        }

        internal static void Tick(bool connected)
        {
            if (!connected || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + IntervalSeconds;
            var main = mainscript.s;
            if (main == null || main.player == null) return;
            Vector3 at = main.player.transform.position;
            var owners = new Dictionary<string, Owner>();
            partslotscript[] slots = UnityEngine.Object.FindObjectsOfType<partslotscript>();
            for (int i = 0; i < slots.Length; i++)
            {
                partslotscript slot = slots[i];
                if (slot == null || (slot.transform.position - at).sqrMagnitude > RadiusMetres * RadiusMetres) continue;
                string key, name;
                if (slot.setidpoi)
                {
                    key = "poi:" + slot.poigenid + "@" + slot.poiid.x.ToString("F0", CultureInfo.InvariantCulture) + "," + slot.poiid.z.ToString("F0", CultureInfo.InvariantCulture);
                    name = slot.transform.root.name;
                }
                else if (slot.tosaveitem != null)
                {
                    key = "item:" + slot.tosaveid;
                    name = slot.tosaveitem.name;
                }
                else
                {
                    key = "unset:" + slot.transform.root.GetInstanceID();
                    name = slot.transform.root.name;
                }
                Owner owner;
                if (!owners.TryGetValue(key, out owner))
                {
                    owner = new Owner { Key = key, Name = name };
                    owners[key] = owner;
                }
                owner.Slots++;
                attachablescript part = slot.hasPart() ? slot.part() : null;
                if (part != null)
                {
                    owner.Filled++;
                    if (owner.Parts.Count < MaxNames)
                    {
                        tosaveitemscript item = part.GetComponentInParent<tosaveitemscript>();
                        owner.Parts.Add(slot.name + "=" + (item == null ? part.name : item.idInSave.ToString(CultureInfo.InvariantCulture)));
                    }
                }
                else if (owner.Empty.Count < MaxNames) owner.Empty.Add(slot.name + (slot.enableSpawnProperPart ? "*" : ""));
            }
            var text = new StringBuilder("[TLD MP parts] t=");
            text.Append(Time.realtimeSinceStartup.ToString("F1", CultureInfo.InvariantCulture)).Append(" host=").Append(syncScript.IsHost())
                .Append(" owners=").Append(owners.Count);
            int shown = 0;
            foreach (var owner in owners.Values)
            {
                if (++shown > MaxOwners) break;
                text.Append(" {").Append(owner.Key).Append(' ').Append(owner.Name).Append(" filled=").Append(owner.Filled).Append('/').Append(owner.Slots)
                    .Append(" parts=[").Append(String.Join(";", owner.Parts.ToArray())).Append("] empty=[").Append(String.Join(";", owner.Empty.ToArray())).Append("]}");
            }
            Debug.Log(text.ToString());
        }
    }
}
