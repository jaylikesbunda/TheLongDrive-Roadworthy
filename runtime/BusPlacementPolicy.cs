using System;

namespace TldMp
{
    // Pure decisions for the starting-house bus stop (GroundDiagnostics), unit-tested.
    public static class BusPlacementPolicy
    {
        // The roadside search runs every frame, so a few seconds is plenty when
        // the world around the house is loaded.
        public const float SpawnDeadline = 5f;
        // While the near terrain collider or the road is still being built the
        // search cannot succeed yet. New worlds (and road-aware terrain) can take
        // far longer than 5 s for that, so the house's props wait up to this long.
        public const float WorldDeadline = 45f;
        // Results that depend only on fixed geometry cannot change by retrying.
        public const float TerminalSettle = 0.5f;
        // After the props are released without a placement, the stop is still put
        // on the ground (height only) once the terrain exists, for this long.
        public const float GroundOnlyLimit = 180f;
        // A stop this close to the ground is left alone (collider vs authored floor).
        public const double GroundTolerance = 0.02;

        public static bool Terminal(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return false;
            return reason.StartsWith("road clearance too far", StringComparison.Ordinal)
                || reason == "road clearance did not converge"
                || reason == "house blocks destination"
                || reason == "missing stop footprint"
                || reason == "road has no usable segment";
        }

        // Reasons that clear up by themselves once nearby terrain and roads are built.
        public static bool WaitsForWorld(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return true;
            return reason.StartsWith("terrain unavailable", StringComparison.Ordinal)
                || reason.StartsWith("roads unavailable", StringComparison.Ordinal)
                || reason == "road not generated"
                || reason == "road mesh unavailable"
                || reason == "road data unavailable";
        }

        // True when the house's props may spawn even though the stop is not placed.
        public static bool ReleaseProps(float waited, string reason)
        {
            if (float.IsNaN(waited)) return true;
            if (Terminal(reason)) return waited >= TerminalSettle;
            return waited >= (WaitsForWorld(reason) ? WorldDeadline : SpawnDeadline);
        }

        // Vertical move that puts the stop's floor marker on the ground, or 0 when
        // the ground is unknown or already within tolerance.
        public static double GroundShift(double markerY, double groundY)
        {
            if (double.IsNaN(markerY) || double.IsInfinity(markerY) || double.IsNaN(groundY) || double.IsInfinity(groundY)) return 0;
            double shift = groundY - markerY;
            return Math.Abs(shift) <= GroundTolerance ? 0 : shift;
        }
    }
}
