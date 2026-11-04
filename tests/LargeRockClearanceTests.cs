using System;
using System.Collections.Generic;
using UnityEngine;
using TldMp;
namespace UnityEngine {
 public struct Vector3d { public double x,y,z; public Vector3d(double a,double b,double c){x=a;y=b;z=c;} }
 public struct Vector3 {public static Vector3 one{get{return new Vector3(1,1,1);}}public static Vector3 operator*(Vector3 v,float n){return new Vector3(v.x*n,v.y*n,v.z*n);}public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 Scale(Vector3 a,Vector3 b){return new Vector3(a.x*b.x,a.y*b.y,a.z*b.z);}}
 public struct Quaternion {Vector3 angles;public static Quaternion Euler(Vector3 v){return new Quaternion{angles=v};}public static Vector3 operator*(Quaternion q,Vector3 v){double z=q.angles.z*Math.PI/180,x=q.angles.x*Math.PI/180,y=q.angles.y*Math.PI/180;double xx=v.x*Math.Cos(z)-v.y*Math.Sin(z),yy=v.x*Math.Sin(z)+v.y*Math.Cos(z);double zz=yy*Math.Sin(x)+v.z*Math.Cos(x);yy=yy*Math.Cos(x)-v.z*Math.Sin(x);return new Vector3((float)(xx*Math.Cos(y)+zz*Math.Sin(y)),(float)yy,(float)(-xx*Math.Sin(y)+zz*Math.Cos(y)));}}
 public static class Mathf {public static float Lerp(float a,float b,float t){return a+(b-a)*t;}public static float Max(float a,float b){return Math.Max(a,b);}}
 public struct Matrix4x4 {public static Matrix4x4 operator*(Matrix4x4 a,Matrix4x4 b){return a;}public Vector3 MultiplyPoint3x4(Vector3 p){return p;}}
 public class Transform {public Vector3 localScale=new Vector3(1,1,1);public Matrix4x4 worldToLocalMatrix,localToWorldMatrix;}
 public struct Bounds {public Vector3 center,extents;} public class Mesh {public Bounds bounds;}
 public class MeshCollider {public bool isTrigger;public Mesh sharedMesh;public Transform transform=new Transform();}
 public class GameObject {public Transform transform=new Transform();public MeshCollider[] colliders;public randomRock random;public T GetComponent<T>() where T:class{return random as T;}public T[] GetComponentsInChildren<T>(bool all){return (T[])(object)colliders;}}
}
public class randomRock {public object r;public object[] materials;public bool noScale;public float sizeMin,sizeMax;}
namespace TldMp { public static class WorldPlacementPolicy { public static int EffectiveVersion; } }
public class terrainGenSettings { public double Height(Vector3d p,List<roadGenScript.roadClass> roads,List<poiGenScript.poiClass> pois){return 77;} }
public class poiGenScript {public class quadClass {public Vector3d[] corners;}public class poiClass {public Vector3d pos;public List<quadClass> terrainHoleQuads=new List<quadClass>(),noObjQuads=new List<quadClass>();}}
public class objGenScript {public List<categoryClass> categories=new List<categoryClass>(); public terrainGenSettings terrain=new terrainGenSettings();public float tileSize=100;public class chunkClass {public Dictionary<Vector3d,objClass> objs=new Dictionary<Vector3d,objClass>();} public class prefabClass {public GameObject prefab;} public class objClass {public int seed;public Vector3 rot;public int prefabIndex;public float scale;public Vector3d gpos;} public class categoryClass { public string name;public int rndCategoryOffset; public float minDistFromRoad;public List<prefabClass> prefabs=new List<prefabClass>();public Dictionary<Vector3d,chunkClass> generatedChunks=new Dictionary<Vector3d,chunkClass>();public objGenScript script=new objGenScript();public float currentChunkSize=1000,minDistFromPOI,offsetty;public bool useTerrainHeight=true,useRemoveInsidePOI=true; } }
public class roadGenScript {
 public class roadElementClass { public List<Vector3d> bonePos=new List<Vector3d>(); }
 public class roadClass { public bool generated; public List<roadElementClass> roadElements=new List<roadElementClass>(); }
 public class dotChunkClass { public bool insideConnected,outsideUConnected,outsideRConnected,innerChunkGenerated; public List<roadClass> roads=new List<roadClass>(); public bool Done(){foreach(var r in roads)if(!r.generated)return false;return true;} }
 public bool hasMainRoad,hasSideRoad; public float chunkSize=10000; public Dictionary<Vector3d,dotChunkClass> chunks=new Dictionary<Vector3d,dotChunkClass>();
}
public class mapSettingScript {public List<objGenScript> objGens=new List<objGenScript>(); public List<roadGenScript> roadGens=new List<roadGenScript>(); public static List<roadGenScript.roadClass> native=new List<roadGenScript.roadClass>(); public static List<roadGenScript.roadClass> RoadsNear(Vector3d p){return new List<roadGenScript.roadClass>(native);} public static List<poiGenScript.poiClass> pois=new List<poiGenScript.poiClass>();public static List<poiGenScript.poiClass> POIsNear(Vector3d p,float size){return pois;} }
public class menuhandler { public static menuhandler s=new menuhandler(); public mapSettingScript currentMainMap=new mapSettingScript(); }
public static class mainscript {public static bool IsPointInRectangle(Vector3d p,Vector3d[] r){return p.x>=r[0].x && p.x<=r[2].x && p.z>=r[0].z && p.z<=r[2].z;} public static Vector3d GetChunkPos(Vector3d p,float size){return GetChunkPos(p,0,0,size);} public static Vector3d GetChunkPos(Vector3d p,int x,int z,float size){return new Vector3d(Math.Round(p.x/size)*size+x*size,0,Math.Round(p.z/size)*size+z*size);} }
public static class LargeRockClearanceTests {
 static int count; static void Check(bool b){count++;if(!b)throw new Exception("check "+count);}
 public static void Main(){
  var p=new Vector3d(0,0,0); var big=new objGenScript.categoryClass{name="BigRocks",minDistFromRoad=1000}; var small=new objGenScript.categoryClass{name="SmallRocks",minDistFromRoad=5};
  var g=new roadGenScript{hasSideRoad=true}; menuhandler.s.currentMainMap.roadGens.Add(g);
  var chunk=new roadGenScript.dotChunkClass();g.chunks.Add(p,chunk);
  var r=new roadGenScript.roadClass{generated=true};var e=new roadGenScript.roadElementClass();e.bonePos.Add(new Vector3d(500,0,-10));e.bonePos.Add(new Vector3d(500,0,10));r.roadElements.Add(e);chunk.roads.Add(r);
  WorldPlacementPolicy.EffectiveVersion=0;Check(LargeRockClearance.RoadsNear(p,big).Count==0);
  WorldPlacementPolicy.EffectiveVersion=1;Check(LargeRockClearance.RoadsNear(p,small).Count==0);
  long before=LargeRockClearance.IncompleteCoverageCount;Check(LargeRockClearance.RoadsNear(p,big).Count==1);Check(LargeRockClearance.IncompleteCoverageCount>before);
  mapSettingScript.native.Add(r);Check(LargeRockClearance.RoadsNear(p,big).Count==1);mapSettingScript.native.Clear();
  e.bonePos.Clear();e.bonePos.Add(new Vector3d(1100,0,0));Check(LargeRockClearance.RoadsNear(p,big).Count==0);
  e.bonePos.Add(new Vector3d(900,0,0));Check(LargeRockClearance.RoadsNear(p,big).Count==1);
  r.generated=false;Check(LargeRockClearance.RoadsNear(p,big).Count==0);
  Check(LargeRockClearance.IntersectsBounds(0,0,1000,1000,-2,1100,2));
  WorldPlacementPolicy.EffectiveVersion=2;
  var prefab=new GameObject {colliders=new[]{new MeshCollider{sharedMesh=new Mesh{bounds=new Bounds{extents=new Vector3(100,1,1)}}}}};
  big.prefabs.Add(new objGenScript.prefabClass{prefab=prefab});var obj=new objGenScript.objClass{scale=1,gpos=p,prefabIndex=0};
  Check(LargeRockClearance.NativeMinDistance(big)==0);Check(LargeRockClearance.NativeMinDistance(small)==5);
  r.generated=true;e.bonePos.Clear();e.bonePos.Add(new Vector3d(500,0,-5));e.bonePos.Add(new Vector3d(500,0,5));
  var objects=new Dictionary<Vector3d,objGenScript.objClass>();LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==1);
  objects.Clear();e.bonePos.Clear();e.bonePos.Add(new Vector3d(100,0,-5));e.bonePos.Add(new Vector3d(100,0,5));LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==0);
  objects.Clear();obj.scale=.1f;LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==1);
  objects.Clear();obj.scale=1;WorldPlacementPolicy.EffectiveVersion=1;Check(LargeRockClearance.NativeMinDistance(big)==1000);LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==1);
  Check(LargeRockClearance.SegmentDistanceSquared(p,new Vector3d(3,0,-5),new Vector3d(3,0,5))==9);
  WorldPlacementPolicy.EffectiveVersion=3;obj.scale=1;prefab.colliders[0].sharedMesh.bounds=new Bounds{extents=new Vector3(100,1000,1)};
  e.bonePos.Clear();e.bonePos.Add(new Vector3d(250,0,-5));e.bonePos.Add(new Vector3d(250,0,5));
  prefab.random=new randomRock{sizeMin=.5f,sizeMax=4f};
  for(int seed=0;seed<100;seed++){obj.seed=seed;objects.Clear();LargeRockClearance.AddGenerated(objects,p,obj,big);float scale=Mathf.Lerp(.5f,4f,(float)new Random(seed).NextDouble());bool allowed=Math.Sqrt(10001)*scale+8<250;Check((objects.Count==1)==allowed);}
  WorldPlacementPolicy.EffectiveVersion=4;prefab.random=null;prefab.colliders[0].sharedMesh.bounds=new Bounds{extents=new Vector3(100,1,1)};obj.gpos=p;obj.scale=1;obj.seed=0;
  e.bonePos.Clear();e.bonePos.Add(new Vector3d(0,0,-500));e.bonePos.Add(new Vector3d(0,0,500));
  var owning=new objGenScript.chunkClass();big.generatedChunks.Add(p,owning);objects=owning.objs;big.script.categories.Add(big);menuhandler.s.currentMainMap.objGens.Add(big.script);WorldRelocationLedger.Clear();
  long moved=LargeRockClearance.RelocatedCount;LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==1 && obj.gpos.x!=0 && obj.gpos.y==77);Check(LargeRockClearance.RelocatedCount==moved+1);
  var first=obj.gpos;string snapshot=WorldRelocationLedger.Stamp("base");WorldRelocationLedger.Clear();WorldRelocationLedger.Restore(snapshot);e.bonePos.Clear();
  objects.Clear();obj.gpos=p;LargeRockClearance.AddGenerated(objects,p,obj,big);Check(obj.gpos.x==first.x && obj.gpos.z==first.z && obj.gpos.y==first.y);
  WorldRelocationLedger.Clear();e.bonePos.Add(new Vector3d(0,0,-500));e.bonePos.Add(new Vector3d(0,0,500));
  objects.Clear();obj.gpos=p;big.currentChunkSize=100;long failed=LargeRockClearance.RelocationFailedCount;LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==0 && obj.gpos.x==0 && LargeRockClearance.RelocationFailedCount==failed+1);
  objects.Clear();obj.gpos=p;WorldRelocationLedger.Clear();big.currentChunkSize=1000;big.minDistFromPOI=10000;mapSettingScript.pois.Add(new poiGenScript.poiClass{pos=p});LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==0 && obj.gpos.x==0);mapSettingScript.pois.Clear();
  objects.Clear();obj.gpos=p;WorldRelocationLedger.Clear();big.minDistFromPOI=0;var distantPoi=new poiGenScript.poiClass{pos=new Vector3d(9000,0,9000)};
  distantPoi.noObjQuads.Add(new poiGenScript.quadClass{corners=new[]{new Vector3d(-400,0,-400),new Vector3d(400,0,-400),new Vector3d(400,0,400),new Vector3d(-400,0,400)}});mapSettingScript.pois.Add(distantPoi);
  LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==0 && obj.gpos.x==0);mapSettingScript.pois.Clear();
  WorldRelocationLedger.Clear();objects.Clear();obj.gpos=p;big.minDistFromPOI=0;
  var v=new WorldRelocationLedger.Record{position=new Vector3d(2000,0,0),rotation=obj.rot,type=obj.prefabIndex,seed=obj.seed};WorldRelocationLedger.TryRecord(2,0,0,p,v);
  bool invalid=false;try{LargeRockClearance.AddGenerated(objects,p,obj,big);}catch(System.IO.InvalidDataException){invalid=true;}Check(invalid && objects.Count==0);WorldRelocationLedger.Clear();
  for(int i=0;i<4096;i++)WorldRelocationLedger.TryRecord(1,0,0,new Vector3d(i+1,0,0),new WorldRelocationLedger.Record());
  long full=LargeRockClearance.LedgerCapacityFallbackCount;obj.gpos=p;LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==1 && obj.gpos.x==0 && LargeRockClearance.LedgerCapacityFallbackCount==full+1);WorldRelocationLedger.Clear();
  for(int i=0;i<LargeRockClearance.RelocationAttempts;i++){double x1,z1,x2,z2;LargeRockClearance.RelocationOffset(123,i,100,out x1,out z1);LargeRockClearance.RelocationOffset(123,i,100,out x2,out z2);Check(x1==x2 && z1==z2);}
  // Profile 5 keeps the empty space beside elongated collider footprints.
  WorldPlacementPolicy.EffectiveVersion=5;WorldRelocationLedger.Clear();objects.Clear();obj.gpos=p;obj.rot=new Vector3(0,0,0);obj.scale=1;prefab.random=null;big.currentChunkSize=1000;big.minDistFromPOI=0;
  e.bonePos.Clear();e.bonePos.Add(new Vector3d(-200,0,50));e.bonePos.Add(new Vector3d(200,0,50));
  moved=LargeRockClearance.RelocatedCount;LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==1 && obj.gpos.x==0 && obj.gpos.z==0 && LargeRockClearance.RelocatedCount==moved);
  // Rotation must turn the narrow footprint into the road before relocation.
  objects.Clear();obj.gpos=p;obj.rot=new Vector3(0,90,0);LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==1 && (obj.gpos.x!=0 || obj.gpos.z!=0));
  first=obj.gpos;snapshot=WorldRelocationLedger.Stamp("profile5");WorldRelocationLedger.Clear();WorldRelocationLedger.Restore(snapshot);objects.Clear();obj.gpos=p;e.bonePos.Clear();LargeRockClearance.AddGenerated(objects,p,obj,big);Check(obj.gpos.x==first.x && obj.gpos.z==first.z);WorldRelocationLedger.Clear();
  // A nearby true overlap still moves, using short moves inside the owner cell.
  objects.Clear();obj.rot=new Vector3(0,0,0);obj.gpos=p;big.currentChunkSize=100;e.bonePos.Add(new Vector3d(-200,0,0));e.bonePos.Add(new Vector3d(200,0,0));LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==1 && Math.Abs(obj.gpos.z)>9 && Math.Abs(obj.gpos.z)<50);WorldRelocationLedger.Clear();
  // A road inside the old radius but farther from the narrow shape stays clear.
  objects.Clear();obj.gpos=p;big.currentChunkSize=1000;e.bonePos.Clear();e.bonePos.Add(new Vector3d(-200,0,20));e.bonePos.Add(new Vector3d(200,0,20));LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==1 && obj.gpos.z==0);
  // Geometry adversaries: crossing segments, tangency, degenerate shapes,
  // collinear disjoint lines, and points inside the footprint.
  var hull=LargeRockClearance.Hull(new[]{new LargeRockClearance.Point(-100,-1),new LargeRockClearance.Point(100,1),new LargeRockClearance.Point(-100,1),new LargeRockClearance.Point(100,-1)});
  Check(hull.Length==4);
  Check(!LargeRockClearance.SegmentNearHull(new LargeRockClearance.Point(-200,50),new LargeRockClearance.Point(200,50),hull,8));
  Check(LargeRockClearance.SegmentNearHull(new LargeRockClearance.Point(0,-200),new LargeRockClearance.Point(0,200),hull,8));
  Check(LargeRockClearance.SegmentNearHull(new LargeRockClearance.Point(-200,9),new LargeRockClearance.Point(200,9),hull,8));
  Check(!LargeRockClearance.SegmentNearHull(new LargeRockClearance.Point(-200,9.01),new LargeRockClearance.Point(200,9.01),hull,8));
  Check(!LargeRockClearance.SegmentNearHull(new LargeRockClearance.Point(200,1),new LargeRockClearance.Point(300,1),hull,8));
  Check(LargeRockClearance.SegmentNearHull(new LargeRockClearance.Point(0,0),new LargeRockClearance.Point(0,0),hull,0));
  var line=LargeRockClearance.Hull(new[]{new LargeRockClearance.Point(0,0),new LargeRockClearance.Point(10,0),new LargeRockClearance.Point(10,0),new LargeRockClearance.Point(0,0)});
  Check(!LargeRockClearance.SegmentNearHull(new LargeRockClearance.Point(20,0),new LargeRockClearance.Point(30,0),line,1));
  Check(LargeRockClearance.SegmentNearHull(new LargeRockClearance.Point(5,-10),new LargeRockClearance.Point(5,10),line,0));
  var point=LargeRockClearance.Hull(new[]{new LargeRockClearance.Point(5,5),new LargeRockClearance.Point(5,5)});
  Check(LargeRockClearance.SegmentNearHull(new LargeRockClearance.Point(0,5),new LargeRockClearance.Point(10,5),point,0));
  Check(!LargeRockClearance.SegmentNearHull(new LargeRockClearance.Point(0,7),new LargeRockClearance.Point(10,7),point,1));
  // Distinct collider footprints retain the gap between them.
  WorldRelocationLedger.Clear();objects.Clear();obj.gpos=p;obj.rot=new Vector3(0,0,0);obj.scale=1;prefab.random=null;
  prefab.colliders=new[]{new MeshCollider{sharedMesh=new Mesh{bounds=new Bounds{center=new Vector3(-100,0,0),extents=new Vector3(5,1,100)}}},new MeshCollider{sharedMesh=new Mesh{bounds=new Bounds{center=new Vector3(100,0,0),extents=new Vector3(5,1,100)}}}};
  e.bonePos.Clear();e.bonePos.Add(new Vector3d(0,0,-200));e.bonePos.Add(new Vector3d(0,0,200));LargeRockClearance.AddGenerated(objects,p,obj,big);Check(objects.Count==1 && obj.gpos.x==0 && obj.gpos.z==0);
  // Per-instance scale and the material RNG draw both affect the narrow phase.
  prefab.colliders=new[]{new MeshCollider{sharedMesh=new Mesh{bounds=new Bounds{extents=new Vector3(100,1,20)}}}};
  prefab.random=new randomRock{sizeMin=.5f,sizeMax=4,materials=new object[3]};big.minDistFromPOI=10000;mapSettingScript.pois.Add(new poiGenScript.poiClass{pos=p});
  e.bonePos.Clear();e.bonePos.Add(new Vector3d(-500,0,50));e.bonePos.Add(new Vector3d(500,0,50));
  for(int material=0;material<2;material++)for(int seed=0;seed<100;seed++){
   prefab.random.r=material==0?null:new object();obj.seed=seed;obj.gpos=p;objects.Clear();WorldRelocationLedger.Clear();
   var rng=new Random(seed);if(material!=0)rng.Next(0,3);float scale=Mathf.Lerp(.5f,4,(float)rng.NextDouble());
   LargeRockClearance.AddGenerated(objects,p,obj,big);Check((objects.Count==1)==(20*scale+8<50));
  }
  mapSettingScript.pois.Clear();big.minDistFromPOI=0;WorldRelocationLedger.Clear();
  Console.WriteLine("PASS "+count+" large rock clearance checks");
 }
}
