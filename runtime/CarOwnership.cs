namespace TldMp
{
    // Car authority. Vanilla gives a car to the highest local role (driving 6 >
    // pushgripping 5 > shotgun 4 > pickedup 3 > justHost 2), so when the driving
    // host steps out a front-seat guest outranks the host and takes the car; each
    // handover snaps the car to the new owner's slightly different pose. A guest
    // passenger now never takes a car while the host is within physics range of
    // it: whoever drives owns it, otherwise the host does. Pushing or lifting a
    // car, and a host too far away to simulate it, keep the vanilla handover.
    internal static class CarOwnershipPolicy
    {
        internal const int Shotgun = 4;
        internal const double HostRangeMetres = 400.0;

        // hostDistance < 0 means unknown (vanilla rules apply).
        internal static bool VetoLocalCharge(bool connected, bool isHost, int localRole, double hostDistance)
        {
            return connected && !isHost && localRole == Shotgun && hostDistance >= 0 && hostDistance <= HostRangeMetres;
        }
    }

    // Received paint colours. A part whose paint the owner never set carries the
    // default colour (0,0,0,0); vanilla applies it to the material and the part
    // renders pure black on the receiver while it shows its prefab colour on the
    // owner. Real paint is opaque, so an all-zero colour is not applied.
    internal static class PaintPolicy
    {
        internal static bool IsUnsetPaint(float r, float g, float b, float a)
        {
            return r == 0f && g == 0f && b == 0f && a == 0f;
        }
    }
}
