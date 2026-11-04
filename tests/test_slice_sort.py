import os,subprocess,tempfile,unittest
from pathlib import Path
class SliceSort(unittest.TestCase):
 def test_stable_parity(self):
  root=Path(__file__).resolve().parents[1];s=(root/'runtime/StableGameBridge.cs').read_text();a=s.index('        static readonly List<float> sliceSortKeys=');b=s.index('        static readonly Dictionary<Vector3,Vector3> slicePoints=',a)
  fixture=r'''
using System;using System.Collections.Generic;using System.Linq;
struct V{public float magnitude;}struct B{public V size;}class Mesh{public B bounds;public int id;}
class P{
HELPERS
static void Main(){int checks=0;var r=new Random(19);for(int n=0;n<50;n++)for(int k=0;k<100;k++){
 var x=new List<Mesh>();for(int i=0;i<n;i++){float v=(float)r.Next(5);if(i==3&&k==0)v=float.NaN;x.Add(new Mesh{id=i,bounds=new B{size=new V{magnitude=v}}});}
 var expected=x.OrderBy(m=>m.bounds.size.magnitude).Select(m=>m.id).ToArray();var actual=SortSliceMeshes(x).Select(m=>m.id).ToArray();if(!expected.SequenceEqual(actual))throw new Exception("stable parity failed");checks++;}
 Console.WriteLine("PASS "+checks+" stable production slice-sort parity checks, including ties and NaN");}
}
'''.replace('HELPERS',s[a:b])
  with tempfile.TemporaryDirectory(prefix='tld-slice-sort-') as d:
   code=Path(d)/'test.cs';exe=Path(d)/'test.exe';code.write_text(fixture);c=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe';subprocess.run([str(c),'/nologo','/out:'+str(exe),str(code)],check=True);out=subprocess.run([str(exe)],text=True,capture_output=True,check=True);print(out.stdout)
