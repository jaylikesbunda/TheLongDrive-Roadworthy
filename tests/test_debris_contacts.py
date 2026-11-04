"""Replay production contact filtering and lifecycle without invoking PhysX."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

class DebrisContacts(unittest.TestCase):
 def test_filter(self):
  source=(Path(__file__).resolve().parents[1]/'runtime/StableGameBridge.cs').read_text()
  code=source[source.index('        static class DebrisContactFilter'):source.index('        // End debris contact filter.')]
  fixture=r'''
using System;using System.Collections.Generic;using System.Diagnostics;
class GameObject{public bool activeInHierarchy=true;}
class Rigidbody{public bool isKinematic;public List<Collider> Children=new List<Collider>();public void GetComponentsInChildren<T>(bool all,List<T> target)where T:class{foreach(var c in Children)target.Add(c as T);}}
class Collider{static int next;public int Id=++next;public bool isTrigger,enabled=true;public GameObject gameObject=new GameObject();public Rigidbody attachedRigidbody;}
class Time{public static float unscaledTime;public static int frameCount;}
class ModDiagnostics{public static bool On;}
class Debug{public static void Log(object x){}public static void LogWarning(object x){}}
class Physics{public static int Writes;static HashSet<string> ignored=new HashSet<string>();static string Key(Collider a,Collider b){return Math.Min(a.Id,b.Id)+"/"+Math.Max(a.Id,b.Id);}public static bool GetIgnoreCollision(Collider a,Collider b){return ignored.Contains(Key(a,b));}public static void IgnoreCollision(Collider a,Collider b,bool value){Writes++;if(value)ignored.Add(Key(a,b));else ignored.Remove(Key(a,b));}}
class Probe{
HELPERS
static int checks;static void Check(bool x){checks++;if(!x)throw new Exception("assertion "+checks);}
static Collider Add(Rigidbody b){var c=new Collider{attachedRigidbody=b};b.Children.Add(c);return c;}
static void Step(){Time.frameCount++;DebrisContactFilter.Tick();}
static void Drain(){for(int i=0;i<100;i++)Step();}
static void Main(){
 var a=new Rigidbody();var b=new Rigidbody();var ca=Add(a);var cb=Add(b);var external=Add(new Rigidbody());var trigger=Add(a);trigger.isTrigger=true;
 DebrisContactFilter.Register(a);DebrisContactFilter.Register(a);DebrisContactFilter.Register(b);Drain();Check(Physics.GetIgnoreCollision(ca,cb));Check(!Physics.GetIgnoreCollision(ca,external));Check(!Physics.GetIgnoreCollision(trigger,cb));
 int writes=Physics.Writes;Drain();Check(Physics.Writes==writes);
 b.isKinematic=true;Drain();Check(!Physics.GetIgnoreCollision(ca,cb));b.isKinematic=false;Drain();Check(Physics.GetIgnoreCollision(ca,cb));
 cb.enabled=false;Drain();Check(!Physics.GetIgnoreCollision(ca,cb));cb.enabled=true;Drain();Check(Physics.GetIgnoreCollision(ca,cb));
 cb.attachedRigidbody=new Rigidbody();Drain();Check(!Physics.GetIgnoreCollision(ca,cb));cb.attachedRigidbody=b;Drain();Check(Physics.GetIgnoreCollision(ca,cb));
 DebrisContactFilter.Clear();Check(!Physics.GetIgnoreCollision(ca,cb));
 Physics.IgnoreCollision(ca,cb,true);DebrisContactFilter.Register(a);DebrisContactFilter.Register(b);Drain();DebrisContactFilter.Clear();Check(Physics.GetIgnoreCollision(ca,cb));Physics.IgnoreCollision(ca,cb,false);
 var bodies=new List<Rigidbody>();var cols=new List<Collider>();for(int i=0;i<300;i++){var body=new Rigidbody();cols.Add(Add(body));bodies.Add(body);DebrisContactFilter.Register(body);}
 for(int i=0;i<100;i++){int before=Physics.Writes;Step();Check(Physics.Writes-before<=2048);for(int j=0;j<20;j++)DebrisContactFilter.Tick();Check(Physics.Writes-before<=2048);}
 Check(Physics.GetIgnoreCollision(cols[0],cols[255]));Check(!Physics.GetIgnoreCollision(cols[0],cols[256]));Check(!Physics.GetIgnoreCollision(cols[0],external));DebrisContactFilter.Clear();Check(!Physics.GetIgnoreCollision(cols[0],cols[255]));
 Console.WriteLine("PASS "+checks+" production early debris pair checks: external contacts, triggers, native ignore ownership, disable, kinematic, reparent, cleanup, saturation and per-frame catch-up budget.");
}}
'''.replace('HELPERS',code)
  with tempfile.TemporaryDirectory(prefix='tld-debris-contacts-') as directory:
   cs=Path(directory)/'probe.cs';exe=Path(directory)/'probe.exe';cs.write_text(fixture)
   subprocess.run([str(Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),'/nologo','/out:'+str(exe),str(cs)],check=True)
   subprocess.run([str(exe)],check=True)
