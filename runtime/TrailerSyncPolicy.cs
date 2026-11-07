namespace TldMp
{
    // Unity-free decisions for hitched trailers (see TrailerSync). Hitched trailers follow
    // their towing car: the trailer has no car script, so the native rule (host only) made
    // it host-owned. Unhitched trailers keep the native rule everywhere.
    internal static class TrailerSyncPolicy
    {
        // Owner of a hitched trailer's towing car. local is the car's native ThisOneInChargeCar
        // (this peer drives or pushes it). That answer is false on every peer while nobody is
        // driving or pushing the car, so a car no peer is driving falls back to the host, as
        // native items do. A remote driver or pusher (remoteActive) keeps the car with that peer.
        internal static bool CarOwner(bool local, bool remoteActive, bool isHost)
        {
            return local || (!remoteActive && isHost);
        }

        // Ownership: a hitched trailer follows its towing car (see CarOwner); otherwise the native answer.
        internal static bool InCharge(bool hitched, bool nativeInCharge, bool towingCarInCharge)
        {
            return hitched ? towingCarInCharge : nativeInCharge;
        }

        // Inbound global poses for a hitched trailer are dropped on the owning peer.
        internal static bool AcceptPose(bool hitched, bool ownedHere)
        {
            return !(hitched && ownedHere);
        }

        // Non-owners of a hitched trailer buffer its pose. Follows the car smoothing option
        // so the trailer and its car are either both smoothed or both teleported.
        internal static bool Smoothed(bool hitched, bool ownedHere, bool smoothing)
        {
            return hitched && !ownedHere && smoothing;
        }

        // A hitched trailer owned here is sent in the car's burst tick. The car burst already
        // requires a moving car owned here with a remote player, so this only adds the trailer.
        internal static bool ForwardWithCar(bool hitched, bool trailerInCharge, bool carBursting)
        {
            return hitched && trailerInCharge && carBursting;
        }
    }
}
