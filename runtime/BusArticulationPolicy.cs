using System;
namespace TldMp
{
    public static class BusArticulationPolicy
    {
        public static bool ShouldResize(float current, float desired)
        {
            return !float.IsNaN(desired) && !float.IsInfinity(desired) && Math.Abs(current - desired) > 0.00001f;
        }
    }
}
