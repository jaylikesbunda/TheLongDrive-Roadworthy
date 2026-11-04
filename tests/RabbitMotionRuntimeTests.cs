using System;
using System.Collections.Generic;
using System.Reflection;
using TldMp;
using UnityEngine;
namespace UnityEngine {
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
 public struct Quaternion {public float x,y,z,w;public Quaternion(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}public static Quaternion Euler(float x,float y,float z){return new Quaternion(0,0,0,1);}}
 public class GameObject {public bool activeInHierarchy=true;}
 public class Transform {public Transform parent;public Vector3 position,eulerAngles;public Quaternion rotation;}
 public static class Debug {public static readonly List<string> logs=new List<string>();public static void LogWarning(string s){throw new Exception(s);}public static void Log(string s){logs.Add(s);}}
}
namespace Steamworks {public struct CSteamID {public CSteamID(ulong x){}}}
namespace GeneszSteamP2PBase {public enum msgType {carPosGlobal} public static class SteamP2PNetworkingUnderTheHoodScript {public static bool Expected(Steamworks.CSteamID x,msgType t){return true;}}}
public struct Vector3d {public double x,y,z;public Vector3d(double a,double b,double c){x=a;y=b;z=c;}}
public class mainscript {public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d(p.x+offset,p.y,p.z);}public static Vector3 UnityPosFromGlobal(Vector3d p){return new Vector3((float)(p.x-offset),(float)p.y,(float)p.z);}public static double offset;}
public class ragdollactivatescript {public bool ragdoll;}
public class movecreaturescript {public float speed=4;}
public class nyulscript {public ragdollactivatescript ragdoll=new ragdollactivatescript();public movecreaturescript move=new movecreaturescript();public bool attacking,roaring;}
public class pickupable {public bool pickedUp,inRightHand;public bool isweared(){return false;}public int inInventory(){return 0;}public bool PickedUp(bool b){return false;}}
public class tosaveitemscript {public uint idInSave;public int id;public Transform transform=new Transform(),useThisForPos=new Transform();public GameObject gameObject=new GameObject();public pickupable P;public bool mppickedup,mpequipped,mpstored,mpweared;public nyulscript root=new nyulscript();public int syncs;public void UpdGotSyncTime(){syncs++;}}
public class savedatascript {public static savedatascript s;public Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();}
namespace TldMp {
 internal static class RabbitGuestAnimation {internal static int motions,stops,actions;internal static bool attacking,roaring;internal static float speed;internal static void Stop(nyulscript r){stops++;}internal static void CosmeticMotion(nyulscript r,float value){motions++;speed=value;}internal static void CosmeticActions(nyulscript r,bool a,bool b){actions++;attacking=a;roaring=b;}}
 internal static class RabbitReplicas {internal static nyulscript RabbitRoot(tosaveitemscript i){return i==null?null:i.root;}}
 internal sealed class StablePeer {public bool Ready=true;}
 internal sealed class StableNode {public bool IsHost,Compatible=true,Ready=true;public string Error;public ulong Host=1,HostSession=9,Session=9;public Dictionary<ulong,StablePeer> Peers=new Dictionary<ulong,StablePeer>();public List<byte[]> sent=new List<byte[]>();public bool PublishNative(byte[] b){sent.Add(b);return true;}}
 internal static class Loopback {internal static ulong Peer=1;}
 public static partial class StableGameBridge {static StableNode node;static bool Connected=true,loopbackMode=true,DiagnosticMode=false;static double Now;static bool WorldSceneAvailable(){return true;}}
}
internal static class RabbitMotionRuntimeTests {
 static Type B=typeof(StableGameBridge);static BindingFlags F=BindingFlags.Static|BindingFlags.NonPublic;static int count;
 static void Set(string n,object v){B.GetField(n,F).SetValue(null,v);}static object Call(string n,params object[] args){return B.GetMethod(n,F).Invoke(null,args);}static void Check(bool v,string n){count++;if(!v)throw new Exception(n);}
 static void Main(){
  var item=new tosaveitemscript{idInSave=123,id=4};item.useThisForPos.position=new Vector3(10,0,0);savedatascript.s=new savedatascript();savedatascript.s.items[123]=item;
  var host=new StableNode{IsHost=true};host.Peers.Add(2,new StablePeer());Set("node",host);Set("Now",0d);Call("RabbitMotionReset");Call("RabbitMotionTick");Check(host.sent.Count==1,"host publishes");RabbitMotionSample a;Check(RabbitMotionCodec.Read(host.sent[0],out a)&&a.Active&&a.X==10,"live pose");
  Set("Now",.05d);Call("RabbitMotionTick");Check(host.sent.Count==1,"rate limit");Set("Now",.11d);item.useThisForPos.position=new Vector3(12,0,0);Call("RabbitMotionTick");Check(host.sent.Count==2,"refresh");
  item.mppickedup=true;Set("Now",.12d);Call("RabbitMotionTick");RabbitMotionSample stop;Check(host.sent.Count==3&&RabbitMotionCodec.Read(host.sent[2],out stop)&&!stop.Active,"remote held stop");
  item.mppickedup=false;item.root.ragdoll.ragdoll=true;Set("Now",.25d);Call("RabbitMotionTick");Check(host.sent.Count==3,"ragdoll not published");item.root.ragdoll.ragdoll=false;
  var guest=new StableNode();Set("node",guest);Call("RabbitMotionReset");Set("Now",1d);Call("RabbitMotionTick");
  Check((bool)Call("ReceiveRabbitMotion",1UL,host.sent[0]),"intercepts pose");Call("RabbitMotionTick");Check(item.useThisForPos.position.x==10&&item.syncs==1,"guest applies host");Check(RabbitGuestAnimation.motions>0&&RabbitGuestAnimation.speed==0,"first sample visual speed zero");
  mainscript.offset=1000;Call("RabbitMotionTick");Check(item.useThisForPos.position.x==-990,"global pose follows recenter");mainscript.offset=0;
  Set("Now",1.1d);Call("ReceiveRabbitMotion",1UL,host.sent[1]);Set("Now",1.2d);Call("RabbitMotionTick");Check(item.useThisForPos.position.x>10,"interpolation advances");
  int stopsBefore=RabbitGuestAnimation.stops;Call("ReceiveRabbitMotion",1UL,host.sent[2]);Check(RabbitGuestAnimation.stops==stopsBefore+1,"inactive packet stops cosmetic immediately");float held=item.useThisForPos.position.x;Set("Now",1.3d);Call("ReceiveRabbitMotion",1UL,host.sent[0]);Call("RabbitMotionTick");Check(item.useThisForPos.position.x==held,"stale pose rejected after stop");
  var bad=RabbitMotionCodec.Write(new RabbitMotionSample{Session=8,Sequence=999,Item=123,Prefab=4,Active=true,X=300});Call("ReceiveRabbitMotion",1UL,bad);Call("RabbitMotionTick");Check(item.useThisForPos.position.x==held,"wrong session rejected");
  item.root.ragdoll.ragdoll=true;Call("ReceiveRabbitMotion",1UL,host.sent[1]);Call("RabbitMotionTick");Check(item.useThisForPos.position.x==held,"ragdoll remains native");
  item.root.ragdoll.ragdoll=false;var resume=RabbitMotionCodec.Write(new RabbitMotionSample{Session=9,Sequence=4,Item=123,Prefab=4,Active=true,X=40});Call("ReceiveRabbitMotion",1UL,resume);Call("RabbitMotionTick");Check(item.useThisForPos.position.x==40,"new motion resumes");
  Set("Now",2d);item.useThisForPos.position=new Vector3(99,0,0);Call("RabbitMotionTick");Check(item.useThisForPos.position.x==99,"expired stream releases transform");
  var replacement=new tosaveitemscript{idInSave=123,id=4};replacement.useThisForPos.position=new Vector3(500,0,0);savedatascript.s.items[123]=replacement;Call("RabbitMotionTick");Call("ReceiveRabbitMotion",1UL,resume);Call("RabbitMotionTick");Check(replacement.useThisForPos.position.x==500,"old sample rejected after ID replacement");
  var newer=RabbitMotionCodec.Write(new RabbitMotionSample{Session=9,Sequence=5,Item=123,Prefab=4,Active=true,X=55});Call("ReceiveRabbitMotion",1UL,newer);Call("RabbitMotionTick");Check(replacement.useThisForPos.position.x==55,"new sample drives replacement");
  var action=RabbitMotionCodec.Write(new RabbitMotionSample{Session=9,Sequence=6,Item=123,Prefab=4,Active=true,Attacking=true,Roaring=true,X=55});Call("ReceiveRabbitMotion",1UL,action);Call("RabbitMotionTick");Check(RabbitGuestAnimation.attacking&&RabbitGuestAnimation.roaring,"authenticated actions reach cosmetics");
  int beforeExpiry=RabbitGuestAnimation.stops;Set("Now",2.7d);Call("RabbitMotionTick");Check(RabbitGuestAnimation.stops==beforeExpiry+1,"stale expiry stops cosmetics");
  int reports=Debug.logs.Count;Set("Now",10.5d);Call("RabbitMotionTick");Check(Debug.logs.Count==reports,"diagnostic throttled");
  Set("Now",11.1d);Call("RabbitMotionTick");Check(Debug.logs.Count==reports+1&&Debug.logs[reports].Contains("received=")&&Debug.logs[reports].Contains("expired="),"diagnostic counters reported");
  var host2=new StableNode{IsHost=true};host2.Peers.Add(2,new StablePeer());Set("node",host2);Set("Now",20d);Call("RabbitMotionTick");
  replacement.root.attacking=true;Set("Now",20.01d);Call("RabbitMotionTick");RabbitMotionSample actionSample;Check(host2.sent.Count==2&&RabbitMotionCodec.Read(host2.sent[1],out actionSample)&&actionSample.Attacking,"host action sent immediately");
  replacement.root.roaring=true;Set("Now",20.02d);Call("RabbitMotionTick");Check(host2.sent.Count==3&&RabbitMotionCodec.Read(host2.sent[2],out actionSample)&&actionSample.Attacking&&actionSample.Roaring,"roar change sent immediately");
  var contactGuest=new StableNode{HostSession=77,Session=33};
  Set("node",contactGuest);Set("Now",30d);Call("RabbitMotionReset");Call("RabbitMotionTick");
  var contactPacket=RabbitMotionCodec.Write(new RabbitMotionSample{Session=77,Sequence=1,Item=123,Prefab=4,Active=true,X=42});
  Call("ReceiveRabbitMotion",1UL,contactPacket);
  Check(StableGameBridge.RabbitContactCurrent(123,replacement,replacement.root,30d),"actual contact helper accepts authenticated live track");
  var tracks=(System.Collections.IDictionary)B.GetField("rabbitMotion",F).GetValue(null);
  var contactTrack=tracks[123u];
  var buffer=(RemotePoseBuffer)contactTrack.GetType().GetField("Buffer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(contactTrack);
  RemotePose contactPose;
  Check(StableGameBridge.TryRabbitPose(123,replacement,30d,out contactPose)&&contactPose.X==42,"actual contact pose samples shared buffer");
  var teleport=RabbitMotionCodec.Write(new RabbitMotionSample{Session=77,Sequence=2,Item=123,Prefab=4,Active=true,X=100});
  Call("ReceiveRabbitMotion",1UL,teleport);
  Check(StableGameBridge.TryRabbitPose(123,replacement,30d,out contactPose),"contact Sample leaves snap pending");
  Check(buffer.TakeSnap(out contactPose)&&contactPose.X==100,"contact sampling preserves visual TakeSnap");
  var inactive=RabbitMotionCodec.Write(new RabbitMotionSample{Session=77,Sequence=3,Item=123,Prefab=4,Active=false});
  Call("ReceiveRabbitMotion",1UL,inactive);
  Check(!StableGameBridge.RabbitContactCurrent(123,replacement,replacement.root,30d),"inactive packet invalidates contact immediately");
  Set("Now",30.1d);
  Call("ReceiveRabbitMotion",1UL,RabbitMotionCodec.Write(new RabbitMotionSample{Session=77,Sequence=4,Item=123,Prefab=4,Active=true,X=43}));
  Check(!StableGameBridge.RabbitContactCurrent(123,replacement,replacement.root,30.71d),"received sample expiry is not refreshed by pose sampling");
  var contactReplacement=new tosaveitemscript{idInSave=123,id=4};
  savedatascript.s.items[123]=contactReplacement;
  Check(!StableGameBridge.RabbitContactCurrent(123,replacement,replacement.root,30.1d),"canonical item replacement invalidates old contact");
  savedatascript.s.items[123]=replacement;
  Check(!StableGameBridge.RabbitContactCurrent(123,replacement,contactReplacement.root,30.1d),"root identity mismatch invalidates contact");
  contactGuest.HostSession=78;
  Check(!StableGameBridge.RabbitContactCurrent(123,replacement,replacement.root,30.1d),"same-node host session change invalidates old contact");
  Call("ReceiveRabbitMotion",1UL,RabbitMotionCodec.Write(new RabbitMotionSample{Session=78,Sequence=1,Item=123,Prefab=4,Active=true,X=44}));
  Check(StableGameBridge.RabbitContactCurrent(123,replacement,replacement.root,30.1d),"new host session accepts fresh lower sequence");
  Console.WriteLine("Rabbit motion runtime: "+count+" checks passed");
 }
}







