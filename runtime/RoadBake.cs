using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // A placed road is a SkinnedMeshRenderer whose bones never move again.
    // roadscript.Place ends with BakeCollider, which bakes that exact pose into
    // roadscript.bakedMesh. The renderer is then skinned again every frame it is
    // visible. Draw the baked mesh with a plain MeshRenderer at the renderer's own
    // transform instead, with the same materials and render settings, and disable
    // the skinned one.
    //
    // BakeMesh's relation to a scaled transform is not assumed. For every road a
    // few vertices are skinned independently from the ORIGINAL mesh and compared
    // with the world position each of two candidate child scales would draw
    // (identity and 1/lossyScale). A road is baked only when one candidate matches
    // the true skinned positions and is clearly better than the other. Anything
    // else leaves the native road enabled. Disable with TLD_MP_ROAD_BAKE=0.
    internal static class RoadBake
    {
        internal const string ChildName = "TldBakedRoad";
        internal const float ScaleTolerance = 1e-4f;
        internal const float MaxWorldError = 0.05f;
        internal const float RelativeError = 0.002f;
        internal const int MaxSamples = 8;
        internal static long Baked, Skipped, IdentityWins, InverseWins;
        static readonly Dictionary<string, long> reasons = new Dictionary<string, long>();
        static int featureState;
        static int logged;

        internal static bool FeatureEnabled
        {
            get
            {
                if (featureState == 0) featureState = Environment.GetEnvironmentVariable("TLD_MP_ROAD_BAKE") == "0" ? 2 : 1;
                return featureState == 1;
            }
        }

        // Unity-free eligibility; null means bake, otherwise the reason not to.
        internal static string Eligibility(bool feature, bool hasRenderer, bool rendererEnabled, bool hasMesh,
            int submeshes, int materials, Vector3 lossyScale, bool hasLodGroup, bool alreadyBaked)
        {
            if (!feature) return "disabled";
            if (!hasRenderer) return "no-renderer";
            if (!hasMesh) return "no-mesh";
            if (alreadyBaked) return "already-baked";
            if (!rendererEnabled) return "renderer-disabled";
            if (hasLodGroup) return "lod-group";
            if (submeshes <= 0 || materials != submeshes) return "material-count";
            if (!PositiveScale(lossyScale)) return "scale";
            return null;
        }

        internal static bool UnitScale(Vector3 s)
        {
            return Near(s.x) && Near(s.y) && Near(s.z);
        }

        // Finite and strictly positive on every axis (rejects NaN, zero and mirrored scale).
        internal static bool PositiveScale(Vector3 s)
        {
            return Positive(s.x) && Positive(s.y) && Positive(s.z);
        }

        // Child scale that cancels a baked lossy scale: lossy * child == 1 on each axis.
        internal static Vector3 InverseScale(Vector3 s)
        {
            return new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
        }

        // Maximum accepted error for a mesh of this bounds diagonal. Both limits
        // from the contract apply: relative to the diagonal and an absolute cap.
        internal static float Tolerance(float boundsDiagonal)
        {
            float relative = RelativeError * Math.Max(1f, boundsDiagonal);
            return Math.Min(MaxWorldError, relative);
        }

        // 0 = reject, 1 = identity child scale, 2 = inverse child scale.
        // identity/inverse/truth are world positions for the same mesh vertices.
        internal static int ChooseScale(Vector3[] identityWorld, Vector3[] inverseWorld, Vector3[] truth,
            float boundsDiagonal, bool unitScale, out float errorIdentity, out float errorInverse)
        {
            errorIdentity = MaxError(identityWorld, truth);
            errorInverse = MaxError(inverseWorld, truth);
            if (float.IsNaN(errorIdentity) || float.IsInfinity(errorIdentity) ||
                float.IsNaN(errorInverse) || float.IsInfinity(errorInverse))
            {
                errorIdentity = float.PositiveInfinity;
                errorInverse = float.PositiveInfinity;
                return 0;
            }
            float limit = Tolerance(boundsDiagonal);
            bool identityOk = errorIdentity <= limit;
            bool inverseOk = errorInverse <= limit;
            if (unitScale) return identityOk ? 1 : 0;
            if (identityOk && (!inverseOk || errorIdentity <= errorInverse * 0.5f)) return 1;
            if (inverseOk && (!identityOk || errorInverse <= errorIdentity * 0.5f)) return 2;
            return 0;
        }

        static float MaxError(Vector3[] candidate, Vector3[] truth)
        {
            if (candidate == null || truth == null || candidate.Length == 0 || candidate.Length != truth.Length)
                return float.PositiveInfinity;
            float worst = 0f;
            for (int i = 0; i < candidate.Length; i++)
            {
                float error = (candidate[i] - truth[i]).magnitude;
                if (float.IsNaN(error) || float.IsInfinity(error)) return float.PositiveInfinity;
                if (error > worst) worst = error;
            }
            return worst;
        }

        static bool Positive(float v) { return !float.IsNaN(v) && !float.IsInfinity(v) && v > 0f; }

        static bool Near(float v) { return !float.IsNaN(v) && Math.Abs(v - 1f) <= ScaleTolerance; }

        internal static void AfterBake(roadscript road)
        {
            GameObject child = null;
            try
            {
                SkinnedMeshRenderer skinned = road == null ? null : road.skinnedMeshRenderer;
                Mesh mesh = road == null ? null : road.bakedMesh;
                Material[] materials = skinned == null ? null : skinned.sharedMaterials;
                bool lod = skinned != null && (skinned.GetComponentInParent<LODGroup>() != null || skinned.GetComponentInChildren<LODGroup>() != null);
                string reason = Eligibility(FeatureEnabled, skinned != null, skinned != null && skinned.enabled, mesh != null,
                    mesh == null ? 0 : mesh.subMeshCount, materials == null ? 0 : materials.Length,
                    skinned == null ? Vector3.one : skinned.transform.lossyScale, lod,
                    skinned != null && skinned.transform.Find(ChildName) != null);
                if (reason != null) { Skip(reason); return; }

                Vector3 lossy = skinned.transform.lossyScale;
                Vector3 inverse = InverseScale(lossy);
                bool unit = UnitScale(lossy);

                Vector3[] bakedAll = mesh.vertices;
                Vector3[] identityWorld, inverseWorld, truth;
                if (!Sample(skinned, mesh, bakedAll, inverse, out identityWorld, out inverseWorld, out truth)) { Skip("skin-data"); return; }

                float diagonal = mesh.bounds.size.magnitude;
                float errIdentity, errInverse;
                int winner = ChooseScale(identityWorld, inverseWorld, truth, diagonal, unit, out errIdentity, out errInverse);
                if (logged < 3)
                {
                    logged++;
                    Debug.Log("[TLD MP roadbake] lossy=" + F3(lossy) + " errIdentity=" + F4(errIdentity) +
                        " errInverse=" + F4(errInverse) + " winner=" + (winner == 1 ? "identity" : winner == 2 ? "inverse" : "rejected"));
                }
                if (winner == 0) { Skip("skin-mismatch"); return; }

                if (winner == 1) IdentityWins++; else InverseWins++;
                child = new GameObject(ChildName);
                child.layer = skinned.gameObject.layer;
                Transform t = child.transform;
                t.SetParent(skinned.transform, false);
                t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity;
                t.localScale = winner == 1 ? Vector3.one : inverse;
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = skinned.shadowCastingMode;
                renderer.receiveShadows = skinned.receiveShadows;
                renderer.lightProbeUsage = skinned.lightProbeUsage;
                renderer.reflectionProbeUsage = skinned.reflectionProbeUsage;
                renderer.probeAnchor = skinned.probeAnchor;
                renderer.lightProbeProxyVolumeOverride = skinned.lightProbeProxyVolumeOverride;
                renderer.motionVectorGenerationMode = skinned.motionVectorGenerationMode;
                renderer.allowOcclusionWhenDynamic = skinned.allowOcclusionWhenDynamic;
                renderer.renderingLayerMask = skinned.renderingLayerMask;
                renderer.sortingLayerID = skinned.sortingLayerID;
                renderer.sortingOrder = skinned.sortingOrder;
                if (skinned.HasPropertyBlock())
                {
                    var block = new MaterialPropertyBlock();
                    skinned.GetPropertyBlock(block);
                    renderer.SetPropertyBlock(block);
                }
                // Last step: only now stop drawing the skinned copy.
                skinned.enabled = false;
                child = null;
                Baked++;
            }
            catch (Exception error)
            {
                if (child != null) UnityEngine.Object.Destroy(child);
                Skip("error:" + error.GetType().Name);
            }
        }

        // Ground truth for up to MaxSamples vertices from linear blend skinning of
        // the original mesh, plus the world position both child scales would draw
        // from the baked mesh. False when the mesh cannot be skinned or its vertex
        // layout differs from the bake.
        static bool Sample(SkinnedMeshRenderer skinned, Mesh baked, Vector3[] bakedAll, Vector3 inverse,
            out Vector3[] identityWorld, out Vector3[] inverseWorld, out Vector3[] truth)
        {
            identityWorld = null; inverseWorld = null; truth = null;
            Mesh original = skinned.sharedMesh;
            Transform[] bones = skinned.bones;
            if (original == null || bones == null || bakedAll == null) return false;
            Vector3[] originalVertices = original.vertices;
            BoneWeight[] weights = original.boneWeights;
            Matrix4x4[] bindposes = original.bindposes;
            if (weights == null || bindposes == null || bindposes.Length == 0 ||
                weights.Length < bakedAll.Length || originalVertices.Length < bakedAll.Length ||
                bones.Length < bindposes.Length) return false;

            int count = Math.Min(MaxSamples, bakedAll.Length);
            if (count <= 0) return false;
            identityWorld = new Vector3[count];
            inverseWorld = new Vector3[count];
            truth = new Vector3[count];
            for (int s = 0; s < count; s++)
            {
                int index = count == 1 ? 0 : (int)((long)s * (bakedAll.Length - 1) / (count - 1));
                Vector3 world = Skin(originalVertices[index], weights[index], bones, bindposes);
                if (float.IsNaN(world.x) || float.IsNaN(world.y) || float.IsNaN(world.z)) return false;
                truth[s] = world;
                identityWorld[s] = skinned.transform.TransformPoint(bakedAll[index]);
                inverseWorld[s] = skinned.transform.TransformPoint(Vector3.Scale(inverse, bakedAll[index]));
            }
            return true;
        }

        static Vector3 Skin(Vector3 vertex, BoneWeight weight, Transform[] bones, Matrix4x4[] bindposes)
        {
            Vector3 result = Vector3.zero;
            float total = 0f;
            Add(ref result, ref total, vertex, weight.boneIndex0, weight.weight0, bones, bindposes);
            Add(ref result, ref total, vertex, weight.boneIndex1, weight.weight1, bones, bindposes);
            Add(ref result, ref total, vertex, weight.boneIndex2, weight.weight2, bones, bindposes);
            Add(ref result, ref total, vertex, weight.boneIndex3, weight.weight3, bones, bindposes);
            if (total > 0f) result /= total;
            return result;
        }

        static void Add(ref Vector3 result, ref float total, Vector3 vertex, int index, float weight,
            Transform[] bones, Matrix4x4[] bindposes)
        {
            if (weight <= 0f || index < 0 || index >= bones.Length || index >= bindposes.Length || bones[index] == null) return;
            result += weight * (bones[index].localToWorldMatrix * bindposes[index]).MultiplyPoint3x4(vertex);
            total += weight;
        }

        static void Skip(string reason)
        {
            Skipped++;
            long count;
            reasons.TryGetValue(reason, out count);
            if (reasons.Count < 16 || count > 0) reasons[reason] = count + 1;
        }

        internal static string Summary()
        {
            var text = new System.Text.StringBuilder("roadBake{baked=").Append(Baked).Append(" skipped=").Append(Skipped)
                .Append(" identity=").Append(IdentityWins).Append(" inverse=").Append(InverseWins);
            foreach (var pair in reasons) text.Append(' ').Append(pair.Key).Append('=').Append(pair.Value);
            return text.Append('}').ToString();
        }

        static string F3(Vector3 value)
        {
            return "(" + value.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "," +
                value.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "," +
                value.z.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        static string F4(float value)
        {
            return value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
