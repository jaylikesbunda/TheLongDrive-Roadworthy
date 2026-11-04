import os, subprocess, tempfile, unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
class GuestRelocationTests(unittest.TestCase):
 def test_host_record_import_and_guest_save(self):
  source=(ROOT/'runtime/StableMap.cs').read_text()
  a=source.index('        static void ImportWorldRelocation(');b=source.index('        static void ApplyChunk(',a)
  helper=source[a:b]
  fixture=r'''
using System;using TldMp;using UnityEngine;
namespace UnityEngine {public struct Vector3d {public double x,y,z;public Vector3d(double x,double y,double z){this.x=x;this.y=y;this.z=z;}} public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static bool operator==(Vector3 a,Vector3 b){return a.x==b.x && a.y==b.y && a.z==b.z;}public static bool operator!=(Vector3 a,Vector3 b){return !(a==b);}public override bool Equals(object x){return x is Vector3 && this==(Vector3)x;}public override int GetHashCode(){return 0;}}}
class Test {
 static bool SamePosition(Vector3d a,Vector3d b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
 HELPER
 static int n;static void Check(bool x){n++;if(!x)throw new Exception("check "+n);}
 static void Main(){var id=new Vector3d(4,0,7);var moved=new Vector3d(37,5,42);var v=new WorldRelocationLedger.Record{position=moved,rotation=new Vector3(0,90,0),aligned=true,type=3};WorldRelocationLedger.Clear();ImportWorldRelocation(1,1,0,id,v);ImportWorldRelocation(1,1,0,id,v);Check(WorldRelocationLedger.Count==1);string save=WorldRelocationLedger.Stamp("native");WorldRelocationLedger.Clear();WorldRelocationLedger.Restore(save);WorldRelocationLedger.Record restored;Check(WorldRelocationLedger.TryGet(1,1,0,id,out restored));Check(SamePosition(restored.position,moved)&&restored.rotation.y==90&&restored.type==3&&restored.aligned);var different=v;different.position=new Vector3d(99,0,0);bool rejected=false;try{ImportWorldRelocation(1,1,0,id,different);}catch(InvalidOperationException){rejected=true;}Check(rejected);Check(WorldRelocationLedger.TryGet(1,1,0,id,out restored)&&SamePosition(restored.position,moved));var mountain=new WorldRelocationLedger.Record{position=new Vector3d(55,6,66),rotation=new Vector3(5,15,25),type=8,seed=123};ImportWorldRelocation(2,2,3,id,mountain);string guestSave=WorldRelocationLedger.Stamp("native");WorldRelocationLedger.Clear();WorldRelocationLedger.Restore(guestSave);Check(WorldRelocationLedger.TryGet(2,2,3,id,out restored)&&restored.type==8&&restored.seed==123&&SamePosition(restored.position,mountain.position));Console.WriteLine("PASS "+n+" guest relocation import/save/reload checks");}
}
'''.replace('HELPER',helper)
  with tempfile.TemporaryDirectory() as directory:
   p=Path(directory);cs=p/'fixture.cs';cs.write_text(fixture);exe=p/'test.exe';csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
   subprocess.run([str(csc),'/nologo','/out:'+str(exe),str(cs),str(ROOT/'runtime/WorldRelocationLedger.cs')],check=True)
   subprocess.run([str(exe)],check=True)
if __name__=='__main__':unittest.main()
