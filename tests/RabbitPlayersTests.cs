using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;

namespace Steamworks
{
    public struct CSteamID
    {
        public ulong id;
        public CSteamID(ulong v){id=v;}
        public static implicit operator ulong(CSteamID s){return s.id;}
        public static implicit operator CSteamID(ulong v){return new CSteamID(v);}
        public override bool Equals(object o){return o is CSteamID&&((CSteamID)o).id==id;}
        public override int GetHashCode(){return id.GetHashCode();}
    }
}
namespace UnityEngine
{
    public static class Mathf{public static float Abs(float v){return System.Math.Abs(v);}public static float Max(float a,float b){return System.Math.Max(a,b);}public static float Min(float a,float b){return System.Math.Min(a,b);}public static float Clamp01(float v){return v<0?0:v>1?1:v;}}
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero{get{return new Vector3();}}
        public static Vector3 up{get{return new Vector3(0,1,0);}}
        public static Vector3 forward{get{return new Vector3(0,0,1);}}
        public static Vector3 right{get{return new Vector3(1,0,0);}}
        public float sqrMagnitude{get{return x*x+y*y+z*z;}}
        public float magnitude{get{return (float)Math.Sqrt(x*x+y*y+z*z);}}
        public static bool operator==(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        public static bool operator!=(Vector3 a,Vector3 b){return !(a==b);}
        public override bool Equals(object o){return o is Vector3&&(Vector3)o==this;}
        public override int GetHashCode(){return x.GetHashCode()^y.GetHashCode()^z.GetHashCode();}
        public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator*(Vector3 a,float s){return new Vector3(a.x*s,a.y*s,a.z*s);}
        public static Vector3 operator/(Vector3 a,float s){return new Vector3(a.x/s,a.y/s,a.z/s);}public static Vector3 operator*(Vector3 a,Vector3 b){return new Vector3(a.x*b.x,a.y*b.y,a.z*b.z);}
        public Vector3 normalized{get{var m=magnitude;return m>0?this/m:new Vector3();}}
        public static float Dot(Vector3 a,Vector3 b){return a.x*b.x+a.y*b.y+a.z*b.z;}
        public static Vector3 ProjectOnPlane(Vector3 v,Vector3 n){var d=Dot(v,n);return new Vector3(v.x-n.x*d,v.y-n.y*d,v.z-n.z*d);}
        public static float Angle(Vector3 a,Vector3 b)
        {
            var ma=a.magnitude;var mb=b.magnitude;
            if(ma<1e-6f||mb<1e-6f)return 0f;
            var d=Dot(a,b)/(ma*mb);
            if(d>1)d=1;if(d<-1)d=-1;
            return (float)(Math.Acos(d)*180.0/Math.PI);
        }
    }
    public struct Quaternion{public static Quaternion identity{get{return new Quaternion();}}}
    public struct Vector3d{public double x,y,z;}
    public class Object{public bool destroyed;public static void Destroy(Object o){if(o!=null)o.destroyed=true;}}
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform{get{return gameObject==null?null:gameObject.transform;}}
        public bool enabled=true;
        public bool isActiveAndEnabled{get{return enabled&&gameObject!=null&&gameObject.activeInHierarchy;}}
        public T GetComponent<T>() where T:Component{return gameObject==null?null:gameObject.Get<T>();}
        public T GetComponentInParent<T>() where T:Component
        {
            var g=gameObject;
            while(g!=null){var c=g.Get<T>();if(c!=null)return c;var pt=g.transform==null?null:g.transform.parent;g=pt==null?null:pt.gameObject;}
            return null;
        }
    }
    public class Transform:Component
    {
        public Quaternion rotation;
        public Vector3 position,eulerAngles,lossyScale=new Vector3(1,1,1);
        public Vector3 TransformPoint(Vector3 p){return p*lossyScale+position;}
        public Vector3 forward{get{return _fwd;}}public Vector3 _fwd;
        public Vector3 up{get{return _up;}}public Vector3 _up;
        public Vector3 right{get{return _right;}}public Vector3 _right;
        public Transform parent;
        public GameObject parentGameObject;
        public bool IsChildOf(Transform p){var t=this;while(t!=null){if(ReferenceEquals(t,p))return true;t=t.parent;}return false;}
    }
    public class GameObject:Object
    {
        public int layer;
        public string tag="";
        public bool activeInHierarchy=true;
        public Transform transform;
        public GameObject parentGameObject;
        public readonly Dictionary<Type,Component> comps=new Dictionary<Type,Component>();
        public static int count;
        public GameObject(){count++;transform=new Transform{gameObject=this};}
        public T Get<T>() where T:Component{Component c;return comps.TryGetValue(typeof(T),out c)?(T)c:null;}
        public T GetComponent<T>() where T:Component{return Get<T>();}
        public T Add<T>(T c) where T:Component{comps[typeof(T)]=c;c.gameObject=this;return c;}
        public void SetActive(bool on){activeInHierarchy=on;}
    }
    public class MonoBehaviour:Component{}
    public static class Time{public static float realtimeSinceStartup=100;public static float time=50;public static float fixedTime=1;public static float deltaTime=0.02f;public static int frameCount;}
    public class Debug{public static void LogWarning(object o){}}
    public enum QueryTriggerInteraction{UseGlobal,Ignore,Collide}
    public struct LayerMask{public int value;public static implicit operator int(LayerMask m){return m.value;}public static implicit operator LayerMask(int v){return new LayerMask{value=v};}}
    public struct Bounds {public Vector3 center;}
    public class Collider:Component{public bool isTrigger;public Bounds bounds;public Rigidbody attachedRigidbody;public virtual Vector3 ClosestPoint(Vector3 p){var c=bounds.center;var d=p-c;return c+d*(1f/(d.magnitude<1e-6f?1f:d.magnitude))*.001f+d;}}
    public class CapsuleCollider:Collider{public float radius,height;public int direction;public Vector3 center;}
    public class SphereCollider:Collider{public float radius;public Vector3 center;}
    public class Rigidbody:Component{}
    public class stimulscript:MonoBehaviour{public bool visible,sound;public List<stimul> stimuls=new List<stimul>();}
    public struct RaycastHit{public Vector3 point;public float distance;public Collider collider;}
    public static class Physics
    {
        public static bool layerExcluded,explicitIgnore,penetrates;
        public static Action onPenetration;
        public static bool GetIgnoreLayerCollision(int a,int b){return layerExcluded&&a==27&&(b==0||b==5);}
        public static bool GetIgnoreCollision(Collider a,Collider b){return explicitIgnore;}
        public static bool ComputePenetration(Collider a,Vector3 ap,Quaternion ar,Collider b,Vector3 bp,Quaternion br,out Vector3 dir,out float depth)
        {dir=Vector3.zero;depth=penetrates?.1f:0f;if(onPenetration!=null)onPenetration();return penetrates;}
        public static RaycastHit[] raycastResult=new RaycastHit[0];public static Collider localVisibleCollider;public static Action onLocalRaycast;
        public static bool Raycast(Vector3 o,Vector3 d,out RaycastHit hit,float dist,LayerMask mask,QueryTriggerInteraction mode){hit=new RaycastHit{collider=localVisibleCollider};if(onLocalRaycast!=null)onLocalRaycast();return localVisibleCollider!=null;}
        public static Action onRaycast;
        public static int raycastCalls;public static int lastMask;public static QueryTriggerInteraction lastMode;public static float lastDist;
        public static RaycastHit[] RaycastAll(Vector3 o,Vector3 d,float dist,LayerMask mask,QueryTriggerInteraction mode){raycastCalls++;lastMask=mask;lastMode=mode;lastDist=dist;if(onRaycast!=null)onRaycast();return raycastResult;}
        public static Collider[] OverlapSphere(Vector3 p,float r,LayerMask m,QueryTriggerInteraction q){return new Collider[0];}
        public static void SyncTransforms(){}
    }
    public enum stimulType{nothing,food,water,threat,light,command}
    public class stimul{public Vector3 pos;public stimulType tipus;public float value;public stimul(Vector3 p,stimulType t,float v){pos=p;tipus=t;value=v;}}
    public class weaponscript:MonoBehaviour{}
    public class meleeweaponscript:MonoBehaviour{}
    public class nyulscript:MonoBehaviour{public ragdollactivatescript ragdoll;public aiscript AI;public sensoryscript senses;public breakablescript breakable;public float size;public bool started=true,attacking;public playerdeathtriggerscript attackTriggerScript;}
    public class ragdollactivatescript:MonoBehaviour{public bool ragdoll;}
    public class aiscript:MonoBehaviour{public nyulscript nyul;public sensoryscript senses;public float flightFight;public bool alive=true,goToTarget=true;public Vector3 interestPos;}
    public class sensoryscript:MonoBehaviour
    {
        public bool alive=true,hasEars=true,hasEyes=true;
        public Transform eyes;
        public float fovX=50f,fovY=50f,fovZ=900f,radius=30f;
        public LayerMask visionMask=1;
        public List<stimul> stimuls=new List<stimul>();public Collider[] colliders;
    }
    public class breakablescript:MonoBehaviour{public float health,shootHealth,explosionHealth;}
    public class ragdollchildpos:MonoBehaviour{public Transform[] childs;public pickupable[] ps;public Vector3d[] poss;}
    public class pickupable:MonoBehaviour{}
    public class tosaveitemscript:MonoBehaviour{public int id;public uint idInSave;public Transform useThisForPos;public nyulscript nyul;public ragdollchildpos ragdollPos;}
    public class savedatascript{public static savedatascript s=new savedatascript();public Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();}
    public class survivalscript:MonoBehaviour{public void DamageInstant(float d,Vector3 dir,bool show){}}
    public class playerdeathtriggerscript:MonoBehaviour{public Collider selfCol;public float damagePlayer=3f;public survivalscript pscript;}
    public class mpplayerscript:MonoBehaviour{public ulong steamID;public bool readyToMove=false;public Transform THead;public CapsuleCollider CBody;public Collider[] cols;}
    public class syncScript:MonoBehaviour
    {
        public static syncScript s=new syncScript();
        public float syncItemInterwall=0.5f,playerRemoveTime=30f;
        public Dictionary<CSteamID,playerSyncClass> players=new Dictionary<CSteamID,playerSyncClass>();
        public int dmgSends;public float lastDmg;public Vector3 lastDir;public mpplayerscript lastMp;
        public bool throwOnDmg;
        public void SendDmgPlayer(mpplayerscript mp,float dmg,Vector3 dir){if(throwOnDmg)throw new Exception("dmg");dmgSends++;lastDmg=dmg;lastDir=dir;lastMp=mp;}
        public class playerSyncClass{public ulong steamID;public GameObject player;public float lastTime;public mpplayerscript mpscript;}
    }
    public class mainscript:MonoBehaviour{public static mainscript s=new mainscript();public fpscontroller player=new GameObject().Add(new fpscontroller());public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d();}}
    public class fpscontroller:MonoBehaviour{public bool loadedmove=true,died;public CapsuleCollider CBody;public Collider[] C;public Rigidbody RB;}
    public class menuhandler{public static menuhandler s=new menuhandler();public mainMap currentMainMap=new mainMap{firstRun=true};public class mainMap{public bool firstRun;}}
}
namespace TldMp
{
    public class StablePeer{public ulong Session=1;public bool Ready=true;}
    public class StableNode
    {
        public bool IsHost,Compatible=true,Ready=true;
        public string Error;
        public ulong Own,Session;
        public Dictionary<ulong,StablePeer> Peers=new Dictionary<ulong,StablePeer>();
        public Action<byte[]> RabbitStateReceived;
    }
}
public static class RabbitPlayersTests
{
    static int checks;
    static void Check(bool ok,string w){checks++;if(!ok)throw new Exception("FAIL "+w);}
    static nyulscript Nyul(GameObject g)
    {
        var n=g.Add(new nyulscript());
        n.ragdoll=g.Add(new ragdollactivatescript());
        n.AI=g.Add(new aiscript());
        n.senses=g.Add(new sensoryscript());
        n.AI.senses=n.senses;
        n.breakable=g.Add(new breakablescript());
        n.senses.eyes=new GameObject().transform;
        return n;
    }
    static tosaveitemscript Item(savedatascript d,GameObject g,nyulscript n,uint idIn)
    {
        var t=g.Add(new tosaveitemscript{id=50,idInSave=idIn,nyul=n});
        d.items[idIn]=t;
        return t;
    }
    class GuestRef{public GameObject root;public mpplayerscript mp;public syncScript.playerSyncClass pc;}
    static GuestRef Guest(ulong id,Vector3 headPos)
    {
        var root=new GameObject();
        var mp=root.Add(new mpplayerscript{steamID=id});
        var head=new GameObject();
        head.transform.position=headPos;
        mp.THead=head.transform;
        head.transform.parent=root.transform;
        var pc=new syncScript.playerSyncClass{steamID=id,player=root,mpscript=mp,lastTime=100f};
        syncScript.s.players[id]=pc;
        return new GuestRef{root=root,mp=mp,pc=pc};
    }
    static TldMp.StableNode Host(ulong own){return new TldMp.StableNode{IsHost=true,Own=own,Compatible=true};}
    static void Setup(TldMp.StableNode n,fpscontroller p,savedatascript d)
    {
        TldMp.RabbitPlayers.Reset();
        TldMp.RabbitPlayers.Bind(true,true,p,d,n);
    }
    public static int Main()
    {
        var d=new savedatascript();
        var host=Host(10);
        var p=mainscript.s.player;
        var rab=new GameObject();
        var nyul=Nyul(rab);
        Item(d,rab,nyul,7);
        nyul.senses.transform.position=Vector3.zero;
        nyul.senses.eyes.position=Vector3.zero;
        nyul.senses.eyes._fwd=new Vector3(0,0,1);
        nyul.senses.eyes._up=new Vector3(0,1,0);
        nyul.senses.eyes._right=new Vector3(1,0,0);
        Physics.raycastResult=new RaycastHit[0];
        host.Peers[20]=new TldMp.StablePeer{Session=200,Ready=true};
        var guest=Guest(20,new Vector3(0,0,10));
        Setup(host,p,d);
        TldMp.RabbitPlayers.Actor actor;
        Check(!TldMp.RabbitPlayers.TryGet(0,out actor)&&!TldMp.RabbitPlayers.TryGet(host.Own,out actor),"zero and own identities cannot resolve as remote actors");
        Check(TldMp.RabbitPlayers.TryGet(20,out actor)&&actor.Session==200&&ReferenceEquals(actor.Mp,guest.mp)&&ReferenceEquals(actor.Root,guest.root),"actor lookup uses the canonical peer and avatar");
        host.Peers[20].Session=201;
        Check(TldMp.RabbitPlayers.TryGet(20,out actor)&&actor.Session==201,"actor lookup observes changed same-ID session");
        host.Peers[20].Session=200;
        p.loadedmove=false;
        Check(!TldMp.RabbitPlayers.TryGet(20,out actor),"unloaded host cannot resolve remote actors");
        p.loadedmove=true;
        guest.pc.player=new GameObject();
        Check(!TldMp.RabbitPlayers.TryGet(20,out actor),"avatar outside canonical player hierarchy is rejected");
        guest.pc.player=guest.root;
        nyul.senses.stimuls.Add(new stimul(new Vector3(9,9,9),stimulType.food,5f));
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==2,"append keeps existing");
        var s=nyul.senses.stimuls[1];
        Check(s.tipus==stimulType.threat&&s.value==10f&&s.pos==guest.mp.THead.position,"threat10 head");
        Check(Physics.lastMask==(int)nyul.senses.visionMask&&Physics.lastMode==QueryTriggerInteraction.Ignore,"vision mask+ignore");
        Check(Math.Abs(Physics.lastDist-10f)<0.01f,"raycast dist==head dist");
        nyul.senses.stimuls.Clear();TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==1,"next native scan starts fresh");
        guest.mp.THead.position=new Vector3(0,0,35);
        nyul.senses.stimuls.Clear();
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"beyond radius+0.2 no stimul");
        guest.mp.THead.position=new Vector3(0,0,10);
        nyul.senses.fovZ=100f;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"fovZ boundary distSq==fovZ rejects");
        nyul.senses.fovZ=101f;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==1,"fovZ>distSq admits");
        nyul.senses.stimuls.Clear();
        nyul.senses.fovZ=900f;
        nyul.senses.fovX=20f;nyul.senses.fovY=60f;
        guest.mp.THead.position=new Vector3(10,0,10);
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==1,"horizontal45 within fovY60 admits");
        nyul.senses.stimuls.Clear();
        guest.mp.THead.position=new Vector3(0,10,10);
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"vertical45 beyond fovX20 rejects");
        guest.mp.THead.position=new Vector3(10,0,0);
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"horizontal90 beyond fovY60 rejects");
        nyul.senses.fovY=90f;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"horizontal angle equal to fovY is rejected");
        nyul.senses.fovY=91f;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==1,"horizontal90 within fovY91 admits");
        nyul.senses.stimuls.Clear();
        nyul.senses.fovY=60f;
        guest.mp.THead.position=new Vector3(0,10,0);
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"vertical90 beyond fovX20 rejects");
        nyul.senses.fovX=90f;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"vertical angle equal to fovX is rejected");
        nyul.senses.fovX=91f;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==1,"vertical90 within fovX91 admits");
        nyul.senses.stimuls.Clear();
        nyul.senses.fovX=50f;nyul.senses.fovY=50f;
        guest.mp.THead.position=new Vector3(0,0,10);
        nyul.senses.hasEyes=false;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"blind no stimul");
        nyul.senses.hasEyes=true;
        nyul.senses.alive=false;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"dead no stimul");
        nyul.senses.alive=true;
        nyul.senses.eyes=null;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"null eyes no stimul");
        nyul.senses.eyes=new GameObject().transform;
        nyul.senses.eyes._fwd=new Vector3(0,0,1);
        nyul.senses.eyes._up=new Vector3(0,1,0);
        nyul.senses.eyes._right=new Vector3(1,0,0);
        guest.pc.lastTime=20f;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"stale age no stimul");
        guest.pc.lastTime=100f;
        guest.pc.lastTime=200f;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"future age no stimul");
        guest.pc.lastTime=100f;
        host.Peers[20].Ready=false;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"unready peer no stimul");
        host.Peers[20].Ready=true;
        host.Peers[20].Session=0;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"zero session no stimul");
        host.Peers[20].Session=200;
        guest.mp.readyToMove=false;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==1,"unused native readyToMove flag no longer rejects actor");
        nyul.senses.stimuls.Clear();
        guest.mp.readyToMove=true;
        guest.mp.steamID=99;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"wrong steamID no stimul");
        guest.mp.steamID=20;
        guest.root.SetActive(false);
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"inactive root no stimul");
        guest.root.SetActive(true);
        guest.mp.THead=null;
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"null THead no stimul");
        guest.mp.THead=new GameObject().transform;guest.mp.THead.parent=guest.root.transform;
        guest.mp.THead.position=new Vector3(0,0,10);
        var notRab=new GameObject();
        var nrNyul=Nyul(notRab);
        nrNyul.senses.eyes.position=Vector3.zero;
        nrNyul.senses.eyes._fwd=new Vector3(0,0,1);
        TldMp.RabbitPlayers.Scan(nrNyul.senses);
        Check(nrNyul.senses.stimuls.Count==0,"non-canonical rabbit no stimul");
        var unstarted=new GameObject();
        var usNyul=Nyul(unstarted);usNyul.started=false;
        Item(d,unstarted,usNyul,8);
        TldMp.RabbitPlayers.Scan(usNyul.senses);
        Check(usNyul.senses.stimuls.Count==0,"unstarted no stimul");
        var blocker=new GameObject().Add(new Collider());
        blocker.transform.position=new Vector3(0,0,5);
        Physics.raycastResult=new RaycastHit[]{new RaycastHit{collider=blocker,distance=5f}};
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"blocker before target blocks");
        blocker.transform.position=new Vector3(0,0,15);
        Physics.raycastResult=new RaycastHit[]{new RaycastHit{collider=blocker,distance=15f}};
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==1,"blocker beyond target no block");
        var targetCol=new GameObject().Add(new Collider());
        targetCol.transform.parent=guest.root.transform;
        targetCol.transform.position=new Vector3(0,0,4);
        Physics.raycastResult=new RaycastHit[]{new RaycastHit{collider=targetCol,distance=4f}};
        nyul.senses.stimuls.Clear();
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==1,"target descendant no block");
        var selfCol=new GameObject().Add(new Collider());
        selfCol.transform.parent=rab.transform;
        selfCol.transform.position=new Vector3(0,0,4);
        Physics.raycastResult=new RaycastHit[]{new RaycastHit{collider=selfCol,distance=4f}};
        nyul.senses.stimuls.Clear();
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==1,"rabbit self collider no block");
        Physics.raycastResult=new RaycastHit[0];
        nyul.senses.stimuls.Clear();
        Physics.onRaycast=delegate{host.Peers[20].Session=201;};
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"session changed during visibility query cannot append a stale actor");
        Physics.onRaycast=null;host.Peers[20].Session=200;
        Physics.onRaycast=delegate{host.Peers[20].Ready=false;};
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(nyul.senses.stimuls.Count==0,"peer becoming unready during visibility query cannot append a stale actor");
        Physics.onRaycast=null;host.Peers[20].Ready=true;

        var trig=rab.Add(new playerdeathtriggerscript());
        var trigCol=rab.Add(new Collider{isTrigger=true});
        trig.selfCol=trigCol;
        guest.mp.steamID=20;
        var contact=new GameObject().Add(new Collider());
        contact.transform.parent=guest.mp.transform;
        guest.root.transform.position=new Vector3(1,0,0);
        Time.fixedTime=1f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==1,"damage send");
        Check(Math.Abs(syncScript.s.lastDmg-3f*0.02f)<0.001f,"amount=damage*dt");
        Check(syncScript.s.lastDir.x==1f,"direction root-trigger");
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==1,"same step dedup");
        var contact2=new GameObject().Add(new Collider());
        contact2.transform.parent=guest.mp.transform;
        TldMp.RabbitPlayers.Damage(trig,contact2);
        Check(syncScript.s.dmgSends==1,"second collider same step dedup");
        Time.fixedTime=2f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==2,"new step sends");
        var rab2=new GameObject();
        var nyul2=Nyul(rab2);
        Item(d,rab2,nyul2,8);
        var trig2=rab2.Add(new playerdeathtriggerscript());
        trig2.selfCol=rab2.Add(new Collider{isTrigger=true});
        TldMp.RabbitPlayers.Damage(trig2,contact);
        Check(syncScript.s.dmgSends==3,"different rabbit same step sends");
        host.Peers[30]=new TldMp.StablePeer{Session=300,Ready=true};
        var guest2=Guest(30,new Vector3(0,0,8));
        var contact3=new GameObject().Add(new Collider());
        contact3.transform.parent=guest2.mp.transform;
        TldMp.RabbitPlayers.Damage(trig,contact3);
        Check(syncScript.s.dmgSends==4,"different peer same step sends");
        var survChild=new GameObject();
        survChild.transform.parent=guest.mp.transform;
        survChild.Add(new survivalscript());
        var survCol=new GameObject().Add(new Collider());
        survCol.transform.parent=survChild.transform;
        Time.fixedTime=5f;
        TldMp.RabbitPlayers.Damage(trig,survCol);
        Check(syncScript.s.dmgSends==4,"survival-on-MP-descendant skip");
        var lone=new GameObject().Add(new Collider());
        Time.fixedTime=5.1f;
        TldMp.RabbitPlayers.Damage(trig,lone);
        Check(syncScript.s.dmgSends==4,"unrelated collider skip");
        host.Peers[20].Ready=false;
        Time.fixedTime=5.2f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"unready peer no damage");
        host.Peers[20].Ready=true;
        guest.pc.lastTime=20f;
        Time.fixedTime=5.3f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"stale peer no damage");
        guest.pc.lastTime=100f;
        nyul.senses.alive=false;
        Time.fixedTime=5.4f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"dead rabbit no damage");
        nyul.senses.alive=true;
        rab.SetActive(false);
        Time.fixedTime=5.5f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"inactive rabbit no damage");
        rab.SetActive(true);
        trig.damagePlayer=float.NaN;
        Time.fixedTime=5.6f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"NaN damage skip");
        trig.damagePlayer=-3f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"negative damage skip");
        trig.damagePlayer=3f;
        Time.deltaTime=-0.02f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"negative dt skip");
        Time.deltaTime=float.NaN;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"NaN dt skip");
        Time.deltaTime=0.02f;
        Time.fixedTime=float.NaN;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"NaN fixedTime skip");
        Time.fixedTime=5.7f;
        trig.enabled=false;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"disabled trigger no damage");
        trig.enabled=true;
        contact.enabled=false;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"disabled contact no damage");
        contact.enabled=true;
        contact.gameObject.SetActive(false);
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"inactive contact no damage");
        contact.gameObject.SetActive(true);
        trigCol.enabled=false;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"disabled selfCol no damage");
        trigCol.enabled=true;trigCol.isTrigger=false;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"non-trigger selfCol no damage");
        trigCol.isTrigger=true;
        var nonHost=Node(false);
        TldMp.RabbitPlayers.Bind(true,true,p,d,nonHost);
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==4,"guest node no damage");
        TldMp.RabbitPlayers.Bind(true,true,p,d,host);
        host.Peers[20].Session=999;
        Time.fixedTime=5.8f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==5,"new session same-ID can damage");
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==5,"same session duplicate dedup");
        host.Peers[20].Session=200;
        syncScript.s.throwOnDmg=true;
        Time.fixedTime=4f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(!TldMp.RabbitPlayers.Healthy,"throw sets unhealthy");
        TldMp.RabbitPlayers.Bind(true,true,p,d,host);
        Check(!TldMp.RabbitPlayers.Healthy,"same bind stays failed");
        TldMp.RabbitPlayers.Bind(true,false,p,d,host);
        Check(TldMp.RabbitPlayers.Healthy,"world change same refs recovers");
        TldMp.RabbitPlayers.Bind(true,true,p,d,host);
        Time.fixedTime=4.1f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(!TldMp.RabbitPlayers.Healthy,"refail after world recover");
        TldMp.RabbitPlayers.Bind(false,true,p,d,host);
        Check(TldMp.RabbitPlayers.Healthy,"disable same refs recovers");
        TldMp.RabbitPlayers.Bind(true,true,p,d,host);
        Time.fixedTime=4.2f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(!TldMp.RabbitPlayers.Healthy,"refail after enable recover");
        host.Session=777;
        TldMp.RabbitPlayers.Bind(true,true,p,d,host);
        Check(TldMp.RabbitPlayers.Healthy,"node session change recovers");
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(!TldMp.RabbitPlayers.Healthy,"refail after session recover");
        host.IsHost=false;
        TldMp.RabbitPlayers.Bind(true,true,p,d,host);
        Check(TldMp.RabbitPlayers.Healthy,"role change recovers");
        host.IsHost=true;host.Session=100;
        TldMp.RabbitPlayers.Bind(true,true,p,d,host);
        syncScript.s.throwOnDmg=true;
        Time.fixedTime=4.4f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(!TldMp.RabbitPlayers.Healthy,"refail final");
        syncScript.s.throwOnDmg=false;
        TldMp.RabbitPlayers.Reset();


        var oldData=d;
        var d2=new savedatascript();
        var rab3=new GameObject();
        var nyul3=Nyul(rab3);
        Item(d2,rab3,nyul3,9);
        var trig3=rab3.Add(new playerdeathtriggerscript());
        trig3.selfCol=rab3.Add(new Collider{isTrigger=true});
        TldMp.RabbitPlayers.Bind(true,true,p,d2,host);
        TldMp.RabbitPlayers.Damage(trig3,contact);
        Check(syncScript.s.dmgSends==6,"new data context damages");
        TldMp.RabbitPlayers.Bind(false,true,p,d2,host);
        TldMp.RabbitPlayers.Damage(trig3,contact);
        Check(syncScript.s.dmgSends==6,"disabled no damage");
        var p2=new GameObject().Add(new fpscontroller());
        TldMp.RabbitPlayers.Bind(true,true,p2,d2,host);
        TldMp.RabbitPlayers.Damage(trig3,contact);
        Check(syncScript.s.dmgSends==7,"new player context works");
        TldMp.RabbitPlayers.Bind(true,false,p2,d2,host);
        TldMp.RabbitPlayers.Damage(trig3,contact);
        Check(syncScript.s.dmgSends==7,"no world no damage");
        TldMp.RabbitPlayers.Bind(true,true,p2,d2,nonHost);
        TldMp.RabbitPlayers.Damage(trig3,contact);
        Check(syncScript.s.dmgSends==7,"node swap to guest no damage");
        var created=GameObject.count;
        TldMp.RabbitPlayers.Bind(true,true,p,d,host);
        TldMp.RabbitPlayers.Scan(nyul.senses);
        Check(GameObject.count==created,"no objects created");
        int damageBefore=syncScript.s.dmgSends;
        Time.fixedTime=100f;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==damageBefore+1,"first session contacts in a fresh physics step");
        host.Peers[20].Session=201;
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==damageBefore+2,"changed peer session is not suppressed by the previous session in the same step");
        TldMp.RabbitPlayers.Damage(trig,contact);
        Check(syncScript.s.dmgSends==damageBefore+2,"duplicate contact within the changed session remains suppressed");
        HumanVisionTests();LocalBodyVisionTests();
        AttackLayerTests();
        Console.WriteLine("PASS "+checks+" rabbit players checks");
        return 0;
    }
    static void NoProbe(nyulscript rabbit,string reason)
    {
        int before=syncScript.s.dmgSends;Time.fixedTime++;
        TldMp.RabbitPlayers.ProbeDamage(rabbit);
        Check(syncScript.s.dmgSends==before,reason);
    }
    static void AttackLayerTests()
    {
        var host=Host(10);host.Peers[20]=new TldMp.StablePeer{Session=200,Ready=true};
        var data=new savedatascript();var go=new GameObject();var rabbit=Nyul(go);Item(data,go,rabbit,77);
        var trigger=go.Add(new playerdeathtriggerscript());
        trigger.selfCol=go.Add(new Collider{isTrigger=true});go.layer=27;
        rabbit.attackTriggerScript=trigger;rabbit.attacking=true;rabbit.size=1;rabbit.AI.flightFight=10;
        var actor=Guest(20,new Vector3(0,0,1));
        actor.mp.CBody=actor.root.Add(new CapsuleCollider());actor.mp.cols=new Collider[]{actor.mp.CBody,actor.mp.CBody};
        actor.root.layer=0;Time.realtimeSinceStartup=100;Time.fixedTime=500;
        syncScript.s.throwOnDmg=false;Setup(host,mainscript.s.player,data);
        Physics.layerExcluded=true;Physics.explicitIgnore=false;Physics.penetrates=true;
        int before=syncScript.s.dmgSends;
        TldMp.RabbitPlayers.ProbeDamage(rabbit);
        Check(syncScript.s.dmgSends==before+1,"excluded layer27/0 overlapping native attack reaches guest");
        Check(Math.Abs(syncScript.s.lastDmg-trigger.damagePlayer*Time.deltaTime)<.00001f,"probe retains native damage rate");
        TldMp.RabbitPlayers.ProbeDamage(rabbit);TldMp.RabbitPlayers.Damage(trigger,actor.mp.CBody);
        Check(syncScript.s.dmgSends==before+1,"query and native callbacks deduplicate in same fixed step");
        Time.fixedTime++;TldMp.RabbitPlayers.ProbeDamage(rabbit);
        Check(syncScript.s.dmgSends==before+2,"next fixed step damages again");
        Physics.penetrates=false;NoProbe(rabbit,"no geometric overlap gives no damage");Physics.penetrates=true;
        trigger.enabled=false;NoProbe(rabbit,"inactive trigger gives no probe damage");trigger.enabled=true;
        trigger.selfCol.enabled=false;NoProbe(rabbit,"disabled native shape gives no damage");trigger.selfCol.enabled=true;
        int beforeFear=syncScript.s.dmgSends;Time.fixedTime++;
        rabbit.AI.flightFight=-10;TldMp.RabbitPlayers.ProbeDamage(rabbit);
        Check(syncScript.s.dmgSends==beforeFear+1,"active native attack window damages through negative fear");
        rabbit.AI.flightFight=0;Time.fixedTime++;
        TldMp.RabbitPlayers.ProbeDamage(rabbit);
        Check(syncScript.s.dmgSends==beforeFear+2,"active native attack window damages through neutral fear");
        TldMp.RabbitPlayers.Damage(trigger,actor.mp.CBody);
        Check(syncScript.s.dmgSends==beforeFear+2,"probe plus native callback dedupe in same fixed step");
        rabbit.AI.flightFight=10;
        rabbit.attacking=false;NoProbe(rabbit,"pursuit alone gives no damage");rabbit.attacking=true;
        rabbit.size=.5f;NoProbe(rabbit,"native small-rabbit threshold retained");rabbit.size=1;
        rabbit.senses.alive=false;NoProbe(rabbit,"dead rabbit gives no damage");rabbit.senses.alive=true;
        host.Peers[20].Ready=false;NoProbe(rabbit,"unready actor rejected");host.Peers[20].Ready=true;
        actor.pc.lastTime=0;NoProbe(rabbit,"stale actor rejected");actor.pc.lastTime=100;
        Physics.explicitIgnore=true;NoProbe(rabbit,"explicit collider exclusion preserved");Physics.explicitIgnore=false;
        Physics.layerExcluded=false;NoProbe(rabbit,"native-compatible layers use native callbacks only");Physics.layerExcluded=true;
        host.IsHost=false;NoProbe(rabbit,"guest cannot apply probe damage");host.IsHost=true;
        Physics.onPenetration=delegate{host.Peers[20].Session=201;};
        NoProbe(rabbit,"session replacement during geometry query rejected");
        Physics.onPenetration=null;host.Peers[20].Session=200;
        Physics.onPenetration=delegate{trigger.enabled=false;};
        NoProbe(rabbit,"trigger deactivation during query revalidated");
        Physics.onPenetration=null;trigger.enabled=true;
        Physics.layerExcluded=false;Physics.penetrates=false;
    }
    static float NativeFlightFight(List<stimul> entries,bool evil)
    {
        float flight=0;foreach(var x in entries)if(x.tipus==stimulType.threat)flight-=x.value;
        if(flight<0&&flight>-11&&evil)flight=-flight;
        return flight;
    }
    static void HumanVisionTests()
    {
        syncScript.s=new syncScript();Physics.onRaycast=null;Physics.raycastResult=new RaycastHit[0];
        var save=new savedatascript();var host=Host(10);var p=new GameObject().Add(new fpscontroller());
        var rabbit=Nyul(new GameObject());Item(save,rabbit.gameObject,rabbit,111);
        var eyes=rabbit.senses.eyes;eyes.position=Vector3.zero;eyes._fwd=Vector3.forward;eyes._up=Vector3.up;eyes._right=Vector3.right;
        rabbit.senses.fovX=25; rabbit.senses.fovY=75;rabbit.senses.fovZ=900;rabbit.senses.radius=30;
        var local=new GameObject();local.transform.parent=p.transform;local.transform.position=new Vector3(0,0,8);
        var localCol=local.Add(new Collider());var ls=local.Add(new stimulscript{visible=true,sound=false});ls.stimuls.Add(new stimul(Vector3.zero,stimulType.threat,10));
        rabbit.senses.colliders=new Collider[]{localCol};Physics.localVisibleCollider=localCol;
        host.Peers[20]=new TldMp.StablePeer{Session=200,Ready=true};var guest=Guest(20,new Vector3(0,0,5));
        host.Peers[30]=new TldMp.StablePeer{Session=300,Ready=true};var other=Guest(30,new Vector3(0,0,9));
        Setup(host,p,save);
        rabbit.senses.stimuls.Add(new stimul(local.transform.position,stimulType.threat,10));
        rabbit.senses.stimuls.Add(new stimul(new Vector3(1,1,1),stimulType.food,4));
        TldMp.RabbitPlayers.Scan(rabbit.senses);
        Check(rabbit.senses.stimuls.Count==2&&rabbit.senses.stimuls[0].tipus==stimulType.food,"nearest guest replaces only local visual, retaining food");
        Check(rabbit.senses.stimuls[1].pos==guest.mp.THead.position&&NativeFlightFight(rabbit.senses.stimuls,true)==10,"two humans keep native evil inversion and target nearer guest");
        Check(NativeFlightFight(rabbit.senses.stimuls,false)==-10,"peaceful rabbit retains native flight");
        rabbit.senses.stimuls.Clear();local.transform.position=new Vector3(0,0,3);rabbit.senses.stimuls.Add(new stimul(local.transform.position,stimulType.threat,10));
        TldMp.RabbitPlayers.Scan(rabbit.senses);Check(rabbit.senses.stimuls.Count==1&&rabbit.senses.stimuls[0].pos==local.transform.position,"closer native local remains selected");
        rabbit.senses.stimuls.Clear();local.transform.position=new Vector3(0,0,8);rabbit.senses.stimuls.Add(new stimul(local.transform.position,stimulType.threat,10));
        var environmental=new stimul(new Vector3(3,0,1),stimulType.threat,1);rabbit.senses.stimuls.Add(environmental);
        TldMp.RabbitPlayers.Scan(rabbit.senses);Check(rabbit.senses.stimuls.Contains(environmental)&&NativeFlightFight(rabbit.senses.stimuls,true)==-11,"environment threat retained and can prevent inversion");
        rabbit.senses.stimuls.Clear();rabbit.senses.colliders=new Collider[0];
        TldMp.RabbitPlayers.Scan(rabbit.senses);Check(rabbit.senses.stimuls.Count==1&&rabbit.senses.stimuls[0].pos==guest.mp.THead.position,"two guests produce one nearest threat");
        rabbit.senses.stimuls.Clear();other.root.SetActive(false);guest.mp.THead.position=new Vector3(0,2,1);guest.root.transform.position=new Vector3(0,0,1);
        var body=guest.root.Add(new CapsuleCollider());body.bounds=new Bounds{center=new Vector3(0,.2f,1)};guest.mp.CBody=body;
        TldMp.RabbitPlayers.Scan(rabbit.senses);Check(rabbit.senses.stimuls.Count==1&&rabbit.senses.stimuls[0].pos==body.bounds.center,"close body visible when head leaves vertical cone");
        rabbit.senses.stimuls.Clear();var wall=new GameObject().Add(new Collider());Physics.raycastResult=new[]{new RaycastHit{collider=wall,distance=.9f}};
        TldMp.RabbitPlayers.Scan(rabbit.senses);Check(rabbit.senses.stimuls.Count==0,"wall just before body blocks target");Physics.raycastResult=new RaycastHit[0];
        rabbit.senses.stimuls.Clear();rabbit.senses.colliders=new[]{localCol};rabbit.senses.stimuls.Add(new stimul(local.transform.position,stimulType.threat,10));rabbit.senses.stimuls.Add(new stimul(local.transform.position,stimulType.threat,10));
        TldMp.RabbitPlayers.Scan(rabbit.senses);Check(rabbit.senses.stimuls.Count==2,"ambiguous copied local stimulus preserved with no extra threat");
        rabbit.senses.stimuls.Clear();rabbit.senses.colliders=new[]{localCol};rabbit.senses.stimuls.Add(new stimul(local.transform.position,stimulType.threat,10));
        Physics.localVisibleCollider=null;TldMp.RabbitPlayers.Scan(rabbit.senses);
        Check(rabbit.senses.stimuls.Count==1&&rabbit.senses.stimuls[0].pos==local.transform.position,"unprovable local visual preserved");Physics.localVisibleCollider=localCol;
        var noise=new GameObject();noise.transform.position=local.transform.position;var noiseCol=noise.Add(new Collider());var noiseSource=noise.Add(new stimulscript{sound=true});noiseSource.stimuls.Add(new stimul(Vector3.zero,stimulType.threat,3));
        rabbit.senses.colliders=new[]{localCol,noiseCol};TldMp.RabbitPlayers.Scan(rabbit.senses);
        Check(rabbit.senses.stimuls.Count==1&&rabbit.senses.stimuls[0].pos==local.transform.position,"ambiguous scaled sound preserved");
        rabbit.senses.colliders=new[]{localCol};rabbit.senses.stimuls.Clear();rabbit.senses.stimuls.Add(new stimul(local.transform.position,stimulType.threat,10));
        int localRays=0;Physics.onLocalRaycast=delegate{localRays++;if(localRays==2)host.Peers[20].Session=201;};
        TldMp.RabbitPlayers.Scan(rabbit.senses);Check(rabbit.senses.stimuls.Count==1&&rabbit.senses.stimuls[0].pos==local.transform.position,"session change during final local recheck preserves native stimulus");
        Physics.onLocalRaycast=null;host.Peers[20].Session=200;
        rabbit.senses.stimuls.Clear();rabbit.senses.colliders=new Collider[0];Physics.onRaycast=delegate{host.Peers[20].Session=201;};
        TldMp.RabbitPlayers.Scan(rabbit.senses);Check(rabbit.senses.stimuls.Count==0,"body fallback revalidates changed session");Physics.onRaycast=null;
    }
    static void LocalBodyVisionTests()
    {
        syncScript.s=new syncScript();Physics.onRaycast=null;Physics.onLocalRaycast=null;Physics.raycastResult=new RaycastHit[0];
        var save=new savedatascript();var host=Host(10);var player=new GameObject().Add(new fpscontroller());
        var rabbit=Nyul(new GameObject());Item(save,rabbit.gameObject,rabbit,120);var senses=rabbit.senses;
        senses.eyes.position=Vector3.zero;senses.eyes._fwd=Vector3.forward;senses.eyes._up=Vector3.up;senses.eyes._right=Vector3.right;
        senses.radius=30;senses.fovZ=900;senses.fovX=25;senses.fovY=75;senses.colliders=new Collider[0];
        var go=new GameObject();go.transform.parent=player.transform;var local=go.Add(new CapsuleCollider());local.bounds=new Bounds{center=new Vector3(0,.2f,1)};player.CBody=local;
        host.Peers[20]=new TldMp.StablePeer{Session=200,Ready=true};var guest=Guest(20,new Vector3(0,0,4));
        Setup(host,player,save);
        var noise=new stimul(new Vector3(9,0,9),stimulType.threat,1);senses.stimuls.Add(noise);
        TldMp.RabbitPlayers.Scan(senses);
        Check(senses.stimuls.Count==2&&senses.stimuls.Contains(noise)&&senses.stimuls[1].pos==local.bounds.center,"local body fallback wins while preserving environmental threat");
        senses.stimuls.Clear();local.bounds=new Bounds{center=new Vector3(0,.2f,6)};TldMp.RabbitPlayers.Scan(senses);
        Check(senses.stimuls.Count==1&&senses.stimuls[0].pos==guest.mp.THead.position,"nearer visible guest beats local body");
        senses.stimuls.Clear();local.bounds=new Bounds{center=new Vector3(0,.2f,1)};guest.root.SetActive(false);
        var wall=new GameObject().Add(new Collider());Physics.raycastResult=new[]{new RaycastHit{collider=wall,distance=.9f}};
        TldMp.RabbitPlayers.Scan(senses);Check(senses.stimuls.Count==0,"wall near local body blocks fallback");
        Physics.raycastResult=new RaycastHit[0];var otherGo=new GameObject();otherGo.transform.parent=player.transform;var otherBody=otherGo.Add(new CapsuleCollider());otherBody.bounds=local.bounds;
        Physics.onRaycast=delegate{player.CBody=otherBody;};TldMp.RabbitPlayers.Scan(senses);
        Check(senses.stimuls.Count==0,"local body replaced during ray cannot become target");Physics.onRaycast=null;
        player.CBody=local;player.died=true;TldMp.RabbitPlayers.Scan(senses);Check(senses.stimuls.Count==0,"dead local body is not a fallback target");
        player.died=false;local.enabled=false;TldMp.RabbitPlayers.Scan(senses);Check(senses.stimuls.Count==0,"disabled local body is not a fallback target");
    }
    static TldMp.StableNode Node(bool host){return new TldMp.StableNode{IsHost=host,Compatible=true};}
}
