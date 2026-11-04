using System.Collections.Generic;
using UnityEngine;
using Actor=TldMp.RabbitPlayers.Actor;

namespace TldMp
{
    internal static class RabbitCombat
    {
        internal sealed class ToolSt
        {
            internal tosaveitemscript item;
            internal weaponscript weapon;
            internal meleeweaponscript melee;
            internal double time;
            internal int frame=-1;
            internal int meleeFrame=-1;
            internal Vector3d sampleGlobal;
            internal bool hasSample;
        }
        internal sealed class Peer
        {
            internal ulong session,nonce,burstToken;
            internal uint burstTool;
            internal weaponscript burstWeapon;
            internal HashSet<int> seen;
            internal readonly Dictionary<uint,ToolSt> tools=new Dictionary<uint,ToolSt>();
        }
        static bool enabled,world,healthy=true,logged,boundHost;
        static fpscontroller player;
        static savedatascript data;
        static StableNode node;
        static ulong boundSession;
        static double lastNow=double.MinValue;
        static ulong clientNonce,clientBurst;
        static uint clientTool;
        static weaponscript clientWeapon;
        static int clientFrame=-1;
        static int meleeFrame=-1;
        static readonly Dictionary<ulong,Peer> peers=new Dictionary<ulong,Peer>();
        internal static bool Healthy{get{return healthy;}}
        static bool Fin(double v){return !double.IsNaN(v)&&!double.IsInfinity(v);}
        static bool Fin(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        static bool Fin(Vector3 v){return Fin(v.x)&&Fin(v.y)&&Fin(v.z);}
        internal static void Reset()
        {
            enabled=world=boundHost=false;player=null;data=null;node=null;boundSession=0;
            peers.Clear();clientNonce=clientBurst=clientTool=0;clientWeapon=null;clientFrame=meleeFrame=-1;
            lastNow=double.MinValue;healthy=true;logged=false;
        }
        static void Fail(string w){if(!logged){Debug.LogWarning("RabbitCombat fail: "+w);logged=true;}healthy=false;peers.Clear();}
        internal static void Bind(bool en,bool worldStarted,fpscontroller p,savedatascript d,StableNode n)
        {
            bool nh=n!=null&&n.IsHost;ulong ns=n!=null?n.Session:0;
            bool changed=!ReferenceEquals(n,node)||!ReferenceEquals(p,player)||!ReferenceEquals(d,data)||worldStarted!=world||en!=enabled||nh!=boundHost||ns!=boundSession;
            if(changed)
            {
                peers.Clear();clientNonce=clientBurst=clientTool=0;clientWeapon=null;clientFrame=meleeFrame=-1;lastNow=double.MinValue;
                healthy=true;logged=false;
                enabled=en;world=worldStarted;player=p;data=d;node=n;boundHost=nh;boundSession=ns;
            }
            if(Active())Prune();
        }
        static void Prune()
        {
            var gone=new List<ulong>();
            foreach(var kv in peers)
            {
                StablePeer p;
                if(!node.Peers.TryGetValue(kv.Key,out p)||p==null||p.Session==0||p.Session!=kv.Value.session)gone.Add(kv.Key);
            }
            foreach(var g in gone)peers.Remove(g);
            var dead=new List<uint>();
            foreach(var kv in peers)
            {
                dead.Clear();
                foreach(var t in kv.Value.tools)
                {
                    tosaveitemscript it;
                    var ts=t.Value;
                    if(!data.items.TryGetValue(t.Key,out it)||it==null||!ReferenceEquals(it,ts.item)||it.P==null)dead.Add(t.Key);
                    else if((object)ts.weapon!=null&&(ts.weapon==null||!ReferenceEquals(ts.weapon,it.P.weapon)))dead.Add(t.Key);
                    else if((object)ts.melee!=null&&(ts.melee==null||!ReferenceEquals(ts.melee,it.P.melee)))dead.Add(t.Key);
                }
                foreach(var k in dead)kv.Value.tools.Remove(k);
                if((object)kv.Value.burstWeapon!=null)
                {
                    tosaveitemscript it;
                    if(kv.Value.burstWeapon==null||!data.items.TryGetValue(kv.Value.burstTool,out it)||it==null||it.P==null||!ReferenceEquals(it.P.weapon,kv.Value.burstWeapon))kv.Value.burstToken=0;
                }
            }
        }
        static bool Active()
        {
            return enabled&&healthy&&world&&node!=null&&node.Compatible&&node.Error==null&&player!=null&&player.loadedmove&&data!=null&&data.items!=null;
        }
        static bool ClientActive()
        {
            return Active()&&!node.IsHost&&node.Ready&&mainscript.s!=null&&ReferenceEquals(mainscript.s.player,player);
        }
        static bool SameActor(Actor a)
        {
            Actor cur;
            return RabbitPlayers.TryGet(a.Id,out cur)&&cur.Session==a.Session&&ReferenceEquals(cur.Mp,a.Mp)&&ReferenceEquals(cur.Root,a.Root);
        }
        static tosaveitemscript CanonItem(uint id)
        {
            tosaveitemscript it;
            if(id==0||!data.items.TryGetValue(id,out it)||it==null)return null;
            if(it.idInSave!=id||!it.gameObject.activeInHierarchy)return null;
            var P=it.P;
            if(P==null||!P.started||!ReferenceEquals(P.tosave,it))return null;
            return it;
        }
        static bool ForeignHeld(tosaveitemscript it,mpplayerscript mp)
        {
            var anc=it.GetComponentInParent<mpplayerscript>();
            return anc!=null&&!ReferenceEquals(anc,mp);
        }
        static bool HostHolds(tosaveitemscript it)
        {
            var P=it.P;var local=mainscript.s;
            if(P==null||local==null||local.player==null)return false;
            return local.player.pickedUp==P||local.player.inHandP==P||P.PickedUp(false)||P.inInventory()>0;
        }
        static bool Owns(tosaveitemscript it,mpplayerscript mp)
        {
            if(mp==null)return false;
            if(it.mpequipped&&!it.mpstored&&!it.mpweared&&mp.Th!=null&&it.transform.IsChildOf(mp.Th))return true;
            if(it.mppickedup&&!it.mpstored&&!it.mpweared&&mp.handParent!=null&&it.transform.IsChildOf(mp.handParent))return true;
            return false;
        }
        static weaponscript CanonGun(tosaveitemscript it)
        {
            var w=it.P.weapon;
            if(w==null||!ReferenceEquals(w.P,it.P)||!w.transform.IsChildOf(it.transform))return null;
            return w;
        }
        static meleeweaponscript CanonMelee(tosaveitemscript it)
        {
            var m=it.P.melee;
            if(m==null||!ReferenceEquals(m.p,it.P)||!m.transform.IsChildOf(it.transform))return null;
            return m;
        }
        static bool WorldGun(tosaveitemscript it,weaponscript w,Transform head)
        {
            if(it.mpequipped||it.mppickedup||it.mpstored||it.mpweared)return false;
            if(it.GetComponentInParent<mpplayerscript>()!=null)return false;
            if(HostHolds(it))return false;
            if(head==null||w.S==null)return false;
            float fray=mainscript.s.player.FrayRange;
            if(!Fin(fray)||fray<=0f)return false;
            return (w.S.position-head.position).magnitude<=fray;
        }
        internal static void Receive(ulong peer,RabbitCommand cmd,double now)
        {
            if(!Active()||!node.IsHost)return;
            if(!Fin(now)||now<0)return;
            if(now<lastNow)foreach(var kv in peers)kv.Value.tools.Clear();
            lastNow=now;
            Prune();
            Actor actor;
            if(!RabbitPlayers.TryGet(peer,out actor))return;
            Peer st;
            if(!peers.TryGetValue(peer,out st)||st.session!=actor.Session){st=new Peer{session=actor.Session};peers[peer]=st;}
            try
            {
                if(cmd.Kind==RabbitCommandKind.Shot)Shot(st,actor,cmd,now);
                else if(cmd.Kind==RabbitCommandKind.Melee)Melee(st,actor,cmd,now);
            }
            catch{Fail("receive");}
        }
        static Vector3 NormVec(Vector3 v)
        {
            double dn=(double)v.x*v.x+(double)v.y*v.y+(double)v.z*v.z;
            if(dn<=0||!Fin(dn))return new Vector3(float.NaN,0,0);
            double s=System.Math.Sqrt(dn);
            return new Vector3((float)(v.x/s),(float)(v.y/s),(float)(v.z/s));
        }
        static Vector3 DirOf(RabbitVector v){return NormVec(new Vector3(v.X,v.Y,v.Z));}
        static Quaternion RotOf(RabbitRotation r)
        {
            double qn=(double)r.X*r.X+(double)r.Y*r.Y+(double)r.Z*r.Z+(double)r.W*r.W;
            if(qn<=0||!Fin(qn))return new Quaternion(float.NaN,0,0,0);
            double s=System.Math.Sqrt(qn);
            return new Quaternion((float)(r.X/s),(float)(r.Y/s),(float)(r.Z/s),(float)(r.W/s));
        }
        static nyulscript NpcRoot(Collider c,out breakchilds bc,out tosaveitemscript tsi)
        {
            bc=c.GetComponentInParent<breakchilds>();
            tsi=null;
            nyulscript n=c.GetComponentInParent<nyulscript>();
            if(n!=null)tsi=n.GetComponent<tosaveitemscript>();
            if(tsi==null)
            {
                var pk=c.GetComponentInParent<pickupable>();
                if(pk!=null&&pk.tosave!=null){tsi=pk.tosave;n=tsi.nyul;}
            }
            if(tsi==null&&bc!=null&&bc.P!=null)
            {
                n=bc.P.GetComponent<nyulscript>();
                if(n!=null)tsi=n.GetComponent<tosaveitemscript>();
            }
            if(n==null||tsi==null||!ReferenceEquals(n.GetComponent<tosaveitemscript>(),tsi))return null;
            if(!n.started||!n.gameObject.activeInHierarchy||n.senses==null||n.AI==null||n.breakable==null||n.ragdoll==null)return null;
            if(tsi.idInSave==0)return null;
            tosaveitemscript canon;
            if(!data.items.TryGetValue(tsi.idInSave,out canon)||!ReferenceEquals(canon,tsi))return null;
            return n;
        }
        static bool CanonRoot(tosaveitemscript tsi)
        {
            tosaveitemscript canon;
            return tsi!=null&&data.items.TryGetValue(tsi.idInSave,out canon)&&ReferenceEquals(canon,tsi);
        }
        static bool SkipHit(Collider col,Actor a,tosaveitemscript it)
        {
            if(col==null)return true;
            if(col.CompareTag("Ignore"))return true;
            if(col.isTrigger&&!col.CompareTag("Flesh")&&!col.CompareTag("Plant"))return true;
            if(col.transform.IsChildOf(a.Root.transform))return true;
            if(it!=null&&col.transform.IsChildOf(it.transform))return true;
            return false;
        }
        static bool Transparent(Collider col)
        {
            return col.CompareTag("Plant")||col.CompareTag("Cobweb")||col.gameObject.layer==23;
        }
        static void Shot(Peer st,Actor actor,RabbitCommand cmd,double now)
        {
            var it=CanonItem(cmd.Tool);if(it==null)return;
            var w=CanonGun(it);if(w==null)return;
            if(!Owns(it,actor.Mp))
            {
                if(!WorldGun(it,w,actor.Mp.THead))return;
            }
            else if(ForeignHeld(it,actor.Mp)||HostHolds(it))return;
            if(!Fin(w.minShootTime)||w.minShootTime<0f)return;
            int max=System.Math.Max(1,w.shotCount);
            if(cmd.Index<0||cmd.Index>=max)return;
            if(cmd.Token==0)return;
            if(st.seen==null||cmd.Token!=st.burstToken)
            {
                if(cmd.Index!=0)return;
                if(cmd.Token<=st.nonce)return;
                st.nonce=cmd.Token;
                ToolSt ts;
                if(st.tools.TryGetValue(cmd.Tool,out ts)&&ReferenceEquals(ts.weapon,w))
                {
                    if(now-ts.time<w.minShootTime)return;
                    if(ts.frame==Time.frameCount)return;
                }
                if(ts==null){ts=new ToolSt();st.tools[cmd.Tool]=ts;}
                ts.item=it;ts.weapon=w;ts.time=now;ts.frame=Time.frameCount;
                st.burstToken=cmd.Token;st.burstTool=cmd.Tool;st.burstWeapon=w;st.seen=new HashSet<int>();
            }
            else if(cmd.Token!=st.nonce||cmd.Index==0||st.burstTool!=cmd.Tool||!ReferenceEquals(st.burstWeapon,w)||st.seen.Contains(cmd.Index))return;
            st.seen.Add(cmd.Index);
            if(!Fin(cmd.Position.X)||!Fin(cmd.Position.Y)||!Fin(cmd.Position.Z))return;
            var origin=mainscript.UnityPosFromGlobal(new Vector3d(cmd.Position.X,cmd.Position.Y,cmd.Position.Z));
            if(!Fin(origin))return;
            var head=actor.Mp.THead;if(head==null)return;
            float fray=mainscript.s.player.FrayRange;
            if(!Fin(fray)||fray<=0f||(origin-head.position).magnitude>fray)return;
            var dir=DirOf(cmd.Direction);
            if(!Fin(dir))return;
            var seg=origin-head.position;
            float segLen=seg.magnitude;
            if(segLen>0f)
            {
                var segHits=Physics.RaycastAll(head.position,seg/segLen,segLen,w.LM,QueryTriggerInteraction.UseGlobal);
                if(segHits!=null)
                {
                    for(int i=0;i<segHits.Length;i++)
                    {
                        var col=segHits[i].collider;
                        if(SkipHit(col,actor,it))continue;
                        if(segHits[i].distance>=segLen)continue;
                        if(Transparent(col))continue;
                        return;
                    }
                }
            }
            float coeff=1f;
            if(w.condition!=null)
            {
                if(!Fin(w.condition.state)||!Fin(w.maxConditionWeakness))return;
                coeff=Mathf.Lerp(w.maxConditionWeakness,1f,Mathf.InverseLerp(4f,0f,w.condition.state));
            }
            if(!Fin(coeff)||coeff<=0f)return;
            var amm=mainscript.ammoMass;var amv=mainscript.ammoVelocity;
            if(w.ammo==null||amm==null||amv==null)return;
            int ai=(int)w.ammo.tipus;
            if(ai<0||ai>=amm.Length||ai>=amv.Length)return;
            if(!Fin(amm[ai])||amm[ai]<=0f||!Fin(amv[ai])||amv[ai]<=0f)return;
            float vel=amv[ai]*coeff;
            float E=0.5f*amm[ai]*Mathf.Pow(vel,2f);
            if(!Fin(E)||E<=0f)return;
            var hits=Physics.RaycastAll(origin,dir,float.PositiveInfinity,w.LM,QueryTriggerInteraction.UseGlobal);
            if(hits==null)return;
            System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            for(int i=0;i<hits.Length;i++)
            {
                var col=hits[i].collider;
                if(SkipHit(col,actor,it))continue;
                breakchilds bc;tosaveitemscript tsi;
                var root=NpcRoot(col,out bc,out tsi);
                if(root!=null)
                {
                    if(!SameActor(actor))return;
                    if(bc!=null&&ReferenceEquals(bc.P,root.breakable))bc.TryBreakShoot(E,dir);
                    else root.breakable.TryBreakShoot(E,dir);
                    if(!SameActor(actor)||root==null||!root.gameObject.activeInHierarchy||col==null)return;
                    if(!CanonRoot(tsi))return;
                    var rb=col.GetComponentInParent<Rigidbody>();
                    if(rb!=null&&!rb.isKinematic&&w.S!=null&&Fin(hits[i].point))
                    {
                        var fwd=NormVec(w.S.forward);
                        if(Fin(fwd))
                        {
                            var imp=fwd*(float)System.Math.Sqrt(E)*5f;
                            if(Fin(imp))rb.AddForceAtPosition(imp,hits[i].point,ForceMode.Impulse);
                        }
                    }
                    return;
                }
                if(Transparent(col))continue;
                return;
            }
        }
        static void Melee(Peer st,Actor actor,RabbitCommand cmd,double now)
        {
            var it=CanonItem(cmd.Tool);if(it==null)return;
            var m=CanonMelee(it);if(m==null)return;
            if(!Owns(it,actor.Mp)||ForeignHeld(it,actor.Mp)||HostHolds(it))return;
            if(cmd.Token==0||cmd.Token<=st.nonce)return;
            st.nonce=cmd.Token;
            ToolSt ts;
            if(!st.tools.TryGetValue(cmd.Tool,out ts)||ts.melee==null||!ReferenceEquals(ts.melee,m))
            {
                if(ts!=null&&!ReferenceEquals(ts.melee,m)){ts.melee=m;ts.meleeFrame=-1;ts.hasSample=false;}
                else if(ts==null){ts=new ToolSt{item=it,melee=m};st.tools[cmd.Tool]=ts;}
            }
            if(ts.meleeFrame==Time.frameCount)return;
            ts.meleeFrame=Time.frameCount;
            if(!Fin(Time.deltaTime)||Time.deltaTime<=0f)return;
            if(!Fin(m.damage)||m.damage<=0f||!Fin(m.force)||m.T==null||!Fin(m.T.position))return;
            var trig=m.Trigger;if(trig==null)return;
            var half=trig.localScale;
            if(!Fin(half)||half.x<=0f||half.y<=0f||half.z<=0f)return;
            var rot=RotOf(cmd.Rotation);
            if(!Fin(rot.x)||!Fin(rot.y)||!Fin(rot.z)||!Fin(rot.w))return;
            if(!Fin(cmd.Position.X)||!Fin(cmd.Position.Y)||!Fin(cmd.Position.Z))return;
            var center=mainscript.UnityPosFromGlobal(new Vector3d(cmd.Position.X,cmd.Position.Y,cmd.Position.Z));
            if(!Fin(center))return;
            var head=actor.Mp.THead;if(head==null)return;
            float fray=mainscript.s.player.FrayRange;
            if(!Fin(fray)||fray<=0f)return;
            if((center-head.position).magnitude>fray+half.magnitude)return;
            Vector3 delta;
            var gpos=mainscript.GlobalFromUnityPos(m.T.position);
            if(ts.hasSample)
            {
                delta=new Vector3((float)(gpos.x-ts.sampleGlobal.x),(float)(gpos.y-ts.sampleGlobal.y),(float)(gpos.z-ts.sampleGlobal.z));
            }
            else
            {
                if(!Fin(m.lastT))return;
                delta=m.T.position-m.lastT;
            }
            ts.sampleGlobal=gpos;ts.hasSample=true;ts.item=it;
            if(!Fin(delta))return;
            var cols=Physics.OverlapBox(center,half,rot,m.mask,QueryTriggerInteraction.Collide);
            if(cols==null)return;
            var done=new HashSet<uint>();
            for(int i=0;i<cols.Length;i++)
            {
                var col=cols[i];
                if(col==null)continue;
                if(col.transform.IsChildOf(actor.Root.transform))continue;
                if(col.transform.IsChildOf(it.transform))continue;
                breakchilds bc;tosaveitemscript tsi;
                var root=NpcRoot(col,out bc,out tsi);
                if(root==null)continue;
                if(done.Contains(tsi.idInSave))continue;
                done.Add(tsi.idInSave);
                if(!SameActor(actor))return;
                float amount=root.breakable.decreaseAfterAttack?m.damage*Time.deltaTime:m.damage;
                if(!Fin(amount)||amount<=0f)continue;
                root.breakable.TryBreak(amount);
                if(!SameActor(actor)||root==null||!root.gameObject.activeInHierarchy||col==null||!CanonRoot(tsi))continue;
                var rb=col.GetComponentInParent<Rigidbody>();
                if(rb!=null&&!rb.isKinematic)
                {
                    var dn=NormVec(delta);
                    if(!Fin(dn)&&delta.sqrMagnitude>0f)continue;
                    if(delta.sqrMagnitude<=0f)dn=Vector3.zero;
                    var f=(dn*m.force+delta)*0.5f*Time.deltaTime;
                    if(Fin(f))rb.AddForce(f);
                }
            }
        }
        internal static void ClientShot(weaponscript w,int index,Vector3 origin,Vector3 direction,double now)
        {
            if(!ClientActive())return;
            if(!Fin(now)||now<0||w==null)return;
            if(now<lastNow){clientBurst=0;clientWeapon=null;clientFrame=-1;meleeFrame=-1;}
            lastNow=now;
            if(index<0||index>=System.Math.Max(1,w.shotCount))return;
            if(!Fin(origin))return;
            double dn=(double)direction.x*direction.x+(double)direction.y*direction.y+(double)direction.z*direction.z;
            if(dn<=0||!Fin(dn))return;
            var P=w.P;if(P==null||!P.started||!P.gameObject.activeInHierarchy)return;
            var tsi=P.tosave;
            if(tsi==null||tsi.idInSave==0)return;
            var canon=CanonItem(tsi.idInSave);
            if(canon==null||!ReferenceEquals(canon,tsi)||!ReferenceEquals(tsi.P,P)||!ReferenceEquals(CanonGun(tsi),w))return;
            var local=mainscript.s.player;
            if(local.pickedUp!=P&&local.inHandP!=P)
            {
                if(!WorldGun(tsi,w,local.Th))return;
            }
            try
            {
                if(index==0)
                {
                    if(clientNonce==ulong.MaxValue){Fail("nonce");return;}
                    clientNonce++;
                    var cmd=new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=tsi.idInSave,Token=clientNonce,Index=0,Position=Point(origin),Direction=Vec(direction)};
                    var bytes=RabbitProtocol.WriteCommand(cmd);
                    if(bytes==null){clientBurst=0;clientWeapon=null;return;}
                    if(!node.SendRabbitRequest(bytes)){clientBurst=0;clientWeapon=null;return;}
                    clientBurst=clientNonce;clientTool=tsi.idInSave;clientWeapon=w;clientFrame=Time.frameCount;
                    return;
                }
                if(clientBurst==0||clientTool!=tsi.idInSave||!ReferenceEquals(clientWeapon,w)||clientFrame!=Time.frameCount)return;
                var c2=new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=tsi.idInSave,Token=clientBurst,Index=index,Position=Point(origin),Direction=Vec(direction)};
                var b2=RabbitProtocol.WriteCommand(c2);
                if(b2==null)return;
                node.SendRabbitRequest(b2);
            }
            catch{Fail("client shot");}
        }
        internal static void ClientMelee(meleeweaponscript m,Vector3 center,Vector3 extents,Quaternion rotation,double now)
        {
            if(!ClientActive())return;
            if(!Fin(now)||now<0||m==null||meleeFrame==Time.frameCount)return;
            if(now<lastNow){clientBurst=0;clientWeapon=null;clientFrame=-1;meleeFrame=-1;}
            lastNow=now;
            var P=m.p;if(P==null||!P.started||!P.gameObject.activeInHierarchy)return;
            var tsi=P.tosave;
            if(tsi==null||tsi.idInSave==0)return;
            var canon=CanonItem(tsi.idInSave);
            if(canon==null||!ReferenceEquals(canon,tsi)||!ReferenceEquals(tsi.P,P)||!ReferenceEquals(CanonMelee(tsi),m))return;
            var local=mainscript.s.player;
            if(local.pickedUp!=P&&local.inHandP!=P)return;
            if(!Fin(center)||!Fin(extents)||extents.x<=0f||extents.y<=0f||extents.z<=0f)return;
            double qn=(double)rotation.x*rotation.x+(double)rotation.y*rotation.y+(double)rotation.z*rotation.z+(double)rotation.w*rotation.w;
            if(qn<=0||!Fin(qn))return;
            try
            {
                if(clientNonce==ulong.MaxValue){Fail("nonce");return;}
                clientNonce++;
                clientBurst=0;clientWeapon=null;
                var cmd=new RabbitCommand{Kind=RabbitCommandKind.Melee,Tool=tsi.idInSave,Token=clientNonce,Position=Point(center),Extents=Vec(extents),Rotation=Rot(rotation)};
                var bytes=RabbitProtocol.WriteCommand(cmd);
                if(bytes==null)return;
                if(node.SendRabbitRequest(bytes))meleeFrame=Time.frameCount;
            }
            catch{Fail("client melee");}
        }
        static RabbitPoint Point(Vector3 v){var g=mainscript.GlobalFromUnityPos(v);return new RabbitPoint{X=g.x,Y=g.y,Z=g.z};}
        static RabbitVector Vec(Vector3 v){return new RabbitVector{X=v.x,Y=v.y,Z=v.z};}
        static RabbitRotation Rot(Quaternion q){return new RabbitRotation{X=q.x,Y=q.y,Z=q.z,W=q.w};}
    }
}
