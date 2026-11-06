using UnityEngine;

namespace TldMp.Experimental
{
    // Read-only adapter. Caller owns network/local ID mapping and authority.
    // It does not install a callback, write physics, or participate in a session.
    internal static class VehicleCapture
    {
        internal static bool TryCapture(tosaveitemscript item, ulong networkId, ulong epoch, out VehiclePose pose)
        {
            pose = null;
            if (item == null || item.car == null || item.RB == null || item.useThisForPos == null || networkId == 0 || epoch == 0) return false;
            Vector3d world = mainscript.GlobalFromUnityPos(item.useThisForPos.position);
            Quaternion rotation = item.useThisForPos.rotation;
            Vector3 velocity = item.RB.velocity;
            var sample = new VehiclePose { Id = networkId, Epoch = epoch, X = world.x, Y = world.y, Z = world.z,
                Qx = rotation.x, Qy = rotation.y, Qz = rotation.z, Qw = rotation.w,
                Vx = velocity.x, Vy = velocity.y, Vz = velocity.z };
            if (!VehiclePoseCodec.Valid(sample)) return false;
            pose = sample;
            return true;
        }
    }
}
