using System;
using System.Collections.Generic;
using TldMp;
namespace UnityEngine {
 public static class Debug {public static void LogWarning(object s){}}
 public struct Vector3 {public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero;public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 Scale(Vector3 a,Vector3 b){return new Vector3(a.x*b.x,a.y*b.y,a.z*b.z);} public float x,y,z; public double magnitude {get{return Math.Sqrt(x*x+y*y+z*z);}} }
 public struct Matrix4x4 {public static Matrix4x4 operator*(Matrix4x4 a,Matrix4x4 b){return a;}public Vector3 MultiplyPoint3x4(Vector3 p){return p;}}
 public struct Bounds {public Vector3 center,extents;public Vector3 min{get{return new Vector3(center.x-extents.x,center.y-extents.y,center.z-extents.z);}}public Vector3 max{get{return new Vector3(center.x+extents.x,center.y+extents.y,center.z+extents.z);}}public Bounds(Vector3 c,Vector3 s){center=c;extents=new Vector3(s.x/2,s.y/2,s.z/2);}}
 public class Collider {public bool isTrigger,enabled=true;public Bounds bounds;public Transform transform=new Transform();}public class BoxCollider:Collider{public Vector3 center,size;}public class Mesh {public Bounds bounds;}public class MeshCollider:Collider {public Mesh sharedMesh;}
 public class Transform {public Matrix4x4 worldToLocalMatrix,localToWorldMatrix; public Vector3 localPosition,localScale; }
 public class GameObject {public Transform transform=new Transform(); public Collider[] colliders=new Collider[0];public terrainHeightAlignToBuildingScript[] align; public T[] GetComponentsInChildren<T>(bool inactive){return typeof(T)==typeof(Collider)?(T[])(object)colliders:(T[])(object)align;} }
 public struct Vector3d {public double x,y,z;public Vector3d(double x,double y,double z){this.x=x;this.y=y;this.z=z;} }
}
public class poiScript {public T[] GetComponentsInChildren<T>(bool x){return new T[0];}}
public class terrainHeightAlignToBuildingScript {public UnityEngine.Transform transform=new UnityEngine.Transform();}
public class itemdatabase {public enum b { house=1 } public static itemdatabase s=new itemdatabase();public UnityEngine.GameObject prefab;public UnityEngine.GameObject Building(int id){return prefab;} }
public class roadGenScript {
 public float chunkSize=10000; public roadClass active=new roadClass(); public enum roadTypeEmu {mainRoad,sideRoad} public class roadClass {public UnityEngine.Vector3d start=new UnityEngine.Vector3d(-1000,0,0),end=new UnityEngine.Vector3d(1000,0,0);public roadTypeEmu roadType=roadTypeEmu.mainRoad;}
 public bool RoadsDoneAtPlace(UnityEngine.Vector3d p){return true;} public List<roadClass> RoadsNear(UnityEngine.Vector3d p,bool n,poiGenScript.alignToRoadEmu t){return new List<roadClass>{active};}
 public static roadClass NearestPointOnRoadBone(UnityEngine.Vector3d p,List<roadClass> roads,out UnityEngine.Vector3d nearest){nearest=new UnityEngine.Vector3d(p.x,0,0);return roads[0];}
}
public class terrainGenSettings {public double slope=2;public bool linear;public double Height(UnityEngine.Vector3d p,List<roadGenScript.roadClass> roads,List<poiGenScript.poiClass> pois){return (linear?p.x:Math.Min(0,p.x))*slope;} }
public class mapSettingScript {public List<poiGenScript> poiGens;public static List<roadGenScript.roadClass> RoadsNear(UnityEngine.Vector3d p){return null;} }
public class mainscript {public static UnityEngine.Vector3d Snap(UnityEngine.Vector3d p,float r){return new UnityEngine.Vector3d(Math.Round(p.x),0,Math.Round(p.z));} public static UnityEngine.Vector3d GetChunkPos(UnityEngine.Vector3d p,int x,int z,float size){var c=GetChunkPos(p,size);return new UnityEngine.Vector3d(c.x+x,0,c.z+z);} public static UnityEngine.Vector3d GetChunkPos(UnityEngine.Vector3d p,float size){return new UnityEngine.Vector3d(Math.Floor((p.x+size/2)/size),0,Math.Floor((p.z+size/2)/size));} }
public class poiGenScript {
 public enum alignToRoadEmu {none,anyRoad,onlyMainRoad,onlySideRoad}
 public class quadClass {public UnityEngine.Vector3d[] corners;}
 public class tempPoiClass {public UnityEngine.Vector3d pos;public int tipus;public alignToRoadEmu roadType;public float roadDist,minSlope,maxSlope,maxDist;}
 public class chunkClass {public Dictionary<UnityEngine.Vector3d,poiClass> pois=new Dictionary<UnityEngine.Vector3d,poiClass>();public List<tempPoiClass> poiToPlace=new List<tempPoiClass>();}
 public class poiClass {public UnityEngine.Vector3d pos,posID;public int id;public UnityEngine.Vector3 rot;public bool saved,aligned;public poiScript pobj;public List<quadClass> noObjQuads=new List<quadClass>(),terrainHoleQuads=new List<quadClass>();}
 public Dictionary<UnityEngine.Vector3d,chunkClass> chunks=new Dictionary<UnityEngine.Vector3d,chunkClass>();public mapSettingScript mapSetting; public terrainGenSettings terrainGen=new terrainGenSettings();public bool roadAffects;public roadGenScript roadgen;public float round=1,alignMaxRadiusMultiplier=1,chunkSize=10000; public int index;
 public Dictionary<UnityEngine.Vector3d,bool> poisFromSave=new Dictionary<UnityEngine.Vector3d,bool>();
 public bool AlignPosToRoad(UnityEngine.Vector3d p,out UnityEngine.Vector3d result,out UnityEngine.Vector3 rot,out bool main,bool middle,alignToRoadEmu type,float dist,float min,float max,float maxDist){result=new UnityEngine.Vector3d(p.x,terrainGen.Height(p,null,null),p.z<0?-dist:dist);rot=new UnityEngine.Vector3(0,90,0);main=true;return true;}
 public poiClass AddPoi(UnityEngine.Vector3d p,UnityEngine.Vector3 r,int id,bool aligned){return AddPoi(p,r,(itemdatabase.b)id,aligned);}
 public poiClass AddPoi(UnityEngine.Vector3d p,UnityEngine.Vector3 r,itemdatabase.b id,bool aligned){var key=PoiSlopePlacement.ResolveIdentity(p,round,this);bool saved=poisFromSave.ContainsKey(key);if(saved)poisFromSave.Remove(key);return new poiClass{pos=p,posID=key,id=(int)id,rot=r,saved=saved,aligned=aligned};}
}
namespace TldMp {
 public static class RoadTerrainClearance {public static double Height(terrainGenSettings s,UnityEngine.Vector3d p,List<roadGenScript.roadClass> r,List<poiGenScript.poiClass> q,bool enabled,double c){return s.Height(p,r,q);} }
 public static class WorldBiomePolicy {public static int PackConfig(int c,int b){return c|(b<<4);}public static bool TryUnpackConfig(int p,out int c,out int b){c=p&15;b=(p>>4)&3;return (p&~63)==0;}}
}
public static class PoiSlopePlacementReplayTests {
 static int n;static void Check(bool b){n++;if(!b)throw new Exception("check "+n);}
 public static void Main(){
  var align=new terrainHeightAlignToBuildingScript();align.transform.localScale=new UnityEngine.Vector3{x=10};itemdatabase.s.prefab=new UnityEngine.GameObject{align=new[]{align}};
  var original=new UnityEngine.Vector3d(0,0,0);var rot=new UnityEngine.Vector3();
  WorldPlacementPolicy.SetVersion(0);var legacy=PoiSlopePlacement.AddRandomPoi(new poiGenScript(),original,rot,itemdatabase.b.house,false);Check(legacy.pos.x==0);
  WorldPlacementPolicy.SetVersion(1);var first=PoiSlopePlacement.AddRandomPoi(new poiGenScript(),original,rot,itemdatabase.b.house,false);Check(first!=null);Check(first.pos.x>0);Check(!first.saved);
  var reload=new poiGenScript();reload.poisFromSave.Add(mainscript.Snap(first.pos,1),true);var restored=PoiSlopePlacement.AddRandomPoi(reload,original,rot,itemdatabase.b.house,false);
  Check(restored!=null && restored.saved);Check(restored.pos.x==first.pos.x && restored.pos.z==first.pos.z && restored.pos.y==first.pos.y);Check(reload.poisFromSave.Count==0);
  var savedOriginal=new poiGenScript();savedOriginal.poisFromSave.Add(mainscript.Snap(original,1),true);var fixedSaved=PoiSlopePlacement.AddRandomPoi(savedOriginal,original,rot,itemdatabase.b.house,false);Check(fixedSaved.saved && fixedSaved.pos.x==0);
  WorldPlacementPolicy.SetVersion(3);itemdatabase.s.prefab.colliders=new UnityEngine.Collider[]{new UnityEngine.BoxCollider{size=new UnityEngine.Vector3(20,10,20)}};
  var gentle=new poiGenScript();gentle.terrainGen.slope=.2;var hillside=PoiSlopePlacement.AddRandomPoi(gentle,original,rot,itemdatabase.b.house,true);Check(hillside!=null && hillside.pos.x==0 && hillside.pos.z==0);
  var steep=new poiGenScript();steep.terrainGen.slope=2;Check(PoiSlopePlacement.AddRandomPoi(steep,original,rot,itemdatabase.b.house,true)==null);
  WorldPlacementPolicy.SetVersion(4);
  var onRoad=new UnityEngine.Vector3d(0,0,10);var roadRot=new UnityEngine.Vector3(0,90,0);
  var roadGen=new poiGenScript{roadAffects=true,roadgen=new roadGenScript()};
  var moved=PoiSlopePlacement.AddRandomPoi(roadGen,onRoad,roadRot,itemdatabase.b.house,true);
  Check(moved!=null && moved.pos.x>0 && moved.pos.z==10 && moved.aligned && moved.rot.y==90);Check(moved.posID.Equals(mainscript.Snap(onRoad,1)));
  string ledgerSave=WorldRelocationLedger.Stamp("native");WorldRelocationLedger.Clear();WorldRelocationLedger.Restore(ledgerSave);
  var reload4=new poiGenScript{roadAffects=true,roadgen=null};reload4.terrainGen.slope=99;reload4.poisFromSave.Add(mainscript.Snap(onRoad,1),true);reload4.poisFromSave.Add(mainscript.Snap(moved.pos,1),true);
  var restored4=PoiSlopePlacement.AddRandomPoi(reload4,onRoad,roadRot,itemdatabase.b.house,true);
  Check(restored4.saved && restored4.pos.Equals(moved.pos));Check(reload4.poisFromSave.Count==1 && reload4.poisFromSave.ContainsKey(mainscript.Snap(moved.pos,1)));
  Check(PoiSlopePlacement.ResolveIdentity(moved.pos,1,reload4).Equals(mainscript.Snap(moved.pos,1)));
  var hard=new poiGenScript();hard.terrainGen.linear=true;hard.terrainGen.slope=4;
  int count=PoiSlopePlacement.ProfileFourFallbackCount;var fallback=PoiSlopePlacement.AddRandomPoi(hard,original,rot,itemdatabase.b.house,false);
  Check(fallback!=null);Check(PoiSlopePlacement.ProfileFourFallbackCount==count+1);Check(PoiSlopePlacement.LastFallbackRange>PoiSlopePlacement.LastFallbackAllowedRange && !PoiSlopePlacement.LastFallbackRoadAligned);
  WorldRelocationLedger.Clear();
  var occupied=new poiGenScript();var existingChunk=new poiGenScript.chunkClass();occupied.chunks.Add(mainscript.GetChunkPos(original,occupied.chunkSize),existingChunk);
  var blocker=new poiGenScript.poiClass{pos=new UnityEngine.Vector3d(16,0,0),posID=new UnityEngine.Vector3d(16,0,0),id=1};existingChunk.pois.Add(blocker.posID,blocker);
  var around=PoiSlopePlacement.AddRandomPoi(occupied,original,rot,itemdatabase.b.house,false);
  Check(around!=null && Math.Abs(around.pos.x-16)>=20 || around!=null && Math.Abs(around.pos.z)>=20);
  WorldRelocationLedger.Clear();var originalBlocked=new poiGenScript();originalBlocked.terrainGen.slope=0;var originalChunk=new poiGenScript.chunkClass();originalBlocked.chunks.Add(mainscript.GetChunkPos(original,originalBlocked.chunkSize),originalChunk);
  var prior=new poiGenScript.poiClass{pos=original,posID=new UnityEngine.Vector3d(30,0,0),id=1};originalChunk.pois.Add(prior.posID,prior);
  var shifted=PoiSlopePlacement.AddRandomPoi(originalBlocked,original,rot,itemdatabase.b.house,false);Check(shifted!=null && (Math.Abs(shifted.pos.x)>=20 || Math.Abs(shifted.pos.z)>=20));
  WorldRelocationLedger.Clear();var pendingGen=new poiGenScript();var pendingChunk=new poiGenScript.chunkClass();pendingGen.chunks.Add(mainscript.GetChunkPos(original,pendingGen.chunkSize),pendingChunk);
  pendingChunk.poiToPlace.Add(new poiGenScript.tempPoiClass{pos=new UnityEngine.Vector3d(16,0,0),tipus=1});
  var pendingMove=PoiSlopePlacement.AddRandomPoi(pendingGen,original,rot,itemdatabase.b.house,false);
  Check(pendingMove!=null && (Math.Abs(pendingMove.pos.x-16)>=20 || Math.Abs(pendingMove.pos.z)>=20));
  WorldRelocationLedger.Clear();var own=new poiGenScript();own.terrainGen.slope=0;var ownChunk=new poiGenScript.chunkClass();own.chunks.Add(mainscript.GetChunkPos(original,own.chunkSize),ownChunk);ownChunk.poiToPlace.Add(new poiGenScript.tempPoiClass{pos=original,tipus=1});
  Check(PoiSlopePlacement.AddRandomPoi(own,original,rot,itemdatabase.b.house,false).pos.Equals(original));
  WorldRelocationLedger.Clear();var guest=new poiGenScript();var guestId=new UnityEngine.Vector3d(8,0,8);var guestPos=new UnityEngine.Vector3d(40,0,12);var guestRot=new UnityEngine.Vector3(0,90,0);
  var imported=new WorldRelocationLedger.Record{position=guestPos,rotation=guestRot,aligned=true,type=1};Check(WorldRelocationLedger.TryRecord(1,guest.index,0,guestId,imported));
  var placedGuest=PoiSlopePlacement.AddAtIdentity(guest,guestId,guestPos,guestRot,1,true);Check(placedGuest.posID.Equals(guestId) && placedGuest.pos.Equals(guestPos));
  string guestSave=WorldRelocationLedger.Stamp("native");WorldRelocationLedger.Clear();WorldRelocationLedger.Restore(guestSave);var singleplayer=new poiGenScript();singleplayer.poisFromSave.Add(guestId,true);
  var afterGuest=PoiSlopePlacement.AddRandomPoi(singleplayer,guestId,rot,itemdatabase.b.house,true);Check(afterGuest.saved && afterGuest.posID.Equals(guestId) && afterGuest.pos.Equals(guestPos) && afterGuest.rot.y==90 && afterGuest.aligned);
  Console.WriteLine("PASS "+n+" POI save/reload replay checks");
 }
}
