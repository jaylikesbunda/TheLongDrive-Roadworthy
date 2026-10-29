using System;

namespace TldMp
{
    // Numeric helpers only. The replacement wire formats, authority gate and
    // snapshot play-back buffer were removed with the rest of the replacement
    // movement path; native serialization and native movement are the sole
    // runtime path.
    public static class Numbers
    {
        public static bool Finite(double x) { return !Double.IsNaN(x) && !Double.IsInfinity(x); }
        public static bool Newer(uint a,uint b) { return unchecked((int)(a-b))>0; }
    }
}
