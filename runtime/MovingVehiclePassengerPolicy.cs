using System;
namespace TldMp
{
    public static class MovingVehiclePassengerPolicy
    {
        public static bool ValidSample(double dx,double dy,double dz,float dt,float angle,float localSquared)
        {
            if (double.IsNaN(dx) || double.IsNaN(dy) || double.IsNaN(dz) || double.IsInfinity(dx) || double.IsInfinity(dy) || double.IsInfinity(dz) ||
                float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f || dt > .2f || float.IsNaN(angle) || angle > 45f ||
                float.IsNaN(localSquared) || float.IsInfinity(localSquared) || localSquared > 1600f) return false;
            return dx*dx + dy*dy + dz*dz <= 10000.0 * dt * dt;
        }
        public static bool ValidVelocity(float squared)
        {
            return !float.IsNaN(squared) && !float.IsInfinity(squared) && squared <= 10000f;
        }

        // Discount only motion already transferred along the support's change.
        // Any perpendicular or opposing player impulse is retained untouched.
        public static bool CarryResidual(float dx, float dz, float observedX, float observedZ, float dt, out float x, out float z)
        {
            x = z = 0f;
            if (!ValidVelocity(dx * dx + dz * dz) || !ValidVelocity(observedX * observedX + observedZ * observedZ) ||
                float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f || dt > .2f) return false;
            float squared = dx * dx + dz * dz;
            float cap = 2f * dt / .02f;
            if (squared > cap * cap) return false;
            if (squared < 1e-10f) return true;
            float fraction = (observedX * dx + observedZ * dz) / squared;
            fraction = Math.Max(0f, Math.Min(1f, fraction));
            x = dx * (1f - fraction); z = dz * (1f - fraction);
            return true;
        }
    }
}
