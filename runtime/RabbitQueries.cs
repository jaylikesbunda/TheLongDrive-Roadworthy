using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TldMp
{
    internal static class RabbitQueries
    {
        sealed class Part
        {
            public nyulscript Root;
            public Collider Source;
            public GameObject Proxy;
            public Collider Shape;
            public bool BaselineOn;
        }
        static readonly List<Part> parts=new List<Part>();
        static readonly Dictionary<GameObject,Part> byProxy=new Dictionary<GameObject,Part>();
        static readonly Dictionary<Collider,Part> bySource=new Dictionary<Collider,Part>();
        static Scene scene;static bool sceneReady,disabled,logged;
        static GameObject container;
        static int lastFrame=-1;
        static readonly RaycastHit[] hitBuffer=new RaycastHit[4096];
        static readonly Collider[] boxBuffer=new Collider[4096];
        public static bool Active{get{return sceneReady&&!disabled&&parts.Count>0;}}
        public static System.Action<weaponscript,int,Vector3,Vector3> WeaponIntent;
        public static System.Action<meleeweaponscript,Vector3,Vector3,Quaternion> MeleeIntent;

        static void Note(string m){if(!logged){logged=true;Debug.LogWarning("[TLD MP rabbit-queries] "+m);}}
        static int seq;
        static bool EnsureScene()
        {
            if(disabled)return false;
            if(sceneReady)return true;
            var before=SceneManager.GetActiveScene();
            Scene s=new Scene();
            GameObject go=null;
            try
            {
                s=SceneManager.CreateScene("tldmp-rabbit-queries-"+(++seq),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var ps=s.GetPhysicsScene();
                if(!s.IsValid()||ps==Physics.defaultPhysicsScene)throw new System.InvalidOperationException("Private physics scene unavailable");
                go=new GameObject("rabbit-query-proxies");
                SceneManager.MoveGameObjectToScene(go,s);
                scene=s;container=go;sceneReady=true;
                return true;
            }
            catch(System.Exception)
            {
                if(go!=null)Object.Destroy(go);
                if(s.IsValid()&&!s.Equals(before))SceneManager.UnloadSceneAsync(s);
                disabled=true;Note("private scene initialization failed; world queries only");return false;
            }
            finally{SceneManager.SetActiveScene(before);}
        }
        static GameObject Proxy(Transform t)
        {
            var go=new GameObject("rq");
            SceneManager.MoveGameObjectToScene(go,scene);
            go.transform.SetParent(container.transform,false);
            go.transform.position=t.position;go.transform.rotation=t.rotation;go.transform.localScale=t.lossyScale;
            go.SetActive(true);
            dirty=true;
            return go;
        }
        static void RetireShape(Part p){if(p!=null&&p.Shape!=null){p.Shape.enabled=false;Object.Destroy(p.Shape);p.Shape=null;}}
        static bool RefreshShape(Part p,Collider src)
        {
            if(p.Shape==null||p.Shape.GetType()!=src.GetType()){RetireShape(p);p.Shape=CloneShape(src,p.Proxy);dirty=true;return p.Shape!=null;}
            var s=p.Shape;
            if(src is CapsuleCollider)
            {
                var c=(CapsuleCollider)src;var n=(CapsuleCollider)s;
                if(n.direction!=c.direction){n.direction=c.direction;dirty=true;}
                if(n.center!=c.center){n.center=c.center;dirty=true;}
                if(n.radius!=c.radius){n.radius=c.radius;dirty=true;}
                if(n.height!=c.height){n.height=c.height;dirty=true;}
            }
            else if(src is SphereCollider)
            {
                var c=(SphereCollider)src;var n=(SphereCollider)s;
                if(n.center!=c.center){n.center=c.center;dirty=true;}
                if(n.radius!=c.radius){n.radius=c.radius;dirty=true;}
            }
            else if(src is BoxCollider)
            {
                var c=(BoxCollider)src;var n=(BoxCollider)s;
                if(n.center!=c.center){n.center=c.center;dirty=true;}
                if(n.size!=c.size){n.size=c.size;dirty=true;}
            }
            else if(src is MeshCollider)
            {
                var c=(MeshCollider)src;var n=(MeshCollider)s;
                if(n.sharedMesh!=c.sharedMesh){n.sharedMesh=c.sharedMesh;dirty=true;}
                if(n.convex!=c.convex){n.convex=c.convex;dirty=true;}
            }
            if(s.isTrigger!=src.isTrigger){s.isTrigger=src.isTrigger;dirty=true;}
            return true;
        }
        static Collider CloneShape(Collider src,GameObject go)
        {
            if(src is CapsuleCollider)
            {
                var c=(CapsuleCollider)src;var n=go.AddComponent<CapsuleCollider>();
                n.direction=c.direction;n.center=c.center;n.radius=c.radius;n.height=c.height;
                n.isTrigger=c.isTrigger;
                return n;
            }
            if(src is SphereCollider)
            {
                var c=(SphereCollider)src;var n=go.AddComponent<SphereCollider>();
                n.center=c.center;n.radius=c.radius;n.isTrigger=c.isTrigger;
                return n;
            }
            if(src is BoxCollider)
            {
                var c=(BoxCollider)src;var n=go.AddComponent<BoxCollider>();
                n.center=c.center;n.size=c.size;n.isTrigger=c.isTrigger;
                return n;
            }
            if(src is MeshCollider)
            {
                var c=(MeshCollider)src;var n=go.AddComponent<MeshCollider>();
                n.sharedMesh=c.sharedMesh;n.convex=c.convex;n.isTrigger=c.isTrigger;
                return n;
            }
            Note("unsupported collider "+src.GetType().Name+" skipped");
            return null;
        }
        internal static void SyncRoot(nyulscript root,IList<Collider> sources,IList<bool> baselineEnabled)
        {
            if(root==null||sources==null||baselineEnabled==null||sources.Count!=baselineEnabled.Count)return;
            if(!EnsureScene())return;
            for(int i=parts.Count-1;i>=0;i--)
            {
                var p=parts[i];
                if(!ReferenceEquals(p.Root,root))continue;
                int idx=-1;
                for(int j=0;j<sources.Count;j++)if(ReferenceEquals(sources[j],p.Source)){idx=j;break;}
                if(idx<0||p.Source==null||p.Source.gameObject==null){retire(p);parts.RemoveAt(i);}
            }
            for(int j=0;j<sources.Count;j++)
            {
                var src=sources[j];
                if(src==null)continue;
                Part p;
                if(!bySource.TryGetValue(src,out p))
                {
                    p=new Part{Root=root,Source=src,Proxy=Proxy(src.transform)};
                    parts.Add(p);byProxy[p.Proxy]=p;bySource[src]=p;
                }
                if(!RefreshShape(p,src)){retire(p);parts.Remove(p);continue;}
                p.BaselineOn=baselineEnabled[j];
            }
            Follow();
        }
        static void retire(Part p)
        {
            if(p==null)return;
            if(p.Shape!=null)p.Shape.enabled=false;
            bySource.Remove(p.Source);
            if(p.Proxy!=null){byProxy.Remove(p.Proxy);Object.Destroy(p.Proxy);}
            dirty=true;
        }
        internal static void RemoveRoot(nyulscript root)
        {
            if((object)root==null)return;
            for(int i=parts.Count-1;i>=0;i--)
                if(ReferenceEquals(parts[i].Root,root)){retire(parts[i]);parts.RemoveAt(i);}
            if(parts.Count==0&&sceneReady)Clear();
        }
        internal static void Clear()
        {
            for(int i=0;i<parts.Count;i++)retire(parts[i]);
            parts.Clear();byProxy.Clear();bySource.Clear();
            if(sceneReady){var s=scene;sceneReady=false;scene=new Scene();SceneManager.UnloadSceneAsync(s);}
            if(container!=null){Object.Destroy(container);container=null;}
            lastFrame=-1;dirty=false;disabled=false;logged=false;
        }
        internal static bool TrySource(Component c,out Component source)
        {
            Part part;
            if(c!=null&&byProxy.TryGetValue(c.gameObject,out part)){source=part.Source;return true;}
            source=c;return false;
        }
        internal static bool TrySource(GameObject go,out GameObject source)
        {
            Part part;
            if(go!=null&&byProxy.TryGetValue(go,out part)){source=part.Source==null?null:part.Source.gameObject;return true;}
            source=go;return false;
        }
        public static Component Source(Component c)
        {
            if(c==null)return null;
            var go=c.gameObject;
            Part p;
            if(go!=null&&byProxy.TryGetValue(go,out p))return p.Source;
            return c;
        }
        public static GameObject Source(GameObject go)
        {
            if(go==null)return null;
            Part p;
            if(byProxy.TryGetValue(go,out p))return p.Source==null?null:p.Source.gameObject;
            return go;
        }
        static bool dirty;
        static bool Prepare()
        {
            if(!Active)return false;
            if(Time.frameCount!=lastFrame||dirty){lastFrame=Time.frameCount;Follow();}
            if(parts.Count==0){Clear();return false;}
            if(dirty){dirty=false;Physics.SyncTransforms();}
            return true;
        }
        static void Follow()
        {
            for(int i=parts.Count-1;i>=0;i--)
            {
                var p=parts[i];
                var src=p.Source;
                if(p.Root==null||src==null||src.gameObject==null){retire(p);parts.RemoveAt(i);continue;}
                var t=src.transform;
                bool ch=p.Proxy.transform.position!=t.position||p.Proxy.transform.rotation!=t.rotation||p.Proxy.transform.localScale!=t.lossyScale||p.Proxy.layer!=src.gameObject.layer||p.Proxy.tag!=src.gameObject.tag;
                if(ch){p.Proxy.transform.position=t.position;p.Proxy.transform.rotation=t.rotation;p.Proxy.transform.localScale=t.lossyScale;p.Proxy.layer=src.gameObject.layer;p.Proxy.tag=src.gameObject.tag;dirty=true;}
                bool on=p.BaselineOn&&p.Root.gameObject.activeInHierarchy&&src.gameObject.activeInHierarchy;
                if(p.Shape!=null){if(p.Shape.isTrigger!=src.isTrigger){p.Shape.isTrigger=src.isTrigger;dirty=true;}if(p.Shape.enabled!=on){p.Shape.enabled=on;dirty=true;}}
            }
        }
        static int PrivateHits(Vector3 o,Vector3 d,RaycastHit[] buf,float dist,int mask)
        {
            if(!Prepare())return 0;
            return scene.GetPhysicsScene().Raycast(o,d,buf,dist,mask,QueryTriggerInteraction.UseGlobal);
        }
        internal static bool Raycast(Vector3 o,Vector3 d,out RaycastHit h,float distance,int mask)
        {
            bool wh=Physics.Raycast(o,d,out h,distance,mask,QueryTriggerInteraction.UseGlobal);
            int n=PrivateHits(o,d,hitBuffer,distance,mask);
            for(int i=0;i<n;i++)
                if(hitBuffer[i].distance<h.distance||!wh){h=hitBuffer[i];wh=true;}
            return wh;
        }
        internal static RaycastHit[] RaycastAll(Vector3 o,Vector3 d,float distance,int mask)
        {
            var world=Physics.RaycastAll(o,d,distance,mask,QueryTriggerInteraction.UseGlobal);
            int n=PrivateHits(o,d,hitBuffer,distance,mask);
            if(n==0)return world;
            var all=new RaycastHit[world.Length+n];
            System.Array.Copy(world,all,world.Length);
            System.Array.Copy(hitBuffer,0,all,world.Length,n);
            return all;
        }
        internal static int RaycastNonAlloc(Vector3 o,Vector3 d,RaycastHit[] results,float distance,int mask)
        {
            int w=Physics.RaycastNonAlloc(o,d,results,distance,mask,QueryTriggerInteraction.UseGlobal);
            int n=PrivateHits(o,d,hitBuffer,distance,mask);
            if(results==null)return w;
            int cap=results.Length;
            for(int i=0;i<n;i++)
            {
                var h=hitBuffer[i];
                if(w<cap){int j=w++;while(j>0&&results[j-1].distance>h.distance){results[j]=results[j-1];j--;}results[j]=h;}
                else
                {
                    int far=-1;float fd=0;
                    for(int j=0;j<cap;j++)if(results[j].distance>fd){fd=results[j].distance;far=j;}
                    if(far>=0&&h.distance<fd){int j=far;while(j>0&&results[j-1].distance>h.distance){results[j]=results[j-1];j--;}results[j]=h;}
                }
            }
            return w;
        }
        internal static Collider[] OverlapBox(Vector3 center,Vector3 half,Quaternion rot,int mask,QueryTriggerInteraction q)
        {
            var world=Physics.OverlapBox(center,half,rot,mask,q);
            if(!Prepare())return world;
            int n=scene.GetPhysicsScene().OverlapBox(center,half,boxBuffer,rot,mask,q);
            if(n==0)return world;
            var all=new Collider[world.Length+n];
            System.Array.Copy(world,all,world.Length);
            System.Array.Copy(boxBuffer,0,all,world.Length,n);
            return all;
        }
    }
    public partial class StableGameBridge
    {
        public static bool RabbitRaycast(Vector3 o,Vector3 d,out RaycastHit h,float dist,int mask)
        {return RabbitQueries.Raycast(o,d,out h,dist,mask);}
        public static RaycastHit[] RabbitRaycastAll(Vector3 o,Vector3 d,float dist,int mask)
        {return RabbitQueries.RaycastAll(o,d,dist,mask);}
        public static int RabbitWeaponRaycast(Vector3 o,Vector3 d,RaycastHit[] hits,float dist,int mask,weaponscript owner,int shotIndex)
        {
            int n=RabbitQueries.RaycastNonAlloc(o,d,hits,dist,mask);
            if(RabbitQueries.Active&&RabbitQueries.WeaponIntent!=null)RabbitQueries.WeaponIntent(owner,shotIndex,o,d);
            return n;
        }
        public static Collider[] RabbitMeleeOverlap(Vector3 center,Vector3 half,Quaternion rot,int mask,QueryTriggerInteraction q,meleeweaponscript owner)
        {
            var r=RabbitQueries.OverlapBox(center,half,rot,mask,q);
            if(RabbitQueries.Active&&RabbitQueries.MeleeIntent!=null)RabbitQueries.MeleeIntent(owner,center,half,rot);
            return r;
        }
        public static T RabbitQueryGet<T>(Component c)
        {
            Component s;
            if(RabbitQueries.TrySource(c,out s))return s==null?default(T):s.GetComponent<T>();
            return c.GetComponent<T>();
        }
        public static T RabbitQueryParent<T>(Component c)
        {
            Component s;
            if(RabbitQueries.TrySource(c,out s))return s==null?default(T):s.GetComponentInParent<T>();
            return c.GetComponentInParent<T>();
        }
        public static T RabbitQueryChild<T>(Component c)
        {
            Component s;
            if(RabbitQueries.TrySource(c,out s))return s==null?default(T):s.GetComponentInChildren<T>();
            return c.GetComponentInChildren<T>();
        }
        public static T[] RabbitQueryChildren<T>(Component c)
        {
            Component s;
            if(RabbitQueries.TrySource(c,out s))return s==null?new T[0]:s.GetComponentsInChildren<T>();
            return c.GetComponentsInChildren<T>();
        }
        public static T RabbitQueryGameGet<T>(GameObject go)
        {
            GameObject s;
            if(RabbitQueries.TrySource(go,out s))return s==null?default(T):s.GetComponent<T>();
            return go.GetComponent<T>();
        }
        public static bool RabbitQueryIsChildOf(Transform child,Transform parent)
        {
            Component s;
            if(RabbitQueries.TrySource(child,out s))return s!=null&&s.transform.IsChildOf(parent);
            return child.IsChildOf(parent);
        }
        public static void RabbitQueryForce(Rigidbody b,Vector3 f)
        {
            if(b.isKinematic)return;
            b.AddForce(f);
        }
        public static void RabbitQueryImpulse(Rigidbody b,Vector3 f,ForceMode m)
        {
            if(b.isKinematic)return;
            b.AddForce(f,m);
        }
        public static void RabbitQueryForceAt(Rigidbody b,Vector3 f,Vector3 p)
        {
            if(b.isKinematic)return;
            b.AddForceAtPosition(f,p);
        }
        public static void RabbitQueryImpulseAt(Rigidbody b,Vector3 f,Vector3 p,ForceMode m)
        {
            if(b.isKinematic)return;
            b.AddForceAtPosition(f,p,m);
        }
    }
}
