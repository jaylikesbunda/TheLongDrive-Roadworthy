using System;using System.IO;using TldMp;using UnityEngine;
namespace UnityEngine {public struct Vector3d{public double x,y,z;public Vector3d(double x,double y,double z){this.x=x;this.y=y;this.z=z;}} public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}}
public static class WorldRelocationLedgerTests {
 static int n;static void Check(bool b){n++;if(!b)throw new Exception("check "+n);}
 static void Bad(string s){bool bad=false;try{WorldRelocationLedger.Restore(s);}catch(InvalidDataException){bad=true;}Check(bad);}
 public static void Main(){
  WorldRelocationLedger.Clear();var k=new Vector3d(1,2,3);var r=new WorldRelocationLedger.Record{position=new Vector3d(4,5,6),rotation=new Vector3(7,8,9),aligned=true,type=12,seed=99};
  Check(WorldRelocationLedger.TryRecord(1,2,0,k,r));Check(WorldRelocationLedger.TryRecord(2,2,1,k,r));string v=WorldRelocationLedger.Stamp("native [Roadworthy placement: 4] [Roadworthy biome: Grass]");Check(v.IndexOf("[Roadworthy relocation:")<v.IndexOf("[Roadworthy biome:"));
  WorldRelocationLedger.Clear();WorldRelocationLedger.Restore(v);Check(WorldRelocationLedger.Count==2);WorldRelocationLedger.Record read;Check(WorldRelocationLedger.TryGet(1,2,0,k,out read));Check(read.position.x==4 && read.position.y==5 && read.position.z==6 && read.rotation.y==8 && read.type==12 && read.seed==99 && read.aligned);Check(WorldRelocationLedger.TryGet(2,2,1,k,out read));Check(WorldRelocationLedger.Stamp(v)==v);
  Bad("x [Roadworthy relocation: !!!]");Bad("x [Roadworthy relocation broken]"+v);Bad("x [Roadworthy relocation: ");Bad(v+" [Roadworthy relocation: bad]");Bad("x [Roadworthy relocation:AAAA]");Bad("x [Roadworthy relocation:x]");Check(WorldRelocationLedger.Count==2);
  bool duplicate=false;try{WorldRelocationLedger.TryRecord(1,2,0,k,r);}catch(InvalidOperationException){duplicate=true;}Check(duplicate);
  bool nonfinite=false;try{WorldRelocationLedger.TryRecord(1,0,0,new Vector3d(double.NaN,0,0),r);}catch(InvalidDataException){nonfinite=true;}Check(nonfinite);
  WorldRelocationLedger.Clear();for(int i=0;i<4096;i++)Check(WorldRelocationLedger.TryRecord(1,0,0,new Vector3d(i,0,0),r));Check(!WorldRelocationLedger.TryRecord(1,0,0,new Vector3d(4096,0,0),r));string full=WorldRelocationLedger.Stamp("native");WorldRelocationLedger.Clear();WorldRelocationLedger.Restore(full);Check(WorldRelocationLedger.Count==4096);Check(WorldRelocationLedger.Stamp(full)==full);WorldRelocationLedger.Restore("native");Check(WorldRelocationLedger.Count==0);
  Console.WriteLine("PASS "+n+" relocation ledger checks");
 }
}
