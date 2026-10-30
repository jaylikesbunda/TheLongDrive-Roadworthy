"""Run the production roadside search with deterministic road/terrain stand-ins."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class StartingStopTests(unittest.TestCase):
    def test_road_ground_and_house_clearance(self):
        source = (ROOT / 'runtime/GroundDiagnostics.cs').read_text(encoding='utf-8')
        start = source.index('static bool StartingStopDestination(')
        end = source.index('public static void TickBusPlacement', start)
        placement = source[source.index('public static bool TryBusPlacement('):source.index('// Only the child of the starting house moves.')]
        fixture = r'''
using System;using System.Collections.Generic;
public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero{get{return new Vector3();}}public static Vector3 one{get{return new Vector3(1,1,1);}}public static Vector3 operator *(Vector3 v,float n){return new Vector3(v.x*n,v.y*n,v.z*n);}public float sqrMagnitude{get{return x*x+y*y+z*z;}}public void Normalize(){float m=(float)Math.Sqrt(sqrMagnitude);x/=m;y/=m;z/=m;}public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}public static Vector3 operator -(Vector3 a){return new Vector3(-a.x,-a.y,-a.z);}}
public struct Vector3d {public double x,y,z;public Vector3d(double a,double b,double c){x=a;y=b;z=c;}public static Vector3d operator +(Vector3d a,Vector3d b){return new Vector3d(a.x+b.x,a.y+b.y,a.z+b.z);}}
public struct Bounds {public Vector3 center,extents;public Bounds(Vector3 c,Vector3 size){center=c;extents=new Vector3(size.x/2,size.y/2,size.z/2);}public Vector3 min{get{return center-extents;}}public Vector3 max{get{return new Vector3(center.x+extents.x,center.y+extents.y,center.z+extents.z);}}public void Encapsulate(Bounds b){var a=min;var c=max;var d=b.min;var e=b.max;var lo=new Vector3(Math.Min(a.x,d.x),Math.Min(a.y,d.y),Math.Min(a.z,d.z));var hi=new Vector3(Math.Max(c.x,e.x),Math.Max(c.y,e.y),Math.Max(c.z,e.z));center=new Vector3((lo.x+hi.x)/2,(lo.y+hi.y)/2,(lo.z+hi.z)/2);extents=new Vector3((hi.x-lo.x)/2,(hi.y-lo.y)/2,(hi.z-lo.z)/2);}}
public class Transform {public Transform parent;public Vector3 lossyScale=new Vector3(1,1,1);public Vector3 position,right=new Vector3(1,0,0);public MeshRenderer[] Renderers=new MeshRenderer[0];public newRandomStuffSpawnScript[] Spawners=new newRandomStuffSpawnScript[0];public bool IsChildOf(Transform t){for(var p=this;p!=null;p=p.parent)if(p==t)return true;return false;}public Vector3 TransformPoint(Vector3 v){return new Vector3(v.x+position.x,v.y+position.y,v.z+position.z);}public T[] GetComponentsInChildren<T>(bool active) where T:class{return typeof(T)==typeof(MeshRenderer)?Renderers as T[]:Spawners as T[];}}
public class newRandomStuffSpawnScript {public item[] items;public class item {public place[] places;}public class place {public Transform T;}}
public class MeshRenderer {public Transform transform;public Bounds bounds;}
public class poiScript {public Transform transform=new Transform();public MeshRenderer[] Renderers;public T[] GetComponentsInChildren<T>(bool active) where T:class{return Renderers as T[];}}
public class terrainGenScript {}
public class Mesh {public Vector3[] vertices=new[]{new Vector3(-100,0,-4),new Vector3(-100,0,4),new Vector3(100,0,-4),new Vector3(100,0,4)};public int[] triangles=new[]{0,1,2,1,3,2};public Bounds bounds=new Bounds(new Vector3(),new Vector3(1093,10000,100));}
public class Skin {public Mesh sharedMesh=new Mesh();public Transform transform=new Transform();}
public class roadscript {public Mesh bakedMesh=new Mesh();public Skin skinnedMeshRenderer=new Skin();}
public class Prefab {public roadscript road=new roadscript();public T GetComponent<T>() where T:class{return road as T;}}
public class itemdatabase {public static itemdatabase s=new itemdatabase();public Prefab Road(int id){return new Prefab();}}
public static class mainscript {public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d(p.x,p.y,p.z);}public static Vector3 UnityPosFromGlobal(Vector3d p){return new Vector3((float)p.x,(float)p.y,(float)p.z);}}
public static class roadGenScript {public class roadElementClass {public Prefab obj=new Prefab();public int roadID;public List<Vector3d> bonePos=new List<Vector3d>();}public class roadClass {public bool generated=true;public float terrainAlignMaxDist=8;public List<roadElementClass> roadElements=new List<roadElementClass>();}public static roadClass NearestPointOnRoadBone(Vector3d p,List<roadClass> roads,out Vector3d near){near=new Vector3d(p.x-1000,0,0);return roads==null || roads.Count==0?null:roads[0];}}
public static class mapSettingScript {public static List<roadGenScript.roadClass> Roads=new List<roadGenScript.roadClass>();public static List<roadGenScript.roadClass> RoadsNear(Vector3d p){return Roads;}}
public static class Debug {public static void Log(string value){}}
public static class Adapter {public static Transform Marker;static Transform StartingBusMarker(poiScript h){return Marker;}static object Map(){return null;}static terrainGenScript NearGen(object m){return new terrainGenScript();}static string F(double n){return n.ToString();}static HashSet<poiScript> busPlacementComplete=new HashSet<poiScript>();static List<poiScript> busPlacementPending=new List<poiScript>();static Dictionary<poiScript,float> busPlacementStarted=new Dictionary<poiScript,float>();static string busPlacementReason; PLACEMENT public static int GroundMode;static double ColliderY(terrainGenScript gen,Vector3d p,out bool tile,out bool collider){tile=collider=GroundMode!=1;return GroundMode==1?double.NaN:GroundMode==2?p.x*10:0;} METHOD public static bool Run(poiScript h,Transform m,out Vector3d p){return StartingStopDestination(h,m,new terrainGenScript(),new Vector3d(0,0,1),out p);}}
public static class Test {static int checks;static void Check(bool v,string s){checks++;if(!v)throw new Exception(s);}static roadGenScript.roadClass Road(double z){var r=new roadGenScript.roadClass();var e=new roadGenScript.roadElementClass();e.obj.road.skinnedMeshRenderer.transform.position=new Vector3(0,0,(float)z);e.bonePos.Add(new Vector3d(-100,0,z));e.bonePos.Add(new Vector3d(100,0,z));r.roadElements.Add(e);return r;}
public static int Main(){var stop=new Transform();var marker=new Transform{parent=stop,position=new Vector3(0,0,1)};stop.Renderers=new[]{new MeshRenderer{transform=stop,bounds=new Bounds(new Vector3(0,0,1),new Vector3(10,6,10))}};var house=new poiScript();house.Renderers=new[]{new MeshRenderer{transform=house.transform,bounds=new Bounds(new Vector3(0,0,-10),new Vector3(10,10,10))},stop.Renderers[0]};mapSettingScript.Roads.Add(Road(0));Vector3d result;
Check(Adapter.Run(house,marker,out result),"roadside destination exists");Check(result.z>=10 && result.z<=12,"footprint clears road and stays nearby");Check(stop.position.sqrMagnitude==0,"search does not mutate stop");
stop.Renderers=new[]{stop.Renderers[0],new MeshRenderer{transform=stop,bounds=new Bounds(new Vector3(-1000,0,-1000),Vector3.zero)}};Check(Adapter.Run(house,marker,out result),"empty inactive renderer does not enlarge footprint");Check(result.z<12,"empty bounds do not affect clearance");stop.Renderers=new[]{stop.Renderers[0]};
var element=mapSettingScript.Roads[0].roadElements[0];element.bonePos[0]=new Vector3d(-1,0,0);element.bonePos[1]=new Vector3d(1,0,0);Check(Adapter.Run(house,marker,out result),"long road mesh clears even when bones cover only its middle");element.bonePos[0]=new Vector3d(-100,0,0);element.bonePos[1]=new Vector3d(100,0,0);element.obj.road.bakedMesh.triangles=new int[0];Check(!Adapter.Run(house,marker,out result),"empty mesh defers placement");element.obj.road.bakedMesh.triangles=new[]{0,1,2,1,3,2};
stop.Renderers=new[]{stop.Renderers[0],new MeshRenderer{transform=stop,bounds=new Bounds(new Vector3(0,0,-12),new Vector3(1,4,1))}};Check(Adapter.Run(house,marker,out result),"attached pole footprint fits");Check(result.z>=18.5,"attached pole clears road");stop.Renderers=new[]{stop.Renderers[0]};
mapSettingScript.Roads.Add(Road(12));Check(Adapter.Run(house,marker,out result),"avoid another road");Check(Math.Abs(result.z-12)>=10,"clears second road");mapSettingScript.Roads.RemoveAt(1);
Adapter.GroundMode=1;Check(!Adapter.Run(house,marker,out result),"missing terrain defers placement");Adapter.GroundMode=2;Check(Adapter.Run(house,marker,out result),"only destination ground is required");Adapter.GroundMode=0;
mapSettingScript.Roads[0].generated=false;Check(!Adapter.Run(house,marker,out result),"incomplete roads defer placement");mapSettingScript.Roads[0].generated=true;
stop.Spawners=new[]{new newRandomStuffSpawnScript{items=new[]{new newRandomStuffSpawnScript.item{places=new[]{new newRandomStuffSpawnScript.place{T=new Transform{position=new Vector3(0,0,-30)}}}}}}};Check(Adapter.Run(house,marker,out result),"distant anchor does not reject footprint");Check(result.z>=37,"distant anchor clears road with margin");stop.Spawners=new newRandomStuffSpawnScript[0];stop.Renderers=new MeshRenderer[0];Check(!Adapter.Run(house,marker,out result),"unknown footprint defers placement");
marker.position=new Vector3(0,5,1);Adapter.Marker=marker;
Check(!Adapter.TryBusPlacement(house),"unresolved horizontal placement still retries");
Check(stop.position.y==-5,"vertical alignment runs even if horizontal search rejects");
Console.WriteLine("PASS: "+checks+" production starting-stop search assertions");return 0;}}
'''.replace('METHOD', source[start:end]).replace('PLACEMENT', placement)
        compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        with tempfile.TemporaryDirectory(prefix='roadworthy-stop-') as directory:
            path = Path(directory)
            (path / 'Test.cs').write_text(fixture, encoding='utf-8')
            build = subprocess.run([str(compiler), '/nologo', '/out:' + str(path / 'Test.exe'), str(path / 'Test.cs')], capture_output=True, text=True)
            self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
            run = subprocess.run([str(path / 'Test.exe')], capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)


if __name__ == '__main__':
    unittest.main()
