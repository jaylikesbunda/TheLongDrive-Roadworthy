using System;
using TldMp;

internal static class TrailerSyncTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        return Run();
    }

    static int Run()
    {
        // Ownership decision table: (hitched, native, towing car in charge) -> owner here.
        Check(TrailerSyncPolicy.InCharge(false, true, false), "unhitched host-owned trailer must keep the native answer");
        Check(!TrailerSyncPolicy.InCharge(false, false, true), "unhitched trailer must ignore the towing car");
        Check(TrailerSyncPolicy.InCharge(true, false, true), "hitched trailer must follow its car when this peer drives it, even if native says no");
        Check(!TrailerSyncPolicy.InCharge(true, true, false), "hitched trailer must not stay with the host when a guest drives its car");

        // Towing car owner (local driver, remote driver, host): local driver always wins; a parked car
        // (no local or remote driver) belongs to the host; a remote driver keeps it with that peer.
        Check(TrailerSyncPolicy.CarOwner(true, false, false), "local driver must own the car even as a guest with no remote driver");
        Check(TrailerSyncPolicy.CarOwner(true, false, true), "local driver must own the car as host with no remote driver");
        Check(TrailerSyncPolicy.CarOwner(true, true, false), "local driver must own the car while a remote peer also reports driving");
        Check(TrailerSyncPolicy.CarOwner(true, true, true), "local driver must own the car on the host while a remote peer also reports driving");
        Check(TrailerSyncPolicy.CarOwner(false, false, true), "host must own a parked car (nobody driving or pushing)");
        Check(!TrailerSyncPolicy.CarOwner(false, false, false), "guest must not own a parked car");
        Check(!TrailerSyncPolicy.CarOwner(false, true, true), "host must not own a car a remote peer is driving");
        Check(!TrailerSyncPolicy.CarOwner(false, true, false), "guest must not own a car a remote peer is driving");

        // Reject rule: a hitched trailer owned here drops inbound poses; everything else is accepted.
        Check(!TrailerSyncPolicy.AcceptPose(true, true), "owner must drop inbound poses for its hitched trailer");
        Check(TrailerSyncPolicy.AcceptPose(true, false), "non-owner must accept the owner's pose for a hitched trailer");
        Check(TrailerSyncPolicy.AcceptPose(false, true) && TrailerSyncPolicy.AcceptPose(false, false), "unhitched trailers keep the native pose path");

        // Smoothing: only a hitched trailer this machine does not own, and only with smoothing on.
        Check(TrailerSyncPolicy.Smoothed(true, false, true), "non-owner of a hitched trailer must smooth it");
        Check(!TrailerSyncPolicy.Smoothed(true, false, false), "smoothing off keeps the native teleport for trailers, like cars");
        Check(!TrailerSyncPolicy.Smoothed(true, true, true), "owner must not smooth its own trailer");
        Check(!TrailerSyncPolicy.Smoothed(false, false, true), "unhitched trailers are never smoothed");

        // Burst eligibility: only in a car burst tick, for a hitched trailer owned here.
        Check(TrailerSyncPolicy.ForwardWithCar(true, true, true), "owned hitched trailer must ride the car burst");
        Check(!TrailerSyncPolicy.ForwardWithCar(true, false, true), "a trailer owned elsewhere must not be forwarded");
        Check(!TrailerSyncPolicy.ForwardWithCar(true, true, false), "no forward outside a car burst tick");
        Check(!TrailerSyncPolicy.ForwardWithCar(false, true, true), "unhitched trailers are never forwarded with a car");

        Console.WriteLine("PASS: " + checks + " trailer sync policy assertions; hitched trailers follow their towing car for ownership, inbound pose acceptance, smoothing and burst forwarding, unhitched trailers keep the native rules.");
        return 0;
    }
}
