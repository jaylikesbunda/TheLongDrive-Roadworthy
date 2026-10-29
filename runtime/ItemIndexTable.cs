using System;
using System.Collections.Generic;

namespace TldMp
{
    // Pass-local ID -> index table for the read-only candidate-selection phase
    // of itemPlaceRemoveScript.PlaceStuff. It mirrors savedatascript.IndexOfID:
    // a backward scan, so the LAST duplicate id wins. It is deliberately not a
    // cross-frame cache; Begin() is called at the start of each selection pass
    // and the table is discarded before the mutating phase.
    internal sealed class ItemIndexTable
    {
        Dictionary<uint, int> map;
        List<save_item> source;
        int sourceCount;
        int lastNull;
        bool armed;

        internal bool Armed { get { return armed; } }
        internal int Count { get { return map == null ? 0 : map.Count; } }

        internal void Clear()
        {
            map = null;
            source = null;
            sourceCount = 0;
            lastNull = -1;
            armed = false;
        }

        internal bool Build(List<save_item> items)
        {
            Clear();
            if (items == null) return false;
            int n = items.Count;
            var table = new Dictionary<uint, int>(n);
            int nullAt = -1;
            for (int i = 0; i < n; i++)
            {
                save_item item = items[i];
                if (item == null) { nullAt = i; continue; }
                table[item.id] = i; // forward assignment: last duplicate wins
            }
            map = table;
            source = items;
            sourceCount = n;
            lastNull = nullAt;
            armed = true;
            return true;
        }

        // true  -> index is authoritative (-1 = miss)
        // false -> the caller must run the native backward scan
        internal bool TryGet(List<save_item> items, uint id, out int index)
        {
            index = -1;
            if (!armed || !ReferenceEquals(items, source) || items == null || items.Count != sourceCount)
                return false;
            int hit;
            if (!map.TryGetValue(id, out hit))
                return lastNull < 0; // a miss is authoritative only when no null would be dereferenced
            // Native scans from the end; a null after the hit is dereferenced first.
            if (hit < 0 || hit >= items.Count || hit < lastNull) return false;
            save_item at = items[hit];
            if (at == null || at.id != id) return false;
            index = hit;
            return true;
        }
    }
}
