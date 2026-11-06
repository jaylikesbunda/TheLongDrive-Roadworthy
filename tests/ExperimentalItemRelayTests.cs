using System;
using TldMp.Experimental;

internal static class ExperimentalItemRelayTests
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Item relay invariant failed at check " + checks); }

    static byte[] Msg(byte type, int length) { var b = new byte[length]; b[0] = type; return b; }
    static byte[] Put(byte[] b, int at, int value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 4); return b; }
    static byte[] Put64(byte[] b, int at, ulong value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 8); return b; }

    static byte[] Damage(uint id) { return Put(Msg(33, 17), 1, (int)id); }
    static byte[] Remove(uint id) { return Put(Msg(36, 5), 1, (int)id); }
    static byte[] Attach(uint id, uint parent)
    {
        var b = Put(Msg(42, 121), 1, (int)id);
        Put(b, 5, 1);
        Put(b, 9, 0);
        Put(b, 13, (int)parent);
        return b;
    }

    public static int Main()
    {
        const ulong host = 10;
        var damage = Damage(7);
        var data = ExperimentalItemRelay.Encode(7, 1, host, damage);
        Check(data != null && data.Length == ExperimentalItemRelay.HeaderBytes + damage.Length);
        uint entity; ulong eventId, source; byte[] native;
        Check(ExperimentalItemRelay.TryDecode(data, out entity, out eventId, out source, out native));
        Check(entity == 7 && eventId == 1 && source == host && native.Length == damage.Length && native[0] == 33);
        Check(ExperimentalItemRelay.Encode(0, 1, host, damage) == null);
        Check(ExperimentalItemRelay.Encode(7, 0, host, damage) == null);
        Check(ExperimentalItemRelay.Encode(7, 1, 0, damage) == null);
        Check(ExperimentalItemRelay.Encode(7, 1, host, null) == null);
        Check(ExperimentalItemRelay.Encode(7, 1, host, new byte[ExperimentalItemRelay.MaxNativeBytes + 1]) == null);
        Check(!ExperimentalItemRelay.TryDecode(null, out entity, out eventId, out source, out native));
        Check(!ExperimentalItemRelay.TryDecode(new byte[ExperimentalItemRelay.HeaderBytes], out entity, out eventId, out source, out native));
        var bad = (byte[])data.Clone(); bad[0] = 0; bad[1] = 0; bad[2] = 0; bad[3] = 0;
        Check(!ExperimentalItemRelay.TryDecode(bad, out entity, out eventId, out source, out native));

        Check(ExperimentalItemRelay.IsRelayType(37) && ExperimentalItemRelay.IsRelayType(40) && ExperimentalItemRelay.IsRelayType(42));
        Check(!ExperimentalItemRelay.IsRelayType(16) && !ExperimentalItemRelay.IsRelayType(39) && !ExperimentalItemRelay.IsRelayType(253));

        var receiver = new ExperimentalItemRelay.Receiver(host, 64, 64, 32);
        byte[][] ready;
        Check(receiver.Accept(ExperimentalItemRelay.Encode(7, 1, host, damage), 1, out ready));
        Check(ready != null && ready.Length == 1 && ready[0][0] == 33);
        Check(receiver.Accept(ExperimentalItemRelay.Encode(7, 1, host, damage), 2, out ready) && ready == null);
        Check(receiver.Accept(ExperimentalItemRelay.Encode(7, 3, host, damage), 3, out ready) && ready != null && ready.Length == 1);
        Check(receiver.Accept(ExperimentalItemRelay.Encode(7, 2, host, damage), 4, out ready) && ready == null);

        // Attach before its parent is known is buffered; the parent event drains it.
        Check(receiver.Accept(ExperimentalItemRelay.Encode(9, 1, host, Attach(9, 8)), 5, out ready) && ready == null);
        Check(receiver.Accept(ExperimentalItemRelay.Encode(8, 1, host, Damage(8)), 6, out ready));
        Check(ready != null && ready.Length == 2 && ready[0][0] == 33 && ready[1][0] == 42 && BitConverter.ToUInt32(ready[1], 1) == 9);

        // Removal tombstones the entity; a later event is refused.
        Check(receiver.Accept(ExperimentalItemRelay.Encode(7, 4, host, Remove(7)), 7, out ready) && ready != null && ready.Length == 1);
        Check(!receiver.Accept(ExperimentalItemRelay.Encode(7, 5, host, Damage(7)), 8, out ready));

        Check(!receiver.Accept(new byte[4], 9, out ready));
        Check(receiver.Verify());

        Console.WriteLine("PASS: item relay framing, host attribution, ordering, dependencies and removal (" + checks + " checks)");
        return 0;
    }
}
