import os,subprocess,tempfile,unittest
from pathlib import Path
class WorldBiomeBridge(unittest.TestCase):
 def test_lifecycle(self):
  root=Path(__file__).resolve().parents[1];source=(root/'runtime/StableGameBridge.cs').read_text();a=source.index('        sealed class TerrainBiomeBinding');b=source.index('        public static void BeforeNativeSaveData()',a)
  fixture=r'''
using System;using UnityEngine;
namespace UnityEngine{public struct Color{public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}}
class DataFromMenuScript{public static DataFromMenuScript s;public bool mainmenu,multi,load,newgame;public int seed;}
class terrainGenSettings{}
class saveproperties{public string version;}
class savedata{public saveproperties properties=new saveproperties();}
class savedatascript{public static savedatascript s;public savedata data=new savedata();}
class gamemodesettingsscript{public static gamemodesettingsscript s;public gamemodesettings S=new gamemodesettings();public class gamemodesettings{public int biome;}}
class TerrainWorker{public static int Clears;public static void Clear(){Clears++;}}class StableMap{public static int Refreshes;public static void RefreshBiomeConfig(){Refreshes++;}}class ModDiagnostics{public static bool On;}class Debug{public static void Log(object o){}}
namespace TldMp{static class WorldRelocationLedger {public static void Restore(string s){} public static string Stamp(string s){return s;}} class Probe{
HELPERS
static int checks;static void Check(bool b){checks++;if(!b)throw new Exception("check "+checks);}
static void Main(){
 Check(SaveVersionLabel("v [Roadworthy placement: 4] [Roadworthy relocation: QWxhZGRpbjpvcGVuIHNlc2FtZQ==] [Roadworthy biome: Grass]")=="v [Roadworthy placement: 4] [Roadworthy biome: Grass]");
 Check(SaveVersionLabel("v [Roadworthy relocation: damaged")=="v [relocation data damaged]");
 DataFromMenuScript.s=new DataFromMenuScript{newgame=true,seed=481};gamemodesettingsscript.s=new gamemodesettingsscript();savedatascript.s=new savedatascript();
 for(int mode=0;mode<4;mode++){
  DataFromMenuScript.s.newgame=true;DataFromMenuScript.s.load=false;gamemodesettingsscript.s.S.biome=mode;ConfigureWorldBiome();Check(WorldBiomePolicy.Mode==mode && DataFromMenuScript.s.seed==481);
  var p=new saveproperties{version="2024.11.26b"};StampBiomeVersion(p);gamemodesettingsscript.s.S.biome=(mode+1)%4;DataFromMenuScript.s.newgame=false;DataFromMenuScript.s.load=true;savedatascript.s.data.properties=p;ConfigureWorldBiome();Check(WorldBiomePolicy.Mode==mode && DataFromMenuScript.s.seed==481);
  DataFromMenuScript.s.load=false;DataFromMenuScript.s.multi=true;WorldBiomePolicy.SetMode(mode);ConfigureWorldBiome();Check(WorldBiomePolicy.Mode==mode);DataFromMenuScript.s.multi=false;
 }
 savedatascript.s.data.properties.version="old native save";DataFromMenuScript.s.load=true;ConfigureWorldBiome();Check(WorldBiomePolicy.Mode==0);
 DataFromMenuScript.s.load=false;DataFromMenuScript.s.mainmenu=true;WorldBiomePolicy.SetMode(2);ConfigureWorldBiome();Check(WorldBiomePolicy.Mode==0);
 DataFromMenuScript.s.mainmenu=false;DataFromMenuScript.s.load=true;savedatascript.s.data.properties.version="v [Roadworthy biome: damaged]";bool failed=false;try{ConfigureWorldBiome();}catch(System.IO.InvalidDataException){failed=true;}Check(failed);
 var oldTerrain=new terrainGenSettings();WorldBiomePolicy.SetMode(1);RegisterTerrainBiome(oldTerrain);WorldBiomePolicy.SetMode(3);
 Check(ApplyWorldBiome(new Color(),oldTerrain).r==1);var newTerrain=new terrainGenSettings();RegisterTerrainBiome(newTerrain);Check(ApplyWorldBiome(new Color(),newTerrain).g==1);
 WorldBiomePolicy.BeginWorker(2);Check(ApplyWorldBiome(new Color(),oldTerrain).b==1);WorldBiomePolicy.EndWorker();Check(ApplyWorldBiome(new Color(),oldTerrain).r==1);
 Check(TerrainWorker.Clears>0 && StableMap.Refreshes>0);
 Console.WriteLine("PASS "+checks+" world biome lifecycle, save mode, unchanged seed, multiplayer override, old-save and corrupt metadata checks.");
}
}}
'''.replace('HELPERS',source[a:b])
  with tempfile.TemporaryDirectory(prefix='tld-biome-bridge-') as d:
   p=Path(d);f=p/'probe.cs';exe=p/'probe.exe';f.write_text(fixture);compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe';subprocess.run([str(compiler),'/nologo','/out:'+str(exe),str(f),str(root/'runtime/WorldBiomePolicy.cs'),str(root/'runtime/WorldPlacementPolicy.cs')],check=True);out=subprocess.run([str(exe)],capture_output=True,text=True);self.assertEqual(out.returncode,0,out.stdout+out.stderr);print(out.stdout)
