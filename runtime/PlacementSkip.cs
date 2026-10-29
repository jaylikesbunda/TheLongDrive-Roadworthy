using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TldMp
{
    // categoryClass.UpdPlaceRemove walks every generated prop of its category
    // every frame (Unity null checks on each), although on most frames nothing
    // waits to be placed or removed. A pass is skipped when the category's
    // fingerprint is unchanged since a pass that finished with nothing left:
    // chunk and prop counts, the needed-chunk set, the prop parent's child count
    // (a prop destroyed or added in play changes it) and an epoch our own map code
    // bumps whenever it changes chunks or place flags. Every MaxSkippedFrames a
    // full pass runs anyway. Unity-free; the caller supplies the fingerprints.
    internal struct PlacementSignature
    {
        internal int Chunks, Objs, Children, NeededCount, NeededHash;
        internal long Epoch;

        internal bool SameAs(PlacementSignature o)
        {
            return Chunks == o.Chunks && Objs == o.Objs && Children == o.Children &&
                NeededCount == o.NeededCount && NeededHash == o.NeededHash && Epoch == o.Epoch;
        }
    }

    internal sealed class PlacementSkipPolicy
    {
        internal const int MaxSkippedFrames = 60;

        sealed class State
        {
            internal PlacementSignature After;
            internal bool Clean;
            internal int Skipped;
        }

        readonly Dictionary<object, State> states = new Dictionary<object, State>();
        internal long Walked, SkippedPasses;

        internal bool ShouldSkip(object key, PlacementSignature now)
        {
            State s;
            if (!states.TryGetValue(key, out s) || !s.Clean || !s.After.SameAs(now) || s.Skipped >= MaxSkippedFrames)
            {
                Walked++;
                return false;
            }
            s.Skipped++;
            SkippedPasses++;
            return true;
        }

        // complete: the pass placed and removed fewer than its per-frame caps, so
        // nothing was left waiting.
        internal void AfterWalk(object key, PlacementSignature after, bool complete)
        {
            State s;
            if (!states.TryGetValue(key, out s)) { s = new State(); states[key] = s; }
            s.After = after;
            s.Clean = complete;
            s.Skipped = 0;
        }

        internal void Clear() { states.Clear(); }
    }

    internal static class PlacementSkip
    {
        static readonly PlacementSkipPolicy policy = new PlacementSkipPolicy();
        static readonly Dictionary<objGenScript.categoryClass, long> epochs = new Dictionary<objGenScript.categoryClass, long>();
        static readonly FieldInfo placedField = typeof(objGenScript.categoryClass).GetField("placed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static readonly FieldInfo removedField = typeof(objGenScript.categoryClass).GetField("removed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static readonly bool enabled = Environment.GetEnvironmentVariable("TLD_MP_PLACE_SKIP") != "0";
        static objGenScript.categoryClass current;
        static bool walking;

        // Our map code changed this category's chunks or place flags.
        internal static void Touch(objGenScript.categoryClass cat)
        {
            if (cat == null) return;
            long e;
            epochs.TryGetValue(cat, out e);
            epochs[cat] = e + 1;
        }

        internal static void Begin(objGenScript.categoryClass cat)
        {
            current = cat;
            walking = false;
        }

        // Called from SortPlaceOrder with the category's fresh placeOrder list.
        // Clearing it makes the native pass loop over nothing.
        internal static bool Skip(List<Vector3d> order)
        {
            var cat = current;
            if (!enabled || cat == null || placedField == null || removedField == null) { walking = false; return false; }
            if (policy.ShouldSkip(cat, Signature(cat)))
            {
                order.Clear();
                walking = false;
                return true;
            }
            walking = true;
            return false;
        }

        internal static void End()
        {
            var cat = current;
            current = null;
            if (!walking || cat == null) return;
            walking = false;
            int placed = (int)placedField.GetValue(cat), removed = (int)removedField.GetValue(cat);
            var script = cat.script;
            bool complete = script != null && placed < script.placePerFrame && removed < script.removePerFrame;
            policy.AfterWalk(cat, Signature(cat), complete);
        }

        static PlacementSignature Signature(objGenScript.categoryClass cat)
        {
            var s = new PlacementSignature();
            if (cat.generatedChunks != null)
            {
                s.Chunks = cat.generatedChunks.Count;
                foreach (var chunk in cat.generatedChunks.Values) if (chunk != null && chunk.objs != null) s.Objs += chunk.objs.Count;
            }
            Transform parent = cat.script == null ? null : cat.script.parent;
            s.Children = parent == null ? -1 : parent.childCount;
            if (cat.chunksNeeded != null)
            {
                s.NeededCount = cat.chunksNeeded.Count;
                unchecked { for (int i = 0; i < cat.chunksNeeded.Count; i++) s.NeededHash += cat.chunksNeeded[i].GetHashCode() * 31 + 7; }
            }
            long e;
            epochs.TryGetValue(cat, out e);
            s.Epoch = e;
            return s;
        }

        internal static void Clear()
        {
            policy.Clear();
            epochs.Clear();
            current = null;
            walking = false;
        }

        internal static string TakeSummary()
        {
            string text = "placeSkip{walked=" + policy.Walked + " skipped=" + policy.SkippedPasses + "}";
            policy.Walked = 0;
            policy.SkippedPasses = 0;
            return text;
        }
    }
}
