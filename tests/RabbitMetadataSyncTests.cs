using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector3{public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero{get{return new Vector3();}}}
    public struct Quaternion{public static Quaternion identity{get{return new Quaternion();}}}
    public struct Vector3d{public double x,y,z;public Vector3d(double a,double b,double c){x=a;y=b;z=c;}}
    public class Object{public bool destroyed;public static void Destroy(Object o){if(o!=null)o.destroyed=true;}public static T Instantiate<T>(T s) where T:Object,new(){return new T();}}
    public class Component:Object{public GameObject gameObject;public T GetComponent<T>() where T:Component{return gameObject==null?null:gameObject.Get<T>();}}
    public class Transform:Component{public Vector3 position,eulerAngles;public Quaternion rotation;}
    public class GameObject:Object{public int layer;public string tag="";public bool activeInHierarchy=true;public Transform transform=new Transform();public readonly Dictionary<Type,Component> comps=new Dictionary<Type,Component>();public T Get<T>() where T:Component{Component c;return comps.TryGetValue(typeof(T),out c)?(T)c:null;}public T GetComponent<T>() where T:Component{return Get<T>();}public T Add<T>(T c) where T:Component{comps[typeof(T)]=c;c.gameObject=this;return c;}public void SetActive(bool on){activeInHierarchy=on;}}
    public class MonoBehaviour:Component{}
    public static class Time{public static float realtimeSinceStartup=100;public static float time=50;public static int frameCount;}
    public class Debug{public static void LogWarning(object o){}}
    public class weaponscript:MonoBehaviour{}
    public class meleeweaponscript:MonoBehaviour{}
    public class nyulscript:MonoBehaviour
    {
        public ragdollactivatescript ragdoll;
        public aiscript AI;
        public sensoryscript senses;
        public breakablescript breakable;
        public float size;
        public bool started=true;
        public static bool throwOnSize;
        public void SetBasedOnSize(float s){if(throwOnSize)throw new Exception("size");size=s;started=true;if(breakable!=null){breakable.health=1;breakable.shootHealth=1;breakable.explosionHealth=1;}}
    }
    public class ragdollactivatescript:MonoBehaviour{public bool ragdoll;public int rags;public void SetRagdol(bool on){rags++;ragdoll=on;}}
    public class aiscript:MonoBehaviour{public nyulscript nyul;public sensoryscript senses;public int dies;public void StartDie(){dies++;if(senses!=null)senses.alive=false;}}
    public class sensoryscript:MonoBehaviour{public bool alive=true,hasEars,hasEyes;public GameObject[] eyeObjects;public int looseEars,looseEyes;public void LooseEars(){looseEars++;hasEars=false;}public void LooseEyes(){looseEyes++;hasEyes=false;if(eyeObjects!=null)foreach(var e in eyeObjects)if(e!=null)e.SetActive(false);}}
    public class breakablescript:MonoBehaviour{public float health,shootHealth,explosionHealth;}
    public class ragdollchildpos:MonoBehaviour{public Transform[] childs;public pickupable[] ps;public Vector3d[] poss;}
    public class pickupable:MonoBehaviour{}
    public class tosaveitemscript:MonoBehaviour{public int id;public uint idInSave;public Transform useThisForPos;public nyulscript nyul;public ragdollchildpos ragdollPos;public void FStart(){}}
    public class savedatascript{public static savedatascript s=new savedatascript();public Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();}
    public class syncScript:MonoBehaviour{public static syncScript s=new syncScript();public float syncItemInterwall=0.5f,playerRemoveTime=30f;public int posSends,ragSends;public void SendItemPosGlobal(uint id,int type,Vector3d gpos,Vector3 rot){posSends++;}public void SendRagdoll(uint id,Transform parent,ragdollchildpos rg){ragSends++;}}
    public class mainscript:MonoBehaviour{public static mainscript s=new mainscript();public fpscontroller player=new GameObject().Add(new fpscontroller());public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d(p.x,p.y,p.z);}}
    public class fpscontroller:MonoBehaviour{public bool loadedmove=true;}
    public class menuhandler{public static menuhandler s=new menuhandler();public mainMap currentMainMap=new mainMap{firstRun=true};public class mainMap{public bool firstRun;}}
    public class PatchOptions{public static bool Rabbits=true;}
}
namespace TldMp
{
    public partial class StableGameBridge{public static bool Connected;public static StableNode node;}
    public class StablePeer{public ulong Session;public bool Ready;}
    public class StableNode
    {
        public bool IsHost,Compatible,Ready=true;
        public string Error;
        public ulong Own,Session,HostSession;
        public bool failPublish;
        public Dictionary<ulong,StablePeer> Peers=new Dictionary<ulong,StablePeer>();
        public int publishes;
        public List<byte[]> published=new List<byte[]>();
        public Action<byte[]> RabbitStateReceived;
        public Action<byte[]> onPublish;
        public bool PublishRabbitState(byte[] b)
        {
            if(failPublish)return false;
            publishes++;published.Add(b);
            if(onPublish!=null)onPublish(b);
            return true;
        }
        public void RaiseState(byte[] b){var h=RabbitStateReceived;if(h!=null)h(b);}
    }
    public static class RabbitReplicas{public static int begins,ends;public static void BeginMutation(nyulscript r){begins++;}public static void EndMutation(nyulscript r){ends++;}}
    internal static class RabbitPopulation{public const bool OnlineReady=false;}
}
public static class RabbitMetadataSyncTests
{
    static int checks;
    static void Check(bool ok,string w){checks++;if(!ok)throw new Exception("FAIL "+w);}
    static TldMp.RabbitMetadata Meta(uint item,int prefab,byte flags,float size,float h,float sh,float eh,ulong holder,ulong sess,ulong lease,ulong req,int part)
    {
        return new TldMp.RabbitMetadata{Item=item,Prefab=prefab,Flags=flags,Size=size,Health=h,ShootHealth=sh,ExplosionHealth=eh,Holder=holder,HolderSession=sess,Lease=lease,Request=req,Part=part};
    }
    static nyulscript Nyul(GameObject g)
    {
        var n=g.Add(new nyulscript());
        n.ragdoll=g.Add(new ragdollactivatescript());
        n.AI=g.Add(new aiscript());
        n.senses=g.Add(new sensoryscript{hasEars=true,hasEyes=true,eyeObjects=new GameObject[]{new GameObject(),new GameObject()}});
        n.AI.senses=n.senses;n.AI.nyul=n;
        n.breakable=g.Add(new breakablescript{health=10,shootHealth=5,explosionHealth=2});
        return n;
    }
    static tosaveitemscript Item(savedatascript d,GameObject g,nyulscript n,int id,uint idIn)
    {
        var t=g.Add(new tosaveitemscript{id=id,idInSave=idIn,nyul=n,useThisForPos=g.transform});
        d.items[idIn]=t;
        return t;
    }
    static TldMp.StableNode Node(bool host,ulong own){return new TldMp.StableNode{IsHost=host,Own=own,Session=100,Compatible=true,Ready=true};}
    static savedatascript Data(){var d=new savedatascript();return d;}
    static void SetupGuest(TldMp.StableNode g)
    {
        TldMp.RabbitMetadataSync.Reset();
        TldMp.RabbitMetadataSync.Tick(true,false,null,null,g,0);
    }
    static void LifecycleRegressions()
    {
        TldMp.RabbitMetadataSync.Reset();
        syncScript.s=new syncScript();
        var guest=Node(false,100);
        var firstPlayer=new GameObject().Add(new fpscontroller());
        var firstData=Data();
        var firstObject=new GameObject();
        var firstRoot=Nyul(firstObject);
        Item(firstData,firstObject,firstRoot,61,4000);
        TldMp.RabbitMetadataSync.Tick(true,true,firstPlayer,firstData,guest,0);
        var state=Meta(4000,61,14,0.4f,9,8,7,0,0,0,0,-1);
        guest.RaiseState(TldMp.RabbitProtocol.WriteMetadata(state));
        TldMp.RabbitMetadataSync.Tick(true,true,firstPlayer,firstData,guest,0.1);
        state.Health=8;state.ShootHealth=7;state.ExplosionHealth=6;
        guest.RaiseState(TldMp.RabbitProtocol.WriteMetadata(state));
        TldMp.RabbitMetadataSync.Tick(true,true,firstPlayer,firstData,guest,0.2);
        Check(firstRoot.breakable.health==8&&firstRoot.breakable.shootHealth==7&&firstRoot.breakable.explosionHealth==6,"unchanged size still restores every health channel");
        var effects=TldMp.RabbitReplicas.begins;
        guest.RaiseState(TldMp.RabbitProtocol.WriteMetadata(state));
        TldMp.RabbitMetadataSync.Tick(true,true,firstPlayer,firstData,guest,0.3);
        Check(TldMp.RabbitReplicas.begins==effects,"identical frame skips all native effects");
        var replacementObject=new GameObject();
        var replacement=Nyul(replacementObject);
        Item(firstData,replacementObject,replacement,61,4000);
        TldMp.RabbitMetadataSync.Tick(true,true,firstPlayer,firstData,guest,0.4);
        Check(replacement.size==0.4f&&replacement.breakable.health==8&&replacement.breakable.shootHealth==7&&replacement.breakable.explosionHealth==6,"replacement native root restores retained metadata without another frame");
        var secondData=Data();
        var secondObject=new GameObject();
        var secondRoot=Nyul(secondObject);
        Item(secondData,secondObject,secondRoot,62,4000);
        TldMp.RabbitMetadataSync.Tick(true,true,firstPlayer,secondData,guest,0.5);
        Check(secondRoot.size==0,"world replacement discards old metadata");
        var secondState=Meta(4000,62,14,0.6f,5,4,3,0,0,0,0,-1);
        guest.RaiseState(TldMp.RabbitProtocol.WriteMetadata(secondState));
        TldMp.RabbitMetadataSync.Tick(true,true,firstPlayer,secondData,guest,0.6);
        Check(secondRoot.size==0.6f&&secondRoot.breakable.health==5,"world replacement switches the canonical data reference");
        secondState.Health=4;
        guest.RaiseState(TldMp.RabbitProtocol.WriteMetadata(secondState));
        TldMp.RabbitMetadataSync.Tick(true,true,firstPlayer,secondData,guest,0.7);
        Check(secondRoot.breakable.health==4&&replacement.breakable.health==8,"subsequent new-world frames do not keep clearing or mutate old world");
        var secondPlayer=new GameObject().Add(new fpscontroller());
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,secondData,guest,0.8);
        firstPlayer.loadedmove=false;
        secondState.Health=3;
        guest.RaiseState(TldMp.RabbitProtocol.WriteMetadata(secondState));
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,secondData,guest,0.9);
        Check(secondRoot.breakable.health==3,"player replacement switches the canonical loaded-player reference");
        secondState.Health=2;
        guest.RaiseState(TldMp.RabbitProtocol.WriteMetadata(secondState));
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,secondData,guest,1.0);
        Check(secondRoot.breakable.health==2,"player replacement does not discard every subsequent frame");

        TldMp.RabbitMetadataSync.Reset();
        syncScript.s=new syncScript();
        var host=Node(true,101);
        host.Peers[102]=new TldMp.StablePeer{Session=1002,Ready=true};
        var hostData=Data();
        var hostObject=new GameObject();
        var hostRoot=Nyul(hostObject);
        Item(hostData,hostObject,hostRoot,73,6000);
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,hostData,host,100);
        var poses=syncScript.s.posSends;
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,hostData,host,100.1);
        Check(syncScript.s.posSends==poses,"native interval suppresses premature pose send");
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,hostData,host,99);
        Check(syncScript.s.posSends==poses+1,"backwards clock immediately reschedules canonical pose");
        hostData.items.Remove(6000);
        host.published.Clear();
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,hostData,host,100);
        TldMp.RabbitMetadata removed;
        Check(host.published.Count==1&&TldMp.RabbitProtocol.TryReadMetadata(host.published[0],out removed)&&removed.Item==6000&&removed.Prefab==73&&removed.Flags==1,"tombstone preserves the disappeared rabbit prefab");

        TldMp.RabbitMetadataSync.Reset();
        syncScript.s=new syncScript();
        hostData=Data();
        var callbackObject=new GameObject();
        Item(hostData,callbackObject,Nyul(callbackObject),74,6001);
        host.onPublish=delegate(byte[] bytes){TldMp.RabbitMetadata m;if(TldMp.RabbitProtocol.TryReadMetadata(bytes,out m)&&(m.Flags&1)==0)hostData.items.Remove(m.Item);};
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,hostData,host,0);
        Check(syncScript.s.posSends==0&&syncScript.s.ragSends==0,"publication callback removing canonical actor cannot send resurrecting poses");
        host.onPublish=null;

        TldMp.RabbitMetadataSync.Reset();
        var capacityNode=Node(false,103);
        var capacityData=Data();
        var capacityObject=new GameObject();
        Item(capacityData,capacityObject,Nyul(capacityObject),75,7000);
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,capacityData,capacityNode,0);
        for(uint i=0;i<256;i++)capacityNode.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(7000+i,75,14,0.5f,1,1,1,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,capacityData,capacityNode,0.1);
        Check(TldMp.RabbitMetadataSync.Healthy,"applied and unknown metadata share the valid cache bound");
        capacityNode.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(8000,75,14,0.5f,1,1,1,0,0,0,0,-1)));
        Check(!TldMp.RabbitMetadataSync.Healthy,"applying an entry cannot create extra capacity beyond the combined cache bound");

        TldMp.RabbitMetadataSync.Reset();
        hostData=Data();
        var invalidObject=new GameObject();
        var invalidRoot=Nyul(invalidObject);invalidRoot.size=-1;
        Item(hostData,invalidObject,invalidRoot,76,9000);
        TldMp.RabbitMetadataSync.Tick(true,true,secondPlayer,hostData,host,0);
        Check(!TldMp.RabbitMetadataSync.Healthy,"invalid finite native metadata fails closed at the tick boundary");
        TldMp.RabbitMetadataSync.Reset();
    }
    static void CarryMetadataApis()
    {
        var host=Node(true,1);host.Peers[2]=new TldMp.StablePeer{Ready=true,Session=20};
        var data=Data();var obj=new GameObject();var rabbit=Nyul(obj);Item(data,obj,rabbit,42,7);
        var player=new GameObject().Add(new fpscontroller());
        TldMp.RabbitMetadataSync.Reset();
        TldMp.RabbitMetadataSync.HostState=delegate(TldMp.RabbitMetadata m){m.Holder=2;m.HolderSession=20;m.Lease=3;m.Request=4;m.Part=0;m.Item=999;m.Health=999;return m;};
        TldMp.RabbitMetadataSync.Tick(true,true,player,data,host,0);
        TldMp.RabbitMetadata state;Check(TldMp.RabbitProtocol.TryReadMetadata(host.published[0],out state),"carry state parses");
        Check(state.Item==7&&state.Health==10&&state.Holder==2&&state.Lease==3&&state.Request==4&&state.Part==0,"host callback only changes carry fields");
        var count=host.publishes;TldMp.RabbitMetadataSync.RequestScan();
        TldMp.RabbitMetadataSync.Tick(true,true,player,data,host,0.1);
        Check(host.publishes==count,"requested scan retains publication ledger");
        TldMp.RabbitMetadataSync.HostState=null;
        TldMp.RabbitMetadataSync.RequestScan();TldMp.RabbitMetadataSync.Tick(true,true,player,data,host,0.2);
        Check(host.publishes==count+1,"requested scan publishes changed carry state");
        var guest=Node(false,2);var gd=Data();var go=new GameObject();var gr=Nyul(go);Item(gd,go,gr,42,7);
        SetupGuest(guest);guest.RaiseState(TldMp.RabbitProtocol.WriteMetadata(state));
        nyulscript found;TldMp.RabbitMetadata actual;
        Check(!TldMp.RabbitMetadataSync.TryApplied(7,out actual,out found),"pending state is not applied");
        TldMp.RabbitMetadataSync.Tick(true,true,player,gd,guest,1);
        Check(TldMp.RabbitMetadataSync.TryApplied(7,out actual,out found)&&ReferenceEquals(found,gr)&&actual.Lease==3,"applied carry grant available");
        var newer=state;newer.Lease=5;guest.RaiseState(TldMp.RabbitProtocol.WriteMetadata(newer));
        Check(!TldMp.RabbitMetadataSync.TryApplied(7,out actual,out found),"new pending grant invalidates old applied state");
        TldMp.RabbitMetadataSync.Tick(true,true,player,gd,guest,1.1);
        guest.Ready=false;Check(!TldMp.RabbitMetadataSync.TryApplied(7,out actual,out found),"unready guest rejects grant");guest.Ready=true;
        player.loadedmove=false;Check(!TldMp.RabbitMetadataSync.TryApplied(7,out actual,out found),"unloaded player rejects grant");player.loadedmove=true;
        var replacement=new GameObject();Item(gd,replacement,Nyul(replacement),42,7);
        Check(!TldMp.RabbitMetadataSync.TryApplied(7,out actual,out found),"replacement root rejects stale grant");
        TldMp.RabbitMetadataSync.Tick(true,true,player,gd,guest,1.2);
        Check(TldMp.RabbitMetadataSync.TryApplied(7,out actual,out found),"replacement is available after application");
        guest.RaiseState(TldMp.RabbitProtocol.WriteMetadata(new TldMp.RabbitMetadata{Item=7,Prefab=42,Flags=1,Part=-1}));
        Check(!TldMp.RabbitMetadataSync.TryApplied(7,out actual,out found),"tombstone immediately invalidates grant");
        TldMp.RabbitMetadataSync.Reset();
    }
    static void MotionOwnership()
    {
        TldMp.RabbitMetadataSync.Reset();syncScript.s=new syncScript();
        var host=Node(true,1);host.Peers[2]=new TldMp.StablePeer{Ready=true,Session=20};
        var data=Data();var obj=new GameObject();var root=Nyul(obj);Item(data,obj,root,42,7);
        var p=new GameObject().Add(new fpscontroller());
        TldMp.RabbitMetadataSync.MotionStreamOwnsLivingPoses=true;
        TldMp.RabbitMetadataSync.Tick(true,true,p,data,host,0);
        Check(host.publishes==1&&syncScript.s.posSends==0,"live metadata publishes without competing pose snaps");
        root.ragdoll.ragdoll=true;
        TldMp.RabbitMetadataSync.Tick(true,true,p,data,host,1);
        Check(host.publishes==2&&syncScript.s.posSends==1,"ragdoll metadata retains native pose fallback");
        TldMp.RabbitMetadataSync.MotionStreamOwnsLivingPoses=false;TldMp.RabbitMetadataSync.Reset();
    }
    public static int Main()
    {
        var hdata=Data();
        var host=Node(true,10);
        host.Peers[20]=new TldMp.StablePeer{Session=200,Ready=true};
        var hroot=new GameObject();
        var hnyul=Nyul(hroot);
        hnyul.size=0f;
        Item(hdata,hroot,hnyul,42,7);
        var hp=mainscript.s.player;
        TldMp.RabbitMetadataSync.Reset();
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,0);
        Check(host.publishes==1,"host first publish");
        TldMp.RabbitMetadata m;TldMp.RabbitProtocol.TryReadMetadata(host.published[0],out m);
        Check(m.Item==7&&m.Prefab==42&&m.Size==0f&&m.Health==10&&m.ShootHealth==5&&m.ExplosionHealth==2,"host meta fields size0");
        Check(m.Flags==14&&m.Part==-1&&m.Holder==0,"host flags holder part");
        Check(hnyul.size==0f&&hnyul.AI.dies==0&&hnyul.ragdoll.rags==0,"host no mutation");
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,0.2);
        Check(host.publishes==1,"unchanged no resend before deadline");
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,0.6);
        Check(host.publishes==1,"unchanged no resend after deadline");
        host.Peers[20].Session=201;
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,0.7);
        Check(host.publishes==2,"same peer new session forces resend");
        host.Peers[21]=new TldMp.StablePeer{Session=201,Ready=true};
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,1.0);
        Check(host.publishes==3,"distinct colliding peers resend");
        host.Peers[21].Ready=false;
        var posBefore=syncScript.s.posSends;
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,1.5);
        Check(syncScript.s.posSends>0,"pose sent with ready peer");
        Check(syncScript.s.ragSends==0,"no rag pose without ragdollPos");
        host.published.Clear();
        var hroot2=new GameObject();
        var hnyul2=Nyul(hroot2);
        var hit2=Item(hdata,hroot2,hnyul2,43,8);
        hit2.ragdollPos=hroot2.Add(new ragdollchildpos{childs=new Transform[]{null}});
        hroot2.activeInHierarchy=false;
        var posBefore2=syncScript.s.posSends;
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,2.0);
        Check(syncScript.s.posSends==posBefore2+1,"inactive root no pose");
        Check(syncScript.s.ragSends==0,"null ragdoll child no rag send");
        host.Peers[21].Ready=true;
        var dicMut=new GameObject();
        var dicMutItem=Item(hdata,dicMut,Nyul(dicMut),44,9);
        host.onPublish=delegate{hdata.items.Remove(9);};
        host.published.Clear();
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,2.6);
        host.onPublish=null;
        hdata.items.Remove(8);
        host.failPublish=true;
        var pubBefore=host.publishes;
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,3.2);
        Check(host.publishes==pubBefore,"failed publish not counted");
        host.failPublish=false;
        host.published.Clear();
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,3.8);
        var tombOk=false;
        foreach(var b in host.published){TldMp.RabbitMetadata t;if(!TldMp.RabbitProtocol.TryReadMetadata(b,out t))continue;if((t.Flags&1)!=0&&t.Item==8)tombOk=true;}
        Check(tombOk,"tombstone retry after failed publish");
        var t5=new GameObject();
        var n5=Nyul(t5);n5.started=false;
        Item(hdata,t5,n5,45,10);
        hdata.items[10].idInSave=10;
        host.published.Clear();
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,4.4);
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,5.0);
        var tomb10=false;
        foreach(var b in host.published){TldMp.RabbitMetadata t;if(!TldMp.RabbitProtocol.TryReadMetadata(b,out t))continue;if((t.Flags&1)!=0&&t.Item==10)tomb10=true;}
        Check(!tomb10,"transient unready not tombstoned");

        var g=Node(false,20);
        var gd=Data();
        var gp=new GameObject().Add(new fpscontroller());
        SetupGuest(g);
        var live=Meta(11,50,14,0.5f,10,5,2,0,0,0,0,-1);
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(live));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,0.1);
        Check(TldMp.RabbitReplicas.begins==0,"unknown root queued no apply");
        var groot=new GameObject();
        var gnyul=Nyul(groot);
        gnyul.started=false;
        Item(gd,groot,gnyul,50,11);
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,0.2);
        Check(TldMp.RabbitReplicas.begins==0,"unstarted root still queued");
        gnyul.started=true;
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,0.3);
        Check(TldMp.RabbitReplicas.begins==1&&TldMp.RabbitReplicas.ends==1,"created root applies");
        Check(gnyul.size==0.5f&&gnyul.breakable.health==10,"applied size+hp");
        var mism=Meta(11,51,14,0.9f,1,1,1,0,0,0,0,-1);
        var begBefore=TldMp.RabbitReplicas.begins;
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(mism));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,0.4);
        Check(TldMp.RabbitReplicas.begins==begBefore,"prefab mismatch not applied");
        var notRab=new GameObject();
        var nritem=notRab.Add(new tosaveitemscript{id=52,idInSave=12});
        gd.items[12]=nritem;
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(12,52,14,0.5f,1,1,1,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,0.5);
        Check(TldMp.RabbitReplicas.begins==begBefore,"non-rabbit not applied");
        gnyul.size=0.5f;
        var dead=Meta(11,50,12,0.5f,0,0,0,0,0,0,0,-1);
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(dead));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,0.6);
        Check(gnyul.AI.dies==1&&!gnyul.senses.alive,"die once");
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(dead));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,0.7);
        Check(gnyul.AI.dies==1,"repeat same frame no extra die");
        var dead2=Meta(11,50,12,0.6f,0,0,0,0,0,0,0,-1);
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(dead2));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,0.8);
        Check(gnyul.AI.dies==1,"duplicate dead frame no extra die");
        var preDead=new GameObject();
        var pdnyul=Nyul(preDead);pdnyul.senses.alive=false;
        Item(gd,preDead,pdnyul,50,13);
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(13,50,12,0.5f,0,0,0,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,0.9);
        Check(pdnyul.AI.dies==0,"pre-dead no StartDie");
        var staleAlive=Meta(11,50,30,0.5f,10,5,2,0,0,0,0,-1);
        var ragBefore=gnyul.ragdoll.rags;
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(staleAlive));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,1.0);
        Check(gnyul.senses.alive==false,"no resurrection");
        Check(gnyul.ragdoll.rags==ragBefore+1,"stale alive does not unrag dead");
        var liveRoot=new GameObject();
        var lr=Nyul(liveRoot);
        Item(gd,liveRoot,lr,51,14);
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(14,51,30,0.5f,10,5,2,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,1.1);
        Check(lr.ragdoll.ragdoll&&lr.ragdoll.rags==1,"live ragdoll on");
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(14,51,14,0.5f,10,5,2,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,1.2);
        Check(!lr.ragdoll.ragdoll&&lr.ragdoll.rags==2,"live unrag");
        var ear=Meta(14,51,2,0.5f,10,5,2,0,0,0,0,-1);
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(ear));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,1.3);
        Check(lr.senses.looseEars==1&&lr.senses.looseEyes==1&&!lr.senses.hasEars&&!lr.senses.hasEyes,"ears+eyes lost");
        Check(lr.senses.eyeObjects[0].activeInHierarchy==false,"eyes objects disabled");
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(14,51,14,0.5f,10,5,2,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,1.4);
        Check(lr.senses.hasEars&&lr.senses.hasEyes&&lr.senses.eyeObjects[0].activeInHierarchy,"ears+eyes restored");
        var rep=new GameObject();
        var repN=Nyul(rep);
        gd.items[11]=rep.Add(new tosaveitemscript{id=50,idInSave=11,nyul=repN,useThisForPos=rep.transform});
        var repDead=Meta(11,50,12,0.7f,0,0,0,0,0,0,0,-1);
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(repDead));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,1.5);
        Check(repN.AI.dies==1,"replacement root dies once");
        var sizeRoot=new GameObject();
        var sn=Nyul(sizeRoot);sn.size=0.3f;
        Item(gd,sizeRoot,sn,53,15);
        g.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(15,53,14,0.8f,7,3,1,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g,1.6);
        Check(sn.size==0.8f&&sn.breakable.health==7&&sn.breakable.shootHealth==3&&sn.breakable.explosionHealth==1,"3 HP restored after size init");
        var oldNode=g;
        var g2=Node(false,21);
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g2,2.0);
        Check(oldNode.RabbitStateReceived==null,"old node unsubscribed");
        Check(g2.RabbitStateReceived!=null,"new node subscribed");
        TldMp.RabbitMetadataSync.Tick(true,true,hp,hdata,host,5.0);
        var hostState=Meta(1,1,14,0.5f,1,1,1,0,0,0,0,-1);
        var hb=host.publishes;
        host.RaiseState(TldMp.RabbitProtocol.WriteMetadata(hostState));
        Check(host.publishes==hb,"host ignores state");
        g2.RaiseState(new byte[]{9,9,9});
        var gb2=TldMp.RabbitReplicas.begins;
        TldMp.RabbitMetadataSync.Tick(true,true,gp,gd,g2,2.1);
        Check(TldMp.RabbitReplicas.begins==gb2,"malformed no mutation");

        var ov=Node(false,30);
        var od=Data();
        var oroot=new GameObject();
        var on=Nyul(oroot);
        Item(od,oroot,on,60,200);
        TldMp.RabbitMetadataSync.Reset();
        TldMp.RabbitMetadataSync.Tick(true,false,null,null,ov,0);
        for(uint i=0;i<256;i++)od.items[1000+i]=oroot.Add(new tosaveitemscript{id=60,idInSave=1000+i,nyul=on});
        for(uint i=0;i<256;i++)ov.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(1000+i,60,14,0.5f,1,1,1,0,0,0,0,-1)));
        ov.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(2000,60,14,0.5f,1,1,1,0,0,0,0,-1)));
        Check(!TldMp.RabbitMetadataSync.Healthy,"257th unique fails closed");
        var ob=TldMp.RabbitReplicas.begins;
        ov.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(2001,60,14,0.5f,1,1,1,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,od,ov,3.0);
        Check(TldMp.RabbitReplicas.begins==ob,"unhealthy stops all work");
        TldMp.RabbitMetadataSync.Reset();
        Check(TldMp.RabbitMetadataSync.Healthy,"reset restores");

        var iv=Node(true,40);
        TldMp.RabbitMetadataSync.Reset();
        syncScript.s.syncItemInterwall=0;
        iv.Peers[50]=new TldMp.StablePeer{Session=1,Ready=true};
        var id2=Data();
        var ivr=new GameObject();
        Item(id2,ivr,Nyul(ivr),70,300);
        TldMp.RabbitMetadataSync.Tick(true,true,hp,id2,iv,0);
        Check(!TldMp.RabbitMetadataSync.Healthy,"zero interval fails closed");
        syncScript.s.syncItemInterwall=0.5f;
        TldMp.RabbitMetadataSync.Reset();
        TldMp.RabbitMetadataSync.Tick(true,true,hp,id2,iv,0);
        Check(TldMp.RabbitMetadataSync.Healthy,"reset after interval");
        TldMp.RabbitMetadataSync.Tick(true,true,hp,id2,iv,double.NaN);
        Check(!TldMp.RabbitMetadataSync.Healthy,"NaN now fails closed");
        TldMp.RabbitMetadataSync.Reset();
        var nh=new GameObject();
        var nn=Nyul(nh);nn.breakable.health=float.NaN;
        var nd=Data();
        Item(nd,nh,nn,71,301);
        var iv2=Node(true,41);
        iv2.Peers[51]=new TldMp.StablePeer{Session=2,Ready=true};
        TldMp.RabbitMetadataSync.Tick(true,true,hp,nd,iv2,0);
        Check(!TldMp.RabbitMetadataSync.Healthy,"NaN native HP fails closed");
        TldMp.RabbitMetadataSync.Reset();

        var th=Node(false,60);
        var td=Data();
        var troot=new GameObject();
        var tn=Nyul(troot);
        Item(td,troot,tn,80,400);
        TldMp.RabbitMetadataSync.Tick(true,false,null,null,th,0);
        nyulscript.throwOnSize=true;
        var eb=TldMp.RabbitReplicas.ends;
        th.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(400,80,14,0.9f,1,1,1,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,td,th,0.5);
        Check(TldMp.RabbitReplicas.ends==eb+1,"EndMutation finally on throw");
        Check(!TldMp.RabbitMetadataSync.Healthy,"native throw fails closed");
        nyulscript.throwOnSize=false;

        var wb=Node(false,70);
        var wd=Data();
        var wr=new GameObject();
        var wn=Nyul(wr);
        Item(wd,wr,wn,90,500);
        TldMp.RabbitMetadataSync.Reset();
        TldMp.RabbitMetadataSync.Tick(true,false,null,null,wb,0);
        wb.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(500,90,14,0.5f,1,1,1,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,wd,wb,0.2);
        Check(wn.size==0.5f&&TldMp.RabbitReplicas.begins>0,"world activation applies queued");
        TldMp.RabbitMetadataSync.Tick(true,true,gp,wd,wb,0.3);
        var wb2=TldMp.RabbitReplicas.begins;
        TldMp.RabbitMetadataSync.Tick(true,false,gp,wd,wb,0.4);
        TldMp.RabbitMetadataSync.Tick(true,true,gp,wd,wb,0.5);
        Check(TldMp.RabbitReplicas.begins==wb2,"world teardown clears pending");
        TldMp.RabbitMetadataSync.Tick(false,true,gp,wd,wb,0.6);
        Check(wb.RabbitStateReceived==null,"disabled unsubscribes");

        var init=Node(false,80);
        var iData=Data();
        var ir=new GameObject();
        var iny=Nyul(ir);
        Item(iData,ir,iny,91,600);
        TldMp.RabbitMetadataSync.Reset();
        TldMp.RabbitMetadataSync.Tick(true,false,null,null,init,0);
        init.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(600,91,14,0.6f,1,1,1,0,0,0,0,-1)));
        TldMp.RabbitMetadataSync.Tick(true,true,gp,iData,init,0.1);
        Check(iny.size==0.6f,"null player/data then valid preserves queue");

        var capN=Node(false,90);
        var capD=Data();
        var cr=new GameObject();
        var cn=Nyul(cr);
        Item(capD,cr,cn,95,700);
        TldMp.RabbitMetadataSync.Reset();
        TldMp.RabbitMetadataSync.Tick(true,false,null,null,capN,0);
        for(uint i=0;i<256;i++)capN.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(2000+i,95,14,0.5f,1,1,1,0,0,0,0,-1)));
        capN.RaiseState(TldMp.RabbitProtocol.WriteMetadata(Meta(2000,95,14,0.9f,2,2,2,0,0,0,0,-1)));
        Check(TldMp.RabbitMetadataSync.Healthy,"existing ID update at capacity ok");
        LifecycleRegressions();
        CarryMetadataApis();
        MotionOwnership();
        var sessionNode=Node(false,100);
        sessionNode.HostSession=77;
        var sessionData=Data();
        var sessionObject=new GameObject();
        var sessionRoot=Nyul(sessionObject);
        Item(sessionData,sessionObject,sessionRoot,96,701);
        TldMp.RabbitMetadataSync.Reset();
        TldMp.RabbitMetadataSync.Tick(true,true,gp,sessionData,sessionNode,0);
        var sessionBytes=TldMp.RabbitProtocol.WriteMetadata(Meta(701,96,14,0.6f,1,1,1,0,0,0,0,-1));
        sessionNode.RaiseState(sessionBytes);
        TldMp.RabbitMetadataSync.Tick(true,true,gp,sessionData,sessionNode,0.1);
        TldMp.RabbitMetadata sessionMeta;nyulscript appliedRoot;
        Check(TldMp.RabbitMetadataSync.TryApplied(701,out sessionMeta,out appliedRoot)&&
              ReferenceEquals(appliedRoot,sessionRoot)&&(sessionMeta.Flags&2)!=0,"production grant marks living rabbit with bit 2");
        sessionNode.HostSession=78;
        Check(!TldMp.RabbitMetadataSync.TryApplied(701,out sessionMeta,out appliedRoot),"host-session change invalidates grant before tick");
        TldMp.RabbitMetadataSync.Tick(true,true,gp,sessionData,sessionNode,0.2);
        Check(!TldMp.RabbitMetadataSync.TryApplied(701,out sessionMeta,out appliedRoot),"new host session requires fresh metadata");
        sessionNode.RaiseState(sessionBytes);
        TldMp.RabbitMetadataSync.Tick(true,true,gp,sessionData,sessionNode,0.3);
        Check(TldMp.RabbitMetadataSync.TryApplied(701,out sessionMeta,out appliedRoot),"fresh metadata restores new-session grant");
        Console.WriteLine("PASS "+checks+" rabbit metadata sync checks");
        return 0;
    }
}
