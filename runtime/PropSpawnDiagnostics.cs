using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TldMp
{
    // Read-only, bounded observations of newly registered native POI props.
    // Positions are POI-local so floating-origin shifts cannot look like falls.
    internal static class PropSpawnDiagnostics
    {
        const int MaxTracked=256,MaxPerPoi=64,MaxLines=2048;
        sealed class Row
        {
            internal tosaveitemscript Item;
            internal poiScript Poi;
            internal string Key,Support;
            internal Vector3 Start;
            internal float Started,Next,Floor=float.NaN;
            internal bool Kinematic,ReportedDrop;
            internal int Step;
        }
        static readonly List<Row> rows=new List<Row>();
        static readonly RaycastHit[] hits=new RaycastHit[32];
        static mainscript world;
        static int lines;
        static float next;
        static readonly bool Enabled=ModDiagnostics.On || Environment.GetEnvironmentVariable("TLD_MP_SPAWN_PROBE_POI")!=null;
        static string F(float n){return n.ToString("F3",CultureInfo.InvariantCulture);}
        static string V(Vector3 v){return F(v.x)+","+F(v.y)+","+F(v.z);}
        static void Log(string text)
        {
            if(lines>=MaxLines)return;
            lines++;
            string line="[TLD MP prop-spawn] frame="+Time.frameCount+" t="+F(Time.realtimeSinceStartup)+" "+text;
            Debug.Log(line);DiagnosticFileLog.Log(line);
            if(lines==MaxLines)Debug.Log("[TLD MP prop-spawn] world log limit reached; further prop records suppressed.");
        }
        static void ResetWorld()
        {
            if(ReferenceEquals(world,mainscript.s))return;
            world=mainscript.s;rows.Clear();lines=0;next=0f;
        }
        internal static uint Begin()
        {
            if(!Enabled)return 0;
            ResetWorld();
            var save=savedatascript.s;return save==null || save.data==null?0:save.data.lastID;
        }
        internal static void Completed(poiScript poi,poiGenScript.poiClass entry,uint previousId)
        {
            if(!Enabled || poi==null)return;
            try
            {
                ResetWorld();var save=savedatascript.s;
                if(save==null || save.items==null)return;
                var map=menuhandler.s==null?null:menuhandler.s.currentMainMap;
                string key="seed="+(map==null?"?":map.seed.ToString())+" poi="+poi.name+" gen="+poi.poigenid+" posID="+entry.posID;
                int registered=0,tracked=0;
                foreach(var pair in save.items)
                {
                    var item=pair.Value;
                    if(pair.Key<=previousId || item==null || item.loaded)continue;
                    registered++;
                    if(item.RB==null || tracked>=MaxPerPoi || rows.Count>=MaxTracked)continue;
                    var row=new Row{Item=item,Poi=poi,Key=key,Started=Time.realtimeSinceStartup,Next=Time.realtimeSinceStartup+3f,Kinematic=item.RB.isKinematic};
                    row.Start=poi.transform.InverseTransformPoint(item.RB.worldCenterOfMass);
                    row.Floor=Floor(row,out row.Support);
                    rows.Add(row);tracked++;
                    Sample(row,"registered",row.Start);
                }
                int active=0,total=0;
                foreach(var collider in poi.GetComponentsInChildren<Collider>(true))
                {if(collider.isTrigger || collider.attachedRigidbody!=null)continue;total++;if(collider.enabled && collider.gameObject.activeInHierarchy)active++;}
                Log(key+" phase=poi-complete registered="+registered+" tracked="+tracked+" staticColliders="+active+"/"+total+" itemsSpawned="+entry.spawnedItems+" fixedSpawned="+entry.spawnedFixed+" trackingCap="+(registered>tracked));
            }
            catch(Exception e){Log("phase=capture-error "+e.GetType().Name+" "+e.Message);}
        }
        static float Floor(Row row,out string surface)
        {
            surface="none";
            var at=row.Item.RB.worldCenterOfMass;
            int count=Physics.RaycastNonAlloc(at+Vector3.up*.1f,Vector3.down,hits,30f,~0,QueryTriggerInteraction.Ignore);
            var found=hits;
            if(count==hits.Length){found=Physics.RaycastAll(at+Vector3.up*.1f,Vector3.down,30f,~0,QueryTriggerInteraction.Ignore);count=found.Length;}
            float highest=float.NaN;
            for(int i=0;i<count;i++)
            {
                var hit=found[i];var col=hit.collider;
                if(col==null || col.attachedRigidbody!=null || !col.transform.IsChildOf(row.Poi.transform) || hit.normal.y<.5f || Physics.GetIgnoreLayerCollision(row.Item.gameObject.layer,col.gameObject.layer))continue;
                float y=row.Poi.transform.InverseTransformPoint(hit.point).y;
                if(float.IsNaN(highest) || y>highest){highest=y;surface=col.name+"#"+col.GetInstanceID();}
            }
            return highest;
        }
        static void Sample(Row row,string phase,Vector3 current)
        {
            string supportNow,surfaceAtCurrent;float floorNow=Floor(row,out supportNow);
            bool originalHere=HasOriginalSurface(row.Poi.transform,current,row.Floor,out surfaceAtCurrent);
            var item=row.Item;var rb=item.RB;int enabled=0,total=0;
            foreach(var col in item.GetComponentsInChildren<Collider>(true))
            {if(col.isTrigger)continue;total++;if(col.enabled && col.gameObject.activeInHierarchy)enabled++;}
            Log(row.Key+" phase="+phase+" name="+item.name+" id="+item.idInSave+" prefab="+item.id+" age="+F(Time.realtimeSinceStartup-row.Started)+" local="+V(current)+" start="+V(row.Start)+" deltaY="+F(current.y-row.Start.y)+" initialSupportY="+F(row.Floor)+" initialSupport="+row.Support+" supportNowY="+F(floorNow)+" supportNow="+supportNow+" originalSurfaceAtCurrent="+originalHere+" originalSurface="+surfaceAtCurrent+" kinematic="+rb.isKinematic+" gravity="+rb.useGravity+" collisions="+rb.detectCollisions+" mode="+rb.collisionDetectionMode+" velocity="+V(rb.velocity)+" colliders="+enabled+"/"+total+" buried="+item.buried+" parent="+(item.transform.parent==null?"none":item.transform.parent.name));
        }
        internal static bool HasOriginalSurface(Transform poi,Vector3 current,float floor,out string surface)
        {
            surface="none";if(poi==null || float.IsNaN(floor))return false;
            var origin=poi.TransformPoint(new Vector3(current.x,floor+.5f,current.z));
            foreach(var hit in Physics.RaycastAll(origin,Vector3.down,1f,~0,QueryTriggerInteraction.Ignore))
            {
                var col=hit.collider;
                if(col==null || col.attachedRigidbody!=null || !col.transform.IsChildOf(poi) || hit.normal.y<.5f)continue;
                if(Mathf.Abs(poi.InverseTransformPoint(hit.point).y-floor)>.15f)continue;
                surface=col.name+"#"+col.GetInstanceID();return true;
            }
            return false;
        }
        internal static void Tick()
        {
            if(!Enabled)return;
            ResetWorld();float now=Time.realtimeSinceStartup;
            if(now<next || rows.Count==0)return;next=now+.25f;
            for(int i=rows.Count-1;i>=0;i--)
            {
                var row=rows[i];
                if(row.Item==null || row.Poi==null || row.Item.RB==null){rows.RemoveAt(i);continue;}
                try
                {
                    var current=row.Poi.transform.InverseTransformPoint(row.Item.RB.worldCenterOfMass);
                    bool kin=row.Item.RB.isKinematic;
                    if(kin!=row.Kinematic){row.Kinematic=kin;Sample(row,kin?"physics-frozen-observed":"physics-released-observed",current);}
                    float horizontal=new Vector2(current.x-row.Start.x,current.z-row.Start.z).magnitude;
                    if(!row.ReportedDrop && current.y<row.Start.y-1.5f)
                    {
                        row.ReportedDrop=true;
                        bool below=!float.IsNaN(row.Floor) && row.Start.y>=row.Floor && current.y<row.Floor-.3f && horizontal<2f;
                        string surface;bool originalHere=below && HasOriginalSurface(row.Poi.transform,current,row.Floor,out surface);
                        Sample(row,below?(originalHere?"suspected-floor-crossing":"support-edge-drop"):"large-drop",current);
                    }
                    if(now>=row.Next)
                    {
                        Sample(row,"follow-up",current);row.Step++;
                        if(row.Step>=3){rows.RemoveAt(i);continue;}
                        row.Next=row.Started+(row.Step==1?10f:30f);
                    }
                }
                catch(Exception e){Log(row.Key+" phase=follow-error "+e.GetType().Name);rows.RemoveAt(i);}
            }
        }
    }
}
