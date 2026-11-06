using System;
using System.Collections.Generic;

namespace TldMp.Experimental
{
    // Experimental vehicle-motion authority. The host is the only arbiter: it
    // admits car poses from the peer with the highest native car role and relays
    // only that writer's motion. Guests apply grants for diagnostics but never
    // enforce them, so there is exactly one writer per car in a session.
    //
    // This is intentionally independent of the native relay: it does not change
    // world join, player replication or native item state. Main thread only.
    internal static class ExperimentalBackend
    {
        // Folded into the StableNode compatibility signature so a native and an
        // experimental peer reject each other at the existing Hello/Reject gate
        // before any world state is exchanged.
        internal const string SignatureTag = "|exp-mp-1";

        // Host fallback ownership (justHost) is not an active driver. Comparing
        // against a stale last-remote role would block the very first guest pose
        // and prevent the host from ever observing the guest's stronger role.
        internal static bool ProtectLocalCar(int localRole, bool localHasPriorityOverIncoming)
        {
            const int driving = 6; // tosaveitemscript.carWhatDoing.driving
            return localRole == driving || localHasPriorityOverIncoming;
        }

        // Until interactions have their own tested claim/handoff protocol, a
        // car-group owner cannot be used to reject another player's door or part
        // actions. Keep this gate explicitly opt-in for diagnostics.
        internal static bool UseItemGate(bool experimental, string setting)
        {
            return experimental && setting == "1";
        }

        internal sealed class Entry
        {
            internal ulong Owner;
            internal int Role;
            internal ulong Epoch;
            internal Entry(ulong owner, int role) { Owner = owner; Role = role; Epoch = 1; }
        }

        static readonly Dictionary<uint, Entry> cars = new Dictionary<uint, Entry>();
        static readonly List<uint> dirty = new List<uint>();
        internal static int Granted { get; private set; }
        internal static int Rejected { get; private set; }
        internal static int Applied { get; private set; }

        internal static void Reset()
        {
            cars.Clear();
            dirty.Clear();
            Granted = Rejected = Applied = 0;
        }

        internal static int Count { get { return cars.Count; } }

        internal static bool AllowPeer(ulong peer, uint carId, int role)
        {
            return Allow(peer, carId, role);
        }

        // Host-local car poses use the same rule: the highest role wins and a
        // lower-role local writer is suppressed rather than relayed.
        internal static bool AllowLocal(ulong self, uint carId, int role)
        {
            return Allow(self, carId, role);
        }

        static bool Allow(ulong owner, uint carId, int role)
        {
            if (owner == 0 || carId == 0 || role < 0) return false;
            Entry entry;
            if (!cars.TryGetValue(carId, out entry))
            {
                cars.Add(carId, new Entry(owner, role));
                MarkDirty(carId);
                Granted++;
                return true;
            }
            if (entry.Owner == owner)
            {
                if (role > entry.Role) entry.Role = role;
                return true;
            }
            if (role <= entry.Role || entry.Epoch == ulong.MaxValue)
            {
                Rejected++;
                return false;
            }
            entry.Owner = owner;
            entry.Role = role;
            entry.Epoch++;
            MarkDirty(carId);
            Granted++;
            return true;
        }

        static void MarkDirty(uint carId)
        {
            if (!dirty.Contains(carId)) dirty.Add(carId);
        }

        // Guest side. Older or inconsistent grants are ignored; the host epoch is
        // monotonic per car for the lifetime of the session.
        internal static void ApplyGrant(uint carId, ulong owner, ulong epoch)
        {
            if (carId == 0 || owner == 0 || epoch == 0) return;
            Entry entry;
            if (!cars.TryGetValue(carId, out entry))
            {
                cars.Add(carId, new Entry(owner, 0) { Epoch = epoch });
                Applied++;
                return;
            }
            if (epoch < entry.Epoch || (epoch == entry.Epoch && entry.Owner != owner)) return;
            entry.Owner = owner;
            entry.Epoch = epoch;
            Applied++;
        }

        internal static void ForgetPeer(ulong peer)
        {
            if (peer == 0 || cars.Count == 0) return;
            var removed = new List<uint>();
            foreach (var pair in cars) if (pair.Value.Owner == peer) removed.Add(pair.Key);
            for (int i = 0; i < removed.Count; i++)
            {
                cars.Remove(removed[i]);
                dirty.Remove(removed[i]);
            }
        }

        internal static bool OwnerOf(uint carId, out ulong owner, out ulong epoch)
        {
            Entry entry;
            if (cars.TryGetValue(carId, out entry)) { owner = entry.Owner; epoch = entry.Epoch; return true; }
            owner = epoch = 0; return false;
        }

        internal static bool TakeChanges(out uint[] ids, out ulong[] owners, out ulong[] epochs)
        {
            ids = null; owners = null; epochs = null;
            if (dirty.Count == 0) return false;
            return Take(dirty, out ids, out owners, out epochs);
        }

        internal static bool TakeAll(out uint[] ids, out ulong[] owners, out ulong[] epochs)
        {
            ids = null; owners = null; epochs = null;
            if (cars.Count == 0) return false;
            var all = new List<uint>(cars.Keys);
            return Take(all, out ids, out owners, out epochs);
        }

        static bool Take(List<uint> source, out uint[] ids, out ulong[] owners, out ulong[] epochs)
        {
            int count = source.Count;
            if (count == 0) { ids = null; owners = null; epochs = null; return false; }
            ids = new uint[count]; owners = new ulong[count]; epochs = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = source[i];
                Entry entry = cars[source[i]];
                owners[i] = entry.Owner;
                epochs[i] = entry.Epoch;
            }
            source.Clear();
            return true;
        }

        internal const int GrantBytes = 4 + 8 + 8;

        internal static byte[] EncodeGrant(uint carId, ulong owner, ulong epoch)
        {
            if (carId == 0 || owner == 0 || epoch == 0) throw new ArgumentException("Invalid authority grant.");
            var data = new byte[GrantBytes];
            Buffer.BlockCopy(BitConverter.GetBytes(carId), 0, data, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(owner), 0, data, 4, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(epoch), 0, data, 12, 8);
            return data;
        }

        internal static bool TryDecodeGrant(byte[] data, out uint carId, out ulong owner, out ulong epoch)
        {
            carId = 0; owner = 0; epoch = 0;
            if (data == null || data.Length != GrantBytes) return false;
            carId = BitConverter.ToUInt32(data, 0);
            owner = BitConverter.ToUInt64(data, 4);
            epoch = BitConverter.ToUInt64(data, 12);
            return carId != 0 && owner != 0 && epoch != 0;
        }

        internal static string Summary()
        {
            return "cars=" + cars.Count + " grants=" + Granted + " rejected=" + Rejected + " applied=" + Applied;
        }
    }

    // Host-side live vehicle authority. Every car is an entity in an
    // EntityCoordinator, so ownership is one-sided and every take-over carries a
    // new epoch. A car is also held by a lease: a claim from another peer is
    // accepted only while the lease is stale or the claimant's current native car
    // role is strictly higher, and the recorded role follows the current sample in
    // both directions. Nothing here touches the game, the transport or the thread
    // model: it is a plain main-thread bookkeeper.
    internal sealed class VehicleAuthority
    {
        // A group is the car's fitted parts and stored cargo. 256 is a hard bound
        // on the atomic transfer set; wider relationships are not modelled yet.
        internal const int MaxGroupMembers = 256;
        const int MaxWarnings = 8;

        sealed class Lease
        {
            internal ulong Owner;
            internal int Role;
            internal double LastSeen;
            internal ulong Epoch;
            internal readonly List<uint> Members = new List<uint>();
            internal Lease(ulong owner, int role) { Owner = owner; Role = role; Epoch = 1; }
        }

        readonly EntityCoordinator coordinator;
        readonly Dictionary<uint, Lease> cars = new Dictionary<uint, Lease>();
        // member -> car of the group it belongs to. A mirror of the lease member
        // lists, so the item gate can resolve a stored/mounted item to its car's
        // owner in one lookup. Bounded: never more than cars * MaxGroupMembers
        // entries, because every entry is written from a lease's member list.
        readonly Dictionary<uint, uint> memberToCar = new Dictionary<uint, uint>();
        readonly List<uint> dirty = new List<uint>();
        readonly List<uint> scratch = new List<uint>();
        readonly ulong self;
        readonly int maxEntities;
        readonly double leaseSeconds;
        int granted, rejected, refused, excluded, warnings;

        // Bounded warning sink. The caller decides where the text goes; the
        // authority never logs more than MaxWarnings lines per session.
        internal Action<string> Warning;

        internal VehicleAuthority(ulong self, int maxEntities, int maxTombstones, double leaseSeconds)
        {
            if (self == 0) throw new ArgumentException("A vehicle authority needs a local peer.");
            if (maxEntities <= 0 || maxTombstones <= 0) throw new ArgumentException("Invalid vehicle authority limits.");
            if (!(leaseSeconds > 0)) throw new ArgumentException("A vehicle lease must be positive.");
            this.self = self;
            this.maxEntities = maxEntities;
            this.leaseSeconds = leaseSeconds;
            coordinator = new EntityCoordinator(true, maxEntities, maxTombstones);
        }

        internal int Count { get { return cars.Count; } }
        internal int Granted { get { return granted; } }
        internal int Rejected { get { return rejected; } }
        internal int Refused { get { return refused; } }
        internal int Excluded { get { return excluded; } }

        // Host admission for a peer car sample. An unknown car is spawned for the
        // sender; the same owner keeps the car and adopts the current role; any
        // other owner must either claim a stale lease or beat the current role.
        internal bool AllowPeer(ulong peer, uint carId, int role, double now)
        {
            if (peer == 0 || carId == 0 || role < 0) { rejected++; return false; }
            Lease lease = Find(carId);
            if (lease == null)
            {
                ulong owner, epoch;
                if (coordinator.TryOwner(carId, out owner, out epoch)) Adopt(carId, owner, epoch, now);
                else
                {
                    // A refused spawn (caps, reserved id) is a refusal, never a grant.
                    if (!coordinator.Spawn(carId, peer)) { rejected++; return false; }
                    lease = new Lease(peer, role);
                    lease.LastSeen = now;
                    cars.Add(carId, lease);
                    MarkDirty(carId);
                    granted++;
                    return true;
                }
                lease = Find(carId);
            }
            if (lease.Owner == peer)
            {
                // The current role, not a high-water mark: a driver who stops
                // driving must not keep a car it no longer simulates.
                lease.Role = role;
                lease.LastSeen = now;
                return true;
            }
            bool stale = now - lease.LastSeen > leaseSeconds;
            if (!stale && role <= lease.Role) { rejected++; return false; }
            if (!Move(carId, lease, peer, role, now)) { rejected++; return false; }
            return true;
        }

        // The host's own car. The native controller is authoritative: a guest-held
        // car is taken back rather than waiting for a lease to expire. Called from
        // the publish path, which must never be suppressed by its result.
        internal void TouchLocal(uint carId, int role, double now)
        {
            if (carId == 0 || role < 0) return;
            Lease lease = Find(carId);
            if (lease == null)
            {
                ulong owner, epoch;
                if (coordinator.TryOwner(carId, out owner, out epoch)) Adopt(carId, owner, epoch, now);
                else if (!coordinator.Spawn(carId, self)) { rejected++; return; }
                else
                {
                    lease = new Lease(self, role);
                    lease.LastSeen = now;
                    cars.Add(carId, lease);
                    MarkDirty(carId);
                    granted++;
                    return;
                }
                lease = Find(carId);
            }
            if (lease.Owner != self)
            {
                if (!Move(carId, lease, self, role, now)) return;
                return;
            }
            lease.Role = role;
            lease.LastSeen = now;
        }

        // A departed peer keeps nothing. Every car leased to it moves back to the
        // host with a new epoch and stays live; the graph is only asked to drop an
        // entity when the entity is provably a leaf and the tombstone cap allows it.
        internal void ForgetPeer(ulong peer, double now)
        {
            if (peer == 0 || cars.Count == 0) return;
            scratch.Clear();
            foreach (var pair in cars) if (pair.Value.Owner == peer) scratch.Add(pair.Key);
            for (int i = 0; i < scratch.Count; i++)
            {
                uint carId = scratch[i];
                Lease lease;
                if (!cars.TryGetValue(carId, out lease) || lease.Owner != peer) continue;
                // The group as it was before any fallback below, so the member map
                // can be resynchronised with whatever the lease ends up holding.
                var members = new List<uint>(lease.Members);
                if (Move(carId, lease, self, lease.Role, now)) { SyncMembers(carId, lease, members); continue; }
                // The group could not move atomically. Fall back to the car alone
                // and then to its contents, so neither stays with the peer.
                lease.Members.Clear();
                if (Move(carId, lease, self, lease.Role, now)) { ReclaimMembers(members, peer, carId); SyncMembers(carId, lease, members); continue; }
                if (coordinator.Remove(carId, lease.Epoch)) { cars.Remove(carId); dirty.Remove(carId); }
                else
                {
                    // Nothing could be retracted: expire the lease immediately so
                    // the next claim wins instead of inheriting the departure.
                    lease.LastSeen = now - leaseSeconds - 1;
                    WarnOnce("[TLD MP experimental] departing peer kept car=" + carId + "; lease expired");
                }
                ReclaimMembers(members, peer, carId);
                SyncMembers(carId, lease, members);
            }
        }

        // memberToCar must describe the lease's member list and nothing else. A car
        // that is no longer leased owns no members: their entries are dropped so a
        // later gate lookup resolves them to nobody instead of a departed car.
        void SyncMembers(uint carId, Lease lease, List<uint> previous)
        {
            bool live = cars.TryGetValue(carId, out lease);
            for (int i = 0; i < previous.Count; i++)
            {
                uint member = previous[i];
                uint mapped;
                if (!memberToCar.TryGetValue(member, out mapped) || mapped != carId) continue;
                if (!live || !lease.Members.Contains(member)) memberToCar.Remove(member);
            }
            if (!live) return;
            for (int i = 0; i < lease.Members.Count; i++) memberToCar[lease.Members[i]] = carId;
        }

        void ReclaimMembers(List<uint> members, ulong peer, uint carId)
        {
            for (int m = 0; m < members.Count; m++)
            {
                ulong owner, epoch, moved;
                if (!coordinator.TryOwner(members[m], out owner, out epoch) || owner != peer) continue;
                if (coordinator.Transfer(members[m], epoch, self, out moved)) granted++;
                else { refused++; WarnOnce("[TLD MP experimental] departing peer kept member=" + members[m] + " car=" + carId); }
            }
        }

        internal bool TryOwner(uint carId, out ulong owner, out ulong epoch)
        {
            if (carId == 0) { owner = 0; epoch = 0; return false; }
            return coordinator.TryOwner(carId, out owner, out epoch);
        }

        // Host only. Registers the car's verified contents as one graph group so a
        // take-over moves the car and its parts/cargo in a single coordinator
        // transfer. Members that are already owned by another peer, or that fail
        // to spawn within the caps, are dropped and counted as excluded: a member
        // is never moved on a guess.
        internal bool RebuildGroup(uint carId, uint[] members)
        {
            if (carId == 0) return false;
            Lease lease = Find(carId);
            if (lease == null) return false;
            var next = new List<uint>();
            if (members != null)
            {
                int limit = members.Length > MaxGroupMembers ? MaxGroupMembers : members.Length;
                for (int i = 0; i < limit; i++)
                {
                    uint member = members[i];
                    if (member == 0 || member == carId) { excluded++; continue; }
                    if (next.Contains(member)) { excluded++; continue; }
                    ulong owner, epoch;
                    if (coordinator.TryOwner(member, out owner, out epoch))
                    {
                        // Ownership must match the car, otherwise the member would
                        // be pulled out of another peer's authority.
                        if (owner != lease.Owner) { excluded++; continue; }
                        // Re-parents an intermediate node onto the car root; the
                        // graph keeps the member live either way.
                        if (!coordinator.Attach(member, carId)) { excluded++; continue; }
                    }
                    else if (!coordinator.Spawn(member, carId, lease.Owner)) { excluded++; continue; }
                    next.Add(member);
                }
                if (members.Length > limit) excluded += members.Length - limit;
            }
            for (int i = 0; i < lease.Members.Count; i++)
            {
                uint old = lease.Members[i];
                if (next.Contains(old)) continue;
                coordinator.Detach(old);
                // A member that left the group is no longer written through this
                // car, so its lookup entry goes with it.
                uint mapped;
                if (memberToCar.TryGetValue(old, out mapped) && mapped == carId) memberToCar.Remove(old);
            }
            lease.Members.Clear();
            for (int i = 0; i < next.Count; i++)
            {
                lease.Members.Add(next[i]);
                memberToCar[next[i]] = carId;
            }
            return true;
        }

        // The one lookup the item gate performs. A car id answers with its lease
        // owner; a group member answers with the owner of the car it belongs to;
        // anything else is unknown and the caller leaves it to native handling.
        internal bool TryMemberOwner(uint member, out ulong owner)
        {
            owner = 0;
            if (member == 0) return false;
            Lease lease = Find(member);
            if (lease != null) { owner = lease.Owner; return true; }
            uint carId;
            if (!memberToCar.TryGetValue(member, out carId)) return false;
            lease = Find(carId);
            if (lease == null) return false;
            owner = lease.Owner;
            return true;
        }

        internal int GroupSize(uint carId)
        {
            Lease lease = Find(carId);
            return lease == null ? 0 : lease.Members.Count;
        }

        internal bool TakeChanges(out uint[] ids, out ulong[] owners, out ulong[] epochs)
        {
            return Take(dirty, true, out ids, out owners, out epochs);
        }

        internal bool TakeAll(out uint[] ids, out ulong[] owners, out ulong[] epochs)
        {
            scratch.Clear();
            foreach (var pair in cars) scratch.Add(pair.Key);
            return Take(scratch, false, out ids, out owners, out epochs);
        }

        bool Take(List<uint> source, bool clear, out uint[] ids, out ulong[] owners, out ulong[] epochs)
        {
            ids = null; owners = null; epochs = null;
            if (source.Count == 0) return false;
            var keep = new uint[source.Count];
            var keepOwners = new ulong[source.Count];
            var keepEpochs = new ulong[source.Count];
            int count = 0;
            for (int i = 0; i < source.Count; i++)
            {
                Lease lease;
                if (!cars.TryGetValue(source[i], out lease)) continue;
                keep[count] = source[i];
                keepOwners[count] = lease.Owner;
                keepEpochs[count] = lease.Epoch;
                count++;
            }
            if (clear) source.Clear();
            if (count == 0) return false;
            if (count == keep.Length) { ids = keep; owners = keepOwners; epochs = keepEpochs; return true; }
            ids = new uint[count]; owners = new ulong[count]; epochs = new ulong[count];
            for (int i = 0; i < count; i++) { ids[i] = keep[i]; owners[i] = keepOwners[i]; epochs[i] = keepEpochs[i]; }
            return true;
        }

        // Invariants only. The member map is a mirror of the lease member lists, so
        // it must stay inside the group bound and never name a car that is not
        // leased or a member that lease no longer holds.
        internal bool Verify()
        {
            if (!coordinator.Verify()) return false;
            if (memberToCar.Count > cars.Count * MaxGroupMembers) return false;
            foreach (var pair in memberToCar)
            {
                if (pair.Key == 0 || pair.Value == 0 || pair.Key == pair.Value) return false;
                Lease lease;
                if (!cars.TryGetValue(pair.Value, out lease)) return false;
                if (!lease.Members.Contains(pair.Key)) return false;
            }
            return true;
        }

        internal string Describe(uint carId)
        {
            Lease lease = Find(carId);
            if (lease == null) return "car=" + carId + " owner=0 epoch=0 role=0 members=0 lastSeen=0";
            return "car=" + carId + " owner=" + lease.Owner + " epoch=" + lease.Epoch + " role=" + lease.Role +
                " members=" + lease.Members.Count +
                " lastSeen=" + lease.LastSeen.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal string Summary()
        {
            int groups = 0;
            foreach (var pair in cars) if (pair.Value.Members.Count > 0) groups++;
            return "cars=" + cars.Count + " grants=" + granted + " rejected=" + rejected +
                " applied=0 suppressed=0 groups=" + groups + " members=" + memberToCar.Count +
                " refused=" + refused + " excluded=" + excluded;
        }

        // One atomic move that also carries the group when one has been built.
        // Any refusal leaves the lease untouched so the caller fails closed.
        bool Move(uint carId, Lease lease, ulong newOwner, int role, double now)
        {
            ulong epoch;
            if (lease.Members.Count > 0)
            {
                if (!coordinator.TransferGroup(carId, coordinator.GraphRevision, lease.Owner, newOwner, maxEntities))
                {
                    refused++;
                    WarnOnce("[TLD MP experimental] group transfer refused car=" + carId + " from=" + lease.Owner +
                        " to=" + newOwner + " members=" + lease.Members.Count);
                    return false;
                }
                ulong owner;
                if (!coordinator.TryOwner(carId, out owner, out epoch) || owner != newOwner)
                {
                    // The authority store and the lease disagree; adopt the store so
                    // the two never describe different owners.
                    refused++;
                    lease.Owner = newOwner;
                    lease.Role = role;
                    lease.LastSeen = now;
                    WarnOnce("[TLD MP experimental] group transfer readback lost car=" + carId);
                    return false;
                }
            }
            else if (!coordinator.Transfer(carId, lease.Epoch, newOwner, out epoch))
            {
                refused++;
                return false;
            }
            lease.Owner = newOwner;
            lease.Role = role;
            lease.LastSeen = now;
            lease.Epoch = epoch;
            MarkDirty(carId);
            granted++;
            return true;
        }

        Lease Find(uint carId)
        {
            Lease lease;
            return cars.TryGetValue(carId, out lease) ? lease : null;
        }

        // An entity that already exists in the coordinator (a group member that is
        // now claiming its own motion) is adopted rather than spawned twice.
        void Adopt(uint carId, ulong owner, ulong epoch, double now)
        {
            var lease = new Lease(owner, 0);
            lease.Epoch = epoch;
            lease.LastSeen = now;
            cars.Add(carId, lease);
        }

        void MarkDirty(uint carId)
        {
            if (!dirty.Contains(carId)) dirty.Add(carId);
        }

        void WarnOnce(string text)
        {
            if (warnings >= MaxWarnings || Warning == null) return;
            warnings++;
            try { Warning(text); }
            catch (Exception) { }
        }
    }
}
