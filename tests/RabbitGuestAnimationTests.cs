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
        public static void Destroy(Object o){if(o==null)return;var g=o as GameObject;if(g!=null)foreach(var c in g.components)c.destroyed=true;o.destroyed=true;}
    }
    public class Component:Object
    {
        internal GameObject go;
        public GameObject gameObject{get{return go;}set{go=value;}}
        public Transform transform{get{return gameObject==null?null:gameObject.transform;}}
        public T GetComponent<T>(){return gameObject==null?default(T):gameObject.GetComponent<T>();}
        public T GetComponentInParent<T>(){if(gameObject==null)return default(T);var c=gameObject.GetComponent<T>();if(c!=null)return c;var p=transform.parent;return p==null?default(T):p.GetComponentInParent<T>();}
        public T[] GetComponentsInChildren<T>(bool inactive){var r=new List<T>();Collect(transform,r);return r.ToArray();}
        public T[] GetComponents<T>(){if(gameObject==null)return new T[0];var r=new List<T>();foreach(var c in gameObject.components)if(c is T)r.Add((T)(object)c);return r.ToArray();}
        static void Collect<T>(Transform t,List<T> r){foreach(var c in t.gameObject.components)if(c is T)r.Add((T)(object)c);for(int i=0;i<t.childCount;i++)Collect<T>(t.GetChild(i),r);}
    }
    public class Behaviour:Component{bool _enabled=true;public bool enabled{get{return _enabled;}set{_enabled=value;}}public bool isActiveAndEnabled{get{return enabled&&gameObject!=null&&gameObject.activeSelf;}}}
    public class MonoBehaviour:Behaviour
    {
        public Coroutine StartCoroutine(System.Collections.IEnumerator r){return null;}
        public void StopAllCoroutines(){}
    }
    public sealed class Coroutine{}
    public class GameObject:Object
    {
        Transform t;public Transform transform{get{return t;}}
        public bool activeSelf=true;
        internal readonly List<Component> components=new List<Component>();
        public GameObject(){t=new Transform();t.gameObject=this;components.Add(t);}
        public T GetComponent<T>(){foreach(var c in components)if(c is T)return (T)(object)c;return default(T);}
        public T AddComponent<T>() where T:Component,new(){var c=new T();c.gameObject=this;components.Add(c);return c;}
        public T[] GetComponents<T>(){var r=new List<T>();foreach(var c in components)if(c is T)r.Add((T)(object)c);return r.ToArray();}
        public void SetActive(bool v){if(activeSelf==v)return;activeSelf=v;if(v)foreach(var c in components){var m=c.GetType().GetMethod("OnEnable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);if(m!=null)m.Invoke(c,null);}}
    }
    public class Transform:Component
    {
        Transform _parent;Vector3 _pos;Quaternion _rot;
        public Transform parent{get{return _parent;}set{if(_parent!=null)_parent.children.Remove(this);_parent=value;if(_parent!=null)_parent.children.Add(this);}}
        public static Action<Transform> beforePose;
        public Vector3 position{get{return _pos;}set{if(beforePose!=null)beforePose(this);_pos=value;}}
        public Quaternion rotation{get{return _rot;}set{_rot=value;}}
        public Vector3 localPosition{get{return _pos;}set{_pos=value;}}
        public Quaternion localRotation{get{return _rot;}set{_rot=value;}}
        public Vector3 eulerAngles{get{return _rot.euler;}set{if(beforePose!=null)beforePose(this);_rot.euler=value;}}
        public Transform root{get{var t=this;while(t.parent!=null)t=t.parent;return t;}}
        public int childCount{get{return children.Count;}}
        public List<Transform> children=new List<Transform>();
        public Transform GetChild(int i){return children[i];}
        public bool IsChildOf(Transform ancestor){var t=parent;while(t!=null){if(t==ancestor)return true;t=t.parent;}return false;} public void Rotate(Vector3 axis,float deg){rotateCalls++;}
        internal static int rotateCalls;
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero{get{return new Vector3();}}
        public static Vector3 up{get{return new Vector3(0,1,0);}}
        public static Vector3 operator *(Vector3 a,float s){return new Vector3(a.x*s,a.y*s,a.z*s);}
        public static bool operator ==(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        public static bool operator !=(Vector3 a,Vector3 b){return !(a==b);}
        public override bool Equals(object o){return o is Vector3&&(Vector3)o==this;}
        public override int GetHashCode(){return 0;}
    }
    public struct Vector3d{public double x,y,z;public Vector3d(double a,double b,double c){x=a;y=b;z=c;}}
    public struct Quaternion{public Vector3 euler;public static Quaternion Euler(Vector3 v){var q=new Quaternion();q.euler=v;return q;}}
    public class Collider:Component{bool _e=true;public bool enabled{get{return _e;}set{_e=value;}}public bool isTrigger{get;set;}}
    public class Rigidbody:Component
    {
        bool _k,_g=true,_d=true;
        public bool isKinematic{get{return _k;}set{_k=value;}}
        public bool useGravity{get{return _g;}set{_g=value;}}
        public bool detectCollisions{get{return _d;}set{_d=value;}}
        public Vector3 velocity{get;set;}public Vector3 angularVelocity{get;set;}
        public CollisionDetectionMode collisionDetectionMode{get;set;}
        public RigidbodyInterpolation interpolation{get;set;}
        public float mass{get;set;}
    }
    public enum CollisionDetectionMode{Discrete,Continuous,ContinuousDynamic}
    public enum RigidbodyInterpolation{None,Interpolate}
        public class RuntimeAnimatorController:Object{public string name;}
    public class StateMachineBehaviour:Object{}
    public class Animator:Behaviour
    {
        public bool applyRootMotion,fireEvents=true;public RuntimeAnimatorController runtimeAnimatorController;
        public float runValue;public int attackTriggers,roarTriggers,plays;public StateMachineBehaviour[] behaviours=new StateMachineBehaviour[0];
        public T[] GetBehaviours<T>() where T:StateMachineBehaviour{return (T[])(object)behaviours;}
        public void SetFloat(string name,float value){runValue=value;}
        public void ResetTrigger(string name){}
        public void SetTrigger(string name){if(name=="Attack")attackTriggers++;if(name=="Roar")roarTriggers++;}
        public void Play(string name,int layer,float time){plays++;}
    }
    public class Joint:Component{}
    public static class Physics{public static int ignored;public static void IgnoreCollision(Collider a,Collider b){ignored++;}public static void IgnoreCollision(Collider a,Collider b,bool i){ignored++;}}
    public static class Debug{public static int logs;public static void Log(object o){logs++;}}
    public static class Mathf{public static float Clamp(float v,float a,float b){return v<a?a:v>b?b:v;}public static float Clamp01(float v){return Clamp(v,0,1);}public static float InverseLerp(float a,float b,float v){return a==b?0f:(v-a)/(b-a);}}
    public static class Time{public static float deltaTime{get{return 0.016f;}}public static float realtimeSinceStartup{get;set;}}
}

public class stimul{}
public class layerScript{public Collider col;}
public class massScript:MonoBehaviour{public void SetMass(float m){}public void Upd(){upd++;}public int upd;}
public class tankcapscript:MonoBehaviour{public void FStart(){}}
public class attachablescript:MonoBehaviour{public bool attached;}
public class savedatascript:MonoBehaviour{public static savedatascript s=new savedatascript();public Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();}
public class tosaveitemscript:MonoBehaviour
{
    public int id;public nyulscript nyul;public ragdollchildpos ragdollPos;public Rigidbody RB;public bool buried;
    public pickupable P;public attachablescript attachable;public Transform useThisForDistance,useThisForPos;public List<tankcapscript> tankcapscripts=new List<tankcapscript>();
    public void FInit(){}
}
public class pickupable:MonoBehaviour{public List<layerScript> cols=new List<layerScript>();public bool cantPickup,dontMount,inRightHand;public Rigidbody physlock;public Transform parentBeforeMount;public void SetCollider(bool e){}}
public class nyulscript:MonoBehaviour{public Animator anim;public ragdollactivatescript ragdoll;public aiscript AI;public sensoryscript senses;public playerdeathtriggerscript attackTriggerScript;public GameObject playerDeathTrigger;}
public class aiscript:MonoBehaviour{public bool alive=true,fleeing;public sensoryscript senses;public nyulscript nyul;public int dieCalls;public void Die(bool s){dieCalls++;alive=false;}public void Update(){}}
public class sensoryscript:MonoBehaviour{public bool alive=true;public List<stimul> stimuls=new List<stimul>();public void Scan(){}}
public class movecreaturescript:MonoBehaviour{public Vector3 moveDir;public float moveRot,speed=1,rotSpeed=90;public void Rot(){}public void Move(){}public void Jump(){}}
public class playerdeathtriggerscript:MonoBehaviour{public Collider selfCol;public int hitCalls;void OnTriggerStay(Collider c){hitCalls++;}}
public class ragdollactivatescript:MonoBehaviour
{
    public movecreaturescript move;public aiscript AI;public bool dontChangeRagdolCollidersEnabled,ragdollCollidersTriggerWhenOff,ragdollCollidersTriggerWhenOn;
    public Animator anim;public Collider[] ragdollColliders=new Collider[0],animColliders=new Collider[0];public pickupable[] pickupables=new pickupable[0];
    public Joint[] joints=new Joint[0];public Transform[] unparent=new Transform[0];public Transform animColliderTransform;
    Vector3 animColliderStartPos;Quaternion animColliderStartRot;
    public Rigidbody RB;public massScript mass;public Rigidbody[] RBs=new Rigidbody[0];public massScript[] massS=new massScript[0];public bool ragdoll,set2;
    void InitColliders(){}
    public void SetRagdol(bool set){}
}
public class ragdollchildpos:MonoBehaviour
{
    public Transform Tparent;public Transform[] childs=new Transform[0];public Vector3d[] poss=new Vector3d[0];public Vector3[] rots=new Vector3[0];public bool freezed;
    public void FStart(){if(Tparent==null)Tparent=transform;}
    public void UpdPos(){}
}
public class jointHelpScript:MonoBehaviour{public bool freezed;public Rigidbody RB;public void UnFreezeStart(){}}
public class mainscript:MonoBehaviour
{
    public static bool AIDisable;
    public static Vector3 UnityPosFromGlobal(Vector3d p){return new Vector3((float)p.x,(float)p.y,(float)p.z);}
    public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d(p.x,p.y,p.z);}
    public static RigidbodyInterpolation RBI(){return RigidbodyInterpolation.Interpolate;}
    public void Update(){}
}
public class syncScript:MonoBehaviour{public static bool isHost;public static bool IsHost(){return isHost;}public static bool IsHostOrSingle(){return isHost;}}

namespace TldMp
{
    public sealed class StableNode{public ulong Host;}
    public static class Loopback{public static bool IsHost;public static ulong Own;}
    public static partial class StableGameBridge
    {
        internal static bool Connected{get{return connected;}}internal static bool connected;
        internal static bool loopbackMode;
        internal static StableNode node=new StableNode();
        internal static bool Find(uint id,out tosaveitemscript item){return savedatascript.s.items.TryGetValue(id,out item);}
        public static void ConfigureRabbitImpact(ragdollactivatescript ragdoll){RabbitReplicas.EndMutation(ragdoll);}
        public static void NativeSystemBegin(int slot){}
        public static void NativeSystemEnd(int slot){}
        public static void DebrisItemInitialized(tosaveitemscript item){}
    }
}

public static class RabbitGuestAnimationTests
{
 static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 public static int Main()
 {
  try
  {
   TldMp.StableGameBridge.connected=true;syncScript.isHost=false;Time.realtimeSinceStartup=10;
   var go=new GameObject();var root=go.AddComponent<nyulscript>();root.AI=go.AddComponent<aiscript>();root.senses=go.AddComponent<sensoryscript>();root.ragdoll=go.AddComponent<ragdollactivatescript>();root.ragdoll.RB=go.AddComponent<Rigidbody>();var col=go.AddComponent<Collider>();
   var child=new GameObject();child.transform.parent=go.transform;var anim=child.AddComponent<Animator>();anim.runtimeAnimatorController=new RuntimeAnimatorController{name="nyul"};anim.applyRootMotion=true;anim.fireEvents=true;root.anim=anim;root.ragdoll.anim=anim;
   var extra=go.AddComponent<Animator>();TldMp.RabbitGuestAnimation.Install();TldMp.RabbitReplicas.Acquire(root);
   Check(!anim.enabled&&!extra.enabled,"no animation before an authenticated motion heartbeat");Check(root.ragdoll.RB.isKinematic&&!col.enabled,"guest physics stays suppressed");
   TldMp.RabbitGuestAnimation.CosmeticMotion(root,.25f);
   Check(anim.enabled&&!extra.enabled,"only audited child animator enabled");Check(!anim.applyRootMotion&&!anim.fireEvents,"root motion and events suppressed");Check(Math.Abs(anim.runValue-.5f)<.001f,"actual normalized motion drives RunValue");Check(anim.plays==1,"audited idle state selected once");
   TldMp.RabbitReplicas.Tick(true);Check(anim.enabled&&anim.plays==1,"replica enforcement preserves animation without restarts");
   TldMp.RabbitGuestAnimation.CosmeticActions(root,true,true);TldMp.RabbitGuestAnimation.CosmeticActions(root,true,true);Check(anim.attackTriggers==1&&anim.roarTriggers==1,"host visual triggers are edge driven");
   TldMp.RabbitGuestAnimation.CosmeticActions(root,false,false);TldMp.RabbitGuestAnimation.CosmeticActions(root,true,false);Check(anim.attackTriggers==2,"new attack episode retriggers once");
   TldMp.RabbitGuestAnimation.Stop(root);Check(!anim.enabled&&anim.applyRootMotion&&anim.fireEvents,"inactive packet stops and restores flags immediately");
   TldMp.RabbitGuestAnimation.CosmeticMotion(root,.5f);Check(anim.enabled,"new active stream restarts animation");Time.realtimeSinceStartup+=.61f;TldMp.RabbitReplicas.Tick(true);Check(!anim.enabled&&anim.applyRootMotion&&anim.fireEvents,"heartbeat expiry suspends and restores flags");
   TldMp.RabbitGuestAnimation.CosmeticMotion(root,.5f);root.ragdoll.ragdoll=true;TldMp.RabbitReplicas.Tick(true);Check(!anim.enabled,"ragdoll suspends animation");root.ragdoll.ragdoll=false;
   TldMp.RabbitGuestAnimation.CosmeticMotion(root,.5f);root.senses.alive=false;TldMp.RabbitReplicas.Tick(true);Check(!anim.enabled,"deferred death suspends animation");root.senses.alive=true;
   TldMp.RabbitGuestAnimation.CosmeticMotion(root,.5f);TldMp.RabbitReplicas.BeginMutation(root);Check(anim.enabled&&anim.applyRootMotion&&anim.fireEvents,"mutation restores native animator baseline");TldMp.RabbitReplicas.EndMutation(root);Check(!anim.enabled,"mutation completion requires a new heartbeat");
   TldMp.RabbitGuestAnimation.CosmeticMotion(root,.5f);syncScript.isHost=true;TldMp.RabbitReplicas.Tick(false);Check(anim.enabled&&anim.applyRootMotion&&anim.fireEvents&&!root.ragdoll.RB.isKinematic&&col.enabled,"host transition restores native baseline");
   syncScript.isHost=false;TldMp.RabbitReplicas.Acquire(root);anim.runtimeAnimatorController=new RuntimeAnimatorController{name="custom"};TldMp.RabbitGuestAnimation.CosmeticMotion(root,.3f);Check(!anim.enabled,"unknown controller fails closed");
   anim.runtimeAnimatorController=new RuntimeAnimatorController{name="nyul"};anim.behaviours=new[]{new StateMachineBehaviour()};TldMp.RabbitGuestAnimation.CosmeticMotion(root,.3f);Check(!anim.enabled,"controller with state behaviour fails closed");
   Console.WriteLine("PASS "+checks+" guest cosmetic checks");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}
