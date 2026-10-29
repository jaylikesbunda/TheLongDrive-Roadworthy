using System;
using System.Globalization;
using System.Text;

namespace TldMp
{
    // Diagnostic observations only. Timings for nested labels are inclusive;
    // they must not be summed. A missing exit is discarded at the next frame
    // boundary and can never poison a later window. Aggregate slots report
    // total/calls only (no percentile ring) for very hot, tiny operations.
    public sealed class NativeScriptTimings
    {
        const int Slots = 54;
        const int AggregateSlots = 2;
        const int Capacity = 512;
        const int MaxDepth = 8;

        internal static readonly string[] Names = new string[]
        {
            "mainscript.Update",
            "mapSettingScript.Update",
            "objGenScript.Upd",
            "itemPlaceRemoveScript.Upd",
            "objGenScript.UpdGen",
            "objGenScript.UpdPlaceRemove",
            "categoryClass.UpdPlaceRemove",
            "poiGenScript.Upd",
            "roadGenScript.UpdChunksNeeded",
            "roadGenScript.UpdPlaceRemove",
            "terrainGenScript.Upd[0]",
            "terrainGenScript.Upd[1]",
            "terrainGenScript.Upd[2]",
            "terrainGenScript.DoPlace",
            "terrainGenScript.CheckPlace",
            "thumbleweedMain.Upd",
            "StableMap.CategoryChunks",
            "StableMap.RestorePlacement",
            "StableMap.RoadUpdate.native",
            "StableMap.PoiAdd",
            "poiGenScript.AddPois",
            "poiGenScript.Spawn.Instantiate",
            "terrainGenScript.GenerateTileMesh(main thread)",
            "terrainGenScript.PlaceMesh",
            "newTerrainTileScript.FStart",
            "newTerrainTileScript.UpdMesh",
            "roadGenScript.Instantiate",
            "roadscript.Init",
            "roadscript.Place",
            "roadscript.PowerPole",
            "roadGenScript.Destroy",
            "poiGenScript.Spawn",
            "poiScript.SpawnStuff",
            "poiScript.ItemSpawn",
            "poiScript.FixedSpawn",
            "poiGenScript.RemovePoi",
            "behaviour.mirrorsUpdate",
            "behaviour.reflectionMove",
            "behaviour.multiUpd",
            "behaviour.carUpdate",
            "behaviour.audioInterior",
            "behaviour.sensorUpdate",
            "behaviour.moveCreature",
            "behaviour.aiUpdate",
            "behaviour.vultureUpdate",
            "behaviour.flyerUpdate",
            "behaviour.attachables",
            "behaviour.partConditions",
            "behaviour.usables",
            "behaviour.wheelGraphics",
            "behaviour.syncScript",
            "mainscript.VisszaRakas",
            "wheelgraphicsscript.Graphics",
            "wheelgraphicsscript.WheelSize"
        };

        static readonly string[] AggregateNames = new string[]
        {
            "StableMap.RoadDependencyStamp(total)",
            "StableMap.PoiDependencyStamp(total)"
        };

        readonly long[,] samples = new long[Slots, Capacity];
        // Per-slot bounded start stack. Index 0 is the outer span used for the
        // recorded sample; deeper entries only balance nested/recursive calls.
        readonly long[,] starts = new long[Slots, MaxDepth];
        readonly long[] total = new long[Slots];
        readonly int[] frame = new int[Slots];
        readonly int[] depth = new int[Slots];
        readonly int[] at = new int[Slots];
        readonly int[] count = new int[Slots];
        readonly int[] calls = new int[Slots];
        readonly int[] discarded = new int[Slots];
        readonly bool[] overflow = new bool[Slots];
        readonly long[] scratch = new long[Capacity];
        readonly long[] aggregateTotal = new long[AggregateSlots];
        readonly int[] aggregateCalls = new int[AggregateSlots];
        int windowFrames = 1;

        public void SetWindowFrames(int frames)
        {
            windowFrames = frames > 0 ? frames : 1;
        }

        public void Begin(int slot, int currentFrame, long timestamp)
        {
            if (slot < 0 || slot >= Slots) return;
            if (depth[slot] != 0 && frame[slot] != currentFrame)
            {
                discarded[slot]++; depth[slot] = 0; overflow[slot] = false;
            }
            if (overflow[slot])
            {
                // A previously overflowed Begin has not been paired yet.
                discarded[slot]++;
                return;
            }
            if (depth[slot] >= MaxDepth)
            {
                overflow[slot] = true;
                discarded[slot]++;
                return;
            }
            if (depth[slot] == 0) { frame[slot] = currentFrame; starts[slot, 0] = timestamp; }
            else starts[slot, depth[slot]] = timestamp;
            depth[slot]++;
        }

        public void End(int slot, int currentFrame, long timestamp)
        {
            if (slot < 0 || slot >= Slots || depth[slot] == 0) return;
            if (overflow[slot])
            {
                // Consume the unmatched overflow marker without changing depth.
                overflow[slot] = false;
                return;
            }
            if (frame[slot] != currentFrame)
            {
                discarded[slot]++; depth[slot] = 0; return;
            }
            depth[slot]--;
            if (depth[slot] != 0) return;
            long elapsed = timestamp - starts[slot, 0];
            if (elapsed < 0) { discarded[slot]++; return; }
            samples[slot, at[slot]] = elapsed;
            at[slot] = (at[slot] + 1) % Capacity;
            if (count[slot] < Capacity) count[slot]++;
            calls[slot]++;
            total[slot] += elapsed;
        }

        public void AddAggregate(int slot, long ticks)
        {
            if (slot < 0 || slot >= AggregateSlots || ticks < 0) return;
            aggregateTotal[slot] += ticks;
            aggregateCalls[slot]++;
        }

        public string ReportAndReset(double millisecondsPerTick)
        {
            var text = new StringBuilder("nativeScriptTimings{inclusive, nested labels overlap; frames=");
            text.Append(windowFrames).Append("; ");
            for (int slot = 0; slot < Slots; slot++)
            {
                if (slot != 0) text.Append("; ");
                text.Append(Names[slot]).Append("=");
                int n = count[slot];
                if (n == 0) text.Append("unavailable");
                else
                {
                    for (int i = 0; i < n; i++) scratch[i] = samples[slot, i];
                    Array.Sort(scratch, 0, n);
                    text.Append("p50/p95=")
                        .Append((scratch[(n - 1) / 2] * millisecondsPerTick).ToString("F3", CultureInfo.InvariantCulture))
                        .Append('/')
                        .Append((scratch[Math.Max(0, (int)Math.Ceiling(n * .95) - 1)] * millisecondsPerTick).ToString("F3", CultureInfo.InvariantCulture))
                        .Append("ms total=").Append((total[slot] * millisecondsPerTick).ToString("F2", CultureInfo.InvariantCulture))
                        .Append("ms calls=").Append(calls[slot])
                        .Append(" samples=").Append(n)
                        .Append(" perFrame=").Append((total[slot] * millisecondsPerTick / windowFrames).ToString("F3", CultureInfo.InvariantCulture))
                        .Append("ms");
                }
                text.Append(" incomplete=").Append(discarded[slot]);
                count[slot] = at[slot] = calls[slot] = discarded[slot] = 0;
                total[slot] = 0;
                depth[slot] = 0;
                overflow[slot] = false;
            }
            for (int slot = 0; slot < AggregateSlots; slot++)
            {
                text.Append("; ").Append(AggregateNames[slot]).Append("=");
                int n = aggregateCalls[slot];
                if (n == 0) text.Append("unavailable");
                else
                {
                    double ms = aggregateTotal[slot] * millisecondsPerTick;
                    text.Append("total=").Append(ms.ToString("F2", CultureInfo.InvariantCulture))
                        .Append("ms calls=").Append(n)
                        .Append(" avg=").Append((ms / n).ToString("F4", CultureInfo.InvariantCulture))
                        .Append("ms");
                }
                aggregateTotal[slot] = 0; aggregateCalls[slot] = 0;
            }
            return text.Append('}').ToString();
        }
    }
}
