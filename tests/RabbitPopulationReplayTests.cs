using System;
using System.Collections.Generic;
using TldMp;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero{get{return new Vector3();}}
        public static Vector3 up{get{return new Vector3(0,1,0);}}
        public static Vector3 down{get{return new Vector3(0,-1,0);}}
        public float sqrMagnitude{get{return x*x+y*y+z*z;}}
        public Vector3 normalized{get{float n=(float)Math.Sqrt(sqrMagnitude);return n>0?this*(1f/n):zero;}}
        public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator*(Vector3 a,float v){return new Vector3(a.x*v,a.y*v,a.z*v);}
        public static bool operator==(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        public static bool operator!=(Vector3 a,Vector3 b){return !(a==b);}
        public override bool Equals(object o){return o is Vector3&&this==(Vector3)o;}
        public override int GetHashCode(){return x.GetHashCode()^y.GetHashCode()^z.GetHashCode();}
        public static float Dot(Vector3 a,Vector3 b){return a.x*b.x+a.y*b.y+a.z*b.z;}
        public static Vector3 ProjectOnPlane(Vector3 a,Vector3 n){float d=n.sqrMagnitude;return d>0?a-n*(Dot(a,n)/d):a;}
        public static float Angle(Vector3 a,Vector3 b)
        {
            double n=Math.Sqrt((double)a.sqrMagnitude*b.sqrMagnitude);
            return n>0?(float)(Math.Acos(Math.Max(-1,Math.Min(1,Dot(a,b)/n)))*180/Math.PI):0;
        }
    }
    public struct Quaternion{public static Quaternion Euler(float x,float y,float z){return new Quaternion();}}
    public struct RaycastHit
    {
        Vector3 p,n;
        public Vector3 point{get{return p;}set{p=value;}}
        public Vector3 normal{get{return n;}set{n=value;}}
    }
    public struct LayerMask
    {
        public int value;
        public static implicit operator int(LayerMask v){return v.value;}
    }
    public enum QueryTriggerInteraction{UseGlobal,Ignore,Collide}
    public static class Time{public static float time,realtimeSinceStartup=100;}
    public static class Random
    {
        public static float Range(float min,float max){return min;}
        public static Vector3 onUnitSphere{get{return new Vector3(0,0,-1);}}
    }
    public static class Physics
    {
        public static bool Ground=true,Blocked;
        public static int Rays,Spheres;
        public static bool Raycast(Vector3 p,Vector3 d,out RaycastHit hit,float range,int mask,QueryTriggerInteraction q)
        {
            Rays++;hit=new RaycastHit{point=new Vector3(p.x,0,p.z),normal=Vector3.up};return Ground;
        }
        public static bool CheckSphere(Vector3 p,float radius,int mask,QueryTriggerInteraction q){Spheres++;return Blocked;}
    }
    public class Object
    {
        public static int Instantiates;
        public static Action BeforeInstantiate;
        public static readonly List<Vector3> SpawnPositions=new List<Vector3>();
        public static T Instantiate<T>(T source,Vector3 p,Quaternion r) where T:Object
        {
            if(BeforeInstantiate!=null)BeforeInstantiate();
            var old=source as GameObject;
            if(old==null)throw new InvalidOperationException("Unexpected non-GameObject prefab");
            var copy=new GameObject();copy.activeSelf=old.activeSelf;
            foreach(var pair in old.Components)copy.Add(pair.Value.Clone());
            copy.transform.position=p;Instantiates++;SpawnPositions.Add(p);
            return (T)(Object)copy;
        }
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform;
        public T GetComponent<T>() where T:Component{return gameObject.GetComponent<T>();}
        public virtual Component Clone(){return (Component)Activator.CreateInstance(GetType());}
    }
    public class Transform:Component
    {
        Vector3 p,f=new Vector3(0,0,1);
        public Vector3 position{get{return p;}set{p=value;}}
        public Vector3 forward{get{return f;}set{f=value;}}
        public Transform parent;
        public Vector3 InverseTransformDirection(Vector3 p){return p;}
    }
    public class GameObject:Object
    {
        public bool activeSelf=true;
        public bool activeInHierarchy{get{return activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);}}
        public Transform transform;
        public readonly Dictionary<Type,Component> Components=new Dictionary<Type,Component>();
        public GameObject(){transform=new Transform{gameObject=this};transform.transform=transform;}
        public T GetComponent<T>() where T:Component{Component c;return Components.TryGetValue(typeof(T),out c)?(T)c:null;}
        public Component Add(Component c){c.gameObject=this;c.transform=transform;Components[c.GetType()]=c;return c;}
        public T Attach<T>(T c) where T:Component{Add(c);return c;}
    }
    public class MonoBehaviour:Component{}
}
namespace Steamworks
{
    public struct CSteamID
    {
        public ulong Value;
        public CSteamID(ulong value){Value=value;}
        public override int GetHashCode(){return Value.GetHashCode();}
        public override bool Equals(object o){return o is CSteamID&&((CSteamID)o).Value==Value;}
    }
}
public class tosaveitemscript:MonoBehaviour
{
    public int id;
    public Transform useThisForPos;
    public static int FStarts;
    public static uint Next=1;
    public void FStart(){FStarts++;savedatascript.s.items.Add(Next++,this);}
    public override Component Clone(){return new tosaveitemscript{id=id};}
}
public class movecreaturescript:MonoBehaviour{}
public class nyulscript:MonoBehaviour{public bool alive=true;}
public class fpscontroller:MonoBehaviour{public bool loadedmove=true;}
public class savedatascript
{
    public static savedatascript s=new savedatascript();
    public readonly Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();
}
public class syncScript
{
    public sealed class playerSyncClass{public float lastTime;public GameObject player;}
    public static syncScript s=new syncScript();
    public float playerRemoveTime=30;
    public readonly Dictionary<Steamworks.CSteamID,playerSyncClass> players=new Dictionary<Steamworks.CSteamID,playerSyncClass>();
}
public class aispawn:MonoBehaviour
{
    public Transform bodyRot;
    public GameObject AIPrefab,GO;
    public List<GameObject> AIs=new List<GameObject>();
    public int maxAINum=4;
    public float chance=1,minTime=50,maxTime=100,minCount=1,maxCount=1,minDist=5,maxDist=10,radius=1,halfFov=90;
    public Vector3 offset,pos;
    public RaycastHit hit;
    public LayerMask maskSurface,maskSphere;
    public void TrySpawn(){throw new InvalidOperationException("Native TrySpawn was not extracted");}
    public Vector3 RandomPlace(){throw new InvalidOperationException("Native RandomPlace was not extracted");}
    public Vector3 CanSpawn(Vector3 p){throw new InvalidOperationException("Native CanSpawn was not extracted");}
    public void Spawn(Vector3 p){throw new InvalidOperationException("Native Spawn was not extracted");}
}
public static class PatchOptions{public static bool Rabbits=true;}
namespace TldMp
{
    public partial class StableGameBridge{public static bool Connected;public static StableNode node;}
}
public static class RabbitPopulationReplayTests
{
    static bool candidate;
    static int checks;
    static void Check(bool ok,string name){checks++;if(!ok)throw new Exception("FAIL "+name);}
    sealed class Lab
    {
        public aispawn Spawner;
        public fpscontroller Player;
        public StableNode Node;
        public Transform Original;
        public Lab()
        {
            RabbitPopulation.Reset();
            savedatascript.s=new savedatascript();syncScript.s=new syncScript();
            UnityEngine.Object.Instantiates=0;UnityEngine.Object.BeforeInstantiate=null;UnityEngine.Object.SpawnPositions.Clear();
            tosaveitemscript.FStarts=0;tosaveitemscript.Next=1;
            Physics.Ground=true;Physics.Blocked=false;Physics.Rays=Physics.Spheres=0;
            Time.realtimeSinceStartup=100;PatchOptions.Rabbits=true;
            Player=new GameObject().Attach(new fpscontroller());
            Player.transform.position=new Vector3(30,0,0);
            Original=new GameObject().transform;
            Spawner=new GameObject().Attach(new aispawn{bodyRot=Original});
            var prefab=new GameObject();
            prefab.Attach(new tosaveitemscript{id=103});prefab.Attach(new nyulscript());prefab.Attach(new movecreaturescript());
            Spawner.AIPrefab=prefab;
            Node=new StableNode(10,10,999,11,"fixture",0);
            StableGameBridge.Connected=true;StableGameBridge.node=Node;
        }
        public void Tick(double now){RabbitPopulation.Tick(Spawner,Player,true,true,Node,now);}
        public void Peer(ulong id,ulong session,float x)
        {
            Node.Peers[id]=new StablePeer{Ready=true,Session=session};
            var go=new GameObject();go.transform.position=new Vector3(x,0,0);
            syncScript.s.players[new Steamworks.CSteamID(id)]=new syncScript.playerSyncClass{player=go,lastTime=100};
        }
        public tosaveitemscript Seed(float x,bool inactive,bool dead)
        {
            var go=new GameObject();go.transform.position=new Vector3(x,0,0);go.activeSelf=!inactive;
            var item=go.Attach(new tosaveitemscript{id=103});
            go.Attach(new movecreaturescript());go.Attach(new nyulscript{alive=!dead});item.FStart();return item;
        }
    }
    static void Admission()
    {
        var l=new Lab();StableGameBridge.Connected=false;
        l.Spawner.Spawn(new Vector3(1,0,0));
        Check(UnityEngine.Object.Instantiates==1&&tosaveitemscript.FStarts==1&&l.Spawner.AIs.Count==1,"SP native Spawn/FStart unchanged");
        l=new Lab();
        l.Spawner.Spawn(new Vector3(1,0,0));
        Check(UnityEngine.Object.Instantiates==(candidate?0:1),"online host direct Spawn requires attempt");
        l=new Lab();StableGameBridge.node=new StableNode(20,10,999,21,"fixture",0);
        l.Spawner.Spawn(new Vector3(1,0,0));
        Check(UnityEngine.Object.Instantiates==(candidate?0:1),"guest direct Spawn counterfactual/candidate");
        l=new Lab();l.Tick(0);
        Check(UnityEngine.Object.Instantiates==1&&tosaveitemscript.FStarts==1&&l.Spawner.AIs.Count==1,"host actual native TrySpawn through FStart");
        Check(savedatascript.s.items.Count==1&&UnityEngine.Object.SpawnPositions[0].z==-5,"spawn registers loaded object and native backwards candidate");
        Check(l.Spawner.bodyRot==l.Original&&UnityEngine.Object.SpawnPositions[0].x==0,"native bodyRot preserved instead of local transform");
        Check(!RabbitPopulation.Admit(l.Spawner,new Vector3(1,0,0)),"attempt closed after native return");
        l=new Lab();var other=new GameObject().Attach(new aispawn{AIPrefab=new GameObject()});
        Check(StableGameBridge.RabbitSpawnGate(other,new Vector3()),"nonrabbit unaffected");
    }
    static void Preconditions()
    {
        var l=new Lab();RabbitPopulation.Tick(l.Spawner,l.Player,true,false,l.Node,0);
        Check(Physics.Rays==0,"disconnected no native attempts");
        l=new Lab();RabbitPopulation.Tick(l.Spawner,l.Player,false,true,l.Node,0);
        Check(Physics.Rays==0,"no world no native attempts");
        l=new Lab();RabbitPopulation.Tick(l.Spawner,l.Player,true,true,null,0);
        Check(Physics.Rays==0,"no node no native attempts");
        l=new Lab();RabbitPopulation.Tick(l.Spawner,null,true,true,l.Node,0);
        Check(Physics.Rays==0,"no player no native attempts");
        l=new Lab();RabbitPopulation.Tick(null,l.Player,true,true,l.Node,0);
        Check(Physics.Rays==0,"no spawner no native attempts");
        l=new Lab();PatchOptions.Rabbits=false;l.Tick(0);
        Check(Physics.Rays==0,"option off no native attempts");
        l=new Lab();l.Player.loadedmove=false;l.Tick(0);
        Check(Physics.Rays==0,"world movement not ready");
        l=new Lab();l.Node=new StableNode(20,10,999,21,"fixture",0);l.Tick(0);
        Check(Physics.Rays==0,"guest core no native attempts");
        l=new Lab();l.Node.Stop("test");l.Tick(0);
        Check(Physics.Rays==0,"stopped host no native attempts");
        l=new Lab();l.Spawner.AIPrefab=new GameObject();l.Tick(0);
        Check(Physics.Rays==0,"nonrabbit core no native attempts");
        l=new Lab();Physics.Ground=false;l.Tick(0);
        Check(Physics.Rays>0&&UnityEngine.Object.Instantiates==0,"real native missing terrain rejection");
        l=new Lab();Physics.Blocked=true;l.Tick(0);
        Check(Physics.Spheres>0&&UnityEngine.Object.Instantiates==0,"real native obstructed sphere rejection");
        l=new Lab();l.Original.forward=new Vector3(0,0,-1);l.Tick(0);
        Check(Physics.Rays>0&&Physics.Spheres==0&&UnityEngine.Object.Instantiates==0,"real native inside FOV rejection");
    }
    static void Centers()
    {
        var l=new Lab();l.Peer(20,21,100);l.Spawner.minTime=l.Spawner.maxTime=.1f;
        l.Tick(0);l.Tick(.5);l.Tick(1);l.Tick(1.5);
        var p=UnityEngine.Object.SpawnPositions;
        Check(p.Count==4&&p[0].x==0&&p[1].x==100&&p[2].x==0&&p[3].x==100,"collision-free identities and fair rotation while always due");
        l=new Lab();l.Peer(20,21,100);l.Tick(0);l.Tick(.5);
        l.Node.Peers[20].Session=22;l.Tick(1);
        Check(UnityEngine.Object.SpawnPositions.Count==3&&UnityEngine.Object.SpawnPositions[2].x==100,"new peer session due without reusing old timer");
        l=new Lab();l.Peer(20,21,100);l.Node.Peers[20].Ready=false;l.Tick(0);l.Tick(.5);
        Check(UnityEngine.Object.Instantiates==1,"not-ready peer excluded");
        l.Node.Peers[20].Ready=true;syncScript.s.players[new Steamworks.CSteamID(20)].lastTime=0;l.Tick(1);
        Check(UnityEngine.Object.Instantiates==1,"stale peer excluded");
        syncScript.s.players[new Steamworks.CSteamID(20)].lastTime=100;l.Tick(1.5);
        Check(UnityEngine.Object.Instantiates==2&&UnityEngine.Object.SpawnPositions[1].x==100,"fresh peer admitted");
        l.Node.Peers.Remove(20);l.Tick(2);
        l.Peer(20,21,100);l.Tick(2.5);
        Check(UnityEngine.Object.Instantiates==3,"removed then returned center resets timer");
        l=new Lab();l.Peer(20,21,100);
        var remote=syncScript.s.players[new Steamworks.CSteamID(20)].player;
        var parent=new GameObject();parent.activeSelf=false;remote.transform.parent=parent.transform;
        l.Tick(0);l.Tick(.5);Check(UnityEngine.Object.Instantiates==1,"inactive hierarchy peer excluded");
        l=new Lab();l.Peer(20,21,100);syncScript.s.playerRemoveTime=float.NaN;l.Tick(0);l.Tick(.5);
        Check(UnityEngine.Object.Instantiates==1,"invalid staleness timeout fails closed");
    }
    static void Budgets()
    {
        var l=new Lab();l.Spawner.maxAINum=1;l.Peer(20,21,100);l.Seed(0,true,true);
        l.Tick(0);l.Tick(.5);
        Check(UnityEngine.Object.Instantiates==1&&UnityEngine.Object.SpawnPositions[0].x==100,"full local dead/inactive quota does not starve distant peer");
        l=new Lab();l.Spawner.maxAINum=1;l.Seed(20,false,false);l.Tick(0);
        Check(UnityEngine.Object.Instantiates==0,"radius exact boundary is inside");
        l=new Lab();l.Spawner.maxAINum=1;l.Peer(20,21,100);l.Seed(20.01f,false,false);l.Tick(0);
        Check(UnityEngine.Object.Instantiates==1&&UnityEngine.Object.SpawnPositions[0].x==0,"radius plus epsilon is outside");
        l=new Lab();l.Spawner.maxAINum=1;l.Peer(20,21,100);
        var anchored=l.Seed(1000,false,true);anchored.useThisForPos=new GameObject().transform;anchored.useThisForPos.position=new Vector3(5,0,0);
        l.Tick(0);Check(UnityEngine.Object.Instantiates==0,"ragdoll anchor not stale root governs local quota");
        l=new Lab();l.Spawner.maxAINum=10;l.Peer(20,21,100);l.Peer(30,31,200);l.Peer(40,41,300);
        for(int i=0;i<31;i++)l.Seed(1000+i,i%2==0,true);
        l.Spawner.minCount=l.Spawner.maxCount=5;l.Tick(0);
        Check(UnityEngine.Object.Instantiates==(candidate?1:5),"native multi-spawn global hard cap/counterfactual");
        Check(savedatascript.s.items.Count==(candidate?32:36),"all loaded dead and inactive records retained");
        int count=UnityEngine.Object.Instantiates;l.Tick(.5);
        Check(UnityEngine.Object.Instantiates==count,"full global quota blocks next center");
        l=new Lab();for(int i=0;i<3;i++)l.Seed(0,false,false);
        l.Spawner.minCount=l.Spawner.maxCount=5;l.Tick(0);
        Check(UnityEngine.Object.Instantiates==(candidate?1:5),"native multi-spawn local cap/counterfactual");
        l=new Lab();l.Spawner.maxAINum=1;l.Peer(20,21,0);l.Tick(0);l.Tick(.5);
        Check(UnityEngine.Object.Instantiates==1,"overlapping centers share loaded population");
    }
    static void InvalidAndLifecycle()
    {
        var bad=new Action<aispawn>[] {
            delegate(aispawn s){s.maxAINum=0;},delegate(aispawn s){s.radius=float.NaN;},
            delegate(aispawn s){s.offset=new Vector3(float.PositiveInfinity,0,0);},
            delegate(aispawn s){s.maxDist=float.MaxValue;},delegate(aispawn s){s.maxDist=1;},
            delegate(aispawn s){s.minTime=float.NaN;},delegate(aispawn s){s.halfFov=181;},
            delegate(aispawn s){s.minCount=float.PositiveInfinity;s.maxCount=float.PositiveInfinity;},
            delegate(aispawn s){s.maxCount=33;},delegate(aispawn s){s.chance=float.NaN;}
        };
        foreach(var change in bad){var b=new Lab();change(b.Spawner);b.Tick(0);Check(Physics.Rays==0,"invalid config rejected before native ray");}
        var l=new Lab();bool nan=false,infinity=false;
        UnityEngine.Object.BeforeInstantiate=delegate {
            nan=!RabbitPopulation.Admit(l.Spawner,new Vector3(float.NaN,0,0));
            infinity=!RabbitPopulation.Admit(l.Spawner,new Vector3(0,float.PositiveInfinity,0));
        };
        l.Tick(0);Check(nan&&infinity&&UnityEngine.Object.Instantiates==1,"nonfinite Spawn positions rejected INSIDE active attempt");
        l=new Lab();UnityEngine.Object.BeforeInstantiate=delegate{throw new InvalidOperationException("injected");};
        bool thrown=false;try{l.Tick(0);}catch(InvalidOperationException){thrown=true;}
        Check(thrown&&l.Spawner.bodyRot==l.Original&&!RabbitPopulation.Admit(l.Spawner,new Vector3()),"exception restores native context and closes admission");
        l=new Lab();l.Tick(0);RabbitPopulation.Reset();l.Tick(.5);
        Check(UnityEngine.Object.Instantiates==2,"explicit reset clears timer and identity");
        l=new Lab();l.Tick(0);RabbitPopulation.Tick(l.Spawner,l.Player,false,true,l.Node,.5);l.Tick(1);
        Check(UnityEngine.Object.Instantiates==2,"world stop/restart clears timer");
        l=new Lab();l.Tick(0);l.Node=new StableNode(10,10,999,12,"fixture",0);StableGameBridge.node=l.Node;l.Tick(.5);
        Check(UnityEngine.Object.Instantiates==2,"new host node clears timer");
        l=new Lab();l.Tick(0);l.Player=new GameObject().Attach(new fpscontroller());l.Tick(.5);
        Check(UnityEngine.Object.Instantiates==2,"new local player clears timer");
        l=new Lab();l.Tick(0);
        var next=new GameObject().Attach(new aispawn{bodyRot=l.Original,AIPrefab=l.Spawner.AIPrefab});l.Spawner=next;l.Tick(.5);
        Check(UnityEngine.Object.Instantiates==2,"new spawner clears timer");
        l=new Lab();UnityEngine.Object.BeforeInstantiate=delegate{RabbitPopulation.Reset();};l.Tick(0);
        UnityEngine.Object.BeforeInstantiate=null;
        Check(!RabbitPopulation.Admit(l.Spawner,new Vector3()),"reset during native attempt cannot leave old authority");
        l.Tick(.5);Check(UnityEngine.Object.Instantiates==2,"reset during native attempt leaves next world runnable");
    }
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length!=1||(args[0]!="baseline"&&args[0]!="candidate"))throw new Exception("Extract actual native methods, then pass baseline or candidate");
            candidate=args[0]=="candidate";
            Check(RabbitPopulation.OnlineReady,"Steam multiplayer rabbit population is enabled");
            Admission();Preconditions();Centers();Budgets();InvalidAndLifecycle();
            Console.WriteLine("PASS "+checks+" rabbit population "+args[0]+" production-method checks");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
