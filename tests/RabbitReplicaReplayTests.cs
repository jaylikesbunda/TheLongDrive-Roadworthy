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
        public void Rotate(Vector3 axis,float deg){rotateCalls++;}
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
    public class Animator:Behaviour{}
    public class Joint:Component{}
    public static class Physics{public static int ignored;public static void IgnoreCollision(Collider a,Collider b){ignored++;}public static void IgnoreCollision(Collider a,Collider b,bool i){ignored++;}}
    public static class Debug{public static int logs;public static void Log(object o){logs++;}}
    public static class Mathf{public static float Clamp(float v,float a,float b){return v<a?a:v>b?b:v;}public static float InverseLerp(float a,float b,float v){return a==b?0f:(v-a)/(b-a);}}
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
public class nyulscript:MonoBehaviour{public ragdollactivatescript ragdoll;public aiscript AI;public sensoryscript senses;public playerdeathtriggerscript attackTriggerScript;public GameObject playerDeathTrigger;}
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

public static class RabbitReplicaReplayTests
{
    static int checks;static bool candidate;
    static int StateCount(){var f=typeof(TldMp.RabbitReplicas).GetField("states",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);return ((System.Collections.IDictionary)f.GetValue(null)).Count;}
    static void Check(bool v,string label){checks++;if(!v)throw new Exception("check "+checks+" failed: "+label);}
    static void AdditionalCases()
    {
        TldMp.StableGameBridge.connected=true;
        TldMp.StableGameBridge.node=null;
        syncScript.isHost=false;
        Check(TldMp.StableGameBridge.IsRabbitGuest,"node-null native join is still a guest");
        TldMp.StableGameBridge.loopbackMode=true;
        TldMp.Loopback.IsHost=true;
        Check(!TldMp.StableGameBridge.IsRabbitGuest,"loopback host is not a guest");
        TldMp.Loopback.IsHost=false;
        Check(TldMp.StableGameBridge.IsRabbitGuest,"loopback guest is isolated");
        TldMp.StableGameBridge.connected=false;
        Check(!TldMp.StableGameBridge.IsRabbitGuest,"disconnected loopback is native");
        TldMp.StableGameBridge.connected=true;
        TldMp.StableGameBridge.loopbackMode=false;
        nyulscript nyul;ragdollactivatescript rag;tosaveitemscript item;
        var go=MakeRabbit(out nyul,out rag,out item);
        var extra=go.AddComponent<Collider>();extra.enabled=false;
        var second=go.AddComponent<Collider>();
        var otherAnim=go.AddComponent<Animator>();otherAnim.enabled=false;
        var child=new GameObject();child.transform.parent=go.transform;child.SetActive(false);
        var staticCol=child.AddComponent<Collider>();
        rag.RB.useGravity=false;rag.RB.detectCollisions=false;
        var pose=item.ragdollPos;
        var bone=pose.childs[0];
        bone.parent=null;
        Check(go.transform.children.IndexOf(bone)<0,"fixture bone is genuinely detached");
        pose.poss=new[]{new Vector3d(9,8,7)};pose.rots=new[]{new Vector3(6,5,4)};
        int writes=0;
        Transform.beforePose=delegate(Transform t)
        {
            if(t!=bone)return;
            writes++;
            Check(rag.RB.isKinematic&&rag.RBs[0].isKinematic,"all bodies frozen before first native pose write");
            Check(!second.enabled&&!extra.enabled&&!staticCol.enabled&&!rag.ragdollColliders[0].enabled&&!nyul.attackTriggerScript.selfCol.enabled,"all colliders disabled before first native pose write");
        };
        try{pose.UpdPos();}finally{Transform.beforePose=null;}
        Check(writes==2,"position and rotation were both observed at their native write boundaries");
        Check(!rag.anim.enabled&&!otherAnim.enabled,"all animator components isolated");
        var state=go.GetComponent<TldMp.RabbitReplicaPart>().owner;
        for(int i=0;i<4;i++){TldMp.RabbitReplicas.Acquire(nyul);TldMp.RabbitReplicas.Tick(true);}
        go.GetComponent<TldMp.RabbitReplicaPart>().enabled=false;
        go.SetActive(false);go.SetActive(true);
        Check(!second.enabled&&rag.RB.isKinematic,"disabled marker and object reactivation never release physics");
        syncScript.isHost=true;
        Check(TldMp.StableGameBridge.RabbitNyulGuard(nyul),"host role resumes immediately without tick");
        Check(rag.anim.enabled&&!otherAnim.enabled,"repeated acquisition preserves enabled and disabled animator baselines");
        Check(second.enabled&&!extra.enabled&&staticCol.enabled,"all original collider flags restored exactly");
        Check(!rag.RB.isKinematic&&!rag.RB.useGravity&&!rag.RB.detectCollisions,"original false body flags restored exactly");
        second.enabled=false;extra.enabled=true;rag.anim.enabled=false;otherAnim.enabled=true;
        rag.RB.useGravity=true;rag.RB.detectCollisions=true;
        syncScript.isHost=false;
        Check(!TldMp.StableGameBridge.RabbitNyulGuard(nyul),"reconnect reacquires from current host flags");
        syncScript.isHost=true;
        TldMp.StableGameBridge.RabbitNyulGuard(nyul);
        Check(!second.enabled&&extra.enabled&&!rag.anim.enabled&&otherAnim.enabled,"reconnect restores new collider and animator flags, not old snapshots");
        Check(rag.RB.useGravity&&rag.RB.detectCollisions,"reconnect restores new body flags");
        syncScript.isHost=false;
        TldMp.RabbitReplicas.BeginMutation(rag);
        Check(state.depth==1&&state.suspended,"interrupted bracket acquired");
        syncScript.isHost=true;
        TldMp.StableGameBridge.RabbitNyulGuard(nyul);
        Check(!state.held&&!state.suspended&&state.depth==0,"role flip clears outstanding bracket state");
        TldMp.RabbitReplicas.EndMutation(rag);
        syncScript.isHost=false;
        TldMp.StableGameBridge.RabbitNyulGuard(nyul);
        Check(state.held&&!state.suspended,"guest can reacquire immediately after interrupted host flip");
        var oldBody=rag.RBs[0];
        UnityEngine.Object.Destroy(oldBody);
        var joint=bone.gameObject.AddComponent<jointHelpScript>();joint.freezed=true;
        joint.UnFreezeStart();
        Check(joint.RB!=null&&!ReferenceEquals(joint.RB,oldBody),"native unfreeze replaces a destroyed body on a detached bone");
        Check(joint.RB.isKinematic&&!joint.RB.useGravity&&!joint.RB.detectCollisions,"replacement body isolated before a physics step");
        syncScript.isHost=true;
        TldMp.StableGameBridge.RabbitNyulGuard(nyul);
        Check(!joint.RB.isKinematic&&joint.RB.useGravity&&joint.RB.detectCollisions,"replacement restores its own native dynamic baseline");
        syncScript.isHost=false;
        var plain=new GameObject().AddComponent<movecreaturescript>();
        plain.moveRot=7;
        Check(TldMp.StableGameBridge.RabbitRotGuard(plain)&&plain.moveRot==7,"unrelated creature remains native");
        TldMp.StableGameBridge.connected=false;
        Check(TldMp.StableGameBridge.RabbitNyulGuard(nyul),"disconnect restores native behavior without tick");
        int count=StateCount();
        UnityEngine.Object.Destroy(go);
        TldMp.RabbitReplicas.Tick(false);
        Check(StateCount()==count-1,"destroyed root really leaves the registry");
        TldMp.StableGameBridge.node=new TldMp.StableNode();
    }
    static GameObject MakeRabbit(out nyulscript nyul,out ragdollactivatescript rag,out tosaveitemscript item)
    {
        var go=new GameObject();nyul=go.AddComponent<nyulscript>();
        var rb=go.AddComponent<Rigidbody>();
        var col=go.AddComponent<Collider>();
        rag=go.AddComponent<ragdollactivatescript>();nyul.ragdoll=rag;
        var ai=go.AddComponent<aiscript>();nyul.AI=ai;rag.AI=ai;
        var senses=go.AddComponent<sensoryscript>();nyul.senses=senses;ai.senses=senses;
        var move=go.AddComponent<movecreaturescript>();rag.move=move;
        rag.anim=go.AddComponent<Animator>();rag.RB=rb;
        var bone1=new GameObject();bone1.transform.parent=go.transform;
        var brb=bone1.AddComponent<Rigidbody>();brb.isKinematic=true;
        var bcol=bone1.AddComponent<Collider>();
        rag.RBs=new[]{brb};rag.ragdollColliders=new[]{bcol};rag.animColliders=new[]{col};
        rag.animColliderTransform=new GameObject().transform;
        item=go.AddComponent<tosaveitemscript>();item.id=3;item.nyul=nyul;
        item.ragdollPos=go.AddComponent<ragdollchildpos>();item.ragdollPos.childs=new[]{bone1.transform};
        
        var trig=new GameObject();nyul.playerDeathTrigger=trig;trig.transform.parent=go.transform;
        var ts=trig.AddComponent<playerdeathtriggerscript>();ts.selfCol=trig.AddComponent<Collider>();nyul.attackTriggerScript=ts;
        return go;
    }
    public static int Main(string[] args)
    {
        try
        {
            candidate=args.Length>0&&args[0]=="candidate";
            TldMp.StableGameBridge.connected=true;TldMp.StableGameBridge.node.Host=7;TldMp.StableGameBridge.loopbackMode=false;
            nyulscript nyul;ragdollactivatescript rag;tosaveitemscript item;
            var go=MakeRabbit(out nyul,out rag,out item);
            savedatascript.s.items[3]=item;
            syncScript.isHost=false;
            item.FInit();
            var state=candidate?go.GetComponent<TldMp.RabbitReplicaPart>().owner:null;
            if(candidate)
            {
                Check(rag.RB.isKinematic&&rag.RBs[0].isKinematic,"guest acquire freezes root and bone bodies");
                Check(!rag.RB.detectCollisions&&!rag.RB.useGravity&&!rag.RBs[0].detectCollisions,"guest acquire drops gravity and collision detection");
                Check(!rag.ragdollColliders[0].enabled&&!rag.animColliders[0].enabled,"guest acquire disables all colliders");
                Check(!rag.anim.enabled,"guest acquire disables animator");
                Check(state!=null,"root marked for detached resolution");
            }
            else
            {
                Check(!rag.RB.isKinematic&&rag.ragdollColliders[0].enabled&&rag.anim.enabled,"native FInit leaves bodies live");
            }
            var boneRb=rag.RBs[0];var boneCol=rag.ragdollColliders[0];
            rag.SetRagdol(true);
            Check(rag.ragdoll,"native SetRagdol ran");
            if(candidate)
            {
                Check(boneRb.isKinematic&&!boneRb.detectCollisions&&!boneCol.enabled,"bracket end re-isolates ragdoll state");
                Check(!rag.anim.enabled,"animator re-disabled after ragdoll");
            }
            else
            {
                Check(!boneRb.isKinematic&&boneRb.useGravity&&boneCol.enabled,"native SetRagdol(true) makes bones dynamic");
            }
            rag.SetRagdol(false);
            if(candidate){Check(boneRb.isKinematic&&!boneCol.enabled,"bracket re-isolates after live restore");}
            else{Check(boneRb.isKinematic&&!boneCol.enabled,"native SetRagdol(false) restores kinematic bones");}
            var j=new GameObject();var jh=j.AddComponent<jointHelpScript>();jh.freezed=true;
            var jp=j.AddComponent<TldMp.RabbitReplicaPart>();if(state!=null)jp.owner=state;
            jh.UnFreezeStart();
            Check(jh.RB!=null,"native UnFreezeStart created the rigidbody");
            if(candidate)Check(jh.RB.isKinematic&&!jh.RB.detectCollisions,"new bone body captured and isolated before physics");
            else Check(!jh.RB.isKinematic&&jh.RB.detectCollisions,"native unfreeze body stays dynamic");
            var pkGO=new GameObject();pkGO.transform.parent=go.transform;
            var pk=pkGO.AddComponent<pickupable>();var pkCol=pkGO.AddComponent<Collider>();pk.cols.Add(new layerScript{col=pkCol});
            pkCol.enabled=false;pk.SetCollider(true);
            if(candidate)Check(!pkCol.enabled,"native pickup collider re-enable ends isolated");
            else Check(pkCol.enabled,"native SetCollider enables colliders");
            var rp=item.ragdollPos;rp.poss=new Vector3d[]{new Vector3d(4,5,6)};rp.rots=new Vector3[]{new Vector3(10,20,30)};
            rp.UpdPos();
            Check(rp.childs[0].position==new Vector3(4,5,6)&&rp.childs[0].eulerAngles==new Vector3(10,20,30),"native pose writes still applied");
            if(candidate)Check(boneRb.isKinematic&&!boneCol.enabled,"isolation ensured before first pose write");
            var move=rag.move;
            int rotBefore=Transform.rotateCalls;
            move.moveRot=45f;move.Rot();
            if(candidate)Check(move.moveRot==0f&&Transform.rotateCalls==rotBefore,"guest Rot gate blocks the transform write");
            else Check(Transform.rotateCalls>rotBefore,"native Rot runs");
            var ai=nyul.AI;
            if(candidate)
            {
                var trig=nyul.attackTriggerScript;
                Check(!TldMp.StableGameBridge.RabbitTriggerGuard(trig),"guest trigger callback blocked");
                nyul.senses.stimuls.Add(new stimul());
                Check(!TldMp.StableGameBridge.RabbitScanGuard(nyul.senses),"guest Scan guard blocks sensing");
                Check(nyul.senses.stimuls.Count==0,"guest Scan clears stimuls");
                Check(!TldMp.StableGameBridge.RabbitNyulGuard(nyul),"guest nyul guard blocks Update/Attack/Roar");
                ai.alive=true;nyul.senses.alive=false;var before=ai.dieCalls;
                Check(!TldMp.StableGameBridge.RabbitAiGuard(ai),"guest ai guard blocks Update");
                Check(ai.dieCalls==before+1,"guest ai guard completes deferred death exactly once");
                TldMp.StableGameBridge.RabbitAiGuard(ai);
                Check(ai.dieCalls==before+1,"deferred death is not repeated");
                Check(TldMp.RabbitReplicas.GuardActive(go.transform),"isolated state resolves from root marker");
                TldMp.RabbitReplicas.BeginMutation(rag);
                Check(rag.animColliders[0].enabled,"suspended bracket restores baseline for native mutation");
                TldMp.RabbitReplicas.BeginMutation(rag);
                Check(rag.animColliders[0].enabled,"nested depth-2 bracket stays suspended");
                TldMp.RabbitReplicas.EndMutation(rag);
                Check(rag.animColliders[0].enabled,"outer bracket still suspended after inner end");
                TldMp.RabbitReplicas.EndMutation(rag);
                Check(!rag.animColliders[0].enabled,"final end re-isolates");
                TldMp.RabbitReplicas.BeginMutation(rag);
                Check(rag.animColliders[0].enabled,"reopened bracket suspends again");
                TldMp.RabbitReplicas.Tick(TldMp.StableGameBridge.IsRabbitGuest);
                Check(!rag.animColliders[0].enabled,"abandoned bracket re-isolates on next tick");
                syncScript.isHost=true;
                Check(TldMp.StableGameBridge.RabbitAiGuard(ai),"host flip releases immediately via guard");
                Check(rag.animColliders[0].enabled,"host release restores canonical flags without tick");
                syncScript.isHost=false;
                TldMp.RabbitReplicas.Tick(TldMp.StableGameBridge.IsRabbitGuest);
                Check(!rag.animColliders[0].enabled,"rejoin reacquires isolation");
                UnityEngine.Object.Destroy(go);
                TldMp.RabbitReplicas.Tick(TldMp.StableGameBridge.IsRabbitGuest);
                Check(!TldMp.RabbitReplicas.GuardActive(go.transform),"destroyed root prunes registry");
            }
            if(candidate)AdditionalCases();
            Console.WriteLine("PASS "+checks+" rabbit replica replay checks ("+(candidate?"candidate":"baseline")+"; no PhysX/car launch validation)");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
