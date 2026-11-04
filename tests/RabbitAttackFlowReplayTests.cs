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
    public class Animator:Behaviour{public int attacks,roars;public void SetTrigger(string s){if(s=="Attack")attacks++;if(s=="Roar")roars++;}} public class AudioClip:Object{}
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
    public static class Time{static float t;public static float time{get{return t;}set{t=value;}}public static float deltaTime{get{return .02f;}}}
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
 public aiscript AI;public ragdollactivatescript ragdoll;public playerdeathtriggerscript attackTriggerScript;public GameObject playerDeathTrigger;
 public sensoryscript senses;public movecreaturescript move;public Animator anim;
 public bool roaring,attacking,idleSound;public float lastRoarTime,roarDuration,lastAttackTime,attackDuration,lastSeeTarget,targetForgetTime,attackAngle,attackDist,attackCooldown,attackModifier,size,lastIdle,currentIdle,minIdle,maxIdle,idleDistance;
 public AudioClip[] Sroar=new AudioClip[1],Sattack=new AudioClip[1],Sidle=new AudioClip[1];public GameObject GIdleSound;
 public void Die(bool silent){}public void SeeTarget(float a,float b){}public void SetAnims(float v){}public void Attack(){}public void Roar(){}public void Update(){}
}
public class movecreaturescript:MonoBehaviour
{
    public Rigidbody RB;
    public Vector3 moveDir;public float moveRot,modifierValue;public bool modifier;
    public void Jump(){}
}
public class ragdollactivatescript:MonoBehaviour{public static int ragdolCalls;public Rigidbody RB;public Rigidbody[] RBs=new Rigidbody[0];public Collider[] ragdollColliders=new Collider[0],animColliders=new Collider[0];public Transform[] unparent;public Transform animColliderTransform;public Behaviour anim;public void SetRagdol(bool v){ragdolCalls++;}public void SetMass(float m){}}
public class seatscript:MonoBehaviour{}
public class fpscontroller:MonoBehaviour{public GameObject currentTarget;}
public class mainscript:MonoBehaviour
{
    public static mainscript s=new mainscript();public static byte[] AudioPriorities=new byte[5];public static GameObject PlayClipAtPoint(AudioClip c,Vector3 p,float v,byte priority){return new GameObject();}public static GameObject PlayClipAtPoint(AudioClip c,Vector3 p,float v,float pitch,bool spati,byte priority){return new GameObject();}
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

public class playerdeathtriggerscript:MonoBehaviour{public Collider selfCol;public float damageBreakable,damagePlayer;public breakablescript bscript;public survivalscript pscript;public void OnTriggerStay(Collider c){}} public class breakablescript:MonoBehaviour{public float damage;public void TryBreak(float v){damage+=v;}} public class survivalscript:MonoBehaviour{public float damage;public void DamageInstant(float v,Vector3 d,bool show){damage+=v;}}
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
        // This replay isolates native timing with reachable contact assumed.
        // The real reach gate/resolver/motor run together in RabbitCombatReachTests.
        public static bool RabbitAttackGate(nyulscript rabbit){return RabbitNyulGuard(rabbit);}
        // Motion is covered by RabbitLocomotionTests; this extracted replay
        // isolates native sensing, attack timing and flee-coroutine behavior.
        public static void RabbitMotionObserve(aiscript ai){} public static void RabbitPlayerContact(playerdeathtriggerscript t,Collider c){}
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


public static class RabbitAttackFlowReplayTests
{
 static int n;static void Check(bool b,string m){n++;if(!b)throw new Exception(m);}
 static void Scan(sensoryscript senses,GameObject player){Physics.overlap=()=>new[]{player.GetComponent<Collider>()};Physics.rayTarget=player.GetComponent<Collider>();senses.Scan();}
 static GameObject Player(float z){var g=new GameObject();g.transform.position=new Vector3(0,0,z);g.AddComponent<Collider>();var stim=g.AddComponent<stimulscript>();stim.visible=true;stim.stimuls.Add(new stimul(g.transform.position,stimulType.threat,10));return g;}
 public static int Main(){try{
  TldMp.StableGameBridge.Connected=false;Time.time=100;var go=new GameObject();var ai=go.AddComponent<aiscript>();var s=go.AddComponent<sensoryscript>();var n=go.AddComponent<nyulscript>();var m=go.AddComponent<movecreaturescript>();m.RB=go.AddComponent<Rigidbody>();var rag=go.AddComponent<ragdollactivatescript>();var anim=go.AddComponent<Animator>();
  ai.senses=s;ai.nyul=n;ai.move=m;ai.ragdoll=rag;ai.goToTarget=true;ai.attack=true;ai.flee=true;ai.enableInvert=true;ai.invertFlightIfAbove=-11;ai.targetDistGoal=1;ai.targetAngleGoal=2;ai.targetAngleMove=50;ai.stimulPreferences=new[]{new stimul(Vector3.zero,stimulType.threat,-1)};
  n.AI=ai;n.senses=s;n.move=m;n.ragdoll=rag;n.anim=anim;n.roarDuration=1;n.attackAngle=10;n.attackDist=4;n.targetForgetTime=10;n.attackDuration=2;n.attackCooldown=1;n.attackModifier=2;n.size=.8f;n.playerDeathTrigger=new GameObject();n.playerDeathTrigger.SetActive(false);
  s.hasEyes=true;s.eyes=go.transform;s.fovX=90;s.fovY=90;s.fovZ=1600;s.hasEars=false;var player=Player(3);var survival=player.AddComponent<survivalscript>();var trigger=n.playerDeathTrigger.AddComponent<playerdeathtriggerscript>();trigger.damagePlayer=20;
  Scan(s,player);Check(s.stimuls.Count==1,"native vision produces one threat");ai.Update();Check(ai.flightFight==10&&n.roaring&&!ai.goToTarget&&anim.roars==1,"one player triggers inversion and native roar");
  Time.time=101.1f;n.Update();Check(!n.roaring&&ai.goToTarget,"native roar expiry resumes targeting");Scan(s,player);ai.Update();Check(n.attacking&&anim.attacks==1&&n.playerDeathTrigger.activeSelf&&m.modifier,"native SeeTarget attacks and activates the trigger");trigger.OnTriggerStay(player.GetComponent<Collider>());Check(Math.Abs(survival.damage-.4f)<.0001f,"native active trigger applies damage per frame");
  Time.time=103.2f;n.Update();Check(!n.attacking&&!n.playerDeathTrigger.activeSelf&&!m.modifier,"native attack timeout clears the trigger");
  Time.time=104;Scan(s,player);s.stimuls.Add(new stimul(new Vector3(0,0,4),stimulType.threat,10));ai.Update();Check(ai.flightFight==-20&&ai.fleeing&&!n.attacking,"two equal threats cross the native -11 inversion boundary and cause flight");
  Console.WriteLine("PASS "+RabbitAttackFlowReplayTests.n+" real native attack flow checks");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
