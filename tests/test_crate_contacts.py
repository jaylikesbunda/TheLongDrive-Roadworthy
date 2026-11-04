"""Execute production contact queue against a counted collision boundary."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

class CrateContacts(unittest.TestCase):
    def test_queue(self):
        source=(Path(__file__).resolve().parents[1]/'runtime/StableGameBridge.cs').read_text()
        start=source.index('        // Only sibling colliders')
        end=source.index('        // The native slicer only reads',start)
        fixture=r'''
using System;using System.Collections.Generic;using System.Diagnostics;
struct Vector3{} struct Quaternion{}
struct Bounds{public bool Intersects(Bounds b){return true;}}
class Transform{public Vector3 position;public Quaternion rotation;}
class GameObject{public string name="crateGib(Clone)";public bool activeInHierarchy=true;public List<Collider> Children=new List<Collider>();public void GetComponentsInChildren<T>(bool active,List<T> result) where T:class{foreach(var c in Children)result.Add(c as T);}}
class Collider{static int next;public int Id=++next;public bool enabled=true,isTrigger;public Rigidbody attachedRigidbody;public GameObject gameObject=new GameObject();public Transform transform=new Transform();public Bounds bounds;public bool Overlap=true;}
class BoxCollider:Collider{}
class Rigidbody{public tosaveitemscript Item=new tosaveitemscript();public T GetComponent<T>() where T:class{return Item as T;}}
class tosaveitemscript{public int id=280;public object car,attachable,attachgrip;}
class Time{public static float fixedTime,fixedDeltaTime=.02f;}
class Debug{public static void Log(object x){}public static void LogWarning(object x){}}
class ModDiagnostics{public static bool On=true;}
class Physics{
 public static HashSet<string> Ignored=new HashSet<string>();public static int Restores,Checks,OverlapRestores;public static bool Fail;
 static string Key(Collider a,Collider b){return Math.Min(a.Id,b.Id)+"/"+Math.Max(a.Id,b.Id);}
 public static bool GetIgnoreCollision(Collider a,Collider b){return Ignored.Contains(Key(a,b));}
 public static void IgnoreCollision(Collider a,Collider b,bool ignore){if(Fail)throw new InvalidOperationException();if(ignore)Ignored.Add(Key(a,b));else{Ignored.Remove(Key(a,b));Restores++;if(a.Overlap && b.Overlap)OverlapRestores++;}}
 public static bool ComputePenetration(Collider a,Vector3 p,Quaternion q,Collider b,Vector3 r,Quaternion s,out Vector3 d,out float distance){Checks++;d=new Vector3();distance=1;return a.Overlap && b.Overlap;}
}
class Probe{
HELPERS
 static int assertions;static void Check(bool b){assertions++;if(!b)throw new Exception("assertion "+assertions);}
 static GameObject Group(int n){var g=new GameObject();for(int i=0;i<n;i++)g.Children.Add(new BoxCollider{attachedRigidbody=new Rigidbody()});return g;}
 static void Step(){Time.fixedTime+=.02f;int prior=Physics.Restores,checks=Physics.Checks,overlaps=Physics.OverlapRestores;TickCrateContacts();Check(Physics.OverlapRestores-overlaps<=8);Check(Physics.Restores-prior<=128);Check(Physics.Checks-checks<=256);}
 static void Main(){
  var g=Group(58);PrepareCrateDebris(g);Check(crateContacts.Count==1653);Check(Physics.Ignored.Count==1653);
  Step();Check(Physics.Restores==0);Step();Check(Physics.Restores==0);
  for(int i=0;i<3;i++){Step();Check(Physics.Restores==0);}
  foreach(var c in g.Children)c.Overlap=false;
  for(int i=0;i<60;i++)Step();Check(crateContacts.Count==0 && Physics.Ignored.Count==0);
  // All-overlapping fallback cannot restore an unbounded contact burst.
  g=Group(58);PrepareCrateDebris(g);for(int i=0;i<45;i++)Step();Check(Physics.Ignored.Count==1653);
  for(int i=0;i<260;i++)Step();Check(crateContacts.Count==0 && Physics.Ignored.Count==0);
  // Preserve pre-existing ignores and external contacts.
  g=Group(3);Physics.IgnoreCollision(g.Children[0],g.Children[1],true);var external=new BoxCollider();
  PrepareCrateDebris(g);Check(crateContacts.Count==2);Check(!Physics.GetIgnoreCollision(g.Children[0],external));ClearCrateContacts();Check(Physics.GetIgnoreCollision(g.Children[0],g.Children[1]));Check(crateContacts.Count==0);Physics.Ignored.Clear();
  // Reject unaudited layouts before modifying anything.
  foreach(int role in new[]{0,1,2,3,4}){g=Group(3);if(role==0)g.name="CactiGibPrefab(Clone)";if(role==1)g.Children[2].attachedRigidbody.Item.id=69;if(role==2)g.Children[2].attachedRigidbody.Item.car=new object();if(role==3)g.Children[2]=new Collider{attachedRigidbody=new Rigidbody()};if(role==4)g.Children[2].attachedRigidbody=null;PrepareCrateDebris(g);Check(crateContacts.Count==0);}
  g=Group(65);PrepareCrateDebris(g);Check(crateContacts.Count==0);
  // Simultaneous breaks share a global work/capacity bound.
  for(int i=0;i<10;i++)PrepareCrateDebris(Group(58));Check(crateContacts.Count==6612);
  for(int i=0;i<1000;i++)Step();Check(crateContacts.Count==0 && Physics.Ignored.Count==0);
  // Failed native setup remains tracked for restoration; dead colliders are released.
  Physics.Fail=true;PrepareCrateDebris(Group(2));Check(crateContacts.Count==1);Physics.Fail=false;ClearCrateContacts();Check(crateContacts.Count==0);
  g=Group(2);PrepareCrateDebris(g);var pair=crateContacts[0];pair.A=null;crateContacts[0]=pair;Step();Check(crateContacts.Count==0);Physics.Ignored.Clear();
  // Disabled/pooled bodies do not retain an owned ignore.
  g=Group(2);PrepareCrateDebris(g);g.Children[0].enabled=false;Step();Step();Step();Check(crateContacts.Count==0);
  Console.WriteLine("PASS "+assertions+" production crate-contact checks; simultaneous batches, separation, timeout, restoration budgets, layout scope, cleanup and failures.");
 }
}
'''.replace('HELPERS',source[start:end])
        with tempfile.TemporaryDirectory(prefix='tld-crate-contacts-') as directory:
            code=Path(directory)/'probe.cs';exe=Path(directory)/'probe.exe'
            code.write_text(fixture)
            compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            subprocess.run([str(compiler),'/nologo','/out:'+str(exe),str(code)],check=True)
            result=subprocess.run([str(exe)],check=True,capture_output=True,text=True)
            print(result.stdout)

if __name__=='__main__':unittest.main()
