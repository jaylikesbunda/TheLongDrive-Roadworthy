using System;
using System.Collections.Generic;
using UnityEngine;
namespace TldMp
{
    // Read-only instrumentation: known actor colliders only. Never creates a
    // contact, changes authority, or invokes damage.
    internal static class RabbitAttackDiagnostics
    {
        const int ScanBudget=128,ActorBudget=32,ColliderBudget=32,MaxReports=4;
        const double Interval=.5;
        static Dictionary<uint,tosaveitemscript>.Enumerator scan;
        static savedatascript source;static bool scanning;
        static readonly Dictionary<uint,double> nextReport=new Dictionary<uint,double>();
        sealed class Seen{internal bool Alive;internal Vector3 Velocity;internal float Health;}
        static readonly Dictionary<uint,Seen> seenAlive=new Dictionary<uint,Seen>();
        static double window;static int reports;static bool warned;
        sealed class Result
        {
            internal string Name="none",OverlapActor="none";internal float Distance=float.PositiveInfinity;
            internal int Checked,Overlaps,Ignored,Inactive,NoBody,HasSurvival,OverlapIgnored,OverlapNoBody,OverlapSurvival;
        }
        internal static void Reset(){source=null;scanning=false;nextReport.Clear();seenAlive.Clear();window=0;reports=0;warned=false;}
        internal static void Tick(bool enabled,StableNode node,savedatascript save,fpscontroller player,double now)
        {
            if(!enabled||node==null||!node.IsHost||!node.Compatible||node.Error!=null||save==null||save.items==null||player==null||!player.loadedmove||double.IsNaN(now)||double.IsInfinity(now))
            {if(source!=null)Reset();return;}
            if(source!=save){Reset();source=save;}
            if(now<window||now-window>=1){window=now;reports=0;}
            if(!scanning){scan=save.items.GetEnumerator();scanning=true;}
            try
            {
                for(int n=0;n<ScanBudget;n++)
                {
                    if(!scan.MoveNext()){scanning=false;break;}
                    var pair=scan.Current;var item=pair.Value;
                    if(item==null||pair.Key==0||item.idInSave!=pair.Key)continue;
                    var rabbit=RabbitReplicas.RabbitRoot(item);
                    if(rabbit==null||rabbit.AI==null||rabbit.senses==null)continue;
                    // Bounded per-ID stage telemetry: living hostile pursuit,
                    // fight or flee gets the contact probe; a death transition
                    // is recorded once with prior velocity and health.
                    bool alive=rabbit.senses.alive;
                    Seen seen;
                    if(!seenAlive.TryGetValue(pair.Key,out seen))seenAlive[pair.Key]=seen=new Seen();
                    if(alive)
                    {
                        seen.Alive=true;
                        var rb=rabbit.move==null?null:rabbit.move.RB;
                        if(rb!=null)seen.Velocity=rb.velocity;
                        if(rabbit.breakable!=null)seen.Health=rabbit.breakable.health;
                    }
                    else if(seen.Alive)
                    {
                        seen.Alive=false;
                        var rb2=rabbit.move==null?null:rabbit.move.RB;
                        var cur=rb2==null?Vector3.zero:rb2.velocity;
                        float health=rabbit.breakable==null?0f:rabbit.breakable.health;
                        Debug.Log(string.Format("[TLD MP rabbit death probe] id={0} priorV=({1:F2},{2:F2},{3:F2}) currentV=({4:F2},{5:F2},{6:F2}) health={7:F2} reason=observed",
                            pair.Key,seen.Velocity.x,seen.Velocity.y,seen.Velocity.z,cur.x,cur.y,cur.z,health));
                        continue;
                    }
                    else continue;
                    if(!rabbit.attacking&&rabbit.AI.flightFight==0f)continue;
                    double due;if(nextReport.TryGetValue(pair.Key,out due)&&now<due)continue;
                    if(reports>=MaxReports)continue;
                    if(nextReport.Count>=64&&!nextReport.ContainsKey(pair.Key))nextReport.Clear();
                    nextReport[pair.Key]=now+Interval;reports++;
                    Report(item,rabbit,node,player);
                }
            }
            catch(InvalidOperationException){scanning=false;}
            catch(Exception e){scanning=false;if(!warned){warned=true;Debug.LogWarning("[TLD MP rabbit attack probe] "+e.GetType().Name);}}
        }
        static void Inspect(Result r,Collider trigger,Collider c,string name)
        {
            if(c==null)return;
            if(!c.enabled||!c.gameObject.activeInHierarchy){r.Inactive++;return;}
            r.Checked++;
            if(c.attachedRigidbody==null)r.NoBody++;
            if(c.GetComponentInParent<survivalscript>()!=null)r.HasSurvival++;
            bool ignored=Physics.GetIgnoreLayerCollision(trigger.gameObject.layer,c.gameObject.layer)||Physics.GetIgnoreCollision(trigger,c);
            if(ignored)r.Ignored++;
            var center=trigger.bounds.center;var delta=c.ClosestPoint(center)-center;
            float distance=delta.magnitude;
            if(distance<r.Distance){r.Distance=distance;r.Name=name;}
            Vector3 direction;float depth;
            if(trigger.enabled&&trigger.gameObject.activeInHierarchy&&
               Physics.ComputePenetration(trigger,trigger.transform.position,trigger.transform.rotation,c,c.transform.position,c.transform.rotation,out direction,out depth))
            {
                r.Overlaps++;if(r.OverlapActor=="none")r.OverlapActor=name;
                if(ignored)r.OverlapIgnored++;if(c.attachedRigidbody==null)r.OverlapNoBody++;
                if(c.GetComponentInParent<survivalscript>()!=null)r.OverlapSurvival++;
            }
        }
        static void InspectActor(Result r,Collider trigger,Collider[] cols,Collider body,string label)
        {
            int count=0;bool hasBody=false;
            if(cols!=null)foreach(var c in cols)
            {if(count++>=ColliderBudget)break;if(c==body)hasBody=true;Inspect(r,trigger,c,label);}
            if(!hasBody&&count<ColliderBudget)Inspect(r,trigger,body,label);
        }
        static void Report(tosaveitemscript item,nyulscript rabbit,StableNode node,fpscontroller player)
        {
            var trigger=rabbit.attackTriggerScript;var col=trigger==null?null:trigger.selfCol;var ai=rabbit.AI;
            if(col==null)
            {
                Debug.Log("[TLD MP rabbit attack probe] id="+item.idInSave+" attack=True trigger=missing");return;
            }
            var result=new Result();InspectActor(result,col,player.C,player.CBody,"local");
            int actors=0;foreach(var peer in node.Peers)
            {
                if(actors++>=ActorBudget)break;RabbitPlayers.Actor actor;
                if(!RabbitPlayers.TryGet(peer.Key,out actor))continue;
                InspectActor(result,col,actor.Mp.cols,actor.Mp.CBody,"guest:"+actor.Id);
            }
            var sphere=col as SphereCollider;float radius=sphere==null?-1:sphere.radius;
            var scale=col.transform.lossyScale;float maxScale=Mathf.Max(Mathf.Abs(scale.x),Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
            Debug.Log(string.Format("[TLD MP rabbit attack probe] id={0} size={1:F2} fight={2:F2} targetDist={3:F2} attack={4} triggerActive={5} triggerEnabled={6} triggerIsTrigger={7} radius={8:F3} worldScale={9:F3} triggerRB={10} closest={11} centerToCollider={12:F3} checked={13} inactive={14} overlapping={15} ignored={16} actorNoRB={17} survivalColliders={18} overlapActor={19} overlapIgnored={20} overlapNoRB={21} overlapSurvival={22}",
                item.idInSave,rabbit.size,ai.flightFight,ai.distFromTarget,rabbit.attacking,col.gameObject.activeInHierarchy,col.enabled,col.isTrigger,radius,maxScale,col.attachedRigidbody!=null,result.Name,result.Distance,result.Checked,result.Inactive,result.Overlaps,result.Ignored,result.NoBody,result.HasSurvival,result.OverlapActor,result.OverlapIgnored,result.OverlapNoBody,result.OverlapSurvival));
        }
    }
}
