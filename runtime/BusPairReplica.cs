using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;
namespace TldMp
{
    // Kinematic replicas of a remote-driven articulated bus: one interpolated
    // timeline drives BOTH collision-capable bodies each fixed step while the
    // native joint is preserved. The bridge owns transport authentication and
    // session binding; this class owns ordering, interpolation and lifecycle.
    //
    // Authority is the host-issued per-bus Generation plus (Origin,Epoch).
    // A newer generation hands off; a same-generation foreign identity or an
    // older generation is refused, so A->B->A transitions work and a delayed
    // prior-generation frame cannot come back.
    internal static class BusPairReplica
    {
        sealed class Track
        {
            internal tosaveitemscript Item; internal Rigidbody Front,Rear;
            internal ulong Origin, Epoch, Generation;
            internal BusPairSample A,B; internal double At,Bt,Received;
            internal bool Active,OldFrontKinematic,OldRearKinematic;
            internal RigidbodyInterpolation OldFrontInterpolation,OldRearInterpolation;
            // Corrections between the authoritative stream and the physical
            // pose, seeded at activation so the pair starts exactly where the
            // bodies are and converges at bounded rates; the bus itself always
            // follows the full stream velocity, never clamped.
            internal bool Seeded; internal Vector3d PosOffset; internal Quaternion RotOffset;
            internal Vector3 RearPosOffset; internal Quaternion RearRotOffset;
            internal bool Noted, Waited;
        }
        static readonly Dictionary<uint,Track> tracks=new Dictionary<uint,Track>();
        static readonly Dictionary<uint,ulong> generations=new Dictionary<uint,ulong>();
        static readonly List<uint> remove=new List<uint>();
        const int Capacity=64;
        const double Delay=.10, Stale=.50, MaxGap=1.0, LossRelease=1.5;
        const float SnapDistance=.5f, CatchUpSpeed=2f, CatchUpTurn=30f;
        internal static long Received,Accepted,Rejected,StaleDropped,Handoffs,Activations,Releases,Reacquires,Held;
        static int waitingNow;
        static double noteWindow;static int noteCount;
        static void Note(string text)
        {
            if(!ModDiagnostics.On)return;
            double now=Time.realtimeSinceStartup;
            if(now-noteWindow>5){noteWindow=now;noteCount=0;}
            if(++noteCount>20)return;
            Debug.Log("[TLD MP pair] t="+now.ToString("F2")+" "+text);
        }
        static Quaternion Q(float[] a){return new Quaternion(a[0],a[1],a[2],a[3]);}
        static Vector3 Vec(float[] a){return new Vector3(a[0],a[1],a[2]);}
        static void Copy(float[] dst,Vector3 v){dst[0]=v.x;dst[1]=v.y;dst[2]=v.z;}
        static void Copy(float[] dst,Quaternion q){dst[0]=q.x;dst[1]=q.y;dst[2]=q.z;dst[3]=q.w;}
        static Rigidbody Rear(tosaveitemscript item,Rigidbody front)
        {
            if(item==null||front==null||item.transform.name!="BusFront"||item.partslotscripts==null)return null;
            foreach(var slot in item.partslotscripts){if(slot==null)continue;Transform t=slot.transform;
                while(t!=null&&t.name!="BusBack")t=t.parent;
                if(t==null)continue;var rear=t.GetComponent<Rigidbody>();var joint=t.GetComponent<ConfigurableJoint>();
                if(rear!=null&&joint!=null&&joint.connectedBody==front)return rear;}
            return null;
        }
        // Canonical structure check: an active BusFront with a real
        // joint-connected BusBack rigidbody.
        internal static bool Pair(tosaveitemscript item,out Rigidbody front,out Rigidbody rear)
        {
            front=rear=null;
            if(item==null||item.car==null||!item.gameObject.activeInHierarchy)return false;
            front=item.RB;if(front==null)return false;
            rear=Rear(item,front);return rear!=null;
        }
        // Call on the driving machine only, after the local driver seat checks.
        internal static BusPairSample Capture(tosaveitemscript item,ulong origin,ulong epoch,uint sequence,uint clockMs)
        {
            Rigidbody front,rear;
            if(item==null||item.car==null||item.CarWhatDoing()!=tosaveitemscript.carWhatDoing.driving||!item.ThisOneInChargeCar()||!Pair(item,out front,out rear)||
               front.isKinematic||rear.isKinematic)return null;
            var pos=mainscript.GlobalFromUnityPos(front.position);Quaternion inv=Quaternion.Inverse(front.rotation);
            var s=new BusPairSample{Origin=origin,RelaySession=0,Epoch=epoch,Generation=0,ID=item.idInSave,Prefab=item.id,Sequence=sequence,ClockMs=clockMs,
                Role=(int)tosaveitemscript.carWhatDoing.driving,X=pos.x,Y=pos.y,Z=pos.z};
            Copy(s.FrontRotation,front.rotation);Copy(s.RearOffset,inv*(rear.position-front.position));Copy(s.RearRotation,inv*rear.rotation);
            Copy(s.FrontVelocity,front.velocity);Copy(s.FrontAngular,front.angularVelocity);Copy(s.RearVelocity,rear.velocity);Copy(s.RearAngular,rear.angularVelocity);
            return BusPairProtocol.Valid(s)?s:null;
        }
        // The caller authenticated the transport source and bound Origin/Epoch
        // to the live session; Generation is the host-issued per-bus authority
        // (upstream self-captures carry the bridge-issued value). mappedTime is
        // on the local clock.
        internal static bool Accept(BusPairSample s,tosaveitemscript item,double mappedTime,double now)
        {
            if(s==null||s.Generation==0||double.IsNaN(mappedTime)||double.IsInfinity(mappedTime)||mappedTime>now+.25||mappedTime<now-1.0)return false;
            Rigidbody front,rear;
            if(!Pair(item,out front,out rear)||item.idInSave!=s.ID||item.id!=s.Prefab||
               s.Role!=(int)tosaveitemscript.carWhatDoing.driving||s.Origin==0||s.Epoch==0||
               item.CarWhatDoing()==tosaveitemscript.carWhatDoing.driving||item.ThisOneInChargeCar((tosaveitemscript.carWhatDoing)s.Role))
            {Rejected++;return false;}
            ulong generation;
            if(generations.TryGetValue(s.ID,out generation))
            {
                if(s.Generation<generation){Rejected++;return false;}
                if(s.Generation>generation){Handoff(s.ID);generations[s.ID]=s.Generation;Handoffs++;}
            }
            else generations[s.ID]=s.Generation;
            Track track;
            if(!tracks.TryGetValue(s.ID,out track))
            {if(tracks.Count>=Capacity){Rejected++;return false;}tracks.Add(s.ID,track=new Track{Item=item,Front=front,Rear=rear,Origin=s.Origin,Epoch=s.Epoch,Generation=s.Generation});}
            if(track.Item!=item||track.Front!=front||track.Rear!=rear||track.Origin!=s.Origin||track.Epoch!=s.Epoch||track.Generation!=s.Generation)
            {
                // Same generation but a different claimed identity is refused.
                if(track.Generation==s.Generation&&(track.Origin!=s.Origin||track.Epoch!=s.Epoch)){Rejected++;return false;}
                Release(track,false);tracks[s.ID]=track=new Track{Item=item,Front=front,Rear=rear,Origin=s.Origin,Epoch=s.Epoch,Generation=s.Generation};
            }
            if(track.B!=null&&(!BusPairProtocol.Newer(s.Sequence,track.B.Sequence)||mappedTime<=track.Bt)){StaleDropped++;return false;}
            if(track.B!=null&&mappedTime-track.Bt>MaxGap){track.A=null;track.B=null;track.Seeded=false;}
            track.A=track.B;track.At=track.Bt;track.B=s;track.Bt=mappedTime;track.Received=now;
            Accepted++;return true;
        }
        static void Handoff(uint id)
        {
            Track track;
            if(tracks.TryGetValue(id,out track)){Release(track,false);tracks.Remove(id);}
        }
        internal static bool Owns(tosaveitemscript item)
        {Track t;return item!=null&&tracks.TryGetValue(item.idInSave,out t)&&t.Item==item&&t.Active;}
        internal static bool OwnsBody(Rigidbody body)
        {if(body==null)return false;foreach(var p in tracks){var t=p.Value;if(t.Active&&(t.Front==body||t.Rear==body))return true;}return false;}
        // Diagnostics: waiting for a first coherent seed or holding on a gap.
        internal static bool Waiting(tosaveitemscript item)
        {Track t;return item!=null&&tracks.TryGetValue(item.idInSave,out t)&&t.Item==item&&t.B!=null&&(!t.Seeded||t.Waited);}
        static void Pose(Track t,double now,out Vector3d frontGlobal,out Quaternion frontRot,out Vector3 rearLocal,out Quaternion rearLocalRot)
        {
            double span=t.Bt-t.At;float a=span>0?Mathf.Clamp01((float)((now-Delay-t.At)/span)):1f;
            frontGlobal=new Vector3d(t.A.X+(t.B.X-t.A.X)*a,t.A.Y+(t.B.Y-t.A.Y)*a,t.A.Z+(t.B.Z-t.A.Z)*a);
            frontRot=Quaternion.Slerp(Q(t.A.FrontRotation),Q(t.B.FrontRotation),a);
            rearLocal=Vector3.Lerp(Vec(t.A.RearOffset),Vec(t.B.RearOffset),a);
            rearLocalRot=Quaternion.Slerp(Q(t.A.RearRotation),Q(t.B.RearRotation),a);
        }
        // The local player's physical (non-trigger) collider bounds against the
        // pair bodies' current bounds. Used to keep a held pair kinematic while
        // occupied instead of releasing into independent dynamics.
        static bool Envelope(Rigidbody body,out Bounds bounds)
        {
            bounds=new Bounds();bool found=false;
            foreach(var c in body.GetComponentsInChildren<Collider>())
            {
                if(c==null||!c.enabled||c.isTrigger)continue;
                if(!found){bounds=c.bounds;found=true;}else bounds.Encapsulate(c.bounds);
            }
            if(found)bounds.Expand(.5f);
            return found;
        }
        static bool PlayerNear(Rigidbody front,Rigidbody rear)
        {
            var player=mainscript.s==null?null:mainscript.s.player;
            if(player==null||player.C==null)return false;
            Bounds fb,rb;bool hasFront=Envelope(front,out fb),hasRear=Envelope(rear,out rb);
            foreach(var pc in player.C)
            {
                if(pc==null||!pc.enabled||pc.isTrigger)continue;var pb=pc.bounds;
                if((hasFront&&pb.Intersects(fb))||(hasRear&&pb.Intersects(rb)))return true;
            }
            return false;
        }
        static void Activate(Track t)
        {
            if(t.Active)return;
            t.OldFrontKinematic=t.Front.isKinematic;t.OldRearKinematic=t.Rear.isKinematic;
            t.OldFrontInterpolation=t.Front.interpolation;t.OldRearInterpolation=t.Rear.interpolation;
            t.Front.isKinematic=true;t.Rear.isKinematic=true;
            t.Front.interpolation=RigidbodyInterpolation.Interpolate;t.Rear.interpolation=RigidbodyInterpolation.Interpolate;
            t.Active=true;Activations++;
        }
        internal static void FixedTick(bool enabled,double now)
        {
            if(!enabled){Clear();return;}
            waitingNow=0;
            remove.Clear();
            float dt=Time.fixedDeltaTime;if(dt<=0f||float.IsNaN(dt)||float.IsInfinity(dt))dt=.02f;
            foreach(var pair in tracks)
            {
                Track t=pair.Value;
                Rigidbody front,rear;
                if(t.Item==null||t.Item.car==null||!Pair(t.Item,out front,out rear)||front!=t.Front||rear!=t.Rear)
                {Release(t,false);remove.Add(pair.Key);continue;}
                bool staleInput=now-t.Received>Stale;
                // Explicit local-driver check first: the paired stream never
                // overrides a machine that is actually driving the bus.
                if(t.Item.CarWhatDoing()==tosaveitemscript.carWhatDoing.driving)
                {Release(t,!staleInput);remove.Add(pair.Key);continue;}
                // Keep the remote driver role fresh while the stream is alive
                // so the native authority order cannot reclaim it mid-stream.
                if(!staleInput&&t.Item.car!=null){t.Item.car.lastMultiWhatDoing=tosaveitemscript.carWhatDoing.driving;t.Item.car.lastMultiWhatDoingTime=(float)now;}
                // Once the remote role goes stale a machine may legitimately
                // take charge; the handoff restores native flags (never stale
                // remote velocities) before local simulation resumes.
                else if(t.Item.ThisOneInChargeCar()&&!PlayerNear(t.Front,t.Rear)){Release(t,false);remove.Add(pair.Key);continue;}
                // First admitted sample: freeze BOTH bodies kinematic at the
                // current physical poses while the second buffers, so no
                // competing native writer owns the pair in that window.
                if(t.A==null&&t.B!=null&&!staleInput)
                {
                    Activate(t);
                    continue;
                }
                if(t.A==null||t.B==null)continue;
                if(staleInput)
                {
                    if(t.Active)
                    {
                        Held++;
                        // Occupied pair never drops into independently driven
                        // dynamics; an unoccupied one releases after the bound.
                        if(now-t.Received>LossRelease&&!PlayerNear(t.Front,t.Rear)){Release(t,false);remove.Add(pair.Key);}
                        continue;
                    }
                    if(now-t.Received>Stale+LossRelease)remove.Add(pair.Key);
                    continue;
                }
                Vector3d fg;Quaternion fr;Vector3 rl;Quaternion rr;Pose(t,now,out fg,out fr,out rl,out rr);
                if(!t.Seeded)
                {
                    // Seed corrections so the applied pose equals the current
                    // physical pose, then take both bodies kinematic once.
                    var cur=mainscript.GlobalFromUnityPos(t.Front.position);
                    t.PosOffset=new Vector3d(cur.x-fg.x,cur.y-fg.y,cur.z-fg.z);
                    t.RotOffset=t.Front.rotation*Quaternion.Inverse(fr);
                    Quaternion inv=Quaternion.Inverse(t.Front.rotation);
                    t.RearPosOffset=inv*(t.Rear.position-t.Front.position)-rl;
                    t.RearRotOffset=inv*t.Rear.rotation*Quaternion.Inverse(rr);
                    t.Seeded=true;
                    Activate(t);
                    if(!t.Noted){t.Noted=true;Note("pair active id="+pair.Key+" front="+Vec(t.B.FrontVelocity).magnitude.ToString("F1")+"m/s rear="+Vec(t.B.RearVelocity).magnitude.ToString("F1")+"m/s owner="+t.Origin+" gen="+t.Generation);}
                }
                else
                {
                    // Decay only the seeded corrections; the stream target
                    // itself is always followed at full authoritative speed.
                    double m=Math.Sqrt(t.PosOffset.x*t.PosOffset.x+t.PosOffset.y*t.PosOffset.y+t.PosOffset.z*t.PosOffset.z);
                    double maxStep=CatchUpSpeed*dt;
                    if(m>maxStep){double k=maxStep/m;t.PosOffset=new Vector3d(t.PosOffset.x*(1-k),t.PosOffset.y*(1-k),t.PosOffset.z*(1-k));}
                    else t.PosOffset=new Vector3d(0,0,0);
                    t.RotOffset=Quaternion.RotateTowards(t.RotOffset,Quaternion.identity,CatchUpTurn*dt);
                    t.RearPosOffset=Vector3.MoveTowards(t.RearPosOffset,Vector3.zero,CatchUpSpeed*dt);
                    t.RearRotOffset=Quaternion.RotateTowards(t.RearRotOffset,Quaternion.identity,CatchUpTurn*dt);
                }
                double cm=Math.Sqrt(t.PosOffset.x*t.PosOffset.x+t.PosOffset.y*t.PosOffset.y+t.PosOffset.z*t.PosOffset.z);
                if(cm>SnapDistance)
                {
                    waitingNow++;
                    if(!t.Waited){t.Waited=true;Note("pair converging id="+pair.Key+" gap="+cm.ToString("F2")+"m");}
                }
                else if(t.Waited){t.Waited=false;Reacquires++;Note("pair reacquired id="+pair.Key);}
                if(!t.Front.isKinematic||!t.Rear.isKinematic){Release(t,false);remove.Add(pair.Key);continue;}
                Vector3 fp=mainscript.UnityPosFromGlobal(new Vector3d(fg.x+t.PosOffset.x,fg.y+t.PosOffset.y,fg.z+t.PosOffset.z));
                Quaternion fq=t.RotOffset*fr;
                t.Front.MovePosition(fp);t.Front.MoveRotation(fq);
                t.Rear.MovePosition(fp+fq*(rl+t.RearPosOffset));t.Rear.MoveRotation(fq*(t.RearRotOffset*rr));
            }
            foreach(uint id in remove)tracks.Remove(id);
        }
        static void Release(Track t,bool velocities)
        {
            if(!t.Active)return;t.Active=false;Releases++;
            if(t.Front!=null&&t.Front.isKinematic){t.Front.isKinematic=t.OldFrontKinematic;t.Front.interpolation=t.OldFrontInterpolation;
                if(!t.OldFrontKinematic){var v=velocities&&t.B!=null?Vec(t.B.FrontVelocity):Vector3.zero;var a=velocities&&t.B!=null?Vec(t.B.FrontAngular):Vector3.zero;t.Front.velocity=v;t.Front.angularVelocity=a;}}
            if(t.Rear!=null&&t.Rear.isKinematic){t.Rear.isKinematic=t.OldRearKinematic;t.Rear.interpolation=t.OldRearInterpolation;
                if(!t.OldRearKinematic){var v=velocities&&t.B!=null?Vec(t.B.RearVelocity):Vector3.zero;var a=velocities&&t.B!=null?Vec(t.B.RearAngular):Vector3.zero;t.Rear.velocity=v;t.Rear.angularVelocity=a;}}
        }
        // Revoke this bus's replica immediately (host authority eviction,
        // disconnect, world/session reset). The generation record survives, so
        // already-issued generations cannot silently return.
        internal static void Forget(uint id)
        {
            Track track;
            if(tracks.TryGetValue(id,out track)){Release(track,false);tracks.Remove(id);}
        }
        internal static void Clear(){foreach(var p in tracks)Release(p.Value,false);tracks.Clear();generations.Clear();}
        internal static string TakeSummary()
        {
            string text="pair{received="+Received+" accepted="+Accepted+" rejected="+Rejected+" stale="+StaleDropped+" handoff="+Handoffs+
                " active="+Activations+" release="+Releases+" reacquire="+Reacquires+" held="+Held+" waiting="+waitingNow+"}";
            Received=Accepted=Rejected=StaleDropped=Handoffs=Activations=Releases=Reacquires=Held=0;
            return text;
        }
    }
}
