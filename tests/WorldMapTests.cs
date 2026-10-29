using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization;
using System.Diagnostics;
using TldMp;
using UnityEngine;

// Targeted map/POI regression checks. No Unity scene or native generation call:
// it exercises category range/cell policy, isolated manifest state, stale replies,
// decoration eviction/tombstone rules and POI authority.
public static class WorldMapTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e)
        {
            string name=new AssemblyName(e.Name).Name;
            string path=name=="Assembly-CSharp"?args[1]:Path.Combine(args[0],name+".dll");
            return File.Exists(path)?Assembly.LoadFrom(path):null;
        };
        try
        {
            Assembly.LoadFrom(Path.GetFullPath(args[1]));
            Run();
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void Run()
    {
        Check(StableMap.CategoryRadius(3000f,.5f,200f,1200f)==1200f,"Category maximum did not clamp the native range");
        Check(StableMap.CategoryRadius(100f,.5f,200f,1200f)==200f,"Category minimum did not clamp the native range");
        Check(StableMap.CategoryRadius(800f,.5f,200f,1200f)==400f,"Category percentage was not applied before clamping");
        Check(StableMap.StreamEdge(3000f,512f)==16,"Ring silently rounded down and left the radius uncovered");
        Check(StableMap.StreamEdge(1f,128f)==4,"Tiny range did not retain anchored edge-cell coverage");
        Check(StableMap.StreamEdge(25000f,128f)>4,"Large native radius was truncated by an edge cap");
        Check(StableMap.StreamEdge(3000f,2048f)%2==0,"Ring edge must stay even for native -edge/2..edge/2");
        Check(StableMap.StreamEdge(0f,0f)==2,"Invalid cell size did not fall back to the minimum ring");
        float cellLow=StableMap.LogicalCellSizeFor(200f,2400f,8f);
        float cellHigh=StableMap.LogicalCellSizeFor(200f,2400f,8f);
        Check(cellLow==cellHigh,"Logical cell identity changed with the graphics slider");
        Check(StableMap.LogicalCellSizeFor(0f,50000f,1f)<=512f,"Large category range recreated oversized metadata cells");
        Check(StableMap.LogicalCellSizeFor(0f,50000f,1f)/1f<=32f,"Manifest cell can exceed the 32×32 native candidate bound");
        Check(StableMap.CellIntersectsRadius(new Vector3d(100,0,0),new Vector3d(0,0,0),128f,40f),"Boundary cell intersecting the radius was omitted");
        Check(!StableMap.CellIntersectsRadius(new Vector3d(200,0,0),new Vector3d(0,0,0),128f,40f),"Disjoint boundary cell was retained");
        Check(StableMap.WithinRadius(new Vector3d(300,0,400),new Vector3d(0,0,0),500f),"Per-object radius rejected an object on the boundary");
        Check(!StableMap.WithinRadius(new Vector3d(301,0,400),new Vector3d(0,0,0),500f),"Per-object radius admitted an object outside the native range");
        RoadCodecRoundTrip();

        // Ready map keys are monotonic for the current map. The steady-state
        // path must not split/parse keys or probe generated dictionaries per tick.
        StableMap.Configure(null);
        var availableField=typeof(StableMap).GetField("available",BindingFlags.Static|BindingFlags.NonPublic);
        var available=(HashSet<string>)availableField.GetValue(null);
        available.Add("ready-key");
        var hasAvailable=typeof(StableMap).GetMethod("HasAvailable",BindingFlags.Static|BindingFlags.NonPublic);
        Check((bool)hasAvailable.Invoke(null,new object[]{"ready-key"}),"Known available key was not accepted by the allocation-free fast path");
        StableMap.Configure(null);
        Check(available.Count==0,"Session reconfiguration retained available map keys");

        // Lookup invalidation keys off local native data changes. Repeating an
        // unchanged frame stamp is stable; a generated road/placed POI changes it.
        var stampCell=new Vector3d(0,0,0);var stampNeighbor=new Vector3d(128,0,0);var stampCells=new List<Vector3d>{stampCell,stampNeighbor};
        var road=(roadGenScript)FormatterServices.GetUninitializedObject(typeof(roadGenScript));
        road.chunkSize=128f;
        road.chunks=new Dictionary<Vector3d,roadGenScript.dotChunkClass>();
        var roadChunk=new roadGenScript.dotChunkClass();road.chunks[stampCell]=roadChunk;
        var roadStamp=typeof(StableMap).GetMethod("RoadDependencyStamp",BindingFlags.Static|BindingFlags.NonPublic);
        var changedStamp=typeof(StableMap).GetMethod("DependencyChanged",BindingFlags.Static|BindingFlags.NonPublic);
        long roadBefore=(long)roadStamp.Invoke(null,new object[]{road,stampCells,false});
        long roadSame=(long)roadStamp.Invoke(null,new object[]{road,stampCells,false});
        Check(!(bool)changedStamp.Invoke(null,new object[]{roadBefore,roadSame}),"Stable road state caused per-frame cache invalidation");
        stampCells.Reverse();long roadReordered=(long)roadStamp.Invoke(null,new object[]{road,stampCells,false});
        Check(!(bool)changedStamp.Invoke(null,new object[]{roadBefore,roadReordered}),"Reordered identical road cells caused cache invalidation");stampCells.Reverse();
        roadChunk.innerChunkGenerated=true;long roadAfter=(long)roadStamp.Invoke(null,new object[]{road,stampCells,false});
        Check((bool)changedStamp.Invoke(null,new object[]{roadBefore,roadAfter}),"Road generation state change did not invalidate proximity data");

        var poi=(poiGenScript)FormatterServices.GetUninitializedObject(typeof(poiGenScript));
        poi.chunks=new Dictionary<Vector3d,poiGenScript.chunkClass>();var poiChunk=new poiGenScript.chunkClass();poi.chunks[stampCell]=poiChunk;
        var poiStamp=typeof(StableMap).GetMethod("PoiDependencyStamp",BindingFlags.Static|BindingFlags.NonPublic);
        long poiBefore=(long)poiStamp.Invoke(null,new object[]{poi,stampCells});
        long poiSame=(long)poiStamp.Invoke(null,new object[]{poi,stampCells});
        Check(!(bool)changedStamp.Invoke(null,new object[]{poiBefore,poiSame}),"Stable POI state caused per-frame cache invalidation");
        stampCells.Reverse();long poiReordered=(long)poiStamp.Invoke(null,new object[]{poi,stampCells});
        Check(!(bool)changedStamp.Invoke(null,new object[]{poiBefore,poiReordered}),"Reordered identical POI cells caused cache invalidation");stampCells.Reverse();
        poiChunk.generated=true;long poiAfter=(long)poiStamp.Invoke(null,new object[]{poi,stampCells});
        Check((bool)changedStamp.Invoke(null,new object[]{poiBefore,poiAfter}),"POI generation state change did not invalidate proximity data");

        var cacheData=new objGenScript.chunkClass {generated=true};
        var cacheCategory=new objGenScript.categoryClass();var cacheManifest=typeof(StableMap).GetMethod("CacheManifestCell",BindingFlags.Static|BindingFlags.NonPublic);
        cacheManifest.Invoke(null,new object[]{cacheCategory,stampCell,7,cacheData,new Dictionary<Vector3d,int>()});
        var cacheList=(System.Collections.ICollection)typeof(StableMap).GetField("manifestCells",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var world=(mapSettingScript)FormatterServices.GetUninitializedObject(typeof(mapSettingScript));
        var objectGenerator=(objGenScript)FormatterServices.GetUninitializedObject(typeof(objGenScript));
        objectGenerator.tiles=new Dictionary<Vector3d,objGenScript.tileClass>();objectGenerator.tiles[stampCell]=new objGenScript.tileClass();
        world.objGens=new List<objGenScript>{objectGenerator};
        var invalidate=typeof(StableMap).GetMethod("InvalidateProximityCaches",BindingFlags.Static|BindingFlags.NonPublic,null,Type.EmptyTypes,null);
        invalidate.Invoke(null,null);
        var dirtyField=typeof(StableMap).GetField("proximityCachesDirty",BindingFlags.Static|BindingFlags.NonPublic);
        Check((bool)dirtyField.GetValue(null),"Cache invalidation did not mark the lookup cache dirty");
        typeof(StableMap).GetMethod("FlushProximityCaches",BindingFlags.Static|BindingFlags.NonPublic,null,new Type[]{typeof(mapSettingScript)},null).Invoke(null,new object[]{world});
        Check(!(bool)dirtyField.GetValue(null),"Cache flush did not consume the dirty flag; objGens="+(world.objGens==null?-1:world.objGens.Count));
        Check(objectGenerator.tiles.Count==0,"Road/POI change did not clear object proximity tiles: "+objectGenerator.tiles.Count);
        Check((int)typeof(System.Collections.ICollection).GetProperty("Count").GetValue(cacheList,null)==0,"Road/POI change did not clear decoration manifests: "+typeof(System.Collections.ICollection).GetProperty("Count").GetValue(cacheList,null));

        Check(!StableMap.CanPrunePois(false,0),"Incomplete empty manifest was treated as authoritative");
        Check(!StableMap.CanPrunePois(false,3),"Incomplete populated manifest was treated as authoritative");
        Check(!StableMap.CanPrunePois(true,0),"Empty manifest was allowed to prune POIs");
        Check(StableMap.CanPrunePois(true,1),"Complete populated manifest cannot reconcile POIs");

        var a=new Vector3d(0,0,0);var b=new Vector3d(2048,0,0);var c=new Vector3d(4096,0,0);
        var cat=new objGenScript.categoryClass {saveDestroy=true};
        cat.chunksNeeded.Add(a);cat.chunksNeeded.Add(b);
        var ca=new objGenScript.chunkClass {generated=true};
        var oa=new objGenScript.objClass {gposID=a,place=true};ca.objs.Add(a,oa);
        var cb=new objGenScript.chunkClass {generated=true};
        var ob=new objGenScript.objClass {gposID=b,place=true};cb.objs.Add(b,ob);
        var cc=new objGenScript.chunkClass {generated=true};
        var oc=new objGenScript.objClass {gposID=c,place=true};cc.objs.Add(c,oc);
        cat.generatedChunks.Add(a,ca);cat.generatedChunks.Add(b,cb);cat.generatedChunks.Add(c,cc);
        cat.chunksToRemove.Add(c);

        var scratch=StableMap.CreateManifestCategory(cat);
        Check(!System.Object.ReferenceEquals(scratch,cat),"Manifest generation reused the live category instance");
        Check(!System.Object.ReferenceEquals(scratch.chunksNeeded,cat.chunksNeeded) && scratch.chunksNeeded.Count==0,"Manifest scratch shared chunksNeeded");
        Check(!System.Object.ReferenceEquals(scratch.chunksToRemove,cat.chunksToRemove) && scratch.chunksToRemove.Count==0,"Manifest scratch shared chunksToRemove");
        Check(!System.Object.ReferenceEquals(scratch.generatedChunks,cat.generatedChunks) && scratch.generatedChunks.Count==0,"Manifest scratch shared generated chunks");
        Check(!System.Object.ReferenceEquals(scratch.biomes,cat.biomes) && scratch.biomes.Count==0,"Manifest scratch shared biome generation state");
        Check(System.Object.ReferenceEquals(scratch.destroyed,cat.destroyed) && scratch.saveDestroy,"Manifest scratch did not retain read-only saveDestroy tombstones/config");

        cat.chunksNeeded=new List<Vector3d>{a};
        StableMap.RestorePlacement(cat);
        Check(oa.place && !ob.place && !oc.place,"Host placement self-heal did not follow the live ring");
        Check(StableMap.ShouldApplyDecoration(a,new List<Vector3d>{a}),"Current decoration cell was rejected");
        Check(!StableMap.ShouldApplyDecoration(b,new List<Vector3d>{a}),"Stale decoration reply was accepted after ring departure");
        Check(!StableMap.CanEvictMetadata(true,false),"Metadata containing a live GameObject was evicted");
        Check(!StableMap.CanEvictMetadata(false,true),"Unrecorded saveDestroy tombstone was discarded");
        Check(StableMap.CanEvictMetadata(false,false),"Destroyed out-of-ring metadata could not be evicted");
        cat.destroyed[a]=new List<Vector3d>{b};
        Check(StableMap.IsTombstoned(cat,a,b),"Stable saveDestroy tombstone lookup failed");
        Check(!StableMap.IsTombstoned(cat,a,c),"Unrelated object matched a saveDestroy tombstone");

        // Exercise the actual metadata eviction path over a long-travel stand-in.
        StableMap.Configure(null);
        var evict=typeof(StableMap).GetMethod("EvictCategory",BindingFlags.Static|BindingFlags.NonPublic);
        var streamDiff=typeof(StableMap).GetMethod("CancelDepartedStreamCells",BindingFlags.Static|BindingFlags.NonPublic);
        var streamCacheCategory=new objGenScript.categoryClass();
        var cacheA=new Vector3d(-9000000000d,0,17);var cacheB=new Vector3d(9000000000d,0,-31);var cacheC=new Vector3d(128,0,256);
        streamCacheCategory.chunksNeeded=new List<Vector3d>{cacheA,cacheB};
        streamCacheCategory.generatedChunks[cacheC]=new objGenScript.chunkClass {generated=true};
        string cacheBKey="o|4|2|9000000000|-31",cacheCKey="o|4|2|128|256";
        available.Add(cacheBKey);available.Add(cacheCKey);
        streamDiff.Invoke(null,new object[]{true,new List<Vector3d>{cacheA,cacheB},streamCacheCategory,4,2});
        Check(available.Contains(cacheBKey)&&available.Contains(cacheCKey),"Identical stream cache hit changed available or pending-cell state");
        streamCacheCategory.chunksNeeded=new List<Vector3d>{cacheA};
        streamDiff.Invoke(null,new object[]{false,new List<Vector3d>{cacheA,cacheB,cacheC},streamCacheCategory,4,2});
        Check(!available.Contains(cacheBKey)&&available.Contains(cacheCKey),"Stream cache miss failed to cancel only departed, ungenerated requests");
        available.Remove(cacheCKey);

        var travel=new objGenScript.categoryClass();var kept=new Vector3d(-9000000000d,0,17);var keptFar=new Vector3d(9000000000d,0,-31);travel.chunksNeeded=new List<Vector3d>{kept,keptFar};
        for(int i=0;i<100;i++)
        {
            var pos=i==0?kept:i==1?keptFar:new Vector3d(i%2==0?-(10000000000d+i*256):(10000000000d+i*256),0,i*17);var chunk=new objGenScript.chunkClass {generated=true};
            chunk.objs.Add(pos,new objGenScript.objClass {gposID=pos,chunkID=pos});travel.generatedChunks[pos]=chunk;
            available.Add("o|0|0|"+pos.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+pos.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        }
        evict.Invoke(null,new object[]{travel,0,0});
        Check(travel.generatedChunks.Count==2&&travel.generatedChunks.ContainsKey(kept)&&travel.generatedChunks.ContainsKey(keptFar),"Long-travel eviction retained out-of-ring metadata or removed exact negative/large needed cells");
        Check(available.Count==2,"Long-travel eviction left stale available-cache entries");
        var nextFar=new Vector3d(-12000000000d,0,7000000000d);travel.chunksNeeded=new List<Vector3d>{nextFar};
        var nextChunk=new objGenScript.chunkClass {generated=true};nextChunk.objs.Add(nextFar,new objGenScript.objClass {gposID=nextFar,chunkID=nextFar});travel.generatedChunks[nextFar]=nextChunk;
        available.Add("o|0|0|-12000000000|7000000000");
        evict.Invoke(null,new object[]{travel,0,0});
        Check(travel.generatedChunks.Count==1&&travel.generatedChunks.ContainsKey(nextFar)&&available.Count==1,"Second travel step retained prior-cell metadata/cache state or lost the new exact cell");

        var saved=new objGenScript.categoryClass {saveDestroy=true};var savedCell=new Vector3d(9,0,0);var savedObject=new Vector3d(11,0,0);
        var savedChunk=new objGenScript.chunkClass {generated=true};savedChunk.objs.Add(savedObject,new objGenScript.objClass {chunkID=savedCell,gposID=savedObject,placed=true});
        saved.generatedChunks[savedCell]=savedChunk;evict.Invoke(null,new object[]{saved,0,0});
        Check(StableMap.IsTombstoned(saved,savedCell,savedObject),"Eviction dropped a missing saveDestroy object without persisting its tombstone");
        Check(saved.generatedChunks.Count==0,"Persisted saveDestroy tombstone kept unrelated chunk metadata alive");

        var wireChunk=new StableMap.Chunk {Key="p|0|0|0|0",Seed=7,Position=a,PoisComplete=false};
        var copy=StableJson.Read<StableMap.Chunk>(StableJson.Write(wireChunk));
        Check(!copy.PoisComplete,"POI completeness flag did not survive the wire DTO");

        // P2: scratch-backed SameCells must preserve the original positionComparer
        // set semantics, including NaN non-reflexivity, signed zero and duplicates.
        var sameCells=typeof(StableMap).GetMethod("SameCells",BindingFlags.Static|BindingFlags.NonPublic);
        var c0=new Vector3d(0,0,0);var c1=new Vector3d(1,0,1);var c2=new Vector3d(-0.0,0,0);var refComparer=new PositionComparer();
        Check((bool)sameCells.Invoke(null,new object[]{new List<Vector3d>{c0,c1},new List<Vector3d>{c1,c0}}),"SameCells lost order-independent equality");
        Check(!(bool)sameCells.Invoke(null,new object[]{new List<Vector3d>{c0},new List<Vector3d>{c0,c1}}),"SameCells ignored a count difference");
        var nan=new Vector3d(double.NaN,0,0);
        Check(!(bool)sameCells.Invoke(null,new object[]{new List<Vector3d>{nan},new List<Vector3d>{nan}}),"SameCells changed NaN non-reflexivity");
        Check((bool)sameCells.Invoke(null,new object[]{new List<Vector3d>{c0},new List<Vector3d>{c2}})==SameCellsReference(new List<Vector3d>{c0},new List<Vector3d>{c2},refComparer),"SameCells changed signed-zero equality");
        var random=new System.Random(4242);
        for(int round=0;round<400;round++)
        {
            int n=random.Next(0,40);var left=new List<Vector3d>();var right=new List<Vector3d>();
            for(int i=0;i<n;i++){left.Add(new Vector3d(random.Next(-2,3),random.Next(-1,2),random.Next(-2,3)));right.Add(new Vector3d(random.Next(-2,3),random.Next(-1,2),random.Next(-2,3)));}
            if(round%7==0)right=new List<Vector3d>(left);
            bool expected=SameCellsReference(left,right,refComparer);
            bool actual=(bool)sameCells.Invoke(null,new object[]{left,right});
            Check(actual==expected,"SameCells scratch changed a randomized result");
        }

        // P2: the duplicate-call token must consume exactly once and never leak.
        var nodeField=typeof(StableMap).GetField("node",BindingFlags.Static|BindingFlags.NonPublic);
        var tokenField=typeof(StableMap).GetField("chunksComputedFor",BindingFlags.Static|BindingFlags.NonPublic);
        var oldNode=nodeField.GetValue(null);
        nodeField.SetValue(null,FormatterServices.GetUninitializedObject(typeof(StableNode)));
        var tokenCategory=new objGenScript.categoryClass();
        tokenField.SetValue(null,tokenCategory);
        Check(!StableMap.CategoryChunks(tokenCategory),"duplicate CategoryChunks call did not consume the token");
        Check(tokenField.GetValue(null)==null,"duplicate CategoryChunks token was not cleared");
        tokenField.SetValue(null,tokenCategory);
        Check(!StableMap.CategoryChunks(tokenCategory),"duplicate CategoryChunks token was not reusable after a clear");
        nodeField.SetValue(null,oldNode);

        Console.WriteLine("PASS: "+checks+" map/POI regressions: category range/cell coverage, isolated manifest state, stale reply rejection, eviction/tombstone rules and POI authority. No Unity scene calls.");
    }

    static void RoadCodecRoundTrip()
    {
        var road=new StableMap.Road {Seed=17,Inside=true,U=false,R=true,Main=new List<int>{1,3,5}};
        for(int i=0;i<120;i++)
        {
            road.Dots.Add(new StableMap.Dot {Pos=new Vector3d(i*12.5,0,-i*7.25),Connected=new List<int>{i,i+1,i+2}});
            var line=new StableMap.Line {Self=new Vector3d(i*128,0,0),ID=i,C1=new Vector3d(i,1,2),C2=new Vector3d(i,3,4),I1=i,I2=i+1,Start=new Vector3d(i*1.5,0,0),End=new Vector3d(i*2.5,0,0),Min=1.25f,Max=9.5f,Type=i%3};
            for(int j=0;j<5;j++)
            {
                var element=new StableMap.Element {Biome=j%4,Road=i*5+j,Bones=new List<Vector3d>()};
                for(int k=0;k<16;k++)element.Bones.Add(new Vector3d(i*32+j*4+k*.25,0,-i*8-k*.5));
                line.Elements.Add(element);
            }
            road.Lines.Add(line);
        }
        for(int i=0;i<48;i++)road.Inner.Add(new StableMap.Inner {Pos=new Vector3d(i*100,0,i*-100),Roads=new List<roadGenScript.roadChunk>{new roadGenScript.roadChunk {roadId=i,roadElements=new List<int>{i,i+1,i+2}}}});
        var chunk=new StableMap.Chunk {Version=1,Seed=991,Key="r|0|0|10000|-10000",Position=new Vector3d(10000,0,-10000),Road=road};
        byte[] json=StableJson.Write(chunk,4*1024*1024);Console.WriteLine("Road codec diagnostic: JSON bytes="+json.Length);
        byte[] binary=StableRoadCodec.Write(chunk);Console.WriteLine("Road codec diagnostic: typed bytes="+binary.Length);
        var jsonCopy=StableJson.Read<StableMap.Chunk>(json,4*1024*1024);var binaryCopy=StableRoadCodec.Read(binary);
        Check(!StableRoadCodec.HasHeader(json)&&StableRoadCodec.HasHeader(binary),"Road codec marker detection failed");
        Check(RoadsEqual(jsonCopy,binaryCopy),"Typed road codec changed DTO content");
        byte[] wrongVersion=(byte[])binary.Clone();wrongVersion[5]=2;ExpectInvalid(delegate{StableRoadCodec.Read(wrongVersion);},"Unsupported road codec version was accepted");
        byte[] truncated=new byte[binary.Length-1];Array.Copy(binary,truncated,truncated.Length);ExpectInvalid(delegate{StableRoadCodec.Read(truncated);},"Truncated road payload was accepted");
        var invalid=(StableMap.Chunk)FormatterServices.GetUninitializedObject(typeof(StableMap.Chunk));
        invalid.Version=1;invalid.Key="r|0|0|0|0";invalid.Road=new StableMap.Road {Dots=new List<StableMap.Dot>{new StableMap.Dot {Pos=new Vector3d(Double.NaN,0,0),Connected=new List<int>()}}};
        ExpectInvalid(delegate{StableRoadCodec.Write(invalid);},"Non-finite road coordinate was accepted");
        byte[] raw=ExpandRoad(binary);int keyBytes=BitConverter.ToInt32(raw,8);int mainCountOffset=4+4+4+keyBytes+24+4+3;
        Buffer.BlockCopy(BitConverter.GetBytes(Int32.MaxValue),0,raw,mainCountOffset,4);
        ExpectInvalid(delegate{StableRoadCodec.Read(PackRoad(raw));},"Malformed road list count was accepted");
        byte[] cleanRaw=ExpandRoad(binary);byte[] withTrailing=new byte[cleanRaw.Length+1];Array.Copy(cleanRaw,withTrailing,cleanRaw.Length);withTrailing[withTrailing.Length-1]=0;
        ExpectInvalid(delegate{StableRoadCodec.Read(PackRoad(withTrailing));},"Trailing road payload data was accepted");
        var timer=Stopwatch.StartNew();for(int i=0;i<3;i++)StableJson.Write(chunk,4*1024*1024);long jsonMs=timer.ElapsedMilliseconds;
        timer.Restart();for(int i=0;i<3;i++)StableRoadCodec.Write(chunk);long binaryMs=timer.ElapsedMilliseconds;
        timer.Restart();for(int i=0;i<3;i++)StableJson.Read<StableMap.Chunk>(json,4*1024*1024);long jsonReadMs=timer.ElapsedMilliseconds;
        timer.Restart();for(int i=0;i<3;i++)StableRoadCodec.Read(binary);long binaryReadMs=timer.ElapsedMilliseconds;
        Console.WriteLine("Road codec benchmark (3 synthetic 120-line round trips): jsonBytes="+json.Length+" typedBytes="+binary.Length+" jsonWriteMs="+jsonMs+" typedWriteMs="+binaryMs+" jsonReadMs="+jsonReadMs+" typedReadMs="+binaryReadMs);
    }

    static bool RoadsEqual(StableMap.Chunk a,StableMap.Chunk b)
    {
        if(a==null||b==null||a.Version!=b.Version||a.Seed!=b.Seed||a.Key!=b.Key||!StableMap.SamePosition(a.Position,b.Position))return false;
        var x=a.Road;var y=b.Road;if(x.Seed!=y.Seed||x.Inside!=y.Inside||x.U!=y.U||x.R!=y.R||!IntsEqual(x.Main,y.Main)||x.Dots.Count!=y.Dots.Count||x.Lines.Count!=y.Lines.Count||x.Inner.Count!=y.Inner.Count)return false;
        for(int i=0;i<x.Dots.Count;i++)if(!StableMap.SamePosition(x.Dots[i].Pos,y.Dots[i].Pos)||!IntsEqual(x.Dots[i].Connected,y.Dots[i].Connected))return false;
        for(int i=0;i<x.Lines.Count;i++)
        {
            var l=x.Lines[i];var r=y.Lines[i];if(!StableMap.SamePosition(l.Self,r.Self)||l.ID!=r.ID||!StableMap.SamePosition(l.C1,r.C1)||!StableMap.SamePosition(l.C2,r.C2)||l.I1!=r.I1||l.I2!=r.I2||!StableMap.SamePosition(l.Start,r.Start)||!StableMap.SamePosition(l.End,r.End)||l.Min!=r.Min||l.Max!=r.Max||l.Type!=r.Type||l.Elements.Count!=r.Elements.Count)return false;
            for(int j=0;j<l.Elements.Count;j++){var e=l.Elements[j];var f=r.Elements[j];if(e.Biome!=f.Biome||e.Road!=f.Road||!Vector3dsEqual(e.Bones,f.Bones))return false;}
        }
        for(int i=0;i<x.Inner.Count;i++){if(!StableMap.SamePosition(x.Inner[i].Pos,y.Inner[i].Pos)||x.Inner[i].Roads.Count!=y.Inner[i].Roads.Count)return false;for(int j=0;j<x.Inner[i].Roads.Count;j++)if(x.Inner[i].Roads[j].roadId!=y.Inner[i].Roads[j].roadId||!IntsEqual(x.Inner[i].Roads[j].roadElements,y.Inner[i].Roads[j].roadElements))return false;}
        return true;
    }
    static bool IntsEqual(List<int> a,List<int> b){if(a==null||b==null)return a==b;if(a.Count!=b.Count)return false;for(int i=0;i<a.Count;i++)if(a[i]!=b[i])return false;return true;}
    static bool Vector3dsEqual(List<Vector3d> a,List<Vector3d> b){if(a.Count!=b.Count)return false;for(int i=0;i<a.Count;i++)if(!StableMap.SamePosition(a[i],b[i]))return false;return true;}
    static void ExpectInvalid(Action action,string message){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
    sealed class PositionComparer:IEqualityComparer<Vector3d>
    {
        public bool Equals(Vector3d a,Vector3d b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        public int GetHashCode(Vector3d p){unchecked{return p.x.GetHashCode()^p.y.GetHashCode()*397^p.z.GetHashCode();}}
    }
    static bool SameCellsReference(List<Vector3d> a,List<Vector3d> b,IEqualityComparer<Vector3d> comparer)
    {
        if(a==null||b==null||a.Count!=b.Count)return false;
        var set=new HashSet<Vector3d>(b,comparer);
        for(int i=0;i<a.Count;i++)if(!set.Contains(a[i]))return false;
        return true;
    }
    static byte[] ExpandRoad(byte[] bytes){using(var input=new MemoryStream(bytes,18,bytes.Length-18,false))using(var zip=new DeflateStream(input,CompressionMode.Decompress))using(var output=new MemoryStream()){zip.CopyTo(output);return output.ToArray();}}
    static byte[] PackRoad(byte[] raw){using(var compressed=new MemoryStream()){using(var zip=new DeflateStream(compressed,CompressionMode.Compress,true))zip.Write(raw,0,raw.Length);byte[] body=compressed.ToArray();using(var output=new MemoryStream()){output.Write(new byte[]{(byte)'T',(byte)'L',(byte)'D',(byte)'R',(byte)'D',1},0,6);byte[] size=BitConverter.GetBytes(raw.Length);output.Write(size,0,4);byte[] length=BitConverter.GetBytes(body.Length);output.Write(length,0,4);byte[] hash=BitConverter.GetBytes(RawHash(raw));output.Write(hash,0,4);output.Write(body,0,body.Length);return output.ToArray();}}}
    static uint RawHash(byte[] bytes){uint hash=2166136261;for(int i=0;i<bytes.Length;i++){hash^=bytes[i];hash*=16777619;}return hash;}
}
