using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TldMp;
using UnityEngine;

// This fixture compiles the production adapter and policies against the
// smallest stand-ins needed at Unity/game boundaries. Assertions exercise the
// adapter's actual wrapper enumerators, session state and repair mutations.
namespace UnityEngine
{
    public class Object
    {
        static int nextId;
        readonly int instanceId = ++nextId;
        public int GetInstanceID() { return instanceId; }
        public bool Destroyed;
        public static void Destroy(Object value) { if (value != null) value.Destroyed = true; }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        Transform ownTransform;
        public virtual Transform transform
        {
            get { return ownTransform ?? (gameObject == null ? null : gameObject.transform); }
            set { ownTransform = value; }
        }
        public virtual T GetComponent<T>() where T : class { return null; }
        public T GetComponentInParent<T>() where T : class { return null; }
    }

    public class MonoBehaviour : Component { }

    public sealed class GameObject : Object
    {
        public readonly Transform transform;
        public GameObject() { transform = new Transform(); transform.gameObject = this; }
    }

    public sealed class Transform : Component
    {
        Transform parentValue;
        Vector3 worldPosition;
        Vector3 localPositionValue;
        public Transform parent { get { return parentValue; } set { SetParent(value, true); } }
        public Vector3 position
        {
            get { return parentValue == null ? localPositionValue : parentValue.position + localPositionValue; }
            set { worldPosition = value; localPositionValue = parentValue == null ? value : value - parentValue.position; }
        }
        public Vector3 localPosition
        {
            get { return localPositionValue; }
            set { localPositionValue = value; worldPosition = position; }
        }
        public void SetParent(Transform parent, bool worldPositionStays)
        {
            Vector3 world = position;
            parentValue = parent;
            if (worldPositionStays) localPositionValue = parentValue == null ? world : world - parentValue.position;
            worldPosition = position;
        }
    }

    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero { get { return new Vector3(0,0,0); } }
        public static Vector3 up { get { return new Vector3(0,1,0); } }
        public static Vector3 down { get { return new Vector3(0,-1,0); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public float magnitude { get { return (float)Math.Sqrt(x*x+y*y+z*z); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public bool Equals(Vector3 other) { return x==other.x && y==other.y && z==other.z; }
        public override bool Equals(object value) { return value is Vector3 && Equals((Vector3)value); }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.Equals(b); }
        public static bool operator !=(Vector3 a, Vector3 b) { return !a.Equals(b); }
        public override string ToString() { return "("+x+","+y+","+z+")"; }
    }

    public struct Bounds { public Vector3 min; }
    public class Collider : Component { public bool enabled=true, isTrigger; public Bounds bounds; }
    public class Rigidbody : Component
    {
        public bool isKinematic, useGravity;
        public Vector3 velocity, angularVelocity, position;
    }
    public struct RaycastHit { public Collider collider; public Vector3 normal, point; }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public static class Physics
    {
        public static RaycastHit[] QueuedHits = new RaycastHit[0];
        public static int NonAllocCalls,RaycastAllCalls;
        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results,
            float distance, int layerMask, QueryTriggerInteraction query)
        {
            NonAllocCalls++;
            int count=Math.Min(results.Length,QueuedHits.Length);
            Array.Copy(QueuedHits,results,count);return count;
        }
        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction, float distance,
            int layerMask, QueryTriggerInteraction query) { RaycastAllCalls++;return QueuedHits; }
    }
    public static class Time { public static int frameCount; public static float fixedTime, time, realtimeSinceStartup; }
}

public struct Vector3d
{
    public double x,y,z;
    public Vector3d(double x,double y,double z) { this.x=x;this.y=y;this.z=z; }
}

public enum msgType : byte { itemPosGlobal=16, velocity=19, attachable=42, itemPosPlayer=18, removeItem=36 }

public sealed class inputscript { public bool useDown; }
public sealed class seatscript : Component { public Transform sitPos; }
public sealed class mpplayerscript : MonoBehaviour { }
public sealed class nativePhyslock { }
public sealed class nativePickup { public nativePhyslock physlock; }
public sealed class nativeAttachable { public bool attached; }
public sealed class tosaveitemscript : MonoBehaviour
{
    public uint idInSave;
    public tosaveitemscript LocalParent;
    public int LocalIndex;
    public nativePickup P;
    public nativeAttachable attachable;
    public void GetLocalStuff(out tosaveitemscript parent,out Transform anchor,out int index)
    { parent=LocalParent;anchor=null;index=LocalIndex; }
}
public sealed class fpscontroller : MonoBehaviour
{
    public bool Bsitting,died;
    public seatscript seat;
    public inputscript input;
    public Rigidbody RB;
    public Collider[] C;
    public override T GetComponent<T>()
    {
        if(typeof(T)==typeof(Rigidbody))return RB as T;
        return null;
    }
}

public sealed class StableNode
{
    public bool IsHost,Ready;
    public Exception Error;
}
public sealed class syncScript
{
    public static syncScript s;
    public float tryAgainTime=.1f;
    public int tryAgains=4;
}
public sealed class mapSettings { }
public sealed class menuhandler
{
    public static menuhandler s;
    public mapSettings currentMainMap;
}
public sealed class mainscript
{
    public static mainscript s;
    public fpscontroller player;
    public static Vector3d GlobalFromUnityPos(Vector3 p) { return new Vector3d(p.x,p.y,p.z); }
    public static Vector3 UnityPosFromGlobal(Vector3d p) { return new Vector3((float)p.x,(float)p.y,(float)p.z); }
}
public static class StablePackets
{
    public static uint Item(byte[] bytes)
    {
        if(bytes==null || bytes.Length<5)return 0;
        return (uint)(bytes[1] | bytes[2]<<8 | bytes[3]<<16 | bytes[4]<<24);
    }
}

namespace TldMp
{
    public static partial class StableGameBridge
    {
        public static bool Connected;
        public static StableNode node;
        static readonly Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();
        static readonly List<string> diagnostics=new List<string>();
        static bool Find(uint id,out tosaveitemscript item) { return items.TryGetValue(id,out item); }
        public static void Register(tosaveitemscript item) { items[item.idInSave]=item; }
        static void JoinDiagnostic(string text) { diagnostics.Add(text); }
        static void DiagLimited(string key,string text,double interval) { diagnostics.Add(text); }
        // Attachment diagnostics live in AttachDiagnostics.cs (not compiled here).
        static void NoteAttachablePacket(string direction,byte[] bytes) { }
        static void NoteAttachOutcome(uint id) { }
        public static string[] Diagnostics { get { return diagnostics.ToArray(); } }
        public static void ClearDiagnostics() { diagnostics.Clear(); }
        // Stand-ins for the Unity-facing clearance helpers (SpawnClearanceAdapter.cs,
        // not compiled here). Disabled, so these tests keep exercising the
        // support/live hold exactly as before; the clearance policy has its own
        // tests in SpawnClearanceTests.cs.
        static bool SpawnClearanceEnabled { get { return false; } }
        static bool SpawnPointClear(fpscontroller player,UnityEngine.Vector3 at,out string blocker) { blocker="none";return true; }
        static bool FindFreeSpawn(fpscontroller player,UnityEngine.Vector3 at,out UnityEngine.Vector3 free) { free=at;return false; }
        static bool FindClearAnchor(fpscontroller player,UnityEngine.Vector3 at,out UnityEngine.Vector3 free) { free=at;return false; }
    }
}

internal static class NativeSafetyAdapterTests
{
    static int checks;
    static void Check(bool condition,string message)
    { checks++;if(!condition)throw new Exception(message); }

    static object Invoke(string name,params object[] args)
    {
        MethodInfo method=typeof(StableGameBridge).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic);
        if(method==null)throw new MissingMethodException(name);
        return method.Invoke(null,args);
    }
    static void SetField(string name,object value)
    { typeof(StableGameBridge).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,value); }
    static T GetField<T>(string name)
    { return (T)typeof(StableGameBridge).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null); }
    static byte[] Packet(msgType type,uint id)
    { return new byte[]{(byte)type,(byte)id,(byte)(id>>8),(byte)(id>>16),(byte)(id>>24)}; }
    static bool AllowPacket(byte[] packet)
    { return (bool)Invoke("AllowNativeSafetyPacket",packet); }

    static void ResetHarness()
    {
        Invoke("ResetNativeSafety");
        StableGameBridge.Connected=true;
        StableGameBridge.node=new StableNode { IsHost=false,Ready=false };
        mainscript.s=new mainscript();
        menuhandler.s=new menuhandler { currentMainMap=new mapSettings() };
        syncScript.s=new syncScript();
        StableGameBridge.ClearDiagnostics();
        StableGameBridge.Register(new tosaveitemscript { idInSave=100 });
        UnityEngine.Time.frameCount=1;UnityEngine.Time.fixedTime=1;UnityEngine.Time.time=1;
        UnityEngine.Physics.QueuedHits=new UnityEngine.RaycastHit[0];
        UnityEngine.Physics.NonAllocCalls=UnityEngine.Physics.RaycastAllCalls=0;
        Invoke("BeginNativeSafetySession",new object[]{(ulong)55});
    }

    static tosaveitemscript Item(uint id)
    { tosaveitemscript value;Check(StableGameBridgeFind(id,out value),"fixture item registered");return value; }
    static bool StableGameBridgeFind(uint id,out tosaveitemscript item)
    {
        MethodInfo method=typeof(StableGameBridge).GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic);
        object[] args=new object[]{id,null};bool found=(bool)method.Invoke(null,args);item=(tosaveitemscript)args[1];return found;
    }
    static void DrainOne(IEnumerator enumerator)
    { if(enumerator==null)throw new Exception("wrapper returned null");enumerator.MoveNext(); }

    sealed class CountingEnumerator : IEnumerator,IDisposable
    {
        readonly int length;
        public int Moves,Disposals;
        public CountingEnumerator(int length) { this.length=length; }
        public object Current { get { return null; } }
        public bool MoveNext() { Moves++;return Moves<=length; }
        public void Reset() { Moves=0; }
        public void Dispose() { Disposals++; }
    }

    static void TestWorldPosePackets()
    {
        ResetHarness();
        tosaveitemscript item=Item(100);item.attachable=new nativeAttachable { attached=true };
        Check(!AllowPacket(Packet(msgType.itemPosGlobal,100)),"mounted item global pose is dropped");
        Check(!AllowPacket(Packet(msgType.velocity,100)),"mounted item velocity is dropped");
        item.attachable.attached=false;item.P=new nativePickup { physlock=new nativePhyslock() };
        Check(!AllowPacket(Packet(msgType.itemPosGlobal,100)),"stored item global pose is dropped");
        Check(!AllowPacket(Packet(msgType.velocity,100)),"stored item velocity is dropped");
        item.P=null;
        Check(AllowPacket(Packet(msgType.itemPosGlobal,100)),"loose item global pose remains native");
        Check(AllowPacket(Packet(msgType.velocity,100)),"loose item velocity remains native");
        Check(AllowPacket(Packet(msgType.itemPosGlobal,999)),"unknown item pose remains native");
    }

    static tosaveitemscript MakeParent(uint id)
    { return new tosaveitemscript { idInSave=id }; }

    static void TestWrapperInvalidationAndSeparation()
    {
        ResetHarness();
        tosaveitemscript item=Item(100);item.LocalParent=MakeParent(200);item.LocalIndex=2;
        var nativeAttachment=new CountingEnumerator(5);
        IEnumerator attachment=StableGameBridge.WrapNativeAttachment(nativeAttachment,item,100,0,0,0,0);
        Check(attachment!=nativeAttachment,"native attachment retry is wrapped during a session");
        DrainOne(attachment);
        Check(nativeAttachment.Moves==1,"first native attachment retry is allowed");

        var nativeLocalPose=new CountingEnumerator(5);
        IEnumerator localPose=StableGameBridge.WrapNativeLocalPose(nativeLocalPose,100,200,2);
        DrainOne(localPose);
        Check(nativeLocalPose.Moves==1,"matching loose-to-storage local pose can begin its native retry");
        DrainOne(attachment);
        Check(nativeAttachment.Moves==2,"local-pose retry does not cancel the independent attachment retry");

        Check(AllowPacket(Packet(msgType.attachable,100)),"incoming type42 packet is accepted natively");
        Check(!attachment.MoveNext() && nativeAttachment.Moves==2,
            "incoming type42 invalidates a pending attachment retry before its next native step");
        Check(!localPose.MoveNext() && nativeLocalPose.Moves==1,
            "the same topology event invalidates the older local-pose retry");
        Dispose(attachment);Dispose(localPose);
        Check(nativeAttachment.Disposals==1 && nativeLocalPose.Disposals==1,
            "canceled wrappers dispose their native enumerators");
    }

    static void TestOutgoingTopologyInvalidation()
    {
        ResetHarness();
        tosaveitemscript item=Item(100);
        var native=new CountingEnumerator(5);
        IEnumerator wrapped=StableGameBridge.WrapNativeAttachment(native,item,100,0,0,0,0);
        DrainOne(wrapped);
        Check(native.Moves==1,"outgoing topology fixture starts its native attachment retry");
        Invoke("ObserveNativeSafetyOutgoing",Packet(msgType.attachable,100));
        Check(!wrapped.MoveNext() && native.Moves==1,
            "outgoing type42 topology update invalidates an older native attachment retry");
        Dispose(wrapped);
        Check(native.Disposals==1,"outgoing-invalidated retry disposes its native enumerator");
    }

    static void TestNativeLocalPoseRetrySemantics()
    {
        ResetHarness();
        var unknownNative=new CountingEnumerator(5);
        IEnumerator unknown=StableGameBridge.WrapNativeLocalPose(unknownNative,999,200,-200004);
        DrainOne(unknown);
        Check(unknownNative.Moves==1,
            "unknown mounted-local item retains its first native attachment opportunity");
        Dispose(unknown);

        ResetHarness();
        tosaveitemscript item=Item(100);item.LocalParent=MakeParent(200);item.LocalIndex=-200004;
        item.attachable=new nativeAttachable { attached=true };
        var matchingNative=new CountingEnumerator(5);
        IEnumerator matching=StableGameBridge.WrapNativeLocalPose(matchingNative,100,200,-200004);
        DrainOne(matching);
        Check(matchingNative.Moves==1,
            "mounted negative-index local pose proceeds on its matching native frame");
        Dispose(matching);

        ResetHarness();
        item=Item(100);item.LocalParent=MakeParent(200);item.LocalIndex=-200005;
        item.attachable=new nativeAttachable { attached=true };
        var wrongNative=new CountingEnumerator(5);
        IEnumerator wrong=StableGameBridge.WrapNativeLocalPose(wrongNative,100,200,-200004);
        Check(!wrong.MoveNext() && wrongNative.Moves==0,
            "mounted negative-index local pose is blocked when its live anchor frame differs");
        Dispose(wrong);

        ResetHarness();
        item=Item(100);item.attachable=new nativeAttachable { attached=true };
        Check(!AllowPacket(Packet(msgType.itemPosGlobal,100)),"mounted world pose is initially suppressed");
        Check(AllowPacket(Packet(msgType.attachable,100)),"native detach type42 is allowed through the wrapper");
        item.attachable.attached=false;
        Check(AllowPacket(Packet(msgType.itemPosGlobal,100)),
            "after a real detach, native World state permits global motion again");
    }

    static void SetSpawnSupport(bool supported)
    {
        UnityEngine.Physics.QueuedHits=supported
            ? new UnityEngine.RaycastHit[]{new UnityEngine.RaycastHit {
                collider=new UnityEngine.Collider(),normal=UnityEngine.Vector3.up,
                point=new UnityEngine.Vector3(0,0,0) }}
            : new UnityEngine.RaycastHit[0];
    }
    static fpscontroller BeginSpawnFixture(out Rigidbody body)
    {
        fpscontroller player=new fpscontroller { gameObject=new GameObject() };
        body=new Rigidbody { isKinematic=false,useGravity=true };
        player.RB=body;player.transform.position=new Vector3(1,2,3);
        mainscript.s.player=player;
        StableGameBridge.AfterNativeSpawnTeleport(player);
        return player;
    }
    static void TickSpawn(bool beforePhysics)
    { Invoke("TickNativeSpawnSafety",beforePhysics); }

    static void TestSpawnSupportAndRelease()
    {
        ResetHarness();
        Rigidbody body;BeginSpawnFixture(out body);
        StableGameBridge.node.Ready=true;
        SetSpawnSupport(false);
        TickSpawn(false);
        Check(GetField<NativeSpawnSafety>("spawnSafety").Active && body.isKinematic && !body.useGravity,
            "live state without collision support keeps the spawn hold active");

        ResetHarness();
        BeginSpawnFixture(out body);
        SetSpawnSupport(true);
        TickSpawn(false);
        Check(GetField<NativeSpawnSafety>("spawnSafety").HasSpawnSupport,
            "native support at the intended spawn is observed");
        StableGameBridge.node.Ready=true;
        TickSpawn(false);
        Check(GetField<NativeSpawnSafety>("spawnSafety").Active && body.isKinematic,
            "support at the same fixed-time does not release before a completed physics opportunity");
        UnityEngine.Time.fixedTime+=.02f;
        TickSpawn(false);
        Check(!GetField<NativeSpawnSafety>("spawnSafety").Active && !body.isKinematic && body.useGravity,
            "live state releases after a later supported physics opportunity and restores native flags");

        ResetHarness();
        BeginSpawnFixture(out body);
        SetSpawnSupport(true);
        TickSpawn(false);
        SetSpawnSupport(false);
        TickSpawn(false);
        Check(!GetField<NativeSpawnSafety>("spawnSafety").HasSpawnSupport,
            "loss of collision support resets the observed-support phase");
        StableGameBridge.node.Ready=true;
        UnityEngine.Time.fixedTime+=.02f;
        TickSpawn(false);
        Check(GetField<NativeSpawnSafety>("spawnSafety").Active && body.isKinematic,
            "time advancing without support cannot release a reset spawn hold");
        SetSpawnSupport(true);
        TickSpawn(false);
        Check(GetField<NativeSpawnSafety>("spawnSafety").Active,
            "support reacquisition starts a fresh opportunity window");
        UnityEngine.Time.fixedTime+=.02f;
        TickSpawn(false);
        Check(!GetField<NativeSpawnSafety>("spawnSafety").Active && !body.isKinematic && body.useGravity,
            "reacquired support releases only after its own completed physics opportunity");

        ResetHarness();
        BeginSpawnFixture(out body);
        var dense=new UnityEngine.RaycastHit[64];
        dense[63]=new UnityEngine.RaycastHit { collider=new UnityEngine.Collider(),
            normal=UnityEngine.Vector3.up,point=new UnityEngine.Vector3(0,0,0) };
        UnityEngine.Physics.QueuedHits=dense;
        TickSpawn(false);
        Check(UnityEngine.Physics.NonAllocCalls==1 && UnityEngine.Physics.RaycastAllCalls==1 &&
            GetField<NativeSpawnSafety>("spawnSafety").HasSpawnSupport,
            "a saturated 64-hit support buffer falls back to RaycastAll and retains the ground hit");
    }

    static void TestSlowFramesDoNotPrematurelyCancelNativeRetries()
    {
        ResetHarness();
        tosaveitemscript item=Item(100);
        var native=new CountingEnumerator(5);
        IEnumerator wrapped=StableGameBridge.WrapNativeAttachment(native,item,100,0,0,0,0);
        DrainOne(wrapped);
        UnityEngine.Time.time+=1000f;
        DrainOne(wrapped);
        Check(native.Moves==2,
            "large scaled-time advance does not expire native retry before its own attempt limit");
        DrainOne(wrapped);
        Check(native.Moves==3,"native attachment retry continues after slow rendered-frame time");
        Dispose(wrapped);
    }

    static void TestSessionResetCancelsRetry()
    {
        ResetHarness();
        tosaveitemscript item=Item(100);
        var native=new CountingEnumerator(5);
        IEnumerator wrapped=StableGameBridge.WrapNativeAttachment(native,item,100,0,0,0,0);
        DrainOne(wrapped);
        Check(native.Moves==1,"native attachment retry starts before session reset");
        Invoke("ResetNativeSafety");
        Check(!wrapped.MoveNext() && native.Moves==1,
            "session reset invalidates pending native attachment retries");
        Dispose(wrapped);
        Check(native.Disposals==1,"session-canceled wrapper disposes the native enumerator");

        ResetHarness();item=Item(100);native=new CountingEnumerator(5);
        wrapped=StableGameBridge.WrapNativeAttachment(native,item,100,0,0,0,0);
        DrainOne(wrapped);
        Invoke("BeforeNativeSafetyWorldReplacement");
        Check(!wrapped.MoveNext() && native.Moves==1 && native.Disposals==1,
            "world replacement retires old retries even when object identity and session remain unchanged");
    }

    static void Dispose(IEnumerator value)
    { IDisposable disposable=value as IDisposable;if(disposable!=null)disposable.Dispose(); }

    static void TestSeatRepairAndExit()
    {
        ResetHarness();
        Transform sitPos=new GameObject().transform;sitPos.position=new Vector3(4,2,1);
        seatscript seat=new seatscript { gameObject=new GameObject(),sitPos=sitPos };
        fpscontroller player=new fpscontroller { gameObject=new GameObject(),Bsitting=true,seat=seat,input=new inputscript() };
        mainscript.s.player=player;
        Vector3 captured=new Vector3(.1f,.2f,.3f);
        player.transform.SetParent(sitPos,true);player.transform.localPosition=captured;
        StableGameBridge.AfterNativeSeatEntry(player,seat);
        Check(GetField<NativeSeatSafety>("seatSafety").HasSnapshot,"native seat entry captures a valid root offset");

        Transform wrongParent=new GameObject().transform;wrongParent.position=new Vector3(50,0,0);
        player.transform.SetParent(wrongParent,true);player.transform.localPosition=new Vector3(9,9,9);
        UnityEngine.Time.frameCount++;
        Invoke("TickNativeSeatSafety");
        Check(player.transform.parent==sitPos,"seated player root returns to the captured native sitPos parent");
        Check(player.transform.localPosition==captured,"seat repair restores only the captured root local offset");

        Rigidbody stray=new Rigidbody { isKinematic=false,useGravity=true };
        player.RB=stray;
        UnityEngine.Time.frameCount++;
        Invoke("TickNativeSeatSafety");
        Check(stray.Destroyed && player.RB==null,"genuinely seated root-only Rigidbody is removed separately");

        player.input.useDown=true;
        StableGameBridge.BeforeNativeSeatExit(player,false);
        player.Bsitting=false;player.seat=null;player.transform.SetParent(null,true);
        StableGameBridge.AfterNativeSeatExit(player);
        UnityEngine.Time.frameCount++;
        Invoke("TickNativeSeatSafety");
        Check(player.transform.parent==null,"legitimate native exit is never repaired back into the seat");
        Check(!GetField<NativeSeatSafety>("seatSafety").HasSnapshot,"legitimate exit clears captured seat state");
        Check(Array.Exists(StableGameBridge.Diagnostics,delegate(string line){return line.Contains("reason=input");}),
            "seat exit diagnostics report the native input reason");
    }

    static void TestSpawnRetargetAndResetRestoresFlags()
    {
        ResetHarness();
        fpscontroller player=new fpscontroller { gameObject=new GameObject() };
        Rigidbody body=new Rigidbody { isKinematic=false,useGravity=true };
        player.RB=body;player.transform.position=new Vector3(1,2,3);
        mainscript.s.player=player;
        StableGameBridge.AfterNativeSpawnTeleport(player);
        Check(GetField<NativeSpawnSafety>("spawnSafety").Active,"spawn teleport begins a protected hold");
        object[] args=new object[]{null};
        bool has=(bool)typeof(StableGameBridge).GetMethod("TryGetProtectedSpawn",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Invoke(null,args);
        Check(has && (Vector3)args[0]==new Vector3(1,2,3),"first native teleport supplies the intended spawn anchor");

        player.transform.position=new Vector3(20,5,-7);
        StableGameBridge.AfterNativeSpawnTeleport(player);
        args=new object[]{null};
        has=(bool)typeof(StableGameBridge).GetMethod("TryGetProtectedSpawn",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Invoke(null,args);
        Check(has && (Vector3)args[0]==new Vector3(20,5,-7),"later native teleport retargets an active spawn hold");

        Invoke("TickNativeSpawnSafety",true);
        Check(body.isKinematic && !body.useGravity && player.transform.position==new Vector3(20,5,-7),
            "pre-physics hold uses the retargeted spawn anchor");
        Invoke("ResetNativeSafety");
        Check(!body.isKinematic && body.useGravity,"session reset restores the captured native Rigidbody flags");
        Check(!GetField<NativeSpawnSafety>("spawnSafety").Active,"session reset retires the spawn hold");
    }

    static void Run()
    {
        TestWorldPosePackets();
        TestWrapperInvalidationAndSeparation();
        TestOutgoingTopologyInvalidation();
        TestNativeLocalPoseRetrySemantics();
        TestSessionResetCancelsRetry();
        TestSeatRepairAndExit();
        TestSpawnRetargetAndResetRestoresFlags();
        TestSpawnSupportAndRelease();
        TestSlowFramesDoNotPrematurelyCancelNativeRetries();
    }

    public static void Main()
    {
        Run();
        Console.WriteLine("PASS: "+checks+" production native safety adapter assertions");
    }
}
