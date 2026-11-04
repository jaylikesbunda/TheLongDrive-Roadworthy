import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]

FIXTURE = r'''
using System; using System.Collections.Generic; using UnityEngine;
namespace UnityEngine { public struct Vector3d { public double x,y,z; public Vector3d(double x,double y,double z){this.x=x;this.y=y;this.z=z;} public static Vector3d operator -(Vector3d a,Vector3d b){return new Vector3d(a.x-b.x,a.y-b.y,a.z-b.z);} public double magnitude {get{return Math.Sqrt(x*x+y*y+z*z);}} }
}
public static class Mathd { public static double InverseLerp(double a,double b,double x){return a==b?0:Math.Max(0,Math.Min(1,(x-a)/(b-a)));} public static double Lerp(double a,double b,double t){return a+(b-a)*Math.Max(0,Math.Min(1,t));} public static double Sin(double x){return Math.Sin(x);} }
public class poiGenScript { public class poiClass {} }
public class terrainGenSettings { public int calls; public double Height(Vector3d p,List<roadGenScript.roadClass> r,List<poiGenScript.poiClass> q){calls++;return r==null?20:123;} }
public class roadGenScript { public float innerChunkSize=20,chunkSize=100; public Dictionary<int,dotChunkClass> chunks=new Dictionary<int,dotChunkClass>(); public class dotChunkClass {public List<roadClass> roads=new List<roadClass>();} public class roadClass {public bool generated=true;public float terrainAlignMinDist=4,terrainAlignMaxDist=8;} public static roadClass NearestPointOnRoadBone(Vector3d p,List<roadClass> roads,out Vector3d nearest){nearest=new Vector3d(0,10,0);return roads.Count>0?roads[0]:null;} }
public class Map { public List<roadGenScript> roadGens=new List<roadGenScript>{new roadGenScript()}; }
public class menuhandler { public static menuhandler s=new menuhandler(); public Map currentMainMap=new Map(); }
public static class mapSettingScript { public static roadGenScript.roadClass corner=new roadGenScript.roadClass(); public static int calls; public static List<roadGenScript.roadClass> RoadsNear(Vector3d p){calls++;return Math.Abs(p.x)>=50 && Math.Abs(p.z)>=50?new List<roadGenScript.roadClass>{corner,corner}:new List<roadGenScript.roadClass>();} }
public static class Test { static void Check(bool x){if(!x)throw new Exception("assertion failed");} public static void Main(){var setting=new terrainGenSettings();var road=new roadGenScript.roadClass();var roads=new List<roadGenScript.roadClass>{road};var p=new Vector3d(0,0,0);Check(TldMp.RoadTerrainClearance.Height(setting,p,roads,null,false,0.5)==123);Check(Math.Abs(TldMp.RoadTerrainClearance.Height(setting,p,roads,null,true,0.5)-9.5)<1e-6);p.x=10;Check(Math.Abs(TldMp.RoadTerrainClearance.Height(setting,p,roads,null,true,0.5)-20)<1e-6);p.x=6;double v=TldMp.RoadTerrainClearance.Height(setting,p,roads,null,true,0.5);Check(v>9.5&&v<20);var found=TldMp.RoadTerrainClearance.RoadsNearTile(new Vector3d(0,0,0),100,2.5f);Check(found.Count==1&&found[0]==mapSettingScript.corner);Check(mapSettingScript.calls>5); int oldCalls=mapSettingScript.calls; var gen=menuhandler.s.currentMainMap.roadGens[0]; gen.chunks[1]=new roadGenScript.dotChunkClass();gen.chunks[1].roads.Add(road); found=TldMp.RoadTerrainClearance.RoadsNearTile(new Vector3d(0,0,0),20000,2.5f); Check(found.Count==1&&found[0]==road&&mapSettingScript.calls==oldCalls+1);Console.WriteLine("Road clearance checks passed");} }
'''

class RoadTerrainClearanceTests(unittest.TestCase):
    def test_clearance_and_corner_lookup(self):
        compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        with tempfile.TemporaryDirectory() as directory:
            fixture = Path(directory) / 'fixture.cs'
            fixture.write_text(FIXTURE, encoding='utf-8')
            executable = Path(directory) / 'test.exe'
            subprocess.run([str(compiler), '/nologo', '/out:' + str(executable), str(fixture), str(ROOT / 'runtime/RoadTerrainClearance.cs')], check=True)
            subprocess.run([str(executable)], check=True)

if __name__ == '__main__': unittest.main()
