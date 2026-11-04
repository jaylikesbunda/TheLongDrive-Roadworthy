using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Optional guest-only cosmetics. The host remains authoritative for AI, collision,
    // damage, root motion and ragdoll poses; the stream supplies only visual run speed.
    public static class RabbitGuestAnimation
    {
        sealed class Visual
        {
            internal Animator animator;
            internal RuntimeAnimatorController controller;
            internal bool owned, rootMotion, fireEvents, attacking, roaring;
            internal float seen, speed;
        }
        static readonly Dictionary<RabbitReplicas.RabbitReplicaState,Visual> visuals=new Dictionary<RabbitReplicas.RabbitReplicaState,Visual>();
        const float FreshSeconds=.6f;

        public static void Install()
        {
            RabbitReplicas.CosmeticEnable=Enable;
            RabbitReplicas.CosmeticRelease=Release;
        }
        // Called only for a fresh, authenticated, active interpolated host motion track.
        // speed is horizontal metres/second divided by the rabbit's native maximum speed.
        public static void CosmeticMotion(nyulscript root,float speed)
        {
            if(root==null||!StableGameBridge.IsRabbitGuest||float.IsNaN(speed)||float.IsInfinity(speed))return;
            var s=RabbitReplicas.Find(root);
            if(s==null||!s.held||s.suspended||s.depth!=0)return;
            Visual v;
            if(!visuals.TryGetValue(s,out v)){v=new Visual();visuals.Add(s,v);}
            v.seen=Time.realtimeSinceStartup;v.speed=Mathf.Clamp01(speed);
            var a=root.anim;
            try{if(a!=null && Enable(s,a))a.enabled=true;}catch(Exception){Release(s);}
        }
        // Authenticated host action flags only drive visual triggers, never native Attack/Roar.
        public static void CosmeticActions(nyulscript root,bool attacking,bool roaring)
        {
            if(root==null||!StableGameBridge.IsRabbitGuest)return;
            var s=RabbitReplicas.Find(root);Visual v;
            if(s==null||!visuals.TryGetValue(s,out v)||!v.owned||v.animator==null)return;
            try
            {
                if(!Enable(s,v.animator)){Release(s);return;}
                if(attacking&&!v.attacking)v.animator.SetTrigger("Attack");
                if(roaring&&!v.roaring)v.animator.SetTrigger("Roar");
                v.attacking=attacking;v.roaring=roaring;
            }
            catch(Exception){Release(s);}
        }
        // The active=false packet or stale/discarded stream must call Stop immediately.
        public static void Stop(nyulscript root)
        {
            if(root==null)return;
            var s=RabbitReplicas.Find(root);if(s!=null)Release(s);
        }
        static bool Enable(RabbitReplicas.RabbitReplicaState s,Behaviour candidate)
        {
            Visual v;
            if(!visuals.TryGetValue(s,out v))return false;
            var root=s.root;var a=candidate as Animator;
            if(root==null||a==null||a!=root.anim)return false;
            if(!StableGameBridge.IsRabbitGuest||s.suspended||s.depth!=0||root.AI==null||!root.AI.alive||root.senses==null||!root.senses.alive||!root.isActiveAndEnabled||root.ragdoll==null||root.ragdoll.ragdoll||root.ragdoll.anim!=a||Time.realtimeSinceStartup-v.seen>FreshSeconds)
            {Release(s);return false;}
            int index=s.anims.IndexOf(a);
            if(index<0||!s.animEnabled[index]||a.transform==root.transform||!a.transform.IsChildOf(root.transform))
            {Release(s);return false;}
            var controller=a.runtimeAnimatorController;
            if(controller==null || controller.name!="nyul") {Release(s);return false;}
            if(!v.owned || v.animator!=a || v.controller!=controller)
            {
                if(v.owned){Release(s);return false;}
                // Avoid running unknown StateMachineBehaviour callbacks. Animation events
                // are disabled as a second boundary even for the audited native clips.
                if(a.GetBehaviours<StateMachineBehaviour>().Length!=0){Release(s);return false;}
                v.animator=a;v.controller=controller;v.rootMotion=a.applyRootMotion;v.fireEvents=a.fireEvents;
                a.applyRootMotion=false;a.fireEvents=false;v.owned=true;
                a.enabled=true;
                a.ResetTrigger("Attack");a.ResetTrigger("Roar");
                a.Play("Base Layer.Idle,move",0,0f);
            }
            if(a.applyRootMotion)a.applyRootMotion=false;
            if(a.fireEvents)a.fireEvents=false;
            a.SetFloat("RunValue",Mathf.Clamp01(v.speed*2f));
            return true;
        }
        static void Release(RabbitReplicas.RabbitReplicaState s)
        {
            Visual v;
            if(s==null||!visuals.TryGetValue(s,out v))return;
            visuals.Remove(s);
            if(v.owned&&v.animator!=null)
            {
                // Disable before restoring settings so mutation/role restoration can safely
                // restore the captured native enabled state afterwards.
                v.animator.enabled=false;
                v.animator.applyRootMotion=v.rootMotion;
                v.animator.fireEvents=v.fireEvents;
            }
        }
    }
}


