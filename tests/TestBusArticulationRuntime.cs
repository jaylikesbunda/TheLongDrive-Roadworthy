using System;
using System.Collections.Generic;
using System.Reflection;
using TldMp;
using UnityEngine;
namespace UnityEngine
{
    public class Object {}
    public class Component : Object {
        public GameObject gameObject; public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T:Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T:Component { Transform t=transform; while(t!=null){T c=t.gameObject.GetComponent<T>(); if(c!=null)return c; t=t.parent;} return null; }
    }
    public class MonoBehaviour : Component { public bool isActiveAndEnabled=true; }
    public class GameObject : Object {
        readonly Dictionary<Type,Component> components=new Dictionary<Type,Component>(); public Transform transform;
        public GameObject(string n) { transform=new Transform(); transform.gameObject=this; transform.name=n; components[typeof(Transform)]=transform; }
        public T AddComponent<T>() where T:Component,new() { T c=new T(); c.gameObject=this; components[typeof(T)]=c; return c; }
        public T GetComponent<T>() where T:Component { Component c; return components.TryGetValue(typeof(T),out c)?(T)c:null; }
        public void Remove<T>() where T:Component { components.Remove(typeof(T)); }
    }
    public class Transform : Component {
        public string name; public Transform parent; readonly List<Transform> children=new List<Transform>(); public Vector3 position; public Quaternion rotation=Quaternion.identity;
        public void Add(Transform c){c.parent=this;children.Add(c);} public Transform Find(string n){return children.Find(t=>t.name==n);}
        public Vector3 TransformPoint(Vector3 p){return position+rotation*p;}
    }
    public struct Vector3 {
        public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public float magnitude {get{return (float)Math.Sqrt(x*x+y*y+z*z);}} public static float Distance(Vector3 a,Vector3 b){return (a-b).magnitude;}
    }
    public struct Quaternion {
        public float x,y,z,w; public Quaternion(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}
        public static Quaternion identity {get{return new Quaternion(0,0,0,1);}} public static Quaternion Inverse(Quaternion q){return new Quaternion(-q.x,-q.y,-q.z,q.w);}
        public static Vector3 operator*(Quaternion q,Vector3 v){float xx=q.x*2, yy=q.y*2, zz=q.z*2;return new Vector3((1-q.y*yy-q.z*zz)*v.x+(q.x*yy-q.w*zz)*v.y+(q.x*zz+q.w*yy)*v.z,(q.x*yy+q.w*zz)*v.x+(1-q.x*xx-q.z*zz)*v.y+(q.y*zz-q.w*xx)*v.z,(q.x*zz-q.w*yy)*v.x+(q.y*zz+q.w*xx)*v.y+(1-q.x*xx-q.y*yy)*v.z);}
    }
    public enum RigidbodyInterpolation {None,Interpolate,Extrapolate}
    public class Rigidbody : Component {public RigidbodyInterpolation interpolation; public Vector3 position,angularVelocity; public Quaternion rotation=Quaternion.identity;}
    public class ConfigurableJoint : Component {public Rigidbody connectedBody; public Vector3 anchor,connectedAnchor;}
    public class BoxCollider : Component {public Vector3 size;}
    public static class Mathf {public static float Max(float x,float y){return Math.Max(x,y);} public static float Abs(float x){return Math.Abs(x);}}
    public static class Time {public static float realtimeSinceStartup;}
    public static class Debug {public static void Log(object x) {}}
}
public class carscript : MonoBehaviour {}
public class dinamiccolliderforbus : MonoBehaviour { public class col {public BoxCollider collider; public Transform t1,t2;} public List<col> cols=new List<col>(); }
public class tosaveitemscript : MonoBehaviour { public List<partslotscript> partslotscripts=new List<partslotscript>(); }
public class partslotscript : MonoBehaviour { public tosaveitemscript tosaveitem; }
public class attachablescript : MonoBehaviour { public bool attached; public Transform point; public partslotscript slot; }
class TestBusArticulationRuntime
{
    static void Check(bool b,string s){if(!b)throw new Exception(s);}
    static GameObject Child(GameObject p,string n){var g=new GameObject(n);p.transform.Add(g.transform);return g;}
    static void Tick(BusArticulation d){typeof(BusArticulation).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(d,null);}
    static void Main(){
        var root=new GameObject("Bus01(Clone)");var front=Child(root,"BusFront");var back=Child(root,"BusBack");
        var car=front.AddComponent<carscript>(); var f=front.AddComponent<Rigidbody>(); var b=back.AddComponent<Rigidbody>();
        var joint=back.AddComponent<ConfigurableJoint>();
        var owner=front.AddComponent<tosaveitemscript>();var rearSlot=Child(back,"RRWheelSlotScript").AddComponent<partslotscript>();rearSlot.tosaveitem=owner;
        var left=Child(back,"DinamicColliderLeft");var col=left.AddComponent<BoxCollider>(); col.size=new Vector3(1,1,5); var script=left.AddComponent<dinamiccolliderforbus>();
        var frontConnector=Child(front,"DinamicColliderLeft (1)").AddComponent<dinamiccolliderforbus>();
        var t1=Child(front,"t1"); var t2=Child(back,"t2");t1.transform.position=new Vector3(100,0,0);t2.transform.position=new Vector3(104,0,0);
        front.transform.position=new Vector3(100,0,0);back.transform.position=new Vector3(104,0,0);f.position=new Vector3(0,0,0);b.position=new Vector3(2,0,0);
        script.cols.Add(new dinamiccolliderforbus.col{collider=col,t1=t1.transform,t2=t2.transform});
        BusArticulation.Install(car);var driver=front.GetComponent<BusArticulation>();Check(driver!=null,"driver installation");
        front.transform.parent=null;back.transform.parent=null;
        Tick(driver);Check(!BusArticulation.IsManagedCollider(script) && f.interpolation==RigidbodyInterpolation.None,"missing slots and joint preserve native behavior");
        owner.partslotscripts.Add(rearSlot);Tick(driver);Check(!BusArticulation.IsManagedCollider(script),"missing connected joint preserves native behavior");
        joint.connectedBody=f;
        var part=new GameObject("rim").AddComponent<attachablescript>();part.attached=true;part.point=rearSlot.transform;part.slot=rearSlot;
        tosaveitemscript found;Check(BusArticulation.TryAttachedOwner(part,out found) && found==owner,"rear slot belongs to front after unparenting");
        part.attached=false;Check(!BusArticulation.TryAttachedOwner(part,out found),"detached part must not acquire owner");part.attached=true;
        owner.partslotscripts.Clear();Check(!BusArticulation.TryAttachedOwner(part,out found),"owner must list slot");owner.partslotscripts.Add(rearSlot);
        Check(!BusArticulation.IsManagedCollider(script),"fallback before validation");Tick(driver);
        Check(BusArticulation.IsManagedCollider(script),"connector acquired");Check(f.interpolation==RigidbodyInterpolation.Interpolate && b.interpolation==RigidbodyInterpolation.Interpolate,"both bodies interpolate");
        Check(BusArticulation.IsManagedCollider(frontConnector),"front connector acquired");
        Check(Math.Abs(col.size.z-2)<0.00001,"uses physics poses, not rendered poses");
        var other=Child(back,"Door").AddComponent<dinamiccolliderforbus>();Check(!BusArticulation.IsManagedCollider(other),"unrelated script fallback");
        back.Remove<ConfigurableJoint>();Tick(driver);Check(!BusArticulation.IsManagedCollider(script),"fallback when joint missing");
        Check(BusArticulation.TryAttachedOwner(part,out found) && found==owner,"previously verified frozen rear retains owner");
        back.Remove<Rigidbody>();b=back.AddComponent<Rigidbody>();b.position=new Vector3(3,0,0);joint=back.AddComponent<ConfigurableJoint>();joint.connectedBody=f;Tick(driver);
        Check(b.interpolation==RigidbodyInterpolation.Interpolate,"recreated body");Check(Math.Abs(col.size.z-3)<0.00001,"recreated body used for collider");
        joint.connectedBody=null;Tick(driver);Check(!BusArticulation.IsManagedCollider(script),"wrong connection fallback");
        Check(!BusArticulation.TryAttachedOwner(part,out found),"wrong live connection is not an owner");
        var newBack=new GameObject("BusBack");var newBody=newBack.AddComponent<Rigidbody>();var newJoint=newBack.AddComponent<ConfigurableJoint>();newJoint.connectedBody=f;
        var newSlot=Child(newBack,"RRWheelSlotScript").AddComponent<partslotscript>();newSlot.tosaveitem=owner;owner.partslotscripts.Clear();owner.partslotscripts.Add(newSlot);
        var newScript=Child(newBack,"DinamicColliderLeft").AddComponent<dinamiccolliderforbus>();newScript.cols.Add(new dinamiccolliderforbus.col{t1=t1.transform,t2=newBack.transform});
        Tick(driver);Check(newBody.interpolation==RigidbodyInterpolation.Interpolate && BusArticulation.IsManagedCollider(newScript),"recreated rear transform is discovered");
        Check(!BusArticulation.IsManagedCollider(script),"old rear connector no longer managed");
        var otherRoot=new GameObject("Bus02");var otherFront=Child(otherRoot,"BusFront");var otherCar=otherFront.AddComponent<carscript>();Child(otherRoot,"BusBack");BusArticulation.Install(otherCar);var otherDriver=otherFront.GetComponent<BusArticulation>();Tick(otherDriver);Check(!BusArticulation.IsManagedCollider(script),"unverified candidate never manages colliders");
        Console.WriteLine("PASS bus runtime lifecycle and physical pose");
    }
}
