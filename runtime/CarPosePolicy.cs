using System;
using UnityEngine;

namespace TldMp
{
    // Adds extra pose updates for a car that is (a) locally authoritative,
    // (b) moving, and (c) seen by at least one remote player. Parked or purely
    // remote-authoritative cars keep the native sendMoreCar count. The count is
    // hard-capped; no authority, physics, seat or wire-format change.
    // Local player position rate. Vanilla sends every syncScript.playerPosSyncTime
    // seconds (about 3-4 per second); remote players are interpolated, so more
    // samples mean smoother walking. ~60 bytes each, ~1.8 KB/s per player at 30 Hz.
    internal static class PlayerRatePolicy
    {
        internal const int HighHz = 30;

        // The interval to use: never slower than the game's own value.
        internal static float Interval(float nativeInterval, bool highRate)
        {
            if (!highRate || !(nativeInterval > 0f) || float.IsInfinity(nativeInterval)) return nativeInterval;
            float high = 1f / HighHz;
            return nativeInterval < high ? nativeInterval : high;
        }
    }

    internal static class CarPosePolicy
    {
        const int MaxExtraCount = 8;
        const float MotionEpsilon = .04f;
        // Target pose rate for a moving car carrying a remote player. One pose and
        // velocity pair is 82 payload bytes and ~1 us to serialize, so 20 Hz is
        // ~1.6 KB/s per car; the bound keeps a misconfigured interval harmless.
        // At 60 Hz that is ~5 KB/s per car. The sender cannot exceed its own frame
        // rate: the stream waits at least one frame between poses.
        internal const int DefaultTargetHz = 20, HighTargetHz = 60, MinTargetHz = 5, MaxTargetHz = 60, MaxBurstCount = 29;
        static int envHz = -1;

        // TLD_MP_CAR_RATE=<5..60> overrides the option for A/B tests.
        internal static int TargetHz
        {
            get
            {
                if (envHz < 0) envHz = ParseOverride(Environment.GetEnvironmentVariable("TLD_MP_CAR_RATE"));
                PatchOptions.Load();
                return RateFor(envHz, PatchOptions.HighRate);
            }
        }

        // 0 = no override.
        internal static int ParseOverride(string value)
        {
            int hz;
            if (String.IsNullOrEmpty(value) || !Int32.TryParse(value, out hz)) return 0;
            return Clamp(hz);
        }

        internal static int RateFor(int overrideHz, bool highRate)
        {
            if (overrideHz > 0) return Clamp(overrideHz);
            return highRate ? HighTargetHz : DefaultTargetHz;
        }

        internal static int ParseRate(string value)
        {
            int hz = ParseOverride(value);
            return hz > 0 ? hz : DefaultTargetHz;
        }

        static int Clamp(int hz)
        {
            if (hz < MinTargetHz) return MinTargetHz;
            if (hz > MaxTargetHz) return MaxTargetHz;
            return hz;
        }

        internal static int AdjustCount(tosaveitemscript item, int baseCount)
        {
            if (!NeedsBurst(item)) return baseCount;
            syncScript sync = syncScript.s;
            return CountForRate(baseCount, true, true, sync == null ? 0f : sync.syncItemInterwall, TargetHz);
        }

        // Extra poses per native sync interval so that, with the native pose that
        // starts the burst, the car sends about targetHz poses per second. Never
        // below the native count or the previous policy, never above MaxBurstCount.
        internal static int CountForRate(int baseCount, bool occupied, bool moving, float interval, int hz)
        {
            if (!occupied || !moving) return baseCount;
            int floor = Adjust(baseCount, true, true);
            if (!(interval > 0f) || float.IsInfinity(interval) || hz <= 0) return floor;
            double wanted = Math.Round(hz * (double)interval) - 1;
            int count = wanted > MaxBurstCount ? MaxBurstCount : (int)wanted;
            if (count < floor) count = floor;
            if (count < baseCount) count = baseCount;
            return count;
        }

        // A pose stream outlives one sync interval so the item sweep's length never
        // shows as a hole; a newer stream for the same car replaces it. The cap only
        // bounds a stream whose replacement never comes (the sweep stalled).
        internal const double StreamMaxSeconds = 5.0;

        internal static bool ContinueStream(bool newest, bool inCharge, bool needsBurst, double elapsedSeconds)
        {
            return newest && inCharge && needsBurst && elapsedSeconds >= 0 && elapsedSeconds < StreamMaxSeconds;
        }

        // Seconds between stream poses for a target rate, clamped to the rate bounds.
        internal static float StreamSpacing(int hz)
        {
            if (hz < MinTargetHz) hz = MinTargetHz;
            if (hz > MaxTargetHz) hz = MaxTargetHz;
            return 1f / hz;
        }

        // Only the local car-pose authority raises its own car's extra count, and
        // only while the car is moving and another player is connected to see it.
        // A car driven alone used to stay at the native rate (about one pose per
        // second), so every other player saw it jump metres back and forth.
        internal static bool NeedsBurst(tosaveitemscript item)
        {
            if (ReferenceEquals(item, null) || item.car == null) return false;
            return WantsBurst(item.ThisOneInChargeCar(), Moving(item), RemotePlayers());
        }

        internal static bool WantsBurst(bool inCharge, bool moving, int remotePlayers)
        {
            return inCharge && moving && remotePlayers > 0;
        }

        // Pure decision helper, kept free of Unity objects so it can be tested
        // outside the player loop.
        internal static int Adjust(int baseCount, bool occupied, bool moving)
        {
            if (!occupied || !moving) return baseCount;
            if (baseCount >= MaxExtraCount) return baseCount;
            int target = baseCount + 2;
            if (target < 4) target = 4;
            if (target > MaxExtraCount) target = MaxExtraCount;
            return target;
        }

        static int RemotePlayers()
        {
            syncScript sync = syncScript.s;
            if (sync == null || sync.players == null) return 0;
            int count = 0;
            foreach (syncScript.playerSyncClass player in sync.players.Values) if (player != null) count++;
            return count;
        }

        static bool Moving(tosaveitemscript item)
        {
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body == null) return false;
            return body.velocity.sqrMagnitude > MotionEpsilon || body.angularVelocity.sqrMagnitude > MotionEpsilon;
        }
    }
}
