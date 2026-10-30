"""Exercise the production pickup adapter with Unity/native interaction stand-ins."""
from pathlib import Path
import os
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class CoveredRimTests(unittest.TestCase):
    def test_native_pickup_and_detach_guards(self):
        source = (ROOT / 'runtime/StableGameBridge.cs').read_text(encoding='utf-8')
        start = source.index('static pickupable LooseWheelAssembly(')
        end = source.index('public static void BusGroundPlaced', start)
        method = source[start:end]
        visual_start = method.index('        static pickupable VisibleWheelPart')
        visual_end = method.index('        public static pickupable ResolveWheelPickup')
        method = method[:visual_start] + 'public static pickupable VisualOverride;static pickupable VisibleWheelPart(pickupable rim,pickupable fallback,fpscontroller player){return VisualOverride ?? rim;}\n' + method[visual_end:]
        fixture = r'''
using System;using System.Collections.Generic;
public struct Vector3 { }
public struct RaycastHit {public Collider collider;public Vector3 point;}
public class Transform {public Vector3 position,forward;}
public class Camera {public Transform transform=new Transform();}
public class Collider {public pickupable Item;public T GetComponentInParent<T>() where T:class{return Item as T;}}
public static class Physics {public static Collider Hit;public static bool Raycast(Vector3 a,Vector3 b,out RaycastHit h,float d,int mask){h=new RaycastHit{collider=Hit};return Hit!=null;}}
public class Input {public bool pickupDown=true,pickup,mountDown,mountUp,usingComputer;}
public class Attach {public bool attached=true;public partslotscript slot;public Save tosave;}
public class partslotscript {public Save tosaveitem;public pickupable pick;public Attach Part;public List<Attach> parts=new List<Attach>();public int UnCraftCalls;public bool UnCraft(){UnCraftCalls++;return true;}public bool hasPart(Attach a){return parts.Contains(a);}public bool hasPart(){return parts.Count>0;}public Attach part(){return parts.Count==0?null:parts[0];}}
public class Save {public pickupable P;public Attach attachable;}
public class Wheel {public partslotscript gumi;}
public class pickupable {public object gumi,physlock;public Save tosave;public Wheel wheel;public bool pickedUp,cantPickupWhileMount,Allowed=true;public bool CanPickup(){return Allowed;}}
public class fpscontroller {public Input input=new Input();public pickupable pickedUp;public bool BcanDetach,BAttach,canusetube,BcanMount=true,Bpickedup,nowMounted,pushgripping,Bleaning,loadedmove=true,Other,SitAllowed=true;public Camera Cam=new Camera();public float FrayRange=4;public int pickupLayer;public int Calls;public bool otherView(){return Other;}public bool CanPickupSit(pickupable p){return SitAllowed;}public void Pickup(pickupable p,Vector3 point){Calls++;pickedUp=p;}}
public class mainscript {public static mainscript s=new mainscript();public bool crsrLocked=true;}
public static class Adapter { METHOD }
public static class Test {
static int checks;
static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
static fpscontroller Player(out pickupable rim,out pickupable tyre,out partslotscript slot){rim=new pickupable{wheel=new Wheel(),tosave=new Save{attachable=new Attach{attached=false}}};rim.tosave.P=rim;slot=new partslotscript{tosaveitem=rim.tosave};rim.wheel.gumi=slot;var a=new Attach{slot=slot};slot.Part=a;slot.parts.Add(a);tyre=new pickupable{gumi=new object(),tosave=new Save{attachable=a}};a.tosave=tyre.tosave;tyre.tosave.P=tyre;Physics.Hit=new Collider{Item=tyre};return new fpscontroller();}
public static int Main(){
pickupable rim,tyre;partslotscript slot;
var p=Player(out rim,out tyre,out slot);
Check(Adapter.ResolveWheelPickup(Physics.Hit,p)==rim,"tyre hit resolves to rim");
Check(!Adapter.WheelPickupMountBlocked(p,rim),"tyre dismount prompt no longer vetoes loose rim pickup");
Check(!p.BcanMount && !p.BcanDetach,"rim view exposes pickup without tyre dismount prompt");
Physics.Hit.Item=rim;Check(Adapter.ResolveWheelPickup(Physics.Hit,p)==rim,"direct centre rim hit stays rim");
Check(!Adapter.WheelPickupMountBlocked(p,rim),"direct rim hit uses same eligibility");
Check(tyre.tosave.attachable.attached && slot.Part==tyre.tosave.attachable,"targeting preserves fitted tyre");
Check(p.Calls==0 && p.pickedUp==null,"resolver never picks up or mutates player");
slot.parts.Insert(0,new Attach{slot=slot,tosave=new Save{P=new pickupable{gumi=new object()}}});
Physics.Hit.Item=tyre;Check(Adapter.ResolveWheelPickup(Physics.Hit,p)==rim,"target tyre need not be first in native slot list");
Physics.Hit.Item=rim;Check(Adapter.ResolveWheelPickup(Physics.Hit,p)==rim,"centre rim resolves with multiple slot entries");
foreach(string mode in new[]{"mountDown","mountUp","stored","attached","slotted","looseTyre","mismatch","missingSlot","miss"}){
p=Player(out rim,out tyre,out slot);
switch(mode){case "mountDown":p.input.mountDown=true;break;case "mountUp":p.input.mountUp=true;break;case "stored":rim.physlock=new object();break;case "attached":rim.tosave.attachable=new Attach();break;case "slotted":rim.tosave.attachable=new Attach{attached=false,slot=new partslotscript()};break;case "looseTyre":tyre.tosave.attachable.attached=false;break;case "mismatch":rim.wheel.gumi=new partslotscript();break;case "missingSlot":tyre.tosave.attachable.slot=null;break;case "miss":Physics.Hit=null;break;}
Check(Adapter.ResolveWheelPickup(Physics.Hit,p)!=rim,"original target retained: "+mode);
}
p=Player(out rim,out tyre,out slot);p.input.mountDown=true;
Check(Adapter.WheelPickupMountBlocked(p,rim) && p.BcanMount && !p.BcanDetach,"dismount press keeps native action priority");
p=Player(out rim,out tyre,out slot);rim.Allowed=false;
Check(Adapter.WheelPickupMountBlocked(p,rim) && p.BcanMount && !p.BcanDetach,"ineligible rim cannot expose pickup prompt");
p=Player(out rim,out tyre,out slot);Adapter.VisualOverride=tyre;
Check(Adapter.ResolveWheelPickup(Physics.Hit,p)==tyre,"visible tyre stays tyre");
Check(Adapter.WheelPickupMountBlocked(p,tyre) && p.BcanMount,"visible tyre retains native dismount prompt");Adapter.VisualOverride=null;
// When pickup and dismount share a key, picking up the rim must not detach the tyre.
p=Player(out rim,out tyre,out slot);p.input.pickup=true;p.input.mountDown=true;
Check(!Adapter.WheelPickupMountBlocked(p,rim),"shared pickup control exposes rim pickup");
Check(!Adapter.WheelSlotUnCraft(slot,p) && slot.UnCraftCalls==0,"shared pickup does not detach tyre");
p.input.pickup=false;Check(Adapter.WheelSlotUnCraft(slot,p) && slot.UnCraftCalls==1,"ordinary dismount runs native UnCraft");
// Permission belongs to the native body, not to the resolver.
p=Player(out rim,out tyre,out slot);rim.Allowed=false;Check(Adapter.ResolveWheelPickup(Physics.Hit,p)==rim && !rim.CanPickup(),"native burial/pickup gate still owns refusal");
Console.WriteLine("PASS: "+checks+" production rim adapter assertions");return 0;}}
'''.replace('METHOD', method)
        compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        with tempfile.TemporaryDirectory(prefix='roadworthy-rim-') as directory:
            path = Path(directory)
            (path / 'Test.cs').write_text(fixture, encoding='utf-8')
            build = subprocess.run([str(compiler), '/nologo', '/out:' + str(path / 'Test.exe'), str(path / 'Test.cs')], capture_output=True, text=True)
            self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
            result = subprocess.run([str(path / 'Test.exe')], check=True, capture_output=True, text=True)
            self.assertIn('26 production rim adapter assertions', result.stdout)


if __name__ == '__main__':
    unittest.main()
