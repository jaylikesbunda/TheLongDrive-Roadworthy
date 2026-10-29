using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Remembers how many elements of a road chunk are still live. The native
    // far branch would otherwise walk every element of every far chunk and
    // null-check it each frame. Only a chunk proven to have zero live elements
    // is skipped; anything unknown or uncertain is walked by the native loop.
    internal static class RoadLiveCache
    {
        sealed class Entry
        {
            internal bool Known;
            internal int Count;
            internal int Elements;
            internal int RoadId;
        }

        static readonly Dictionary<roadGenScript.roadChunk, Entry> entries =
            new Dictionary<roadGenScript.roadChunk, Entry>();

        internal static void Clear()
        {
            entries.Clear();
        }

        internal static void Dirty(roadGenScript.roadChunk road)
        {
            if (road != null) entries.Remove(road);
        }

        internal static void Destroyed(roadGenScript.roadChunk road)
        {
            // Invalidate instead of decrementing: Unity may keep the destroyed
            // object non-null until the end of the frame, so a recount is safer
            // than trusting a decrement. Chunks are tiny (maxList <= 2).
            Dirty(road);
        }

        internal static bool MayHaveLive(roadGenScript.dotChunkClass chunk, roadGenScript.roadChunk road)
        {
            if (chunk == null || road == null || road.roadElements == null || chunk.roads == null)
                return true;
            int elements = road.roadElements.Count;
            Entry entry;
            if (entries.TryGetValue(road, out entry) && !NeedsRecount(entry.Known, entry.Elements, elements) && entry.RoadId == road.roadId)
                return entry.Count > 0;
            if (road.roadId < 0 || road.roadId >= chunk.roads.Count) return true;
            roadGenScript.roadClass owner = chunk.roads[road.roadId];
            if (owner == null || owner.roadElements == null) return true;
            int live = 0;
            for (int i = 0; i < elements; i++)
            {
                int index = road.roadElements[i];
                if (index < 0 || index >= owner.roadElements.Count) return true;
                roadGenScript.roadElementClass element = owner.roadElements[index];
                if (element == null) return true;
                if (element.obj != null) live++;
            }
            entries[road] = new Entry { Known = true, Count = live, Elements = elements, RoadId = road.roadId };
            return live > 0;
        }

        internal static bool NeedsRecount(bool known, int cachedElements, int currentElements)
        {
            return !known || cachedElements != currentElements;
        }

    }
}
