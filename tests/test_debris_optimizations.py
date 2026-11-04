"""Execute the production slicing helpers against counted Unity boundaries."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


class DebrisOptimizations(unittest.TestCase):
    def test_production_helpers(self):
        root = Path(__file__).resolve().parents[1]
        source = (root / 'runtime/StableGameBridge.cs').read_text()
        start = source.index('        static readonly Dictionary<Vector3,Vector3> slicePoints=')
        end = source.index('        // Captured once per process', start)
        helpers = source[start:end]
        fixture = r'''
using System;using System.Collections.Generic;
struct Vector3 : IEquatable<Vector3> {
 public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
 public bool Equals(Vector3 v){return x.Equals(v.x)&&y.Equals(v.y)&&z.Equals(v.z);}
 public override bool Equals(object o){return o is Vector3 && Equals((Vector3)o);}
 public override int GetHashCode(){return x.GetHashCode()^y.GetHashCode()^z.GetHashCode();}
}
class MeshSlicer {public struct CustomPlane {public static int Calls; public float GetSide(Vector3 p){Calls++;return p.x-p.y;}}}
class Transform {
 public int Id,Calls;public float Offset;
 public int GetInstanceID(){return Id;}
 public Vector3 TransformPoint(Vector3 p){Calls++;return new Vector3(p.x+Offset,p.y*3,p.z-2);}
}
class Collider { public int Id;public int GetInstanceID(){return Id;} }
class Physics {
 public static int Calls;public static bool Fail;
 public static HashSet<string> Pairs=new HashSet<string>();
 public static void IgnoreCollision(Collider a,Collider b){
  Calls++;if(Fail)throw new InvalidOperationException("native");
  if(a==null||b==null)throw new ArgumentNullException();
  Pairs.Add(Math.Min(a.Id,b.Id)+":"+Math.Max(a.Id,b.Id));
 }
}
class FrameDiagnostics {public static void NativeBegin(int i){} public static void NativeEnd(int i){} }
class Probe {
HELPERS
 static int checks;static void Check(bool v){checks++;if(!v)throw new Exception("assertion "+checks);}
 static void Main(){
  var t=new Transform{Id=1,Offset=5};var p=new Vector3(1,2,3);
  SliceGeometryBegin();for(int i=0;i<100;i++)Check(SliceTransformPoint(t,p).Equals(new Vector3(6,6,1)));
  Check(t.Calls==1);var plane=new MeshSlicer.CustomPlane();for(int i=0;i<100;i++)Check(SlicePlaneSide(ref plane,p)==-1);Check(MeshSlicer.CustomPlane.Calls==1);SliceGeometryEnd();
  t.Offset=9;SliceGeometryBegin();Check(SliceTransformPoint(t,p).x==10);Check(t.Calls==2);
  var other=new Transform{Id=2,Offset=20};Check(SliceTransformPoint(other,p).x==21);
  Check(SliceTransformPoint(t,p).x==10);SliceGeometryEnd();
  for(int i=0;i<2;i++)SliceTransformPoint(t,p);Check(t.Calls==4);
  SliceGeometryBegin();for(int i=0;i<9000;i++)SliceTransformPoint(t,new Vector3(i,0,0));Check(slicePoints.Count==8192);SliceGeometryEnd();Check(slicePoints.Count==0);
  bool threw=false;try{SliceTransformPoint(null,p);}catch(NullReferenceException){threw=true;}Check(threw);
  var a=new Collider{Id=-3};var b=new Collider{Id=42};
  SlicePairsBegin();SliceIgnoreCollision(a,b);SliceIgnoreCollision(b,a);Check(Physics.Calls==1);Check(Physics.Pairs.Count==1);SlicePairsEnd();
  SlicePairsBegin();SliceIgnoreCollision(b,a);Check(Physics.Calls==2);SlicePairsEnd();
  SliceIgnoreCollision(a,b);Check(Physics.Calls==3);
  SlicePairsBegin();Physics.Fail=true;threw=false;try{SliceIgnoreCollision(a,b);}catch(InvalidOperationException){threw=true;}Check(threw);Check(sliceCollisionPairs.Count==0);Physics.Fail=false;SliceIgnoreCollision(a,b);Check(sliceCollisionPairs.Count==1);SlicePairsEnd();
  var cs=new List<Collider>();for(int i=0;i<300;i++)cs.Add(new Collider{Id=i+100});
  SlicePairsBegin();for(int i=0;i<cs.Count;i++)for(int j=0;j<cs.Count;j++)if(i!=j)SliceIgnoreCollision(cs[i],cs[j]);
  Check(sliceCollisionPairs.Count==32768);Check(Physics.Pairs.Count==44851);SlicePairsEnd();Check(sliceCollisionPairs.Count==0);
  Console.WriteLine("PASS "+checks+" production slicing helper checks: exact transform results, cut isolation, bounds, ordered-pair dedupe, exception retry, native fallback and saturation coverage.");
 }
}
'''.replace('HELPERS', helpers)
        with tempfile.TemporaryDirectory(prefix='tld-debris-helpers-') as directory:
            folder = Path(directory)
            code, binary = folder / 'probe.cs', folder / 'probe.exe'
            code.write_text(fixture)
            compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            subprocess.run([str(compiler), '/nologo', '/out:' + str(binary), str(code)], check=True)
            result = subprocess.run([str(binary)], check=True, capture_output=True, text=True)
            print(result.stdout)


if __name__ == '__main__':
    unittest.main()
