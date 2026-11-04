using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TldMp
{
    // Only the audited stock StartStuff asset is eligible. Native component
    // references never cross its direct-child boundaries (see asset audit).
    // Script references do, and are closed over and relocated before activation.
    // No source object is modified. No Awake/OnEnable/Start, registration or RNG
    // work is deliberately moved out of the native publication call.
    internal static class FixedPropConstruction
    {
        const string AssetHash="D62EA9EB5CB5A609F5B855EF33A4A97FA9047CD09D8B1F0DED2CB2AD913257DD";
        const int MaxNodes=20000, MaxComponents=70000, MaxManagedVisits=250000;
        static int assetStatus; // 0 unstarted, 1 checking, 2 verified, -1 refused
        static Plan pending;
        static itemdatabase database;
        static bool consumed, failed;
        static readonly Dictionary<Type,FieldInfo[]> fields=new Dictionary<Type,FieldInfo[]>();
        static readonly Stopwatch clock=Stopwatch.StartNew();
        static double Ms {get{return clock.ElapsedTicks*1000.0/Stopwatch.Frequency;}}

        internal static void StartAssetCheck()
        {
            if(Interlocked.CompareExchange(ref assetStatus,1,0)!=0)return;
            // Resolve Unity's path on the main thread; the worker only reads bytes.
            string path=Path.Combine(Application.dataPath,"sharedassets0.assets");
            ThreadPool.QueueUserWorkItem(delegate
            {
                int result=-1;
                try{using(var stream=File.OpenRead(path))using(var hash=SHA256.Create())
                    result=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","")==AssetHash?2:-1;}
                catch { }
                Volatile.Write(ref assetStatus,result);
            });
        }

        internal static void Clear()
        {
            if(pending!=null)pending.Dispose();pending=null;database=null;consumed=failed=false;
        }

        internal static void Tick()
        {
            StartAssetCheck();
            var scene=DataFromMenuScript.s;
            if(scene==null || scene.load || scene.multi || (!scene.mainmenu && !scene.newgame))return;
            if(database!=itemdatabase.s){Clear();database=itemdatabase.s;}
            if(scene.mainmenu)consumed=false;
            if(consumed || failed || database==null || Volatile.Read(ref assetStatus)!=2)return;
            int car=SelectedCar();
            if(pending!=null && pending.Car!=car){pending.Dispose();pending=null;}
            if(pending==null)
            {
                var prefabs=database.pois;if(prefabs==null)return;
                for(int i=0;i<prefabs.Length;i++)
                {
                    var poi=prefabs[i]==null?null:prefabs[i].GetComponent<poiScript>();
                    if(poi!=null && Eligible(poi.fixedSpawn)){pending=new Plan(poi.fixedSpawn,car);break;}
                }
            }
            if(pending!=null && !pending.Ready)
            {
                double start=Ms;
                try{do{pending.Step();}while(!pending.Ready && Ms-start<3.0);}
                catch(Exception e){Refuse(e);}
            }
        }

        static int SelectedCar()
        {
            var scene=DataFromMenuScript.s;
            return scene!=null && scene.mainmenu?(int)scene.startcar:
                mainscript.s!=null?(int)mainscript.s.StartCar:scene!=null?(int)scene.startcar:0;
        }
        static bool Eligible(GameObject source)
        {
            if(source==null || source.name!="StartStuff" || source.transform.childCount!=127)return false;
            var parts=source.GetComponents<Component>();
            var fixedScript=source.GetComponent<fixedSpawnScript>();
            return parts.Length==2 && parts[0] is Transform && fixedScript!=null &&
                fixedScript.useStartCar && fixedScript.startCars!=null && fixedScript.startCars.Count==23;
        }
        static void Refuse(Exception e)
        {
            string phase=pending==null?"admission":pending.Phase;
            if(pending!=null)pending.Dispose();pending=null;failed=true;
            DiagnosticFileLog.Log("[TLD MP construction] staged fixed props refused before publication phase="+phase+" reason="+e.GetType().Name+": "+e.Message);
        }

        internal static GameObject Instantiate(GameObject source,Vector3 position,Quaternion rotation,Transform parent)
        {
            // A load/join continues to use exactly the native construction path.
            var scene=DataFromMenuScript.s;
            if(failed || Volatile.Read(ref assetStatus)!=2 || !Eligible(source) ||
                scene==null || !scene.newgame || scene.load || scene.multi)
                return Object.Instantiate(source,position,rotation,parent);
            int car=SelectedCar();
            if(pending!=null && (pending.Source!=source || pending.Car!=car)){pending.Dispose();pending=null;}
            double start=Ms;int forced=0;
            try
            {
                if(pending==null)pending=new Plan(source,car);
                // Never yield after native ItemSpawn or between random draws. If
                // prewarming is late, finish here rather than reorder gameplay RNG.
                while(!pending.Ready){pending.Step();forced++;}
            }
            catch(Exception e){Refuse(e);return Object.Instantiate(source,position,rotation,parent);}
            var ready=pending;pending=null;consumed=true;
            // Once activation begins it may have side effects: never retry native.
            var result=ready.Publish(position,rotation,parent);
            DiagnosticFileLog.Log("[TLD MP construction] fixed props published car="+car+" branches="+ready.BranchCount+" omittedCars="+ready.Omitted+" nodes="+ready.Nodes+" forcedUnits="+forced+" demandMs="+(Ms-start).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" prepareMs="+ready.WorkMs.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" maxUnitMs="+ready.MaxUnitMs.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
            ready.Release();return result;
        }

        static FieldInfo[] SerializedFields(Type type)
        {
            FieldInfo[] result;if(fields.TryGetValue(type,out result))return result;
            var list=new List<FieldInfo>();
            for(var t=type;t!=null && t!=typeof(MonoBehaviour) && t!=typeof(Behaviour) && t!=typeof(Component) && t!=typeof(Object);t=t.BaseType)
                foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                    if(!f.IsStatic && !f.IsInitOnly && !f.IsNotSerialized && (f.IsPublic || f.IsDefined(typeof(SerializeField),true)))list.Add(f);
            result=list.ToArray();fields.Add(type,result);return result;
        }
        sealed class RefComparer:IEqualityComparer<object>
        {
            internal static readonly RefComparer Instance=new RefComparer();
            public new bool Equals(object a,object b){return ReferenceEquals(a,b);}
            public int GetHashCode(object o){return RuntimeHelpers.GetHashCode(o);}
        }

        sealed class Plan
        {
            internal readonly GameObject Source;internal readonly int Car;
            internal bool Ready;internal string Phase="plan";
            internal int Nodes,Omitted;internal double WorkMs,MaxUnitMs;
            internal int BranchCount{get{int n=0;for(int i=0;i<keep.Length;i++)if(keep[i])n++;return n;}}
            readonly Transform[] branches;
            readonly bool[] keep,scanned;
            readonly Queue<int> closure=new Queue<int>();
            readonly Dictionary<Object,Object> relocated=new Dictionary<Object,Object>();
            readonly Dictionary<Transform,int> branchIndex=new Dictionary<Transform,int>();
            readonly List<MonoBehaviour> scripts=new List<MonoBehaviour>();
            readonly List<MonoBehaviour> sourceScripts=new List<MonoBehaviour>();
            readonly HashSet<object> visited=new HashSet<object>(RefComparer.Instance);
            GameObject root;fixedSpawnScript sourceFixed;
            int next,repair,verify,managedVisits,components;

            internal Plan(GameObject source,int car)
            {
                Source=source;Car=car;sourceFixed=source.GetComponent<fixedSpawnScript>();
                int count=source.transform.childCount;branches=new Transform[count];keep=new bool[count];scanned=new bool[count];
                var cars=new HashSet<GameObject>(sourceFixed.startCars);
                GameObject selected=car>=0 && car<sourceFixed.startCars.Count?sourceFixed.startCars[car]:null;
                for(int i=0;i<count;i++)
                {
                    var t=branches[i]=source.transform.GetChild(i);branchIndex.Add(t,i);
                    // Only dormant alternatives named by the one-shot start-car
                    // list may be omitted. Retained scripts can pull them back in.
                    if(!cars.Contains(t.gameObject) || t.gameObject.activeSelf || t.gameObject==selected)Require(i);
                }
                // Stock entries are all direct children; refuse other layouts.
                foreach(var c in sourceFixed.startCars)
                    if(c!=null && c.transform.parent!=source.transform)throw new InvalidOperationException("nested starting-car entry");
            }
            void Require(int index){if(!keep[index]){keep[index]=true;closure.Enqueue(index);}}
            void FindReference(Object obj)
            {
                var go=obj as GameObject;var component=obj as Component;
                var t=go!=null?go.transform:component!=null?component.transform:null;
                if(t==null || t==Source.transform)return;
                while(t.parent!=null && t.parent!=Source.transform)t=t.parent;
                int index;if(branchIndex.TryGetValue(t,out index))Require(index);
            }
            Object Remap(Object obj,bool check)
            {
                if(obj==null)return obj;
                Object replacement;
                if(relocated.TryGetValue(obj,out replacement))
                {
                    if(check)throw new InvalidOperationException("unrelocated prefab reference");
                    return replacement;
                }
                var go=obj as GameObject;var component=obj as Component;
                var t=go!=null?go.transform:component!=null?component.transform:null;
                if(t!=null && (t==Source.transform || t.IsChildOf(Source.transform)))
                    throw new InvalidOperationException("reference into an omitted prefab branch");
                return obj;
            }
            object Walk(object value,int mode,int depth)
            {
                if(value==null)return null;
                if(++managedVisits>MaxManagedVisits || depth>64)throw new InvalidOperationException("serialized reference walk limit");
                var unity=value as Object;
                if(!ReferenceEquals(unity,null))
                {
                    if(mode==0){FindReference(unity);return value;}
                    return Remap(unity,mode==2);
                }
                Type type=value.GetType();
                if(type.IsPrimitive || type.IsEnum || type==typeof(string) || type==typeof(decimal))return value;
                if(!type.IsValueType && !visited.Add(value))return value;
                var array=value as Array;
                if(array!=null)
                {
                    if(array.Rank!=1)throw new InvalidOperationException("multidimensional serialized array");
                    for(int i=0;i<array.Length;i++){var old=array.GetValue(i);var changed=Walk(old,mode,depth+1);if(mode==1 && ((old!=null && old.GetType().IsValueType) || !ReferenceEquals(old,changed)))array.SetValue(changed,i);}return value;
                }
                var list=value as IList;
                if(list!=null)
                {
                    for(int i=0;i<list.Count;i++){var old=list[i];var changed=Walk(old,mode,depth+1);if(mode==1 && ((old!=null && old.GetType().IsValueType) || !ReferenceEquals(old,changed)))list[i]=changed;}return value;
                }
                if(!type.IsSerializable)return value;
                foreach(var f in SerializedFields(type))
                {
                    var old=f.GetValue(value);var changed=Walk(old,mode,depth+1);
                    if(mode==1 && (f.FieldType.IsValueType || !ReferenceEquals(old,changed)))f.SetValue(value,changed);
                }
                return value;
            }
            void WalkScript(MonoBehaviour script,int mode)
            {
                if(script==null)throw new InvalidOperationException("missing script in fixed prefab");
                visited.Clear();managedVisits=0;
                foreach(var f in SerializedFields(script.GetType()))
                {
                    var old=f.GetValue(script);var changed=Walk(old,mode,0);
                    if(mode==1 && (f.FieldType.IsValueType || !ReferenceEquals(old,changed)))f.SetValue(script,changed);
                }
            }
            internal void Step()
            {
                double start=Ms;var random=UnityEngine.Random.state;
                try{StepCore();}
                finally
                {
                    double cost=Ms-start;WorkMs+=cost;MaxUnitMs=Math.Max(MaxUnitMs,cost);
                    // Preparation must not consume gameplay random numbers.
                    if(!random.Equals(UnityEngine.Random.state)){UnityEngine.Random.state=random;throw new InvalidOperationException("inactive construction consumed random numbers");}
                }
            }
            void StepCore()
            {
                if(closure.Count>0)
                {
                    int index=closure.Dequeue();if(scanned[index])return;scanned[index]=true;
                    sourceScripts.Clear();branches[index].GetComponentsInChildren<MonoBehaviour>(true,sourceScripts);
                    for(int i=0;i<sourceScripts.Count;i++)WalkScript(sourceScripts[i],0);
                    return;
                }
                if(root==null)
                {
                    Phase="root";root=new GameObject(Source.name+"(Clone)");root.SetActive(false);
                    root.layer=Source.layer;root.tag=Source.tag;root.hideFlags=Source.hideFlags;
                    root.transform.localScale=Source.transform.localScale;
                    Object.DontDestroyOnLoad(root);
                    var f=root.AddComponent<fixedSpawnScript>();f.enabled=sourceFixed.enabled;f.useStartCar=sourceFixed.useStartCar;
                    f.startCars=new List<GameObject>(sourceFixed.startCars.Count);
                    relocated.Add(Source,root);relocated.Add(Source.transform,root.transform);relocated.Add(sourceFixed,f);
                    for(int i=0;i<keep.Length;i++)if(!keep[i])Omitted++;
                    return;
                }
                while(next<branches.Length && !keep[next])next++;
                if(next<branches.Length)
                {
                    Phase="clone";var source=branches[next++];
                    var clone=Object.Instantiate(source.gameObject,root.transform,false);clone.name=source.name;
                    Pair(source,clone.transform);return;
                }
                if(repair<scripts.Count){Phase="relocate";int end=Math.Min(repair+32,scripts.Count);while(repair<end)WalkScript(scripts[repair++],1);return;}
                if(verify<scripts.Count){Phase="verify";int end=Math.Min(verify+32,scripts.Count);while(verify<end)WalkScript(scripts[verify++],2);return;}
                var target=root.GetComponent<fixedSpawnScript>();
                foreach(var car in sourceFixed.startCars)
                {
                    Object clone;target.startCars.Add(car!=null && relocated.TryGetValue(car,out clone)?(GameObject)clone:null);
                }
                if(Car>=0 && Car<target.startCars.Count && sourceFixed.startCars[Car]!=null && target.startCars[Car]==null)
                    throw new InvalidOperationException("selected car missing");
                Phase="ready";Ready=true;
            }
            void Pair(Transform source,Transform target)
            {
                if(++Nodes>MaxNodes)throw new InvalidOperationException("fixed prefab node limit");
                if(source.childCount!=target.childCount || source.gameObject.activeSelf!=target.gameObject.activeSelf)
                    throw new InvalidOperationException("clone hierarchy changed");
                relocated.Add(source.gameObject,target.gameObject);
                var a=source.GetComponents<Component>();var b=target.GetComponents<Component>();
                components+=a.Length;if(components>MaxComponents || a.Length!=b.Length)throw new InvalidOperationException("clone component layout changed");
                for(int i=0;i<a.Length;i++)
                {
                    if(a[i]==null || b[i]==null || a[i].GetType()!=b[i].GetType())throw new InvalidOperationException("clone component type changed");
                    relocated.Add(a[i],b[i]);var script=b[i] as MonoBehaviour;if(script!=null)scripts.Add(script);
                }
                for(int i=0;i<source.childCount;i++)Pair(source.GetChild(i),target.GetChild(i));
            }
            internal GameObject Publish(Vector3 position,Quaternion rotation,Transform parent)
            {
                if(!Ready || root==null || root.activeSelf)throw new InvalidOperationException("fixed prefab not ready");
                var result=root;root=null;
                if(parent!=null)UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(result,parent.gameObject.scene);
                result.transform.SetParent(parent,false);result.transform.SetPositionAndRotation(position,rotation);
                result.SetActive(Source.activeSelf);return result;
            }
            internal void Release(){relocated.Clear();scripts.Clear();sourceScripts.Clear();visited.Clear();}
            internal void Dispose(){if(root!=null){Object.Destroy(root);root=null;}Release();}
        }
    }
}
