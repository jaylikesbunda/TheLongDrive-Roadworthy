using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Used only at the two RoadsNear call sites inside categoryClass.UpdGen.
    // It augments the native lookup with already-generated road geometry; it
    // cannot discover roads whose owning chunks have not yet been generated.
    public static class LargeRockClearance
    {
        public static long IncompleteCoverageCount { get; private set; }
        public static long ExpandedLookupCount { get; private set; }

        public static List<roadGenScript.roadClass> RoadsNear(Vector3d position, objGenScript.categoryClass category)
        {
            var result = mapSettingScript.RoadsNear(position);
            if (WorldPlacementPolicy.EffectiveVersion != 1 || category == null || category.name != "BigRocks") return result;
            ExpandedLookupCount++;
            var map = menuhandler.s.currentMainMap;
            if (map == null || map.roadGens == null) { IncompleteCoverageCount++; return result; }
            var seen = new HashSet<roadGenScript.roadClass>(result);
            foreach (var generator in map.roadGens)
            {
                if (generator == null) { IncompleteCoverageCount++; continue; }
                if (!CoveragePresent(generator, position)) IncompleteCoverageCount++;
                foreach (var entry in generator.chunks)
                {
                    var chunk = entry.Value;
                    if (chunk == null || chunk.roads == null) continue;
                    foreach (var road in chunk.roads)
                    {
                        if (road == null || !road.generated || seen.Contains(road)) continue;
                        if (RoadBoundsNear(position, road, Math.Max(0.0, category.minDistFromRoad)))
                        { seen.Add(road); result.Add(road); }
                    }
                }
            }
            return result;
        }

        // Profile 2 checks the selected footprint instead of imposing a 1 km
        // exclusion on every prefab, including the small BigRock 4 prefab.
        public const float MountainRadius = 30f;
        public const double RoadHalfWidth = 8.0;
        static readonly Dictionary<GameObject,double> radii = new Dictionary<GameObject,double>();
        static bool IsMountainCategory(objGenScript.categoryClass category)
        {return WorldPlacementPolicy.EffectiveVersion >= 2 && category != null && category.name == "BigRocks";}
        public static float NativeMinDistance(objGenScript.categoryClass category)
        {return IsMountainCategory(category) ? 0f : category.minDistFromRoad;}

        internal struct Point {internal double x,z;internal Point(double x,double z){this.x=x;this.z=z;}}
        sealed class Footprint {internal readonly List<Point[]> hulls=new List<Point[]>();}
        sealed class PointOrder:IComparer<Point>
        {internal static readonly PointOrder Instance=new PointOrder();public int Compare(Point a,Point b){int c=a.x.CompareTo(b.x);return c!=0?c:a.z.CompareTo(b.z);}}
        static double Cross(Point a,Point b,Point c){return (b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);}
        internal static Point[] Hull(Point[] points)
        {
            Array.Sort(points,PointOrder.Instance);
            var hull=new Point[points.Length*2];int count=0;
            for(int i=0;i<points.Length;i++){while(count>=2 && Cross(hull[count-2],hull[count-1],points[i])<=0)count--;hull[count++]=points[i];}
            int lower=count;
            for(int i=points.Length-2;i>=0;i--){while(count>lower && Cross(hull[count-2],hull[count-1],points[i])<=0)count--;hull[count++]=points[i];}
            if(count>1)count--;
            var result=new Point[count];Array.Copy(hull,result,count);return result;
        }
        static double PointDistance(Point p,Point a,Point b)
        {
            double x=b.x-a.x,z=b.z-a.z,length=x*x+z*z;
            double t=length==0?0:Math.Max(0,Math.Min(1,((p.x-a.x)*x+(p.z-a.z)*z)/length));
            x=p.x-a.x-t*x;z=p.z-a.z-t*z;return x*x+z*z;
        }
        static bool Inside(Point p,Point[] hull)
        {
            if(hull.Length<3)return false;
            for(int i=0;i<hull.Length;i++)if(Cross(hull[i],hull[(i+1)%hull.Length],p)<0)return false;
            return true;
        }
        internal static bool SegmentNearHull(Point a,Point b,Point[] hull,double margin)
        {
            if(hull.Length==0)return false;
            if(Inside(a,hull) || Inside(b,hull))return true;
            double limit=margin*margin;
            for(int i=0;i<hull.Length;i++)
            {
                Point c=hull[i],d=hull[(i+1)%hull.Length];
                double abC=Cross(a,b,c),abD=Cross(a,b,d),cdA=Cross(c,d,a),cdB=Cross(c,d,b);
                // Strict crossings; collinear/disjoint and degenerate edges use
                // finite segment distances below, never infinite line distance.
                if(((abC>0 && abD<0)||(abC<0 && abD>0)) && ((cdA>0 && cdB<0)||(cdA<0 && cdB>0)))return true;
                if(PointDistance(a,c,d)<=limit || PointDistance(b,c,d)<=limit ||
                    PointDistance(c,a,b)<=limit || PointDistance(d,a,b)<=limit)return true;
            }
            return false;
        }
        static double PrefabRadius(GameObject prefab,objGenScript.objClass instance,Footprint footprint)
        {
            if(prefab==null)return double.NaN;
            Vector3 scale=prefab.transform.localScale;
            bool legacy=WorldPlacementPolicy.EffectiveVersion<3;
            randomRock random=prefab.GetComponent<randomRock>();
            if(random!=null && !random.noScale)
            {
                // Replay this object's private seeded stream, including the material draw.
                // This does not touch Unity's global random state.
                var rng=new System.Random(instance.seed);
                if(random.r!=null)rng.Next(0,random.materials.Length);
                scale=Vector3.one*(legacy?Mathf.Max(Math.Abs(random.sizeMin),Math.Abs(random.sizeMax)):Mathf.Lerp(random.sizeMin,random.sizeMax,(float)rng.NextDouble()));
            }
            scale*=instance.scale;
            Quaternion rotation=Quaternion.Euler(instance.rot);
            double radius=0;bool found=false;
            foreach(MeshCollider collider in prefab.GetComponentsInChildren<MeshCollider>(true))
            {
                if(collider==null || collider.sharedMesh==null || collider.isTrigger)continue;
                var bounds=collider.sharedMesh.bounds;
                var relative=prefab.transform.worldToLocalMatrix*collider.transform.localToWorldMatrix;
                var projected=footprint==null?null:new Point[8];int corner=0;
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                {
                    Vector3 point=rotation*Vector3.Scale(relative.MultiplyPoint3x4(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z))),scale);
                    radius=Math.Max(radius,Math.Sqrt(point.x*point.x+(legacy?point.y*point.y:0)+point.z*point.z));
                    if(projected!=null)projected[corner++]=new Point(point.x,point.z);
                }
                if(projected!=null)footprint.hulls.Add(Hull(projected));
                found=true;
            }
            return found?radius:double.NaN;
        }
        static bool LedgerIdentity(objGenScript.categoryClass category,out int gen,out int index)
        {
            gen=index=-1;
            var map=menuhandler.s==null?null:menuhandler.s.currentMainMap;
            if(map==null || map.objGens==null || category==null || category.script==null || category.script.categories==null)return false;
            gen=map.objGens.IndexOf(category.script);index=category.script.categories.IndexOf(category);
            if(index<0)
                for(int i=0;i<category.script.categories.Count;i++)
                {var c=category.script.categories[i];if(c!=null && c.name==category.name && c.rndCategoryOffset==category.rndCategoryOffset){if(index>=0)return false;index=i;}}
            return gen>=0 && index>=0;
        }
        static bool OwnerContains(Dictionary<Vector3d,objGenScript.objClass> objects,objGenScript.categoryClass category,Vector3d pos)
        {
            if(category.generatedChunks==null || !(category.currentChunkSize>0))return false;
            Vector3d cell=mainscript.GetChunkPos(pos,category.currentChunkSize);
            foreach(var pair in category.generatedChunks)
                if(pair.Value!=null && object.ReferenceEquals(pair.Value.objs,objects))return pair.Key.x==cell.x && pair.Key.z==cell.z;
            return false;
        }
        public static void AddGenerated(Dictionary<Vector3d,objGenScript.objClass> objects,Vector3d key,objGenScript.objClass obj,objGenScript.categoryClass category)
        {
            if(!IsMountainCategory(category)){objects.Add(key,obj);return;}
            bool persistent=WorldPlacementPolicy.EffectiveVersion>=4;
            int gen=-1,index=-1;
            if(persistent)
            {
                // If identity cannot be represented safely, retain native placement.
                if(obj==null || !LedgerIdentity(category,out gen,out index)){LedgerIdentityFallbackCount++;objects.Add(key,obj);return;}
                WorldRelocationLedger.Record saved;
                if(WorldRelocationLedger.TryGet(2,gen,index,key,out saved))
                {
                    if(saved.seed!=obj.seed || saved.type!=obj.prefabIndex || saved.rotation.x!=obj.rot.x || saved.rotation.y!=obj.rot.y || saved.rotation.z!=obj.rot.z || !OwnerContains(objects,category,saved.position))
                        throw new System.IO.InvalidDataException("Mountain relocation identity or streaming cell does not match.");
                    obj.gpos=saved.position;objects.Add(key,obj);return;
                }
            }
            double radius=1000.0;
            // Project each solid collider's transformed bounds separately. This
            // remains conservative for the entire mesh, but does not treat the
            // empty space beside a long thin mountain as solid geometry.
            Footprint footprint=WorldPlacementPolicy.EffectiveVersion>=5?new Footprint():null;
            if(obj!=null && category.prefabs!=null && obj.prefabIndex>=0 && obj.prefabIndex<category.prefabs.Count && category.prefabs[obj.prefabIndex]!=null)
                radius=PrefabRadius(category.prefabs[obj.prefabIndex].prefab,obj,footprint);
            if(footprint!=null && footprint.hulls.Count==0)footprint=null;
            if(double.IsNaN(radius) || radius < MountainRadius || !OverlapsKnownRoad(obj.gpos,radius+RoadHalfWidth,footprint)){objects.Add(key,obj);return;}
            if(persistent)
            {
                Vector3d original=obj.gpos;
                if(TryRelocate(objects,obj,category,radius,footprint))
                {
                    var record=new WorldRelocationLedger.Record{position=obj.gpos,rotation=obj.rot,type=obj.prefabIndex,seed=obj.seed,aligned=false};
                    if(WorldRelocationLedger.TryRecord(2,gen,index,key,record))
                    {
                        try{objects.Add(key,obj);}catch{WorldRelocationLedger.Remove(2,gen,index,key);obj.gpos=original;throw;}
                        RelocatedCount++;return;
                    }
                    obj.gpos=original;LedgerCapacityFallbackCount++;objects.Add(key,obj);return;
                }
                RelocationFailedCount++;
            }
        }
        public static long LedgerIdentityFallbackCount {get;private set;}
        public static long LedgerCapacityFallbackCount {get;private set;}
        public static long RelocatedCount {get;private set;}
        public static long RelocationFailedCount {get;private set;}
        public const int RelocationAttempts=16;
        // Two concentric rings, eight deterministic directions, no global RNG.
        public static void RelocationOffset(int seed,int attempt,double radius,out double x,out double z)
        {
            int direction=((int)((uint)seed%8u)+attempt%8)%8;
            double angle=direction*(Math.PI/4.0);
            double distance=(radius+RoadHalfWidth+1.0)*(attempt<8?1.0:2.0);
            x=Math.Cos(angle)*distance;z=Math.Sin(angle)*distance;
        }
        static bool TryRelocate(Dictionary<Vector3d,objGenScript.objClass> objects,objGenScript.objClass obj,objGenScript.categoryClass category,double radius,Footprint footprint)
        {
            if(category.generatedChunks==null || category.script==null || category.script.terrain==null || !(category.currentChunkSize>0))return false;
            Vector3d owner=new Vector3d();bool found=false;
            foreach(var pair in category.generatedChunks)
                if(pair.Value!=null && object.ReferenceEquals(pair.Value.objs,objects)){owner=pair.Key;found=true;break;}
            if(!found)return false;
            Vector3d original=obj.gpos;
            int attempts=footprint==null?RelocationAttempts:RelocationAttempts*2;
            for(int i=0;i<attempts;i++)
            {
                double x,z;RelocationOffset(obj.seed,i,radius,out x,out z);
                if(footprint!=null)
                {
                    // Try short moves before large moves; all remain in the
                    // original streaming cell and retain the original save key.
                    RelocationOffset(obj.seed,i%8,radius,out x,out z);
                    double factor=i<8?0.25:i<16?0.5:i<24?1.0:2.0;
                    x*=factor;z*=factor;
                }
                Vector3d candidate=original;candidate.x+=x;candidate.z+=z;
                Vector3d cell=mainscript.GetChunkPos(candidate,category.currentChunkSize);
                if(cell.x!=owner.x || cell.z!=owner.z)continue;
                if(OverlapsKnownRoad(candidate,radius+RoadHalfWidth,footprint))continue;
                var pois=mapSettingScript.POIsNear(candidate,(float)(Math.Max(radius,category.minDistFromPOI)+category.script.tileSize));
                if(!ClearOfPois(candidate,radius,category.minDistFromPOI,category.useRemoveInsidePOI,pois))continue;
                if(category.useTerrainHeight)
                    candidate.y=category.script.terrain.Height(candidate,mapSettingScript.RoadsNear(candidate),pois)+category.offsetty;
                obj.gpos=candidate;return true;
            }
            return false;
        }
        static bool ClearOfPois(Vector3d pos,double radius,float nativeDistance,bool checkQuads,List<poiGenScript.poiClass> pois)
        {
            if(pois==null)return true;
            foreach(var poi in pois)
            {
                if(poi==null)continue;
                double dx=pos.x-poi.pos.x,dz=pos.z-poi.pos.z;
                double clearance=Math.Max(radius,nativeDistance);
                if(dx*dx+dz*dz<clearance*clearance)return false;
                if(checkQuads && (TouchesQuads(pos,radius,poi.terrainHoleQuads) || TouchesQuads(pos,radius,poi.noObjQuads)))return false;
            }
            return true;
        }
        static bool TouchesQuads(Vector3d pos,double radius,List<poiGenScript.quadClass> quads)
        {
            if(quads==null)return false;
            foreach(var q in quads)
            {
                if(q==null || q.corners==null || q.corners.Length<4)continue;
                if(mainscript.IsPointInRectangle(pos,q.corners))return true;
                for(int i=0;i<q.corners.Length;i++)
                    if(SegmentDistanceSquared(pos,q.corners[i],q.corners[(i+1)%q.corners.Length])<=radius*radius)return true;
            }
            return false;
        }
        static bool PreciseRoadOverlap(Vector3d position,Vector3d a,Vector3d b,Footprint footprint)
        {
            if(footprint==null)return true;
            var start=new Point(a.x-position.x,a.z-position.z);var end=new Point(b.x-position.x,b.z-position.z);
            foreach(var hull in footprint.hulls)if(SegmentNearHull(start,end,hull,RoadHalfWidth))return true;
            return false;
        }
        static bool OverlapsKnownRoad(Vector3d position,double radius,Footprint footprint)
        {
            var map=menuhandler.s.currentMainMap;
            if(map==null || map.roadGens==null){IncompleteCoverageCount++;return false;}
            foreach(var gen in map.roadGens)
            {
                if(gen==null){IncompleteCoverageCount++;continue;}
                if(!CoveragePresent(gen,position))IncompleteCoverageCount++;
                foreach(var chunk in gen.chunks.Values)
                {
                    if(chunk==null || chunk.roads==null)continue;
                    foreach(var road in chunk.roads)
                    {
                        if(road==null || !road.generated || !RoadBoundsNear(position,road,radius))continue;
                        foreach(var element in road.roadElements)
                        {
                            if(element==null || element.bonePos==null)continue;
                            var points=element.bonePos;
                            if(points.Count==1 && SegmentDistanceSquared(position,points[0],points[0])<=radius*radius && PreciseRoadOverlap(position,points[0],points[0],footprint))return true;
                            for(int i=1;i<points.Count;i++)
                                if(SegmentDistanceSquared(position,points[i-1],points[i])<=radius*radius && PreciseRoadOverlap(position,points[i-1],points[i],footprint))return true;
                        }
                    }
                }
            }
            return false;
        }
        public static double SegmentDistanceSquared(Vector3d p,Vector3d a,Vector3d b)
        {
            double x=b.x-a.x,z=b.z-a.z,len=x*x+z*z;
            double t=len==0?0:((p.x-a.x)*x+(p.z-a.z)*z)/len;
            t=Math.Max(0,Math.Min(1,t));x=p.x-(a.x+t*x);z=p.z-(a.z+t*z);return x*x+z*z;
        }

        // This is a warning signal, not a proof of complete coverage: chunk
        // ownership and eviction can extend beyond the examined neighbourhood.
        static bool CoveragePresent(roadGenScript gen, Vector3d position)
        {
            if (!gen.hasMainRoad && !gen.hasSideRoad) return true;
            if (!(gen.chunkSize > 0)) return false;
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
            {
                Vector3d key = mainscript.GetChunkPos(position, x, z, gen.chunkSize);
                if (!gen.hasSideRoad && gen.hasMainRoad && key.x != 0) continue;
                roadGenScript.dotChunkClass chunk;
                if (!gen.chunks.TryGetValue(key, out chunk) || chunk == null ||
                    !chunk.insideConnected || !chunk.outsideUConnected || !chunk.outsideRConnected ||
                    !chunk.innerChunkGenerated || !chunk.Done()) return false;
            }
            return true;
        }

        static bool RoadBoundsNear(Vector3d pos, roadGenScript.roadClass road, double radius)
        {
            if (road.roadElements == null) return false;
            foreach (var element in road.roadElements)
            {
                if (element == null || element.bonePos == null || element.bonePos.Count == 0) continue;
                double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
                double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
                foreach (var point in element.bonePos)
                { minX = Math.Min(minX, point.x); maxX = Math.Max(maxX, point.x); minZ = Math.Min(minZ, point.z); maxZ = Math.Max(maxZ, point.z); }
                if (IntersectsBounds(pos.x, pos.z, radius, minX, minZ, maxX, maxZ)) return true;
            }
            return false;
        }

        public static bool IntersectsBounds(double x, double z, double radius, double minX, double minZ, double maxX, double maxZ)
        { return x >= minX - radius && x <= maxX + radius && z >= minZ - radius && z <= maxZ + radius; }
    }
}

