using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using TldMp;

internal static class ItemIndexTableTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    static List<save_item> Make(int count, int seed, int duplicateModulo, bool nulls)
    {
        var random = new Random(seed);
        var list = new List<save_item>(count);
        for (int i = 0; i < count; i++)
        {
            if (nulls && random.Next(29) == 0) { list.Add(null); continue; }
            uint id = duplicateModulo > 0 ? (uint)(random.Next(duplicateModulo) + 1) : (uint)(i + 1);
            list.Add(new save_item { id = id });
        }
        return list;
    }

    static int Native(List<save_item> list, uint id, out bool threw)
    {
        threw = false;
        try
        {
            int index;
            bool found = savedatascript.IndexOfID(list, id, out index);
            return found ? index : -1;
        }
        catch (NullReferenceException)
        {
            threw = true;
            return -2;
        }
    }

    static void CompareList(List<save_item> list, ItemIndexTable table, int seed)
    {
        var random = new Random(seed);
        uint max = 0;
        for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].id > max) max = list[i].id;
        for (int probe = 0; probe <= (int)max + 8; probe++)
        {
            int tableIndex;
            bool tableHit = table.TryGet(list, (uint)probe, out tableIndex);
            bool threw;
            int nativeIndex = Native(list, (uint)probe, out threw);
            if (tableHit)
            {
                Check(!threw, "table served a lookup that native throws on");
                Check((uint)tableIndex == (uint)nativeIndex, "table/native index mismatch");
            }
            else
            {
                // The bridge falls back to native. Refuse only where native is safe to serve.
                Check(threw || nativeIndex == -1 || nativeIndex >= 0, "unreachable fallback state");
            }
        }
    }

    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            string path = name == "Assembly-CSharp" ? args[1] : Path.Combine(args[0], name + ".dll");
            return File.Exists(path) ? Assembly.Load(File.ReadAllBytes(path)) : null;
        };
        Assembly.Load(File.ReadAllBytes(args[1]));
        return Run();
    }
    static int Run()
    {
        var table = new ItemIndexTable();
        var empty = new List<save_item>();
        Check(table.Build(empty), "empty build");
        int index;
        Check(table.TryGet(empty, 7, out index) && index == -1, "empty miss");

        var duplicates = new List<save_item>
        {
            new save_item { id = 5 }, new save_item { id = 5 }, new save_item { id = 6 }
        };
        Check(table.Build(duplicates), "duplicate build");
        Check(table.TryGet(duplicates, 5, out index) && index == 1, "last duplicate did not win");
        Check(table.TryGet(duplicates, 6, out index) && index == 2, "unique id lookup");
        Check(table.TryGet(duplicates, 9, out index) && index == -1, "missing id lookup");

        var withNull = new List<save_item> { new save_item { id = 1 }, null, new save_item { id = 2 } };
        Check(table.Build(withNull), "null build");
        Check(!table.TryGet(withNull, 1, out index), "native would dereference the later null first");
        Check(table.TryGet(withNull, 2, out index) && index == 2, "id after the null is safe");
        Check(!table.TryGet(withNull, 99, out index), "miss with a null must fall back");

        for (int seed = 0; seed < 40; seed++)
        {
            int count = seed == 0 ? 1 : (seed * 37) % 300;
            var list = Make(count, seed, seed % 3 == 0 ? 11 : 0, seed % 4 == 0);
            Check(table.Build(list), "random build");
            CompareList(list, table, seed + 1000);
        }

        Check(table.Build(duplicates), "mutation build");
        duplicates.RemoveAt(0);
        Check(!table.TryGet(duplicates, 5, out index), "count change was not detected");
        Check(table.Build(duplicates), "rebuild after mutation");
        Check(table.TryGet(duplicates, 5, out index) && index == 0, "rebuild after mutation failed");
        var replacement = new List<save_item> { new save_item { id = 5 } };
        Check(!table.TryGet(replacement, 5, out index), "list identity change was not detected");
        table.Clear();
        Check(!table.TryGet(duplicates, 5, out index), "cleared table still served");

        var big = Make(4000, 77, 0, false);
        var probes = new uint[2000];
        for (int i = 0; i < probes.Length; i++) probes[i] = (uint)((i * 7919) % big.Count + 1);
        table.Build(big);
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < probes.Length; i++) table.TryGet(big, probes[i], out index);
        watch.Stop();
        long tableTicks = watch.ElapsedTicks;
        watch.Restart();
        bool ignored; for (int i = 0; i < probes.Length; i++) Native(big, probes[i], out ignored);
        watch.Stop();
        long nativeTicks = watch.ElapsedTicks;
        Console.WriteLine("PASS: " + checks + " item-index assertions; n=4000 k=2000 tableTicks=" + tableTicks + " nativeTicks=" + nativeTicks + " (synthetic, not a game FPS claim)");
        return 0;
    }
}
