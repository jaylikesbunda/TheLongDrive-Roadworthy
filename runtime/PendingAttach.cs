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
        public const int ByteCapacity = 2 * 1024 * 1024;

        public struct Held { public byte[] Bytes; public ulong Sender; }
        sealed class Entry { internal readonly List<Held> Messages = new List<Held>(); internal uint ID, Parent; internal double Expires, Created; internal LinkedListNode<Entry> Node; }
        readonly Dictionary<uint, Entry> pending = new Dictionary<uint, Entry>();
        readonly LinkedList<Entry> order = new LinkedList<Entry>();
        LinkedListNode<Entry> cursor;
        int total, bytes;

        public int Count { get { return total; } }
        public int Items { get { return pending.Count; } }
        public int Bytes { get { return bytes; } }
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
            // Check every insertion, including an item already in the table.
            if (total >= Capacity || bytes.Length > ByteCapacity - this.bytes) { Dropped++; return false; }
            Entry entry;
            if (!pending.TryGetValue(id, out entry))
            {
                entry = new Entry { ID = id, Created = now, Expires = now + ExpirySeconds };
                entry.Node = order.AddLast(entry);
                if (cursor == null) cursor = entry.Node;
                pending[id] = entry;
            }
            if (entry.Messages.Count >= PerItemCapacity) { Dropped++; return false; }
            entry.Messages.Add(new Held { Bytes = (byte[])bytes.Clone(), Sender = sender });
            // The latest attachment decides which parent the replay waits for.
            if (bytes.Length > 0 && bytes[0] == 42) entry.Parent = parent;
            total++; this.bytes += bytes.Length; HeldCount++;
            return true;
        }

        // Everything held for one item, in arrival order, removed from the queue.
        public List<Held> Take(uint id)
        {
            Entry entry;
            if (!pending.TryGetValue(id, out entry)) return null;
            Remove(entry); Replayed += entry.Messages.Count;
            return entry.Messages;
        }

        void Remove(Entry entry)
        {
            if (cursor == entry.Node) cursor = entry.Node.Next ?? order.First;
            order.Remove(entry.Node);
            if (order.Count == 0) cursor = null;
            pending.Remove(entry.ID); total -= entry.Messages.Count;
            foreach (var message in entry.Messages) bytes -= message.Bytes.Length;
        }

        // A confirmed native deletion cancels queued state, rather than allowing
        // it to be replayed after removal or requesting the object back.
        public void Remove(uint id)
        { Entry entry; if (pending.TryGetValue(id, out entry)) Remove(entry); }

        public double OldestAge(double now)
        { return order.First == null ? 0 : Math.Max(0, now - order.First.Value.Created); }

        // Production drain: bounded scans, bounded messages, and a caller-owned
        // time budget. One item remains FIFO even when spread across frames.
        // Cursor rotation gives missing items and different ready items a turn.
        public int Drain(Func<uint, bool> exists, double now, int maxItems, int maxMessages,
            Func<bool> withinBudget, Action<Held> replay, Action<uint> missing)
        {
            int sent = 0, visited = 0, initial = pending.Count;
            while (cursor != null && visited < initial && visited < maxItems && sent < maxMessages && withinBudget())
            {
                Entry entry = cursor.Value;
                cursor = cursor.Next ?? order.First;
                visited++;
                if (now >= entry.Expires)
                {
                    Expired += entry.Messages.Count;
                    if (missing != null) missing(entry.ID);
                    Remove(entry); continue;
                }
                if (!exists(entry.ID)) { if (missing != null) missing(entry.ID); continue; }
                if (entry.Parent != 0 && !exists(entry.Parent)) { if (missing != null) missing(entry.Parent); continue; }
                // Keep a busy item from consuming the whole per-frame allowance.
                int allowance = Math.Min(8, maxMessages - sent);
                while (entry.Messages.Count > 0 && allowance-- > 0 && withinBudget())
                {
                    Held message = entry.Messages[0]; entry.Messages.RemoveAt(0);
                    total--; bytes -= message.Bytes.Length; Replayed++; sent++;
                    replay(message);
                    // A callback may process a confirmed deletion of this item.
                    if (!pending.ContainsKey(entry.ID)) break;
                }
                if (entry.Messages.Count == 0 && pending.ContainsKey(entry.ID)) Remove(entry);
            }
            return sent;
        }

        // Held messages whose item (and parent) now exist, grouped per item in
        // arrival order. Expired items are discarded.
        public List<Held> TakeReady(Func<uint, bool> exists, double now)
        {
            List<Held> ready = null;
            // Compatibility helper for offline fixtures. Runtime uses Drain.
            foreach (uint id in new List<uint>(pending.Keys))
            {
                Entry entry = pending[id];
                if (now >= entry.Expires) { Expired += entry.Messages.Count; Remove(entry); continue; }
                if (!exists(id) || entry.Parent != 0 && !exists(entry.Parent)) continue;
                if (ready == null) ready = new List<Held>();
                ready.AddRange(Take(id));
            }
            return ready;
        }

        public void Clear() { pending.Clear(); order.Clear(); cursor = null; total = bytes = 0; HeldCount = Replayed = Expired = Dropped = 0; }
    }

    // Bounded, deduplicated recovery requests. Native askItemData responses carry
    // a complete SyncAll and already support reliable, targeted replies.
    public sealed class ItemRecoveryRequests
    {
        public const int Capacity = 4096, MaxAttempts = 6;
        sealed class Request { internal double Next, Until; internal int Attempts; internal bool Deleted, Refresh; }
        readonly Dictionary<uint, Request> requests = new Dictionary<uint, Request>();
        readonly Queue<uint> order = new Queue<uint>();
        public long Sent, Exhausted;
        public int Count { get { return requests.Count; } }

        public bool Deleted(uint id, double now)
        { Request r; return requests.TryGetValue(id, out r) && r.Deleted && now < r.Until; }
        public void Watch(uint id, double now)
        {
            if (id == 0 || requests.ContainsKey(id) || requests.Count >= Capacity) return;
            requests.Add(id, new Request { Next = now, Until = now + PendingItemQueue.ExpirySeconds });
            order.Enqueue(id);
        }
        // Overflow needs a fresh complete SyncAll even if creation subsequently
        // arrives. Wait for the old FIFO to drain before applying that refresh.
        public void Refresh(uint id, double now)
        {
            Watch(id, now); Request r;
            if (requests.TryGetValue(id, out r) && !r.Deleted) r.Refresh = true;
        }
        public void Remove(uint id, double now)
        {
            if (id == 0) return;
            Request r;
            if (!requests.TryGetValue(id, out r))
            { if (requests.Count >= Capacity) return; requests.Add(id, r = new Request()); order.Enqueue(id); }
            r.Deleted = true; r.Until = now + PendingItemQueue.ExpirySeconds;
        }
        public int Tick(Func<uint, bool> exists, double now, int maxRequests, Action<uint> send)
        { return Tick(exists, now, maxRequests, send, delegate(uint id) { return false; }); }
        public int Tick(Func<uint, bool> exists, double now, int maxRequests, Action<uint> send, Func<uint, bool> blocked)
        {
            int sent = 0, probes = Math.Min(128, order.Count);
            for (int i = 0; i < probes; i++)
            {
                uint id = order.Dequeue(); Request r = requests[id];
                bool present = exists(id);
                if (now >= r.Until || !r.Deleted && present && !r.Refresh) { requests.Remove(id); continue; }
                if (r.Deleted || now < r.Next || r.Attempts >= MaxAttempts || sent >= maxRequests || present && blocked(id)) { order.Enqueue(id); continue; }
                r.Attempts++; r.Next = now + Math.Min(16, 1 << (r.Attempts - 1));
                if (r.Attempts == MaxAttempts) Exhausted++;
                send(id); Sent++; sent++;
                if (present && r.Refresh) requests.Remove(id); else order.Enqueue(id);
            }
            return sent;
        }
        public void Clear() { requests.Clear(); order.Clear(); Sent = Exhausted = 0; }
    }

    public static class AttachmentReplayPolicy
    {
        // A slot is a unique mounting point. Already-attached is insufficient:
        // a different parent or slot must run through native reconciliation.
        // Collider/world anchors also need point/pose checks, so run native there.
        public static bool SameSlot(byte[] wanted, int attachType, int indexType,
            uint parent, int poiGen, double poiX, double poiY, double poiZ, int index)
        {
            if (wanted == null || wanted.Length < 49 || wanted[0] != 42 || indexType != 2 || BitConverter.ToInt32(wanted, 9) != 2 ||
                BitConverter.ToInt32(wanted, 5) != attachType || BitConverter.ToInt32(wanted, 45) != index) return false;
            if (attachType == 1) return BitConverter.ToUInt32(wanted, 13) == parent;
            return attachType == 2 && BitConverter.ToInt32(wanted, 17) == poiGen &&
                BitConverter.ToDouble(wanted, 21) == poiX && BitConverter.ToDouble(wanted, 29) == poiY && BitConverter.ToDouble(wanted, 37) == poiZ;
        }
    }
}
