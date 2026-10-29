using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace TldMp
{
    // Bounded, opt-in census for general-FPS attribution. It does not touch
    // renderers, materials or cameras; it only reads their state and logs one
    // bounded line every 30 seconds. Run it with TLD_MP_DIAGNOSTICS=1.
    internal static class RendererCensus
    {
        // Two snapshots only: one while the 18 km world is settled and one after
        // the 5 km switch. This avoids a repeated census spike in the p50/p99.
        const float SecondCensusAt = 120f;
        const float FirstCensusAt = 60f;
        const int MaxGroups = 18;
        static int runs;
        static readonly Dictionary<string, Group> groups = new Dictionary<string, Group>(256);
        static readonly List<Group> ordered = new List<Group>(256);
        static readonly HashSet<int> meshIds = new HashSet<int>();
        static readonly HashSet<int> materialIds = new HashSet<int>();
        static readonly HashSet<int> shaderIds = new HashSet<int>();

        sealed class Group
        {
            internal string Type, Mesh, Material, Shader;
            internal int Count, Visible, Submeshes, Draws, ShadowDraws, Queue;
            internal bool Static, Skinned, PropertyBlock, Instancing;
        }

        internal static void Tick()
        {
            if (!FrameDiagnostics.Enabled || runs >= 2) return;
            float now = Time.realtimeSinceStartup;
            if (now < (runs == 0 ? FirstCensusAt : SecondCensusAt)) return;
            runs++;
            try { Run(now); }
            catch (Exception error) { UnityEngine.Debug.Log("[TLD MP render] census failed: " + error.GetType().Name + ": " + error.Message); }
        }

        static string Key(Renderer renderer, Mesh mesh, Material[] materials, int submeshes, bool propertyBlock)
        {
            var text = new StringBuilder(96);
            text.Append(renderer.GetType().Name).Append('|').Append(mesh == null ? 0 : mesh.GetInstanceID()).Append('|');
            if (materials != null)
            {
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null) { text.Append("null|"); continue; }
                    string[] keywords = material.shaderKeywords;
                    text.Append(material.GetInstanceID()).Append('~').Append(material.renderQueue).Append('~')
                        .Append(material.enableInstancing ? 1 : 0).Append('~').Append(material.shader == null ? "" : material.shader.name).Append('~')
                        .Append(keywords == null ? "" : string.Join(",", keywords)).Append('|');
                }
            }
            text.Append((int)renderer.shadowCastingMode).Append('|').Append(renderer.receiveShadows ? 1 : 0)
                .Append('|').Append((int)renderer.lightProbeUsage).Append('|').Append((int)renderer.reflectionProbeUsage)
                .Append('|').Append(renderer.isPartOfStaticBatch ? 1 : 0).Append('|').Append(propertyBlock ? 1 : 0)
                .Append('|').Append(submeshes);
            return text.ToString();
        }

        static void Run(float now)
        {
            groups.Clear(); ordered.Clear(); meshIds.Clear(); materialIds.Clear(); shaderIds.Clear();
            Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            int total = 0, visible = 0, staticBatched = 0, skinned = 0, propertyBlocked = 0;
            long estimatedDraws = 0, estimatedShadowDraws = 0;
            bool shadowsEnabled = QualitySettings.shadows != ShadowQuality.Disable;
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                total++;
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer) mesh = ((SkinnedMeshRenderer)renderer).sharedMesh;
                else if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) mesh = filter.sharedMesh;
                }
                else if (renderer is SpriteRenderer)
                {
                    Sprite sprite = ((SpriteRenderer)renderer).sprite;
                    if (sprite != null) meshIds.Add(sprite.GetInstanceID());
                }
                Material[] materials = renderer.sharedMaterials;
                int submeshes = mesh == null ? 1 : Math.Max(1, mesh.subMeshCount);
                bool hasBlock = renderer.HasPropertyBlock();
                string key = Key(renderer, mesh, materials, submeshes, hasBlock);
                Group group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new Group();
                    group.Type = renderer.GetType().Name;
                    group.Mesh = mesh == null ? (renderer is SpriteRenderer && ((SpriteRenderer)renderer).sprite != null ? "sprite:" + ((SpriteRenderer)renderer).sprite.name : "<none>") : (string.IsNullOrEmpty(mesh.name) ? "mesh#" + mesh.GetInstanceID() : mesh.name);
                    group.Material = materials == null || materials.Length == 0 || materials[0] == null ? "<none>" : (string.IsNullOrEmpty(materials[0].name) ? "material#" + materials[0].GetInstanceID() : materials[0].name) + (materials.Length > 1 ? "+" + (materials.Length - 1) : "");
                    group.Shader = materials == null || materials.Length == 0 || materials[0] == null || materials[0].shader == null ? "<none>" : materials[0].shader.name;
                    group.Queue = materials == null || materials.Length == 0 || materials[0] == null ? 0 : materials[0].renderQueue;
                    group.Submeshes = submeshes;
                    group.Static = renderer.isPartOfStaticBatch;
                    group.Skinned = renderer is SkinnedMeshRenderer;
                    group.PropertyBlock = hasBlock;
                    group.Instancing = materials != null && materials.Length > 0 && materials[0] != null && materials[0].enableInstancing;
                    groups.Add(key, group);
                    ordered.Add(group);
                }
                group.Count++;
                group.Draws += submeshes;
                if (shadowsEnabled && renderer.shadowCastingMode != ShadowCastingMode.Off) group.ShadowDraws += submeshes;
                if (renderer.isVisible) { group.Visible++; visible++; }
                if (renderer.isPartOfStaticBatch) staticBatched++;
                if (renderer is SkinnedMeshRenderer) skinned++;
                if (hasBlock) propertyBlocked++;
                estimatedDraws += submeshes;
                if (shadowsEnabled && renderer.shadowCastingMode != ShadowCastingMode.Off) estimatedShadowDraws += submeshes;
                if (mesh != null) meshIds.Add(mesh.GetInstanceID());
                if (materials != null) for (int m = 0; m < materials.Length; m++)
                {
                    Material material = materials[m];
                    if (material == null) continue;
                    materialIds.Add(material.GetInstanceID());
                    if (material.shader != null) shaderIds.Add(material.shader.GetInstanceID());
                }
            }
            ordered.Sort(delegate(Group a, Group b) { return b.Draws.CompareTo(a.Draws); });
            var text = new StringBuilder(2048);
            text.Append("[TLD MP render] t=").Append(now.ToString("F3", CultureInfo.InvariantCulture))
                .Append(" census renderers=").Append(total)
                .Append(" visible=").Append(visible)
                .Append(" meshes=").Append(meshIds.Count)
                .Append(" materials=").Append(materialIds.Count)
                .Append(" shaders=").Append(shaderIds.Count)
                .Append(" staticBatched=").Append(staticBatched)
                .Append(" skinned=").Append(skinned)
                .Append(" propBlock=").Append(propertyBlocked)
                .Append(" estDraws=").Append(estimatedDraws)
                .Append(" estShadowDraws=").Append(estimatedShadowDraws)
                .Append(" groups=").Append(ordered.Count);
            for (int i = 0; i < ordered.Count && i < MaxGroups; i++)
            {
                Group g = ordered[i];
                text.Append(" #").Append(i + 1)
                    .Append("{n=").Append(g.Count)
                    .Append(";vis=").Append(g.Visible)
                    .Append(";draws=").Append(g.Draws)
                    .Append(";shadow=").Append(g.ShadowDraws)
                    .Append(";type=").Append(g.Type)
                    .Append(";mesh=").Append(g.Mesh)
                    .Append(";mat=").Append(g.Material)
                    .Append(";shader=").Append(g.Shader)
                    .Append(";sub=").Append(g.Submeshes)
                    .Append(";q=").Append(g.Queue)
                    .Append(";static=").Append(g.Static ? 1 : 0)
                    .Append(";skinned=").Append(g.Skinned ? 1 : 0)
                    .Append(";propBlock=").Append(g.PropertyBlock ? 1 : 0)
                    .Append(";instancing=").Append(g.Instancing ? 1 : 0)
                    .Append('}');
            }
            Camera[] cameras = Camera.allCameras;
            text.Append(" cameras=").Append(cameras == null ? 0 : cameras.Length);
            if (cameras != null) for (int i = 0; i < cameras.Length && i < 12; i++)
            {
                Camera camera = cameras[i];
                if (camera == null) continue;
                RenderTexture target = camera.targetTexture;
                text.Append(" cam").Append(i).Append("{name=").Append(camera.name)
                    .Append(";target=").Append(target == null ? "screen" : target.name + ":" + target.width + "x" + target.height)
                    .Append(";far=").Append(camera.farClipPlane.ToString("F1", CultureInfo.InvariantCulture))
                    .Append(";depth=").Append(camera.depth.ToString("F1", CultureInfo.InvariantCulture))
                    .Append(";path=").Append(camera.renderingPath)
                    .Append(";hdr=").Append(camera.allowHDR ? 1 : 0)
                    .Append(";msaa=").Append(camera.allowMSAA ? 1 : 0)
                    .Append(";cull=").Append(camera.cullingMask)
                    .Append('}');
            }
            int mirrors = mainscript.s == null || mainscript.s.Mirrors == null ? 0 : mainscript.s.Mirrors.Count;
            int mirrorCams = 0;
            if (mainscript.s != null && mainscript.s.Mirrors != null)
                for (int i = 0; i < mainscript.s.Mirrors.Count; i++)
                {
                    mainscript.mirror mirror = mainscript.s.Mirrors[i];
                    if (mirror != null && mirror.C != null && mirror.C.enabled) mirrorCams++;
                }
            int planes = reflectionMainScript.s == null || reflectionMainScript.s.reflectionPlanes == null ? 0 : reflectionMainScript.s.reflectionPlanes.Count;
            text.Append(" mirrors=").Append(mirrors).Append(" mirrorCams=").Append(mirrorCams).Append(" reflectionPlanes=").Append(planes)
                .Append(" quality=").Append(QualitySettings.names.Length == 0 ? "?" : QualitySettings.names[QualitySettings.GetQualityLevel()])
                .Append(" shadows=").Append(QualitySettings.shadows)
                .Append(" shadowDist=").Append(QualitySettings.shadowDistance.ToString("F1", CultureInfo.InvariantCulture))
                .Append(" cascades=").Append(QualitySettings.shadowCascades)
                .Append(" msaa=").Append(QualitySettings.antiAliasing)
                .Append(" realtimeProbes=").Append(QualitySettings.realtimeReflectionProbes ? 1 : 0)
                .Append(" pixelLights=").Append(QualitySettings.pixelLightCount)
                .Append(" lodBias=").Append(QualitySettings.lodBias.ToString("F2", CultureInfo.InvariantCulture))
                .Append(" vSync=").Append(QualitySettings.vSyncCount)
                .Append(" targetFps=").Append(Application.targetFrameRate)
                .Append(" screen=").Append(Screen.width).Append('x').Append(Screen.height)
                .Append(" pipeline=").Append(GraphicsSettings.renderPipelineAsset == null ? "builtin" : GraphicsSettings.renderPipelineAsset.GetType().Name)
                .Append(" gpu=").Append(SystemInfo.graphicsDeviceName)
                .Append(" vramMB=").Append(SystemInfo.graphicsMemorySize)
                .Append(" cpus=").Append(SystemInfo.processorCount);
            UnityEngine.Debug.Log(text.ToString());
            RoadCensus(now);
        }

        // Read-only road breakdown at the same two snapshots. Two passes so the
        // distance classification and the element bookkeeping can be timed
        // separately without per-cell timestamps. No placement, destruction or
        // cache is performed here.
        static void RoadCensus(float now)
        {
            if (menuhandler.s == null || menuhandler.s.currentMainMap == null) return;
            mapSettingScript map = menuhandler.s.currentMainMap;
            if (map.roadGens == null) return;
            long needed = 0, existing = 0, inner = 0, nearCells = 0, farCells = 0;
            long refs = 0, uniqueRefs = 0, dupRefs = 0, live = 0, nearRefs = 0, farRefs = 0, maxList = 0;
            var unique = new HashSet<roadGenScript.roadElementClass>();
            var nearFlags = new Dictionary<roadGenScript.innerChunkClass, bool>();
            var origins = new List<Vector3d>(4);
            long distanceTicks = 0, elementTicks = 0;
            const int MaxWalk = 200000;
            long walkedA = 0, walkedB = 0;
            for (int g = 0; g < map.roadGens.Count && walkedA < MaxWalk; g++)
            {
                roadGenScript road = map.roadGens[g];
                if (road == null) continue;
                needed += road.chunksNeeded == null ? 0 : road.chunksNeeded.Count;
                origins.Clear();
                if (road.mapSetting != null && road.mapSetting.genArounds != null)
                    for (int a = 0; a < road.mapSetting.genArounds.Count; a++)
                    {
                        mapSettingScript.genAround around = road.mapSetting.genArounds[a];
                        if (around != null) origins.Add(mainscript.GlobalFromUnityPos(around.upos));
                    }
                if (road.chunksNeeded == null || road.chunks == null) continue;

                long start = Stopwatch.GetTimestamp();
                for (int c = 0; c < road.chunksNeeded.Count && walkedA < MaxWalk; c++)
                {
                    roadGenScript.dotChunkClass chunk;
                    if (!road.chunks.TryGetValue(road.chunksNeeded[c], out chunk) || chunk == null || chunk.innerChunks == null) continue;
                    existing++;
                    foreach (KeyValuePair<Vector3d, roadGenScript.innerChunkClass> pair in chunk.innerChunks)
                    {
                        if (walkedA++ >= MaxWalk) break;
                        inner++;
                        bool isNear = false;
                        for (int o = 0; o < origins.Count; o++)
                        {
                            Vector3d delta = pair.Key - origins[o];
                            delta.y = 0.0;
                            if (delta.magnitude < (double)road.roadPlaceDist) { isNear = true; break; }
                        }
                        if (isNear) nearCells++; else farCells++;
                        if (pair.Value != null) nearFlags[pair.Value] = isNear;
                    }
                }
                distanceTicks += Stopwatch.GetTimestamp() - start;

                start = Stopwatch.GetTimestamp();
                for (int c = 0; c < road.chunksNeeded.Count && walkedB < MaxWalk; c++)
                {
                    roadGenScript.dotChunkClass chunk;
                    if (!road.chunks.TryGetValue(road.chunksNeeded[c], out chunk) || chunk == null || chunk.innerChunks == null) continue;
                    foreach (KeyValuePair<Vector3d, roadGenScript.innerChunkClass> pair in chunk.innerChunks)
                    {
                        if (walkedB++ >= MaxWalk) break;
                        roadGenScript.innerChunkClass cell = pair.Value;
                        if (cell == null || cell.roads == null) continue;
                        bool isNear = nearFlags.ContainsKey(cell) && nearFlags[cell];
                        for (int r = 0; r < cell.roads.Count; r++)
                        {
                            roadGenScript.roadChunk roadChunk = cell.roads[r];
                            if (roadChunk == null || roadChunk.roadElements == null) continue;
                            if (roadChunk.roadElements.Count > maxList) maxList = roadChunk.roadElements.Count;
                            refs += roadChunk.roadElements.Count;
                            if (roadChunk.roadId < 0 || roadChunk.roadId >= chunk.roads.Count) continue;
                            List<roadGenScript.roadElementClass> elements = chunk.roads[roadChunk.roadId].roadElements;
                            for (int e = 0; e < roadChunk.roadElements.Count; e++)
                            {
                                int index = roadChunk.roadElements[e];
                                if (elements == null || index < 0 || index >= elements.Count) continue;
                                roadGenScript.roadElementClass element = elements[index];
                                if (element == null) continue;
                                if (unique.Add(element)) uniqueRefs++; else dupRefs++;
                                if (isNear) nearRefs++; else farRefs++;
                                if (element.obj != null) live++;
                            }
                        }
                    }
                }
                elementTicks += Stopwatch.GetTimestamp() - start;
            }
            double perMs = 1000.0 / Stopwatch.Frequency;
            UnityEngine.Debug.Log("[TLD MP roads] t=" + now.ToString("F3", CultureInfo.InvariantCulture) +
                " gens=" + map.roadGens.Count +
                " needed=" + needed + " existing=" + existing + " inner=" + inner +
                " nearCells=" + nearCells + " farCells=" + farCells +
                " refs=" + refs + " uniqueRefs=" + uniqueRefs + " dupRefs=" + dupRefs +
                " nearRefs=" + nearRefs + " farRefs=" + farRefs +
                " live=" + live + " maxList=" + maxList +
                " distanceMs=" + (distanceTicks * perMs).ToString("F3", CultureInfo.InvariantCulture) +
                " elementMs=" + (elementTicks * perMs).ToString("F3", CultureInfo.InvariantCulture) +
                " walkedDistance=" + walkedA + " walkedElements=" + walkedB +
                ((walkedA >= MaxWalk || walkedB >= MaxWalk) ? " capped=1" : " capped=0") +
                " " + RoadFarSkip.Summary());
        }
    }
}
