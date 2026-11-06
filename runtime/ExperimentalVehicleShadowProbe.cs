using System;
using UnityEngine;

namespace TldMp.Experimental
{
    // Diagnostic only. No hook is installed by this class. IDs and epochs in
    // the log are synthetic local labels, not experimental authority identities.
    public static class VehicleShadowProbe
    {
        const int MaxSamples = 64;
        static readonly bool enabled = Enabled();
        static int count;
        static VehicleShadowProbe() { }
        static bool Enabled()
        {
            try { return Environment.GetEnvironmentVariable("TLD_MP_VEHICLE_SHADOW_PROBE") == "1"; }
            catch (Exception) { return false; }
        }
        public static void Observe(tosaveitemscript item)
        {
            if (!enabled || count >= MaxSamples) return;
            try
            {
                VehiclePose pose;
                if (item == null || item.idInSave == 0 || !VehicleCapture.TryCapture(item, item.idInSave, 1, out pose)) return;
                count++;
                Vector3 anchor = item.useThisForPos.position, root = item.transform.position;
                Debug.Log("[Roadworthy vehicle shadow] observer-only localId=" + item.idInSave +
                    " global=" + pose.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    pose.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    pose.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                    " rotation=" + F(pose.Qx) + "," + F(pose.Qy) + "," + F(pose.Qz) + "," + F(pose.Qw) +
                    " velocity=" + F(pose.Vx) + "," + F(pose.Vy) + "," + F(pose.Vz) +
                    " anchorMinusRoot=" + F(anchor.x - root.x) + "," + F(anchor.y - root.y) + "," + F(anchor.z - root.z));
            }
            catch (Exception) { /* Never allow diagnostics to change native gameplay. */ }
        }
        static string F(float value) { return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
    }
}
