using System;
using TldMp;
using TldMp.Experimental;

// Item/cargo event-stream tests (Phase 4). Console Check pattern: the first failed
// invariant throws, so a non-zero exit means a real regression. Covers ordering,
// the authority rejections, the parent dependency buffer for 17 and 42, drain order,
// every bound, expiry with the repair schedule and backoff, Remove cancellation, the
// join journal barrier and Verify.
internal static class ExperimentalItemStreamTests
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Item event stream invariant failed at check " + checks); }
    static void Check(bool ok, string what) { checks++; if (!ok) throw new Exception("Item event stream invariant failed at check " + checks + ": " + what); }

    static byte[] Msg(int type, int length) { var b = new byte[length]; b[0] = (byte)type; return b; }

    static byte[] Put(byte[] b, int at, int value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 4);
        return b;
    }

    // Native framings follow StablePackets.Valid exactly (length = 1 + fields).
    static byte[] WorldPose(int id) { return Put(Msg(16, 45), 1, id); }
    static byte[] Velocity(int id) { return Put(Msg(19, 29), 1, id); }

    // itemPosLocal (17): id @1, parentID @5, physlock index @9, lpos @13, lrot @25.
    static byte[] LocalPose(int id, int parent, int index) { return Put(Put(Put(Msg(17, 37), 1, id), 5, parent), 9, index); }

    // attachable (42): id @1, attachType @5, parentid @13 (attachType 1 is an item).
    static byte[] AttachTo(int id, int parent) { return Put(Put(Put(Msg(42, 121), 1, id), 5, 1), 13, parent); }
    static byte[] DetachFrom(int id) { return Put(Put(Msg(42, 9), 1, id), 5, 0); }
    static byte[] RemoveItem(int id) { return Put(Msg(36, 5), 1, id); }
    static byte[] Outfit(int id) { return Put(Msg(48, 17), 1, id); }

    static ItemEvent Make(ulong entity, ulong owner, ulong epoch, ulong id, ItemEventKind kind, byte[] payload)
    {
        return new ItemEvent
        {
            Entity = entity, Owner = owner, Epoch = epoch, EventId = id, Kind = kind,
            Flags = (byte)ItemEventPolicy.FlagsFor(kind), Payload = payload
        };
    }

    // A registered loose item, the shape most tests offer.
    static ItemEvent Pose(ulong entity, ulong id) { return Make(entity, 100, 1, id, ItemEventKind.PoseWorld, WorldPose((int)entity)); }

    static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static ItemEventStream New(out ItemAuthority authority, int total, int perEntity, int bytes, double expiry)
    {
        authority = new ItemAuthority();
        var stream = new ItemEventStream(authority, total, perEntity, bytes, expiry);
        Check(stream.Verify());
        Check(stream.Summary().Contains("applied=") && stream.Summary().Contains("overflow=") &&
            stream.Summary().Contains("expired=") && stream.Summary().Contains("repairs=") &&
            stream.Summary().Contains("journal=") && stream.Summary().Contains("owner="));
        return stream;
    }

    // Per-entity watermark ordering is the authority's; the stream must deliver exactly
    // the accepted order and never re-deliver a duplicate or an older id.
    static void Ordering()
    {
        ItemAuthority authority;
        ItemEventStream stream = New(out authority, 128, 32, 1024 * 1024, 60);
        Check(authority.Register(7, 100));
        Check(authority.Register(8, 100));

        ItemEvent[] ready;
        for (ulong id = 1; id <= 3; id++)
        {
            Check(stream.Offer(Pose(7, id), 100, (double)id, out ready) == StreamResult.Applied);
            Check(ready != null && ready.Length == 1, "one accepted event delivers one event");
            Check(ready[0].EventId == id && ready[0].Entity == 7);
            Check(ready[0].Kind == ItemEventKind.PoseWorld && Same(ready[0].Payload, WorldPose(7)));
        }

        // Duplicate ids are no-ops, including a duplicate of the newest id.
        Check(stream.Offer(Pose(7, 3), 100, 4, out ready) == StreamResult.Duplicate);
        Check(stream.Offer(Pose(7, 2), 100, 4.1, out ready) == StreamResult.Duplicate);
        Check(ready.Length == 0, "a rejected event never fills ready");

        // A forward skip is accepted; a late arrival inside the gap is stale.
        Check(stream.Offer(Pose(8, 10), 100, 5, out ready) == StreamResult.Applied);
        Check(stream.Offer(Pose(8, 20), 100, 5.1, out ready) == StreamResult.Applied);
        Check(stream.Offer(Pose(8, 15), 100, 5.2, out ready) == StreamResult.Stale);
        Check(stream.Offer(Pose(8, 25), 100, 5.3, out ready) == StreamResult.Applied);
        Check(stream.Offer(Pose(8, 22), 100, 5.4, out ready) == StreamResult.Stale);

        // Each entity has its own watermark: 7's newest id says nothing about 8's.
        Check(stream.Offer(Pose(7, 4), 100, 6, out ready) == StreamResult.Applied);
        Check(stream.Summary().Contains("applied=7"), "seven events delivered");
        Check(stream.Summary().Contains("duplicate=2") && stream.Summary().Contains("stale=2"));
        Check(stream.Verify());
    }

    // Owner/epoch/sender mismatch, tombstones, malformed envelopes, entities the
    // authority has no record of, and the bounded dedupe window.
    static void Rejections()
    {
        ItemAuthority authority;
        ItemEventStream stream = New(out authority, 128, 32, 1024 * 1024, 60);
        Check(authority.Register(7, 100));
        ItemEvent[] ready;

        Check(stream.Offer(Make(7, 101, 1, 1, ItemEventKind.PoseWorld, WorldPose(7)), 101, 1, out ready) == StreamResult.OwnerMismatch);
        Check(stream.Offer(Make(7, 100, 2, 1, ItemEventKind.PoseWorld, WorldPose(7)), 100, 1.1, out ready) == StreamResult.OwnerMismatch);
        Check(stream.Offer(Make(7, 100, 1, 1, ItemEventKind.PoseWorld, WorldPose(7)), 101, 1.2, out ready) == StreamResult.OwnerMismatch);
        Check(stream.Offer(Make(7, 100, 1, 1, ItemEventKind.PoseWorld, WorldPose(7)), 0, 1.3, out ready) == StreamResult.OwnerMismatch);
        // A rejected write never advances the watermark.
        Check(stream.Offer(Pose(7, 2), 100, 1.4, out ready) == StreamResult.Applied);

        // Invalid envelopes.
        Check(stream.Offer(null, 100, 2, out ready) == StreamResult.Invalid && ready.Length == 0);
        Check(stream.Offer(Make(7, 100, 1, 3, ItemEventKind.Unknown, WorldPose(7)), 100, 2.1, out ready) == StreamResult.Invalid);
        Check(stream.Offer(Make(7, 100, 1, 3, ItemEventKind.PoseWorld, null), 100, 2.2, out ready) == StreamResult.Invalid);
        Check(stream.Offer(Make(7, 100, 1, 3, ItemEventKind.PoseWorld, new byte[0]), 100, 2.3, out ready) == StreamResult.Invalid);
        Check(stream.Offer(Make(0, 100, 1, 3, ItemEventKind.PoseWorld, WorldPose(7)), 100, 2.4, out ready) == StreamResult.Invalid);
        Check(stream.Offer(Make(7, 100, 1, 0, ItemEventKind.PoseWorld, WorldPose(7)), 100, 2.5, out ready) == StreamResult.Invalid);
        Check(stream.Offer(Make(7, 100, 1, 3, ItemEventKind.PoseWorld, new byte[ItemEvent.MaxPayload + 1]), 100, 2.6, out ready) == StreamResult.Invalid);

        // An entity with no authority record is never delivered and never registered.
        Check(authority.Register(12, 100));
        Check(authority.Remove(12, 1));
        Check(authority.IsRemoved(12));
        // A tombstoned entity is Removed, before the unknown-entity case can apply.
        Check(stream.Offer(Make(12, 100, 1, 1, ItemEventKind.Remove, RemoveItem(12)), 100, 3, out ready) == StreamResult.Removed);
        Check(stream.Offer(Pose(99, 1), 100, 3.1, out ready) == StreamResult.Invalid);
        Check(!authority.Contains(99), "the stream never registers an entity");

        // Bounded dedupe window: an id the authority can no longer classify fails closed.
        var small = new ItemAuthority(8, 8, 2);
        var narrow = new ItemEventStream(small, 64, 16, 4096, 60);
        Check(small.Register(40, 100));
        Check(narrow.Offer(Pose(40, 5), 100, 4, out ready) == StreamResult.Applied);
        Check(narrow.Offer(Pose(40, 6), 100, 4.1, out ready) == StreamResult.Applied);
        Check(narrow.Offer(Pose(40, 4), 100, 4.2, out ready) == StreamResult.Overflow);
        Check(narrow.Summary().Contains("overflow=1"));
        Check(narrow.Verify());

        Check(stream.Summary().Contains("owner=4") && stream.Summary().Contains("removed=1") &&
            stream.Summary().Contains("invalid=8"));
        Check(stream.Verify());

        // The result codes are part of the contract; keep the order stable.
        Check((int)StreamResult.Applied == 0);
        Check((int)StreamResult.Buffered == 1);
        Check((int)StreamResult.Duplicate == 2);
        Check((int)StreamResult.Stale == 3);
        Check((int)StreamResult.OwnerMismatch == 4);
        Check((int)StreamResult.Removed == 5);
        Check((int)StreamResult.Invalid == 6);
        Check((int)StreamResult.Overflow == 7);
        Check((int)StreamResult.Expired == 8);
    }

    // The dependency a native payload names is read at the documented offsets and
    // nothing else is guessed at.
    static void ParentExtraction()
    {
        Check(ItemEventStream.ParentOf(Make(21, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(21, 20, 0))) == 20);
        Check(ItemEventStream.ParentOf(Make(21, 100, 1, 1, ItemEventKind.PoseLocalMount, LocalPose(21, 20, -100000))) == 20);
        Check(ItemEventStream.ParentOf(Make(31, 100, 1, 1, ItemEventKind.Attach, AttachTo(31, 30))) == 30);
        // Detach, a POI/collider attachment and every parentless family need no parent.
        Check(ItemEventStream.ParentOf(Make(31, 100, 1, 1, ItemEventKind.Detach, DetachFrom(31))) == 0);
        byte[] poi = Put(Put(Put(Msg(42, 121), 1, 31), 5, 2), 13, 30);
        Check(ItemEventStream.ParentOf(Make(31, 100, 1, 1, ItemEventKind.Attach, poi)) == 0);
        Check(ItemEventStream.ParentOf(Pose(7, 1)) == 0);
        Check(ItemEventStream.ParentOf(null) == 0);
        Check(ItemEventStream.ParentOf(Make(21, 100, 1, 1, ItemEventKind.PoseLocalStorage, new byte[] { 17, 1, 2 })) == 0);
        // A kind/payload mismatch is not decoded as a dependency.
        Check(ItemEventStream.ParentOf(Make(21, 100, 1, 1, ItemEventKind.PoseLocalStorage, WorldPose(21))) == 0);
        Check(ItemEventStream.ParentOf(Make(31, 100, 1, 1, ItemEventKind.Attach, Msg(42, 9))) == 0);
    }

    // A stored child (17) and an item attachment (42) both wait for their parent and
    // drain in arrival order once the parent's first accepted event arrives.
    static void DependencyBuffer()
    {
        ItemAuthority authority;
        ItemEventStream stream = New(out authority, 128, 32, 1024 * 1024, 60);
        Check(authority.Register(21, 100));
        Check(authority.Register(22, 100));
        Check(authority.Register(31, 100));

        ItemEvent[] ready;
        Check(stream.Offer(Make(21, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(21, 20, 0)), 100, 1, out ready) == StreamResult.Buffered);
        Check(ready.Length == 0, "a buffered event is not delivered");
        Check(stream.Offer(Make(22, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(22, 20, 1)), 100, 1.1, out ready) == StreamResult.Buffered);
        Check(stream.Offer(Make(31, 100, 1, 1, ItemEventKind.Attach, AttachTo(31, 30)), 100, 1.2, out ready) == StreamResult.Buffered);
        Check(stream.Summary().Contains("buffered=3") && stream.Summary().Contains("pending=3"));
        Check(stream.Summary().Contains("pendingEntities=2"));

        // Nothing can drain while the parent has no record.
        Check(!stream.TryDrain(20, out ready) && ready.Length == 0);
        Check(!stream.TryDrain(0, out ready));
        Check(stream.TryDrain(21, out ready) == false, "a known child is not a dependency of anything");
        Check(stream.Verify());

        // Parent 20 registers and its own first event is accepted: parent first, then
        // its children in arrival order.
        Check(authority.Register(20, 100));
        Check(stream.Offer(Pose(20, 1), 100, 2, out ready) == StreamResult.Applied);
        Check(ready.Length == 3, "parent plus two children");
        Check(ready[0].Entity == 20 && ready[0].Kind == ItemEventKind.PoseWorld);
        Check(ready[1].Entity == 21 && ready[1].Kind == ItemEventKind.PoseLocalStorage);
        Check(ready[2].Entity == 22 && ready[2].Kind == ItemEventKind.PoseLocalStorage);
        Check(ready[1].EventId == 1 && ready[2].EventId == 1);
        Check(!stream.TryDrain(20, out ready) && ready.Length == 0, "the dependency queue is empty");
        Check(stream.Summary().Contains("pending=1") && stream.Summary().Contains("pendingEntities=1"));

        // The 42 dependency uses the same path.
        Check(authority.Register(30, 100));
        Check(stream.Offer(Pose(30, 1), 100, 3, out ready) == StreamResult.Applied);
        Check(ready.Length == 2);
        Check(ready[0].Entity == 30 && ready[1].Entity == 31);
        Check(ready[1].Kind == ItemEventKind.Attach);
        Check(!stream.TryDrain(30, out ready));

        // Once the parent is known, later children are applied immediately.
        Check(stream.Offer(Make(21, 100, 1, 2, ItemEventKind.PoseLocalStorage, LocalPose(21, 20, 2)), 100, 4, out ready) == StreamResult.Applied);
        Check(ready.Length == 1 && ready[0].Entity == 21);
        // And an attachment to a parent that is known needs no buffer.
        Check(stream.Offer(Make(31, 100, 1, 3, ItemEventKind.Attach, AttachTo(31, 30)), 100, 4.1, out ready) == StreamResult.Applied);

        // A later child of a known parent still validates the child's own authority.
        Check(stream.Offer(Make(21, 101, 1, 4, ItemEventKind.PoseLocalStorage, LocalPose(21, 20, 0)), 101, 4.2, out ready) == StreamResult.OwnerMismatch);

        // A child whose parent is tombstoned can never land.
        Check(authority.Register(40, 100));
        Check(authority.Register(41, 100));
        Check(authority.Remove(40, 1));
        Check(stream.Offer(Make(41, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(41, 40, 0)), 100, 5, out ready) == StreamResult.Removed);
        Check(stream.Summary().Contains("pending=0"), "nothing waits on a tombstone");
        Check(stream.Verify());
    }

    // One Offer is bounded at 32 events; the remainder stays buffered and is readable
    // through TryDrain in the same arrival order.
    static void DrainBound()
    {
        ItemAuthority authority;
        ItemEventStream stream = New(out authority, 256, 64, 1024 * 1024, 60);
        for (uint i = 0; i < 40; i++) Check(authority.Register(100 + i, 100));

        ItemEvent[] ready;
        for (int i = 0; i < 40; i++)
        {
            uint child = (uint)(100 + i);
            Check(stream.Offer(Make(child, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose((int)child, 50, i)), 100, 1 + i, out ready) == StreamResult.Buffered);
        }
        Check(stream.Summary().Contains("pending=40"));

        Check(authority.Register(50, 100));
        Check(stream.Offer(Pose(50, 1), 100, 50, out ready) == StreamResult.Applied);
        Check(ready.Length == ItemEventStream.MaxReadyPerCall, "one call never exceeds the ready bound");
        Check(ready[0].Entity == 50);
        for (int i = 1; i < ready.Length; i++) Check(ready[i].Entity == (ulong)(100 + i - 1));

        Check(stream.TryDrain(50, out ready));
        Check(ready.Length == 9, "the remaining children flush in one bounded drain");
        for (int i = 0; i < ready.Length; i++) Check(ready[i].Entity == (ulong)(131 + i));
        Check(!stream.TryDrain(50, out ready) && ready.Length == 0);
        Check(stream.Summary().Contains("pending=0") && stream.Summary().Contains("applied=41"));
        Check(stream.Verify());
    }

    // Every bound fails closed: the buffered children of the offending dependency are
    // dropped, its repair is noted, and the authority's accepted state is untouched.
    static void Bounds()
    {
        ItemEvent[] ready;

        // Per-entity: three children per parent, the fourth overflows and clears it.
        ItemAuthority authority;
        ItemEventStream perEntity = New(out authority, 100, 3, 1024 * 1024, 60);
        for (uint i = 0; i < 5; i++) Check(authority.Register(70 + i, 100));
        for (int i = 0; i < 3; i++)
        {
            uint child = (uint)(70 + i);
            Check(perEntity.Offer(Make(child, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose((int)child, 60, i)), 100, 1 + i, out ready) == StreamResult.Buffered);
        }
        Check(perEntity.Offer(Make(73, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(73, 60, 3)), 100, 4, out ready) == StreamResult.Overflow);
        Check(ready.Length == 0);
        Check(perEntity.Summary().Contains("pending=0"), "overflow drops the buffered events for that entity");
        Check(perEntity.Summary().Contains("overflow=1") && perEntity.Summary().Contains("buffered=3"));
        Check(perEntity.Summary().Contains("repairs=1"), "the dropped dependency is repaired");
        // The accepted state survived: the parent is still known and new events deliver.
        Check(authority.Register(60, 100));
        Check(perEntity.Offer(Pose(60, 1), 100, 5, out ready) == StreamResult.Applied && ready.Length == 1);
        Check(perEntity.Verify());

        // Total: the fourth buffered event across two dependencies overflows only the
        // dependency it belongs to.
        ItemEventStream total = New(out authority, 3, 3, 1024 * 1024, 60);
        for (uint i = 0; i < 5; i++) Check(authority.Register(90 + i, 100));
        Check(total.Offer(Make(90, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(90, 80, 0)), 100, 1, out ready) == StreamResult.Buffered);
        Check(total.Offer(Make(91, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(91, 80, 1)), 100, 1.1, out ready) == StreamResult.Buffered);
        Check(total.Offer(Make(92, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(92, 81, 0)), 100, 1.2, out ready) == StreamResult.Buffered);
        Check(total.Offer(Make(93, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(93, 81, 1)), 100, 1.3, out ready) == StreamResult.Overflow);
        Check(total.Summary().Contains("pending=2") && total.Summary().Contains("pendingEntities=1"));
        Check(!total.TryDrain(81, out ready));
        Check(authority.Register(80, 100));
        Check(total.TryDrain(80, out ready) && ready.Length == 2);
        Check(total.Verify());

        // Bytes: a 37-byte itemPosLocal, an 80-byte budget, the third overflows.
        ItemEventStream bytes = New(out authority, 100, 64, 80, 60);
        for (uint i = 0; i < 4; i++) Check(authority.Register(110 + i, 100));
        Check(bytes.Offer(Make(110, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(110, 100, 0)), 100, 1, out ready) == StreamResult.Buffered);
        Check(bytes.Offer(Make(111, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(111, 100, 1)), 100, 1.1, out ready) == StreamResult.Buffered);
        Check(bytes.Offer(Make(112, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(112, 100, 2)), 100, 1.2, out ready) == StreamResult.Overflow);
        Check(bytes.Summary().Contains("pending=0") && bytes.Summary().Contains("bytes=0"));
        Check(bytes.Verify());

        // Constructor validation.
        ItemAuthority a = new ItemAuthority();
        Check(Throws(delegate { return new ItemEventStream(null, 1, 1, 1, 1); }));
        Check(Throws(delegate { return new ItemEventStream(a, 0, 1, 1, 1); }));
        Check(Throws(delegate { return new ItemEventStream(a, 1, 0, 1, 1); }));
        Check(Throws(delegate { return new ItemEventStream(a, 1, 1, 0, 1); }));
        Check(Throws(delegate { return new ItemEventStream(a, 1, 1, 1, 0); }));
        Check(Throws(delegate { return new ItemEventStream(a, 1, 1, 1, -1); }));
        Check(Throws(delegate { return new ItemEventStream(a, 1, 1, 1, Double.NaN); }));
        Check(Throws(delegate { return new ItemEventStream(a, 1, 1, 1, Double.PositiveInfinity); }));
        Check(Throws(delegate { return new ItemEventStream(a, 2, 3, 1, 1); }), "per-entity may not exceed the total");
        Check(!Throws(delegate { return new ItemEventStream(a, 2, 2, 1, 1); }));
    }

    static bool Throws(Func<ItemEventStream> create)
    {
        try { ItemEventStream stream = create(); return stream == null; }
        catch (ArgumentException) { return true; }
    }

    // Expiry drops the wait, notes one deduplicated repair, and the repair schedule
    // backs off 1, 2, 4, 8, 16, 16 with a hard cap of six attempts.
    static void Expiry()
    {
        ItemAuthority authority;
        ItemEventStream stream = New(out authority, 128, 32, 1024 * 1024, 5);
        Check(authority.Register(21, 100));
        Check(authority.Register(7, 100));

        ItemEvent[] ready;
        uint entity;
        double due;
        Check(stream.Offer(Make(21, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(21, 20, 0)), 100, 0, out ready) == StreamResult.Buffered);
        Check(!stream.TryNextRepair(out entity, out due, 1), "a child still buffered is not repaired");

        // Any later offer ages the buffer: at t=6 the dependency is older than 5 s.
        Check(stream.Offer(Pose(7, 1), 100, 6, out ready) == StreamResult.Applied);
        Check(stream.Summary().Contains("expired=1") && stream.Summary().Contains("pending=0"));
        Check(stream.Summary().Contains("repairs=1"));

        Check(stream.TryNextRepair(out entity, out due, 6) && entity == 20 && due == 7.0);
        Check(!stream.TryNextRepair(out entity, out due, 6.5), "the backoff has not elapsed");
        Check(stream.TryNextRepair(out entity, out due, 7) && entity == 20 && due == 9.0);
        Check(stream.TryNextRepair(out entity, out due, 9) && entity == 20 && due == 13.0);
        Check(stream.TryNextRepair(out entity, out due, 13) && entity == 20 && due == 21.0);
        Check(stream.TryNextRepair(out entity, out due, 21) && entity == 20 && due == 37.0);
        Check(stream.TryNextRepair(out entity, out due, 37) && entity == 20 && due == 53.0);
        Check(stream.Summary().Contains("repairSends=6"));
        // Six attempts is the cap: dedup per entity, no seventh request.
        Check(!stream.TryNextRepair(out entity, out due, 1000) && entity == 0 && due == 0);
        Check(stream.Summary().Contains("repairSends=6"));

        // An exhausted dependency refuses new children instead of buffering forever.
        Check(stream.Offer(Make(21, 100, 1, 2, ItemEventKind.PoseLocalStorage, LocalPose(21, 20, 0)), 100, 1000, out ready) == StreamResult.Expired);
        Check(ready.Length == 0 && stream.Summary().Contains("pending=0"));

        // Clearing the repair reopens the dependency.
        stream.ClearRepair(20);
        Check(stream.Offer(Make(21, 100, 1, 3, ItemEventKind.PoseLocalStorage, LocalPose(21, 20, 0)), 100, 1001, out ready) == StreamResult.Buffered);
        Check(stream.Summary().Contains("repairPending=0"), "the cleared repair is gone");

        // The dependency resolving retires the repair record without spending an attempt.
        Check(authority.Register(20, 100));
        Check(!stream.TryNextRepair(out entity, out due, 2000));
        Check(stream.TryDrain(20, out ready) && ready.Length == 1 && ready[0].EventId == 3);
        Check(stream.Summary().Contains("repairSends=6") && stream.Summary().Contains("pending=0"));
        Check(stream.Verify());

        // A repair for an entity that shows up on its own is retired by TryNextRepair.
        ItemAuthority lateAuthority;
        ItemEventStream late = New(out lateAuthority, 64, 16, 1024 * 1024, 1);
        Check(lateAuthority.Register(31, 100));
        Check(late.Offer(Make(31, 100, 1, 1, ItemEventKind.Attach, AttachTo(31, 33)), 100, 0, out ready) == StreamResult.Buffered);
        Check(lateAuthority.Register(7, 100));
        Check(late.Offer(Pose(7, 1), 100, 2.1, out ready) == StreamResult.Applied);
        Check(late.Summary().Contains("expired=1") && late.Summary().Contains("repairs=1"));
        Check(lateAuthority.Register(33, 100));
        Check(!late.TryNextRepair(out entity, out due, 3), "a resolved dependency is not repaired");
        Check(late.Summary().Contains("repairPending=0"), "the resolved repair is retired");
        // The expired child is gone for good: only the repair can restore it.
        Check(!late.TryDrain(33, out ready) && ready.Length == 0);
        Check(late.Verify());
    }

    // Remove cancels buffered work per entity and per dependency, and never resurrects
    // the events it cancelled.
    static void RemoveCancels()
    {
        ItemAuthority authority;
        ItemEventStream stream = New(out authority, 128, 32, 1024 * 1024, 60);
        Check(authority.Register(21, 100));
        Check(authority.Register(22, 100));
        Check(authority.Register(23, 100));

        ItemEvent[] ready;
        Check(stream.Offer(Make(21, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(21, 20, 0)), 100, 1, out ready) == StreamResult.Buffered);
        Check(stream.Offer(Make(22, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(22, 20, 1)), 100, 1.1, out ready) == StreamResult.Buffered);
        Check(stream.Offer(Make(23, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(23, 30, 0)), 100, 1.2, out ready) == StreamResult.Buffered);
        Check(stream.Summary().Contains("pending=3"));

        // Entity-scoped cancellation leaves the sibling and the other dependency alone.
        stream.Remove(22);
        Check(stream.Summary().Contains("canceled=1") && stream.Summary().Contains("pending=2"));
        Check(stream.Summary().Contains("pendingEntities=2"));
        Check(stream.Verify());

        // Dependency-scoped cancellation drops everything waiting on parent 20.
        stream.Remove(20);
        Check(stream.Summary().Contains("canceled=2") && stream.Summary().Contains("pending=1"));
        Check(stream.Summary().Contains("pendingEntities=1"));
        Check(!stream.TryDrain(20, out ready) && ready.Length == 0);
        Check(stream.Verify());

        // Once the parent exists the cancelled children stay cancelled.
        Check(authority.Register(20, 100));
        Check(authority.Register(30, 100));
        Check(stream.Offer(Pose(20, 1), 100, 2, out ready) == StreamResult.Applied);
        Check(ready.Length == 1 && ready[0].Entity == 20, "no cancelled event is resurrected");
        Check(stream.TryDrain(30, out ready) && ready.Length == 1 && ready[0].Entity == 23);
        Check(stream.Summary().Contains("pending=0"));
        stream.Remove(0);
        Check(stream.Verify());

        // Remove also retires the repair record for that entity.
        ItemEventStream repairing = New(out authority, 64, 16, 1024 * 1024, 1);
        Check(authority.Register(51, 100));
        Check(repairing.Offer(Make(51, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(51, 55, 0)), 100, 0, out ready) == StreamResult.Buffered);
        Check(repairing.Offer(Pose(52, 1), 100, 5, out ready) == StreamResult.Invalid);
        Check(repairing.Summary().Contains("repairs=1"));
        repairing.Remove(55);
        Check(repairing.Summary().Contains("repairPending=0") && repairing.Summary().Contains("pending=0"));
        Check(repairing.Verify());

        // A buffer left waiting on a tombstoned parent is a contract violation: the
        // stream must be told, and saying so restores Verify.
        ItemAuthority other = new ItemAuthority();
        var stale = new ItemEventStream(other, 16, 16, 1024 * 1024, 60);
        Check(other.Register(62, 100));
        Check(stale.Offer(Make(62, 100, 1, 1, ItemEventKind.PoseLocalStorage, LocalPose(62, 61, 0)), 100, 1, out ready) == StreamResult.Buffered);
        Check(other.Register(61, 100));
        Check(other.Remove(61, 1));
        Check(!stale.Verify(), "a tombstoned dependency must be cancelled");
        stale.Remove(61);
        Check(stale.Verify());
        Check(stale.Offer(Make(62, 100, 1, 2, ItemEventKind.PoseLocalStorage, LocalPose(62, 61, 0)), 100, 2, out ready) == StreamResult.Removed);
    }

    // Join journal: barrier, ordered record, replay with the barrier revision, and
    // fail-closed invalidation on overflow or a bad record.
    static void Journal()
    {
        ItemAuthority authority;
        ItemEventStream stream = New(out authority, 128, 32, 1024 * 1024, 60);
        Check(authority.Register(7, 100));

        ItemEvent[] replay;
        ulong final;
        Check(!stream.TryReplay(out replay, out final) && replay.Length == 0 && final == 0, "no barrier, no replay");
        Check(!stream.Record(Pose(7, 1)), "nothing may be recorded before a barrier");

        stream.BeginBarrier(9);
        Check(stream.Record(Pose(7, 1)) && stream.Record(Pose(7, 2)));
        Check(stream.Summary().Contains("journal=2"));
        Check(stream.TryReplay(out replay, out final) && final == 9 && replay.Length == 2);
        Check(replay[0].EventId == 1 && replay[1].EventId == 2, "the journal preserves order");
        Check(Same(replay[0].Payload, WorldPose(7)));

        // A malformed record invalidates the whole journal.
        Check(!stream.Record(null));
        Check(!stream.TryReplay(out replay, out final) && replay.Length == 0 && final == 0);
        Check(stream.Summary().Contains("journalInvalid=1"));

        // A new barrier starts a clean journal.
        stream.BeginBarrier(10);
        Check(stream.Record(Pose(7, 3)));
        Check(stream.TryReplay(out replay, out final) && final == 10 && replay.Length == 1 && replay[0].EventId == 3);

        // Capacity overflow invalidates: 256 recorded events is the cap.
        stream.BeginBarrier(11);
        for (int i = 0; i < ItemEventStream.JournalCapacity; i++) Check(stream.Record(Make(7, 100, 1, (ulong)(i + 1), ItemEventKind.Outfit, Outfit(7))));
        Check(!stream.Record(Make(7, 100, 1, 999, ItemEventKind.Outfit, Outfit(7))));
        Check(!stream.TryReplay(out replay, out final) && replay.Length == 0 && final == 0);
        Check(stream.Summary().Contains("journalInvalid=2"));

        // Byte overflow invalidates too (63 x 4105 bytes fits, the 64th does not).
        var wide = new byte[ItemEvent.MaxPayload];
        wide[0] = 48;
        stream.BeginBarrier(12);
        for (int i = 1; i <= 63; i++) Check(stream.Record(Make(7, 100, 1, (ulong)i, ItemEventKind.Outfit, wide)));
        Check(!stream.Record(Make(7, 100, 1, 64, ItemEventKind.Outfit, wide)));
        Check(!stream.TryReplay(out replay, out final));

        // A zero revision is a programming error, not a silent reset.
        bool threw = false;
        try { stream.BeginBarrier(0); } catch (ArgumentException) { threw = true; }
        Check(threw);

        // The journal still replays after a valid barrier following an invalidation.
        stream.BeginBarrier(13);
        Check(stream.Record(Pose(7, 4)));
        Check(stream.TryReplay(out replay, out final) && final == 13 && replay.Length == 1);
        Check(stream.Verify());
    }

    // Verification is compositional: a fresh stream verifies, and every scenario above
    // left a stream whose bounds, accounting and key lists agree.
    static void Verification()
    {
        var authority = new ItemAuthority();
        var stream = new ItemEventStream(authority, 8, 8, 4096, 60);
        Check(stream.Verify());
        Check(stream.Summary().Contains("pending=0") && stream.Summary().Contains("bytes=0"));
        Check(stream.Summary().Contains("journalInvalid=0") && stream.Summary().Contains("repairSends=0"));
        Check(stream.Summary().StartsWith("stream{"));

        // Nothing in the stream touches the authority's own counters.
        Check(authority.Summary().Contains("accepted=0"));

        // The defaults are the inventory section 4 bounds: 4096 events, 64 per entity, 2 MiB, 60 s.
        var defaults = new ItemEventStream(authority);
        Check(defaults.Verify());
        Check(ItemEventStream.DefaultMaxTotalEvents == 4096 && ItemEventStream.DefaultMaxEventsPerEntity == 64);
        Check(ItemEventStream.DefaultMaxBytes == 2 * 1024 * 1024 && ItemEventStream.DefaultExpirySeconds == 60.0);

        ItemEvent[] ready;
        Check(authority.Register(7, 100));
        Check(stream.Offer(Pose(7, 1), 100, 1, out ready) == StreamResult.Applied);
        Check(authority.Summary().Contains("accepted=1"));
        Check(stream.Verify());
    }

    public static int Main()
    {
        Ordering();
        Rejections();
        ParentExtraction();
        DependencyBuffer();
        DrainBound();
        Bounds();
        Expiry();
        RemoveCancels();
        Journal();
        Verification();
        Console.WriteLine("PASS: item event stream ordering, dependencies, bounds, expiry/repair, cancellation and join journal (" + checks + " checks)");
        return 0;
    }
}
