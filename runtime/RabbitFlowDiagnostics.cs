using System;
using System.Collections.Generic;
using UnityEngine;
namespace TldMp
{
    // Measures native decisions independently of the locomotion component.
    internal static class RabbitFlowDiagnostics
    {
        static long observations,scans,contacts,localContacts,remoteRequests,lastPhysics;
        static double next;
        internal static void Reset(){observations=scans=contacts=localContacts=remoteRequests=0;next=0;lastPhysics=RabbitLocomotion.PhysicsSteps;}
        internal static void Observe(aiscript ai){if(ai!=null&&ai.nyul!=null)observations++;}
        internal static void Scan(){scans++;}
        internal static void Contact(playerdeathtriggerscript trigger){contacts++;if(trigger!=null&&trigger.pscript!=null&&trigger.damagePlayer>0)localContacts++;}
        internal static void RemoteRequest(){remoteRequests++;}
        internal static void Tick(bool enabled,bool host,savedatascript data,double now)
        {
            if(!enabled||!host||data==null||data.items==null||now<next)return;
            next=now+10;
            try
            {
                int total=0,alive=0,controlled=0,hostile=0,fighting=0,fleeing=0,attacking=0;
                var examples=new List<string>();
                foreach(var pair in data.items)
                {
                    var item=pair.Value;if(item==null)continue;
                    var root=item.nyul;
                    if(root==null&&item.ragdollPos!=null)root=item.GetComponent<nyulscript>();
                    if(root==null||root.AI==null)continue;
                    var ai=root.AI;total++;if(ai.alive)alive++;if(ai.alive&&ai.enableInvert)hostile++;
                    bool has=root.GetComponent<RabbitLocomotion>()!=null;if(has)controlled++;
                    if(ai.alive&&ai.flightFight>0)fighting++;if(ai.alive&&ai.flightFight<0)fleeing++;if(ai.alive&&root.attacking)attacking++;
                    if(examples.Count<3&&ai.alive&&ai.enableInvert)
                        examples.Add(string.Format("id={0} evil={1} fight={2:F2} stim={3} go={4} moved={5} angle={6:F1} dist={7:F2} roar={8} attack={9} controller={10} size={11:F2} triggerActive={12} triggerCollider={13} damageRate={14:F1} aiEnabled={15} kinematic={16}",pair.Key,ai.enableInvert,ai.flightFight,ai.senses==null?-1:ai.senses.stimuls.Count,ai.goToTarget,ai.moved,ai.angleFromTarget,ai.distFromTarget,root.roaring,root.attacking,has,root.size,root.playerDeathTrigger!=null&&root.playerDeathTrigger.activeInHierarchy,root.attackTriggerScript!=null&&root.attackTriggerScript.selfCol!=null&&root.attackTriggerScript.selfCol.enabled,root.attackTriggerScript==null?0:root.attackTriggerScript.damagePlayer,ai.isActiveAndEnabled,root.move!=null&&root.move.RB!=null&&root.move.RB.isKinematic));
                }
                long physics=RabbitLocomotion.PhysicsSteps;
                Debug.Log(string.Format("[TLD MP rabbit flow] rabbits={0} alive={1} evil={2} controllers={3} fighting={4} fleeing={5} attacking={6} nativeObservations={7} scans={8} physicsSteps={9} contacts={10} remoteDamageRequests={11} localPlayerContacts={12} aiDisabled={14} [{13}]",total,alive,hostile,controlled,fighting,fleeing,attacking,observations,scans,physics-lastPhysics,contacts,remoteRequests,localContacts,string.Join(" | ",examples.ToArray()),mainscript.s!=null&&mainscript.s.AIDisable));
                observations=scans=contacts=localContacts=remoteRequests=0;lastPhysics=physics;
            }
            catch(Exception e){Debug.LogWarning("[TLD MP rabbit flow] diagnostic failed: "+e.GetType().Name);}
        }
    }
}
