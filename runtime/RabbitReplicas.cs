using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    public sealed class RabbitReplicaPart : MonoBehaviour
    {
        internal RabbitReplicas.RabbitReplicaState owner;
        void OnEnable(){if(owner!=null)RabbitReplicas.PartEnabled(owner);}
    }

    public static class RabbitReplicas
    {
        public sealed class RabbitReplicaState
        {
            public nyulscript root;
            public bool held,suspended;
            public int depth;
            public readonly List<Rigidbody> bodies=new List<Rigidbody>();
            public readonly List<bool> kinematic=new List<bool>(),gravity=new List<bool>(),detect=new List<bool>();
            public readonly List<Collider> colliders=new List<Collider>();
            public readonly List<bool> colEnabled=new List<bool>();
            public readonly List<Behaviour> anims=new List<Behaviour>();
            public readonly List<bool> animEnabled=new List<bool>();
        }
        internal static System.Func<RabbitReplicaState,Behaviour,bool> CosmeticEnable;
        internal static System.Action<RabbitReplicaState> CosmeticRelease;
        static readonly Dictionary<nyulscript,RabbitReplicaState> states=new Dictionary<nyulscript,RabbitReplicaState>();
        static readonly List<nyulscript> deadScratch=new List<nyulscript>();

        internal static RabbitReplicaState Find(Component c)
        {
            if(c==null)return null;
            var part=c.GetComponent<RabbitReplicaPart>();
            if(part!=null&&part.owner!=null)return part.owner;
            var nyul=c.GetComponent<nyulscript>();
            if(nyul==null)nyul=c.GetComponentInParent<nyulscript>();
            if(nyul==null)return null;
            RabbitReplicaState s;
            return states.TryGetValue(nyul,out s)?s:null;
        }
        static RabbitReplicaState Resolve(Component c)
        {
            var s=Find(c);
            if(!StableGameBridge.IsRabbitGuest)
            {
                if(s!=null&&s.held)Restore(s);
                return null;
            }
            if(s!=null)
            {
                Acquire(s.root);
                return s;
            }
            if(c==null)return null;
            var nyul=c.GetComponent<nyulscript>();
            if(nyul==null)nyul=c.GetComponentInParent<nyulscript>();
            if(nyul==null)return null;
            Acquire(nyul);
            return states.TryGetValue(nyul,out s)?s:null;
        }
        internal static bool GuardActive(Component c)
        {
            var s=Resolve(c);
            return s!=null&&s.held;
        }
        internal static bool IsRabbitItem(tosaveitemscript item)
        {
            return item!=null&&(item.nyul!=null||(item.ragdollPos!=null&&item.GetComponent<nyulscript>()!=null));
        }
        internal static nyulscript RabbitRoot(tosaveitemscript item)
        {
            if(item==null)return null;
            if(item.nyul!=null)return item.nyul;
            if(item.ragdollPos==null)return null;
            return item.GetComponent<nyulscript>();
        }

        static void AddObject(RabbitReplicaState s,GameObject go,HashSet<GameObject> seen,List<GameObject> gos)
        {
            if(go==null||!seen.Add(go))return;
            gos.Add(go);
        }
        static void AddSubtree(RabbitReplicaState s,Transform t,HashSet<GameObject> seen,List<GameObject> gos)
        {
            if(t==null)return;
            AddObject(s,t.gameObject,seen,gos);
            foreach(var c in t.GetComponentsInChildren<Transform>(true))AddObject(s,c.gameObject,seen,gos);
        }
        static void Collect(RabbitReplicaState s,bool recapture)
        {
            if(s.root==null)return;
            var seen=new HashSet<GameObject>();
            var gos=new List<GameObject>();
            AddSubtree(s,s.root.transform,seen,gos);
            var rag=s.root.ragdoll;
            if(rag!=null)
            {
                if(rag.RB!=null)AddObject(s,rag.RB.gameObject,seen,gos);
                if(rag.RBs!=null)foreach(var rb in rag.RBs)if(rb!=null)AddObject(s,rb.gameObject,seen,gos);
                if(rag.ragdollColliders!=null)foreach(var c in rag.ragdollColliders)if(c!=null)AddObject(s,c.gameObject,seen,gos);
                if(rag.animColliders!=null)foreach(var c in rag.animColliders)if(c!=null)AddObject(s,c.gameObject,seen,gos);
                if(rag.unparent!=null)foreach(var u in rag.unparent)if(u!=null)AddSubtree(s,u,seen,gos);
                if(rag.animColliderTransform!=null)AddSubtree(s,rag.animColliderTransform,seen,gos);
            }
            var item=s.root.GetComponent<tosaveitemscript>();
            if(item!=null&&item.ragdollPos!=null&&item.ragdollPos.childs!=null)
                foreach(var ch in item.ragdollPos.childs)if(ch!=null)AddSubtree(s,ch,seen,gos);
            var trig=s.root.attackTriggerScript;
            if(trig!=null&&trig.selfCol!=null)AddObject(s,trig.selfCol.gameObject,seen,gos);
            if(s.root.playerDeathTrigger!=null)AddSubtree(s,s.root.playerDeathTrigger.transform,seen,gos);
            foreach(var go in gos)
            {
                var part=go.GetComponent<RabbitReplicaPart>();
                if(part==null)part=go.AddComponent<RabbitReplicaPart>();
                part.owner=s;
            }
            CaptureAll(s,gos,recapture);
        }
        static void CaptureAll(RabbitReplicaState s,List<GameObject> gos,bool recapture)
        {
            var haveBodies=new HashSet<Rigidbody>(s.bodies);
            var haveCols=new HashSet<Collider>(s.colliders);
            var haveAnims=new HashSet<Behaviour>(s.anims);
            if(recapture)
            {
                for(int i=0;i<s.bodies.Count;i++)
                {
                    var rb=s.bodies[i];
                    if(rb==null)continue;
                    s.kinematic[i]=rb.isKinematic;s.gravity[i]=rb.useGravity;s.detect[i]=rb.detectCollisions;
                }
                for(int i=0;i<s.colliders.Count;i++){var c=s.colliders[i];if(c!=null)s.colEnabled[i]=c.enabled;}
                for(int i=0;i<s.anims.Count;i++){var a=s.anims[i];if(a!=null)s.animEnabled[i]=a.enabled;}
            }
            foreach(var go in gos)
            {
                if(go==null)continue;
                foreach(var rb in go.GetComponents<Rigidbody>())
                    if(rb!=null&&haveBodies.Add(rb)){s.bodies.Add(rb);s.kinematic.Add(rb.isKinematic);s.gravity.Add(rb.useGravity);s.detect.Add(rb.detectCollisions);}
                foreach(var c in go.GetComponents<Collider>())
                    if(c!=null&&haveCols.Add(c)){s.colliders.Add(c);s.colEnabled.Add(c.enabled);}
                foreach(var a in go.GetComponents<Animator>())
                    if(a!=null&&haveAnims.Add(a)){s.anims.Add(a);s.animEnabled.Add(a.enabled);}
            }
        }
        internal static void CaptureNew(RabbitReplicaState s,GameObject go)
        {
            if(go==null)return;
            var part=go.GetComponent<RabbitReplicaPart>();
            if(part==null)part=go.AddComponent<RabbitReplicaPart>();
            part.owner=s;
            foreach(var rb in go.GetComponents<Rigidbody>())
                if(rb!=null&&!s.bodies.Contains(rb)){s.bodies.Add(rb);s.kinematic.Add(rb.isKinematic);s.gravity.Add(rb.useGravity);s.detect.Add(rb.detectCollisions);}
            foreach(var c in go.GetComponents<Collider>())
                if(c!=null&&!s.colliders.Contains(c)){s.colliders.Add(c);s.colEnabled.Add(c.enabled);}
            foreach(var a in go.GetComponents<Animator>())
                if(a!=null&&!s.anims.Contains(a)){s.anims.Add(a);s.animEnabled.Add(a.enabled);}
        }
        static void Enforce(RabbitReplicaState s)
        {
            if(s.root==null)return;
            for(int i=0;i<s.bodies.Count;i++)
            {
                var rb=s.bodies[i];
                if(rb==null)continue;
                if(!rb.isKinematic){rb.velocity=Vector3.zero;rb.angularVelocity=Vector3.zero;}
                rb.isKinematic=true;rb.useGravity=false;rb.detectCollisions=false;
            }
            for(int i=0;i<s.colliders.Count;i++){var c=s.colliders[i];if(c!=null)c.enabled=false;}
            for(int i=0;i<s.anims.Count;i++)
            {
                var a=s.anims[i];if(a==null)continue;
                bool allow=false;
                if(CosmeticEnable!=null)try{allow=CosmeticEnable(s,a);}catch(System.Exception){allow=false;if(CosmeticRelease!=null)try{CosmeticRelease(s);}catch(System.Exception){}}
                if(a.enabled!=allow)a.enabled=allow;
            }
            s.held=true;
        }
        static void ApplyBaseline(RabbitReplicaState s)
        {
            if(CosmeticRelease!=null)try{CosmeticRelease(s);}catch(System.Exception){}
            for(int i=0;i<s.bodies.Count;i++)
            {
                var rb=s.bodies[i];
                if(rb==null)continue;
                rb.isKinematic=s.kinematic[i];rb.useGravity=s.gravity[i];rb.detectCollisions=s.detect[i];
            }
            for(int i=0;i<s.colliders.Count;i++){var c=s.colliders[i];if(c!=null)c.enabled=s.colEnabled[i];}
            for(int i=0;i<s.anims.Count;i++){var a=s.anims[i];if(a!=null)a.enabled=s.animEnabled[i];}
        }
        static void Restore(RabbitReplicaState s)
        {
            ApplyBaseline(s);
            s.held=false;s.depth=0;s.suspended=false;
        }
        internal static void PartEnabled(RabbitReplicaState s)
        {
            if(s!=null&&s.held&&!s.suspended&&s.depth==0)Enforce(s);
        }

        internal static void Acquire(nyulscript root)
        {
            if(root==null)return;
            if(!StableGameBridge.IsRabbitGuest)return;
            RabbitReplicaState s;
            if(!states.TryGetValue(root,out s)){s=new RabbitReplicaState();s.root=root;states[root]=s;}
            if(s.suspended)return;
            if(!s.held)Collect(s,true);
            Enforce(s);
            if(s.root.AI!=null)RabbitAi.CancelFlee(s.root.AI);
        }
        internal static void BeginMutation(Component owner)
        {
            var s=Resolve(owner);
            if(s==null||!s.held)return;
            s.depth++;
            if(s.depth==1){s.suspended=true;ApplyBaseline(s);}
        }
        internal static void EndMutation(Component owner)
        {
            var s=Find(owner);
            if(s==null||s.depth==0)return;
            s.depth--;
            if(s.depth==0&&s.suspended){s.suspended=false;Collect(s,true);Enforce(s);}
        }
        public static void Tick(bool guest)
        {
            if(states.Count==0)return;
            deadScratch.Clear();
            foreach(var kv in states)
            {
                var s=kv.Value;
                if(s.root==null){if(CosmeticRelease!=null)try{CosmeticRelease(s);}catch(System.Exception){}deadScratch.Add(kv.Key);continue;}
                if(s.depth>0){s.depth=0;s.suspended=false;}
                int n=0;
                for(int i=0;i<s.bodies.Count;i++)if(s.bodies[i]!=null){s.bodies[n]=s.bodies[i];s.kinematic[n]=s.kinematic[i];s.gravity[n]=s.gravity[i];s.detect[n]=s.detect[i];n++;}
                if(n<s.bodies.Count){s.bodies.RemoveRange(n,s.bodies.Count-n);s.kinematic.RemoveRange(n,s.kinematic.Count-n);s.gravity.RemoveRange(n,s.gravity.Count-n);s.detect.RemoveRange(n,s.detect.Count-n);}
                n=0;
                for(int i=0;i<s.colliders.Count;i++)if(s.colliders[i]!=null){s.colliders[n]=s.colliders[i];s.colEnabled[n]=s.colEnabled[i];n++;}
                if(n<s.colliders.Count){s.colliders.RemoveRange(n,s.colliders.Count-n);s.colEnabled.RemoveRange(n,s.colEnabled.Count-n);}
                n=0;
                for(int i=0;i<s.anims.Count;i++)if(s.anims[i]!=null){s.anims[n]=s.anims[i];s.animEnabled[n]=s.animEnabled[i];n++;}
                if(n<s.anims.Count){s.anims.RemoveRange(n,s.anims.Count-n);s.animEnabled.RemoveRange(n,s.animEnabled.Count-n);}
                if(guest){if(!s.held)Acquire(s.root);else Enforce(s);}
                else if(s.held)Restore(s);
            }
            foreach(var k in deadScratch)states.Remove(k);
            deadScratch.Clear();
        }

        internal static bool GuestMove(movecreaturescript creature)
        {
            var s=Resolve(creature);
            if(s==null||!s.held)return false;
            if(creature.moveDir!=Vector3.zero)creature.moveDir=Vector3.zero;
            return true;
        }
        internal static bool GuestRot(movecreaturescript creature)
        {
            var s=Resolve(creature);
            if(s==null||!s.held)return false;
            if(creature.moveRot!=0f)creature.moveRot=0f;
            return true;
        }
        internal static bool GuestAi(aiscript ai)
        {
            var s=Resolve(ai);
            if(s==null||!s.held)return false;
            if(ai.alive&&ai.senses!=null&&!ai.senses.alive)ai.Die(false);
            return true;
        }
        internal static bool GuestScan(sensoryscript senses)
        {
            var s=Resolve(senses);
            if(s==null||!s.held)return false;
            senses.stimuls.Clear();
            return true;
        }
        internal static bool GuestSimple(Component c){return GuardActive(c);}
        internal static void ObserveInit(tosaveitemscript item)
        {
            if(!StableGameBridge.IsRabbitGuest)return;
            var root=RabbitRoot(item);
            if(root!=null)Acquire(root);
        }
        internal static void BeforePose(Component owner)
        {
            Resolve(owner);
        }
        internal static void AfterUnfreeze(Component owner)
        {
            var s=Resolve(owner);
            if(s==null||!s.held||s.suspended)return;
            CaptureNew(s,owner.gameObject);
            Enforce(s);
        }
    }
}



