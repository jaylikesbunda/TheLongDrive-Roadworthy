using System;
using TldMp.Experimental;

// Offline tests for the generic entity replication core
// (runtime/ExperimentalReplication.cs). Nothing here touches the game, the
// transport or a thread: it exercises the policy table, the bounded registry and
// the writer gate only.
internal static class ExperimentalReplicationTests
{
    static void Check(bool ok)
    {
        if (!ok) throw new Exception("Generic replication invariant failed");
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("Generic replication invariant failed: " + what);
    }

    static byte[] Bytes(int length, byte value)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++) data[i] = value;
        return data;
    }

    public static int Main()
    {
        PolicyTable();
        Registration();
        RevisionOrdering();
        OwnerPlayerGate();
        HostOnlyGate();
        FirstWriterGate();
        CarOwnerGate();
        TransferRules();
        RemovalAndTombstones();
        Caps();
        ForgetPeerTransfers();
        ForgetPeerExpires();
        VerifyAndSummary();

        Console.WriteLine("PASS: generic entity replication policy, records, epochs, revisions, tombstones");
        return 0;
    }

    // The exact policy table from the file header, one assertion per kind.
    static void PolicyTable()
    {
        Check(EntityPolicy.PolicyFor(EntityKind.Player) == AuthorityPolicy.OwnerPlayer, "player");
        Check(EntityPolicy.PolicyFor(EntityKind.Car) == AuthorityPolicy.CarOwner, "car");
        Check(EntityPolicy.PolicyFor(EntityKind.Item) == AuthorityPolicy.CarOwner, "item");
        Check(EntityPolicy.PolicyFor(EntityKind.Cargo) == AuthorityPolicy.CarOwner, "cargo");
        Check(EntityPolicy.PolicyFor(EntityKind.Creature) == AuthorityPolicy.LeasedInterest, "creature");
        Check(EntityPolicy.PolicyFor(EntityKind.Door) == AuthorityPolicy.HostOnly, "door");
        Check(EntityPolicy.PolicyFor(EntityKind.Poi) == AuthorityPolicy.HostOnly, "poi");
        Check(EntityPolicy.PolicyFor(EntityKind.Weather) == AuthorityPolicy.HostOnly, "weather");
        Check(EntityPolicy.PolicyFor(EntityKind.Gun) == AuthorityPolicy.FirstWriter, "gun");
        Check(EntityPolicy.PolicyFor(EntityKind.Damage) == AuthorityPolicy.FirstWriter, "damage");
        Check(EntityPolicy.PolicyFor(EntityKind.Prop) == AuthorityPolicy.FirstWriter, "prop");

        // Fail closed: the undefined kind and any byte cast from outside the table
        // have no writer at all.
        Check(EntityPolicy.PolicyFor(EntityKind.Unknown) == AuthorityPolicy.None, "unknown");
        Check(EntityPolicy.PolicyFor((EntityKind)200) == AuthorityPolicy.None, "cast");
        Check(!EntityPolicy.Known(EntityKind.Unknown), "known unknown");
        Check(!EntityPolicy.Known((EntityKind)200), "known cast");
        Check(EntityPolicy.Known(EntityKind.Player) && EntityPolicy.Known(EntityKind.Poi) && EntityPolicy.Known(EntityKind.Prop), "known members");
    }

    static void Registration()
    {
        var registry = new ReplicationRegistry(4, 4, 8);
        ReplicationRecord record;
        RejectReason why;

        Check(!registry.Register(0, EntityKind.Player, 10, Bytes(1, 1)), "zero id");
        Check(!registry.Register(1, EntityKind.Player, 0, Bytes(1, 1)), "zero owner");
        Check(!registry.Register(1, EntityKind.Unknown, 10, Bytes(1, 1)), "unknown kind");
        Check(!registry.Register(1, (EntityKind)99, 10, Bytes(1, 1)), "unmodelled kind");
        Check(!registry.Register(1, EntityKind.Player, 10, Bytes(9, 1)), "state over the bound");
        Check(registry.Count == 0, "nothing registered by the refused calls");

        Check(registry.Register(1, EntityKind.Player, 10, Bytes(2, 7)), "register");
        Check(registry.Register(2, EntityKind.Door, 10, null), "register without state");
        Check(!registry.Register(1, EntityKind.Player, 10, Bytes(1, 1)), "duplicate id");

        Check(registry.TryGet(1, out record), "get registered");
        Check(record.NetworkId == 1 && record.Owner == 10 && record.Epoch == 1 && record.Revision == 0, "record identity");
        Check(record.Kind == EntityKind.Player && !record.Removed, "record kind");
        Check(record.State != null && record.State.Length == 2 && record.State[0] == 7, "record state");

        Check(registry.TryGet(2, out record) && record.State == null && record.Kind == EntityKind.Door, "record without state");
        Check(!registry.TryGet(0, out record), "get zero id");
        Check(!registry.TryGet(3, out record), "get unknown id");
        Check(!registry.IsRemoved(1) && !registry.IsRemoved(3), "nothing removed yet");
        Check(registry.Verify(), "verify after registration");

        // An unknown entity cannot be first-writer registered through the gate: the
        // gate signature carries no kind, so the caller registers first. That makes
        // an unregistered id a refusal, never a blind claim.
        Check(!registry.TryAcceptState(3, 10, 1, 1, Bytes(1, 1), out why), "unregistered gate write");
        Check(why == RejectReason.UnknownEntity, "unregistered reason");
    }

    static void RevisionOrdering()
    {
        var registry = new ReplicationRegistry(4, 4, 8);
        RejectReason why;
        ReplicationRecord record;
        Check(registry.Register(1, EntityKind.Player, 42, null), "register");

        Check(!registry.TryAcceptState(1, 42, 1, 0, Bytes(1, 1), out why) && why == RejectReason.Invalid, "revision zero");
        Check(!registry.TryAcceptState(1, 42, 1, 3, null, out why) && why == RejectReason.Invalid, "null state");
        Check(!registry.TryAcceptState(0, 42, 1, 3, Bytes(1, 1), out why) && why == RejectReason.Invalid, "zero id");
        Check(!registry.TryAcceptState(1, 0, 1, 3, Bytes(1, 1), out why) && why == RejectReason.Invalid, "zero sender");
        Check(!registry.TryAcceptState(1, 42, 0, 3, Bytes(1, 1), out why) && why == RejectReason.Invalid, "zero epoch");
        Check(!registry.TryAcceptState(1, 42, 1, 3, Bytes(9, 1), out why) && why == RejectReason.StateTooLarge, "state over the bound");

        Check(registry.TryAcceptState(1, 42, 1, 1, Bytes(2, 5), out why) && why == RejectReason.Accepted, "first write");
        Check(registry.TryGet(1, out record) && record.Revision == 1 && record.State[0] == 5, "revision and state stored");

        Check(!registry.TryAcceptState(1, 42, 1, 1, Bytes(2, 6), out why) && why == RejectReason.StaleRevision, "equal revision");
        Check(!registry.TryAcceptState(1, 42, 1, 0, Bytes(2, 6), out why) && why == RejectReason.Invalid, "older revision zero");
        Check(!registry.TryAcceptState(1, 42, 1, 1, Bytes(2, 6), out why) && why == RejectReason.StaleRevision, "duplicate does not overwrite");
        Check(registry.TryGet(1, out record) && record.State[0] == 5, "state unchanged by the duplicates");

        Check(registry.TryAcceptState(1, 42, 1, 7, Bytes(3, 8), out why), "newer revision");
        Check(registry.TryGet(1, out record) && record.Revision == 7 && record.State[0] == 8, "newer revision applied");
        Check(!registry.TryAcceptState(1, 42, 1, 6, Bytes(1, 1), out why) && why == RejectReason.StaleRevision, "older after newer");

        // The caller's buffer is copied, so a later mutation cannot rewrite state.
        byte[] mine = Bytes(2, 9);
        Check(registry.TryAcceptState(1, 42, 1, 8, mine, out why), "accepted buffer");
        mine[0] = 111;
        Check(registry.TryGet(1, out record) && record.State[0] == 9, "accepted state is a copy");
        Check(registry.Verify(), "verify after revisions");
    }

    static void OwnerPlayerGate()
    {
        var registry = new ReplicationRegistry(4, 4, 8);
        RejectReason why;
        Check(registry.Register(7, EntityKind.Player, 42, null), "register player");

        Check(!registry.TryAcceptState(7, 43, 1, 1, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "foreign writer");
        Check(!registry.TryAcceptState(7, 42, 2, 1, Bytes(1, 1), out why) && why == RejectReason.EpochMismatch, "wrong epoch");
        Check(registry.TryAcceptState(7, 42, 1, 1, Bytes(1, 1), out why) && why == RejectReason.Accepted, "own player write");

        // A host sender gains nothing: OwnerPlayer has exactly one writer.
        registry.SetHost(100);
        Check(!registry.TryAcceptState(7, 100, 1, 2, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "host is not the owner");
    }

    static void HostOnlyGate()
    {
        var registry = new ReplicationRegistry(8, 8, 8);
        RejectReason why;

        // A host is required before anything is accepted; an unset host refuses.
        Check(registry.Register(5, EntityKind.Door, 100, null), "register door");
        Check(!registry.TryAcceptState(5, 100, 1, 1, Bytes(1, 1), out why) && why == RejectReason.HostUnset, "host unset");

        registry.SetHost(100);
        Check(registry.TryAcceptState(5, 100, 1, 1, Bytes(1, 1), out why) && why == RejectReason.Accepted, "host write");
        Check(!registry.TryAcceptState(5, 200, 1, 2, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "guest write");

        // A HostOnly record owned by a guest accepts nobody, host included: the
        // owner field and the sender must both be the host.
        Check(registry.Register(6, EntityKind.Weather, 200, null), "register weather for a guest");
        Check(!registry.TryAcceptState(6, 100, 1, 1, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "host on a guest record");
        Check(!registry.TryAcceptState(6, 200, 1, 1, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "guest on its own record");

        Check(registry.Register(8, EntityKind.Poi, 100, null), "register poi");
        Check(registry.TryAcceptState(8, 100, 1, 1, Bytes(1, 1), out why), "poi host write");

        // Clearing the host takes HostOnly writes back to the refusing state.
        registry.SetHost(0);
        Check(!registry.TryAcceptState(8, 100, 1, 2, Bytes(1, 1), out why) && why == RejectReason.HostUnset, "host cleared");
        Check(registry.Verify(), "verify after host-only");
    }

    static void FirstWriterGate()
    {
        var registry = new ReplicationRegistry(8, 8, 8);
        RejectReason why;
        ulong epoch;
        EntityKind[] kinds = new EntityKind[] { EntityKind.Gun, EntityKind.Damage, EntityKind.Prop };
        ulong id = 10;
        for (int i = 0; i < kinds.Length; i++)
        {
            ulong entity = id + (ulong)i;
            Check(registry.Register(entity, kinds[i], 10, null), "register first writer");
            Check(registry.TryAcceptState(entity, 10, 1, 1, Bytes(1, 1), out why) && why == RejectReason.Accepted, "first writer accepted");
            Check(!registry.TryAcceptState(entity, 30, 1, 2, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "second writer refused");
        }

        // Hand-over restarts the revision stream for the new writer.
        Check(registry.Transfer(10, 1, 30, out epoch) && epoch == 2, "transfer gun");
        Check(!registry.TryAcceptState(10, 10, 1, 2, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "old writer after transfer");
        Check(!registry.TryAcceptState(10, 30, 1, 2, Bytes(1, 1), out why) && why == RejectReason.EpochMismatch, "old epoch after transfer");
        Check(registry.TryAcceptState(10, 30, 2, 1, Bytes(1, 2), out why) && why == RejectReason.Accepted, "revision restarted at 1");
        Check(!registry.TryAcceptState(10, 30, 2, 1, Bytes(1, 3), out why) && why == RejectReason.StaleRevision, "equal revision after restart");
        Check(registry.Verify(), "verify after first writer");
    }

    static void CarOwnerGate()
    {
        var registry = new ReplicationRegistry(8, 8, 8);
        RejectReason why;
        ReplicationRecord record;

        Check(registry.Register(11, EntityKind.Car, 500, null), "register car");
        Check(registry.Register(12, EntityKind.Item, 500, null), "register item");
        Check(registry.Register(13, EntityKind.Cargo, 500, null), "register cargo");

        // No resolver: the record's owner field is the car owner.
        Check(registry.TryAcceptState(11, 500, 1, 1, Bytes(1, 1), out why) && why == RejectReason.Accepted, "car owner write");
        Check(!registry.TryAcceptState(11, 501, 1, 2, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "non car owner");

        // Resolver installed: the car decides, and the recorded owner is not
        // consulted for the write gate.
        registry.SetCarOwnerResolver(delegate(ulong entity) { return entity == 12 ? 600UL : 0UL; });
        Check(!registry.TryAcceptState(12, 500, 1, 1, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "stale record owner vs resolver");
        Check(registry.TryAcceptState(12, 600, 1, 1, Bytes(1, 1), out why) && why == RejectReason.Accepted, "resolved car owner");
        Check(registry.TryGet(12, out record) && record.Owner == 500, "resolver does not rewrite the record");

        // A resolver answer of 0 means "no car": a loose item falls back to its
        // recorded (first-writer) owner.
        Check(registry.TryAcceptState(13, 500, 1, 1, Bytes(1, 1), out why) && why == RejectReason.Accepted, "loose cargo owner");
        Check(!registry.TryAcceptState(13, 600, 1, 2, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "loose cargo foreign");

        // Removing the resolver returns to the recorded owner.
        registry.SetCarOwnerResolver(null);
        Check(registry.TryAcceptState(13, 500, 1, 2, Bytes(1, 1), out why), "owner after resolver removal");
        Check(!registry.TryAcceptState(12, 600, 1, 2, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "owner after resolver removal for item");
        Check(registry.Verify(), "verify after car owner");
    }

    static void TransferRules()
    {
        var registry = new ReplicationRegistry(4, 4, 4);
        ulong epoch;
        RejectReason why;
        Check(registry.Register(1, EntityKind.Car, 10, Bytes(1, 1)), "register");

        Check(!registry.Transfer(0, 1, 20, out epoch) && epoch == 0, "zero id");
        Check(!registry.Transfer(1, 0, 20, out epoch) && epoch == 0, "zero expected epoch");
        Check(!registry.Transfer(1, 1, 0, out epoch) && epoch == 0, "zero new owner");
        Check(!registry.Transfer(2, 1, 20, out epoch) && epoch == 0, "unknown id");
        Check(!registry.Transfer(1, 1, 10, out epoch) && epoch == 0, "same owner");
        Check(!registry.Transfer(1, 2, 20, out epoch) && epoch == 0, "wrong expected epoch");
        Check(registry.Transfer(1, 1, 20, out epoch) && epoch == 2, "transfer");
        Check(!registry.Transfer(1, 1, 30, out epoch), "replayed transfer");

        ReplicationRecord record;
        Check(registry.TryGet(1, out record) && record.Owner == 20 && record.Epoch == 2, "owner and epoch after transfer");
        Check(registry.TryAcceptState(1, 20, 2, 1, Bytes(1, 2), out why), "new owner write after transfer");
        Check(registry.Verify(), "verify after transfer");
    }

    static void RemovalAndTombstones()
    {
        var registry = new ReplicationRegistry(3, 2, 4);
        RejectReason why;
        ReplicationRecord record;

        Check(registry.Register(21, EntityKind.Player, 1, null), "register 21");
        Check(registry.Register(22, EntityKind.Player, 1, null), "register 22");
        Check(registry.Register(23, EntityKind.Player, 1, null), "register 23");
        Check(!registry.Remove(21, 2), "removal epoch mismatch");
        Check(!registry.Remove(99, 1), "removal of an unknown id");
        Check(registry.Remove(21, 1), "remove");
        Check(registry.IsRemoved(21), "tombstone exists");
        Check(registry.TryGet(21, out record) && record.Removed && record.Epoch == 1 && record.Kind == EntityKind.Player && record.State == null, "tombstone record");
        Check(!registry.Register(21, EntityKind.Player, 1, null), "no resurrection");
        Check(!registry.TryAcceptState(21, 1, 1, 1, Bytes(1, 1), out why) && why == RejectReason.Removed, "write to a removed id");
        Check(!registry.Remove(21, 1), "double removal");

        // The tombstone store holds two; the third removal is refused so the entity
        // stays live (removal is never silently forgotten).
        Check(registry.Remove(22, 1), "second tombstone");
        Check(!registry.Remove(23, 1), "tombstone store full");
        Check(registry.Count == 1 && registry.TombstoneCount == 2, "counts after the refused removal");
        Check(registry.TryAcceptState(23, 1, 1, 1, Bytes(1, 1), out why), "still writable");
        Check(registry.Verify(), "verify after removal");
    }

    static void Caps()
    {
        var registry = new ReplicationRegistry(2, 2, 4);
        Check(registry.Register(1, EntityKind.Item, 1, null), "register 1");
        Check(registry.Register(2, EntityKind.Item, 1, null), "register 2");
        Check(!registry.Register(3, EntityKind.Item, 1, null), "entity store full");
        Check(registry.Count == 2, "cap respected");
        Check(registry.MaxEntities == 2 && registry.MaxTombstones == 2 && registry.MaxStateBytes == 4, "limits exposed");

        // A refused registration is not a tombstone and leaves no trace.
        ReplicationRecord record;
        Check(!registry.TryGet(3, out record) && !registry.IsRemoved(3), "refused id is unknown");
        Check(registry.Summary().IndexOf("full=1", StringComparison.Ordinal) >= 0, "full counter");
        Check(registry.Verify(), "verify at the cap");

        bool threw = false;
        try { new ReplicationRegistry(0, 1, 1); } catch (ArgumentException) { threw = true; }
        Check(threw, "zero entity cap rejected");
        threw = false;
        try { new ReplicationRegistry(1, 0, 1); } catch (ArgumentException) { threw = true; }
        Check(threw, "zero tombstone cap rejected");
        threw = false;
        try { new ReplicationRegistry(1, 1, 0); } catch (ArgumentException) { threw = true; }
        Check(threw, "zero state cap rejected");
    }

    static void ForgetPeerTransfers()
    {
        var registry = new ReplicationRegistry(8, 8, 8);
        registry.SetHost(100);
        RejectReason why;
        ReplicationRecord record;

        Check(registry.Register(31, EntityKind.Player, 200, Bytes(1, 1)), "register player 200");
        Check(registry.Register(32, EntityKind.Car, 200, null), "register car 200");
        Check(registry.Register(33, EntityKind.Prop, 300, null), "register prop 300");
        Check(registry.TryAcceptState(31, 200, 1, 5, Bytes(1, 1), out why), "revision before forget");

        registry.ForgetPeer(200);
        Check(registry.TryGet(31, out record) && record.Owner == 100 && record.Epoch == 2 && record.Revision == 0, "player given to the host");
        Check(registry.TryGet(32, out record) && record.Owner == 100 && record.Epoch == 2, "car given to the host");
        Check(!registry.TryAcceptState(31, 200, 1, 6, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "old owner after forget");
        Check(!registry.TryAcceptState(31, 200, 2, 1, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "old owner with the new epoch");
        Check(registry.TryAcceptState(31, 100, 2, 1, Bytes(1, 1), out why), "host writes the transferred record");

        Check(registry.TryGet(33, out record) && record.Owner == 300 && record.Epoch == 1, "other peer untouched");
        registry.ForgetPeer(300);
        Check(registry.TryGet(33, out record) && record.Owner == 100 && record.Epoch == 2, "second peer given to the host");
        registry.ForgetPeer(0);
        Check(registry.TryGet(33, out record) && record.Owner == 100, "forget zero is a no-op");
        Check(registry.Summary().IndexOf("forgotten=3", StringComparison.Ordinal) >= 0, "forget counter");
        Check(registry.Verify(), "verify after forget");
    }

    static void ForgetPeerExpires()
    {
        RejectReason why;
        ReplicationRecord record;

        // No host configured: nothing can take the record, so it expires.
        var noHost = new ReplicationRegistry(8, 8, 4);
        Check(noHost.Register(51, EntityKind.Creature, 700, null), "register creature");
        noHost.ForgetPeer(700);
        Check(noHost.IsRemoved(51) && noHost.Count == 0 && noHost.TombstoneCount == 1, "expired without a host");
        Check(!noHost.TryAcceptState(51, 700, 1, 1, Bytes(1, 1), out why) && why == RejectReason.Removed, "expired record refuses");
        Check(!noHost.Register(51, EntityKind.Creature, 700, null), "expired id is not reusable");
        Check(noHost.Verify(), "verify after expiry");

        // The host itself leaving: expiry, not a transfer to itself.
        var hostLeft = new ReplicationRegistry(8, 8, 4);
        hostLeft.SetHost(400);
        Check(hostLeft.Register(41, EntityKind.Creature, 400, null), "register host creature");
        hostLeft.ForgetPeer(400);
        Check(hostLeft.IsRemoved(41), "host record expired");
        Check(hostLeft.TryGet(41, out record) && record.Removed && record.Epoch == 1, "host tombstone keeps the epoch");
        Check(hostLeft.Verify(), "verify after host expiry");

        // A full tombstone store skips the expiry instead of forgetting the id.
        var full = new ReplicationRegistry(4, 1, 4);
        full.SetHost(900);
        Check(full.Register(61, EntityKind.Player, 701, null), "register 61");
        Check(full.Register(62, EntityKind.Player, 701, null), "register 62");
        Check(full.Remove(61, 1), "fill the tombstone store");
        full.ForgetPeer(701);
        Check(!full.IsRemoved(62) && full.Count == 1, "expiry skipped when the store is full");
        Check(full.Verify(), "verify after the skipped expiry");
    }

    static void VerifyAndSummary()
    {
        var registry = new ReplicationRegistry(8, 8, 8);
        RejectReason why;
        Check(registry.Verify(), "empty registry verifies");
        Check(registry.Summary().IndexOf("entities=0", StringComparison.Ordinal) >= 0, "summary counts entities");

        Check(registry.Register(1, EntityKind.Car, 10, null), "register");
        Check(registry.TryAcceptState(1, 10, 1, 1, Bytes(1, 1), out why), "accept");
        Check(!registry.TryAcceptState(1, 20, 1, 2, Bytes(1, 1), out why) && why == RejectReason.NotOwner, "reject");
        Check(registry.Verify(), "verify after a rejection");

        string summary = registry.Summary();
        Check(summary.IndexOf("accepted=1", StringComparison.Ordinal) >= 0, "accepted counter");
        Check(summary.IndexOf("notOwner=1", StringComparison.Ordinal) >= 0, "rejection counter");
        Check(summary.IndexOf("host=", StringComparison.Ordinal) >= 0, "host in the summary");
    }
}
