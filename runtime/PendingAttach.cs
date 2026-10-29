using System;
using System.Collections.Generic;

namespace TldMp
{
    // Native per-item state messages that reached this machine before their item
    // (or, for attachments, the parent) existed. The stock receivers drop them,
    // and the sender then treats the state as delivered: a car's parts stayed
    // loose and unpainted, wheels were thrown out and snapped back. Messages are
    // held per item in arrival order and replayed, in order, once the item and
    // parent exist. If a newer message for that item arrives first, the held ones
    // are replayed before it so older state can never overwrite newer state.
    public sealed class PendingItemQueue
    {
        public const double ExpirySeconds = 60.0;
        public const int PerItemCapacity = 64;
        public const int Capacity = 4096;

        public struct Held { public byte[] Bytes; public ulong Sender; }
        sealed class Entry { internal readonly List<Held> Messages = new List<Held>(); internal uint Parent; internal double Expires; }
        readonly Dictionary<uint, Entry> pending = new Dictionary<uint, Entry>();
        readonly List<uint> scratch = new List<uint>();
        int total;

        public int Count { get { return total; } }
        public int Items { get { return pending.Count; } }
        public long HeldCount, Replayed, Expired, Dropped;

        // Per-item state types (msgType): partcondition 23, licenseplate 24, bus 25,
        // rndType 26, food 27, ammo 28, weapon 30, color 32, breakable 33, carValues 37,
        // carHandbrake 38, usable 40, fluid 41 (item form), attachable 42, outfit 48.
        // Poses, velocities, one-shot events, asks and removeItem are never held.
        public static bool Holdable(byte type)
        {
            switch (type)
            {
                case 23: case 24: case 25: case 26: case 27: case 28: case 30: case 32: case 33:
                case 37: case 38: case 40: case 41: case 42: case 48: return true;
                default: return false;
            }
        }

        // An attachment to an item (attachable, attachType 1) also needs its parent,
        // whose id is at byte 13.
        public static uint Parent(byte[] b)
        {
            if (b == null || b.Length < 17 || b[0] != 42 || BitConverter.ToInt32(b, 5) != 1) return 0;
            return BitConverter.ToUInt32(b, 13);
        }

        public bool Has(uint id) { return pending.ContainsKey(id); }

        public bool Hold(uint id, uint parent, ulong sender, byte[] bytes, double now)
        {
            if (id == 0 || bytes == null) return false;
            Entry entry;
            if (!pending.TryGetValue(id, out entry))
            {
                if (pending.Count >= Capacity || total >= Capacity) { Dropped++; return false; }
                entry = new Entry { Expires = now + ExpirySeconds };
                pending[id] = entry;
            }
            if (entry.Messages.Count >= PerItemCapacity) { Dropped++; return false; }
            entry.Messages.Add(new Held { Bytes = (byte[])bytes.Clone(), Sender = sender });
            // The latest attachment decides which parent the replay waits for.
            if (bytes.Length > 0 && bytes[0] == 42) entry.Parent = parent;
            total++; HeldCount++;
            return true;
        }

        // Everything held for one item, in arrival order, removed from the queue.
        public List<Held> Take(uint id)
        {
            Entry entry;
            if (!pending.TryGetValue(id, out entry)) return null;
            pending.Remove(id); total -= entry.Messages.Count; Replayed += entry.Messages.Count;
            return entry.Messages;
        }

        // Held messages whose item (and parent) now exist, grouped per item in
        // arrival order. Expired items are discarded.
        public List<Held> TakeReady(Func<uint, bool> exists, double now)
        {
            if (pending.Count == 0) return null;
            List<Held> ready = null;
            scratch.Clear();
            foreach (var pair in pending)
            {
                if (now >= pair.Value.Expires) { scratch.Add(pair.Key); Expired += pair.Value.Messages.Count; total -= pair.Value.Messages.Count; continue; }
                if (!exists(pair.Key) || (pair.Value.Parent != 0 && !exists(pair.Value.Parent))) continue;
                if (ready == null) ready = new List<Held>();
                ready.AddRange(pair.Value.Messages); scratch.Add(pair.Key);
                total -= pair.Value.Messages.Count; Replayed += pair.Value.Messages.Count;
            }
            for (int i = 0; i < scratch.Count; i++) pending.Remove(scratch[i]);
            return ready;
        }

        public void Clear() { pending.Clear(); total = 0; }
    }
}
