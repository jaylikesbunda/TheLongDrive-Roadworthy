using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TldMp.Experimental
{
    // Phase 4 item authority core (docs/MULTIPLAYER_ITEM_AUTHORITY_INVENTORY.md).
    // Pure bookkeeping: no Unity types, no game calls, no transport, no delivery.
    // It answers two questions only: may this authenticated peer describe this
    // item entity, and is this event newer than the last accepted one for it.
    // The payload is always the untouched native message, so replay is exact.

    // One family per native item message (inventory §2). The numeric value is a
    // local family id and is never written to the wire; the native message type
    // stays inside the payload. 17 splits by pose index, 42 splits by attachType.
    internal enum ItemEventKind : byte
    {
        Unknown = 0,
        PoseWorld = 1,         // 16 itemPosGlobal
        PoseLocalStorage = 2,  // 17 itemPosLocal, index >= 0
        PoseLocalMount = 3,    // 17 itemPosLocal, index <= -100000 (attached)
        PosePlayer = 4,        // 18 itemPosPlayer
        Velocity = 5,          // 19
        CarVelocity = 6,       // 20
        PartCondition = 7,     // 23
        Plate = 8,             // 24
        BusDoors = 9,          // 25
        RndType = 10,          // 26
        Food = 11,             // 27
        Ammo = 12,             // 28
        Push = 13,             // 29
        Weapon = 14,           // 30
        WeaponShot = 15,       // 31
        Paint = 16,            // 32
        Breakable = 17,        // 33
        Remove = 18,           // 36
        CarValues = 19,        // 37
        CarHandbrake = 20,     // 38
        CarPose = 21,          // 39
        Usable = 22,           // 40
        Fluid = 23,            // 41
        Attach = 24,           // 42 attachType != 0
        Detach = 25,           // 42 attachType == 0
        Ragdoll = 26,          // 43
        Outfit = 27,           // 48
        RepairRequest = 28     // 21 (ask item data) / 46 (ask item pos)
    }

    // Transport policy per family, never an authority decision. Reliable/Ordered
    // describe delivery expectations; LatestValue means the message carries the
    // complete state of one object, so a still-queued older one is obsolete
    // (the same set StablePackets.LatestValueKey coalesces); Replaceable means a
    // newer event for the same entity supersedes an older one without needing
    // the full-state property; OneOff means the event must never be coalesced.
    [Flags]
    internal enum ItemEventFlags : byte
    {
        None = 0,
        Reliable = 1,
        Ordered = 2,
        LatestValue = 4,
        Replaceable = 8,
        OneOff = 16
    }

    // Member -> owning peer of the car group the member belongs to. Implemented
    // by the host's VehicleAuthority (memberToCar); supplied as a delegate so the
    // gate below stays pure and testable without a coordinator.
    internal delegate bool ItemMemberLookup(uint member, out ulong owner);

    internal static class ItemEventPolicy
    {
        internal static ItemEventFlags FlagsFor(ItemEventKind kind)
        {
            switch (kind)
            {
                // Unreliable, complete one-object state: newest wins.
                case ItemEventKind.PoseWorld:
                case ItemEventKind.Velocity:
                case ItemEventKind.CarVelocity:
                case ItemEventKind.CarPose:
                    return ItemEventFlags.LatestValue | ItemEventFlags.Replaceable;
                // Unreliable, partial or per-part state: superseded by a newer
                // event of the same entity, but never merged across families.
                case ItemEventKind.PoseLocalStorage:
                case ItemEventKind.PoseLocalMount:
                case ItemEventKind.PosePlayer:
                case ItemEventKind.Ragdoll:
                    return ItemEventFlags.Replaceable;
                // Reliable per-entity state: ordered, idempotent, never merged.
                case ItemEventKind.PartCondition:
                case ItemEventKind.Plate:
                case ItemEventKind.BusDoors:
                case ItemEventKind.RndType:
                case ItemEventKind.Food:
                case ItemEventKind.Ammo:
                case ItemEventKind.Weapon:
                case ItemEventKind.Paint:
                case ItemEventKind.Breakable:
                case ItemEventKind.Remove:
                case ItemEventKind.CarValues:
                case ItemEventKind.CarHandbrake:
                case ItemEventKind.Usable:
                case ItemEventKind.Fluid:
                case ItemEventKind.Attach:
                case ItemEventKind.Detach:
                case ItemEventKind.Outfit:
                    return ItemEventFlags.Reliable | ItemEventFlags.Ordered;
                // One-off impulses: never coalesced, never reordered into a state.
                case ItemEventKind.Push:
                    return ItemEventFlags.OneOff;
                case ItemEventKind.WeaponShot:
                    return ItemEventFlags.Reliable | ItemEventFlags.Ordered | ItemEventFlags.OneOff;
                // Requests carry no state; 21 is reliable and 46 unreliable in
                // stock send policy, so the envelope promises ordering only.
                case ItemEventKind.RepairRequest:
                    return ItemEventFlags.Ordered;
                default:
                    return ItemEventFlags.None;
            }
        }

        // Every state family needs the owning peer. A repair request is the one
        // exception: a peer that does not own an item may ask the owner to
        // resend it, which is the entire purpose of 21/46.
        internal static bool RequiresOwner(ItemEventKind kind)
        {
            return kind != ItemEventKind.Unknown && kind != ItemEventKind.RepairRequest;
        }

        internal static bool Has(byte flags, ItemEventFlags flag)
        {
            return (flags & (byte)flag) != 0;
        }
    }

    // Identity and authority around one native item message. The envelope adds
    // (entity, owner, epoch, eventId) and leaves the payload byte-for-byte as
    // the native sender produced it, so the receiver can replay it unchanged.
    // magic u32 "ETM4" | version u8 | entity u64 | owner u64 | epoch u64 |
    // eventId u64 | kind u8 | flags u8 | payloadLen u16 | payload
    internal sealed class ItemEvent
    {
        internal const uint Magic = 0x344D5445; // "ETM4"
        internal const byte Version = 1;
        internal const int HeaderBytes = 4 + 1 + 8 + 8 + 8 + 8 + 1 + 1 + 2;
        internal const int MaxPayload = 4096;

        internal ulong Entity, Owner, Epoch, EventId;
        internal ItemEventKind Kind;
        internal byte Flags;
        internal byte[] Payload;

        const byte KnownFlags = (byte)(ItemEventFlags.Reliable | ItemEventFlags.Ordered |
            ItemEventFlags.LatestValue | ItemEventFlags.Replaceable | ItemEventFlags.OneOff);

        internal bool Valid()
        {
            return Entity != 0 && Owner != 0 && Epoch != 0 && EventId != 0 &&
                Kind != ItemEventKind.Unknown && Kind <= ItemEventKind.RepairRequest &&
                (Flags & ~KnownFlags) == 0 &&
                Payload != null && Payload.Length > 0 && Payload.Length <= MaxPayload;
        }

        internal byte[] Write()
        {
            if (!Valid()) throw new ArgumentException("Invalid item event.");
            var bytes = new byte[HeaderBytes + Payload.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(Magic), 0, bytes, 0, 4);
            bytes[4] = Version;
            Buffer.BlockCopy(BitConverter.GetBytes(Entity), 0, bytes, 5, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(Owner), 0, bytes, 13, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(Epoch), 0, bytes, 21, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(EventId), 0, bytes, 29, 8);
            bytes[37] = (byte)Kind;
            bytes[38] = Flags;
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)Payload.Length), 0, bytes, 39, 2);
            Buffer.BlockCopy(Payload, 0, bytes, HeaderBytes, Payload.Length);
            return bytes;
        }

        // Never throws: every malformed frame (magic, version, framing, zero
        // identity, unknown kind, reserved flag bits, payload length) is false.
        internal static bool TryRead(byte[] frame, out ItemEvent e)
        {
            e = null;
            try
            {
                if (frame == null || frame.Length < HeaderBytes || frame.Length > HeaderBytes + MaxPayload) return false;
                if (BitConverter.ToUInt32(frame, 0) != Magic || frame[4] != Version) return false;
                ulong entity = BitConverter.ToUInt64(frame, 5);
                ulong owner = BitConverter.ToUInt64(frame, 13);
                ulong epoch = BitConverter.ToUInt64(frame, 21);
                ulong eventId = BitConverter.ToUInt64(frame, 29);
                byte kind = frame[37];
                byte flags = frame[38];
                int length = BitConverter.ToUInt16(frame, 39);
                if (entity == 0 || owner == 0 || epoch == 0 || eventId == 0) return false;
                if (kind == 0 || kind > (byte)ItemEventKind.RepairRequest) return false;
                if ((flags & ~KnownFlags) != 0) return false;
                if (length < 1 || length > MaxPayload || length != frame.Length - HeaderBytes) return false;
                var value = new ItemEvent { Entity = entity, Owner = owner, Epoch = epoch, EventId = eventId,
                    Kind = (ItemEventKind)kind, Flags = flags, Payload = new byte[length] };
                Buffer.BlockCopy(frame, HeaderBytes, value.Payload, 0, length);
                e = value;
                return true;
            }
            catch (Exception) { e = null; return false; }
        }
    }

    // Inventory §1. The category is supplied by the caller; it is never guessed
    // here and no heuristic inference is allowed.
    internal enum ItemCategory : byte
    {
        Unknown = 0,
        Loose = 1,
        Held = 2,
        Worn = 3,
        Stored = 4,
        MountedPart = 5,
        Container = 6,
        Weapon = 7,
        Ammo = 8,
        Fluid = 9,
        CarPart = 10,
        PlayerItem = 11,
        Poi = 12,
        CarRoot = 13
    }

    // Category write rules. sender is the authenticated transport peer; owner is
    // the registered owner of the item (its parent for stored/mounted items, the
    // player for held/worn/player items, the car for a car root); holder is the
    // peer carrying a held or worn item, 0 when unknown. Every rule is exact
    // equality against one peer, never a group or a hierarchy walk.
    internal static class ItemPolicy
    {
        internal static bool MayWrite(ItemCategory category, ulong authenticatedSender, ulong owner, ulong holder)
        {
            if (authenticatedSender == 0) return false;
            switch (category)
            {
                // Only the carrying peer may write a held or worn item, and only
                // if that peer is known. Ownership of the parent (a car, a
                // container, another player) never authorises a held write.
                case ItemCategory.Held:
                case ItemCategory.Worn:
                    return holder != 0 && authenticatedSender == holder;
                // Cargo, fitted parts, nested containers and car parts are
                // written through the parent's owner; a holder cannot move them.
                case ItemCategory.Stored:
                case ItemCategory.MountedPart:
                case ItemCategory.Container:
                case ItemCategory.CarPart:
                    return owner != 0 && authenticatedSender == owner;
                // One peer owns the whole object: the loose/kinematic entity, the
                // weapon/ammo/fluid item, or the car root.
                case ItemCategory.Loose:
                case ItemCategory.Weapon:
                case ItemCategory.Ammo:
                case ItemCategory.Fluid:
                case ItemCategory.CarRoot:
                    return owner != 0 && authenticatedSender == owner;
                // A player's inventory items belong to that player.
                case ItemCategory.PlayerItem:
                    return owner != 0 && authenticatedSender == owner;
                // POI records are host-owned world state.
                case ItemCategory.Poi:
                    return owner != 0 && authenticatedSender == owner;
                default:
                    return false;
            }
        }

        // The car-group membership classes of inventory §3. Held, worn, loose,
        // weapon/ammo/fluid and player items are excluded: a held item is never a
        // car/container group member, even when its holder owns the car.
        internal static bool GroupMember(ItemCategory category)
        {
            return category == ItemCategory.Stored || category == ItemCategory.MountedPart ||
                category == ItemCategory.Container || category == ItemCategory.CarPart;
        }
    }

    internal enum ItemDecision : byte
    {
        Accepted = 0,
        Duplicate = 1,
        Stale = 2,
        OwnerMismatch = 3,
        Unknown = 4,
        Removed = 5,
        Full = 6,
        Invalid = 7
    }

    // In-memory entity registry with bounded everything. Entities carry a single
    // owner and epoch (as AuthorityRegistry does for cars); events carry the pair
    // they were produced under, and the authenticated sender must be that owner.
    // A removed entity becomes a tombstone and is never registered again.
    internal sealed class ItemAuthority
    {
        internal const int DefaultMaxEntities = 4096;
        internal const int DefaultMaxTombstones = 4096;
        internal const int DefaultMaxEventsPerEntity = 128;

        sealed class Entry
        {
            internal ulong Owner, Epoch;
            internal bool Removed;
            // Ring of the newest accepted event ids; the dedupe window that lets
            // Accept tell a duplicate (replay) from a stale (older, unseen) id.
            internal readonly ulong[] Accepted;
            internal int Count, At;
            internal ulong EventId;
            internal double LastSeen;

            internal Entry(ulong owner, int window)
            {
                Owner = owner;
                Epoch = 1;
                Accepted = new ulong[window];
            }

            internal bool Contains(ulong id)
            {
                for (int i = 0; i < Count; i++) if (Accepted[i] == id) return true;
                return false;
            }

            // Oldest id still retained; only meaningful when the window is full.
            internal ulong Oldest { get { return Accepted[At]; } }
            internal bool FullWindow { get { return Count == Accepted.Length; } }

            internal void Add(ulong id)
            {
                Accepted[At] = id;
                At = (At + 1) % Accepted.Length;
                if (Count < Accepted.Length) Count++;
                EventId = id;
            }

            // A new epoch is a new event stream: the previous owner's ids and
            // watermark must not constrain the new owner.
            internal void Reset()
            {
                Count = 0;
                At = 0;
                EventId = 0;
                LastSeen = 0;
            }
        }

        readonly Dictionary<ulong, Entry> entities = new Dictionary<ulong, Entry>();
        readonly Dictionary<ulong, ulong> tombstones = new Dictionary<ulong, ulong>();
        readonly int maxEntities, maxTombstones, maxEventsPerEntity;
        int accepted, duplicate, stale, ownerMismatch, removed, full, unknown, invalid;

        internal ItemAuthority() : this(DefaultMaxEntities, DefaultMaxTombstones, DefaultMaxEventsPerEntity) { }

        internal ItemAuthority(int maxEntities, int maxTombstones, int maxEventsPerEntity)
        {
            if (maxEntities <= 0 || maxTombstones <= 0 || maxEventsPerEntity <= 0)
                throw new ArgumentException("Invalid item authority limits.");
            this.maxEntities = maxEntities;
            this.maxTombstones = maxTombstones;
            this.maxEventsPerEntity = maxEventsPerEntity;
        }

        internal int Count { get { return entities.Count; } }
        internal int TombstoneCount { get { return tombstones.Count; } }

        internal bool Contains(ulong entity)
        {
            Entry entry;
            return entities.TryGetValue(entity, out entry) && !entry.Removed;
        }

        internal bool IsRemoved(ulong entity) { return tombstones.ContainsKey(entity); }

        internal bool TryOwner(ulong entity, out ulong owner, out ulong epoch)
        {
            owner = 0; epoch = 0;
            Entry entry;
            if (!entities.TryGetValue(entity, out entry) || entry.Removed) return false;
            owner = entry.Owner;
            epoch = entry.Epoch;
            return true;
        }

        // Registration never resurrects a tombstoned id and never evicts: a full
        // registry refuses the entity instead of guessing which one to drop.
        internal bool Register(ulong entity, ulong owner)
        {
            if (entity == 0 || owner == 0 || entities.ContainsKey(entity) || tombstones.ContainsKey(entity)) return false;
            if (entities.Count >= maxEntities) { full++; return false; }
            entities.Add(entity, new Entry(owner, maxEventsPerEntity));
            return true;
        }

        // The expected epoch makes a repeated or concurrent transfer fail rather
        // than moving the entity twice. Epochs are per entity and never reused.
        internal bool Transfer(ulong entity, ulong expectedEpoch, ulong newOwner, out ulong epoch)
        {
            epoch = 0;
            Entry entry;
            if (newOwner == 0 || expectedEpoch == 0 || !entities.TryGetValue(entity, out entry)) return false;
            if (entry.Removed || entry.Epoch != expectedEpoch || entry.Owner == newOwner) return false;
            if (entry.Epoch == UInt64.MaxValue) return false;
            entry.Owner = newOwner;
            epoch = ++entry.Epoch;
            entry.Reset();
            return true;
        }

        // Removal is final. Without a tombstone the id could be registered again,
        // so a full tombstone store refuses the removal (the entity stays live).
        internal bool Remove(ulong entity, ulong expectedEpoch)
        {
            Entry entry;
            if (expectedEpoch == 0 || !entities.TryGetValue(entity, out entry) || entry.Removed) return false;
            if (entry.Epoch != expectedEpoch) return false;
            if (tombstones.Count >= maxTombstones) { full++; return false; }
            entry.Removed = true;
            entities.Remove(entity);
            tombstones.Add(entity, entry.Epoch);
            return true;
        }

        // Receive-side gate. The event must name the registered owner and epoch,
        // the authenticated sender must be that owner, and the event id must be
        // newer than everything already accepted for the entity. Accepted means
        // "deliver this envelope as is"; the payload is not decoded, structurally
        // checked or applied here, and nothing touches native code.
        internal ItemDecision Accept(ItemEvent e, ulong authenticatedSender, double now)
        {
            if (e == null || e.Entity == 0 || e.Owner == 0 || e.Epoch == 0 || e.EventId == 0 ||
                e.Kind == ItemEventKind.Unknown || e.Kind > ItemEventKind.RepairRequest ||
                e.Payload == null || e.Payload.Length == 0 || e.Payload.Length > ItemEvent.MaxPayload)
            { invalid++; return ItemDecision.Invalid; }
            if (tombstones.ContainsKey(e.Entity)) { removed++; return ItemDecision.Removed; }
            Entry entry;
            if (!entities.TryGetValue(e.Entity, out entry)) { unknown++; return ItemDecision.Unknown; }
            if (entry.Removed) { removed++; return ItemDecision.Removed; }
            if (authenticatedSender == 0 || authenticatedSender != entry.Owner ||
                e.Owner != entry.Owner || e.Epoch != entry.Epoch)
            { ownerMismatch++; return ItemDecision.OwnerMismatch; }
            if (entry.Contains(e.EventId)) { duplicate++; return ItemDecision.Duplicate; }
            if (e.EventId < entry.EventId)
            {
                // An id older than the retained window can no longer be
                // classified as a replay; refuse it instead of guessing.
                if (entry.FullWindow && e.EventId < entry.Oldest) { full++; return ItemDecision.Full; }
                stale++; return ItemDecision.Stale;
            }
            entry.Add(e.EventId);
            entry.LastSeen = now;
            accepted++;
            return ItemDecision.Accepted;
        }

        // Object id carried by a native message. StablePackets.Item covers every
        // state family; the two request families (21 ask item data, 46 ask item
        // pos) are a bare int at offset 1 and are not enumerated there.
        internal static uint NativeEntity(byte[] native)
        {
            if (native == null || native.Length < 5) return 0;
            switch (native[0])
            {
                case 21:
                case 46: return BitConverter.ToUInt32(native, 1);
                default: return StablePackets.Item(native);
            }
        }

        // Native message -> event. The kind, flags and entity come from the
        // validated message; owner/epoch belong to the caller (the owner that is
        // currently registered), and the payload is kept byte-for-byte.
        internal bool TryTranslate(byte[] native, ulong eventId, out ItemEvent e)
        {
            e = null;
            try
            {
                if (native == null || native.Length == 0 || native.Length > ItemEvent.MaxPayload) return false;
                if (eventId == 0 || !StablePackets.Valid(native)) return false;
                uint id = NativeEntity(native);
                if (id == 0) return false;
                ItemEventKind kind;
                switch (native[0])
                {
                    case 16: kind = ItemEventKind.PoseWorld; break;
                    case 17: kind = LocalPoseKind(native); break;
                    case 18: kind = ItemEventKind.PosePlayer; break;
                    case 19: kind = ItemEventKind.Velocity; break;
                    case 20: kind = ItemEventKind.CarVelocity; break;
                    case 23: kind = ItemEventKind.PartCondition; break;
                    case 24: kind = ItemEventKind.Plate; break;
                    case 25: kind = ItemEventKind.BusDoors; break;
                    case 26: kind = ItemEventKind.RndType; break;
                    case 27: kind = ItemEventKind.Food; break;
                    case 28: kind = ItemEventKind.Ammo; break;
                    case 29: kind = ItemEventKind.Push; break;
                    case 30: kind = ItemEventKind.Weapon; break;
                    case 31: kind = ItemEventKind.WeaponShot; break;
                    case 32: kind = ItemEventKind.Paint; break;
                    case 33: kind = ItemEventKind.Breakable; break;
                    case 36: kind = ItemEventKind.Remove; break;
                    case 37: kind = ItemEventKind.CarValues; break;
                    case 38: kind = ItemEventKind.CarHandbrake; break;
                    case 39: kind = ItemEventKind.CarPose; break;
                    case 40: kind = ItemEventKind.Usable; break;
                    case 41: kind = ItemEventKind.Fluid; break;
                    case 42: kind = BitConverter.ToInt32(native, 5) != 0 ? ItemEventKind.Attach : ItemEventKind.Detach; break;
                    case 43: kind = ItemEventKind.Ragdoll; break;
                    case 48: kind = ItemEventKind.Outfit; break;
                    case 21:
                    case 46: kind = ItemEventKind.RepairRequest; break;
                    default: return false;
                }
                if (kind == ItemEventKind.Unknown) return false;
                e = new ItemEvent { Entity = id, EventId = eventId, Kind = kind,
                    Flags = (byte)ItemEventPolicy.FlagsFor(kind), Payload = (byte[])native.Clone() };
                return true;
            }
            catch (Exception) { e = null; return false; }
        }

        // Native itemPosLocal: id u32@1, parent u32@5, physlock index i32@9,
        // local position @13, local rotation @25. index >= 0 is local storage;
        // index <= -100000 is an attached mount. The gap is refused, not guessed.
        static ItemEventKind LocalPoseKind(byte[] native)
        {
            int index = BitConverter.ToInt32(native, 9);
            if (index >= 0) return ItemEventKind.PoseLocalStorage;
            if (index <= -100000) return ItemEventKind.PoseLocalMount;
            return ItemEventKind.Unknown;
        }

        // Replay handle. The payload must still be a structurally valid native
        // message for the same entity; the caller feeds the result to the native
        // dispatcher. Returns a copy, so the envelope cannot be mutated through
        // the replay buffer.
        internal bool TryReplay(ItemEvent e, out byte[] native)
        {
            native = null;
            try
            {
                if (e == null || e.Entity == 0 || e.Payload == null ||
                    e.Payload.Length == 0 || e.Payload.Length > ItemEvent.MaxPayload) return false;
                if (!StablePackets.Valid(e.Payload)) return false;
                if ((ulong)NativeEntity(e.Payload) != e.Entity) return false;
                native = (byte[])e.Payload.Clone();
                return true;
            }
            catch (Exception) { native = null; return false; }
        }

        internal string Summary()
        {
            return "items{entities=" + entities.Count.ToString(CultureInfo.InvariantCulture) +
                " tombstones=" + tombstones.Count.ToString(CultureInfo.InvariantCulture) +
                " accepted=" + accepted.ToString(CultureInfo.InvariantCulture) +
                " duplicate=" + duplicate.ToString(CultureInfo.InvariantCulture) +
                " stale=" + stale.ToString(CultureInfo.InvariantCulture) +
                " ownerMismatch=" + ownerMismatch.ToString(CultureInfo.InvariantCulture) +
                " removed=" + removed.ToString(CultureInfo.InvariantCulture) +
                " full=" + full.ToString(CultureInfo.InvariantCulture) +
                " unknown=" + unknown.ToString(CultureInfo.InvariantCulture) +
                " invalid=" + invalid.ToString(CultureInfo.InvariantCulture) + "}";
        }

        // Invariants only: bounded stores, no live/tombstone overlap, every entry
        // has an owner and epoch, and no window holds more ids than allowed.
        internal bool Verify()
        {
            if (entities.Count > maxEntities || tombstones.Count > maxTombstones) return false;
            foreach (var pair in entities)
            {
                if (pair.Key == 0 || pair.Value.Owner == 0 || pair.Value.Epoch == 0) return false;
                if (pair.Value.Removed) return false;
                if (pair.Value.Count < 0 || pair.Value.Count > maxEventsPerEntity) return false;
                if (pair.Value.Count > 0 && pair.Value.EventId == 0) return false;
                if (tombstones.ContainsKey(pair.Key)) return false;
            }
            foreach (var pair in tombstones)
            {
                if (pair.Key == 0 || pair.Value == 0) return false;
                if (entities.ContainsKey(pair.Key)) return false;
            }
            return true;
        }
    }

    // Receive-side item authority gate (inventory §5). Conservative and bounded:
    // it refuses only a write that provably describes an item owned by a different
    // authenticated peer. Everything it cannot classify - malformed bytes, a loose
    // or unknown item, an unmodelled message type, a repair request - is allowed,
    // so the existing native validation and the native receiver decide exactly as
    // they do today. The car group (a car, its fitted parts and its stored cargo)
    // is resolved through the host's member lookup, so a part or a crate is written
    // by the peer that owns the car. O(1) per message: one dictionary lookup, no
    // allocation, no chain walk, no native call.
    internal static class ExperimentalItemGate
    {
        internal enum GateResult
        {
            Allow = 0,
            RejectUnknownOwner = 1,
            RejectSenderIdentity = 2,
            RejectOwnerMismatch = 3
        }

        // itemPosPlayer (18) = bool hasVel, u64 steamID, u32 id, Vector3 pos,
        // Vector3 rot [, Vector3 vel]; every other modelled type carries its item
        // id where StablePackets.Item reads it.
        internal static GateResult Check(ulong authenticatedSender, byte[] native, ItemMemberLookup lookup)
        {
            try
            {
                // Short or structurally invalid bytes are native validation's job.
                if (native == null || native.Length < 5) return GateResult.Allow;
                if (!StablePackets.Valid(native)) return GateResult.Allow;
                switch (native[0])
                {
                    case 18:
                    {
                        // The message names its own sender. A zero or a mismatched
                        // identity is forged or stale, never a legitimate write.
                        ulong steamId = BitConverter.ToUInt64(native, 2);
                        if (steamId == 0 || steamId != authenticatedSender) return GateResult.RejectSenderIdentity;
                        return Owned(BitConverter.ToUInt32(native, 10), authenticatedSender, lookup);
                    }
                    case 16: case 17: case 19: case 20: case 23: case 24: case 25:
                    case 26: case 27: case 28: case 29: case 30: case 31: case 32:
                    case 33: case 36: case 37: case 38: case 39: case 40: case 41:
                    case 42: case 43: case 48:
                        return Owned(StablePackets.Item(native), authenticatedSender, lookup);
                    // 21 ask item data / 46 ask item pos: any peer may ask the owner.
                    case 21:
                    case 46:
                        return GateResult.Allow;
                    default:
                        return GateResult.Allow;
                }
            }
            catch (Exception) { return GateResult.Allow; }
        }

        internal static bool Allow(ulong sender, byte[] native, ItemMemberLookup lookup)
        {
            return Check(sender, native, lookup) == GateResult.Allow;
        }

        // A zero or unknown entity is never ours to judge; only a known owner that
        // is not the authenticated sender is refused.
        static GateResult Owned(uint entity, ulong sender, ItemMemberLookup lookup)
        {
            if (entity == 0 || lookup == null) return GateResult.Allow;
            ulong owner;
            if (!lookup(entity, out owner) || owner == 0) return GateResult.Allow;
            return owner == sender ? GateResult.Allow : GateResult.RejectOwnerMismatch;
        }
    }
}
