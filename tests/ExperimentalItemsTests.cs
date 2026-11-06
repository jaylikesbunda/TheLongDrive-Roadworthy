using System;
using System.Collections.Generic;
using TldMp;
using TldMp.Experimental;

// Item authority core tests (Phase 4). Console Check pattern: the first failed
// invariant throws, so a non-zero exit means a real regression.
internal static class ExperimentalItemsTests
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Item authority invariant failed at check " + checks); }

    static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static byte[] Msg(int type, int length)
    {
        var b = new byte[length];
        b[0] = (byte)type;
        return b;
    }

    static byte[] Put(byte[] b, int at, int value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 4);
        return b;
    }

    // Native framings follow StablePackets.Valid exactly (length = 1 + fields).
    static byte[] WorldPose(int id) { return Put(Msg(16, 45), 1, id); }
    // itemPosLocal: id u32@1, parent u32@5, physlock index i32@9, pose @13.
    static byte[] LocalPose(int id, int index) { return Put(Put(Put(Msg(17, 37), 1, id), 5, 7), 9, index); }
    static byte[] PlayerPose(int id) { return Put(Msg(18, 42), 10, id); }
    static byte[] ItemVelocity(int id) { return Put(Msg(19, 29), 1, id); }
    static byte[] CarVelocity(int id) { return Put(Msg(20, 33), 1, id); }
    static byte[] PartCondition(int id) { return Put(Msg(23, 41), 1, id); }
    static byte[] Plate(int id) { return Put(Msg(24, 9), 1, id); }
    static byte[] BusDoors(int id) { return Put(Msg(25, 13), 1, id); }
    static byte[] RndType(int id) { return Put(Msg(26, 13), 1, id); }
    static byte[] Food(int id) { return Put(Msg(27, 14), 1, id); }
    static byte[] Ammo(int id) { return Put(Msg(28, 13), 1, id); }
    static byte[] Push(int id) { return Put(Msg(29, 37), 9, id); }
    static byte[] Weapon(int id) { return Put(Msg(30, 23), 1, id); }
    static byte[] WeaponShot(int id) { return Put(Msg(31, 13), 1, id); }
    static byte[] Paint(int id) { return Put(Msg(32, 25), 1, id); }
    static byte[] Breakable(int id) { return Put(Msg(33, 17), 1, id); }
    static byte[] RemoveItem(int id) { return Put(Msg(36, 5), 1, id); }
    static byte[] CarValues(int id) { return Put(Msg(37, 43), 1, id); }
    static byte[] CarHandbrake(int id) { return Put(Msg(38, 9), 1, id); }
    static byte[] CarPose(int id) { return Put(Msg(39, 49), 1, id); }
    static byte[] Usable(int id) { return Put(Msg(40, 30), 2, id); }
    static byte[] Fluid(int id) { return Put(Msg(41, 18), 2, id); }
    static byte[] Attach(int id, int attachType) { return Put(Put(Msg(42, attachType == 0 ? 9 : 121), 1, id), 5, attachType); }
    static byte[] Ragdoll(int id) { return Put(Msg(43, 21), 1, id); }
    static byte[] Outfit(int id) { return Put(Msg(48, 17), 1, id); }
    static byte[] AskItemData(int id) { return Put(Msg(21, 5), 1, id); }
    static byte[] AskItemPos(int id) { return Put(Msg(46, 5), 1, id); }

    static ItemEvent Make(ulong entity, ulong owner, ulong epoch, ulong id, ItemEventKind kind, byte[] payload)
    {
        return new ItemEvent
        {
            Entity = entity, Owner = owner, Epoch = epoch, EventId = id, Kind = kind,
            Flags = (byte)ItemEventPolicy.FlagsFor(kind), Payload = payload
        };
    }

    static bool Throws(ItemEvent e)
    {
        try { e.Write(); return false; }
        catch (ArgumentException) { return true; }
    }

    static ItemEvent Sample()
    {
        return Make(7, 100, 1, 3, ItemEventKind.PoseWorld, WorldPose(7));
    }

    static void Envelope()
    {
        ItemEvent original = Sample();
        byte[] frame = original.Write();
        Check(frame.Length == ItemEvent.HeaderBytes + original.Payload.Length);
        ItemEvent back;
        Check(ItemEvent.TryRead(frame, out back));
        Check(back.Entity == 7 && back.Owner == 100 && back.Epoch == 1 && back.EventId == 3);
        Check(back.Kind == ItemEventKind.PoseWorld);
        Check(back.Flags == (byte)ItemEventPolicy.FlagsFor(ItemEventKind.PoseWorld));
        Check(Same(back.Payload, original.Payload));

        // A reliable one-off combination survives the flags byte unchanged.
        ItemEvent shot = Make(7, 100, 1, 4, ItemEventKind.WeaponShot, WeaponShot(7));
        Check(ItemEvent.TryRead(shot.Write(), out back) && back.Flags == (byte)(ItemEventFlags.Reliable | ItemEventFlags.Ordered | ItemEventFlags.OneOff));

        // Maximum payload still frames exactly.
        var large = new ItemEvent { Entity = 7, Owner = 100, Epoch = 1, EventId = 5, Kind = ItemEventKind.Outfit, Flags = 0, Payload = new byte[ItemEvent.MaxPayload] };
        large.Payload[0] = 48;
        Check(ItemEvent.TryRead(large.Write(), out back) && back.Payload.Length == ItemEvent.MaxPayload);

        // Bad magic, bad version.
        byte[] bad = (byte[])frame.Clone(); bad[0] ^= (byte)0xFF;
        Check(!ItemEvent.TryRead(bad, out back) && back == null);
        bad = (byte[])frame.Clone(); bad[4] = 2;
        Check(!ItemEvent.TryRead(bad, out back));

        // Framing: empty, header-only, short payload, stray trailing byte.
        Check(!ItemEvent.TryRead(null, out back));
        Check(!ItemEvent.TryRead(new byte[0], out back));
        Check(!ItemEvent.TryRead(new byte[ItemEvent.HeaderBytes], out back));
        Check(!ItemEvent.TryRead(new byte[ItemEvent.HeaderBytes - 1], out back));
        var truncated = new byte[frame.Length - 1];
        Buffer.BlockCopy(frame, 0, truncated, 0, truncated.Length);
        Check(!ItemEvent.TryRead(truncated, out back));
        var trailing = new byte[frame.Length + 1];
        Buffer.BlockCopy(frame, 0, trailing, 0, frame.Length);
        Check(!ItemEvent.TryRead(trailing, out back));

        // Oversized payload: declared, and actually present.
        var oversized = new byte[ItemEvent.HeaderBytes + ItemEvent.MaxPayload + 1];
        Buffer.BlockCopy(frame, 0, oversized, 0, ItemEvent.HeaderBytes);
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)(ItemEvent.MaxPayload + 1)), 0, oversized, 39, 2);
        Check(!ItemEvent.TryRead(oversized, out back));
        var declared = (byte[])frame.Clone();
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)(original.Payload.Length + 1)), 0, declared, 39, 2);
        Check(!ItemEvent.TryRead(declared, out back));

        // Zero identity fields.
        for (int field = 0; field < 4; field++)
        {
            var zeroed = (byte[])frame.Clone();
            Buffer.BlockCopy(new byte[8], 0, zeroed, 5 + field * 8, 8);
            Check(!ItemEvent.TryRead(zeroed, out back));
        }

        // Unknown kind and reserved flag bits.
        bad = (byte[])frame.Clone(); bad[37] = 0;
        Check(!ItemEvent.TryRead(bad, out back));
        bad = (byte[])frame.Clone(); bad[37] = 99;
        Check(!ItemEvent.TryRead(bad, out back));
        bad = (byte[])frame.Clone(); bad[38] = 0x40;
        Check(!ItemEvent.TryRead(bad, out back));
        Check(!ItemEvent.TryRead(new byte[ItemEvent.HeaderBytes], out back));
        var garbage = new byte[ItemEvent.HeaderBytes];
        for (int i = 0; i < garbage.Length; i++) garbage[i] = 0xFF;
        Check(!ItemEvent.TryRead(garbage, out back));

        // Write refuses to produce an invalid envelope.
        Check(Throws(Make(0, 100, 1, 3, ItemEventKind.PoseWorld, WorldPose(7))));
        Check(Throws(Make(7, 0, 1, 3, ItemEventKind.PoseWorld, WorldPose(7))));
        Check(Throws(Make(7, 100, 0, 3, ItemEventKind.PoseWorld, WorldPose(7))));
        Check(Throws(Make(7, 100, 1, 0, ItemEventKind.PoseWorld, WorldPose(7))));
        Check(Throws(Make(7, 100, 1, 3, ItemEventKind.Unknown, WorldPose(7))));
        Check(Throws(Make(7, 100, 1, 3, ItemEventKind.PoseWorld, null)));
        Check(Throws(Make(7, 100, 1, 3, ItemEventKind.PoseWorld, new byte[0])));
        Check(Throws(Make(7, 100, 1, 3, ItemEventKind.PoseWorld, new byte[ItemEvent.MaxPayload + 1])));
    }

    static void Registry()
    {
        var a = new ItemAuthority();
        Check(a.Verify());
        Check(a.Register(1, 100));
        Check(!a.Register(1, 200));
        Check(!a.Register(0, 100));
        Check(!a.Register(2, 0));
        Check(a.Contains(1) && !a.IsRemoved(1));
        ulong owner, epoch;
        Check(a.TryOwner(1, out owner, out epoch) && owner == 100 && epoch == 1);

        ulong next;
        Check(!a.Transfer(1, 0, 200, out next) && next == 0);
        Check(!a.Transfer(1, 2, 200, out next));
        Check(!a.Transfer(1, 1, 0, out next));
        Check(!a.Transfer(1, 1, 100, out next));
        Check(a.Transfer(1, 1, 200, out next) && next == 2);
        Check(!a.Transfer(1, 1, 300, out next));
        Check(a.Transfer(1, 2, 300, out next) && next == 3);

        Check(!a.Remove(1, 2));
        Check(a.Remove(1, 3));
        Check(!a.Remove(1, 3));
        Check(!a.Contains(1) && a.IsRemoved(1));
        Check(!a.Register(1, 100));           // no resurrection
        Check(a.Count == 0 && a.TombstoneCount == 1);
        Check(!a.Transfer(1, 3, 100, out next));
        Check(a.Verify());

        // Entity cap fails closed.
        var b = new ItemAuthority(2, 2, 4);
        Check(b.Register(1, 10) && b.Register(2, 20));
        Check(!b.Register(3, 30));
        Check(!b.Register(3, 30));
        Check(b.Count == 2 && b.Verify());

        // Tombstone cap refuses the removal instead of allowing a resurrection.
        var c = new ItemAuthority(4, 1, 4);
        Check(c.Register(1, 10) && c.Register(2, 20));
        Check(c.Remove(1, 1));
        Check(!c.Remove(2, 1));
        Check(c.Contains(2) && !c.IsRemoved(2));
        Check(c.TombstoneCount == 1 && c.Verify());

        // Rejected constructor limits.
        bool threw = false;
        try { new ItemAuthority(0, 1, 1); } catch (ArgumentException) { threw = true; }
        Check(threw);
        threw = false;
        try { new ItemAuthority(1, 1, 0); } catch (ArgumentException) { threw = true; }
        Check(threw);
    }

    static void Acceptance()
    {
        var a = new ItemAuthority();
        Check(a.Register(7, 100));
        ItemEvent e = Sample();
        Check(a.Accept(e, 100, 1.0) == ItemDecision.Accepted);
        Check(a.Accept(e, 100, 1.1) == ItemDecision.Duplicate);
        Check(a.Accept(Make(7, 100, 1, 4, ItemEventKind.PoseWorld, WorldPose(7)), 100, 1.2) == ItemDecision.Accepted);

        // Older id that was never seen is stale, never an overwrite.
        Check(a.Accept(Make(7, 100, 1, 2, ItemEventKind.Velocity, ItemVelocity(7)), 100, 1.3) == ItemDecision.Stale);

        // Forward skip is fine; a late arrival in the gap is stale.
        Check(a.Accept(Make(7, 100, 1, 9, ItemEventKind.Velocity, ItemVelocity(7)), 100, 1.4) == ItemDecision.Accepted);
        Check(a.Accept(Make(7, 100, 1, 8, ItemEventKind.Velocity, ItemVelocity(7)), 100, 1.5) == ItemDecision.Stale);

        // Owner, epoch and authenticated sender must all be the registered owner.
        Check(a.Accept(Make(7, 101, 1, 10, ItemEventKind.PoseWorld, WorldPose(7)), 101, 1.6) == ItemDecision.OwnerMismatch);
        Check(a.Accept(Make(7, 100, 2, 10, ItemEventKind.PoseWorld, WorldPose(7)), 100, 1.7) == ItemDecision.OwnerMismatch);
        Check(a.Accept(Make(7, 100, 1, 10, ItemEventKind.PoseWorld, WorldPose(7)), 101, 1.8) == ItemDecision.OwnerMismatch);
        Check(a.Accept(Make(7, 100, 1, 10, ItemEventKind.PoseWorld, WorldPose(7)), 0, 1.9) == ItemDecision.OwnerMismatch);

        // Unknown entity is never auto-registered.
        Check(a.Accept(Make(8, 100, 1, 10, ItemEventKind.PoseWorld, WorldPose(8)), 100, 2.0) == ItemDecision.Unknown);
        Check(!a.Contains(8));

        // Removed entity is tombstoned for the rest of the session.
        Check(a.Register(9, 100));
        Check(a.Remove(9, 1));
        Check(a.Accept(Make(9, 100, 1, 10, ItemEventKind.Remove, RemoveItem(9)), 100, 2.1) == ItemDecision.Removed);

        // Owner mismatch is checked before the watermark: a forged event never
        // advances it.
        Check(a.Accept(Make(7, 100, 1, 11, ItemEventKind.PoseWorld, WorldPose(7)), 100, 2.2) == ItemDecision.Accepted);
        Check(a.Accept(Make(7, 101, 1, 12, ItemEventKind.PoseWorld, WorldPose(7)), 101, 2.3) == ItemDecision.OwnerMismatch);

        // Invalid shapes.
        Check(a.Accept(null, 100, 2.4) == ItemDecision.Invalid);
        Check(a.Accept(Make(7, 100, 1, 13, ItemEventKind.Unknown, WorldPose(7)), 100, 2.5) == ItemDecision.Invalid);
        Check(a.Accept(Make(7, 100, 1, 13, ItemEventKind.PoseWorld, null), 100, 2.6) == ItemDecision.Invalid);
        Check(a.Accept(Make(7, 100, 1, 13, ItemEventKind.PoseWorld, new byte[0]), 100, 2.7) == ItemDecision.Invalid);
        Check(a.Accept(Make(0, 100, 1, 13, ItemEventKind.PoseWorld, WorldPose(7)), 100, 2.8) == ItemDecision.Invalid);
        Check(a.Accept(Make(7, 100, 1, 0, ItemEventKind.PoseWorld, WorldPose(7)), 100, 2.9) == ItemDecision.Invalid);
        Check(a.Accept(Make(7, 100, 1, 13, ItemEventKind.PoseWorld, new byte[ItemEvent.MaxPayload + 1]), 100, 3.0) == ItemDecision.Invalid);
        Check(a.Verify());

        // A new epoch starts a new event stream: ids may restart.
        ulong epoch;
        Check(a.Register(20, 100));
        Check(a.Accept(Make(20, 100, 1, 40, ItemEventKind.PoseWorld, WorldPose(20)), 100, 3.1) == ItemDecision.Accepted);
        Check(a.Transfer(20, 1, 200, out epoch) && epoch == 2);
        Check(a.Accept(Make(20, 200, 2, 1, ItemEventKind.PoseWorld, WorldPose(20)), 200, 3.2) == ItemDecision.Accepted);
        Check(a.Accept(Make(20, 100, 1, 41, ItemEventKind.PoseWorld, WorldPose(20)), 100, 3.3) == ItemDecision.OwnerMismatch);

        // Bounded dedupe window: an id that fell out of the window can no longer
        // be classified as a replay, so the store fails closed instead of guessing.
        var small = new ItemAuthority(8, 8, 3);
        Check(small.Register(30, 100));
        Check(small.Accept(Make(30, 100, 1, 5, ItemEventKind.PoseWorld, WorldPose(30)), 100, 4.0) == ItemDecision.Accepted);
        Check(small.Accept(Make(30, 100, 1, 6, ItemEventKind.PoseWorld, WorldPose(30)), 100, 4.1) == ItemDecision.Accepted);
        Check(small.Accept(Make(30, 100, 1, 7, ItemEventKind.PoseWorld, WorldPose(30)), 100, 4.2) == ItemDecision.Accepted);
        Check(small.Accept(Make(30, 100, 1, 6, ItemEventKind.PoseWorld, WorldPose(30)), 100, 4.3) == ItemDecision.Duplicate);
        Check(small.Accept(Make(30, 100, 1, 4, ItemEventKind.PoseWorld, WorldPose(30)), 100, 4.4) == ItemDecision.Full);
        Check(small.Accept(Make(30, 100, 1, 8, ItemEventKind.PoseWorld, WorldPose(30)), 100, 4.5) == ItemDecision.Accepted);
        Check(small.Verify());

        string summary = a.Summary();
        Check(summary.Contains("accepted=") && summary.Contains("duplicate=") && summary.Contains("stale=") &&
            summary.Contains("ownerMismatch=") && summary.Contains("removed=") && summary.Contains("full=") &&
            summary.Contains("entities=") && summary.Contains("tombstones="));
        Check(small.Summary().Contains("full=1"));
    }

    static void Policy()
    {
        // Held and worn: only the carrying peer, and only when it is known.
        Check(ItemPolicy.MayWrite(ItemCategory.Held, 5, 5, 5));
        Check(!ItemPolicy.MayWrite(ItemCategory.Held, 5, 5, 6));
        Check(!ItemPolicy.MayWrite(ItemCategory.Held, 5, 5, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.Held, 0, 0, 0));
        Check(ItemPolicy.MayWrite(ItemCategory.Worn, 9, 1, 9));
        Check(!ItemPolicy.MayWrite(ItemCategory.Worn, 9, 9, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.Worn, 9, 9, 8));

        // A held item is never a car or container group member, even when its
        // holder owns the car it sits in.
        const ulong carOwner = 100, holder = 200;
        Check(!ItemPolicy.MayWrite(ItemCategory.Held, carOwner, carOwner, holder));
        Check(!ItemPolicy.GroupMember(ItemCategory.Held));
        Check(!ItemPolicy.GroupMember(ItemCategory.Worn));
        Check(!ItemPolicy.GroupMember(ItemCategory.Loose));
        Check(!ItemPolicy.GroupMember(ItemCategory.PlayerItem));
        Check(!ItemPolicy.GroupMember(ItemCategory.Unknown));
        Check(ItemPolicy.GroupMember(ItemCategory.Stored));
        Check(ItemPolicy.GroupMember(ItemCategory.MountedPart));
        Check(ItemPolicy.GroupMember(ItemCategory.Container));
        Check(ItemPolicy.GroupMember(ItemCategory.CarPart));

        // Stored, mounted part, container and car part: the parent's owner only.
        Check(ItemPolicy.MayWrite(ItemCategory.Stored, 7, 7, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.Stored, 7, 8, 7));
        Check(!ItemPolicy.MayWrite(ItemCategory.Stored, 8, 7, 0));
        Check(ItemPolicy.MayWrite(ItemCategory.MountedPart, 7, 7, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.MountedPart, 8, 7, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.MountedPart, 7, 8, 7));
        Check(ItemPolicy.MayWrite(ItemCategory.Container, 7, 7, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.Container, 8, 7, 8));
        Check(ItemPolicy.MayWrite(ItemCategory.CarPart, 7, 7, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.CarPart, 8, 7, 8));

        // Loose / weapon / ammo / fluid / car root: the item owner only.
        Check(ItemPolicy.MayWrite(ItemCategory.Loose, 3, 3, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.Loose, 3, 4, 3));
        Check(!ItemPolicy.MayWrite(ItemCategory.Loose, 0, 0, 0));
        Check(ItemPolicy.MayWrite(ItemCategory.Weapon, 3, 3, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.Weapon, 4, 3, 4));
        Check(ItemPolicy.MayWrite(ItemCategory.Ammo, 3, 3, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.Ammo, 4, 3, 0));
        Check(ItemPolicy.MayWrite(ItemCategory.Fluid, 3, 3, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.Fluid, 4, 3, 4));
        Check(ItemPolicy.MayWrite(ItemCategory.CarRoot, 3, 3, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.CarRoot, 4, 3, 4));

        // Player item: the player only.
        Check(ItemPolicy.MayWrite(ItemCategory.PlayerItem, 3, 3, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.PlayerItem, 4, 3, 4));
        Check(!ItemPolicy.MayWrite(ItemCategory.PlayerItem, 3, 4, 3));

        // POI: the host/owner only.
        Check(ItemPolicy.MayWrite(ItemCategory.Poi, 1, 1, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.Poi, 2, 1, 2));

        // Unknown and a zero owner fail closed.
        Check(!ItemPolicy.MayWrite(ItemCategory.Unknown, 1, 1, 1));
        Check(!ItemPolicy.MayWrite(ItemCategory.Loose, 1, 0, 0));
        Check(!ItemPolicy.MayWrite(ItemCategory.Stored, 1, 0, 1));
        Check(!ItemPolicy.MayWrite(ItemCategory.PlayerItem, 1, 0, 1));
    }

    static void TranslateCase(ItemAuthority a, byte[] native, int id, ItemEventKind expected)
    {
        ItemEvent e;
        Check(a.TryTranslate(native, 1, out e));
        Check(e.Kind == expected);
        Check(e.Entity == (ulong)id && e.EventId == 1);
        Check(e.Owner == 0 && e.Epoch == 0);                       // caller fills these
        Check(e.Flags == (byte)ItemEventPolicy.FlagsFor(expected));
        Check(Same(e.Payload, native));                            // byte-for-byte
        byte[] replay;
        Check(a.TryReplay(e, out replay) && Same(replay, native));
        e.Entity = (ulong)id + 1;                                  // payload says otherwise
        Check(!a.TryReplay(e, out replay) && replay == null);
    }

    static void Translation()
    {
        var a = new ItemAuthority();
        TranslateCase(a, WorldPose(11), 11, ItemEventKind.PoseWorld);
        TranslateCase(a, LocalPose(11, 0), 11, ItemEventKind.PoseLocalStorage);
        TranslateCase(a, LocalPose(11, 3), 11, ItemEventKind.PoseLocalStorage);
        TranslateCase(a, LocalPose(11, -100000), 11, ItemEventKind.PoseLocalMount);
        TranslateCase(a, LocalPose(11, -200000), 11, ItemEventKind.PoseLocalMount);
        TranslateCase(a, PlayerPose(11), 11, ItemEventKind.PosePlayer);
        TranslateCase(a, ItemVelocity(11), 11, ItemEventKind.Velocity);
        TranslateCase(a, CarVelocity(11), 11, ItemEventKind.CarVelocity);
        TranslateCase(a, PartCondition(11), 11, ItemEventKind.PartCondition);
        TranslateCase(a, Plate(11), 11, ItemEventKind.Plate);
        TranslateCase(a, BusDoors(11), 11, ItemEventKind.BusDoors);
        TranslateCase(a, RndType(11), 11, ItemEventKind.RndType);
        TranslateCase(a, Food(11), 11, ItemEventKind.Food);
        TranslateCase(a, Ammo(11), 11, ItemEventKind.Ammo);
        TranslateCase(a, Push(11), 11, ItemEventKind.Push);
        TranslateCase(a, Weapon(11), 11, ItemEventKind.Weapon);
        TranslateCase(a, WeaponShot(11), 11, ItemEventKind.WeaponShot);
        TranslateCase(a, Paint(11), 11, ItemEventKind.Paint);
        TranslateCase(a, Breakable(11), 11, ItemEventKind.Breakable);
        TranslateCase(a, RemoveItem(11), 11, ItemEventKind.Remove);
        TranslateCase(a, CarValues(11), 11, ItemEventKind.CarValues);
        TranslateCase(a, CarHandbrake(11), 11, ItemEventKind.CarHandbrake);
        TranslateCase(a, CarPose(11), 11, ItemEventKind.CarPose);
        TranslateCase(a, Usable(11), 11, ItemEventKind.Usable);
        TranslateCase(a, Fluid(11), 11, ItemEventKind.Fluid);
        TranslateCase(a, Ragdoll(11), 11, ItemEventKind.Ragdoll);
        TranslateCase(a, Outfit(11), 11, ItemEventKind.Outfit);
        TranslateCase(a, AskItemData(11), 11, ItemEventKind.RepairRequest);
        TranslateCase(a, AskItemPos(11), 11, ItemEventKind.RepairRequest);

        // 42 splits on attachType; a zero attachType is the detach form.
        TranslateCase(a, Attach(11, 2), 11, ItemEventKind.Attach);
        TranslateCase(a, Attach(11, 0), 11, ItemEventKind.Detach);
        ItemEvent e;
        Check(a.TryTranslate(Attach(11, 0), 1, out e) && BitConverter.ToInt32(e.Payload, 5) == 0);

        // 17 outside both windows (the -1..-99999 gap) is not a mapped family.
        Check(!a.TryTranslate(LocalPose(11, -1), 1, out e) && e == null);
        Check(!a.TryTranslate(LocalPose(11, -99999), 1, out e) && e == null);

        // Invalid native messages.
        Check(!a.TryTranslate(null, 1, out e));
        Check(!a.TryTranslate(new byte[0], 1, out e));
        Check(!a.TryTranslate(Msg(16, 44), 1, out e));             // truncated pose
        Check(!a.TryTranslate(new byte[] { 16, 1, 2, 3 }, 1, out e));
        Check(!a.TryTranslate(new byte[] { 0 }, 1, out e));

        // A zero entity id is refused for every family.
        Check(!a.TryTranslate(WorldPose(0), 1, out e));
        Check(!a.TryTranslate(RemoveItem(0), 1, out e));
        Check(!a.TryTranslate(AskItemData(0), 1, out e));
        Check(!a.TryTranslate(AskItemPos(0), 1, out e));
        Check(!a.TryTranslate(Msg(40, 30), 1, out e));             // usable with a zero item id

        // Event id 0 is never a valid identity.
        Check(!a.TryTranslate(WorldPose(11), 0, out e));

        // A structurally valid native message larger than the envelope budget is
        // refused rather than split.
        var wide = new byte[6009];
        wide[0] = 24; Put(wide, 1, 11); Put(wide, 5, 3000);
        Check(StablePackets.Valid(wide));
        Check(!a.TryTranslate(wide, 1, out e));

        // Replay refusals.
        byte[] replay;
        Check(a.TryTranslate(WorldPose(11), 1, out e));
        Check(!a.TryReplay(null, out replay) && replay == null);
        var broken = new ItemEvent { Entity = 11, Owner = 100, Epoch = 1, EventId = 1, Kind = ItemEventKind.PoseWorld, Payload = (byte[])e.Payload.Clone() };
        broken.Payload[0] = 99;
        Check(!a.TryReplay(broken, out replay));
        broken.Payload = null;
        Check(!a.TryReplay(broken, out replay));
        e.Entity = 0;
        Check(!a.TryReplay(e, out replay));

        // End to end: native -> event -> envelope -> event -> accept -> replay.
        ItemEvent translated;
        Check(a.TryTranslate(WorldPose(7), 3, out translated));
        translated.Owner = 100;
        translated.Epoch = 1;
        Check(a.Register(7, 100));
        ItemEvent received;
        Check(ItemEvent.TryRead(translated.Write(), out received));
        Check(a.Accept(received, 100, 1.0) == ItemDecision.Accepted);
        Check(a.TryReplay(received, out replay) && Same(replay, WorldPose(7)));
    }

    static void FlagTable()
    {
        // Unreliable complete-state families are latest-value and replaceable.
        for (int i = 0; i < 4; i++)
        {
            ItemEventKind kind = i == 0 ? ItemEventKind.PoseWorld : i == 1 ? ItemEventKind.Velocity :
                i == 2 ? ItemEventKind.CarVelocity : ItemEventKind.CarPose;
            byte flags = (byte)ItemEventPolicy.FlagsFor(kind);
            Check(ItemEventPolicy.Has(flags, ItemEventFlags.LatestValue));
            Check(ItemEventPolicy.Has(flags, ItemEventFlags.Replaceable));
            Check(!ItemEventPolicy.Has(flags, ItemEventFlags.Reliable));
            Check(!ItemEventPolicy.Has(flags, ItemEventFlags.OneOff));
        }
        // Unreliable partial-state families are replaceable but not coalesced.
        for (int i = 0; i < 4; i++)
        {
            ItemEventKind kind = i == 0 ? ItemEventKind.PoseLocalStorage : i == 1 ? ItemEventKind.PoseLocalMount :
                i == 2 ? ItemEventKind.PosePlayer : ItemEventKind.Ragdoll;
            byte flags = (byte)ItemEventPolicy.FlagsFor(kind);
            Check(ItemEventPolicy.Has(flags, ItemEventFlags.Replaceable));
            Check(!ItemEventPolicy.Has(flags, ItemEventFlags.LatestValue));
            Check(!ItemEventPolicy.Has(flags, ItemEventFlags.Reliable));
        }

        // One-off families are never coalesced.
        Check((byte)ItemEventPolicy.FlagsFor(ItemEventKind.Push) == (byte)ItemEventFlags.OneOff);
        Check(ItemEventPolicy.Has((byte)ItemEventPolicy.FlagsFor(ItemEventKind.WeaponShot), ItemEventFlags.OneOff));
        Check(ItemEventPolicy.Has((byte)ItemEventPolicy.FlagsFor(ItemEventKind.WeaponShot), ItemEventFlags.Reliable));

        // Reliable state families are ordered and neither latest-value nor one-off.
        ItemEventKind[] ordered = new ItemEventKind[]
        {
            ItemEventKind.PartCondition, ItemEventKind.Plate, ItemEventKind.BusDoors, ItemEventKind.RndType,
            ItemEventKind.Food, ItemEventKind.Ammo, ItemEventKind.Weapon, ItemEventKind.Paint,
            ItemEventKind.Breakable, ItemEventKind.Remove, ItemEventKind.CarValues, ItemEventKind.CarHandbrake,
            ItemEventKind.Usable, ItemEventKind.Fluid, ItemEventKind.Attach, ItemEventKind.Detach, ItemEventKind.Outfit
        };
        for (int i = 0; i < ordered.Length; i++)
        {
            byte flags = (byte)ItemEventPolicy.FlagsFor(ordered[i]);
            Check(ItemEventPolicy.Has(flags, ItemEventFlags.Reliable));
            Check(ItemEventPolicy.Has(flags, ItemEventFlags.Ordered));
            Check(!ItemEventPolicy.Has(flags, ItemEventFlags.LatestValue));
            Check(!ItemEventPolicy.Has(flags, ItemEventFlags.OneOff));
        }

        // Requests carry no state and no owner requirement; Unknown has no policy.
        Check((byte)ItemEventPolicy.FlagsFor(ItemEventKind.RepairRequest) == (byte)ItemEventFlags.Ordered);
        Check((byte)ItemEventPolicy.FlagsFor(ItemEventKind.Unknown) == 0);

        // Every state family requires the owner; the request family does not.
        for (int i = 1; i < (int)ItemEventKind.RepairRequest; i++)
            Check(ItemEventPolicy.RequiresOwner((ItemEventKind)i));
        Check(!ItemEventPolicy.RequiresOwner(ItemEventKind.Unknown));
        Check(!ItemEventPolicy.RequiresOwner(ItemEventKind.RepairRequest));
    }

    // itemPosPlayer (18) with the sender identity it carries: bool hasVel,
    // u64 steamID, u32 id, Vector3 pos, Vector3 rot.
    static byte[] PlayerPoseAs(ulong steam, int id)
    {
        byte[] b = Msg(18, 42);
        Buffer.BlockCopy(BitConverter.GetBytes(steam), 0, b, 2, 8);
        return Put(b, 10, id);
    }

    static ItemMemberLookup Lookup(Dictionary<uint, ulong> map)
    {
        return delegate(uint member, out ulong owner) { return map.TryGetValue(member, out owner); };
    }

    static ExperimentalItemGate.GateResult Verdict(ulong sender, byte[] native, ItemMemberLookup lookup)
    {
        return ExperimentalItemGate.Check(sender, native, lookup);
    }

    // Gate rules: only a provably foreign write is refused. Malformed bytes, loose
    // or unknown items, unmodelled types and repair requests stay native.
    static void Gate()
    {
        var members = new Dictionary<uint, ulong>();
        members[50] = 100;   // stored cargo of a car owned by peer 100
        members[60] = 100;   // fitted part of the same car
        ItemMemberLookup lookup = Lookup(members);

        // itemPosPlayer: the sender identity inside the message must match the
        // authenticated peer, and zero is never a legitimate identity.
        Check(Verdict(100, PlayerPoseAs(100, 50), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, PlayerPoseAs(100, 50), lookup) == ExperimentalItemGate.GateResult.RejectSenderIdentity);
        Check(Verdict(100, PlayerPoseAs(0, 50), lookup) == ExperimentalItemGate.GateResult.RejectSenderIdentity);
        Check(Verdict(0, PlayerPoseAs(0, 50), lookup) == ExperimentalItemGate.GateResult.RejectSenderIdentity);
        // The identity is checked before the item, so a forged holder never reaches
        // the owner comparison.
        Check(Verdict(200, PlayerPoseAs(200, 50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        // A held item that is not a group member is written by whoever holds it.
        Check(Verdict(7, PlayerPoseAs(7, 90), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(!ExperimentalItemGate.Allow(200, PlayerPoseAs(100, 50), lookup));

        // A known group member is writable only by the owner of its car.
        Check(Verdict(100, WorldPose(50), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, WorldPose(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(100, LocalPose(50, 3), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, LocalPose(50, 3), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(100, LocalPose(60, -100000), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, LocalPose(60, -100000), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);

        // removeItem (36) is destructive, so a non-owner is refused.
        Check(Verdict(100, RemoveItem(50), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, RemoveItem(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(!ExperimentalItemGate.Allow(200, RemoveItem(50), lookup));

        // Every modelled state family uses the same rule.
        ItemMemberLookup noOwner = Lookup(new Dictionary<uint, ulong>());
        Check(Verdict(100, ItemVelocity(50), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(100, CarVelocity(50), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(100, ItemVelocity(50), noOwner) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, ItemVelocity(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, CarVelocity(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, PartCondition(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Plate(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, BusDoors(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, RndType(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Food(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Ammo(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Push(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Weapon(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, WeaponShot(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Paint(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Breakable(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, CarValues(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, CarHandbrake(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, CarPose(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Usable(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Fluid(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Attach(50, 2), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Attach(50, 0), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Ragdoll(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(200, Outfit(50), lookup) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);

        // Repair requests (21 ask item data, 46 ask item pos): any peer may ask.
        Check(Verdict(999, AskItemData(50), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(999, AskItemPos(50), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(999, AskItemData(0), lookup) == ExperimentalItemGate.GateResult.Allow);

        // An unknown (loose) item and a zero entity stay native.
        Check(Verdict(5, WorldPose(51), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(5, RemoveItem(51), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(5, WorldPose(0), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(5, RemoveItem(0), lookup) == ExperimentalItemGate.GateResult.Allow);

        // No lookup at all means no owner information: everything is allowed.
        Check(Verdict(5, WorldPose(50), null) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(5, RemoveItem(50), null) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(5, PlayerPoseAs(5, 50), null) == ExperimentalItemGate.GateResult.Allow);

        // Malformed bytes are never the gate's decision: native validation rejects
        // them, and the gate must not turn a malformed packet into a rejection.
        Check(Verdict(200, null, lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, new byte[0], lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, new byte[] { 16, 1, 2, 3 }, lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, Msg(16, 44), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, Msg(18, 41), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, Msg(36, 4), lookup) == ExperimentalItemGate.GateResult.Allow);
        var junk = new byte[45];
        for (int i = 0; i < junk.Length; i++) junk[i] = 0xFF;
        Check(Verdict(200, junk, lookup) == ExperimentalItemGate.GateResult.Allow);

        // Unmodelled and session types are untouched: player pose (10), map (8),
        // vehicle motion (253) and every other type stay native.
        Check(Verdict(200, Msg(10, 39), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, Msg(12, 9), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, Msg(39, 49), lookup) == ExperimentalItemGate.GateResult.Allow); // no entity in the table
        Check(Verdict(200, Msg(253, 11), lookup) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(200, Msg(254, 3), lookup) == ExperimentalItemGate.GateResult.Allow);

        // The enum is part of the contract the bridge logs; keep the order stable.
        Check((int)ExperimentalItemGate.GateResult.Allow == 0);
        Check((int)ExperimentalItemGate.GateResult.RejectUnknownOwner == 1);
        Check((int)ExperimentalItemGate.GateResult.RejectSenderIdentity == 2);
        Check((int)ExperimentalItemGate.GateResult.RejectOwnerMismatch == 3);
    }

    // The lookup the host supplies: a car root answers with its lease owner, a
    // group member with the owner of its car, and anything else is unknown.
    static void GateLookup()
    {
        const ulong host = 1, peer = 10, other = 30;

        var authority = new VehicleAuthority(host, 64, 64, 1.5);
        Check(authority.AllowPeer(peer, 7, 2, 0));
        Check(authority.Verify());

        ulong owner;

        // A car root resolves to its lease owner.
        Check(authority.TryMemberOwner(7, out owner) && owner == peer);

        // An item that was never in a group is unknown, never a rejection.
        Check(!authority.TryMemberOwner(0, out owner) && owner == 0);
        Check(!authority.TryMemberOwner(99, out owner) && owner == 0);

        // A rebuilt group maps every member to the car.
        Check(authority.RebuildGroup(7, new uint[] { 20, 21 }));
        Check(authority.GroupSize(7) == 2);
        Check(authority.TryMemberOwner(20, out owner) && owner == peer);
        Check(authority.TryMemberOwner(21, out owner) && owner == peer);
        Check(authority.Verify());

        // The gate now resolves stored/mounted items through the car group.
        Check(Verdict(peer, WorldPose(20), authority.TryMemberOwner) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(other, WorldPose(20), authority.TryMemberOwner) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(other, LocalPose(21, -100000), authority.TryMemberOwner) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(other, RemoveItem(20), authority.TryMemberOwner) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(peer, RemoveItem(20), authority.TryMemberOwner) == ExperimentalItemGate.GateResult.Allow);
        Check(Verdict(other, WorldPose(99), authority.TryMemberOwner) == ExperimentalItemGate.GateResult.Allow);

        // A removal from the group drops the lookup entry and the gate falls back
        // to native handling for that item.
        Check(authority.RebuildGroup(7, new uint[] { 21 }));
        Check(authority.GroupSize(7) == 1);
        Check(!authority.TryMemberOwner(20, out owner) && owner == 0);
        Check(authority.TryMemberOwner(21, out owner) && owner == peer);
        Check(Verdict(other, WorldPose(20), authority.TryMemberOwner) == ExperimentalItemGate.GateResult.Allow);
        Check(authority.Verify());

        // A takeover carries the group, so the lookup follows the new owner.
        Check(authority.AllowPeer(other, 7, 4, 0.1));
        Check(authority.TryMemberOwner(7, out owner) && owner == other);
        Check(authority.TryMemberOwner(21, out owner) && owner == other);
        Check(Verdict(peer, WorldPose(21), authority.TryMemberOwner) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(other, WorldPose(21), authority.TryMemberOwner) == ExperimentalItemGate.GateResult.Allow);
        Check(authority.Verify());
        Check(authority.Summary().Contains("members="));

        // Departed peer: the group moves back to the host with the car and no
        // lookup entry keeps pointing at the peer that left.
        var departing = new VehicleAuthority(host, 64, 64, 1.5);
        Check(departing.AllowPeer(peer, 7, 2, 0));
        Check(departing.RebuildGroup(7, new uint[] { 20, 21 }));
        departing.ForgetPeer(peer, 0.5);
        Check(departing.TryMemberOwner(7, out owner) && owner == host);
        Check(departing.TryMemberOwner(20, out owner) && owner == host);
        Check(departing.TryMemberOwner(21, out owner) && owner == host);
        Check(!departing.TryMemberOwner(99, out owner));
        Check(Verdict(peer, WorldPose(20), departing.TryMemberOwner) == ExperimentalItemGate.GateResult.RejectOwnerMismatch);
        Check(Verdict(host, WorldPose(20), departing.TryMemberOwner) == ExperimentalItemGate.GateResult.Allow);
        Check(departing.Verify());

        // A split group cannot move atomically, so the car is reclaimed alone and
        // its members are dropped from the lookup instead of inheriting the peer.
        var split = new VehicleAuthority(host, 64, 64, 1.5);
        Check(split.AllowPeer(peer, 7, 2, 0));
        Check(split.RebuildGroup(7, new uint[] { 20, 21 }));
        Check(split.AllowPeer(other, 20, 6, 0.1));   // member 20 wins its own lease
        Check(split.TryMemberOwner(20, out owner) && owner == other);
        split.ForgetPeer(peer, 0.2);
        Check(split.GroupSize(7) == 0);
        Check(!split.TryMemberOwner(21, out owner) && owner == 0);
        Check(split.TryMemberOwner(20, out owner) && owner == other);
        Check(split.Verify());
    }

    public static int Main()
    {
        Envelope();
        Registry();
        Acceptance();
        Policy();
        Translation();
        FlagTable();
        Gate();
        GateLookup();
        Console.WriteLine("PASS: item event envelope, registry, category policy, native translation and authority gate (" + checks + " checks)");
        return 0;
    }
}
