using System;
using System.Collections.Generic;

namespace TldMp
{
    // Unity-free accounting for nested camera renders. Mirror cameras render
    // inside the main camera's OnPreRender, so each bucket records both the
    // inclusive span (onPreCull..onPostRender) and the exclusive span with
    // nested renders removed. Unmatched ends are ignored; a missing end is
    // closed by the next frame boundary and counted as dropped.
    internal sealed class CameraCostCore
    {
        internal const int MaxDepth = 16;

        internal sealed class Bucket
        {
            internal string Label;
            internal int Renders;
            internal long Inclusive, Exclusive, MaxExclusive;
        }

        struct Open { internal int Id; internal string Label; internal long Start; internal long Children; }

        readonly Open[] stack = new Open[MaxDepth];
        int depth;
        readonly Dictionary<string, Bucket> buckets = new Dictionary<string, Bucket>();
        internal readonly List<Bucket> Ordered = new List<Bucket>();
        internal int Frames, Dropped, Overflow;

        internal void Begin(int id, string label, long now)
        {
            if (depth >= MaxDepth) { Overflow++; return; }
            stack[depth].Id = id; stack[depth].Label = label; stack[depth].Start = now; stack[depth].Children = 0;
            depth++;
        }

        internal void End(int id, long now)
        {
            int at = -1;
            for (int i = depth - 1; i >= 0; i--) if (stack[i].Id == id) { at = i; break; }
            if (at < 0) return;
            // Renders opened after this one but never ended are abandoned.
            Dropped += depth - 1 - at;
            depth = at;
            Open open = stack[at];
            long inclusive = Math.Max(0, now - open.Start);
            long exclusive = Math.Max(0, inclusive - open.Children);
            if (depth > 0) stack[depth - 1].Children += inclusive;
            Bucket bucket;
            if (!buckets.TryGetValue(open.Label, out bucket))
            {
                bucket = new Bucket { Label = open.Label };
                buckets.Add(open.Label, bucket); Ordered.Add(bucket);
            }
            bucket.Renders++; bucket.Inclusive += inclusive; bucket.Exclusive += exclusive;
            if (exclusive > bucket.MaxExclusive) bucket.MaxExclusive = exclusive;
        }

        internal void FrameBoundary()
        {
            Dropped += depth; depth = 0; Frames++;
        }

        internal Bucket Get(string label) { Bucket b; return buckets.TryGetValue(label, out b) ? b : null; }

        internal void ResetWindow()
        {
            buckets.Clear(); Ordered.Clear(); Frames = 0; Dropped = 0; Overflow = 0;
        }
    }
}
