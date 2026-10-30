// Exact production vehicle ingress/dispatch IL is transplanted into this native
// API boundary. StableNode, codec, sequence filter and sender clock are real.
// Scene readiness, Unity objects, native physics and Steam admission are stand-ins.
using System;
using System.Collections.Generic;
using TldMp;
namespace UnityEngine
{
    public class Object
    {
        public static bool operator ==(Object a,Object b){return System.Object.ReferenceEquals(a,b);}
        public static bool operator !=(Object a,Object b){return !System.Object.ReferenceEquals(a,b);}
        public override bool Equals(object other){return System.Object.ReferenceEquals(this,other);}
        public override int GetHashCode(){return base.GetHashCode();}
    }
    public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero {get{return new Vector3();}}}
    public static class Debug {public static void LogError(object message){}}
}
namespace Steamworks
{
    public struct CSteamID {public ulong Value;public CSteamID(ulong value){Value=value;}public static explicit operator ulong(CSteamID id){return id.Value;}}
}
public enum msgType:byte {carPosGlobal=39,carVelocity=20}
public class carscript:UnityEngine.Object {}
public class tosaveitemscript:UnityEngine.Object
{
    public enum carWhatDoing {notCar,nothing,justHost,pickedup,shotgun,pushgripping,driving}
    public carscript car=new carscript();public carWhatDoing Role;
    public carWhatDoing CarWhatDoing(){return Role;}
    public bool ThisOneInChargeCar(carWhatDoing remote){return Role>=carWhatDoing.justHost && Role>remote;}
}
namespace GeneszSteamP2PBase
{
    public class SteamP2PNetworkingUnderTheHoodScript
    {public static bool Admit=true;public static bool Expected(Steamworks.CSteamID sender,msgType type){return Admit;}}
    public class networkingScript
    {
        public static networkingScript s=new networkingScript();
        public void ProcessMessage(Steamworks.CSteamID sender,byte[] bytes,msgType type){StableGameBridge.Native((ulong)sender,bytes);}
    }
}
namespace TldMp
{
    internal static class NetStats {internal static long SequenceStale,SequencePassed;}
    public static class StableGameBridge
    {
        static StableNode node;
        static readonly PoseSequenceFilter poseFilter=new PoseSequenceFilter();
        static readonly SenderClock vehicleClock=new SenderClock(),senderClock=new SenderClock();
        static VehicleMotionSample dispatchVehicleMotion;
        static ulong dispatchSender;static uint dispatchSequence;
        static bool applyingVehicleMotion,dispatching;internal static int DispatchType=-1;
        static int packetErrors;static double nextFailure,fixtureClock;
        static readonly Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();
        static ulong Own {get{return node.Own;}}internal static double Now {get{return fixtureClock;}}
        static bool WorldSceneAvailable(){return SceneReady;}
        static bool Find(uint id,out tosaveitemscript item){return items.TryGetValue(id,out item);}
        static bool ValidateData(byte[] bytes){return StablePackets.Valid(bytes);}
        static void NoteHandler(byte[] bytes,long ticks){}
        // Bodies replaced by production IL.
        static bool ReceiveVehicleMotion(ulong source,byte[] bytes){throw new Exception("production IL missing");}
        internal static bool DispatchVehicleVelocity(uint id,out UnityEngine.Vector3 v,out UnityEngine.Vector3 w,out bool impact){throw new Exception("production IL missing");}
        internal static bool DispatchSendTime(double now,out double time,out double lateness){throw new Exception("production IL missing");}
        public static bool AllowForward(){throw new Exception("production IL missing");}
        internal static ulong MotionSender {get{throw new Exception("production IL missing");}}
        public static bool SceneReady=true,ThrowPose;
        public static int NativeForwards;public static int Errors {get{return packetErrors;}}
        public static readonly List<string> Events=new List<string>();
        public static readonly List<double> Times=new List<double>();
        public static void Configure(StableNode current)
        {
            node=current;poseFilter.Clear();vehicleClock.Clear();senderClock.Clear();items.Clear();Events.Clear();Times.Clear();
            packetErrors=0;nextFailure=0;dispatchSequence=0;DispatchType=-1;dispatchVehicleMotion=null;applyingVehicleMotion=false;fixtureClock=1;NativeForwards=0;SceneReady=true;ThrowPose=false;
            GeneszSteamP2PBase.SteamP2PNetworkingUnderTheHoodScript.Admit=true;
        }
        public static void LocalDriver(uint id){items[id]=new tosaveitemscript {Role=tosaveitemscript.carWhatDoing.driving};}
        public static void LocalHost(uint id){items[id]=new tosaveitemscript {Role=tosaveitemscript.carWhatDoing.justHost};}
        public static bool Receive(ulong source,byte[] bytes){fixtureClock+=.02;return ReceiveVehicleMotion(source,bytes);}
        public static void Native(ulong sender,byte[] bytes)
        {
            if(AllowForward())NativeForwards++;
            if(ThrowPose && bytes[0]==39)throw new Exception("injected native handler failure");
            uint id=BitConverter.ToUInt32(bytes,1);UnityEngine.Vector3 v,w;bool impact;
            if(!DispatchVehicleVelocity(id,out v,out w,out impact) || sender!=MotionSender)throw new Exception("atomic sample context missing");
            UnityEngine.Vector3 otherV,otherW;bool otherImpact;
            if(DispatchVehicleVelocity(id+1,out otherV,out otherW,out otherImpact))throw new Exception("another car consumed this sample's velocity");
            double time,late;if(!DispatchSendTime(Now,out time,out late))throw new Exception("send clock context missing");
            Events.Add(sender+":"+bytes[0]+":"+v.x);Times.Add(time);
        }
        public static bool ContextRestored()
        {UnityEngine.Vector3 v,w;bool impact;return AllowForward() && !DispatchVehicleVelocity(7,out v,out w,out impact) && DispatchType==-1;}
    }
}
internal static class VehicleMotionReplayFixture
{
    static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Put(byte[] b,int offset,byte[] value){Buffer.BlockCopy(value,0,b,offset,value.Length);}
    static VehicleMotionSample Frame(uint seq,uint clock,float speed)
    {
        var pose=new byte[49];pose[0]=39;Put(pose,1,BitConverter.GetBytes(7u));Put(pose,5,BitConverter.GetBytes(1));Put(pose,45,BitConverter.GetBytes(6));
        var velocity=new byte[33];velocity[0]=20;Put(velocity,1,BitConverter.GetBytes(7u));Put(velocity,5,BitConverter.GetBytes(speed));Put(velocity,29,BitConverter.GetBytes(6));
        return new VehicleMotionSample {Sequence=seq,ClockMs=clock,Pose=pose,Velocity=velocity};
    }
    static void Main()
    {
        var host=new StableNode(10,10,999,110,"fixture",100);var client=new StableNode(30,10,999,130,"fixture",100);
        host.Send=delegate(ulong target,byte[] bytes,bool reliable){if(target==30 && bytes[0]==254)client.Receive(10,bytes,0);};
        client.Send=delegate(ulong target,byte[] bytes,bool reliable){host.Receive(30,bytes,0);};client.Tick(0);
        host.Peers[30].Ready=true;host.Peers[20]=new StablePeer {Ready=true,Session=120};
        var relayed=new List<byte[]>();host.Send=delegate(ulong target,byte[] bytes,bool reliable){Check(target==30 && !reliable,"motion relay target/reliability changed");relayed.Add(bytes);};
        var first=Frame(1,1000,10);var second=Frame(2,1050,20);byte[] packet=VehicleMotionCodec.Encode(20,second,first);
        StableGameBridge.Configure(host);Check(StableGameBridge.Receive(20,packet),"envelope not consumed before native dispatcher");
        Check(StableGameBridge.Events.Count==4 && StableGameBridge.Events[0]=="20:39:10" && StableGameBridge.Events[1]=="20:20:10" && StableGameBridge.Events[2]=="20:39:20","production dispatch mixed identity or motion halves");
        Check(StableGameBridge.NativeForwards==0 && relayed.Count==1,"native forwarding split or duplicated the complete relay");
        Check(Math.Abs(StableGameBridge.Times[2]-StableGameBridge.Times[0]-.05)<1e-6,"first repeated packet collapsed two send times into one arrival time");
        StableGameBridge.Receive(20,packet);Check(StableGameBridge.Events.Count==4 && relayed.Count==1,"duplicate frame replayed physics or re-relayed");
        Check(StableGameBridge.ContextRestored(),"motion context leaked after native dispatch");

        StableGameBridge.Configure(client);StableGameBridge.Receive(10,relayed[0]);
        Check(StableGameBridge.Events.Count==4 && StableGameBridge.Events[0].StartsWith("20:"),"host relay lost original driver identity");
        StableGameBridge.Configure(client);StableGameBridge.Receive(99,packet);Check(StableGameBridge.Events.Count==0 && StableGameBridge.Errors==1,"non-host injected relayed vehicle state");
        StableGameBridge.Configure(host);StableGameBridge.Receive(20,VehicleMotionCodec.Encode(99,second,first));Check(StableGameBridge.Events.Count==0 && StableGameBridge.Errors==1,"client spoofed another driver");
        StableGameBridge.Configure(client);StableGameBridge.LocalDriver(7);StableGameBridge.Receive(10,packet);
        Check(StableGameBridge.Events.Count==0 && StableGameBridge.ContextRestored(),"previous driver overwrote local driver's pose or velocity during handover");
        StableGameBridge.Configure(host);StableGameBridge.LocalDriver(7);relayed.Clear();StableGameBridge.Receive(20,packet);
        Check(StableGameBridge.Events.Count==0 && relayed.Count==0,"host rejected stale driver locally but forwarded it to spectators");
        StableGameBridge.Configure(host);StableGameBridge.LocalHost(7);var unowned=Frame(4,1150,5);Put(unowned.Pose,45,BitConverter.GetBytes(1));Put(unowned.Velocity,29,BitConverter.GetBytes(1));
        StableGameBridge.Receive(20,VehicleMotionCodec.Encode(20,unowned,null));
        Check(StableGameBridge.Events.Count==0 && relayed.Count==0,"motion rejected by native host authority still reached spectators");
        StableGameBridge.Configure(client);StableGameBridge.ThrowPose=true;StableGameBridge.Receive(10,packet);
        Check(StableGameBridge.Errors==2 && StableGameBridge.ContextRestored(),"failed native handler leaked forwarding/context state");
        StableGameBridge.ThrowPose=false;StableGameBridge.Receive(10,VehicleMotionCodec.Encode(20,Frame(3,1100,30),second));
        Check(StableGameBridge.Events.Count==2 && StableGameBridge.Events[0]=="20:39:30","native failure wedged later motion");
        StableGameBridge.Configure(client);StableGameBridge.SceneReady=false;StableGameBridge.Receive(10,packet);Check(StableGameBridge.Events.Count==0,"motion touched an unavailable Unity scene");
        Console.WriteLine("PASS: "+checks+" exact production vehicle ingress/dispatch IL assertions; relay identity, native forwarding suppression, atomic motion, send-time spacing, duplicate rejection, local-driver protection and exception recovery. Native scene/physics/Steam stand-ins.");
    }
}
