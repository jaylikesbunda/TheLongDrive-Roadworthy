using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

namespace TldMp
{
    // Optional, bounded frame profiling for live diagnosis. The phase
    // measurements cover only this mod's bridge callbacks and their native
    // observers; Unity's FrameTimingManager values are reported separately.
    // Nothing here runs when diagnostics are disabled.
    public static class FrameDiagnostics
    {
        const int Capacity = 1024;
        const double ReportSeconds = 5.0;
        const int BaseNativeCapacity = 11;
        const int NativeCapacity = BaseNativeCapacity + ProfilerSlotPolicy.SlotCount;
        const int NativeSampleCapacity = Capacity;
        const int MaxCountChunks = 256;
        const int MaxCountObjects = 4096;
        const int MaxRoadCells = 32;
        const int MaxRoadInner = 128;
        const int MaxRoadElements = 512;
        const int MaxPoiChunks = 64;
        const int MaxCategoryNames = 16;
        const int MaxTerrainInstances = 3;
        static readonly bool enabled = ModDiagnostics.On || Loopback.Active;
        static readonly double[] frame = enabled ? new double[Capacity] : null, pulse = enabled ? new double[Capacity] : null, physics = enabled ? new double[Capacity] : null, render = enabled ? new double[Capacity] : null;
        static readonly double[] scratchFrame = enabled ? new double[Capacity] : null, scratchPulse = enabled ? new double[Capacity] : null, scratchPhysics = enabled ? new double[Capacity] : null, scratchRender = enabled ? new double[Capacity] : null;
        static readonly double[] cpu = enabled ? new double[Capacity] : null, gpu = enabled ? new double[Capacity] : null;
        static readonly double[] scratchCpu = enabled ? new double[Capacity] : null, scratchGpu = enabled ? new double[Capacity] : null;
        static readonly FrameTiming[] timings = enabled ? new FrameTiming[1] : null;
        static readonly ProfilerRecorder[] nativeRecorders = enabled ? new ProfilerRecorder[NativeCapacity] : null;
        static readonly string[] nativeLabels = enabled ? BuildNativeLabels() : null;
        static readonly string[] nativeNames = enabled ? new string[NativeCapacity] : null;
        static readonly string[] nativeUnits = enabled ? new string[NativeCapacity] : null;
        static readonly long[,] nativeSamples = enabled ? new long[NativeCapacity, NativeSampleCapacity] : null;
        static readonly long[] nativeScratch = enabled ? new long[NativeSampleCapacity] : null;
        static readonly int[] nativeCounts = enabled ? new int[NativeCapacity] : null;
        static readonly int[] nativeAts = enabled ? new int[NativeCapacity] : null;
        static readonly bool[] nativeUnavailable = enabled ? new bool[NativeCapacity] : null;
        static readonly string[] terrainNames = enabled ? new string[MaxTerrainInstances] : null;
        static readonly terrainGenScript[] terrainRefs = enabled ? new terrainGenScript[MaxTerrainInstances] : null;
        static readonly string[] categoryNames = enabled ? new string[MaxCategoryNames] : null;
        static readonly int[] categoryCounts = enabled ? new int[MaxCategoryNames] : null;
        static readonly bool[] categoryUsed = enabled ? new bool[MaxCategoryNames] : null;
        static bool nativeInitialized, nativeDiscoveryFailed;
        static string nativeFailure;
        static long nativeInitTimestamp;
        static bool nativeRetryAttempted;
        static int frameAt, frameCount, pulseAt, pulseCount, physicsAt, physicsCount, renderAt, renderCount, cpuAt, cpuCount, gpuAt, gpuCount;
        static long previousFrame, pulseStart, physicsStart, renderStart;
        static ulong previousTimingTimestamp;
        static double windowStart;
        static int windowFrames, gc0, gc1, gc2;
        static bool timingCaptureFailed;
        static readonly NativeScriptTimings scriptTimings = enabled ? new NativeScriptTimings() : null;
        static readonly SlowFrameDiagnostics slowFrames = enabled ? new SlowFrameDiagnostics(NativeScriptTimings.Names, NativeLoopTimings.Labels, 1024) : null;
        static volatile int mainThreadId;
        static int workerMeshBegins, workerMeshEnds;
        // One bounded line per terrain instance so the next live run can report
        // the actual noise volume behind GenerateTileMesh. Read-only.
        static readonly List<terrainGenScript> terrainProfileLogged = new List<terrainGenScript>(4);
        static int terrainOverflow, terrainUnmapped, categoryOther;
        static int categoryChunkHits, categoryChunkMisses, categoryChunkDuplicates;
        static int restoreHits, restoreMisses, poiAddSkipped, poiAddExecuted;
        static int roadStampCalls, poiStampCalls;
        const int BehaviorCount = 11;
        static readonly string[] BehaviorNames = new string[] { "mirrorsUpdate", "reflectionMove", "multiUpd", "carUpdate", "audioInterior", "sensorUpdate", "moveCreature", "aiUpdate", "vultureUpdate", "flyerUpdate", "flyerKinematicSkipped" };
        static readonly int[] behaviorCounts = new int[BehaviorCount];
        internal static long placementObjects;

        public static bool Enabled { get { return enabled; } }

        static string[] BuildNativeLabels()
        {
            var labels = new string[NativeCapacity];
            labels[0] = "CPU Main Thread marker";
            labels[1] = "CPU Render Thread marker";
            labels[2] = "Physics simulation time";
            labels[3] = "GC allocation";
            labels[4] = "Draw calls";
            labels[5] = "Batches";
            labels[6] = "Triangles";
            labels[7] = "Vertices";
            labels[8] = "CPU present wait marker";
            labels[9] = "CPU frame-time marker";
            labels[10] = "GPU frame-time marker";
            for (int i = 0; i < ProfilerSlotPolicy.SlotCount; i++) labels[BaseNativeCapacity + i] = ProfilerSlotPolicy.Labels[i];
            return labels;
        }

        public static void RegisterMainThread()
        {
            if (!enabled || mainThreadId != 0) return;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            PhaseTimings.SpikeLog = LogPhaseSpike;
            PhaseTimings.RegisterMainThread(mainThreadId);
            NativeLoopTimings.PhaseObserved = RecordLoopPhase;
            NativeLoopTimings.FrameCompleted = CompleteSlowFrame;
        }
        static void LogPhaseSpike(string text)
        { UnityEngine.Debug.Log("[TLD MP phase-spike] t=" + Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture) + " frame=" + Time.frameCount + " " + text); }
        static bool IsMainThread { get { return mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == mainThreadId; } }
        public static void NativeBegin(int slot)
        {
            if (!enabled || !IsMainThread) return;
            int frameId = Time.frameCount; long ticks = Stopwatch.GetTimestamp();
            scriptTimings.Begin(slot, frameId, ticks); slowFrames.Begin(frameId, slot, ticks);
        }
        public static void NativeEnd(int slot)
        {
            if (!enabled || !IsMainThread) return;
            int frameId = Time.frameCount; long ticks = Stopwatch.GetTimestamp();
            scriptTimings.End(slot, frameId, ticks); slowFrames.End(frameId, slot, ticks);
        }
        internal static void RecordLoopPhase(int frameId, int phase, long start, long end)
        { if (enabled && IsMainThread) slowFrames.AddPhase(frameId, phase, start, end); }
        internal static void CompleteSlowFrame(int frameId, int configuredMask)
        { if (enabled && IsMainThread) slowFrames.CompleteFrame(frameId, configuredMask); }
        public static void NativePoiNativeAddBegin() { NativeBegin(20); }
        public static void NativePoiNativeAddEnd() { NativeEnd(20); }
        public static void NativePoiInstantiateBegin() { NativeBegin(21); }
        public static void NativePoiInstantiateEnd() { NativeEnd(21); }
        static void NoteTerrainProfile(terrainGenScript terrain)
        {
            if (terrain == null || terrain.setting == null) return;
            if (terrainProfileLogged.Count >= 8) return;
            for (int i = 0; i < terrainProfileLogged.Count; i++)
                if (ReferenceEquals(terrainProfileLogged[i], terrain)) return;
            terrainProfileLogged.Add(terrain);
            terrainGenSettings setting = terrain.setting;
            int terrainNoises = setting.terrainNoises == null ? 0 : setting.terrainNoises.Length;
            int subNoises = 0, octaveSum = 0;
            if (setting.terrainNoises != null)
            {
                for (int i = 0; i < setting.terrainNoises.Length; i++)
                {
                    terrainGenSettings.Noises noises = setting.terrainNoises[i];
                    if (noises.noises == null) continue;
                    subNoises += noises.noises.Length;
                    for (int j = 0; j < noises.noises.Length; j++) octaveSum += noises.noises[j].octaves;
                }
            }
            bool imageMap = setting.mapSetting != null && setting.mapSetting.imageMapLoad != null;
            UnityEngine.Debug.Log("[TLD MP terrain] name=" + (terrain.terrainName ?? "") +
                " resolution=" + terrain.resolution + " imageMap=" + imageMap +
                " terrainNoises=" + terrainNoises + " subNoises=" + subNoises +
                " octaveSum=" + octaveSum + " hasCollider=" + terrain.hasCollider);
        }
        public static void NativeTerrainMeshBegin(terrainGenScript terrain)
        {
            if (!enabled) return;
            if (!IsMainThread) { Interlocked.Increment(ref workerMeshBegins); return; }
            NoteTerrainProfile(terrain);
            NativeBegin(22);
        }
        public static void NativeTerrainMeshEnd(terrainGenScript terrain)
        {
            if (!enabled) return;
            if (!IsMainThread) { Interlocked.Increment(ref workerMeshEnds); return; }
            NativeEnd(22);
        }
        public static void NativeTerrainPlaceMeshBegin() { NativeBegin(23); }
        public static void NativeTerrainPlaceMeshEnd() { NativeEnd(23); }
        public static void NativeTerrainTileStartBegin() { NativeBegin(24); }
        public static void NativeTerrainTileStartEnd() { NativeEnd(24); }
        public static void NativeTerrainMeshUploadBegin() { NativeBegin(25); }
        public static void NativeTerrainMeshUploadEnd() { NativeEnd(25); }
        public static void NativeRoadInstantiateBegin() { NativeBegin(26); }
        public static void NativeRoadInstantiateEnd() { NativeEnd(26); }
        public static void NativeRoadInitBegin() { NativeBegin(27); }
        public static void NativeRoadInitEnd() { NativeEnd(27); }
        public static void NativeRoadGeometryBegin() { NativeBegin(28); }
        public static void NativeRoadGeometryEnd() { NativeEnd(28); }
        public static void NativeRoadPoleBegin() { NativeBegin(29); }
        public static void NativeRoadPoleEnd() { NativeEnd(29); }
        public static void NativeRoadDestroyBegin() { NativeBegin(30); }
        public static void NativeRoadDestroyEnd() { NativeEnd(30); }
        public static void NativePoiSpawnBegin() { NativeBegin(31); }
        public static void NativePoiSpawnEnd() { NativeEnd(31); }
        public static void NativePoiStuffBegin() { NativeBegin(32); }
        public static void NativePoiStuffEnd() { NativeEnd(32); }
        public static void NativePoiItemsBegin() { NativeBegin(33); }
        public static void NativePoiItemsEnd() { NativeEnd(33); }
        public static void NativePoiFixedBegin() { NativeBegin(34); }
        public static void NativePoiFixedEnd() { NativeEnd(34); }
        public static void NativePoiRemoveBegin() { NativeBegin(35); }
        public static void NativePoiRemoveEnd() { NativeEnd(35); }
        public static void NativeBehaviorMirrorsBegin() { NoteBehavior(0); NativeBegin(36); }
        public static void NativeBehaviorMirrorsEnd() { NativeEnd(36); }
        public static void NativeBehaviorReflectionBegin() { NoteBehavior(1); NativeBegin(37); }
        public static void NativeBehaviorReflectionEnd() { NativeEnd(37); }
        public static void NativeBehaviorMultiBegin() { NoteBehavior(2); NativeBegin(38); }
        public static void NativeBehaviorMultiEnd() { NativeEnd(38); }
        public static void NativeBehaviorCarBegin() { NoteBehavior(3); NativeBegin(39); }
        public static void NativeBehaviorCarEnd() { NativeEnd(39); }
        public static void NativeBehaviorAudioBegin() { NoteBehavior(4); NativeBegin(40); }
        public static void NativeBehaviorAudioEnd() { NativeEnd(40); }
        public static void NativeBehaviorSensorBegin() { NoteBehavior(5); NativeBegin(41); }
        public static void NativeBehaviorSensorEnd() { NativeEnd(41); }
        public static void NativeBehaviorCreatureBegin() { NoteBehavior(6); NativeBegin(42); }
        public static void NativeBehaviorCreatureEnd() { NativeEnd(42); }
        public static void NativeBehaviorAiBegin() { NoteBehavior(7); NativeBegin(43); }
        public static void NativeBehaviorAiEnd() { NativeEnd(43); }
        public static void NativeBehaviorVultureBegin() { NoteBehavior(8); NativeBegin(44); }
        public static void NativeBehaviorVultureEnd() { NativeEnd(44); }
        public static void NativeBehaviorFlyerBegin() { NoteBehavior(9); NativeBegin(45); }
        public static void NativeBehaviorFlyerEnd() { NativeEnd(45); }
        public static void NativeBehaviorAttachablesBegin() { NativeBegin(46); }
        public static void NativeBehaviorAttachablesEnd() { NativeEnd(46); }
        public static void NativeBehaviorPartConditionsBegin() { NativeBegin(47); }
        public static void NativeBehaviorPartConditionsEnd() { NativeEnd(47); }
        public static void NativeBehaviorUsablesBegin() { NativeBegin(48); }
        public static void NativeBehaviorUsablesEnd() { NativeEnd(48); }
        public static void NativeBehaviorWheelGraphicsBegin() { NativeBegin(49); }
        public static void NativeBehaviorWheelGraphicsEnd() { NativeEnd(49); }
        public static void NativeBehaviorSyncBegin() { NativeBegin(50); }
        public static void NativeBehaviorSyncEnd() { NativeEnd(50); }
        public static void NativeVisszaRakasBegin() { NativeBegin(51); }
        public static void NativeVisszaRakasEnd() { NativeEnd(51); }
        public static void NativeWheelGraphicsBegin() { NativeBegin(52); }
        public static void NativeWheelGraphicsEnd() { NativeEnd(52); }
        public static void NativeWheelSizeBegin() { NativeBegin(53); }
        public static void NativeWheelSizeEnd() { NativeEnd(53); }

        // Fixed native observer entry points. Slot numbers are part of the
        // diagnostic contract; see NativeScriptTimings.Names.
        public static void NativeObjGenBegin() { NativeBegin(4); }
        public static void NativeObjGenEnd() { NativeEnd(4); }
        public static void NativeObjPlaceBegin() { NativeBegin(5); }
        public static void NativeObjPlaceEnd() { NativeEnd(5); }
        public static void NativeCategoryPlaceBegin(objGenScript.categoryClass cat)
        {
            if (!enabled) return;
            NoteCategory(cat);
            NativeBegin(6);
        }
        public static void NativeCategoryPlaceEnd() { NativeEnd(6); }
        public static void NativePoiBegin() { NativeBegin(7); }
        public static void NativePoiEnd() { NativeEnd(7); }
        public static void NativeRoadNeededBegin() { NativeBegin(8); }
        public static void NativeRoadNeededEnd() { NativeEnd(8); }
        public static void NativeRoadPlaceBegin() { NativeBegin(9); }
        public static void NativeRoadPlaceEnd() { NativeEnd(9); }
        public static void NativeTerrainBegin(terrainGenScript terrain)
        {
            if (!enabled) return;
            int slot = TerrainSlot(terrain);
            if (slot < 0) { terrainUnmapped++; return; }
            NativeBegin(10 + slot);
        }
        public static void NativeTerrainEnd(terrainGenScript terrain)
        {
            if (!enabled) return;
            int slot = TerrainSlot(terrain);
            if (slot < 0) return;
            NativeEnd(10 + slot);
        }
        public static void NativeTerrainPlaceBegin() { NativeBegin(13); }
        public static void NativeTerrainPlaceEnd() { NativeEnd(13); }
        public static void NativeTerrainCheckBegin() { NativeBegin(14); }
        public static void NativeTerrainCheckEnd() { NativeEnd(14); }
        public static void NativeThumbleweedBegin() { NativeBegin(15); }
        public static void NativeThumbleweedEnd() { NativeEnd(15); }
        public static void NativeCategoryChunksBegin() { NativeBegin(16); }
        public static void NativeCategoryChunksEnd() { NativeEnd(16); }
        public static void NativeRestorePlacementBegin() { NativeBegin(17); }
        public static void NativeRestorePlacementEnd() { NativeEnd(17); }
        public static void NativeRoadNativeBegin() { NativeBegin(18); }
        public static void NativeRoadNativeEnd() { NativeEnd(18); }
        public static void NativePoiAddBegin() { NativeBegin(19); }
        public static void NativePoiAddEnd() { NativeEnd(19); }
        public static void AddRoadStamp(long ticks) { if (enabled) scriptTimings.AddAggregate(0, ticks); }
        public static void AddPoiStamp(long ticks) { if (enabled) scriptTimings.AddAggregate(1, ticks); }

        static int TerrainSlot(terrainGenScript terrain)
        {
            if (ReferenceEquals(terrain, null)) return -1;
            for (int i = 0; i < MaxTerrainInstances; i++)
            {
                if (ReferenceEquals(terrainRefs[i], terrain)) return i;
                // Unity's destroyed-object comparison allows a scene reload to
                // reclaim a slot without retaining a dead reference forever.
                if (!ReferenceEquals(terrainRefs[i], null) && terrainRefs[i] == null)
                {
                    terrainRefs[i] = null; terrainNames[i] = null;
                }
            }
            for (int i = 0; i < MaxTerrainInstances; i++)
            {
                if (terrainRefs[i] == null)
                {
                    terrainRefs[i] = terrain; terrainNames[i] = terrain.terrainName;
                    return i;
                }
            }
            terrainOverflow++;
            return -1;
        }

        static void NoteCategory(objGenScript.categoryClass cat)
        {
            string name = cat == null ? null : cat.name;
            for (int i = 0; i < MaxCategoryNames; i++)
            {
                if (categoryUsed[i] && String.Equals(categoryNames[i], name, StringComparison.Ordinal))
                { categoryCounts[i]++; return; }
            }
            for (int i = 0; i < MaxCategoryNames; i++)
            {
                if (!categoryUsed[i])
                {
                    categoryUsed[i] = true; categoryNames[i] = name; categoryCounts[i] = 1;
                    return;
                }
            }
            categoryOther++;
        }

        public static void NoteCategoryChunks(bool hit, bool duplicate)
        {
            if (!enabled) return;
            if (duplicate) categoryChunkDuplicates++;
            else if (hit) categoryChunkHits++;
            else categoryChunkMisses++;
        }
        public static void NoteRestorePlacement(bool hit)
        {
            if (!enabled) return;
            if (hit) restoreHits++; else restoreMisses++;
        }
        public static void NotePoiAdd(bool executed)
        {
            if (!enabled) return;
            if (executed) poiAddExecuted++; else poiAddSkipped++;
        }
        public static void NoteBehavior(int slot)
        {
            if (!enabled || slot < 0 || slot >= BehaviorCount) return;
            if (behaviorCounts[slot] < Int32.MaxValue) behaviorCounts[slot]++;
        }
        static string BehaviorSummary()
        {
            var text = new StringBuilder("behaviors{");
            for (int i = 0; i < BehaviorCount; i++)
            {
                if (i != 0) text.Append(';');
                text.Append(BehaviorNames[i]).Append('=').Append(behaviorCounts[i]);
                behaviorCounts[i] = 0;
            }
            return text.Append('}').ToString();
        }
        public static void NoteRoadStamp() { if (enabled) roadStampCalls++; }
        public static void NotePoiStamp() { if (enabled) poiStampCalls++; }

        static double Milliseconds(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }
        static void Add(double[] values, ref int at, ref int count, double value)
        { values[at] = value; at = (at + 1) % Capacity; if (count < Capacity) count++; }

        public static void BeginFrame()
        {
            if (!enabled) return;
            RegisterMainThread();
            NativeLoopTimings.TryInstall();
            NativeLoopTimings.SamplePreviousFrame(Time.frameCount);
            long now = Stopwatch.GetTimestamp();
            if (!nativeInitialized) { nativeInitialized = true; nativeInitTimestamp = now; InitializeNativeCounters(); }
            else if (!nativeRetryAttempted && now - nativeInitTimestamp >= 30L * Stopwatch.Frequency && HasUnavailableNativeCounters())
            { nativeRetryAttempted = true; InitializeNativeCounters(); }
            SampleNativeCounters();
            RendererCensus.Tick();
            CameraCost.Tick();
            if (previousFrame != 0) Add(frame, ref frameAt, ref frameCount, Milliseconds(now - previousFrame));
            previousFrame = now; windowFrames++;
            if (windowStart == 0)
            {
                windowStart = now / (double)Stopwatch.Frequency;
                gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2);
            }
            if (!timingCaptureFailed)
            {
                try
                {
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                    {
                        FrameTiming sample = timings[0]; ulong stamp = sample.frameStartTimestamp;
                        if (stamp == 0 || stamp != previousTimingTimestamp)
                        {
                            if (sample.cpuFrameTime > 0) Add(cpu, ref cpuAt, ref cpuCount, sample.cpuFrameTime);
                            if (sample.gpuFrameTime > 0) Add(gpu, ref gpuAt, ref gpuCount, sample.gpuFrameTime);
                            if (stamp != 0) previousTimingTimestamp = stamp;
                        }
                    }
                }
                catch { timingCaptureFailed = true; }
            }
            if (now / (double)Stopwatch.Frequency - windowStart >= ReportSeconds) Report(now / (double)Stopwatch.Frequency);
        }

        public static void PulseBegin() { if (enabled) pulseStart = Stopwatch.GetTimestamp(); }
        public static void PulseEnd() { if (enabled && pulseStart != 0) { Add(pulse, ref pulseAt, ref pulseCount, Milliseconds(Stopwatch.GetTimestamp() - pulseStart)); pulseStart = 0; } }
        public static void PhysicsBegin() { if (enabled) physicsStart = Stopwatch.GetTimestamp(); }
        public static void PhysicsEnd() { if (enabled && physicsStart != 0) { Add(physics, ref physicsAt, ref physicsCount, Milliseconds(Stopwatch.GetTimestamp() - physicsStart)); physicsStart = 0; } }
        public static void RenderBegin() { if (enabled) renderStart = Stopwatch.GetTimestamp(); }
        public static void RenderEnd() { if (enabled && renderStart != 0) { Add(render, ref renderAt, ref renderCount, Milliseconds(Stopwatch.GetTimestamp() - renderStart)); renderStart = 0; } }

        static double Percentile(double[] values, int count, double fraction)
        { if (count == 0) return 0; int index = (int)Math.Ceiling(fraction * count) - 1; return values[Math.Max(0, Math.Min(count - 1, index))]; }
        static string Summary(double[] ring, int count, double[] scratch)
        {
            if (count == 0) return "unavailable";
            Array.Copy(ring, 0, scratch, 0, count); Array.Sort(scratch, 0, count);
            return "p50/p95/p99=" + Percentile(scratch, count, .50).ToString("F2", CultureInfo.InvariantCulture) + "/" +
                Percentile(scratch, count, .95).ToString("F2", CultureInfo.InvariantCulture) + "/" +
                Percentile(scratch, count, .99).ToString("F2", CultureInfo.InvariantCulture) + "ms n=" + count;
        }
        static string FrameTimings()
        {
            if (timingCaptureFailed) return "cpuFrameMs{unavailable} gpuFrameMs{unavailable}";
            return "cpuFrameMs{" + Summary(cpu, cpuCount, scratchCpu) + "} gpuFrameMs{" + Summary(gpu, gpuCount, scratchGpu) + "}";
        }
        static bool HasUnavailableNativeCounters()
        { for (int i = 0; i < NativeCapacity; i++) if (nativeUnavailable[i]) return true; return false; }

        static void InitializeNativeCounters()
        {
            nativeDiscoveryFailed = false; nativeFailure = null;
            List<ProfilerRecorderHandle> available = new List<ProfilerRecorderHandle>();
            bool[] selected = new bool[NativeCapacity];
            bool[] startedThisAttempt = new bool[NativeCapacity];
            try
            {
                ProfilerRecorderHandle.GetAvailable(available);
                for (int slot = 0; slot < NativeCapacity; slot++)
                {
                    if (nativeNames[slot] != null && !nativeUnavailable[slot]) continue;
                    for (int i = 0; i < available.Count; i++)
                    {
                        ProfilerRecorderHandle handle = available[i];
                        if (!handle.Valid) continue;
                        ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
                        if (!MatchesNativeSlot(slot, description)) continue;
                        bool alreadyUsed = false;
                        string discoveredName = description.Category.Name + "/" + description.Name;
                        for (int j = 0; j < NativeCapacity; j++) if (selected[j] && nativeNames[j] == discoveredName) { alreadyUsed = true; break; }
                        if (alreadyUsed) continue;
                        try
                        {
                            ProfilerRecorder recorder = ProfilerRecorder.StartNew(description.Category, description.Name, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
                            if (!recorder.Valid) { recorder.Dispose(); continue; }
                            nativeRecorders[slot] = recorder;
                            nativeNames[slot] = discoveredName;
                            nativeUnits[slot] = description.UnitType.ToString(); nativeUnavailable[slot] = false;
                            selected[slot] = true; startedThisAttempt[slot] = true; break;
                        }
                        catch { }
                    }
                    if (!selected[slot]) nativeUnavailable[slot] = true;
                }
            }
            catch (Exception ex)
            {
                nativeDiscoveryFailed = true; nativeFailure = ex.GetType().Name;
                for (int i = 0; i < NativeCapacity; i++) if (startedThisAttempt[i])
                { try { nativeRecorders[i].Dispose(); } catch { } nativeRecorders[i] = default(ProfilerRecorder); nativeNames[i] = null; nativeUnits[i] = null; nativeUnavailable[i] = true; }
                for (int i = 0; i < NativeCapacity; i++) if (nativeNames[i] == null) nativeUnavailable[i] = true;
            }
        }

        static bool Has(string value, string token)
        { return value != null && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0; }

        static bool MatchesNativeSlot(int slot, ProfilerRecorderDescription d)
        {
            if (slot >= ProfilerSlotPolicy.FirstSlot)
                return ProfilerSlotPolicy.Matches(slot, d.Category.Name, d.Name, d.UnitType);
            string name = d.Name ?? "", category = d.Category.Name ?? "";
            switch (slot)
            {
                case 0: return Has(name, "main thread");
                case 1: return Has(name, "render thread");
                case 2: return (Has(name, "physics.simulate") || Has(name, "physics.simulation") ||
                    (Has(category, "physics") && (Has(name, "simulate") || Has(name, "simulation")))) &&
                    d.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds;
                case 3: return (Has(name, "gc allocated") || Has(name, "gc alloc") || Has(name, "gc.alloc")) &&
                    d.UnitType != ProfilerMarkerDataUnit.Bytes;
                case 4: return Has(name, "draw calls") || Has(name, "drawcall");
                case 5: return Has(name, "batches");
                case 6: return Has(name, "triangles");
                case 7: return Has(name, "vertices");
                case 8: return Has(name, "waitforpresent") || Has(name, "wait for present") || Has(name, "wait for target fps");
                case 9: return !Has(category, "gpu") && !Has(name, "gpu") && Has(name, "frame") &&
                    (Has(name, "cpu") || Has(category, "cpu"));
                case 10: return (Has(category, "gpu") || Has(name, "gpu")) && Has(name, "frame") && d.UnitType.ToString() == "TimeNanoseconds";
                default: return false;
            }
        }

        static void SampleNativeCounters()
        {
            for (int i = 0; i < NativeCapacity; i++)
            {
                if (nativeUnavailable[i]) continue;
                try
                {
                    ProfilerRecorder recorder = nativeRecorders[i];
                    if (!recorder.Valid) { try { nativeRecorders[i].Dispose(); } catch { } nativeRecorders[i] = default(ProfilerRecorder); nativeUnavailable[i] = true; continue; }
                    if (recorder.Count <= 0) continue;
                    long value = recorder.LastValue;
                    nativeSamples[i, nativeAts[i]] = value; nativeAts[i] = (nativeAts[i] + 1) % NativeSampleCapacity;
                    if (nativeCounts[i] < NativeSampleCapacity) nativeCounts[i]++;
                }
                catch { nativeUnavailable[i] = true; try { nativeRecorders[i].Dispose(); } catch { } }
            }
        }

        static string NativeCounters()
        {
            string result = "nativeCounters{discovery=" + (nativeDiscoveryFailed ? "unavailable:" + nativeFailure : "available") +
                "; Main Thread marker values can include waits; units follow Unity descriptions";
            for (int i = 0; i < NativeCapacity; i++)
            {
                result += "; " + nativeLabels[i] + "=";
                if (nativeUnavailable[i] || nativeNames[i] == null) result += "unavailable";
                else result += nativeNames[i] + "[" + nativeUnits[i] + "] " + NativeSummary(i);
            }
            return result + "}";
        }

        static string NativeSummary(int slot)
        {
            int count = nativeCounts[slot]; if (count == 0) return "no samples";
            for (int i = 0; i < count; i++) nativeScratch[i] = nativeSamples[slot, i];
            Array.Sort(nativeScratch, 0, count);
            long p50 = nativeScratch[Math.Max(0, (int)Math.Ceiling(.50 * count) - 1)];
            long p95 = nativeScratch[Math.Max(0, (int)Math.Ceiling(.95 * count) - 1)];
            long max = nativeScratch[count - 1];
            return "p50/p95/max=" + p50.ToString(CultureInfo.InvariantCulture) + "/" + p95.ToString(CultureInfo.InvariantCulture) + "/" + max.ToString(CultureInfo.InvariantCulture) + " n=" + count;
        }

        static string WorldCounts(out double milliseconds, out bool capped)
        {
            long started = Stopwatch.GetTimestamp();
            capped = false;
            var text = new StringBuilder("mapCounts{");
            mapSettingScript map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
            if (map == null) { text.Append("unavailable}"); milliseconds = Milliseconds(Stopwatch.GetTimestamp() - started); return text.ToString(); }

            int categories = 0, generated = 0, needed = 0, queue = 0, objects = 0, removals = 0, sampledChunks = 0;
            int pendingPlace = 0, pendingRemove = 0, externalMissing = 0, objectsVisited = 0;
            bool objectCap = false;
            int budget = MaxCountChunks;
            int objectBudget = MaxCountObjects;
            if (map.objGens != null)
            {
                for (int g = 0; g < map.objGens.Count; g++)
                {
                    var gen = map.objGens[g];
                    if (gen == null || gen.categories == null) continue;
                    categories += gen.categories.Count;
                    for (int c = 0; c < gen.categories.Count; c++)
                    {
                        var cat = gen.categories[c];
                        if (cat == null) continue;
                        if (cat.generatedChunks != null) generated += cat.generatedChunks.Count;
                        if (cat.chunksNeeded != null) needed += cat.chunksNeeded.Count;
                        if (cat.placeOrder != null) queue += cat.placeOrder.Count;
                        if (cat.generatedChunks != null)
                        {
                            foreach (var pair in cat.generatedChunks)
                            {
                                if (budget <= 0) { objectCap = true; break; }
                                budget--; sampledChunks++;
                                if (pair.Value != null && pair.Value.objs != null)
                                {
                                    objects += pair.Value.objs.Count;
                                    foreach (var obj in pair.Value.objs.Values)
                                    {
                                        if (objectBudget <= 0) { objectCap = true; break; }
                                        objectBudget--; objectsVisited++;
                                        if (obj == null) continue;
                                        if (obj.place) { if (!obj.placed) pendingPlace++; }
                                        else if (obj.obj != null) pendingRemove++;
                                        if (obj.place && obj.placed && obj.obj == null) externalMissing++;
                                    }
                                }
                                if (pair.Value != null && pair.Value.objsToRemove != null) removals += pair.Value.objsToRemove.Count;
                                if (objectBudget <= 0) { objectCap = true; break; }
                            }
                        }
                        if (budget <= 0 || objectCap) { objectCap = true; break; }
                    }
                    if (budget <= 0 || objectCap) { objectCap = true; break; }
                }
            }
            capped |= objectCap;
            text.Append("objGens=").Append(map.objGens == null ? 0 : map.objGens.Count)
                .Append(" categories=").Append(categories)
                .Append(" generatedChunks=").Append(generated)
                .Append(" neededChunks=").Append(needed)
                .Append(" queueChunks=").Append(queue)
                .Append(" objectsSampled=").Append(objects)
                .Append(" objectsVisited=").Append(objectsVisited)
                .Append(" pendingPlace=").Append(pendingPlace)
                .Append(" pendingRemove=").Append(pendingRemove)
                .Append(" externalMissing=").Append(externalMissing)
                .Append(" removalsSampled=").Append(removals)
                .Append(" chunksSampled=").Append(sampledChunks)
                .Append(" sampleCapped=").Append(objectCap);

            int roadCells = 0, roadInner = 0, roadElements = 0, roadChunks = 0, roadNeeded = 0;
            bool roadCap = false;
            if (map.roadGens != null)
            {
                for (int g = 0; g < map.roadGens.Count; g++)
                {
                    var road = map.roadGens[g];
                    if (road == null) continue;
                    roadChunks += road.chunks == null ? 0 : road.chunks.Count;
                    roadNeeded += road.chunksNeeded == null ? 0 : road.chunksNeeded.Count;
                    int cells = 0, innerBudget = MaxRoadInner, elemBudget = MaxRoadElements;
                    if (road.chunksNeeded != null && road.chunks != null)
                    {
                        for (int i = 0; i < road.chunksNeeded.Count && cells < MaxRoadCells; i++, cells++)
                        {
                            roadGenScript.dotChunkClass chunk;
                            if (!road.chunks.TryGetValue(road.chunksNeeded[i], out chunk) || chunk == null || chunk.innerChunks == null) continue;
                            roadCells++;
                            foreach (var inner in chunk.innerChunks)
                            {
                                if (innerBudget-- <= 0) { roadCap = true; break; }
                                roadInner++;
                                if (inner.Value == null || inner.Value.roads == null) continue;
                                for (int r = 0; r < inner.Value.roads.Count; r++)
                                {
                                    var roadChunk = inner.Value.roads[r];
                                    if (roadChunk == null || inner.Value.roads == null) continue;
                                    if (elemBudget <= 0) { roadCap = true; break; }
                                    elemBudget--;
                                    roadElements += roadChunk.roadElements == null ? 0 : roadChunk.roadElements.Count;
                                }
                                if (elemBudget <= 0) { roadCap = true; break; }
                            }
                            if (innerBudget <= 0 || elemBudget <= 0) break;
                        }
                    }
                    if (road.chunksNeeded != null && road.chunksNeeded.Count > MaxRoadCells) roadCap = true;
                }
            }
            capped |= roadCap;
            text.Append(" roadGens=").Append(map.roadGens == null ? 0 : map.roadGens.Count)
                .Append(" roadChunks=").Append(roadChunks)
                .Append(" roadNeeded=").Append(roadNeeded)
                .Append(" roadCellsSampled=").Append(roadCells)
                .Append(" roadInnerSampled=").Append(roadInner)
                .Append(" roadElementsSampled=").Append(roadElements)
                .Append(" roadCapped=").Append(roadCap);

            int poiGens = 0, poiChunks = 0, poiNeeded = 0, pois = 0, poiPending = 0;
            bool poiCap = false;
            if (map.poiGens != null)
            {
                poiGens = map.poiGens.Count;
                int budgetPoi = MaxPoiChunks;
                for (int g = 0; g < map.poiGens.Count; g++)
                {
                    var gen = map.poiGens[g];
                    if (gen == null) continue;
                    poiChunks += gen.chunks == null ? 0 : gen.chunks.Count;
                    poiNeeded += gen.chunksNeeded == null ? 0 : gen.chunksNeeded.Count;
                    if (gen.chunks == null) continue;
                    foreach (var pair in gen.chunks)
                    {
                        if (budgetPoi-- <= 0) { poiCap = true; break; }
                        if (pair.Value == null) continue;
                        if (pair.Value.pois != null) pois += pair.Value.pois.Count;
                        if (pair.Value.poiToPlace != null) poiPending += pair.Value.poiToPlace.Count;
                    }
                    if (budgetPoi <= 0) { poiCap = true; break; }
                }
            }
            capped |= poiCap;
            text.Append(" poiGens=").Append(poiGens)
                .Append(" poiChunks=").Append(poiChunks)
                .Append(" poiNeeded=").Append(poiNeeded)
                .Append(" pois=").Append(pois)
                .Append(" poiPending=").Append(poiPending)
                .Append(" poiCapped=").Append(poiCap);

            int terrains = 0;
            if (map.terrainGen != null)
            {
                for (int i = 0; i < map.terrainGen.Count; i++)
                {
                    var terrain = map.terrainGen[i];
                    if (terrain == null) continue;
                    terrains++;
                    text.Append(" terrain").Append(i)
                        .Append("{name=").Append(terrain.terrainName ?? "")
                        .Append(" tiles=").Append(terrain.terrains == null ? 0 : terrain.terrains.Count)
                        .Append(" needed=").Append(terrain.coordsNeeded == null ? 0 : terrain.coordsNeeded.Count)
                        .Append(" removing=").Append(terrain.coordsToRemove == null ? 0 : terrain.coordsToRemove.Count)
                        .Append(" resolution=").Append(terrain.resolution)
                        .Append(" tileSize=").Append(terrain.tileSize.ToString("0.##", CultureInfo.InvariantCulture))
                        .Append(" draw=").Append(terrain.drawDistance.ToString("0.##", CultureInfo.InvariantCulture))
                        .Append('}');
                }
            }
            text.Append(" terrains=").Append(terrains)
                .Append(" thumbleweeds=").Append(thumbleweedMain.s == null || thumbleweedMain.s.thumbleweeds == null ? 0 : thumbleweedMain.s.thumbleweeds.Count)
                .Append(" mirrors=").Append(mainscript.s == null || mainscript.s.Mirrors == null ? 0 : mainscript.s.Mirrors.Count)
                .Append(" reflectionPlanes=").Append(reflectionMainScript.s == null || reflectionMainScript.s.reflectionPlanes == null ? 0 : reflectionMainScript.s.reflectionPlanes.Count)
                .Append('}');
            milliseconds = Milliseconds(Stopwatch.GetTimestamp() - started);
            return text.ToString();
        }

        static string BridgeCounters()
        {
            var text = new StringBuilder("bridgeCounters{");
            text.Append("categoryChunksHit=").Append(categoryChunkHits)
                .Append(" categoryChunksMiss=").Append(categoryChunkMisses)
                .Append(" duplicateCategoryChunks=").Append(categoryChunkDuplicates)
                .Append(" restoreHit=").Append(restoreHits)
                .Append(" restoreMiss=").Append(restoreMisses)
                .Append(" placementObjects=").Append(placementObjects)
                .Append(" poiAddSkipped=").Append(poiAddSkipped)
                .Append(" poiAddExecuted=").Append(poiAddExecuted)
                .Append(" roadStampCalls=").Append(roadStampCalls)
                .Append(" poiStampCalls=").Append(poiStampCalls)
                .Append(" terrainUnmapped=").Append(terrainUnmapped)
                .Append(" terrainOverflow=").Append(terrainOverflow)
                .Append('}');
            categoryChunkHits = categoryChunkMisses = categoryChunkDuplicates = 0;
            restoreHits = restoreMisses = 0; placementObjects = 0;
            poiAddSkipped = poiAddExecuted = 0;
            roadStampCalls = poiStampCalls = 0;
            terrainUnmapped = terrainOverflow = 0;
            return text.ToString();
        }

        static string CategoryCalls()
        {
            var text = new StringBuilder("categoryCalls{");
            bool first = true;
            for (int i = 0; i < MaxCategoryNames; i++)
            {
                if (!categoryUsed[i]) continue;
                if (!first) text.Append(';');
                first = false;
                text.Append(categoryNames[i] ?? "<null>").Append('=').Append(categoryCounts[i]);
                categoryUsed[i] = false; categoryNames[i] = null; categoryCounts[i] = 0;
            }
            if (categoryOther != 0) { if (!first) text.Append(';'); text.Append("other=").Append(categoryOther); }
            categoryOther = 0;
            return text.Append('}').ToString();
        }

        static string TerrainMap()
        {
            var text = new StringBuilder("terrainSlots{");
            for (int i = 0; i < MaxTerrainInstances; i++)
            {
                if (i != 0) text.Append(';');
                text.Append(i).Append('=').Append(terrainNames[i] ?? "unassigned");
            }
            return text.Append('}').ToString();
        }

        public static void Shutdown()
        {
            if (!enabled) return;
            NativeLoopTimings.Shutdown();
            NativeLoopTimings.PhaseObserved = null; NativeLoopTimings.FrameCompleted = null;
            slowFrames.Reset(); mainThreadId = 0; PhaseTimings.Shutdown(); PhaseTimings.SpikeLog = null;
            Interlocked.Exchange(ref workerMeshBegins, 0); Interlocked.Exchange(ref workerMeshEnds, 0);
            if (nativeRecorders == null) return;
            for (int i = 0; i < NativeCapacity; i++)
            {
                try { nativeRecorders[i].Dispose(); } catch { }
                nativeRecorders[i] = default(ProfilerRecorder); nativeNames[i] = null;
                nativeCounts[i] = nativeAts[i] = 0; nativeUnavailable[i] = false;
            }
            for (int i = 0; i < MaxTerrainInstances; i++) { terrainRefs[i] = null; terrainNames[i] = null; }
            nativeDiscoveryFailed = false; nativeFailure = null; nativeRetryAttempted = false; nativeInitTimestamp = 0;
            nativeInitialized = false;
        }

        static void Report(double now)
        {
            double elapsed = Math.Max(.001, now - windowStart);
            int nextGc0 = GC.CollectionCount(0), nextGc1 = GC.CollectionCount(1), nextGc2 = GC.CollectionCount(2);
            string cpuGpu = FrameTimings();
            double countsMs; bool countsCapped;
            string counts = WorldCounts(out countsMs, out countsCapped);
            scriptTimings.SetWindowFrames(windowFrames);
            string script = scriptTimings.ReportAndReset(1000.0 / Stopwatch.Frequency);
            UnityEngine.Debug.Log("[TLD MP fps] t=" + Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture) + " window=" + elapsed.ToString("F1", CultureInfo.InvariantCulture) + "s fps=" + (windowFrames / elapsed).ToString("F1", CultureInfo.InvariantCulture) +
                " frameMs{" + Summary(frame, frameCount, scratchFrame) + "} bridgePulseMs{" + Summary(pulse, pulseCount, scratchPulse) + "}" +
                " bridgePhysicsMs{" + Summary(physics, physicsCount, scratchPhysics) + "} bridgeRenderMs{" + Summary(render, renderCount, scratchRender) + "}" +
                " " + cpuGpu + " " + NativeCounters() + " " + NativeLoopTimings.Report() + " " + script + " gcDelta=" + (nextGc0 - gc0) + "/" + (nextGc1 - gc1) + "/" + (nextGc2 - gc2) + " (0/1/2) vSync=" + QualitySettings.vSyncCount +
                " targetFps=" + Application.targetFrameRate + " samplesCap=" + Capacity);
            UnityEngine.Debug.Log("[TLD MP fps-world] t=" + Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture) + " " + counts +
                " mapCountsMs=" + countsMs.ToString("F3", CultureInfo.InvariantCulture) +
                " mapCountsCapped=" + countsCapped +
                " managedHeapBytes=" + GC.GetTotalMemory(false).ToString(CultureInfo.InvariantCulture) +
                " " + BridgeCounters() + " " + CategoryCalls() + " " + TerrainMap() + " " + BehaviorSummary() +
                " " + TerrainRoadSelection.ReportAndReset() +
                " kinematicWarnings=" + CargoDiagnostics.TakeKinematicWarnings() +
                " gcDeltaNote=collections may be reported across generation counters; not allocation volume or full collections");
            UnityEngine.Debug.Log("[TLD MP fps-slow] t=" + Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture) + " " +
                slowFrames.ReportAndReset(Stopwatch.Frequency) + " workerTerrainMesh{frame=uncorrelated begins=" +
                Interlocked.Exchange(ref workerMeshBegins, 0) + " ends=" + Interlocked.Exchange(ref workerMeshEnds, 0) + "}");
            UnityEngine.Debug.Log("[TLD MP fps-phase] t=" + Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture) + " " + PhaseTimings.ReportAndReset(windowFrames));
            NativeLoopTimings.ResetWindow();
            windowStart = now; windowFrames = 0; gc0 = nextGc0; gc1 = nextGc1; gc2 = nextGc2;
            frameAt = frameCount = pulseAt = pulseCount = physicsAt = physicsCount = renderAt = renderCount = cpuAt = cpuCount = gpuAt = gpuCount = 0;
            for (int i = 0; i < NativeCapacity; i++) nativeAts[i] = nativeCounts[i] = 0;
        }
    }
}
