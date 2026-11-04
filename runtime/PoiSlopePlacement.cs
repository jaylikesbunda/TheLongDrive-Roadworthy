using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Called only from the random AddPois path, on the game thread. Saved,
    // fixed and starting POIs bypass this hook; pre-policy worlds use native placement.
    public static class PoiSlopePlacement
    {
        public const double MaxHeightRange = 8.0;
        public const int CandidateCount = 17;
        const int RingSamples = 8;
        sealed class Footprint { public GameObject prefab; public float multiplier; public double radius; public int version; public double minX,maxX,minZ,maxZ; public bool bounds; }
        static readonly Dictionary<int, Footprint> footprints = new Dictionary<int, Footprint>();

        public static double FootprintRadius(double offsetMagnitude, double scaleX, double multiplier)
        {
            return Math.Max(0.0, offsetMagnitude + scaleX * multiplier);
        }

        // Two rings, stable direction and order; no random numbers or mutable retry state.
        public static void CandidateOffset(int attempt, double radius, out double x, out double z)
        {
            if (attempt <= 0) { x = z = 0; return; }
            int direction = (attempt - 1) % 8;
            double distance = Math.Max(8.0, Math.Min(48.0, radius)) * (attempt <= 8 ? 0.5 : 1.0);
            double angle = direction * Math.PI / 4.0;
            x = Math.Cos(angle) * distance; z = Math.Sin(angle) * distance;
        }

        public static bool IsAcceptable(double min, double max, double baseHeight)
        {
            return !double.IsNaN(min) && !double.IsInfinity(min) &&
                !double.IsNaN(max) && !double.IsInfinity(max) &&
                !double.IsNaN(baseHeight) && !double.IsInfinity(baseHeight) &&
                max >= min && max - min <= MaxHeightRange &&
                baseHeight >= min - MaxHeightRange / 2 && baseHeight <= max + MaxHeightRange / 2;
        }

        static double Radius(int id, float multiplier)
        {
            GameObject prefab = itemdatabase.s.Building(id);
            Footprint cached;
            if (footprints.TryGetValue(id, out cached) && cached.prefab == prefab && cached.multiplier == multiplier && cached.version==WorldPlacementPolicy.Version)
                return cached.radius;
            double radius = 0;
            if(WorldPlacementPolicy.Version<3)
            {
                foreach(var align in prefab.GetComponentsInChildren<terrainHeightAlignToBuildingScript>(true))radius=Math.Max(radius,FootprintRadius(align.transform.localPosition.magnitude,align.transform.localScale.x,multiplier));
                footprints[id]=new Footprint{prefab=prefab,multiplier=multiplier,radius=radius,version=WorldPlacementPolicy.Version};return radius;
            }
            // Solid collision geometry describes the building; pad blend ranges do not.
            double minX=double.PositiveInfinity,maxX=double.NegativeInfinity,minZ=double.PositiveInfinity,maxZ=double.NegativeInfinity;
            foreach(Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if(collider==null || collider.isTrigger)continue;
                Bounds bounds;
                var box=collider as BoxCollider;var mesh=collider as MeshCollider;
                if(box!=null)bounds=new Bounds(box.center,box.size);
                else if(mesh!=null && mesh.sharedMesh!=null)bounds=mesh.sharedMesh.bounds;
                else continue;
                var relative=prefab.transform.worldToLocalMatrix*collider.transform.localToWorldMatrix;
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                {
                    var point=relative.MultiplyPoint3x4(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                    radius=Math.Max(radius,Math.Sqrt(point.x*point.x+point.z*point.z));
                    minX=Math.Min(minX,point.x);maxX=Math.Max(maxX,point.x);minZ=Math.Min(minZ,point.z);maxZ=Math.Max(maxZ,point.z);
                }
            }
            footprints[id] = new Footprint { prefab = prefab, multiplier = multiplier, radius = radius, version=WorldPlacementPolicy.Version, minX=minX,maxX=maxX,minZ=minZ,maxZ=maxZ,bounds=!double.IsInfinity(minX) };
            return radius;
        }

        static double RawHeight(poiGenScript gen, Vector3d pos)
        {
            return RoadTerrainClearance.Height(gen.terrainGen, pos, gen.roadAffects ? mapSettingScript.RoadsNear(pos) : null,
                (List<poiGenScript.poiClass>)null, WorldPlacementPolicy.EffectiveVersion > 0, WorldPlacementPolicy.RoadClearance);
        }

        static bool Suitable(poiGenScript gen, Vector3d pos, double radius)
        {
            double center = RawHeight(gen, pos), min = center, max = center;
            for (int ring = 1; ring <= 2; ring++)
                for (int i = 0; i < RingSamples; i++)
                {
                    double angle = i * Math.PI * 2 / RingSamples;
                    Vector3d p = pos;
                    p.x += Math.Cos(angle) * radius * ring / 2;
                    p.z += Math.Sin(angle) * radius * ring / 2;
                    double y = RawHeight(gen, p);
                    if (double.IsNaN(y) || double.IsInfinity(y)) return false;
                    min = Math.Min(min, y); max = Math.Max(max, y);
                }
            if(WorldPlacementPolicy.Version<3)return IsAcceptable(min,max,pos.y);
            double allowedRange=Math.Max(MaxHeightRange,2.0*radius*0.35);
            return !double.IsNaN(min) && !double.IsNaN(max) && !double.IsInfinity(min) && !double.IsInfinity(max) && max-min<=allowedRange && pos.y>=min-allowedRange*0.5 && pos.y<=max+allowedRange*0.5;
        }

        // AddPoi's native snapped key identifies the original generated point,
        // even when its world position is moved. Its scoped override also makes
        // native save lookup use that stable key. Restore nesting on all exits.
        [ThreadStatic] static poiGenScript identityOwner;
        [ThreadStatic] static Vector3d identityValue;
        public static Vector3d ResolveIdentity(Vector3d actual, float round, poiGenScript gen)
        {
            return identityOwner == gen ? identityValue : mainscript.Snap(actual, round);
        }
        public static poiGenScript.poiClass AddAtIdentity(poiGenScript gen, Vector3d id, Vector3d pos, Vector3 rot, int type, bool aligned)
        {
            poiGenScript previousOwner = identityOwner; Vector3d previousValue = identityValue;
            identityOwner = gen; identityValue = id;
            try { return gen.AddPoi(pos, rot, type, aligned); }
            finally { identityOwner = previousOwner; identityValue = previousValue; }
        }
        static poiGenScript.poiClass AddWithIdentity(poiGenScript gen, Vector3d pos, Vector3 rot,
            itemdatabase.b type, bool aligned, Vector3d originalId)
        {
            poiGenScript previousOwner = identityOwner;
            Vector3d previousValue = identityValue;
            identityOwner = gen; identityValue = originalId;
            try { return gen.AddPoi(pos, rot, type, aligned); }
            finally { identityOwner = previousOwner; identityValue = previousValue; }
        }

        static double TerrainRange(poiGenScript gen, Vector3d pos, double radius)
        {
            double center = RawHeight(gen, pos), min = center, max = center;
            if (double.IsNaN(center) || double.IsInfinity(center)) return double.PositiveInfinity;
            for (int ring = 1; ring <= 2; ring++)
                for (int i = 0; i < RingSamples; i++)
                {
                    double angle = i * Math.PI * 2 / RingSamples;
                    Vector3d p = pos;
                    p.x += Math.Cos(angle) * radius * ring / 2;
                    p.z += Math.Sin(angle) * radius * ring / 2;
                    double y = RawHeight(gen, p);
                    if (double.IsNaN(y) || double.IsInfinity(y)) return double.PositiveInfinity;
                    min = Math.Min(min, y); max = Math.Max(max, y);
                }
            if (pos.y < min - MaxHeightRange / 2 || pos.y > max + MaxHeightRange / 2)
                return double.PositiveInfinity;
            return max - min;
        }

        public static int ProfileFourFallbackCount { get; private set; }
        public static double LastFallbackRange { get; private set; }
        public static double LastFallbackAllowedRange { get; private set; }
        public static bool LastFallbackRoadAligned { get; private set; }

        static void RecordFallback(double range, double allowed, bool aligned)
        {
            ProfileFourFallbackCount++;
            LastFallbackRange = range; LastFallbackAllowedRange = allowed; LastFallbackRoadAligned = aligned;
            if (ProfileFourFallbackCount == 1 || ProfileFourFallbackCount % 64 == 0)
                UnityEngine.Debug.LogWarning("[Roadworthy] POI slope fallback count=" + ProfileFourFallbackCount +
                    " range=" + range.ToString("F2") + " allowed=" + allowed.ToString("F2") + " roadAligned=" + aligned);
        }

        static bool SameRoadCoverage(Vector3d a, Vector3d b, float chunkSize)
        {
            Vector3d ca = mainscript.GetChunkPos(a, chunkSize);
            Vector3d cb = mainscript.GetChunkPos(b, chunkSize);
            if (!ca.Equals(cb)) return false;
            int ax = a.x < ca.x ? -1 : 1, az = a.z < ca.z ? -1 : 1;
            int bx = b.x < cb.x ? -1 : 1, bz = b.z < cb.z ? -1 : 1;
            return ax == bx && az == bz;
        }

        struct SiteBounds { public double minX,maxX,minZ,maxZ; }
        static SiteBounds BoundsFor(int id,float multiplier,Vector3d pos,Vector3 rotation)
        {
            Radius(id,multiplier);Footprint f=footprints[id];
            if(!f.bounds) return new SiteBounds{minX=pos.x,maxX=pos.x,minZ=pos.z,maxZ=pos.z};
            double angle=rotation.y*Math.PI/180.0,cos=Math.Cos(angle),sin=Math.Sin(angle);
            SiteBounds b=new SiteBounds{minX=double.PositiveInfinity,maxX=double.NegativeInfinity,minZ=double.PositiveInfinity,maxZ=double.NegativeInfinity};
            for(int x=0;x<2;x++)for(int z=0;z<2;z++){
                double lx=x==0?f.minX:f.maxX,lz=z==0?f.minZ:f.maxZ;
                double xx=pos.x+lx*cos+lz*sin,zz=pos.z-lx*sin+lz*cos;
                b.minX=Math.Min(b.minX,xx);b.maxX=Math.Max(b.maxX,xx);b.minZ=Math.Min(b.minZ,zz);b.maxZ=Math.Max(b.maxZ,zz);
            }
            return b;
        }
        static bool Intersects(SiteBounds a,SiteBounds b)
        {return a.minX<b.maxX && a.maxX>b.minX && a.minZ<b.maxZ && a.maxZ>b.minZ;}
        static bool QuadOverlap(SiteBounds site,List<poiGenScript.quadClass> quads)
        {
            if(quads==null)return false;
            foreach(var quad in quads)
            {
                if(quad==null || quad.corners==null || quad.corners.Length==0)continue;
                SiteBounds b=new SiteBounds{minX=double.PositiveInfinity,maxX=double.NegativeInfinity,minZ=double.PositiveInfinity,maxZ=double.NegativeInfinity};
                foreach(Vector3d p in quad.corners){b.minX=Math.Min(b.minX,p.x);b.maxX=Math.Max(b.maxX,p.x);b.minZ=Math.Min(b.minZ,p.z);b.maxZ=Math.Max(b.maxZ,p.z);}
                if(Intersects(site,b))return true;
            }
            return false;
        }
        static bool SiteOccupied(poiGenScript gen,Vector3d candidate,Vector3 rot,int type,Vector3d ownId)
        {
            SiteBounds site=BoundsFor(type,gen.alignMaxRadiusMultiplier,candidate,rot);
            if(site.minX==site.maxX || site.minZ==site.maxZ)return false;
            var gens=gen.mapSetting!=null && gen.mapSetting.poiGens!=null ? gen.mapSetting.poiGens : new List<poiGenScript>{gen};
            int scanned=0;
            foreach(poiGenScript otherGen in gens)
            {
                if(otherGen==null || !(otherGen.chunkSize>0))continue;
                for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
                {
                    poiGenScript.chunkClass chunk;
                    if(!otherGen.chunks.TryGetValue(mainscript.GetChunkPos(candidate,x,z,otherGen.chunkSize),out chunk))continue;
                    foreach(var existing in chunk.pois.Values)
                    {
                        if(++scanned>2048)return true;
                        if(otherGen==gen && existing.posID.Equals(ownId))continue;
                        if(QuadOverlap(site,existing.noObjQuads) || QuadOverlap(site,existing.terrainHoleQuads))return true;
                        if(existing.id>=0 && Intersects(site,BoundsFor(existing.id,otherGen.alignMaxRadiusMultiplier,existing.pos,existing.rot)))return true;
                        if(existing.id<0 && existing.pobj!=null)
                            foreach(Collider c in existing.pobj.GetComponentsInChildren<Collider>(true))
                            {
                                if(c==null || c.isTrigger || !c.enabled)continue;Bounds b=c.bounds;
                                if(Intersects(site,new SiteBounds{minX=b.min.x,maxX=b.max.x,minZ=b.min.z,maxZ=b.max.z}))return true;
                            }
                    }
                    if(chunk.poiToPlace==null)continue;
                    foreach(var pending in chunk.poiToPlace)
                    {
                        if(++scanned>2048)return true;
                        Vector3d anchor=pending.pos;Vector3 pendingRot=Vector3.zero;bool main;
                        if(otherGen.roadAffects && otherGen.roadgen!=null && pending.roadType!=poiGenScript.alignToRoadEmu.none && otherGen.roadgen.RoadsDoneAtPlace(pending.pos))
                            otherGen.AlignPosToRoad(pending.pos,out anchor,out pendingRot,out main,false,pending.roadType,pending.roadDist,pending.minSlope,pending.maxSlope,pending.maxDist);
                        Vector3d anchorId=mainscript.Snap(anchor,gen.round);
                        if(otherGen==gen && anchorId.x==ownId.x && anchorId.z==ownId.z)continue;
                        if(pending.tipus<0)continue;
                        if(Intersects(site,BoundsFor(pending.tipus,otherGen.alignMaxRadiusMultiplier,anchor,pendingRot)))return true;
                    }
                }
            }
            return false;
        }

        static poiGenScript.poiClass AddProfileFour(poiGenScript gen, Vector3d pos, Vector3 rot,
            itemdatabase.b type, bool aligned)
        {
            Vector3d originalId = mainscript.Snap(pos, gen.round);
            WorldRelocationLedger.Record saved;
            if (WorldRelocationLedger.TryGet(1, gen.index, 0, originalId, out saved))
            {
                if (saved.type != (int)type) throw new System.IO.InvalidDataException("POI relocation type mismatch.");
                return AddWithIdentity(gen, saved.position, saved.rotation, type, saved.aligned, originalId);
            }
            // A saved original without a relocation entry was placed at its native
            // position (including the bounded-ledger fallback).
            if (gen.poisFromSave.ContainsKey(originalId)) return AddWithIdentity(gen, pos, rot, type, aligned, originalId);
            Vector3d ownerChunk = mainscript.GetChunkPos(pos, gen.chunkSize);
            double radius = Radius((int)type, gen.alignMaxRadiusMultiplier);
            if (!(radius > 0)) return AddWithIdentity(gen, pos, rot, type, aligned, originalId);
            double allowedRange = Math.Max(MaxHeightRange, 2.0 * radius * 0.35);
            double bestRange = TerrainRange(gen, pos, radius);
            bool originalOccupied=SiteOccupied(gen,pos,rot,(int)type,originalId);
            if (bestRange <= allowedRange && !originalOccupied) return AddWithIdentity(gen, pos, rot, type, aligned, originalId);
            Vector3d best = pos; Vector3 bestRot = rot;
            if(originalOccupied) bestRange=double.PositiveInfinity;
            roadGenScript.roadClass road = null; Vector3d nearest = pos;
            double tangentX = 0, tangentZ = 0, roadDistance = 0;
            if (aligned && gen.roadgen != null)
            {
                road = roadGenScript.NearestPointOnRoadBone(pos,
                    gen.roadgen.RoadsNear(pos, true, poiGenScript.alignToRoadEmu.anyRoad), out nearest);
                if (road != null)
                {
                    tangentX = road.end.x - road.start.x; tangentZ = road.end.z - road.start.z;
                    double length = Math.Sqrt(tangentX * tangentX + tangentZ * tangentZ);
                    if (length > 0) { tangentX /= length; tangentZ /= length; }
                    else road = null;
                    double x = pos.x - nearest.x, z = pos.z - nearest.z;
                    roadDistance = Math.Sqrt(x * x + z * z);
                }
            }
            // A road-aligned building must not be arbitrarily moved off its road.
            if (aligned && road == null)
            { RecordFallback(bestRange, allowedRange, true); return AddWithIdentity(gen, pos, rot, type, aligned, originalId); }
            for (int attempt = 1; attempt <= 32; attempt++)
            {
                Vector3d candidate = pos; Vector3 candidateRot = rot;
                if (aligned)
                {
                    double distance = ((attempt + 1) / 2) * 16.0 * ((attempt & 1) != 0 ? 1 : -1);
                    Vector3d probe = pos; probe.x += tangentX * distance; probe.z += tangentZ * distance;
                    if (!SameRoadCoverage(pos, probe, gen.roadgen.chunkSize)) continue;
                    bool main;
                    var roadType = road.roadType == roadGenScript.roadTypeEmu.mainRoad
                        ? poiGenScript.alignToRoadEmu.onlyMainRoad : poiGenScript.alignToRoadEmu.onlySideRoad;
                    if (!gen.roadgen.RoadsDoneAtPlace(probe)) continue;
                    Vector3d probeRoadPoint;
                    if (roadGenScript.NearestPointOnRoadBone(probe,
                        gen.roadgen.RoadsNear(probe, true, roadType), out probeRoadPoint) != road) continue;
                    if (!gen.AlignPosToRoad(probe, out candidate, out candidateRot, out main, false,
                            roadType, (float)roadDistance, 0, 0, (float)roadDistance)) continue;
                }
                else
                {
                    int direction = (attempt - 1) % 8;
                    double angle = direction * Math.PI / 4.0;
                    double distance = ((attempt - 1) / 8 + 1) * 16.0;
                    candidate.x += Math.Cos(angle) * distance; candidate.z += Math.Sin(angle) * distance;
                    if (gen.roadAffects && gen.roadgen != null &&
                        (!SameRoadCoverage(pos, candidate, gen.roadgen.chunkSize) || !gen.roadgen.RoadsDoneAtPlace(candidate))) continue;
                    candidate.y = RawHeight(gen, candidate);
                }
                // Keep the original owner and stable save key together.
                if (!mainscript.GetChunkPos(candidate, gen.chunkSize).Equals(ownerChunk)) continue;
                if (SiteOccupied(gen,candidate,candidateRot,(int)type,originalId)) continue;
                double range = TerrainRange(gen, candidate, radius);
                if (range < bestRange) { bestRange = range; best = candidate; bestRot = candidateRot; }
                if (range <= allowedRange) break;
            }
            // Never discard a POI: choose the least steep checked site, even on
            // terrain where no candidate meets the preferred range.
            if (bestRange > allowedRange) RecordFallback(bestRange, allowedRange, aligned);
            if (best.Equals(pos) && bestRot.Equals(rot)) return AddWithIdentity(gen, pos, rot, type, aligned, originalId);
            var record = new WorldRelocationLedger.Record { position = best, rotation = bestRot, aligned = aligned, type = (int)type };
            if (!WorldRelocationLedger.TryRecord(1, gen.index, 0, originalId, record))
            {
                UnityEngine.Debug.LogWarning("[Roadworthy] POI relocation ledger full; using native position.");
                return AddWithIdentity(gen, pos, rot, type, aligned, originalId);
            }
            try { return AddWithIdentity(gen, best, bestRot, type, aligned, originalId); }
            catch { WorldRelocationLedger.Remove(1, gen.index, 0, originalId); throw; }
        }

        public static poiGenScript.poiClass AddRandomPoi(poiGenScript gen, Vector3d pos, Vector3 rot, itemdatabase.b type, bool alignedToRoad)
        {
            if (WorldPlacementPolicy.Version >= 4) return AddProfileFour(gen, pos, rot, type, alignedToRoad);
            if (WorldPlacementPolicy.Version == 0 || gen.poisFromSave.ContainsKey(mainscript.Snap(pos, gen.round)))
                return gen.AddPoi(pos, rot, type, alignedToRoad);
            double radius = Radius((int)type, gen.alignMaxRadiusMultiplier);
            if (radius <= 0) return gen.AddPoi(pos, rot, type, alignedToRoad);
            if(WorldPlacementPolicy.Version<3)
            {
                for(int attempt=0;attempt<CandidateCount;attempt++)
                {
                    double dx,dz;CandidateOffset(attempt,radius,out dx,out dz);Vector3d candidate=pos;candidate.x+=dx;candidate.z+=dz;
                    if(attempt!=0){if(gen.roadAffects && gen.roadgen!=null && !gen.roadgen.RoadsDoneAtPlace(candidate))continue;candidate.y=RawHeight(gen,candidate);}
                    if(Suitable(gen,candidate,radius))return gen.AddPoi(candidate,rot,type,attempt==0 && alignedToRoad);
                }
                return null;
            }
            // Keep the native roadside location, rotation, and aligned flag together.
            // Only reject genuinely excessive cut/fill across the solid footprint.
            if(Suitable(gen,pos,radius))return gen.AddPoi(pos,rot,type,alignedToRoad);
            return null;
        }
    }
}




