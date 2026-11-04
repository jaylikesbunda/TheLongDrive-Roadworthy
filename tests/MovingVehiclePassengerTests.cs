using System;
using System.Collections.Generic;
using UnityEngine;
using TldMp;
namespace UnityEngine
{
 public class Component {public GameObject gameObject;public Transform transform{get{return gameObject.transform;}}public string name{get{return gameObject.name;}}public T GetComponent<T>() where T:Component{return gameObject.GetComponent<T>();}public T[] GetComponents<T>() where T:Component{return gameObject.GetComponents<T>();}public T GetComponentInParent<T>() where T:Component{for(Transform t=transform;t!=null;t=t.parent){T c=t.gameObject.GetComponent<T>();if(c!=null)return c;}return null;}}
 public class GameObject {public string name;public Transform transform;readonly List<Component> components=new List<Component>();public GameObject(string n){name=n;transform=Add<Transform>();}public T Add<T>()where T:Component,new(){var c=new T();c.gameObject=this;components.Add(c);return c;}public T GetComponent<T>()where T:Component{foreach(var c in components)if(c is T)return (T)c;return null;}public T[] GetComponents<T>()where T:Component{var r=new List<T>();foreach(var c in components)if(c is T)r.Add((T)c);return r.ToArray();}}
 public class Transform:Component {public Transform parent;public Vector3 position;public Quaternion rotation=Quaternion.identity;}
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero{get{return new Vector3();}}public static Vector3 up{get{return new Vector3(0,1,0);}}public static Vector3 down{get{return new Vector3(0,-1,0);}}public float sqrMagnitude{get{return x*x+y*y+z*z;}}public float magnitude{get{return (float)Math.Sqrt(sqrMagnitude);}}public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}public static Vector3 operator*(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}public static Vector3 operator/(Vector3 a,float b){return a*(1/b);}public static float Dot(Vector3 a,Vector3 b){return a.x*b.x+a.y*b.y+a.z*b.z;}public static float Distance(Vector3 a,Vector3 b){return (a-b).magnitude;}public static Vector3 Lerp(Vector3 a,Vector3 b,float t){return a+(b-a)*t;}}
 public struct Vector3d {public double x,y,z;public Vector3d(double a,double b,double c){x=a;y=b;z=c;}}
 public struct Quaternion {public double yaw;public float x,y,z,w;public Quaternion(float a,float b,float c,float d){x=a;y=b;z=c;w=d;yaw=0;}public static Quaternion identity{get{return new Quaternion(0,0,0,1);}}public static Quaternion Inverse(Quaternion q){return new Quaternion{yaw=-q.yaw,w=1};}public static Quaternion Slerp(Quaternion a,Quaternion b,float t){return new Quaternion{yaw=a.yaw+(b.yaw-a.yaw)*t,w=1};}public static Quaternion operator*(Quaternion a,Quaternion b){return new Quaternion{yaw=a.yaw+b.yaw,w=1};}public static float Angle(Quaternion a,Quaternion b){return (float)(Math.Abs(a.yaw-b.yaw)*180/Math.PI);}public static Vector3 operator*(Quaternion q,Vector3 v){return new Vector3((float)(Math.Cos(q.yaw)*v.x+Math.Sin(q.yaw)*v.z),v.y,(float)(-Math.Sin(q.yaw)*v.x+Math.Cos(q.yaw)*v.z));}}
 public enum RigidbodyInterpolation{None,Interpolate,Extrapolate}
 public class Rigidbody:Component {public Vector3 position,velocity,angularVelocity;public Quaternion rotation=Quaternion.identity;public bool isKinematic;public RigidbodyInterpolation interpolation;public void MovePosition(Vector3 v){position=v;}public void MoveRotation(Quaternion q){rotation=q;}}
 public class ConfigurableJoint:Component{public Rigidbody connectedBody;}
 public struct Bounds{public Vector3 center,extents;}
 public struct Ray{public Ray(Vector3 p,Vector3 d){}}
 public enum QueryTriggerInteraction{Ignore}
 public struct RaycastHit{public Vector3 normal,point;public Collider collider;public float distance;}
 public class Collider:Component {public bool enabled=true,isTrigger;public Rigidbody attachedRigidbody;public Bounds bounds;public bool Raycast(Ray r,out RaycastHit h,float d){h=new RaycastHit{normal=Physics.normal};return Physics.ray;}}
 public static class Physics {public static bool contact=true,ray=true;public static Vector3 normal=Vector3.up,point=new Vector3(0,-.1f,0);public static Collider surface,nearer;public static int SphereCastNonAlloc(Vector3 o,float r,Vector3 d,RaycastHit[] hits,float length,int mask,QueryTriggerInteraction query){if(!ray||surface==null)return 0;hits[0]=new RaycastHit{normal=normal,point=point,collider=surface,distance=.2f};if(nearer!=null){hits[1]=new RaycastHit{normal=Vector3.up,point=point,collider=nearer,distance=.1f};return 2;}return 1;}public static bool ComputePenetration(Collider a,Vector3 p,Quaternion q,Collider b,Vector3 p2,Quaternion q2,out Vector3 direction,out float distance){direction=Vector3.up;distance=.1f;return contact;}}
 public static class Time{public static float time,realtimeSinceStartup,fixedDeltaTime=.02f;}
 public static class Mathf{public static float Max(float a,float b){return Math.Max(a,b);}public static float Min(float a,float b){return Math.Min(a,b);}public static float Clamp01(float x){return Math.Max(0,Math.Min(1,x));}}
 public static class Debug{public static void Log(object o){}}
}
public class fpscontroller:Component {public Rigidbody RB;public bool loadedmove=true,Bsitting,died,ladder,griping,pushgripping;public float FmaxSpeed=5;public sens grounded;public Inputs input=new Inputs();}
public class Inputs{public bool jumpDown;}
public class sens:Component{public bool sensed=true;public Transform T;public Collider selfCol;}
public class partslotscript:Component{}
public class tosaveitemscript:Component{public enum carWhatDoing{nothing,driving}public carWhatDoing role;public uint idInSave;public carWhatDoing CarWhatDoing(){return role;}public object car=new object();public Rigidbody RB;public List<partslotscript> partslotscripts=new List<partslotscript>();public bool owned,smoothed=true;public bool ThisOneInChargeCar(){return owned;}}
public class savedatascript{public Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();}
public static class mainscript{public static double offset;public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d(p.x+offset,p.y,p.z);}public static Vector3 UnityPosFromGlobal(Vector3d p){return new Vector3((float)(p.x-offset),(float)p.y,(float)p.z);}}
namespace TldMp{public class BusArticulation:Component{}}
#if !BUS_PAIR_TEST
namespace TldMp{internal static class BusPairReplica{internal static bool Owns(tosaveitemscript item){return item.smoothed;}internal static bool OwnsBody(Rigidbody body){return body!=null;}}}
#endif
static class MovingVehiclePassengerTests
{
 static int checks;static void Check(bool value,string what){checks++;if(!value)throw new Exception(what);}static void Near(float a,float b,string what){Check(Math.Abs(a-b)<.002f,what);}
 sealed class Fixture
 {
  internal fpscontroller p;internal Rigidbody floor;internal tosaveitemscript car;
  internal Fixture(){MovingVehiclePassenger.Reset();Time.time=0;Time.fixedDeltaTime=.02f;mainscript.offset=0;Physics.contact=Physics.ray=true;Physics.normal=Vector3.up;Physics.point=new Vector3(0,-.1f,0);Physics.nearer=null;var vehicle=new GameObject("car");floor=vehicle.Add<Rigidbody>();floor.interpolation=RigidbodyInterpolation.Interpolate;car=vehicle.Add<tosaveitemscript>();car.RB=floor;var surface=new GameObject("floor");surface.transform.parent=vehicle.transform;Physics.surface=surface.Add<Collider>();Physics.surface.attachedRigidbody=floor;var person=new GameObject("player");p=person.Add<fpscontroller>();p.RB=person.Add<Rigidbody>();p.grounded=person.Add<sens>();p.grounded.T=surface.transform;p.grounded.selfCol=person.Add<Collider>();p.grounded.selfCol.bounds=new Bounds{center=Vector3.zero,extents=new Vector3(.2f,.1f,.2f)};}
  internal void Tick(float dx){floor.position+=new Vector3(dx,0,0);Time.time+=.02f;MovingVehiclePassenger.FixedTick(p,true);}
  internal void Ready(){Tick(0);Tick(.4f);p.RB.velocity=new Vector3(20,0,0);}
 }
 static void AcceleratingRide(int fps,float friction,float walk)
 {
  var f=new Fixture();f.Tick(0);f.Tick(0);float speed=0,expectedWalk=0,render=0;
  for(int i=0;i<100;i++)
  {
   float change=i<50?.2f:-.2f;speed+=change;
   f.p.RB.velocity+=new Vector3(change*friction,0,0);
   render+=fps/50f;
   while(render>=1f)
   {
    render-=1f;MovingVehiclePassenger.BeginMove(f.p);
    float input=walk/fps;f.p.RB.velocity+=new Vector3(input,0,0);expectedWalk+=input;
    MovingVehiclePassenger.EndMove(f.p);
   }
   f.Tick(speed*.02f);
   Near(f.p.RB.velocity.x,speed*friction+expectedWalk,"no inferred support acceleration fps="+fps+" friction="+friction+" walk="+walk+" step="+i);
  }
 }
 static void Main()
 {
  var f=new Fixture();f.Tick(0);f.p.RB.velocity=new Vector3(20,0,0);MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,20,"warmup preserves velocity");MovingVehiclePassenger.EndMove(f.p);
  f.Tick(.4f);MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,0,"native walking sees platform-relative velocity");Near(MovingVehiclePassenger.Velocity(f.floor).x,0,"interior reads are in same frame");f.p.RB.velocity=new Vector3(1,2,0);MovingVehiclePassenger.EndMove(f.p);Near(f.p.RB.velocity.x,21,"restores platform speed plus walking");Near(f.p.RB.velocity.y,2,"retains vertical impulse");Check(f.p.RB.interpolation==RigidbodyInterpolation.Interpolate,"render phase matches interpolated floor");
  f.floor.velocity=new Vector3(999,0,0);Near(MovingVehiclePassenger.Velocity(f.floor).x,999,"outside bracket native velocity unchanged");
  f.p.input.jumpDown=true;MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,21,"jump disengages without removing inherited motion");MovingVehiclePassenger.EndMove(f.p);
  f.p.input.jumpDown=false;Physics.ray=false;f.Tick(.4f);MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,21,"lingering sensor without contact rejected");Check(f.p.RB.interpolation==RigidbodyInterpolation.None,"lost support restores interpolation");
  f=new Fixture();Physics.contact=false;f.Ready();f.p.grounded.sensed=false;f.p.grounded.T=null;MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,0,"near floor accepted without penetration or fresh sensor");MovingVehiclePassenger.EndMove(f.p);
  f=new Fixture();f.Ready();Physics.normal=new Vector3(1,0,0);MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,20,"wall is not support");
  f=new Fixture();f.Ready();Physics.point=new Vector3(0,-.5f,0);MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,20,"airborne floor gap rejected");
  f=new Fixture();f.Ready();Physics.nearer=new GameObject("ground").Add<Collider>();MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,20,"nearer world floor blocks vehicle support");
  f=new Fixture();f.p.grounded.sensed=false;f.Tick(0);f.Tick(.4f);f.p.RB.velocity=new Vector3(20,0,0);MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,20,"airborne player cannot acquire a new support");
  f=new Fixture();f.Ready();f.p.RB.velocity=new Vector3(20,2,0);MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,20,"upward jump momentum bypasses bracket");
  f=new Fixture();f.Ready();Time.time+=.2f;MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,20,"stale samples rejected");
  f=new Fixture();f.Ready();f.car.owned=true;MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,20,"local authority keeps native motor");
  f=new Fixture();f.Ready();f.p.Bsitting=true;MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,20,"seated path unaffected");
  f=new Fixture();f.Ready();f.Tick(100);MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,20,"teleport sample rejected");
  f=new Fixture();f.Tick(0);mainscript.offset=1000;f.floor.position=new Vector3(-1000,0,0);Physics.point=new Vector3(-1000,-.1f,0);f.p.grounded.selfCol.bounds=new Bounds{center=new Vector3(-1000,0,0),extents=new Vector3(.2f,.1f,.2f)};Time.time+=.02f;MovingVehiclePassenger.FixedTick(f.p,true);f.p.RB.velocity=Vector3.zero;MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.x,0,"recenter does not add platform speed");MovingVehiclePassenger.EndMove(f.p);
  f=new Fixture();Physics.point=new Vector3(2,-.1f,0);f.p.grounded.selfCol.bounds=new Bounds{center=new Vector3(2,0,0),extents=new Vector3(.2f,.1f,.2f)};f.Tick(0);f.floor.rotation=new Quaternion{yaw=.02};f.Tick(0);f.p.RB.velocity=new Vector3(0,0,-2);MovingVehiclePassenger.BeginMove(f.p);Near(f.p.RB.velocity.z,0,"point motion includes angular velocity");MovingVehiclePassenger.EndMove(f.p);
  f=new Fixture();f.Ready();try{MovingVehiclePassenger.BeginMove(f.p);throw new InvalidOperationException();}catch(InvalidOperationException){}finally{MovingVehiclePassenger.EndMove(f.p);}Near(f.p.RB.velocity.x,20,"finally restores frame after native failure");
  MovingVehiclePassenger.Reset();Check(f.p.RB.interpolation==RigidbodyInterpolation.None,"reset restores interpolation");bool threw=false;try{MovingVehiclePassenger.Velocity(null);}catch(NullReferenceException){threw=true;}Check(threw,"null velocity receiver preserves native exception");
  Check(!MovingVehiclePassengerPolicy.ValidSample(double.NaN,0,0,.02f,0,1),"nonfinite motion rejected");Check(!MovingVehiclePassengerPolicy.ValidSample(0,0,0,.02f,90,1),"rotation snap rejected");Check(!MovingVehiclePassengerPolicy.ValidVelocity(float.PositiveInfinity),"nonfinite point velocity rejected");
  foreach(int fps in new int[]{30,60,144})foreach(float friction in new float[]{0,.5f,1})foreach(float walk in new float[]{-2f,-1f,0,1f,2f})AcceleratingRide(fps,friction,walk);
  f=new Fixture();var localPoint=new Vector3(2,-.1f,0);Physics.point=localPoint;f.p.grounded.selfCol.bounds=new Bounds{center=new Vector3(2,0,0),extents=new Vector3(.2f,.1f,.2f)};f.Tick(0);f.Tick(0);
  var oldRotation=Quaternion.identity;
  for(int i=1;i<=100;i++){
   var nextRotation=new Quaternion{yaw=.001*i};Physics.point=nextRotation*localPoint;
   f.p.grounded.selfCol.bounds=new Bounds{center=Physics.point+new Vector3(0,.1f,0),extents=new Vector3(.2f,.1f,.2f)};
   f.floor.rotation=nextRotation;Vector3 expected=(nextRotation*localPoint-oldRotation*localPoint)/.02f;f.Tick(0);
   Near(f.p.RB.velocity.x,0,"rotation does not inject x");Near(f.p.RB.velocity.z,0,"rotation does not inject z");oldRotation=nextRotation;
  }
  f=new Fixture();f.Tick(0);f.Tick(0);f.p.RB.velocity=new Vector3(-3,0,4);f.Tick(.02f);Near(f.p.RB.velocity.x,-3,"opposing impulse is preserved without synthetic acceleration");Near(f.p.RB.velocity.z,4,"orthogonal impulse is preserved");
  f=new Fixture();f.Tick(0);f.Tick(0);f.Tick(.2f);Near(f.p.RB.velocity.x,0,"large acceleration sample cannot cause arbitrary catchup");f.Tick(.4f);Near(f.p.RB.velocity.x,0,"large acceleration resets warmup");
  f=new Fixture();f.Tick(0);f.Tick(.2f);f.Tick(.2f);Near(f.p.RB.velocity.x,0,"boarding an already moving vehicle never injects its whole speed");
  f=new Fixture();f.Tick(0);f.Tick(0);f.p.input.jumpDown=true;f.Tick(.02f);Near(f.p.RB.velocity.x,0,"jump does not gain support acceleration");
  f=new Fixture();f.Tick(0);f.Tick(0);Physics.ray=false;f.Tick(.02f);Near(f.p.RB.velocity.x,0,"lost contact does not gain support acceleration");
  var data=new savedatascript();var bus=new GameObject("front");var item=bus.Add<tosaveitemscript>();item.idInSave=42;data.items[42]=item;var packet=new byte[]{43,42,0,0,0};
  item.role=tosaveitemscript.carWhatDoing.driving;Check(BusRearAuthority.Allow(packet,true,data),"unrelated driven car retains native rear path");bus.Add<BusArticulation>();Check(!BusRearAuthority.Allow(packet,true,data),"local bus driver rejects delayed rear teleport");
  item.role=tosaveitemscript.carWhatDoing.nothing;item.smoothed=false;Check(BusRearAuthority.Allow(packet,true,data),"remote bus retains incoming rear pose");item.role=tosaveitemscript.carWhatDoing.driving;item.smoothed=true;
  Check(BusRearAuthority.Allow(packet,false,data),"offline path retained");Check(BusRearAuthority.Allow(new byte[]{43,99,0,0,0},true,data),"unknown item follows native repair");Check(BusRearAuthority.Allow(new byte[]{16,42,0,0,0},true,data),"unrelated message unaffected");
  Console.WriteLine("PASS "+checks+" moving passenger checks");
 }
}
