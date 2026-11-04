using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Color{public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
    public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 operator/(Vector3 v,float n){return new Vector3(v.x/n,v.y/n,v.z/n);}public static Vector3 zero{get{return new Vector3();}}}
    public struct Vector4{public float x,y,z,w;public static Vector4 operator*(Vector4 v,float n){return new Vector4{x=v.x*n,y=v.y*n,z=v.z*n,w=v.w*n};}}
    public struct Quaternion{public static Quaternion identity{get{return new Quaternion();}}}
    public struct Vector3d{public double x,y,z;public Vector3d(double x,double y,double z){this.x=x;this.y=y;this.z=z;}public static Vector3d zero{get{return new Vector3d();}}}
    public static class Mathf{public static float Clamp01(float x){return Math.Max(0,Math.Min(1,x));}}
    public static class Shader{public static int PropertyToID(string s){return s=="_MaxTessellation"?9:10;}}
    public class Object{public bool destroyed;public static T Instantiate<T>(T src) where T:Object,new(){var c=new T();c.CopyFrom(src);return c;}public static void Destroy(Object o){if(o!=null)o.destroyed=true;}protected virtual void CopyFrom(Object src){}public static bool operator==(Object a,Object b){bool an=ReferenceEquals(a,null)||a.destroyed,bn=ReferenceEquals(b,null)||b.destroyed;if(an||bn)return an&&bn;return ReferenceEquals(a,b);}public static bool operator!=(Object a,Object b){return !(a==b);}}
    public class Component:Object{public GameObject gameObject;public Transform transform;public T GetComponent<T>() where T:Component{return gameObject==null?null:gameObject.GetComponent<T>();}}
    public class Behaviour:Component{public bool enabled=true;}
    public class GameObject:Object
    {
        public List<Component> components=new List<Component>();
        public string name;public int layer;public bool activeSelf=true;
        public Transform transform;
        public GameObject(){transform=new Transform{gameObject=this};components.Add(transform);}
        public GameObject(string n):this(){name=n;}
        public T AddComponent<T>() where T:Component,new(){var c=new T();c.gameObject=this;c.transform=transform;components.Add(c);return c;}
        public T GetComponent<T>() where T:Component{foreach(var c in components)if(c is T)return (T)c;return null;}
        public void SetActive(bool on){activeSelf=on;}
    }
    public class MonoBehaviour:Component{public bool activeSelf{get{return gameObject==null?true:gameObject.activeSelf;}}}
    public class Transform:Component{public Vector3 localPosition;public Quaternion localRotation;public Transform parent;public void SetParent(Transform p,bool keep){parent=p;}public Vector3 position{get;set;}}
    public class Mesh:Object
    {
        int[] _tris;public static int triGetCount;
        public Vector3[] vertices;public Color[] colors;public Vector3[] normals;public float[] uv;
        public int indexFormat;public float bounds;
        public bool isReadable=true;
        public int[] triangles{get{triGetCount++;return _tris;}set{_tris=value;}}
        public int subMeshCount=1;
        public readonly Dictionary<int,int[]> subMeshes=new Dictionary<int,int[]>();
        public void SetTriangles(int[] idx,int sub){subMeshes[sub]=idx;}
        public void SetTriangles(List<int> idx,int sub){subMeshes[sub]=idx.ToArray();}
        public void RecalculateNormals(){}public void RecalculateBounds(){}
        protected override void CopyFrom(Object src){var m=(Mesh)src;vertices=(Vector3[])m.vertices.Clone();colors=(Color[])m.colors.Clone();_tris=(int[])m._tris.Clone();normals=m.normals==null?null:(Vector3[])m.normals.Clone();uv=m.uv==null?null:(float[])m.uv.Clone();bounds=m.bounds;indexFormat=m.indexFormat;subMeshCount=m.subMeshCount;foreach(var kv in m.subMeshes)subMeshes[kv.Key]=(int[])kv.Value.Clone();}
    }
    public struct Vector2{public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
    public struct Vector4b{public float x,y,z,w;}
    public class MeshFilter:Component{Mesh _m;public Mesh sharedMesh{get{return _m;}set{_m=value;}}}
    public class MeshCollider:Component{Mesh _m;public Mesh sharedMesh{get{return _m;}set{_m=value;}}}
    public class Material:Object
    {
        public bool supported=true;public Vector4 density=new Vector4{x=1,y=.5f,z=.2f,w=.1f};
        public bool HasProperty(int n){return supported;}
        public bool IsKeywordEnabled(string k){return supported;}
        public float GetFloat(int n){return n==9?5:12;}
        public static int setCount;
    }
    public class MaterialPropertyBlock{public Vector4 density;public float unrelated;public void SetFloat(int id,float v){if(id==9)density.x=v;else density.y=v;}}
    public enum ShadowCastingMode{Off,On}public enum ReflectionProbeUsage{Off,Simple}public enum LightProbeUsage{Off,On}
    public class Renderer:Component
    {
        Material[] _mats;
        public Material[] sharedMaterials{get{return _mats;}set{_mats=value;}}
        public Material sharedMaterial{get{return _mats!=null&&_mats.Length>0?_mats[0]:null;}set{_mats=new Material[]{value};}}
    }
    public class MeshRenderer:Renderer
    {
        public MaterialPropertyBlock[] blocks={new MaterialPropertyBlock(),new MaterialPropertyBlock(),new MaterialPropertyBlock(),new MaterialPropertyBlock(),new MaterialPropertyBlock(),new MaterialPropertyBlock(),new MaterialPropertyBlock(),new MaterialPropertyBlock(),new MaterialPropertyBlock()};
        public ShadowCastingMode shadowCastingMode;public bool receiveShadows=true;public ReflectionProbeUsage reflectionProbeUsage;public LightProbeUsage lightProbeUsage;
        public Transform probeAnchor;public int lightmapIndex,realtimeLightmapIndex;public Vector4b lightmapScaleOffset,realtimeLightmapScaleOffset;
        public void GetPropertyBlock(MaterialPropertyBlock b,int i){if(i>=blocks.Length)System.Array.Resize(ref blocks,i+1);b.density=blocks[i].density;b.unrelated=blocks[i].unrelated;}
        public void SetPropertyBlock(MaterialPropertyBlock b,int i){if(i>=blocks.Length)System.Array.Resize(ref blocks,i+1);blocks[i].density=b.density;blocks[i].unrelated=b.unrelated;}
    }
    public class mainscript:MonoBehaviour{public static mainscript s;public Vector3d visszarakva;public static Vector3d GlobalFromUnityPos(Vector3 p){return s==null?Vector3d.zero:new Vector3d(-s.visszarakva.x+p.x,-s.visszarakva.y+p.y,-s.visszarakva.z+p.z);}}
}
public class terrainGenScript{}
public class visszarako:UnityEngine.MonoBehaviour{public bool moveWhenParented,dontlookUnderMap;}
public class newTerrainTileScript:UnityEngine.MonoBehaviour
{
    public UnityEngine.Transform t;public UnityEngine.MeshFilter mf;public UnityEngine.MeshCollider mc;public UnityEngine.MeshRenderer mr;
    public bool hasCollider,destroyMesh;public visszarako vissza;
    public UnityEngine.Color biomeColor;public UnityEngine.Material material,grassMaterial;
    public bool Grass(){return false;}
    public void UpdMaterial(bool _grassEnabled){}
    public void UpdMesh(UnityEngine.Mesh _mesh){}
}
namespace TldMp
{
    public static class WorldBiomePolicy{public static int Mode;public static void SetMode(int m){Mode=m;}}
    public static class StableGameBridge
    {
        public static bool BiomeGrassDecision(newTerrainTileScript tile){return GrassTransition.HasGrass(tile);}
        public static void BiomeGrassApplied(newTerrainTileScript tile,bool enabled){GrassTransition.Apply(tile,enabled);}
        public static void BiomeGrassMeshChanged(newTerrainTileScript tile){GrassTransition.MeshChanged(tile);}
        public static void NativeTerrainMeshUploadBegin(){}
        public static void NativeTerrainMeshUploadEnd(){}
    }
}

public static class GrassTransitionTests
{
    static int checks;static bool candidate;
    static void Check(bool v,string label){checks++;if(!v)throw new Exception("check "+checks+" failed: "+label);}
    static UnityEngine.Mesh MakeMesh(int n,System.Func<int,UnityEngine.Color> color)
    {
        var m=new UnityEngine.Mesh();
        var v=new UnityEngine.Vector3[n*3];var c=new UnityEngine.Color[n*3];var t=new int[n*3];
        for(int i=0;i<n;i++)
        {
            for(int k=0;k<3;k++){v[i*3+k]=new UnityEngine.Vector3(i,k,0);c[i*3+k]=color(i*3+k);t[i*3+k]=i*3+k;}
        }
        m.vertices=v;m.colors=c;m.normals=(UnityEngine.Vector3[])v.Clone();m.uv=new float[v.Length];m.triangles=t;
        return m;
    }
    static newTerrainTileScript Tile(UnityEngine.Mesh mesh,UnityEngine.Color biome,UnityEngine.Material ground,UnityEngine.Material grass)
    {
        var go=new UnityEngine.GameObject("tile");
        var tile=go.AddComponent<newTerrainTileScript>();
        tile.t=go.transform;tile.mf=go.AddComponent<UnityEngine.MeshFilter>();tile.mr=go.AddComponent<UnityEngine.MeshRenderer>();
        tile.mf.sharedMesh=mesh;tile.biomeColor=biome;tile.material=ground;tile.grassMaterial=grass;
        tile.mr.sharedMaterials=new UnityEngine.Material[]{ground,grass};
        return tile;
    }
    public static int Main(string[] args)
    {
        try
        {
            candidate=args.Length>0&&args[0]=="candidate";
            var grass=new UnityEngine.Material();var ground=new UnityEngine.Material();
            var t=Tile(null,new UnityEngine.Color(0,1,0,1),ground,grass);t.mr.blocks[1].unrelated=123;
            Check(TldMp.GrassTransition.HasGrass(t),"full native grass has grass");
            TldMp.GrassTransition.Apply(t,true);
            Check(t.mr.blocks[1].density.x==5&&t.mr.blocks[1].unrelated==123,"coarse full density unchanged");
            t.biomeColor=new UnityEngine.Color(.2f,.75f,.1f,.8f);float w=TldMp.GrassTransition.Density(t.biomeColor);
            Check(Math.Abs(w-.432f)<.00001,"density formula");
            Check(TldMp.GrassTransition.HasGrass(t),"mixed has grass");
            TldMp.GrassTransition.Apply(t,true);
            Check(Math.Abs(t.mr.blocks[1].density.x-(1+4*Math.Sqrt(w)))<.00001&&Math.Abs(t.mr.blocks[1].density.y-12/Math.Sqrt(w))<.00001,"coarse mixed factor");
            Check(grass.density.x==1,"source material unchanged");
            var other=Tile(null,new UnityEngine.Color(.5f,1,0,1),ground,grass);TldMp.GrassTransition.Apply(other,true);
            Check(Math.Abs(other.mr.blocks[1].density.x-(1+4*Math.Sqrt(.5)))<.00001&&Math.Abs(t.mr.blocks[1].density.x-(1+4*Math.Sqrt(w)))<.00001,"per-tile independent");
            t.biomeColor=new UnityEngine.Color(1,1,0,1);Check(!TldMp.GrassTransition.HasGrass(t),"yellow has no grass");
            t.biomeColor=new UnityEngine.Color(0,1,1,1);Check(!TldMp.GrassTransition.HasGrass(t),"cyan has no grass");
            TldMp.WorldBiomePolicy.Mode=1;t.biomeColor=new UnityEngine.Color(.2f,.75f,.1f,.8f);Check(!TldMp.GrassTransition.HasGrass(t),"fixed mode uses native biome");
            TldMp.WorldBiomePolicy.Mode=3;t.biomeColor=new UnityEngine.Color(0,1,0,1);Check(TldMp.GrassTransition.HasGrass(t),"winter native still has grass");
            TldMp.WorldBiomePolicy.Mode=0;grass.supported=false;t.biomeColor=new UnityEngine.Color(.2f,1,0,1);Check(!TldMp.GrassTransition.HasGrass(t),"unsupported shader native");
            grass.supported=true;t.biomeColor=new UnityEngine.Color(float.NaN,1,0,1);Check(!TldMp.GrassTransition.HasGrass(t),"NaN safe");
            t.material=null;t.mr.sharedMaterials=new UnityEngine.Material[]{grass};t.biomeColor=new UnityEngine.Color(.5f,1,0,1);TldMp.GrassTransition.Apply(t,true);
            Check(Math.Abs(t.mr.blocks[0].density.x-(1+4*Math.Sqrt(.5)))<.00001,"ground-null slot factor");
            t.mr.blocks[0].density.x=.25f;TldMp.GrassTransition.Apply(t,false);Check(t.mr.blocks[0].density.x==.25f,"disabled apply leaves block");
            UnityEngine.Vector3d origin=new UnityEngine.Vector3d(1000,0,1000);
            var mixed=MakeMesh(9,i=>{int tri=i/3;return tri<3?new UnityEngine.Color(1,0,0,1):(tri<6?new UnityEngine.Color(.5f,.5f,0,1):new UnityEngine.Color(0,1,0,1));});
            TldMp.GrassMeshPlan plan;
            Check(TldMp.GrassTransition.TryBuild(mixed.vertices,mixed.colors,mixed.triangles,origin,out plan),"plan builds");
            int used=0;for(int g=1;g<=8;g++)used+=plan.groupCounts[g];
            Check(used+plan.groupCounts[0]==plan.triangles&&plan.groupCounts[0]==3,"zero bucket omitted only");
            Check(plan.groupCounts[8]==3,"full density keeps bucket 8");
            Check(!plan.full&&plan.any,"mixed plan not full");
            Check(plan.triangles==9,"all triangles budgeted");
            foreach(var b in plan.buckets)Check(b<=8,"buckets bounded");
            var allFull=MakeMesh(4,i=>new UnityEngine.Color(0,1,0,1));
            Check(TldMp.GrassTransition.TryBuild(allFull.vertices,allFull.colors,allFull.triangles,origin,out plan)&&plan.full&&plan.groupCounts[8]==4,"full plan keeps native path");
            var allZero=MakeMesh(4,i=>new UnityEngine.Color(1,0,0,1));
            Check(TldMp.GrassTransition.TryBuild(allZero.vertices,allZero.colors,allZero.triangles,origin,out plan)&&!plan.any,"zero plan none");
            Check(!TldMp.GrassTransition.TryBuild(mixed.vertices,null,mixed.triangles,origin,out plan),"null colors rejected");
            Check(!TldMp.GrassTransition.TryBuild(mixed.vertices,new UnityEngine.Color[2],mixed.triangles,origin,out plan),"mismatched colors rejected");
            var bad=(int[])mixed.triangles.Clone();bad[0]=9999;
            Check(!TldMp.GrassTransition.TryBuild(mixed.vertices,mixed.colors,bad,origin,out plan),"out-of-range index rejected");
            var nan=(UnityEngine.Vector3[])mixed.vertices.Clone();nan[0].x=float.NaN;
            Check(!TldMp.GrassTransition.TryBuild(nan,mixed.colors,mixed.triangles,origin,out plan),"NaN vertex rejected");
            var mod=MakeMesh(60,i=>{int tri=i/3;float u=tri/60f;return new UnityEngine.Color(1f-u,u,0f,1f);});
            TldMp.GrassMeshPlan pa,pb;
            bool okA=TldMp.GrassTransition.TryBuild(mod.vertices,mod.colors,mod.triangles,new UnityEngine.Vector3d(0,0,0),out pa);
            bool okB=TldMp.GrassTransition.TryBuild(mod.vertices,mod.colors,mod.triangles,new UnityEngine.Vector3d(0,0,0),out pb);
            Check(okA&&okB,"rebuild deterministic");
            bool same=true;for(int i=0;i<pa.triangles;i++)if(pa.buckets[i]!=pb.buckets[i])same=false;
            Check(same,"same world coords identical buckets");
            Check(TldMp.GrassTransition.TryBuild(mod.vertices,mod.colors,mod.triangles,new UnityEngine.Vector3d(5.37,0,7.91),out pb),"offset origin builds");
            bool diff=false;for(int i=0;i<pa.triangles;i++)if(pa.buckets[i]!=pb.buckets[i])diff=true;
            Check(diff,"offset world origin varies buckets");
            var prod=Tile(mixed,new UnityEngine.Color(0,1,0,1),ground,grass);
            prod.UpdMaterial(true);
            Check(prod.mf.sharedMesh==mixed,"source mesh ref unchanged by UpdMaterial");
            prod.UpdMesh(mixed);
            Check(prod.mf.sharedMesh==mixed&&(prod.mc==null||prod.mc.sharedMesh==mixed),"native UpdMesh assigns filter/collider");
            if(candidate)
            {
                UnityEngine.mainscript.s=new UnityEngine.mainscript{visszarakva=new UnityEngine.Vector3d(50,0,-25)};
                var ramp=MakeMesh(60,i=>{int tri=i/3;float u=tri/60f;return new UnityEngine.Color(1f-u,u,0f,1f);});
                var live=Tile(ramp,new UnityEngine.Color(1,0,0,1),ground,grass);
                live.t.position=new UnityEngine.Vector3(300,0,200);
                live.UpdMesh(ramp);
                live.UpdMaterial(true);
                var st=live.GetComponent<TldMp.GrassTransitionState>();
                Check(st!=null&&st.plan!=null,"candidate center-red tile builds vertex plan via real origin");
                Check(st.origin.x==250&&st.origin.z==225,"origin = -visszarakva + tile position");
                int ngr=0;for(int g=1;g<=8;g++)if(st.plan.groupCounts[g]>0)ngr++;
                Check(ngr>=2,"multiple buckets inside one tile");
                Check(st.child!=null&&st.child.activeSelf,"owned grass child active");
                Check(live.mr.sharedMaterials.Length==1&&live.mr.sharedMaterials[0]==ground,"root keeps ground only");
                var cchild=st.child.GetComponent<UnityEngine.MeshRenderer>();
                Check(cchild!=null&&cchild.sharedMaterials.Length==ngr,"child renders grass buckets only");
                Check(cchild.sharedMaterials[0]==grass,"child shares source material");
                var cm=st.child.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
                Check(cm!=ramp&&cm.subMeshCount==ngr,"owned cloned mesh grouped");
                int totalIdx=0;for(int g=1;g<=8;g++)totalIdx+=st.plan.groupCounts[g]*3;
                Check(totalIdx==ramp.triangles.Length-st.plan.groupCounts[0]*3,"every non-zero triangle emitted once");
                int beforeTri=UnityEngine.Mesh.triGetCount;
                TldMp.GrassTransition.Apply(live,true);
                Check(UnityEngine.Mesh.triGetCount<=beforeTri+2,"re-apply does not rescan source triangles");
                live.UpdMaterial(false);
                Check(st.child==null||!st.child.activeSelf,"grass off deactivates child");
                var keptChild=st.child;var keptMesh=st.ownMesh;
                live.UpdMaterial(true);
                Check(st.child==keptChild&&st.ownMesh==keptMesh,"toggle reuse without new mesh/child");
                var oldChild=st.child;
                var rebuilt=MakeMesh(12,i=>new UnityEngine.Color(0,1,0,1));
                live.UpdMesh(rebuilt);
                Check(st.child==null&&oldChild.destroyed,"mesh change releases owned child");
                live.UpdMaterial(true);
                Check(st.plan!=null&&st.plan.full&&st.child==null&&live.mr.sharedMaterials.Length==2,"full plan native fast path no child");
                TldMp.WorldBiomePolicy.Mode=1;
                live.UpdMaterial(true);
                Check(st.child==null||!st.child.activeSelf,"fixed mode hides stale child");
                TldMp.WorldBiomePolicy.Mode=0;
                var dead=Tile(ramp,new UnityEngine.Color(0,1,0,1),ground,grass);
                dead.t.position=new UnityEngine.Vector3(-9999,0,-7777);
                dead.UpdMaterial(true);
                Check(dead.GetComponent<TldMp.GrassTransitionState>().plan!=null,"negative world coords still plan");
                var nanT=Tile(ramp,new UnityEngine.Color(0,1,0,1),ground,grass);
                nanT.mf.sharedMesh=null;
                TldMp.GrassTransition.Apply(nanT,true);
                Check(nanT.mr.sharedMaterials!=null&&nanT.mr.sharedMaterials.Length==2,"null mesh coarse fallback keeps native materials");
                var unread=Tile(ramp,new UnityEngine.Color(0,1,0,1),ground,grass);
                ((UnityEngine.Mesh)unread.mf.sharedMesh).isReadable=false;
                TldMp.GrassTransition.Apply(unread,true);
                Check(unread.mr.sharedMaterials.Length==2,"unreadable mesh atomic native fallback");
                var before=st.plan;var beforeMesh=st.ownMesh;
                live.t.position=new UnityEngine.Vector3(300+40,0,200-10);
                UnityEngine.mainscript.s.visszarakva=new UnityEngine.Vector3d(90,0,-35);
                live.UpdMaterial(true);
                Check(st.plan==before&&st.ownMesh==beforeMesh&&st.origin.x==250,"recenter keeps cached origin/plan");
                var attr=MakeMesh(8,i=>i/3<4?new UnityEngine.Color(0,1,0,1):new UnityEngine.Color(1,0,0,1));
                attr.normals[0].x=7f;attr.uv[0]=4f;
                live.UpdMesh(attr);
                live.UpdMaterial(true);
                Check(st.ownMesh!=null&&st.ownMesh.normals[0].x==7f&&st.ownMesh.uv[0]==4f,"clone carries new source attributes");
                attr.colors[0]=new UnityEngine.Color(1,0,0,1);
                live.UpdMesh(attr);
                Check(st.plan==null||st.ownMesh!=beforeMesh,"same-ref mesh mutation invalidates");
                UnityEngine.mainscript.s=null;
            }
            Console.WriteLine("PASS "+checks+" grass transition checks ("+(candidate?"candidate":"baseline")+"; synthetic-menu-probe graphics pending lead review)");
            return 0;
        }
        catch(Exception e){Console.WriteLine(e.ToString());return 1;}
    }
}
