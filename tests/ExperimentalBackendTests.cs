using System;
using TldMp.Experimental;

internal static class ExperimentalBackendTests
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Experimental backend invariant failed at assertion " + checks); }

    public static int Main()
    {
        // Native justHost fallback is not active driving; a guest driver must
        // get a chance to claim before host-side control is compared.
        Check(!ExperimentalBackend.ProtectLocalCar(2, false));
        Check(ExperimentalBackend.ProtectLocalCar(6, false));
        Check(ExperimentalBackend.ProtectLocalCar(5, true));
        Check(!ExperimentalBackend.UseItemGate(true, null));
        Check(!ExperimentalBackend.UseItemGate(true, "0"));
        Check(ExperimentalBackend.UseItemGate(true, "1"));
        Check(!ExperimentalBackend.UseItemGate(false, "1"));
        ExperimentalBackend.Reset();
        uint[] ids; ulong[] owners; ulong[] epochs;
        Check(!ExperimentalBackend.TakeChanges(out ids, out owners, out epochs));
        Check(ExperimentalBackend.AllowPeer(10, 7, 2));
        Check(ExperimentalBackend.TakeChanges(out ids, out owners, out epochs) && ids.Length == 1 && ids[0] == 7 && owners[0] == 10 && epochs[0] == 1);
        Check(!ExperimentalBackend.TakeChanges(out ids, out owners, out epochs));
        Check(ExperimentalBackend.AllowPeer(10, 7, 4));
        Check(ExperimentalBackend.AllowPeer(10, 7, 2));
        Check(!ExperimentalBackend.TakeChanges(out ids, out owners, out epochs));
        Check(!ExperimentalBackend.AllowPeer(20, 7, 3));
        Check(ExperimentalBackend.Rejected == 1);
        Check(ExperimentalBackend.AllowPeer(20, 7, 6));
        Check(ExperimentalBackend.TakeChanges(out ids, out owners, out epochs) && owners[0] == 20 && epochs[0] == 2);
        Check(!ExperimentalBackend.AllowLocal(10, 7, 6));
        Check(ExperimentalBackend.AllowLocal(10, 7, 7));
        Check(ExperimentalBackend.TakeChanges(out ids, out owners, out epochs) && owners[0] == 10 && epochs[0] == 3);

        ExperimentalBackend.Reset();
        ulong owner, epoch;
        ExperimentalBackend.ApplyGrant(9, 30, 5);
        Check(ExperimentalBackend.OwnerOf(9, out owner, out epoch) && owner == 30 && epoch == 5);
        ExperimentalBackend.ApplyGrant(9, 40, 4);
        Check(ExperimentalBackend.OwnerOf(9, out owner, out epoch) && owner == 30 && epoch == 5);
        ExperimentalBackend.ApplyGrant(9, 40, 5);
        Check(ExperimentalBackend.OwnerOf(9, out owner, out epoch) && owner == 30 && epoch == 5);
        ExperimentalBackend.ApplyGrant(9, 40, 6);
        Check(ExperimentalBackend.OwnerOf(9, out owner, out epoch) && owner == 40 && epoch == 6);

        ExperimentalBackend.Reset();
        Check(ExperimentalBackend.AllowPeer(60, 12, 3));
        Check(ExperimentalBackend.AllowPeer(61, 13, 2));
        ExperimentalBackend.ForgetPeer(60);
        Check(!ExperimentalBackend.OwnerOf(12, out owner, out epoch));
        Check(ExperimentalBackend.OwnerOf(13, out owner, out epoch) && owner == 61);

        uint carId; ulong grantOwner, grantEpoch;
        byte[] grant = ExperimentalBackend.EncodeGrant(7, 10, 1);
        Check(grant.Length == ExperimentalBackend.GrantBytes);
        Check(ExperimentalBackend.TryDecodeGrant(grant, out carId, out grantOwner, out grantEpoch) && carId == 7 && grantOwner == 10 && grantEpoch == 1);
        Check(!ExperimentalBackend.TryDecodeGrant(new byte[19], out carId, out grantOwner, out grantEpoch));
        Check(!ExperimentalBackend.TryDecodeGrant(new byte[20], out carId, out grantOwner, out grantEpoch));
        Check(ExperimentalBackend.SignatureTag != null && ExperimentalBackend.SignatureTag.Length > 0);
        Check(ExperimentalBackend.Summary().IndexOf("cars=", StringComparison.Ordinal) == 0);

        Console.WriteLine("PASS: experimental vehicle authority ownership, grants and bounds");
        return 0;
    }
}
