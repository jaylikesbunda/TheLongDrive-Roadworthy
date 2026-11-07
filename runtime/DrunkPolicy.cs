using System;

namespace TldMp
{
    // Unity-free drunk-level math. Camera wiring lives in DrunkEffects.cs.
    // Level runs 0..MaxLevel (3). One liter of alcohol adds GainPerLiter, and
    // the level wears off at one level per DecaySeconds of real time, so one
    // liter lasts five minutes and a full three levels lasts fifteen.
    // Milk sobers you up: each liter removes MilkSoberPerLiter levels, so half
    // a liter of milk takes off one level.
    // Sway grows faster than linearly with the level: l + l*l degrees, so a
    // light drink is barely visible and three levels reach 12 degrees. The
    // sway also speeds up with the level (SwaySpeed), so heavy drinking
    // wobbles faster as well as wider. Angles and FovOffset take a phase clock
    // the caller advances by deltaTime * SwaySpeed(level), so changing the
    // level never makes the phase jump.
    public static class DrunkPolicy
    {
        public const float MaxLevel = 3f;
        public const float GainPerLiter = 1f;
        public const float MilkSoberPerLiter = 2f;
        public const float DecaySeconds = 300f;
        public const float MaxSwayDegrees = 12f;
        // Peak field-of-view change in degrees at max level. A slow "breathing"
        // zoom, so the effect reads indoors and while standing still.
        public const float MaxFovPulse = 6f;
        // A single frame can never sober someone up or drink them more than
        // a second's worth, so hitches and paused frames stay harmless.
        const float MaxStep = 1f;

        public static float Clamp(float level)
        {
            if (float.IsNaN(level) || level < 0f) return 0f;
            return level > MaxLevel ? MaxLevel : level;
        }

        public static float Add(float level, float liters)
        {
            if (!(liters > 0f) || float.IsInfinity(liters)) return Clamp(level);
            return Clamp(Clamp(level) + liters * GainPerLiter);
        }

        // Milk takes levels off. Bad liters (NaN, zero, negative, infinite) change nothing.
        public static float Sober(float level, float liters)
        {
            if (!(liters > 0f) || float.IsInfinity(liters)) return Clamp(level);
            return Clamp(Clamp(level) - liters * MilkSoberPerLiter);
        }

        public static float Decay(float level, float seconds)
        {
            if (!(seconds > 0f)) return Clamp(level);
            float step = seconds > MaxStep ? MaxStep : seconds;
            return Clamp(Clamp(level) - step / DecaySeconds);
        }

        public static float SwayAmplitude(float level)
        {
            float l = Clamp(level);
            float degrees = l + l * l;
            return degrees > MaxSwayDegrees ? MaxSwayDegrees : degrees;
        }

        // Phase clock multiplier: 1x sober, 1.75x at max level.
        public static float SwaySpeed(float level)
        {
            return 1f + 0.25f * Clamp(level);
        }

        // Peak field-of-view change in degrees for a level. Zero when sober.
        public static float FovAmplitude(float level)
        {
            float l = Clamp(level);
            return MaxFovPulse * (float)Math.Pow(l / MaxLevel, 0.75);
        }

        // Breathing zoom around the base field of view, bounded by FovAmplitude.
        public static float FovOffset(float level, float time)
        {
            if (float.IsNaN(time) || float.IsInfinity(time)) return 0f;
            return (float)(FovAmplitude(level) * Math.Sin(2.0 * Math.PI * 0.12 * time));
        }

        // Slow drifts that never sit near zero: pitch and yaw pair a sin with a
        // cos (or a second sin) so the head keeps moving in a loop. Roll is
        // calmer and adds a slow lean bias; the total is clamped to 0.95 of the
        // amplitude.
        public static void Angles(float level, float time, out float pitch, out float yaw, out float roll)
        {
            float amplitude = SwayAmplitude(level);
            if (!(amplitude > 0f) || float.IsNaN(time) || float.IsInfinity(time))
            {
                pitch = 0f; yaw = 0f; roll = 0f;
                return;
            }
            double t = time;
            double p = 0.75 * Math.Sin(2.0 * Math.PI * 0.17 * t) + 0.25 * Math.Sin(2.0 * Math.PI * 0.43 * t + 1.3);
            double y = 0.75 * Math.Cos(2.0 * Math.PI * 0.15 * t + 0.4) + 0.25 * Math.Sin(2.0 * Math.PI * 0.39 * t + 2.1);
            double r = 0.6 * (0.7 * Math.Sin(2.0 * Math.PI * 0.11 * t + 0.9) + 0.3 * Math.Sin(2.0 * Math.PI * 0.31 * t))
                + 0.35 * Math.Sin(2.0 * Math.PI * 0.05 * t + 0.5);
            double rollLimit = 0.95 * amplitude;
            if (r > rollLimit / amplitude) r = rollLimit / amplitude;
            if (r < -rollLimit / amplitude) r = -rollLimit / amplitude;
            pitch = (float)(amplitude * p);
            yaw = (float)(amplitude * y);
            roll = (float)(amplitude * r);
        }
    }
}
