using System;
using UnityEngine;
namespace TldMp
{
    // Combat reach geometry for canonical rabbits: the actual native attack
    // trigger sphere vs the exact physical colliders of ONE resolved combat
    // target. A positive surface gap is a movement input only; an actual
    // overlap (ComputePenetration) is the attack-entry gate. Neither is damage
    // authorization, which stays native/trigger-routed.
    public static class RabbitCombatReach
    {
        internal sealed class Target
        {
            internal Collider[] Colliders;
            internal Func<bool> Valid;
            internal Func<Vector3> Aim;
            internal object Identity;
        }
        internal static Func<aiscript,Target> Resolve;
        static bool Fin(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        static bool Fin(Vector3 v){return Fin(v.x)&&Fin(v.y)&&Fin(v.z);}
        // The native attack volume may sit on an inactive child before the
        // strike begins; its transform/scene matrix still describes the
        // geometry, so compute against it directly, never enable/move it.
        internal struct Result{internal float Gap,Approach;internal bool Overlapping;}
        internal static Result Measure(nyulscript rabbit,Target t)
        {
            var r=new Result{Gap=-1f,Approach=-1f};
            if(rabbit==null||t==null||t.Valid==null||t.Aim==null||!t.Valid())return r;
            Vector3 center;float radius;
            if(!Sphere(rabbit,out center,out radius))return r;
            if(t.Colliders==null)return r;
            float best=float.MaxValue;
            var sp=(Collider)rabbit.attackTriggerScript.selfCol;Vector3 dir;float depth;
            foreach(var c in t.Colliders)
            {
                if(!Usable(c))continue;
                var near=c.ClosestPoint(center);
                if(!Fin(near)||!t.Valid())return new Result{Gap=-1f,Approach=-1f};
                float gap=(near-center).magnitude-radius;
                if(!Fin(gap))return new Result{Gap=-1f,Approach=-1f};
                best=Mathf.Min(best,gap);
                if(Physics.ComputePenetration(sp,sp.transform.position,sp.transform.rotation,
                    c,c.transform.position,c.transform.rotation,out dir,out depth)&&Fin(depth)&&depth>0f)
                    r.Overlapping=true;
                if(!t.Valid())return new Result{Gap=-1f,Approach=-1f};
            }
            if(best==float.MaxValue)return r;
            r.Gap=Mathf.Max(0f,best);
            // Approach just inside the existing sphere (never enlarges it); a
            // moving target uses the surface gap toward that tiny in-volume point.
            r.Approach=r.Overlapping?0f:r.Gap+Mathf.Min(.02f,radius*.05f);
            if(!t.Valid())return new Result{Gap=-1f,Approach=-1f};
            return r;
        }
        static bool Sphere(nyulscript rabbit,out Vector3 center,out float radius)
        {
            center=Vector3.zero;radius=0;
            var trigger=rabbit==null?null:rabbit.attackTriggerScript;
            var col=trigger==null?null:trigger.selfCol as SphereCollider;
            if(col==null||!col.enabled||!col.isTrigger)return false;
            var s=col.transform.lossyScale;
            if(!Fin(s)||!Fin(col.center)||!Fin(col.radius)||col.radius<=0f)return false;
            float side=Mathf.Max(Mathf.Abs(s.x),Mathf.Max(Mathf.Abs(s.y),Mathf.Abs(s.z)));
            center=col.transform.TransformPoint(col.center);radius=col.radius*side;
            return Fin(center)&&Fin(radius)&&radius>0f;
        }
        static bool Usable(Collider c)
        {
            return c!=null&&c.enabled&&!c.isTrigger&&c.gameObject.activeInHierarchy&&Fin(c.transform.position)&&Fin(c.bounds.center);
        }
        // Minimum positive surface gap between the native attack sphere and any
        // validated target collider member. Returns -1 when no usable target
        // geometry exists; zero is reserved for touching/overlapping.
        internal static float Gap(nyulscript rabbit,Target t)
        {
            return Measure(rabbit,t).Gap;
        }
        static bool Clear(nyulscript rabbit,Target t,Collider victim,Vector3 center)
        {
            var near=victim.ClosestPoint(center);
            if(!Fin(near)||!t.Valid())return false;
            var delta=near-center;float distance=delta.magnitude;
            if(distance<=.000001f)return t.Valid();
            var hits=Physics.RaycastAll(center,delta/distance,distance,~0,QueryTriggerInteraction.Ignore);
            if(!t.Valid())return false;
            if(hits!=null)foreach(var hit in hits)
            {
                var c=hit.collider;
                if(c==null||c.isTrigger||!c.enabled||!c.gameObject.activeInHierarchy)continue;
                if(c.transform.IsChildOf(rabbit.transform)||Contains(t,c))continue;
                if(!Fin(hit.distance)||hit.distance<distance)return false;
            }
            return t.Valid();
        }
        // Attack entry: an actual canonical target AND real geometric overlap.
        // Small living rabbits keep native harmless animation behaviour.
        internal static bool AllowAttack(nyulscript rabbit)
        {
            if(rabbit==null||rabbit.AI==null||!rabbit.AI.alive||!rabbit.AI.isActiveAndEnabled||
               rabbit.ragdoll==null||rabbit.ragdoll.ragdoll||rabbit.senses==null||!rabbit.senses.alive||
               !rabbit.gameObject.activeInHierarchy)return false;
            if(!Fin(rabbit.size))return false;
            if(rabbit.size<=.5f)return true;
            var t=Resolve==null?null:Resolve(rabbit.AI);
            if(t==null||t.Valid==null||!t.Valid())return false;
            Vector3 center;float radius;
            if(!Sphere(rabbit,out center,out radius))return false;
            var cols=t.Colliders;if(cols==null)return false;
            Vector3 dir;float depth;
            foreach(var c in cols)
            {
                if(!Usable(c)||!t.Valid())continue;
                var sp=(Collider)rabbit.attackTriggerScript.selfCol;
                if(Physics.ComputePenetration(sp,sp.transform.position,sp.transform.rotation,
                    c,c.transform.position,c.transform.rotation,out dir,out depth)&&Fin(depth)&&depth>0f)
                {
                    if(t.Valid()&&Clear(rabbit,t,c,center))return true;
                }
            }
            return false;
        }
        // Navigation exemption: only the exact validated collider members of an
        // active combat target are transparent to path queries.
        internal static bool Contains(Target t,Collider c)
        {
            if(t==null||c==null||t.Colliders==null||t.Valid==null||!t.Valid())return false;
            var cols=t.Colliders;
            for(int i=0;i<cols.Length;i++)if(ReferenceEquals(cols[i],c))return t.Valid()&&Usable(c);
            return false;
        }
    }
}
