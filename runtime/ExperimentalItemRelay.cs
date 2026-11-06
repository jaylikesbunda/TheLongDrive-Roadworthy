using System;
using System.Collections.Generic;

namespace TldMp.Experimental
{
    // Host -> guest relay for item interaction/state messages (doors, parts,
    // car values, paint, damage, storage, attach/detach, usables, fluids, ...).
    // Motion poses stay on the native path. The host assigns a monotonic event id
    // per entity and the guest applies each event once, in order, with dependency
    // buffering from ItemEventStream. This is a host-originated path: guest
    // actions still travel native to the host, which is the arbiter.
    //
    // Payload: u32 entity | u64 eventId | u64 source | native item message.
    internal static class ExperimentalItemRelay
    {
        internal const int HeaderBytes = 4 + 8 + 8;
        internal const int MaxNativeBytes = 4096;
        internal const int MaxBytes = HeaderBytes + MaxNativeBytes;

        internal static bool IsRelayType(byte type)
        {
            switch (type)
            {
                case 23: case 24: case 25: case 26: case 27: case 28: case 29:
                case 30: case 31: case 32: case 33: case 36: case 37: case 38:
                case 40: case 41: case 42: case 43: case 48:
                    return true;
                default:
                    return false;
            }
        }

        internal static byte[] Encode(uint entity, ulong eventId, ulong source, byte[] native)
        {
            if (entity == 0 || eventId == 0 || source == 0 || native == null ||
                native.Length == 0 || native.Length > MaxNativeBytes) return null;
            var data = new byte[HeaderBytes + native.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(entity), 0, data, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(eventId), 0, data, 4, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(source), 0, data, 12, 8);
            Buffer.BlockCopy(native, 0, data, HeaderBytes, native.Length);
            return data;
        }

        internal static bool TryDecode(byte[] data, out uint entity, out ulong eventId, out ulong source, out byte[] native)
        {
            entity = 0; eventId = 0; source = 0; native = null;
            if (data == null || data.Length <= HeaderBytes || data.Length > MaxBytes) return false;
            entity = BitConverter.ToUInt32(data, 0);
            eventId = BitConverter.ToUInt64(data, 4);
            source = BitConverter.ToUInt64(data, 12);
            int count = data.Length - HeaderBytes;
            var payload = new byte[count];
            Buffer.BlockCopy(data, HeaderBytes, payload, 0, count);
            if (entity == 0 || eventId == 0 || source == 0 || !StablePackets.Valid(payload)) return false;
            native = payload;
            return true;
        }

        // Guest-side receiver. The host is trusted: every event is attributed to
        // the host owner, so ItemAuthority accepts it; event ids are monotonic per
        // entity and duplicates/stale events are consumed without applying.
        internal sealed class Receiver
        {
            readonly ulong host;
            readonly ItemAuthority authority;
            readonly ItemEventStream stream;
            internal Receiver(ulong host, int maxEntities, int maxTombstones, int maxEventsPerEntity)
            {
                if (host == 0) throw new ArgumentException("A relay receiver needs the host identity.");
                this.host = host;
                authority = new ItemAuthority(maxEntities, maxTombstones, maxEventsPerEntity);
                stream = new ItemEventStream(authority, 4096, 64, 2 * 1024 * 1024, 60);
            }

            internal bool Accept(byte[] data, double now, out byte[][] ready)
            {
                ready = null;
                uint entity; ulong eventId, source; byte[] native;
                if (!TryDecode(data, out entity, out eventId, out source, out native)) return false;
                ulong owner, epoch;
                if (!authority.TryOwner(entity, out owner, out epoch))
                {
                    if (!authority.Register(entity, host)) return false;
                    owner = host; epoch = 1;
                }
                ItemEvent e;
                if (!authority.TryTranslate(native, eventId, out e)) return false;
                e.Owner = owner;
                e.Epoch = epoch;
                ItemEvent[] events;
                StreamResult result = stream.Offer(e, host, now, out events);
                if (events == null || events.Length == 0)
                    return result == StreamResult.Applied || result == StreamResult.Buffered ||
                        result == StreamResult.Duplicate || result == StreamResult.Stale;
                var list = new List<byte[]>();
                for (int i = 0; i < events.Length; i++)
                {
                    byte[] payload;
                    if (authority.TryReplay(events[i], out payload)) list.Add(payload);
                }
                ready = list.Count == 0 ? null : list.ToArray();
                if (e.Kind == ItemEventKind.Remove)
                {
                    authority.Remove(entity, epoch);
                    stream.Remove(entity);
                }
                return true;
            }

            internal string Summary() { return "relay{" + stream.Summary() + "}"; }
            internal bool Verify() { return stream.Verify(); }
        }
    }
}
