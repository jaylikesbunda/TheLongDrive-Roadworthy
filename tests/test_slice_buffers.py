import os,subprocess,tempfile,unittest
from pathlib import Path
class SliceBuffers(unittest.TestCase):
 def test_mutation_and_reuse(self):
  source=(Path(__file__).resolve().parents[1]/'runtime/StableGameBridge.cs').read_text();a=source.index('        // The native slicer only reads');b=source.index('        static readonly List<float> sliceSortKeys=',a)
  fixture=r'''
using System;using System.Collections.Generic;using System.Linq;
struct Vector2{public int x;}struct Vector3{public int x;}
class Mesh{
 public List<Vector3> v=new List<Vector3>();public List<Vector2> u=new List<Vector2>();public List<int> t=new List<int>();public int subMeshCount=1;public int NativeTriangles;
 public Vector3[] vertices{get{return v.ToArray();}}public Vector2[] uv{get{return u.ToArray();}}public int[] triangles{get{NativeTriangles++;return t.ToArray();}}
 public void GetVertices(List<Vector3> dst){dst.Clear();dst.AddRange(v);}public void GetUVs(int channel,List<Vector2> dst){dst.Clear();dst.AddRange(u);}public void GetTriangles(List<int> dst,int sub,bool baseVertex){dst.Clear();dst.AddRange(t);}
}
class Probe{
HELPERS
static void Main(){int checks=0;var m=new Mesh();var r=new Random(91);for(int n=0;n<1000;n++){
 m.v.Clear();m.u.Clear();m.t.Clear();int size=r.Next(20);for(int i=0;i<size;i++){m.v.Add(new Vector3{x=r.Next()});m.u.Add(new Vector2{x=r.Next()});m.t.Add(r.Next());}
 var v=SliceVertices(m);var uv=SliceUv(m);var t=SliceTriangles(m);
 if(!v.Select(x=>x.x).SequenceEqual(m.vertices.Select(x=>x.x)) || !uv.Select(x=>x.x).SequenceEqual(m.uv.Select(x=>x.x)) || !t.SequenceEqual(m.t))throw new Exception("mesh mutation parity");
 if((sliceVertexBuffers.ContainsKey(size) && !Object.ReferenceEquals(v,SliceVertices(m))) || (sliceUvBuffers.ContainsKey(size) && !Object.ReferenceEquals(uv,SliceUv(m))) || (sliceIndexBuffers.ContainsKey(size) && !Object.ReferenceEquals(t,SliceTriangles(m))))throw new Exception("buffer reuse");checks++;
 }
 m.v.Clear();for(int i=0;i<70000;i++)m.v.Add(new Vector3{x=i});var large=SliceVertices(m);if(large.Length!=70000 || sliceVertexList.Capacity!=0 || sliceVertexBuffers.ContainsKey(70000))throw new Exception("oversize retention");
 m.subMeshCount=2;var fallback=SliceTriangles(m);if(m.NativeTriangles!=1 || !fallback.SequenceEqual(m.t))throw new Exception("multi submesh fallback");
 bool threw=false;try{SliceVertices(null);}catch(NullReferenceException){threw=true;}if(!threw)throw new Exception("null parity");
 Console.WriteLine("PASS "+checks+" mutable-mesh parity and bounded buffer tests; multi-submesh and null fallback");}
}
'''.replace('HELPERS',source[a:b])
  with tempfile.TemporaryDirectory(prefix='tld-slice-buffers-') as d:
   p=Path(d);code=p/'probe.cs';exe=p/'probe.exe';code.write_text(fixture);compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe';subprocess.run([str(compiler),'/nologo','/out:'+str(exe),str(code)],check=True);out=subprocess.run([str(exe)],check=True,text=True,capture_output=True);print(out.stdout)
