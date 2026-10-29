using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization.Json;
using UnityEngine;

namespace TldMp
{
    // The host supplies road bones, placed POIs and decorative object manifests.
    // Clients retain mesh construction/LOD, but do not make placement decisions.
    public static class StableMap
    {
        sealed class PositionComparer:IEqualityComparer<Vector3d>
        {
            public bool Equals(Vector3d a,Vector3d b){return SamePosition(a,b);}
            public int GetHashCode(Vector3d p){unchecked{return p.x.GetHashCode()^p.y.GetHashCode()*397^p.z.GetHashCode();}}
        }
        static readonly PositionComparer positionComparer=new PositionComparer();
        static StableNode node;static readonly HashSet<string> available=new HashSet<string>();
        // Available map data is monotonic for a loaded map: road/POI chunk data
        // survives scene unloads, and decoration metadata is explicitly removed
        // when its category cell is evicted. Track the map identity so the hot
        // path can trust this set without splitting/parsing every key each frame.
        static mapSettingScript availableWorld;static int availableSeed;static bool availableWorldKnown;
        static readonly Dictionary<string,double> incompletePois=new Dictionary<string,double>();
        static readonly Dictionary<string,int> incompletePoiTries=new Dictionary<string,int>();
        static readonly HashSet<string> forcedPoiChunks=new HashSet<string>();
        sealed class ManifestCell
        {public objGenScript.categoryClass Category;public Vector3d Position;public int Seed;public long Used;public objGenScript.chunkClass Data;public Dictionary<Vector3d,int> Biomes;}
        static readonly List<ManifestCell> manifestCells=new List<ManifestCell>();static long manifestCellUse;
        const int MaxManifestCells=32;
        static objGenScript.categoryClass forced,activeNativeCategory;static Vector3d forcedChunk;
        // Consumed once per host CategoryGen to skip the redundant bridge
        // recomputation of the ring. The native UpdChunksNeeded body is already
        // guard-skipped; this leaves the first call's identical ring in place.
        static objGenScript.categoryClass chunksComputedFor;
        // Call-local scratch for map bookkeeping. These arrays are cleared at
        // every use and never stored in a cache; snapshots are copied on miss.
        static readonly List<Vector3d> streamCentersScratch=new List<Vector3d>();
        static readonly HashSet<Vector3d> streamSeenScratch=new HashSet<Vector3d>(positionComparer);
        static readonly List<Vector3d> placementOriginsScratch=new List<Vector3d>();
        static readonly HashSet<Vector3d> placementSeenScratch=new HashSet<Vector3d>(positionComparer);
        static readonly List<Vector3d> placementCentersScratch=new List<Vector3d>();
        static readonly HashSet<Vector3d> sameCellsScratch=new HashSet<Vector3d>(positionComparer);
        static readonly HashSet<roadGenScript> activeRoadNative=new HashSet<roadGenScript>();
        static readonly HashSet<poiGenScript> activePoiAdd=new HashSet<poiGenScript>();
        static bool proximityCachesDirty;
        static readonly FieldInfo lastTerrainDistance=typeof(terrainGenScript).GetField("lastDrawDistance",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly Dictionary<terrainGenScript,float> colliderDistances=new Dictionary<terrainGenScript,float>();
        static readonly Dictionary<terrainGenScript,terrainGenScript.drawDistSetting> clampedMax=new Dictionary<terrainGenScript,terrainGenScript.drawDistSetting>();
        static readonly Dictionary<terrainGenScript,terrainGenScript.drawDistSetting> clampedMin=new Dictionary<terrainGenScript,terrainGenScript.drawDistSetting>();
        static readonly Dictionary<terrainGenScript,TerrainDistancePolicy.ClampState> clampStates=new Dictionary<terrainGenScript,TerrainDistancePolicy.ClampState>();
        static readonly HashSet<terrainGenScript> clampLogged=new HashSet<terrainGenScript>();
        // Logical cell size is derived only from category data, never from the local
        // slider. This keeps cell IDs stable across peers while allowing short-range
        // categories to use much smaller cells than the old 1 km floor.
        const float MinStreamCellSize=128f;
        static readonly Dictionary<objGenScript.categoryClass,List<Vector3d>> lastPlacementCells=new Dictionary<objGenScript.categoryClass,List<Vector3d>>();
        static readonly Dictionary<objGenScript.categoryClass,List<Vector3d>> lastPlacementOrigins=new Dictionary<objGenScript.categoryClass,List<Vector3d>>();
        static readonly Dictionary<objGenScript.categoryClass,float> lastPlacementRadius=new Dictionary<objGenScript.categoryClass,float>();
        static readonly Dictionary<objGenScript.categoryClass,List<Vector3d>> lastStreamCenters=new Dictionary<objGenScript.categoryClass,List<Vector3d>>();
        static readonly Dictionary<objGenScript.categoryClass,float> lastStreamRadius=new Dictionary<objGenScript.categoryClass,float>();
        static readonly Dictionary<objGenScript.categoryClass,float> lastStreamCellSize=new Dictionary<objGenScript.categoryClass,float>();
        static readonly Dictionary<objGenScript.categoryClass,bool> lastStreamEnabled=new Dictionary<objGenScript.categoryClass,bool>();
        static readonly Dictionary<objGenScript.categoryClass,int> placementRevision=new Dictionary<objGenScript.categoryClass,int>();
        static readonly Dictionary<objGenScript.categoryClass,int> lastPlacementRevision=new Dictionary<objGenScript.categoryClass,int>();
        // Guest manifest requests are idempotent inside the transport's own retry
        // window: StableNode.RequestMap ignores a pending key until its progress
        // timeout (5 s). Rebuilding the same keys every frame only burns CPU, so a
        // repeat pass runs at most once a second or when the needed list changes.
        sealed class NeedState { internal List<Vector3d> List; internal double Next; internal int Cursor; }
        static readonly Dictionary<objGenScript.categoryClass,NeedState> needStates=new Dictionary<objGenScript.categoryClass,NeedState>();
        // Cleanup of cells outside the ring and the "all manifests available" answer
        // only recompute when their inputs change (see StreamScanPolicy.cs).
        static readonly Dictionary<objGenScript.categoryClass,EvictGate> evictGates=new Dictionary<objGenScript.categoryClass,EvictGate>();
        static readonly ManifestReadyCache manifestReady=new ManifestReadyCache();
        const double NeedRepeatSeconds=1.0;
        const double IncompletePoiRetry=5.0;
        static bool ringLogged;
        static readonly MethodInfo memberwiseClone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        public static bool HasCalendar;static int calendar;
        [Serializable] public sealed class Dot {public Vector3d Pos;public List<int> Connected;}
        [Serializable] public sealed class Element {public int Biome,Road;public List<Vector3d> Bones;}
        [Serializable] public sealed class Line
        {public Vector3d Self,C1,C2,Start,End;public int ID,I1,I2,Type;public float Min,Max;public List<Element> Elements=new List<Element>();}
        [Serializable] public sealed class Inner {public Vector3d Pos;public List<roadGenScript.roadChunk> Roads;}
        [Serializable] public sealed class Road
        {public List<Dot> Dots=new List<Dot>();public List<Line> Lines=new List<Line>();public List<Inner> Inner=new List<Inner>();public List<int> Main;public int Seed;public bool Inside,U,R;}
        [Serializable] public sealed class Poi {public int ID;public Vector3d Pos;public Vector3 Rot;public bool Aligned;public save_poi State;}
        [Serializable] public sealed class Decoration
        {public int Seed,Prefab,Biome;public Vector3d Chunk,PosID,GPosID,Pos;public Vector3 Rot;public float Scale;}
        [Serializable] public sealed class Chunk
        {public int Version=1,Seed;public string Key;public Vector3d Position;public Road Road;public List<Poi> Pois=new List<Poi>();public List<Decoration> Objects=new List<Decoration>();public bool PoisComplete=true;}
        static mapSettingScript Map {get{return menuhandler.s==null?null:menuhandler.s.currentMainMap;}}
        public static void Configure(StableNode value)
        {
            node=value;available.Clear();incompletePois.Clear();incompletePoiTries.Clear();forcedPoiChunks.Clear();manifestCells.Clear();manifestCellUse=0;lastPlacementCells.Clear();lastPlacementOrigins.Clear();lastPlacementRadius.Clear();lastStreamCenters.Clear();lastStreamRadius.Clear();lastStreamCellSize.Clear();lastStreamEnabled.Clear();placementRevision.Clear();lastPlacementRevision.Clear();needStates.Clear();evictGates.Clear();manifestReady.Invalidate();ringLogged=false;HasCalendar=false;
            availableWorld=Map;availableSeed=availableWorld==null?0:availableWorld.seed;availableWorldKnown=true;
            proximityCachesDirty=false;
            if(node==null)return;
            if(node.IsHost){int bits=0;for(int i=0;i<4;i++)if(mainscript.CheckHoliday((mainscript.holiday)i))bits|=1<<i;calendar=bits;HasCalendar=true;node.HostConfig=BitConverter.GetBytes(bits);}
            node.ConfigReceived=delegate(byte[] b){if(b.Length!=4){node.Stop("Missing host world-generation settings.");return;}calendar=BitConverter.ToInt32(b,0);HasCalendar=true;};
            node.MapRequested=Provide;node.MapApplied=Apply;
            node.CanEnterWorld=Ready;
        }
        public static bool OriginalHoliday(){return node==null || !HasCalendar;}
        public static bool Holiday(mainscript.holiday h){return (int)h>=0 && (int)h<4 && (calendar&(1<<(int)h))!=0;}
        static string Key(string kind,int gen,int category,Vector3d p)
        {return kind+"|"+gen+"|"+category+"|"+p.x.ToString("R",CultureInfo.InvariantCulture)+"|"+p.z.ToString("R",CultureInfo.InvariantCulture);}
        static bool Need(string key)
        {
            EnsureAvailableWorld();
            double retryAt;
            if(incompletePois.TryGetValue(key,out retryAt))
            {
                if(Time.realtimeSinceStartup<retryAt)return true;
                int tries;incompletePoiTries.TryGetValue(key,out tries);
                // Stop after three refreshes. A host chunk that never really generated
                // stays non-authoritative, so caching it is safe and avoids a 5 s loop.
                if(tries>=3){incompletePois.Remove(key);incompletePoiTries.Remove(key);return true;}
                incompletePoiTries[key]=tries+1;
                incompletePois.Remove(key);available.Remove(key);
            }
            if(HasAvailable(key))return true;
            node.RequestMap(key);return false;
        }
        static bool HasAvailable(string key){return available.Contains(key);}
        static void EnsureAvailableWorld()
        {
            var world=Map;int seed=world==null?0:world.seed;
            if(!availableWorldKnown || !ReferenceEquals(world,availableWorld) || seed!=availableSeed)
            {
                available.Clear();incompletePois.Clear();incompletePoiTries.Clear();
                availableWorld=world;availableSeed=seed;availableWorldKnown=true;
            }
        }
        static int Index<T>(List<T> list,T value){return list==null?-1:list.IndexOf(value);}
        // The supported game's Vector3d operators compare squared distance with
        // zero: even equal finite coordinates fail == and pass !=. Cell identity
        // must compare the components directly.
        public static bool SamePosition(Vector3d a,Vector3d b)
        {return a.x==b.x && a.y==b.y && a.z==b.z;}
        public static bool RoadUpdate(roadGenScript road,bool more)
        {
            if(node==null || node.IsHost)
            {
                if(activeRoadNative.Contains(road))return true;
                road.UpdChunksNeeded();var before=RoadDependencyStamp(road,road.chunksNeeded,false);
                activeRoadNative.Add(road);
                FrameDiagnostics.NativeRoadNativeBegin();
                try{road.Upd(more);}
                finally{FrameDiagnostics.NativeRoadNativeEnd();activeRoadNative.Remove(road);}
                if(DependencyChanged(before,RoadDependencyStamp(road,road.chunksNeeded,false)))InvalidateProximityCaches();
                FlushProximityCaches();
                return false;
            }
            if(Map==null)return false;
            // Complete collision prerequisites before requesting the whole visual
            // draw-distance ring. Otherwise distant scenery occupies the join queue.
            if(!node.Ready){Terrain(null);road.UpdPlaceRemove(more);return false;}
            road.UpdChunksNeeded();int gen=Index(Map.roadGens,road);
            foreach(var pos in road.chunksNeeded)for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
            {var chunk=mainscript.GetChunkPos(pos,x,z,road.chunkSize);if(!road.hasSideRoad && road.hasMainRoad && chunk.x!=0)continue;Need(Key("r",gen,0,chunk));}
            road.UpdPlaceRemove(more);return false;
        }
        public static bool RoadAt(roadGenScript road,Vector3d pos)
        {
            if(node==null || node.IsHost)
            {
                // Let the stock Upd(Vector3d) run in place. Calling it from this
                // prefix would execute native place/remove work twice and can
                // unload host roads while answering a proximity lookup.
                return true;
            }
            int gen=Index(Map.roadGens,road);var chunk=mainscript.GetChunkPos(pos,road.chunkSize);Need(Key("r",gen,0,chunk));return false;
        }
        public static void RoadGenerated(roadGenScript road)
        {
            // RoadsNear can generate a line directly during a proximity lookup,
            // outside the regular road updater. Clear only on that real mutation.
            InvalidateProximityCaches();
        }
        public static bool PoiChunk(poiGenScript gen,Vector3d chunk)
        {if(node==null || node.IsHost)return true;Need(Key("p",Index(Map.poiGens,gen),0,chunk));return false;}
        public static bool PoiAdd(poiGenScript gen)
        {
            if(node!=null && !node.IsHost){FrameDiagnostics.NotePoiAdd(false);return true;}
            if(activePoiAdd.Contains(gen)){FrameDiagnostics.NotePoiAdd(false);return true;}
            FrameDiagnostics.NotePoiAdd(true);
            FrameDiagnostics.NativePoiAddBegin();
            try
            {
                var before=PoiDependencyStamp(gen,gen.chunksNeeded);activePoiAdd.Add(gen);
                try{gen.AddPois();}
                finally{activePoiAdd.Remove(gen);}
                if(DependencyChanged(before,PoiDependencyStamp(gen,gen.chunksNeeded)))InvalidateProximityCaches();
                FlushProximityCaches();
                return false;
            }
            finally{FrameDiagnostics.NativePoiAddEnd();}
        }
        static bool DependencyChanged(long before,long after){return before!=after;}
        static int Mix(int hash,int value){unchecked{return (hash^value)*16777619;}}
        static int PositionHash(Vector3d p)
        {unchecked{return p.x.GetHashCode()*397^p.y.GetHashCode()*31^p.z.GetHashCode();}}
        static List<Vector3d> OrderedPositions(IEnumerable<Vector3d> positions)
        {
            var result=new List<Vector3d>(positions);result.Sort(delegate(Vector3d a,Vector3d b)
            {int order=a.x.CompareTo(b.x);if(order!=0)return order;order=a.y.CompareTo(b.y);return order!=0?order:a.z.CompareTo(b.z);});return result;
        }
        static long RoadDependencyStamp(roadGenScript road,IList<Vector3d> centers,bool includeNeighbors)
        {
            long started=FrameDiagnostics.Enabled?Stopwatch.GetTimestamp():0;
            long value;
            unchecked
            {
                int hash=17;var cells=new HashSet<Vector3d>(positionComparer);
                if(centers!=null)foreach(var center in centers)
                {if(includeNeighbors)for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)cells.Add(mainscript.GetChunkPos(center,x,z,road.chunkSize));else cells.Add(center);}
                foreach(var cell in OrderedPositions(cells))
                {
                    hash=Mix(hash,PositionHash(cell));roadGenScript.dotChunkClass chunk;
                    if(!road.chunks.TryGetValue(cell,out chunk)||chunk==null){hash=Mix(hash,-1);continue;}
                    hash=Mix(hash,chunk.dots==null?0:chunk.dots.Count);hash=Mix(hash,chunk.roads==null?0:chunk.roads.Count);
                    hash=Mix(hash,chunk.innerChunks==null?0:chunk.innerChunks.Count);
                    hash=Mix(hash,chunk.innerChunkGenerated?1:0);hash=Mix(hash,chunk.insideConnected?1:0);
                    hash=Mix(hash,chunk.outsideUConnected?1:0);hash=Mix(hash,chunk.outsideRConnected?1:0);
                    if(chunk.roads!=null)foreach(var r in chunk.roads)
                    {hash=Mix(hash,r.generated?1:0);hash=Mix(hash,r.roadElements==null?0:r.roadElements.Count);}
                }
                value=((long)hash<<32)|(uint)cells.Count;
            }
            if(started!=0)FrameDiagnostics.AddRoadStamp(Stopwatch.GetTimestamp()-started);
            return value;
        }
        static long PoiDependencyStamp(poiGenScript gen,IList<Vector3d> cells)
        {
            long started=FrameDiagnostics.Enabled?Stopwatch.GetTimestamp():0;
            long value;
            unchecked
            {
                int hash=23;int count=0;var unique=new HashSet<Vector3d>(positionComparer);if(cells!=null)foreach(var cell in cells)unique.Add(cell);
                foreach(var cell in OrderedPositions(unique))
                {
                    hash=Mix(hash,PositionHash(cell));poiGenScript.chunkClass chunk;
                    if(!gen.chunks.TryGetValue(cell,out chunk)||chunk==null){hash=Mix(hash,-1);continue;}
                    hash=Mix(hash,chunk.generated?1:0);hash=Mix(hash,chunk.poiToPlace==null?0:chunk.poiToPlace.Count);
                    hash=Mix(hash,chunk.pois==null?0:chunk.pois.Count);count+=chunk.pois==null?0:chunk.pois.Count;
                    if(chunk.pois!=null)foreach(var key in OrderedPositions(chunk.pois.Keys))
                    {var poi=chunk.pois[key];hash=Mix(hash,PositionHash(key));hash=Mix(hash,poi==null?-1:poi.id);if(poi!=null){hash=Mix(hash,PositionHash(poi.pos));hash=Mix(hash,poi.rot.GetHashCode());hash=Mix(hash,poi.alignedToRoad?1:0);}}
                }
                value=((long)hash<<32)|(uint)count;
            }
            if(started!=0)FrameDiagnostics.AddPoiStamp(Stopwatch.GetTimestamp()-started);
            return value;
        }
        static void InvalidateProximityCaches()
        {
            proximityCachesDirty=true;
            // Manifest chunks depend on these lookups, but they are not used by
            // an in-progress native tile fill and can be dropped immediately.
            if(manifestCells.Count>0){manifestCells.Clear();manifestCellUse=0;}
        }
        static void FlushProximityCaches(){FlushProximityCaches(Map);}
        static void FlushProximityCaches(mapSettingScript map)
        {
            if(!proximityCachesDirty)return;
            // These are UnityEngine.Objects, whose overloaded == treats test
            // stand-ins without native pointers as null. Cache state is managed,
            // so use CLR identity here and still flush such generators.
            if(ReferenceEquals(map,null) || ReferenceEquals(map.objGens,null))return;
            foreach(var generator in map.objGens)
                if(!ReferenceEquals(generator,null) && generator.tiles!=null)generator.tiles.Clear();
            proximityCachesDirty=false;
        }
        public static float CategoryRadius(float slider,float percent,float min,float max)
        {
            float value=slider*percent;
            if(float.IsNaN(min)||float.IsInfinity(min))min=0f;
            if(float.IsNaN(max)||float.IsInfinity(max))max=min;
            if(max<min){float t=max;max=min;min=t;}
            if(float.IsNaN(value))value=min;
            else if(float.IsPositiveInfinity(value))value=max;
            else if(float.IsNegativeInfinity(value))value=min;
            return Mathf.Round(Mathf.Clamp(value,min,max));
        }
        public static float LogicalCellSizeFor(float drawDistMin,float drawDistMax,float gridSize)
        {
            float grid=gridSize;
            if(!(grid>0f)||!Numbers.Finite(grid))grid=MinStreamCellSize;
            if(!Numbers.Finite(drawDistMin))drawDistMin=MinStreamCellSize;
            if(!Numbers.Finite(drawDistMax))drawDistMax=MinStreamCellSize;
            float maxRange=Math.Max(Math.Max(drawDistMin,drawDistMax),MinStreamCellSize);
            // At the category maximum, keep the ordinary ring to roughly 16 cells
            // across. The rounded grid multiple is serialized category data, so it
            // remains identical when a player moves the graphics slider.
            float target=Math.Max(MinStreamCellSize,Math.Min(512f,maxRange/8f));
            // Native scratch generation examines resolution² grid points at once.
            // Bound each deterministic cell to 32×32 candidates (≤1024) even when
            // a category has an unusually coarse authored grid.
            double cells=Math.Max(1,Math.Min(32,Math.Round(target/grid)));
            return (float)(cells*grid);
        }
        static void Grid(objGenScript.categoryClass cat)
        {
            float slider=settingsscript.s==null?cat.script.drawDist:settingsscript.s.S.FDrawDistance;
            cat.currentDrawDist=CategoryRadius(slider,cat.drawDistPercent,cat.drawDistMin,cat.drawDistMax);
            double cells=Math.Max(1,Mathf.RoundToInt((float)(LogicalCellSizeFor(cat.drawDistMin,cat.drawDistMax,cat.gridSize)/Math.Max(.01,cat.gridSize))));
            cat.resolution=(int)cells;cat.currentChunkSize=(float)(cells*cat.gridSize);cat.offset=-(float)Math.Floor(cells/2)*cat.gridSize;
            cat.lastDrawDist=cat.currentDrawDist;
        }
        static float LocalDrawDistance(objGenScript.categoryClass cat){return cat.currentDrawDist;}
        // GetChunksNeeded iterates -edge/2..edge/2; keep enough cells for every
        // location within the category radius plus its intersecting edge cells.
        public static int StreamEdge(float drawDistance,float chunkSize)
        {
            if(!(chunkSize>0) || !Numbers.Finite(chunkSize))return 2;
            if(!(drawDistance>0)||!Numbers.Finite(drawDistance))drawDistance=0;
            int half=(int)Math.Ceiling(drawDistance/chunkSize+1.5f);
            if(half<1)half=1;
            return half*2;
        }
        static void LogRing(objGenScript.categoryClass cat,int edge)
        {
            if(ringLogged)return;ringLogged=true;
            float coverage=cat.currentChunkSize*(edge/2)+cat.currentChunkSize/2f;
            StableGameBridge.JoinDiagnostic("Streamed prop ring: cell="+cat.currentChunkSize.ToString("0",CultureInfo.InvariantCulture)+
                "m edge="+edge+" draw=~"+LocalDrawDistance(cat).ToString("0",CultureInfo.InvariantCulture)+
                "m coverage=~"+coverage.ToString("0",CultureInfo.InvariantCulture)+"m");
        }
        public static bool CellIntersectsRadius(Vector3d cell,Vector3d center,float size,float radius)
        {
            double dx=Math.Max(0,Math.Abs(cell.x-center.x)-size*.5),dz=Math.Max(0,Math.Abs(cell.z-center.z)-size*.5);
            return dx*dx+dz*dz<=radius*radius;
        }
        static List<Vector3d> PlacementCenters(objGenScript.categoryClass cat)
        {
            if(cat.script==null||cat.script.mapSetting==null)return null;
            var centers=placementCentersScratch;centers.Clear();
            foreach(var around in cat.script.mapSetting.genArounds)
            {if(around.t!=null)centers.Add(mainscript.GlobalFromUnityPos(around.upos));}
            return centers;
        }
        public static bool WithinAnyRadius(Vector3d position,IList<Vector3d> centers,float radius)
        {
            if(centers==null)return true;
            for(int i=0;i<centers.Count;i++)if(WithinRadius(position,centers[i],radius))return true;
            return false;
        }
        public static bool WithinRadius(Vector3d position,Vector3d center,float radius)
        {double dx=position.x-center.x,dz=position.z-center.z;return dx*dx+dz*dz<=(double)radius*radius;}
        static void AddUnique(List<Vector3d> list,HashSet<Vector3d> seen,Vector3d value)
        {if(seen.Add(value))list.Add(value);}
        static List<Vector3d> StreamCenters(objGenScript.categoryClass cat)
        {
            var centers=streamCentersScratch;centers.Clear();
            if(cat.script==null||cat.script.mapSetting==null)return centers;
            var seen=streamSeenScratch;seen.Clear();
            foreach(var around in cat.script.mapSetting.genArounds)if(around.t!=null)
            {var p=mainscript.GlobalFromUnityPos(around.upos);double q=Math.Min(cat.currentChunkSize,Math.Max(1,cat.gridSize*16));var center=new Vector3d(Math.Round(p.x/q)*q,0,Math.Round(p.z/q)*q);if(seen.Add(center))centers.Add(center);}
            return centers;
        }
        static bool CategoryEnabled(objGenScript.categoryClass cat)
        {return !cat.disable&&!cat.script.disabled&&(cat.onlyOnHoliday==mainscript.holiday.none||Holiday(cat.onlyOnHoliday))&&(cat.disableOnHoliday==mainscript.holiday.none||!Holiday(cat.disableOnHoliday));}
        public static bool CategoryChunks(objGenScript.categoryClass cat)
        {
            if(node==null)return true;
            bool duplicate=ReferenceEquals(cat,chunksComputedFor);
            if(duplicate)
            {
                chunksComputedFor=null;
                FrameDiagnostics.NoteCategoryChunks(true,true);
                return false;
            }
            FrameDiagnostics.NativeCategoryChunksBegin();
            try
            {
                Grid(cat);
                if(forced==cat){cat.chunksNeeded=new List<Vector3d>{forcedChunk};return false;}
                var previous=cat.chunksNeeded;
                bool enabled=CategoryEnabled(cat);var centers=StreamCenters(cat);List<Vector3d> cachedCenters;float cachedRadius,cachedCell;bool cachedEnabled;
                bool cacheHit=lastStreamCenters.TryGetValue(cat,out cachedCenters)&&SameCells(cachedCenters,centers)&&
                    lastStreamRadius.TryGetValue(cat,out cachedRadius)&&cachedRadius==cat.currentDrawDist&&
                    lastStreamCellSize.TryGetValue(cat,out cachedCell)&&cachedCell==cat.currentChunkSize&&
                    lastStreamEnabled.TryGetValue(cat,out cachedEnabled)&&cachedEnabled==enabled;
                FrameDiagnostics.NoteCategoryChunks(cacheHit,false);
                int edge=2;
                if(!cacheHit)
                {
                    cat.chunksNeeded=new List<Vector3d>();
                    double jitter=Math.Abs(cat.tollas*cat.gridSize);
                    // Centers stay anchored to their logical cell until the player
                    // crosses it. Inflate the query by the anchor displacement and
                    // native per-object jitter so every in-radius object is covered.
                    double anchorQuantum=Math.Min(cat.currentChunkSize,Math.Max(1,cat.gridSize*16));
                    float expanded=(float)(cat.currentDrawDist+anchorQuantum*.71);
                    edge=StreamEdge(expanded,cat.currentChunkSize);
                    if(enabled&&centers.Count>0)
                    {
                        var candidates=cat.script.mapSetting.GetChunksNeeded(cat.currentChunkSize,edge);
                        var seen=new HashSet<Vector3d>(positionComparer);
                        foreach(var candidate in candidates)
                        {
                            var cell=mainscript.Snap(candidate,cat.gridSize);
                            foreach(var center in centers)
                            {if(CellIntersectsRadius(cell,center,cat.currentChunkSize+(float)(2*jitter),expanded)){AddUnique(cat.chunksNeeded,seen,cell);break;}}
                        }
                    }
                    else cat.chunksNeeded.Clear();
                    lastStreamCenters[cat]=new List<Vector3d>(centers);lastStreamRadius[cat]=cat.currentDrawDist;lastStreamCellSize[cat]=cat.currentChunkSize;lastStreamEnabled[cat]=enabled;
                }
                if(!cacheHit)
                {
                    int gen=Index(Map.objGens,cat.script),category=cat.script.categories.IndexOf(cat);
                    CancelDepartedStreamCells(false,previous,cat,gen,category);
                    LogRing(cat,edge);
                }
                return false;
            }
            finally{FrameDiagnostics.NativeCategoryChunksEnd();}
        }
        static void CancelDepartedStreamCells(bool cacheHit,List<Vector3d> previous,objGenScript.categoryClass cat,int gen,int category)
        {
            // A cache hit leaves chunksNeeded untouched, so previous and the current
            // ring are identical. Avoid rebuilding the ring hash set and walking it.
            if(cacheHit)return;
            var neededSet=new HashSet<Vector3d>(cat.chunksNeeded,positionComparer);
            foreach(var old in previous)if(!neededSet.Contains(old) && !cat.generatedChunks.ContainsKey(old))
            {string key=Key("o",gen,category,old);available.Remove(key);if(node!=null)node.CancelMap(key);}
        }
        public static void RestorePlacement(objGenScript.categoryClass cat)
        {
            if(cat==null)return;
            FrameDiagnostics.NativeRestorePlacementBegin();
            try
            {
                List<Vector3d> previous,oldOrigins;float oldRadius;int oldRevision,currentRevision;
                placementRevision.TryGetValue(cat,out currentRevision);var origins=PlacementOrigins(cat);
                bool cacheHit=lastPlacementCells.TryGetValue(cat,out previous) && SameCells(previous,cat.chunksNeeded) &&
                    lastPlacementOrigins.TryGetValue(cat,out oldOrigins) && SameCells(oldOrigins,origins) &&
                    lastPlacementRadius.TryGetValue(cat,out oldRadius) && oldRadius==cat.currentDrawDist &&
                    lastPlacementRevision.TryGetValue(cat,out oldRevision) && oldRevision==currentRevision;
                FrameDiagnostics.NoteRestorePlacement(cacheHit);
                if(cacheHit)return;
                lastPlacementCells[cat]=new List<Vector3d>(cat.chunksNeeded);
                lastPlacementOrigins[cat]=new List<Vector3d>(origins);
                lastPlacementRadius[cat]=cat.currentDrawDist;
                lastPlacementRevision[cat]=currentRevision;
                // This scan contains no Unity callbacks or yield points. Lazily snapshot
                // exact ordered active centers at the first needed object, then reuse
                // them instead of converting every player transform for every object.
                List<Vector3d> centers=null;bool centersCaptured=false;float radius=cat.currentDrawDist;
                var neededSet=new HashSet<Vector3d>(cat.chunksNeeded,positionComparer);
                bool countObjects=FrameDiagnostics.Enabled;
                foreach(var pair in cat.generatedChunks)
                {
                    bool needed=neededSet.Contains(pair.Key);
                    foreach(var obj in pair.Value.objs.Values)
                    {
                        if(countObjects)FrameDiagnostics.placementObjects++;
                        if(!needed){obj.place=false;continue;}
                        if(!centersCaptured){centers=PlacementCenters(cat);centersCaptured=true;}
                        obj.place=WithinAnyRadius(obj.gpos,centers,radius);
                    }
                }
                PlacementSkip.Touch(cat);
            }
            finally{FrameDiagnostics.NativeRestorePlacementEnd();}
        }
        static List<Vector3d> PlacementOrigins(objGenScript.categoryClass cat)
        {
            var result=placementOriginsScratch;result.Clear();
            if(cat.script==null||cat.script.mapSetting==null)return result;
            double quantum=Math.Max(.25,cat.gridSize*.5);
            var seen=placementSeenScratch;seen.Clear();
            foreach(var around in cat.script.mapSetting.genArounds)if(around.t!=null)
            {var p=mainscript.GlobalFromUnityPos(around.upos);AddUnique(result,seen,new Vector3d(Math.Round(p.x/quantum)*quantum,0,Math.Round(p.z/quantum)*quantum));}
            return result;
        }
        static bool ContainsPosition(List<Vector3d> positions,Vector3d value){for(int i=0;i<positions.Count;i++)if(SamePosition(positions[i],value))return true;return false;}
        // Stable in-place replacement for the LINQ OrderBy/ToList prologue in
        // categoryClass.UpdPlaceRemove. The native list is already the key source;
        // equal distances retain their original relative order, exactly like
        // Enumerable.OrderBy's documented stable sort.
        static readonly List<Vector3d> orderKeysScratch=new List<Vector3d>();
        static double[] orderDistScratch=new double[0];
        static int[] orderIndexScratch=new int[0];
        sealed class OrderIndexComparer:IComparer<int>
        {
            public int Compare(int a,int b)
            {
                int order=orderDistScratch[a].CompareTo(orderDistScratch[b]);
                return order!=0?order:a.CompareTo(b);
            }
        }
        static readonly IComparer<int> orderIndexComparer=new OrderIndexComparer();
        public static void SortPlaceOrder(List<Vector3d> order,Vector3d origin)
        {
            if(order==null)return;
            if(PlacementSkip.Skip(order))return;
            int n=order.Count;if(n<2)return;
            if(orderDistScratch.Length<n)
            {
                int cap=orderDistScratch.Length==0?16:orderDistScratch.Length;
                while(cap<n)cap<<=1;
                orderDistScratch=new double[cap];orderIndexScratch=new int[cap];
            }
            orderKeysScratch.Clear();
            for(int i=0;i<n;i++)
            {
                Vector3d key=order[i];orderKeysScratch.Add(key);
                Vector3d delta=key-origin;orderDistScratch[i]=delta.sqrMagnitude;orderIndexScratch[i]=i;
            }
            Array.Sort(orderIndexScratch,0,n,orderIndexComparer);
            for(int i=0;i<n;i++)order[i]=orderKeysScratch[orderIndexScratch[i]];
        }
        public static bool ShouldApplyDecoration(Vector3d position,List<Vector3d> needed){return ContainsPosition(needed,position);}
        public static bool CanEvictMetadata(bool hasLiveObject,bool saveDestroyPending){return !hasLiveObject&&!saveDestroyPending;}
        public static bool IsTombstoned(objGenScript.categoryClass cat,Vector3d chunk,Vector3d position)
        {List<Vector3d> destroyed;return cat!=null&&cat.destroyed!=null&&cat.destroyed.TryGetValue(chunk,out destroyed)&&ContainsPosition(destroyed,position);}
        static bool SameCells(List<Vector3d> a,List<Vector3d> b)
        {
            if(a==null||b==null||a.Count!=b.Count)return false;
            sameCellsScratch.Clear();
            for(int i=0;i<b.Count;i++)sameCellsScratch.Add(b[i]);
            for(int i=0;i<a.Count;i++)if(!sameCellsScratch.Contains(a[i]))return false;
            return true;
        }
        public static bool CanPrunePois(bool complete,int count){return complete && count>0;}
        // Native UpdGen mutates its category's working collections and scans every
        // generated cell to mark/remove old chunks. A shallow category clone keeps
        // configuration and script references, while these fresh collections confine
        // its mutable generation state to the single requested manifest cell.
        public static objGenScript.categoryClass CreateManifestCategory(objGenScript.categoryClass source)
        {
            if(source==null)throw new ArgumentNullException("source");
            var scratch=(objGenScript.categoryClass)memberwiseClone.Invoke(source,null);
            scratch.chunksNeeded=new List<Vector3d>();
            scratch.chunksToRemove=new List<Vector3d>();
            scratch.generatedChunks=new Dictionary<Vector3d,objGenScript.chunkClass>();
            // Biome lookups are cheap to rebuild and native generation adds to this map.
            // Keep destruction tombstones shared and read-only; UpdGen only consults them.
            scratch.biomes=new Dictionary<Vector3d,int>();
            return scratch;
        }
        static ManifestCell FindManifestCell(objGenScript.categoryClass category,Vector3d position,int seed)
        {
            foreach(var entry in manifestCells)if(entry.Category==category && entry.Seed==seed && SamePosition(entry.Position,position))
            {entry.Used=++manifestCellUse;return entry;}
            return null;
        }
        static void CacheManifestCell(objGenScript.categoryClass category,Vector3d position,int seed,objGenScript.chunkClass data,Dictionary<Vector3d,int> biomes)
        {
            var entry=FindManifestCell(category,position,seed);
            if(entry!=null){entry.Data=data;entry.Biomes=biomes;return;}
            if(manifestCells.Count>=MaxManifestCells)
            {
                int oldest=0;for(int i=1;i<manifestCells.Count;i++)if(manifestCells[i].Used<manifestCells[oldest].Used)oldest=i;
                manifestCells.RemoveAt(oldest);
            }
            manifestCells.Add(new ManifestCell {Category=category,Position=position,Seed=seed,Used=++manifestCellUse,Data=data,Biomes=biomes});
        }
        public static bool CategoryGen(objGenScript.categoryClass cat,bool more)
        {
            if(node==null)return true;
            // Re-enter the native method once so its seeded generation and per-frame
            // pacing remain intact, then clip the newly generated edge-cell objects
            // before UpdPlaceRemove can instantiate them.
            if(activeNativeCategory==cat)return true;
            FlushProximityCaches();
            // Manifest scratch generation deliberately runs the native generator for
            // exactly one requested cell and must not touch the live world state.
            if(forced==cat)return true;
            if(node.IsHost)
            {
                CategoryChunks(cat);int before=CompletedChunks(cat);activeNativeCategory=cat;
                chunksComputedFor=cat;
                long nativeStart=PhaseTimings.Begin();
                try{cat.UpdGen(more);}
                finally{activeNativeCategory=null;chunksComputedFor=null;PhaseTimings.End(PhaseTimings.HostCategoryGen,nativeStart,cat.name);}
                FlushProximityCaches();
                if(CompletedChunks(cat)!=before){int revision;placementRevision.TryGetValue(cat,out revision);placementRevision[cat]=revision+1;}
                long restoreStart=PhaseTimings.Begin();
                RestorePlacement(cat);PhaseTimings.End(PhaseTimings.HostRestorePlacement,restoreStart,cat.name);return false;
            }
            if(!node.Ready)return false;
            long phaseStart=PhaseTimings.Begin(),stepStart=phaseStart;
            CategoryChunks(cat);int gen=Index(Map.objGens,cat.script),category=cat.script.categories.IndexOf(cat);
            PhaseTimings.End(PhaseTimings.CategoryChunks,stepStart,cat.name);stepStart=PhaseTimings.Begin();
            RequestCategoryChunks(cat,gen,category);
            PhaseTimings.End(PhaseTimings.RequestCategoryChunks,stepStart,cat.name);stepStart=PhaseTimings.Begin();
            // Native LOD cleanup is preserved without native random generation.
            RestorePlacement(cat);
            PhaseTimings.End(PhaseTimings.RestorePlacement,stepStart,cat.name);stepStart=PhaseTimings.Begin();
            if(EvictDue(cat))EvictCategory(cat,gen,category);
            PhaseTimings.End(PhaseTimings.EvictCategory,stepStart,cat.name);
            PhaseTimings.End(PhaseTimings.CategoryGen,phaseStart,cat.name);
            return false;
        }
        static void RequestCategoryChunks(objGenScript.categoryClass cat,int gen,int category)
        {
            double now=Time.realtimeSinceStartup;
            NeedState state;
            if(!needStates.TryGetValue(cat,out state)){state=new NeedState();needStates[cat]=state;}
            var needed=cat.chunksNeeded;
            // A new list restarts the pass at once; an unchanged one repeats once a
            // second. A pass is cut into bounded slices (one per call) so a large
            // ring never formats thousands of keys in a single frame.
            if(!ReferenceEquals(state.List,needed)){state.List=needed;state.Cursor=0;state.Next=0;}
            else if(state.Cursor==0 && now<state.Next){PhaseTimings.Count(PhaseTimings.NeedPassesSkipped,1);return;}
            ScanStep step=ScanBudget.Next(state.Cursor,needed.Count,ScanBudget.KeysPerCall);
            int requested=0;
            for(int i=step.Start;i<step.End;i++)if(!Need(Key("o",gen,category,needed[i])))requested++;
            int visited=step.End-step.Start;
            PhaseTimings.Count(PhaseTimings.KeysFormatted,visited);
            PhaseTimings.Count(PhaseTimings.NeedRequested,requested);PhaseTimings.Count(PhaseTimings.NeedAvailable,visited-requested);
            if(step.Finished){state.Cursor=0;state.Next=now+NeedRepeatSeconds;}
            else state.Cursor=step.End;
        }
        static int CompletedChunks(objGenScript.categoryClass cat)
        {int count=0;foreach(var chunk in cat.generatedChunks.Values)if(chunk.generated)count++;return count;}
        // Gate for the per-frame caller; EvictCategory itself always does the full pass.
        static bool EvictDue(objGenScript.categoryClass cat)
        {
            if(cat.generatedChunks==null)return false;
            EvictGate gate;if(!evictGates.TryGetValue(cat,out gate)){gate=new EvictGate();evictGates[cat]=gate;}
            return gate.ShouldRun(cat.chunksNeeded,cat.chunksNeeded.Count,cat.generatedChunks.Count,Time.realtimeSinceStartup);
        }
        static void EvictCategory(objGenScript.categoryClass cat,int gen,int category)
        {
            if(cat.generatedChunks==null)return;
            var neededSet=new HashSet<Vector3d>(cat.chunksNeeded,positionComparer);
            var remove=new List<Vector3d>();
            PhaseTimings.Count(PhaseTimings.EvictCells,cat.generatedChunks.Count);
            foreach(var pair in cat.generatedChunks)
            {
                if(neededSet.Contains(pair.Key))continue;
                bool safe=true;
                PhaseTimings.Count(PhaseTimings.EvictObjects,pair.Value.objs.Count);
                foreach(var obj in pair.Value.objs.Values)
                {
                    bool missingSavedObject=cat.saveDestroy&&obj.placed&&obj.obj==null;
                    if(missingSavedObject&&!IsTombstoned(cat,obj.chunkID,obj.gposID))PersistTombstone(cat,obj);
                    bool pendingTombstone=missingSavedObject&&!IsTombstoned(cat,obj.chunkID,obj.gposID);
                    if(!CanEvictMetadata(obj.obj!=null,pendingTombstone)){safe=false;break;}
                }
                if(safe)remove.Add(pair.Key);
            }
            if(remove.Count>0)PlacementSkip.Touch(cat);
            PhaseTimings.Count(PhaseTimings.EvictRemoved,remove.Count);
            foreach(var position in remove)
            {
                cat.generatedChunks.Remove(position);
                string key=Key("o",gen,category,position);available.Remove(key);if(node!=null)node.CancelMap(key);
            }
        }
        static void PersistTombstone(objGenScript.categoryClass cat,objGenScript.objClass obj)
        {
            List<Vector3d> ids;if(!cat.destroyed.TryGetValue(obj.chunkID,out ids))cat.destroyed[obj.chunkID]=ids=new List<Vector3d>();
            if(!ContainsPosition(ids,obj.gposID))ids.Add(obj.gposID);
        }
        public static bool Terrain(terrainGenScript terrain)
        {
            ProtectColliderGrid(terrain);
            if(node==null || node.IsHost)return true;if(Map==null || !HasCalendar)return false;
            // All manifests around every player were available a moment ago and the
            // players are still in the same chunks: nothing new to request.
            double now=Time.realtimeSinceStartup;long signature=TerrainNeedSignature();
            if(manifestReady.TryReuse(signature,now)){PhaseTimings.Count(PhaseTimings.TerrainCacheHits,1);return true;}
            bool ready=true;long start=PhaseTimings.Begin();int keys=0;
            foreach(var around in Map.genArounds)
            {
                Vector3d p=mainscript.GlobalFromUnityPos(around.upos);
                for(int i=0;i<Map.poiGens.Count;i++)if(Map.poiGens[i].isActiveAndEnabled)for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
                    {keys++;ready=Need(Key("p",i,0,mainscript.GetChunkPos(p,x,z,Map.poiGens[i].chunkSize))) && ready;}
                for(int i=0;i<Map.roadGens.Count;i++)for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
                {var road=Map.roadGens[i];if(!road.isActiveAndEnabled)continue;var chunk=mainscript.GetChunkPos(p,x,z,road.chunkSize);if(!road.hasSideRoad && road.hasMainRoad && chunk.x!=0)continue;keys++;ready=Need(Key("r",i,0,chunk)) && ready;}
            }
            PhaseTimings.Count(PhaseTimings.TerrainNeedKeys,keys);PhaseTimings.End(PhaseTimings.TerrainNeed,start);
            if(ready)manifestReady.StoreReady(signature,now);else manifestReady.Invalidate();
            return ready;
        }
        // The centre chunk of every active generator around every player decides all
        // nine neighbours, so this is enough to tell when the checked set changed.
        static long TerrainNeedSignature()
        {
            long hash=ManifestReadyCache.Seed;int around=0;
            foreach(var genAround in Map.genArounds)
            {
                Vector3d p=mainscript.GlobalFromUnityPos(genAround.upos);hash=ManifestReadyCache.Combine(hash,++around);
                for(int i=0;i<Map.poiGens.Count;i++)if(Map.poiGens[i].isActiveAndEnabled)
                {var c=mainscript.GetChunkPos(p,0,0,Map.poiGens[i].chunkSize);hash=ManifestReadyCache.Combine(ManifestReadyCache.Combine(ManifestReadyCache.Combine(hash,i),BitConverter.DoubleToInt64Bits(c.x)),BitConverter.DoubleToInt64Bits(c.z));}
                for(int i=0;i<Map.roadGens.Count;i++)if(Map.roadGens[i].isActiveAndEnabled)
                {var c=mainscript.GetChunkPos(p,0,0,Map.roadGens[i].chunkSize);hash=ManifestReadyCache.Combine(ManifestReadyCache.Combine(ManifestReadyCache.Combine(hash,1000+i),BitConverter.DoubleToInt64Bits(c.x)),BitConverter.DoubleToInt64Bits(c.z));}
            }
            return hash;
        }
        static void LimitTerrainDistance(terrainGenScript terrain)
        {
            // Native FStart maps the slider through the scene's min/max radius.
            // Keep a capped range installed before Restart/FStart consumes it, so
            // one slider change produces one correctly sized rebuild. Collision
            // grids are pinned separately and never rendered.
            if(terrain==null || terrain.hasCollider || settingsscript.s==null || lastTerrainDistance==null)return;
            float requested=settingsscript.s.S.FDrawDistance;
            if(!(requested>0f))return;
            TerrainDistancePolicy.ClampState state;
            if(!clampStates.TryGetValue(terrain,out state)){state=new TerrainDistancePolicy.ClampState();clampStates[terrain]=state;}
            float observed;try{observed=(float)lastTerrainDistance.GetValue(terrain);}catch{return;}
            var max=terrain.maxDrawDist;var min=terrain.minDrawDist;
            var plan=TerrainDistancePolicy.Prepare(min.drawDistance,max.drawDistance,requested,observed,state);
            if(plan.KeepCurrentClamp)return;
            if(plan.RestoreOriginal)
            {
                terrainGenScript.drawDistSetting saved;
                if(clampedMax.TryGetValue(terrain,out saved)){terrain.maxDrawDist=saved;clampedMax.Remove(terrain);}
                if(clampedMin.TryGetValue(terrain,out saved)){terrain.minDrawDist=saved;clampedMin.Remove(terrain);}
                max=terrain.maxDrawDist;min=terrain.minDrawDist;
            }
            if(!plan.Clamp)return;

            // Pin only the visual radii. Tile size and resolution remain authored
            // so a distance change does not silently change mesh quality.
            if(!clampedMax.ContainsKey(terrain))clampedMax[terrain]=max;
            if(!clampedMin.ContainsKey(terrain))clampedMin[terrain]=min;
            max.drawDistance=plan.Max;min.drawDistance=plan.Min;
            terrain.maxDrawDist=max;terrain.minDrawDist=min;
            if(plan.ForceRestart)lastTerrainDistance.SetValue(terrain,requested-1f);
            if(clampLogged.Add(terrain))
                StableGameBridge.JoinDiagnostic("Terrain range clamped to draw distance: name="+terrain.terrainName+
                    " requested="+requested.ToString("0",CultureInfo.InvariantCulture)+
                    " requestedRadius="+plan.EffectiveRadius.ToString("0",CultureInfo.InvariantCulture));
        }
        public static void ProtectColliderGrid(terrainGenScript terrain)
        {
            LimitTerrainDistance(terrain);
            // Stock Upd restarts the entire generator when view distance changes,
            // destroying every tile/collider before replacements are available.
            // Keep the collision grid's session configuration; separate distant
            // visual generators can rebuild for the new horizon independently.
            if(terrain==null || !terrain.hasCollider || terrain.terrains==null || terrain.terrains.Count==0 || settingsscript.s==null || lastTerrainDistance==null)return;
            float requested=settingsscript.s.S.FDrawDistance;
            float observed;if(!colliderDistances.TryGetValue(terrain,out observed))observed=(float)lastTerrainDistance.GetValue(terrain);
            if(observed!=requested)
            {lastTerrainDistance.SetValue(terrain,requested);StableGameBridge.JoinDiagnostic("View distance changed; retained live terrain collision grid.");}
            colliderDistances[terrain]=requested;
        }

        static void GenerateRoadData(roadGenScript road,Vector3d pos,bool neighbors)
        {
            // Upd(pos) invokes UpdChunksToRemove and UpdPlaceRemove, unloading the
            // host's live roads while answering a remote generation request. Only
            // run the data stages, with independent queues and no removal work.
            var stampCells=new List<Vector3d>{pos};long before=RoadDependencyStamp(road,stampCells,true);
            var needed=road.chunksNeeded;var removing=road.chunksToRemove;
            try
            {
                road.chunksNeeded=new List<Vector3d>();road.chunksToRemove=new List<Vector3d>();
                int radius=neighbors?1:0;
                for(int x=-radius;x<=radius;x++)for(int z=-radius;z<=radius;z++)
                {var cell=mainscript.GetChunkPos(pos,x,z,road.chunkSize);if(!road.hasSideRoad && road.hasMainRoad && cell.x!=0)continue;road.chunksNeeded.Add(cell);}
                road.UpdChunks(false);road.ConnectChunks();road.UpdGenRoad(false);
            }
            finally{road.chunksNeeded=needed;road.chunksToRemove=removing;}
            if(DependencyChanged(before,RoadDependencyStamp(road,stampCells,true)))InvalidateProximityCaches();
            FlushProximityCaches();
        }
        static string lastReadyDiagnostic;static double nextReadyDiagnostic;
        static bool ReportReady(bool ready,string reason)
        {
            string state="CanEnterWorld="+ready+" reason="+reason+" chunks="+available.Count+" calendar="+HasCalendar+" "+StableGameBridge.MapDiagnostic();
            if(state!=lastReadyDiagnostic || Time.realtimeSinceStartup>=nextReadyDiagnostic)
            {lastReadyDiagnostic=state;nextReadyDiagnostic=Time.realtimeSinceStartup+5;StableGameBridge.JoinDiagnostic(state);}
            return ready;
        }
        static bool Ready()
        {
            if(Map==null)return ReportReady(false,"no map");
            if(!Terrain(null))return ReportReady(false,"waiting road/POI manifests");
            if(Map.terrainGen!=null)foreach(var t in Map.terrainGen)if(t!=null && t.isActiveAndEnabled && t.hasCollider)
            {
                if(mainscript.s==null || mainscript.s.player==null || t.terrains==null)return ReportReady(false,"waiting local terrain collider");
                Vector3 spawn;
                if(!StableGameBridge.TryGetProtectedSpawn(out spawn))spawn=mainscript.s.player.transform.position;
                var center=mainscript.GetChunkPos(mainscript.GlobalFromUnityPos(spawn),t.tileSize);
                newTerrainTileScript tile;
                if(!t.terrains.TryGetValue(center,out tile) || tile==null || tile.mc==null || !tile.mc.enabled || tile.mc.sharedMesh==null)
                    return ReportReady(false,"waiting local terrain collider");
            }
            return ReportReady(true,"world built");
        }
        static byte[] Provide(ulong peer,string key)
        {
            if(Map==null)return null;string[] parts=key.Split('|');int gen,category;double x,z;
            if(parts.Length!=5 || !Int32.TryParse(parts[1],out gen) || !Int32.TryParse(parts[2],out category) || !Double.TryParse(parts[3],NumberStyles.Float,CultureInfo.InvariantCulture,out x) || !Double.TryParse(parts[4],NumberStyles.Float,CultureInfo.InvariantCulture,out z) || !Numbers.Finite(x) || !Numbers.Finite(z) || Math.Abs(x)>1e8 || Math.Abs(z)>1e8)return null;
            Vector3d pos=new Vector3d(x,0,z);var result=new Chunk {Key=key,Position=pos,Seed=Map.seed};
            if(parts[0]=="r")
            {
                if(gen<0 || gen>=Map.roadGens.Count)return null;var road=Map.roadGens[gen];if(!SamePosition(mainscript.GetChunkPos(pos,road.chunkSize),pos))return null;
                long generateStart=PhaseTimings.Begin();
                GenerateRoadData(road,pos,false);PhaseTimings.End(PhaseTimings.RoadGenerate,generateStart,key);
                roadGenScript.dotChunkClass c;if(!road.chunks.TryGetValue(pos,out c))return null;
                long copyStart=PhaseTimings.Begin();result.Road=CopyRoad(c);PhaseTimings.End(PhaseTimings.RoadCopy,copyStart,key);
            }
            else if(parts[0]=="p")
            {
                if(gen<0 || gen>=Map.poiGens.Count)return null;var script=Map.poiGens[gen];if(!SamePosition(mainscript.GetChunkPos(pos,script.chunkSize),pos))return null;
                long depsStart=PhaseTimings.Begin();
                if(script.roadgen!=null)GenerateRoadData(script.roadgen,pos,true);
                PhaseTimings.End(PhaseTimings.PoiRoadDeps,depsStart,key);long generateStart=PhaseTimings.Begin();
                script.GenChunk(pos);var old=new List<Vector3d>(script.chunksNeeded);try{script.chunksNeeded=new List<Vector3d>{pos};script.AddPois();}finally{script.chunksNeeded=old;}
                PhaseTimings.End(PhaseTimings.PoiGenerate,generateStart,key);
                poiGenScript.chunkClass c;if(!script.chunks.TryGetValue(pos,out c))return null;
                long collectStart=PhaseTimings.Begin();
                bool complete=!forcedPoiChunks.Contains(key);
                if(!c.generated)
                {
                    complete=false;
                    if(c.poiToPlace==null){c.generated=true;c.poiToPlace=new List<poiGenScript.tempPoiClass>();forcedPoiChunks.Add(key);}
                }
                if(c.poiToPlace!=null && c.poiToPlace.Count>0)complete=false;
                result.PoisComplete=complete;
                foreach(var p in c.pois.Values)if(p.id>=0)result.Pois.Add(new Poi {ID=p.id,Pos=p.pos,Rot=p.rot,Aligned=p.alignedToRoad,State=p.pobj!=null || p.save!=null?new save_poi(p):null});
                PhaseTimings.End(PhaseTimings.PoiCollect,collectStart,key);
            }
            else if(parts[0]=="o")
            {
                if(gen<0 || gen>=Map.objGens.Count || category<0 || category>=Map.objGens[gen].categories.Count)return null;
                var cat=Map.objGens[gen].categories[category];var scratch=CreateManifestCategory(cat);Grid(scratch);
                if(!SamePosition(mainscript.Snap(mainscript.GetChunkPos(pos,scratch.currentChunkSize),scratch.gridSize),pos))return null;
                objGenScript.chunkClass c;Dictionary<Vector3d,int> biomeLookup=cat.biomes;bool fromHost=cat.generatedChunks.TryGetValue(pos,out c) && c!=null && c.generated;
                if(!fromHost)
                {
                    var cached=FindManifestCell(cat,pos,Map.seed);
                    if(cached!=null){c=cached.Data;biomeLookup=cached.Biomes;}
                }
                if(!fromHost && c==null)
                {
                    biomeLookup=scratch.biomes;
                    var oldForced=forced;var oldForcedChunk=forcedChunk;
                    forced=scratch;forcedChunk=pos;
                    long generateStart=PhaseTimings.Begin();
                    try
                    {
                        scratch.UpdGen(false);
                        if(!scratch.generatedChunks.TryGetValue(pos,out c) || c==null || !c.generated)return null;
                        CacheManifestCell(cat,pos,Map.seed,c,scratch.biomes);
                    }
                    finally{forced=oldForced;forcedChunk=oldForcedChunk;FlushProximityCaches();PhaseTimings.End(PhaseTimings.ObjGenerate,generateStart,key);}
                    PhaseTimings.Count(PhaseTimings.ObjGenerated,1);
                }
                else PhaseTimings.Count(PhaseTimings.ObjReused,1);
                long collectStart=PhaseTimings.Begin();
                foreach(var o in c.objs.Values)
                {
                    if(cat.saveDestroy && (o.placed && o.obj==null || IsTombstoned(cat,o.chunkID,o.gposID)))continue;
                    int biome;biomeLookup.TryGetValue(o.chunkID,out biome);
                    result.Objects.Add(new Decoration {Seed=o.seed,Prefab=o.prefabIndex,Biome=biome,Chunk=o.chunkID,PosID=o.posID,GPosID=o.gposID,Pos=o.gpos,Rot=o.rot,Scale=o.scale});
                }
                PhaseTimings.End(PhaseTimings.ObjCollect,collectStart,key);
            }
            else return null;
            long encodeStart=PhaseTimings.Begin();
            byte[] encoded=parts[0]=="r"?StableRoadCodec.Write(result):StableJson.Write(result,4*1024*1024);
            int phase=parts[0]=="r"?PhaseTimings.RoadEncode:parts[0]=="p"?PhaseTimings.PoiEncode:PhaseTimings.ObjEncode;
            PhaseTimings.End(phase,encodeStart,key);
            if(encoded!=null)PhaseTimings.Count(parts[0]=="r"?PhaseTimings.RoadBytes:parts[0]=="p"?PhaseTimings.PoiBytes:PhaseTimings.ObjBytes,encoded.Length);
            return encoded;
        }
        static Road CopyRoad(roadGenScript.dotChunkClass c)
        {
            var dto=new Road {Seed=c.seed,Inside=c.insideConnected,U=c.outsideUConnected,R=c.outsideRConnected,Main=c.mainRoad};
            foreach(var dot in c.dots)dto.Dots.Add(new Dot {Pos=dot.pos,Connected=dot.connectedDotIDsInSameChunk});
            foreach(var line in c.roads)
            {
                var l=new Line {Self=line.selfChunk,ID=line.selfID,C1=line.chunk1,C2=line.chunk2,I1=line.id1,I2=line.id2,Start=line.start,End=line.end,Min=line.terrainAlignMinDist,Max=line.terrainAlignMaxDist,Type=(int)line.roadType};
                foreach(var e in line.roadElements)l.Elements.Add(new Element {Biome=e.biome,Road=e.roadID,Bones=e.bonePos});dto.Lines.Add(l);
            }
            foreach(var i in c.innerChunks)dto.Inner.Add(new Inner {Pos=i.Key,Roads=i.Value.roads});return dto;
        }
        static void Apply(string key,byte[] bytes)
        {
            long start=PhaseTimings.Begin();
            try{ApplyChunk(key,bytes);}
            finally
            {
                if(bytes!=null)PhaseTimings.Count(PhaseTimings.ApplyBytes,bytes.Length);
                int phase=key!=null && key.StartsWith("r|",StringComparison.Ordinal)?PhaseTimings.ApplyRoad:key!=null && key.StartsWith("p|",StringComparison.Ordinal)?PhaseTimings.ApplyPoi:PhaseTimings.ApplyObj;
                PhaseTimings.End(phase,start,key);
            }
        }
        static void ApplyChunk(string key,byte[] bytes)
        {
            if(StableRoadCodec.HasMagic(bytes)&&!StableRoadCodec.HasHeader(bytes))throw new InvalidOperationException("Unsupported road payload version.");
            bool roadPayload=StableRoadCodec.HasHeader(bytes);
            if(roadPayload && (key==null || !key.StartsWith("r|",StringComparison.Ordinal)))throw new InvalidOperationException("Road payload received for a non-road request.");
            long decodeStart=PhaseTimings.Begin();
            var dto=roadPayload?StableRoadCodec.Read(bytes):StableJson.Read<Chunk>(bytes,4*1024*1024);PhaseTimings.End(PhaseTimings.ApplyDecode,decodeStart,key);
            if(dto==null || dto.Version!=1 || dto.Key!=key || Map==null || dto.Seed!=Map.seed)throw new InvalidOperationException("Generated chunk identity/seed mismatch.");
            var parts=key.Split('|');int gen=Int32.Parse(parts[1]),category=Int32.Parse(parts[2]);
            bool trackRoad=parts[0]=="r"&&gen>=0&&gen<Map.roadGens.Count;
            bool trackPoi=parts[0]=="p"&&gen>=0&&gen<Map.poiGens.Count;
            var dependencyCell=new List<Vector3d>{dto.Position};
            long dependencyBefore=trackRoad?RoadDependencyStamp(Map.roadGens[gen],dependencyCell,true):trackPoi?PoiDependencyStamp(Map.poiGens[gen],dependencyCell):0;
            if(parts[0]=="o")
            {
                if(gen<0||gen>=Map.objGens.Count||category<0||category>=Map.objGens[gen].categories.Count){available.Remove(key);return;}
                var current=Map.objGens[gen].categories[category];
                // A reply can outlive the ring that requested it. Dropping it here
                // prevents stale cells from reintroducing unbounded client metadata.
                if(!ShouldApplyDecoration(dto.Position,current.chunksNeeded)){available.Remove(key);return;}
                objGenScript.chunkClass existing;
                // Retransmitted or duplicate replies must not replace a chunk whose
                // object records may already point at live GameObjects.
                if(current.generatedChunks.TryGetValue(dto.Position,out existing)&&existing!=null&&existing.generated)
                {available.Add(key);return;}
            }
            if(parts[0]=="r")
            {
                var road=Map.roadGens[gen];var r=dto.Road;if(r==null)throw new InvalidOperationException("Missing road data.");
                var c=new roadGenScript.dotChunkClass {seed=r.Seed,insideConnected=r.Inside,outsideUConnected=r.U,outsideRConnected=r.R,innerChunkGenerated=true,mainRoad=r.Main};
                foreach(var d in r.Dots)c.dots.Add(new roadGenScript.dotClass(d.Pos){connectedDotIDsInSameChunk=d.Connected});c.dotCount=c.dots.Count;
                foreach(var l in r.Lines)
                {
                    var line=new roadGenScript.roadClass(l.C1,l.I1,l.C2,l.I2,l.Start,l.End,l.Min,l.Max,l.Self,l.ID,(roadGenScript.roadTypeEmu)l.Type){generated=true};
                    foreach(var e in l.Elements)line.roadElements.Add(new roadGenScript.roadElementClass {biome=e.Biome,roadID=e.Road,bonePos=e.Bones});c.roads.Add(line);
                }
                foreach(var i in r.Inner)c.innerChunks[i.Pos]=new roadGenScript.innerChunkClass {roads=i.Roads};road.chunks[dto.Position]=c;
            }
            else if(parts[0]=="p")
            {
                var script=Map.poiGens[gen];poiGenScript.chunkClass c;if(!script.chunks.TryGetValue(dto.Position,out c))script.chunks[dto.Position]=c=new poiGenScript.chunkClass();
                // A host manifest that was force-marked generated (or that still had
                // unplaced POIs) is not authoritative. Never delete local POIs from it.
                if(CanPrunePois(dto.PoisComplete,dto.Pois==null?0:dto.Pois.Count))
                {
                    var expected=new HashSet<Vector3d>();foreach(var p in dto.Pois)expected.Add(mainscript.Snap(p.Pos,script.round));
                    foreach(var p in new List<KeyValuePair<Vector3d,poiGenScript.poiClass>>(c.pois))
                        if(p.Value.id>=0 && SamePosition(mainscript.GetChunkPos(p.Value.pos,script.chunkSize),dto.Position) && !expected.Contains(p.Key))
                        {if(p.Value.pobj!=null)UnityEngine.Object.Destroy(p.Value.pobj.gameObject);c.pois.Remove(p.Key);}
                }
                foreach(var p in dto.Pois)
                {
                    Vector3d id=mainscript.Snap(p.Pos,script.round);poiGenScript.poiClass current;
                    if(c.pois.TryGetValue(id,out current) && (current.id!=p.ID || !SamePosition(current.pos,p.Pos) || current.rot!=p.Rot)){if(current.pobj!=null)UnityEngine.Object.Destroy(current.pobj.gameObject);c.pois.Remove(id);}
                    var placed=script.AddPoi(p.Pos,p.Rot,p.ID,p.Aligned);if(p.State!=null){p.State.Load(placed);if(placed.pobj!=null)p.State.Load(placed.pobj);}
                }
                c.generated=true;c.poiToPlace=new List<poiGenScript.tempPoiClass>();
                if(dto.PoisComplete){incompletePois.Remove(key);incompletePoiTries.Remove(key);}
                else incompletePois[key]=Time.realtimeSinceStartup+IncompletePoiRetry;
            }
            else
            {
                var cat=Map.objGens[gen].categories[category];var c=new objGenScript.chunkClass {generated=true};
                foreach(var o in dto.Objects)
                {
                    if(o.Prefab<0 || o.Prefab>=cat.prefabs.Count || !Numbers.Finite(o.Scale))throw new InvalidOperationException("Generated object prefab mismatch.");
                    List<Vector3d> destroyed;
                    if(cat.saveDestroy && cat.destroyed.TryGetValue(o.Chunk,out destroyed) && ContainsPosition(destroyed,o.GPosID))continue;
                    c.objs[o.GPosID]=new objGenScript.objClass {seed=o.Seed,prefabIndex=o.Prefab,chunkID=o.Chunk,posID=o.PosID,gposID=o.GPosID,gpos=o.Pos,rot=o.Rot,scale=o.Scale,place=true};cat.biomes[o.Chunk]=o.Biome;
                }
                cat.generatedChunks[dto.Position]=c;PlacementSkip.Touch(cat);
                int revision;placementRevision.TryGetValue(cat,out revision);placementRevision[cat]=revision+1;
            }
            if(trackRoad && DependencyChanged(dependencyBefore,RoadDependencyStamp(Map.roadGens[gen],dependencyCell,true)) ||
               trackPoi && DependencyChanged(dependencyBefore,PoiDependencyStamp(Map.poiGens[gen],dependencyCell)))InvalidateProximityCaches();
            FlushProximityCaches();
            available.Add(key);
        }
    }
    // Uses the framework already shipped with the game. Unity's native JSON
    // serializer silently omits helper-assembly DTO lists in the live player.
    // DataContractJsonSerializer preserves the declared serializable data graph;
    // it does not resolve arbitrary CLR type names from incoming JSON.
    public static class StableJson
    {
        static DataContractJsonSerializer Serializer<T>()
        {return new DataContractJsonSerializer(typeof(T),new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings {MaxItemsInObjectGraph=2000000});}
        public static byte[] Write<T>(T value){return Write(value,StableNode.MaxSnapshotBytes);}
        public static byte[] Write<T>(T value,int limit)
        {
            using(var json=new MemoryStream())
            {
                Serializer<T>().WriteObject(json,value);
                if(json.Length>limit)throw new InvalidOperationException("World JSON exceeds transfer limit.");
                using(var packed=new MemoryStream())
                {
                    var header=BitConverter.GetBytes((int)json.Length);packed.Write(header,0,4);
                    json.Position=0;using(var deflate=new DeflateStream(packed,CompressionMode.Compress,true))json.CopyTo(deflate);
                    return packed.ToArray();
                }
            }
        }
        public static T Read<T>(byte[] bytes){return Read<T>(bytes,StableNode.MaxSnapshotBytes);}
        public static T Read<T>(byte[] bytes,int limit)
        {
            if(bytes==null || bytes.Length<4)throw new InvalidOperationException("Truncated world payload.");
            int size=BitConverter.ToInt32(bytes,0);
            if(size<0 || size>limit)throw new InvalidOperationException("Expanded world payload exceeds transfer limit.");
            using(var input=new MemoryStream(bytes,4,bytes.Length-4,false))
            using(var deflate=new DeflateStream(input,CompressionMode.Decompress))
            using(var json=new MemoryStream())
            {
                var buffer=new byte[8192];int count;
                while((count=deflate.Read(buffer,0,buffer.Length))>0)
                {if(json.Length+count>size)throw new InvalidOperationException("Expanded world payload length mismatch.");json.Write(buffer,0,count);}
                if(json.Length!=size)throw new InvalidOperationException("Truncated expanded world payload.");
                json.Position=0;return (T)Serializer<T>().ReadObject(json);
            }
        }
    }
}
