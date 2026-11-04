// Executes the production constructor with a deterministic Unity stand-in.
// This verifies graph/state/fallback behavior, not actual Unity lifecycle costs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using TldMp;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField:Attribute {}
    public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
    public struct Quaternion {public float x,y,z,w;}
    public static class Random {public struct State {public int value;}static State current;public static State state{get{return current;}set{current=value;}}public static int Draw(){current.value=unchecked(current.value*1664525+1013904223);return current.value;}}
    public static class Application {public static string dataPath=".";}
    public class Object
    {
        public static int NativeBuilds,BranchBuilds,Destroyed;public static Action BeforeBranch,BeforeActivate;
        public string name;public int hideFlags;
        public static bool operator ==(Object a,Object b){return ReferenceEquals(a,b);}public static bool operator !=(Object a,Object b){return !ReferenceEquals(a,b);}
        public override bool Equals(object o){return ReferenceEquals(this,o);}public override int GetHashCode(){return base.GetHashCode();}
        public static void DontDestroyOnLoad(Object o){}
        public static void Destroy(Object o){Destroyed++;var g=o as GameObject;if(g!=null){g.activeSelf=false;g.transform.SetParent(null,false);}}
        public static GameObject Instantiate(GameObject source,Vector3 pos,Quaternion rot,Transform parent)
        {NativeBuilds++;var r=Clone(source,parent);r.transform.SetPositionAndRotation(pos,rot);return r;}
        public static GameObject Instantiate(GameObject source,Transform parent,bool world)
        {BranchBuilds++;if(BeforeBranch!=null)BeforeBranch();return Clone(source,parent);}
        static GameObject Clone(GameObject source,Transform parent)
        {
            var map=new Dictionary<Object,Object>();var result=Tree(source,parent,map);
            foreach(var pair in map.ToArray())
            {
                var from=pair.Key as Component;var to=pair.Value as Component;if(from==null || from is Transform)continue;
                var seen=new Dictionary<object,object>();
                foreach(var f in Fields(from.GetType()))f.SetValue(to,Copy(f.GetValue(from),map,seen));
            }
            return result;
        }
        static IEnumerable<FieldInfo> Fields(Type type)
        {
            for(var t=type;t!=null && t!=typeof(Component) && t!=typeof(Object);t=t.BaseType)
                foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                    if(!f.IsStatic && !f.IsInitOnly && !f.IsNotSerialized && (f.IsPublic || f.IsDefined(typeof(SerializeField),true)))yield return f;
        }
        static object Copy(object value,Dictionary<Object,Object> map,Dictionary<object,object> seen)
        {
            if(value==null)return null;var u=value as Object;if(u!=null){Object r;return map.TryGetValue(u,out r)?r:u;}
            var type=value.GetType();if(type.IsPrimitive || type.IsEnum || type==typeof(string) || !type.IsSerializable)return value;
            object previous;if(!type.IsValueType && seen.TryGetValue(value,out previous))return previous;
            var array=value as Array;if(array!=null){var r=Array.CreateInstance(type.GetElementType(),array.Length);seen[value]=r;for(int i=0;i<array.Length;i++)r.SetValue(Copy(array.GetValue(i),map,seen),i);return r;}
            var list=value as IList;if(list!=null){var r=(IList)Activator.CreateInstance(type);seen[value]=r;foreach(var v in list)r.Add(Copy(v,map,seen));return r;}
            var result=FormatterServices.GetUninitializedObject(type);if(!type.IsValueType)seen[value]=result;
            foreach(var f in Fields(type))f.SetValue(result,Copy(f.GetValue(value),map,seen));return result;
        }
        static GameObject Tree(GameObject source,Transform parent,Dictionary<Object,Object> map)
        {
            var result=new GameObject(source.name+"(Clone)");result.activeSelf=source.activeSelf;result.layer=source.layer;result.tag=source.tag;
            result.transform.SetParent(parent,false);result.transform.localScale=source.transform.localScale;result.transform.position=source.transform.position;
            map.Add(source,result);map.Add(source.transform,result.transform);
            foreach(var c in source.Components)if(!(c is Transform))map.Add(c,result.AddComponent(c.GetType()));
            foreach(var child in source.transform.Children)Tree(child.gameObject,result.transform,map);return result;
        }
    }
    public class Component:Object
    {
        public GameObject gameObject;public Transform transform{get{return gameObject.transform;}}
        public T[] GetComponents<T>() where T:Component{return gameObject.GetComponents<T>();}
        public T GetComponent<T>() where T:Component{return gameObject.GetComponent<T>();}
        public void GetComponentsInChildren<T>(bool inactive,List<T> values) where T:Component {gameObject.GetComponentsInChildren(inactive,values);}
    }
    public class Behaviour:Component {public bool enabled=true;}
    public class MonoBehaviour:Behaviour {}
    public class Transform:Component
    {
        public Transform parent;public Vector3 localScale,position;public Quaternion rotation;
        internal readonly List<Transform> Children=new List<Transform>();public int childCount{get{return Children.Count;}}
        public Transform GetChild(int i){return Children[i];}
        public void SetParent(Transform p,bool world){if(parent!=null)parent.Children.Remove(this);parent=p;if(p!=null)p.Children.Add(this);}
        public void SetPositionAndRotation(Vector3 p,Quaternion r){position=p;rotation=r;}
        public bool IsChildOf(Transform p){for(var t=parent;t!=null;t=t.parent)if(t==p)return true;return false;}
    }
    public class GameObject:Object
    {
        public bool activeSelf=true;public int layer;public string tag="Untagged";public Transform transform;public int scene;
        public bool activeInHierarchy{get{return activeSelf && (transform.parent==null || transform.parent.gameObject.activeInHierarchy);}}
        internal readonly List<Component> Components=new List<Component>();
        public GameObject(string n){name=n;transform=new Transform{gameObject=this,name=n};Components.Add(transform);}
        public T AddComponent<T>() where T:Component,new(){return (T)AddComponent(typeof(T));}
        public Component AddComponent(Type t){var c=(Component)Activator.CreateInstance(t);c.gameObject=this;Components.Add(c);return c;}
        public T GetComponent<T>() where T:Component{return Components.OfType<T>().FirstOrDefault();}
        public T[] GetComponents<T>() where T:Component{return Components.OfType<T>().ToArray();}
        public void GetComponentsInChildren<T>(bool inactive,List<T> values) where T:Component {values.Clear();Query(inactive,values,true);}
        void Query<T>(bool inactive,List<T> values,bool first) where T:Component {if(!first && !inactive && !activeSelf)return;values.AddRange(Components.OfType<T>());foreach(var child in transform.Children)child.gameObject.Query(inactive,values,false);}
        public void SetActive(bool active){bool was=activeInHierarchy;activeSelf=active;if(!was && activeInHierarchy && Object.BeforeActivate!=null)Object.BeforeActivate();}
    }
}
namespace UnityEngine.SceneManagement {public static class SceneManager {public static void MoveGameObjectToScene(UnityEngine.GameObject g,int scene){g.scene=scene;}}}
public class itemdatabase:UnityEngine.MonoBehaviour {public static itemdatabase s;public enum CarType {None,One,Two}public UnityEngine.GameObject[] pois;}
public class DataFromMenuScript {public static DataFromMenuScript s;public bool mainmenu,newgame,load,multi;public itemdatabase.CarType startcar;}
public class mainscript {public static mainscript s;public itemdatabase.CarType StartCar;}
public class fixedSpawnScript:UnityEngine.MonoBehaviour {public bool useStartCar;public List<UnityEngine.GameObject> startCars;}
public class poiScript:UnityEngine.MonoBehaviour {public UnityEngine.GameObject fixedSpawn;}
[Serializable] public struct RefStruct {public UnityEngine.Object target;}
[Serializable] public class RefNode {public UnityEngine.Object target;public RefNode cycle;}
public class GraphScript:UnityEngine.MonoBehaviour
{
    public UnityEngine.Object target;public UnityEngine.Object[] array;public RefStruct value;public List<RefStruct> values;
    public RefNode node;[UnityEngine.SerializeField] UnityEngine.Object hidden;public void SetHidden(UnityEngine.Object o){hidden=o;}public UnityEngine.Object Hidden{get{return hidden;}}
}
namespace TldMp {internal static class DiagnosticFileLog {internal static readonly List<string> Lines=new List<string>();internal static void Log(object o){Lines.Add(o.ToString());}}}
public static class FixedPropConstructionTests
{
    static int checks;
    static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
    static void Status(int value){typeof(FixedPropConstruction).GetField("assetStatus",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,value);}
    static UnityEngine.GameObject Fixture(int car)
    {
        FixedPropConstruction.Clear();Status(2);UnityEngine.Object.BeforeActivate=UnityEngine.Object.BeforeBranch=null;
        UnityEngine.Object.NativeBuilds=UnityEngine.Object.BranchBuilds=0;DiagnosticFileLog.Lines.Clear();
        DataFromMenuScript.s=new DataFromMenuScript{newgame=true,startcar=(itemdatabase.CarType)car};mainscript.s=new mainscript{StartCar=(itemdatabase.CarType)car};
        var root=new UnityEngine.GameObject("StartStuff");var f=root.AddComponent<fixedSpawnScript>();f.useStartCar=true;f.startCars=new List<UnityEngine.GameObject>();
        for(int i=0;i<23;i++)f.startCars.Add(null);
        for(int i=0;i<127;i++)
        {
            var c=new UnityEngine.GameObject("child"+i);c.transform.SetParent(root.transform,false);c.AddComponent<GraphScript>();
            if(i<22){c.activeSelf=false;f.startCars[i+1]=c;}
        }
        var a=root.transform.GetChild(30).GetComponent<GraphScript>();var retained=root.transform.GetChild(2).gameObject;
        a.target=retained;a.array=new UnityEngine.Object[]{retained.GetComponent<GraphScript>(),root};a.SetHidden(root.GetComponent<fixedSpawnScript>());
        a.value=new RefStruct{target=retained.transform};a.values=new List<RefStruct>{a.value};a.node=new RefNode{target=retained};a.node.cycle=a.node;
        var owner=new UnityEngine.GameObject("poi");owner.AddComponent<poiScript>().fixedSpawn=root;
        itemdatabase.s=new UnityEngine.GameObject("database").AddComponent<itemdatabase>();itemdatabase.s.pois=new[]{owner};
        UnityEngine.Random.state=new UnityEngine.Random.State{value=131};return root;
    }
    static UnityEngine.GameObject Build(UnityEngine.GameObject source){return FixedPropConstruction.Instantiate(source,new UnityEngine.Vector3(10,20,30),new UnityEngine.Quaternion(),new UnityEngine.GameObject("world").transform);}
    static void Graph(UnityEngine.GameObject source,UnityEngine.GameObject result,int car)
    {
        var f=result.GetComponent<fixedSpawnScript>();Check(f.startCars.Count==23,"start-car indexing");
        if(car>0)Check(f.startCars[car]!=null && f.startCars[car]!=source.GetComponent<fixedSpawnScript>().startCars[car],"selected car cloned");
        Check(f.startCars[3]!=null,"referenced dormant car retained");
        var child=result.transform.Children.Single(t=>t.gameObject.name=="child30").gameObject.GetComponent<GraphScript>();
        Check(child.target==f.startCars[3],"cross-branch gameobject");Check(child.array[0]==f.startCars[3].GetComponent<GraphScript>(),"array component reference");
        Check(child.array[1]==result && child.Hidden==f,"root references");Check(child.value.target==f.startCars[3].transform,"boxed struct reference");
        Check(child.values[0].target==f.startCars[3].transform,"list of structs");Check(child.node.target==f.startCars[3] && child.node.cycle==child.node,"managed cycle");
        Check(result.transform.position.x==10 && result.transform.position.y==20,"publication position");
        Check(source.transform.childCount==127 && source.GetComponent<fixedSpawnScript>().startCars[1]!=null,"source asset unchanged");
        Check(source.transform.GetChild(30).GetComponent<GraphScript>().target==source.transform.GetChild(2).gameObject,"source nested graph unchanged");
    }
    public static int Main()
    {
        try
        {
            for(int car=0;car<23;car++)
            {
                var source=Fixture(car);int seed=UnityEngine.Random.state.value;int activations=0;
                UnityEngine.Object.BeforeActivate=delegate{activations++;};var result=Build(source);if(UnityEngine.Object.NativeBuilds>0)throw new Exception("unexpected fallback: "+String.Join(" | ",DiagnosticFileLog.Lines.ToArray()));Graph(source,result,car);
                Check(UnityEngine.Object.NativeBuilds==0,"optimized constructor selected");Check(activations==1,"single publication activation");
                Check(UnityEngine.Random.state.value==seed,"no preparation random draws");Check(UnityEngine.Object.BranchBuilds<=107,"unused vehicles not constructed");
                Check(DiagnosticFileLog.Lines.Last().Contains("fixed props published"),"publication diagnostics");
            }
            var asset=Fixture(1);DataFromMenuScript.s.mainmenu=true;DataFromMenuScript.s.newgame=false;
            for(int i=0;i<1000;i++)FixedPropConstruction.Tick();int warmed=UnityEngine.Object.BranchBuilds;
            Check(warmed>0,"prewarm advanced");DataFromMenuScript.s.mainmenu=false;DataFromMenuScript.s.newgame=true;
            var warm=Build(asset);Graph(asset,warm,1);Check(UnityEngine.Object.BranchBuilds==warmed,"ready hierarchy reused");
            Check(DiagnosticFileLog.Lines.Last().Contains("forcedUnits=0"),"prewarmed demand has no preparation");
            asset=Fixture(1);DataFromMenuScript.s.mainmenu=true;for(int i=0;i<1000;i++)FixedPropConstruction.Tick();
            DataFromMenuScript.s.mainmenu=false;mainscript.s.StartCar=itemdatabase.CarType.Two;
            var changed=Build(asset);Graph(asset,changed,2);Check(changed.GetComponent<fixedSpawnScript>().startCars[1]==null,"selection change invalidates prepared car");
            asset=Fixture(1);Status(-1);Build(asset);Check(UnityEngine.Object.NativeBuilds==1,"modified asset native fallback");
            asset=Fixture(1);DataFromMenuScript.s.load=true;Build(asset);Check(UnityEngine.Object.NativeBuilds==1,"existing save native fallback");
            asset=Fixture(1);DataFromMenuScript.s.multi=true;Build(asset);Check(UnityEngine.Object.NativeBuilds==1,"joining guest native fallback");
            asset=Fixture(1);asset.name="modded";Build(asset);Check(UnityEngine.Object.NativeBuilds==1,"unknown prefab native fallback");
            asset=Fixture(1);int before=UnityEngine.Random.state.value;
            UnityEngine.Object.BeforeBranch=delegate{UnityEngine.Object.BeforeBranch=null;UnityEngine.Random.Draw();};Build(asset);
            Check(UnityEngine.Object.NativeBuilds==1 && UnityEngine.Random.state.value==before,"random mutation refuses and restores before fallback");
            asset=Fixture(1);UnityEngine.Object.BeforeBranch=delegate{throw new InvalidOperationException("injected clone failure");};Build(asset);
            Check(UnityEngine.Object.NativeBuilds==1,"prepublication exception falls back once");UnityEngine.Object.BeforeBranch=null;
            asset=Fixture(1);UnityEngine.Object.BeforeActivate=delegate{throw new Exception("publication error");};bool threw=false;
            try{Build(asset);}catch(Exception){threw=true;}Check(threw && UnityEngine.Object.NativeBuilds==0,"never retry after publication side effects");
            asset=Fixture(1);DataFromMenuScript.s.mainmenu=true;for(int i=0;i<1000;i++)FixedPropConstruction.Tick();int destroyed=UnityEngine.Object.Destroyed;
            FixedPropConstruction.Clear();Check(UnityEngine.Object.Destroyed==destroyed+1,"cancel destroys unpublished hierarchy");
            Console.WriteLine("PASS: staged fixed-prop production constructor "+checks+" graph, selection, publication, cancellation and fallback checks.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
