using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace UnityEngine {
 public class Object { public static bool operator ==(Object a,Object b){return ReferenceEquals(a,b);} public static bool operator !=(Object a,Object b){return !ReferenceEquals(a,b);} public override bool Equals(object x){return ReferenceEquals(this,x);} public override int GetHashCode(){return base.GetHashCode();} }
 public class Component:Object { public GameObject gameObject; public Transform transform {get{return gameObject.transform;}} public T GetComponent<T>() where T:class{return gameObject.GetComponent<T>();} public T GetComponentInParent<T>() where T:class{return GetComponent<T>();} public T[] GetComponentsInChildren<T>(bool b) where T:class{return gameObject.GetComponents<T>();} }
 public class Behaviour:Component {public bool enabled=true; public bool isActiveAndEnabled{get{return enabled&&gameObject.active;}}}
 public class MonoBehaviour:Behaviour{}
 public class GameObject:Object {public bool active=true;public bool activeInHierarchy{get{return active;}}public string name="";public Transform transform;public List<Component> cs=new List<Component>();public GameObject(){transform=AddComponent<Transform>();}public T AddComponent<T>() where T:Component,new(){T c=new T();c.gameObject=this;cs.Add(c);return c;}public T GetComponent<T>() where T:class{foreach(var c in cs)if(c is T)return c as T;return null;}public T[] GetComponents<T>() where T:class{var l=new List<T>();foreach(var c in cs)if(c is T)l.Add(c as T);return l.ToArray();}}
 public class Transform:Component {public Vector3 position,lossyScale=new Vector3(1,1,1);public Vector3 forward{get{var rb=gameObject.GetComponent<Rigidbody>();float y=rb==null?0:rb.rotation.yaw*(float)Math.PI/180f;return new Vector3((float)Math.Sin(y),0,(float)Math.Cos(y));}} public bool IsChildOf(Transform t){return false;}public Quaternion rotation;public Vector3 TransformDirection(Vector3 v){return v;}public Vector3 TransformPoint(Vector3 v){return v*lossyScale+position;}}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 zero{get{return new Vector3();}} public static Vector3 up{get{return new Vector3(0,1,0);}}public static Vector3 down{get{return new Vector3(0,-1,0);}}public static Vector3 right{get{return new Vector3(1,0,0);}}public static Vector3 forward{get{return new Vector3(0,0,1);}}public float sqrMagnitude{get{return x*x+y*y+z*z;}}public float magnitude{get{return (float)Math.Sqrt(sqrMagnitude);}}public Vector3 normalized{get{float m=magnitude;return m<.00001?zero:this/m;}}public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}public static Vector3 operator*(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}public static Vector3 operator/(Vector3 a,float b){return a*(1/b);}public static Vector3 operator*(Vector3 a,Vector3 b){return new Vector3(a.x*b.x,a.y*b.y,a.z*b.z);}public static float Dot(Vector3 a,Vector3 b){return a.x*b.x+a.y*b.y+a.z*b.z;}public static Vector3 Min(Vector3 a,Vector3 b){return new Vector3(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Min(a.z,b.z));}public static Vector3 Max(Vector3 a,Vector3 b){return new Vector3(Math.Max(a.x,b.x),Math.Max(a.y,b.y),Math.Max(a.z,b.z));}public static Vector3 Cross(Vector3 a,Vector3 b){return new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);}public static float Distance(Vector3 a,Vector3 b){return (a-b).magnitude;}public static float Angle(Vector3 a,Vector3 b){return (float)(Math.Acos(Math.Max(-1,Math.Min(1,Dot(a.normalized,b.normalized))))*180/Math.PI);}public static Vector3 Lerp(Vector3 a,Vector3 b,float t){return a+(b-a)*t;}public static Vector3 RotateTowards(Vector3 a,Vector3 b,float radians,float delta){float aa=(float)Math.Atan2(a.x,a.z),bb=(float)Math.Atan2(b.x,b.z),d=bb-aa;while(d>(float)Math.PI)d-=2*(float)Math.PI;while(d<-(float)Math.PI)d+=2*(float)Math.PI;d=Math.Max(-radians,Math.Min(radians,d));float v=aa+d;return new Vector3((float)Math.Sin(v),0,(float)Math.Cos(v));}public static Vector3 MoveTowards(Vector3 a,Vector3 b,float d){Vector3 v=b-a;return v.magnitude<=d?b:a+v.normalized*d;}}
 public struct Quaternion {public float yaw;public static Quaternion LookRotation(Vector3 d,Vector3 u){return new Quaternion{yaw=(float)(Math.Atan2(d.x,d.z)*180/Math.PI)};}public static Quaternion RotateTowards(Quaternion a,Quaternion b,float t){float d=b.yaw-a.yaw;while(d>180)d-=360;while(d< -180)d+=360;return new Quaternion{yaw=a.yaw+Math.Max(-t,Math.Min(t,d))};}public static Vector3 operator*(Quaternion q,Vector3 v){float r=q.yaw*(float)Math.PI/180f;return new Vector3(v.x*(float)Math.Cos(r)+v.z*(float)Math.Sin(r),v.y,v.z*(float)Math.Cos(r)-v.x*(float)Math.Sin(r));}}
 public static class Mathf {public static float Max(float a,float b){return Math.Max(a,b);}public static float Min(float a,float b){return Math.Min(a,b);}public static float Abs(float a){return Math.Abs(a);}public static float Sqrt(float a){return (float)Math.Sqrt(a);}public static float Atan2(float a,float b){return (float)Math.Atan2(a,b);}public static float Cos(float a){return (float)Math.Cos(a);}public static float Sin(float a){return (float)Math.Sin(a);}public static float Clamp(float v,float a,float b){return Math.Max(a,Math.Min(v,b));}public static float Clamp01(float v){return Clamp(v,0,1);}public static int CeilToInt(float a){return (int)Math.Ceiling(a);}}
 public static class Time {public static float time,fixedTime,fixedDeltaTime=.02f;}
 public class Rigidbody:Component {public Vector3 position,velocity;public Quaternion rotation;public bool isKinematic;public void MoveRotation(Quaternion q){rotation=q;}}
 public struct Bounds {public Vector3 min,max;public Vector3 center{get{return (min+max)*.5f;}}}
 public class Collider:Component{public bool enabled=true,isTrigger;public Bounds bounds;public virtual Vector3 ClosestPoint(Vector3 p){var c=bounds.center;var d=p-c;return c+d.normalized*Mathf.Min(d.magnitude,(bounds.max.x-bounds.min.x)*.5f);}}
 public class CapsuleCollider:Collider{public int direction;public float radius,height;public Vector3 center;public override Vector3 ClosestPoint(Vector3 p){var c=transform.TransformPoint(center);var d=p-c;var m=d.magnitude;return m<1e-6f?c+d:c+(d*(radius*Mathf.Max(transform.lossyScale.x,transform.lossyScale.z))/m);}}
 public class SphereCollider:Collider{public float radius;public Vector3 center;public override Vector3 ClosestPoint(Vector3 p){var c=transform.TransformPoint(center);var d=p-c;var m=d.magnitude;return m<1e-6f?c+d:c+(d*(radius*Mathf.Max(transform.lossyScale.x,transform.lossyScale.z))/m);}}
 public struct RaycastHit {public Collider collider;public Vector3 point,normal;public float distance;}
 public enum QueryTriggerInteraction{Ignore}
 public static class Physics {public static bool wall,localWall,ceiling,playerObstacle,ground=true;public static float playerZ;public static float slope,groundY;public static Collider floor=new GameObject().AddComponent<Collider>();public static List<Collider> bodies=new List<Collider>();public static RaycastHit[] RaycastAll(Vector3 p,Vector3 d,float len,int mask,QueryTriggerInteraction q){if(!ground)return new RaycastHit[0];return new[]{new RaycastHit{collider=floor,point=new Vector3(p.x,groundY,p.z),normal=new Vector3(slope,1,0).normalized,distance=1}};}public static Collider[] OverlapCapsule(Vector3 a,Vector3 b,float r,int m,QueryTriggerInteraction q){return ceiling?new[]{floor}:new Collider[0];}public static RaycastHit[] CapsuleCastAll(Vector3 p,Vector3 upper,float r,Vector3 d,float len,int m,QueryTriggerInteraction q){if(wall || (playerObstacle && d.z>0 && Math.Max(p.z,upper.z)+d.z*len+r+.3f>=playerZ) || (localWall && Math.Abs(p.x)<.8f && d.z>0 && p.z<2 && p.z+d.z*len>=2))return new[]{new RaycastHit{collider=floor}};return new RaycastHit[0];}public static int RaycastNonAlloc(Vector3 p,Vector3 d,RaycastHit[] h,float len,int m,QueryTriggerInteraction q){var v=RaycastAll(p,d,len,m,q);Array.Copy(v,h,v.Length);return v.Length;}public static int CapsuleCastNonAlloc(Vector3 p,Vector3 u,float r,Vector3 d,RaycastHit[] h,float len,int m,QueryTriggerInteraction q){var v=CapsuleCastAll(p,u,r,d,len,m,q);Array.Copy(v,h,v.Length);return v.Length;}public static int OverlapCapsuleNonAlloc(Vector3 a,Vector3 b,float r,Collider[] h,int m,QueryTriggerInteraction q){var v=OverlapCapsule(a,b,r,m,q);Array.Copy(v,h,v.Length);return v.Length;}public static int OverlapSphereNonAlloc(Vector3 p,float radius,Collider[] a,int m,QueryTriggerInteraction q){int n=0;foreach(var c in bodies)if((c.transform.position-p).magnitude<radius&&n<a.Length)a[n++]=c;return n;}
 public static bool ComputePenetration(Collider a,Vector3 ap,Quaternion aq,Collider b,Vector3 bp,Quaternion bq,out Vector3 dir,out float depth){dir=Vector3.zero;depth=0;float ra=a is SphereCollider?((SphereCollider)a).radius:a is CapsuleCollider?((CapsuleCollider)a).radius:.5f;float rb=a is SphereCollider?((SphereCollider)a).radius:b is CapsuleCollider?((CapsuleCollider)b).radius:.5f;var ca=a.transform.TransformPoint(a is SphereCollider?((SphereCollider)a).center:a is CapsuleCollider?((CapsuleCollider)a).center:Vector3.zero);var cb=b.transform.TransformPoint(b is SphereCollider?((SphereCollider)b).center:b is CapsuleCollider?((CapsuleCollider)b).center:Vector3.zero);var d=cb-ca;float dist=d.magnitude;depth=ra+rb-dist;if(depth<=0f){depth=0;return false;}dir=dist<1e-6f?Vector3.up:d/dist;return true;}}
}
public struct Vector3d{public double x,y,z;public Vector3d(double a,double b,double c){x=a;y=b;z=c;}}
public class mainscript {public static mainscript s=new mainscript();public bool AIDisable;public static Vector3d offset;
 public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d(p.x+offset.x,p.y+offset.y,p.z+offset.z);}
 public static Vector3 UnityPosFromGlobal(Vector3d p){return new Vector3((float)(p.x-offset.x),(float)(p.y-offset.y),(float)(p.z-offset.z));}}
public class pickupable:MonoBehaviour {public bool pickedUp;}
public class sensoryscript:MonoBehaviour {public bool alive=true;}
public class ragdollactivatescript:MonoBehaviour {public bool ragdoll;public Collider[] animColliders;}
public class movecreaturescript:MonoBehaviour {public Rigidbody RB;public float speed=5,rotSpeed=90,acceleration=2,modifierValue=1,moveRot;public bool modifier;public Vector3 moveDir;}
public class playerdeathtriggerscript:MonoBehaviour {public Collider selfCol;}
public class nyulscript:MonoBehaviour {public float lastAnim;public bool attacking;public float size=1;public sensoryscript senses;public playerdeathtriggerscript attackTriggerScript;public aiscript AI;public ragdollactivatescript ragdoll;public void SetAnims(float f){lastAnim=f;}}
public class aiscript:MonoBehaviour {public nyulscript nyul;public movecreaturescript move;public sensoryscript senses;public ragdollactivatescript ragdoll;public bool alive=true,isActiveAndEnabled=true,goToTarget=true,moved=true,fleeing,flee;public float minFleeSpeed=1,walkSpeedModifier=1,moveSpeed=1,targetDistGoal,angleFromTarget,distFromTarget,flightFight,attackSpeed=1;public Vector3 interestPos;}
namespace TldMp {public static class StableGameBridge {internal static bool IsRabbitGuest;} internal static class RabbitContact {internal static bool IgnoreForNavigation(UnityEngine.Collider c){return false;}}}
public static class RabbitLocomotionTests {
 static int n;static void Check(bool b,string s){n++;if(!b)throw new Exception(s);}
 static void Tick(TldMp.RabbitLocomotion x,float dt){Time.time+=dt;Time.fixedTime+=dt;typeof(TldMp.RabbitLocomotion).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(x,null);}
 static aiscript Make(float x,float z,float tx,float tz){var g=new GameObject();g.transform.position=new Vector3(x,0,z);var a=g.AddComponent<aiscript>();a.move=g.AddComponent<movecreaturescript>();a.move.RB=g.AddComponent<Rigidbody>();a.move.RB.position=g.transform.position;a.nyul=g.AddComponent<nyulscript>();a.senses=g.AddComponent<sensoryscript>();a.ragdoll=g.AddComponent<ragdollactivatescript>();var initial=g.AddComponent<Collider>();initial.bounds=new Bounds{min=new Vector3(x-.2f,0,z-.3f),max=new Vector3(x+.2f,.7f,z+.3f)};a.ragdoll.animColliders=new[]{initial};a.interestPos=new Vector3(tx,0,tz);TldMp.RabbitLocomotion.Observe(a);Physics.bodies.Add(g.AddComponent<Collider>());return a;}
 static void Integrate(aiscript a,float dt)
 {
  Vector3 delta=a.move.RB.velocity*dt;a.move.RB.position+=delta;a.transform.position=a.move.RB.position;
  if(a.ragdoll.animColliders!=null)foreach(var c in a.ragdoll.animColliders){Bounds b=c.bounds;b.min+=delta;b.max+=delta;c.bounds=b;}
 }
 static void PhysicsStep(params aiscript[] items)
 {
  Time.time+=.02f;Time.fixedTime+=.02f;
  foreach(var a in items)typeof(TldMp.RabbitLocomotion).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(a.GetComponent<TldMp.RabbitLocomotion>(),null);
  foreach(var a in items)Integrate(a,.02f);
 }
 static object Field(object o,string name){return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void SimulateStep(aiscript a)
 {
  var d=a.move.RB.velocity*.02f;a.move.RB.position+=d;a.transform.position+=d;
  foreach(var c in a.ragdoll.animColliders){var b=c.bounds;b.min+=d;b.max+=d;c.bounds=b;}
 }
 static void Sustained()
 {
  foreach(int fps in new[]{30,60,144})
  {
   Physics.bodies.Clear();Time.time=Time.fixedTime=0;var a=Make(0,0,0,30);a.move.speed=15;a.move.acceleration=20;a.flee=true;a.fleeing=true;a.flightFight=-10;a.targetDistGoal=1;
   var state=a.GetComponent<TldMp.RabbitLocomotion>();float nextRender=0;
   for(int i=0;i<400;i++)
   {
    float t=(i+1)*.02f;Time.time=Time.fixedTime=t;
    while(nextRender<=t){Time.time=nextRender;a.interestPos=a.move.RB.position+new Vector3(0,0,30);a.moved=true;TldMp.RabbitLocomotion.Observe(a);nextRender+=1f/fps;}Time.time=t;
    typeof(TldMp.RabbitLocomotion).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(state,null);SimulateStep(a);
    Check(a.move.RB.velocity.z>=-.001f,"continuous native flee point never causes reversal at "+fps);
   }
   Check(a.move.RB.position.z>70,"moving flee heading continues beyond its first world-space point at "+fps);
   Physics.bodies.Clear();Time.time=Time.fixedTime=0;a=Make(0,0,0,10);a.move.speed=15;a.move.acceleration=20;a.targetDistGoal=1;state=a.GetComponent<TldMp.RabbitLocomotion>();nextRender=0;
   float maxZ=0;
   for(int i=0;i<500;i++)
   {
    float t=(i+1)*.02f;Time.time=Time.fixedTime=t;
    while(nextRender<=t){Time.time=nextRender;a.moved=(a.interestPos-a.move.RB.position).magnitude>1;TldMp.RabbitLocomotion.Observe(a);nextRender+=1f/fps;}Time.time=t;
    typeof(TldMp.RabbitLocomotion).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(state,null);SimulateStep(a);maxZ=Math.Max(maxZ,a.move.RB.position.z);
   }
   Check(maxZ<10.15f,"arrival does not overshoot into a reversal loop at "+fps);
   Check(a.move.RB.velocity.magnitude<.1f&&a.move.RB.position.z>8,"arrives and settles at "+fps);
  }
  Physics.bodies.Clear();Time.time=Time.fixedTime=0;
 }
 public static int Main(){try{Sustained();var a=Make(0,0,0,10);var x=a.GetComponent<TldMp.RabbitLocomotion>();a.move.RB.velocity=new Vector3(0,3,0);Tick(x,.02f);Check(a.move.RB.velocity.y==3,"vertical velocity preserved");Check(a.move.RB.velocity.z>0,"accelerates");float old=a.move.RB.velocity.z;a.move.RB.velocity=new Vector3(7,3,old);Tick(x,.02f);Check(a.move.RB.velocity.x==7,"large external impulse preserved");Tick(x,.02f);Check(a.move.RB.velocity.x==7,"impulse suspension persists");TldMp.RabbitLocomotion.Observe(a);Tick(x,.36f);Check(Math.Abs(a.move.RB.velocity.x-7)<.0001f,"impulse retained after control resumes");a.ragdoll.ragdoll=true;Vector3 before=a.move.RB.velocity;Tick(x,.02f);Check((before-a.move.RB.velocity).sqrMagnitude==0,"ragdoll suspended");Check(!TldMp.RabbitLocomotion.NativeMove(a.move),"native motion suppressed when ragdoll");a.ragdoll.ragdoll=false;TldMp.StableGameBridge.IsRabbitGuest=true;Check(TldMp.RabbitLocomotion.NativeMove(a.move),"guest fallback to existing network guard");TldMp.StableGameBridge.IsRabbitGuest=false;
 Physics.bodies.Clear();Time.time=10;Time.fixedTime=10;var left=Make(0,0,0,10);var right=Make(0,1,0,-10);var l=left.GetComponent<TldMp.RabbitLocomotion>();var r=right.GetComponent<TldMp.RabbitLocomotion>();right.move.RB.rotation=new Quaternion{yaw=180};Tick(l,.02f);Tick(r,.02f);Check(((Vector3)Field(l,"passSide")).x*((Vector3)Field(r,"passSide")).x<0,"head-on rabbits choose reciprocal sides");
 Physics.bodies.Clear();Time.time=20;Time.fixedTime=20;var b=Make(0,0,0,10);var bs=b.GetComponent<TldMp.RabbitLocomotion>();Physics.ground=false;Tick(bs,.02f);Check(b.move.RB.velocity.sqrMagnitude==0,"no ground stops");Physics.ground=true;Physics.groundY=.6f;TldMp.RabbitLocomotion.Observe(b);Tick(bs,.02f);Check(b.move.RB.velocity.sqrMagnitude==0,"roof above origin rejected as ground");Physics.groundY=0;Physics.ceiling=true;TldMp.RabbitLocomotion.Observe(b);Tick(bs,.02f);Check(b.move.RB.velocity.sqrMagnitude==0,"body overlap stops motion");Physics.ceiling=false;Physics.slope=2;TldMp.RabbitLocomotion.Observe(b);Tick(bs,.02f);Check(b.move.RB.velocity.sqrMagnitude==0,"steep ground stops");Physics.slope=0;Physics.wall=true;for(int i=0;i<15;i++){TldMp.RabbitLocomotion.Observe(b);Tick(bs,.02f);}Check(b.move.RB.velocity.sqrMagnitude==0,"wall stops while route searched");Physics.wall=false;
 Physics.bodies.Clear();Time.time=25;Time.fixedTime=25;var route=Make(0,0,0,10);var rs=route.GetComponent<TldMp.RabbitLocomotion>();Physics.localWall=true;
 for(int i=0;i<110;i++){TldMp.RabbitLocomotion.Observe(route);Tick(rs,.02f);}
 Check((bool)Field(rs,"hasWaypoint"),"bounded search finds route around local wall");Check(Math.Abs(((Vector3)Field(rs,"waypoint")).x)>.5f,"route first goes around wall");Physics.localWall=false;
 var hold=Make(0,0,0,10);var hs=hold.GetComponent<TldMp.RabbitLocomotion>();var pickup=hold.gameObject.AddComponent<pickupable>();pickup.pickedUp=true;typeof(TldMp.RabbitLocomotion).GetField("pickups",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(hs,null);Tick(hs,.02f);Check(hold.move.RB.velocity.sqrMagnitude==0,"picked up rabbit suspended");Check(!TldMp.RabbitLocomotion.NativeMove(hold.move),"picked up native motor suppressed");
 var dead=Make(0,0,0,10);var ds=dead.GetComponent<TldMp.RabbitLocomotion>();dead.senses.alive=false;Tick(ds,.02f);Check(dead.move.RB.velocity.sqrMagnitude==0,"deferred death suspended");
 var stale=Make(0,0,0,10);var ss=stale.GetComponent<TldMp.RabbitLocomotion>();Time.time+=1;Check(TldMp.RabbitLocomotion.NativeMove(stale.move),"stale observer restores native fallback");Tick(ss,.02f);Check(stale.move.RB.velocity.sqrMagnitude==0,"stale command discarded");
 var h=Make(0,0,0,10);var hh=h.GetComponent<TldMp.RabbitLocomotion>();h.interestPos=new Vector3(10,0,0);TldMp.RabbitLocomotion.Observe(h);Check(((Vector3)Field(hh,"target")).z==10,"single target change held");Time.time+=.5f;TldMp.RabbitLocomotion.Observe(h);Check(((Vector3)Field(hh,"target")).x==10,"persistent target change accepted");
 Physics.bodies.Clear();Time.time=27;Time.fixedTime=27;var pivot=Make(0,0,0,10);pivot.move.RB.position=new Vector3(0,1,0);var shape=pivot.gameObject.AddComponent<Collider>();shape.bounds=new Bounds{min=new Vector3(-.2f,0,-.3f),max=new Vector3(.2f,.7f,.3f)};pivot.ragdoll.animColliders=new[]{shape};var ps=pivot.GetComponent<TldMp.RabbitLocomotion>();Tick(ps,.02f);Check(Math.Abs(((Vector3)Field(ps,"feetOffset")).y+1)<.001f,"feet derived from enabled collider bounds");Check(pivot.move.RB.velocity.z>0,"elevated pivot can move on valid ground");Check((float)Field(ps,"radius")>.35f,"body width includes collider corners");
 Physics.bodies.Clear();Time.time=29;Time.fixedTime=29;var turn=Make(0,0,1,0);turn.moved=false;turn.targetDistGoal=2;TldMp.RabbitLocomotion.Observe(turn);var ts=turn.GetComponent<TldMp.RabbitLocomotion>();Tick(ts,.02f);Check(turn.move.RB.rotation.yaw>0 && turn.move.RB.rotation.yaw<=1.8001f,"turns toward target inside stop distance at bounded rate");Check(turn.move.RB.velocity.sqrMagnitude==0,"turning in place does not translate");
 var prefab=Make(0,0,0,10);prefab.gameObject.name="MainCollider";var cap=prefab.gameObject.AddComponent<CapsuleCollider>();cap.radius=.3f;cap.height=1;cap.direction=2;cap.center=new Vector3(0,.353f,0);cap.bounds=new Bounds{min=new Vector3(-.3f,.053f,-.5f),max=new Vector3(.3f,.653f,.5f)};prefab.ragdoll.animColliders=new Collider[]{cap};var pcs=prefab.GetComponent<TldMp.RabbitLocomotion>();Tick(pcs,.02f);Check((bool)Field(pcs,"nativeCapsule"),"uses prefab main capsule");Check(Math.Abs((float)Field(pcs,"radius")-.3f)<.0001f,"prefab radius is preserved instead of AABB diagonal");Check(Math.Abs(((Vector3)Field(pcs,"capsuleAOffset")).z+.2f)<.0001f && Math.Abs(((Vector3)Field(pcs,"capsuleBOffset")).z-.2f)<.0001f,"prefab horizontal capsule endpoints preserved");prefab.transform.lossyScale=new Vector3(.6f,.6f,.6f);Tick(pcs,.02f);Check(Math.Abs((float)Field(pcs,"radius")-.18f)<.0001f,"minimum prefab scale reflected in capsule radius");prefab.transform.lossyScale=new Vector3(1.1f,1.1f,1.1f);Tick(pcs,.02f);Check(Math.Abs((float)Field(pcs,"radius")-.33f)<.0001f,"maximum prefab scale reflected in capsule radius");
 Physics.bodies.Clear();Time.time=29.5f;Time.fixedTime=29.5f;var chase=Make(0,0,0,4.5f);chase.gameObject.name="MainCollider";var chaseCap=chase.gameObject.AddComponent<CapsuleCollider>();chaseCap.radius=.3f;chaseCap.height=1;chaseCap.direction=2;chaseCap.center=new Vector3(0,.353f,0);chaseCap.bounds=new Bounds{min=new Vector3(-.3f,.053f,-.5f),max=new Vector3(.3f,.653f,.5f)};chase.ragdoll.animColliders=new Collider[]{chaseCap};var cs=chase.GetComponent<TldMp.RabbitLocomotion>();Physics.playerObstacle=true;Physics.playerZ=4.5f;TldMp.RabbitLocomotion.Observe(chase);Tick(cs,.02f);Check(chase.move.RB.velocity.z>0,"player collider at target does not stop pursuit outside attack distance");Physics.playerObstacle=false;
 Physics.bodies.Clear();Time.time=200;Time.fixedTime=200;var escaping=Make(0,0,0,30);escaping.flee=true;escaping.fleeing=true;escaping.flightFight=-10;escaping.move.speed=12;escaping.move.acceleration=20;escaping.move.rotSpeed=300;
 for(int i=0;i<200;i++){if(i%2==0){escaping.interestPos=escaping.move.RB.position+new Vector3(0,0,30);TldMp.RabbitLocomotion.Observe(escaping);}PhysicsStep(escaping);}
 Check(escaping.move.RB.position.z>35,"moving flee destination remains ahead through four seconds at 25 Hz AI");Check(escaping.move.RB.velocity.z>0,"flee velocity does not reverse after passing old world waypoint");
 var es=escaping.GetComponent<TldMp.RabbitLocomotion>();Vector3 beforeHeading=(Vector3)Field(es,"fleeHeading");
 for(int i=0;i<20;i++){escaping.interestPos=escaping.move.RB.position+new Vector3(0,0,(i%2==0?-30:30));TldMp.RabbitLocomotion.Observe(escaping);PhysicsStep(escaping);}
 Check(Vector3.Dot(beforeHeading,(Vector3)Field(es,"fleeHeading"))>.9f,"alternating opposite flee signals do not flip direction");
 for(int i=0;i<100;i++){escaping.interestPos=escaping.move.RB.position+new Vector3(0,0,-30);TldMp.RabbitLocomotion.Observe(escaping);PhysicsStep(escaping);}
 Check(((Vector3)Field(es,"fleeHeading")).z<-.8f,"persistent opposite threat eventually changes flee heading");Check(Math.Abs(((Vector3)Field(es,"fleeHeading")).y)<.001f,"opposite flee steering remains horizontal");
 Physics.bodies.Clear();Time.time=210;Time.fixedTime=210;var moving=Make(0,0,0,15);moving.move.speed=12;moving.move.acceleration=20;moving.move.rotSpeed=300;
 for(int i=0;i<150;i++){moving.interestPos=new Vector3(0,0,15+i*.16f);TldMp.RabbitLocomotion.Observe(moving);PhysicsStep(moving);}
 Check(moving.move.RB.position.z>25,"moving pursuit target is followed instead of freezing a pending world point");Check(((Vector3)Field(moving.GetComponent<TldMp.RabbitLocomotion>(),"target")).z>35,"continuously moving target memory advances");
 Physics.bodies.Clear();Time.time=220;Time.fixedTime=220;var jitter=Make(0,0,0,20);var js=jitter.GetComponent<TldMp.RabbitLocomotion>();for(int i=0;i<40;i++){jitter.interestPos=new Vector3(i%2==0?8:-8,0,20);TldMp.RabbitLocomotion.Observe(jitter);PhysicsStep(jitter);}Check(Math.Abs(((Vector3)Field(js,"target")).x)<1,"alternating distant targets do not induce steering reversals");
 Physics.bodies.Clear();Time.time=230;Time.fixedTime=230;var aleft=Make(0,-5,0,10);var aright=Make(0,5,0,-10);aleft.move.speed=6;aright.move.speed=6;aleft.move.acceleration=20;aright.move.acceleration=20;aleft.move.rotSpeed=300;aright.move.rotSpeed=300;aright.move.RB.rotation=new Quaternion{yaw=180};
 for(int i=0;i<150;i++){TldMp.RabbitLocomotion.Observe(aleft);TldMp.RabbitLocomotion.Observe(aright);PhysicsStep(aleft,aright);}
 Check(aleft.move.RB.position.z>2&&aright.move.RB.position.z< -2,"opposing rabbits progress through sustained encounter");Check(aleft.move.RB.position.x*aright.move.RB.position.x<0,"opposing rabbits retain reciprocal passing sides");
 Physics.bodies.Clear();Time.time=240;Time.fixedTime=240;var reverse=Make(0,0,0,-10);reverse.move.speed=12;reverse.move.acceleration=20;reverse.move.rotSpeed=180;var reverseStart=reverse.move.RB.position;TldMp.RabbitLocomotion.Observe(reverse);PhysicsStep(reverse);Check((reverse.move.RB.position-reverseStart).sqrMagnitude<.0001f,"rabbit turns before accelerating toward a target behind it");
 var f=typeof(TldMp.RabbitLocomotion).GetField("queries",BindingFlags.Instance|BindingFlags.NonPublic);var lim=typeof(TldMp.RabbitLocomotion).GetField("QueryLimit",BindingFlags.Static|BindingFlags.NonPublic);Time.time=30;Time.fixedTime=30;for(int i=0;i<100;i++){var t=Make(i*10,0,i*10,10);var item=t.GetComponent<TldMp.RabbitLocomotion>();typeof(TldMp.RabbitLocomotion).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(item,null);Check((int)f.GetValue(item)<=(int)lim.GetRawConstantValue(),"per-rabbit physics query cap");Check(t.move.RB.velocity.z>0,"per-rabbit budget avoids ordering starvation");} var messages=new List<string>();TldMp.RabbitLocomotion.Diagnostics=delegate(string message){messages.Add(message);};Time.time=100;var report=typeof(TldMp.RabbitLocomotion).GetMethod("Report",BindingFlags.Instance|BindingFlags.NonPublic);for(int i=0;i<6;i++){var sample=Make(i*3,0,i*3,10);report.Invoke(sample.GetComponent<TldMp.RabbitLocomotion>(),new object[]{"blocked"});}Check(messages.Count==0,"diagnostic report is rate limited");Time.time=110.1f;report.Invoke(bs,new object[]{"budget"});Check(messages.Count==1&&messages[0].Contains("blocked=6")&&messages[0].Contains("budget=1"),"diagnostic aggregate reports blocked and budget reasons");Check(messages[0].Split(new[]{"reason="},StringSplitOptions.None).Length-1==3,"diagnostic samples capped at three");TldMp.RabbitLocomotion.Diagnostics=null;
// Committed strike under a REAL independent render schedule: render Observe
// runs on its own nextRender clock at 30/60/144fps while physics stays fixed
// at 50Hz (same scheduler technique as Sustained). The attack commits heading
// + original global goal; mid-strike negative fear and a flee destination
// behind must NOT reverse or replace it.
var fixedM=typeof(TldMp.RabbitLocomotion).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
foreach(int fps in new[]{30,60,144})
{
    Physics.bodies.Clear();Time.time=Time.fixedTime=400+fps;
    var st=Make(0,0,0,3);st.move.speed=12;st.move.acceleration=20;st.move.rotSpeed=300;
    st.move.modifier=true;st.move.modifierValue=1.5f;st.moveSpeed=1;st.attackSpeed=1;st.targetDistGoal=1;
    st.nyul.attacking=true;
    var sx=st.GetComponent<TldMp.RabbitLocomotion>();
    float t=400+fps,nextRender=t;int renders=0;
    TldMp.RabbitLocomotion.Observe(st);renders++;
    Check((bool)Field(sx,"strikeActive"),fps+"fps strike commits on attack flag");
    float peak=0f,prevZ=st.move.RB.position.z;
    for(int step=0;step<100;step++)
    {
        t+=.02f;
        while(nextRender<=t+.0001f){Time.time=nextRender;st.flee=true;st.fleeing=true;st.flightFight=-20;st.interestPos=new Vector3(0,0,-100);TldMp.RabbitLocomotion.Observe(st);renders++;nextRender+=1f/fps;}
        Time.time=Time.fixedTime=t;
        fixedM.Invoke(sx,null);SimulateStep(st);
        peak=Math.Max(peak,st.move.RB.position.z);
        Check(st.move.RB.position.z>=prevZ-.001f,fps+"fps strike never moves backward");
        Check(Math.Abs(st.move.RB.rotation.yaw)<8f,fps+"fps body rotation holds strike heading");
        Check(st.move.RB.velocity.magnitude<=st.move.speed*st.move.modifierValue+1f,fps+"fps strike speed stays natively bounded");
        prevZ=st.move.RB.position.z;
    }
    Check(Math.Abs(renders-2f*fps)<=2f,fps+"fps render observations follow independent cadence "+renders);
    Check((bool)Field(sx,"strikeActive"),fps+"fps negative fear mid-strike keeps committed strike");
    Check(((Vector3)Field(sx,"strikeHeading")).z>0.5f,fps+"fps strike heading preserved toward original goal");
    Check(st.move.RB.position.z>0f,fps+"fps strike advances toward goal");
    Check(Math.Abs(st.move.RB.position.z-3f)<=1.15f,fps+"fps strike stops within stop radius of committed goal");
    Check(peak<=4.6f,fps+"fps strike stays within braking epsilon of committed goal");
    Check(st.move.RB.velocity.magnitude<.5f,fps+"fps strike brakes at committed goal");
    // attack flag ends -> native flee/goal decisions resume at bounded turn rate
    st.nyul.attacking=false;
    TldMp.RabbitLocomotion.Observe(st);
    Check(!(bool)Field(sx,"strikeActive"),fps+"fps attack end clears strike for native decisions");
    float yaw0=st.move.RB.rotation.yaw,elapsed=0f;
    for(int step=0;step<20;step++){elapsed+=.02f;st.interestPos=st.move.RB.position+new Vector3(0,0,-30);TldMp.RabbitLocomotion.Observe(st);fixedM.Invoke(sx,null);SimulateStep(st);}
    float yawDelta=Math.Abs(st.move.RB.rotation.yaw-yaw0);
    Check(yawDelta>1f,fps+"fps native flee actually turns after attack ends");
    Check(yawDelta<=st.move.rotSpeed*elapsed+.5f,fps+"fps flee turning stays inside native turn rate");
}
// Real recenter: run an unshifted reference strike first and record its
// GLOBAL trajectory. Then rebuild the same initial state, commit the strike,
// shift the ENTIRE world (body, transform, ragdoll animColliders and every
// world collider) by the inverse of the new offset, and require the
// recentered GLOBAL pose/velocity to match the reference every tick.
var refTrack=new List<Vector3>();
{
    Physics.bodies.Clear();Time.time=Time.fixedTime=520;
    var refR=Make(0,0,0,3);refR.move.speed=12;refR.move.acceleration=20;refR.move.rotSpeed=300;refR.targetDistGoal=1;refR.nyul.attacking=true;refR.moveSpeed=1;refR.attackSpeed=1;refR.move.modifier=true;refR.move.modifierValue=1.5f;
    var refX=refR.GetComponent<TldMp.RabbitLocomotion>();
    TldMp.RabbitLocomotion.Observe(refR);fixedM.Invoke(refX,null);SimulateStep(refR);
    for(int step=0;step<100;step++){if(step%10==0)TldMp.RabbitLocomotion.Observe(refR);fixedM.Invoke(refX,null);SimulateStep(refR);refTrack.Add(refR.move.RB.position);}
}
Physics.bodies.Clear();Time.time=Time.fixedTime=520;
var rc=Make(0,0,0,3);rc.move.speed=12;rc.move.acceleration=20;rc.move.rotSpeed=300;rc.targetDistGoal=1;rc.nyul.attacking=true;rc.moveSpeed=1;rc.attackSpeed=1;rc.move.modifier=true;rc.move.modifierValue=1.5f;
var rcx=rc.GetComponent<TldMp.RabbitLocomotion>();
TldMp.RabbitLocomotion.Observe(rc);fixedM.Invoke(rcx,null);SimulateStep(rc);
Check((bool)Field(rcx,"strikeActive"),"recenter strike commits");
var feetBefore=(Vector3)Field(rcx,"feetOffset");
var feetBeforeGlobal=new Vector3(feetBefore.x+rc.move.RB.position.x,feetBefore.y+rc.move.RB.position.y,feetBefore.z+rc.move.RB.position.z);
mainscript.offset=new Vector3d(500,0,0);
var shift=new Vector3(500,0,0);
rc.move.RB.position-=shift;rc.gameObject.transform.position=rc.move.RB.position;
foreach(var col in rc.ragdoll.animColliders){var bb=col.bounds;bb.min-=shift;bb.max-=shift;col.bounds=bb;}
foreach(var col in Physics.bodies){var bq=col.bounds;bq.min-=shift;bq.max-=shift;col.bounds=bq;}
Check(Math.Abs(((Vector3)Field(rcx,"feetOffset")-feetBefore).magnitude)<.01f,"recenter keeps feet geometry identical");
for(int step=0;step<100;step++)
{
    if(step%10==0)TldMp.RabbitLocomotion.Observe(rc);
    fixedM.Invoke(rcx,null);SimulateStep(rc);
    var gRc=mainscript.GlobalFromUnityPos(rc.move.RB.position);
    var gRef=refTrack[step];
    Check(Math.Abs(gRef.x-gRc.x)<.001f&&Math.Abs(gRef.z-gRc.z)<.001f,"recentered strike tracks reference global pose at tick "+step);
}
Check((bool)Field(rcx,"strikeActive"),"recentered strike survives to goal");
Check(rc.move.RB.velocity.magnitude<.5f,"recentered strike brakes at committed goal");
Check(Math.Abs(rc.move.RB.position.z-refTrack[99].z)<.001f,"recentered strike stops at same global goal as reference");
// Cancellation paths during an active strike
foreach(string kill in new[]{"dead","grab","ragdoll","guest"})
{
    Physics.bodies.Clear();Time.time=540;Time.fixedTime=540;
    var victim=Make(0,0,0,3);victim.move.speed=12;victim.move.acceleration=20;victim.targetDistGoal=1;victim.nyul.attacking=true;
    var vx=victim.GetComponent<TldMp.RabbitLocomotion>();
    TldMp.RabbitLocomotion.Observe(victim);
    typeof(TldMp.RabbitLocomotion).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(vx,null);
    Check((bool)Field(vx,"strikeActive"),kill+" strike active before cancel");
    if(kill=="dead")victim.senses.alive=false;
    else if(kill=="grab")victim.gameObject.AddComponent<pickupable>().pickedUp=true;
    else if(kill=="ragdoll")victim.ragdoll.ragdoll=true;
    else TldMp.StableGameBridge.IsRabbitGuest=true;
    typeof(TldMp.RabbitLocomotion).GetField("pickups",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vx,null);
    TldMp.RabbitLocomotion.Observe(victim);
    typeof(TldMp.RabbitLocomotion).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(vx,null);
    Check(!(bool)Field(vx,"strikeActive"),kill+" cancels committed strike");
    TldMp.StableGameBridge.IsRabbitGuest=false;
}
Console.WriteLine("PASS "+n+" locomotion checks");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}






















