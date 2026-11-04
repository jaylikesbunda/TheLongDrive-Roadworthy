import os,subprocess,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
FIXTURE=r'''
using System;
namespace UnityEngine {public static class Time {public static float deltaTime=1;}public struct Vector3 {public float x,y,z;public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3{x=a.x-b.x,y=a.y-b.y,z=a.z-b.z};}}public class Rigidbody {public Vector3 velocity;}}
public class inputscript {public bool moving;}
public class fpscontroller {public bool sat,Brun;public inputscript input;public UnityEngine.Rigidbody RB,interiorRB;}
public class mainscript {public static mainscript s;public bool sleeping;}
public class survivalscript {public fpscontroller player;public bool running;public float foodLoss,waterLoss,runFoodLossModifier,runWaterLossModifier,carFluidsDamage=2;}
public static class Test {static int count;static void Check(bool b){count++;if(!b)throw new Exception("check "+count);}public static void Main(){
 var s=new survivalscript{foodLoss=1,waterLoss=6,runFoodLossModifier=3,runWaterLossModifier=3};Check(TldMp.SurvivalRates.Food(s)==1);Check(TldMp.SurvivalRates.Water(s)==6);
 s.player=new fpscontroller{input=new inputscript{moving=true},RB=new UnityEngine.Rigidbody{velocity=new UnityEngine.Vector3{x=2}},Brun=true};Check(TldMp.SurvivalRates.Food(s)==3&&s.running);Check(TldMp.SurvivalRates.Water(s)==18);
 s.player.input.moving=false;Check(TldMp.SurvivalRates.Food(s)==1&&!s.running);s.player.input.moving=true;s.player.RB.velocity=new UnityEngine.Vector3();Check(TldMp.SurvivalRates.Water(s)==6&&!s.running);
 s.player.RB.velocity=new UnityEngine.Vector3{z=4};s.player.interiorRB=new UnityEngine.Rigidbody{velocity=new UnityEngine.Vector3{z=4}};Check(TldMp.SurvivalRates.Water(s)==6);s.player.interiorRB=null;
 s.player.sat=true;Check(TldMp.SurvivalRates.Food(s)==1);s.player.sat=false;mainscript.s=new mainscript{sleeping=true};Check(TldMp.SurvivalRates.Water(s)==6);mainscript.s.sleeping=false;
 s.runWaterLossModifier=float.NaN;s.runFoodLossModifier=0;Check(TldMp.SurvivalRates.Water(s)==6);Check(TldMp.SurvivalRates.Food(s)==1);s.player.Brun=false;Check(TldMp.SurvivalRates.Water(s)==6&&!s.running);TldMp.SurvivalRates.AddSickness(s,.1f);Check(TldMp.SurvivalRates.Water(s)==12);for(int i=0;i<5;i++)TldMp.SurvivalRates.Water(s);Check(TldMp.SurvivalRates.Water(s)==6);TldMp.SurvivalRates.AddSickness(s,1000);TldMp.SurvivalRates.Clear(s);Check(TldMp.SurvivalRates.Water(s)==6);TldMp.SurvivalRates.AddSickness(s,float.NaN);Check(TldMp.SurvivalRates.Water(s)==6);Console.WriteLine("PASS "+count+" survival loss checks");}}
'''
class SurvivalTests(unittest.TestCase):
 def test_production_rates(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d);(p/'fixture.cs').write_text(FIXTURE);exe=p/'test.exe';csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
   subprocess.run([str(csc),'/nologo','/out:'+str(exe),str(p/'fixture.cs'),str(ROOT/'runtime/SurvivalRates.cs')],check=True);subprocess.run([str(exe)],check=True)
