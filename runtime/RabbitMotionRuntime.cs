using System;
using System.Collections.Generic;
using Steamworks;
using GeneszSteamP2PBase;
using UnityEngine;
namespace TldMp
{
    public static partial class StableGameBridge
    {
        sealed class RabbitMotionTrack
        {
            internal tosaveitemscript Item;internal RemotePoseBuffer Buffer=new RemotePoseBuffer();
            internal uint Sequence;internal bool Active,Attacking,Roaring,VisualPosition;internal double Seen,Next,VisualX,VisualZ,VisualTime;
        }
        const int RabbitMotionMax=256,RabbitMotionScanPerFrame=128,RabbitMotionSendsPerFrame=8;
        const double RabbitMotionInterval=.1,RabbitMotionStale=.6;
        static readonly Dictionary<uint,RabbitMotionTrack> rabbitMotion=new Dictionary<uint,RabbitMotionTrack>();
        static readonly List<uint> rabbitMotionKeys=new List<uint>();
        static Dictionary<uint,tosaveitemscript>.Enumerator rabbitMotionScan;
        static bool rabbitMotionScanning;static double rabbitMotionNextScan,rabbitMotionWarning;static int rabbitMotionCursor;static uint rabbitMotionSeq;
        static savedatascript rabbitMotionSave;static StableNode rabbitMotionNode;static ulong rabbitMotionSession;
        static long rabbitMotionSent,rabbitMotionReceived,rabbitMotionExpired,rabbitMotionStaleRejected;
        static double rabbitMotionNextReport;
        static void RabbitMotionReset()
        {
            if(rabbitMotionNode!=null&&!rabbitMotionNode.IsHost)foreach(var track in rabbitMotion.Values)RabbitMotionStop(track);
            rabbitMotion.Clear();rabbitMotionKeys.Clear();rabbitMotionScanning=false;rabbitMotionNextScan=0;rabbitMotionCursor=0;rabbitMotionSeq=0;rabbitMotionSave=null;rabbitMotionNode=null;rabbitMotionSession=0;
            rabbitMotionSent=rabbitMotionReceived=rabbitMotionExpired=rabbitMotionStaleRejected=0;rabbitMotionNextReport=0;
        }
        static void RabbitMotionStop(RabbitMotionTrack track)
        {
            if(track.Item!=null)RabbitGuestAnimation.Stop(RabbitReplicas.RabbitRoot(track.Item));
            track.Active=false;track.Attacking=false;track.Roaring=false;track.VisualPosition=false;track.Buffer=new RemotePoseBuffer();
        }
        internal static bool RabbitMotionEligible(tosaveitemscript item)
        {
            if(item==null||item.idInSave==0||item.useThisForPos==null||!item.gameObject.activeInHierarchy||item.mppickedup||item.mpequipped||item.mpstored||item.mpweared)return false;
            var root=RabbitReplicas.RabbitRoot(item);
            if(root==null||root.ragdoll==null||root.ragdoll.ragdoll)return false;
            if(item.transform.parent!=null)return false;
            var p=item.P;
            return p==null||(!p.pickedUp&&!p.inRightHand&&!p.isweared()&&p.inInventory()==0&&!p.PickedUp(false));
        }
        static bool ReceiveRabbitMotion(ulong source,byte[] bytes)
        {
            if(bytes==null||bytes.Length==0||bytes[0]!=RabbitMotionCodec.Type)return false;
            if(node==null||node.IsHost||!node.Compatible||!node.Ready||node.Error!=null||source!=node.Host||!WorldSceneAvailable())return true;
            if(loopbackMode){if(source!=Loopback.Peer)return true;}
            else if(!SteamP2PNetworkingUnderTheHoodScript.Expected(new CSteamID(source),msgType.carPosGlobal))return true;
            RabbitMotionSample sample;if(!RabbitMotionCodec.Read(bytes,out sample)||sample.Session!=node.HostSession)return true;
            var save=savedatascript.s;tosaveitemscript item;
            if(save==null||save.items==null||!save.items.TryGetValue(sample.Item,out item)||item==null||item.id!=sample.Prefab||RabbitReplicas.RabbitRoot(item)==null)return true;
            if(rabbitMotionNode!=node||rabbitMotionSave!=save||rabbitMotionSession!=node.HostSession)
            {RabbitMotionReset();rabbitMotionNode=node;rabbitMotionSave=save;rabbitMotionSession=node.HostSession;}
            RabbitMotionTrack track;
            if(!rabbitMotion.TryGetValue(sample.Item,out track))
            {if(rabbitMotion.Count>=RabbitMotionMax)return true;track=new RabbitMotionTrack{Item=item};rabbitMotion.Add(sample.Item,track);}
            else if(track.Item!=item){RabbitMotionStop(track);track.Item=item;}
            if(track.Sequence!=0&&!Numbers.Newer(sample.Sequence,track.Sequence)){rabbitMotionStaleRejected++;return true;}
            track.Sequence=sample.Sequence;track.Seen=Now;rabbitMotionReceived++;
            if(!sample.Active){RabbitMotionStop(track);return true;}
            if(!RabbitMotionEligible(item)){RabbitMotionStop(track);return true;}
            var q=Quaternion.Euler(sample.Rx,sample.Ry,sample.Rz);var p=new RemotePose{X=sample.X,Y=sample.Y,Z=sample.Z,Qx=q.x,Qy=q.y,Qz=q.z,Qw=q.w};
            if(!track.Active)track.Buffer=new RemotePoseBuffer();track.Active=true;track.Attacking=sample.Attacking;track.Roaring=sample.Roaring;track.Buffer.Add(Now,p);item.UpdGotSyncTime();return true;
        }
        static void RabbitMotionTick()
        {
            try{RabbitMotionTickCore();}catch(Exception e){if(Now>=rabbitMotionWarning){rabbitMotionWarning=Now+5;Debug.LogWarning("[TLD MP rabbit motion] "+e.GetType().Name);}}
        }
        static void RabbitMotionTickCore()
        {
            var save=savedatascript.s;
            if(node==null||!node.Compatible||!node.Ready||node.Error!=null||!Connected||!WorldSceneAvailable()||save==null||save.items==null)
            {if(rabbitMotionNode!=null)RabbitMotionReset();return;}
            if(rabbitMotionNode!=node||rabbitMotionSave!=save||(!node.IsHost&&rabbitMotionSession!=node.HostSession))
            {RabbitMotionReset();rabbitMotionNode=node;rabbitMotionSave=save;rabbitMotionSession=node.HostSession;}
            if(node.IsHost)RabbitMotionHost(save);else RabbitMotionGuest(save);
            RabbitMotionReport();
        }
        static void RabbitMotionHost(savedatascript save)
        {
            bool peers=false;foreach(var p in node.Peers)if(p.Value.Ready){peers=true;break;}if(!peers)return;
            if(!rabbitMotionScanning&&Now>=rabbitMotionNextScan){rabbitMotionScan=save.items.GetEnumerator();rabbitMotionScanning=true;}
            if(rabbitMotionScanning)
            {
                try
                {
                    for(int i=0;i<RabbitMotionScanPerFrame;i++)
                    {
                        if(!rabbitMotionScan.MoveNext()){rabbitMotionScanning=false;rabbitMotionNextScan=Now+1;break;}
                        var pair=rabbitMotionScan.Current;var item=pair.Value;
                        if(item==null||pair.Key==0||item.idInSave!=pair.Key||RabbitReplicas.RabbitRoot(item)==null)continue;
                        RabbitMotionTrack old;if(rabbitMotion.TryGetValue(pair.Key,out old)){if(old.Item!=item){old.Item=item;old.Next=0;old.Active=false;}}
                        else if(rabbitMotion.Count<RabbitMotionMax){rabbitMotion[pair.Key]=new RabbitMotionTrack{Item=item};rabbitMotionKeys.Add(pair.Key);}
                    }
                }
                catch(InvalidOperationException){rabbitMotionScanning=false;rabbitMotionNextScan=Now+.1;}
            }
            int count=rabbitMotionKeys.Count,visited=0,sent=0;
            while(visited<count && sent<RabbitMotionSendsPerFrame && rabbitMotionKeys.Count>0)
            {
                if(rabbitMotionCursor>=rabbitMotionKeys.Count)rabbitMotionCursor=0;
                uint id=rabbitMotionKeys[rabbitMotionCursor];RabbitMotionTrack track=rabbitMotion[id];tosaveitemscript canonical;
                if(!save.items.TryGetValue(id,out canonical)||canonical==null||canonical!=track.Item)
                {rabbitMotion.Remove(id);rabbitMotionKeys.RemoveAt(rabbitMotionCursor);visited++;continue;}
                rabbitMotionCursor++;visited++;bool active=RabbitMotionEligible(canonical);
                var root=RabbitReplicas.RabbitRoot(canonical);bool attacking=active&&root.attacking,roaring=active&&root.roaring;
                if((!active&&!track.Active)||(active&&Now<track.Next&&track.Attacking==attacking&&track.Roaring==roaring))continue;
                var t=canonical.useThisForPos;if(t==null)continue;var pos=mainscript.GlobalFromUnityPos(t.position);var rot=t.eulerAngles;
                rabbitMotionSeq++;if(rabbitMotionSeq==0)rabbitMotionSeq++;
                var sample=new RabbitMotionSample{Session=node.Session,Sequence=rabbitMotionSeq,Item=id,Prefab=canonical.id,Active=active,Attacking=attacking,Roaring=roaring,X=pos.x,Y=pos.y,Z=pos.z,Rx=rot.x,Ry=rot.y,Rz=rot.z};
                if(!RabbitMotionCodec.Valid(sample))continue;
                if(node.PublishNative(RabbitMotionCodec.Write(sample))){track.Active=active;track.Attacking=attacking;track.Roaring=roaring;track.Next=Now+RabbitMotionInterval;sent++;rabbitMotionSent++;}
            }
        }
        // Read-only pose access for the guest contact proxy: samples the
        // buffered track at the render delay WITHOUT consuming TakeSnap
        // (the visual path owns it). READY/authenticated-host-motion only.
        internal static bool RabbitContactCurrent(uint id,tosaveitemscript item,nyulscript root,double now)
        {
            if(node==null||node.IsHost||!node.Ready||!node.Compatible||node.Error!=null||item==null||
               double.IsNaN(now)||double.IsInfinity(now)||!WorldSceneAvailable()||
               rabbitMotionNode!=node||rabbitMotionSession!=node.HostSession)return false;
            var save=savedatascript.s;
            tosaveitemscript canonical;
            if(save==null||rabbitMotionSave!=save||save.items==null||!save.items.TryGetValue(id,out canonical)||
               !ReferenceEquals(canonical,item)||!ReferenceEquals(RabbitReplicas.RabbitRoot(item),root)||
               !RabbitMotionEligible(item))return false;
            RabbitMotionTrack t;
            if(!rabbitMotion.TryGetValue(id,out t)||t==null||!t.Active||t.Item!=item)return false;
            return now>=t.Seen&&now-t.Seen<=RabbitMotionStale;
        }
        internal static bool TryRabbitPose(uint id,tosaveitemscript item,double now,out RemotePose pose)
        {
            pose=default(RemotePose);
            if(!RabbitContactCurrent(id,item,RabbitReplicas.RabbitRoot(item),now))return false;
            RabbitMotionTrack t=rabbitMotion[id];
            return t.Buffer.Sample(now-t.Buffer.Delay,out pose);
        }
        // Active guest motion ids only (bounded by RabbitMotionMax); used for
        // contact-proxy discovery without scanning the whole item world.
        internal static void ForEachGuestMotionId(List<uint> dst)
        {
            dst.Clear();
            if(node==null||node.IsHost)return;
            foreach(var kv in rabbitMotion)if(kv.Value.Active&&kv.Value.Item!=null)dst.Add(kv.Key);
        }
        static void RabbitMotionReport()
        {
            if((!loopbackMode&&!DiagnosticMode)||Now<rabbitMotionNextReport)return;
            rabbitMotionNextReport=Now+10;int active=0;
            foreach(var track in rabbitMotion.Values)if(track.Active)active++;
            Debug.Log("[TLD MP rabbit motion] role="+(node.IsHost?"host":"guest")+" tracks="+rabbitMotion.Count+
                " active="+active+" published="+rabbitMotionSent+" received="+rabbitMotionReceived+
                " expired="+rabbitMotionExpired+" staleRejected="+rabbitMotionStaleRejected+" (since last report)");
            rabbitMotionSent=rabbitMotionReceived=rabbitMotionExpired=rabbitMotionStaleRejected=0;
        }
        static void RabbitMotionGuest(savedatascript save)
        {
            rabbitMotionKeys.Clear();foreach(var pair in rabbitMotion)rabbitMotionKeys.Add(pair.Key);
            foreach(uint id in rabbitMotionKeys)
            {
                var track=rabbitMotion[id];tosaveitemscript current;
                if(!save.items.TryGetValue(id,out current)||current==null||track.Item!=current)
                {
                    // Keep the sequence briefly across despawn/replacement so a
                    // delayed packet cannot animate a new object with the same ID.
                    RabbitMotionStop(track);track.Item=null;
                    if(Now-track.Seen>30)rabbitMotion.Remove(id);continue;
                }
                if(!track.Active)continue;
                if(Now-track.Seen>RabbitMotionStale){rabbitMotionExpired++;RabbitMotionStop(track);continue;}
                if(!RabbitMotionEligible(current)){RabbitMotionStop(track);continue;}
                RemotePose pose;if(!track.Buffer.TakeSnap(out pose) && !track.Buffer.Sample(Now-track.Buffer.Delay,out pose))continue;
                current.useThisForPos.position=mainscript.UnityPosFromGlobal(new Vector3d(pose.X,pose.Y,pose.Z));
                current.useThisForPos.rotation=new Quaternion((float)pose.Qx,(float)pose.Qy,(float)pose.Qz,(float)pose.Qw);
                var root=RabbitReplicas.RabbitRoot(current);float speed=0;
                double dt=Now-track.VisualTime,dx=pose.X-track.VisualX,dz=pose.Z-track.VisualZ;
                if(track.VisualPosition&&dt>0&&dt<.5&&dx*dx+dz*dz<100&&root!=null&&root.move!=null&&root.move.speed>.01f)
                    speed=(float)(Math.Sqrt(dx*dx+dz*dz)/dt/root.move.speed);
                track.VisualPosition=true;track.VisualX=pose.X;track.VisualZ=pose.Z;track.VisualTime=Now;
                RabbitGuestAnimation.CosmeticMotion(root,speed);
                RabbitGuestAnimation.CosmeticActions(root,track.Attacking,track.Roaring);
            }
        }
    }
}






