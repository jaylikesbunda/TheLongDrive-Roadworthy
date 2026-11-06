using System;
using TldMp;
using TldMp.Experimental;

// Breakable/prop authority core tests (Phase 6). Console Check pattern: the first
// failed invariant throws, so a non-zero exit means a real regression. No Unity,
// no game assembly, no transport: the core is compiled with the native packet
// validator only.
internal static class ExperimentalBreakablesTests
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Breakable authority invariant failed at check " + checks); }
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message + " (check " + checks + ")"); }

    const ulong Host = 1, A = 10, B = 20, C = 30;
    const double Lease = 2.0;

    static byte[] Msg(int type, int length) { var b = new byte[length]; b[0] = (byte)type; return b; }
    static byte[] Put(byte[] b, int at, int value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 4); return b; }

    // Native framings follow StablePackets.Valid exactly (length = 1 + fields).
    static byte[] Damage(int id) { return Put(Msg(33, 17), 1, id); }          // breakable: i f f f
    static byte[] RemoveItem(int id) { return Put(Msg(36, 5), 1, id); }       // removeItem: i
    static byte[] Paint(int id) { return Put(Msg(32, 25), 1, id); }           // color: i i c
    static byte[] Push(int id) { return Put(Msg(29, 37), 9, id); }            // push: t i v v

    static bool Refused(ulong host, int maxEntities, int maxTombstones, double lease)
    {
        try { new BreakableAuthority(host, maxEntities, maxTombstones, lease); return false; }
        catch (ArgumentException) { return true; }
    }

    static void Enums()
    {
        // The kind value is the native message type and is never written to a wire.
        Check((byte)BreakableKind.Damage == 33, "breakable kind keeps the native type 33");
        Check((byte)BreakableKind.Remove == 36, "breakable kind keeps the native type 36");
        Check((byte)BreakableKind.Paint == 32, "breakable kind keeps the native type 32");
        Check((byte)BreakableKind.Push == 29, "breakable kind keeps the native type 29");
        Check((byte)BreakableKind.Unknown == 0, "unknown kind is zero");

        // The decision order is part of the contract a caller logs; keep it stable.
        Check((byte)BreakableDecision.Accepted == 0, "decision Accepted is 0");
        Check((byte)BreakableDecision.OwnerMismatch == 1, "decision OwnerMismatch is 1");
        Check((byte)BreakableDecision.AlreadyBroken == 2, "decision AlreadyBroken is 2");
        Check((byte)BreakableDecision.UnknownEntity == 3, "decision UnknownEntity is 3");
        Check((byte)BreakableDecision.Full == 4, "decision Full is 4");
        Check((byte)BreakableDecision.Invalid == 5, "decision Invalid is 5");
    }

    static void Policy()
    {
        Check(BreakablePolicy.IsBreakableMessage(29), "push is a breakable message");
        Check(BreakablePolicy.IsBreakableMessage(32), "color is a breakable message");
        Check(BreakablePolicy.IsBreakableMessage(33), "breakable is a breakable message");
        Check(BreakablePolicy.IsBreakableMessage(36), "removeItem is a breakable message");

        Check(BreakablePolicy.KindOf(33) == BreakableKind.Damage, "33 maps to Damage");
        Check(BreakablePolicy.KindOf(36) == BreakableKind.Remove, "36 maps to Remove");
        Check(BreakablePolicy.KindOf(32) == BreakableKind.Paint, "32 maps to Paint");
        Check(BreakablePolicy.KindOf(29) == BreakableKind.Push, "29 maps to Push");

        // Exhaustive over the byte range: exactly four types, and kind and type
        // always agree in both directions.
        int mapped = 0;
        for (int type = 0; type < 256; type++)
        {
            bool breakable = BreakablePolicy.IsBreakableMessage((byte)type);
            BreakableKind kind = BreakablePolicy.KindOf((byte)type);
            Check(breakable == (kind != BreakableKind.Unknown), "type " + type + " disagrees with its kind");
            if (!breakable) continue;
            mapped++;
            Check((byte)kind == type, "kind of type " + type + " must equal the type");
        }
        Check(mapped == 4, "exactly four native types are breakable messages");
        Check(BreakablePolicy.KindOf(16) == BreakableKind.Unknown, "itemPosGlobal is not a breakable message");
        Check(BreakablePolicy.KindOf(43) == BreakableKind.Unknown, "ragdoll is not a breakable message");
        Check(BreakablePolicy.KindOf(0) == BreakableKind.Unknown, "a zero type is not a breakable message");
        Check(!BreakablePolicy.IsBreakableMessage(255), "255 is not a breakable message");
    }

    static void Entity()
    {
        uint id;
        Check(BreakablePolicy.TryEntity(Damage(7), out id) && id == 7, "breakable carries its entity id");
        Check(BreakablePolicy.TryEntity(RemoveItem(7), out id) && id == 7, "removeItem carries its entity id");
        Check(BreakablePolicy.TryEntity(Paint(7), out id) && id == 7, "color carries its entity id");
        Check(BreakablePolicy.TryEntity(Push(7), out id) && id == 7, "push carries its entity id");
        Check(BreakablePolicy.TryEntity(Push(unchecked((int)UInt32.MaxValue)), out id) && id == UInt32.MaxValue, "a maximum id is read");

        // Malformed or unmodelled packets are never this layer's decision.
        Check(!BreakablePolicy.TryEntity(null, out id) && id == 0, "a null packet has no entity");
        Check(!BreakablePolicy.TryEntity(new byte[0], out id) && id == 0, "an empty packet has no entity");
        Check(!BreakablePolicy.TryEntity(Msg(33, 16), out id), "a truncated breakable is refused");
        Check(!BreakablePolicy.TryEntity(Msg(33, 21), out id), "an over-long breakable is refused");
        Check(!BreakablePolicy.TryEntity(Msg(36, 4), out id), "a truncated removeItem is refused");
        Check(!BreakablePolicy.TryEntity(Msg(32, 21), out id), "a truncated color is refused");
        Check(!BreakablePolicy.TryEntity(Msg(29, 33), out id), "a truncated push is refused");
        Check(!BreakablePolicy.TryEntity(new byte[] { 33, 1, 2, 3, 4 }, out id), "a short breakable is refused");
        Check(!BreakablePolicy.TryEntity(Put(Msg(16, 45), 1, 7), out id), "itemPosGlobal is not extracted");
        Check(!BreakablePolicy.TryEntity(Msg(40, 30), out id), "a usable is not extracted");
        Check(!BreakablePolicy.TryEntity(Msg(31, 13), out id), "weaponShot is not extracted");

        // Zero ids belong to no entity. A push stores its id at offset 9, so a
        // zero at offset 1 is not a zero entity.
        Check(!BreakablePolicy.TryEntity(Damage(0), out id) && id == 0, "a zero breakable id is refused");
        Check(!BreakablePolicy.TryEntity(RemoveItem(0), out id) && id == 0, "a zero removeItem id is refused");
        Check(!BreakablePolicy.TryEntity(Paint(0), out id) && id == 0, "a zero color id is refused");
        Check(!BreakablePolicy.TryEntity(Push(0), out id) && id == 0, "a zero push id is refused");
        var pushAtWrongOffset = Msg(29, 37);
        Put(pushAtWrongOffset, 1, 7);
        Check(!BreakablePolicy.TryEntity(pushAtWrongOffset, out id) && id == 0, "push reads its id at offset 9 only");
    }

    static void Construction()
    {
        Check(Refused(0, 8, 8, 1.0), "a zero host is refused");
        Check(Refused(Host, 0, 8, 1.0), "a zero entity cap is refused");
        Check(Refused(Host, 8, 0, 1.0), "a zero tombstone cap is refused");
        Check(Refused(Host, -1, 8, 1.0), "a negative entity cap is refused");
        Check(Refused(Host, 8, 8, 0.0), "a zero lease is refused");
        Check(Refused(Host, 8, 8, -1.0), "a negative lease is refused");
        Check(Refused(Host, 8, 8, Double.NaN), "a NaN lease is refused");

        var auth = new BreakableAuthority(Host, 8, 8, Lease);
        Check(auth.Host == Host, "the host is retained");
        Check(auth.Count == 0 && auth.TombstoneCount == 0, "a new authority is empty");
        Check(auth.Verify(), "an empty authority verifies");
        Check(auth.Summary() == "entities=0 tombstones=0 accepted=0 ownerMismatch=0 alreadyBroken=0 unknown=0 full=0",
            "the empty summary has the documented shape: " + auth.Summary());
    }

    static void Claims()
    {
        ulong owner, epoch;
        var auth = new BreakableAuthority(Host, 8, 8, Lease);

        // First-writer claim: an unknown id is registered to whoever writes first.
        Check(auth.AllowWrite(A, 7, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "the first guest claim is accepted");
        Check(auth.TryOwner(7, out owner, out epoch) && owner == A && epoch == 1, "the claimant owns the entity at epoch 1");
        Check(!auth.IsBroken(7), "a claim does not break the entity");
        Check(auth.Count == 1 && auth.TombstoneCount == 0, "one live entity, no tombstone");
        Check(auth.Classify(7) == BreakableDecision.Accepted, "a live entity classifies as accepted");

        // The second guest loses to the live lease.
        Check(auth.AllowWrite(B, 7, BreakableKind.Damage, 0.5) == BreakableDecision.OwnerMismatch, "a second guest is refused");
        Check(auth.TryOwner(7, out owner, out epoch) && owner == A && epoch == 1, "a refused write changes nothing");

        // The owner keeps it and each accepted write refreshes the lease.
        Check(auth.AllowWrite(A, 7, BreakableKind.Paint, 1.5) == BreakableDecision.Accepted, "the owner may paint");
        Check(auth.AllowWrite(A, 7, BreakableKind.Push, 2.5) == BreakableDecision.Accepted, "the owner may push");
        Check(auth.AllowWrite(A, 7, BreakableKind.Damage, 3.5) == BreakableDecision.Accepted, "the owner may damage");
        // Only 1.0 s after the last refresh, so the lease is still live: the
        // refresh above, not the original claim time, is what kept it.
        Check(auth.AllowWrite(B, 7, BreakableKind.Damage, 4.0) == BreakableDecision.OwnerMismatch, "the refreshed lease still holds");
        Check(auth.AllowWrite(B, 8, BreakableKind.Damage, 4.0) == BreakableDecision.Accepted, "another unknown id is claimed by the second guest");
        Check(auth.TryOwner(8, out owner, out epoch) && owner == B && epoch == 1, "the second claimant owns its own entity");
        Check(auth.Count == 2, "two live entities");
        Check(auth.Verify(), "claims verify");
        Check(auth.Summary().Contains("unknown=2 "), "both claims came from unknown ids");
    }

    static void HostOverride()
    {
        ulong owner, epoch;
        var auth = new BreakableAuthority(Host, 8, 8, Lease);
        Check(auth.AllowWrite(A, 7, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "a guest claims the entity");

        // The host is authoritative: it becomes the owner with a new epoch even
        // though the guest's lease is fresh.
        Check(auth.AllowWrite(Host, 7, BreakableKind.Damage, 0.1) == BreakableDecision.Accepted, "the host overrides a fresh guest claim");
        Check(auth.TryOwner(7, out owner, out epoch) && owner == Host && epoch == 2, "the override carries a new epoch");
        Check(auth.AllowWrite(A, 7, BreakableKind.Damage, 0.2) == BreakableDecision.OwnerMismatch, "the overridden guest cannot write");

        // The host writing again is not a new ownership: the epoch stays.
        Check(auth.AllowWrite(Host, 7, BreakableKind.Paint, 0.3) == BreakableDecision.Accepted, "the host keeps writing");
        Check(auth.TryOwner(7, out owner, out epoch) && owner == Host && epoch == 2, "a repeated host write does not bump the epoch");

        // The host is also a normal first writer of an unknown id.
        Check(auth.AllowWrite(Host, 9, BreakableKind.Push, 0.0) == BreakableDecision.Accepted, "the host claims an unknown id");
        Check(auth.TryOwner(9, out owner, out epoch) && owner == Host && epoch == 1, "the host claim starts at epoch 1");

        // A stale guest claim is overridden just the same.
        Check(auth.AllowWrite(B, 10, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "a guest claims a third entity");
        Check(auth.AllowWrite(Host, 10, BreakableKind.Push, 5.0) == BreakableDecision.Accepted, "the host overrides after the lease is stale");
        Check(auth.TryOwner(10, out owner, out epoch) && owner == Host && epoch == 2, "the stale override carries a new epoch");
        Check(auth.Verify(), "host overrides verify");
    }

    static void StaleTakeover()
    {
        ulong owner, epoch;
        var auth = new BreakableAuthority(Host, 8, 8, Lease);
        Check(auth.AllowWrite(A, 7, BreakableKind.Damage, 10.0) == BreakableDecision.Accepted, "the first owner claims at t=10");

        // Exactly at the lease edge the lease is still live (strictly greater).
        Check(auth.AllowWrite(B, 7, BreakableKind.Damage, 12.0) == BreakableDecision.OwnerMismatch, "a lease at its edge is live");
        // Past it the next guest takes over with a new epoch.
        Check(auth.AllowWrite(B, 7, BreakableKind.Damage, 12.001) == BreakableDecision.Accepted, "a stale lease is taken over");
        Check(auth.TryOwner(7, out owner, out epoch) && owner == B && epoch == 2, "the takeover carries a new epoch");
        // The previous owner is gone for good: its own later write must wait for
        // the new lease to go stale.
        Check(auth.AllowWrite(A, 7, BreakableKind.Damage, 13.0) == BreakableDecision.OwnerMismatch, "the previous owner lost the entity");
        Check(auth.AllowWrite(C, 7, BreakableKind.Paint, 20.0) == BreakableDecision.Accepted, "a third guest takes over a stale lease");
        Check(auth.TryOwner(7, out owner, out epoch) && owner == C && epoch == 3, "each takeover increments the epoch");
        Check(auth.AllowWrite(C, 7, BreakableKind.Push, 20.5) == BreakableDecision.Accepted, "the newest owner writes");
        Check(auth.TryOwner(7, out owner, out epoch) && owner == C && epoch == 3, "the newest owner keeps its epoch");
        Check(auth.Verify(), "takeovers verify");
    }

    static void Removals()
    {
        ulong owner, epoch;
        var auth = new BreakableAuthority(Host, 8, 8, Lease);

        // A non-owner cannot remove a live entity; the owner can.
        Check(auth.AllowWrite(A, 7, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "the owner claims entity 7");
        Check(auth.AllowWrite(B, 7, BreakableKind.Remove, 0.1) == BreakableDecision.OwnerMismatch, "a non-owner cannot remove");
        Check(!auth.IsBroken(7), "a refused removal breaks nothing");
        Check(auth.AllowWrite(A, 7, BreakableKind.Remove, 0.2) == BreakableDecision.Accepted, "the owner removes it");
        Check(auth.IsBroken(7), "the removed entity is broken");
        Check(auth.TombstoneCount == 1 && auth.Count == 0, "the removed entity is tombstoned, not live");
        Check(!auth.TryOwner(7, out owner, out epoch), "a finished entity has no owner");

        // Broken is terminal for every kind and every sender, the host included.
        Check(auth.AllowWrite(A, 7, BreakableKind.Damage, 0.3) == BreakableDecision.AlreadyBroken, "damage after break is refused");
        Check(auth.AllowWrite(A, 7, BreakableKind.Remove, 0.3) == BreakableDecision.AlreadyBroken, "a repeated remove is refused");
        Check(auth.AllowWrite(B, 7, BreakableKind.Remove, 100.0) == BreakableDecision.AlreadyBroken, "a stale guest cannot take a broken entity");
        Check(auth.AllowWrite(Host, 7, BreakableKind.Damage, 0.4) == BreakableDecision.AlreadyBroken, "even the host cannot damage a broken entity");
        Check(auth.AllowWrite(Host, 7, BreakableKind.Remove, 0.4) == BreakableDecision.AlreadyBroken, "the host cannot remove a broken entity twice");

        // A stale non-owner removal applies to a live entity: the claim and the
        // removal are one write.
        Check(auth.AllowWrite(B, 8, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "the second guest claims entity 8");
        Check(auth.AllowWrite(C, 8, BreakableKind.Remove, 50.0) == BreakableDecision.Accepted, "a stale lease lets a guest remove");
        Check(auth.IsBroken(8), "the taken-over entity is broken");

        // The host removes a guest-owned entity without waiting for a lease.
        Check(auth.AllowWrite(A, 9, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "a guest claims entity 9");
        Check(auth.AllowWrite(Host, 9, BreakableKind.Remove, 0.1) == BreakableDecision.Accepted, "the host removes a guest-owned entity");
        Check(auth.IsBroken(9), "the host removal breaks it");
        Check(auth.AllowWrite(A, 9, BreakableKind.Damage, 0.2) == BreakableDecision.AlreadyBroken, "the guest cannot revive it");
        Check(auth.AllowWrite(Host, 9, BreakableKind.Remove, 0.3) == BreakableDecision.AlreadyBroken, "no second host removal");
        Check(auth.TombstoneCount == 3 && auth.Count == 0, "every removed entity is tombstoned");
        Check(auth.Verify(), "removals verify");
    }

    static void Tombstones()
    {
        ulong owner, epoch;
        var auth = new BreakableAuthority(Host, 8, 8, Lease);
        Check(auth.AllowWrite(A, 11, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "entity 11 is claimed");
        Check(auth.TryOwner(11, out owner, out epoch) && owner == A && epoch == 1, "entity 11 is owned at epoch 1");

        // The explicit tombstone needs a real entity and the live epoch.
        Check(!auth.Remove(0, 1), "a zero entity cannot be tombstoned");
        Check(!auth.Remove(11, 0), "a zero epoch cannot tombstone");
        Check(!auth.Remove(11, 2), "a stale epoch cannot tombstone");
        Check(!auth.Remove(12, 1), "an unknown entity cannot be tombstoned");
        Check(auth.Remove(11, 1), "the owner epoch tombstones the entity");
        Check(auth.IsBroken(11), "a tombstoned entity is broken");
        Check(auth.TombstoneCount == 1 && auth.Count == 0, "the tombstone replaced the live entry");
        Check(!auth.Remove(11, 1), "a tombstone cannot be written twice");

        // No resurrection: neither another peer nor the host can claim it back.
        Check(auth.AllowWrite(B, 11, BreakableKind.Damage, 0.0) == BreakableDecision.AlreadyBroken, "a tombstoned id is never claimable");
        Check(auth.AllowWrite(Host, 11, BreakableKind.Remove, 0.0) == BreakableDecision.AlreadyBroken, "the host cannot resurrect a tombstone");
        Check(auth.Classify(11) == BreakableDecision.AlreadyBroken, "a tombstone classifies as already broken");
        Check(auth.Count == 0, "no resurrection left a live entry");

        // A live entity can be finalised explicitly as well.
        Check(auth.AllowWrite(B, 12, BreakableKind.Paint, 0.0) == BreakableDecision.Accepted, "entity 12 is claimed");
        Check(auth.Remove(12, 1), "a live entity can be finalised by its epoch");
        Check(auth.IsBroken(12) && auth.TombstoneCount == 2, "the finalised entity is tombstoned");
        Check(auth.Verify(), "tombstones verify");
    }

    static void Caps()
    {
        var caps = new BreakableAuthority(Host, 2, 4, 2.0);
        Check(caps.AllowWrite(A, 1, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "the first entity fits");
        Check(caps.AllowWrite(A, 2, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "the second entity fits");
        Check(caps.AllowWrite(A, 3, BreakableKind.Damage, 0.0) == BreakableDecision.Full, "a full registry refuses a claim");
        Check(caps.Count == 2, "a refused claim registers nothing");
        Check(caps.Classify(3) == BreakableDecision.UnknownEntity, "a refused claim is still unknown");
        Check(caps.Summary().EndsWith("full=1"), "the refusal is counted: " + caps.Summary());

        // The cap is on live entities, so a tombstone frees a slot.
        Check(caps.AllowWrite(A, 1, BreakableKind.Remove, 0.1) == BreakableDecision.Accepted, "the first entity is removed");
        Check(caps.Count == 1 && caps.TombstoneCount == 1, "the tombstone freed the slot");
        Check(caps.AllowWrite(B, 3, BreakableKind.Damage, 0.2) == BreakableDecision.Accepted, "the freed slot is claimable");
        Check(caps.Count == 2, "the registry is full again");
        Check(caps.Verify(), "capped claims verify");

        // Tombstone cap: an entity that cannot be tombstoned stays registered
        // broken, which refuses every later write exactly like a tombstone and
        // still occupies a live slot.
        var tight = new BreakableAuthority(Host, 2, 1, 2.0);
        Check(tight.AllowWrite(A, 1, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "tight: entity 1 claimed");
        Check(tight.AllowWrite(A, 2, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "tight: entity 2 claimed");
        Check(tight.AllowWrite(A, 1, BreakableKind.Remove, 0.1) == BreakableDecision.Accepted, "tight: entity 1 removed");
        Check(tight.TombstoneCount == 1 && tight.Count == 1, "tight: the only tombstone is used");
        Check(tight.AllowWrite(A, 2, BreakableKind.Remove, 0.2) == BreakableDecision.Accepted, "tight: entity 2 is removed without a tombstone");
        Check(tight.IsBroken(2), "tight: entity 2 is broken anyway");
        Check(tight.TombstoneCount == 1 && tight.Count == 1, "tight: the broken entity stayed registered");
        Check(tight.AllowWrite(A, 2, BreakableKind.Damage, 0.3) == BreakableDecision.AlreadyBroken, "tight: a broken entry refuses writes");
        Check(!tight.Remove(2, 1), "tight: no tombstone room, so finalisation is refused");
        Check(tight.IsBroken(2), "tight: the refused finalisation keeps it broken");
        Check(tight.Classify(2) == BreakableDecision.AlreadyBroken, "tight: it classifies as broken");
        Check(tight.AllowWrite(A, 3, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "tight: one slot is left");
        Check(tight.AllowWrite(A, 4, BreakableKind.Damage, 0.1) == BreakableDecision.Full, "tight: the broken entry still holds its slot");
        Check(tight.Verify(), "tight caps verify");
    }

    static void ForgetPeer()
    {
        ulong owner, epoch;
        var auth = new BreakableAuthority(Host, 8, 1, Lease);
        Check(auth.AllowWrite(A, 31, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "peer A owns entity 31");
        Check(auth.AllowWrite(A, 32, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "peer A owns entity 32");
        Check(auth.AllowWrite(A, 33, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "peer A owns entity 33");
        Check(auth.AllowWrite(B, 34, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "peer B owns entity 34");
        Check(auth.AllowWrite(A, 32, BreakableKind.Remove, 0.1) == BreakableDecision.Accepted, "entity 32 becomes a tombstone");
        Check(auth.TombstoneCount == 1 && auth.Count == 3, "one tombstone, three live entities");
        Check(auth.AllowWrite(A, 33, BreakableKind.Remove, 0.2) == BreakableDecision.Accepted, "entity 33 breaks without a tombstone");
        Check(auth.TombstoneCount == 1 && auth.Count == 3 && auth.IsBroken(33), "entity 33 stays registered broken");

        auth.ForgetPeer(A, 1.0);

        // Every live entity of the departed peer moves to the host with a new
        // epoch; the other peer keeps its entity.
        Check(auth.TryOwner(31, out owner, out epoch) && owner == Host && epoch == 2, "a live entity falls back to the host with a new epoch");
        Check(auth.TryOwner(34, out owner, out epoch) && owner == B && epoch == 1, "another peer's entity is untouched");
        Check(auth.IsBroken(32) && auth.IsBroken(33), "finished entities stay finished");
        Check(!auth.TryOwner(33, out owner, out epoch), "a broken entity has no owner lookup");

        // The departed peer cannot write through its old ownership; the host can.
        Check(auth.AllowWrite(A, 31, BreakableKind.Damage, 1.1) == BreakableDecision.OwnerMismatch, "the departed peer lost its entity");
        Check(auth.AllowWrite(Host, 31, BreakableKind.Damage, 1.2) == BreakableDecision.Accepted, "the host now owns it");
        Check(auth.TryOwner(31, out owner, out epoch) && owner == Host && epoch == 2, "the fallback epoch did not move again");
        Check(auth.AllowWrite(Host, 33, BreakableKind.Damage, 1.3) == BreakableDecision.AlreadyBroken, "a disconnect resurrects nothing");

        // Zero and the host are no-ops: the host never hands its own entities away.
        auth.ForgetPeer(0, 2.0);
        auth.ForgetPeer(Host, 2.0);
        Check(auth.TryOwner(34, out owner, out epoch) && owner == B && epoch == 1, "a zero or host forget changes nothing");
        Check(auth.TryOwner(31, out owner, out epoch) && owner == Host && epoch == 2, "the host keeps what it inherited");
        Check(auth.Verify(), "the fallback state verifies");
    }

    static void Decisions()
    {
        ulong owner, epoch;
        var auth = new BreakableAuthority(Host, 8, 8, Lease);
        Check(auth.AllowWrite(A, 1, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "entity 1 is claimed");
        Check(auth.AllowWrite(B, 1, BreakableKind.Damage, 0.0) == BreakableDecision.OwnerMismatch, "a second guest is refused");
        Check(auth.AllowWrite(A, 1, BreakableKind.Remove, 0.1) == BreakableDecision.Accepted, "the owner removes it");
        Check(auth.AllowWrite(A, 1, BreakableKind.Damage, 0.2) == BreakableDecision.AlreadyBroken, "the break latches");
        Check(auth.AllowWrite(Host, 2, BreakableKind.Damage, 0.0) == BreakableDecision.Accepted, "the host claims entity 2");
        Check(auth.TryOwner(2, out owner, out epoch) && owner == Host && epoch == 1, "the host claim is the live owner at epoch 1");

        // Invalid caller input never changes state and is never counted as a
        // peer's decision.
        Check(auth.AllowWrite(0, 3, BreakableKind.Damage, 0.0) == BreakableDecision.Invalid, "a zero sender is invalid");
        Check(auth.AllowWrite(A, 0, BreakableKind.Damage, 0.0) == BreakableDecision.Invalid, "a zero entity is invalid");
        Check(auth.AllowWrite(A, 4, BreakableKind.Unknown, 0.0) == BreakableDecision.Invalid, "an unknown kind is invalid");
        Check(auth.AllowWrite(A, 5, (BreakableKind)34, 0.0) == BreakableDecision.Invalid, "an unmodelled kind is invalid");
        Check(auth.AllowWrite(A, 6, BreakableKind.Paint, Double.NaN) == BreakableDecision.Invalid, "a NaN clock is invalid");
        Check(auth.AllowWrite(A, 6, BreakableKind.Paint, Double.PositiveInfinity) == BreakableDecision.Invalid, "an infinite clock is invalid");
        Check(auth.AllowWrite(0, 0, BreakableKind.Unknown, Double.NaN) == BreakableDecision.Invalid, "every invalid field at once is invalid");
        Check(auth.Count == 1, "invalid input registers nothing");
        Check(auth.Classify(3) == BreakableDecision.UnknownEntity, "the refused entity is still unknown");
        Check(auth.Classify(0) == BreakableDecision.Invalid, "a zero entity classifies as invalid");

        // Classify is read-only, so the counters must not move.
        string before = auth.Summary();
        Check(auth.Classify(1) == BreakableDecision.AlreadyBroken, "a tombstone classifies as broken");
        Check(auth.Classify(2) == BreakableDecision.Accepted, "a live entity classifies as accepted");
        Check(auth.Classify(9) == BreakableDecision.UnknownEntity, "an unregistered entity classifies as unknown");
        Check(auth.Summary() == before, "classification changes no counter");

        Check(auth.Summary() == "entities=1 tombstones=1 accepted=3 ownerMismatch=1 alreadyBroken=1 unknown=2 full=0",
            "the counters mirror the decisions: " + auth.Summary());
        Check(auth.Verify(), "the final state verifies");
    }

    public static int Main()
    {
        Enums();
        Policy();
        Entity();
        Construction();
        Claims();
        HostOverride();
        StaleTakeover();
        Removals();
        Tombstones();
        Caps();
        ForgetPeer();
        Decisions();
        Console.WriteLine("PASS: breakable/prop authority core (" + checks + " checks): kind/decision enums, native type and entity mapping, first-writer claims, owner refresh, host override, stale takeover, broken latch, tombstone, caps, ForgetPeer, Verify and Summary");
        return 0;
    }
}
