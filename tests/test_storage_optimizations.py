"""Execute the production storage query/membership helpers against a physics boundary."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


def method(source, name):
    start = source.index(name)
    start = source.rfind('\n', 0, start) + 1
    body = source.index('{', start)
    depth = 1
    end = body + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


class StorageOptimizationsTests(unittest.TestCase):
    def test_production_query_and_membership(self):
        source = (ROOT / 'runtime/StableGameBridge.cs').read_text()
        helpers = '\n'.join(method(source, name) for name in
                            ['static bool StorageContains(', 'public static bool StorageScanGate('])
        fixture = r'''
using System;using System.Collections.Generic;using System.Reflection;
public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 operator +(Vector3 a,Vector3 b){return a;}public static Vector3 operator *(Vector3 a,float b){return a;}}
public class Transform {public Vector3 position,localPosition,localScale;public int rotation;public Vector3 TransformDirection(Vector3 v){return v;}}
public class Collider {public int Id;public int GetInstanceID(){return Id;}}
public class BoxCollider:Collider {public Transform transform=new Transform();public Vector3 center,size;}
public class mountStuff {private Transform colParentTransform=new Transform();public BoxCollider col=new BoxCollider();public Collider[] cols;}
public class mainscript {public static mainscript s=new mainscript();public int mountLayerMask;}
public class Physics {
 public static Collider[] Results;public static int Allocating,Queries;
 public static int OverlapBoxNonAlloc(Vector3 c,Vector3 s,Collider[] buffer,int r,int mask){Queries++;int n=Math.Min(buffer.Length,Results.Length);Array.Copy(Results,buffer,n);return n;}
 public static Collider[] OverlapBox(Vector3 c,Vector3 s,int r,int mask){Allocating++;return (Collider[])Results.Clone();}
}
public class Test {
 static readonly FieldInfo storageParent=typeof(mountStuff).GetField("colParentTransform",BindingFlags.Instance|BindingFlags.NonPublic);
 static readonly Stack<Collider[]> storageBuffers=new Stack<Collider[]>();
 static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 /*HELPERS*/
 static void Main(){
  var mount=new mountStuff();var a=new Collider{Id=1};var b=new Collider{Id=2};
  Physics.Results=new[]{a,b};Check(!StorageScanGate(mount),"optimized scan not used");var old=mount.cols;
  StorageScanGate(mount);Check(Object.ReferenceEquals(old,mount.cols),"unchanged snapshot reallocated");
  Physics.Results=new[]{b};StorageScanGate(mount);Check(mount.cols.Length==1 && mount.cols[0]==b && old.Length==2 && old[0]==a,"changed scan mutated prior snapshot");
  Physics.Results=new Collider[4097];for(int i=0;i<4097;i++)Physics.Results[i]=new Collider{Id=i+10};
  StorageScanGate(mount);Check(mount.cols.Length==4097 && Physics.Allocating==1,"saturated query truncated colliders");
  Physics.Results=new Collider[0];StorageScanGate(mount);Check(mount.cols.Length==0,"stale buffer entries leaked");
  var random=new Random(123);for(int trial=0;trial<1000;trial++){
   var pool=new Collider[]{null,a,b,new Collider{Id=3}};var previous=new Collider[random.Next(20)];var set=new HashSet<int>();
   for(int i=0;i<previous.Length;i++){previous[i]=pool[random.Next(pool.Length)];if(previous[i]!=null)set.Add(previous[i].Id);}
   foreach(var current in pool){bool native=false;foreach(var p in previous)if(p==current){native=true;break;}
    Check(native==StorageContains(previous,set,current),"two-scan membership changed");}
  }
  Console.WriteLine("PASS production storage helpers: "+checks+" assertions; query saturation, snapshot isolation and native membership parity. No Unity/PhysX.");
 }
}
'''.replace('/*HELPERS*/', helpers)
        compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        with tempfile.TemporaryDirectory(prefix='tld-storage-') as directory:
            src = Path(directory) / 'StorageTests.cs'
            exe = Path(directory) / 'StorageTests.exe'
            src.write_text(fixture)
            subprocess.run([str(compiler), '/nologo', '/optimize+', f'/out:{exe}', str(src)], check=True)
            subprocess.run([str(exe)], check=True)
