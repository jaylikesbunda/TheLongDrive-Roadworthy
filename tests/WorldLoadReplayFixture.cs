using System;
using System.Collections.Generic;
using TldMp;

// World-load IL replay for the LANE-SPECS A5 step-1 evidence test.
// Pattern: the exact tests\CargoBridgeFixture/SeatBridgeFixture/ItemBridgeFixture
// convention. Production IL is transplanted by tools\ExtractReplay.cs into these
// API-shaped stand-ins:
//   pass "worldnative" (Assembly-CSharp original):
//     savedatascript.SaveToDictionary/LoadStuff/Save/Load/Copy/IndexOfID/
//     IndexOfID2/Remove/Remov2, itemDataClass.Copy/RemoveID,
//     save_attachable.Restart/Save/Load/NeedsSave,
//     save_physicslock.Save/Load/NeedsSave
//   pass "world" (Tld.Multiplayer.dll):
//     TldMp.StableGameBridge.CaptureWorld/SendWorld/ApplyWorld,
//     TldMp.StableJson.Write/Read/Serializer
// The node Resync/Begin/Part/End/Ack/Live path runs the real StableCore.cs,
// NetCore.cs, StablePackets.cs and DeferredCore.cs sources. Everything else is a
// stand-in: no Unity runtime, no PhysX, no Steam, no scene streaming. Rotation
// math is real quaternion math; native joint physics is represented only by the
// saved attachment-slot and mount-storage relations.
namespace UnityEngine
{
 [Serializable] public struct Vector3
 {
  public float x,y,z;
  public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public static Vector3 zero {get{return new Vector3(0,0,0);}}
 }
 [Serializable] public struct Vector3d
 {
  public double x,y,z;
  public Vector3d(double a,double b,double c){x=a;y=b;z=c;}
  public Vector3d(float a,float b,float c){x=a;y=b;z=c;}
  public static Vector3d zero {get{return new Vector3d(0,0,0);}}
  public static Vector3d op_Subtraction(Vector3 a,Vector3d b){return new Vector3d(a.x-b.x,a.y-b.y,a.z-b.z);}
  public static explicit operator Vector3(Vector3d v){return new Vector3((float)v.x,(float)v.y,(float)v.z);}
 }
 [Serializable] public struct Vector2d {public double x,y;}
 [Serializable] public struct Quaternion
 {
  public float x,y,z,w;
  public Quaternion(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}
  public static Quaternion identity {get{return new Quaternion(0,0,0,1);}}
  public static Quaternion Euler(Vector3 v)
  {
   double hx=v.x*Math.PI/360,hy=v.y*Math.PI/360,hz=v.z*Math.PI/360;
   var qx=new Quaternion((float)Math.Sin(hx),0,0,(float)Math.Cos(hx));
   var qy=new Quaternion(0,(float)Math.Sin(hy),0,(float)Math.Cos(hy));
   var qz=new Quaternion(0,0,(float)Math.Sin(hz),(float)Math.Cos(hz));
   return qy*qx*qz;
  }
  public static Quaternion Inverse(Quaternion q){return new Quaternion(-q.x,-q.y,-q.z,q.w);}
  public static Quaternion operator *(Quaternion a,Quaternion b)
  {return new Quaternion(a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,a.w*b.z-a.x*b.y+a.y*b.x+a.z*b.w,a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z);}
  public Vector3 eulerAngles
  {
   get
   {
    // Unity ZXY extraction: x=asin(-R12), y=atan2(R02,R22), z=atan2(R10,R11)
    double ex=Math.Asin(Math.Max(-1,Math.Min(1,-(2*(y*z-w*x)))))*180/Math.PI;
    double ey=Math.Atan2(2*(x*z+w*y),1-2*(x*x+y*y))*180/Math.PI;
    double ez=Math.Atan2(2*(x*y+w*z),1-2*(x*x+z*z))*180/Math.PI;
    return new Vector3((float)ex,(float)ey,(float)ez);
   }
  }
 }
 public class Object
 {
  public bool Dead;string objectName="object";public string name {get{return objectName;}set{objectName=value;}}
  public static bool operator ==(Object a,Object b){return (ReferenceEquals(a,null)||a.Dead)?(ReferenceEquals(b,null)||b.Dead):ReferenceEquals(a,b);}
  public static bool operator !=(Object a,Object b){return !(a==b);}
  public override bool Equals(object b){return ReferenceEquals(this,b);}
  public override int GetHashCode(){return base.GetHashCode();}
  public override string ToString(){return name;}
  public static void Destroy(Object o){if(o!=null)o.Dead=true;}
 }
 public class GameObject:Object
 {
  public Transform transform;public bool activeInHierarchy{get;set;}public List<Component> Components=new List<Component>();
  public GameObject(){activeInHierarchy=true;name="go";transform=new Transform();transform.gameObject=this;Components.Add(transform);}
  public GameObject(string n):this(){name=n;}
  public void SetActive(bool active){activeInHierarchy=active;}
  public T Add<T>() where T:Component,new(){var c=new T();c.gameObject=this;Components.Add(c);return c;}
  public T Get<T>(){foreach(var c in Components)if(c is T && !c.Dead)return (T)(object)c;return default(T);}
  public T[] All<T>(){var a=new List<T>();foreach(var c in Components)if(c is T)a.Add((T)(object)c);return a.ToArray();}
 }
 public class Component:Object
 {
  public GameObject gameObject{get;set;}
  public Transform transform{get{return gameObject.transform;}}
  public T GetComponent<T>(){return gameObject.Get<T>();}
  public T GetComponentInParent<T>() where T:class
  {
   Transform t=transform;while(t!=null){var c=t.gameObject.Get<T>();if(c!=null)return c;t=t.parent;}return null;
  }
  public T[] GetComponentsInChildren<T>(bool inactive){return gameObject.All<T>();}
 }
 public class MonoBehaviour:Component { }
 public class Collider:Component { }
 public class Transform:Component
 {
  Vector3 p;Quaternion r=Quaternion.identity;
  public Transform parent{get;set;}
  public Vector3 position {get{return p;}set{p=value;}}
  public Quaternion rotation {get{return r;}set{r=value;}}
  public Vector3 eulerAngles {get{return r.eulerAngles;}set{r=Quaternion.Euler(value);}}
  public void SetParent(Transform t){parent=t;}
  public void SetPositionAndRotation(Vector3 pos,Quaternion rot){p=pos;r=rot;}
  static Vector3 Rotate(Quaternion q,Vector3 v)
  {
   double tx=2*(q.y*v.z-q.z*v.y),ty=2*(q.z*v.x-q.x*v.z),tz=2*(q.x*v.y-q.y*v.x);
   return new Vector3(
    (float)(v.x+q.w*tx+(q.y*tz-q.z*ty)),
    (float)(v.y+q.w*ty+(q.z*tx-q.x*tz)),
    (float)(v.z+q.w*tz+(q.x*ty-q.y*tx)));
  }
  public Vector3 TransformPoint(Vector3 local){var o=Rotate(r,local);return new Vector3(p.x+o.x,p.y+o.y,p.z+o.z);}
  public Vector3 InverseTransformPoint(Vector3 world)
  {return Rotate(new Quaternion(-r.x,-r.y,-r.z,r.w),new Vector3(world.x-p.x,world.y-p.y,world.z-p.z));}
 }
 public static class Debug
 {
  public static List<string> Logs=new List<string>();
  public static void Log(object value){Logs.Add(Convert.ToString(value));}
  public static void LogWarning(object value){Log(value);}
  public static void LogError(object value){Log(value);}
 }
}
[Serializable] public struct vector3
{
 public float x,y,z;
 public vector3(UnityEngine.Vector3 v){x=v.x;y=v.y;z=v.z;}
 public UnityEngine.Vector3 Load(){return new UnityEngine.Vector3(x,y,z);}
}
[Serializable] public struct posRotd
{
 public UnityEngine.Vector3d pos;public vector3 rot;
 public posRotd(UnityEngine.Vector3d p,UnityEngine.Vector3 r){pos=p;rot=new vector3(r);}
 public posRotd(posRotd from){pos=from.pos;rot=from.rot;}
}
public interface iid
{
 uint id{get;set;}
 void Save(tosaveitemscript tosave);
 void Load(tosaveitemscript tosave);
 bool NeedsSave(tosaveitemscript tosave);
}
public interface iid2
{
 uint id{get;set;}
 int ind{get;}
 void Save(tosaveitemscript tosave,int ind);
 void Load(tosaveitemscript tosave,int ind);
 bool NeedsSave(tosaveitemscript tosave,int ind);
}
public interface ICopyable<T> { T Copy(); }
// Native component stand-ins referenced by the transplanted save/Load IL.
public class partconditionscript:UnityEngine.Component { }
public class ammoscript:UnityEngine.Component { }
public class weaponscript:UnityEngine.Component { }
public class tankscript:UnityEngine.Component { }
public class usablescript:UnityEngine.Component { }
public class meterscript:UnityEngine.Component { }
public class enginescript:UnityEngine.Component { }
public class nyulscript:UnityEngine.Component { }
public class ragdollchildpos:UnityEngine.Component { }
public class busdoorscripts:UnityEngine.Component { }
public class colorscript:UnityEngine.Component { }
public class objSlideOnColliderScript:UnityEngine.Component { }
public class rendszamscript:UnityEngine.Component { }
public class randomTypeSelector:UnityEngine.Component { }
public class door_rot:UnityEngine.Component { }
public class ginlampscript:UnityEngine.Component { }
public class wearable:UnityEngine.Component { }
public class ediblescript:UnityEngine.Component { }
public class playermodeloutfitscript:UnityEngine.Component { }
public class randomScaleScript:UnityEngine.Component { }
public class paintingScript:UnityEngine.Component { }
public class newAiScript:UnityEngine.Component { }
public class poiScript:UnityEngine.Component
{
 public int poigenid;public UnityEngine.Vector3d posID;
 public System.Collections.Generic.List<UnityEngine.Collider> cols=new System.Collections.Generic.List<UnityEngine.Collider>();
 public System.Collections.Generic.List<partslotscript> partslots=new System.Collections.Generic.List<partslotscript>();
 public System.Collections.Generic.List<usablescript> usables=new System.Collections.Generic.List<usablescript>();
 public System.Collections.Generic.List<tankscript> tanks=new System.Collections.Generic.List<tankscript>();
}
public class partslotscript:UnityEngine.Component
{
 public static int CraftCalls;
 public attachablescript attachable;
 public void Craft(attachablescript a)
 {
  CraftCalls++;attachable=a;a.attached=true;a.slot=this;
  var point=a.NewPoint(transform,new UnityEngine.Vector3(0,0,0),UnityEngine.Quaternion.identity);a.Attach(point);
 }
}
public class attachablescript:UnityEngine.Component
{
 public bool attached;public UnityEngine.Transform point;public partslotscript slot;
 public bool tempDontMoveAtAttach,dontSendMulti;
 public poiScript AttachedToPOI(){return null;}
 public tosaveitemscript AttachedToTosave()
 {
  UnityEngine.Transform t=transform==null?null:transform.parent;
  while(t!=null){var item=t.gameObject.Get<tosaveitemscript>();if(item!=null)return item;t=t.parent;}
  return null;
 }
 public UnityEngine.Transform NewPoint(UnityEngine.Transform parent,UnityEngine.Vector3 local,UnityEngine.Quaternion rot)
 {
  var go=new UnityEngine.GameObject("attach-point");var t=go.transform;t.SetParent(parent);
  t.SetPositionAndRotation(parent.TransformPoint(local),parent.rotation*rot);return t;
 }
 public UnityEngine.Transform NewPoint(UnityEngine.Vector3 pos,UnityEngine.Quaternion rot)
 {var go=new UnityEngine.GameObject("attach-anchor");go.transform.SetPositionAndRotation(pos,rot);return go.transform;}
 public void Attach(UnityEngine.Transform to){point=to;attached=true;transform.SetParent(to);transform.SetPositionAndRotation(to.position,to.rotation);}
 public void Attach(UnityEngine.Transform to,UnityEngine.Collider col){Attach(to);}
}
public class pickupable:UnityEngine.Component
{
 public tosaveitemscript tosave;public mountStuff physlock;public bool pickedUp,Held;
}
public class mountStuff:UnityEngine.Component
{
 public tosaveitemscript parentTosave;public int id;
 public System.Collections.Generic.List<pickupable> stored=new System.Collections.Generic.List<pickupable>();
 public void Store(pickupable p,bool force)
 {
  if(!stored.Contains(p))stored.Add(p);
  p.physlock=this;p.tosave.transform.SetParent(transform);p.tosave.useThisForPos=p.tosave.transform;
 }
}
public class carscript:UnityEngine.Component { public tosaveitemscript tosave;public seatscript[] seats; }
public class fpscontroller:UnityEngine.MonoBehaviour { public bool Bsitting;public seatscript seat;public carscript Car; }
public class seatscript:UnityEngine.MonoBehaviour
{
 public carscript Car;public bool driverSeat0;public bool inUseMulti;public float inUseMultiTime;public string Key;
}
public class tosaveitemscript:UnityEngine.Component
{
 public uint idInSave;public int id;public bool buried,loaded,dontSyncOnSpawn;
 public UnityEngine.Transform useThisForDistance,useThisForPos;
 public pickupable P;public carscript car;public seatscript[] seats;
 public attachablescript attachable;
 public System.Collections.Generic.List<partslotscript> partslotscripts=new System.Collections.Generic.List<partslotscript>();
 public System.Collections.Generic.List<mountStuff> physLocks=new System.Collections.Generic.List<mountStuff>();
 public System.Collections.Generic.List<partconditionscript> partconditions;
 public ammoscript ammo;public weaponscript weapon;
 public System.Collections.Generic.List<tankscript> tanks;
 public System.Collections.Generic.List<usablescript> usables;
 public meterscript meter;public enginescript engine;public nyulscript nyul;public ragdollchildpos ragdollPos;
 public busdoorscripts busdoorscript;
 public System.Collections.Generic.List<colorscript> colors;
 public objSlideOnColliderScript slideOnCollider;public rendszamscript rendszam;
 public System.Collections.Generic.List<randomTypeSelector> randoms;
 public System.Collections.Generic.List<door_rot> door_rots;
 public ginlampscript ginlamp;public wearable wear;public ediblescript food;public playermodeloutfitscript outfit;
 public System.Collections.Generic.List<randomScaleScript> randomScales;
 public paintingScript painting;public newAiScript npc;
 public static int PreLoadCalls,ForcePosCalls;
 public void PreLoadStuff(){PreLoadCalls++;}
 public UnityEngine.Collider GetColliderFromIndex(int index){return null;}
 public int GetColliderIndex(UnityEngine.Collider col){return -1;}
 public bool ThisOneInChargeItem(){return P!=null && P.Held;}
 public void ForcePosUpd(bool parent,bool global,bool even)
 {ForcePosCalls++;TldMp.StableGameBridge.CaptureRoute(TldMp.StableGameBridge.HeldPacket(idInSave));}
}
public class saveproperties { }
[Serializable] public class itemDataClass
{
 public List<save_item> items=new List<save_item>();
 public List<save_partcondition> partconditions=new List<save_partcondition>();
 public List<save_ammo> ammo=new List<save_ammo>();
 public List<save_weapon> weapon=new List<save_weapon>();
 public List<save_tankscript> tanks=new List<save_tankscript>();
 public List<save_usablescript> usable=new List<save_usablescript>();
 public List<save_meterscript> meter=new List<save_meterscript>();
 public List<save_carscript> car=new List<save_carscript>();
 public List<save_engine> engine=new List<save_engine>();
 public List<save_attachable> attachable=new List<save_attachable>();
 public List<save_nyulscript> nyul=new List<save_nyulscript>();
 public List<save_ragdollpos> ragdollpos=new List<save_ragdollpos>();
 public List<save_busdoors> busdoorscript=new List<save_busdoors>();
 public List<save_color> colors=new List<save_color>();
 public List<save_slideOnCollider> slideOnCollider=new List<save_slideOnCollider>();
 public List<save_rendszam> rendszam=new List<save_rendszam>();
 public List<save_randomType> randoms=new List<save_randomType>();
 public List<save_door_rot> door_rots=new List<save_door_rot>();
 public List<save_ginlamp> ginlamp=new List<save_ginlamp>();
 public List<save_wear> wear=new List<save_wear>();
 public List<save_food> food=new List<save_food>();
 public List<save_outfit> outfit=new List<save_outfit>();
 public List<save_randomScale> randomScales=new List<save_randomScale>();
 public List<save_newai> newai=new List<save_newai>();
 public List<save_painting> painting=new List<save_painting>();
 public List<save_physicslock> physLocks=new List<save_physicslock>();
 // Bodies replaced by extracted production IL (pass "worldnative").
 public void Copy(itemDataClass from){throw new Exception("production IL missing");}
 public void RemoveID(uint id){throw new Exception("production IL missing");}
}
[Serializable] public class savedata
{
 public uint lastID;public saveproperties properties=new saveproperties();
 public itemDataClass itemData=new itemDataClass();
}
public class savedatascript:UnityEngine.MonoBehaviour
{
 public static savedatascript s=new savedatascript();
 public savedata data=new savedata();
 public Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();
 public Dictionary<UnityEngine.Vector2d,List<uint>> itemChunks=new Dictionary<UnityEngine.Vector2d,List<uint>>();
 public static int Spawned;
 // Fixture scene assembly: the prefab composition a native Instantiate would give.
 public UnityEngine.GameObject SpawnItem(save_item value)
 {
  Spawned++;var prefab=itemdatabase.s.Item(value.prefabID);var go=new UnityEngine.GameObject(prefab.name);
  foreach(var component in prefab.Components)
  {
   if(component is UnityEngine.Transform)continue;
   var clone=(UnityEngine.Component)Activator.CreateInstance(component.GetType());
   clone.gameObject=go;go.Components.Add(clone);
   if(component is partslotscript)go.Get<tosaveitemscript>().partslotscripts.Add(clone as partslotscript);
   if(component is mountStuff)go.Get<tosaveitemscript>().physLocks.Add(clone as mountStuff);
  }
  var item=go.Get<tosaveitemscript>();item.id=value.prefabID;
  var p=go.Get<pickupable>();if(p!=null){item.P=p;p.tosave=item;}
  var car=go.Get<carscript>();if(car!=null){item.car=car;car.tosave=item;car.seats=new seatscript[0];item.seats=car.seats;}
  var attachable=go.Get<attachablescript>();if(attachable!=null)item.attachable=attachable;
  return go;
 }
 public tosaveitemscript InitAfterSpawn(UnityEngine.GameObject go,uint key)
 {
  var item=go.Get<tosaveitemscript>();item.idInSave=key;item.loaded=false;
  item.useThisForPos=go.transform;item.useThisForDistance=go.transform;
  if(item.P!=null){item.P.tosave=item;}
  items[key]=item;return item;
 }
 public void AddToChunk(UnityEngine.Vector3 upos,uint id){}
 // Bodies replaced by extracted production IL (pass "worldnative").
 public static void SaveToDictionary(tosaveitemscript tosave,itemDataClass data){throw new Exception("production IL missing");}
 public void LoadStuff(savedata data,uint key){throw new Exception("production IL missing");}
 public void LoadStuff(savedata data,uint key,bool clientDontAskOnSpawn){throw new Exception("production IL missing");}
 public static void Save<T>(List<T> data,tosaveitemscript tosave){throw new Exception("production IL missing");}
 public static void Save<T,T2>(List<T> data,List<T2> list,tosaveitemscript tosave){throw new Exception("production IL missing");}
 public static void Load<T>(tosaveitemscript tosave,List<T> list){throw new Exception("production IL missing");}
 public static void Load<T2,T>(tosaveitemscript tosave,List<T2> list2,List<T> list){throw new Exception("production IL missing");}
 public static void Copy<T>(List<T> to,List<T> from){throw new Exception("production IL missing");}
 public static bool IndexOfID<T>(List<T> list,uint id,out int index){index=-1;throw new Exception("production IL missing");}
 public static bool IndexOfID2<T>(List<T> list,uint id,out int index){index=-1;throw new Exception("production IL missing");}
 public static bool IndexOfID<T>(List<T> list,uint id,int ind,out int index){index=-1;throw new Exception("production IL missing");}
 public static void Remove<T>(List<T> list,uint id){throw new Exception("production IL missing");}
 public static void Remov2<T>(List<T> list,uint id){throw new Exception("production IL missing");}
}
public class itemPlaceRemoveScript:UnityEngine.MonoBehaviour
{
 public static itemPlaceRemoveScript s=new itemPlaceRemoveScript();
 public static int PlaceOneCalls,GetOneCalls,DeferredAnnouncements;
 public tosaveitemscript PlaceOne(uint id,int index)
 {
  PlaceOneCalls++;tosaveitemscript existing;
  if(savedatascript.s.items.TryGetValue(id,out existing))return existing;
  int at;if(!savedatascript.IndexOfID(savedatascript.s.data.itemData.items,id,out at))return null;
  var definition=savedatascript.s.data.itemData.items[at];
  return savedatascript.s.InitAfterSpawn(savedatascript.s.SpawnItem(definition),id);
 }
 public bool GetOne(uint id,out tosaveitemscript item)
 {
  GetOneCalls++;if(savedatascript.s.items.TryGetValue(id,out item))return true;
  item=PlaceOne(id,-1);return item!=null;
 }
}
public class itemdatabase:UnityEngine.MonoBehaviour
{
 public static itemdatabase s=new itemdatabase();
 public const int Prefab371=371,Prefab43=43;
 public Dictionary<int,UnityEngine.GameObject> prefabs=new Dictionary<int,UnityEngine.GameObject>();
 public itemdatabase()
 {
  var car=new UnityEngine.GameObject("Car09");car.Add<tosaveitemscript>().car=car.Add<carscript>();car.Get<carscript>().seats=new seatscript[0];
  var carItem=car.Get<tosaveitemscript>();carItem.car.tosave=carItem;carItem.seats=carItem.car.seats;
  carItem.partslotscripts.Add(car.Add<partslotscript>());carItem.physLocks.Add(car.Add<mountStuff>());
  prefabs[371]=car;
  var wheel=new UnityEngine.GameObject("TireFelni");wheel.Add<tosaveitemscript>();wheel.Add<attachablescript>();
  prefabs[43]=wheel;
  var cargo=new UnityEngine.GameObject("cargo-can");var cargoItem=cargo.Add<tosaveitemscript>();cargoItem.P=cargo.Add<pickupable>();
  prefabs[5]=cargo;
  var held=new UnityEngine.GameObject("held-tool");var heldItem=held.Add<tosaveitemscript>();heldItem.P=held.Add<pickupable>();
  prefabs[7]=held;
 }
 public UnityEngine.GameObject Item(int id){UnityEngine.GameObject o;return prefabs.TryGetValue(id,out o)?o:null;}
 public bool Item(int id,out UnityEngine.GameObject o){return prefabs.TryGetValue(id,out o);}
}
public class mapSettingScript { public List<poiGenScript> poiGens=new List<poiGenScript>(); }
public class menuhandler:UnityEngine.MonoBehaviour
{
 public static menuhandler s=new menuhandler();
 public mapSettingScript currentMainMap;
}
public class poiGenScript
{
 public float chunkSize=100;
 public Dictionary<UnityEngine.Vector3d,chunkClass> chunks=new Dictionary<UnityEngine.Vector3d,chunkClass>();
 public Dictionary<UnityEngine.Vector3d,save_poi> poisFromSave=new Dictionary<UnityEngine.Vector3d,save_poi>();
 public class chunkClass { public Dictionary<UnityEngine.Vector3d,poiClass> pois=new Dictionary<UnityEngine.Vector3d,poiClass>(); }
 public class poiClass { public poiScript pobj;public save_poi save;public UnityEngine.Vector3d posID; }
}
public class napszakvaltakozas:UnityEngine.MonoBehaviour
{
 public static napszakvaltakozas s=new napszakvaltakozas();
 public float t,tekeres;
}
public class syncScript:UnityEngine.MonoBehaviour
{
 public static syncScript s=new syncScript();
 public static bool IsHostOrSingle(){return true;}
 public void SendAskItemData(uint id){}
 public void SendPlayerPos(fpscontroller player){TldMp.StableGameBridge.CaptureRoute(TldMp.StableGameBridge.PlayerPacket(player));}
}
public class mainscript:UnityEngine.MonoBehaviour
{
 public static mainscript s=new mainscript();
 public fpscontroller player=new fpscontroller();
 public UnityEngine.Vector3d visszarakva;
 public static UnityEngine.Vector3 UnityPosFromGlobal(UnityEngine.Vector3d v){return new UnityEngine.Vector3((float)v.x,(float)v.y,(float)v.z);}
 public static UnityEngine.Vector3d GlobalFromUnityPos(UnityEngine.Vector3 v){return new UnityEngine.Vector3d(v.x,v.y,v.z);}
 public static UnityEngine.Vector3d GetChunkPos(UnityEngine.Vector3d p,float size)
 {return new UnityEngine.Vector3d(Math.Floor(p.x/size)*size,0,Math.Floor(p.z/size)*size);}
}
// Native save record stand-ins. save_item / save_attachable / save_physicslock
// relations are produced and consumed by the extracted production IL above;
// their per-record field containers and the other 24 record types are simple
// fixture records (vehicle-condition and cosmetic records are out of scope).
[Serializable] public class save_item:iid,ICopyable<save_item>
{
 public uint id;public int prefabID;public posRotd transform;public bool buried;
 public save_item(){}
 public save_item(uint idInSave,int prefab, UnityEngine.Vector3d pos,UnityEngine.Vector3 rot)
 {id=idInSave;prefabID=prefab;transform=new posRotd(pos,rot);}
 public save_item(save_item from){Copy(from);}
 public void Copy(save_item from){if(from==null)return;id=from.id;prefabID=from.prefabID;transform=new posRotd(from.transform);buried=from.buried;}
 public save_item Copy(){return new save_item(this);}
 uint iid.id {get{return id;}set{id=value;}}
 void iid.Save(tosaveitemscript tosave){}
 void iid.Load(tosaveitemscript tosave){}
 bool iid.NeedsSave(tosaveitemscript tosave){return true;}
}
[Serializable] public class save_attachable:iid,ICopyable<save_attachable>
{
 public uint id;public int indexType,attachType,poigenid,index;
 public uint parentid;public UnityEngine.Vector3d poiid,lpos,gpos;
 public save_attachable(){}
 public save_attachable(save_attachable from){Copy(from);}
 public save_attachable(attachablescript script,uint itemId){Save(script,itemId);}
 public void Copy(save_attachable from)
 {if(from==null)return;id=from.id;indexType=from.indexType;attachType=from.attachType;parentid=from.parentid;poigenid=from.poigenid;poiid=from.poiid;index=from.index;lpos=from.lpos;gpos=from.gpos;}
 public save_attachable Copy(){return new save_attachable(this);}
 // Bodies replaced by extracted production IL (pass "worldnative").
 public void Restart(){throw new Exception("production IL missing");}
 public void Save(tosaveitemscript tosave){throw new Exception("production IL missing");}
 public void Save(attachablescript script,uint itemId){throw new Exception("production IL missing");}
 public void Load(tosaveitemscript tosave){throw new Exception("production IL missing");}
 public void Load(attachablescript script){throw new Exception("production IL missing");}
 public static void Load(attachablescript script,int indexType,int attachType,uint parentid,int poigenid,UnityEngine.Vector3d poiid,int index,UnityEngine.Vector3d lpos,UnityEngine.Vector3d gpos){throw new Exception("production IL missing");}
 public bool NeedsSave(tosaveitemscript tosave){throw new Exception("production IL missing");}
 uint iid.id {get{return id;}set{id=value;}}
 void iid.Save(tosaveitemscript tosave){Save(tosave);}
 void iid.Load(tosaveitemscript tosave){Load(tosave);}
 bool iid.NeedsSave(tosaveitemscript tosave){return NeedsSave(tosave);}
}
[Serializable] public class save_physicslock:iid2,ICopyable<save_physicslock>
{
 public uint id;public int ind;public uint[] IDs;public vector3[] poss;public vector3[] rots;
 public save_physicslock(){}
 public save_physicslock(save_physicslock from){Copy(from);}
 public save_physicslock(mountStuff script,uint itemId,int index){Save(script,itemId,index);}
 public void Copy(save_physicslock from)
 {if(from==null)return;id=from.id;ind=from.ind;IDs=from.IDs;poss=from.poss;rots=from.rots;}
 public save_physicslock Copy(){return new save_physicslock(this);}
 // Bodies replaced by extracted production IL (pass "worldnative").
 public void Save(tosaveitemscript tosave,int ind){throw new Exception("production IL missing");}
 public void Save(mountStuff script,uint itemId,int ind){throw new Exception("production IL missing");}
 public void Load(tosaveitemscript tosave,int ind){throw new Exception("production IL missing");}
 public void Load(mountStuff script){throw new Exception("production IL missing");}
 public bool NeedsSave(tosaveitemscript tosave,int ind){throw new Exception("production IL missing");}
 uint iid2.id {get{return id;}set{id=value;}}
 int iid2.ind {get{return ind;}}
 void iid2.Save(tosaveitemscript tosave,int ind){Save(tosave,ind);}
 void iid2.Load(tosaveitemscript tosave,int ind){Load(tosave,ind);}
 bool iid2.NeedsSave(tosaveitemscript tosave,int ind){return NeedsSave(tosave,ind);}
}
[Serializable] public class save_poi:ICopyable<save_poi>
{
 public UnityEngine.Vector3d posID;
 public List<save_usablescript> usables=new List<save_usablescript>();
 public List<save_tankscript> tanks=new List<save_tankscript>();
 public save_poi(){}
 public save_poi(save_poi from){if(from!=null)posID=from.posID;}
 public save_poi(poiGenScript.poiClass poi){if(poi!=null)posID=poi.posID;}
 public save_poi(poiScript poi){if(poi!=null)posID=poi.posID;}
 public void Load(poiGenScript.poiClass poi){if(poi!=null)poi.posID=posID;}
 public void Load(poiScript poi){if(poi!=null)poi.posID=posID;}
 public void Save(poiGenScript.poiClass poi){if(poi!=null)posID=poi.posID;}
 public void Save(poiScript poi){if(poi!=null)posID=poi.posID;}
 public save_poi Copy(){return new save_poi(this);}
}
[Serializable] public class save_partcondition:iid2,ICopyable<save_partcondition> { public uint id;public int ind;public save_partcondition Copy(){return new save_partcondition{id=id,ind=ind};} uint iid2.id{get{return id;}set{id=value;}} int iid2.ind{get{return ind;}} void iid2.Save(tosaveitemscript t,int i){} void iid2.Load(tosaveitemscript t,int i){} bool iid2.NeedsSave(tosaveitemscript t,int i){return false;} }
[Serializable] public class save_tankscript:iid2,ICopyable<save_tankscript> { public uint id;public int ind;public save_tankscript Copy(){return new save_tankscript{id=id,ind=ind};} uint iid2.id{get{return id;}set{id=value;}} int iid2.ind{get{return ind;}} void iid2.Save(tosaveitemscript t,int i){} void iid2.Load(tosaveitemscript t,int i){} bool iid2.NeedsSave(tosaveitemscript t,int i){return false;} }
[Serializable] public class save_usablescript:iid2,ICopyable<save_usablescript> { public uint id;public int ind;public save_usablescript Copy(){return new save_usablescript{id=id,ind=ind};} uint iid2.id{get{return id;}set{id=value;}} int iid2.ind{get{return ind;}} void iid2.Save(tosaveitemscript t,int i){} void iid2.Load(tosaveitemscript t,int i){} bool iid2.NeedsSave(tosaveitemscript t,int i){return false;} }
[Serializable] public class save_color:iid2,ICopyable<save_color> { public uint id;public int ind;public save_color Copy(){return new save_color{id=id,ind=ind};} uint iid2.id{get{return id;}set{id=value;}} int iid2.ind{get{return ind;}} void iid2.Save(tosaveitemscript t,int i){} void iid2.Load(tosaveitemscript t,int i){} bool iid2.NeedsSave(tosaveitemscript t,int i){return false;} }
[Serializable] public class save_randomType:iid2,ICopyable<save_randomType> { public uint id;public int ind;public save_randomType Copy(){return new save_randomType{id=id,ind=ind};} uint iid2.id{get{return id;}set{id=value;}} int iid2.ind{get{return ind;}} void iid2.Save(tosaveitemscript t,int i){} void iid2.Load(tosaveitemscript t,int i){} bool iid2.NeedsSave(tosaveitemscript t,int i){return false;} }
[Serializable] public class save_door_rot:iid2,ICopyable<save_door_rot> { public uint id;public int ind;public save_door_rot Copy(){return new save_door_rot{id=id,ind=ind};} uint iid2.id{get{return id;}set{id=value;}} int iid2.ind{get{return ind;}} void iid2.Save(tosaveitemscript t,int i){} void iid2.Load(tosaveitemscript t,int i){} bool iid2.NeedsSave(tosaveitemscript t,int i){return false;} }
[Serializable] public class save_randomScale:iid2,ICopyable<save_randomScale> { public uint id;public int ind;public save_randomScale Copy(){return new save_randomScale{id=id,ind=ind};} uint iid2.id{get{return id;}set{id=value;}} int iid2.ind{get{return ind;}} void iid2.Save(tosaveitemscript t,int i){} void iid2.Load(tosaveitemscript t,int i){} bool iid2.NeedsSave(tosaveitemscript t,int i){return false;} }
[Serializable] public class save_ammo:iid,ICopyable<save_ammo> { public uint id;public save_ammo Copy(){return new save_ammo{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_weapon:iid,ICopyable<save_weapon> { public uint id;public save_weapon Copy(){return new save_weapon{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_meterscript:iid,ICopyable<save_meterscript> { public uint id;public save_meterscript Copy(){return new save_meterscript{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_carscript:iid,ICopyable<save_carscript> { public uint id;public save_carscript Copy(){return new save_carscript{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_engine:iid,ICopyable<save_engine> { public uint id;public save_engine Copy(){return new save_engine{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_nyulscript:iid,ICopyable<save_nyulscript> { public uint id;public save_nyulscript Copy(){return new save_nyulscript{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_ragdollpos:iid,ICopyable<save_ragdollpos> { public uint id;public save_ragdollpos Copy(){return new save_ragdollpos{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_busdoors:iid,ICopyable<save_busdoors> { public uint id;public save_busdoors Copy(){return new save_busdoors{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_slideOnCollider:iid,ICopyable<save_slideOnCollider> { public uint id;public save_slideOnCollider Copy(){return new save_slideOnCollider{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_rendszam:iid,ICopyable<save_rendszam> { public uint id;public save_rendszam Copy(){return new save_rendszam{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_ginlamp:iid,ICopyable<save_ginlamp> { public uint id;public save_ginlamp Copy(){return new save_ginlamp{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_wear:iid,ICopyable<save_wear> { public uint id;public save_wear Copy(){return new save_wear{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_food:iid,ICopyable<save_food> { public uint id;public save_food Copy(){return new save_food{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_outfit:iid,ICopyable<save_outfit> { public uint id;public save_outfit Copy(){return new save_outfit{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_newai:iid,ICopyable<save_newai> { public uint id;public save_newai Copy(){return new save_newai{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }
[Serializable] public class save_painting:iid,ICopyable<save_painting> { public uint id;public save_painting Copy(){return new save_painting{id=id};} uint iid.id{get{return id;}set{id=value;}} void iid.Save(tosaveitemscript t){} void iid.Load(tosaveitemscript t){} bool iid.NeedsSave(tosaveitemscript t){return false;} }

namespace TldMp
{
 public static class StableJson
 {
  // Bodies replaced by extracted production IL (pass "world").
  static System.Runtime.Serialization.Json.DataContractJsonSerializer Serializer<T>(){throw new Exception("production IL missing");}
  public static byte[] Write<T>(T value){throw new Exception("production IL missing");}
  public static byte[] Write<T>(T value,int limit){throw new Exception("production IL missing");}
  public static T Read<T>(byte[] bytes){throw new Exception("production IL missing");}
  public static T Read<T>(byte[] bytes,int limit){throw new Exception("production IL missing");}
 }
 public static class StableGameBridge
 {
  static StableNode node;static string fingerprint="fixture";static bool loopbackMode;
  static bool capturing,applying;static System.Collections.Generic.List<byte[]> captured;
  static readonly System.Collections.Generic.Dictionary<ulong,byte[]> lastPlayers=new System.Collections.Generic.Dictionary<ulong,byte[]>();
  static readonly System.Collections.Generic.Dictionary<uint,MessageState> lastItems=new System.Collections.Generic.Dictionary<uint,MessageState>();
  static double fixtureClock;
  [Serializable] public sealed class ActiveItem {public uint ID;public bool Active;}
  [Serializable] public sealed class PoiState {public int Generator;public save_poi State;}
  [Serializable] public sealed class MessageState {public ulong Origin;public string Payload;}
  [Serializable] public sealed class WorldFrame
  {
   public int Version=3;public string Fingerprint;public float Time;
   public itemDataClass Items=new itemDataClass();
   public System.Collections.Generic.List<ActiveItem> Active=new System.Collections.Generic.List<ActiveItem>();
   public System.Collections.Generic.List<PoiState> Pois=new System.Collections.Generic.List<PoiState>();
   public System.Collections.Generic.List<MessageState> PlayerMessages=new System.Collections.Generic.List<MessageState>();
  }
  static ulong Own {get{return node==null?0:node.Own;}}
  static double Now {get{return fixtureClock;}}
  // Fixture-side helpers standing in for the production diagnostic/scene seams.
  public static readonly System.Collections.Generic.List<string> Diagnostics=new System.Collections.Generic.List<string>();
  public static readonly System.Collections.Generic.List<byte[]> Delivered=new System.Collections.Generic.List<byte[]>();
  static bool hostContext;
  static readonly System.Collections.Generic.List<byte[]> deferredHost=new System.Collections.Generic.List<byte[]>();
  static readonly System.Collections.Generic.List<byte[]> deferredClient=new System.Collections.Generic.List<byte[]>();
  internal static void JoinDiagnostic(string text){Diagnostics.Add(text);}
  static string MapDiagnostic(){return "fixture-map";}
  // Retry invalidation is exercised by NativeSafetyAdapterTests; this fixture
  // covers native save/load and snapshot replay without Unity coroutines.
  static void BeforeNativeSafetyWorldReplacement(){}
  static bool Find(uint id,out tosaveitemscript item){item=null;return savedatascript.s!=null && savedatascript.s.items.TryGetValue(id,out item) && item!=null;}
  internal static void Deliver(ulong origin,byte[] bytes)
  {
   Delivered.Add((byte[])bytes.Clone());
   if(bytes.Length>=42 && bytes[0]==18)
   {
    // Native held-state packet: the stock handler flips the pickupable's held
    // flag on the receiving copy. Stand-in at the native dispatch seam.
    uint heldID=BitConverter.ToUInt32(bytes,10);tosaveitemscript heldItem;
    if(savedatascript.s!=null && savedatascript.s.items.TryGetValue(heldID,out heldItem) && heldItem.P!=null)heldItem.P.Held=true;
   }
   if(bytes.Length>0 && bytes[0]==42 && bytes.Length==121 && BitConverter.ToInt32(bytes,5)!=0)
   {
    // Native attachment announcement. The child's ID may not be registered yet
    // (the live failure had announcements at t=26/28 with the wheel at epoch
    // zero). Consuming it must never wedge snapshot production.
    uint id=BitConverter.ToUInt32(bytes,1);tosaveitemscript child;
    if(savedatascript.s.items.TryGetValue(id,out child) && child.attachable!=null)ApplyAnnouncement(child.attachable,bytes);
    else (hostContext?deferredHost:deferredClient).Add((byte[])bytes.Clone());
   }
  }
  static void ApplyAnnouncement(attachablescript script,byte[] bytes)
  {
   save_attachable.Load(script,BitConverter.ToInt32(bytes,5),BitConverter.ToInt32(bytes,9),BitConverter.ToUInt32(bytes,13),
    BitConverter.ToInt32(bytes,17),G(bytes,21),BitConverter.ToInt32(bytes,45),G(bytes,49),G(bytes,73));
  }
  static UnityEngine.Vector3d G(byte[] b,int at)
  {return new UnityEngine.Vector3d(BitConverter.ToDouble(b,at),BitConverter.ToDouble(b,at+8),BitConverter.ToDouble(b,at+16));}
  public static int HostDeferred {get{return deferredHost.Count;}}
  public static int ClientDeferred {get{return deferredClient.Count;}}
  public static int ApplyDeferred(uint id)
  {
   var deferredAnnouncements=hostContext?deferredHost:deferredClient;
   int applied=0;for(int i=0;i<deferredAnnouncements.Count;)
   {
    var bytes=deferredAnnouncements[i];if(BitConverter.ToUInt32(bytes,1)!=id){i++;continue;}
    deferredAnnouncements.RemoveAt(i);tosaveitemscript child;
    if(savedatascript.s.items.TryGetValue(id,out child) && child.attachable!=null){ApplyAnnouncement(child.attachable,bytes);applied++;}
   }
   return applied;
  }
  // Bodies replaced by extracted production IL (pass "world"): the failure seam.
  static WorldFrame CaptureWorld(){throw new Exception("production IL missing");}
  internal static void SendWorld(ulong peer){throw new Exception("production IL missing");}
  internal static void ApplyWorld(byte[] bytes){throw new Exception("production IL missing");}
  // Far item placement queue flush: the replay world has no queued placements, so the stand-in is a no-op.
  internal static void FlushFarPlace(){}
  // Fixture API (test-only stand-ins at the native dispatch/scene seams).
  public static void UseNode(StableNode n){node=n;}
  public static void UseContext(bool host){hostContext=host;}
  public static void SetClock(double t){fixtureClock=t;}
  public static void ResetFixture()
  {Diagnostics.Clear();Delivered.Clear();deferredHost.Clear();deferredClient.Clear();lastPlayers.Clear();lastItems.Clear();capturing=false;captured=null;fixtureClock=0;}
  public static void CaptureRoute(byte[] bytes)
  {if(capturing){captured.Add((byte[])bytes.Clone());return;}}
  public static byte[] PlayerPacket(fpscontroller player)
  {
   bool seated=player!=null && player.Bsitting;
   var b=new byte[seated?59:51];b[0]=10;b[1]=seated?(byte)1:(byte)0;b[2]=0;
   Buffer.BlockCopy(BitConverter.GetBytes(Own),0,b,3,8);
   Buffer.BlockCopy(BitConverter.GetBytes(1000.0),0,b,11,8);
   Buffer.BlockCopy(BitConverter.GetBytes(0.0),0,b,19,8);
   Buffer.BlockCopy(BitConverter.GetBytes(2000.0),0,b,27,8);
   Buffer.BlockCopy(BitConverter.GetBytes(0f),0,b,35,4);
   Buffer.BlockCopy(BitConverter.GetBytes(30f),0,b,39,4);
   Buffer.BlockCopy(BitConverter.GetBytes(0f),0,b,43,4);
   Buffer.BlockCopy(BitConverter.GetBytes(1f),0,b,47,4);
   return b;
  }
  public static byte[] HeldPacket(uint id)
  {
   var b=new byte[42];b[0]=18;b[1]=0;
   Buffer.BlockCopy(BitConverter.GetBytes(Own),0,b,2,8);
   Buffer.BlockCopy(BitConverter.GetBytes(id),0,b,10,4);
   Buffer.BlockCopy(BitConverter.GetBytes(1001f),0,b,14,4);
   Buffer.BlockCopy(BitConverter.GetBytes(0.5f),0,b,18,4);
   Buffer.BlockCopy(BitConverter.GetBytes(2001f),0,b,22,4);
   Buffer.BlockCopy(BitConverter.GetBytes(0f),0,b,26,4);
   Buffer.BlockCopy(BitConverter.GetBytes(0f),0,b,30,4);
   Buffer.BlockCopy(BitConverter.GetBytes(0f),0,b,34,4);
   Buffer.BlockCopy(BitConverter.GetBytes(1),0,b,38,4);
   return b;
  }
 }
}

public static class WorldLoadReplayFixture
{
 static int checks;static bool printedB;
 static void Check(bool b,string m){checks++;if(!b)throw new Exception(m);}
 sealed class Flight {public ulong From,To;public byte[] Bytes;}
 static readonly List<Flight> wire=new List<Flight>();
 static readonly List<StableMessage> transfer=new List<StableMessage>();
 static StableNode hostNode,clientNode;
 static savedatascript hostSave,clientSave;
 static menuhandler hostMenu,clientMenu;
 static mainscript hostMain,clientMain;
 static napszakvaltakozas hostClock,clientClock;
 static itemPlaceRemoveScript hostPlace,clientPlace;
 static syncScript hostSync,clientSync;
 static double now;
 const ulong Host=10,Client=20,Other=30;
 const uint HostSession=1001,ClientSession=2002;
 const uint CarID=2147483763,WheelID=2147483764,CargoID=2147483765,HeldID=2147483766;

 static void EnterWorld(bool host)
 {
  savedatascript.s=host?hostSave:clientSave;
  menuhandler.s=host?hostMenu:clientMenu;
  mainscript.s=host?hostMain:clientMain;
  napszakvaltakozas.s=host?hostClock:clientClock;
  itemPlaceRemoveScript.s=host?hostPlace:clientPlace;
  syncScript.s=host?hostSync:clientSync;
  StableGameBridge.UseNode(host?hostNode:clientNode);StableGameBridge.UseContext(host);
 }
 static void WireNode(StableNode n)
 {
  n.ApplySnapshot=delegate(byte[] bytes){StableGameBridge.ApplyWorld(bytes);};
  n.SnapshotRequested=delegate(ulong peer){StableGameBridge.SendWorld(peer);};
  n.Deliver=delegate(ulong origin,byte[] bytes){StableGameBridge.Deliver(origin,bytes);};
  n.Trace=StableGameBridge.JoinDiagnostic;
 }
 static void NewSession(bool hostWorldLoaded)
 {
  wire.Clear();transfer.Clear();
  hostSave=new savedatascript();clientSave=new savedatascript();
  hostMenu=new menuhandler();clientMenu=new menuhandler();
  hostMain=new mainscript();clientMain=new mainscript();
  hostClock=new napszakvaltakozas();clientClock=new napszakvaltakozas();
  hostPlace=new itemPlaceRemoveScript();clientPlace=new itemPlaceRemoveScript();
  hostSync=new syncScript();clientSync=new syncScript();
  if(!hostWorldLoaded)hostMenu.currentMainMap=null;else hostMenu.currentMainMap=new mapSettingScript();
  clientMenu.currentMainMap=new mapSettingScript();
  itemPlaceRemoveScript.PlaceOneCalls=0;itemPlaceRemoveScript.GetOneCalls=0;
  tosaveitemscript.PreLoadCalls=0;tosaveitemscript.ForcePosCalls=0;partslotscript.CraftCalls=0;
  StableGameBridge.ResetFixture();
  hostNode=new StableNode(Host,Host,999,HostSession,"fixture",100);
  clientNode=new StableNode(Client,Host,999,ClientSession,"fixture",100);
  hostNode.Send=delegate(ulong peer,byte[] bytes,bool reliable)
  {
   wire.Add(new Flight{From=Host,To=peer,Bytes=bytes});
   var m=StableMessage.Decode(bytes);
   if(m!=null && (m.Op==StableOp.Begin||m.Op==StableOp.Part||m.Op==StableOp.End))transfer.Add(m);
  };
  clientNode.Send=delegate(ulong peer,byte[] bytes,bool reliable){wire.Add(new Flight{From=Client,To=peer,Bytes=bytes});};
  WireNode(hostNode);WireNode(clientNode);
  now=1;StableGameBridge.SetClock(now);
 }
 static void Pump()
 {
  int guard=0;
  while(wire.Count>0)
  {
   if(++guard>50000)throw new Exception("wire loop");
   var f=wire[0];wire.RemoveAt(0);
   bool toHost=f.To==Host;
   EnterWorld(toHost);
   if(f.Bytes.Length>0 && f.Bytes[0]!=254)StableGameBridge.Deliver(f.From,f.Bytes);
   else (toHost?hostNode:clientNode).Receive(f.From,f.Bytes,now);
  }
 }
 static void Tick(double at)
 {
  now=at;StableGameBridge.SetClock(now);
  EnterWorld(true);hostNode.Tick(now);Pump();
  EnterWorld(false);clientNode.Tick(now);Pump();
 }
 static void Handshake()
 {
  Tick(1);Tick(2);Tick(3);Tick(4);
  Check(clientNode.Compatible,"client completes the fingerprint handshake");
 }
 static void Join(double at)
 {
  EnterWorld(false);clientNode.RequestSnapshot();
  for(int i=0;i<20 && !clientNode.Ready;i++)Tick(at+i*.05);
  string tail="";int skip=Math.Max(0,StableGameBridge.Diagnostics.Count-8);
  for(int i=skip;i<StableGameBridge.Diagnostics.Count;i++)tail=tail+" | "+StableGameBridge.Diagnostics[i];
  Check(clientNode.Ready,"client reaches Live/Ready: "+(clientNode.Error??"no-error")+" status="+clientNode.JoinStatus+" stage="+clientNode.JoinStage+tail);
 }
 static TldMp.StableGameBridge.WorldFrame ReadFrame()
 {
  using(var ms=new System.IO.MemoryStream())
  {
   foreach(var m in transfer)if(m.Op==StableOp.Part)ms.Write(m.Data,0,m.Data.Length);
   return StableJson.Read<TldMp.StableGameBridge.WorldFrame>(ms.ToArray());
  }
 }
 static void Put(byte[] b,int at,byte[] v){Buffer.BlockCopy(v,0,b,at,v.Length);}
 static byte[] AttachAnnouncement(uint id,uint parentid,int index)
 {
  var b=new byte[121];b[0]=42;
  Put(b,1,BitConverter.GetBytes((int)id));
  Put(b,5,BitConverter.GetBytes(2));       // indexType 2: saved part slot
  Put(b,9,BitConverter.GetBytes(1));       // attachType 1: attached to a tosave parent
  Put(b,13,BitConverter.GetBytes((int)parentid));
  Put(b,17,BitConverter.GetBytes(-1));     // poigenid
  Put(b,45,BitConverter.GetBytes(index));
  return b;
  }
 static tosaveitemscript BuildCar(bool withWheel,bool withCargo,bool withHeld)
 {
  EnterWorld(true);
  var save=savedatascript.s;
  var carItem=save.InitAfterSpawn(save.SpawnItem(new save_item(CarID,371,new UnityEngine.Vector3d(1000,0,2000),new UnityEngine.Vector3(0,30,0))),CarID);
  if(withWheel)
  {
   var wheelItem=save.InitAfterSpawn(save.SpawnItem(new save_item(WheelID,43,new UnityEngine.Vector3d(1002,1,2000),new UnityEngine.Vector3(0,30,0))),WheelID);
   carItem.partslotscripts[0].Craft(wheelItem.attachable);
  }
  if(withCargo)
  {
   var cargoItem=save.InitAfterSpawn(save.SpawnItem(new save_item(CargoID,5,new UnityEngine.Vector3d(1000.5,.5f,2000),new UnityEngine.Vector3(0,30,0))),CargoID);
   carItem.physLocks[0].Store(cargoItem.P,false);
  }
  if(withHeld)
  {
   var heldItem=save.InitAfterSpawn(save.SpawnItem(new save_item(HeldID,7,new UnityEngine.Vector3d(1001,0,2001),new UnityEngine.Vector3(0,10,0))),HeldID);
   heldItem.P.Held=true;
  }
  return carItem;
 }
 static void SetupPlayer(tosaveitemscript carItem)
 {
  var seat=new seatscript {Car=carItem.car,driverSeat0=true,Key="seat:"+CarID+":0"};
  carItem.seats=new seatscript[]{seat};carItem.car.seats=carItem.seats;
  hostMain.player=new fpscontroller{Bsitting=true,seat=seat,Car=carItem.car};
 }
 static itemDataClass diskData;
 static void PersistToDisk()
 {
  EnterWorld(true);
  var save=savedatascript.s;save.data.itemData=new itemDataClass();
  foreach(var pair in new List<KeyValuePair<uint,tosaveitemscript>>(save.items))
   savedatascript.SaveToDictionary(pair.Value,save.data.itemData);
  // The save file survives; LoadStuff consumes only the in-memory copy.
  diskData=new itemDataClass();diskData.Copy(save.data.itemData);
 }
 static void HostReloadFromDisk(bool reverseOrder)
 {
  EnterWorld(true);
  var save=savedatascript.s;save.items.Clear();
  save.data.itemData=new itemDataClass();save.data.itemData.Copy(diskData);
  var order=new List<save_item>(save.data.itemData.items);
  if(reverseOrder)order.Reverse();
  foreach(var definition in order)
  {
   tosaveitemscript item;
   if(!save.items.TryGetValue(definition.id,out item))item=save.InitAfterSpawn(save.SpawnItem(definition),definition.id);
   item.loaded=false;
   save.LoadStuff(save.data,definition.id,true);
  }
  // Held state is authoritative player state, not save data: it survives with
  // the player across a save/reload.
  tosaveitemscript held;if(save.items.TryGetValue(HeldID,out held) && held.P!=null)held.P.Held=true;
 }
 static void AssertHostSnapshot(TldMp.StableGameBridge.WorldFrame frame,bool expectWheel,bool expectCargo)
 {
  Check(frame.Items.items.Count>=(expectWheel?3:2),"snapshot carries the item roster");
  Check(frame.Active.Count==frame.Items.items.Count,"every captured item has an active-state record");
  bool car=false,wheel=false,cargo=false;
  foreach(var record in frame.Items.items)
  {
   if(record.id==CarID){car=true;Check(record.prefabID==371,"Car09 root keeps its prefab identity");}
   if(record.id==WheelID)wheel=true;
   if(record.id==CargoID)cargo=true;
  }
  Check(car,"snapshot contains the Car09 root");
  Check(wheel==expectWheel,"snapshot contains exactly the mounted wheel children present in the save");
  Check(cargo==expectCargo,"snapshot contains exactly the stored cargo children present in the save");
  bool slot=false;
  foreach(var attach in frame.Items.attachable)
   if(attach.id==WheelID && attach.parentid==CarID && attach.indexType==2 && attach.index==0)slot=true;
  Check(slot==expectWheel,"snapshot contains the native saved attachment slot relation (wheel -> Car09 slot 0)");
  bool stored=false;
  foreach(var lockRecord in frame.Items.physLocks)
  {
   if(lockRecord.id!=CarID || lockRecord.IDs==null)continue;
   foreach(var id in lockRecord.IDs)if(id==CargoID)stored=true;
  }
  Check(stored==expectCargo,"snapshot contains the native mount-storage relation (cargo -> Car09 mount)");
  Check(frame.PlayerMessages.Count>=2,"snapshot contains authoritative player and held state");
  bool player=false,held=false;
  foreach(var message in frame.PlayerMessages)
  {
   var bytes=Convert.FromBase64String(message.Payload);
   if(bytes[0]==10)player=true;
   if(bytes[0]==18 && BitConverter.ToUInt32(bytes,10)==HeldID)held=true;
  }
  Check(player,"player state rides the snapshot");
  Check(held,"held item state rides the snapshot");
 }
 static void AssertClientWorld(bool expectWheel,bool expectCargo)
 {
  var save=clientSave;tosaveitemscript car,wheel,cargo;
  Check(save.items.TryGetValue(CarID,out car),"client world contains the Car09 root");
  Check(save.items.TryGetValue(WheelID,out wheel)==expectWheel,"client world contains exactly the snapshot's wheel children");
  Check(save.items.TryGetValue(CargoID,out cargo)==expectCargo,"client world contains exactly the snapshot's cargo children");
  if(expectWheel)
  {
   Check(wheel.attachable!=null && wheel.attachable.attached,"client wheel is attached through the native attachment relation");
   Check(wheel.attachable.slot!=null && wheel.transform.parent!=null && wheel.transform.parent.parent==car.transform,
    "client wheel is mounted in the Car09 saved slot under the car root");
  }
  if(expectCargo)
  {
   var mount=car.physLocks[0];
   Check(mount.stored.Contains(cargo.P) && cargo.P.physlock==mount,"client cargo is stored through the native mount-storage relation");
   Check(cargo.transform.parent==mount.transform,"client cargo keeps the mount parent");
  }
 }
 static void AssertSentWorld()
 {
  bool sent=false;foreach(var line in StableGameBridge.Diagnostics)if(line.StartsWith("SendWorld sent",StringComparison.Ordinal))sent=true;
  Check(sent,"production SendWorld reached its send boundary (the failed build threw before 'SendWorld sent')");
 }
 static void CheckSeamIsolation()
 {
  // The candidate has no graph admission machinery and the executed seam must
  // not depend on any. Name scan over the replay assembly (which carries the
  // transplanted production method bodies).
  string[] vocabulary=new string[]{"admission","bodyrole","topology","graph"};
  var asm=typeof(StableGameBridge).Assembly;
  foreach(var type in asm.GetTypes())
  {
   foreach(var word in vocabulary)Check(type.FullName.ToLowerInvariant().IndexOf(word)<0,"no graph admission dependency: type "+type.FullName);
   foreach(var method in type.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly))
    foreach(var word in vocabulary)Check(method.Name.ToLowerInvariant().IndexOf(word)<0,"no graph admission dependency: method "+type.FullName+"::"+method.Name);
  }
 }

 // Scenario A: native attachment announcements for a child that does not exist
 // yet must pass through the transport/journal without wedging snapshot
 // production, and the delayed child must attach through the native save record.
 static void AnnouncementsBeforeChild()
 {
  NewSession(true);
  var carItem=BuildCar(false,true,true);SetupPlayer(carItem);
  Handshake();transfer.Clear();Join(20);
  AssertSentWorld();
  AssertHostSnapshot(ReadFrame(),false,true);
  Check(clientNode.Ready,"snapshot production starts with the stored cargo only");
  // The host's native layer announces the mount twice while the wheel has no
  // registered item. Both native bytes pass the transport unchanged.
  Check(clientNode.PublishNative(AttachAnnouncement(WheelID,CarID,0)),"first pre-registration announcement passes the transport");
  Check(clientNode.PublishNative(AttachAnnouncement(WheelID,CarID,0)),"second pre-registration announcement passes the transport");
  Pump();
  Check(hostNode.Error==null && StableGameBridge.HostDeferred==2,"pre-registration announcements reach the host without wedging the node");
  EnterWorld(true);
  var save=savedatascript.s;
  var wheelItem=save.InitAfterSpawn(save.SpawnItem(new save_item(WheelID,43,new UnityEngine.Vector3d(1002,1,2000),new UnityEngine.Vector3(0,30,0))),WheelID);
  Check(!wheelItem.attachable.attached,"the delayed child exists unattached before its announcement applies");
  Check(StableGameBridge.ApplyDeferred(WheelID)==2,"both pre-registration announcements applied once the child exists");
  Check(wheelItem.attachable.attached && wheelItem.attachable.slot==carItem.partslotscripts[0],
   "deferred announcements run the production save_attachable.Load relation apply");
  // The next full snapshot carries the completed vehicle.
  StableGameBridge.Diagnostics.Clear();
  transfer.Clear();Join(40);
  AssertSentWorld();
  AssertHostSnapshot(ReadFrame(),true,true);
  AssertClientWorld(true,true);
  StableGameBridge.Diagnostics.Clear();
  transfer.Clear();Join(60);
  AssertSentWorld();
  Check(clientNode.Ready,"snapshot production and readiness survive pre-registration native announcements");
  Console.WriteLine("PASS: scenario A - native attachment announcements for a not-yet-registered child pass the transport without wedging production, delayed child creation attaches through the native save record, and the following snapshot/join completes.");
 }

 // Scenario B: fresh load, join after host world load.
 static void FreshLoadAndJoin()
 {
  NewSession(true);
  var carItem=BuildCar(true,true,true);SetupPlayer(carItem);
  Handshake();transfer.Clear();Join(20);
  AssertSentWorld();
  var frame=ReadFrame();
  AssertHostSnapshot(frame,true,true);
  AssertClientWorld(true,true);
  Check(itemPlaceRemoveScript.PlaceOneCalls>=1,"delayed child creation: the production save_physicslock.Load places the stored cargo during the car root's LoadStuff");
  Check(itemPlaceRemoveScript.GetOneCalls>=1,"delayed child creation: the production save_attachable.Load resolves the already-created parent");
  Check(tosaveitemscript.PreLoadCalls>=3,"production LoadStuff ran its PreLoadStuff/record stages per item");
  Check(clientSave.items[HeldID].P.Held,"held state survives the production apply path");
  Check(clientNode.Ready && hostNode.Peers[Client].Ready,"host marks the peer Ready after Ack and sends Live");
  if(!printedB){printedB=true;Console.WriteLine("PASS: scenario B - fresh native load -> host Resync -> production CaptureWorld/SendWorld -> client ApplyWorld -> readiness -> Ack/Live, root+children+slot/storage+player state in the snapshot.");}
 }

 // Scenario C: save/reload must not revive or lose items.
 static void ReloadNoReviveNoLoss()
 {
  FreshLoadAndJoin();
  // The game saved: real SaveToDictionary writes the persisted roster.
  PersistToDisk();
  Check(hostSave.data.itemData.attachable.Count==1 && hostSave.data.itemData.physLocks.Count==1,
   "the persisted save carries the captured attachment-slot and mount-storage relation records");
  // Reload 1: full world, everything must come back through real LoadStuff.
  HostReloadFromDisk(false);
  EnterWorld(true);
  tosaveitemscript car=null,wheel=null,cargo=null;
  Check(hostSave.items.TryGetValue(CarID,out car) && hostSave.items.TryGetValue(WheelID,out wheel) && hostSave.items.TryGetValue(CargoID,out cargo),
   "reload restores every saved item");
  Check(wheel.attachable.attached && wheel.attachable.slot==car.partslotscripts[0],"reload restores the saved attachment slot relation");
  Check(car.physLocks[0].stored.Contains(cargo.P) && cargo.P.physlock==car.physLocks[0],
   "reload restores the saved mount-storage relation (stored="+car.physLocks[0].stored.Count+" cargoPhys="+(cargo.P.physlock!=null)+" place="+itemPlaceRemoveScript.PlaceOneCalls+" records="+hostSave.data.itemData.physLocks.Count+" diskIDs="+(diskData.physLocks.Count>0 && diskData.physLocks[0].IDs!=null?diskData.physLocks[0].IDs.Length:-1)+")");
  // The wheel leaves the save (removed record). Reload must not revive it and
  // must not lose anything else.
  diskData.RemoveID(WheelID);
  Check(diskData.attachable.Count==0,"removing the wheel drops its attachment record from the persisted save");
  HostReloadFromDisk(false);
  EnterWorld(true);
  Check(!hostSave.items.ContainsKey(WheelID) && hostSave.items.ContainsKey(CarID) && hostSave.items.ContainsKey(CargoID),
   "reload does not revive a removed item and does not lose the rest");
  // The client resyncs the reloaded world.
  transfer.Clear();Join(40);
  AssertSentWorld();
  AssertHostSnapshot(ReadFrame(),false,true);
  AssertClientWorld(false,true);
  Check(!clientSave.items.ContainsKey(WheelID),"the reloaded snapshot cannot revive the removed wheel on the client");
  Check(clientSave.items.Count==hostSave.items.Count,"the second apply neither duplicates nor loses items");
  Console.WriteLine("PASS: scenario C - save -> reload -> reload-with-removal through real SaveToDictionary/LoadStuff; removed item never revived, remaining items and their relations never lost.");
 }

 // Scenario D: delayed child creation in both orders.
 static void DelayedChildCreation()
 {
  NewSession(true);
  BuildCar(true,true,true);SetupPlayer(hostSave.items[CarID]);
  PersistToDisk();
  // Reverse registration order: the child is created before its parent.
  HostReloadFromDisk(true);
  EnterWorld(true);
  tosaveitemscript car=null,wheel=null,cargo=null;
  Check(hostSave.items.TryGetValue(CarID,out car) && hostSave.items.TryGetValue(WheelID,out wheel) && hostSave.items.TryGetValue(CargoID,out cargo),
   "reverse-order reload creates every item exactly once");
  Check(wheel.attachable.attached && wheel.attachable.slot==car.partslotscripts[0],
   "child-before-parent creation still restores the saved attachment slot relation");
  Check(car.physLocks[0].stored.Contains(cargo.P),"child-before-parent creation still restores mount storage");
  int getBefore=itemPlaceRemoveScript.GetOneCalls;
  Check(getBefore>0,"production save_attachable.Load created/looked up the delayed parent through itemPlaceRemoveScript");
  Handshake();transfer.Clear();Join(20);
  AssertSentWorld();
  AssertHostSnapshot(ReadFrame(),true,true);
  AssertClientWorld(true,true);
  Check(itemPlaceRemoveScript.GetOneCalls>0 || itemPlaceRemoveScript.PlaceOneCalls>0,
   "the client apply creates whichever child arrives late through the production Load path");
  Console.WriteLine("PASS: scenario D - delayed child creation in both orders (child placed during the parent's LoadStuff, parent placed during the child's) with relations intact.");
 }

 // Scenario E: join before the host world load.
 static void JoinBeforeHostLoad()
 {
  NewSession(false);
  Handshake();
  EnterWorld(false);clientNode.RequestSnapshot();
  Tick(10);Tick(11);
  Check(!clientNode.Ready && clientNode.JoinStage=="WaitingHostLoad","the client waits in WaitingHostLoad while the host has no native world");
  bool blocked=false;foreach(var line in StableGameBridge.Diagnostics)if(line.StartsWith("SendWorld blocked",StringComparison.Ordinal))blocked=true;
  Check(blocked,"the host-side SendWorld seam reports the unloaded world instead of sending a snapshot");
  Tick(60);
  Check(clientNode.Error==null,"the pre-load wait carries no deadline the host cannot service");
  // The host load completes; the join finishes.
  var carItem=BuildCar(true,true,true);SetupPlayer(carItem);
  hostMenu.currentMainMap=new mapSettingScript();
  EnterWorld(false);clientNode.HostWorldLoaded=true;clientNode.RequestSnapshot();
  for(int i=0;i<20 && !clientNode.Ready;i++)Tick(61+i*.05);
  Check(clientNode.Ready,"join before host world load completes once the host load finishes: "+(clientNode.Error??"no-error"));
  AssertSentWorld();
  AssertClientWorld(true,true);
  Console.WriteLine("PASS: scenario E - join before host world load waits without burning the stage deadline, then completes after the host load and reaches Live/Ready.");
 }

 public static int Main(string[] args)
 {
  try
  {
   AnnouncementsBeforeChild();
   FreshLoadAndJoin();
   ReloadNoReviveNoLoss();
   DelayedChildCreation();
   JoinBeforeHostLoad();
   CheckSeamIsolation();
   Console.WriteLine("PASS: world-load production-method replay "+checks+" checks. Production IL executed: savedatascript.SaveToDictionary/LoadStuff/Save/Load/Copy/IndexOfID/Remove, itemDataClass.Copy/RemoveID, save_attachable.Save/Load, save_physicslock.Save/Load, StableGameBridge.CaptureWorld/SendWorld/ApplyWorld, StableJson.Write/Read over the real StableNode Resync/Begin/Part/End/Ack/Live path.");
   Console.WriteLine("Stand-ins only: no Unity runtime, no PhysX joints, no Steam, no scene streaming. Attachment-slot and mount-storage relations are the real native save records; native joint physics is not.");
   return 0;
  }
  catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}
