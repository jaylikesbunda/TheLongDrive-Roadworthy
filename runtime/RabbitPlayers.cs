using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace TldMp
{
    internal static class RabbitPlayers
    {
        internal sealed class Actor{internal ulong Id;internal ulong Session;internal GameObject Root;internal mpplayerscript Mp;}
        const float ThreatValue=10f;
        const float BlockPad=0.2f;
        static StableNode node;
        static fpscontroller player;
        static savedatascript data;
        static bool world;
        static bool enabled;
        static bool boundHost;
        static ulong boundSession;
        static bool healthy=true;
        static bool logged;
        static float lastFixed=float.NaN;
        static readonly Dictionary<uint,Dictionary<ulong,ulong>> contacts=new Dictionary<uint,Dictionary<ulong,ulong>>();
        static readonly List<Actor> snap=new List<Actor>();
        static readonly List<Actor> attackActors=new List<Actor>();
        static object sightScope=new object();
        internal static long OverlapContacts;
        internal static bool Healthy{get{return healthy;}}
        internal static System.Action RemoteRequestObserved;
        static bool Fin(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        static bool Fin(Vector3 v){return Fin(v.x)&&Fin(v.y)&&Fin(v.z);}
        internal static void Reset()
        {
            node=null;player=null;data=null;world=false;enabled=false;boundHost=false;boundSession=0;
            contacts.Clear();snap.Clear();attackActors.Clear();sightings.Clear();sightScope=new object();OverlapContacts=0;lastFixed=float.NaN;healthy=true;logged=false;
        }
        static void Fail(string why)
        {
            if(!logged){Debug.LogWarning("RabbitPlayers fail: "+why);logged=true;}
            healthy=false;contacts.Clear();sightings.Clear();sightScope=new object();
        }
        internal static void Bind(bool en,bool worldStarted,fpscontroller p,savedatascript d,StableNode n)
        {
            bool nHost=n!=null&&n.IsHost;
            ulong nSess=n!=null?n.Session:0;
            bool changed=!ReferenceEquals(n,node)||!ReferenceEquals(p,player)||!ReferenceEquals(d,data)||worldStarted!=world||en!=enabled||nHost!=boundHost||nSess!=boundSession;
            if(changed)
            {
                contacts.Clear();snap.Clear();attackActors.Clear();sightings.Clear();sightScope=new object();OverlapContacts=0;lastFixed=float.NaN;healthy=true;logged=false;
                node=n;player=p;data=d;world=worldStarted;enabled=en;boundHost=nHost;boundSession=nSess;
                return;
            }
        }
        static bool Active()
        {
            return enabled&&healthy&&world&&node!=null&&node.IsHost&&node.Compatible&&node.Error==null&&player!=null&&player.loadedmove&&data!=null&&data.items!=null;
        }
        // Single-player/local visibility context: no authoritative peer session
        // is required to see the local body as a combat target.
        static bool SightActive()
        {
            return healthy&&world&&player!=null&&player.loadedmove&&!player.died&&data!=null&&data.items!=null&&
                   (!enabled||(node!=null&&node.IsHost&&node.Compatible&&node.Error==null));
        }
        internal sealed class Sighting
        {
            internal nyulscript Rabbit;internal object Stimul,Scope;internal Vector3 Pos;internal float At;
            internal Actor Actor;internal fpscontroller Player;internal Rigidbody PlayerBody;
            internal Transform Source,Head;internal CapsuleCollider Body;internal bool BodyAim;
            internal RabbitCombatReach.Target Target;
        }
        static readonly Dictionary<nyulscript,Sighting> sightings=new Dictionary<nyulscript,Sighting>();
        internal static bool TryGet(ulong id,out Actor actor)
        {
            actor=null;
            if(!Active())return false;
            if(id==0||id==node.Own)return false;
            StablePeer peer;
            if(!node.Peers.TryGetValue(id,out peer)||peer==null||!peer.Ready||peer.Session==0)return false;
            var sc=syncScript.s;
            if(sc==null||sc.players==null)return false;
            syncScript.playerSyncClass pc;
            if(!sc.players.TryGetValue(new CSteamID(id),out pc)||pc==null)return false;
            var mp=pc.mpscript;
            var root=pc.player;
            if(mp==null||root==null||!root.activeInHierarchy)return false;
            if(mp.steamID!=id)return false;
            if(!mp.transform.IsChildOf(root.transform))return false;
            var head=mp.THead;
            if(head==null||!head.IsChildOf(root.transform))return false;
            float now=Time.realtimeSinceStartup;
            if(!Fin(now)||!Fin(sc.playerRemoveTime)||sc.playerRemoveTime<0f)return false;
            float age=now-pc.lastTime;
            if(!Fin(age)||age<0f||age>sc.playerRemoveTime)return false;
            if(!Fin(head.position)||!Fin(root.transform.position))return false;
            actor=new Actor{Id=id,Session=peer.Session,Root=root,Mp=mp};
            return true;
        }
        static bool SameActor(Actor a)
        {
            Actor cur;
            if(!TryGet(a.Id,out cur))return false;
            return cur.Session==a.Session&&ReferenceEquals(cur.Mp,a.Mp)&&ReferenceEquals(cur.Root,a.Root);
        }
        static nyulscript CanonicalRabbit(Component c)
        {
            if(c==null||data==null||data.items==null)return null;
            var nyul=c.GetComponentInParent<nyulscript>();
            if(nyul==null||!nyul.started||!nyul.gameObject.activeInHierarchy)return null;
            if(nyul.senses==null||nyul.AI==null||nyul.breakable==null||nyul.ragdoll==null)return null;
            var tsi=nyul.GetComponent<tosaveitemscript>();
            if(tsi==null||tsi.idInSave==0)return null;
            tosaveitemscript canon;
            if(!data.items.TryGetValue(tsi.idInSave,out canon)||!ReferenceEquals(canon,tsi))return null;
            var cn=tsi.GetComponent<nyulscript>();
            if(cn==null)cn=tsi.nyul;
            if(!ReferenceEquals(cn,nyul))return null;
            return nyul;
        }
        // Native Scan copies stimuli without source IDs. Attribute a local visual
        // record only when its original collider and matching entry are unique.
        // An ambiguous source keeps native behavior and suppresses guest additions.
        static int LocalVisual(sensoryscript senses,out bool ambiguous)
        {
            Collider source;return LocalVisual(senses,out ambiguous,out source);
        }
        static int LocalVisual(sensoryscript senses,out bool ambiguous,out Collider sourceCollider)
        {
            sourceCollider=null;ambiguous=false;int found=-1;var cols=senses.colliders;
            if(cols==null)return -1;
            foreach(var col in cols)
            {
                if(col==null || !col.transform.IsChildOf(player.transform))continue;
                var script=col.GetComponent<stimulscript>();
                if(script==null || !script.visible || script.sound || script.stimuls==null)continue;
                foreach(var source in script.stimuls)
                {
                    if(source==null || source.tipus!=stimulType.threat || source.value!=ThreatValue)continue;
                    int match=-1, count=0;var pos=col.transform.position;
                    for(int i=0;i<senses.stimuls.Count;i++)
                    {
                        var candidate=senses.stimuls[i];
                        if(candidate!=null && candidate.tipus==source.tipus && candidate.value==source.value && candidate.pos==pos){match=i;count++;}
                    }
                    if(count==0)continue;
                    if(count!=1 || found>=0){ambiguous=true;return -1;}
                    var delta=pos-senses.eyes.position;RaycastHit hit;
                    if(!(delta.sqrMagnitude<senses.fovZ) ||
                       !(Vector3.Angle(senses.eyes.forward,Vector3.ProjectOnPlane(delta,senses.eyes.up))<senses.fovY) ||
                       !(Vector3.Angle(senses.eyes.forward,Vector3.ProjectOnPlane(delta,senses.eyes.right))<senses.fovX) ||
                       !Physics.Raycast(senses.eyes.position,delta,out hit,senses.fovZ,senses.visionMask,QueryTriggerInteraction.Ignore) || hit.collider!=col)
                    {ambiguous=true;return -1;}
                    // Other stimuli at the same position and value cannot be distinguished
                    // from this copied record; never remove an uncertain sound/threat.
                    foreach(var other in cols)
                    {
                        if(other==null || ReferenceEquals(other,col) || other.transform.position!=pos)continue;
                        var os=other.GetComponent<stimulscript>();if(os==null || os.stimuls==null)continue;
                        foreach(var entry in os.stimuls)
                            if(entry!=null && entry.tipus==source.tipus){ambiguous=true;return -1;}
                    }
                    found=match;sourceCollider=col;
                }
            }
            return found;
        }
        static bool Visible(sensoryscript senses,nyulscript rabbit,Transform humanRoot,Vector3 point)
        {
            if(!Fin(point))return false;
            var fromRoot=point-senses.transform.position;float radius=senses.radius+BlockPad;
            if(fromRoot.sqrMagnitude>radius*radius)return false;
            var eyes=senses.eyes;var delta=point-eyes.position;float squared=delta.sqrMagnitude;
            if(!(squared>0f)||!(squared<senses.fovZ))return false;
            if(!(Vector3.Angle(eyes.forward,Vector3.ProjectOnPlane(delta,eyes.up))<senses.fovY))return false;
            if(!(Vector3.Angle(eyes.forward,Vector3.ProjectOnPlane(delta,eyes.right))<senses.fovX))return false;
            float distance=(float)System.Math.Sqrt(squared);
            var hits=Physics.RaycastAll(eyes.position,delta/distance,distance,senses.visionMask,QueryTriggerInteraction.Ignore);
            if(hits!=null)foreach(var hit in hits)
            {
                var col=hit.collider;if(col==null || hit.distance>=distance)continue;
                if(col.transform.IsChildOf(humanRoot)||col.transform.IsChildOf(rabbit.transform))continue;
                return false;
            }
            return true;
        }
        internal static void Scan(sensoryscript senses)
        {
            var canon=senses==null?null:senses.GetComponent<nyulscript>();
            if(canon!=null)sightings.Remove(canon); // every scan invalidates prior provenance
            if(!SightActive())return;
            if(senses==null||!senses.hasEyes||senses.eyes==null)return;
            if(!Fin(senses.radius)||senses.radius<=0f||!Fin(senses.fovZ)||senses.fovZ<=0f||!Fin(senses.fovX)||!Fin(senses.fovY))return;
            var nyul=CanonicalRabbit(senses);
            if(nyul==null||!ReferenceEquals(nyul.senses,senses)||!senses.alive)return;
            try
            {
                bool ambiguous;Collider localSource;
                int local=LocalVisual(senses,out ambiguous,out localSource);if(ambiguous)return;
                var localRecord=local>=0?senses.stimuls[local]:null;
                float nearest=local<0?float.PositiveInfinity:(senses.stimuls[local].pos-senses.eyes.position).sqrMagnitude;
                Actor winner=null;Vector3 target=Vector3.zero;Transform winnerHead=null;CapsuleCollider winnerBody=null;
                var localPlayer=player;var localBody=localPlayer.CBody;bool localBodyWinner=false;
                if(local<0 && localBody!=null && localBody.enabled && localBody.gameObject.activeInHierarchy &&
                   localPlayer.gameObject.activeInHierarchy && !localPlayer.died && localBody.transform.IsChildOf(localPlayer.transform))
                {
                    var point=localBody.bounds.center;
                    if(Visible(senses,nyul,localPlayer.transform,point) && ReferenceEquals(player,localPlayer) &&
                       ReferenceEquals(localPlayer.CBody,localBody) && localBody.enabled && localBody.gameObject.activeInHierarchy)
                    {nearest=(point-senses.eyes.position).sqrMagnitude;target=point;localBodyWinner=true;}
                }
                snap.Clear();if(Active()&&node!=null)foreach(var kv in node.Peers){Actor a;if(TryGet(kv.Key,out a))snap.Add(a);}
                foreach(var actor in snap)
                {
                    var head=actor.Mp.THead;var body=actor.Mp.CBody;bool usedBody=false;
                    Vector3 point=head.position;
                    if(!Visible(senses,nyul,actor.Root.transform,point))
                    {
                        // Close to the rabbit, the head can leave the narrow vertical cone.
                        // Use the actual proxy body, never a guessed point through a wall.
                        if(body==null || !body.enabled || !body.gameObject.activeInHierarchy || !body.transform.IsChildOf(actor.Root.transform))continue;
                        point=body.bounds.center;usedBody=true;
                        if(!Visible(senses,nyul,actor.Root.transform,point))continue;
                    }
                    if(!SameActor(actor) || !ReferenceEquals(actor.Mp.THead,head) ||
                       (usedBody?(!ReferenceEquals(actor.Mp.CBody,body)||!body.enabled||!body.gameObject.activeInHierarchy||body.bounds.center!=point):head.position!=point))continue;
                    float distance=(point-senses.eyes.position).sqrMagnitude;
                    if(distance<nearest){nearest=distance;winner=actor;winnerHead=head;winnerBody=usedBody?body:null;target=point;localBodyWinner=false;}
                }
                if((winner==null&&!localBodyWinner&&local<0) || !SightActive() || (winner!=null&&!SameActor(winner)) || !ReferenceEquals(CanonicalRabbit(senses),nyul))return;
                bool changed;int stillLocal=LocalVisual(senses,out changed);
                if(changed || stillLocal!=local || (local>=0&&!ReferenceEquals(localRecord,senses.stimuls[local])) ||
                    !SightActive() || (winner!=null&&!SameActor(winner)) || !ReferenceEquals(CanonicalRabbit(senses),nyul))return;
                if(winner!=null && (!ReferenceEquals(winner.Mp.THead,winnerHead) ||
                   (winnerBody!=null?(!ReferenceEquals(winner.Mp.CBody,winnerBody)||!winnerBody.enabled||!winnerBody.gameObject.activeInHierarchy||winnerBody.bounds.center!=target):winnerHead.position!=target)))return;
                if(localBodyWinner && (!ReferenceEquals(player,localPlayer) || !ReferenceEquals(localPlayer.CBody,localBody) ||
                   !localBody.enabled || !localBody.gameObject.activeInHierarchy || !localPlayer.gameObject.activeInHierarchy || localPlayer.died ||
                   !localBody.transform.IsChildOf(localPlayer.transform) || localBody.bounds.center!=target))return;
                // Provenance is recorded for EVERY verified winner — including the
                // native local visual, which adds no synthetic stimulus.
                bool nativeLocal=winner==null&&!localBodyWinner;
                stimul threat;
                if(nativeLocal){target=localRecord.pos;threat=localRecord;}
                else
                {
                    if(local>=0)senses.stimuls.RemoveAt(local);
                    threat=new stimul(target,stimulType.threat,ThreatValue);senses.stimuls.Add(threat);
                }
                var published=new Sighting{Rabbit=nyul,Stimul=threat,Pos=target,At=Time.realtimeSinceStartup,
                    Actor=winner,Player=player,PlayerBody=player.RB,Scope=sightScope,
                    Source=winner!=null?(winnerBody!=null?winnerBody.transform:winnerHead):
                        (nativeLocal?localSource.transform:localBody.transform),
                    Head=winner!=null?winnerHead:null,Body=winner!=null?winner.Mp.CBody:localBody,
                    BodyAim=winner!=null?winnerBody!=null:localBodyWinner};
                sightings[nyul]=published;
            }
            catch{Fail("scan");}
        }
        // Native attack layer 27 excludes standing MP avatars (layer 0).
        // Query only those excluded pairs against the exact active native
        // attack shape; preserve native timing, reach and damage deduplication.
        internal static void ProbeDamage(nyulscript rabbit)
        {
            // The active native attack window is authoritative; fear going neutral or
            // negative mid-strike does not cancel it (same rule as host native damage).
            if(!Active()||rabbit==null||rabbit.AI==null||
               !rabbit.attacking||!Fin(rabbit.size)||rabbit.size<=.5f)return;
            var trigger=rabbit.attackTriggerScript;
            if(trigger==null||!ReferenceEquals(CanonicalRabbit(trigger),rabbit)||!rabbit.senses.alive||
               !trigger.isActiveAndEnabled||!trigger.gameObject.activeInHierarchy)return;
            var shape=trigger.selfCol;
            if(shape==null||!shape.enabled||!shape.isTrigger||!shape.gameObject.activeInHierarchy)return;
            try
            {
                attackActors.Clear();
                foreach(var peer in node.Peers)
                {
                    Actor actor;
                    if(TryGet(peer.Key,out actor))attackActors.Add(actor);
                    if(attackActors.Count>=32)break;
                }
                for(int i=0;i<attackActors.Count;i++)
                {
                    var actor=attackActors[i];if(!SameActor(actor))continue;
                    var cols=actor.Mp.cols;var body=actor.Mp.CBody;
                    bool touched=false,bodyChecked=false;
                    if(cols!=null)for(int j=0;j<cols.Length&&j<32;j++)
                    {
                        if(cols[j]==body)bodyChecked=true;
                        if(ProbeContact(trigger,shape,cols[j],actor)){touched=true;break;}
                    }
                    if(!touched&&!bodyChecked)ProbeContact(trigger,shape,body,actor);
                }
            }
            catch{Fail("attack probe");}
        }
        static bool ProbeContact(playerdeathtriggerscript trigger,Collider shape,Collider contact,Actor actor)
        {
            if(contact==null||!contact.enabled||!contact.gameObject.activeInHierarchy||
               !contact.transform.IsChildOf(actor.Root.transform)||
               !Physics.GetIgnoreLayerCollision(shape.gameObject.layer,contact.gameObject.layer)||
               Physics.GetIgnoreCollision(shape,contact))return false;
            Vector3 direction;float depth;
            if(!Physics.ComputePenetration(shape,shape.transform.position,shape.transform.rotation,
                contact,contact.transform.position,contact.transform.rotation,out direction,out depth)||
               !Fin(depth)||depth<=0f||!SameActor(actor))return false;
            OverlapContacts++;
            Damage(trigger,contact);
            return true;
        }
        // Resolved combat target for the native pursuit/attack path. Only the
        // rabbit's own current visual stimulus of a canonical local player or
        // canonical ready remote actor resolves; sounds or stale scans do not.
        internal static RabbitCombatReach.Target CombatTarget(aiscript ai)
        {
            if(ai==null||ai.nyul==null||!SightActive())return null;
            var nyul=CanonicalRabbit(ai);if(nyul==null||!ReferenceEquals(nyul.AI,ai))return null;
            var senses=nyul.senses;if(senses==null||!senses.alive)return null;
            if(!Fin(ai.flightFight)||ai.flightFight<=0f||!ai.goToTarget)return null;
            Sighting sg;
            if(!sightings.TryGetValue(nyul,out sg)||sg==null||!ReferenceEquals(sg.Rabbit,nyul))return null;
            if(!ReferenceEquals(sg.Scope,sightScope))return null;
            float at=sg.At,now=Time.realtimeSinceStartup;
            if(!Fin(now)||!Fin(at)||now-at>.5f||now<at)return null;
            bool present=false;Vector3 seen=Vector3.zero;
            if(senses.stimuls!=null)foreach(var st in senses.stimuls)if(ReferenceEquals(st,sg.Stimul)&&st.pos==sg.Pos){present=true;seen=st.pos;break;}
            if(!present||seen!=sg.Pos||!Fin(ai.interestPos))return null;
            if((ai.interestPos-seen).sqrMagnitude>1e-8f)return null;
            if(sg.Target!=null)return sg.Target.Valid()?sg.Target:null;
            var list=new List<Collider>();
            if(sg.Actor!=null)
            {
                if(!SameActor(sg.Actor))return null;
                AddMember(sg,list,sg.Actor.Mp.CBody);
                var cols=sg.Actor.Mp.cols;if(cols!=null)foreach(var c in cols)AddMember(sg,list,c);
            }
            else
            {
                AddMember(sg,list,sg.Player.CBody);
                var cols=sg.Player.C;if(cols!=null)foreach(var c in cols)AddMember(sg,list,c);
            }
            if(list.Count==0)return null;
            var result=new RabbitCombatReach.Target{Colliders=list.ToArray(),
                Identity=sg.Actor!=null?(object)sg.Actor.Root:sg.Player,
                Aim=()=>sg.BodyAim?sg.Body.bounds.center:sg.Source.position};
            result.Valid=()=>ValidTarget(sg,result);
            sg.Target=result;
            return result.Valid()?result:null;
        }
        static bool PhysicalMember(Sighting s,Collider c)
        {
            if(c==null||!c.enabled||c.isTrigger||!c.gameObject.activeInHierarchy)return false;
            if(s.Actor!=null)
            {
                if(!c.transform.IsChildOf(s.Actor.Root.transform))return false;
                if(ReferenceEquals(c,s.Actor.Mp.CBody))return true;
                var cols=s.Actor.Mp.cols;
                if(cols!=null)foreach(var member in cols)if(ReferenceEquals(c,member))return true;
                return false;
            }
            if(s.Player==null||s.PlayerBody==null||!c.transform.IsChildOf(s.Player.transform)||
               c.attachedRigidbody!=s.PlayerBody)return false;
            if(ReferenceEquals(c,s.Player.CBody))return true;
            var local=s.Player.C;
            if(local!=null)foreach(var member in local)if(ReferenceEquals(c,member))return true;
            return false;
        }
        static void AddMember(Sighting s,List<Collider> list,Collider c)
        {
            if(PhysicalMember(s,c)&&!list.Contains(c))list.Add(c);
        }
        static bool ValidTarget(Sighting s,RabbitCombatReach.Target t)
        {
            if(!ReferenceEquals(s.Scope,sightScope)||!SightActive()||s.Rabbit==null||
               !ReferenceEquals(CanonicalRabbit(s.Rabbit),s.Rabbit)||s.Rabbit.AI==null||
               !s.Rabbit.AI.alive||!s.Rabbit.AI.isActiveAndEnabled||!s.Rabbit.senses.alive||
               s.Rabbit.ragdoll.ragdoll||s.Source==null||!s.Source.gameObject.activeInHierarchy)return false;
            if(s.Actor!=null)
            {
                if(!SameActor(s.Actor)||!ReferenceEquals(s.Actor.Mp.THead,s.Head)||
                   !ReferenceEquals(s.Actor.Mp.CBody,s.Body)||!s.Source.IsChildOf(s.Actor.Root.transform))return false;
            }
            else if(!ReferenceEquals(player,s.Player)||!player.gameObject.activeInHierarchy||
                !ReferenceEquals(player.RB,s.PlayerBody)||!ReferenceEquals(player.CBody,s.Body)||
                !s.Source.IsChildOf(player.transform))return false;
            if(s.BodyAim&&(s.Body==null||!PhysicalMember(s,s.Body)))return false;
            foreach(var c in t.Colliders)if(!PhysicalMember(s,c))return false;
            return Fin(t.Aim());
        }
        internal static void Damage(playerdeathtriggerscript trigger,Collider contact)
        {
            if(!Active())return;
            if(trigger==null||contact==null)return;
            var nyul=CanonicalRabbit(trigger);
            if(nyul==null||!nyul.senses.alive)return;
            if(!trigger.isActiveAndEnabled||!trigger.gameObject.activeInHierarchy)return;
            var selfCol=trigger.selfCol;
            if(selfCol==null||!selfCol.enabled||!selfCol.isTrigger)return;
            if(!contact.enabled||!contact.gameObject.activeInHierarchy)return;
            if(contact.GetComponentInParent<survivalscript>()!=null)return;
            var mp=contact.GetComponentInParent<mpplayerscript>();
            if(mp==null||mp.steamID==0)return;
            Actor actor;
            if(!TryGet(mp.steamID,out actor)||!ReferenceEquals(actor.Mp,mp))return;
            if(!contact.transform.IsChildOf(actor.Root.transform))return;
            float ft=Time.fixedTime;
            float dt=Time.deltaTime;
            float dmg=trigger.damagePlayer;
            if(!Fin(ft)||!Fin(dt)||dt<=0f||!Fin(dmg)||dmg<=0f)return;
            if(ft!=lastFixed){contacts.Clear();lastFixed=ft;}
            var tsi=nyul.GetComponent<tosaveitemscript>();
            Dictionary<ulong,ulong> hit;
            if(!contacts.TryGetValue(tsi.idInSave,out hit)){hit=new Dictionary<ulong,ulong>();contacts[tsi.idInSave]=hit;}
            ulong seen;
            if(hit.TryGetValue(actor.Id,out seen)&&seen==actor.Session)return;
            var direction=actor.Root.transform.position-trigger.transform.position;
            if(!Fin(direction))return;
            try
            {
                if(!SameActor(actor))return;
                var cur=CanonicalRabbit(trigger);
                if(cur==null||!cur.senses.alive)return;
                float amount=dmg*dt;
                if(!Fin(amount)||amount<=0f)return;
                hit[actor.Id]=actor.Session;
                syncScript.s.SendDmgPlayer(actor.Mp,amount,direction);
                var observed=RemoteRequestObserved;if(observed!=null)try{observed();}catch{}
            }
            catch{Fail("damage");}
        }
    }
}
