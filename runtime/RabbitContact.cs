using System;
using System.Collections.Generic;
using System.Threading;
using Steamworks;
using Unity.Collections;
using UnityEngine;
namespace TldMp
{
    // Physical contact layer for canonical rabbits.
    // Guest: an unparented kinematic capsule proxy per living replica follows
    // the SAME authenticated motion buffer (Sample, never TakeSnap). Through
    // Collider.hasModifiableContacts + ContactModifyEvent the proxy collides
    // ONLY with the local player's physical colliders.
    // Host: canonical ready remote actors' proxy colliders are physically
    // non-authoritative, so canonical rabbit SOLID colliders take scoped
    // Physics.IgnoreCollision against ONLY those colliders. Pairs that were
    // already ignored natively are never touched or restored.
    // The worker callback reads ONE immutable snapshot object (proxy ids plus
    // player ids) swapped atomically on the main thread; it never touches
    // UnityEngine objects.
    internal static class RabbitContact
    {
        internal sealed class Snapshot{internal int[] Proxies,Players;internal Snapshot(){Proxies=Empty;Players=Empty;}}
        static readonly int[] Empty=new int[0];
        static volatile Snapshot snap=new Snapshot();
        internal sealed class Proxy
        {
            internal uint ID;internal tosaveitemscript Item;internal nyulscript Root;
            internal GameObject Go;internal Rigidbody RB;internal CapsuleCollider Cap;
            internal Vector3 LocalOffset;internal Quaternion LocalRotOffset;
            internal double LastPose;internal bool WaitingClear,Enabled;
            internal float Radius,Height;
        }
        internal sealed class IgnorePair{internal Collider A,B;}
        static readonly Dictionary<uint,Proxy> proxies=new Dictionary<uint,Proxy>();
        static readonly List<uint> remove=new List<uint>();
        static readonly List<IgnorePair> hostPairs=new List<IgnorePair>();
        internal static List<IgnorePair> HostPairsInternal{get{return hostPairs;}}
        static readonly HashSet<int> navIgnore=new HashSet<int>();
        static readonly List<int> scratchProxies=new List<int>(),scratchPlayers=new List<int>();
        static readonly List<nyulscript> registry=new List<nyulscript>();
        static readonly List<uint> motionIds=new List<uint>();
        static bool eventsRegistered;
        static PhysicMaterial sharedMat;
        static int motionCursor;
        const int ScanBudget=64;
        const double PoseStale=.6;
        static StableNode boundNode;static savedatascript boundSave;static ulong boundSession,boundHostSession;
        internal static int ActiveProxies,WaitingProxies,PublishedPlayerColliders,PublishedProxies,HostPairs,FilterBlocked;
        internal static int[] SnapshotProxyIds{get{var s=snap;return s==null?null:s.Proxies;}}
        static int filterBlocked;
        internal static long Created,Destroyed,DeferredOverlap;
        static double noteWindow;static int noteCount;
        static void Note(string text)
        {
            if(!ModDiagnostics.On)return;
            double now=Time.realtimeSinceStartup;
            if(now-noteWindow>5){noteWindow=now;noteCount=0;}
            if(++noteCount>20)return;
            Debug.Log("[TLD MP rabbit contact] t="+now.ToString("F2")+" "+text);
        }
        static bool Contains(int[] ids,int id){if(ids==null)return false;for(int i=0;i<ids.Length;i++)if(ids[i]==id)return true;return false;}
        // Worker-thread entry: immutable id arrays only. Any contact involving
        // a proxy whose other end is not a canonical local-player physical
        // collider is dropped entirely.
        internal static void OnContacts(PhysicsScene scene,NativeArray<ModifiableContactPair> pairs)
        {
            var s=snap;if(s==null||s.Proxies==null||s.Proxies.Length==0)return;
            for(int n=0;n<pairs.Length;n++)
            {
                var pair=pairs[n];
                int a=pair.colliderInstanceID,b=pair.otherColliderInstanceID;
                bool pa=Contains(s.Proxies,a),pb=Contains(s.Proxies,b);
                if(!pa&&!pb)continue;
                if(pa&&pb)goto drop; // proxies never touch each other
                int other=pa?b:a;
                if(Contains(s.Players,other))continue; // canonical local player: real blocking contact
                drop:
                for(int i=0;i<pair.contactCount;i++)pair.IgnoreContact(i);
                Interlocked.Increment(ref filterBlocked);
            }
        }
        static void EnsureEvents()
        {
            if(eventsRegistered)return;eventsRegistered=true;
            Physics.ContactModifyEvent+=OnContacts;Physics.ContactModifyEventCCD+=OnContacts;
        }
        static void DropEvents()
        {
            if(!eventsRegistered)return;eventsRegistered=false;
            Physics.ContactModifyEvent-=OnContacts;Physics.ContactModifyEventCCD-=OnContacts;
        }
        // Single atomic publish: proxy ids plus player ids in one immutable
        // object, replaced in full. Called BEFORE enabling any collider and
        // again BEFORE destroying any remaining-proxy context.
        static void Publish()
        {
            scratchProxies.Clear();
            foreach(var p in proxies.Values)if(p.Enabled)scratchProxies.Add(p.Cap.GetInstanceID());
            scratchPlayers.Clear();
            var player=mainscript.s==null?null:mainscript.s.player;
            if(player!=null&&player.RB!=null)
            {
                var body=player.CBody;
                if(body!=null&&body.enabled&&!body.isTrigger)scratchPlayers.Add(body.GetInstanceID());
                var cols=player.C;
                if(cols!=null)foreach(var c in cols)
                    if(c!=null&&c.enabled&&!c.isTrigger&&c.attachedRigidbody==player.RB)scratchPlayers.Add(c.GetInstanceID());
            }
            snap=new Snapshot{Proxies=scratchProxies.ToArray(),Players=scratchPlayers.ToArray()};
            PublishedProxies=scratchProxies.Count;PublishedPlayerColliders=scratchPlayers.Count;
        }
        static CapsuleCollider MainCollider(nyulscript root)
        {
            var rag=root==null?null:root.ragdoll;
            if(rag==null||rag.animColliders==null)return null;
            foreach(var c in rag.animColliders)
            {
                // Guest replicas disable the originals on purpose; shape must
                // be copied regardless of the native enabled flag.
                var cap=c as CapsuleCollider;
                if(cap!=null&&!cap.isTrigger&&cap.gameObject.name=="MainCollider")return cap;
            }
            return null;
        }
        // The native capsule center in the anchor's rotation frame, in world
        // metres: TransformPoint applies the full hierarchy scale, so the
        // offset is a real world-space distance, never unscaled units.
        static Vector3 LocalOffsetOf(tosaveitemscript item,CapsuleCollider main)
        {
            var anchor=item.useThisForPos;
            return Quaternion.Inverse(anchor.rotation)*(main.transform.TransformPoint(main.center)-anchor.position);
        }
        static void ShapeOf(CapsuleCollider main,out float radius,out float height)
        {
            var scale=main.transform.lossyScale;
            float axis=main.direction==0?Mathf.Abs(scale.x):main.direction==1?Mathf.Abs(scale.y):Mathf.Abs(scale.z);
            float side=main.direction==0?Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)):main.direction==1?Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z)):Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y));
            radius=Mathf.Max(.05f,main.radius*side);
            height=Mathf.Max(main.height*axis,radius*2f);
        }
        // Created DISABLED at the target pose (no MovePosition sweep), then
        // published into the snapshot and only afterwards enabled.
        static bool CreateProxy(uint id,tosaveitemscript item,nyulscript root,CapsuleCollider main,Vector3 pos,Quaternion rot)
        {
            float radius,height;ShapeOf(main,out radius,out height);
            var go=new GameObject("TLD MP rabbit contact");
            go.layer=2; // Ignore Raycast, same convention as native MainCollider
            go.transform.position=pos;go.transform.rotation=rot;
            var rb=go.AddComponent<Rigidbody>();rb.isKinematic=true;rb.interpolation=RigidbodyInterpolation.None;
            rb.position=pos;rb.rotation=rot;
            var cap=go.AddComponent<CapsuleCollider>();
            cap.direction=main.direction;cap.center=Vector3.zero;
            cap.radius=radius;cap.height=height;
            cap.enabled=false;cap.hasModifiableContacts=true;
            if(sharedMat==null){sharedMat=new PhysicMaterial("TLD MP rabbit contact"){bounciness=0,dynamicFriction=0,staticFriction=0,bounceCombine=PhysicMaterialCombine.Minimum,frictionCombine=PhysicMaterialCombine.Minimum};}
            cap.material=sharedMat;
            var p=new Proxy{ID=id,Item=item,Root=root,Go=go,RB=rb,Cap=cap,
                LocalOffset=LocalOffsetOf(item,main),
                LocalRotOffset=Quaternion.Inverse(item.useThisForPos.rotation)*main.transform.rotation,
                LastPose=Time.realtimeSinceStartup,Radius=radius,Height=height};
            proxies[id]=p;Created++;return true;
        }
        // Local player's physical colliders only: CBody plus C[] entries riding
        // the real player body, never trigger sensors.
        static bool PlayerClear(Bounds candidate)
        {
            var player=mainscript.s==null?null:mainscript.s.player;
            if(player==null||player.RB==null)return true;
            var body=player.CBody;
            if(body!=null&&body.enabled&&!body.isTrigger&&body.bounds.Intersects(candidate))return false;
            var cols=player.C;
            if(cols!=null)foreach(var c in cols)if(c!=null&&c.enabled&&!c.isTrigger&&c.attachedRigidbody==player.RB&&c.bounds.Intersects(candidate))return false;
            return true;
        }
        static Bounds ProxyBounds(Vector3 pos,CapsuleCollider cap)
        {
            float half=Mathf.Max(cap.radius,cap.height*.5f);
            return new Bounds(pos,new Vector3(half*2,half*2,half*2));
        }
        // A live proxy is valid while its canonical item, registered root and
        // applied metadata all agree it is a living, unheld rabbit; a mere
        // buffer gap may hold briefly, anything else removes at once.
        static bool AliveGuest(Proxy p,double now)
        {
            var item=p.Item;var root=p.Root;
            if(item==null||!item.gameObject.activeInHierarchy||root==null||!root.gameObject.activeInHierarchy)return false;
            if(!ReferenceEquals(RabbitReplicas.RabbitRoot(item),root))return false;
            if(root.senses==null||!root.senses.alive||!StableGameBridge.RabbitContactCurrent(p.ID,item,root,now))return false;
            RabbitMetadata meta;nyulscript metaRoot;
            // Metadata bit 2 means ALIVE, not dead.
            if(!RabbitMetadataSync.TryApplied(p.ID,out meta,out metaRoot)||(meta.Flags&2)==0||!ReferenceEquals(metaRoot,root))return false;
            return true;
        }
        static void GuestTick(double now)
        {
            var save=savedatascript.s;
            // 1) validate + move live proxies on the shared motion timeline
            remove.Clear();
            foreach(var pair in proxies)
            {
                var p=pair.Value;
                if(!AliveGuest(p,now)){DestroyProxy(p);remove.Add(pair.Key);continue;}
                RemotePose pose;
                if(!StableGameBridge.TryRabbitPose(pair.Key,p.Item,now,out pose))
                {
                    if(now-p.LastPose<=PoseStale)continue; // buffer gap only
                    DestroyProxy(p);remove.Add(pair.Key);continue;
                }
                p.LastPose=now;
                var main=MainCollider(p.Root);
                if(main==null){DestroyProxy(p);remove.Add(pair.Key);continue;}
                p.LocalOffset=LocalOffsetOf(p.Item,main);
                p.LocalRotOffset=Quaternion.Inverse(p.Item.useThisForPos.rotation)*main.transform.rotation;
                float radius,height;ShapeOf(main,out radius,out height);
                if(p.Cap.radius!=radius)p.Cap.radius=radius;
                if(p.Cap.height!=height)p.Cap.height=height;
                p.Cap.direction=main.direction;
                var anchorPos=mainscript.UnityPosFromGlobal(new Vector3d(pose.X,pose.Y,pose.Z));
                var anchorRot=new Quaternion((float)pose.Qx,(float)pose.Qy,(float)pose.Qz,(float)pose.Qw);
                Vector3 pos=anchorPos+anchorRot*p.LocalOffset;Quaternion rot=anchorRot*p.LocalRotOffset;
                if(p.WaitingClear)
                {
                    if(!PlayerClear(ProxyBounds(pos,p.Cap)))continue;
                    p.WaitingClear=false;
                    // snapshot first, enable second
                    p.Enabled=true;Publish();p.Cap.enabled=true;
                }
                p.RB.MovePosition(pos);p.RB.MoveRotation(rot);
            }
            foreach(var id in remove){proxies.Remove(id);Publish();}
            // 2) discovery bounded to ACTIVE guest motion ids (max 256), never
            // a whole-world item scan
            motionIds.Clear();StableGameBridge.ForEachGuestMotionId(motionIds);
            if(motionIds.Count==0)return;
            if(motionCursor>=motionIds.Count)motionCursor=0;
            int scanned=0;
            while(motionCursor<motionIds.Count&&scanned<ScanBudget)
            {
                var id=motionIds[motionCursor++];scanned++;
                if(proxies.ContainsKey(id))continue;
                tosaveitemscript item;
                if(!save.items.TryGetValue(id,out item)||item==null)continue;
                nyulscript metaRoot;RabbitMetadata meta;
                if(!RabbitMetadataSync.TryApplied(id,out meta,out metaRoot)||(meta.Flags&2)==0||metaRoot==null||
                   !StableGameBridge.RabbitContactCurrent(id,item,metaRoot,now))continue;
                var main=MainCollider(metaRoot);if(main==null)continue;
                RemotePose pose;
                if(!StableGameBridge.TryRabbitPose(id,item,now,out pose))continue;
                var anchor=item.useThisForPos;if(anchor==null)continue;
                var anchorPos=mainscript.UnityPosFromGlobal(new Vector3d(pose.X,pose.Y,pose.Z));
                var anchorRot=new Quaternion((float)pose.Qx,(float)pose.Qy,(float)pose.Qz,(float)pose.Qw);
                var off=LocalOffsetOf(item,main);
                Vector3 pos=anchorPos+anchorRot*off;
                Quaternion rot=anchorRot*Quaternion.Inverse(anchor.rotation)*main.transform.rotation;
                if(!CreateProxy(id,item,metaRoot,main,pos,rot))continue;
                var created=proxies[id];
                // publish the new id snapshot BEFORE enabling the collider
                if(PlayerClear(ProxyBounds(pos,created.Cap))){created.Enabled=true;Publish();created.Cap.enabled=true;}
                else{created.WaitingClear=true;DeferredOverlap++;WaitingProxies++;}
            }
        }
        // Cheap per-root registration: called from the native rabbit-impact
        // configuration and the motion observe path so a freshly initialised
        // rabbit is inside the exclusion set before its first physics step.
        internal static void ObserveRabbit(nyulscript root)
        {
            if(root==null||registry.Contains(root))return;
            registry.Add(root);
        }
        // Canonical ready remote actor SOLID colliders only, validated against
        // the actor hierarchy so a swapped proxy object cannot slip in.
        static void RemoteColliders(List<Collider> dst)
        {
            var node=StableGameBridge.Node;
            if(node==null)return;
            RabbitPlayers.Actor actor;
            foreach(var kv in node.Peers)
            {
                if(!RabbitPlayers.TryGet(kv.Key,out actor)||actor==null||actor.Mp==null||actor.Root==null)continue;
                var body=actor.Mp.CBody;
                if(body!=null&&body.enabled&&!body.isTrigger&&body.transform.IsChildOf(actor.Root.transform))dst.Add(body);
                var cols=actor.Mp.cols;
                if(cols!=null)foreach(var c in cols)
                    if(c!=null&&c.enabled&&!c.isTrigger&&c.transform.IsChildOf(actor.Root.transform))dst.Add(c);
            }
        }
        static void HostTick(double now)
        {
            var save=savedatascript.s;
            if(save==null||save.items==null){RestorePairs();return;}
            var wantedRabbits=new List<Collider>();
            for(int i=registry.Count-1;i>=0;i--)
            {
                var root=registry[i];
                if(root==null||root.senses==null||!root.senses.alive||root.ragdoll==null||root.ragdoll.animColliders==null)
                {registry.RemoveAt(i);continue;}
                var item=root.GetComponent<tosaveitemscript>();tosaveitemscript canonical;
                if(item==null){registry.RemoveAt(i);continue;}
                if(item.idInSave==0)continue;
                if(!save.items.TryGetValue(item.idInSave,out canonical)||canonical!=item||
                   !ReferenceEquals(RabbitReplicas.RabbitRoot(item),root))
                {registry.RemoveAt(i);continue;}
                RabbitPlayers.ProbeDamage(root);
                foreach(var c in root.ragdoll.animColliders)if(c!=null&&c.enabled&&!c.isTrigger)wantedRabbits.Add(c);
            }
            var wantedRemote=new List<Collider>();RemoteColliders(wantedRemote);
            navIgnore.Clear();
            foreach(var c in wantedRemote)navIgnore.Add(c.GetInstanceID());
            // drop pairs that left scope; restore only flags we set
            for(int i=hostPairs.Count-1;i>=0;i--)
            {
                var pair=hostPairs[i];bool keep=false;
                foreach(var a in wantedRabbits){if(pair.A!=a)continue;foreach(var b in wantedRemote)if(pair.B==b){keep=true;break;}if(keep)break;}
                if(!keep){if(pair.A!=null&&pair.B!=null&&Physics.GetIgnoreCollision(pair.A,pair.B))Physics.IgnoreCollision(pair.A,pair.B,false);hostPairs.RemoveAt(i);}
            }
            foreach(var a in wantedRabbits)
            {
                if(a==null)continue;
                foreach(var b in wantedRemote)
                {
                    if(b==null)continue;
                    bool have=false;foreach(var pair in hostPairs)if(pair.A==a&&pair.B==b){have=true;break;}
                    // never overwrite a pre-existing native ignore, and never
                    // restore one we did not create
                    if(!have&&!Physics.GetIgnoreCollision(a,b)){Physics.IgnoreCollision(a,b,true);hostPairs.Add(new IgnorePair{A=a,B=b});}
                }
            }
            HostPairs=hostPairs.Count;
        }
        static void RestorePairs()
        {
            foreach(var pair in hostPairs)if(pair.A!=null&&pair.B!=null&&Physics.GetIgnoreCollision(pair.A,pair.B))Physics.IgnoreCollision(pair.A,pair.B,false);
            hostPairs.Clear();HostPairs=0;
        }
        // Disabled first, removed from the snapshot-published set, then the
        // GameObject destroyed. The dictionary removal is the caller's job so
        // iteration is never disturbed.
        static void DestroyProxy(Proxy p)
        {
            if(p.Cap!=null)p.Cap.enabled=false;
            if(p.RB!=null)p.RB.isKinematic=true;
            p.Enabled=false;
            Publish();
            if(p.Go!=null)UnityEngine.Object.Destroy(p.Go);
            Destroyed++;
        }
        static void DestroyAll()
        {
            foreach(var p in proxies.Values)
            {
                if(p.Cap!=null)p.Cap.enabled=false;p.Enabled=false;
                if(p.Go!=null)UnityEngine.Object.Destroy(p.Go);
            }
            proxies.Clear();Publish();
        }
        // Remote-avatar solid proxies are navigation-transparent for canonical
        // host rabbits; they are never terrain, walls or blockers.
        internal static bool IgnoreForNavigation(Collider c)
        {
            return c!=null&&navIgnore.Contains(c.GetInstanceID());
        }
        internal static void FixedTick(bool connected,double now)
        {
            var node=StableGameBridge.Node;var save=savedatascript.s;
            var player=mainscript.s==null?null:mainscript.s.player;
            bool ready=connected&&node!=null&&node.Compatible&&node.Error==null&&StableGameBridge.WorldScene()&&
                       save!=null&&save.items!=null&&player!=null&&player.loadedmove&&player.RB!=null&&
                       (node.IsHost||node.Ready);
            if(!ready){Teardown();return;}
            if(boundNode!=node||boundSave!=save||boundSession!=node.Session||boundHostSession!=node.HostSession)
            {Teardown();boundNode=node;boundSave=save;boundSession=node.Session;boundHostSession=node.HostSession;}
            EnsureEvents();
            Publish();
            FilterBlocked=Interlocked.Exchange(ref filterBlocked,0);
            WaitingProxies=0;foreach(var p in proxies.Values)if(p.WaitingClear)WaitingProxies++;
            if(node.IsHost){DestroyAllProxiesOnly();HostTick(now);}
            else{RestorePairs();navIgnore.Clear();GuestTick(now);}
            ActiveProxies=proxies.Count;
        }
        static void DestroyAllProxiesOnly()
        {
            if(proxies.Count==0){Publish();return;}
            DestroyAll();
        }
        static void Teardown()
        {
            foreach(var p in proxies.Values){if(p.Cap!=null)p.Cap.enabled=false;p.Enabled=false;if(p.Go!=null)UnityEngine.Object.Destroy(p.Go);}
            proxies.Clear();RestorePairs();navIgnore.Clear();
            boundNode=null;boundSave=null;boundSession=boundHostSession=0;
            ActiveProxies=WaitingProxies=0;
            snap=new Snapshot();
            DropEvents();
        }
        internal static void Reset()
        {
            Teardown();registry.Clear();motionIds.Clear();motionCursor=0;
            snap=new Snapshot();DropEvents();
            Created=Destroyed=DeferredOverlap=0;FilterBlocked=0;
            Interlocked.Exchange(ref filterBlocked,0);
        }
        internal static string TakeSummary()
        {
            string text="contact{enabled="+PublishedProxies+" waiting="+WaitingProxies+" created="+Created+" destroyed="+Destroyed+" deferred="+DeferredOverlap+
                " playerCols="+PublishedPlayerColliders+" hostPairs="+HostPairs+" blocked="+FilterBlocked+"}";
            Created=Destroyed=DeferredOverlap=0;
            return text;
        }
    }
}
