using System;
using System.Collections.Generic;
using Steamworks;
using TldMp;
using UnityEngine;
using Unity.Collections;
namespace UnityEngine
{
    public class Object
    {
        static int nextId=1;public readonly int instanceId=nextId++;
        public static readonly List<GameObject> destroyed=new List<GameObject>();
        public int GetInstanceID(){return instanceId;}
        public static void Destroy(Object o){var g=o as GameObject;if(g!=null)destroyed.Add(g);}
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero{get{return new Vector3(0,0,0);}}public static Vector3 up{get{return new Vector3(0,1,0);}}
        public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator*(Vector3 a,float s){return new Vector3(a.x*s,a.y*s,a.z*s);}
        public float magnitude{get{return (float)Math.Sqrt(x*x+y*y+z*z);}}
        public override string ToString(){return "("+x+","+y+","+z+")";}
    }
    public struct Vector3d{public double x,y,z;public Vector3d(double a,double b,double c){x=a;y=b;z=c;}}
    public struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}
        public static Quaternion identity{get{return new Quaternion(0,0,0,1);}}
        public static Quaternion Inverse(Quaternion q){return new Quaternion(-q.x,-q.y,-q.z,q.w);}
        public static Vector3 operator*(Quaternion q,Vector3 v){return v;} // tests never rotate
        public static Quaternion operator*(Quaternion a,Quaternion b){return b;} // tests never rotate
    }
    public enum RigidbodyInterpolation{None,Interpolate}
    public enum PhysicMaterialCombine{Minimum,Average}
    public class PhysicMaterial{public float bounciness,dynamicFriction,staticFriction;public PhysicMaterialCombine bounceCombine,frictionCombine;public PhysicMaterial(string n){}}
    public struct Bounds
    {
        public Vector3 center,extents;
        public Bounds(Vector3 c,Vector3 size){center=c;extents=new Vector3(size.x*.5f,size.y*.5f,size.z*.5f);}
        public bool Intersects(Bounds o){return Math.Abs(center.x-o.center.x)<=extents.x+o.extents.x&&Math.Abs(center.y-o.center.y)<=extents.y+o.extents.y&&Math.Abs(center.z-o.center.z)<=extents.z+o.extents.z;}
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform{get{return gameObject==null?null:gameObject.transform;}}
        public T GetComponent<T>() where T:Component{return gameObject==null?null:gameObject.GetComponent<T>();}
        public T GetComponentInParent<T>() where T:class, new(){Transform t=transform;while(t!=null){var c=t.gameObject.GetComponent<Component>();if(c is T)return c as T;t=t.parent;}return null;}
    }
    public class Transform:Component
    {
        public string name;public Transform parent;
        public Vector3 position,lossyScale=new Vector3(1,1,1);public Quaternion rotation=Quaternion.identity;
        public Vector3 InverseTransformPoint(Vector3 p){return p-position;}
        public Vector3 TransformPoint(Vector3 p){return new Vector3(p.x*lossyScale.x,p.y*lossyScale.y,p.z*lossyScale.z)+position;}
        public bool IsChildOf(Transform t){var c=this;while(c!=null){if(c==t)return true;c=c.parent;}return false;}
    }
    public class GameObject:Object
    {
        public string name;public int layer;public bool activeInHierarchy=true;
        public readonly Dictionary<Type,Component> components=new Dictionary<Type,Component>();
        public readonly Transform transform;
        public GameObject(string n){name=n;transform=new Transform{gameObject=this};components[typeof(Transform)]=transform;}
        public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components[typeof(T)]=c;return c;}
        public T GetComponent<T>() where T:Component{Component c;return components.TryGetValue(typeof(T),out c)?(T)c:null;}
    }
    public class Collider:Component{public bool enabled=true,isTrigger,hasModifiableContacts;public Bounds bounds;public Rigidbody attachedRigidbody;public PhysicMaterial material;}
    public class CapsuleCollider:Collider{public float radius,height;public int direction;public Vector3 center;}
    public class Rigidbody:Component{public bool isKinematic;public RigidbodyInterpolation interpolation;public Vector3 position,velocity;public Quaternion rotation=Quaternion.identity;
        public void MovePosition(Vector3 p){position=p;transform.position=p;}public void MoveRotation(Quaternion q){rotation=q;}}
    public class ModifiableContactPair
    {
        public int colliderInstanceID,otherColliderInstanceID,contactCount,ignored;
        public void IgnoreContact(int i){ignored++;}
    }
    public struct PhysicsScene{}
    public static class Physics
    {
        public static readonly List<Tuple<int,int>> ignored=new List<Tuple<int,int>>();
        public static readonly List<Tuple<int,int>> restored=new List<Tuple<int,int>>();
        static readonly Dictionary<Tuple<Collider,Collider>,bool> flags=new Dictionary<Tuple<Collider,Collider>,bool>();
        public static void IgnoreCollision(Collider a,Collider b,bool ig){flags[Tuple.Create(a,b)]=ig;if(ig)ignored.Add(Tuple.Create(a.GetInstanceID(),b.GetInstanceID()));else restored.Add(Tuple.Create(a.GetInstanceID(),b.GetInstanceID()));}
        public static bool IsIgnored(Collider a,Collider b){bool f;return flags.TryGetValue(Tuple.Create(a,b),out f)&&f;}
        public static bool GetIgnoreCollision(Collider a,Collider b){return IsIgnored(a,b);}
        public static event Action<PhysicsScene,NativeArray<ModifiableContactPair>> ContactModifyEvent,ContactModifyEventCCD;
    }
    public static class Time{public static float realtimeSinceStartup,fixedDeltaTime;}
    public static class Mathf{public static float Abs(float v){return Math.Abs(v);}public static float Max(float a,float b){return Math.Max(a,b);}}
    public static class Debug{public static readonly List<string> logs=new List<string>();public static void Log(string s){logs.Add(s);}public static void LogWarning(string s){logs.Add("W:"+s);}}
    public class MonoBehaviour:Component{}
}
namespace Unity.Collections
{
    public struct NativeArray<T>
    {
        T[] a;public NativeArray(T[] src){a=src;}public int Length{get{return a==null?0:a.Length;}}public T this[int i]{get{return a[i];}}
        public static implicit operator NativeArray<T>(T[] src){return new NativeArray<T>(src);}
    }
}
namespace Steamworks
{
    public struct CSteamID
    {
        public ulong id;public CSteamID(ulong v){id=v;}
        public static implicit operator ulong(CSteamID s){return s.id;}
        public static implicit operator CSteamID(ulong v){return new CSteamID(v);}
    }
}
public class seatscript{}
public class breakablescript{public float health;}
public class sensoryscript{public bool alive=true;}
public class aiscript{public bool alive=true;}
public class nyulragdollscript{public bool ragdoll;public Collider[] animColliders;}
public class nyulscript:MonoBehaviour
{
    public bool started=true,attacking;public sensoryscript senses=new sensoryscript();public aiscript AI=new aiscript();
    public breakablescript breakable=new breakablescript();public nyulragdollscript ragdoll=new nyulragdollscript();
    public movescript move;
}
public class movescript:MonoBehaviour{public Rigidbody RB;}
public class tosaveitemscript:MonoBehaviour{public uint idInSave;public int id;public Transform useThisForPos;public bool mppickedup;}
public class savedatascript{public static savedatascript s;public readonly Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();}
public class mainscript
{
    public static mainscript s;public fpscontroller player;
    public static Vector3d offset;
    public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d(p.x+offset.x,p.y+offset.y,p.z+offset.z);}
    public static Vector3 UnityPosFromGlobal(Vector3d p){return new Vector3((float)(p.x-offset.x),(float)(p.y-offset.y),(float)(p.z-offset.z));}
}
public class fpscontroller:MonoBehaviour{public Rigidbody RB;public Collider[] C;public CapsuleCollider CBody;public bool loadedmove=true;}
public class mpplayerscript:MonoBehaviour{public Collider[] cols;public CapsuleCollider CBody;public ulong steamID;public bool readyToMove=true;public Transform THead;}
public class syncScript{public static syncScript s;public class playerSyncClass{public ulong steamID;public GameObject player;public mpplayerscript mpscript;}}
namespace TldMp
{
    internal static class ModDiagnostics{internal static bool On;}
    internal class StablePeer{public bool Ready=true;public ulong Session=200;}
    internal class StableNode{public bool IsHost,Ready=true,Compatible=true;public string Error;public ulong Own=10,Session=100,HostSession=100;public readonly Dictionary<ulong,StablePeer> Peers=new Dictionary<ulong,StablePeer>();}
    public struct RemotePose{internal double X,Y,Z,Qx,Qy,Qz,Qw;}
    internal static class StableGameBridge
    {
        internal static StableNode Node=new StableNode();
        internal static bool WorldScene(){return true;}
        internal static readonly Dictionary<uint,RemotePose> Poses=new Dictionary<uint,RemotePose>();
        internal static bool TryRabbitPose(uint id,tosaveitemscript item,double now,out RemotePose pose)
        {
            pose=new RemotePose();
            tosaveitemscript canonical;
            if(savedatascript.s==null||!savedatascript.s.items.TryGetValue(id,out canonical)||!ReferenceEquals(canonical,item)||
               !RabbitMotionEligible(item))return false;
            return Poses.TryGetValue(id,out pose);
        }
        internal static void ForEachGuestMotionId(List<uint> dst){dst.Clear();foreach(var kv in Poses)dst.Add(kv.Key);}
        internal static bool RabbitMotionEligible(tosaveitemscript item){return item!=null&&!item.mppickedup;}
        internal static bool RabbitContactCurrent(uint id,tosaveitemscript item,nyulscript root,double now)
        {
            tosaveitemscript canonical;
            return savedatascript.s!=null&&savedatascript.s.items.TryGetValue(id,out canonical)&&ReferenceEquals(canonical,item)&&
                   Poses.ContainsKey(id)&&root!=null;
        }
    }
    internal static class RabbitReplicas
    {
        internal static nyulscript RabbitRoot(tosaveitemscript item){return item.GetComponent<nyulscript>();}
    }
    internal static class RabbitMetadataSync
    {
        internal static readonly Dictionary<uint,nyulscript> AppliedRoots=new Dictionary<uint,nyulscript>();
        internal static readonly HashSet<uint> DeadIds=new HashSet<uint>();
        internal static bool TryApplied(uint id,out RabbitMetadata meta,out nyulscript root)
        {
            meta=new RabbitMetadata();root=null;
            if(!AppliedRoots.TryGetValue(id,out root))return false;
            meta.Flags=(byte)(DeadIds.Contains(id)?0:2);
            return true;
        }
    }
    internal static class RabbitPlayers
    {
        internal static void ProbeDamage(nyulscript root){}
        internal sealed class Actor{internal ulong Id,Session;internal GameObject Root;internal mpplayerscript Mp;}
        internal static readonly Dictionary<ulong,Actor> actors=new Dictionary<ulong,Actor>();
        internal static bool TryGet(ulong id,out Actor a){return actors.TryGetValue(id,out a);}
    }
}
internal static class RabbitContactTests
{
    static int checks;static void Check(bool x,string m){checks++;if(!x)throw new Exception("CHECK "+checks+": "+m);}
    static void Near(float a,float e,string m){Check(Math.Abs(a-e)<.001f,m+" ("+a+" vs "+e+")");}
    static tosaveitemscript MakeRabbit(uint id,CapsuleCollider main,out nyulscript root)
    {
        var go=new GameObject("rabbit");var item=go.AddComponent<tosaveitemscript>();item.idInSave=id;item.id=7;
        var anchor=new GameObject("anchor");item.useThisForPos=anchor.transform;
        root=go.AddComponent<nyulscript>();
        var rb=go.AddComponent<movescript>();rb.RB=go.AddComponent<Rigidbody>();
        main.gameObject=new GameObject("MainCollider");main.transform.parent=anchor.transform;
        main.transform.position=anchor.transform.position+new Vector3(0,.5f,0);
        root.ragdoll.animColliders=new Collider[]{main};
        savedatascript.s.items[id]=item;return item;
    }
    static void Feed(uint id,float x,float z)
    {
        var g=mainscript.GlobalFromUnityPos(new Vector3(x,0,z));
        StableGameBridge.Poses[id]=new RemotePose{X=g.x,Y=g.y,Z=g.z,Qw=1};
    }
    static void Main()
    {
        var capShape=new CapsuleCollider{radius=.3f,height=1f,direction=2,center=new Vector3(0,.5f,0)};
        // ---------- guest proxies follow the shared motion buffer ----------
        savedatascript.s=new savedatascript();mainscript.s=new mainscript();
        var playerGO=new GameObject("player");var player=playerGO.AddComponent<fpscontroller>();
        player.RB=playerGO.AddComponent<Rigidbody>();
        player.CBody=playerGO.AddComponent<CapsuleCollider>();player.CBody.attachedRigidbody=player.RB;
        player.CBody.bounds=new Bounds(new Vector3(50,0,50),new Vector3(1,2,1));
        player.C=new Collider[]{player.CBody};
        mainscript.s.player=player;
        nyulscript root;mainscript.offset=new Vector3d(0,0,0);
        var item=MakeRabbit(9,capShape,out root);
        Feed(9,5,5);
        StableGameBridge.Node.IsHost=false;
        RabbitContact.Reset();
        RabbitMetadataSync.AppliedRoots[9]=root;
        RabbitContact.FixedTick(true,1.0);
        Check(RabbitContact.ActiveProxies==1,"living replica gets one proxy");
        var proxy=RabbitContactTests.FindProxy(9);
        Check(proxy.Go.transform.parent==null,"proxy unparented so replicas cannot disable it");
        Check(proxy.RB.isKinematic,"proxy body kinematic");
        Check(proxy.Cap.hasModifiableContacts,"proxy opts into contact modification");
        Check(proxy.Go.layer==2,"proxy on Ignore Raycast layer like native main");
        Near(proxy.Cap.radius,.3f,"capsule radius copied");
        Near(proxy.Cap.height,1f,"capsule height copied");
        Check(proxy.Cap.direction==2,"capsule direction copied");
        Near(proxy.RB.position.x,5f,"proxy at sampled pose x");
        Near(proxy.RB.position.z,5f,"proxy follows sampled z");
        Near(proxy.RB.position.y,1f,"proxy keeps world-metre collider center offset");
        Feed(9,7,5);
        RabbitContact.FixedTick(true,1.05);
        Near(proxy.RB.position.x,7f,"proxy follows buffered pose, not visual snap");
        // lifecycle: metadata death / grabbed / replaced / stale / disconnected
        RabbitMetadataSync.DeadIds.Add(9);
        RabbitContact.FixedTick(true,1.6);
        Check(RabbitContact.ActiveProxies==0&&UnityEngine.Object.destroyed.Count>=1,"dead senses destroys proxy");
        RabbitMetadataSync.DeadIds.Remove(9);
        RabbitContact.FixedTick(true,1.7);
        Check(RabbitContact.ActiveProxies==1,"alive again re-creates");
        item.mppickedup=true;
        RabbitContact.FixedTick(true,1.8);
        Check(RabbitContact.ActiveProxies==0,"picked up destroys proxy");
        item.mppickedup=false;
        RabbitContact.FixedTick(true,1.9);
        Check(RabbitContact.ActiveProxies==1,"dropped re-creates");
        StableGameBridge.Poses.Remove(9);
        RabbitContact.FixedTick(true,3.0);
        Check(RabbitContact.ActiveProxies==0,"stale stream destroys proxy");
        Feed(9,5,5);RabbitContact.FixedTick(true,3.1);
        Check(RabbitContact.ActiveProxies==1,"stream resume re-creates");
        RabbitContact.FixedTick(false,3.2);
        Check(RabbitContact.ActiveProxies==0,"disconnect destroys all");
        // deep overlap at creation defers until separable
        RabbitContact.Reset();savedatascript.s=new savedatascript();
        Feed(9,50,50);
        var item2=MakeRabbit(9,capShape,out root);
        RabbitMetadataSync.AppliedRoots[9]=root;
        player.CBody.bounds=new Bounds(new Vector3(50,0,50),new Vector3(1,2,1));
        RabbitContact.FixedTick(true,4.0);
        Check(RabbitContact.ActiveProxies==1&&RabbitContactTests.FindProxy(9).WaitingClear,"deep overlap at spawn defers enable");
        Check(!RabbitContactTests.FindProxy(9).Cap.enabled,"deferred proxy collider stays off");
        Check(!RabbitContactTests.PublishedProxies(),"unpublished colliders are never enabled");
        player.CBody.bounds=new Bounds(new Vector3(500,0,500),new Vector3(1,2,1));
        RabbitContact.FixedTick(true,4.1);
        Check(!RabbitContactTests.FindProxy(9).WaitingClear&&RabbitContactTests.FindProxy(9).Cap.enabled,"separable pose enables proxy");
        // ---------- contact filter: only canonical local player blocks ----------
        var proxyId=RabbitContactTests.FindProxy(9).Cap.GetInstanceID();
        var playerId=player.CBody.GetInstanceID();
        var pair=new ModifiableContactPair{colliderInstanceID=proxyId,otherColliderInstanceID=playerId,contactCount=2};
        RabbitContact.OnContacts(new PhysicsScene(),new[]{pair});
        Check(pair.ignored==0,"local player contact kept");
        var wall=new ModifiableContactPair{colliderInstanceID=proxyId,otherColliderInstanceID=424242,contactCount=3};
        RabbitContact.OnContacts(new PhysicsScene(),new[]{wall});
        Check(wall.ignored==3,"non-player contact dropped");
        var rev=new ModifiableContactPair{colliderInstanceID=playerId,otherColliderInstanceID=proxyId,contactCount=1};
        RabbitContact.OnContacts(new PhysicsScene(),new[]{rev});
        Check(rev.ignored==0,"reversed player pair kept");
        var pp=new ModifiableContactPair{colliderInstanceID=proxyId,otherColliderInstanceID=proxyId,contactCount=2};
        RabbitContact.OnContacts(new PhysicsScene(),new[]{pp});
        Check(pp.ignored==2,"proxy-vs-proxy dropped");
        var world=new ModifiableContactPair{colliderInstanceID=1,otherColliderInstanceID=2,contactCount=2};
        RabbitContact.OnContacts(new PhysicsScene(),new[]{world});
        Check(world.ignored==0,"non-proxy pair untouched");
        // ---------- host scoped isolation for canonical remote actors ----------
        RabbitContact.Reset();savedatascript.s=new savedatascript();
        var item3=MakeRabbit(9,capShape,out root);
        RabbitContact.ObserveRabbit(root);
        var rabbitCol=new GameObject("bone").AddComponent<CapsuleCollider>();root.ragdoll.animColliders=new Collider[]{capShape,rabbitCol};
        var remoteGO=new GameObject("remote");var mp=remoteGO.AddComponent<mpplayerscript>();
        mp.steamID=42;mp.CBody=remoteGO.AddComponent<CapsuleCollider>();
        var bone=new GameObject("rbone").AddComponent<CapsuleCollider>();bone.transform.parent=remoteGO.transform;
        var trig=new GameObject("trig").AddComponent<CapsuleCollider>();trig.isTrigger=true;trig.transform.parent=remoteGO.transform;
        mp.cols=new Collider[]{bone,trig};
        RabbitPlayers.actors[42]=new RabbitPlayers.Actor{Id=42,Session=200,Mp=mp,Root=remoteGO};
        StableGameBridge.Node.IsHost=true;StableGameBridge.Node.Peers[42]=new StablePeer{Session=200};
        Physics.ignored.Clear();Physics.restored.Clear();
        RabbitContact.FixedTick(true,5.0);
        Check(Physics.IsIgnored(rabbitCol,mp.CBody)&&Physics.IsIgnored(rabbitCol,bone),"canonical remote solid colliders ignored vs rabbit");
        Check(!Physics.IsIgnored(rabbitCol,trig),"remote trigger perception untouched");
        Check(!Physics.IsIgnored(rabbitCol,player.CBody),"local player contact untouched");
        Check(RabbitContact.IgnoreForNavigation(mp.CBody)&&RabbitContact.IgnoreForNavigation(bone),"remote solids navigation-transparent");
        Check(!RabbitContact.IgnoreForNavigation(player.CBody)&&!RabbitContact.IgnoreForNavigation(trig),"local player and triggers block navigation");
        int pairs=RabbitContactTests.PairCount();
        RabbitContact.FixedTick(true,5.6);
        Check(RabbitContactTests.PairCount()==pairs,"refresh does not duplicate pairs");
        RabbitPlayers.actors.Remove(42);
        RabbitContact.FixedTick(true,6.2);
        Check(!Physics.IsIgnored(rabbitCol,mp.CBody),"actor loss restores only owned pairs");
        Check(!RabbitContact.IgnoreForNavigation(mp.CBody),"gone actor navigates normally again");
        // world recenter: proxy pose stays mapped to the live global stream
        RabbitContact.Reset();StableGameBridge.Node.IsHost=false;savedatascript.s=new savedatascript();
        mainscript.offset=new Vector3d(1000,0,0);
        MakeRabbit(9,capShape,out root);RabbitMetadataSync.AppliedRoots[9]=root;Feed(9,0,0);
        RabbitContact.FixedTick(true,7.0);
        Near(RabbitContactTests.FindProxy(9).RB.position.x,0f,"recentered global pose maps to unity origin");
        mainscript.offset=new Vector3d(0,0,0);
        Console.WriteLine("PASS "+checks+" rabbit contact checks");
    }
    // access helpers (reflection-free: proxies dictionary is internal to the module)
    internal static RabbitContact.Proxy FindProxy(uint id)
    {
        var f=typeof(RabbitContact).GetField("proxies",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        var d=(System.Collections.IDictionary)f.GetValue(null);return (RabbitContact.Proxy)d[id];
    }
    internal static int PairCount(){var f=typeof(RabbitContact).GetField("hostPairs",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);return ((System.Collections.ICollection)f.GetValue(null)).Count;}
    internal static bool PublishedProxies(){var ids=RabbitContact.SnapshotProxyIds;return ids!=null&&ids.Length>0;}
}
