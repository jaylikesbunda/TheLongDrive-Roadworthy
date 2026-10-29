using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TldMp;
using UnityEngine;

public static class WorldSerializationTests
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
            int result=Run();
            if(args.Length>2)CheckCapture(args[2]);
            return result;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void CheckCapture(string path)
    {
        StableGameBridge.WorldFrame actual;
        if(path.EndsWith(".json",StringComparison.OrdinalIgnoreCase))
        {using(var input=File.OpenRead(path))actual=(StableGameBridge.WorldFrame)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(StableGameBridge.WorldFrame)).ReadObject(input);}
        else actual=StableJson.Read<StableGameBridge.WorldFrame>(File.ReadAllBytes(path));
        var copy=StableJson.Read<StableGameBridge.WorldFrame>(StableJson.Write(actual));
        Check(actual.Items.items.Count>0 && copy.Items.items.Count==actual.Items.items.Count,"Captured game item graph lost");
        Check(StableJson.Write(actual).Length==StableJson.Write(copy).Length,"Captured save graph changed on second roundtrip");
        Console.WriteLine("PASS: captured live snapshot item graph, "+copy.Items.items.Count+" items, stable complete-data roundtrip.");
    }
    static int Run()
    {
        var p=new Vector3d(-10000,0,20000);
        var equal=new Vector3d(p.x,p.y,p.z);
        Check(p!=equal,"Expected supported-game Vector3d operator defect to reproduce");
        Check(StableMap.SamePosition(p,equal),"Equal cell was refused");
        Check(!StableMap.SamePosition(p,new Vector3d(-9999,0,20000)),"Off-grid position accepted");
        Check(!StableMap.SamePosition(p,new Vector3d(Double.NaN,0,20000)),"NaN accepted");
        var frame=new StableGameBridge.WorldFrame {Fingerprint="test",Time=.75f};
        frame.Items.items.Add(new save_item(0x80000001u,5,p,new Vector3(1,2,3)));
        frame.Active.Add(new StableGameBridge.ActiveItem {ID=0x80000001u,Active=true});
        frame.Pois.Add(new StableGameBridge.PoiState {Generator=2,State=new save_poi {posID=p}});
        frame.PlayerMessages.Add(new StableGameBridge.MessageState {Origin=UInt64.MaxValue,Payload="AQID"});
        var world=StableJson.Read<StableGameBridge.WorldFrame>(StableJson.Write(frame));
        Check(world.Active.Count==1 && world.Active[0].ID==0x80000001u && world.Active[0].Active,"Active item list lost");
        Check(world.Pois.Count==1 && world.Pois[0].Generator==2 && StableMap.SamePosition(world.Pois[0].State.posID,p),"POI state lost");
        Check(world.PlayerMessages.Count==1 && world.PlayerMessages[0].Origin==UInt64.MaxValue && world.PlayerMessages[0].Payload=="AQID","Player state lost");
        Check(world.Items.items.Count==1 && StableMap.SamePosition(world.Items.items[0].transform.pos,p),"Game item record lost");
        var chunk=new StableMap.Chunk {Key="r|0|0|-10000|20000",Seed=-123,Position=p,Road=new StableMap.Road {Seed=7,Main=new List<int>{3}}};
        chunk.Road.Dots.Add(new StableMap.Dot {Pos=p,Connected=new List<int>{1,2}});
        var line=new StableMap.Line {Start=p,End=new Vector3d(0,0,0),ID=4};
        line.Elements.Add(new StableMap.Element {Biome=2,Road=3,Bones=new List<Vector3d>{p,equal}});
        chunk.Road.Lines.Add(line);
        chunk.Road.Inner.Add(new StableMap.Inner {Pos=p,Roads=new List<roadGenScript.roadChunk>{new roadGenScript.roadChunk {roadId=4,roadElements=new List<int>{2}}}});
        chunk.Pois.Add(new StableMap.Poi {ID=5,Pos=p,State=new save_poi {posID=p}});
        chunk.Objects.Add(new StableMap.Decoration {Prefab=6,Pos=p,Scale=1.5f});
        var copy=StableJson.Read<StableMap.Chunk>(StableJson.Write(chunk));
        Check(copy.Key==chunk.Key && copy.Seed==-123 && StableMap.SamePosition(copy.Position,p),"Chunk identity lost");
        Check(copy.Road.Dots.Count==1 && copy.Road.Dots[0].Connected.Count==2,"Road dots lost");
        Check(copy.Road.Lines[0].Elements[0].Bones.Count==2,"Road geometry lost");
        Check(copy.Road.Inner[0].Roads[0].roadElements[0]==2,"Inner road records lost");
        Check(copy.Pois.Count==1 && copy.Pois[0].State!=null,"Chunk POI list lost");
        Check(copy.Objects.Count==1 && copy.Objects[0].Scale==1.5f,"Decoration list lost");
        var packed=StableJson.Write(chunk);
        Check(packed.Length<BitConverter.ToInt32(packed,0),"Expected repetitive chunk JSON to compress");
        bool rejected=false;try{StableJson.Read<StableMap.Chunk>(packed,8);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Expanded payload limit bypassed");
        var corrupt=(byte[])packed.Clone();Buffer.BlockCopy(BitConverter.GetBytes(0),0,corrupt,0,4);
        rejected=false;try{StableJson.Read<StableMap.Chunk>(corrupt);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Compressed data expanded beyond declared length");
        Console.WriteLine("PASS: "+checks+" world/chunk regressions using production DTOs and actual game data types; Vector3d refusal reproduced; active items, POIs, player state and road/decorative graphs preserved. No Unity native calls.");
        return 0;
    }
}

