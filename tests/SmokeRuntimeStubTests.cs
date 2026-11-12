using System;
using System.Collections.Generic;
using System.Reflection;
using TldMp;
using UnityEngine;
using Steamworks;
namespace UnityEngine {
 public class Object {public bool destroyed;public static void Destroy(Object x){if(x!=null)x.destroyed=true;}}
 public class Transform:Object {public Vector3 localPosition;public Quaternion localRotation;public void SetParent(Transform p,bool world){}}
 public struct Vector3 {public Vector3(float x,float y,float z){}}
 public struct Quaternion {public static Quaternion Euler(float x,float y,float z){return new Quaternion();}}
 public struct Color {public Color(float r,float g,float b,float a){}}
 public class AudioClip:Object {}
 public class AudioSource:Object {public AudioClip clip;public bool loop,playOnAwake,isPlaying;public float volume,spatialBlend,minDistance,maxDistance;public object rolloffMode,outputAudioMixerGroup;public int plays,stops;public void Play(){isPlaying=true;plays++;} public void Stop(){isPlaying=false;stops++;}}
 public class GameObject:Object {public Transform transform=new Transform();public bool activeInHierarchy=true;public ParticleSystem ps;public ParticleSystemRenderer renderer=new ParticleSystemRenderer();public GameObject(string n){} public T AddComponent<T>() where T:new(){var x=new T();if(x is ParticleSystem){ps=(ParticleSystem)(object)x;ps.gameObject=this;}return x;} public T GetComponent<T>() where T:class{return renderer as T;}}
 public enum ParticleSystemStopBehavior {StopEmittingAndClear} public enum ParticleSystemSimulationSpace {Local,World,Custom}
 public class ParticleSystemRenderer:Object {public object sharedMaterial,renderMode;}
 public class ParticleSystem:Object {
  public struct Curve {public float constantMax;public static implicit operator Curve(float v){return new Curve{constantMax=v};}}
  public struct MainModule {public bool loop,playOnAwake;public ParticleSystemSimulationSpace simulationSpace;public Transform customSimulationSpace;public Curve startLifetime,startSpeed,startSize,startRotation,gravityModifier;public object startColor;public int maxParticles;}
  public struct ShapeModule {public bool enabled;public int shapeType;public float angle,radius;}
  public struct EmissionModule {ParticleSystem p;public EmissionModule(ParticleSystem ps){p=ps;} public bool enabled{get{return p.enabled;}set{p.enabled=value;}} public Curve rateOverTime{get{return p.rate;}set{p.rate=value;}}}
  public GameObject gameObject=new GameObject("particle"); public bool isPlaying,enabled;public int stopCount;public Curve rate;public MainModule main {get{return settings;}set{settings=value;}} public MainModule settings;public ShapeModule shape {get{return sh;}set{sh=value;}} public ShapeModule sh;public EmissionModule emission{get{return new EmissionModule(this);}}
  public void Stop(bool b,ParticleSystemStopBehavior s){stopCount++;isPlaying=false;} public void Play(){isPlaying=true;} public T GetComponent<T>() where T:class{return gameObject.GetComponent<T>();}
 }
 public static class Mathf {public static float Max(float a,float b){return Math.Max(a,b);}} public static class Debug {public static void LogWarning(string s){throw new Exception(s);}}
}
namespace Steamworks {public struct CSteamID {public ulong value;public CSteamID(ulong v){value=v;}public static explicit operator ulong(CSteamID x){return x.value;}}}
namespace GeneszSteamP2PBase {public enum msgType {carPosGlobal} public static class SteamP2PNetworkingUnderTheHoodScript {public static bool Expected(CSteamID id,msgType type){return true;}}}
public class tankcapscript {public ParticleSystem ps;public AudioSource SSource;public float maxRate,maxVolume;}
public class fpscontroller {public ParticleSystem smokeParticle;public float maxSmokeParticleRate;public tankcapscript piss;}
public class mainscript {public static mainscript s;public fpscontroller player;public Transform TCustomSpaceMain;}
public class mpplayerscript {public Transform lipT,THead;}
public class syncScript {public class playerSyncClass {public GameObject player;public mpplayerscript mpscript;}public static syncScript s;public Dictionary<CSteamID,playerSyncClass> players=new Dictionary<CSteamID,playerSyncClass>();}
namespace TldMp {
 internal sealed class StablePeer {public bool Ready;public ulong Session;}
 internal sealed class StableNode {public bool Compatible=true,Ready=true,IsHost;public string Error;public ulong Host=1,HostSession=99,Session=88;public Dictionary<ulong,StablePeer> Peers=new Dictionary<ulong,StablePeer>();public byte[] Last,LastRelay;public bool PublishNative(byte[] b){Last=b;return true;}public void PublishNativeTo(ulong p,byte[] b){LastRelay=b;}}
 internal static class Loopback {internal static ulong Peer=1;}
 public static partial class StableGameBridge {static StableNode node;static bool loopbackMode=true;static bool Connected=true;static ulong Own=2;static double Now;static bool WorldSceneAvailable(){return true;}}
}
internal static class SmokeRuntimeStubTests {
 static Type bridge=typeof(StableGameBridge);static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;static int count;
 static void Check(bool x,string m){count++;if(!x)throw new Exception(m);}static object Call(string name,params object[] args){return bridge.GetMethod(name,flags).Invoke(null,args);}static void Set(string name,object value){bridge.GetField(name,flags).SetValue(null,value);}
 static void Main(){
  var source=new ParticleSystem();source.gameObject=new GameObject("source");source.isPlaying=true;source.enabled=true;source.rate=1f;
  mainscript.s=new mainscript{player=new fpscontroller{smokeParticle=source,maxSmokeParticleRate=1},TCustomSpaceMain=new Transform()};
  syncScript.s=new syncScript();var id=new CSteamID(5);var player=new syncScript.playerSyncClass{player=new GameObject("first"),mpscript=new mpplayerscript{lipT=new Transform()}};syncScript.s.players.Add(id,player);
  Set("node",new StableNode());Set("Now",0d);Call("SmokeReset");
  var on=SmokeCodec.Encode(5,99,10,15);Check((bool)Call("ReceiveSmoke",1UL,on),"intercept");Call("SmokeTick");
  var effects=(System.Collections.IDictionary)bridge.GetField("smokeEffects",flags).GetValue(null);Check(effects.Count==1,"effect created");
  object effect=effects[5UL];var ps=(ParticleSystem)effect.GetType().GetField("Particle",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(effect);var go=(GameObject)effect.GetType().GetField("Object",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(effect);
  Check(ps.enabled && ps.rate.constantMax==1,"emission starts");
  player.player=new GameObject("replacement");player.mpscript.lipT=new Transform();Set("Now",.1d);Call("SmokeTick");Check(go.destroyed && effects.Count==1,"replacement destroys old effect");
  var old=SmokeCodec.Encode(5,99,9,0);Call("ReceiveSmoke",1UL,old);Call("SmokeTick");effect=effects[5UL];ps=(ParticleSystem)effect.GetType().GetField("Particle",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(effect);Check(ps.enabled,"stale stop rejected after replacement");
  int stops=ps.stopCount;Set("Now",2.1d);Call("SmokeTick");Check(!ps.enabled && ps.stopCount==stops,"missing refresh expires without clearing particles");
  var late=SmokeCodec.Encode(5,99,9,15);Call("ReceiveSmoke",1UL,late);Call("SmokeTick");Check(!ps.enabled,"expired stale packet rejected");
  var refresh=SmokeCodec.Encode(5,99,11,15);Call("ReceiveSmoke",1UL,refresh);Call("SmokeTick");Check(ps.enabled,"new refresh resumes");
  syncScript.s.players.Clear();Call("SmokeTick");Check(effects.Count==0,"gone player effect removed");
  syncScript.s.players.Add(id,player);Call("ReceiveSmoke",1UL,old);Call("SmokeTick");Check(effects.Count==1,"reappearance retains current state");effect=effects[5UL];ps=(ParticleSystem)effect.GetType().GetField("Particle",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(effect);Check(ps.enabled,"stale stop rejected after disappearance");
  // Urine stream. The local cap's own emission drives the outgoing packet; remote replicas mirror it.
  Call("SmokeReset");
  var pissPs=new ParticleSystem();pissPs.gameObject=new GameObject("piss");pissPs.isPlaying=true;pissPs.enabled=true;pissPs.rate=1f;
  var pissSound=new AudioSource();pissSound.clip=new AudioClip();
  mainscript.s.player.piss=new tankcapscript{ps=pissPs,SSource=pissSound,maxRate=1,maxVolume=.5f};
  player.mpscript.THead=new Transform();
  var sender=new StableNode();Set("node",sender);Set("Now",10d);Call("SmokeTick");
  ulong so,ss;uint sq;byte si,su;
  Check(SmokeCodec.Read(sender.Last,out so,out ss,out sq,out si,out su) && so==2 && si==15 && su==15,"sender publishes smoke and urine intensity");
  Check((bool)Call("ReceiveSmoke",1UL,SmokeCodec.Encode(5,99,20,0,12)),"urine packet intercepted");Call("SmokeTick");
  var urineEffects=(System.Collections.IDictionary)bridge.GetField("urineEffects",flags).GetValue(null);
  Check(urineEffects.Count==1,"urine replica created");
  object ue=urineEffects[5UL];
  var uPs=(ParticleSystem)ue.GetType().GetField("Particle",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(ue);
  var uSound=(AudioSource)ue.GetType().GetField("Sound",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(ue);
  var uObject=(GameObject)ue.GetType().GetField("Object",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(ue);
  Check(uPs.enabled && Math.Abs(uPs.rate.constantMax-12f/15f)<1e-4f && uPs.isPlaying,"urine stream emits at intensity");
  Check(uSound.isPlaying && uSound.loop && Math.Abs(uSound.volume-12f/15f*.5f)<1e-4f,"urine pour loop plays at intensity");
  Check(((System.Collections.IDictionary)bridge.GetField("smokeEffects",flags).GetValue(null)).Count==0,"urine packet alone creates no smoke replica");
  Call("ReceiveSmoke",1UL,SmokeCodec.Encode(5,99,21,0,0));Call("SmokeTick");
  Check(!uPs.enabled && !uSound.isPlaying && !uObject.destroyed && urineEffects.Count==1,"urine stop stops stream and sound without destroying the replica");
  Call("ReceiveSmoke",1UL,SmokeCodec.Encode(5,99,22,0,9));Set("Now",10.1d);Call("SmokeTick");Check(uPs.enabled && uSound.isPlaying,"urine resumes");
  Set("Now",12.2d);Call("SmokeTick");Check(!uPs.enabled && !uSound.isPlaying && !uObject.destroyed,"missing urine refresh expires");
  Call("ReceiveSmoke",1UL,SmokeCodec.Encode(5,99,23,0,6));Set("Now",12.3d);Call("SmokeTick");Check(uPs.enabled,"urine resumes after expiry");
  Call("SmokePeerLeft",5UL);Check(uObject.destroyed && urineEffects.Count==0,"urine replica destroyed on peer leave");
  Call("ReceiveSmoke",1UL,SmokeCodec.Encode(5,99,24,0,6));Call("SmokeTick");Check(urineEffects.Count==1,"urine replica recreated");
  syncScript.s.players.Clear();Call("SmokeTick");Check(urineEffects.Count==0,"urine replica removed when player gone");
  syncScript.s.players.Add(id,player);Call("ReceiveSmoke",1UL,SmokeCodec.Encode(5,99,25,0,6));Call("SmokeTick");Check(urineEffects.Count==1,"urine replica back");
  Call("SmokeReset");Check(urineEffects.Count==0,"reset clears urine replicas");
  // Host relay carries the urine intensity to the other peers.
  var host=new StableNode{IsHost=true};host.Peers.Add(1,new StablePeer{Ready=true,Session=55});host.Peers.Add(8,new StablePeer{Ready=true,Session=66});
  Set("node",host);Call("SmokeReset");Set("Now",20d);
  Check((bool)Call("ReceiveSmoke",1UL,SmokeCodec.Encode(1,55,30,0,4)),"host intercepts urine");
  ulong ro,rs;uint rq;byte ri,ru;
  Check(host.LastRelay!=null && SmokeCodec.Read(host.LastRelay,out ro,out rs,out rq,out ri,out ru) && ro==1 && ru==4 && rs==88,"host relays urine with its own session");
  // Single player and invalid packets stay inert.
  Set("node",null);Call("SmokeTick");Check((bool)Call("ReceiveSmoke",1UL,SmokeCodec.Encode(5,99,40,0,3))==true,"no node is a no-op");
  Call("SmokeReset");Console.WriteLine("Smoke runtime stubs: "+count+" checks passed");
 }
}

