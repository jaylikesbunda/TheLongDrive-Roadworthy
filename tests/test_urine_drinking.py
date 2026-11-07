import os, subprocess, tempfile, unittest
from pathlib import Path

class UrineDrinking(unittest.TestCase):
 def test_production_drink_transfer(self):
  root=Path(__file__).resolve().parents[1]
  source=(root/'runtime/StableGameBridge.cs').read_text(encoding='utf-8')
  start=source.index('        static fpscontroller fluidDrinker;')
  end=source.index('        sealed class FluidPromptState',start)
  helper=source[start:end]
  fixture=r'''
using System;using System.Collections.Generic;
struct Vector3 {public static Vector3 zero;}
static class Mathf {public static float Max(float a,float b){return Math.Max(a,b);}}
class survival {public float maxHp=150,water,damage;public void Drink(float a){water+=a;}public void Damage(float a,Vector3 d){damage+=a;}}
class survivalscript : survival {}
static class DrunkEffects {public static float AlcoholDrunk(mainscript.fluidcontainer c,float a){return 0;}public static void NoteAlcohol(survivalscript s,float l){}public static float MilkDrunk(mainscript.fluidcontainer c,float a){return 0;}public static void NoteMilk(survivalscript s,float l){}}
static class SurvivalRates {public static float dose;public static void AddSickness(survivalscript s,float a){dose+=a;}}
class fpscontroller {public survivalscript survival=new survivalscript();}
class mainscript {
 public enum fluidenum {gas=0,oil=1,water=3,urine=7}
 public class fluid {public fluidenum type;public float amount;}
 public class fluidcontainer {
  public float maxC=100;public List<fluid> fluids=new List<fluid>();
  public float GetAmount(){float t=0;foreach(var f in fluids)t+=f.amount;return t;}
  public float Amount(fluidenum t){foreach(var f in fluids)if(f.type==t)return f.amount;return 0;}
  public float ChangeOne(float a,fluidenum t){a=Math.Min(a,maxC-GetAmount());foreach(var f in fluids)if(f.type==t){f.amount+=a;return a;}if(a>0)fluids.Add(new fluid{type=t,amount=a});return a>0?a:0;}
  public void pour(fluidcontainer donor,float amount,bool direct,bool filter,List<fluidenum> filtered){
   foreach(var f in donor.fluids){float portion=f.amount/donor.GetAmount();if(direct)donor.ChangeOne(-ChangeOne(amount*portion,f.type),f.type);else ChangeOne(amount*portion,f.type);if(filter && filtered.Contains(f.type))ChangeOne(-amount*portion,f.type);}
  }
 }
}
class Test {
 const mainscript.fluidenum UrineFluid=(mainscript.fluidenum)7;
 static void TraceUrineDrink(float a,float b,float c){}
 HELPER
 static int checks;static void Near(float a,float b){checks++;if(Math.Abs(a-b)>0.0001f)throw new Exception("check "+checks+": "+a+" != "+b);}
 static mainscript.fluidcontainer Tank(float cap,float urine,float water){var t=new mainscript.fluidcontainer{maxC=cap};t.ChangeOne(urine,UrineFluid);t.ChangeOne(water,mainscript.fluidenum.water);return t;}
 static void Main(){
  var poisonSource=new mainscript.fluidcontainer();poisonSource.ChangeOne(1,mainscript.fluidenum.oil);var poisonMouth=new mainscript.fluidcontainer{maxC=.2f};fluidDrinker=new fpscontroller();DrinkTransfer(poisonMouth,poisonSource,1,true,false,null);Near(SurvivalRates.dose,.2f);
  var p=new fpscontroller();var src=Tank(10,2,0);var mouth=Tank(10,0,0);fluidDrinker=p;DrinkTransfer(mouth,src,1,true,false,null);Near(src.Amount(UrineFluid),1);Near(mouth.Amount(UrineFluid),0);Near(p.survival.water,.25f);Near(p.survival.damage,3);
  src=Tank(10,2,0);mouth=Tank(.2f,0,0);fluidDrinker=p;DrinkTransfer(mouth,src,1,true,false,null);Near(src.Amount(UrineFluid),1.8f);Near(mouth.Amount(UrineFluid),0);Near(p.survival.water,.30f);Near(p.survival.damage,3.6f);
  src=Tank(10,2,0);mouth=Tank(1,0,1);fluidDrinker=p;DrinkTransfer(mouth,src,1,true,false,null);Near(src.Amount(UrineFluid),2);Near(p.survival.water,.30f);Near(p.survival.damage,3.6f);
  src=Tank(10,1,1);mouth=Tank(10,0,0);fluidDrinker=p;DrinkTransfer(mouth,src,1,true,false,null);Near(src.Amount(UrineFluid),.5f);Near(mouth.Amount(UrineFluid),0);Near(p.survival.water,.425f);Near(p.survival.damage,5.1f);
  src=Tank(10,1,0);mouth=Tank(10,0,0);DrinkTransfer(mouth,src,.5f,true,false,null);Near(src.Amount(UrineFluid),.5f);Near(mouth.Amount(UrineFluid),.5f);Near(p.survival.water,.425f);Near(p.survival.damage,5.1f);
  src=Tank(10,0,0);mouth=Tank(10,0,0);fluidDrinker=p;DrinkTransfer(mouth,src,1,true,false,null);Near(p.survival.water,.425f);Near(p.survival.damage,5.1f);
  Console.WriteLine("PASS "+checks+" production urine transfer checks");
 }
}
'''.replace('HELPER',helper)
  with tempfile.TemporaryDirectory(prefix='roadworthy-urine-') as d:
   p=Path(d);cs=p/'test.cs';exe=p/'test.exe';cs.write_text(fixture)
   compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
   subprocess.run([str(compiler),'/nologo','/out:'+str(exe),str(cs)],check=True)
   subprocess.run([str(exe)],check=True)
