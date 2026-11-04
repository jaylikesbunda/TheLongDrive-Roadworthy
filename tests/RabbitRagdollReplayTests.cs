using System;
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
    }
    public class Component:Object
    {
        internal GameObject go;
        public GameObject gameObject{get{return go;}set{go=value;}}
        public Transform transform{get{return gameObject==null?null:gameObject.transform;}}
        public T GetComponent<T>(){return gameObject==null?default(T):gameObject.GetComponent<T>();}
        public T[] GetComponents<T>(){return gameObject==null?new T[0]:gameObject.GetComponents<T>();}
        public T GetComponentInParent<T>(){if(gameObject==null)return default(T);var c=gameObject.GetComponent<T>();if(c!=null)return c;var p=transform.parent;return p==null?default(T):p.GetComponentInParent<T>();}
        public T[] GetComponentsInChildren<T>(bool inactive){var r=new List<T>();Collect(transform,r);return r.ToArray();}
        static void Collect<T>(Transform t,List<T> r){foreach(var c in t.gameObject.components)if(c is T)r.Add((T)(object)c);for(int i=0;i<t.childCount;i++)Collect<T>(t.GetChild(i),r);}
    }
    public class Behaviour:Component{public bool enabled=true;public bool isActiveAndEnabled{get{return enabled&&gameObject!=null;}}}
    public class MonoBehaviour:Behaviour
    {
        public Coroutine StartCoroutine(System.Collections.IEnumerator r){return null;}
        public void StopAllCoroutines(){}
    }
    public sealed class Coroutine{}
    public class GameObject:Object
    {
        Transform t;
        public Transform transform{get{return t;}}
        internal readonly List<Component> components=new List<Component>();
        public GameObject(){t=new Transform();t.gameObject=this;components.Add(t);}
        public T GetComponent<T>(){foreach(var c in components)if(c is T)return (T)(object)c;return default(T);}
        public T AddComponent<T>() where T:Component,new(){var c=new T();c.gameObject=this;components.Add(c);return c;}
        public T[] GetComponents<T>(){var r=new List<T>();foreach(var c in components)if(c is T)r.Add((T)(object)c);return r.ToArray();}
    }
    public class Transform:Component
    {
        public Transform parent;
        public readonly List<Transform> children=new List<Transform>();
        public int childCount{get{return children.Count;}}
        public Transform GetChild(int i){return children[i];}
    }
    public class Collider:Component{public bool enabled=true;public bool isTrigger;}
    public class Animator:Behaviour{}
    public class Joint:Component{}
    public class Rigidbody:Component
    {
        public bool isKinematic,useGravity=true,detectCollisions=true;public int velocityWrites,angularWrites;
        Vector3 vel,ang;
        public Vector3 velocity{get{return vel;}set{velocityWrites++;vel=value;}}
        public Vector3 angularVelocity{get{return ang;}set{angularWrites++;ang=value;}}
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero{get{return new Vector3();}}
        public static bool operator ==(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        public static bool operator !=(Vector3 a,Vector3 b){return !(a==b);}
        public override bool Equals(object o){return o is Vector3&&(Vector3)o==this;}
        public override int GetHashCode(){return 0;}
    }
    public struct Quaternion{public Vector3 euler;public static Quaternion Euler(Vector3 v){var q=new Quaternion();q.euler=v;return q;}}
    public static class Mathf{public static float InverseLerp(float a,float b,float v){return a==b?0f:(v-a)/(b-a);}}
}

namespace GeneszSteamP2PBase
{
    public static class DataByteConverterScript
    {
        public static void Receive<T0,T1,T2,T3,T4>(byte[] bytes,out T0 v0,out T1 v1,out T2 v2,out T3 v3,out T4 v4)
        {
            v0=(T0)(object)BitConverter.ToUInt32(bytes,1);
            int p=5;
            v1=(T1)(object)ReadVecs(bytes,ref p);v2=(T2)(object)ReadVecs(bytes,ref p);v3=(T3)(object)ReadVecs(bytes,ref p);v4=(T4)(object)ReadVecs(bytes,ref p);
        }
        static Vector3[] ReadVecs(byte[] b,ref int p)
        {
            int n=BitConverter.ToInt32(b,p);p+=4;var r=new Vector3[n];
            for(int i=0;i<n;i++){r[i]=new Vector3(BitConverter.ToSingle(b,p),BitConverter.ToSingle(b,p+4),BitConverter.ToSingle(b,p+8));p+=12;}
            return r;
        }
    }
}

public class savedatascript:MonoBehaviour
{
    public static savedatascript s=new savedatascript();
    public Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();
}
public class tosaveitemscript:MonoBehaviour{public int id;public ragdollchildpos ragdollPos;public Transform useThisForPos;public nyulscript nyul;}
public class ragdollchildpos:MonoBehaviour{public Transform[] childs;}
public class stimul{}
public class aiscript:MonoBehaviour{public bool alive=true,fleeing;public sensoryscript senses;public nyulscript nyul;public void Die(bool s){}public void Update(){}}
public class sensoryscript:MonoBehaviour{public bool alive=true;public List<stimul> stimuls=new List<stimul>();public void Scan(){}}
public class movecreaturescript:MonoBehaviour{public Vector3 moveDir;public float moveRot;public void Rot(){}}
public class ragdollactivatescript:MonoBehaviour{public Rigidbody RB;public Rigidbody[] RBs=new Rigidbody[0];public Collider[] ragdollColliders=new Collider[0],animColliders=new Collider[0];public Transform[] unparent=new Transform[0];public Transform animColliderTransform;public Animator anim;}
public class pickupable:MonoBehaviour{}
public class jointHelpScript:MonoBehaviour{}
public class playerdeathtriggerscript:MonoBehaviour{public Collider selfCol;}
public class nyulscript:MonoBehaviour{public ragdollactivatescript ragdoll;public aiscript AI;public sensoryscript senses;public playerdeathtriggerscript attackTriggerScript;public GameObject playerDeathTrigger;}
public class mainscript:MonoBehaviour
{
    public sealed class Pose{public Transform child,parent;public Vector3 pos;public Quaternion rot;}
    public static int poseCalls;
    public static readonly List<Pose> poses=new List<Pose>();
    public static void SetFromLocalPosRot(Transform child,Transform parent,Vector3 pos,Quaternion rot){poseCalls++;poses.Add(new Pose{child=child,parent=parent,pos=pos,rot=rot});}
}
public class syncScript:MonoBehaviour
{
    public static bool isHost;
    public static bool IsHost(){return isHost;}
    public int asked;public uint askedId;
    public void SendAskItemData(uint id){asked++;askedId=id;}
    public void RecRagdoll(byte[] bytes){}
}

namespace TldMp
{
    public sealed class StableNode{public ulong Host;}
    public static class Loopback{public static bool IsHost;public static ulong Own;}
    public static partial class StableGameBridge
    {
        public static bool BusRagdollAllowed(byte[] bytes){return true;}
        internal static bool Connected{get{return true;}}
        internal static bool loopbackMode;
        internal static StableNode node=new StableNode();
        internal static ulong Own{get{return 0;}}
        internal static bool Find(uint id,out tosaveitemscript item){return savedatascript.s.items.TryGetValue(id,out item);}
        public static void ConfigureRabbitImpact(ragdollactivatescript ragdoll){}
    }
}

public static class RabbitRagdollReplayTests
{
    static int checks;static bool candidate;
    static void Check(bool v,string label){checks++;if(!v)throw new Exception("check "+checks+" failed: "+label);}
    static byte[] Write43(uint id,Vector3[][] arrays)
    {
        int size=5;foreach(var a in arrays)size+=4+a.Length*12;
        var b=new byte[size];b[0]=43;var w=1;
        Buffer.BlockCopy(BitConverter.GetBytes(id),0,b,w,4);w+=4;
        foreach(var a in arrays)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(a.Length),0,b,w,4);w+=4;
            foreach(var v in a){Buffer.BlockCopy(BitConverter.GetBytes(v.x),0,b,w,4);w+=4;Buffer.BlockCopy(BitConverter.GetBytes(v.y),0,b,w,4);w+=4;Buffer.BlockCopy(BitConverter.GetBytes(v.z),0,b,w,4);w+=4;}
        }
        return b;
    }
    static GameObject Child(bool kinematic,out Rigidbody rb)
    {
        var go=new GameObject();rb=go.AddComponent<Rigidbody>();rb.isKinematic=kinematic;return go;
    }
    public static int Main(string[] args)
    {
        try
        {
            candidate=args.Length>0&&args[0]=="candidate";
            var sync=new GameObject().AddComponent<syncScript>();
            var itemGo=new GameObject();var item=itemGo.AddComponent<tosaveitemscript>();item.id=7;
            item.ragdollPos=itemGo.AddComponent<ragdollchildpos>();item.useThisForPos=itemGo.transform;
            Rigidbody kin,dyn;
            var c1=Child(true,out kin).transform;var c2=Child(false,out dyn).transform;
            item.ragdollPos.childs=new[]{c1,c2};
            savedatascript.s=new savedatascript();savedatascript.s.items[7]=item;
            Vector3 v1=new Vector3(1,0,0),v2=new Vector3(0,2,0),v3=new Vector3(0,0,3),v4=new Vector3(4,0,0);
            Vector3 v5=new Vector3(5,0,0),v6=new Vector3(0,6,0),v7=new Vector3(0,0,7),v8=new Vector3(8,0,0);
            sync.RecRagdoll(Write43(7,new[]{
                new[]{v1,v2},new[]{v3,v4},new[]{v5,v6},new[]{v7,v8}}));
            Check(mainscript.poseCalls==2,"native pose application per child unchanged");
            Check(mainscript.poses.Count==2&&mainscript.poses[0].child==c1&&mainscript.poses[1].child==c2,"pose child order preserved");
            Check(mainscript.poses[0].parent==item.useThisForPos&&mainscript.poses[1].parent==item.useThisForPos,"pose parent preserved");
            Check(mainscript.poses[0].pos==v1&&mainscript.poses[1].pos==v2,"per-child positions exact");
            Check(mainscript.poses[0].rot.euler==v3&&mainscript.poses[1].rot.euler==v4,"per-child rotations exact");
            if(candidate){Check(kin.velocityWrites==0&&kin.angularWrites==0,"kinematic body skips velocity writes");}
            else{Check(kin.velocityWrites==1&&kin.angularWrites==1,"native writes a kinematic body");}
            Check(dyn.velocityWrites==1&&dyn.velocity==v6,"dynamic velocity parity");
            Check(dyn.angularWrites==1&&dyn.angularVelocity==v8,"dynamic angular velocity parity");
            kin.isKinematic=false;dyn.isKinematic=true;
            Vector3 w1=new Vector3(9,0,0),w2=new Vector3(0,9,0),w3=new Vector3(0,0,9),w4=new Vector3(9,9,0);
            sync.RecRagdoll(Write43(7,new[]{
                new[]{v1,v2},new[]{v3,v4},new[]{w1,w2},new[]{w3,w4}}));
            Check(kin.velocity==w1&&kin.angularVelocity==w3,"kinematic-to-dynamic applies the newest velocities");
            Check(kin.velocityWrites==(candidate?1:2)&&kin.angularWrites==(candidate?1:2),"kinematic write count matches the sender mode");
            if(candidate){Check(dyn.velocityWrites==1&&dyn.angularWrites==1&&dyn.velocity==v6,"dynamic-to-kinematic keeps last dynamic velocity, no stale motion");}
            else{Check(dyn.velocityWrites==2,"native writes the newly kinematic body again");}
            mainscript.poseCalls=0;sync.asked=0;
            sync.RecRagdoll(Write43(9,new[]{
                new[]{v1},new[]{v1},new[]{v1},new[]{v1}}));
            Check(sync.asked==1&&sync.askedId==9,"unknown item keeps the native ask");
            var plain=new GameObject().AddComponent<tosaveitemscript>();plain.id=11;savedatascript.s.items[11]=plain;
            sync.RecRagdoll(Write43(11,new[]{
                new[]{v1},new[]{v1},new[]{v1},new[]{v1}}));
            Check(mainscript.poseCalls==0,"missing ragdollPos stays a native no-op");
            bool threw=false;try{TldMp.RagdollSafety.SetVelocity(null,v1);}catch(NullReferenceException){threw=true;}
            Check(threw,"null receiver still throws the native null exception");
            threw=false;try{TldMp.RagdollSafety.SetAngularVelocity(null,v1);}catch(NullReferenceException){threw=true;}
            Check(threw,"null angular receiver still throws the native null exception");
            Console.WriteLine("PASS "+checks+" rabbit ragdoll receiver replay checks ("+(candidate?"candidate":"baseline")+"; no PhysX/car launch validation)");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
