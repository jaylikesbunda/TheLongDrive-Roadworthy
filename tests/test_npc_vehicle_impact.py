import os, subprocess, tempfile, unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
FIXTURE=r''' 
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public struct Vector3 {public float x,y,z;public static Vector3 operator*(Vector3 v,float s){return new Vector3{x=v.x*s,y=v.y*s,z=v.z*s};} public float magnitude {get{return (float)Math.Sqrt(x*x+y*y+z*z);}}}
 public class GameObject {
  public GameObject parent;public readonly List<Component> components=new List<Component>();
  public T GetComponent<T>() where T:class {foreach(var c in components)if(c is T)return c as T;return null;}
  public T AddComponent<T>() where T:Component,new(){var c=new T();c.gameObject=this;components.Add(c);return c;}
 }
 public class Component {static int next;int id=++next;public GameObject gameObject;public int GetInstanceID(){return id;} public virtual T GetComponent<T>() where T:class{return gameObject==null?null:gameObject.GetComponent<T>();}public virtual T GetComponentInParent<T>() where T:class {for(var g=gameObject;g!=null;g=g.parent){var c=g.GetComponent<T>();if(c!=null)return c;}return null;}}
 public class MonoBehaviour:Component {public bool enabled=true;}
 public class Rigidbody:Component {public float mass;public bool car;public bool isKinematic;public override T GetComponentInParent<T>() {return car?new carscript() as T:base.GetComponentInParent<T>();}}
 public class Collider:Component {public bool isTrigger;public Rigidbody attachedRigidbody;}
 public class Collision {public Rigidbody rigidbody;public Vector3 relativeVelocity;public int contactCount=1;}
 public static class Time {public static float fixedTime,unscaledTime;}
 public static class Debug {public static void Log(string s){}}
}
public class carscript:UnityEngine.Component {}
public class nyulscript:UnityEngine.Component {}
public static class syncScript {public static bool authority=true;public static bool IsHostOrSingle(){return authority;}}
public class ragdollactivatescript:UnityEngine.MonoBehaviour {public UnityEngine.Rigidbody RB;public UnityEngine.Collider[] animColliders;public bool ragdoll;}
public class breakablescript:UnityEngine.MonoBehaviour {
 public object AI,newAI;public UnityEngine.Rigidbody rb;public bool destroyed,noCollider,onlyDecreaseAfterAttackIfAbove=true;public float onlyDecreaseAfterAttackIfAboveHealth=8,health=10;public int nativeCalls;public float damage;
 public override T GetComponent<T>() {return typeof(T)==typeof(UnityEngine.Rigidbody)?rb as T:base.GetComponent<T>();}
 private void OnCollisionEnter(UnityEngine.Collision c){if(!TldMp.NpcVehicleImpact.AdmitRabbitCollision(this,c)||noCollider||c.contactCount==0)return;nativeCalls++;float force=TldMp.NpcVehicleImpact.RelativeVelocity(c,this).magnitude;if(onlyDecreaseAfterAttackIfAbove){if(force<onlyDecreaseAfterAttackIfAboveHealth)return;force-=onlyDecreaseAfterAttackIfAboveHealth;}damage+=force;if(force>=health){health=0;destroyed=true;var rag=GetComponent<ragdollactivatescript>();if(rag!=null){rag.ragdoll=true;TldMp.NpcVehicleImpact.ConfigureRabbit(rag);}}else health-=force;}
 public void RootHit(UnityEngine.Collision c){OnCollisionEnter(c);}
}
public class Test {
 static int n;static void Check(bool v){n++;if(!v)throw new Exception("check "+n);}
 static float V(UnityEngine.Collision c,breakablescript v){return TldMp.NpcVehicleImpact.RelativeVelocity(c,v).x;}
 static void Hit(TldMp.RabbitVehicleImpactReceiver receiver,UnityEngine.Collision c){typeof(TldMp.RabbitVehicleImpactReceiver).GetMethod("OnCollisionEnter",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(receiver,new object[]{c});}
 public static void Main(){
 var v=new breakablescript{newAI=new object(),rb=new UnityEngine.Rigidbody{mass=70}};var c=new UnityEngine.Collision{rigidbody=new UnityEngine.Rigidbody{mass=1120,car=true},relativeVelocity=new UnityEngine.Vector3{x=15}};
 Check(V(c,v)==60);Check(V(c,v)-8>=50);c.relativeVelocity=new UnityEngine.Vector3{x=1};Check(V(c,v)<8);c.relativeVelocity=new UnityEngine.Vector3{x=10};c.rigidbody.mass=280;Check(V(c,v)==20);c.rigidbody.mass=7;Check(V(c,v)==10);c.rigidbody.car=false;Check(V(c,v)==10);c.rigidbody.car=true;v.newAI=null;Check(V(c,v)==10);v.AI=new object();v.rb.mass=12;c.rigidbody.mass=410;Check(V(c,v)==40);v.rb.mass=0;Check(V(c,v)==10);v.rb.mass=float.NaN;Check(V(c,v)==10);v.rb=null;Check(V(c,v)==10);Check(V(c,null)==10);
 var root=new UnityEngine.GameObject();root.AddComponent<nyulscript>();v=root.AddComponent<breakablescript>();v.AI=new object();v.rb=root.AddComponent<UnityEngine.Rigidbody>();v.rb.mass=10;
 var rag=root.AddComponent<ragdollactivatescript>();rag.RB=v.rb;
 var rootCol=root.AddComponent<UnityEngine.Collider>();rootCol.attachedRigidbody=v.rb;
 var bone=new UnityEngine.GameObject();bone.parent=root;var body=bone.AddComponent<UnityEngine.Rigidbody>();body.mass=1;body.isKinematic=true;
 var boneCol=bone.AddComponent<UnityEngine.Collider>();boneCol.attachedRigidbody=body;
 var trigger=new UnityEngine.GameObject();var triggerCol=trigger.AddComponent<UnityEngine.Collider>();triggerCol.isTrigger=true;triggerCol.attachedRigidbody=trigger.AddComponent<UnityEngine.Rigidbody>();
 rag.animColliders=new[]{rootCol,boneCol,boneCol,triggerCol};TldMp.NpcVehicleImpact.ConfigureRabbit(rag);
 var receiver=bone.GetComponent<TldMp.RabbitVehicleImpactReceiver>();Check(receiver!=null);Check(root.GetComponent<TldMp.RabbitVehicleImpactReceiver>()==null);Check(trigger.GetComponent<TldMp.RabbitVehicleImpactReceiver>()==null);
 int components=bone.components.Count;TldMp.NpcVehicleImpact.ConfigureRabbit(rag);Check(components==bone.components.Count);
 c.rigidbody.mass=1000;c.relativeVelocity=new UnityEngine.Vector3{x=20};Hit(receiver,c);Check(v.destroyed&&rag.ragdoll&&v.nativeCalls==1);Check(!receiver.enabled);Check(body.isKinematic);Hit(receiver,c);Check(v.nativeCalls==1);
 // Reset an alive rabbit; repeated root/bone events are one impact per car/step.
 v.destroyed=false;v.health=100;v.damage=0;rag.ragdoll=false;TldMp.NpcVehicleImpact.ConfigureRabbit(rag);UnityEngine.Time.fixedTime=1;c.relativeVelocity=new UnityEngine.Vector3{x=3};Hit(receiver,c);Check(v.damage==4);v.RootHit(c);Hit(receiver,c);Check(v.damage==4);
 UnityEngine.Time.fixedTime=2;v.RootHit(c);Hit(receiver,c);Check(v.damage==8);
 // A harmless root contact cannot suppress a stronger bone hit in this step.
 UnityEngine.Time.fixedTime=3;c.relativeVelocity=new UnityEngine.Vector3{x=1};v.RootHit(c);c.relativeVelocity=new UnityEngine.Vector3{x=3};Hit(receiver,c);Check(v.damage==12);
 syncScript.authority=false;UnityEngine.Time.fixedTime=4;Hit(receiver,c);Check(v.damage==12);syncScript.authority=true;Hit(receiver,c);Check(v.damage==16);
 var second=new UnityEngine.Rigidbody{mass=1000,car=true};c.rigidbody=second;Hit(receiver,c);Check(v.damage==20);
 c.contactCount=0;UnityEngine.Time.fixedTime=5;Hit(receiver,c);Check(v.damage==20);c.contactCount=1;c.rigidbody.car=false;Hit(receiver,c);Check(v.damage==20);c.rigidbody.car=true;receiver.enabled=false;Hit(receiver,c);Check(v.damage==20);receiver.enabled=true;v.noCollider=true;Hit(receiver,c);Check(v.damage==20);v.noCollider=false;Hit(receiver,c);Check(v.damage==24);
 rag.ragdoll=true;TldMp.NpcVehicleImpact.ConfigureRabbit(rag);Check(!receiver.enabled);UnityEngine.Time.fixedTime=6;Hit(receiver,c);Check(v.damage==24);
 // Non-rabbit ragdolls are untouched.
 var other=new UnityEngine.GameObject();var otherRag=other.AddComponent<ragdollactivatescript>();other.AddComponent<breakablescript>();otherRag.RB=other.AddComponent<UnityEngine.Rigidbody>();otherRag.animColliders=new[]{boneCol};TldMp.NpcVehicleImpact.ConfigureRabbit(otherRag);Check(other.GetComponent<TldMp.RabbitVehicleImpactState>()==null);
 Console.WriteLine("PASS "+n+" NPC/rabbit vehicle impact checks");
 }
}
'''
class NpcVehicleImpactTests(unittest.TestCase):
 def test_production_vehicle_damage(self):
  with tempfile.TemporaryDirectory() as directory:
   p=Path(directory);fixture=p/'test.cs';fixture.write_text(FIXTURE);exe=p/'test.exe';csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
   subprocess.run([str(csc),'/nologo','/out:'+str(exe),str(fixture),str(ROOT/'runtime/NpcVehicleImpact.cs')],check=True)
   subprocess.run([str(exe)],check=True)
if __name__=='__main__': unittest.main()
