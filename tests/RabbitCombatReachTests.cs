using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TldMp;

// A deterministic geometry/physics model, not Unity PhysX evidence. These tests
// compile the real sight resolver, motor and attack-entry policy together.
namespace UnityEngine
{
    public class Object
    {
        public static bool operator==(Object a,Object b){return ReferenceEquals(a,b);}
        public static bool operator!=(Object a,Object b){return !ReferenceEquals(a,b);}
        public override bool Equals(object x){return ReferenceEquals(this,x);}
        public override int GetHashCode(){return base.GetHashCode();}
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform{get{return gameObject.transform;}}
        public T GetComponent<T>() where T:class{return gameObject.GetComponent<T>();}
        public T GetComponentInParent<T>() where T:class
        {for(var p=transform;p!=null;p=p.parent){var c=p.gameObject.GetComponent<T>();if(c!=null)return c;}return null;}
        public T[] GetComponentsInChildren<T>(bool all) where T:class
        {
            var items=new List<T>();Collect(transform,items);return items.ToArray();
        }
        static void Collect<T>(Transform p,List<T> list) where T:class
        {foreach(var c in p.gameObject.Components)if(c is T)list.Add(c as T);foreach(var ch in p.Children)Collect(ch,list);}
    }
    public class Behaviour:Component{public bool enabled=true;public bool isActiveAndEnabled{get{return enabled&&gameObject.activeInHierarchy;}}}
    public class MonoBehaviour:Behaviour{}
    public class GameObject:Object
    {
        public string name="";public int layer;public bool active=true;public readonly Transform transform;
        internal readonly List<Component> Components=new List<Component>();
        public GameObject(){transform=new Transform{gameObject=this};Components.Add(transform);}
        public bool activeInHierarchy{get{return active&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);}}
        public void SetActive(bool v){active=v;}
        public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};Components.Add(c);return c;}
        public T GetComponent<T>() where T:class{foreach(var c in Components)if(c is T)return c as T;return null;}
    }
    public class Transform:Component
    {
        Transform upTree;Vector3 local;Quaternion localRot;public Vector3 localScale=new Vector3(1,1,1);
        internal readonly List<Transform> Children=new List<Transform>();
        public Transform parent{get{return upTree;}set{var p=position;if(upTree!=null)upTree.Children.Remove(this);upTree=value;if(value!=null)value.Children.Add(this);position=p;}}
        public Vector3 position{get{return upTree==null?local:upTree.TransformPoint(local);}set{local=upTree==null?value:upTree.InversePoint(value);}}
        public Quaternion rotation{get{return new Quaternion{yaw=localRot.yaw+(upTree==null?0:upTree.rotation.yaw)};}set{localRot=new Quaternion{yaw=value.yaw-(upTree==null?0:upTree.rotation.yaw)};}}
        public Vector3 lossyScale{get{return upTree==null?localScale:localScale*upTree.lossyScale;}}
        public Vector3 forward{get{return rotation*Vector3.forward;}}
        public Vector3 up{get{return Vector3.up;}}public Vector3 right{get{return rotation*Vector3.right;}}
        public Vector3 TransformPoint(Vector3 p){return position+rotation*(p*lossyScale);}
        Vector3 InversePoint(Vector3 p){var v=new Quaternion{yaw=-rotation.yaw}*(p-position);var s=lossyScale;return new Vector3(v.x/s.x,v.y/s.y,v.z/s.z);}
        public Vector3 TransformDirection(Vector3 p){return rotation*p;}
        public bool IsChildOf(Transform p){for(var t=this;t!=null;t=t.parent)if(ReferenceEquals(t,p))return true;return false;}
    }
    public struct Vector3
    {
        public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero{get{return new Vector3();}}public static Vector3 up{get{return new Vector3(0,1,0);}}
        public static Vector3 down{get{return new Vector3(0,-1,0);}}public static Vector3 forward{get{return new Vector3(0,0,1);}}public static Vector3 right{get{return new Vector3(1,0,0);}}
        public float sqrMagnitude{get{return x*x+y*y+z*z;}}public float magnitude{get{return (float)Math.Sqrt(sqrMagnitude);}}
        public Vector3 normalized{get{return magnitude<1e-8f?zero:this/magnitude;}}
        public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator*(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}
        public static Vector3 operator*(Vector3 a,Vector3 b){return new Vector3(a.x*b.x,a.y*b.y,a.z*b.z);}
        public static Vector3 operator/(Vector3 a,float b){return a*(1f/b);}
        public static bool operator==(Vector3 a,Vector3 b){return (a-b).sqrMagnitude<1e-10f;}public static bool operator!=(Vector3 a,Vector3 b){return !(a==b);}
        public override bool Equals(object o){return o is Vector3&&this==(Vector3)o;}public override int GetHashCode(){return x.GetHashCode();}
        public static float Dot(Vector3 a,Vector3 b){return a.x*b.x+a.y*b.y+a.z*b.z;}
        public static Vector3 Cross(Vector3 a,Vector3 b){return new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);}
        public static Vector3 Min(Vector3 a,Vector3 b){return new Vector3(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Min(a.z,b.z));}
        public static Vector3 Max(Vector3 a,Vector3 b){return new Vector3(Math.Max(a.x,b.x),Math.Max(a.y,b.y),Math.Max(a.z,b.z));}
        public static Vector3 Lerp(Vector3 a,Vector3 b,float f){return a+(b-a)*f;}
        public static Vector3 MoveTowards(Vector3 a,Vector3 b,float d){return (b-a).magnitude<=d?b:a+(b-a).normalized*d;}
        public static float Angle(Vector3 a,Vector3 b){return (float)(Math.Acos(Math.Max(-1,Math.Min(1,Dot(a.normalized,b.normalized))))*180/Math.PI);}
        public static Vector3 ProjectOnPlane(Vector3 a,Vector3 n){return a-n*Dot(a,n);}
        public static Vector3 RotateTowards(Vector3 a,Vector3 b,float radians,float unused){return Quaternion.RotateTowards(Quaternion.LookRotation(a,up),Quaternion.LookRotation(b,up),radians*180/(float)Math.PI)*forward;}
    }
    public struct Quaternion
    {
        public float yaw;public static Quaternion identity{get{return new Quaternion();}}
        public static Quaternion LookRotation(Vector3 d,Vector3 u){return new Quaternion{yaw=(float)(Math.Atan2(d.x,d.z)*180/Math.PI)};}
        public static Quaternion RotateTowards(Quaternion a,Quaternion b,float limit){float d=b.yaw-a.yaw;while(d>180)d-=360;while(d< -180)d+=360;return new Quaternion{yaw=a.yaw+Math.Max(-limit,Math.Min(limit,d))};}
        public static Vector3 operator*(Quaternion q,Vector3 v){float r=q.yaw*(float)Math.PI/180,s=(float)Math.Sin(r),c=(float)Math.Cos(r);return new Vector3(v.x*c+v.z*s,v.y,v.z*c-v.x*s);}
    }
    public static class Mathf
    {
        public static float Abs(float v){return Math.Abs(v);}public static float Max(float a,float b){return Math.Max(a,b);}public static float Min(float a,float b){return Math.Min(a,b);}
        public static float Clamp(float v,float a,float b){return Math.Max(a,Math.Min(b,v));}public static float Clamp01(float v){return Clamp(v,0,1);}
        public static float Sqrt(float v){return (float)Math.Sqrt(v);}public static float Atan2(float y,float x){return (float)Math.Atan2(y,x);}public static float Sin(float x){return (float)Math.Sin(x);}public static float Cos(float x){return (float)Math.Cos(x);}
        public static int CeilToInt(float x){return (int)Math.Ceiling(x);}
    }
    public struct Bounds{public Vector3 min,max;public Vector3 center{get{return (min+max)*.5f;}}}
    public class Collider:Behaviour
    {
        public bool isTrigger;public Rigidbody attachedRigidbody{get{return GetComponentInParent<Rigidbody>();}}
        public Bounds bounds{get{Vector3 a,b;float r;Geometry.Segment(this,out a,out b,out r);var e=new Vector3(r,r,r);return new Bounds{min=Vector3.Min(a,b)-e,max=Vector3.Max(a,b)+e};}}
        public Vector3 ClosestPoint(Vector3 p){var cb=Physics.onClosest;if(cb!=null)cb();Vector3 a,b;float r;Geometry.Segment(this,out a,out b,out r);var q=Geometry.Point(a,b,p);var d=p-q;return d.magnitude<=r?p:q+d.normalized*r;}
    }
    public class SphereCollider:Collider{public float radius;public Vector3 center;}
    public class CapsuleCollider:Collider{public float radius,height;public int direction=1;public Vector3 center;}
    public class Rigidbody:Component
    {
        public bool isKinematic;public Vector3 velocity;
        public Vector3 position{get{return transform.position;}set{transform.position=value;}}
        public Quaternion rotation{get{return transform.rotation;}set{transform.rotation=value;}}
        public void MoveRotation(Quaternion q){rotation=q;}
    }
    internal static class Geometry
    {
        internal static void Segment(Collider c,out Vector3 a,out Vector3 b,out float r)
        {
            var sphere=c as SphereCollider;var cap=c as CapsuleCollider;
            var s=c.transform.lossyScale;s=new Vector3(Math.Abs(s.x),Math.Abs(s.y),Math.Abs(s.z));
            if(sphere!=null){a=b=c.transform.TransformPoint(sphere.center);r=sphere.radius*Math.Max(s.x,Math.Max(s.y,s.z));return;}
            if(cap==null){a=b=c.transform.position;r=0;return;}
            int d=cap.direction;float axial=d==0?s.x:d==1?s.y:s.z,radial=d==0?Math.Max(s.y,s.z):d==1?Math.Max(s.x,s.z):Math.Max(s.x,s.y);
            r=cap.radius*radial;float half=Math.Max(0,cap.height*axial*.5f-r);
            var axis=c.transform.TransformDirection(d==0?Vector3.right:d==1?Vector3.up:Vector3.forward);
            var center=c.transform.TransformPoint(cap.center);a=center-axis*half;b=center+axis*half;
        }
        internal static Vector3 Point(Vector3 a,Vector3 b,Vector3 p){var d=b-a;return a+d*Mathf.Clamp01(d.sqrMagnitude<1e-12f?0:Vector3.Dot(p-a,d)/d.sqrMagnitude);}
        internal static float Distance(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var u=b-a;var v=d-c;var w=a-c;float aa=Vector3.Dot(u,u),bb=Vector3.Dot(u,v),cc=Vector3.Dot(v,v),dd=Vector3.Dot(u,w),ee=Vector3.Dot(v,w),s,t;
            if(aa<1e-12f)return (a-Point(c,d,a)).magnitude;
            if(cc<1e-12f)return (c-Point(a,b,c)).magnitude;
            float den=aa*cc-bb*bb;s=den>1e-12f?Mathf.Clamp01((bb*ee-cc*dd)/den):0;t=(bb*s+ee)/cc;
            if(t<0){t=0;s=Mathf.Clamp01(-dd/aa);}else if(t>1){t=1;s=Mathf.Clamp01((bb-dd)/aa);}
            return (a+u*s-c-v*t).magnitude;
        }
        internal static float Depth(Collider a,Collider b)
        {Vector3 aa,ab,ba,bb;float ar,br;Segment(a,out aa,out ab,out ar);Segment(b,out ba,out bb,out br);return ar+br-Distance(aa,ab,ba,bb);}
    }
    public struct RaycastHit{public Collider collider;public Vector3 point,normal;public float distance;}
    public enum QueryTriggerInteraction{UseGlobal,Ignore,Collide}
    public struct LayerMask{public int value;public static implicit operator int(LayerMask x){return x.value;}public static implicit operator LayerMask(int x){return new LayerMask{value=x};}}
    public static class Time{public static float time,realtimeSinceStartup,fixedTime,fixedDeltaTime=.02f,deltaTime=.02f;}
    public static class Debug{public static void LogWarning(object x){}}
    public static class Physics
    {
        public static bool ground=true,excluded=true;public static float groundY,slope,wallZ=float.NaN,wallWidth=100;
        public static Action onClosest,onPenetration,onRay;
        public static readonly List<Collider> solids=new List<Collider>();
        public static Collider floor=new GameObject().AddComponent<Collider>(),wall=new GameObject().AddComponent<Collider>(),visual;
        static bool Wall(Vector3 a,Vector3 b,float pad)
        {if(float.IsNaN(wallZ)||Math.Abs(b.z-a.z)<1e-8f)return false;float t=(wallZ-a.z)/(b.z-a.z);return t>=0&&t<=1&&Math.Abs(a.x+(b.x-a.x)*t)<=wallWidth+pad;}
        public static RaycastHit[] RaycastAll(Vector3 o,Vector3 d,float length,int mask,QueryTriggerInteraction q)
        {
            var cb=onRay;if(cb!=null)cb();
            if(d.y<-.5f){if(!ground)return new RaycastHit[0];return new[]{new RaycastHit{collider=floor,point=new Vector3(o.x,groundY,o.z),normal=new Vector3(slope,1,0).normalized,distance=o.y-groundY}};}
            return Wall(o,o+d*length,0)?new[]{new RaycastHit{collider=wall,distance=Math.Abs((wallZ-o.z)/d.z)}}:new RaycastHit[0];
        }
        public static bool Raycast(Vector3 o,Vector3 d,out RaycastHit h,float length,int mask,QueryTriggerInteraction q)
        {h=new RaycastHit();if(Wall(o,visual==null?o+d*length:visual.transform.position,0)||visual==null)return false;h.collider=visual;return true;}
        public static int RaycastNonAlloc(Vector3 o,Vector3 d,RaycastHit[] h,float length,int mask,QueryTriggerInteraction q){var hits=RaycastAll(o,d,length,mask,q);Array.Copy(hits,h,hits.Length);return hits.Length;}
        public static bool ComputePenetration(Collider a,Vector3 ap,Quaternion aq,Collider b,Vector3 bp,Quaternion bq,out Vector3 dir,out float depth)
        {var cb=onPenetration;if(cb!=null)cb();dir=Vector3.forward;depth=Geometry.Depth(a,b);return depth>0;}
        public static bool GetIgnoreLayerCollision(int a,int b){return excluded;}public static bool GetIgnoreCollision(Collider a,Collider b){return false;}
        public static int OverlapCapsuleNonAlloc(Vector3 a,Vector3 b,float r,Collider[] hits,int mask,QueryTriggerInteraction q)
        {int n=0;foreach(var c in solids){Vector3 x,y;float cr;Geometry.Segment(c,out x,out y,out cr);if(c.enabled&&Geometry.Distance(a,b,x,y)<r+cr&&n<hits.Length)hits[n++]=c;}return n;}
        public static int CapsuleCastNonAlloc(Vector3 a,Vector3 b,float r,Vector3 d,RaycastHit[] hits,float length,int mask,QueryTriggerInteraction q)
        {
            int n=0;if(Wall(a,b+d*length,r)||Wall(b,b+d*length,r))hits[n++]=new RaycastHit{collider=wall};
            foreach(var c in solids)
            {
                if(!c.enabled||c.isTrigger||!c.gameObject.activeInHierarchy)continue;Vector3 x,y;float cr;Geometry.Segment(c,out x,out y,out cr);
                bool hit=false;int steps=Math.Max(1,(int)Math.Ceiling(length/.02f));
                for(int i=0;i<=steps&&!hit;i++)hit=Geometry.Distance(a+d*(length*i/steps),b+d*(length*i/steps),x,y)<r+cr;
                if(hit&&n<hits.Length)hits[n++]=new RaycastHit{collider=c};
            }
            return n;
        }
        public static int OverlapSphereNonAlloc(Vector3 p,float r,Collider[] hits,int mask,QueryTriggerInteraction q){return 0;}
    }
}
namespace Steamworks
{
    public struct CSteamID
    {
        ulong id;public CSteamID(ulong v){id=v;}public static implicit operator ulong(CSteamID v){return v.id;}
        public override bool Equals(object x){return x is CSteamID&&((CSteamID)x).id==id;}public override int GetHashCode(){return id.GetHashCode();}
    }
}
public struct Vector3d{public double x,y,z;public Vector3d(double a,double b,double c){x=a;y=b;z=c;}}
public enum stimulType{nothing,food,water,threat,light,command}
public class stimul{public Vector3 pos;public stimulType tipus;public float value;public stimul(Vector3 p,stimulType t,float v){pos=p;tipus=t;value=v;}}
public class stimulscript:MonoBehaviour{public bool visible,sound;public List<stimul> stimuls=new List<stimul>();}
public class sensoryscript:MonoBehaviour
{
    public bool alive=true,hasEyes=true;public Transform eyes;
    public float radius=40,fovZ=900,fovX=25,fovY=75;public int visionMask=-1;
    public Collider[] colliders;public List<stimul> stimuls=new List<stimul>();
}
public class ragdollactivatescript:MonoBehaviour{public bool ragdoll;public Collider[] animColliders;}
public class movecreaturescript:MonoBehaviour{public Rigidbody RB;public float speed=12,rotSpeed=300,acceleration=20,modifierValue=1.5f,moveRot;public bool modifier;public Vector3 moveDir;}
public class nyulscript:MonoBehaviour
{
    public bool started=true,attacking;public float size=1;
    public aiscript AI;public sensoryscript senses;public ragdollactivatescript ragdoll;public breakablescript breakable;public playerdeathtriggerscript attackTriggerScript;
    public void SetAnims(float v){}
}
public class aiscript:MonoBehaviour
{
    public nyulscript nyul;public sensoryscript senses;public ragdollactivatescript ragdoll;public movecreaturescript move;
    public bool alive=true,goToTarget=true,moved=true,flee=true,fleeing;
    public float flightFight=10,moveSpeed=1,minFleeSpeed=.5f,walkSpeedModifier=.3f,targetDistGoal=1,angleFromTarget,distFromTarget,attackSpeed=.5f;
    public Vector3 interestPos;
}
public class pickupable:MonoBehaviour{public bool pickedUp;}
public class breakablescript:MonoBehaviour{}
public class tosaveitemscript:MonoBehaviour{public int id=50;public uint idInSave;public nyulscript nyul;}
public class savedatascript{public static savedatascript s;public Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();}
public class playerdeathtriggerscript:MonoBehaviour{public Collider selfCol;public float damagePlayer=5;public survivalscript pscript;}
public class survivalscript:MonoBehaviour{}
public class fpscontroller:MonoBehaviour{public bool loadedmove=true,died;public Rigidbody RB;public CapsuleCollider CBody;public Collider[] C;}
public class mpplayerscript:MonoBehaviour{public ulong steamID;public bool readyToMove;public Transform THead;public CapsuleCollider CBody;public Collider[] cols;}
public class syncScript
{
    public static syncScript s;public float playerRemoveTime=30;public int sends;public float damage;
    public Dictionary<Steamworks.CSteamID,playerSyncClass> players=new Dictionary<Steamworks.CSteamID,playerSyncClass>();
    public class playerSyncClass{public GameObject player;public mpplayerscript mpscript;public float lastTime;}
    public void SendDmgPlayer(mpplayerscript p,float d,Vector3 direction){sends++;damage+=d;}
}
public class mainscript
{
    public static mainscript s;public static Vector3d offset;public bool AIDisable;public fpscontroller player;
    public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d(p.x+offset.x,p.y+offset.y,p.z+offset.z);}
    public static Vector3 UnityPosFromGlobal(Vector3d p){return new Vector3((float)(p.x-offset.x),(float)(p.y-offset.y),(float)(p.z-offset.z));}
}
namespace TldMp
{
    public class StablePeer{public bool Ready=true;public ulong Session=200;}
    public class StableNode{public bool IsHost=true,Compatible=true;public string Error;public ulong Own=10,Session=100;public Dictionary<ulong,StablePeer> Peers=new Dictionary<ulong,StablePeer>();}
    public static class StableGameBridge{internal static bool IsRabbitGuest;}
    public static class RabbitContact{internal static bool IgnoreForNavigation(Collider c){return false;}}
}
internal static class RabbitCombatReachTests
{
    static int checks;
    static readonly MethodInfo fixedUpdate=typeof(RabbitLocomotion).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
    static void Check(bool b,string message){checks++;if(!b)throw new Exception(message);}
    static void Near(float a,float b,string message){Check(Math.Abs(a-b)<.001f,message+" ["+a+" vs "+b+"]");}
    static GameObject Child(GameObject root,Vector3 p){var g=new GameObject();g.transform.parent=root.transform;g.transform.position=root.transform.TransformPoint(p);return g;}
    sealed class Scene
    {
        internal GameObject rabbit,human;internal nyulscript n;internal aiscript ai;internal RabbitLocomotion motor;
        internal CapsuleCollider hull,victim;internal SphereCollider bite;internal Collider source;internal savedatascript save;
        internal fpscontroller local;internal mpplayerscript guest;internal StableNode node;internal bool online;
        internal float health=100,lastAttack=-100;internal int starts,renders,scans;internal bool everContact;
        internal Scene(bool remote,float scale,float head)
        {
            Physics.solids.Clear();Physics.wallZ=float.NaN;Physics.ground=true;Physics.slope=Physics.groundY=0;Physics.onClosest=Physics.onPenetration=Physics.onRay=null;
            Time.time=Time.realtimeSinceStartup=Time.fixedTime=100;Time.deltaTime=Time.fixedDeltaTime=.02f;
            mainscript.offset=new Vector3d();StableGameBridge.IsRabbitGuest=false;
            save=savedatascript.s=new savedatascript();syncScript.s=new syncScript();mainscript.s=new mainscript();
            var lp=new GameObject();local=lp.AddComponent<fpscontroller>();local.RB=lp.AddComponent<Rigidbody>();
            mainscript.s.player=local;human=remote?new GameObject():lp;human.transform.position=new Vector3(0,0,4);
            victim=human.AddComponent<CapsuleCollider>();victim.radius=.3f;victim.height=1.6f;victim.direction=1;victim.center=new Vector3(0,.8f,0);
            var headObject=Child(human,new Vector3(0,1.5f,0));
            var stimulus=headObject.AddComponent<stimulscript>();stimulus.visible=true;stimulus.stimuls.Add(new stimul(Vector3.zero,stimulType.threat,10));
            source=headObject.AddComponent<SphereCollider>();((SphereCollider)source).radius=.05f;source.isTrigger=true;Physics.visual=source;
            local.CBody=remote?null:victim;local.C=remote?new Collider[0]:new Collider[]{victim};
            if(remote)
            {
                lp.transform.position=new Vector3(100,0,100);guest=human.AddComponent<mpplayerscript>();guest.steamID=20;
                guest.THead=headObject.transform;guest.CBody=victim;guest.cols=new Collider[]{victim};
                node=new StableNode();node.Peers[20]=new StablePeer();online=true;
                syncScript.s.players[new Steamworks.CSteamID(20)]=new syncScript.playerSyncClass{player=human,mpscript=guest,lastTime=100};
            }
            rabbit=new GameObject();rabbit.name="MainCollider";rabbit.transform.localScale=new Vector3(scale,scale,scale);
            n=rabbit.AddComponent<nyulscript>();ai=rabbit.AddComponent<aiscript>();ai.nyul=n;n.AI=ai;
            n.senses=ai.senses=rabbit.AddComponent<sensoryscript>();n.senses.eyes=Child(rabbit,new Vector3(0,.75f,0)).transform;
            n.ragdoll=ai.ragdoll=rabbit.AddComponent<ragdollactivatescript>();n.breakable=rabbit.AddComponent<breakablescript>();
            ai.move=rabbit.AddComponent<movecreaturescript>();ai.move.RB=rabbit.AddComponent<Rigidbody>();
            hull=rabbit.AddComponent<CapsuleCollider>();hull.radius=.3f;hull.height=1;hull.direction=2;hull.center=new Vector3(0,.353f,0);
            n.ragdoll.animColliders=new Collider[]{hull};var triggerObject=Child(rabbit,new Vector3(0,.5f,head));
            var trigger=triggerObject.AddComponent<playerdeathtriggerscript>();bite=triggerObject.AddComponent<SphereCollider>();bite.radius=.25f;bite.isTrigger=true;trigger.selfCol=bite;n.attackTriggerScript=trigger;triggerObject.SetActive(false);
            var item=rabbit.AddComponent<tosaveitemscript>();item.idInSave=7;item.nyul=n;save.items[7]=item;
            Physics.solids.Add(victim);Physics.solids.Add(hull);
            RabbitPlayers.Reset();Bind();RabbitCombatReach.Resolve=RabbitPlayers.CombatTarget;
            Scan();Decide(false);motor=rabbit.GetComponent<RabbitLocomotion>();
        }
        internal void Bind(){RabbitPlayers.Bind(online,true,local,save,node);}
        internal void Scan()
        {
            scans++;n.senses.stimuls.Clear();n.senses.colliders=new Collider[]{source};
            if(!online)
            {
                var delta=source.transform.position-n.senses.eyes.position;
                bool seen=Vector3.Angle(n.senses.eyes.forward,Vector3.ProjectOnPlane(delta,n.senses.eyes.up))<n.senses.fovY&&
                    Vector3.Angle(n.senses.eyes.forward,Vector3.ProjectOnPlane(delta,n.senses.eyes.right))<n.senses.fovX;
                if(seen)n.senses.stimuls.Add(new stimul(source.transform.position,stimulType.threat,10));
            }
            RabbitPlayers.Scan(n.senses);
        }
        internal void Decide(bool attacks)
        {
            renders++;
            if(n.senses.stimuls.Count>0)ai.interestPos=n.senses.stimuls[n.senses.stimuls.Count-1].pos;
            var d=ai.interestPos-rabbit.transform.position;d.y=0;ai.moved=d.magnitude>1;ai.moveSpeed=1;
            if(n.attacking&&Time.time-lastAttack>2){n.attacking=false;ai.move.modifier=false;bite.gameObject.SetActive(false);}
            if(attacks&&!n.attacking&&Time.time-lastAttack>3&&d.magnitude<4&&Vector3.Angle(rabbit.transform.forward,d)<10)
            {
                bool allow=RabbitCombatReach.AllowAttack(n);
                Check(!allow||Geometry.Depth(bite,victim)>0,"entry never succeeds outside native sphere");
                if(allow){n.attacking=true;lastAttack=Time.time;starts++;bite.gameObject.SetActive(true);ai.move.modifier=true;}
            }
            RabbitLocomotion.Observe(ai);
        }
        internal void Step()
        {
            fixedUpdate.Invoke(motor,null);
            var rb=ai.move.RB;var old=rb.position;rb.position+=rb.velocity*.02f;
            if(Geometry.Depth(hull,victim)>0){rb.position=old;rb.velocity=new Vector3(0,rb.velocity.y,0);}
            Check(Geometry.Depth(hull,victim)<.0001f,"approach never walks through solid player");
            bool contact=Geometry.Depth(bite,victim)>0;everContact|=contact;
            if(n.attacking&&contact)
            {
                if(online)RabbitPlayers.ProbeDamage(n);else health-=5*.02f;
            }
        }
        internal void Run(int fps,float seconds,bool attacks)
        {
            float nextRender=Time.time,nextScan=Time.time,end=Time.time+seconds;
            while(Time.fixedTime<end)
            {
                float tick=Time.fixedTime+.02f;
                while(nextRender<=tick)
                {
                    Time.time=Time.realtimeSinceStartup=nextRender;
                    if(nextRender>=nextScan){Scan();nextScan=nextRender+.1f;}
                    Decide(attacks);nextRender+=1f/fps;
                }
                Time.time=Time.realtimeSinceStartup=Time.fixedTime=tick;Step();
            }
        }
    }
    static void GeometryChecks()
    {
        var s=new Scene(false,1,.7f);
        // Test the finite capsule segment, endpoints, and inside-point semantics.
        Near((s.victim.ClosestPoint(new Vector3(0,.8f,3))-new Vector3(0,.8f,4)).magnitude,.3f,"capsule side surface");
        Near(s.victim.ClosestPoint(new Vector3(0,2,4)).y,1.6f,"capsule upper endpoint");
        Check(s.victim.ClosestPoint(new Vector3(0,.8f,4))==new Vector3(0,.8f,4),"inside point stays inside");
        var target=RabbitPlayers.CombatTarget(s.ai);Check(target!=null,"real SP Scan resolves target");
        Check(!RabbitCombatReach.AllowAttack(s.n),"inactive attack shape cannot hit at four metres");
        s.rabbit.transform.position=new Vector3(0,0,3);
        Check(!s.bite.gameObject.activeInHierarchy&&RabbitCombatReach.AllowAttack(s.n),"inactive shape supports real preattack overlap");
        // Put the separator BEFORE the player's front surface (z=3.7), not
        // inside the player behind an already-embedded sphere center.
        s.rabbit.transform.position=new Vector3(0,0,2.9f);Physics.wallZ=3.65f;
        Check(!RabbitCombatReach.AllowAttack(s.n),"overlap cannot authorize bite through intervening wall");
        Physics.wallZ=float.NaN;
        Physics.onPenetration=delegate{s.local.died=true;};
        Check(!RabbitCombatReach.AllowAttack(s.n),"post-query actor death rejects attack");
        Physics.onPenetration=null;s.local.died=false;
        s.bite.radius=float.NaN;Check(RabbitCombatReach.Measure(s.n,target).Gap<0,"nonfinite sphere fails closed");
        s.bite.radius=.25f;s.n.size=.5f;Check(RabbitCombatReach.AllowAttack(s.n),"small living rabbit keeps harmless native animation");
        s.ai.alive=false;Check(!RabbitCombatReach.AllowAttack(s.n),"dead small rabbit cannot reenter attack");
    }
    static void Provenance()
    {
        foreach(bool remote in new[]{false,true})
        {
            var s=new Scene(remote,1,.7f);var target=RabbitPlayers.CombatTarget(s.ai);
            Check(target!=null,"Scan publishes "+(remote?"guest":"native local")+" winner");
            s.ai.flightFight=-20;Check(target.Valid(),"captured actor survives fear flip");
            Check(RabbitPlayers.CombatTarget(s.ai)==null,"negative fear never starts assisted pursuit");
            s.ai.flightFight=10;var old=s.ai.interestPos;s.ai.interestPos+=new Vector3(.01f,0,0);
            Check(RabbitPlayers.CombatTarget(s.ai)==null,"nearby sound point cannot impersonate visual source");s.ai.interestPos=old;
            s.n.senses.stimuls.Clear();Check(RabbitPlayers.CombatTarget(s.ai)==null,"replaced stimulus invalidates sighting");
            s.Scan();s.Decide(false);target=RabbitPlayers.CombatTarget(s.ai);Check(target!=null,"fresh sighting resolves again");
            Time.realtimeSinceStartup+=.6f;Check(RabbitPlayers.CombatTarget(s.ai)==null,"stale visual source expires");
            Time.realtimeSinceStartup-=.6f;s.Scan();s.Decide(false);target=RabbitPlayers.CombatTarget(s.ai);
            var replacement=s.human.AddComponent<CapsuleCollider>();replacement.radius=.3f;replacement.height=1.6f;
            if(remote)s.guest.CBody=replacement;else s.local.CBody=replacement;
            Check(!target.Valid(),"body replacement invalidates captured target");
            if(remote)s.guest.CBody=s.victim;else s.local.CBody=s.victim;
            s.Scan();s.Decide(false);target=RabbitPlayers.CombatTarget(s.ai);
            RabbitPlayers.Bind(s.online,false,s.local,s.save,s.node);s.Bind();
            Check(!target.Valid(),"same references after world rebound cannot revive old target");
            s.Scan();s.Decide(false);target=RabbitPlayers.CombatTarget(s.ai);
            if(remote){s.node.Peers[20].Session++;Check(!target.Valid(),"new peer session invalidates target");}
            else{s.n.senses.hasEyes=false;s.Scan();Check(RabbitPlayers.CombatTarget(s.ai)==null,"scan losing eyes clears provenance");}
        }
        var guestScene=new Scene(true,1,.7f);guestScene.node.IsHost=false;guestScene.Bind();guestScene.Scan();
        Check(RabbitPlayers.CombatTarget(guestScene.ai)==null,"guest role cannot choose authoritative combat target");
    }
    static void Pursuit()
    {
        foreach(bool remote in new[]{false,true})foreach(int fps in new[]{30,60,144})foreach(float scale in new[]{.6f,.975f,1.1f})
        {
            float head=scale<.9f?.7f:.35f;
            var s=new Scene(remote,scale,head);s.Run(fps,5,true);
            Check(s.starts>0&&s.everContact,"stationary victim bitten without walking forward at "+fps+" scale "+scale+" remote "+remote);
            Check((s.human.transform.position-s.rabbit.transform.position).z<1,"controller advances past old one metre stop");
            Check(remote?syncScript.s.damage>0:s.health<100,"contact reaches unchanged native damage route");
            Check(s.renders>fps*4.9f&&s.renders<fps*5.2f,"independent render cadence actually used");
            Check(s.ai.move.RB.velocity.magnitude<1,"contact approach settles");
        }
        var tangent=new Scene(false,1,.35f);tangent.rabbit.transform.position=new Vector3(0,0,3.1f);
        tangent.Scan();tangent.Decide(false);tangent.Run(60,1,true);
        Check(tangent.starts>0,"exact surface tangent advances into real penetration");
        var wall=new Scene(false,1,.7f);Physics.wallZ=2;wall.Run(60,3,true);
        Check(wall.starts==0&&wall.rabbit.transform.position.z<2,"wall blocks combat navigation and attack");
        var other=new Scene(false,1,.7f);var blocker=new GameObject().AddComponent<CapsuleCollider>();blocker.radius=.3f;blocker.height=2;blocker.center=new Vector3(0,1,2);
        Physics.solids.Add(blocker);other.Run(60,.5f,true);
        Check(other.rabbit.transform.position.z<1.5f,"unrelated player capsule is not target-exempt");
        var elevated=new Scene(false,1,.7f);elevated.human.transform.position+=new Vector3(0,3,0);elevated.Run(60,2,true);
        Check(elevated.starts==0,"unreachable elevated geometry never creates phantom hit");
        var moving=new Scene(false,1,.35f);moving.Run(60,.4f,false);moving.human.transform.position+=new Vector3(.4f,0,1);
        moving.Scan();moving.Decide(false);var chosen=RabbitPlayers.CombatTarget(moving.ai);
        Check(chosen!=null&&(chosen.Aim()-moving.human.transform.position).z<.1f,"pursuit aim follows current actor transform");
        moving.Run(60,3,true);Check(moving.starts>0,"moving target can be reacquired and reached");
        var committed=new Scene(false,1,.35f);committed.Run(60,3,true);
        committed.n.attacking=false;committed.rabbit.transform.position=new Vector3(0,0,3.11f);committed.Scan();committed.Decide(false);
        committed.n.attacking=true;RabbitLocomotion.Observe(committed.ai);
        float heading=committed.rabbit.transform.rotation.yaw;
        committed.human.transform.position+=new Vector3(2,0,2);committed.ai.flightFight=-20;committed.ai.interestPos=new Vector3(0,0,-20);
        for(int i=0;i<10;i++){Time.time+=.02f;RabbitLocomotion.Observe(committed.ai);committed.Step();}
        Near(committed.rabbit.transform.rotation.yaw,heading,"strike does not rotate toward fear or moving victim");
        Check(!RabbitCombatReach.AllowAttack(committed.n),"escaped victim cannot be damaged by entry geometry");
        var shift=new Scene(false,1,.7f);shift.Run(60,.3f,false);var t=RabbitPlayers.CombatTarget(shift.ai);float before=RabbitCombatReach.Measure(shift.n,t).Gap;
        var delta=new Vector3(400,0,-300);shift.rabbit.transform.position-=delta;shift.human.transform.position-=delta;
        mainscript.offset=new Vector3d(delta.x,delta.y,delta.z);
        Near(RabbitCombatReach.Measure(shift.n,t).Gap,before,"recentering preserves real reach geometry");
    }
    public static int Main()
    {
        try{GeometryChecks();Provenance();Pursuit();Console.WriteLine("PASS "+checks+" coupled rabbit combat reach checks");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
