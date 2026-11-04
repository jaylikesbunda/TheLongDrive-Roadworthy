using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        static bool Dead(Object o){return ReferenceEquals(o,null)||o.destroyed;}
        public static bool operator ==(Object a,Object b){bool an=Dead(a),bn=Dead(b);if(an||bn)return an&&bn;return ReferenceEquals(a,b);}
        public static bool operator !=(Object a,Object b){return !(a==b);}
        public static void Destroy(Object o){var g=o as GameObject;var c=o as Component;if(g!=null)g.SetActive(false);else if(c!=null&&c.gameObject!=null)c.gameObject.SetActive(false);if(o!=null)o.destroyed=true;}
    }
    public class Coroutine{}
    public class Component:Object
    {
        internal GameObject go;
        public GameObject gameObject{get{return go;}set{go=value;}}
        public Transform transform{get{return gameObject==null?null:gameObject.transform;}}
        public T GetComponent<T>(){return gameObject==null?default(T):gameObject.GetComponent<T>();}
        public T GetComponentInParent<T>(){if(gameObject==null)return default(T);var c=gameObject.GetComponent<T>();if(c!=null)return c;var p=transform.parent;return p==null?default(T):p.GetComponentInParent<T>();}
        public T[] GetComponents<T>(){return gameObject==null?new T[0]:gameObject.GetComponents<T>();}
    }
    public class Behaviour:Component
    {
        bool on=true;
        public bool enabled{get{return on;}set{bool was=on;on=value;if(was&&!on&&gameObject!=null)gameObject.FireDisable(this);}}
        public bool isActiveAndEnabled{get{return on&&gameObject!=null&&gameObject.activeSelf;}}
    }
    public class MonoBehaviour:Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine){return RabbitFixture.Start(this,routine);}
        public void StopAllCoroutines(){RabbitFixture.StopAll(this);}
    }
    public class GameObject:Object
    {
        internal bool activeSelf=true;
        Transform t;
        public Transform transform{get{return t;}}
        internal readonly List<Component> components=new List<Component>();
        public GameObject(){t=new Transform();t.gameObject=this;components.Add(t);}
        public T GetComponent<T>(){foreach(var c in components)if(c is T)return (T)(object)c;return default(T);}
        public T[] GetComponentsInChildren<T>(bool inactive){var r=new List<T>();CollectT(transform,r);return r.ToArray();}
        internal static void CollectT2<T>(Transform t,List<T> r){CollectT(t,r);}
        static void CollectT<T>(Transform t,List<T> r){foreach(var c in t.gameObject.GetComponents<T>())r.Add(c);foreach(var ch in t.children)CollectT(ch,r);}
        internal void GetComponentsInChildrenHelper<T>(List<T> r){CollectT(transform,r);}
        public T[] GetComponents<T>(){var r=new List<T>();foreach(var c in components)if(c is T)r.Add((T)(object)c);return r.ToArray();}
        public T AddComponent<T>() where T:Component,new(){var c=new T();c.gameObject=this;components.Add(c);return c;}
        internal void FireDisable(Component c){var s=c as TldMp.RabbitFleeState;if(s!=null)s.OnDisable();}
        public void SetActive(bool v){if(activeSelf==v)return;activeSelf=v;if(!v)foreach(var c in components.ToArray()){RabbitFixture.StopAll(c);FireDisable(c);}}
    }
    public class Transform:Component{public List<Transform> children=new List<Transform>();public Transform parent;public Vector3 position{get;set;}public T[] GetComponentsInChildren<T>(bool inactive){var r=new List<T>();UnityEngine.GameObject.CollectT2(this,r);return r.ToArray();}public Vector3 forward{get;set;}public Vector3 up{get;set;}public Vector3 right{get;set;}public Transform(){forward=new Vector3(0,0,1);up=new Vector3(0,1,0);right=new Vector3(1,0,0);}}
    public class Collider:Component{public bool enabled=true;}
    public class Rigidbody:Component{public Vector3 velocity{get;set;}public Vector3 angularVelocity{get;set;}public bool isKinematic,useGravity=true,detectCollisions=true;}
    public class Animator:Behaviour{}
    public class AudioSource:MonoBehaviour{}
    public struct RaycastHit{public Collider c;public Collider collider{get{return c;}}}
    public enum QueryTriggerInteraction{UseGlobal,Ignore,Collide}
    public struct LayerMask{public int value;public static implicit operator int(LayerMask m){return m.value;}}
    public static class Physics
    {
        public static Func<Collider[]> overlap;
        public static Collider rayTarget;
        public static Collider[] OverlapSphere(Vector3 pos,float radius,int mask,QueryTriggerInteraction q){return overlap==null?new Collider[0]:overlap();}
        public static bool Raycast(Vector3 o,Vector3 d,ref RaycastHit hit,float dist,int mask,QueryTriggerInteraction q){hit.c=rayTarget;return rayTarget!=null;}
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator *(Vector3 v,float n){return new Vector3(v.x*n,v.y*n,v.z*n);}
        public float sqrMagnitude{get{return x*x+y*y+z*z;}}
        public float magnitude{get{return (float)Math.Sqrt(sqrMagnitude);}}
        public Vector3 normalized{get{float m=magnitude;return m==0?new Vector3():new Vector3(x/m,y/m,z/m);}}
        public static Vector3 zero{get{return new Vector3();}}
        public static Vector3 up{get{return new Vector3(0,1,0);}}
        public static Vector3 down{get{return new Vector3(0,-1,0);}}
        public static Vector3 left{get{return new Vector3(-1,0,0);}}
        public static Vector3 right{get{return new Vector3(1,0,0);}}
        public static Vector3 forward{get{return new Vector3(0,0,1);}}
        public static Vector3 back{get{return new Vector3(0,0,-1);}}
        public static bool operator ==(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        public static bool operator !=(Vector3 a,Vector3 b){return a.x!=b.x||a.y!=b.y||a.z!=b.z;}
        public override bool Equals(object o){return o is Vector3 && (Vector3)o==this;}
        public override int GetHashCode(){return (int)(x*31+y*17+z);}
        public static float Angle(Vector3 a,Vector3 b){float d=a.magnitude*b.magnitude;if(d==0)return 0;return (float)(Math.Acos(Math.Max(-1,Math.Min(1,(a.x*b.x+a.y*b.y+a.z*b.z)/d)))*180/Math.PI);}
        public static float SignedAngle(Vector3 a,Vector3 b,Vector3 axis){var cross=new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);float s=(cross.x*axis.x+cross.y*axis.y+cross.z*axis.z)>=0?1:-1;return Angle(a,b)*s;}
        public static Vector3 ProjectOnPlane(Vector3 v,Vector3 n){float d=n.x*n.x+n.y*n.y+n.z*n.z;if(d==0)return v;float k=(v.x*n.x+v.y*n.y+v.z*n.z)/d;return new Vector3(v.x-n.x*k,v.y-n.y*k,v.z-n.z*k);}
        public override string ToString(){return "("+x+","+y+","+z+")";}
    }
    public static class Mathf
    {
        public static float Clamp01(float v){return Math.Max(0,Math.Min(1,v));}
        public static float InverseLerp(float a,float b,float v){return a==b?0f:Clamp01((v-a)/(b-a));}
        public static float Lerp(float a,float b,float t){return a+(b-a)*Clamp01(t);}
        public static float Abs(float v){return Math.Abs(v);}
    }
    public static class Time{static float t;public static float time{get{return t;}set{t=value;}}}
    public static class Random{public static float Range(float a,float b){return a;}public static int Range(int a,int b){return a;}}
}

public enum stimulType{nothing,food,water,threat,light,command}
public class stimul
{
    public Vector3 pos;public stimulType tipus;public float value;
    public stimul(Vector3 p,stimulType t,float v){pos=p;tipus=t;value=v;}
    public int IS(stimul[] prefs){if(prefs==null)return -1;for(int i=0;i<prefs.Length;i++)if(prefs[i]!=null&&prefs[i].tipus==tipus)return i;return -1;}
}
public class stimulscript:MonoBehaviour{public bool visible,sound,smell,DontStartCheckingAudioSources;public List<stimul> stimuls=new List<stimul>();public AudioSource[] audioSources;public float maxSoundValue,max;}
public class sensoryscript:MonoBehaviour
{
    public float scanInterval,fovX=360,fovY=360,fovZ=1e9f,hearDistance=40f*40f,hearModifier=2f,minHearValue=.15f,minHearDistance=.1f*.1f,dist,dist2,radius=10,debugDuration;
    public bool alive=true,hasEyes,hasEars=true,drawdebugray;
    public Transform eyes;
    public GameObject[] eyeObjects;
    public LayerMask stimulMask,visionMask;
    public visionRay[] visionRays=new visionRay[0];
    public Collider[] colliders;
    public RaycastHit hit;
    public List<stimul> stimuls=new List<stimul>();
    public class visionRay{public bool enabled,sensed,inverted,symmetry,BOOL;public float value,dist,rayLength,maxValue,spread;public Vector3 rayDir;public LayerMask rayMask;}
    void TestRay(visionRay r,Transform t){}
    public void Scan(){}
}
public class nyulscript:MonoBehaviour
{
    public aiscript AI;public ragdollactivatescript ragdoll;public attackTriggerScript attackTriggerScript;public playerdeathtriggerscript playerDeathTrigger;
    public void Die(bool silent){}
    public void SeeTarget(float a,float b){}
    public void SetAnims(float v){}
}
public class movecreaturescript:MonoBehaviour
{
    public Rigidbody RB;
    public Vector3 moveDir;public float moveRot;public bool modifier;
    public void Jump(){}
}
public class ragdollactivatescript:MonoBehaviour{public static int ragdolCalls;public Rigidbody RB;public Rigidbody[] RBs=new Rigidbody[0];public Collider[] ragdollColliders=new Collider[0],animColliders=new Collider[0];public Transform[] unparent;public Transform animColliderTransform;public Behaviour anim;public void SetRagdol(bool v){ragdolCalls++;}public void SetMass(float m){}}
public class seatscript:MonoBehaviour{}
public class fpscontroller:MonoBehaviour{public GameObject currentTarget;}
public class mainscript:MonoBehaviour
{
    public static mainscript s=new mainscript();
    public bool AIDisable;public int nyulAlive;public fpscontroller player=new fpscontroller();
}
public class aiscript:MonoBehaviour
{
    public Animator anim;public bool alive=true;public float accelerationDeath;
    public Vector3 lastVelocity,mov;public float rot,sensRot;
    public bool moved,rotated,goToTarget,flee,preCalcFlee,avoidObstacles,roam,attack,jump,enableInvert,goToPlayerTarget;
    public float jumpIfSpeed,jumpSensDist;public LayerMask jumpLayerMask;
    public movecreaturescript move;public nyulscript nyul;public sensoryscript senses;
    public ragdollactivatescript ragdoll;public seatscript seat;
    public GameObject targetPrefab;
    public float targetPreCalcDist=5,targetDistGoal,targetAngleGoal=30,targetAngleMove=5,walkDist=3,runDist=8,attackSpeed,fleeDist=30,minFleeSpeed,fleeTime=10f,walkSpeedModifier=1,moveTowardsTargetModifier=1,moveForwardModifier=1;
    public float roamMinDistX,roamMaxDistX,roamMinDistZ,roamMaxDistZ,roamMinTime=10,roamMaxTime=20,lastRoam,nextRoam=1e9f;
    public stimul[] stimulPreferences=new stimul[0];
    public float invertFlightIfAbove,moveSpeed,distFromTarget,signedAngleFromTarget,angleFromTarget,flightFight;
    public Vector3 interestPos,targetDir,tempV0,tempV1,tempV2;
    public bool fleeing,startedDie;
    public stimul overRideStimul;
    public float currentStimulValue,positiveStimulValue,negativeStimulValue;
    public Vector3 positiveStimulPos,negativeStimulPos;
    public int tempInt;public float lastFleeSignalTime;
    bool JumpRay(){return false;}
    public void Update(){}
    public void ProcessStimul(stimul s){}
    public IEnumerator stopFleeing(){return null;}
    public void StartDie(){}
    public void Die(bool silent){}
}

public class playerdeathtriggerscript:MonoBehaviour{}
public class attackTriggerScript:MonoBehaviour{public Collider selfCol;}
public class ragdollchildpos:MonoBehaviour{public List<Transform> childs=new List<Transform>();}
public class tosaveitemscript:MonoBehaviour{public nyulscript nyul;public ragdollchildpos ragdollPos;}
public class pickupable:MonoBehaviour{}
public class jointHelpScript:MonoBehaviour{public Rigidbody RB;}
public class SyncSession{public ulong Host;public static SyncSession find;public static bool Find(ulong id,out tosaveitemscript item){item=null;return false;}}

namespace TldMp
{
    public static partial class StableGameBridge
    {
        internal static bool Connected=true;internal static bool loopbackMode;internal static SyncSession node=new SyncSession();
        public static bool Find(ulong id,out tosaveitemscript item){item=null;return SyncSession.Find(id,out item);}
        public static float RabbitHearingFalloff(float from,float to,float squaredDistance,sensoryscript senses){return RabbitAi.HearingFalloff(from,to,squaredDistance,senses);}
        public static Coroutine RabbitStartFlee(MonoBehaviour owner,IEnumerator routine){return RabbitAi.StartFlee(owner,routine);}
        // Motion is covered by RabbitLocomotionTests; this extracted replay
        // isolates native sensing, attack timing and flee-coroutine behavior.
        public static void RabbitMotionObserve(aiscript ai){}
        // Remote-player perception has its own canonical-actor fixture.
        public static void RabbitScanComplete(sensoryscript senses){}
        public static void RabbitMotionAnimate(nyulscript rabbit,float speed){rabbit.SetAnims(speed);}
    }
    public static class Loopback{public static bool IsHost=true;}
    public class syncScript{internal static bool IsHost(){return StableGameBridge.host;}internal static bool host=true;}
    public static partial class StableGameBridge{internal static bool host=true;}
}

public static class RabbitFixture
{
    public sealed class Entry
    {
        public MonoBehaviour owner;public IEnumerator routine;public Coroutine handle=new Coroutine();public bool done;
    }
    public static readonly List<Entry> entries=new List<Entry>();
    public static Coroutine Start(MonoBehaviour owner,IEnumerator routine)
    {
        var e=new Entry{owner=owner,routine=routine};entries.Add(e);
        if(!e.routine.MoveNext())e.done=true;
        return e.handle;
    }
    public static void StopAll(Component owner){entries.RemoveAll(e=>e.owner==owner);}
    public static void StepAll()
    {
        foreach(var e in entries.ToArray()){if(e.done||e.owner==null||!e.owner.isActiveAndEnabled)continue;if(!e.routine.MoveNext())e.done=true;}
        entries.RemoveAll(e=>e.done);
    }
    public static int Count(MonoBehaviour owner){int n=0;foreach(var e in entries)if(e.owner==owner&&!e.done)n++;return n;}
    public static int Total(){int n=0;foreach(var e in entries)if(!e.done)n++;return n;}
}

public sealed class MarkerRoutine:System.Collections.IEnumerator,System.IDisposable
{
    public bool disposed;public object Current{get{return null;}}
    public bool MoveNext(){return true;}public void Reset(){}public void Dispose(){disposed=true;}
}

public static class RabbitAiReplayTests
{
    static int checks;static bool candidate;
    static void Check(bool v,string label){checks++;if(!v)throw new Exception("check "+checks+" failed: "+label);}
    static void Frame(aiscript ai){ai.Update();RabbitFixture.StepAll();}
    static aiscript MakeRabbit(out sensoryscript senses)
    {
        var go=new GameObject();var ai=go.AddComponent<aiscript>();
        senses=go.AddComponent<sensoryscript>();ai.senses=senses;
        ai.nyul=go.AddComponent<nyulscript>();ai.nyul.AI=ai;
        ai.move=go.AddComponent<movecreaturescript>();ai.move.RB=go.AddComponent<Rigidbody>();
        ai.ragdoll=go.AddComponent<ragdollactivatescript>();
        ai.flee=true;ai.fleeTime=10f;
        ai.stimulPreferences=new[]{new stimul(Vector3.zero,stimulType.threat,-1f)};
        mainscript.s.AIDisable=false;
        return ai;
    }
    static GameObject SoundSource(float metres,float value)
    {
        var src=new GameObject();src.transform.position=new Vector3(0,0,metres);
        var col=src.AddComponent<Collider>();
        var st=src.AddComponent<stimulscript>();st.sound=true;
        st.stimuls.Add(new stimul(src.transform.position,stimulType.threat,value));
        Physics.overlap=()=>new[]{col};Physics.rayTarget=col;
        return src;
    }
    static float Hear(sensoryscript s,float metres,float value)
    {
        s.stimuls.Clear();SoundSource(metres,value);s.Scan();
        return s.stimuls.Count==0?float.NaN:s.stimuls[s.stimuls.Count-1].value;
    }
    public static int Main(string[] args)
    {
        try
        {
            candidate=args.Length>0&&args[0]=="candidate";
            sensoryscript s;float v;
            for(int variant=0;variant<2;variant++)
            {
                var go=new GameObject();s=go.AddComponent<sensoryscript>();
                if(variant==1)go.AddComponent<nyulscript>();
                bool rabbit=variant==1;
                Check(float.IsNaN(Hear(s,0f,1f)),"0m stimulus must stay below minHearDistance");
                Check(float.IsNaN(Hear(s,.1f,1f)),"0.1m squared boundary must stay excluded");
                Check(!float.IsNaN(Hear(s,.1001f,1f)),"just above 0.1m must be admitted");
                Check(float.IsNaN(Hear(s,40f,1f)),"40m boundary must stay excluded");
                Check(float.IsNaN(Hear(s,41f,1f)),"41m stimulus must stay out of range");
                float near=Hear(s,1f,1f),mid=Hear(s,10f,1f),midfar=Hear(s,20f,1f),far=Hear(s,39f,1f);
                Check(!float.IsNaN(near)&&!float.IsNaN(mid)&&!float.IsNaN(midfar)&&!float.IsNaN(far),"interior distances must be admitted");
                if(!rabbit||!candidate){Check(far>midfar&&midfar>mid&&mid>near,"native far>near order at variant="+variant);}
                else{Check(near>mid&&mid>midfar&&midfar>far,"rabbit near>far order");Check(Math.Abs(near-2f*(1600f-1f)/1600f)<1e-4,"rabbit near weight matches reversed falloff");}
            }
            var g2=new GameObject();s=g2.AddComponent<sensoryscript>();g2.AddComponent<nyulscript>();
            Check(float.IsNaN(Hear(s,10f,.15f)),"value equal to minHearValue is skipped");
            Check(float.IsNaN(Hear(s,10f,.14f)),"value below minHearValue is skipped");
            Check(!float.IsNaN(Hear(s,10f,.16f)),"value above minHearValue is admitted");
            var silentStim=SoundSource(10f,1f).GetComponent<stimulscript>();silentStim.sound=false;s.stimuls.Clear();s.Scan();
            Check(s.stimuls.Count==0,"silent stimulscript must add nothing");
            s.hasEars=false;s.stimuls.Clear();SoundSource(10f,1f);s.Scan();
            Check(s.stimuls.Count==0,"hasEars false must skip the hearing pass");
            s.hasEars=true;s.hasEyes=true;s.eyes=new GameObject().transform;
            var vs=SoundSource(5f,1f);var vst=vs.GetComponent<stimulscript>();vst.visible=true;vst.sound=false;
            Physics.rayTarget=vs.GetComponent<Collider>();s.stimuls.Clear();s.Scan();
            Check(s.stimuls.Count==1&&s.stimuls[0].value==1f,"visible stimulus keeps its raw value");
            var ai=MakeRabbit(out s);ai.ProcessStimul(new stimul(new Vector3(1,0,2),stimulType.threat,1f));
            Check(ai.negativeStimulValue<0&&ai.positiveStimulValue==0,"feared stimul records negative");
            Check(ai.negativeStimulPos==new Vector3(1,0,2),"feared stimul position recorded");
            Check(ai.flightFight<0,"feared stimul lowers flightFight");
            ai.ProcessStimul(new stimul(Vector3.zero,stimulType.nothing,9f));
            Check(ai.negativeStimulValue==-1f&&ai.flightFight==-1f,"unpreferred stimul changes nothing");
            ai=MakeRabbit(out s);s.stimuls.Add(new stimul(new Vector3(9,0,0),stimulType.threat,1f));
            for(int f=0;f<20;f++){Time.time=f;Frame(ai);}
            var state=ai.GetComponent<TldMp.RabbitFleeState>();
            if(candidate){Check(state!=null&&RabbitFixture.Count(state)==1,"one flee wrapper per episode");Check(RabbitFixture.Count(ai)==0,"no native iterator on the ai component");}
            else{Check(RabbitFixture.Count(ai)==20,"baseline repeats native StartCoroutine each threat frame");}
            Check(ai.fleeing,"fleeing during sustained threat");
            for(int f=0;f<30;f++){Time.time=20+f*2;s.stimuls.Clear();s.stimuls.Add(new stimul(new Vector3(9,0,0),stimulType.threat,1f));Frame(ai);}
            Check(ai.fleeing,"sustained threat keeps refreshing the timeout");
            if(candidate)Check(RabbitFixture.Total()==1,"still exactly one wrapper after refresh");
            s.stimuls.Clear();float last=ai.lastFleeSignalTime;
            Time.time=last+10f;Frame(ai);Check(ai.fleeing,"exact fleeTime boundary still flees");
            Time.time=last+10.5f;Frame(ai);Check(!ai.fleeing,"past fleeTime expires");
            Check(ai.interestPos==ai.transform.position,"expiry resets interestPos to own position");
            s.stimuls.Add(new stimul(new Vector3(8,0,0),stimulType.threat,1f));Frame(ai);
            Check(ai.fleeing&&(!candidate||RabbitFixture.Total()==1),"new threat after expiry starts exactly one episode");
            ai.Die(false);Check(!ai.alive,"native Die marks dead");
            s.stimuls.Clear();s.stimuls.Add(new stimul(new Vector3(8,0,0),stimulType.threat,1f));
            int before=RabbitFixture.Total();Frame(ai);
            Check(RabbitFixture.Total()<=before,"dead ai starts no new routine");
            for(int f=0;f<3;f++)RabbitFixture.StepAll();
            if(candidate)Check(!ai.fleeing,"wrapper clears fleeing after death");
            ai=MakeRabbit(out s);s.stimuls.Add(new stimul(new Vector3(7,0,0),stimulType.threat,1f));Frame(ai);
            Check(ai.fleeing,"second rabbit flees");
            ai.enabled=false;RabbitFixture.StepAll();
            if(candidate)Check(!ai.fleeing,"disabled component clears fleeing");
            Check(!candidate||RabbitFixture.Total()==0,"disabled component drains the wrapper");
            ai.enabled=true;Frame(ai);
            Check(ai.fleeing&&(!candidate||RabbitFixture.Total()==1),"re-enabled component reacquires");
            ai.gameObject.SetActive(false);
            if(candidate)Check(!ai.fleeing,"deactivated object clears fleeing");
            if(candidate)Check(RabbitFixture.Total()==0,"deactivate leaves no scheduler entries");
            ai.gameObject.SetActive(true);Frame(ai);
            Check(ai.fleeing&&(!candidate||RabbitFixture.Total()==1),"reactivated object reacquires");
            UnityEngine.Object.Destroy(ai.gameObject);
            if(candidate)Check(RabbitFixture.Total()==0,"destroy leaks no scheduler entries");
            var evil=MakeRabbit(out s);evil.enableInvert=true;evil.invertFlightIfAbove=-11f;
            s.stimuls.Add(new stimul(new Vector3(4,0,0),stimulType.threat,2f));Time.time=3000;Frame(evil);
            Check(evil.flightFight==2f&&evil.positiveStimulPos==new Vector3(4,0,0),"GetEvil inversion turns a weak threat positive");
            Check(!evil.fleeing,"inverted weak threat does not flee");
            var evil2=MakeRabbit(out s);evil2.enableInvert=true;evil2.invertFlightIfAbove=-11f;
            s.stimuls.Add(new stimul(new Vector3(4,0,0),stimulType.threat,12f));Frame(evil2);
            Check(evil2.flightFight==-12f&&evil2.fleeing,"strong threat stays negative past the inversion threshold");
            var evil3=MakeRabbit(out s);evil3.enableInvert=true;evil3.invertFlightIfAbove=-11f;
            s.stimuls.Add(new stimul(new Vector3(4,0,0),stimulType.threat,11f));Frame(evil3);
            Check(evil3.flightFight==-11f&&evil3.fleeing,"inversion boundary remains fleeing");
            UnityEngine.Object.Destroy(evil2.gameObject);UnityEngine.Object.Destroy(evil3.gameObject);
            ai=MakeRabbit(out s);s.stimuls.Add(new stimul(new Vector3(7,0,0),stimulType.threat,1f));Frame(ai);
            Check(ai.fleeing,"deferred-death rabbit flees first");
            ai.StartDie();Check(ai.startedDie&&ai.alive,"StartDie defers death to Update");
            Frame(ai);Check(!ai.alive,"Update completes the deferred death");
            for(int f=0;f<3;f++)RabbitFixture.StepAll();
            if(candidate){Check(RabbitFixture.Total()==0,"deferred death drains the wrapper");Check(!ai.fleeing,"deferred death clears fleeing");}
            var dead=MakeRabbit(out s);dead.alive=false;dead.fleeing=true;
            var marker=new MarkerRoutine();
            Check(TldMp.RabbitAi.StartFlee(dead,marker)==null,"dead StartFlee returns null");
            Check(dead.GetComponent<TldMp.RabbitFleeState>()==null,"dead StartFlee adds no state component");
            Check(marker.disposed,"dead StartFlee disposes the routine");
            Check(!dead.fleeing,"dead StartFlee clears fleeing");
            var off=MakeRabbit(out s);off.enabled=false;off.fleeing=true;marker=new MarkerRoutine();
            Check(TldMp.RabbitAi.StartFlee(off,marker)==null,"disabled StartFlee returns null");
            Check(off.GetComponent<TldMp.RabbitFleeState>()==null,"disabled StartFlee adds no state component");
            Check(marker.disposed,"disabled StartFlee disposes the routine");
            Check(!off.fleeing,"disabled StartFlee clears fleeing");
            ai=MakeRabbit(out s);s.stimuls.Add(new stimul(new Vector3(7,0,0),stimulType.threat,1f));
            Time.time=1000;Frame(ai);Check(ai.fleeing,"third rabbit flees");
            mainscript.s.AIDisable=true;s.stimuls.Clear();
            Time.time=1011;Frame(ai);
            Check(!ai.fleeing,"AIDisable still lets the native timeout expire");
            mainscript.s.AIDisable=false;
            sensoryscript s2;var a1=MakeRabbit(out s);var a2=MakeRabbit(out s2);
            s.stimuls.Add(new stimul(new Vector3(5,0,0),stimulType.threat,1f));
            s2.stimuls.Add(new stimul(new Vector3(5,0,0),stimulType.threat,1f));
            Frame(a1);Frame(a2);
            Check(a1.fleeing&&a2.fleeing,"two rabbits flee independently");
            if(candidate){Check(RabbitFixture.Total()==2,"two wrappers for two rabbits");}
            var plain=new GameObject().AddComponent<aiscript>();
            plain.flee=true;plain.fleeTime=10f;plain.senses=new GameObject().AddComponent<sensoryscript>();
            plain.stimulPreferences=new[]{new stimul(Vector3.zero,stimulType.threat,-1f)};
            plain.senses.stimuls.Add(new stimul(new Vector3(5,0,0),stimulType.threat,1f));
            int entriesBefore=RabbitFixture.Count(plain);
            for(int f=0;f<5;f++){Time.time=2000+f;Frame(plain);}
            Check(RabbitFixture.Count(plain)==entriesBefore+5,"nonrabbit retains repeated native starts");
            if(candidate)
            {
                TldMp.StableGameBridge.Connected=true;TldMp.StableGameBridge.loopbackMode=false;TldMp.StableGameBridge.host=false;TldMp.StableGameBridge.node=null;
                var g=MakeRabbit(out s);var gnyul=g.nyul;gnyul.ragdoll=g.ragdoll;g.gameObject.AddComponent<tosaveitemscript>().nyul=gnyul;
                Check(TldMp.StableGameBridge.IsRabbitGuest,"native guest role resolves before node creation");
                s.stimuls.Add(new stimul(new Vector3(3,0,0),stimulType.threat,1f));
                Frame(g);
                Check(!g.fleeing,"guest Update does not start flee");
                Check(!g.attack,"guest Update does not start attack");
                s.Scan();
                Check(s.stimuls.Count==0,"guest Scan clears stimuli without sensing");
                ragdollactivatescript.ragdolCalls=0;
                g.StartDie();
                Check(!g.senses.alive,"native StartDie marks the guest senses dead");
                Frame(g);
                Check(!g.alive,"guest deferred death completes once");
                Check(ragdollactivatescript.ragdolCalls==1,"native Die boundary ragdoll once");
                Frame(g);
                Check(ragdollactivatescript.ragdolCalls==1,"second Update does not re-die");
                TldMp.StableGameBridge.Connected=false;
                var h=MakeRabbit(out s);s.stimuls.Add(new stimul(new Vector3(3,0,0),stimulType.threat,1f));
                Frame(h);
                Check(h.fleeing,"disconnected native behavior unaffected");
                TldMp.StableGameBridge.Connected=true;TldMp.StableGameBridge.host=true;
                var hh=MakeRabbit(out s);s.stimuls.Add(new stimul(new Vector3(3,0,0),stimulType.threat,1f));
                Frame(hh);
                Check(hh.fleeing,"host rabbit runs native AI");
                TldMp.StableGameBridge.host=false;
            }
            Console.WriteLine("PASS "+checks+" rabbit ai production replay checks ("+(candidate?"candidate":"baseline")+")");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
