using System;
using System.Collections.Generic;
class Time {public static int frameCount;public static float deltaTime=1;}
enum KeyCode {Semicolon}
namespace UnityEngine {static class Input {public static bool pressed;public static bool GetKeyDown(KeyCode k){return pressed;}}}
class Mathf {public static float Clamp(float x,float lo,float hi){return Math.Max(lo,Math.Min(hi,x));}}
class ls {public enum w{fill}public static ls l=new ls();public string g(w word){return "Fill";}}
class mainscript {public static mainscript s=new mainscript();public bool crsrLocked=true;public float pourSpeed=1,slowOilModifier=1;public enum fluidenum{oil}}
class Fluid {public float amount,maxC=10;public float GetAmount(){return amount;}public float GetPercent(mainscript.fluidenum e){return 0;}public void pour(Fluid other,float n,bool direct,bool filter,object x){n=Math.Min(n,Math.Min(other.amount,maxC-amount));amount+=n;other.amount-=n;}}
class tankscript {public bool noFillFrom,noFillThis,slowOil;public Fluid F=new Fluid();public int updates;public void Upd(){updates++;}}
class tankcapscript {public tankscript Tank;public float valve=1,flowA=1;public void Filling(){}public void PlayFillSound(){}}
class pickupable {public tankscript tank;}
class InputState {public bool lmb;}
class fpscontroller {public pickupable pickedUp;public tankcapscript drink,pointedCap,lookedCap,selectedCap;public string LMB;public bool drank,noLMBUse,BLMB,filled;public InputState input=new InputState();}
class Test {
static bool ContainsUrine(tankscript tank){return false;}
static fpscontroller fluidDrinker;
HELPER
static int checks;static void Check(bool ok){checks++;if(!ok)throw new Exception("check "+checks);}
static void Main(){var p=new fpscontroller();var held=new tankscript();var cap=new tankcapscript{Tank=new tankscript()};p.pointedCap=cap;var item=new pickupable{tank=held};
Time.frameCount=1;FluidFillPrompt(p,item,true);Check(p.LMB.Contains(";: reverse flow") && p.selectedCap==cap && p.BLMB);Check(NativeFluidFillAllowed(p,held,cap,false));
Time.frameCount++;UnityEngine.Input.pressed=true;FluidFillPrompt(p,item,true);Check(p.LMB.Contains("Drain"));cap.Tank.F.amount=5;p.input.lmb=true;Check(!NativeFluidFillAllowed(p,held,cap,false));Check(held.F.amount==1 && cap.Tank.F.amount==4);Check(held.updates==1 && cap.Tank.updates==1);
var other=new tankscript();var second=new pickupable{tank=other};FluidFillPrompt(p,second,false);FluidFillPrompt(p,item,true);Check(p.LMB.Contains("Drain"));Check(!NativeFluidFillAllowed(p,held,cap,false));Check(held.F.amount==2);

Check(!NativeFluidFillAllowed(p,held,cap,false));Check(held.F.amount==3);UnityEngine.Input.pressed=false;
held.F.maxC=3;Check(!NativeFluidFillAllowed(p,held,cap,false) && held.F.amount==3);held.F.maxC=10;cap.valve=0;NativeFluidFillAllowed(p,held,cap,false);Check(held.F.amount==3);cap.valve=1;p.input.lmb=false;NativeFluidFillAllowed(p,held,cap,false);Check(held.F.amount==3);
Check(NativeFluidFillAllowed(p,held,cap,true));Time.frameCount+=3;FluidFillPrompt(p,item,true);Check(!p.LMB.Contains("Drain"));
Time.frameCount++;UnityEngine.Input.pressed=true;FluidFillPrompt(p,item,true);p.input.lmb=true;cap.Tank.noFillFrom=true;NativeFluidFillAllowed(p,held,cap,false);Check(held.F.amount==3);cap.Tank.noFillFrom=false;held.noFillThis=true;NativeFluidFillAllowed(p,held,cap,false);Check(held.F.amount==3);
// Both native prompt calls in the same frame: physical pickedUp owns key.
var handTank=new tankscript();var handItem=new pickupable{tank=handTank};var physical=new tankscript();var physicalItem=new pickupable{tank=physical};var engine=new tankcapscript{Tank=new tankscript()};engine.Tank.F.amount=4;p.pointedCap=engine;p.pickedUp=physicalItem;p.input.lmb=true;physical.F.amount=0;
Time.frameCount+=3;UnityEngine.Input.pressed=true;FluidFillPrompt(p,handItem,true);FluidFillPrompt(p,physicalItem,false);Check(p.LMB.Contains("Drain"));Check(!NativeFluidFillAllowed(p,physical,engine,false));Check(physical.F.amount==1 && engine.Tank.F.amount==3);
// A non-fluid picked-up object must not suppress the hand prompt.
p.pickedUp=new pickupable();Time.frameCount+=3;UnityEngine.Input.pressed=false;FluidFillPrompt(p,handItem,true);Check(p.LMB.Contains("reverse flow"));
Console.WriteLine("PASS: "+checks+" production reverse-fluid prompt, toggle, once-per-frame handling, empty held container, transfer conservation, capacity, valve, input, restrictions, drinking and focus-reset checks.");}
}