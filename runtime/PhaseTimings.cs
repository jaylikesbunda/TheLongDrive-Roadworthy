using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace TldMp
{
    // Diagnostic sub-phase timers and counters inside the mod's own world
    // streaming, manifest and snapshot paths. Inclusive, main thread only;
    // nested phases overlap and must not be summed. Nothing is recorded until
    // FrameDiagnostics registers the main thread (diagnostics enabled).
    public sealed class PhaseTimingTable
    {
        internal static readonly string[] Names = new string[]
        {
            "client.CategoryGen",
            "client.CategoryChunks",
            "client.RequestCategoryChunks",
            "client.RestorePlacement",
            "client.EvictCategory",
            "client.TerrainNeed",
            "host.CategoryGen.native",
            "host.RestorePlacement",
            "serve.road.generate",
            "serve.road.copy",
            "serve.road.encode",
            "serve.poi.roadDeps",
            "serve.poi.generate",
            "serve.poi.collect",
            "serve.poi.encode",
            "serve.obj.generate",
            "serve.obj.collect",
            "serve.obj.encode",
            "apply.decode",
            "apply.road",
            "apply.poi",
            "apply.obj",
            "snapshot.send",
            "snapshot.apply",
            "fingerprint.wait"
        };
        internal static readonly string[] CounterNames = new string[]
        {
            "need.keysFormatted",
            "need.available",
            "need.requested",
            "need.passesSkipped",
            "evict.cellsWalked",
            "evict.objectsWalked",
            "evict.removed",
            "serve.road.bytes",
            "serve.poi.bytes",
            "serve.obj.bytes",
            "apply.bytes",
            "serve.obj.reused",
            "serve.obj.generated",
            "terrain.needKeys",
            "terrain.cacheHits"
        };
        public const double SpikeMilliseconds = 8.0;
        public const int MaxSpikesPerWindow = 32;

        readonly long[] total = new long[Names.Length];
        readonly long[] max = new long[Names.Length];
        readonly int[] calls = new int[Names.Length];
        readonly long[] counters = new long[CounterNames.Length];
        int spikes, spikesDropped;

        // Returns a spike description when the call crosses the spike threshold
        // and the window still has room for one, otherwise null.
        public string Record(int slot, long elapsedTicks, double millisecondsPerTick, string detail)
        {
            if (slot < 0 || slot >= Names.Length || elapsedTicks < 0) return null;
            total[slot] += elapsedTicks; calls[slot]++;
            if (elapsedTicks > max[slot]) max[slot] = elapsedTicks;
            double ms = elapsedTicks * millisecondsPerTick;
            if (ms < SpikeMilliseconds) return null;
            if (spikes >= MaxSpikesPerWindow) { spikesDropped++; return null; }
            spikes++;
            return Names[slot] + " ms=" + ms.ToString("F2", CultureInfo.InvariantCulture) + (String.IsNullOrEmpty(detail) ? "" : " " + detail);
        }

        public void Count(int counter, long amount)
        {
            if (counter < 0 || counter >= CounterNames.Length || amount <= 0) return;
            counters[counter] += amount;
        }

        public string ReportAndReset(int frames, double millisecondsPerTick)
        {
            if (frames <= 0) frames = 1;
            var text = new StringBuilder("phaseTimings{inclusive, nested phases overlap; frames=");
            text.Append(frames);
            for (int slot = 0; slot < Names.Length; slot++)
            {
                text.Append("; ").Append(Names[slot]).Append('=');
                if (calls[slot] == 0) text.Append("none");
                else
                {
                    double sum = total[slot] * millisecondsPerTick;
                    text.Append("total=").Append(sum.ToString("F2", CultureInfo.InvariantCulture))
                        .Append("ms calls=").Append(calls[slot])
                        .Append(" avg=").Append((sum / calls[slot]).ToString("F3", CultureInfo.InvariantCulture))
                        .Append("ms max=").Append((max[slot] * millisecondsPerTick).ToString("F3", CultureInfo.InvariantCulture))
                        .Append("ms perFrame=").Append((sum / frames).ToString("F3", CultureInfo.InvariantCulture)).Append("ms");
                }
                total[slot] = max[slot] = 0; calls[slot] = 0;
            }
            text.Append("} phaseCounters{");
            for (int i = 0; i < CounterNames.Length; i++)
            {
                if (i != 0) text.Append(' ');
                text.Append(CounterNames[i]).Append('=').Append(counters[i].ToString(CultureInfo.InvariantCulture));
                counters[i] = 0;
            }
            text.Append(" spikesLogged=").Append(spikes).Append(" spikesDropped=").Append(spikesDropped).Append('}');
            spikes = spikesDropped = 0;
            return text.ToString();
        }
    }

    public static class PhaseTimings
    {
        public const int CategoryGen = 0, CategoryChunks = 1, RequestCategoryChunks = 2, RestorePlacement = 3, EvictCategory = 4, TerrainNeed = 5;
        public const int HostCategoryGen = 6, HostRestorePlacement = 7;
        public const int RoadGenerate = 8, RoadCopy = 9, RoadEncode = 10;
        public const int PoiRoadDeps = 11, PoiGenerate = 12, PoiCollect = 13, PoiEncode = 14;
        public const int ObjGenerate = 15, ObjCollect = 16, ObjEncode = 17;
        public const int ApplyDecode = 18, ApplyRoad = 19, ApplyPoi = 20, ApplyObj = 21;
        public const int SnapshotSend = 22, SnapshotApply = 23, FingerprintWait = 24;

        public const int KeysFormatted = 0, NeedAvailable = 1, NeedRequested = 2, NeedPassesSkipped = 3;
        public const int EvictCells = 4, EvictObjects = 5, EvictRemoved = 6;
        public const int RoadBytes = 7, PoiBytes = 8, ObjBytes = 9, ApplyBytes = 10, ObjReused = 11, ObjGenerated = 12, TerrainNeedKeys = 13, TerrainCacheHits = 14;

        static readonly PhaseTimingTable table = new PhaseTimingTable();
        static readonly double millisecondsPerTick = 1000.0 / Stopwatch.Frequency;
        static volatile int mainThread;
        // Set by FrameDiagnostics so this file stays free of Unity references.
        internal static Action<string> SpikeLog;

        public static bool Active { get { return mainThread != 0 && Thread.CurrentThread.ManagedThreadId == mainThread; } }
        internal static void RegisterMainThread(int id) { mainThread = id; }
        internal static void Shutdown() { mainThread = 0; table.ReportAndReset(1, millisecondsPerTick); }

        public static long Begin() { return Active ? Stopwatch.GetTimestamp() : 0; }
        public static void End(int slot, long start) { End(slot, start, null); }
        public static void End(int slot, long start, string detail)
        {
            if (start == 0 || !Active) return;
            string spike = table.Record(slot, Stopwatch.GetTimestamp() - start, millisecondsPerTick, detail);
            if (spike != null && SpikeLog != null) SpikeLog(spike);
        }
        public static void Count(int counter, long amount) { if (Active) table.Count(counter, amount); }
        internal static string ReportAndReset(int frames) { return table.ReportAndReset(frames, millisecondsPerTick); }
    }
}
