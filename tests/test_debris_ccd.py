"""Production comparison hooks executed with counted physics boundaries."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


class DebrisCcd(unittest.TestCase):
    def test_modes_and_scope(self):
        root = Path(__file__).resolve().parents[1]
        source = (root / 'runtime/StableGameBridge.cs').read_text()
        start = source.index('        // Captured once per process')
        end = source.index('        static double debrisLogWindow;', start)
        methods = source[start:end]
        fixture = r'''
using System;using System.Collections.Generic;using System.IO;
enum CollisionDetectionMode{Discrete,Continuous,ContinuousDynamic,ContinuousSpeculative}
struct Vector3{}struct Quaternion{}
class Rigidbody{
 CollisionDetectionMode mode=CollisionDetectionMode.ContinuousDynamic;
 public int Writes;public bool Fail;public int solverIterations=20,solverVelocityIterations=20;
 public CollisionDetectionMode collisionDetectionMode{get{return mode;}set{if(Fail)throw new InvalidOperationException();mode=value;Writes++;}}
}
class tosaveitemscript{
 public int id;public object car,attachable,attachgrip;public Rigidbody RB;
 public T GetComponent<T>() where T:class{return RB as T;}
}
class GameObject{
 public List<tosaveitemscript> Children=new List<tosaveitemscript>();
 public void GetComponentsInChildren<T>(bool all,List<T> target) where T:class{foreach(var x in Children)target.Add(x as T);}
}
namespace UnityEngine{class Object{public static int Calls;public static GameObject Instantiate(GameObject prefab,Vector3 p,Quaternion q){Calls++;if(prefab==null)throw new ArgumentNullException();return prefab;}}}
class Debug{public static void Log(object x){}public static void LogWarning(object x){}}
class StableGameBridge{
METHODS
 static class DebrisContactFilter{internal static void Register(Rigidbody body){}}
 static void PrepareCrateDebris(GameObject group){}
 static int checks;static void Check(bool x){checks++;if(!x)throw new Exception("assertion "+checks);}
 static void Main(string[] args){
  bool enabled=args.Length>0;Check(debrisSpeculative==enabled);
  for(int id=0;id<1000;id++){
   var body=new Rigidbody();var item=new tosaveitemscript{id=id,RB=body};DebrisItemInitialized(item);
   Check((body.collisionDetectionMode==CollisionDetectionMode.ContinuousSpeculative)==(enabled && (id==69 || id>=280 && id<=291)));
   DebrisItemInitialized(item);Check(body.Writes<2);Check((body.solverIterations==8 && body.solverVelocityIterations==2)==(id==67 || id==68 || id==69 || id>=203 && id<=210 || id>=280 && id<=291 || id>=322 && id<=332));
  }
  foreach(int role in new[]{0,1,2}){
   var body=new Rigidbody();var item=new tosaveitemscript{id=69,RB=body};
   if(role==0)item.car=new object();else if(role==1)item.attachable=new object();else item.attachgrip=new object();
   DebrisItemInitialized(item);Check(body.Writes==0);Check(body.solverIterations==20 && body.solverVelocityIterations==20);
  }
  var other=new Rigidbody();other.collisionDetectionMode=CollisionDetectionMode.Continuous;DebrisItemInitialized(new tosaveitemscript{id=69,RB=other});Check(other.Writes==1);
  DebrisItemInitialized(null);DebrisItemInitialized(new tosaveitemscript{id=69});
  var bad=new Rigidbody{Fail=true};DebrisItemInitialized(new tosaveitemscript{id=69,RB=bad});Check(bad.Writes==0);
  var prefab=new GameObject();var child=new tosaveitemscript{id=280,RB=new Rigidbody()};var car=new tosaveitemscript{id=280,car=new object(),RB=new Rigidbody()};prefab.Children.Add(child);prefab.Children.Add(car);
  Check(Object.ReferenceEquals(prefab,CreateBreakDebris(prefab,new Vector3(),new Quaternion())));Check(car.RB.Writes==0);Check(child.RB.Writes==(enabled?1:0));
  bool threw=false;try{CreateBreakDebris(null,new Vector3(),new Quaternion());}catch(ArgumentNullException){threw=true;}Check(threw);
  var slice=new Rigidbody();SetSliceCollisionMode(slice,CollisionDetectionMode.ContinuousDynamic);Check(slice.collisionDetectionMode==(enabled?CollisionDetectionMode.ContinuousSpeculative:CollisionDetectionMode.ContinuousDynamic));
  SetSliceCollisionMode(slice,CollisionDetectionMode.Discrete);Check(slice.collisionDetectionMode==CollisionDetectionMode.Discrete);
  Console.WriteLine("PASS "+checks+" production debris CCD assertions; mode="+(enabled?"speculative":"native")+"; exact ID scope, vehicle exclusions, idempotence, requested modes, failure and native return semantics.");
 }
}
'''.replace('METHODS', methods)
        with tempfile.TemporaryDirectory(prefix='tld-debris-ccd-') as directory:
            game = Path(directory) / 'Game'
            managed = game / 'TheLongDrive_Data/Managed'
            managed.mkdir(parents=True)
            code, exe = managed / 'probe.cs', managed / 'probe.exe'
            code.write_text(fixture)
            compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            subprocess.run([str(compiler), '/nologo', '/out:' + str(exe), str(code)], check=True)
            for mode in ('native', 'speculative'):
                if mode == 'speculative':
                    (game / 'tldmp-debris-speculative.on').touch()
                result = subprocess.run([str(exe)] + ([] if mode == 'native' else ['speculative']),
                                        capture_output=True, text=True, check=True)
                print(result.stdout)


if __name__ == '__main__':
    unittest.main()
