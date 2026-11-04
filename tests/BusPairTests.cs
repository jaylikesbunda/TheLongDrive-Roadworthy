using System;
using System.Collections.Generic;
using Steamworks;
using TldMp;
using UnityEngine;

namespace UnityEngine
{
    public class Object {}
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T:Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T:Component { Transform t=transform; while(t!=null){T c=t.gameObject.GetComponent<T>(); if(c!=null)return c; t=t.parent;} return null; }
        public List<T> GetComponentsInChildren<T>() where T:Component { return gameObject.GetComponentsInChildren<T>(); }
    }
    public class MonoBehaviour : Component {}
    public class GameObject : Object
    {
        internal readonly Dictionary<Type,Component> components=new Dictionary<Type,Component>();
        public Transform transform; public bool activeInHierarchy=true;
        public GameObject(string n) { transform=new Transform(); transform.gameObject=this; transform.name=n; components[typeof(Transform)]=transform; }
        public T AddComponent<T>() where T:Component,new() { T c=new T(); c.gameObject=this; components[typeof(T)]=c; return c; }
        public T GetComponent<T>() where T:Component { Component c; return components.TryGetValue(typeof(T),out c)?(T)c:null; }
        public void Add2(Component c){components[c.GetType()]=c;c.gameObject=this;}
        public List<T> GetComponentsInChildren<T>() where T:Component { var r=new List<T>(); Collect<T>(transform,r); return r; }
        void Collect<T>(Transform t,List<T> r) where T:Component { foreach(var kv in t.gameObject.components)if(kv.Value is T)r.Add((T)kv.Value); foreach(var ch in t.children)Collect<T>(ch,r); }
    }
    public class Transform : Component
    {
        public string name; public Transform parent; internal readonly List<Transform> children=new List<Transform>();
        public void Add(Transform c){c.parent=this;children.Add(c);}
    }
    public struct Bounds
    {
        public Vector3 center,extents;
        public Bounds(Vector3 c,Vector3 size){center=c;extents=new Vector3(size.x*.5f,size.y*.5f,size.z*.5f);}
        public bool Intersects(Bounds o){return Math.Abs(center.x-o.center.x)<=extents.x+o.extents.x&&Math.Abs(center.y-o.center.y)<=extents.y+o.extents.y&&Math.Abs(center.z-o.center.z)<=extents.z+o.extents.z;}
        public void Encapsulate(Bounds o){var mn=new Vector3(Math.Min(center.x-extents.x,o.center.x-o.extents.x),Math.Min(center.y-extents.y,o.center.y-o.extents.y),Math.Min(center.z-extents.z,o.center.z-o.extents.z));var mx=new Vector3(Math.Max(center.x+extents.x,o.center.x+o.extents.x),Math.Max(center.y+extents.y,o.center.y+o.extents.y),Math.Max(center.z+extents.z,o.center.z+o.extents.z));center=new Vector3((mn.x+mx.x)*.5f,(mn.y+mx.y)*.5f,(mn.z+mx.z)*.5f);extents=new Vector3((mx.x-mn.x)*.5f,(mx.y-mn.y)*.5f,(mx.z-mn.z)*.5f);}
        public void Expand(float amount){extents=new Vector3(extents.x+amount*.5f,extents.y+amount*.5f,extents.z+amount*.5f);}
    }
    public class Collider : Component { public bool isTrigger,enabled=true; public Bounds bounds; }
    public class BoxCollider : Collider {}
    public class CapsuleCollider : Collider { public float radius,height; public int direction; public Vector3 center; }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero {get{return new Vector3(0,0,0);}}
        public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator*(Vector3 a,float s){return new Vector3(a.x*s,a.y*s,a.z*s);}
        public float magnitude {get{return (float)Math.Sqrt(x*x+y*y+z*z);}}
        public static Vector3 MoveTowards(Vector3 a,Vector3 b,float d){var dd=b-a;float m=dd.magnitude;return m<=d?b:a+dd*(m>0?d/m:0);}
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t){return new Vector3(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);}
        public static float Distance(Vector3 a,Vector3 b){return (a-b).magnitude;}
        public override string ToString(){return "("+x+","+y+","+z+")";}
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}
        public static Quaternion identity {get{return new Quaternion(0,0,0,1);}}
        public static Quaternion Inverse(Quaternion q){return new Quaternion(-q.x,-q.y,-q.z,q.w);}
        public static Vector3 operator*(Quaternion q,Vector3 v){float xx=q.x*2,yy=q.y*2,zz=q.z*2;return new Vector3((1-q.y*yy-q.z*zz)*v.x+(q.x*yy-q.w*zz)*v.y+(q.x*zz+q.w*yy)*v.z,(q.x*yy+q.w*zz)*v.x+(1-q.x*xx-q.z*zz)*v.y+(q.y*zz-q.w*xx)*v.z,(q.x*zz-q.w*yy)*v.x+(q.y*zz+q.w*xx)*v.y+(1-q.x*xx-q.y*yy)*v.z);}
        public static Quaternion operator*(Quaternion a,Quaternion b){return new Quaternion(a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w,a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z);}
        static float Norm(Quaternion q){return q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;}
        public static Quaternion Slerp(Quaternion a,Quaternion b,float t){return new Quaternion(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t,a.w+(b.w-a.w)*t);}
        public static float Angle(Quaternion a,Quaternion b){float d=Math.Abs(a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w);return d>=1f?0f:(float)(2*Math.Acos(d)*180/Math.PI);}
        public static Quaternion RotateTowards(Quaternion a,Quaternion b,float d){float ang=Angle(a,b);return ang<=d?b:Slerp(a,b,d/ang);}
    }
    public struct Vector3d
    {
        public double x,y,z;
        public Vector3d(double a,double b,double c){x=a;y=b;z=c;}
    }
    public enum RigidbodyInterpolation {None,Interpolate,Extrapolate}
    public class Rigidbody : Component
    {
        public RigidbodyInterpolation interpolation;public bool isKinematic;
        public Vector3 position,velocity,angularVelocity;public Quaternion rotation=Quaternion.identity;
        public Vector3 movedTo;public Quaternion rotatedTo;public int moves,rotations;
        public void MovePosition(Vector3 p){movedTo=p;moves++;position=p;}
        public void MoveRotation(Quaternion q){rotatedTo=q;rotations++;rotation=q;}
    }
    public class ConfigurableJoint : Component {public Rigidbody connectedBody;}
    public static class Mathf {public static float Clamp01(float v){return v<0?0:v>1?1:v;}}
    public static class Time {public static float realtimeSinceStartup,fixedDeltaTime;}
    public static class Debug {public static void Log(object x){}public static void LogWarning(object x){}}
}
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
public class carscript : MonoBehaviour
{
    public tosaveitemscript tosave;
    public tosaveitemscript.carWhatDoing lastMultiWhatDoing;
    public float lastMultiWhatDoingTime;
}
public class seatscript : MonoBehaviour
{
    public bool driverSeat0,driverSeat1;
    public carscript Car;
    public Transform parentObj;
}
public class partslotscript : MonoBehaviour {}
public class tosaveitemscript : MonoBehaviour
{
    public enum carWhatDoing { notCar, nothing, justHost, pickedup, shotgun, pushgripping, driving }
    public uint idInSave; public int id; public carscript car; public Rigidbody RB;
    public List<partslotscript> partslotscripts=new List<partslotscript>();
    public seatscript[] seats=new seatscript[0];
    public carWhatDoing doing=carWhatDoing.nothing;
    public carWhatDoing CarWhatDoing(){return car==null?carWhatDoing.notCar:doing;}
    public bool ThisOneInChargeCar(carWhatDoing remote){return (int)CarWhatDoing()>=2&&(int)CarWhatDoing()>(int)remote;}
    public bool ThisOneInChargeCar(){return car!=null&&ThisOneInChargeCar(car.lastMultiWhatDoing);}
}
public class fpscontroller : MonoBehaviour {public Rigidbody RB;public seatscript seat;public Collider[] C;}
public class syncScript : MonoBehaviour
{
    public static syncScript s;
    public class playerSyncClass { public ulong steamID; public GameObject player; public bool sitting; public Transform sitObjParent; }
    public Dictionary<CSteamID,playerSyncClass> players=new Dictionary<CSteamID,playerSyncClass>();
}
public class mainscript : MonoBehaviour
{
    public static mainscript s;
    public fpscontroller player;
    public static Vector3d offset=new Vector3d(0,0,0);
    public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d(p.x+offset.x,p.y+offset.y,p.z+offset.z);}
    public static Vector3 UnityPosFromGlobal(Vector3d p){return new Vector3((float)(p.x-offset.x),(float)(p.y-offset.y),(float)(p.z-offset.z));}
}
namespace TldMp { internal static class ModDiagnostics { internal static bool On=false; } }

static class BusPairTests
{
    static int checks;
    static void Check(bool b,string s){checks++;if(!b)throw new Exception("CHECK "+checks+" failed: "+s);}
    static void Near(float a,float b,string s){checks++;if(Math.Abs(a-b)>.001f)throw new Exception("CHECK "+checks+" failed: "+s+" ("+a+" vs "+b+")");}
    static void Near3(Vector3 a,Vector3 b,string s){checks++;if(Vector3.Distance(a,b)>.01f)throw new Exception("CHECK "+checks+" failed: "+s+" ("+a+" vs "+b+")");}

    // ---- codec ------------------------------------------------------------
    static BusPairSample Sample(uint seq=1,ulong origin=100,ulong epoch=200,ulong gen=1)
    {
        var s=new BusPairSample{Origin=origin,RelaySession=0,Epoch=epoch,Generation=gen,ID=42,Prefab=7,Sequence=seq,ClockMs=5000+seq*50,Role=6,X=10,Y=1.5,Z=-4};
        s.FrontRotation[0]=0;s.FrontRotation[3]=1;s.RearRotation[0]=0;s.RearRotation[1]=(float)Math.Sin(0.1);s.RearRotation[3]=(float)Math.Cos(0.1);
        s.RearOffset[0]=0;s.RearOffset[1]=-0.5f;s.RearOffset[2]=-4;
        s.FrontVelocity[0]=8;s.FrontAngular[1]=0.4f;s.RearVelocity[0]=8.1f;s.RearAngular[1]=0.39f;
        return s;
    }
    static void Codec()
    {
        var bytes=BusPairProtocol.Encode(Sample());
        Check(bytes!=null&&bytes.Length==BusPairProtocol.Size&&BusPairProtocol.Size==170,"wire size exactly 170");
        Check(bytes[0]==BusPairProtocol.Type&&bytes[0]==250&&bytes[1]==BusPairProtocol.Version,"type250/version header");
        Check(BitConverter.ToUInt64(bytes,BusPairProtocol.OriginOffset)==100,"origin at offset 2");
        Check(BitConverter.ToUInt32(bytes,BusPairProtocol.IDOffset)==42,"id at offset 26");
        BusPairSample d;
        Check(BusPairProtocol.Decode(bytes,out d)&&d.Origin==100&&d.Epoch==200&&d.ID==42&&d.Prefab==7&&d.Sequence==1&&d.ClockMs==5050&&d.Role==6,"roundtrip identity fields");
        Check(d.X==10&&d.Y==1.5&&d.Z==-4,"roundtrip global doubles");
        Near(d.FrontRotation[3],1f,"quat roundtrip");Near(d.RearOffset[2],-4f,"rear offset");Near(d.RearAngular[1],0.39f,"rear angular");
        Check(BitConverter.ToUInt64(bytes,BusPairProtocol.RelaySessionOffset)==0,"upstream relay session zero");
        // malformed framing
        Check(!BusPairProtocol.Decode(null,out d),"null packet");
        Check(!BusPairProtocol.Decode(new byte[169],out d)&&!BusPairProtocol.Decode(new byte[171],out d),"truncated/padded rejected");
        var bad=(byte[])bytes.Clone();bad[0]=249;Check(!BusPairProtocol.Decode(bad,out d),"wrong type");
        bad=(byte[])bytes.Clone();bad[1]=1;Check(!BusPairProtocol.Decode(bad,out d),"old version rejected");
        // field validation
        var s=Sample();s.Origin=0;Check(BusPairProtocol.Encode(s)==null,"origin zero rejected");
        s=Sample();s.Epoch=0;Check(BusPairProtocol.Encode(s)==null,"epoch zero rejected");
        s=Sample();s.ID=0;Check(BusPairProtocol.Encode(s)==null,"id zero rejected");
        s=Sample();s.Sequence=0;Check(BusPairProtocol.Encode(s)==null,"sequence zero rejected");
        s=Sample();s.Prefab=0;Check(BusPairProtocol.Encode(s)==null,"prefab zero rejected");
        s=Sample();s.Role=7;Check(BusPairProtocol.Encode(s)==null,"role out of range rejected");
        s=Sample();s.Role=-1;Check(BusPairProtocol.Encode(s)==null,"negative role rejected");
        s=Sample();s.X=double.NaN;Check(BusPairProtocol.Encode(s)==null,"NaN position rejected");
        s=Sample();s.Y=double.PositiveInfinity;Check(BusPairProtocol.Encode(s)==null,"infinite position rejected");
        s=Sample();s.FrontRotation[3]=2f;Check(BusPairProtocol.Encode(s)==null,"denormalized quat rejected");
        s=Sample();s.FrontRotation[0]=float.NaN;Check(BusPairProtocol.Encode(s)==null,"NaN quat rejected");
        s=Sample();s.RearOffset[0]=200f;Check(BusPairProtocol.Encode(s)==null,"huge rear offset rejected");
        s=Sample();s.FrontVelocity[0]=300f;Check(BusPairProtocol.Encode(s)==null,"huge velocity rejected");
        s=Sample();s.RearAngular[2]=60f;Check(BusPairProtocol.Encode(s)==null,"huge angular rejected");
        // malformed wire bytes with bad payload still fail ValidPacket
        bad=(byte[])bytes.Clone();BitConverter.GetBytes(0UL).CopyTo(bad,BusPairProtocol.OriginOffset);
        Check(!BusPairProtocol.ValidPacket(bad),"tampered origin fails structural packet validation");
        Check(BusPairProtocol.ValidPacket(bytes),"clean packet passes structural validation");
        // relay stamp: only offset 10 changes
        var stamped=(byte[])bytes.Clone();
        Check(BusPairProtocol.StampRelay(stamped,777,3),"relay stamp applied");
        Check(BitConverter.ToUInt64(stamped,BusPairProtocol.RelaySessionOffset)==777&&BitConverter.ToUInt64(stamped,BusPairProtocol.GenerationOffset)==3,"relay session+generation written");
        for(int i=0;i<bytes.Length;i++)if((i>=BusPairProtocol.RelaySessionOffset&&i<BusPairProtocol.RelaySessionOffset+8)||(i>=BusPairProtocol.GenerationOffset&&i<BusPairProtocol.GenerationOffset+8))continue;else Check(stamped[i]==bytes[i],"relay stamp touches only its fields");
        Check(!BusPairProtocol.StampRelay(bytes,0,1)&&!BusPairProtocol.StampRelay(new byte[10],9,1)&&!BusPairProtocol.StampRelay(bytes,7,0),"bad stamp inputs rejected");
        Check(BusPairProtocol.Newer(2,1)&&!BusPairProtocol.Newer(1,1)&&!BusPairProtocol.Newer(1,2),"sequence ordering");
        Check(BusPairProtocol.Newer(0,0xFFFFFFFFUL>0?0xFFFFFFFFu:0u),"sequence wrap");
    }

    // ---- replica ------------------------------------------------------------
    sealed class Bus
    {
        internal tosaveitemscript Item;internal Rigidbody Front,Rear;internal GameObject Back;
    }
    static Bus MakeBus()
    {
        var root=new GameObject("BusFront");
        var item=root.AddComponent<tosaveitemscript>();item.idInSave=42;item.id=7;
        var car=root.AddComponent<carscript>();car.tosave=item;item.car=car;
        var front=root.AddComponent<Rigidbody>();item.RB=front;
        var back=new GameObject("BusBack");var rear=back.AddComponent<Rigidbody>();
        var joint=back.AddComponent<ConfigurableJoint>();joint.connectedBody=front;
        var slot=back.AddComponent<partslotscript>();item.partslotscripts.Add(slot);
        item.doing=tosaveitemscript.carWhatDoing.nothing;
        return new Bus{Item=item,Front=front,Rear=rear,Back=back};
    }
    static BusPairSample Wire(Bus b,uint seq,double x,ulong origin=100,ulong epoch=200,ulong gen=1)
    {
        var s=Sample(seq,origin,epoch,gen);s.X=x;s.Y=0;s.Z=0;
        var inv=Quaternion.Inverse(b.Front.rotation);var off=inv*(b.Rear.position-b.Front.position);
        s.RearOffset[0]=off.x;s.RearOffset[1]=off.y;s.RearOffset[2]=off.z;
        return s;
    }
    static void Feed(Bus b,BusPairSample s,double now){Check(BusPairReplica.Accept(s,b.Item,now,now),"accept expected seq="+s.Sequence);}
    static void Replica()
    {
        BusPairReplica.Clear();mainscript.s=new mainscript();Time.fixedDeltaTime=.05f;
        // canonical structure gates
        var b=MakeBus();b.Item.gameObject.activeInHierarchy=false;
        Check(!BusPairReplica.Accept(Wire(b,1,0),b.Item,1,1),"inactive item rejected");
        b.Item.gameObject.activeInHierarchy=true;
        var bus2=MakeBus();bus2.Item.idInSave=43;
        Check(!BusPairReplica.Accept(Wire(b,1,0),bus2.Item,1,1),"id mismatch rejected");
        bus2.Item.idInSave=42;bus2.Item.id=8;
        Check(!BusPairReplica.Accept(Wire(b,1,0),bus2.Item,1,1),"prefab mismatch rejected");
        var s=Wire(b,1,0);s.Role=4;Check(!BusPairReplica.Accept(s,b.Item,1,1),"non-driving role rejected");
        s=Wire(b,1,0);s.Origin=0;Check(!BusPairReplica.Accept(s,b.Item,1,1),"origin zero rejected");
        s=Wire(b,1,0);s.Generation=0;Check(!BusPairReplica.Accept(s,b.Item,1,1),"unissued generation rejected");
        Check(!BusPairReplica.Accept(Wire(b,1,0),b.Item,double.NaN,1),"NaN mapped time rejected");
        Check(!BusPairReplica.Accept(Wire(b,1,0),b.Item,2.0,1.0),"future mapped time rejected");
        Check(!BusPairReplica.Accept(Wire(b,1,0),b.Item,-1.0,1.0),"stale-clock mapped time rejected");
        b.Item.doing=tosaveitemscript.carWhatDoing.driving;
        Check(!BusPairReplica.Accept(Wire(b,1,0),b.Item,1,1),"local driver rejects stream");
        b.Item.car.lastMultiWhatDoing=tosaveitemscript.carWhatDoing.nothing;b.Item.doing=tosaveitemscript.carWhatDoing.justHost;
        Check(BusPairReplica.Accept(Wire(b,1,0),b.Item,1,1),"host without local charge accepts remote driver");
        BusPairReplica.Clear();b=MakeBus();
        var noJoint=MakeBus();noJoint.Back.GetComponent<ConfigurableJoint>().connectedBody=null;
        Check(!BusPairReplica.Accept(Wire(noJoint,1,0),noJoint.Item,1,1),"missing joint link rejected");
        // activation seeds at current physical pose with a passenger inside the
        // rear carriage; the pair then follows the full authoritative speed
        b.Front.position=new Vector3(0,0,0);b.Rear.position=new Vector3(0,0,-4);
        var pc=new GameObject("pc").AddComponent<BoxCollider>();pc.bounds=new Bounds(new Vector3(0,0,-4),new Vector3(2,2,2));
        mainscript.s.player=new fpscontroller{C=new Collider[]{pc}};
        Feed(b,Wire(b,1,10),1.0);Feed(b,Wire(b,2,10.5),1.05);
        BusPairReplica.FixedTick(true,1.1);
        Check(b.Front.isKinematic&&b.Rear.isKinematic,"both bodies kinematic at seed");
        Check(b.Front.position.x==0f,"seed applies current physical pose, no snap past passenger");
        for(int i=0;i<100;i++){Feed(b,Wire(b,(uint)(3+i),10.5+(i+1)*1.0),1.15+i*.05);BusPairReplica.FixedTick(true,1.2+i*.05);}
        Check(b.Front.position.x>95f&&b.Front.position.x<115f,"20 m/s stream followed at full speed with bounded residual lag");
        Check(BusPairReplica.Owns(b.Item)&&BusPairReplica.OwnsBody(b.Front)&&BusPairReplica.OwnsBody(b.Rear),"owns item and both bodies");
        Check(b.Front.interpolation==RigidbodyInterpolation.Interpolate,"interpolation owned by pair");
        Near3(b.Rear.position,b.Front.position+b.Front.rotation*new Vector3(0,0,-4),"rear follows front-relative offset");
        Check(b.Item.car.lastMultiWhatDoing==tosaveitemscript.carWhatDoing.driving,"remote role reapplied while owned");
        for(int i=100;i<140;i++){Feed(b,Wire(b,(uint)(3+i),10.5+(i+1)*1.75),1.15+i*.05);BusPairReplica.FixedTick(true,1.2+i*.05);}
        Check(b.Front.position.x>230f,"35 m/s stream still followed at full speed");
        mainscript.s.player=null;
        // ordering + generation authority
        Check(!BusPairReplica.Accept(Wire(b,150,0.3),b.Item,1.16,11),"old sequence rejected");
        long h=BusPairReplica.Handoffs;
        Check(BusPairReplica.Accept(Wire(b,1,0,300,400,2),b.Item,11.2,11.2),"newer generation hands off");
        Check(BusPairReplica.Handoffs==h+1,"handoff counted");
        Check(!BusPairReplica.Accept(Wire(b,5,0,300,400,1),b.Item,11.3,11.3),"prior generation rejected");
        Check(!BusPairReplica.Accept(Wire(b,6,0,999,888,2),b.Item,11.35,11.35),"same generation foreign identity rejected");
        Check(BusPairReplica.Accept(Wire(b,2,0,300,400,2),b.Item,11.4,11.4),"same generation same driver resumes");
        Check(BusPairReplica.Accept(Wire(b,1,0,100,200,3),b.Item,11.5,11.5),"A->B->A via new generation accepted");
        // stream loss: kinematic hold while occupied, release when clear
        BusPairReplica.Clear();b=MakeBus();
        Feed(b,Wire(b,1,0),2);Feed(b,Wire(b,2,0.2),2.05);
        BusPairReplica.FixedTick(true,2.1);
        Check(b.Front.isKinematic,"active before loss");
        b.Front.gameObject.AddComponent<BoxCollider>().bounds=new Bounds(new Vector3(0,0,0),new Vector3(4,2,2));
        b.Back.AddComponent<BoxCollider>().bounds=new Bounds(new Vector3(0,0,-4),new Vector3(4,2,2));
        pc.bounds=new Bounds(new Vector3(0,0,0),new Vector3(1,1,1));
        mainscript.s.player=new fpscontroller{C=new Collider[]{pc}};
        BusPairReplica.FixedTick(true,3.5);
        Check(b.Front.isKinematic,"loss holds kinematic while player bounds overlap");
        BusPairReplica.FixedTick(true,4.2);
        Check(b.Front.isKinematic,"occupied pair never drops into independent dynamics");
        pc.bounds=new Bounds(new Vector3(50,0,0),new Vector3(1,1,1));
        BusPairReplica.FixedTick(true,4.3);
        Check(!b.Front.isKinematic&&!b.Rear.isKinematic,"bounded loss releases when clear");
        Near3(b.Front.velocity,Vector3.zero,"loss release never restores stale velocity");
        // first admitted sample already freezes both bodies before the second
        BusPairReplica.Clear();b=MakeBus();mainscript.s.player=null;
        Feed(b,Wire(b,1,0),4);
        BusPairReplica.FixedTick(true,4.1);
        Check(b.Front.isKinematic&&b.Rear.isKinematic,"first sample freezes both bodies at physical pose");
        Check(b.Front.position.x==0f,"frozen pair keeps physical pose until second sample");
        Feed(b,Wire(b,2,0.2),4.15);
        BusPairReplica.FixedTick(true,4.2);
        Check(BusPairReplica.Owns(b.Item),"pair owns after second sample");
        // stale native role timeout must not release an occupied hold
        BusPairReplica.Clear();b=MakeBus();mainscript.s.player=new fpscontroller{C=new Collider[]{pc}};
        b.Front.gameObject.AddComponent<BoxCollider>().bounds=new Bounds(new Vector3(0,0,0),new Vector3(4,2,2));
        b.Back.AddComponent<BoxCollider>().bounds=new Bounds(new Vector3(0,0,-4),new Vector3(4,2,2));
        pc.bounds=new Bounds(new Vector3(0,0,-4),new Vector3(2,2,2));
        Feed(b,Wire(b,1,0),6);Feed(b,Wire(b,2,0.2),6.05);
        BusPairReplica.FixedTick(true,6.1);
        Check(b.Front.isKinematic,"active before stale role timeout");
        b.Item.car.lastMultiWhatDoing=tosaveitemscript.carWhatDoing.justHost;b.Item.car.lastMultiWhatDoingTime=6.1f;
        BusPairReplica.FixedTick(true,7.9);
        Check(b.Front.isKinematic,"stale native role cannot release occupied pair");
        // local driver handoff restores flags + newest current-authority velocity
        BusPairReplica.Clear();b=MakeBus();mainscript.s.player=null;
        var d=Wire(b,1,0);d.FrontVelocity[0]=6;Feed(b,d,5);
        var d2=Wire(b,2,0.2);d2.FrontVelocity[0]=6;Feed(b,d2,5.05);
        BusPairReplica.FixedTick(true,5.1);
        Check(b.Front.isKinematic,"active before local handoff");
        b.Item.doing=tosaveitemscript.carWhatDoing.driving;
        BusPairReplica.FixedTick(true,7.5);
        Check(!b.Front.isKinematic,"local driver handoff releases kinematics");
        Near3(b.Front.velocity,Vector3.zero,"stale stream takeover never injects old velocity");
        // fresh stream takeover restores the newest current-authority velocity
        BusPairReplica.Clear();b=MakeBus();mainscript.s.player=null;
        var f=Wire(b,1,0);f.FrontVelocity[0]=6;Feed(b,f,5);
        var f2=Wire(b,2,0.2);f2.FrontVelocity[0]=6;Feed(b,f2,5.05);
        BusPairReplica.FixedTick(true,5.1);
        Check(b.Front.isKinematic,"active before fresh handoff");
        b.Item.doing=tosaveitemscript.carWhatDoing.driving;
        BusPairReplica.FixedTick(true,5.4);
        Check(!b.Front.isKinematic,"fresh takeover releases kinematics");
        Near(b.Front.velocity.x,6f,"fresh takeover restores newest current-authority velocity");
        // Forget revokes immediately
        BusPairReplica.Clear();b=MakeBus();b.Item.doing=tosaveitemscript.carWhatDoing.nothing;
        Feed(b,Wire(b,1,0),8);Feed(b,Wire(b,2,0.2),8.05);
        BusPairReplica.FixedTick(true,8.1);
        Check(b.Front.isKinematic,"active before revoke");
        BusPairReplica.Forget(42);
        Check(!b.Front.isKinematic&&!BusPairReplica.Owns(b.Item),"Forget revokes replica immediately");
        // world recenter: global timeline unaffected by unity offset shift
        BusPairReplica.Clear();b=MakeBus();mainscript.offset=new Vector3d(500,0,0);
        Feed(b,Wire(b,1,500),9);Feed(b,Wire(b,2,500.2),9.05);
        BusPairReplica.FixedTick(true,9.1);
        Check(b.Front.position.x<1f,"recenter maps global stream to current unity origin");
        mainscript.offset=new Vector3d(0,0,0);
        BusPairReplica.Clear();
        Check(!b.Front.isKinematic&&b.Front.interpolation==RigidbodyInterpolation.None,"clear restores native flags");
    }
    static void SeatsAndCapture()
    {
        BusPairReplica.Clear();BusSeatClaims.Clear();
        var b=MakeBus();
        var seat=new GameObject("DriverSeat").AddComponent<seatscript>();
        seat.driverSeat0=true;seat.Car=b.Item.car;seat.parentObj=b.Item.transform;
        var passenger=new GameObject("PassengerSeat").AddComponent<seatscript>();
        passenger.Car=b.Item.car;passenger.parentObj=b.Item.transform;
        b.Item.seats=new[]{seat,passenger};
        syncScript.s=new syncScript();
        var go=new GameObject("mp");
        var p=new syncScript.playerSyncClass{steamID=100,player=go,sitting=true,sitObjParent=b.Item.transform};
        syncScript.s.players[new CSteamID(100)]=p;
        Check(!BusSeatClaims.DriverOf(100,b.Item,200,1.0),"no claim yet rejected");
        BusSeatClaims.Observe(p,true,false,42,0,200,1.0);
        Check(BusSeatClaims.DriverOf(100,b.Item,200,1.1),"canonical driver-seat claim accepted");
        Check(!BusSeatClaims.DriverOf(101,b.Item,200,1.1),"other origin not bound");
        Check(!BusSeatClaims.DriverOf(100,b.Item,201,1.1),"claim bound to its session only");
        Check(!BusSeatClaims.DriverOf(100,b.Item,200,4.0),"stale claim expired");
        Check(!BusSeatClaims.DriverOf(100,b.Item,200,double.NaN),"NaN age rejected");
        Check(!BusSeatClaims.DriverOf(100,b.Item,200,0.5),"negative age rejected");
        BusSeatClaims.Observe(p,true,false,42,1,200,1.2);
        Check(!BusSeatClaims.DriverOf(100,b.Item,200,1.3),"passenger seat on same parent is not a driver");
        BusSeatClaims.Observe(p,true,true,42,0,200,1.4);
        Check(!BusSeatClaims.DriverOf(100,b.Item,200,1.5),"poi sit clears vehicle claim");
        BusSeatClaims.Observe(p,true,false,42,0,200,1.6);
        p.sitting=false;
        Check(!BusSeatClaims.DriverOf(100,b.Item,200,1.7),"native unsit blocks claim");
        p.sitting=true;
        BusSeatClaims.Observe(p,true,false,42,0,300,1.8);
        Check(!BusSeatClaims.DriverOf(100,b.Item,200,1.9),"reconnect new session invalidates old-session claim");
        Check(BusSeatClaims.DriverOf(100,b.Item,300,1.9),"reconnect claim valid under new session");
        BusSeatClaims.ClearPeer(100);
        Check(!BusSeatClaims.DriverOf(100,b.Item,300,2.0),"peer disconnect clears claim");
        // capture only from the actual local driver, exact same-step pose
        BusPairReplica.Clear();b=MakeBus();
        Check(BusPairReplica.Capture(b.Item,1,1,1,1)==null,"non-driver cannot capture");
        b.Item.doing=tosaveitemscript.carWhatDoing.driving;b.Item.car.lastMultiWhatDoing=tosaveitemscript.carWhatDoing.nothing;
        b.Front.position=new Vector3(3,0.5f,2);b.Rear.position=new Vector3(3,0,-2);
        var c=BusPairReplica.Capture(b.Item,100,200,9,4000);
        Check(c!=null&&c.Origin==100&&c.Epoch==200&&c.RelaySession==0&&c.Generation==0&&c.ID==42&&c.Prefab==7&&c.Sequence==9&&c.Role==6,"capture identity fields");
        Check(c.X==3&&c.Y==0.5&&c.Z==2,"capture global position");
        Near(c.RearOffset[2],-4f,"capture rear front-relative offset");
        b.Front.isKinematic=true;
        Check(BusPairReplica.Capture(b.Item,1,1,2,1)==null,"kinematic front never captured");
        b.Front.isKinematic=false;b.Rear.isKinematic=true;
        Check(BusPairReplica.Capture(b.Item,1,1,2,1)==null,"kinematic rear never captured");
    }
    static int Main()
    {
        try{Codec();Replica();SeatsAndCapture();}
        catch(Exception e){Console.Error.WriteLine("FAIL: "+e.Message);return 1;}
        Console.WriteLine("PASS "+checks+" bus pair checks");return 0;
    }
}
