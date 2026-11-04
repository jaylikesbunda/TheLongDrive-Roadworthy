using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Visual only: the biome classifier, world positions, saves and networking
    // still use the exact original biome values. Per-tile tessellation settings
    // taper the stock simple-grass shader without modifying its shared material.
    public sealed class GrassMeshPlan
    {
        public Mesh source;
        public Vector3d origin;
        public byte[] buckets;
        public readonly int[] groupCounts=new int[9];
        public bool full,any;
        public int triangles;
    }

    public static class GrassTransition
    {
        static readonly int MaxDensityId=Shader.PropertyToID("_MaxTessellation");
        static readonly int FalloffId=Shader.PropertyToID("_EdgeLength");
        static MaterialPropertyBlock block;
        const uint NoiseSalt=0x9E3779B9u,DitherSalt=0x85EBCA6Bu;
        static float Clamp(float v){return float.IsNaN(v)||float.IsInfinity(v)?0f:Mathf.Clamp01(v);}
        public static float Density(Color c)
        {if(float.IsNaN(c.r)||float.IsNaN(c.g)||float.IsNaN(c.b)||float.IsNaN(c.a)||float.IsInfinity(c.r)||float.IsInfinity(c.g)||float.IsInfinity(c.b)||float.IsInfinity(c.a))return 0f;return (1f-Clamp(c.r))*(1f-Clamp(c.b))*Clamp(c.g)*Clamp(c.a);}
        static bool NativeGrass(Color c){return c.r==0f && c.g==1f && c.b==0f && c.a==1f;}
        static bool Supported(newTerrainTileScript tile)
        {return tile!=null && tile.grassMaterial!=null && tile.grassMaterial.HasProperty(MaxDensityId) && tile.grassMaterial.HasProperty(FalloffId) && tile.grassMaterial.IsKeywordEnabled("SIMPLE_GRASS") && tile.grassMaterial.IsKeywordEnabled("UNIFORM_DENSITY");}

        static uint Hash(int x,int z,uint salt)
        {
            unchecked
            {
                uint h=2166136261u^salt;
                h=(h^(uint)x)*16777619u;
                h=(h^(uint)z)*16777619u;
                h^=h>>13;h*=0x5bd1e995u;h^=h>>15;
                return h;
            }
        }
        static float HashFloat(int x,int z,uint salt){return (Hash(x,z,salt)&0xFFFFFF)/16777216f;}
        static float Smooth(float t){return t*t*(3f-2f*t);}
        static float SmoothNoise(double x,double z)
        {
            int ix=(int)System.Math.Floor(x/8.0),iz=(int)System.Math.Floor(z/8.0);
            float fx=Smooth((float)((x-ix*8.0)/8.0)),fz=Smooth((float)((z-iz*8.0)/8.0));
            float a=HashFloat(ix,iz,NoiseSalt),b=HashFloat(ix+1,iz,NoiseSalt),c=HashFloat(ix,iz+1,NoiseSalt),d=HashFloat(ix+1,iz+1,NoiseSalt);
            return a+(b-a)*fx+(c-a)*fz+(a-b-c+d)*fx*fz;
        }
        static bool Finite(double v){return !double.IsNaN(v)&&!double.IsInfinity(v);}
        public static bool TryBuild(Vector3[] vertices,Color[] colors,int[] triangles,Vector3d globalOrigin,out GrassMeshPlan plan)
        {
            plan=null;
            if(vertices==null||colors==null||triangles==null||colors.Length!=vertices.Length)return false;
            if(vertices.Length>65536||triangles.Length>393216||triangles.Length==0||triangles.Length%3!=0)return false;
            if(!Finite(globalOrigin.x)||!Finite(globalOrigin.y)||!Finite(globalOrigin.z))return false;
            for(int i=0;i<vertices.Length;i++)
            {
                var v=vertices[i];
                if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z))return false;
                var c=colors[i];
                if(float.IsNaN(c.r)||float.IsNaN(c.g)||float.IsNaN(c.b)||float.IsNaN(c.a)||float.IsInfinity(c.r)||float.IsInfinity(c.g)||float.IsInfinity(c.b)||float.IsInfinity(c.a))return false;
            }
            for(int i=0;i<triangles.Length;i++)if(triangles[i]<0||triangles[i]>=vertices.Length)return false;
            var p=new GrassMeshPlan();
            p.origin=globalOrigin;p.triangles=triangles.Length/3;p.buckets=new byte[p.triangles];
            for(int i=0;i<p.triangles;i++)
            {
                int a=triangles[i*3],b=triangles[i*3+1],c=triangles[i*3+2];
                double w=(Density(colors[a])+Density(colors[b])+Density(colors[c]))/3.0;
                byte bucket;
                if(w>=1.0)bucket=8;
                else if(w<=0.0)bucket=0;
                else
                {
                    double wx=((double)vertices[a].x+vertices[b].x+vertices[c].x)/3.0+globalOrigin.x;
                    double wz=((double)vertices[a].z+vertices[b].z+vertices[c].z)/3.0+globalOrigin.z;
                    if(!Finite(wx)||!Finite(wz)||System.Math.Abs(wx)>1e7||System.Math.Abs(wz)>1e7)return false;
                    double n=SmoothNoise(wx,wz);
                    double mod=w+(2.0*n-1.0)*0.12*w*(1.0-w);
                    if(mod<0.0)mod=0.0;else if(mod>1.0)mod=1.0;
                    double scaled=8.0*mod;
                    int level=(int)System.Math.Floor(scaled);
                    int dx=(int)System.Math.Floor(wx/2.0),dz=(int)System.Math.Floor(wz/2.0);
                    if(HashFloat(dx,dz,DitherSalt)<scaled-level)level++;
                    bucket=(byte)(level<0?0:(level>8?8:level));
                }
                p.buckets[i]=bucket;
                p.groupCounts[bucket]++;
            }
            p.full=p.groupCounts[8]==p.triangles;
            for(int i=1;i<=8;i++)if(p.groupCounts[i]>0){p.any=true;break;}
            plan=p;
            return true;
        }

        static GrassTransitionState Peek(newTerrainTileScript tile)
        {return tile==null?null:tile.GetComponent<GrassTransitionState>();}
        static GrassTransitionState Ensure(newTerrainTileScript tile)
        {
            var st=Peek(tile);
            if(st==null)st=tile.gameObject.AddComponent<GrassTransitionState>();
            st.tile=tile;
            return st;
        }
        internal static void Prepare(newTerrainTileScript tile,Vector3d globalOrigin)
        {
            var st=Ensure(tile);
            if(st!=null)st.PrimeExplicit(globalOrigin);
        }
        internal static void MeshChanged(newTerrainTileScript tile)
        {
            var st=Peek(tile);
            if(st!=null)st.Invalidate();
        }
        public static bool HasGrass(newTerrainTileScript tile)
        {
            if(tile==null)return false;
            if(WorldBiomePolicy.Mode!=0 || !Supported(tile))return NativeGrass(tile.biomeColor);
            var st=Ensure(tile);
            if(st!=null&&st.EnsurePrepared()&&st.plan!=null)return st.plan.any;
            return Density(tile.biomeColor)>0.001f;
        }
        public static void Apply(newTerrainTileScript tile,bool enabled)
        {
            if(tile==null||tile.mr==null)return;
            var st=Ensure(tile);
            if(!enabled){if(st!=null)st.SetActive(false);return;}
            if(!Supported(tile)||!HasGrass(tile)){if(st!=null)st.SetActive(false);return;}
            float density;
            if(WorldBiomePolicy.Mode==0&&st!=null&&st.EnsurePrepared()&&st.plan!=null&&st.plan.any)
            {
                if(!st.plan.full&&st.ApplyMixed())return;
                density=st.plan.full?1f:Density(tile.biomeColor);
            }
            else{density=WorldBiomePolicy.Mode==0?Density(tile.biomeColor):1f;if(st!=null)st.SetActive(false);}
            var materials=tile.mr.sharedMaterials;
            int index=tile.material==null?0:1;
            if(materials==null || index>=materials.Length || materials[index]!=tile.grassMaterial)return;
            if(block==null)block=new MaterialPropertyBlock();
            tile.mr.GetPropertyBlock(block,index);
            float maxDensity=tile.grassMaterial.GetFloat(MaxDensityId);
            float falloff=tile.grassMaterial.GetFloat(FalloffId);
            if(float.IsNaN(maxDensity)||float.IsInfinity(maxDensity)||maxDensity<1f || float.IsNaN(falloff)||float.IsInfinity(falloff)||falloff<=0f)return;
            float factor=(float)System.Math.Sqrt(density);
            block.SetFloat(MaxDensityId,1f+(maxDensity-1f)*factor);
            block.SetFloat(FalloffId,falloff/System.Math.Max(0.125f,factor));
            tile.mr.SetPropertyBlock(block,index);
        }
    }

    public sealed class GrassTransitionState : MonoBehaviour
    {
        internal newTerrainTileScript tile;
        internal Mesh sourceMesh;
        internal Material sourceGrass;
        internal Vector3d origin;
        internal bool originSet;
        internal GrassMeshPlan plan;
        internal int[] sourceTris;
        internal int[][] groups;
        internal Material[] childMats;
        internal GameObject child;
        internal Mesh ownMesh;
        internal static readonly int MaxDensityId=Shader.PropertyToID("_MaxTessellation");
        static readonly int FalloffId=Shader.PropertyToID("_EdgeLength");

        void ReleaseOwned()
        {
            if(ownMesh!=null){Object.Destroy(ownMesh);ownMesh=null;}
            if(child!=null){Object.Destroy(child);child=null;}
            childMats=null;
        }
        internal void Invalidate()
        {
            ReleaseOwned();
            sourceMesh=null;sourceGrass=null;plan=null;sourceTris=null;groups=null;originSet=false;
        }
        internal void SetActive(bool on)
        {
            if(child!=null&&child.activeSelf!=on)child.SetActive(on);
        }
        internal void PrimeExplicit(Vector3d o)
        {
            var mesh=tile==null||tile.mf==null?null:tile.mf.sharedMesh;
            var gm=tile==null?null:tile.grassMaterial;
            if(mesh!=sourceMesh||gm!=sourceGrass){Invalidate();sourceMesh=mesh;sourceGrass=gm;}
            origin=o;originSet=true;
            Rebuild();
        }
        internal bool EnsurePrepared()
        {
            if(tile==null)return false;
            var mesh=tile.mf==null?null:tile.mf.sharedMesh;
            var gm=tile.grassMaterial;
            if(mesh!=sourceMesh||gm!=sourceGrass){Invalidate();sourceMesh=mesh;sourceGrass=gm;}
            if(plan!=null)return true;
            if(sourceMesh==null||sourceGrass==null)return false;
            if(!originSet)
            {
                if(mainscript.s==null||tile.t==null)return false;
                origin=mainscript.GlobalFromUnityPos(tile.t.position);originSet=true;
            }
            return Rebuild();
        }
        bool Rebuild()
        {
            plan=null;sourceTris=null;groups=null;
            if(sourceMesh==null||tile==null)return false;
            if(!sourceMesh.isReadable||sourceMesh.subMeshCount!=1)return false;
            var v=sourceMesh.vertices;var c=sourceMesh.colors;var tris=sourceMesh.triangles;
            if(v==null||c==null||tris==null)return false;
            GrassMeshPlan p;
            if(!GrassTransition.TryBuild(v,c,tris,origin,out p))return false;
            p.source=sourceMesh;
            sourceTris=tris;
            groups=new int[9][];
            for(int g=1;g<=8;g++)
            {
                var l=new List<int>(p.groupCounts[g]*3);
                for(int i=0;i<p.triangles;i++)if(p.buckets[i]==g){l.Add(tris[i*3]);l.Add(tris[i*3+1]);l.Add(tris[i*3+2]);}
                groups[g]=l.ToArray();
            }
            plan=p;
            return true;
        }
        internal bool ApplyMixed()
        {
            if(plan==null||!plan.any||plan.full)return false;
            if(tile==null||tile.mr==null||sourceMesh==null||groups==null)return false;
            float maxD=tile.grassMaterial.GetFloat(MaxDensityId);
            float edge=tile.grassMaterial.GetFloat(FalloffId);
            if(float.IsNaN(maxD)||float.IsInfinity(maxD)||maxD<1f||float.IsNaN(edge)||float.IsInfinity(edge)||edge<=0f)return false;
            try{BuildChild();}catch{ReleaseOwned();return false;}
            if(child==null)return false;
            if(tile.material!=null)tile.mr.sharedMaterials=new Material[]{tile.material};
            else tile.mr.sharedMaterials=new Material[0];
            if(!child.activeSelf)child.SetActive(true);
            return true;
        }
        void BuildChild()
        {
            if(child==null)
            {
                child=new GameObject("grass-density");
                child.transform.SetParent(tile.t,false);
                child.transform.localPosition=Vector3.zero;
                child.transform.localRotation=Quaternion.identity;
                child.layer=tile.gameObject.layer;
                var f=child.AddComponent<MeshFilter>();
                ownMesh=Object.Instantiate(sourceMesh);
                f.sharedMesh=ownMesh;
                var r=child.AddComponent<MeshRenderer>();
                r.shadowCastingMode=tile.mr.shadowCastingMode;
                r.receiveShadows=tile.mr.receiveShadows;
                r.reflectionProbeUsage=tile.mr.reflectionProbeUsage;
                r.lightProbeUsage=tile.mr.lightProbeUsage;
                r.probeAnchor=tile.mr.probeAnchor;
                r.lightmapIndex=tile.mr.lightmapIndex;
                r.lightmapScaleOffset=tile.mr.lightmapScaleOffset;
                r.realtimeLightmapIndex=tile.mr.realtimeLightmapIndex;
                r.realtimeLightmapScaleOffset=tile.mr.realtimeLightmapScaleOffset;
            }
            var mf2=child.GetComponent<MeshFilter>();
            if(mf2.sharedMesh!=ownMesh)mf2.sharedMesh=ownMesh;
            int groups=0;
            for(int g=1;g<=8;g++)if(plan.groupCounts[g]>0)groups++;
            if(ownMesh.subMeshCount!=groups)ownMesh.subMeshCount=groups;
            int k=0;
            for(int g=1;g<=8;g++)if(plan.groupCounts[g]>0)ownMesh.SetTriangles(this.groups[g],k++);
            if(childMats==null||childMats.Length!=groups)childMats=new Material[groups];
            for(int i=0;i<groups;i++)childMats[i]=tile.grassMaterial;
            var r2=child.GetComponent<MeshRenderer>();
            r2.sharedMaterials=childMats;
            var pb=new MaterialPropertyBlock();
            float maxD=tile.grassMaterial.GetFloat(MaxDensityId);
            float edge=tile.grassMaterial.GetFloat(FalloffId);
            int slot=tile.material==null?0:1;
            k=0;
            for(int g=1;g<=8;g++)
            {
                if(plan.groupCounts[g]==0)continue;
                tile.mr.GetPropertyBlock(pb,slot);
                float factor=(float)System.Math.Sqrt(g/8f);
                pb.SetFloat(MaxDensityId,1f+(maxD-1f)*factor);
                pb.SetFloat(FalloffId,edge/System.Math.Max(0.125f,factor));
                r2.SetPropertyBlock(pb,k);
                k++;
            }
        }
        void OnDestroy()
        {
            if(ownMesh!=null)Object.Destroy(ownMesh);
            if(child!=null)Object.Destroy(child);
        }
    }
}
