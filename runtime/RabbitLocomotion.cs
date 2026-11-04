using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Experimental physics controller: gameplay and performance validation is required.
    // Native AI owns sensing and combat. This component owns only living rabbit motion.
    public sealed class RabbitLocomotion : MonoBehaviour
    {
        const int Side=9, Count=Side*Side, Center=4;
        const float Cell=1.25f, MaxStep=.35f, MaxSlope=40f;
        aiscript ai; movecreaturescript motor; Rigidbody body;
        pickupable[] pickups;
        Vector3 target, pending, lastCandidate, searchOrigin, searchTarget, waypoint, progressOrigin;
        Vector3 fleeHeading, pendingFlee, passSide;
        float seenAt, pendingAt, nextSearch, nextProgress, impulseUntil, lastCaptureAt, fleePendingAt, fleeDistance, passUntil;
        Vector3 lastApplied, controlled; bool haveApplied;
        int queries;
        const int QueryLimit=32; bool budgetExhausted;
        readonly Collider[] nearby=new Collider[24];
        readonly RaycastHit[] hits=new RaycastHit[32];
        static readonly int[] dx={1,0,-1,0}, dz={0,1,0,-1};
        bool hasTarget, hasPending, searching, hasWaypoint, fleeActive, fleePending, captureSeen, progressSeen, strikeActive;
        Vector3 strikeHeading;Vector3d strikeTarget;float strikeCommand;
        RabbitCombatReach.Target combatTarget,strikeCombatTarget;float combatGap=-1f;
        readonly int[] queue=new int[Count], parent=new int[Count];
        readonly Vector3[] positions=new Vector3[Count];
        int head,tail,best;
        float radius=.3f, height=.65f;
        Vector3 feetOffset, capsuleAOffset, capsuleBOffset; bool shapeReady, nativeCapsule, wantsMove, groundObserved;
        // Optional bounded aggregate; enabled by the bridge only for requested diagnostics.
        public static Action<string> Diagnostics;
        internal static long PhysicsSteps;
        static float diagAt;
        static int diagTicks,diagTurn,diagBlocked,diagBudget,diagStale,diagShape;
        static readonly List<RabbitLocomotion> diagSeen=new List<RabbitLocomotion>();
        static readonly List<string> diagSamples=new List<string>();
        void Report(string reason)
        {
            if(Diagnostics==null)return;
            diagTicks++;
            if(reason=="turn")diagTurn++;else if(reason=="blocked")diagBlocked++;else if(reason=="budget")diagBudget++;else if(reason=="stale")diagStale++;else if(reason=="shape")diagShape++;
            if(diagSeen.Count<3&&!diagSeen.Contains(this))
            {
                diagSeen.Add(this);
                diagSamples.Add(string.Format("reason={0} moved={1} goToTarget={2} angle={3:F1} dist={4:F1} speed={5:F2} radius={6:F2} height={7:F2} ground={8} queries={9}",reason,ai!=null&&ai.moved,ai!=null&&ai.goToTarget,ai==null?0f:ai.angleFromTarget,ai==null?0f:ai.distFromTarget,body==null?0f:body.velocity.magnitude,radius,height,groundObserved,queries));
            }
            if(diagAt<=0f)diagAt=Time.time+10f;
            if(Time.time<diagAt)return;
            string message=string.Format("[TLD MP rabbit locomotion] ticks={0} turnOnly={1} blocked={2} budget={3} stale={4} shapeFallback={5} samples=[{6}]",diagTicks,diagTurn,diagBlocked,diagBudget,diagStale,diagShape,string.Join(" | ",diagSamples.ToArray()));
            diagAt=Time.time+10f;diagTicks=diagTurn=diagBlocked=diagBudget=diagStale=diagShape=0;diagSeen.Clear();diagSamples.Clear();
            try{Diagnostics(message);}catch(Exception){}
        }

        // Call once after the native AI update. Native AI remains responsible for SeeTarget,
        // roar/attack and choosing a flee, pursuit or roam destination.
        public static void Observe(aiscript value)
        {
            if(value==null || value.nyul==null || value.move==null || StableGameBridge.IsRabbitGuest)return;
            var state=value.GetComponent<RabbitLocomotion>();
            if(state==null)state=value.gameObject.AddComponent<RabbitLocomotion>();
            state.ai=value;state.motor=value.move;state.body=value.move.RB;
            if(state.body!=null)state.UpdateShape();
            state.Capture();
        }
        void Capture()
        {
            float dt=captureSeen?Mathf.Clamp(Time.time-lastCaptureAt,0f,.1f):0f;
            captureSeen=true;lastCaptureAt=seenAt=Time.time;
            wantsMove=ai.moved;
            if(!Eligible())
            {strikeActive=false;strikeCombatTarget=null;combatTarget=null;hasTarget=false;hasPending=false;hasWaypoint=false;searching=false;fleeActive=false;fleePending=false;return;}
            // A committed native strike keeps the heading and the ORIGINAL attack
            // goal captured on the rising edge. While the native flag persists the
            // controller must not adopt a flee/roam destination even if fear goes
            // negative mid-strike.
            if(ai.nyul.attacking)
            {
                if(!strikeActive)
                {
                    Vector3 fwd=body.rotation*Vector3.forward;fwd.y=0f;
                    strikeHeading=fwd.sqrMagnitude<.001f?Vector3.forward:fwd.normalized;
                    strikeTarget=mainscript.GlobalFromUnityPos(ai.interestPos);
                    strikeCommand=Mathf.Clamp01(Mathf.Max(ai.moveSpeed,ai.attackSpeed));
                    strikeActive=true;fleeActive=false;fleePending=false;hasPending=false;Invalidate();
                    var atStrike=RabbitCombatReach.Resolve==null?null:RabbitCombatReach.Resolve(ai);
                    strikeCombatTarget=atStrike!=null&&atStrike.Valid()?atStrike:null;
                }
                return;
            }
            if(strikeActive){strikeActive=false;strikeCombatTarget=null;hasTarget=false;Invalidate();}
            if(!ai.goToTarget)
            {hasTarget=false;hasPending=false;hasWaypoint=false;searching=false;fleeActive=false;fleePending=false;return;}
            Vector3 candidate=ai.interestPos;
            // Native active flee creates a moving point relative to this rabbit every
            // frame. Retaining that point in world space makes the rabbit chase its
            // own old escape destination. Track the escape heading instead.
            if(ai.flee && ai.flightFight<0f)
            {
                Vector3 heading=candidate-body.position;heading.y=0f;
                float distance=heading.magnitude;
                if(distance<.01f){hasTarget=false;return;}
                heading=heading/distance;
                if(!fleeActive)
                {
                    fleeHeading=heading;fleeActive=true;fleePending=false;hasPending=false;Invalidate();
                }
                else
                {
                    float dot=Vector3.Dot(fleeHeading,heading);
                    if(dot<-.25f)
                    {
                        if(!fleePending || Vector3.Dot(pendingFlee,heading)<.8f){pendingFlee=heading;fleePendingAt=Time.time;fleePending=true;}
                        if(Time.time-fleePendingAt>=.3f)fleeHeading=RotateHeading(fleeHeading,heading,dt*3.141593f);
                    }
                    else
                    {
                        fleePending=false;
                        fleeHeading=RotateHeading(fleeHeading,heading,dt*3.141593f);
                    }
                }
                fleeDistance=Mathf.Max(1f,distance);target=body.position+fleeHeading*fleeDistance;hasTarget=true;return;
            }
            if(fleeActive){fleeActive=false;fleePending=false;hasTarget=false;Invalidate();}
            // Follow continuous target motion at a time-based rate. A discontinuous
            // switch must remain consistent briefly before replacing the target.
            if(!hasTarget){target=lastCandidate=candidate;hasTarget=true;hasPending=false;Invalidate();return;}
            float jump=(candidate-lastCandidate).sqrMagnitude;lastCandidate=candidate;
            if(!hasPending && jump<2.25f)
            {target=Vector3.Lerp(target,candidate,Mathf.Clamp01(dt*8f));return;}
            if(!hasPending || jump>=2.25f)
            {pending=candidate;pendingAt=Time.time;hasPending=true;return;}
            pending=candidate;
            if(Time.time-pendingAt>.45f){target=pending;hasPending=false;Invalidate();}
        }
        static Vector3 RotateHeading(Vector3 from,Vector3 to,float maxRadians)
        {
            float cross=from.z*to.x-from.x*to.z;
            float dot=Vector3.Dot(from,to);
            float delta=Mathf.Clamp(Mathf.Atan2(cross,dot),-maxRadians,maxRadians);
            float c=Mathf.Cos(delta),s=Mathf.Sin(delta);
            return new Vector3(from.x*c+from.z*s,0f,from.z*c-from.x*s).normalized;
        }
        bool Eligible()
        {
            if(ai==null || motor==null || body==null || ai.nyul==null || !ai.alive || !ai.isActiveAndEnabled || !motor.isActiveAndEnabled || body.isKinematic || StableGameBridge.IsRabbitGuest)return false;
            if(ai.senses!=null && !ai.senses.alive)return false;
            if(mainscript.s!=null && mainscript.s.AIDisable)return false;
            if(ai.ragdoll!=null && ai.ragdoll.ragdoll)return false;
            if(pickups==null)pickups=ai.GetComponentsInChildren<pickupable>(true);
            for(int i=0;i<pickups.Length;i++)if(pickups[i]!=null && pickups[i].pickedUp)return false;
            return true;
        }
        // Return true to permit the native method; false only when this component owns it.
        public static bool NativeMove(movecreaturescript value)
        {
            if(value==null)return true;
            var s=value.GetComponentInParent<RabbitLocomotion>();
            if(s==null || !s.isActiveAndEnabled || StableGameBridge.IsRabbitGuest)return true;
            if(!s.Eligible()){value.moveDir=Vector3.zero;value.moveRot=0f;return false;}
            if(!s.shapeReady)return true;
            return Time.time-s.seenAt>.5f;
        }
        public static bool NativeJump(movecreaturescript value){return NativeMove(value);}
        public static void Animate(nyulscript rabbit,float nativeSpeed)
        {
            if(rabbit==null)return;
            var s=rabbit.GetComponent<RabbitLocomotion>();
            if(s==null || !s.Eligible() || s.body==null || !s.shapeReady){rabbit.SetAnims(nativeSpeed);return;}
            Vector3 v=s.body.velocity;v.y=0f;
            rabbit.SetAnims(s.motor.speed>0.01f?Mathf.Clamp01(v.magnitude/s.motor.speed):0f);
        }
        void Invalidate(){searching=false;hasWaypoint=false;nextSearch=0f;}
        void OnDisable(){hasTarget=false;hasPending=false;haveApplied=false;fleeActive=false;fleePending=false;strikeActive=false;strikeCombatTarget=null;combatTarget=null;captureSeen=false;progressSeen=false;controlled=Vector3.zero;Invalidate();}
        void FixedUpdate()
        {
            PhysicsSteps++;
            if(!Eligible() || Time.time-seenAt>.5f){Report(Time.time-seenAt>.5f?"stale":"suspended");strikeActive=false;strikeCombatTarget=null;combatTarget=null;hasTarget=false;haveApplied=false;controlled=Vector3.zero;Invalidate();return;}
            UpdateShape();if(!shapeReady){Report("shape");strikeActive=false;strikeCombatTarget=null;combatTarget=null;combatGap=-1f;hasTarget=false;haveApplied=false;controlled=Vector3.zero;Invalidate();return;}
            if(combatTarget!=null&&!combatTarget.Valid()){combatTarget=null;combatGap=-1f;Invalidate();}
            queries=0;budgetExhausted=false;groundObserved=false;
            Vector3 observed=body.velocity;observed.y=0f;
            if(haveApplied && (observed-lastApplied).sqrMagnitude>4f){impulseUntil=Time.time+.35f;controlled=Vector3.zero;}
            if(Time.time<impulseUntil){Report("impulse");haveApplied=false;return;}
            if(controlled.sqrMagnitude>.0001f)
            {Vector3 axis=controlled.normalized;controlled=axis*Mathf.Clamp(Vector3.Dot(observed,axis),0f,controlled.magnitude);}
            if(strikeActive&&ai.nyul!=null&&ai.nyul.attacking)
            {
                combatTarget=strikeCombatTarget;combatGap=-1f;
                if(strikeCombatTarget!=null&&!strikeCombatTarget.Valid()){Brake();Report("attack-lost");return;}
                // Drive/Brake still read `target` for stop-radius and arrival, so the
                // committed global goal must be assigned, not computed locally.
                target=mainscript.UnityPosFromGlobal(strikeTarget);
                Vector3 strikeHere=Feet();
                Vector3 remaining=target-strikeHere;remaining.y=0f;
                bool contact=false;
                if(strikeCombatTarget!=null)
                {
                    var reach=RabbitCombatReach.Measure(ai.nyul,strikeCombatTarget);
                    if(reach.Gap<0f){Brake();Report("attack-lost");return;}
                    contact=reach.Overlapping;combatGap=reach.Approach;
                }
                if(contact||Vector3.Dot(remaining,strikeHeading)<=0f||
                   (strikeCombatTarget==null&&remaining.magnitude<=Mathf.Max(.4f,ai.targetDistGoal)))
                {Turn(strikeHeading);Brake();}
                else Drive(strikeHeading,false,strikeCommand);
                Report("attack");return;
            }
            // Positive-fight pursuit: a resolved canonical combat target overrides
            // the native moved flag and root stop distance, but still uses the
            // normal bounded navigation (Segment/waypoint/ground) below. Overlap
            // is the only hold; approach geometry feeds the final arrival speed.
            var previousCombat=combatTarget;
            combatTarget=float.IsNaN(ai.nyul.size)||float.IsInfinity(ai.nyul.size)||ai.nyul.size<=.5f?null:RabbitCombatReach.Resolve==null?null:RabbitCombatReach.Resolve(ai);
            if((previousCombat==null)!=(combatTarget==null)||
               (previousCombat!=null&&combatTarget!=null&&!ReferenceEquals(previousCombat.Identity,combatTarget.Identity)))Invalidate();
            combatGap=-1f;
            if(combatTarget!=null)
            {
                if(!combatTarget.Valid()){combatTarget=null;}
                else
                {
                    var reach=RabbitCombatReach.Measure(ai.nyul,combatTarget);
                    if(!combatTarget.Valid()||reach.Gap<0f){combatTarget=null;}
                    else
                    {
                        combatGap=reach.Approach;
                        target=combatTarget.Aim();hasTarget=true;wantsMove=true;hasPending=false;
                        if(float.IsNaN(target.x)||float.IsInfinity(target.x)||float.IsNaN(target.y)||float.IsInfinity(target.y)||
                           float.IsNaN(target.z)||float.IsInfinity(target.z)){combatTarget=null;Brake();return;}
                        if(reach.Overlapping){Turn(target-Feet());Brake();Report("contact");return;}
                    }
                }
            }
            if(!hasTarget){Brake();Report("idle");return;}
            Vector3 here=Feet();
            if(fleeActive&&combatTarget==null)target=body.position+fleeHeading*fleeDistance;
            Vector3 direction=target-here;direction.y=0f;
            if(!wantsMove || (combatTarget==null&&direction.magnitude<=Mathf.Max(.4f,ai.targetDistGoal)))
            {Turn(direction);Brake();Invalidate();Report("turn");return;}
            float lookahead=combatTarget!=null?Mathf.Min(4f,Mathf.Max(.05f,combatGap)):
                Mathf.Min(4f,Mathf.Max(0f,direction.magnitude-Mathf.Max(1f,radius*2f+.25f)));
            if(lookahead<.05f||direction.sqrMagnitude<.0001f){Turn(direction);Brake();Invalidate();Report("turn");return;}
            Vector3 direct=here+direction.normalized*lookahead;
            Vector3 goal;
            bool blocked=false;
            if(Segment(here,direct,out goal)){hasWaypoint=false;searching=false;Drive(direction.normalized);}
            else
            {
                blocked=true;
                if(hasWaypoint && ((waypoint-here).sqrMagnitude<.5f || !Segment(here,waypoint,out goal)))hasWaypoint=false;
                if(!hasWaypoint && !searching && Time.time>=nextSearch)BeginSearch(here);
                if(searching)SearchSlice();
                if(hasWaypoint){Vector3 d=waypoint-here;d.y=0;Drive(d.normalized);}else {Turn(direction);Brake();}
            }
            Report(budgetExhausted?"budget":blocked?"blocked":"active");
            if(Time.time>=nextProgress)
            {
                Vector3 progress=here-progressOrigin;progress.y=0f;
                if(progressSeen && progress.sqrMagnitude<.1225f && hasWaypoint){Invalidate();nextSearch=Time.time+.2f;}
                progressOrigin=here;progressSeen=true;nextProgress=Time.time+1.5f;
            }
        }
        Vector3 Feet(){return body.position+feetOffset;}
        void UpdateShape()
        {

            Collider[] cols=ai.ragdoll==null?null:ai.ragdoll.animColliders;
            bool found=false;Vector3 lo=Vector3.zero,hi=Vector3.zero;CapsuleCollider main=null;Collider feet=null;
            if(cols!=null)for(int i=0;i<cols.Length;i++)
            {
                Collider col=cols[i];if(col==null || !col.enabled || col.isTrigger)continue;
                if(col.gameObject.name=="MainCollider")main=col as CapsuleCollider;
                if(col.gameObject.name=="FeetCollider")feet=col;
                Bounds b=col.bounds;
                if(!found){lo=b.min;hi=b.max;found=true;}
                else {lo=Vector3.Min(lo,b.min);hi=Vector3.Max(hi,b.max);}
            }
            shapeReady=found;nativeCapsule=main!=null;
            if(nativeCapsule)
            {
                Vector3 scale=main.transform.lossyScale;
                float axisScale=main.direction==0?Mathf.Abs(scale.x):main.direction==1?Mathf.Abs(scale.y):Mathf.Abs(scale.z);
                float sideScale=main.direction==0?Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)):main.direction==1?Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z)):Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y));
                radius=Mathf.Max(.05f,main.radius*sideScale);height=main.height*axisScale;
                float half=Mathf.Max(0f,main.height*axisScale*.5f-radius);
                Vector3 axis=main.direction==0?Vector3.right:main.direction==1?Vector3.up:Vector3.forward;
                axis=main.transform.TransformDirection(axis).normalized;
                Vector3 center=main.transform.TransformPoint(main.center);
                capsuleAOffset=center-axis*half-body.position+Vector3.up*.05f;
                capsuleBOffset=center+axis*half-body.position+Vector3.up*.05f;
                float floor=feet!=null?feet.bounds.min.y:main.bounds.min.y;
                feetOffset=new Vector3(center.x-body.position.x,floor-body.position.y,center.z-body.position.z);
            }
            else if(found)
            {
                Vector3 center=(lo+hi)*.5f;
                feetOffset=new Vector3(center.x-body.position.x,lo.y-body.position.y,center.z-body.position.z);
                Vector3 ext=(hi-lo)*.5f;
                radius=Mathf.Max(.05f,Mathf.Sqrt(ext.x*ext.x+ext.z*ext.z));
                height=Mathf.Max(radius*2f,hi.y-lo.y);
            }
        }
        void Drive(Vector3 direction,bool avoid=true,float commanded=-1f)
        {
            float speed=Mathf.Max(0f,motor.speed)*(motor.modifier?Mathf.Max(0f,motor.modifierValue):1f);
            float command=commanded>=0f?Mathf.Clamp01(commanded):Mathf.Clamp01(Mathf.Max(ai.moveSpeed, ai.fleeing?ai.minFleeSpeed:ai.walkSpeedModifier));
            Vector3 v=body.velocity, flat=new Vector3(v.x,0,v.z);
            if(avoid)direction=Avoid(direction);
            Turn(direction);
            Vector3 forward=transform.forward;forward.y=0f;forward=forward.normalized;
            float align=Vector3.Dot(forward,direction);
            if(forward.sqrMagnitude<.001f || align<=.15f){Brake();return;}
            Vector3 safeEnd;
            if(budgetExhausted || !Segment(Feet(),Feet()+forward*Mathf.Min(1f,speed*Time.fixedDeltaTime+.35f),out safeEnd)){Brake();return;}
            float turnSpeed=Mathf.Clamp01((align-.15f)/.55f);
            float acceleration=Mathf.Max(2f,Mathf.Min(20f,motor.acceleration));
            Vector3 destination=hasWaypoint?waypoint:target;
            Vector3 remaining=destination-Feet();remaining.y=0f;
            float stopRadius=hasWaypoint?.3f:combatTarget!=null?0f:Mathf.Max(.4f,ai.targetDistGoal);
            float arrival=combatTarget!=null&&!hasWaypoint?combatGap:remaining.magnitude-stopRadius;
            float arrivalSpeed=Mathf.Sqrt(2f*acceleration*Mathf.Max(0f,arrival));
            Vector3 desired=forward*Mathf.Min(speed*command*turnSpeed,arrivalSpeed);
            Vector3 next=Vector3.MoveTowards(controlled,desired,acceleration*Time.fixedDeltaTime);
            Vector3 result=flat+(next-controlled);controlled=next;
            body.velocity=new Vector3(result.x,v.y,result.z);lastApplied=result;haveApplied=true;
            motor.moveDir=Vector3.zero;motor.moveRot=0f;
        }
        void Turn(Vector3 direction)
        {
            direction.y=0f;
            if(direction.sqrMagnitude<.001f)return;
            Quaternion wanted=Quaternion.LookRotation(direction,Vector3.up);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation,wanted,Mathf.Max(0f,motor.rotSpeed)*Time.fixedDeltaTime));
        }
        void Brake()
        {
            Vector3 v=body.velocity, flat=new Vector3(v.x,0,v.z);
            Vector3 next=Vector3.MoveTowards(controlled,Vector3.zero,Mathf.Max(2f,Mathf.Min(20f,motor.acceleration))*Time.fixedDeltaTime);
            flat+=next-controlled;controlled=next;
            body.velocity=new Vector3(flat.x,v.y,flat.z);lastApplied=flat;haveApplied=true;motor.moveDir=Vector3.zero;motor.moveRot=0f;
        }
        bool TakeQuery()
        {
            if(queries>=QueryLimit){budgetExhausted=true;return false;}
            queries++;return true;
        }
        bool Own(Collider c){return c==null || c.transform==transform || c.transform.IsChildOf(transform);}
        bool OtherRabbit(Collider c)
        {
            if(c==null)return false;
            var other=c.GetComponentInParent<RabbitLocomotion>();
            return other!=null && other!=this && other.Eligible();
        }
        Vector3 Avoid(Vector3 direction)
        {
            if(!TakeQuery())return direction;
            int count=Physics.OverlapSphereNonAlloc(body.position,Mathf.Max(1.5f,radius*4f),nearby,~0,QueryTriggerInteraction.Ignore);
            if(count>=nearby.Length){budgetExhausted=true;return direction;}
            float strength=0f;bool encounter=false;
            for(int i=0;i<count;i++)
            {
                Collider c=nearby[i];if(Own(c))continue;
                var other=c.GetComponentInParent<RabbitLocomotion>();
                if(other==null || other==this || !other.Eligible() || other.body==null)continue;
                Vector3 delta=other.body.position-body.position;delta.y=0f;
                float dist=delta.magnitude;
                if(dist<.01f || Vector3.Dot(direction,delta/dist)<-.2f)continue;
                // Both animals choose their own right. Head-on directions are opposite,
                // so the world-space displacements are opposite as well.
                encounter=true;
                strength=Mathf.Max(strength,Mathf.Clamp01((Mathf.Max(1.5f,radius*4f)-dist)/Mathf.Max(.1f,radius*3f)));
            }
            if(!encounter){if(Time.time>=passUntil)passSide=Vector3.zero;return direction;}
            if(Time.time>=passUntil || passSide.sqrMagnitude<.001f){passSide=Vector3.Cross(Vector3.up,direction).normalized;passUntil=Time.time+.7f;}
            Vector3 adjusted=direction+passSide*strength;
            return adjusted.sqrMagnitude>.001f?adjusted.normalized:direction;
        }
        bool CombatExempt(Collider c){return combatTarget!=null&&RabbitCombatReach.Contains(combatTarget,c);}
        bool Ground(Vector3 p,out Vector3 ground)
        {
            ground=p;if(!TakeQuery())return false;int count=Physics.RaycastNonAlloc(p+Vector3.up*MaxStep,Vector3.down,hits,MaxStep*2f,~0,QueryTriggerInteraction.Ignore);if(count>=hits.Length)return false;
            float bestHit=float.MaxValue;bool found=false;
            for(int i=0;i<count;i++)if(!Own(hits[i].collider) && !OtherRabbit(hits[i].collider) && !RabbitContact.IgnoreForNavigation(hits[i].collider) && !CombatExempt(hits[i].collider) && hits[i].point.y<=p.y+MaxStep && hits[i].point.y>=p.y-MaxStep && hits[i].distance<bestHit)
            {bestHit=hits[i].distance;ground=hits[i].point;found=Vector3.Angle(hits[i].normal,Vector3.up)<=MaxSlope;}
            if(found)groundObserved=true;
            return found;
        }
        bool Segment(Vector3 from,Vector3 to,out Vector3 end)
        {
            end=to;Vector3 d=to-from;d.y=0;float length=d.magnitude;
            if(length<.01f)return true;
            Vector3 previous;if(!Ground(from,out previous))return false;
            if(!TakeQuery())return false;
            float initialTop=Mathf.Max(radius,height-radius);
            Vector3 startA=nativeCapsule?previous-feetOffset+capsuleAOffset:previous+Vector3.up*(radius+.05f);
            Vector3 startB=nativeCapsule?previous-feetOffset+capsuleBOffset:previous+Vector3.up*(initialTop+.05f);
            int overlap=Physics.OverlapCapsuleNonAlloc(startA,startB,radius,nearby,~0,QueryTriggerInteraction.Ignore);
            if(overlap>=nearby.Length)return false;
            for(int j=0;j<overlap;j++)if(!Own(nearby[j])&&!OtherRabbit(nearby[j])&&!RabbitContact.IgnoreForNavigation(nearby[j])&&!CombatExempt(nearby[j]))return false;
            int steps=Mathf.CeilToInt(length/.5f);
            for(int i=1;i<=steps;i++)
            {
                Vector3 sample=Vector3.Lerp(from,to,(float)i/steps),g;
                sample.y=previous.y;
                if(!Ground(sample,out g) || Mathf.Abs(g.y-previous.y)>MaxStep)return false;
                Vector3 travel=g-previous;float distance=travel.magnitude;
                if(distance>.001f)
                {
                    if(!TakeQuery())return false;
                    float r=radius, top=Mathf.Max(radius,height-radius);
                    Vector3 bottom=nativeCapsule?previous-feetOffset+capsuleAOffset:previous+Vector3.up*(r+.05f);
                    Vector3 upper=nativeCapsule?previous-feetOffset+capsuleBOffset:previous+Vector3.up*(top+.05f);
                    int count=Physics.CapsuleCastNonAlloc(bottom,upper,r,travel/distance,hits,distance,~0,QueryTriggerInteraction.Ignore);
                    if(count>=hits.Length)return false;
                    for(int j=0;j<count;j++)if(!Own(hits[j].collider) && !OtherRabbit(hits[j].collider) && !RabbitContact.IgnoreForNavigation(hits[j].collider) && !CombatExempt(hits[j].collider))return false;
                }
                previous=g;
            }
            end=new Vector3(to.x,previous.y,to.z);return true;
        }
        void BeginSearch(Vector3 here)
        {
            searchOrigin=here;searchTarget=target;head=0;tail=0;best=Center*Side+Center;
            for(int i=0;i<Count;i++)parent[i]=-2;
            parent[best]=-1;positions[best]=here;queue[tail++]=best;searching=true;
        }
        void SearchSlice()
        {
            if((Feet()-searchOrigin).sqrMagnitude>Cell*Cell || (target-searchTarget).sqrMagnitude>4f){Invalidate();return;}
            int budget=1;
            while(budget-->0 && head<tail)
            {
                int n=queue[head++],x=n%Side,z=n/Side;
                if((positions[n]-searchTarget).sqrMagnitude<(positions[best]-searchTarget).sqrMagnitude)best=n;

                for(int k=0;k<4;k++)
                {
                    int nx=x+dx[k],nz=z+dz[k];if(nx<0||nz<0||nx>=Side||nz>=Side)continue;
                    int j=nz*Side+nx;if(parent[j]!=-2)continue;
                    Vector3 p=searchOrigin+new Vector3((nx-Center)*Cell,0,(nz-Center)*Cell),g;p.y=positions[n].y;
                    if(!Segment(positions[n],p,out g)){if(budgetExhausted){head--;return;}continue;}
                    parent[j]=n;positions[j]=g;queue[tail++]=j;
                }
            }
            if(head<tail)return;
            searching=false;nextSearch=Time.time+.4f;
            if(best==Center*Side+Center)return;
            int step=best;while(parent[step]>=0 && parent[step]!=Center*Side+Center)step=parent[step];
            waypoint=positions[step];hasWaypoint=true;
        }
    }
}



















