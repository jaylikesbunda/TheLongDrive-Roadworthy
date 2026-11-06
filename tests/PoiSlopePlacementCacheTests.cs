using System;
using System.Collections.Generic;
using System.Text;
using TldMp;
namespace UnityEngine {
 public static class Debug {public static void LogWarning(object s){}}
 public struct Vector3 {public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero;public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 Scale(Vector3 a,Vector3 b){return new Vector3(a.x*b.x,a.y*b.y,a.z*b.z);} public float x,y,z; public double magnitude {get{return Math.Sqrt(x*x+y*y+z*z);}} }
 public struct Matrix4x4 {public static Matrix4x4 operator*(Matrix4x4 a,Matrix4x4 b){return a;}public Vector3 MultiplyPoint3x4(Vector3 p){return p;}}
 public struct Bounds {public Vector3 center,extents;public Vector3 min{get{return new Vector3(center.x-extents.x,center.y-extents.y,center.z-extents.z);}}public Vector3 max{get{return new Vector3(center.x+extents.x,center.y+extents.y,center.z+extents.z);}}public Bounds(Vector3 c,Vector3 s){center=c;extents=new Vector3(s.x/2,s.y/2,s.z/2);}}
 public class Transform {public Matrix4x4 worldToLocalMatrix,localToWorldMatrix; public Vector3 localPosition,localScale; }
 public class Collider {public bool isTrigger,enabled=true;public Bounds bounds;public Transform transform=new Transform();}
 public class BoxCollider:Collider{public Vector3 center,size;}
 public class Mesh {public Bounds bounds;}
 public class MeshCollider:Collider {public Mesh sharedMesh;}
 public class GameObject {public Transform transform=new Transform(); public Collider[] colliders=new Collider[0]; public terrainHeightAlignToBuildingScript[] align=new terrainHeightAlignToBuildingScript[0]; public T[] GetComponentsInChildren<T>(bool inactive){return typeof(T)==typeof(Collider)?(T[])(object)colliders:(T[])(object)align;} }
 public struct Vector3d {public double x,y,z;public Vector3d(double x,double y,double z){this.x=x;this.y=y;this.z=z;}
  public bool Equals(Vector3d o){return x==o.x&&y==o.y&&z==o.z;}
  public override bool Equals(object o){return o is Vector3d && Equals((Vector3d)o);}
  public override int GetHashCode(){return x.GetHashCode()*31+z.GetHashCode();} }
}
public class terrainHeightAlignToBuildingScript {public UnityEngine.Transform transform=new UnityEngine.Transform();}
public class poiScript {public UnityEngine.Collider[] colliders=new UnityEngine.Collider[0];
 public T[] GetComponentsInChildren<T>(bool x){return typeof(T)==typeof(UnityEngine.Collider)?(T[])(object)colliders:new T[0];}}
public class itemdatabase {public enum b { house=1 } public static itemdatabase s=new itemdatabase();public UnityEngine.GameObject prefab;public UnityEngine.GameObject Building(int id){return prefab;} }
// Stand-in roads with native-shaped lazy generation. RoadsNear finds only generated roads through
// inner chunks; the notJustChunks fallbacks (chunk list, then every chunk) reach ungenerated roads,
// and every listed road that is not generated is generated before the call returns. Generate is
// the writer that bumps RoadStateEpoch, as the IL hook on roadClass.Generate does.
public class roadGenScript {
 public float chunkSize=10000, innerChunkSize=40; public int baround=1, iaround=1; public bool hasMainRoad, hasSideRoad=true;
 public enum roadTypeEmu {mainRoad,sideRoad}
 public static readonly List<int> GenerationLog = new List<int>();
 public sealed class roadElementClass { public List<UnityEngine.Vector3d> bonePos = new List<UnityEngine.Vector3d>(); }
 public sealed class roadClass { public UnityEngine.Vector3d start,end,selfChunk; public roadTypeEmu roadType=roadTypeEmu.mainRoad; public bool generated; public int id, selfID; public List<roadElementClass> roadElements = new List<roadElementClass>(); }
 public sealed class roadChunk { public int roadId; public List<int> roadElements = new List<int>(); }
 public sealed class innerChunkClass { public List<roadChunk> roads = new List<roadChunk>(); }
 public sealed class dotChunkClass { public List<roadClass> roads = new List<roadClass>(); public Dictionary<UnityEngine.Vector3d,innerChunkClass> innerChunks = new Dictionary<UnityEngine.Vector3d,innerChunkClass>(); }
 public Dictionary<UnityEngine.Vector3d,dotChunkClass> chunks = new Dictionary<UnityEngine.Vector3d,dotChunkClass>();
 bool Accepts(roadClass r,poiGenScript.alignToRoadEmu align){
  if(!hasMainRoad)return true;
  if(align==poiGenScript.alignToRoadEmu.anyRoad)return true;
  if(align==poiGenScript.alignToRoadEmu.onlyMainRoad)return r.roadType==roadTypeEmu.mainRoad;
  if(align==poiGenScript.alignToRoadEmu.onlySideRoad)return r.roadType==roadTypeEmu.sideRoad;
  return false;}
 public List<roadClass> RoadsNear(UnityEngine.Vector3d p,bool notJustChunks,poiGenScript.alignToRoadEmu align){
  var list=new List<roadClass>();
  var chunkKeys=new List<UnityEngine.Vector3d>();
  for(int i=-baround;i<=baround;i++)for(int j=-baround;j<=baround;j++){var k=mainscript.GetChunkPos(p,j,i,chunkSize);if(chunks.ContainsKey(k))chunkKeys.Add(k);}
  var innerKeys=new List<UnityEngine.Vector3d>();
  for(int k=-iaround;k<=iaround;k++)for(int l=-iaround;l<=iaround;l++)innerKeys.Add(mainscript.GetChunkPos(p,l,k,innerChunkSize));
  foreach(var ck in chunkKeys){
   var dcc=chunks[ck];
   for(int m=0;m<innerKeys.Count;m++){
    innerChunkClass icc;
    if(!dcc.innerChunks.TryGetValue(innerKeys[m],out icc)||icc.roads==null)continue;
    for(int n=0;n<icc.roads.Count;n++){var r=dcc.roads[icc.roads[n].roadId];if(Accepts(r,align))list.Add(r);}
   }
  }
  if(notJustChunks){
   if(list.Count==0)foreach(var ck in chunkKeys)foreach(var r in chunks[ck].roads)if(Accepts(r,align))list.Add(r);
   if(list.Count==0)foreach(var kv in chunks)foreach(var r in kv.Value.roads)if(Accepts(r,align))list.Add(r);
  }
  foreach(var r in list)if(!r.generated)Generate(r);
  return list;}
 public void Generate(roadClass r){
  RoadStateEpoch.Bump();
  var el=new roadElementClass();
  var mid=new UnityEngine.Vector3d((r.start.x+r.end.x)*0.5+Math.Sin(r.id*1.7)*15,(r.start.y+r.end.y)*0.5+Math.Cos(r.id*0.9)*2,(r.start.z+r.end.z)*0.5+Math.Cos(r.id*1.3)*15);
  el.bonePos.Add(r.start);el.bonePos.Add(mid);el.bonePos.Add(r.end);
  r.roadElements.Add(el);
  var self=chunks[r.selfChunk];
  UpdInnerChunk(r.start,self,r,r.selfID);UpdInnerChunk(mid,self,r,r.selfID);UpdInnerChunk(r.end,self,r,r.selfID);
  r.generated=true;GenerationLog.Add(r.id);}
 // Mirrors UpdInnerChunk: a new inner cell, or the last road chunk of an existing cell.
 void UpdInnerChunk(UnityEngine.Vector3d cur,dotChunkClass chunk,roadClass road,int roadId){
  var key=mainscript.GetChunkPos(cur,innerChunkSize);
  innerChunkClass icc;
  if(chunk.innerChunks.TryGetValue(key,out icc)){
   bool has=false;foreach(var rc in icc.roads)if(rc.roadId==roadId){has=true;break;}
   if(!has)icc.roads.Add(new roadChunk{roadId=roadId});
  } else {
   icc=new innerChunkClass();chunk.innerChunks.Add(key,icc);icc.roads.Add(new roadChunk{roadId=roadId});
  }
  icc.roads[icc.roads.Count-1].roadElements.Add(road.roadElements.Count-1);}
 // Mirrors ChunkDone for hasSideRoad: the chunk must exist and every road in it must be generated.
 bool ChunkDone(UnityEngine.Vector3d c){
  dotChunkClass d;if(!chunks.TryGetValue(c,out d))return false;
  foreach(var r in d.roads)if(!r.generated)return false;
  return true;}
 public bool RoadsDoneAtPlace(UnityEngine.Vector3d p){
  var c=mainscript.GetChunkPos(p,0,0,chunkSize);
  if(!ChunkDone(c))return false;
  int sx=Math.Sign(p.x-c.x),sz=Math.Sign(p.z-c.z);if(sx==0)sx=1;if(sz==0)sz=1;
  return ChunkDone(mainscript.GetChunkPos(p,sx,0,chunkSize))&&ChunkDone(mainscript.GetChunkPos(p,0,sz,chunkSize))&&ChunkDone(mainscript.GetChunkPos(p,sx,sz,chunkSize));}
 public static roadClass NearestPointOnRoadBone(UnityEngine.Vector3d p,List<roadClass> rs,out UnityEngine.Vector3d nearest){
  nearest=p;roadClass best=null;double bestD=double.MaxValue;
  if(rs==null)return null;
  foreach(var r in rs)foreach(var el in r.roadElements)for(int i=1;i<el.bonePos.Count;i++){
   var a=el.bonePos[i-1];var b=el.bonePos[i];
   double dx=b.x-a.x,dz=b.z-a.z,len2=dx*dx+dz*dz;
   double t=len2>0?((p.x-a.x)*dx+(p.z-a.z)*dz)/len2:0;if(t<0)t=0;if(t>1)t=1;
   double nx=a.x+t*dx,ny=a.y+t*(b.y-a.y),nz=a.z+t*dz;
   double d=Math.Sqrt((nx-p.x)*(nx-p.x)+(nz-p.z)*(nz-p.z));
   if(d<bestD){bestD=d;best=r;nearest=new UnityEngine.Vector3d(nx,ny,nz);}
  }
  return best;}
}
public class terrainGenSettings {public double slope=2;public bool linear;public double ruggedness,phase;
 public double Height(UnityEngine.Vector3d p,List<roadGenScript.roadClass> r,List<poiGenScript.poiClass> q){
  return (linear?p.x:Math.Min(0,p.x))*slope+ruggedness*Math.Sin(p.x*0.031+phase)*Math.Cos(p.z*0.027);}}
public class mapSettingScript {public List<poiGenScript> poiGens;public static List<roadGenScript> RoadGens=new List<roadGenScript>();
 // Native static RoadsNear: union over every road generator, notJustChunks = false.
 public static List<roadGenScript.roadClass> RoadsNear(UnityEngine.Vector3d p){
  var list=new List<roadGenScript.roadClass>();
  foreach(var g in RoadGens){var l=g.RoadsNear(p,false,poiGenScript.alignToRoadEmu.anyRoad);if(l!=null&&l.Count>0)list.AddRange(l);}
  return list;}}
public class mainscript {
 public static UnityEngine.Vector3d Snap(UnityEngine.Vector3d p,float r){return new UnityEngine.Vector3d(Math.Round(p.x),0,Math.Round(p.z));}
 public static UnityEngine.Vector3d ChunkKey(UnityEngine.Vector3d p,float size){return new UnityEngine.Vector3d(Math.Floor((p.x+size/2)/size)*size,0,Math.Floor((p.z+size/2)/size)*size);}
 public static UnityEngine.Vector3d GetChunkPos(UnityEngine.Vector3d p,float size){return ChunkKey(p,size);}
 public static UnityEngine.Vector3d GetChunkPos(UnityEngine.Vector3d p,int x,int z,float size){var c=ChunkKey(p,size);return new UnityEngine.Vector3d(c.x+x*size,0,c.z+z*size);} }
public class poiGenScript {
 public enum alignToRoadEmu {none,anyRoad,onlyMainRoad,onlySideRoad}
 public class quadClass {public UnityEngine.Vector3d[] corners;}
 public class tempPoiClass {public UnityEngine.Vector3d pos;public int tipus;public alignToRoadEmu roadType;public float roadDist,minSlope,maxSlope,maxDist;}
 public class chunkClass {public Dictionary<UnityEngine.Vector3d,poiClass> pois=new Dictionary<UnityEngine.Vector3d,poiClass>();public List<tempPoiClass> poiToPlace=new List<tempPoiClass>();}
 public class poiClass {public UnityEngine.Vector3d pos,posID;public int id;public UnityEngine.Vector3 rot;public bool saved,aligned;public poiScript pobj;public List<quadClass> noObjQuads=new List<quadClass>(),terrainHoleQuads=new List<quadClass>();}
 public Dictionary<UnityEngine.Vector3d,chunkClass> chunks=new Dictionary<UnityEngine.Vector3d,chunkClass>();
 public mapSettingScript mapSetting;public terrainGenSettings terrainGen=new terrainGenSettings();public bool roadAffects;public roadGenScript roadgen;
 public float round=1,alignMaxRadiusMultiplier=1,chunkSize=100;public int index;
 public Dictionary<UnityEngine.Vector3d,bool> poisFromSave=new Dictionary<UnityEngine.Vector3d,bool>();
 // Native AlignPosToRoad: the nearest generated-or-generated-on-query road bone, then a
 // deterministic offset from it. Reads roads only through roadgen.RoadsNear (lazy generation).
 public bool AlignPosToRoad(UnityEngine.Vector3d p,out UnityEngine.Vector3d result,out UnityEngine.Vector3 rot,out bool main,bool middle,alignToRoadEmu type,float dist,float min,float max,float maxDist){
  var roads=roadgen.RoadsNear(p,true,type);
  var r=roadGenScript.NearestPointOnRoadBone(p,roads,out result);
  rot=UnityEngine.Vector3.zero;
  if(r==null){main=false;return false;}
  double f=0.5+0.5*Math.Sin(result.x*0.013+result.z*0.007);
  double d=dist+(maxDist-dist)*f;
  result=new UnityEngine.Vector3d(result.x+d*0.6,result.y,result.z+d*0.8);
  rot=new UnityEngine.Vector3(0,(float)(90.0+f*45.0),0);
  main=r.roadType==roadGenScript.roadTypeEmu.mainRoad;
  return true;}
 public poiClass AddPoi(UnityEngine.Vector3d p,UnityEngine.Vector3 r,int id,bool aligned){return AddPoi(p,r,(itemdatabase.b)id,aligned);}
 public poiClass AddPoi(UnityEngine.Vector3d p,UnityEngine.Vector3 r,itemdatabase.b id,bool aligned){
  var key=PoiSlopePlacement.ResolveIdentity(p,round,this);bool saved=poisFromSave.ContainsKey(key);if(saved)poisFromSave.Remove(key);
  var poi=new poiClass{pos=p,posID=key,id=(int)id,rot=r,saved=saved,aligned=aligned};
  var ck=mainscript.GetChunkPos(p,chunkSize);chunkClass c;if(!chunks.TryGetValue(ck,out c)){c=new chunkClass();chunks[ck]=c;}
  c.pois[key]=poi;
  return poi;}
}
namespace TldMp {
 // Stand-in for runtime/RoadTerrainClearance.cs: native noise, then the road blend when enabled.
 public static class RoadTerrainClearance {
  public static double Height(terrainGenSettings s,UnityEngine.Vector3d p,List<roadGenScript.roadClass> r,List<poiGenScript.poiClass> q,bool enabled,double c){
   double h=s.Height(p,null,q);
   if(!enabled||r==null||r.Count==0)return h;
   UnityEngine.Vector3d n;var road=roadGenScript.NearestPointOnRoadBone(p,r,out n);
   if(road==null)return h;
   double dx=n.x-p.x,dz=n.z-p.z;double d=Math.Sqrt(dx*dx+dz*dz);
   double w=1.0/(1.0+d*0.02);
   return h*(1-w)+(n.y-c)*w;}
 }
 public static class WorldBiomePolicy {public static int PackConfig(int c,int b){return c|(b<<4);}public static bool TryUnpackConfig(int p,out int c,out int b){c=p&15;b=(p>>4)&3;return (p&~63)==0;}}
}

// Runs AddRandomPoi (version 4, i.e. AddProfileFour / AddProfileFourCached) on seeded worlds
// with the cache OFF and ON, and requires byte-identical transcripts. The transcript covers
// every returned position, rotation, aligned/saved flag and identity key (as exact bits),
// the fallback counters, the ledger encoding, and the ordered list of generated roads.
// Also: road-state epoch semantics, stand-in lazy generation, a mutation check (epoch ignored
// must diverge), and the same comparison with road pregeneration on.
public static class PoiSlopePlacementCacheTests {
 static int checks; static void Check(bool x){checks++;if(!x)throw new Exception("check "+checks);}
 sealed class Stats {
  public int placements, moved, aligned, nulls, exceptions, capScenarios;
  public long siteCalls, rangeCalls, generations, rawHits, rawMisses, pendingHits, pendingMisses, epochInvalid, preGen;
  public void Add(Stats o){
   placements+=o.placements;moved+=o.moved;aligned+=o.aligned;nulls+=o.nulls;exceptions+=o.exceptions;capScenarios+=o.capScenarios;
   siteCalls+=o.siteCalls;rangeCalls+=o.rangeCalls;generations+=o.generations;rawHits+=o.rawHits;rawMisses+=o.rawMisses;
   pendingHits+=o.pendingHits;pendingMisses+=o.pendingMisses;epochInvalid+=o.epochInvalid;preGen+=o.preGen;}
 }

 public static void Main(){
  string env=Environment.GetEnvironmentVariable("TLD_MP_POI_CACHE");
  Check(PoiSlopePlacement.PoiCacheEnabled == (env!="0"));
  CheckEpochSemantics();
  CheckStandInLazyGeneration();
  const int seeds=400;
  var total=new Stats();
  for(int seed=0;seed<seeds;seed++)RunPair(seed,false,total);
  // The cached path must actually have run, or the comparison proves nothing.
  Console.WriteLine("aggregate: siteCalls="+total.siteCalls+" rangeCalls="+total.rangeCalls+" moved="+total.moved+" capScenarios="+total.capScenarios+" generations="+total.generations+" rawHits="+total.rawHits+" pendingHits="+total.pendingHits+" rawMisses="+total.rawMisses+" pendingMisses="+total.pendingMisses+" invalid="+total.epochInvalid+" exceptions="+total.exceptions);
  Check(total.siteCalls>0 && total.rangeCalls>0 && total.moved>0 && total.capScenarios>0 && total.generations>0);
  Check(total.rawHits>0 && total.pendingHits>0);
  var pregen=new Stats();
  for(int seed=0;seed<200;seed++)RunPair(seed,true,pregen);
  Check(pregen.preGen>0 && pregen.generations>0);
  int detectedPlain=MutationDivergences(seeds,false);
  int detectedPregen=MutationDivergences(200,true);
  // The targeted probe is deterministic: the stale value must be the one the mutated cache returns.
  Check(StaleProbeMatchesFresh());
  RoadStateEpoch.MutationIgnoreEpochForTest=true;
  bool mutatedMatches;
  try { mutatedMatches=StaleProbeMatchesFresh(); } finally { RoadStateEpoch.MutationIgnoreEpochForTest=false; }
  Check(!mutatedMatches);
  Console.WriteLine("mutation detections: targeted probe rejected by epoch-ignoring cache=true; random sweep plain="+detectedPlain+"/"+seeds+" pregen="+detectedPregen+"/200");
  Check(detectedPlain>0);
  PoiSlopePlacement.PoiCacheEnabled=true;
  PoiSlopePlacement.PreGenEnabled=false;
  Console.WriteLine("PASS "+checks+" checks: "+seeds+" seeded worlds, "+total.placements+" placements identical with cache OFF/ON (pregen off); moved="+total.moved+" aligned="+total.aligned+" exceptions="+total.exceptions+" capScenarios="+total.capScenarios+" generatedRoads="+total.generations);
  Console.WriteLine("  pregen on: 200 seeded worlds, "+pregen.placements+" placements identical; preGenRoadsNearCalls="+pregen.preGen+" generatedRoads="+pregen.generations);
  Console.WriteLine("  hits pregen off: rawHeight "+Rate(total.rawHits,total.rawMisses)+" (hits="+total.rawHits+" misses="+total.rawMisses+"); pending anchor "+Rate(total.pendingHits,total.pendingMisses)+" (hits="+total.pendingHits+" misses="+total.pendingMisses+"); epochInvalidations="+total.epochInvalid+"; siteOccupiedCalls="+total.siteCalls+"; terrainRangeCalls="+total.rangeCalls);
  Console.WriteLine("  hits pregen on: rawHeight "+Rate(pregen.rawHits,pregen.rawMisses)+" (hits="+pregen.rawHits+" misses="+pregen.rawMisses+"); pending anchor "+Rate(pregen.pendingHits,pregen.pendingMisses)+" (hits="+pregen.pendingHits+" misses="+pregen.pendingMisses+"); epochInvalidations="+pregen.epochInvalid);
  Console.WriteLine("  mutation (epoch ignored) diverged from cache OFF on "+detectedPlain+"/"+seeds+" seeds (pregen off) and "+detectedPregen+"/200 seeds (pregen on)");
 }

 static string Rate(long hits,long misses){long n=hits+misses;return n==0?"n/a":(100.0*hits/n).ToString("F1")+"%";}

 static void RunPair(int seed,bool pregen,Stats acc){
  var off=new Stats();var on=new Stats();
  long siteBefore=PoiSlopePlacement.SiteOccupiedCalls, rangeBefore=PoiSlopePlacement.TerrainRangeCalls;
  string a=Run(false,pregen,seed,off);
  Check(PoiSlopePlacement.SiteOccupiedCalls==siteBefore && PoiSlopePlacement.TerrainRangeCalls==rangeBefore);
  string b=Run(true,pregen,seed,on);
  if(!string.Equals(a,b,StringComparison.Ordinal))throw new Exception("cache changed the result for seed "+seed+" pregen="+pregen);
  acc.Add(on);
 }

 // Each seed runs the cache ON with the epoch ignored; the stock OFF run is the reference.
 // A cache that ignores the epoch must produce a different transcript for some seeds.
 static int MutationDivergences(int seeds,bool pregen){
  int detected=0;
  RoadStateEpoch.MutationIgnoreEpochForTest=true;
  try{
   for(int seed=0;seed<seeds;seed++){
    var scratch=new Stats();
    string a=Run(false,pregen,seed,scratch);
    string b=Run(true,pregen,seed,scratch);
    if(!string.Equals(a,b,StringComparison.Ordinal))detected++;
   }
  } finally { RoadStateEpoch.MutationIgnoreEpochForTest=false; }
  return detected;
 }

 // Reads a height, generates a road that changes it, then reads again through the memo.
 // Returns true when the memo value equals a fresh stock RawHeight after the generation.
 static bool StaleProbeMatchesFresh(){
  WorldPlacementPolicy.SetVersion(4);
  var rg=new roadGenScript{chunkSize=100,innerChunkSize=50};
  var p=new UnityEngine.Vector3d(20,0,20);
  var key=mainscript.GetChunkPos(p,rg.chunkSize);
  var dcc=new roadGenScript.dotChunkClass();rg.chunks[key]=dcc;
  var r=new roadGenScript.roadClass{start=new UnityEngine.Vector3d(22,0,20),end=new UnityEngine.Vector3d(80,0,30),id=3,selfChunk=key,selfID=0};
  dcc.roads.Add(r);
  mapSettingScript.RoadGens=new List<roadGenScript>{rg};
  var gen=new poiGenScript{roadAffects=true,roadgen=rg,chunkSize=100,terrainGen=new terrainGenSettings{slope=1,ruggedness=2,phase=0.5}};
  double before=PoiSlopePlacement.RawHeightMemoForTest(gen,p);
  Check(before==PoiSlopePlacement.RawHeightStockForTest(gen,p));
  rg.RoadsNear(p,true,poiGenScript.alignToRoadEmu.anyRoad);
  Check(r.generated);
  double fresh=PoiSlopePlacement.RawHeightStockForTest(gen,p);
  Check(fresh!=before);
  return PoiSlopePlacement.RawHeightMemoForTest(gen,p)==fresh;
 }

 static void CheckEpochSemantics(){
  long e0=RoadStateEpoch.Current;
  Check(RoadStateEpoch.UnchangedSince(e0));
  RoadStateEpoch.Bump();
  Check(RoadStateEpoch.Current==e0+1);
  Check(!RoadStateEpoch.UnchangedSince(e0));
  Check(RoadStateEpoch.UnchangedSince(RoadStateEpoch.Current));
  RoadStateEpoch.MutationIgnoreEpochForTest=true;
  long frozen=RoadStateEpoch.Current;RoadStateEpoch.Bump();
  Check(RoadStateEpoch.Current==frozen && RoadStateEpoch.UnchangedSince(e0));
  RoadStateEpoch.MutationIgnoreEpochForTest=false;
  Check(RoadStateEpoch.Current==e0+2);
 }

 // Stand-in lazy generation: a query generates, the fallback reaches chunk roads, and the epoch moves.
 static void CheckStandInLazyGeneration(){
  var rg=new roadGenScript{chunkSize=100,innerChunkSize=50};
  var start=new UnityEngine.Vector3d(10,0,10);
  var key=mainscript.GetChunkPos(start,rg.chunkSize);
  var dcc=new roadGenScript.dotChunkClass();rg.chunks[key]=dcc;
  var r=new roadGenScript.roadClass{start=start,end=new UnityEngine.Vector3d(60,0,10),id=7,selfChunk=key,selfID=0};
  dcc.roads.Add(r);
  roadGenScript.GenerationLog.Clear();
  Check(rg.RoadsNear(start,false,poiGenScript.alignToRoadEmu.anyRoad).Count==0 && !r.generated);
  long e=RoadStateEpoch.Current;
  var near=rg.RoadsNear(start,true,poiGenScript.alignToRoadEmu.anyRoad);
  Check(near.Count>=1 && AllAre(near,r) && r.generated && RoadStateEpoch.Current>e);
  Check(roadGenScript.GenerationLog.Count==1 && roadGenScript.GenerationLog[0]==7);
  // A road sits in every inner cell it touches, so the native list may repeat it.
  var after=rg.RoadsNear(start,false,poiGenScript.alignToRoadEmu.anyRoad);
  Check(after.Count>=1 && AllAre(after,r));
  // Empty-list fallback: a far query finds nothing through inner chunks, then reaches the chunk list.
  var far=rg.RoadsNear(new UnityEngine.Vector3d(5000,0,5000),true,poiGenScript.alignToRoadEmu.anyRoad);
  Check(far.Count==1 && far[0]==r);
  Check(rg.RoadsNear(new UnityEngine.Vector3d(5000,0,5000),false,poiGenScript.alignToRoadEmu.anyRoad).Count==0);
  Check(roadGenScript.GenerationLog.Count==1);
  roadGenScript.GenerationLog.Clear();
 }

 static bool AllAre(List<roadGenScript.roadClass> list,roadGenScript.roadClass road){
  foreach(var x in list)if(x!=road)return false;
  return true;}

 static double U(Random r,double lo,double hi){return lo+(hi-lo)*r.NextDouble();}
 static UnityEngine.Vector3d InChunk(Random r,UnityEngine.Vector3d key,float size){return new UnityEngine.Vector3d(key.x+U(r,-size/2,size/2),0,key.z+U(r,-size/2,size/2));}
 static poiGenScript.quadClass Quad(Random r,UnityEngine.Vector3d c){
  var q=new poiGenScript.quadClass{corners=new UnityEngine.Vector3d[4]};
  for(int i=0;i<4;i++)q.corners[i]=new UnityEngine.Vector3d(c.x+U(r,-18,18),0,c.z+U(r,-18,18));
  return q;}
 static UnityEngine.Collider[] RandomColliders(Random r){
  int n=1+r.Next(3);var list=new UnityEngine.Collider[n];
  for(int i=0;i<n;i++){
   var c=new UnityEngine.Vector3((float)U(r,-2,2),0,(float)U(r,-2,2));
   var s=new UnityEngine.Vector3((float)U(r,4,34),(float)U(r,2,8),(float)U(r,4,34));
   list[i]=new UnityEngine.BoxCollider{center=c,size=s,bounds=new UnityEngine.Bounds(c,s),isTrigger=r.Next(8)==0,enabled=r.Next(10)!=0};
  }
  return list;}

 static List<poiGenScript> Build(Random rng,bool cap,out int genCount){
  itemdatabase.s.prefab=new UnityEngine.GameObject{colliders=RandomColliders(rng)};
  var map=new mapSettingScript{poiGens=new List<poiGenScript>()};
  mapSettingScript.RoadGens=new List<roadGenScript>();
  genCount=1+rng.Next(3);
  int roadId=0;
  for(int g=0;g<genCount;g++){
   var gen=new poiGenScript{index=g,chunkSize=(float)(90+rng.Next(90)),round=1,alignMaxRadiusMultiplier=(float)U(rng,0.5,2.0),
    roadAffects=rng.Next(10)<6,mapSetting=map,
    terrainGen=new terrainGenSettings{slope=U(rng,0,3),linear=rng.Next(2)==0,ruggedness=U(rng,0,4),phase=U(rng,0,6)}};
   if(rng.Next(10)!=0){
    var rg=new roadGenScript{chunkSize=(float)(120+rng.Next(200)),innerChunkSize=(float)(30+rng.Next(40)),hasMainRoad=rng.Next(3)==0};
    for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++){var k=mainscript.GetChunkPos(new UnityEngine.Vector3d(0,0,0),x,z,rg.chunkSize);if(!rg.chunks.ContainsKey(k))rg.chunks[k]=new roadGenScript.dotChunkClass();}
    int roadCount=rng.Next(7);
    for(int i=0;i<roadCount;i++){
     var rc=new roadGenScript.roadClass{start=new UnityEngine.Vector3d(U(rng,-300,300),0,U(rng,-300,300)),end=new UnityEngine.Vector3d(U(rng,-300,300),0,U(rng,-300,300)),
      roadType=rng.Next(2)==0?roadGenScript.roadTypeEmu.mainRoad:roadGenScript.roadTypeEmu.sideRoad,id=roadId++};
     var key=mainscript.GetChunkPos(rc.start,rg.chunkSize);
     roadGenScript.dotChunkClass dcc;
     if(!rg.chunks.TryGetValue(key,out dcc)){dcc=new roadGenScript.dotChunkClass();rg.chunks[key]=dcc;}
     rc.selfChunk=key;rc.selfID=dcc.roads.Count;dcc.roads.Add(rc);
    }
    // Native UpdGenRoad generates whole chunks on its own schedule; about half the chunks are done here.
    foreach(var kv in rg.chunks)if(rng.Next(2)==0)foreach(var r in kv.Value.roads)if(!r.generated)rg.Generate(r);
    gen.roadgen=rg;mapSettingScript.RoadGens.Add(rg);
   }
   map.poiGens.Add(gen);
  }
  foreach(var gen in map.poiGens){
   var origin=new UnityEngine.Vector3d(0,0,0);
   var keys=new List<UnityEngine.Vector3d>();
   for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++){
    var key=mainscript.GetChunkPos(origin,x,z,gen.chunkSize);keys.Add(key);
    if(!gen.chunks.ContainsKey(key))gen.chunks[key]=new poiGenScript.chunkClass();}
   int fixedCount=rng.Next(0,12);
   for(int i=0;i<fixedCount;i++){
    var key=keys[rng.Next(keys.Count)];var chunk=gen.chunks[key];
    var p=InChunk(rng,key,gen.chunkSize);
    var poi=new poiGenScript.poiClass{pos=p,posID=mainscript.Snap(p,gen.round),id=rng.Next(3)==0?-1-rng.Next(2):1+rng.Next(2),rot=new UnityEngine.Vector3(0,(float)U(rng,0,360),0),aligned=rng.Next(2)==0};
    if(poi.id<0)poi.pobj=new poiScript{colliders=RandomColliders(rng)};
    if(rng.Next(4)==0)poi.noObjQuads.Add(Quad(rng,p));
    if(rng.Next(5)==0)poi.terrainHoleQuads.Add(Quad(rng,p));
    chunk.pois[poi.posID]=poi;}
   int pendingCount=rng.Next(0,10);
   for(int i=0;i<pendingCount;i++){
    var key=keys[rng.Next(keys.Count)];var chunk=gen.chunks[key];
    chunk.poiToPlace.Add(new poiGenScript.tempPoiClass{pos=InChunk(rng,key,gen.chunkSize),tipus=rng.Next(12)==0?-1:1+rng.Next(2),
     roadType=(poiGenScript.alignToRoadEmu)rng.Next(4),roadDist=(float)U(rng,0,12),minSlope=(float)U(rng,0,0.5),maxSlope=(float)U(rng,0.5,2),maxDist=(float)U(rng,4,24)});}
  }
  if(cap){
   // Fill one chunk past the 2048 scan cap with entries that never intersect, so the cap decides.
   var gen=map.poiGens[0];var key=mainscript.GetChunkPos(new UnityEngine.Vector3d(0,0,0),0,0,gen.chunkSize);
   var chunk=gen.chunks[key];int target=2040+rng.Next(17);
   for(int i=chunk.pois.Count;i<target;i++){
    var p=InChunk(rng,key,gen.chunkSize);
    var id=new UnityEngine.Vector3d(-5000000-i,0,0);
    chunk.pois[id]=new poiGenScript.poiClass{pos=p,posID=id,id=-1,pobj=new poiScript()};}
  }
  return map.poiGens;}

 static string Bits(double d){return BitConverter.DoubleToInt64Bits(d).ToString();}
 static void AppendVec(StringBuilder sb,UnityEngine.Vector3d v){sb.Append(Bits(v.x)).Append(',').Append(Bits(v.y)).Append(',').Append(Bits(v.z)).Append(';');}
 static void AppendRot(StringBuilder sb,UnityEngine.Vector3 v){sb.Append(Bits(v.x)).Append(',').Append(Bits(v.y)).Append(',').Append(Bits(v.z)).Append(';');}

 static string Run(bool cache,bool pregen,int seed,Stats st){
  PoiSlopePlacement.PoiCacheEnabled=cache;
  PoiSlopePlacement.PreGenEnabled=pregen;
  WorldPlacementPolicy.SetVersion(4);
  WorldRelocationLedger.Clear();
  var rng=new Random(seed);
  bool cap=seed%25==24;
  if(cap)st.capScenarios++;
  int genCount;var gens=Build(rng,cap,out genCount);
  roadGenScript.GenerationLog.Clear();
  var sb=new StringBuilder();
  long fallbackBefore=PoiSlopePlacement.ProfileFourFallbackCount;
  long siteBefore=PoiSlopePlacement.SiteOccupiedCalls,rangeBefore=PoiSlopePlacement.TerrainRangeCalls;
  long rawHitBefore=PoiSlopePlacement.RawHeightHits,rawMissBefore=PoiSlopePlacement.RawHeightMisses;
  long pendHitBefore=PoiSlopePlacement.PendingHits,pendMissBefore=PoiSlopePlacement.PendingMisses;
  long invalidBefore=PoiSlopePlacement.EpochInvalidations,preGenBefore=PoiSlopePlacement.PreGenCalls;
  int placements=3+rng.Next(4);
  UnityEngine.Vector3d lastPos=new UnityEngine.Vector3d(0,0,0);
  for(int k=0;k<placements;k++){
   var gen=gens[rng.Next(gens.Count)];
   // About a third of placements revisit the previous position, so a height read at that position
   // can come back after the earlier placement generated roads. That is the stale-read window.
   bool revisit=rng.Next(3)==0;
   var pos=(k>0 && revisit)?lastPos:new UnityEngine.Vector3d(U(rng,-40,40),0,U(rng,-40,40));
   lastPos=pos;
   var rot=new UnityEngine.Vector3(0,(float)U(rng,0,360),0);
   bool aligned=rng.Next(3)==0;
   st.placements++;
   sb.Append("p").Append(k).Append('|');
   try{
    var result=PoiSlopePlacement.AddRandomPoi(gen,pos,rot,itemdatabase.b.house,aligned);
    if(result==null){st.nulls++;sb.Append("null|");}
    else{
     if(!result.pos.Equals(pos))st.moved++;
     if(result.aligned)st.aligned++;
     AppendVec(sb,result.pos);AppendVec(sb,result.posID);AppendRot(sb,result.rot);
     sb.Append(result.aligned).Append(result.saved).Append(result.id).Append('|');
    }
   }catch(Exception e){st.exceptions++;sb.Append("EX:").Append(e.GetType().FullName).Append('|');}
  }
  st.siteCalls+=PoiSlopePlacement.SiteOccupiedCalls-siteBefore;
  st.rangeCalls+=PoiSlopePlacement.TerrainRangeCalls-rangeBefore;
  st.rawHits+=PoiSlopePlacement.RawHeightHits-rawHitBefore;st.rawMisses+=PoiSlopePlacement.RawHeightMisses-rawMissBefore;
  st.pendingHits+=PoiSlopePlacement.PendingHits-pendHitBefore;st.pendingMisses+=PoiSlopePlacement.PendingMisses-pendMissBefore;
  st.epochInvalid+=PoiSlopePlacement.EpochInvalidations-invalidBefore;st.preGen+=PoiSlopePlacement.PreGenCalls-preGenBefore;
  st.generations+=roadGenScript.GenerationLog.Count;
  sb.Append("generated=");
  foreach(int id in roadGenScript.GenerationLog)sb.Append(id).Append(',');
  sb.Append('|');
  sb.Append("fallbacks=").Append(PoiSlopePlacement.ProfileFourFallbackCount-fallbackBefore).Append('|');
  sb.Append("fallbackRange=").Append(Bits(PoiSlopePlacement.LastFallbackRange)).Append('|');
  sb.Append("ledger=").Append(WorldRelocationLedger.Stamp("native"));
  if(!cache)Check(st.siteCalls==0 && st.rangeCalls==0 && st.rawHits==0 && st.pendingHits==0 && st.epochInvalid==0);
  return sb.ToString();
 }
}
