namespace TldMp.Experimental
{
    // Host-side only: the sender must own the exact vehicle generation. This is
    // structural validation, not a speed, collision or position plausibility test.
    internal static class VehicleAdmission
    {
        internal static bool Accept(AuthorityRegistry authority, ulong authenticatedSender, byte[] payload)
        {
            VehiclePose pose;
            return authority != null && VehiclePoseCodec.Read(payload, out pose) &&
                authority.Accepts(pose.Id, authenticatedSender, pose.Epoch);
        }
    }
}
