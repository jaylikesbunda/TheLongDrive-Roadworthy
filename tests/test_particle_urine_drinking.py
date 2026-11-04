import os, subprocess, tempfile, unittest
from pathlib import Path

class ParticleUrineDrinking(unittest.TestCase):
 def test_production_particle_transfer(self):
  root=Path(__file__).resolve().parents[1]
  fixture=r'''
using System;using System.Collections.Generic;using TldMp;
namespace UnityEngine { public struct Vector3 {public static Vector3 zero;} public static class Mathf {public static float Max(float a,float b){return Math.Max(a,b);}} }
public class survival {public float maxHp=150,water,damage;public void Drink(float a){water+=a;}public void Damage(float a,UnityEngine.Vector3 d){damage+=a;}}
public class fpscontroller {public survival survival=new survival();}
public class mainscript {
 public enum fluidenum {gas=0,oil=1,water=3,urine=7} public class fluid {public fluidenum type;public float amount;}
 public class fluidcontainer {
  public float maxC=100;public List<fluid> fluids=new List<fluid>();
  public float GetAmount(){float a=0;foreach(var f in fluids)a+=f.amount;return a;}
  public float Amount(fluidenum t){float a=0;foreach(var f in fluids)if(f.type==t)a+=f.amount;return a;}
  public float ChangeOne(float amount,fluidenum t){amount=Math.Min(amount,maxC-GetAmount());foreach(var f in fluids)if(f.type==t){f.amount+=amount;return amount;}if(amount>0)fluids.Add(new fluid{type=t,amount=amount});return amount>0?amount:0;}
  public void pour(fluidcontainer donor,float value,bool direct,bool filter,List<fluidenum> list){foreach(var f in donor.fluids){float part=f.amount/donor.GetAmount();if(direct)donor.ChangeOne(-ChangeOne(value*part,f.type),f.type);else ChangeOne(value*part,f.type);if(filter && list.Contains(f.type))ChangeOne(-value*part,f.type);}}
 }
}
public class tankscript {public mainscript.fluidcontainer F;}
public class tankcapscript {public bool isDrinkCap;public fpscontroller player;public tankscript Tank;}
public class tankcapparticledetectionscript {public tankcapscript T;}
namespace TldMp {public static class StableGameBridge {internal const mainscript.fluidenum UrineFluid=(mainscript.fluidenum)7;public static float dose;public static float BadFluidAmount(mainscript.fluidcontainer f){float n=0;foreach(var x in f.fluids)if((int)x.type==0||(int)x.type==1||(int)x.type==5)n+=x.amount;return n;}public static void NoteBadFluidDrink(survival s,float a){dose+=a;}public static void TraceUrineDrink(float a,float b,float c){}}}
public class Test {
 static int n;static void Near(float a,float b){n++;if(Math.Abs(a-b)>0.0001f)throw new Exception("check "+n+" "+a+" != "+b);}
 static mainscript.fluidcontainer Tank(float cap,float u,float w){var t=new mainscript.fluidcontainer{maxC=cap};t.ChangeOne(u,mainscript.fluidenum.urine);t.ChangeOne(w,mainscript.fluidenum.water);return t;}
 static void Main(){
  var poison=new mainscript.fluidcontainer();poison.ChangeOne(1,mainscript.fluidenum.oil);var poisonMouth=new mainscript.fluidcontainer{maxC=.2f};var poisonPlayer=new fpscontroller();var poisonCap=new tankcapscript{isDrinkCap=true,player=poisonPlayer,Tank=new tankscript{F=poisonMouth}};var poisonDetector=new tankcapparticledetectionscript{T=new tankcapscript{Tank=new tankscript{F=poison}}};ParticleFluidDrinking.Transfer(poisonMouth,poison,1,false,false,null,poisonDetector,poisonCap);Near(StableGameBridge.dose,.2f);
  var p=new fpscontroller();var source=Tank(10,2,0);var mouth=Tank(10,0,0);var cap=new tankcapscript{isDrinkCap=true,player=p,Tank=new tankscript{F=mouth}};var det=new tankcapparticledetectionscript{T=new tankcapscript{Tank=new tankscript{F=source}}};
  ParticleFluidDrinking.Transfer(mouth,source,1,false,false,null,det,cap);Near(source.Amount(mainscript.fluidenum.urine),2);Near(mouth.Amount(mainscript.fluidenum.urine),0);Near(p.survival.water,.25f);Near(p.survival.damage,3);
  mouth.maxC=.2f;ParticleFluidDrinking.Transfer(mouth,source,1,false,false,null,det,cap);Near(source.Amount(mainscript.fluidenum.urine),2);Near(mouth.Amount(mainscript.fluidenum.urine),0);Near(p.survival.water,.30f);Near(p.survival.damage,3.6f);
  mouth=Tank(1,0,1);cap.Tank.F=mouth;ParticleFluidDrinking.Transfer(mouth,source,1,false,false,null,det,cap);Near(mouth.Amount(mainscript.fluidenum.urine),0);Near(p.survival.damage,3.6f);
  mouth=Tank(10,.4f,0);source=Tank(10,1,1);cap.Tank.F=mouth;det.T.Tank.F=source;ParticleFluidDrinking.Transfer(mouth,source,1,false,false,null,det,cap);Near(mouth.Amount(mainscript.fluidenum.urine),.4f);Near(source.Amount(mainscript.fluidenum.urine),1);Near(p.survival.water,.425f);Near(p.survival.damage,5.1f);
  mouth=Tank(10,0,0);source=Tank(10,1,0);cap.Tank.F=mouth;det.T.Tank.F=source;cap.isDrinkCap=false;ParticleFluidDrinking.Transfer(mouth,source,.5f,false,false,null,det,cap);Near(mouth.Amount(mainscript.fluidenum.urine),.5f);Near(p.survival.damage,5.1f);Near(source.Amount(mainscript.fluidenum.urine),1);
  Console.WriteLine("PASS "+n+" particle drinking transfer checks");
 }
}
'''
  with tempfile.TemporaryDirectory(prefix='roadworthy-particle-') as d:
   p=Path(d);cs=p/'test.cs';exe=p/'test.exe';cs.write_text(fixture)
   compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
   subprocess.run([str(compiler),'/nologo','/out:'+str(exe),str(cs),str(root/'runtime/ParticleFluidDrinking.cs')],check=True)
   subprocess.run([str(exe)],check=True)
