using System;
using UnityEngine;

namespace TldMp
{
    // Unity-facing half of the join spawn clearance rule (SpawnClearance.cs holds
    // the pure policy). Kept out of NativeSafetyAdapter.cs so that file still
    // compiles against the offline stand-ins in tests\NativeSafetyAdapterTests.cs.
    public static partial class StableGameBridge
    {
        static readonly Collider[] spawnOverlapHits=new Collider[128];
        static System.Collections.Generic.List<double[]> spawnOffsets;
        static int spawnClearanceState;
        // TLD_MP_SPAWN_CLEARANCE=0 releases the join hold without the clearance rule.
        static bool SpawnClearanceEnabled
        {
            get
            {
                if(spawnClearanceState==0)spawnClearanceState=Environment.GetEnvironmentVariable("TLD_MP_SPAWN_CLEARANCE")=="0"?2:1;
                return spawnClearanceState==1;
            }
        }
        // The player's standing capsule in world space at anchor position "at":
        // taken from its non-trigger CapsuleCollider (else the colliders' bounds),
        // radius shrunk slightly and lifted off the floor so ground contact
        // under the feet does not count.
        static bool SpawnCapsule(fpscontroller player,Vector3 at,out Vector3 p0,out Vector3 p1,out float radius)
        {
            p0=p1=at;radius=0;
            if(player==null || player.C==null)return false;
            Vector3 shift=at-player.transform.position;
            foreach(Collider c in player.C)
            {
                var capsule=c as CapsuleCollider;
                if(capsule==null || !capsule.enabled || capsule.isTrigger)continue;
                Vector3 scale=capsule.transform.lossyScale;
                radius=capsule.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));
                float half=Mathf.Max(capsule.height*Mathf.Abs(scale.y)*.5f,radius);
                Vector3 center=capsule.transform.TransformPoint(capsule.center)+shift;
                p0=center-Vector3.up*(half-radius);p1=center+Vector3.up*(half-radius);
                break;
            }
            if(radius<=0)
            {
                bool any=false;Bounds b=new Bounds();
                foreach(Collider c in player.C)
                    if(c!=null && c.enabled && !c.isTrigger){if(!any){b=c.bounds;any=true;}else b.Encapsulate(c.bounds);}
                if(!any)return false;
                radius=Mathf.Min(Mathf.Max(b.extents.x,b.extents.z),.6f);
                Vector3 center=b.center+shift;float half=Mathf.Max(b.extents.y,radius);
                p0=center-Vector3.up*(half-radius);p1=center+Vector3.up*(half-radius);
            }
            radius=Mathf.Max(.05f,radius-.02f);
            p0+=Vector3.up*.15f;if(p1.y<p0.y)p1=p0;
            return true;
        }
        // The capsule is lifted off the floor, so even terrain or roads that
        // overlap it indicate unsafe penetration into the standing body.
        // True only when no solid collider overlaps the standing space.
        static bool SpawnPointClear(fpscontroller player,Vector3 at,out string blocker)
        {
            blocker="none";
            Vector3 p0,p1;float radius;
            if(!SpawnCapsule(player,at,out p0,out p1,out radius)){blocker="player collider unavailable";return false;}
            int count=Physics.OverlapCapsuleNonAlloc(p0,p1,radius,spawnOverlapHits,~0,QueryTriggerInteraction.Ignore);
            Collider[] hits=spawnOverlapHits;
            if(count==hits.Length){hits=Physics.OverlapCapsule(p0,p1,radius,~0,QueryTriggerInteraction.Ignore);count=hits.Length;}
            Transform self=player.transform;bool clear=true;
            for(int i=0;i<count;i++)
            {
                Collider c=hits[i];hits[i]=null;
                if(c==null)continue;
                bool isSelf=c.transform.IsChildOf(self);
                if(!SpawnClearancePolicy.Blocks(c.isTrigger,isSelf,false,c.enabled))continue;
                if(clear){clear=false;blocker=c.name+(c.attachedRigidbody!=null?"(body)":"");}
            }
            return clear;
        }
        // Nearest clear standing spot on rings around "at", or false.
        static bool FindFreeSpawn(fpscontroller player,Vector3 at,out Vector3 free)
        {
            free=at;
            if(spawnOffsets==null)spawnOffsets=SpawnClearancePolicy.Offsets(SpawnClearancePolicy.RingStep,SpawnClearancePolicy.MaxRadius);
            float footY=at.y+spawnFootOffset;Transform self=player.transform;float rayLength=1.5f+(float)SpawnClearancePolicy.MaxHeightChange+1f;
            for(int n=-1;n<spawnOffsets.Count;n++)
            {
                // Check directly under the held player first, then nearby rings.
                // Starting above the feet also detects a floor slightly above them.
                float dx=n<0?0f:(float)spawnOffsets[n][0], dz=n<0?0f:(float)spawnOffsets[n][1];
                Vector3 origin=new Vector3(at.x+dx,footY+1.5f,at.z+dz);
                int count=Physics.RaycastNonAlloc(origin,Vector3.down,spawnSupportHits,rayLength,~0,QueryTriggerInteraction.Ignore);
                RaycastHit[] hits=spawnSupportHits;
                if(count==hits.Length){hits=Physics.RaycastAll(origin,Vector3.down,rayLength,~0,QueryTriggerInteraction.Ignore);count=hits.Length;}
                int best=-1;
                for(int i=0;i<count;i++)
                {
                    Collider c=hits[i].collider;
                    if(c==null || !c.enabled || c.transform.IsChildOf(self))continue;
                    if(best<0 || hits[i].distance<hits[best].distance)best=i;
                }
                if(best<0)continue;
                RaycastHit ground=hits[best];
                if(!SpawnClearancePolicy.UsableGround(ground.normal.y,ground.collider.attachedRigidbody!=null,ground.point.y-footY))continue;
                Vector3 candidate=ground.point-Vector3.up*spawnFootOffset+Vector3.up*.05f;
                string blocker;
                if(SpawnPointClear(player,candidate,out blocker)){free=candidate;return true;}
            }
            return false;
        }
    }
}
