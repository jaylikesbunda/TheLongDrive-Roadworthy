using System;
using System.Collections.Generic;
using System.Linq;
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
        Transform t;public Transform transform{get{if(t==null){t=new Transform();t.gameObject=this;components.Add(t);}return t;}}
        internal readonly List<Component> components=new List<Component>();
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
    public class Collider:Component{public bool enabled=true;}
    public class Animator:Behaviour{}
    public struct Vector3{public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero{get{return new Vector3();}}public static bool operator ==(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}public static bool operator !=(Vector3 a,Vector3 b){return !(a==b);}public override bool Equals(object o){return o is Vector3&&(Vector3)o==this;}public override int GetHashCode(){return 0;}}
    public class Rigidbody:Component{public bool isKinematic,useGravity=true,detectCollisions=true;public Vector3 velocity{get;set;}public Vector3 angularVelocity{get;set;}}
    public static class Mathf{public static float InverseLerp(float a,float b,float v){return a==b?0f:(v-a)/(b-a);}}

}
public class tosaveitemscript:MonoBehaviour{public int id;public nyulscript nyul;public ragdollchildpos ragdollPos;}
public class stimul{}
public class aiscript:MonoBehaviour{public bool alive=true,fleeing;public sensoryscript senses;public nyulscript nyul;public void Die(bool s){}public void Update(){}}
public class sensoryscript:MonoBehaviour{public bool alive=true;public List<stimul> stimuls=new List<stimul>();public void Scan(){}}
public class movecreaturescript:MonoBehaviour{public Vector3 moveDir;public float moveRot;public void Rot(){}}
public class ragdollactivatescript:MonoBehaviour{public Rigidbody RB;public Rigidbody[] RBs=new Rigidbody[0];public Collider[] ragdollColliders=new Collider[0],animColliders=new Collider[0];public Transform[] unparent=new Transform[0];public Transform animColliderTransform;public Animator anim;}
public class pickupable:MonoBehaviour{}
public class jointHelpScript:MonoBehaviour{}
public class playerdeathtriggerscript:MonoBehaviour{public Collider selfCol;}
public class syncScript:MonoBehaviour{public static bool isHost;public static bool IsHost(){return isHost;}}
public class nyulscript:MonoBehaviour{public ragdollactivatescript ragdoll;public aiscript AI;public sensoryscript senses;public playerdeathtriggerscript attackTriggerScript;public GameObject playerDeathTrigger;}
public class ragdollchildpos:MonoBehaviour{public Transform[] childs;}

namespace TldMp
{
    public sealed class StableNode{public ulong Host;}
    public static class Loopback{public static bool IsHost;public static ulong Own;}
    public static partial class StableGameBridge
    {
        internal static bool connected=true;
        internal static bool Connected{get{return connected;}}
        internal static bool loopbackMode;
        internal static StableNode node=new StableNode();
        internal static ulong own=20;
        internal static ulong Own{get{return own;}}
        internal static readonly Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();
        internal static bool Find(uint id,out tosaveitemscript item){return items.TryGetValue(id,out item);}
        public static void ConfigureRabbitImpact(ragdollactivatescript ragdoll){}
    }
}

public static class RabbitNetworkGuardTests
{
    static int checks;
    static void Check(bool v,string label){checks++;if(!v)throw new Exception("check "+checks+" failed: "+label);}
    static byte[] State(byte type,uint id)
    {
        int at=type==18?10:type==29?9:1;
        var b=new byte[at+4];b[0]=type;Buffer.BlockCopy(BitConverter.GetBytes(id),0,b,at,4);return b;
    }
    static tosaveitemscript Rabbit(uint id)
    {
        var item=new tosaveitemscript{id=(int)id,nyul=new nyulscript()};
        TldMp.StableGameBridge.items[id]=item;return item;
    }
    static tosaveitemscript Corpse(uint id)
    {
        var item=new tosaveitemscript{id=(int)id,ragdollPos=new ragdollchildpos()};
        var go=new GameObject();go.AddComponent<nyulscript>();item.gameObject=go;
        TldMp.StableGameBridge.items[id]=item;return item;
    }
    static tosaveitemscript Normal(uint id)
    {
        var item=new tosaveitemscript{id=(int)id};TldMp.StableGameBridge.items[id]=item;return item;
    }
    static int Scenarios()
    {
        const ulong host=7,guest=20;
        TldMp.StableGameBridge.connected=true;TldMp.StableGameBridge.node.Host=host;
        Rabbit(1);Corpse(2);Normal(3);
        Check(TldMp.StableGameBridge.AllowRabbitState(host,State(43,1)),"host emits accepted rabbit state");
        Check(!TldMp.StableGameBridge.AllowRabbitState(guest,State(43,1)),"guest rabbit state rejected");
        Check(!TldMp.StableGameBridge.AllowRabbitState(guest,State(43,2)),"guest corpse-rabbit ragdoll write rejected");
        Check(TldMp.StableGameBridge.AllowRabbitState(guest,State(43,3)),"nonrabbit state stays native");
        Check(TldMp.StableGameBridge.AllowRabbitState(guest,State(43,99)),"unknown item stays native");
        Check(TldMp.StableGameBridge.AllowRabbitState(guest,State(4,1)),"unprotected packet stays native");
        Check(TldMp.StableGameBridge.AllowRabbitState(guest,null),"null packet stays native");
        Check(TldMp.StableGameBridge.AllowRabbitState(guest,new byte[0]),"empty packet stays native");
        TldMp.StableGameBridge.connected=false;
        Check(TldMp.StableGameBridge.AllowRabbitState(guest,State(43,1)),"disconnected stays native");
        TldMp.StableGameBridge.connected=true;
        foreach(byte t in new byte[]{16,17,18,19,29,33,36,42,43})
            Check(!TldMp.StableGameBridge.AllowRabbitState(guest,State(t,1)),"protected type "+t+" rejected from guest");
        foreach(byte t in new byte[]{3,4,8,9,10,11,14,21,25,30,34,37,40,41,45})
            Check(TldMp.StableGameBridge.AllowRabbitState(guest,State(t,1)),"unprotected type "+t+" stays native");
        Check(!TldMp.RabbitNetworkPolicy.AllowState(true,true,43,0,host),"zero origin rejected");
        Check(!TldMp.RabbitNetworkPolicy.AllowState(true,true,43,guest,0),"zero host rejected");
        Check(TldMp.RabbitNetworkPolicy.AllowState(false,true,43,guest,host),"policy passes disconnected");
        Check(TldMp.RabbitNetworkPolicy.AllowState(true,false,43,guest,host),"policy passes nonrabbit");
        TldMp.StableGameBridge.node.Host=guest;
        Check(TldMp.StableGameBridge.AllowRabbitState(guest,State(43,1)),"promoted host emits after host change");
        TldMp.StableGameBridge.node.Host=host;
        Check(!TldMp.StableGameBridge.AllowRabbitState(guest,State(43,1)),"demoted member rejected after host change");
        TldMp.StableGameBridge.node=null;
        Check(TldMp.StableGameBridge.AllowRabbitState(guest,State(43,1)),"null node stays native");
        TldMp.StableGameBridge.node=new TldMp.StableNode{Host=host};
        foreach(byte t in new byte[]{16,17,18,19,29,33,36,42,43})
            Check(TldMp.RabbitNetworkPolicy.IsHostStateType(t),"policy protects type "+t);
        foreach(byte t in new byte[]{3,4,8,9,10,11,14,21,25,30,34,37,40,41,44,45,46,47,48})
            Check(!TldMp.RabbitNetworkPolicy.IsHostStateType(t),"policy ignores type "+t);
        Console.WriteLine("PASS "+checks+" rabbit network policy and host-state admission checks");
        return 0;
    }
    static int[] Calls(Mono.Cecil.MethodDefinition m,string name)
    {
        return m.Body.Instructions.Where(i=>i.Operand is Mono.Cecil.MethodReference && ((Mono.Cecil.MethodReference)i.Operand).Name==name && ((Mono.Cecil.MethodReference)i.Operand).DeclaringType.FullName=="TldMp.StableGameBridge").Select(i=>m.Body.Instructions.IndexOf(i)).ToArray();
    }
    static int FirstCallTo(Mono.Cecil.MethodDefinition m,params string[] names)
    {
        for(int i=0;i<m.Body.Instructions.Count;i++)
        {
            var mr=m.Body.Instructions[i].Operand as Mono.Cecil.MethodReference;
            if(mr!=null && names.Contains(mr.Name))return i;
        }
        return -1;
    }
    static Mono.Cecil.MethodDefinition Method(Mono.Cecil.TypeDefinition t,string n,int p){return t.Methods.Single(x=>x.Name==n&&x.Parameters.Count==p);}
    static int Index(Mono.Cecil.MethodDefinition m,Mono.Cecil.Cil.Instruction i){return m.Body.Instructions.IndexOf(i);}
    static bool BrTrue(Mono.Cecil.Cil.Instruction i){return i.OpCode==Mono.Cecil.Cil.OpCodes.Brtrue || i.OpCode==Mono.Cecil.Cil.OpCodes.Brtrue_S;}
    static void VoidReject(Mono.Cecil.MethodDefinition m,int site,string label)
    {
        var ins=m.Body.Instructions;
        Check(site+2<ins.Count&&BrTrue(ins[site+1]),label+" guard result feeds a conditional branch");
        Check(ins[site+2].OpCode==Mono.Cecil.Cil.OpCodes.Ret,label+" rejected fallthrough returns immediately");
        Check(ins[site+1].Operand is Mono.Cecil.Cil.Instruction&&Index(m,(Mono.Cecil.Cil.Instruction)ins[site+1].Operand)>site+2,label+" accepted branch reaches the expected continuation");
    }
    static int Ordering(string runtime){return Ordering(Mono.Cecil.AssemblyDefinition.ReadAssembly(runtime));}
    static int Ordering(Mono.Cecil.AssemblyDefinition a)
    {
        var bridge=a.MainModule.GetTypes().Single(t=>t.FullName=="TldMp.StableGameBridge");
        Mono.Cecil.MethodDefinition allow=Method(bridge,"AllowIncoming",2),deliver=Method(bridge,"Deliver",2),broad=Method(bridge,"RouteBroadcast",2),target=Method(bridge,"RouteTarget",3);
        var allowSites=Calls(allow,"AllowRabbitState");
        Check(allowSites.Length==2,"AllowIncoming has dispatch and normal-path rabbit guards");
        var ins=allow.Body.Instructions;
        Check(allowSites[0]+1<ins.Count&&ins[allowSites[0]+1].OpCode==Mono.Cecil.Cil.OpCodes.Ret,"dispatch-path guard feeds the return value directly");
        int validate=FirstCallTo(allow,"ValidateData"),native=FirstCallTo(allow,"AllowNativeSafetyPacket");
        Check(allowSites[0]<validate,"dispatch-path guard precedes validation");
        Check(validate<allowSites[1]&&allowSites[1]<native,"incoming guard sits between ValidateData and native-safety gate");
        Check(allowSites[1]+3<ins.Count&&BrTrue(ins[allowSites[1]+1]),"incoming guard result feeds a conditional branch");
        Check(ins[allowSites[1]+2].OpCode==Mono.Cecil.Cil.OpCodes.Ldc_I4_0&&ins[allowSites[1]+3].OpCode==Mono.Cecil.Cil.OpCodes.Ret,"incoming rejected fallthrough returns false");
        Check(ins[allowSites[1]+1].Operand is Mono.Cecil.Cil.Instruction&&Index(allow,(Mono.Cecil.Cil.Instruction)ins[allowSites[1]+1].Operand)>allowSites[1]+3,"incoming accepted branch reaches the expected continuation");
        var deliverSites=Calls(deliver,"AllowRabbitState");
        Check(deliverSites.Length==1,"Deliver has exactly one rabbit guard");
        Check(FirstCallTo(deliver,"ValidateData")<deliverSites[0]&&deliverSites[0]<FirstCallTo(deliver,"ProcessMessage"),"Deliver guard precedes native ProcessMessage");
        VoidReject(deliver,deliverSites[0],"Deliver");
        foreach(var m in new[]{broad,target})
        {
            var sites=Calls(m,"AllowRabbitState");
            Check(sites.Length==1,m.Name+" has exactly one rabbit guard");
            int publish=FirstCallTo(m,"PublishNative","PublishNativeTo","QueueMap","SendBootstrap","RequestSnapshot");
            Check(sites[0]<publish,m.Name+" guard precedes native publish/forward");
            VoidReject(m,sites[0],m.Name);
        }
        var driver=a.MainModule.GetTypes().Single(t=>t.FullName=="TldMp.StableGameBridge/Driver");
        var driverMethods=driver.Methods.Where(x=>x.Name=="Update"||x.Name=="FixedUpdate"||x.Name=="LateUpdate").ToArray();
        Check(driverMethods.Length==3,"all three Driver phases exist");
        foreach(var dm in driverMethods)
        {
            var dins=dm.Body.Instructions;
            var ret=dins.Last(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Ret);
            int retIdx=dins.IndexOf(ret);
            var tickCall=retIdx>=1?dins[retIdx-1].Operand as Mono.Cecil.MethodReference:null;
            var guestCall=retIdx>=2?dins[retIdx-2].Operand as Mono.Cecil.MethodReference:null;
            bool tick=tickCall!=null&&tickCall.Name=="Tick"&&tickCall.DeclaringType.FullName=="TldMp.RabbitReplicas";
            bool guest=guestCall!=null&&guestCall.Name=="get_IsRabbitGuest"&&guestCall.DeclaringType.FullName=="TldMp.StableGameBridge";
            Check(tick&&guest,"Driver."+dm.Name+" ends with RabbitReplicas.Tick(IsRabbitGuest)");
            var prop=a.MainModule.GetTypes().Single(t=>t.FullName=="TldMp.RabbitReplicas").Methods.Single(m=>m.Name=="Tick");
            Check(prop.Parameters.Count==1&&prop.Parameters[0].ParameterType.FullName=="System.Boolean","RabbitReplicas.Tick signature bool");
        }
        Console.WriteLine("PASS "+checks+" rabbit network guard call-order and branch checks on the real runtime");
        return 0;
    }
    static int Negative(string runtime)
    {
        var a=Mono.Cecil.AssemblyDefinition.ReadAssembly(runtime);
        var bridge=a.MainModule.GetTypes().Single(t=>t.FullName=="TldMp.StableGameBridge");
        var deliver=Method(bridge,"Deliver",2);
        var ins=deliver.Body.Instructions;
        var call=ins[Calls(deliver,"AllowRabbitState")[0]];
        call.OpCode=Mono.Cecil.Cil.OpCodes.Pop;call.Operand=null;
        int at=ins.IndexOf(call);
        ins.Insert(at+1,Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Pop));
        ins.Insert(at+2,Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_1));
        bool rejected=false;int before=checks;
        try{Ordering(a);}catch{rejected=true;}
        checks=before;
        Check(rejected,"bypassed Deliver guard must be rejected by the ordering checks");
        Console.WriteLine("PASS bypass mutation rejected (Deliver guard bypass detected)");
        var broken=Mono.Cecil.AssemblyDefinition.ReadAssembly(runtime);
        var fixedUpdate=broken.MainModule.GetTypes().Single(t=>t.FullName=="TldMp.StableGameBridge/Driver").Methods.Single(m=>m.Name=="FixedUpdate");
        var fixedTick=fixedUpdate.Body.Instructions.Single(i=>i.Operand is Mono.Cecil.MethodReference&&((Mono.Cecil.MethodReference)i.Operand).DeclaringType.FullName=="TldMp.RabbitReplicas"&&((Mono.Cecil.MethodReference)i.Operand).Name=="Tick");
        fixedTick.OpCode=Mono.Cecil.Cil.OpCodes.Pop;fixedTick.Operand=null;
        rejected=false;before=checks;
        try{Ordering(broken);}catch{rejected=true;}
        checks=before;
        Check(rejected,"missing pre-physics Driver tick must be rejected");
        Console.WriteLine("PASS missing FixedUpdate tick mutation rejected");
        return 0;
    }
    public static int Main(string[] args)
    {
        try{if(args.Length==2&&args[0]=="order"){Ordering(args[1]);return Negative(args[1]);}return Scenarios();}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
