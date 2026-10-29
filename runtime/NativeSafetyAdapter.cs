using System;
using System.Collections;
using UnityEngine;

namespace TldMp
{
    // Narrow adapters around native state. No ownership, simulation writer,
    // vehicle pose stream or physics timestep is replaced here.
    public static partial class StableGameBridge
    {
        static readonly NativeSpawnSafety spawnSafety=new NativeSpawnSafety();
        static readonly NativeSeatSafety seatSafety=new NativeSeatSafety();
        static readonly NativeAttachmentGuard attachmentSafety=new NativeAttachmentGuard();
        static fpscontroller spawnPlayer;
        static Rigidbody spawnBody;
        static Vector3d spawnGlobal;
        static float spawnFootOffset,spawnSupportFixedTime,spawnGroundSearchAt,spawnCandidateSince;
        static bool spawnCandidateValid;
        static bool spawnFinished,spawnSupportWasPresent;
        static ulong safetyWorld;
        static int seatCaptureFrame;
        static readonly RaycastHit[] spawnSupportHits=new RaycastHit[64];

        static void BeginNativeSafetySession(ulong identity)
        {
            safetyWorld=identity;attachmentSafety.BeginSession(identity);
            spawnFinished=false;spawnSupportWasPresent=false;seatSafety.Clear();
        }
        static void ResetNativeSafety()
        {
            RestoreSpawnBody();spawnSafety.Abort();spawnBody=null;spawnPlayer=null;
            spawnFinished=false;spawnSupportWasPresent=false;seatSafety.Clear();
            attachmentSafety.EndSession();safetyWorld=0;
        }
        static void BeforeNativeSafetyWorldReplacement()
        {
            // The same IDs and even the same Unity objects can survive a
            // resnapshot. Retire retries from the previous world baseline.
            if(safetyWorld!=0)attachmentSafety.BeginSession(safetyWorld);
        }
        static void RestoreSpawnBody()
        {
            if(spawnBody==null || !spawnSafety.Active)return;
            spawnBody.isKinematic=spawnSafety.CapturedIsKinematic;
            spawnBody.useGravity=spawnSafety.CapturedUseGravity;
        }
        static bool ShouldProtectSpawn()
        {
            return Connected && node!=null && !node.IsHost && node.Error==null &&
                mainscript.s!=null && mainscript.s.player!=null && menuhandler.s!=null &&
                menuhandler.s.currentMainMap!=null && !spawnFinished;
        }
        static void StartSpawnHold(fpscontroller player)
        {
            if(player==null || player.RB==null || player.Bsitting || player.died)return;
            spawnPlayer=player;spawnBody=player.RB;
            spawnGlobal=mainscript.GlobalFromUnityPos(player.transform.position);
            spawnFootOffset=0;
            if(player.C!=null)foreach(Collider c in player.C)
                if(c!=null && c.enabled && !c.isTrigger)
                    spawnFootOffset=Math.Min(spawnFootOffset,c.bounds.min.y-player.transform.position.y);
            spawnFootOffset=Math.Max(-3f,spawnFootOffset);
            spawnSupportWasPresent=false;spawnSupportFixedTime=Time.fixedTime;spawnGroundSearchAt=0;spawnCandidateValid=false;
            spawnSafety.Begin(spawnBody.isKinematic,spawnBody.useGravity);
            JoinDiagnostic("spawn hold started at="+player.transform.position);
        }
        // Both native Teleport overloads pass through the two-argument method.
        // Observe the destination after native code has chosen it; never hold
        // the temporary origin at which map FStart creates the player.
        public static void AfterNativeSpawnTeleport(fpscontroller player)
        {
            if(!ShouldProtectSpawn() || player!=mainscript.s.player || player.Bsitting || player.died)return;
            if(!spawnSafety.Active)StartSpawnHold(player);
            else if(player==spawnPlayer)
            {
                spawnGlobal=mainscript.GlobalFromUnityPos(player.transform.position);
                spawnSupportWasPresent=false;spawnSupportFixedTime=Time.fixedTime;
                spawnCandidateValid=false;spawnGroundSearchAt=0;
                JoinDiagnostic("spawn hold retargeted after native teleport at="+player.transform.position);
            }
        }
        internal static bool TryGetProtectedSpawn(out Vector3 position)
        {
            position=Vector3.zero;
            if(!spawnSafety.Active || spawnPlayer==null)return false;
            position=mainscript.UnityPosFromGlobal(spawnGlobal);return true;
        }
        static bool HasSpawnSupport(Vector3 position)
        {
            // Query the anchored foot position, not an arbitrary tile or the
            // falling player's current position. Ignore own/remote avatars.
            Vector3 foot=position+Vector3.up*spawnFootOffset;
            Vector3 origin=foot+Vector3.up*.65f;
            // Native can choose a spawn above the floor (near a host standing
            // on a vehicle, for example). Require loaded collision below it,
            // rather than requiring the held avatar to already touch it.
            int count=Physics.RaycastNonAlloc(origin,Vector3.down,spawnSupportHits,64f,~0,QueryTriggerInteraction.Ignore);
            // NonAlloc does not guarantee which hits fit when the buffer fills.
            // A dense pile must not omit the ground and wedge joining forever.
            RaycastHit[] hits=spawnSupportHits;
            if(count==spawnSupportHits.Length)
            {hits=Physics.RaycastAll(origin,Vector3.down,64f,~0,QueryTriggerInteraction.Ignore);count=hits.Length;}
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];Collider collider=hit.collider;
                if(collider==null || !collider.enabled || hit.normal.y<.45f ||
                    collider.GetComponentInParent<fpscontroller>()!=null || collider.GetComponentInParent<mpplayerscript>()!=null)continue;
                // Support must be below the chosen foot position.
                if(hit.point.y>foot.y+.15f)continue;
                return true;
            }
            return false;
        }
        static void TickNativeSpawnSafety(bool beforePhysics)
        {
            if(!ShouldProtectSpawn())
            {
                if(spawnSafety.Active){RestoreSpawnBody();spawnSafety.Abort();}
                spawnBody=null;spawnPlayer=null;return;
            }
            fpscontroller player=mainscript.s.player;
            if(player.died || player.Bsitting)
            {
                RestoreSpawnBody();spawnSafety.Abort();spawnBody=null;spawnPlayer=null;
                spawnFinished=true;return;
            }
            if(spawnSafety.Active && (spawnPlayer!=player || spawnBody!=player.RB))
            {RestoreSpawnBody();spawnSafety.Abort();spawnBody=null;spawnPlayer=null;}
            if(!spawnSafety.Active)
            {
                // A world that was already Live before this local player
                // existed uses native respawn, rather than joining again.
                if(node.Ready){spawnFinished=true;return;}
                StartSpawnHold(player);
            }
            if(!spawnSafety.Active || spawnBody==null)return;
            Vector3 position=mainscript.UnityPosFromGlobal(spawnGlobal);
            // Physics is about to run in FixedUpdate. A completed opportunity
            // is observed only later from Update, after PhysX ran with support.
            bool support=!beforePhysics && HasSpawnSupport(position);
            if(!beforePhysics)
            {
                if(!support)spawnSupportWasPresent=false;
                else if(!spawnSupportWasPresent){spawnSupportWasPresent=true;spawnSupportFixedTime=Time.fixedTime;}
            }
            bool opportunity=!beforePhysics && spawnSupportWasPresent && Time.fixedTime>spawnSupportFixedTime;
            if(beforePhysics)
            {
                HoldSpawnBody(position);return;
            }
            // Preserve the captured flags before Evaluate retires the policy.
            bool oldKinematic=spawnSafety.CapturedIsKinematic,oldGravity=spawnSafety.CapturedUseGravity;
            if(SpawnClearanceEnabled)
            {
                // A candidate must have walkable static ground and room for the
                // standing capsule. Never release at a blocked or unsupported point.
                if(!node.Ready){HoldSpawnBody(position);return;}
                if(!spawnCandidateValid)
                {
                    if(Time.realtimeSinceStartup<spawnGroundSearchAt){HoldSpawnBody(position);return;}
                    spawnGroundSearchAt=Time.realtimeSinceStartup+.5f;
                    Vector3 candidate;
                    if(!FindFreeSpawn(player,position,out candidate))
                    {
                        DiagLimited("native-spawn-wait","spawn hold waiting for a clear grounded position at="+position,5);
                        HoldSpawnBody(position);return;
                    }
                    spawnGlobal=mainscript.GlobalFromUnityPos(candidate);
                    spawnCandidateValid=true;spawnCandidateSince=Time.realtimeSinceStartup;
                    spawnSupportWasPresent=false;spawnSupportFixedTime=Time.fixedTime;
                    JoinDiagnostic("spawn hold selected clear ground at="+candidate);
                    HoldSpawnBody(candidate);return;
                }
                string blocker;
                if(!support || !SpawnPointClear(player,position,out blocker))
                {
                    spawnCandidateValid=false;spawnSupportWasPresent=false;
                    spawnSafety.Evaluate(true,false,true,false,false,false,false);
                    HoldSpawnBody(position);return;
                }
                if(Time.realtimeSinceStartup-spawnCandidateSince<1f)
                {HoldSpawnBody(position);return;}
            }
            else if(!support || !node.Ready)
                DiagLimited("native-spawn-wait","spawn hold waiting support="+support+" live="+node.Ready+" anchor="+position,5);
            NativeSpawnSafetyAction action=spawnSafety.Evaluate(true,false,!player.died,player.Bsitting,support,opportunity,node.Ready);
            if(action==NativeSpawnSafetyAction.Release || action==NativeSpawnSafetyAction.Restore)
            {
                spawnBody.isKinematic=oldKinematic;spawnBody.useGravity=oldGravity;
                spawnFinished=true;spawnBody=null;spawnPlayer=null;
                JoinDiagnostic("spawn hold released after live collision support");return;
            }
            HoldSpawnBody(position);
        }
        static void HoldSpawnBody(Vector3 position)
        {
            if(spawnBody==null || spawnPlayer==null)return;
            if(!spawnBody.isKinematic){spawnBody.velocity=Vector3.zero;spawnBody.angularVelocity=Vector3.zero;}
            spawnBody.isKinematic=true;spawnBody.useGravity=false;
            spawnPlayer.transform.position=position;spawnBody.position=position;
        }

        public static void AfterNativeSeatEntry(fpscontroller player,seatscript seat)
        {
            if(!Connected || player==null || !player.Bsitting || player.seat!=seat || seat==null || seat.sitPos==null)return;
            // Native GetIn has just scheduled Destroy(RB). Do not mistake that
            // deferred destruction for a new independent body in this frame.
            seatCaptureFrame=Time.frameCount;
            seatSafety.Capture(true,player.GetInstanceID(),seat.GetInstanceID(),seat.sitPos.GetInstanceID(),
                player.transform.parent==null?0:player.transform.parent.GetInstanceID(),false,player.transform.localPosition);
        }
        public static void BeforeNativeSeatExit(fpscontroller player,bool forced)
        {
            if(!Connected || player==null || !player.Bsitting)return;
            bool input=player.input!=null && player.input.useDown;
            if(seatSafety.ShouldLogExit(forced,input))JoinDiagnostic("native seat exit reason="+NativeSeatSafety.ExitReason(forced,input));
        }
        public static void AfterNativeSeatExit(fpscontroller player)
        {if(player==null || !player.Bsitting)seatSafety.Clear();}
        static void TickNativeSeatSafety()
        {
            fpscontroller player=mainscript.s==null?null:mainscript.s.player;
            if(!Connected || player==null || player.died || !player.Bsitting || player.seat==null || player.seat.sitPos==null)
            {seatSafety.Clear();return;}
            if(Time.frameCount==seatCaptureFrame)return;
            seatscript seat=player.seat;Transform root=player.transform;
            Rigidbody independent=player.GetComponent<Rigidbody>();NativeSeatSafety.Reason reason;
            NativeSeatSafety.Repair repair=seatSafety.Evaluate(true,true,player.GetInstanceID(),seat.GetInstanceID(),seat.sitPos.GetInstanceID(),
                root.parent==null?0:root.parent.GetInstanceID(),independent!=null,root.localPosition,out reason);
            if((repair&NativeSeatSafety.Repair.Parent)!=0)root.SetParent(seat.sitPos,true);
            if((repair&NativeSeatSafety.Repair.Offset)!=0)root.localPosition=seatSafety.CapturedLocalOffset;
            if((repair&NativeSeatSafety.Repair.IndependentRigidbody)!=0)
            {if(player.RB==independent)player.RB=null;UnityEngine.Object.Destroy(independent);}
            if(seatSafety.ShouldLog(reason))JoinDiagnostic("native seat repaired reason="+NativeSeatSafety.ReasonText(reason));
        }

        static NativeAttachmentState ReadNativeAttachment(uint id,out ulong frame)
        {
            frame=0;tosaveitemscript item;
            if(!Find(id,out item))return NativeAttachmentState.Unknown;
            tosaveitemscript parent;Transform anchor;int index;
            item.GetLocalStuff(out parent,out anchor,out index);
            if(parent!=null)frame=NativeFrame(parent.idInSave,index);
            if(item.P!=null && item.P.physlock!=null)return NativeAttachmentState.Stored;
            if(item.attachable!=null && item.attachable.attached)return NativeAttachmentState.Mounted;
            return NativeAttachmentState.World;
        }
        static ulong NativeFrame(uint parent,int index){return ((ulong)parent<<32)|(uint)index;}
        static double NativeRetryDeadline()
        {
            // The native IEnumerator already bounds its own attempt count.
            // WaitForSeconds resumes on rendered frames: tries*delay is not a
            // true expiry at low FPS and must not cut off native repair early.
            // finally retires metadata on completion/cancellation instead.
            return Double.MaxValue;
        }
        public static IEnumerator WrapNativeLocalPose(IEnumerator original,uint id,uint parent,int index)
        {
            if(!Connected || safetyWorld==0 || original==null)return original;
            NativeAttachmentTicket ticket;ulong frame;
            NativeAttachmentState state=ReadNativeAttachment(id,out frame);
            NativeLocalPoseKind kind=index>=0?NativeLocalPoseKind.StorageLock:NativeLocalPoseKind.MountedAnchor;
            if(!attachmentSafety.BeginLocalPose(id,kind,NativeFrame(parent,index),state,frame,Time.time,NativeRetryDeadline(),out ticket))return original;
            return RunNativeLocalPose(original,id,ticket);
        }
        static IEnumerator RunNativeLocalPose(IEnumerator original,uint id,NativeAttachmentTicket ticket)
        {
            tosaveitemscript initial=null;bool observed=false;
            try
            {
                while(true)
                {
                    tosaveitemscript item;ulong frame;
                    if(Find(id,out item))
                    {
                        if(!observed){initial=item;observed=true;}
                        else if(initial!=item)yield break;
                    }
                    NativeAttachmentState state=ReadNativeAttachment(id,out frame);
                    // Let native missing-item/parent repair run. Once mounted,
                    // only its current or pending native frame is compatible.
                    if(!attachmentSafety.IsLocalPoseCurrent(ticket,state,frame,Time.time))yield break;
                    if(!original.MoveNext())yield break;
                    yield return original.Current;
                }
            }
            finally{attachmentSafety.Complete(ticket,Time.time);var disposable=original as IDisposable;if(disposable!=null)disposable.Dispose();}
        }
        public static IEnumerator WrapNativeAttachment(IEnumerator original,tosaveitemscript item,uint id,int indexType,int attachType,uint parent,int index)
        {
            if(!Connected || safetyWorld==0 || original==null)return original;
            int localIndex=indexType==2?-100000-index:-200000-index;
            ulong target=attachType==1?NativeFrame(parent,localIndex):0;
            NativeAttachmentTicket ticket;
            if(!attachmentSafety.BeginAttachmentUpdate(id,target,Time.time,NativeRetryDeadline(),out ticket))return original;
            return RunNativeAttachment(original,item,id,ticket);
        }
        static IEnumerator RunNativeAttachment(IEnumerator original,tosaveitemscript initial,uint id,NativeAttachmentTicket ticket)
        {
            try
            {
                while(true)
                {
                    tosaveitemscript item;ulong frame;
                    if(initial==null || !Find(id,out item) || item!=initial)yield break;
                    ReadNativeAttachment(id,out frame);
                    // Generation invalidation retires obsolete operations. Do
                    // not veto a newer native mount merely because the item is
                    // still in its previous frame before Load gets to run.
                    if(!attachmentSafety.IsAttachmentUpdateCurrent(ticket,false,frame,Time.time))yield break;
                    if(!original.MoveNext())yield break;
                    yield return original.Current;
                }
            }
            finally{attachmentSafety.Complete(ticket,Time.time);var disposable=original as IDisposable;if(disposable!=null)disposable.Dispose();NoteAttachOutcome(id);}
        }
        static void ObserveNativeSafetyOutgoing(byte[] bytes)
        {
            NoteAttachablePacket("tx",bytes);
            if(safetyWorld==0 || bytes==null || bytes.Length<5)return;
            byte type=bytes[0];
            if(type==(byte)msgType.attachable || type==(byte)msgType.itemPosPlayer || type==(byte)msgType.removeItem)
                attachmentSafety.InvalidateTopology(StablePackets.Item(bytes),Time.time);
        }
        static bool AllowNativeSafetyPacket(byte[] bytes)
        {
            NoteAttachablePacket("rx",bytes);
            if(!Connected || bytes==null || bytes.Length==0 || safetyWorld==0)return true;
            byte type=bytes[0];uint id=StablePackets.Item(bytes);ulong frame;
            if(type==(byte)msgType.itemPosGlobal || type==(byte)msgType.velocity)
            {
                NativeAttachmentState state=ReadNativeAttachment(id,out frame);
                if(!attachmentSafety.AllowWorldPose(id,state,frame,Time.time))
                {DiagLimited("native-mounted-pose:"+id,"native world pose ignored for mounted/stored member="+id,.5);return false;}
            }
            if(type==(byte)msgType.attachable || type==(byte)msgType.itemPosPlayer || type==(byte)msgType.removeItem)
                attachmentSafety.InvalidateTopology(id,Time.time);
            return true;
        }
    }
}
