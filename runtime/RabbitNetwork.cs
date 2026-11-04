using UnityEngine;

namespace TldMp
{
    public static class RagdollSafety
    {
        public static void SetVelocity(Rigidbody body,Vector3 value)
        {
            if(body.isKinematic)return;
            body.velocity=value;
        }
        public static void SetAngularVelocity(Rigidbody body,Vector3 value)
        {
            if(body.isKinematic)return;
            body.angularVelocity=value;
        }
    }

    public static class RabbitNetworkPolicy
    {
        public static bool IsHostStateType(byte type)
        {
            return type==16 || type==17 || type==18 || type==19 || type==29 || type==33 || type==36 || type==42 || type==43;
        }
        public static bool AllowState(bool connected,bool rabbit,byte type,ulong origin,ulong host)
        {
            if(!connected || !rabbit || !IsHostStateType(type))return true;
            return origin!=0 && host!=0 && origin==host;
        }
    }

    public static partial class StableGameBridge
    {
        public static void RagdollVelocity(Rigidbody body,Vector3 value){RagdollSafety.SetVelocity(body,value);}
        public static void RagdollAngularVelocity(Rigidbody body,Vector3 value){RagdollSafety.SetAngularVelocity(body,value);}
        internal static bool IsRabbitGuest{get{return Connected && (loopbackMode?!Loopback.IsHost:!syncScript.IsHost());}}
        public static bool RabbitAiGuard(aiscript ai){return !RabbitReplicas.GuestAi(ai);}
        public static bool RabbitScanGuard(sensoryscript senses){return !RabbitReplicas.GuestScan(senses);}
        internal static System.Func<movecreaturescript,bool> RabbitMotionGuard;
        public static bool RabbitRotGuard(movecreaturescript creature){return !RabbitReplicas.GuestRot(creature)&&(RabbitMotionGuard==null||RabbitMotionGuard(creature));}
        public static bool RabbitNyulGuard(nyulscript nyul){return !RabbitReplicas.GuestSimple(nyul);}
        // Attack entry gate lives on the bridge (RabbitAttackGate) so this file
        // stays compilable standalone in fixture recipes.
        public static bool RabbitTriggerGuard(playerdeathtriggerscript trigger){return !RabbitReplicas.GuestSimple(trigger);}
        public static void BeforeRabbitRagdoll(ragdollactivatescript ragdoll){RabbitReplicas.BeginMutation(ragdoll);}
        public static void BeforeRabbitCollider(pickupable item){RabbitReplicas.BeginMutation(item);}
        public static void AfterRabbitCollider(pickupable item){RabbitReplicas.EndMutation(item);}
        public static void BeforeRabbitPose(ragdollchildpos pos){RabbitReplicas.BeforePose(pos);}
        public static void RabbitUnfreeze(jointHelpScript joint){RabbitReplicas.AfterUnfreeze(joint);}
        public static void AfterRabbitInit(tosaveitemscript item){RabbitReplicas.ObserveInit(item);}
        public static void BeforeRabbitRecRagdoll(byte[] bytes)
        {
            if(!IsRabbitGuest||bytes==null||bytes.Length<5||bytes[0]!=43)return;
            tosaveitemscript item;
            if(!Find(StablePackets.Item(bytes),out item))return;
            var root=RabbitReplicas.RabbitRoot(item);
            if(root!=null)RabbitReplicas.Acquire(root);
        }
        internal static bool AllowRabbitState(ulong origin,byte[] bytes)
        {
            if(!Connected || node==null || bytes==null || bytes.Length==0)return true;
            byte type=bytes[0];
            if(!RabbitNetworkPolicy.IsHostStateType(type))return true;
            if(bytes.Length<(type==18?14:type==29?13:5))return true;
            tosaveitemscript item;
            if(!Find(StablePackets.Item(bytes),out item) || item==null)return true;
            bool rabbit=item.nyul!=null || (item.ragdollPos!=null && item.GetComponent<nyulscript>()!=null);
            return RabbitNetworkPolicy.AllowState(Connected,rabbit,type,origin,node.Host);
        }
    }
}
