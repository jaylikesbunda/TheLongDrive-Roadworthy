using System;
using System.Collections.Generic;
using TldMp;
using UnityEngine;
namespace UnityEngine
{
 public class Object {}
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero{get{return new Vector3(0,0,0);}}public float magnitude{get{return (float)Math.Sqrt(x*x+y*y+z*z);}}public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}}
 public struct Quaternion{}
 public class Transform {public Vector3 position,lossyScale=new Vector3(1,1,1);public Quaternion rotation;}
 public class GameObject {public bool activeInHierarchy=true;public int layer;}
 public struct Bounds{public Vector3 center;}
 public class Collider:Object {public bool enabled=true,isTrigger;public GameObject gameObject=new GameObject();public Transform transform=new Transform();public Bounds bounds;public object attachedRigidbody;public bool survival,overlap;public Vector3 ClosestPoint(Vector3 p){return bounds.center;}public T GetComponentInParent<T>() where T:class{return survival?new survivalscript() as T:null;}}
 public class SphereCollider:Collider{public float radius=.25f;}
 public static class Mathf{public static float Max(float a,float b){return Math.Max(a,b);}public static float Abs(float x){return Math.Abs(x);}}
 public static class Physics {public static int checks;public static bool GetIgnoreLayerCollision(int a,int b){return false;}public static bool GetIgnoreCollision(Collider a,Collider b){return false;}public static bool ComputePenetration(Collider a,Vector3 ap,Quaternion ar,Collider b,Vector3 bp,Quaternion br,out Vector3 dir,out float depth){checks++;dir=new Vector3();depth=0;return b.overlap;}}
 public static class Debug {public static List<string> logs=new List<string>();public static void Log(string s){logs.Add(s);}public static void LogWarning(string s){throw new Exception(s);}}
}
public class survivalscript{}
public class fpscontroller {public bool loadedmove=true;public Collider[] C;public Collider CBody;}
public class playerdeathtriggerscript {public Collider selfCol;}
public class sensoryscript {public bool alive=true;}
public class aiscript {public float flightFight,distFromTarget;}
public class movescript{public Rigidbody RB;} public class Rigidbody{public Vector3 velocity;} public class breakablescript{public float health;} public class nyulscript {public bool attacking;public float size=1;public aiscript AI=new aiscript();public sensoryscript senses=new sensoryscript();public playerdeathtriggerscript attackTriggerScript;public movescript move;public breakablescript breakable;}
public class tosaveitemscript {public uint idInSave;public nyulscript root;}
public class savedatascript {public Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();}
public class mpplayerscript {public Collider[] cols;public Collider CBody;}
namespace TldMp
{
 internal class StablePeer {}
 internal class StableNode {public bool IsHost=true,Compatible=true;public string Error;public Dictionary<ulong,StablePeer> Peers=new Dictionary<ulong,StablePeer>();}
 internal static class RabbitReplicas {internal static nyulscript RabbitRoot(tosaveitemscript i){return i.root;}}
 internal static class RabbitPlayers {internal class Actor{internal ulong Id;internal mpplayerscript Mp;}internal static Dictionary<ulong,Actor> actors=new Dictionary<ulong,Actor>();internal static bool TryGet(ulong id,out Actor a){return actors.TryGetValue(id,out a);}}
}
internal static class RabbitAttackDiagnosticsTests
{
 static int checks;static void Check(bool x,string message){checks++;if(!x)throw new Exception(message);}
 static void Main()
 {
  var trigger=new SphereCollider{isTrigger=true};var rabbit=new nyulscript{attacking=true,attackTriggerScript=new playerdeathtriggerscript{selfCol=trigger}};
  var save=new savedatascript();save.items[4]=new tosaveitemscript{idInSave=4,root=rabbit};
  var local=new Collider{overlap=true,survival=true,bounds=new Bounds{center=new Vector3(1,0,0)}};var player=new fpscontroller{C=new[]{local},CBody=local};
  var guestCol=new Collider{bounds=new Bounds{center=new Vector3(3,0,0)}};var node=new StableNode();node.Peers[7]=new StablePeer();RabbitPlayers.actors[7]=new RabbitPlayers.Actor{Id=7,Mp=new mpplayerscript{cols=new[]{guestCol}}};
  RabbitAttackDiagnostics.Reset();RabbitAttackDiagnostics.Tick(false,node,save,player,1);Check(Debug.logs.Count==0&&Physics.checks==0,"disabled is inert");
  RabbitAttackDiagnostics.Tick(true,node,save,player,1);Check(Debug.logs.Count==1&&Physics.checks==2,"tests only known actor colliders");
  Check(Debug.logs[0].Contains("overlapping=1")&&Debug.logs[0].Contains("closest=local")&&Debug.logs[0].Contains("survivalColliders=1")&&Debug.logs[0].Contains("overlapActor=local")&&Debug.logs[0].Contains("overlapNoRB=1"),"reported overlap and actor");
  RabbitAttackDiagnostics.Tick(true,node,save,player,1.1);Check(Debug.logs.Count==1,"per rabbit throttled");
  trigger.gameObject.activeInHierarchy=false;RabbitAttackDiagnostics.Tick(true,node,save,player,1.6);Check(Debug.logs.Count==2&&Physics.checks==2&&Debug.logs[1].Contains("triggerActive=False"),"inactive trigger never queried");
  trigger.gameObject.activeInHierarchy=true;node.IsHost=false;RabbitAttackDiagnostics.Tick(true,node,save,player,2);Check(Debug.logs.Count==2,"nonhost inert");
  node.IsHost=true;rabbit.attacking=false;RabbitAttackDiagnostics.Tick(true,node,save,player,3);Check(Debug.logs.Count==2,"no inactive attack probes");
  rabbit.attacking=true;RabbitAttackDiagnostics.Tick(true,node,save,player,4);Check(Debug.logs.Count==3,"reset allows new session report");
  Console.WriteLine("Rabbit attack diagnostics: "+checks+" checks passed");
 }
}
