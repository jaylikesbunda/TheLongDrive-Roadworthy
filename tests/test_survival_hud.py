import os,subprocess,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
FIXTURE=r'''
using System;
namespace UnityEngine {public static class Time {public static float unscaledTime;}public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}}
namespace UnityEngine.UI {public class Image {public UnityEngine.Color color;}}
public class survivalscript {public bool sick;}
public class fpscontroller {public survivalscript survival;public int shows;public void ShowStats(){shows++;}}
public class menuhandler {public UnityEngine.UI.Image survivalSliderWater;}
public class mainscript {public static mainscript s;public fpscontroller player;public menuhandler menu;}
namespace TldMp {public static class SurvivalRates {public static bool IsSick(survivalscript s){return s!=null&&s.sick;}}}
public class Test {static int n;static void Check(bool b){n++;if(!b)throw new Exception("check "+n);}static void Main(){
 TldMp.SurvivalHud.Begin();TldMp.SurvivalHud.End();var image=new UnityEngine.UI.Image{color=new UnityEngine.Color(.7f,.1f,.2f,.5f)};var p=new fpscontroller{survival=new survivalscript()};mainscript.s=new mainscript{player=p,menu=new menuhandler{survivalSliderWater=image}};
 TldMp.SurvivalHud.Begin();TldMp.SurvivalHud.End();Check(image.color.r==.7f&&p.shows==0);p.survival.sick=true;TldMp.SurvivalHud.Begin();TldMp.SurvivalHud.End();Check(p.shows==1&&image.color.r==1&&image.color.g==.85f&&image.color.b==.1f&&image.color.a==.5f);
 image.color=new UnityEngine.Color(1,.85f,.1f,.25f);UnityEngine.Time.unscaledTime=1.5f;TldMp.SurvivalHud.End();Check(image.color.r==.7f&&image.color.g==.1f&&image.color.b==.2f&&image.color.a==.25f);UnityEngine.Time.unscaledTime=0;TldMp.SurvivalHud.End();Check(image.color.r==1&&image.color.g==.85f);p.survival.sick=false;TldMp.SurvivalHud.End();Check(image.color.r==.7f&&image.color.g==.1f&&image.color.b==.2f&&image.color.a==.25f);
 p.survival.sick=true;TldMp.SurvivalHud.End();var other=new UnityEngine.UI.Image{color=new UnityEngine.Color(.2f,.3f,.4f,.7f)};mainscript.s.menu.survivalSliderWater=other;TldMp.SurvivalHud.End();Check(image.color.r==.7f&&other.color.r==1);mainscript.s=null;TldMp.SurvivalHud.End();Check(other.color.r==.2f&&other.color.g==.3f&&other.color.a==.7f);Console.WriteLine("PASS "+n+" sickness HUD transition checks");}}
'''
class SurvivalHudTests(unittest.TestCase):
 def test_hud(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d);(p/'fixture.cs').write_text(FIXTURE);exe=p/'test.exe';csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
   subprocess.run([str(csc),'/nologo','/out:'+str(exe),str(p/'fixture.cs'),str(ROOT/'runtime/SurvivalHud.cs')],check=True);subprocess.run([str(exe)],check=True)
