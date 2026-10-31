"""Exercise the production loading overlay against deterministic world stand-ins."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class LoadingOverlayTests(unittest.TestCase):
    def test_world_readiness_and_scene_lifecycle(self):
        source = (ROOT / 'runtime/GroundDiagnostics.cs').read_text(encoding='utf-8')
        begin = source.index('        static menuhandler loadingOwner;')
        end = source.index('        // A spawned haz02 can precede', begin)
        fixture = r'''
using System;using System.Collections.Generic;
public struct Vector3 {public float x,y,z;public static readonly Vector3 zero=new Vector3();}
public struct Vector3d {public double x,y,z;}
public class Transform {public Vector3 position;}
public class GameObject {public bool activeSelf;public void SetActive(bool b){activeSelf=b;}}
public class Rigidbody {public Vector3 velocity,angularVelocity;}
public class Player {public Transform transform=new Transform();public Rigidbody RB=new Rigidbody();}
public class mainscript {public static mainscript s=new mainscript();public Player player=new Player();public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d{x=p.x,y=p.y,z=p.z};}public static Vector3d GetChunkPos(Vector3d p,float size){return new Vector3d();}}
public class DataFromMenuScript {public static DataFromMenuScript s=new DataFromMenuScript();public bool mainmenu;}
public class mapSettingScript {public List<poiGenScript> poiGens=new List<poiGenScript>();}
public class menuhandler {public static menuhandler s;public GameObject GLoadText=new GameObject();public mapSettingScript currentMainMap=new mapSettingScript();}
public class terrainGenScript {}
public class poiScript {public GameObject gameObject=new GameObject();}
public class poiGenScript {public bool isActiveAndEnabled=true,placeStartHouse,startHouseStarted;public float chunkSize=100;public Dictionary<Vector3d,chunkClass> chunks=new Dictionary<Vector3d,chunkClass>();public class chunkClass {public bool generated;public Dictionary<Vector3d,poiClass> pois=new Dictionary<Vector3d,poiClass>();}public class poiClass {public Vector3d pos;public poiScript pobj;public bool spawnedItems,spawnedFixed;}}
public class itemPlaceRemoveScript {public static itemPlaceRemoveScript s=new itemPlaceRemoveScript();public float itemSpawnDist=100;}
public static class Time {public static float realtimeSinceStartup;}
public static class Debug {public static void Log(string s){}public static void LogWarning(string s){}}
public static class Adapter {
static bool hasGround;public static void Ground(bool v){hasGround=v;}
static terrainGenScript NearGen(mapSettingScript map){return new terrainGenScript();}
static double ColliderY(terrainGenScript g,Vector3d p,out bool tile,out bool collider){tile=collider=hasGround;return hasGround?0:double.NaN;}
static string F(double x){return x.ToString();}
METHOD
}
public static class Test {
static void Check(bool v,string s){if(!v)throw new Exception(s);}
static void Tick(float t,bool live){Time.realtimeSinceStartup=t;Adapter.TickLoadingOverlay(live);}
public static void Main(){
var menu=new menuhandler();menuhandler.s=menu;var gen=new poiGenScript{placeStartHouse=true};menu.currentMainMap.poiGens.Add(gen);var chunk=new poiGenScript.chunkClass();gen.chunks.Add(new Vector3d(),chunk);
Tick(1,true);Check(menu.GLoadText.activeSelf,"initial overlay shown");
Adapter.Ground(true);Tick(2,true);Check(menu.GLoadText.activeSelf,"wait for starting house");
gen.startHouseStarted=true;Tick(3,true);Check(menu.GLoadText.activeSelf,"wait for generated chunk");chunk.generated=true;
var poi=new poiGenScript.poiClass{pobj=new poiScript()};poi.pobj.gameObject.activeSelf=true;chunk.pois.Add(new Vector3d(),poi);
Tick(4,true);Check(menu.GLoadText.activeSelf,"wait for POI props");poi.spawnedItems=true;poi.spawnedFixed=true;
Tick(5,false);Check(menu.GLoadText.activeSelf,"wait for guest spawn");Tick(6,true);Tick(6.5f,true);Check(menu.GLoadText.activeSelf,"wait for stability");Tick(7.1f,true);Check(!menu.GLoadText.activeSelf,"ready overlay hidden");
menu.GLoadText.SetActive(true);DataFromMenuScript.s.mainmenu=true;Tick(8,true);Check(menu.GLoadText.activeSelf,"preserve native transition overlay");
DataFromMenuScript.s.mainmenu=false;var next=new menuhandler();menuhandler.s=next;Adapter.Ground(false);Tick(10,true);Tick(40.1f,true);Check(!next.GLoadText.activeSelf,"timeout releases");
var third=new menuhandler();menuhandler.s=third;Tick(50,true);Adapter.LoadingOverlayFailed(new Exception());Check(!third.GLoadText.activeSelf,"error releases");
var fourth=new menuhandler();menuhandler.s=fourth;Adapter.Ground(true);mainscript.s.player.transform.position=new Vector3{y=-10};Tick(60,true);Check(fourth.GLoadText.activeSelf,"hold while below ground");Check(Math.Abs(mainscript.s.player.transform.position.y-0.05f)<0.001,"player lifted onto ground");Tick(61,true);Tick(61.5f,true);Check(fourth.GLoadText.activeSelf,"wait for stability after lift");Tick(62.1f,true);Check(!fourth.GLoadText.activeSelf,"overlay hidden after lift and hold");
Console.WriteLine("PASS loading overlay readiness, multiplayer, lifecycle and fallback");}}
'''.replace('METHOD', source[begin:end])
        compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        with tempfile.TemporaryDirectory(prefix='roadworthy-loading-') as directory:
            path = Path(directory)
            (path / 'Test.cs').write_text(fixture, encoding='utf-8')
            build = subprocess.run([str(compiler), '/nologo', '/out:' + str(path / 'Test.exe'), str(path / 'Test.cs')], capture_output=True, text=True)
            self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
            run = subprocess.run([str(path / 'Test.exe')], capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn('PASS loading overlay', run.stdout)
