using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace TldMp
{
    internal static class RabbitPopulation
    {
        internal const bool OnlineReady=true;
        const int HardCap=32;
        const double TickInterval=0.5;
        sealed class Center
        {
            public ulong Id,Session;
            public Transform T;
            public double Next;
        }
        static readonly List<Center> centers=new List<Center>();
        static readonly Dictionary<ulong,Center> reuse=new Dictionary<ulong,Center>();
        static double nextTick;
        static int rr,attemptDepth,reservedGlobal,reservedLocal,globalLimit,localLimit;
        static aispawn activeSpawner;
        static aispawn lastSpawner;
        static fpscontroller lastPlayer;
        static StableNode lastNode;
        static bool lastWorld;

        internal static void Reset()
        {
            centers.Clear();reuse.Clear();
            rr=0;attemptDepth=0;activeSpawner=null;
            reservedGlobal=reservedLocal=globalLimit=localLimit=0;
            nextTick=0;
            lastSpawner=null;lastPlayer=null;lastNode=null;lastWorld=false;
        }
        static bool Finite(Vector3 v)
        {return !(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z));}
        static bool Finite(double v){return !double.IsNaN(v)&&!double.IsInfinity(v);}
        static bool ValidConfig(aispawn spawner)
        {
            if(spawner.maxAINum<=0)return false;
            if(!(spawner.minTime>=0f)||!(spawner.maxTime>=spawner.minTime)||!(spawner.maxTime>0f))return false;
            if(!(spawner.minDist>=0f)||!(spawner.maxDist>=spawner.minDist)||!(spawner.maxDist>0f))return false;
            if(!(spawner.radius>0f)||!(spawner.halfFov>=0f)||!(spawner.halfFov<=180f))return false;
            if(!(spawner.chance>=0f)||!(spawner.chance<=1f)||!Finite(spawner.chance))return false;
            if(!(spawner.minCount>=0f)||!(spawner.maxCount>=spawner.minCount)||spawner.maxCount>HardCap||!Finite(spawner.minCount)||!Finite(spawner.maxCount))return false;
            if(float.IsNaN(spawner.minTime)||float.IsInfinity(spawner.minTime)||float.IsNaN(spawner.maxTime)||float.IsInfinity(spawner.maxTime))return false;
            if(float.IsNaN(spawner.minDist)||float.IsInfinity(spawner.minDist)||float.IsNaN(spawner.maxDist)||float.IsInfinity(spawner.maxDist))return false;
            if(float.IsNaN(spawner.radius)||float.IsInfinity(spawner.radius)||float.IsNaN(spawner.halfFov)||float.IsInfinity(spawner.halfFov))return false;
            if(!Finite(spawner.offset)||!Finite(spawner.maxDist*2f))return false;
            return true;
        }
        static int CountRabbits(int prefabId,Vector3 near,float radius,out int total)
        {
            total=0;int within=0;
            var save=savedatascript.s;
            if(save==null||save.items==null)return 0;
            float r2=radius*radius;
            foreach(var pair in save.items)
            {
                var item=pair.Value;
                if(item==null||item.id!=prefabId||item.GetComponent<movecreaturescript>()==null)continue;
                total++;
                var t=item.useThisForPos!=null?item.useThisForPos:item.transform;
                var p=t.position;
                float dx=p.x-near.x,dz=p.z-near.z;
                if(dx*dx+dz*dz<=r2)within++;
            }
            return within;
        }
        static void Collect(StableNode node,aispawn spawner)
        {
            ulong own=node.Own,session=node.Session;
            reuse.Clear();
            for(int i=0;i<centers.Count;i++)reuse[centers[i].Id]=centers[i];
            centers.Clear();
            var lt=spawner.bodyRot!=null?spawner.bodyRot:(lastPlayer==null?null:lastPlayer.transform);
            if(lt!=null&&Finite(lt.position)&&Finite(lt.forward))centers.Add(Existing(own,session,lt));
            var sync=syncScript.s;
            if(sync!=null&&sync.players!=null&&Finite(sync.playerRemoveTime)&&sync.playerRemoveTime>=0f)
            {
                foreach(var pair in node.Peers)
                {
                    if(pair.Key==own)continue;
                    if(!pair.Value.Ready)continue;
                    syncScript.playerSyncClass pc;
                    if(!sync.players.TryGetValue(new CSteamID(pair.Key),out pc)||pc==null)continue;
                    if(pc.player==null||!pc.player.activeInHierarchy)continue;
                    float age=Time.realtimeSinceStartup-pc.lastTime;
                    if(!Finite(age)||age<0f||age>sync.playerRemoveTime)continue;
                    var t=pc.player.transform;
                    if(t==null||!Finite(t.position)||!Finite(t.forward))continue;
                    centers.Add(Existing(pair.Key,pair.Value.Session,t));
                }
            }
            centers.Sort(delegate(Center a,Center b){return a.Id.CompareTo(b.Id);});
            reuse.Clear();
            if(rr>=centers.Count)rr=0;
        }
        static Center Existing(ulong id,ulong session,Transform t)
        {
            Center c;
            if(reuse.TryGetValue(id,out c)&&c.Session==session){c.T=t;return c;}
            return new Center{Id=id,Session=session,T=t,Next=0};
        }
        internal static bool Admit(aispawn spawner,Vector3 position)
        {
            if(attemptDepth<=0||spawner!=activeSpawner||!Finite(position))return false;
            if(reservedGlobal>=globalLimit||reservedLocal>=localLimit)return false;
            reservedGlobal++;reservedLocal++;
            return true;
        }
        internal static void Tick(aispawn spawner,fpscontroller local,bool worldStarted,bool connected,StableNode node,double now)
        {
            if(!Finite(now)){Reset();return;}
            bool prereqs=connected&&worldStarted&&node!=null&&node.IsHost&&node.Compatible&&node.Error==null&&local!=null&&spawner!=null&&PatchOptions.Rabbits;
            var prefab=prereqs&&spawner.AIPrefab!=null?spawner.AIPrefab.GetComponent<tosaveitemscript>():null;
            if(prereqs)prereqs=prefab!=null&&prefab.GetComponent<nyulscript>()!=null&&prefab.GetComponent<movecreaturescript>()!=null&&ValidConfig(spawner);
            if(!prereqs){Reset();return;}
            bool changed=spawner!=lastSpawner||local!=lastPlayer||node!=lastNode||worldStarted!=lastWorld;
            if(changed){Reset();lastSpawner=spawner;lastPlayer=local;lastNode=node;lastWorld=worldStarted;}
            if(local!=null&&!local.loadedmove)return;
            if(now<nextTick)return;
            nextTick=now+TickInterval;
            Collect(node,spawner);
            if(centers.Count==0)return;
            long limit=(long)spawner.maxAINum*centers.Count;
            if(limit>HardCap)limit=HardCap;
            int total;
            float radius=spawner.maxDist*2f;
            Center pick=null;int pickIndex=-1,start=rr;
            for(int i=0;i<centers.Count;i++)
            {
                int idx=(start+i)%centers.Count;
                if(now>=centers[idx].Next){pick=centers[idx];pickIndex=idx;break;}
            }
            if(pick==null)return;
            rr=(pickIndex+1)%centers.Count;
            int near=CountRabbits(prefab.id,pick.T.position,radius,out total);
            if(total>=limit||near>=spawner.maxAINum){pick.Next=now+Random.Range(spawner.minTime,spawner.maxTime);return;}
            globalLimit=(int)limit;localLimit=spawner.maxAINum;
            reservedGlobal=total;reservedLocal=near;
            var original=spawner.bodyRot;
            try
            {
                activeSpawner=spawner;attemptDepth++;
                spawner.bodyRot=pick.T;
                spawner.TrySpawn();
            }
            finally
            {
                if(spawner!=null)spawner.bodyRot=original;
                attemptDepth=0;activeSpawner=null;
                if(lastNode==node&&lastSpawner==spawner&&lastPlayer==local)
                    pick.Next=now+Random.Range(spawner.minTime,spawner.maxTime);
            }
        }
    }
    public partial class StableGameBridge
    {
        public static bool RabbitSpawnGate(aispawn spawner,Vector3 position)
        {
            if(!Connected)return true;
            if(spawner==null||spawner.AIPrefab==null||spawner.AIPrefab.GetComponent<nyulscript>()==null)return true;
            var n=node;
            if(n==null||!n.IsHost)return false;
            return RabbitPopulation.Admit(spawner,position);
        }
        internal static void TickRabbitPopulation(aispawn spawner,fpscontroller player,bool worldStarted)
        {
            RabbitPopulation.Tick(spawner,player,worldStarted,Connected,node,(double)Time.time);
        }
    }
}
