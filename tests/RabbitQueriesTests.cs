using System;
using System.Collections.Generic;
using TldMp;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero{get{return new Vector3();}}
        public static bool operator==(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        public static bool operator!=(Vector3 a,Vector3 b){return !(a==b);}
        public override bool Equals(object o){return o is Vector3&&this==(Vector3)o;}
        public override int GetHashCode(){return x.GetHashCode()^y.GetHashCode()^z.GetHashCode();}
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public static bool operator==(Quaternion a,Quaternion b){return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;}
        public static bool operator!=(Quaternion a,Quaternion b){return !(a==b);}
        public override bool Equals(object o){return o is Quaternion&&this==(Quaternion)o;}
        public override int GetHashCode(){return x.GetHashCode()^w.GetHashCode();}
    }
    public enum QueryTriggerInteraction{UseGlobal,Ignore,Collide}
    public enum ForceMode{Force,Impulse}
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object value)
        {
            if(ReferenceEquals(value,null))return;
            value.Destroyed=true;
            var go=value as GameObject;
            if(!ReferenceEquals(go,null))foreach(var component in go.Components)component.Destroyed=true;
        }
        public static bool operator==(Object a,Object b)
        {
            if(ReferenceEquals(a,null))return ReferenceEquals(b,null)||b.Destroyed;
            if(ReferenceEquals(b,null))return a.Destroyed;
            return ReferenceEquals(a,b);
        }
        public static bool operator!=(Object a,Object b){return !(a==b);}
        public override bool Equals(object o){return ReferenceEquals(this,o);}
        public override int GetHashCode(){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);}
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform{get{return gameObject.transform;}}
        public T GetComponent<T>(){return gameObject.GetComponent<T>();}
        public T GetComponentInParent<T>()
        {
            for(var t=transform;t!=null;t=t.parent){var c=t.gameObject.GetComponent<T>();if(!ReferenceEquals(c,null))return c;}
            return default(T);
        }
        public T GetComponentInChildren<T>(){var all=GetComponentsInChildren<T>();return all.Length==0?default(T):all[0];}
        public T[] GetComponentsInChildren<T>()
        {
            var result=new List<T>();
            foreach(var go in GameObject.All)if(go!=null&&go.activeInHierarchy&&go.transform.IsChildOf(transform))
                foreach(var c in go.Components)if(c is T)result.Add((T)(object)c);
            return result.ToArray();
        }
    }
    public class Transform:Component
    {
        public Vector3 position,localScale=new Vector3(1,1,1);
        public Quaternion rotation;
        public Transform parent;
        public Vector3 lossyScale{get{return localScale;}}
        public void SetParent(Transform p,bool keepWorld){parent=p;}
        public bool IsChildOf(Transform p){for(Transform t=this;t!=null;t=t.parent)if(ReferenceEquals(t,p))return true;return false;}
    }
    public class GameObject:Object
    {
        public static readonly List<GameObject> All=new List<GameObject>();
        public readonly List<Component> Components=new List<Component>();
        public Transform transform;
        public Scene scene;
        public string name,tag="Untagged";
        public int layer;
        public bool activeSelf=true;
        public bool activeInHierarchy{get{return !Destroyed&&activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);}}
        public GameObject():this("test"){}
        public GameObject(string name){this.name=name;scene=SceneManager.GetActiveScene();transform=new Transform{gameObject=this};Components.Add(transform);All.Add(this);}
        public void SetActive(bool active){activeSelf=active;}
        public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};Components.Add(c);return c;}
        public T GetComponent<T>(){foreach(var c in Components)if(c!=null&&c is T)return (T)(object)c;return default(T);}
    }
    public class MonoBehaviour:Component{}
    public class Collider:Component{public bool enabled=true,isTrigger;}
    public class CapsuleCollider:Collider{public int direction;public Vector3 center;public float radius,height;}
    public class SphereCollider:Collider{public Vector3 center;public float radius;}
    public class BoxCollider:Collider{public Vector3 center,size;}
    public class MeshCollider:Collider{public Mesh sharedMesh;public bool convex;}
    public class UnsupportedCollider:Collider{}
    public class Mesh:Object{}
    public class Rigidbody:Component
    {
        public bool isKinematic;
        public int Calls;
        public Vector3 Force,Point;
        public ForceMode Mode;
        public void AddForce(Vector3 f){Force=f;Mode=ForceMode.Force;Calls++;}
        public void AddForce(Vector3 f,ForceMode m){Force=f;Mode=m;Calls++;}
        public void AddForceAtPosition(Vector3 f,Vector3 p){Force=f;Point=p;Mode=ForceMode.Force;Calls++;}
        public void AddForceAtPosition(Vector3 f,Vector3 p,ForceMode m){Force=f;Point=p;Mode=m;Calls++;}
    }
    public struct RaycastHit{public Collider collider;public float distance;public Vector3 point;}
    public struct PhysicsScene
    {
        public int Handle;
        public static bool operator==(PhysicsScene a,PhysicsScene b){return a.Handle==b.Handle;}
        public static bool operator!=(PhysicsScene a,PhysicsScene b){return !(a==b);}
        public override bool Equals(object o){return o is PhysicsScene&&this==(PhysicsScene)o;}
        public override int GetHashCode(){return Handle;}
        public int Raycast(Vector3 o,Vector3 d,RaycastHit[] hits,float distance,int mask,QueryTriggerInteraction q)
        {
            int count=0;
            foreach(var c in Physics.Colliders(Handle,mask,q))
            {
                var p=c.transform.position;
                if(d.z<=0||p.z<o.z||p.z-o.z>distance||p.x!=o.x||p.y!=o.y)continue;
                if(count==hits.Length)break;
                hits[count++]=new RaycastHit{collider=c,distance=p.z-o.z,point=p};
            }
            return count;
        }
        public int OverlapBox(Vector3 c,Vector3 half,Collider[] hits,Quaternion r,int mask,QueryTriggerInteraction q)
        {
            int count=0;
            foreach(var shape in Physics.Colliders(Handle,mask,q))
            {
                var p=shape.transform.position;
                if(Math.Abs(p.x-c.x)>half.x||Math.Abs(p.y-c.y)>half.y||Math.Abs(p.z-c.z)>half.z)continue;
                if(count==hits.Length)break;hits[count++]=shape;
            }
            return count;
        }
    }
    public static class Physics
    {
        public static PhysicsScene defaultPhysicsScene=new PhysicsScene{Handle=1};
        public static bool queriesHitTriggers=true;
        public static int Syncs;
        public static List<Collider> Colliders(int scene,int mask,QueryTriggerInteraction q)
        {
            var result=new List<Collider>();
            foreach(var go in GameObject.All)if(go!=null&&go.scene.Handle==scene&&go.activeInHierarchy&&(mask&(1<<go.layer))!=0)
                foreach(var value in go.Components)
                {
                    var c=value as Collider;
                    if(c==null||!c.enabled||c.isTrigger&&(q==QueryTriggerInteraction.Ignore||q==QueryTriggerInteraction.UseGlobal&&!queriesHitTriggers))continue;
                    result.Add(c);
                }
            return result;
        }
        public static bool Raycast(Vector3 o,Vector3 d,out RaycastHit hit,float distance,int mask,QueryTriggerInteraction q)
        {
            var all=RaycastAll(o,d,distance,mask,q);hit=new RaycastHit();bool found=false;
            foreach(var h in all)if(!found||h.distance<hit.distance){hit=h;found=true;}return found;
        }
        public static RaycastHit[] RaycastAll(Vector3 o,Vector3 d,float distance,int mask,QueryTriggerInteraction q)
        {
            var hits=new RaycastHit[128];int n=defaultPhysicsScene.Raycast(o,d,hits,distance,mask,q);
            Array.Resize(ref hits,n);return hits;
        }
        public static int RaycastNonAlloc(Vector3 o,Vector3 d,RaycastHit[] hits,float distance,int mask,QueryTriggerInteraction q)
        {return defaultPhysicsScene.Raycast(o,d,hits,distance,mask,q);}
        public static Collider[] OverlapBox(Vector3 p,Vector3 half,Quaternion r,int mask,QueryTriggerInteraction q)
        {
            var hits=new Collider[128];int n=defaultPhysicsScene.OverlapBox(p,half,hits,r,mask,q);Array.Resize(ref hits,n);return hits;
        }
        public static void SyncTransforms(){Syncs++;}
    }
    public static class PhysicsSceneExtensions
    {
        public static PhysicsScene GetPhysicsScene(this Scene scene){return new PhysicsScene{Handle=SceneManager.DefaultPrivate?1:scene.Handle};}
    }
    public static class Time{public static int frameCount;}
    public static class Debug{public static int Warnings;public static void LogWarning(object value){Warnings++;}}
}
namespace UnityEngine.SceneManagement
{
    public enum LocalPhysicsMode{None,Physics2D,Physics3D}
    public struct CreateSceneParameters{public LocalPhysicsMode Mode;public CreateSceneParameters(LocalPhysicsMode mode){Mode=mode;}}
    public struct Scene{public int Handle;public bool IsValid(){return Handle>0;}}
    public static class SceneManager
    {
        public static Scene Active=new Scene{Handle=1};
        public static int Next=2;
        public static bool FailCreate,FailMove,DefaultPrivate;
        public static readonly List<int> Unloaded=new List<int>();
        public static Scene GetActiveScene(){return Active;}
        public static bool SetActiveScene(Scene s){Active=s;return true;}
        public static Scene CreateScene(string name,CreateSceneParameters p)
        {
            if(FailCreate)throw new InvalidOperationException("create");
            if(p.Mode!=LocalPhysicsMode.Physics3D)throw new Exception("Wrong private physics mode");
            var scene=new Scene{Handle=Next++};Active=scene;return scene;
        }
        public static void MoveGameObjectToScene(GameObject go,Scene s)
        {if(FailMove)throw new InvalidOperationException("move");go.scene=s;}
        public static void UnloadSceneAsync(Scene s)
        {
            if(s.Handle==1)throw new Exception("Attempted to unload main world");
            Unloaded.Add(s.Handle);foreach(var go in GameObject.All)if(go.scene.Handle==s.Handle)UnityEngine.Object.Destroy(go);
        }
    }
}
public class weaponscript:MonoBehaviour{}
public class meleeweaponscript:MonoBehaviour{}
public class nyulscript:MonoBehaviour{}
public class QueryMarker:MonoBehaviour{}
public static class RabbitQueriesTests
{
    static int checks;
    static readonly Vector3 Origin=new Vector3(),Forward=new Vector3(0,0,1);
    static void Check(bool ok,string name){checks++;if(!ok)throw new Exception("FAIL "+name);}
    static void Reset()
    {
        RabbitQueries.Clear();RabbitQueries.WeaponIntent=null;RabbitQueries.MeleeIntent=null;
        GameObject.All.Clear();SceneManager.Active=new Scene{Handle=1};SceneManager.Next=2;
        SceneManager.Unloaded.Clear();SceneManager.FailCreate=SceneManager.FailMove=SceneManager.DefaultPrivate=false;
        Physics.Syncs=0;Physics.queriesHitTriggers=true;Time.frameCount=0;Debug.Warnings=0;
    }
    static nyulscript Root(){return new GameObject("rabbit").AddComponent<nyulscript>();}
    static T Shape<T>(float z) where T:Collider,new()
    {var go=new GameObject();go.transform.position=new Vector3(0,0,z);return go.AddComponent<T>();}
    static void Sync(nyulscript root,params Collider[] shapes)
    {
        var enabled=new List<bool>();foreach(var c in shapes){enabled.Add(true);c.enabled=false;}
        RabbitQueries.SyncRoot(root,shapes,enabled);
    }
    static Collider Proxy(Collider source)
    {
        foreach(var go in GameObject.All)if(go!=null&&go.scene.Handle!=1)
            foreach(var value in go.Components){var c=value as Collider;if(c!=null&&ReferenceEquals(RabbitQueries.Source(c),source))return c;}
        throw new Exception("No live proxy for source");
    }
    static bool Ray(out RaycastHit hit,int mask){return RabbitQueries.Raycast(Origin,Forward,out hit,200,mask);}
    static void GeometryAndMapping()
    {
        Reset();var root=Root();var body=Shape<CapsuleCollider>(5);body.transform.SetParent(root.transform,true);
        body.direction=2;body.center=new Vector3(1,2,3);body.radius=.4f;body.height=2;
        body.gameObject.layer=3;body.gameObject.tag="rabbit";
        body.transform.localScale=new Vector3(2,3,4);body.transform.rotation=new Quaternion{x=.1f,y=.2f,z=.3f,w=.9f};
        var child=new GameObject();child.transform.parent=body.transform;var marker=child.AddComponent<QueryMarker>();
        Sync(root,body);var proxy=(CapsuleCollider)Proxy(body);
        Check(proxy.gameObject.scene.Handle!=1&&proxy.gameObject.activeInHierarchy&&proxy.enabled,"query shape active only in private scene");
        Check(!body.enabled&&body.gameObject.scene.Handle==1&&body.gameObject.activeSelf,"original collider and main scene preserved");
        Check(proxy.direction==2&&proxy.center==body.center&&proxy.radius==.4f&&proxy.height==2,"capsule geometry copied");
        Check(proxy.transform.position==body.transform.position&&proxy.transform.rotation==body.transform.rotation&&proxy.transform.localScale==body.transform.lossyScale,"world pose scale copied");
        Check(proxy.gameObject.GetComponent<Rigidbody>()==null&&proxy.gameObject.Components.Count==2,"proxy contains transform and shape only");
        Check(ReferenceEquals(StableGameBridge.RabbitQueryGet<CapsuleCollider>(proxy),body),"mapped GetComponent");
        Check(ReferenceEquals(StableGameBridge.RabbitQueryParent<nyulscript>(proxy),root),"mapped parent");
        Check(ReferenceEquals(StableGameBridge.RabbitQueryChild<QueryMarker>(proxy),marker),"mapped child");
        Check(StableGameBridge.RabbitQueryChildren<QueryMarker>(proxy).Length==1,"mapped children");
        Check(ReferenceEquals(StableGameBridge.RabbitQueryGameGet<CapsuleCollider>(proxy.gameObject),body),"mapped game object");
        Check(StableGameBridge.RabbitQueryIsChildOf(proxy.transform,root.transform),"mapped IsChildOf");
        Check(ReferenceEquals(StableGameBridge.RabbitQueryGet<CapsuleCollider>(body),body),"ordinary lookup unchanged");
        bool threw=false;try{StableGameBridge.RabbitQueryGet<Collider>(null);}catch(NullReferenceException){threw=true;}
        Check(threw,"ordinary null not silently swallowed");
        RaycastHit hit;Check(Ray(out hit,1<<3)&&ReferenceEquals(hit.collider,proxy),"registered private source used by ray");
        int syncs=Physics.Syncs;Sync(root,body);Ray(out hit,1<<3);
        Check(ReferenceEquals(proxy,Proxy(body))&&Physics.Syncs==syncs,"identical sync reuses shape and does not resync physics");
        body.height=3;Sync(root,body);Ray(out hit,1<<3);
        Check(proxy.height==3&&Physics.Syncs==syncs+1,"same-frame geometry dirty batch synced");
        syncs=Physics.Syncs;Time.frameCount++;Ray(out hit,1<<3);
        Check(Physics.Syncs==syncs,"stationary next frame needs no physics sync");
        body.transform.position=new Vector3(0,0,7);Time.frameCount++;Ray(out hit,1<<3);
        Check(hit.distance==7&&Physics.Syncs==syncs+1,"source motion followed before query");
        Check(SceneManager.GetActiveScene().Handle==1,"main active scene preserved");
        UnityEngine.Object.Destroy(body);
        Check(StableGameBridge.RabbitQueryGet<Collider>(proxy)==null,"owned proxy with destroyed original defaults safely");
        Time.frameCount++;Check(!Ray(out hit,1<<3)&&!RabbitQueries.Active,"destroyed source cleaned before cast");
    }
    static void ShapesAndRoots()
    {
        Reset();var a=Root();var b=Root();
        var sphere=Shape<SphereCollider>(2);sphere.radius=.8f;sphere.center=new Vector3(1,2,3);
        var box=Shape<BoxCollider>(3);box.size=new Vector3(1,2,3);box.center=new Vector3(4,5,6);
        var mesh=Shape<MeshCollider>(4);mesh.sharedMesh=new Mesh();mesh.convex=true;
        Sync(a,sphere,box);Sync(b,mesh);
        Check(((SphereCollider)Proxy(sphere)).radius==.8f&&((SphereCollider)Proxy(sphere)).center==sphere.center,"sphere properties");
        Check(((BoxCollider)Proxy(box)).size==box.size&&((BoxCollider)Proxy(box)).center==box.center,"box properties");
        Check(ReferenceEquals(((MeshCollider)Proxy(mesh)).sharedMesh,mesh.sharedMesh)&&((MeshCollider)Proxy(mesh)).convex,"readonly mesh shared without mutation");
        Check(RabbitQueries.RaycastAll(Origin,Forward,200,1).Length==3,"two roots and detached colliders retained");
        RabbitQueries.RemoveRoot(a);
        Check(RabbitQueries.RaycastAll(Origin,Forward,200,1).Length==1&&Proxy(mesh)!=null,"remove one root preserves other");
        UnityEngine.Object.Destroy(b);RabbitQueries.RemoveRoot(b);
        Check(!RabbitQueries.Active&&SceneManager.Unloaded.Count==1,"destroyed owner can remove last root");
        Reset();a=Root();sphere=Shape<SphereCollider>(2);box=Shape<BoxCollider>(3);Sync(a,sphere,box);Sync(a,box);
        Check(RabbitQueries.RaycastAll(Origin,Forward,200,1).Length==1,"removed source pruned only within its root");
        a.gameObject.SetActive(false);Time.frameCount++;RaycastHit hit;
        Check(!Ray(out hit,1),"inactive owner cannot be targeted");
        a.gameObject.SetActive(true);box.gameObject.SetActive(false);Time.frameCount++;
        Check(!Ray(out hit,1),"inactive source cannot be targeted");
        box.gameObject.SetActive(true);Time.frameCount++;RabbitQueries.SyncRoot(a,new Collider[]{box},new bool[]{false});
        Check(!Ray(out hit,1)&&!box.enabled,"baseline disabled preserved independently of source isolation");
        int objects=GameObject.All.Count;RabbitQueries.SyncRoot(a,new Collider[]{box},new bool[0]);
        Check(GameObject.All.Count==objects,"invalid baseline length is nonmutating");
    }
    static void QueriesAndIntents()
    {
        Reset();var root=Root();var source=Shape<CapsuleCollider>(5);Sync(root,source);
        var wall=Shape<BoxCollider>(2);RaycastHit hit;Ray(out hit,1);
        Check(ReferenceEquals(hit.collider,wall),"nearer world wall occludes rabbit");
        wall.transform.position=new Vector3(0,0,100);Ray(out hit,1);
        Check(ReferenceEquals(hit.collider,Proxy(source)),"nearer private rabbit wins");
        Check(RabbitQueries.RaycastAll(Origin,Forward,200,1).Length==2,"All merges both scenes");
        source.gameObject.layer=3;Time.frameCount++;Ray(out hit,1);
        Check(ReferenceEquals(hit.collider,wall),"source layer respected");
        Ray(out hit,1<<3);Check(ReferenceEquals(hit.collider,Proxy(source)),"updated source layer copied");
        source.isTrigger=true;Physics.queriesHitTriggers=false;Time.frameCount++;
        Check(!Ray(out hit,1<<3),"global trigger setting respected");
        Check(RabbitQueries.OverlapBox(Origin,new Vector3(10,10,10),new Quaternion(),1<<3,QueryTriggerInteraction.Collide).Length==1,"explicit trigger mode passed to private overlap");
        Reset();root=Root();var s1=Shape<CapsuleCollider>(5);var s2=Shape<CapsuleCollider>(1);Sync(root,s1,s2);
        Shape<BoxCollider>(100);Shape<BoxCollider>(2);
        var result=new RaycastHit[2];int n=RabbitQueries.RaycastNonAlloc(Origin,Forward,result,200,1);
        Check(n==2&&((result[0].distance==1&&result[1].distance==2)||(result[0].distance==2&&result[1].distance==1)),"unsorted world/nonalloc merge retains exact nearest set");
        Check(RabbitQueries.RaycastNonAlloc(Origin,Forward,new RaycastHit[0],200,1)==0,"zero-capacity nonalloc");
        var all=new RaycastHit[8];all[7].distance=777;n=RabbitQueries.RaycastNonAlloc(Origin,Forward,all,200,1);
        Check(n==4&&all[7].distance==777,"nonalloc count and outside-count storage");
        Check(RabbitQueries.OverlapBox(Origin,new Vector3(200,200,200),new Quaternion(),1,QueryTriggerInteraction.Ignore).Length==4,"overlap merges both scenes");
        int shots=0,melee=0;var weapon=new GameObject().AddComponent<weaponscript>();var tool=new GameObject().AddComponent<meleeweaponscript>();
        RabbitQueries.WeaponIntent=delegate(weaponscript w,int index,Vector3 o,Vector3 d){Check(ReferenceEquals(w,weapon)&&index==7&&o==Origin&&d==Forward,"weapon intent exact arguments");shots++;};
        RabbitQueries.MeleeIntent=delegate(meleeweaponscript w,Vector3 c,Vector3 half,Quaternion r){Check(ReferenceEquals(w,tool)&&half==new Vector3(200,200,200),"melee intent exact arguments");melee++;};
        StableGameBridge.RabbitWeaponRaycast(Origin,Forward,result,200,1,weapon,7);
        StableGameBridge.RabbitMeleeOverlap(Origin,new Vector3(200,200,200),new Quaternion(),1,QueryTriggerInteraction.Ignore,tool);
        Check(shots==1&&melee==1,"one intent per operation not per bone");
        RabbitQueries.Clear();StableGameBridge.RabbitWeaponRaycast(Origin,Forward,result,200,1,weapon,7);
        StableGameBridge.RabbitMeleeOverlap(Origin,new Vector3(200,200,200),new Quaternion(),1,QueryTriggerInteraction.Ignore,tool);
        Check(shots==1&&melee==1,"inactive module emits no intents");
    }
    static void FailureAndForces()
    {
        foreach(int failure in new[]{0,1,2})
        {
            Reset();var r=Root();var c=Shape<CapsuleCollider>(2);
            SceneManager.FailCreate=failure==0;SceneManager.FailMove=failure==1;SceneManager.DefaultPrivate=failure==2;
            Sync(r,c);
            Check(!RabbitQueries.Active&&SceneManager.GetActiveScene().Handle==1,"failed private setup preserves active world");
            Check(SceneManager.Unloaded.Count==(failure==0?0:1)&&r!=null&&c!=null,"failed owned resources cleaned without deleting originals");
            SceneManager.FailCreate=SceneManager.FailMove=SceneManager.DefaultPrivate=false;
            RabbitQueries.Clear();Sync(r,c);
            Check(RabbitQueries.Active,"quick reconnect after failed setup");
        }
        Reset();var root=Root();var source=Shape<CapsuleCollider>(2);Sync(root,source);
        int first=Proxy(source).gameObject.scene.Handle;RabbitQueries.Clear();Sync(root,source);
        Check(Proxy(source).gameObject.scene.Handle!=first&&SceneManager.GetActiveScene().Handle==1,"quick reconnect uses new owned scene handle");
        var body=new GameObject().AddComponent<Rigidbody>();body.isKinematic=true;
        var f=new Vector3(1,2,3);var p=new Vector3(4,5,6);
        StableGameBridge.RabbitQueryForce(body,f);StableGameBridge.RabbitQueryImpulse(body,f,ForceMode.Impulse);
        StableGameBridge.RabbitQueryForceAt(body,f,p);StableGameBridge.RabbitQueryImpulseAt(body,f,p,ForceMode.Impulse);
        Check(body.Calls==0,"all kinematic force writes suppressed");
        body.isKinematic=false;StableGameBridge.RabbitQueryForce(body,f);
        Check(body.Calls==1&&body.Force==f&&body.Mode==ForceMode.Force,"dynamic force preserved");
        StableGameBridge.RabbitQueryImpulse(body,f,ForceMode.Impulse);
        Check(body.Calls==2&&body.Mode==ForceMode.Impulse,"dynamic impulse mode preserved");
        StableGameBridge.RabbitQueryForceAt(body,f,p);Check(body.Calls==3&&body.Point==p,"force position preserved");
        StableGameBridge.RabbitQueryImpulseAt(body,f,p,ForceMode.Impulse);
        Check(body.Calls==4&&body.Point==p&&body.Mode==ForceMode.Impulse,"impulse position and mode preserved");
        bool threw=false;try{StableGameBridge.RabbitQueryForce(null,f);}catch(NullReferenceException){threw=true;}
        Check(threw,"ordinary null body behavior preserved");
        RabbitQueries.Clear();
    }
    public static int Main()
    {
        try{GeometryAndMapping();ShapesAndRoots();QueriesAndIntents();FailureAndForces();Console.WriteLine("PASS "+checks+" rabbit query lifecycle/model checks");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
