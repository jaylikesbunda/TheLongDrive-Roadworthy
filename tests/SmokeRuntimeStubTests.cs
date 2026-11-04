using System;
using System.Collections.Generic;
using System.Reflection;
using TldMp;
using UnityEngine;
using Steamworks;
namespace UnityEngine {
 public class Object {public bool destroyed;public static void Destroy(Object x){if(x!=null)x.destroyed=true;}}
 public class Transform:Object {public Vector3 localPosition;public void SetParent(Transform p,bool world){}}
 public struct Vector3 {public Vector3(float x,float y,float z){}}
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
public class fpscontroller {public ParticleSystem smokeParticle;public float maxSmokeParticleRate;}
public class mainscript {public static mainscript s;public fpscontroller player;public Transform TCustomSpaceMain;}
public class mpplayerscript {public Transform lipT,THead;}
public class syncScript {public class playerSyncClass {public GameObject player;public mpplayerscript mpscript;}public static syncScript s;public Dictionary<CSteamID,playerSyncClass> players=new Dictionary<CSteamID,playerSyncClass>();}
namespace TldMp {
 internal sealed class StablePeer {public bool Ready;public ulong Session;}
 internal sealed class StableNode {public bool Compatible=true,Ready=true,IsHost;public string Error;public ulong Host=1,HostSession=99,Session=88;public Dictionary<ulong,StablePeer> Peers=new Dictionary<ulong,StablePeer>();public bool PublishNative(byte[] b){return true;}public void PublishNativeTo(ulong p,byte[] b){}}
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
  syncScript.s.players.Add(id,player);Call("ReceiveSmoke",1UL,old);Call("SmokeTick");Check(effects.Count==1,"reappearance retains current state");effect=effects[5UL];ps=(ParticleSystem)effect.GetType().GetField("Particle",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(effect);Check(ps.enabled,"stale stop rejected after disappearance");Call("SmokeReset");Console.WriteLine("Smoke runtime stubs: "+count+" checks passed");
 }
}

