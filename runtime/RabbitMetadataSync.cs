using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    internal static class RabbitMetadataSync
    {
        const int MaxCache=256;
        static readonly Dictionary<uint,RabbitMetadata> pending=new Dictionary<uint,RabbitMetadata>();
        static readonly Dictionary<uint,RabbitMetadata> sent=new Dictionary<uint,RabbitMetadata>();
        static readonly Dictionary<uint,nyulscript> sentRoots=new Dictionary<uint,nyulscript>();
        static readonly Dictionary<uint,Applied> applied=new Dictionary<uint,Applied>();
        static readonly Dictionary<ulong,ulong> ready=new Dictionary<ulong,ulong>();
        static readonly List<tosaveitemscript> snap=new List<tosaveitemscript>();
        static readonly List<uint> gone=new List<uint>();
        static StableNode node;
        static ulong hostSession;
        static savedatascript data;
        static fpscontroller player;
        static double lastNow=double.MinValue;
        static double nextScan;
        static bool healthy=true;
        static bool world;
        static bool logged;
        internal static System.Func<RabbitMetadata,RabbitMetadata> HostState;
        internal static bool MotionStreamOwnsLivingPoses;
        internal static bool Healthy{get{return healthy;}}
        internal static void RequestScan(){nextScan=0;}
        internal static bool TryApplied(uint id,out RabbitMetadata meta,out nyulscript root)
        {
            meta=default(RabbitMetadata);root=null;
            if(!healthy||node==null||node.IsHost||hostSession!=node.HostSession||!node.Compatible||node.Error!=null||!node.Ready||
                !world||player==null||!player.loadedmove||data==null)return false;
            RabbitMetadata current;Applied ap;tosaveitemscript item;
            if(!pending.TryGetValue(id,out current)||!applied.TryGetValue(id,out ap)||!current.Equals(ap.meta)||
                !data.items.TryGetValue(id,out item)||item==null||item.idInSave!=id||item.id!=current.Prefab||
                !ReferenceEquals(Root(item),ap.root)||!Ready(ap.root)||!Canonical(data,item,ap.root))return false;
            meta=current;root=ap.root;return true;
        }
        struct Applied{public nyulscript root;public RabbitMetadata meta;}
        static bool Fin(double v){return !double.IsNaN(v)&&!double.IsInfinity(v);}
        static bool Fin(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        internal static void Reset()
        {
            if(node!=null)node.RabbitStateReceived-=OnState;
            ClearWorld();gone.Clear();snap.Clear();
            node=null;hostSession=0;player=null;data=null;world=false;healthy=true;logged=false;
        }
        static void Fail(string why)
        {
            if(!logged){Debug.LogWarning("RabbitMetadataSync fail: "+why);logged=true;}
            healthy=false;pending.Clear();applied.Clear();sent.Clear();sentRoots.Clear();
        }
        static void ClearWorld()
        {
            pending.Clear();sent.Clear();sentRoots.Clear();applied.Clear();ready.Clear();nextScan=0;lastNow=double.MinValue;
        }
        internal static void Tick(bool enabled,bool worldStarted,fpscontroller p,savedatascript d,StableNode n,double now)
        {
            if(!enabled||n==null||!n.Compatible||n.Error!=null){Reset();return;}
            if(!ReferenceEquals(n,node)||hostSession!=n.HostSession)
            {
                Reset();
                node=n;hostSession=n.HostSession;n.RabbitStateReceived+=OnState;
                player=p;data=d;nextScan=now;
            }
            else
            {
                if((player!=null&&!ReferenceEquals(p,player))||
                    (data!=null&&!ReferenceEquals(d,data))||(world&&!worldStarted))
                {
                    ClearWorld();healthy=true;logged=false;
                }
                player=p;data=d;
            }
            bool newWorld=worldStarted;
            if(!world&&newWorld){nextScan=now;}
            world=newWorld;
            if(!healthy)return;
            if(!Fin(now)){Fail("bad now");return;}
            if(now<lastNow)nextScan=now;
            lastNow=now;
            try
            {
                if(n.IsHost)
                {
                    if(newWorld&&player!=null&&player.loadedmove&&data!=null)HostTick(n,data,now);
                }
                else GuestTick(n,player,data,newWorld);
            }
            catch(System.Exception e){Fail("native "+e.GetType().Name);}
        }
        static void OnState(byte[] bytes)
        {
            RabbitMetadata meta;
            if(!healthy||node==null||node.IsHost||hostSession!=node.HostSession||!RabbitProtocol.TryReadMetadata(bytes,out meta))return;
            if((meta.Flags&1)!=0)
            {
                pending.Remove(meta.Item);applied.Remove(meta.Item);
                return;
            }
            if(!pending.ContainsKey(meta.Item)&&pending.Count>=MaxCache){Fail("cache overflow");return;}
            pending[meta.Item]=meta;
        }
        static nyulscript Root(tosaveitemscript item)
        {
            if(item==null)return null;
            var n=item.GetComponent<nyulscript>();
            if(n!=null)return n;
            return item.nyul;
        }
        static bool Ready(nyulscript n)
        {
            return n!=null&&n.started&&n.senses!=null&&n.breakable!=null&&n.AI!=null&&n.ragdoll!=null;
        }
        static RabbitMetadata Meta(uint id,int prefab,nyulscript n)
        {
            var f=(byte)0;
            if(n.senses.alive)f|=2;
            if(n.senses.hasEars)f|=4;
            if(n.senses.hasEyes)f|=8;
            if(n.ragdoll.ragdoll)f|=16;
            return new RabbitMetadata{Item=id,Prefab=prefab,Flags=f,Size=n.size,Health=n.breakable.health,ShootHealth=n.breakable.shootHealth,ExplosionHealth=n.breakable.explosionHealth,Part=-1};
        }
        static bool Canonical(savedatascript d,tosaveitemscript item,nyulscript root)
        {
            tosaveitemscript current;
            return item!=null&&root!=null&&d.items.TryGetValue(item.idInSave,out current)&&
                ReferenceEquals(current,item)&&ReferenceEquals(Root(current),root);
        }
        static bool Gone(savedatascript d,uint id)
        {
            tosaveitemscript item;
            return !d.items.TryGetValue(id,out item)||item==null||Root(item)==null;
        }
        static void HostTick(StableNode n,savedatascript d,double now)
        {
            var sc=syncScript.s;
            if(sc==null||!Fin(sc.syncItemInterwall)||sc.syncItemInterwall<=0f){Fail("bad interval");return;}
            bool fresh=false;
            var cur=new Dictionary<ulong,ulong>();
            foreach(var kv in n.Peers){var pr=kv.Value;if(pr!=null&&pr.Ready)cur[kv.Key]=pr.Session;}
            if(cur.Count!=ready.Count)fresh=true;
            else foreach(var kv in cur){ulong s;if(!ready.TryGetValue(kv.Key,out s)||s!=kv.Value){fresh=true;break;}}
            if(fresh){ready.Clear();foreach(var kv in cur)ready[kv.Key]=kv.Value;nextScan=now;}
            if(!fresh&&now<nextScan)return;
            nextScan=now+sc.syncItemInterwall;
            snap.Clear();
            foreach(var kv in d.items)snap.Add(kv.Value);
            var anyPeer=ready.Count>0;
            for(int i=0;i<snap.Count;i++)
            {
                var item=snap[i];
                if(item==null)continue;
                var nyul=Root(item);
                if(nyul==null||!Ready(nyul))continue;
                if(item.idInSave==0)continue;
                if(!Canonical(d,item,nyul))continue;
                if(!Fin(nyul.size)||!Fin(nyul.breakable.health)||!Fin(nyul.breakable.shootHealth)||!Fin(nyul.breakable.explosionHealth)){Fail("native nan");return;}
                var meta=Meta(item.idInSave,item.id,nyul);
                var hostState=HostState;
                if(hostState!=null)
                {
                    var carry=hostState(meta);
                    meta.Holder=carry.Holder;meta.HolderSession=carry.HolderSession;
                    meta.Lease=carry.Lease;meta.Request=carry.Request;meta.Part=carry.Part;
                    if(!Canonical(d,item,nyul))continue;
                }
                RabbitMetadata old;
                nyulscript oldRoot;
                if(fresh||!sent.TryGetValue(item.idInSave,out old)||!old.Equals(meta)||
                    !sentRoots.TryGetValue(item.idInSave,out oldRoot)||!ReferenceEquals(oldRoot,nyul))
                {
                    if(!anyPeer)continue;
                    if(n.PublishRabbitState(RabbitProtocol.WriteMetadata(meta)))
                    {
                        sent[item.idInSave]=meta;sentRoots[item.idInSave]=nyul;
                    }
                }
                if(!Canonical(d,item,nyul))continue;
                if(anyPeer&&nyul.gameObject.activeInHierarchy&&item.useThisForPos!=null&&(!MotionStreamOwnsLivingPoses||nyul.ragdoll.ragdoll))
                {
                    var pos=item.useThisForPos.position;var eul=item.useThisForPos.eulerAngles;
                    if(Fin(pos.x)&&Fin(pos.y)&&Fin(pos.z)&&Fin(eul.x)&&Fin(eul.y)&&Fin(eul.z))
                    {
                        sc.SendItemPosGlobal(item.idInSave,item.id,mainscript.GlobalFromUnityPos(pos),eul);
                        if(!Canonical(d,item,nyul))continue;
                        var rp=item.ragdollPos;
                        if(rp!=null&&rp.childs!=null&&rp.childs.Length>=1&&rp.childs.Length<=128)
                        {
                            bool okc=true;
                            for(int c=0;c<rp.childs.Length;c++)
                            {
                                var t=rp.childs[c];
                                if(t==null){okc=false;break;}
                                var cp=t.position;var cr=t.eulerAngles;
                                if(!Fin(cp.x)||!Fin(cp.y)||!Fin(cp.z)||!Fin(cr.x)||!Fin(cr.y)||!Fin(cr.z)){okc=false;break;}
                            }
                            if(okc)sc.SendRagdoll(item.idInSave,item.useThisForPos,rp);
                        }
                    }
                }
            }
            gone.Clear();
            foreach(var kv in sent)if(Gone(d,kv.Key))gone.Add(kv.Key);
            for(int i=0;i<gone.Count;i++)
            {
                var id=gone[i];
                if(!Gone(d,id))continue;
                var tomb=new RabbitMetadata{Item=id,Prefab=sent[id].Prefab,Flags=1,Part=-1};
                if(anyPeer&&n.PublishRabbitState(RabbitProtocol.WriteMetadata(tomb)))
                {
                    sent.Remove(id);sentRoots.Remove(id);
                }
            }
        }
        static void GuestTick(StableNode n,fpscontroller p,savedatascript d,bool worldStarted)
        {
            if(!worldStarted||p==null||!p.loadedmove||d==null||!n.Ready)return;
            var list=new List<RabbitMetadata>(pending.Values);
            for(int i=0;i<list.Count;i++)
            {
                var meta=list[i];
                tosaveitemscript item;
                if(!d.items.TryGetValue(meta.Item,out item)||item==null||item.id!=meta.Prefab)continue;
                var nyul=Root(item);
                if(!Ready(nyul))continue;
                Apply(meta,nyul);
                if(!healthy)return;
            }
        }
        static void Apply(RabbitMetadata meta,nyulscript nyul)
        {
            Applied ap;bool have=applied.TryGetValue(meta.Item,out ap);
            bool sameRoot=have&&ReferenceEquals(ap.root,nyul);
            bool sameMeta=have&&ap.meta.Equals(meta);
            if(sameRoot&&sameMeta)return;
            if(!have&&applied.Count>=MaxCache){Fail("applied overflow");return;}
            bool wasDead=sameRoot&&(ap.meta.Flags&2)==0;
            RabbitReplicas.BeginMutation(nyul);
            try
            {
                var senses=nyul.senses;
                if(nyul.size!=meta.Size){nyul.started=false;nyul.SetBasedOnSize(meta.Size);}
                nyul.breakable.health=meta.Health;nyul.breakable.shootHealth=meta.ShootHealth;nyul.breakable.explosionHealth=meta.ExplosionHealth;
                if((meta.Flags&4)==0&&senses.hasEars)senses.LooseEars();
                else if((meta.Flags&4)!=0)senses.hasEars=true;
                if((meta.Flags&8)==0&&senses.hasEyes)senses.LooseEyes();
                else if((meta.Flags&8)!=0){senses.hasEyes=true;var eo=senses.eyeObjects;if(eo!=null)for(int i=0;i<eo.Length;i++)if(eo[i]!=null)eo[i].SetActive(true);}
                if((meta.Flags&2)==0&&senses.alive&&!wasDead)nyul.AI.StartDie();
                if((meta.Flags&16)!=0&&!nyul.ragdoll.ragdoll)nyul.ragdoll.SetRagdol(true);
                else if((meta.Flags&16)==0&&nyul.ragdoll.ragdoll&&senses.alive)nyul.ragdoll.SetRagdol(false);
                applied[meta.Item]=new Applied{root=nyul,meta=meta};
            }
            finally{RabbitReplicas.EndMutation(nyul);}
        }
    }
}
