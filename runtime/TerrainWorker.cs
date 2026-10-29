using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEngine;

namespace TldMp
{
    // Distant terrain tile meshes are built on a worker thread. The main thread
    // gathers the same road and POI lists that vanilla GenerateTileMesh gathers,
    // at the same moment, so road and POI generation keeps its timing. The worker
    // is a port of the no-hole GenerateTileMesh path: every scratch buffer is a
    // local or a fresh allocation, and the arithmetic runs in the vanilla order so
    // the output is bit-identical. Holes, collider terrain, image maps and any
    // generator without a snapshot keep the stock synchronous Tile.
    internal sealed class TerrainTileSnapshot
    {
        internal readonly int Resolution;
        internal readonly float VertexDist;
        internal readonly Vector3 Offset;
        internal readonly float TileSize;
        internal readonly terrainGenSettings Setting;
        // Read-only copies of the FStart buffers. Workers never write them.
        internal readonly int[] T;
        internal readonly Vector2[] U;
        internal readonly int[] T2;

        internal TerrainTileSnapshot(int resolution, float vertexDist, Vector3 offset, float tileSize,
            terrainGenSettings setting, int[] t, Vector2[] u, int[] t2)
        {
            // Reference checks: a Unity == on an uninitialized MonoBehaviour reads as null.
            if (resolution < 2 || (object)setting == null || t == null || u == null || t2 == null)
                throw new ArgumentException("Terrain tile snapshot is incomplete.");
            if (t.Length != (resolution - 1) * (resolution - 1) * 6 || u.Length != resolution * resolution ||
                t2.Length != (resolution + 1) * (resolution + 1) * 6)
                throw new ArgumentException("Terrain tile buffers do not match the resolution.");
            Resolution = resolution;
            VertexDist = vertexDist;
            Offset = offset;
            TileSize = tileSize;
            Setting = setting;
            T = t;
            U = u;
            T2 = t2;
        }
    }

    // Pure port of terrainGenScript.GenerateTileMesh for the case with no terrain
    // holes. Compare each statement with terrainGenScript.cs before changing it.
    internal static class TerrainTileMesh
    {
        internal static terrainGenScript.smesh Generate(TerrainTileSnapshot s, Vector3d gpos,
            List<roadGenScript.roadClass> roadlist, List<poiGenScript.poiClass> poiList)
        {
            int resolution = s.Resolution;
            float vertexDist = s.VertexDist;
            Vector3 offset = s.Offset;
            terrainGenSettings setting = s.Setting;
            int vertexNum = resolution * resolution;
            int vertexNum2 = (resolution + 2) * (resolution + 2);
            Vector3[] v = new Vector3[vertexNum];
            Vector3[] n = new Vector3[vertexNum];
            Color[] c = new Color[vertexNum];
            Vector3[] v2 = new Vector3[vertexNum2];
            Vector3[] n2 = new Vector3[vertexNum2];
            int[] t2 = s.T2;
            for (int i = -1; i < resolution + 1; i++)
            {
                for (int j = -1; j < resolution + 1; j++)
                {
                    int i2 = j + 1 + (i + 1) * (resolution + 2);
                    Vector3 currentPos = new Vector3(j, 0f, i) * vertexDist + offset;
                    Vector3d currentGPos = gpos + currentPos;
                    currentGPos.y = setting.Height(currentGPos, roadlist, poiList);
                    currentPos.y = (float)currentGPos.y;
                    v2[i2] = currentPos;
                    if (j >= 0 && j < resolution && i >= 0 && i < resolution)
                    {
                        int index = j + i * resolution;
                        v[index] = currentPos;
                        c[index] = setting.Biome(currentGPos, currentGPos.y);
                    }
                }
            }
            for (int k = 0; k < n2.Length; k++)
            {
                n2[k] = Vector3.zero;
            }
            for (int l = 0; l + 2 < t2.Length; l += 3)
            {
                Vector3 vertexA = v2[t2[l]];
                Vector3 vertexB = v2[t2[l + 1]];
                Vector3 vertexC = v2[t2[l + 2]];
                Vector3 triangleNormal = terrainGenScript.CalculateTriangleNormal(vertexA, vertexB, vertexC);
                n2[t2[l]] += triangleNormal;
                n2[t2[l + 1]] += triangleNormal;
                n2[t2[l + 2]] += triangleNormal;
            }
            for (int m = 0; m < n2.Length; m++)
            {
                n2[m] = n2[m].normalized;
            }
            for (int row = 1; row < resolution + 1; row++)
            {
                for (int col = 1; col < resolution + 1; col++)
                {
                    n[col - 1 + (row - 1) * resolution] = n2[col + row * (resolution + 2)];
                }
            }
            // The triangle and UV buffers are the FStart copies. The stock path
            // returns the same buffers when no hole quad touches the tile.
            return new terrainGenScript.smesh { v = v, t = s.T, u = s.U, n = n, c = c };
        }
    }

    // Main-thread scheduler for distant terrain. A generator's coordinates are
    // generated by Tile, which DoPlace calls in order. Eligible coordinates are
    // queued on the thread pool, at most two per generator at a time. Finished
    // meshes are placed with the stock PlaceMesh, at most one per generator per
    // frame, and only if the coordinate is still needed and still absent.
    public static class TerrainWorker
    {
        const int MaxInFlight = 2;
        static readonly bool enabled = Environment.GetEnvironmentVariable("TLD_MP_TERRAIN_WORKER") != "0";
        static readonly Dictionary<terrainGenScript, GenState> states = new Dictionary<terrainGenScript, GenState>();
        static int jobs, placed, fallback, dropped;
        static bool logged;
        static MethodInfo vanillaTile, vanillaPlace;
        static FieldInfo vertexDistField, offsetField, tField, uField, t2Field;

        sealed class Job
        {
            internal GenState State;
            internal Vector3d Gpos;
            internal List<roadGenScript.roadClass> Roads;
            internal List<poiGenScript.poiClass> Pois;
            internal terrainGenScript.smesh Mesh;
            internal Exception Error;
        }

        sealed class GenState
        {
            internal readonly object Sync = new object();
            // The dictionary FStart creates. A new identity means a restart.
            internal readonly object Terrains;
            // Null when this generator cannot use the worker (capture failed).
            internal TerrainTileSnapshot Snapshot;
            // Guarded by Sync: finished jobs waiting for the main thread.
            internal readonly Queue<Job> Ready = new Queue<Job>();
            // Main thread only: submitted jobs not yet taken from Ready.
            internal readonly Dictionary<Vector3d, Job> Pending = new Dictionary<Vector3d, Job>();
            // Coordinates whose worker job failed. They use the stock path.
            internal readonly HashSet<Vector3d> Forced = new HashSet<Vector3d>();
            // Guarded by Sync. Set when the state is replaced or the world is cleared.
            internal bool Retired;
            internal int PlacedFrame = -1;

            internal GenState(object terrains)
            {
                Terrains = terrains;
            }
        }

        // Drop-in replacement for the call in terrainGenScript.DoPlace. Returns the
        // same break signal as the stock Tile: true stops this DoPlace pass.
        public static bool Tile(terrainGenScript gen, Vector3d gpos, bool regen, bool async)
        {
            // Same first check as the stock Tile.
            if (!regen && gen.terrains.ContainsKey(gpos)) return false;
            if (regen || !enabled || !Eligible(gen)) return Vanilla(gen, gpos, regen, async);
            GenState state = StateFor(gen);
            if (state.Snapshot == null) return Vanilla(gen, gpos, regen, async);
            if (TryPlaceReady(gen, state)) return true;
            // In flight: the worker owns this coordinate, so DoPlace moves on.
            if (state.Pending.ContainsKey(gpos)) return false;
            if (state.Forced.Contains(gpos))
            {
                fallback++;
                return Vanilla(gen, gpos, regen, async);
            }
            // Full: stop this pass without generating synchronously.
            if (state.Pending.Count >= MaxInFlight) return true;
            // Same calls, same moment, as the start of stock GenerateTileMesh.
            List<roadGenScript.roadClass> roads = gen.roadAffectsThis ? mapSettingScript.RoadsNear(gpos) : null;
            List<poiGenScript.poiClass> pois = gen.poiAffectsThis ? mapSettingScript.POIsNear(gpos, gen.tileSize) : null;
            if (HasHole(pois, gpos, state.Snapshot))
            {
                fallback++;
                return Vanilla(gen, gpos, regen, async);
            }
            var job = new Job { State = state, Gpos = gpos, Roads = roads, Pois = pois };
            state.Pending.Add(gpos, job);
            jobs++;
            ThreadPool.QueueUserWorkItem(RunJob, job);
            return false;
        }

        // Runs the stock Tile for coordinates the worker does not own.
        static bool Vanilla(terrainGenScript gen, Vector3d gpos, bool regen, bool async)
        {
            if (vanillaTile == null)
                vanillaTile = typeof(terrainGenScript).GetMethod("Tile", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new Type[] { typeof(Vector3d), typeof(bool), typeof(bool) }, null);
            if (vanillaTile == null) throw new MissingMethodException("terrainGenScript.Tile(Vector3d,bool,bool)");
            try
            {
                return (bool)vanillaTile.Invoke(gen, new object[] { gpos, regen, async });
            }
            catch (TargetInvocationException e)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        static void InvokePlace(terrainGenScript gen, Vector3d gpos, terrainGenScript.smesh mesh)
        {
            if (vanillaPlace == null)
                vanillaPlace = typeof(terrainGenScript).GetMethod("PlaceMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new Type[] { typeof(Vector3d), typeof(terrainGenScript.smesh) }, null);
            if (vanillaPlace == null) throw new MissingMethodException("terrainGenScript.PlaceMesh(Vector3d,smesh)");
            try
            {
                vanillaPlace.Invoke(gen, new object[] { gpos, mesh });
            }
            catch (TargetInvocationException e)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            }
        }

        static bool Eligible(terrainGenScript gen)
        {
            return gen.isDistantTerrain && !gen.hasCollider && gen.mapSetting != null &&
                gen.mapSetting.imageMapLoad == null && gen.setting != null &&
                gen.terrains != null && gen.coordsNeeded != null;
        }

        // The same test as the stock hole path. A tile it would touch keeps the
        // stock synchronous generation.
        static bool HasHole(List<poiGenScript.poiClass> pois, Vector3d gpos, TerrainTileSnapshot snapshot)
        {
            if (pois == null) return false;
            double range = (double)snapshot.TileSize * 0.5 + (double)(snapshot.VertexDist * 2f);
            for (int i = 0; i < pois.Count; i++)
            {
                if (pois[i].terrainHoleQuads == null) continue;
                foreach (var quad in pois[i].terrainHoleQuads)
                {
                    if (quad.InsideTile(gpos, range)) return true;
                }
            }
            return false;
        }

        static GenState StateFor(terrainGenScript gen)
        {
            GenState state;
            if (states.TryGetValue(gen, out state) && ReferenceEquals(state.Terrains, gen.terrains)) return state;
            if (state != null) Retire(state);
            Prune();
            state = new GenState(gen.terrains);
            try
            {
                state.Snapshot = Capture(gen);
            }
            catch (Exception e)
            {
                Log("setup failed, using the stock path: " + e.GetType().Name + ": " + e.Message);
            }
            states[gen] = state;
            return state;
        }

        // Builds the snapshot from the FStart buffers. Runs on the main thread.
        internal static TerrainTileSnapshot Capture(terrainGenScript gen)
        {
            if (vertexDistField == null)
            {
                vertexDistField = Field("vertexDist");
                offsetField = Field("offset");
                tField = Field("t");
                uField = Field("u");
                t2Field = Field("t2");
            }
            float vertexDist = (float)vertexDistField.GetValue(gen);
            Vector3 offset = (Vector3)offsetField.GetValue(gen);
            int[] t = (int[])tField.GetValue(gen);
            Vector2[] u = (Vector2[])uField.GetValue(gen);
            int[] t2 = (int[])t2Field.GetValue(gen);
            // NoiseS3D builds its permutation table lazily. Build it here, on the main thread.
            NoiseS3D.Noise(0.0, 0.0, 0.0);
            return new TerrainTileSnapshot(gen.resolution, vertexDist, offset, gen.tileSize, gen.setting, t, u, t2);
        }

        static FieldInfo Field(string name)
        {
            var field = typeof(terrainGenScript).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) throw new MissingFieldException("terrainGenScript." + name);
            return field;
        }

        static void Prune()
        {
            List<terrainGenScript> gone = null;
            foreach (var pair in states)
            {
                if (pair.Key == null)
                {
                    if (gone == null) gone = new List<terrainGenScript>();
                    gone.Add(pair.Key);
                }
            }
            if (gone == null) return;
            for (int i = 0; i < gone.Count; i++)
            {
                Retire(states[gone[i]]);
                states.Remove(gone[i]);
            }
        }

        static void Retire(GenState state)
        {
            dropped += state.Pending.Count;
            state.Pending.Clear();
            state.Forced.Clear();
            lock (state.Sync)
            {
                state.Retired = true;
                state.Ready.Clear();
            }
        }

        // Places one finished mesh for this generator this frame. Jobs that are
        // no longer needed, or already present, are dropped and counted.
        static bool TryPlaceReady(terrainGenScript gen, GenState state)
        {
            int frame = Time.frameCount;
            if (state.PlacedFrame == frame) return false;
            while (true)
            {
                Job job;
                lock (state.Sync)
                {
                    if (state.Ready.Count == 0) return false;
                    job = state.Ready.Dequeue();
                }
                state.Pending.Remove(job.Gpos);
                if (job.Error != null)
                {
                    state.Forced.Add(job.Gpos);
                    dropped++;
                    Log("worker job failed, using the stock path: " + job.Error.GetType().Name + ": " + job.Error.Message);
                    continue;
                }
                if (!gen.coordsNeeded.Contains(job.Gpos) || gen.terrains.ContainsKey(job.Gpos))
                {
                    dropped++;
                    continue;
                }
                InvokePlace(gen, job.Gpos, job.Mesh);
                placed++;
                state.PlacedFrame = frame;
                return true;
            }
        }

        // Thread-pool body. Touches no Unity object and no mutable generator state.
        static void RunJob(object item)
        {
            var job = (Job)item;
            bool retired;
            lock (job.State.Sync) retired = job.State.Retired;
            if (!retired)
            {
                try
                {
                    job.Mesh = TerrainTileMesh.Generate(job.State.Snapshot, job.Gpos, job.Roads, job.Pois);
                }
                catch (Exception e)
                {
                    job.Error = e;
                }
            }
            lock (job.State.Sync)
            {
                if (!job.State.Retired) job.State.Ready.Enqueue(job);
            }
        }

        // World unload or reset: drop every queued and in-flight job.
        public static void Clear()
        {
            foreach (var state in states.Values) Retire(state);
            states.Clear();
        }

        static void Log(string message)
        {
            if (logged) return;
            logged = true;
            UnityEngine.Debug.Log("[TLD MP terrain worker] " + message);
        }

        // Log fragment for the camera diagnostics line, then resets the window counts.
        public static string TakeSummary()
        {
            string text = "terrainWorker{jobs=" + jobs.ToString(CultureInfo.InvariantCulture) +
                " placed=" + placed.ToString(CultureInfo.InvariantCulture) +
                " fallback=" + fallback.ToString(CultureInfo.InvariantCulture) +
                " dropped=" + dropped.ToString(CultureInfo.InvariantCulture) + "}";
            jobs = 0;
            placed = 0;
            fallback = 0;
            dropped = 0;
            return text;
        }
    }
}
