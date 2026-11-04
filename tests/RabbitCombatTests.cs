using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using TldMp;

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
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero{get{return new Vector3();}}
        public float sqrMagnitude{get{return x*x+y*y+z*z;}}
        public float magnitude{get{return (float)Math.Sqrt(x*x+y*y+z*z);}}
        public Vector3 normalized{get{var m=magnitude;return m>0?this/m:new Vector3();}}
        public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator*(Vector3 a,float s){return new Vector3(a.x*s,a.y*s,a.z*s);}
        public static Vector3 operator/(Vector3 a,float s){return new Vector3(a.x/s,a.y/s,a.z/s);}
        public static float Dot(Vector3 a,Vector3 b){return a.x*b.x+a.y*b.y+a.z*b.z;}
        public static Vector3 ProjectOnPlane(Vector3 v,Vector3 n){var d=Dot(v,n);return new Vector3(v.x-n.x*d,v.y-n.y*d,v.z-n.z*d);}
        public static float Angle(Vector3 a,Vector3 b){var ma=a.magnitude;var mb=b.magnitude;if(ma<1e-6f||mb<1e-6f)return 0f;var d=Dot(a,b)/(ma*mb);if(d>1)d=1;if(d<-1)d=-1;return (float)(Math.Acos(d)*180.0/Math.PI);}
        public static bool operator==(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        public static bool operator!=(Vector3 a,Vector3 b){return !(a==b);}
        public override bool Equals(object o){return o is Vector3&&(Vector3)o==this;}
        public override int GetHashCode(){return x.GetHashCode()^y.GetHashCode()^z.GetHashCode();}
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}
        public static Quaternion identity{get{return new Quaternion(0,0,0,1);}}
    }
    public struct Vector3d{public double x,y,z;public Vector3d(double a,double b,double c){x=a;y=b;z=c;}}
    public class Object{public bool destroyed;public static void Destroy(Object o){if(o!=null)o.destroyed=true;}}
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform{get{return gameObject==null?null:gameObject.transform;}}
        public bool enabled=true;
        public bool isActiveAndEnabled{get{return enabled&&gameObject!=null&&gameObject.activeInHierarchy;}}
        public T GetComponent<T>() where T:Component{return gameObject==null?null:gameObject.Get<T>();}
        public bool CompareTag(string t){return gameObject!=null&&gameObject.tag==t;}
        public T GetComponentInParent<T>() where T:Component
        {
            var g=gameObject;
            while(g!=null){var c=g.Get<T>();if(c!=null)return c;var pt=g.transform==null?null:g.transform.parent;g=pt==null?null:pt.gameObject;}
            return null;
        }
    }
    public class Transform:Component
    {
        public Vector3 position,eulerAngles,localScale;
        public Vector3 forward;
        public Vector3 up;
        public Vector3 right;
        public Transform parent;
        public bool IsChildOf(Transform p){var t=this;while(t!=null){if(ReferenceEquals(t,p))return true;t=t.parent;}return false;}
    }
    public class GameObject:Object
    {
        public int layer;
        public string tag="";
        public bool activeInHierarchy=true;
        public Transform transform;
        public static int count;
        public readonly Dictionary<Type,Component> comps=new Dictionary<Type,Component>();
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
    public enum ForceMode{Force,Impulse}
    public struct LayerMask{public int value;public static implicit operator int(LayerMask m){return m.value;}public static implicit operator LayerMask(int v){return new LayerMask{value=v};}}
    public class Collider:Component{public bool isTrigger;}
    public struct RaycastHit{public Vector3 point;public Vector3 normal;public float distance;public Collider collider;}
    public class Rigidbody:Component
    {
        public bool isKinematic;
        public Vector3 lastImpulse;public Vector3 lastPoint;public ForceMode lastMode;public int impulseCalls,forceCalls;
        public void AddForceAtPosition(Vector3 f,Vector3 p,ForceMode m){impulseCalls++;lastImpulse=f;lastPoint=p;lastMode=m;}
        public void AddForce(Vector3 f){forceCalls++;lastImpulse=f;}
    }
    public static class Physics
    {
        public static RaycastHit[] rayResult=new RaycastHit[0];
        public static Collider[] boxResult=new Collider[0];
        public static Action onRaycast,onBox;
        public static int rayCalls,boxCalls;public static int lastMask;public static QueryTriggerInteraction lastMode;public static float lastDist;
        public static Vector3 lastOrigin,lastDir,lastCenter,lastHalf;public static Quaternion lastRot;
        public static Func<Vector3,Vector3,float,LayerMask,QueryTriggerInteraction,RaycastHit[]> rayFactory;
        public static RaycastHit[] RaycastAll(Vector3 o,Vector3 d,float dist,LayerMask mask,QueryTriggerInteraction mode){rayCalls++;lastOrigin=o;lastDir=d;lastDist=dist;lastMask=mask;lastMode=mode;if(onRaycast!=null)onRaycast();if(rayFactory!=null)return rayFactory(o,d,dist,mask,mode);return rayResult;}
        public static Collider[] OverlapBox(Vector3 c,Vector3 half,Quaternion r,LayerMask mask,QueryTriggerInteraction mode){boxCalls++;lastCenter=c;lastHalf=half;lastRot=r;lastMask=mask;lastMode=mode;if(onBox!=null)onBox();return boxResult;}
        public static Collider[] OverlapSphere(Vector3 p,float r,LayerMask m,QueryTriggerInteraction q){return new Collider[0];}
    }
    public static class Mathf
    {
        public static float Lerp(float a,float b,float t){if(t<0)t=0;if(t>1)t=1;return a+(b-a)*t;}
        public static float InverseLerp(float a,float b,float v){return a==b?0f:(v-a)/(b-a);}
        public static float Pow(float x,float y){return (float)Math.Pow(x,y);}
        public static float Max(float a,float b){return a>b?a:b;}
        public static float Sqrt(float v){return (float)Math.Sqrt(v);}
    }
    public enum stimulType{nothing,food,water,threat,light,command}
    public class stimul{public Vector3 pos;public stimulType tipus;public float value;public stimul(Vector3 p,stimulType t,float v){pos=p;tipus=t;value=v;}}
    public enum ammoType{Shot=0}
    public class ammoscript:MonoBehaviour{public mainscript.ammoType tipus;}
    public class partconditionscript:MonoBehaviour{public float state;}
    public class nyulscript:MonoBehaviour{public ragdollactivatescript ragdoll;public aiscript AI;public sensoryscript senses;public breakablescript breakable;public float size;public bool started=true;}
    public class ragdollactivatescript:MonoBehaviour{public bool ragdoll;}
    public class aiscript:MonoBehaviour{public nyulscript nyul;public sensoryscript senses;}
    public class sensoryscript:MonoBehaviour
    {
        public bool alive=true,hasEars=true,hasEyes=true;
        public Transform eyes;
        public float fovX=50f,fovY=50f,fovZ=900f,radius=30f;
        public LayerMask visionMask=1;
        public List<stimul> stimuls=new List<stimul>();
    }
    public class breakablescript:MonoBehaviour
    {
        public float health=50;public bool decreaseAfterAttack;public int shootCalls,breakCalls;public float lastForce;public Vector3 lastDir;public bool throwOnHit;
        public Action onBreak;
        public void TryBreakShoot(float force,Vector3 dir){if(throwOnHit)throw new Exception("shoot");shootCalls++;lastForce=force;lastDir=dir;health-=force;if(onBreak!=null)onBreak();}
        public void TryBreak(float force){if(throwOnHit)throw new Exception("melee");breakCalls++;lastForce=force;if(force>=health)Break();else if(decreaseAfterAttack)health-=force;if(onBreak!=null)onBreak();}
        public void Break(){health=0;}
    }
    public class breakchilds:MonoBehaviour{public float modifier=1f;public breakablescript P;public void TryBreakShoot(float force,Vector3 dir){P.TryBreakShoot(force*modifier,dir);}public void TryBreak(float force){P.TryBreak(force*modifier);}}
    public class ragdollchildpos:MonoBehaviour{public Transform[] childs;}
    public class weaponscript:MonoBehaviour
    {
        public int shotCount=1;public LayerMask LM=1;public Transform S;
        public partconditionscript condition;public float maxConditionWeakness=0.4f;
        public ammoscript ammo;public float minShootTime=0.5f;public pickupable P;
    }
    public class meleeweaponscript:MonoBehaviour{public pickupable p;public LayerMask mask=1;public float force=5,damage=4;public Transform Trigger,T;public Vector3 lastT;}
    public class pickupable:MonoBehaviour
    {
        public bool started=true;public tosaveitemscript tosave;
        public weaponscript weapon;public meleeweaponscript melee;
        public bool pickedUpState;public int invCount;
        public bool PickedUp(bool noRagdol){return pickedUpState;}
        public int inInventory(){return invCount;}
    }
    public class tosaveitemscript:MonoBehaviour
    {
        public bool started=true;public int id;public uint idInSave;
        public pickupable P;public nyulscript nyul;public weaponscript weapon;
        public bool mppickedup,mpequipped,mpstored,mpweared;
        public Transform useThisForPos;public ragdollchildpos ragdollPos;
    }
    public class savedatascript{public static savedatascript s=new savedatascript();public Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();}
    public class survivalscript:MonoBehaviour{}
    public class playerdeathtriggerscript:MonoBehaviour{public Collider selfCol;public float damagePlayer=3f;}
    public class mpplayerscript:MonoBehaviour{public ulong steamID;public bool readyToMove=true;public Transform THead,Th,handParent;}
    public class syncScript:MonoBehaviour
    {
        public static syncScript s=new syncScript();
        public float syncItemInterwall=0.5f,playerRemoveTime=30f;
        public Dictionary<CSteamID,playerSyncClass> players=new Dictionary<CSteamID,playerSyncClass>();
        public int dmgSends;
        public void SendDmgPlayer(mpplayerscript mp,float dmg,Vector3 dir){dmgSends++;}
        public class playerSyncClass{public ulong steamID;public GameObject player;public float lastTime;public mpplayerscript mpscript;}
    }
    public class mainscript:MonoBehaviour
    {
        public static mainscript s=new mainscript();
        public fpscontroller player;
        public Vector3d global=new Vector3d(5000,0,-3000);
        public static float[] ammoMass=new float[]{0.01f,0.02f};
        public static float[] ammoVelocity=new float[]{300f,400f};
        public float jouleToHp=1f;
        public static Vector3 UnityPosFromGlobal(Vector3d g){return new Vector3((float)(g.x-s.global.x),(float)(g.y-s.global.y),(float)(g.z-s.global.z));}
        public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d(p.x+s.global.x,p.y+s.global.y,p.z+s.global.z);}
        public enum ammoType{Shot=0,Buck=1}
        public static int count;
    }
    public class fpscontroller:MonoBehaviour{public bool loadedmove=true;public pickupable pickedUp,inHandP;public float FrayRange=15f;public Transform Th;}
    public class menuhandler{public static menuhandler s=new menuhandler();}
}
namespace TldMp
{
    public class StablePeer{public ulong Session=1;public bool Ready=true;}
    public class StableNode
    {
        public bool IsHost,Compatible=true,Ready=true;
        public string Error;
        public ulong Own,Session;
        public readonly ulong Host;
        public Dictionary<ulong,StablePeer> Peers=new Dictionary<ulong,StablePeer>();
        public List<byte[]> sent=new List<byte[]>();
        public bool sendResult=true;
        public bool SendRabbitRequest(byte[] b){if(sendResult)sent.Add(b);return sendResult;}
    }
}
public static class RabbitCombatTests
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
        return n;
    }
    static tosaveitemscript Item(savedatascript d,GameObject g,nyulscript n,uint idIn)
    {
        var t=g.Add(new tosaveitemscript{id=50,idInSave=idIn,nyul=n});
        d.items[idIn]=t;
        return t;
    }
    static tosaveitemscript ToolItem(savedatascript d,uint idIn,out weaponscript w,out pickupable P)
    {
        var g=new GameObject();
        var t=g.Add(new tosaveitemscript{id=90,idInSave=idIn});
        P=g.Add(new pickupable{tosave=t});
        t.P=P;
        w=g.Add(new weaponscript{P=P,ammo=new ammoscript()});
        P.weapon=w;
        g.Add(w.ammo);
        w.ammo.tipus=mainscript.ammoType.Shot;
        w.S=new GameObject().transform;
        w.S.position=new Vector3(0,0,0);
        w.S.forward=new Vector3(0,0,1);
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
        mp.Th=head.transform;
        mp.handParent=new GameObject().transform;
        mp.handParent.position=headPos;
        var pc=new syncScript.playerSyncClass{steamID=id,player=root,mpscript=mp,lastTime=100f};
        syncScript.s.players[id]=pc;
        return new GuestRef{root=root,mp=mp,pc=pc};
    }
    static TldMp.StableNode Host(ulong own){var n=new TldMp.StableNode{Own=own,Session=100,Compatible=true};n.Peers[20]=new TldMp.StablePeer{Session=200,Ready=true};return n;}
    static void Setup(TldMp.StableNode n,fpscontroller p,savedatascript d,bool host)
    {
        n.IsHost=host;
        TldMp.RabbitPlayers.Reset();
        TldMp.RabbitPlayers.Bind(true,true,p,d,n);
        TldMp.RabbitCombat.Reset();
        TldMp.RabbitCombat.Bind(true,true,p,d,n);
    }
    static TldMp.RabbitCommand ShotCmd(uint tool,ulong token,int index,Vector3 origin,Vector3 dir)
    {
        var g=mainscript.GlobalFromUnityPos(origin);
        return new TldMp.RabbitCommand{Kind=TldMp.RabbitCommandKind.Shot,Tool=tool,Token=token,Index=index,Position=new TldMp.RabbitPoint{X=g.x,Y=g.y,Z=g.z},Direction=new TldMp.RabbitVector{X=dir.x,Y=dir.y,Z=dir.z}};
    }
    static TldMp.RabbitCommand MeleeCmd(uint tool,ulong token,Vector3 center,Vector3 ext,Quaternion rot)
    {
        var g=mainscript.GlobalFromUnityPos(center);
        return new TldMp.RabbitCommand{Kind=TldMp.RabbitCommandKind.Melee,Tool=tool,Token=token,Position=new TldMp.RabbitPoint{X=g.x,Y=g.y,Z=g.z},Extents=new TldMp.RabbitVector{X=ext.x,Y=ext.y,Z=ext.z},Rotation=new TldMp.RabbitRotation{X=rot.x,Y=rot.y,Z=rot.z,W=rot.w}};
    }
    static Collider Col(Transform parent,string tag,int layer,bool trigger)
    {
        var g=new GameObject{tag=tag,layer=layer};
        g.transform.parent=parent;
        return g.Add(new Collider{isTrigger=trigger});
    }
    class F
    {
        internal savedatascript d;
        internal TldMp.StableNode host;
        internal fpscontroller p;
        internal GuestRef guest;
        internal GameObject rab;
        internal nyulscript nyul;
        internal tosaveitemscript rabItem;
        internal Collider rabCol;
        internal Rigidbody rb;
        internal tosaveitemscript tool;
        internal weaponscript w;
        internal pickupable P;
        internal GameObject mg;
        internal tosaveitemscript mt;
        internal meleeweaponscript m;
        internal pickupable mP;
        internal double now;
        internal ulong tok;
        internal F()
        {
            GameObject.count=0;
            d=new savedatascript();
            host=Host(10);
            p=new GameObject().Add(new fpscontroller());
            p.Th=new GameObject().transform;
            mainscript.s.player=p;
            mainscript.s.global=new Vector3d(5000,0,-3000);
            mainscript.ammoMass=new float[]{0.01f,0.02f};
            mainscript.ammoVelocity=new float[]{300f,400f};
            guest=Guest(20,Vector3.zero);
            guest.mp.handParent.parent=guest.root.transform;
            Setup(host,p,d,true);
            rab=new GameObject();
            nyul=Nyul(rab);
            rabItem=Item(d,rab,nyul,7);
            rabCol=Col(rab.transform,"Flesh",9,false);
            rabCol.transform.position=new Vector3(0,0,10);
            rb=rab.Add(new Rigidbody());
            tool=ToolItem(d,40,out w,out P);
            tool.mppickedup=true;
            tool.transform.parent=guest.mp.handParent;
            mg=new GameObject();
            mt=mg.Add(new tosaveitemscript{id=95,idInSave=50});
            mP=mg.Add(new pickupable{tosave=mt});
            mt.P=mP;
            m=mg.Add(new meleeweaponscript{p=mP});
            mP.melee=m;
            m.Trigger=new GameObject().transform;
            m.Trigger.localScale=new Vector3(0.4f,0.3f,0.2f);
            m.T=new GameObject().transform;
            m.lastT=Vector3.zero;
            mt.mppickedup=true;
            mg.transform.parent=guest.mp.handParent;
            d.items[50]=mt;
            Physics.rayResult=new RaycastHit[0];
            Physics.boxResult=new Collider[0];
            Physics.onRaycast=Physics.onBox=null;
            Physics.rayFactory=null;
            Physics.rayCalls=Physics.boxCalls=0;
            Time.frameCount=0;Time.deltaTime=0.02f;Time.realtimeSinceStartup=100;
            now=0;tok=0;
        }
        internal TldMp.RabbitCommand Shot(uint t,int index,Vector3 origin,Vector3 dir){return ShotCmd(t,++tok,index,origin,dir);}
        internal TldMp.RabbitCommand Shot(uint t,ulong token,int index,Vector3 origin,Vector3 dir){if(token>tok)tok=token;return ShotCmd(t,token,index,origin,dir);}
        internal TldMp.RabbitCommand Melee(uint t,Vector3 center,Quaternion rot){return MeleeCmd(t,++tok,center,new Vector3(1,1,1),rot);}
        internal TldMp.RabbitCommand Melee(uint t,ulong token,Vector3 center,Quaternion rot){if(token>tok)tok=token;return MeleeCmd(t,token,center,new Vector3(1,1,1),rot);}
        internal void Hit(){Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=rabCol,distance=10f,point=new Vector3(0,0,10)}};}
        internal void Advance(){Time.frameCount++;now+=0.6;}
    }
    static void GroupEnergy()
    {
        var f=new F();
        f.Hit();
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"energy: shot breaks rabbit");
        float E1=f.nyul.breakable.lastForce;
        Check(Math.Abs(E1-0.5f*0.01f*300f*300f)<1f,"energy: condition-null formula");
        Check(f.rb.impulseCalls==1&&f.rb.lastMode==ForceMode.Impulse&&Math.Abs(f.rb.lastImpulse.z-(float)Math.Sqrt(E1)*5f)<0.5f,"energy: impulse sqrt(E)*5 mode");
        f.w.condition=new partconditionscript{state=0f};f.w.gameObject.Add(f.w.condition);
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==2&&Math.Abs(f.nyul.breakable.lastForce-E1)<1f,"energy: state0 full");
        f.w.condition.state=4f;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==3&&Math.Abs(f.nyul.breakable.lastForce-E1*0.16f)<1f,"energy: state4 weakened");
        var before=f.nyul.breakable.shootCalls;
        f.w.ammo.tipus=(mainscript.ammoType)9;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==before,"energy: ammo index OOB rejects");
        f.w.ammo.tipus=mainscript.ammoType.Shot;
        mainscript.ammoVelocity[0]=-300f;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==before,"energy: negative velocity rejects");
        mainscript.ammoVelocity[0]=300f;
        mainscript.ammoMass[0]=float.NaN;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==before&&TldMp.RabbitCombat.Healthy,"energy: NaN mass rejects Healthy");
        mainscript.ammoMass[0]=0.01f;
    }
    static void GroupFilters()
    {
        var f=new F();
        var wall=Col(null,"",0,false);
        wall.transform.position=new Vector3(0,0,5);
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=wall,distance=5f},new RaycastHit{collider=f.rabCol,distance=10f}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==0,"filters: wall before NPC blocks");
        var plant=Col(null,"Plant",0,false);
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=plant,distance=4f},new RaycastHit{collider=f.rabCol,distance=10f}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"filters: Plant passes");
        var cob=Col(null,"Cobweb",0,false);
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=cob,distance=4f},new RaycastHit{collider=f.rabCol,distance=10f}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==2,"filters: Cobweb passes");
        var glass=Col(null,"",23,false);
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=glass,distance=4f},new RaycastHit{collider=f.rabCol,distance=10f}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==3,"filters: layer23 passes");
        var ign=Col(null,"Ignore",0,false);
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=ign,distance=3f},new RaycastHit{collider=f.rabCol,distance=10f}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==4,"filters: Ignore tag skipped");
        var trig=Col(null,"",0,true);
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=trig,distance=4f},new RaycastHit{collider=f.rabCol,distance=10f}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==5,"filters: non-Flesh/Plant trigger skipped");
        var ftr=Col(f.rab.transform,"Flesh",9,true);
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=ftr,distance=8f,point=new Vector3(0,0,8)}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==6,"filters: Flesh trigger on NPC hits");
        var own=Col(f.guest.root.transform,"",0,false);
        var too=Col(f.tool.transform,"",0,false);
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=own,distance=2f},new RaycastHit{collider=too,distance=4f},new RaycastHit{collider=f.rabCol,distance=10f}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==7,"filters: actor+tool descendants skipped");
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,new Vector3(0,0,100),new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==7,"filters: far origin rejects");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,Vector3.zero),f.now);
        Check(f.nyul.breakable.shootCalls==7,"filters: zero dir rejects");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(1e-40f,0,0)),f.now);
        Check(f.nyul.breakable.shootCalls==8,"filters: subnormal dir normalizes");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(float.MaxValue,0,0)),f.now);
        Check(f.nyul.breakable.shootCalls==9,"filters: floatMax dir normalizes");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(float.NaN,0,0)),f.now);
        Check(f.nyul.breakable.shootCalls==9,"filters: NaN dir rejects");
    }
    static void GroupSegment()
    {
        var f=new F();
        var wall=Col(null,"",0,false);
        wall.transform.position=new Vector3(1.5f,0,0);
        var calls=new System.Collections.Generic.List<Vector3>();
        var dists=new System.Collections.Generic.List<float>();
        Physics.rayFactory=(o,dg,dist,mask,mode)=>{
            calls.Add(o);dists.Add(dist);
            if(o.x==0&&dist==2f)return new RaycastHit[]{new RaycastHit{collider=wall,distance=1.5f}};
            return new RaycastHit[]{new RaycastHit{collider=f.rabCol,distance=10f}};
        };
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,new Vector3(2,0,0),new Vector3(0,0,1)),f.now);
        Check(calls.Count==1&&calls[0].x==0&&dists[0]==2f,"segment: head-origin ray queried");
        Check(f.nyul.breakable.shootCalls==0,"segment: wall before origin rejects");
        calls.Clear();dists.Clear();
        Physics.rayFactory=(o,dg,dist,mask,mode)=>{
            calls.Add(o);dists.Add(dist);
            if(o.x==0&&dist==2f)return new RaycastHit[0];
            return new RaycastHit[]{new RaycastHit{collider=f.rabCol,distance=10f}};
        };
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,new Vector3(2,0,0),new Vector3(0,0,1)),f.now);
        Check(calls.Count==2&&calls[1].x==2&&float.IsPositiveInfinity(dists[1]),"segment: shot ray distinct +Inf");
        Check(Physics.lastMode==QueryTriggerInteraction.UseGlobal&&Physics.lastMask==f.w.LM,"segment: native LM+UseGlobal");
        Check(f.nyul.breakable.shootCalls==1,"segment: unobstructed accepts");
        Physics.rayFactory=null;
    }
    static void GroupBurst()
    {
        var f=new F();
        f.w.shotCount=3;
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,1,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==0,"burst: nonzero first rejects");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"burst: start index0");
        var burstTok=f.tok;
        TldMp.RabbitCombat.Receive(20,f.Shot(40,burstTok,0,Vector3.zero,new Vector3(0,0,1)),f.now+0.1);
        Check(f.nyul.breakable.shootCalls==1,"burst: index0 duplicate dedup");
        f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,burstTok,1,Vector3.zero,new Vector3(0,0,1)),f.now+0.2);
        Check(f.nyul.breakable.shootCalls==2,"burst: pellet1 same token");
        f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,burstTok,2,Vector3.zero,new Vector3(0,0,1)),f.now+0.3);
        Check(f.nyul.breakable.shootCalls==3,"burst: pellet2 same token");
        f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,burstTok,1,Vector3.zero,new Vector3(0,0,1)),f.now+0.4);
        Check(f.nyul.breakable.shootCalls==3,"burst: pellet index dup dedup");
        f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,burstTok,5,Vector3.zero,new Vector3(0,0,1)),f.now+0.5);
        Check(f.nyul.breakable.shootCalls==3,"burst: out-of-range index rejects");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==4,"burst: fresh burst next frame accepts");
        ulong cdTok=++f.tok;
        Time.frameCount++;
        TldMp.RabbitCombat.Receive(20,f.Shot(40,cdTok,0,Vector3.zero,new Vector3(0,0,1)),f.now+0.1);
        Check(f.nyul.breakable.shootCalls==4,"burst: cooldown-time rejects");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==5,"burst: after cooldown accepts");
        TldMp.RabbitCombat.Receive(20,f.Shot(40,cdTok,0,Vector3.zero,new Vector3(0,0,1)),f.now+0.1);
        Check(f.nyul.breakable.shootCalls==5,"burst: rejected-cooldown token cannot replay");
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now+0.6);
        Check(f.nyul.breakable.shootCalls==5,"burst: same-frame second start rejects");
        weaponscript w2;pickupable P2;
        var tool2=ToolItem(f.d,41,out w2,out P2);
        tool2.mppickedup=true;tool2.transform.parent=f.guest.mp.handParent;
        w2.minShootTime=0;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(41,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==6,"burst: tool-switch independent");
        TldMp.RabbitCombat.Receive(20,f.Shot(41,0,Vector3.zero,new Vector3(0,0,1)),f.now+0.1);
        Check(f.nyul.breakable.shootCalls==6,"burst: minShootTime0 still frame-capped");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(41,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==7,"burst: next frame accepts");
        var flapTok=f.tok;
        f.host.Peers[20].Ready=false;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==7,"burst: unready peer no shot");
        f.host.Peers[20].Ready=true;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,flapTok,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==7,"burst: Ready-flap preserves nonce watermark");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==8,"burst: same session resumes");
        f.host.Peers[20].Session=300;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,1UL,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==9,"burst: new session resets nonce");
        f.host.Peers[20].Session=200;
    }
    static void GroupOwnership()
    {
        var f=new F();
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(43,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==0,"own: missing canonical tool rejects");
        weaponscript fw;pickupable fP;
        var ftool=ToolItem(f.d,44,out fw,out fP);
        var foreign=new GameObject();
        foreign.Add(new mpplayerscript{steamID=99});
        ftool.transform.parent=foreign.transform;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(44,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==0,"own: foreign-MP-ancestor rejects");
        ftool.mppickedup=true;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(44,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==0,"own: foreign-held rejects");
        weaponscript w2;pickupable P2;
        var wg=ToolItem(f.d,45,out w2,out P2);
        w2.S.position=new Vector3(0,0,5);
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(45,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"own: worldgun near accepts");
        w2.S.position=new Vector3(0,0,100);
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(45,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"own: worldgun far rejects");
        w2.S.position=new Vector3(0,0,5);
        wg.mppickedup=true;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(45,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"own: mp-flagged worldgun rejects");
        wg.mppickedup=false;
        f.p.pickedUp=P2;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(45,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"own: host-held worldgun rejects");
        f.p.pickedUp=null;
        P2.invCount=1;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(45,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"own: inventory worldgun rejects");
        P2.invCount=0;
        var bad=new GameObject();
        var bt=bad.Add(new tosaveitemscript{id=91,idInSave=46});
        var bP=bad.Add(new pickupable{tosave=null});
        bt.P=bP;
        f.d.items[46]=bt;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(46,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"own: P.tosave mismatch rejects");
        f.P.started=false;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"own: unstarted P rejects");
        f.P.started=true;
        f.tool.gameObject.SetActive(false);
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"own: inactive item rejects");
        f.tool.gameObject.SetActive(true);
    }
    static void GroupNpc()
    {
        var f=new F();
        var bcGo=new GameObject();
        bcGo.transform.parent=f.rab.transform;
        bcGo.Add(new breakchilds{P=f.nyul.breakable,modifier=2f});
        var bcCol=Col(bcGo.transform,"Flesh",9,false);
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=bcCol,distance=5f,point=new Vector3(0,0,5)}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        float E=0.5f*0.01f*300f*300f;
        Check(f.nyul.breakable.shootCalls==1&&Math.Abs(f.nyul.breakable.lastForce-2f*E)<1f,"npc: head bc modifier*2");
        f.nyul.senses.alive=false;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==2,"npc: corpse still shootable");
        f.nyul.senses.alive=true;
        var bcOther=f.rab.Add(new breakablescript());
        var bcWrong=new GameObject();
        bcWrong.Add(new breakchilds{P=bcOther,modifier=3f});
        var bcWCol=Col(bcWrong.transform,"",0,false);
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=bcWCol,distance=5f,point=new Vector3(0,0,5)}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==3&&bcOther.shootCalls==0,"npc: detached bc resolves root via P, root breakable");
        Physics.onRaycast=()=>{f.host.Peers[20].Session=999;};
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==3,"npc: session-change during physics no damage");
        Physics.onRaycast=null;
        f.host.Peers[20].Session=200;
        f.nyul.breakable.onBreak=()=>{f.d.items.Remove(7);};
        var impB=f.rb.impulseCalls;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==4,"npc: onBreak removal still damages");
        Check(f.rb.impulseCalls==impB,"npc: removed root gets no impulse");
        f.nyul.breakable.onBreak=null;
        f.d.items[7]=f.rabItem;
        var kin=new GameObject();
        var kNyul=Nyul(kin);
        Item(f.d,kin,kNyul,47);
        var kCol=Col(kin.transform,"Flesh",9,false);
        var kRb=kin.Add(new Rigidbody{isKinematic=true});
        Physics.rayResult=new RaycastHit[]{new RaycastHit{collider=kCol,distance=10f,point=new Vector3(0,0,10)}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(kNyul.breakable.shootCalls==1&&kRb.impulseCalls==0,"npc: kinematic HP no impulse");
        f.nyul.breakable.throwOnHit=true;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(!TldMp.RabbitCombat.Healthy,"npc: native exception fails closed");
        TldMp.RabbitCombat.Bind(true,true,f.p,f.d,f.host);
        Check(!TldMp.RabbitCombat.Healthy,"npc: same-context bind stays failed");
        TldMp.RabbitCombat.Bind(true,false,f.p,f.d,f.host);
        TldMp.RabbitCombat.Bind(true,true,f.p,f.d,f.host);
        Check(TldMp.RabbitCombat.Healthy,"npc: world transition recovers");
    }
    static void GroupGates()
    {
        var f=new F();
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(99,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==0,"gates: unknown peer no shot");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),-1);
        Check(f.nyul.breakable.shootCalls==0,"gates: negative now rejects");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),double.NaN);
        Check(f.nyul.breakable.shootCalls==0,"gates: NaN now rejects");
        var guestNode=new TldMp.StableNode{IsHost=false,Compatible=true,Ready=true,Own=10,Session=100};
        TldMp.RabbitCombat.Bind(true,true,f.p,f.d,guestNode);
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==0,"gates: guest node Receive no-op");
        TldMp.RabbitCombat.Bind(true,true,f.p,f.d,f.host);
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"gates: host context restored");
        TldMp.RabbitCombat.Bind(false,true,f.p,f.d,f.host);
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"gates: disabled no shot");
        TldMp.RabbitCombat.Bind(true,false,f.p,f.d,f.host);
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"gates: world-stopped no shot");
        TldMp.RabbitCombat.Bind(true,true,f.p,f.d,f.host);
        f.p.loadedmove=false;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"gates: unloaded player no shot");
        f.p.loadedmove=true;
        f.host.Compatible=false;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"gates: incompatible node no shot");
        f.host.Compatible=true;
        f.host.Peers.Remove(20);
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"gates: absent peer record no throw");
        f.host.Peers[20]=new TldMp.StablePeer{Session=200,Ready=true};
    }
    static void GroupMelee()
    {
        var f=new F();
        Physics.boxResult=new Collider[]{f.rabCol};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(f.nyul.breakable.breakCalls==1&&Math.Abs(f.nyul.breakable.lastForce-4f)<0.001f,"melee: canonical damage");
        Check(Physics.boxCalls==1&&Physics.lastMode==QueryTriggerInteraction.Collide&&Physics.lastMask==f.m.mask,"melee: mask+Collide");
        Check(Math.Abs(Physics.lastHalf.x-0.4f)<0.001f&&Math.Abs(Physics.lastHalf.y-0.3f)<0.001f&&Math.Abs(Physics.lastHalf.z-0.2f)<0.001f,"melee: exact Trigger.localScale half");
        var big=new TldMp.RabbitCommand{Kind=TldMp.RabbitCommandKind.Melee,Tool=50,Token=++f.tok,Position=f.Melee(50,new Vector3(0,0,10),Quaternion.identity).Position,Extents=new TldMp.RabbitVector{X=999,Y=999,Z=999},Rotation=new TldMp.RabbitRotation{X=0,Y=0,Z=0,W=1}};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,big,f.now);
        Check(f.nyul.breakable.breakCalls==2&&Math.Abs(Physics.lastHalf.x-0.4f)<0.001f,"melee: cmd extents ignored");
        var rab2=new GameObject();
        var nyul2=Nyul(rab2);
        Item(f.d,rab2,nyul2,8);
        var rabCol2=Col(rab2.transform,"Flesh",9,false);
        rab2.Add(new Rigidbody());
        nyul2.breakable.decreaseAfterAttack=true;
        Physics.boxResult=new Collider[]{f.rabCol,rabCol2};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(f.nyul.breakable.breakCalls==3&&nyul2.breakable.breakCalls==1,"melee: two NPCs each once");
        Check(Math.Abs(nyul2.breakable.lastForce-4f*Time.deltaTime)<0.01f,"melee: decreaseAfterAttack host dt");
        var rabColB=Col(f.rab.transform,"Flesh",9,false);
        Physics.boxResult=new Collider[]{f.rabCol,rabColB};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(f.nyul.breakable.breakCalls==4,"melee: same NPC two colliders once");
        var sameFrameTok=++f.tok;
        TldMp.RabbitCombat.Receive(20,f.Melee(50,sameFrameTok,new Vector3(0,0,10),Quaternion.identity),f.now+0.1);
        Check(f.nyul.breakable.breakCalls==4,"melee: second nonce same host frame rejected");
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(f.nyul.breakable.breakCalls==5,"melee: later frame accepts");
        TldMp.RabbitCombat.Receive(20,f.Melee(50,1UL,new Vector3(0,0,10),Quaternion.identity),f.now+0.1);
        Check(f.nyul.breakable.breakCalls==5,"melee: old nonce rejects");
        var bcGo=new GameObject();
        bcGo.transform.parent=f.rab.transform;
        bcGo.Add(new breakchilds{P=f.nyul.breakable,modifier=2f});
        var bcCol=Col(bcGo.transform,"Flesh",9,false);
        Physics.boxResult=new Collider[]{bcCol};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(f.nyul.breakable.breakCalls==6&&Math.Abs(f.nyul.breakable.lastForce-4f)<0.001f,"melee: root TryBreak not bc modifier");
        var eq=new Quaternion(float.Epsilon,float.Epsilon,float.Epsilon,float.Epsilon);
        var qn=Math.Sqrt(4.0*(double)float.Epsilon*float.Epsilon);
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),eq),f.now);
        Check(f.nyul.breakable.breakCalls==7&&Math.Abs(Physics.lastRot.w-(float)(float.Epsilon/qn))<0.001f,"melee: epsilon quat normalizes");
        var bq=new Quaternion(float.MaxValue,0,0,float.MaxValue);
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),bq),f.now);
        Check(f.nyul.breakable.breakCalls==8&&Math.Abs(Physics.lastRot.w-0.7071f)<0.001f,"melee: floatMax quat normalizes");
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),new Quaternion(0,0,0,0)),f.now);
        Check(f.nyul.breakable.breakCalls==8,"melee: zero quat rejects");
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(float.NaN,0,10),Quaternion.identity),f.now);
        Check(f.nyul.breakable.breakCalls==8,"melee: NaN center rejects");
        var loneCol=Col(null,"",0,false);
        Physics.boxResult=new Collider[]{loneCol};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(f.nyul.breakable.breakCalls==8,"melee: non-NPC ignored");
        f.rb.impulseCalls=0;f.rb.forceCalls=0;
        f.m.T.position=new Vector3(0.5f,0,0);
        Physics.boxResult=new Collider[]{f.rabCol};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(f.rb.forceCalls==1&&f.rb.impulseCalls==0,"melee: AddForce not impulse");
        Check(Math.Abs(f.rb.lastImpulse.x-((1f*5f)+0.5f)*0.5f*Time.deltaTime)<0.001f,"melee: native force formula");
        mainscript.s.global=new Vector3d(6000,0,-3000);
        f.m.T.position=new Vector3(-999.5f,0,0);
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(f.rb.forceCalls==2&&Math.Abs(f.rb.lastImpulse.x)<0.001f,"melee: origin-shift between samples zero force");
        mainscript.s.global=new Vector3d(5000,0,-3000);
        f.m.T.position=new Vector3(0.5f,0,0);
        f.nyul.breakable.onBreak=()=>{f.d.items.Remove(7);};
        var fcB=f.rb.forceCalls;
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(f.nyul.breakable.breakCalls==11&&f.rb.forceCalls==fcB,"melee: root removal during callback no force");
        f.nyul.breakable.onBreak=null;
        f.d.items[7]=f.rabItem;
        Physics.onBox=()=>{f.host.Peers[20].Session=999;};
        var bcB=f.nyul.breakable.breakCalls;
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(f.nyul.breakable.breakCalls==bcB&&f.rb.forceCalls==fcB,"melee: session-change during overlap no effects");
        Physics.onBox=null;
        f.host.Peers[20].Session=200;
        var kin=new GameObject();
        var kNyul=Nyul(kin);
        Item(f.d,kin,kNyul,47);
        var kCol=Col(kin.transform,"Flesh",9,false);
        var kRb=kin.Add(new Rigidbody{isKinematic=true});
        Physics.boxResult=new Collider[]{kCol};
        f.Advance();
        TldMp.RabbitCombat.Receive(20,f.Melee(50,new Vector3(0,0,10),Quaternion.identity),f.now);
        Check(kNyul.breakable.breakCalls==1&&kRb.forceCalls==0,"melee: kinematic HP no force");
    }
    static void GroupClient()
    {
        var f=new F();
        var guestNode=new TldMp.StableNode{IsHost=false,Compatible=true,Ready=true,Own=10,Session=100};
        TldMp.RabbitCombat.Bind(true,true,f.p,f.d,guestNode);
        weaponscript cw;pickupable cP;
        ToolItem(f.d,60,out cw,out cP);
        f.p.inHandP=cP;
        cw.shotCount=1;
        Time.frameCount=20;
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),new Vector3(0,0,1),20);
        Check(guestNode.sent.Count==1,"client: held tool shot emits");
        TldMp.RabbitCommand decoded;
        Check(TldMp.RabbitProtocol.TryReadCommand(guestNode.sent[0],out decoded)&&decoded.Token==1&&decoded.Index==0,"client: wire token1 index0");
        Check(Math.Abs(decoded.Position.X-5001)<0.01&&Math.Abs(decoded.Position.Z-(-2997))<0.01,"client: global position serialized");
        TldMp.RabbitCombat.ClientShot(cw,1,new Vector3(1,2,3),new Vector3(0,0,1),20);
        Check(guestNode.sent.Count==1,"client: index>shotCount rejects");
        cw.shotCount=2;
        Time.frameCount=21;
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),new Vector3(0,0,1),20);
        TldMp.RabbitCombat.ClientShot(cw,1,new Vector3(1,2,3),new Vector3(0,0,1),20);
        Check(guestNode.sent.Count==3,"client: burst pellet reuses token");
        Check(TldMp.RabbitProtocol.TryReadCommand(guestNode.sent[2],out decoded)&&decoded.Token==2&&decoded.Index==1,"client: pellet token==burst");
        guestNode.sendResult=false;
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),new Vector3(0,0,1),20);
        TldMp.RabbitCombat.ClientShot(cw,1,new Vector3(1,2,3),new Vector3(0,0,1),20);
        Check(guestNode.sent.Count==3,"client: failed send0 no pellets");
        guestNode.sendResult=true;
        TldMp.RabbitCombat.ClientShot(cw,1,new Vector3(1,2,3),new Vector3(0,0,1),20);
        Check(guestNode.sent.Count==3,"client: no burst no pellet emit");
        var cm=new GameObject();
        var cmt=cm.Add(new tosaveitemscript{id=96,idInSave=61});
        var cmP=cm.Add(new pickupable{tosave=cmt});
        cmt.P=cmP;
        var cmw=cm.Add(new meleeweaponscript{p=cmP});
        cmP.melee=cmw;
        cmw.Trigger=new GameObject().transform;
        cmw.T=new GameObject().transform;
        f.d.items[61]=cmt;
        f.p.inHandP=cmP;
        Time.frameCount=22;
        TldMp.RabbitCombat.ClientMelee(cmw,new Vector3(1,2,3),new Vector3(1,1,1),Quaternion.identity,21);
        Check(guestNode.sent.Count==4,"client: melee emits");
        Check(TldMp.RabbitProtocol.TryReadCommand(guestNode.sent[3],out decoded)&&decoded.Token==4&&decoded.Kind==TldMp.RabbitCommandKind.Melee,"client: shared nonce shot-melee");
        Check(decoded.Extents.X==1f&&decoded.Rotation.W==1f,"client: extents+rotation encoded");
        TldMp.RabbitCombat.ClientMelee(cmw,new Vector3(1,2,3),new Vector3(1,1,1),Quaternion.identity,21);
        Check(guestNode.sent.Count==4,"client: same frame melee dedup");
        Time.frameCount=23;
        TldMp.RabbitCombat.ClientMelee(cmw,new Vector3(1,2,3),new Vector3(1,1,1),Quaternion.identity,21);
        Check(guestNode.sent.Count==5&&TldMp.RabbitProtocol.TryReadCommand(guestNode.sent[4],out decoded)&&decoded.Token==5,"client: fresh frame new nonce");
        TldMp.RabbitCombat.ClientShot(cw,1,new Vector3(1,2,3),new Vector3(0,0,1),21);
        Check(guestNode.sent.Count==5,"client: stale shot pellet after melee no emit");
        f.p.inHandP=cP;
        Time.frameCount=24;
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),new Vector3(0,0,1),21);
        Check(guestNode.sent.Count==6&&TldMp.RabbitProtocol.TryReadCommand(guestNode.sent[5],out decoded)&&decoded.Token==6,"client: shared nonce melee-shot");
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),Vector3.zero,21);
        Check(guestNode.sent.Count==6&&TldMp.RabbitCombat.Healthy,"client: zero dir quietly rejects");
        TldMp.RabbitCombat.ClientMelee(cmw,new Vector3(1,2,3),new Vector3(-1,1,1),Quaternion.identity,21);
        Check(guestNode.sent.Count==6&&TldMp.RabbitCombat.Healthy,"client: negative extents quietly rejects");
        TldMp.RabbitCombat.ClientMelee(cmw,new Vector3(1,2,3),new Vector3(1,1,1),new Quaternion(0,0,0,0),21);
        Check(guestNode.sent.Count==6&&TldMp.RabbitCombat.Healthy,"client: zero quat quietly rejects");
        TldMp.RabbitCombat.Bind(true,false,f.p,f.d,guestNode);
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),new Vector3(0,0,1),21);
        Check(guestNode.sent.Count==6&&TldMp.RabbitCombat.Healthy,"client: world false no emit");
        TldMp.RabbitCombat.Bind(true,true,f.p,f.d,guestNode);
        var staleP=new fpscontroller();
        mainscript.s.player=staleP;
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),new Vector3(0,0,1),21);
        Check(guestNode.sent.Count==6,"client: stale player binding no emit");
        mainscript.s.player=f.p;
        f.p.inHandP=null;
        cw.S.position=new Vector3(0,0,100);
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),new Vector3(0,0,1),21);
        Check(guestNode.sent.Count==6,"client: unheld far worldgun no emit");
        f.p.loadedmove=false;
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),new Vector3(0,0,1),21);
        Check(guestNode.sent.Count==6,"client: unloaded no emit");
        f.p.loadedmove=true;
        guestNode.Ready=false;
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),new Vector3(0,0,1),21);
        Check(guestNode.sent.Count==6,"client: not-ready no emit");
        guestNode.Ready=true;
        TldMp.RabbitCombat.Bind(true,true,f.p,f.d,f.host);
        TldMp.RabbitCombat.ClientShot(cw,0,new Vector3(1,2,3),new Vector3(0,0,1),21);
        Check(f.host.sent.Count==0,"client: host never emits");
    }
    static void GroupPrune()
    {
        var f=new F();
        weaponscript w2;pickupable P2;
        var tool2=ToolItem(f.d,41,out w2,out P2);
        tool2.mppickedup=true;tool2.transform.parent=f.guest.mp.handParent;
        w2.minShootTime=1000f;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(41,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"prune: tool2 burst");
        var tool2Burst=f.tok;
        f.d.items.Remove(41);
        TldMp.RabbitCombat.Bind(true,true,f.p,f.d,f.host);
        weaponscript w3;pickupable P3;
        var tool3=ToolItem(f.d,41,out w3,out P3);
        tool3.mppickedup=true;tool3.transform.parent=f.guest.mp.handParent;
        w3.minShootTime=0;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(41,tool2Burst,1,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==1,"prune: removed tool old pellet rejected");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(41,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==2,"prune: same-ID replaced tool fresh cooldown");
        f.host.Peers.Remove(20);
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==2,"prune: absent peer no throw");
        f.host.Peers[20]=new TldMp.StablePeer{Session=200,Ready=true};
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now-100);
        Check(f.nyul.breakable.shootCalls==2,"prune: clock rollback replay rejected");
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(f.nyul.breakable.shootCalls==3,"prune: post-rollback fresh token accepts");
    }
    public static int Main()
    {
        GroupEnergy();
        GroupFilters();
        GroupSegment();
        GroupBurst();
        GroupOwnership();
        GroupNpc();
        GroupGates();
        GroupMelee();
        GroupClient();
        GroupPrune();
        var f=new F();
        var count=GameObject.count;
        f.Advance();f.Hit();
        TldMp.RabbitCombat.Receive(20,f.Shot(40,0,Vector3.zero,new Vector3(0,0,1)),f.now);
        Check(GameObject.count==count,"no object/collider/rigidbody creation");
        Console.WriteLine("PASS "+checks+" rabbit combat checks");
        return 0;
    }
}
