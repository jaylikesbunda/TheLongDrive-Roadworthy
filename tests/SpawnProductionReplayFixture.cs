using System;
using System.Collections.Generic;
using System.IO;
using TldMp;

namespace UnityEngine
{
    public class Object
    {
        public static bool operator ==(Object a,Object b){return ReferenceEquals(a,b);}
        public static bool operator !=(Object a,Object b){return !ReferenceEquals(a,b);}
        public override bool Equals(object o){return ReferenceEquals(this,o);}
        public override int GetHashCode(){return base.GetHashCode();}
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public T[] GetComponentsInChildren<T>() where T:Component
        {var list=new List<T>();GetComponentsInChildren(false,list);return list.ToArray();}
        public void GetComponentsInChildren<T>(bool inactive,List<T> list) where T:Component
        {Replay.Record("query:"+typeof(T).Name);list.Clear();gameObject.Query(list,inactive,true);}
    }
    public class Behaviour:Component {}
    public class MonoBehaviour:Behaviour {}
    public class GameObject:Object
    {
        public string Name;public bool Active=true;
        public readonly List<Component> Components=new List<Component>();
        public readonly List<GameObject> Children=new List<GameObject>();
        public GameObject(string name){Name=name;}
        public T Add<T>(T value) where T:Component{value.gameObject=this;Components.Add(value);return value;}
        public void SetActive(bool value){Active=value;Replay.Record("active:"+Name+":"+value);}
        public void Query<T>(List<T> list,bool inactive,bool root) where T:Component
        {
            if(!root && !Active && !inactive)return;
            foreach(var c in Components){var t=c as T;if(t!=null)list.Add(t);}
            foreach(var child in Children)child.Query(list,inactive,false);
        }
    }
    public class Rigidbody:Component
    {
        bool value;public bool isKinematic{get{return value;}set{this.value=value;Replay.Record("freeze:"+gameObject.Name);}}
    }
    public class Collider:Component {}
    public struct Vector3d {public double x,y,z;public Vector3d(double x,double y,double z){this.x=x;this.y=y;this.z=z;}}
    public static class Debug {public static void Log(object o){}public static void LogWarning(object o){}}
}
public class itemdatabase {public enum CarType {First=0,Second=1}}
public class mainscript {public static mainscript s=new mainscript();public itemdatabase.CarType StartCar;}
public class fixedSpawnScript:UnityEngine.MonoBehaviour
{
    public bool useStartCar;public List<UnityEngine.GameObject> startCars=new List<UnityEngine.GameObject>();
    public void FStart(){throw new Exception("native FStart was not extracted");}
}
public class randomTypeSelector:UnityEngine.MonoBehaviour
{
    public string Name;public void FStart(){Replay.Record("selector:"+Name+":"+Replay.Random());}
}
public class tosaveitemscript:UnityEngine.MonoBehaviour
{
    public string Name;public Action Started;public uint idInSave;public attachablescript attachable;
    public pickupable P;public List<mountStuff> physLocks=new List<mountStuff>();
    public List<partslotscript> partslotscripts=new List<partslotscript>();
    public bool gotSyncLocal;public uint gotSyncLocalParentID;
    public readonly List<UnityEngine.Collider> Colliders=new List<UnityEngine.Collider>();
    public UnityEngine.Collider GetColliderFromIndex(int i){return i>=0 && i<Colliders.Count?Colliders[i]:null;}
    public void FStart(){Replay.Record("item:"+Name+":"+Replay.Random());if(Started!=null)Started();}
}
public class pickupable:UnityEngine.MonoBehaviour {public mountStuff physlock;public tosaveitemscript tosave;}
public class mountStuff:UnityEngine.MonoBehaviour {public List<pickupable> stored=new List<pickupable>();}
public class partslotscript:UnityEngine.MonoBehaviour {}
public class attachablescript:UnityEngine.MonoBehaviour
{
    public bool attached;public save_attachable State=new save_attachable();
}
public class save_attachable
{
    public int attachType,indexType,poigenid,index;public uint parentid;public UnityEngine.Vector3d poiid,lpos,gpos;
    public save_attachable(){}
    public save_attachable(attachablescript a,uint id)
    {var s=a.State;attachType=s.attachType;indexType=s.indexType;poigenid=s.poigenid;index=s.index;parentid=s.parentid;poiid=s.poiid;lpos=s.lpos;gpos=s.gpos;}
}
public static class Replay
{
    public static readonly List<string> Trace=new List<string>();static uint rng;
    public static void Record(string value){Trace.Add(value);}
    public static int Random(){rng=unchecked(rng*1664525+1013904223);return (int)(rng&0x7fffffff);}
    public static void Reset(){rng=1234;Trace.Clear();}
}
namespace TldMp
{
    public static class StableGameBridge
    {
        static readonly Stack<List<UnityEngine.Rigidbody>> fixedBodyLists=new Stack<List<UnityEngine.Rigidbody>>();
        static readonly Stack<List<tosaveitemscript>> fixedItemLists=new Stack<List<tosaveitemscript>>();
        static readonly Stack<List<randomTypeSelector>> fixedSelectorLists=new Stack<List<randomTypeSelector>>();
        public sealed class SpawnApplyLedger
        {
            public ulong Session,Sequence;public byte[] Hash;public List<byte[]> Packets;
            public HashSet<uint> Definitions;public int NextPacket,WireBytes;public bool Completed,Published;
            public string Failure,PendingReason;
        }
        static readonly Dictionary<ulong,SpawnApplyLedger> spawnLedgers=new Dictionary<ulong,SpawnApplyLedger>();
        static readonly Dictionary<ulong,ulong> spawnCommitted=new Dictionary<ulong,ulong>();
        static readonly ItemRecoveryRequests itemRecovery=new ItemRecoveryRequests();
        static readonly SpawnCoordinator Construction=new SpawnCoordinator(delegate{return 0;});
        static int spawnApplyDepth,spawnNativeCallDepth,spawnLedgerBytes,spawnLedgerHigh,spawnLedgerHighWaterBytes;
        static bool dispatching;static StableNode node;
        public static readonly Dictionary<uint,tosaveitemscript> Items=new Dictionary<uint,tosaveitemscript>();
        public static Action<byte[]> Dispatch;public static int Dispatches,Published;
        static double Now{get{return 1;}}
        static bool Find(uint id,out tosaveitemscript item){return Items.TryGetValue(id,out item);}
        static bool HeldItemExists(uint id){return Items.ContainsKey(id);}
        static bool ValidateData(byte[] packet){return true;}
        static void EnsureWorldGeneration(){}
        static void Deliver(ulong source,byte[] packet)
        {
            Dispatches++;
            if(packet[0]==16){uint id=StablePackets.Item(packet);if(!Items.ContainsKey(id))Items[id]=new tosaveitemscript {idInSave=id,attachable=new attachablescript()};}
            if(Dispatch!=null)Dispatch(packet);
        }
        static bool TryReplayOneHeld(PendingItemQueue.Held held){Deliver(held.Sender,held.Bytes);return true;}
        public static void FixedSpawnFStart(fixedSpawnScript s){throw new Exception("helper was not extracted");}
        public static bool VerifyAttachmentPacket(byte[] p){throw new Exception("verifier was not extracted");}
        public static bool VerifyStoredPacket(byte[] p){throw new Exception("verifier was not extracted");}
        static bool CloseD(byte[] p,int o,UnityEngine.Vector3d v,double t){throw new Exception("not extracted");}
        static bool BytesEqual(byte[] a,byte[] b){throw new Exception("not extracted");}
        static bool SpawnHashMatches(byte[] a,byte[] b){throw new Exception("not extracted");}
        public static bool ApplySpawn(ulong source,ulong sequence,byte[] bytes){throw new Exception("not extracted");}
        static SpawnApplyLedger ParseSpawnLedger(ulong source,StablePeer peer,ulong sequence,byte[] bytes){throw new Exception("not extracted");}
        static bool ApplySpawnLedger(ulong source,StablePeer peer,SpawnApplyLedger ledger){throw new Exception("not extracted");}
        static bool VerifySpawnLedger(ulong source,SpawnApplyLedger ledger){throw new Exception("not extracted");}
        static void ReleaseLedger(SpawnApplyLedger ledger){throw new Exception("not extracted");}
        static bool VerifyPendingSpawnConstruction(out string reason){throw new Exception("not extracted");}
        public static void ConstructionFence(string reason){throw new Exception("not extracted");}
        public static void RequireConstructionSettled(){throw new Exception("not extracted");}
        public static int PendingBytes{get{return spawnLedgerBytes;}}
        public static void Reset()
        {
            Items.Clear();spawnLedgers.Clear();spawnCommitted.Clear();spawnApplyDepth=spawnNativeCallDepth=spawnLedgerBytes=0;
            string reason;Construction.ResetWorld(Construction.Generation+1,out reason);
            node=new StableNode(10,10,123,456,"same",100);
            node.Peers[20]=new StablePeer {Session=999,First=1000,Last=2000,Ready=true};
            node.Peers[30]=new StablePeer {Session=998,First=2001,Last=3000,Ready=true};
            Published=Dispatches=0;Dispatch=null;
            node.Send=delegate{Published++;};
        }
        public static int PoolReferences()
        {
            int n=0;foreach(var l in fixedBodyLists)n+=l.Count;foreach(var l in fixedItemLists)n+=l.Count;foreach(var l in fixedSelectorLists)n+=l.Count;return n;
        }
    }
}
public static class SpawnProductionReplayFixture
{
    static int checks;static bool helper;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception(text);}
    static void Throws(Action action,string text){bool thrown=false;try{action();}catch(InvalidOperationException){thrown=true;}Check(thrown,text);}
    static void Invoke(fixedSpawnScript value){if(helper)StableGameBridge.FixedSpawnFStart(value);else value.FStart();}
    static fixedSpawnScript Hierarchy(int car,bool nested,bool throws)
    {
        var root=new UnityEngine.GameObject("root");var script=root.Add(new fixedSpawnScript {useStartCar=true});
        var selected=new UnityEngine.GameObject("selected"){Active=false};script.startCars.Add(selected);script.startCars.Add(null);root.Children.Add(selected);
        selected.Add(new UnityEngine.Rigidbody());selected.Add(new tosaveitemscript {Name="car"});selected.Add(new randomTypeSelector {Name="car"});
        var late=new UnityEngine.GameObject("late"){Active=false};root.Children.Add(late);
        late.Add(new tosaveitemscript {Name="late"});late.Add(new randomTypeSelector {Name="late"});
        root.Add(new UnityEngine.Rigidbody());
        root.Add(new tosaveitemscript {Name="first",Started=delegate {
            late.SetActive(true);root.Add(new randomTypeSelector {Name="added"});
            if(nested){var inner=new UnityEngine.GameObject("nested");var child=inner.Add(new fixedSpawnScript());inner.Add(new tosaveitemscript {Name="nested"});inner.Add(new randomTypeSelector {Name="nested"});Invoke(child);}
            if(throws)throw new InvalidOperationException("fixture");
        }});
        root.Add(new randomTypeSelector {Name="root"});mainscript.s.StartCar=(itemdatabase.CarType)car;return script;
    }
    static string Trace(int car,bool nested,bool throws,bool candidate)
    {
        helper=candidate;Replay.Reset();var script=Hierarchy(car,nested,throws);
        try{Invoke(script);}catch(InvalidOperationException){Replay.Record("exception");}
        return String.Join("|",Replay.Trace);
    }
    static byte[] Attachment(uint id,int type)
    {
        var p=new byte[type==0?9:121];p[0]=42;Buffer.BlockCopy(BitConverter.GetBytes(id),0,p,1,4);Buffer.BlockCopy(BitConverter.GetBytes(type),0,p,5,4);return p;
    }
    static void Int(byte[] p,int at,int value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,p,at,4);}
    static byte[] Definition(uint id)
    {var p=new byte[45];p[0]=16;Int(p,1,(int)id);Int(p,5,1);return p;}
    static byte[] Stored(uint id,uint parent,int index)
    {var p=new byte[37];p[0]=17;Int(p,1,(int)id);Int(p,5,(int)parent);Int(p,9,index);return p;}
    static byte[] Transaction(params byte[][] packets)
    {
        using(var s=new MemoryStream())using(var w=new BinaryWriter(s))
        {int definitions=0;while(definitions<packets.Length && packets[definitions][0]==16)definitions++;w.Write(definitions);w.Write(packets.Length);foreach(var p in packets){w.Write(p.Length);w.Write(p);}return s.ToArray();}
    }
    static void FixedParity()
    {
        foreach(int car in new[]{-1,0,1,2,Int32.MaxValue})foreach(bool nested in new[]{false,true})foreach(bool throws in new[]{false,true})
        {
            string reference=Trace(car,nested,throws,false),candidate=Trace(car,nested,throws,true);
            Check(reference==candidate,"native/helper callback/RNG/hierarchy trace mismatch "+car+" "+nested+" "+throws);
            Check(StableGameBridge.PoolReferences()==0,"pooled lists retained scene references");
        }
        Check(!Trace(-1,false,false,true).Contains("item:late"),"item query incorrectly reran after activation");
        Check(Trace(-1,false,false,true).Contains("selector:late"),"selector query missed earlier callback activation");
        var root=new UnityEngine.GameObject("large");var script=root.Add(new fixedSpawnScript());
        for(int i=0;i<1100;i++)root.Add(new tosaveitemscript {Name="large"});
        helper=true;Invoke(script);Check(StableGameBridge.PoolReferences()==0,"large traversal retained references");
    }
    static void NativeVerifiers()
    {
        StableGameBridge.Reset();var item=new tosaveitemscript {idInSave=1000,attachable=new attachablescript()};StableGameBridge.Items[1000]=item;
        var detached=Attachment(1000,0);Check(StableGameBridge.VerifyAttachmentPacket(detached),"short detached packet rejected");
        item.attachable.attached=true;Check(!StableGameBridge.VerifyAttachmentPacket(detached),"still attached acknowledged detached");
        Check(!StableGameBridge.VerifyAttachmentPacket(new byte[8]),"short malformed accepted");
        var parent=new tosaveitemscript {idInSave=1001};StableGameBridge.Items[1001]=parent;
        var a=Attachment(1000,1);Int(a,9,2);Int(a,13,1001);Int(a,45,0);
        item.attachable.State=new save_attachable {attachType=1,indexType=2,parentid=1001,index=0};
        Check(StableGameBridge.VerifyAttachmentPacket(a),"matching item slot rejected");Int(a,13,1002);
        Check(!StableGameBridge.VerifyAttachmentPacket(a),"wrong slot parent accepted");
        var locker=new mountStuff();parent.physLocks.Add(locker);item.P=new pickupable {tosave=item,physlock=locker};
        var stored=Stored(1000,1001,0);Check(!StableGameBridge.VerifyStoredPacket(stored),"locker without membership accepted");
        locker.stored.Add(item.P);Check(StableGameBridge.VerifyStoredPacket(stored),"native 37byte storage lock rejected");
        item.P.physlock=new mountStuff();Check(!StableGameBridge.VerifyStoredPacket(stored),"wrong live locker accepted");
        item.gotSyncLocal=true;item.gotSyncLocalParentID=1001;parent.Colliders.Add(new UnityEngine.Collider());parent.partslotscripts.Add(new partslotscript());
        Check(StableGameBridge.VerifyStoredPacket(Stored(1000,1001,-200000)),"collider local pose rejected");
        Check(StableGameBridge.VerifyStoredPacket(Stored(1000,1001,-100000)),"slot local pose rejected");
        Check(!StableGameBridge.VerifyStoredPacket(Stored(1000,1001,-100001)),"missing slot accepted");
    }
    static void ApplyParity()
    {
        StableGameBridge.Reset();var wire=Transaction(Definition(1000),Attachment(1000,0));
        Check(StablePackets.Valid(Definition(1000)) && StablePackets.Valid(Attachment(1000,0)),"fixture framing is not native");
        Check(StableGameBridge.ApplySpawn(20,1,wire),"native creation/detach transaction rejected");
        int applied=StableGameBridge.Dispatches,published=StableGameBridge.Published;
        StableGameBridge.Items[1000].attachable.attached=true;
        Check(StableGameBridge.ApplySpawn(20,1,wire),"lost ACK retry rechecked changed committed graph");
        Check(StableGameBridge.Dispatches==applied && StableGameBridge.Published==published,"committed duplicate replayed/published");
        var changed=(byte[])wire.Clone();changed[changed.Length-1]^=1;
        Check(!StableGameBridge.ApplySpawn(20,1,changed),"same sequence different payload accepted");
        Check(StableGameBridge.PendingBytes==0,"committed ledger retained payload bytes");
        StableGameBridge.Reset();StableGameBridge.Dispatch=delegate(byte[] p){if(p[0]==42)StableGameBridge.Items[1000].attachable.attached=true;};
        Check(!StableGameBridge.ApplySpawn(20,1,wire),"unsettled relationship ACKed");
        applied=StableGameBridge.Dispatches;Throws(delegate{StableGameBridge.ConstructionFence("snapshot");},"snapshot copied partial relationship");
        Check(StableGameBridge.Dispatches==applied,"pending fence replayed native packets");
        StableGameBridge.Items[1000].attachable.attached=false;
        Check(StableGameBridge.ApplySpawn(20,1,wire),"settled relationship did not commit");
        Check(StableGameBridge.Dispatches==applied,"settled retry replayed native packets");
        StableGameBridge.Reset();StableGameBridge.Dispatch=delegate(byte[] p){if(p[0]==42)throw new InvalidOperationException("native mutation failed");};
        Check(!StableGameBridge.ApplySpawn(20,1,wire),"native throw ACKed");applied=StableGameBridge.Dispatches;
        Check(!StableGameBridge.ApplySpawn(20,1,wire),"failed transaction retried");
        Check(!StableGameBridge.ApplySpawn(20,2,wire),"new transaction bypassed failed partial mutation");
        Throws(delegate{StableGameBridge.ConstructionFence("save1");},"first failed save passed");
        Throws(delegate{StableGameBridge.ConstructionFence("save2");},"second failed save passed");
        Check(StableGameBridge.Dispatches==applied,"failed cursor replayed");
        StableGameBridge.Reset();StableGameBridge.Dispatch=delegate {
            Throws(delegate{StableGameBridge.ConstructionFence("nested");},"nested reader did not fail");
            Throws(StableGameBridge.RequireConstructionSettled,"per-record nested reader did not fail");
        };
        Check(StableGameBridge.ApplySpawn(20,1,wire),"nested-read rejection corrupted outer apply");
    }
    public static int Main()
    {
        try{FixedParity();NativeVerifiers();ApplyParity();Console.WriteLine("PASS: extracted spawn production methods "+checks+" checks; offline hierarchy/receiver stand-ins, not live Unity.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
