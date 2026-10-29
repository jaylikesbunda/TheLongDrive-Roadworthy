using System;

namespace TldMp
{
    // Pure counterpart of terrainGenScript.FStart's slider interpolation and
    // Mathf.Lerp/Mathf.Round radius calculation. Kept Unity-free so policy tests
    // exercise the same production logic without a live scene.
    internal static class TerrainDistancePolicy
    {
        internal sealed class ClampState
        {
            internal bool Applied;
            internal float AppliedDistance;
            internal float OriginalMin;
            internal float OriginalMax;
        }

        internal struct Plan
        {
            internal bool KeepCurrentClamp;
            internal bool RestoreOriginal;
            internal bool Clamp;
            internal bool ForceRestart;
            internal float Min;
            internal float Max;
            internal float EffectiveRadius;
        }

        internal static float SliderLerp(float drawDistance)
        {
            return Clamp01((drawDistance - 500f) / (25000f - 500f));
        }

        internal static float EffectiveRadius(float min, float max, float lerp)
        {
            float value = min + (max - min) * Clamp01(lerp);
            return (float)Math.Round(value, MidpointRounding.ToEven);
        }

        internal static bool NeedsClamp(float effectiveRadius, float requestedRadius)
        {
            return effectiveRadius > requestedRadius;
        }

        // The runtime applies this plan immediately before native Upd. If a clamp
        // was already consumed at this slider value, it remains installed. On a
        // setting change, the plan first restores authored radii, then computes
        // whether the next native restart needs a fresh cap.
        internal static Plan Prepare(float min, float max, float requested, float observed,
            ClampState state)
        {
            if (state.Applied && Math.Abs(state.AppliedDistance - requested) < .5f)
                return new Plan { KeepCurrentClamp = true, Min = min, Max = max };

            bool restore = state.Applied;
            if (restore)
            {
                min = state.OriginalMin;
                max = state.OriginalMax;
                state.Applied = false;
            }

            float effective = EffectiveRadius(min, max, SliderLerp(requested));
            var plan = new Plan { RestoreOriginal = restore, Min = min, Max = max, EffectiveRadius = effective };
            if (!NeedsClamp(effective, requested)) return plan;

            state.OriginalMin = min;
            state.OriginalMax = max;
            state.AppliedDistance = requested;
            state.Applied = true;
            plan.Clamp = true;
            plan.Min = requested;
            plan.Max = requested;
            plan.ForceRestart = Math.Abs(observed - requested) < .5f;
            return plan;
        }

        static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
