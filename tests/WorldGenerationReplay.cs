using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;

// Replay the production generation scope with scene/road stages as stand-ins.
// This checks queue identity, exclusion of scene placement/removal, and finally
// restoration on exceptions. It does not run Unity mesh/collider operations.
public class roadGenScript
{
    public List<Vector3d> chunksNeeded=new List<Vector3d>();
    public List<Vector3d> chunksToRemove=new List<Vector3d>();
    public bool hasSideRoad=true,hasMainRoad=true,Throw;
    public float chunkSize=10000;
    public int DataCalls,SceneCalls,QueuedRemovals,Cells;
    public void UpdChunks(bool more)
    {
        DataCalls++;QueuedRemovals+=chunksToRemove.Count;Cells=chunksNeeded.Count;
        if(Throw)throw new InvalidOperationException("injected data-stage failure");
    }
    public void ConnectChunks(){DataCalls++;}
    public void UpdGenRoad(bool more){DataCalls++;}
    public void Upd(Vector3d pos){SceneCalls++;}
    public void UpdPlaceRemove(bool more){SceneCalls++;}
}

public static class WorldGenerationReplay
{
    // The transplanted production scope calls private cache-stamp helpers. Keep
    // this fixture focused on the generation lifecycle and remap those calls to
    // stand-ins instead of widening StableMap's production visibility.
    static bool cachesInvalidated;
    static int cacheFlushes;
    static long RoadDependencyStamp(roadGenScript road,IList<Vector3d> centers,bool includeNeighbors)
    {return ((long)road.DataCalls<<32)|(uint)road.Cells;}
    static bool DependencyChanged(long before,long after){return before!=after;}
    static void InvalidateProximityCaches(){cachesInvalidated=true;}
    static void FlushProximityCaches(){cacheFlushes++;}

    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e)
        {
            string name=new AssemblyName(e.Name).Name;
            string file=name=="Assembly-CSharp"?args[1]:Path.Combine(args[0],name+".dll");
            return File.Exists(file)?Assembly.LoadFrom(file):null;
        };
        try
        {
            string self=Assembly.GetExecutingAssembly().Location;
            string output=Path.Combine(Path.GetDirectoryName(self),"WorldGenerationInjected.exe");
            using(var fixture=AssemblyDefinition.ReadAssembly(self))
            using(var runtime=AssemblyDefinition.ReadAssembly(args[2]))
            {
                var target=fixture.MainModule.Types.Single(t=>t.Name=="WorldGenerationReplay").Methods.Single(m=>m.Name=="Generate");
                var source=runtime.MainModule.Types.Single(t=>t.FullName=="TldMp.StableMap").Methods.Single(m=>m.Name=="GenerateRoadData");
                Copy(source,target,fixture.MainModule);
                fixture.Write(output);
            }
            var loaded=Assembly.LoadFile(output);
            return (int)loaded.GetType("WorldGenerationReplay").GetMethod("Run").Invoke(null,null);
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void Copy(MethodDefinition source,MethodDefinition target,ModuleDefinition module)
    {
        target.Body=new Mono.Cecil.Cil.MethodBody(target);
        target.Body.InitLocals=source.Body.InitLocals;target.Body.MaxStackSize=source.Body.MaxStackSize;
        var variables=new Dictionary<VariableDefinition,VariableDefinition>();
        foreach(var old in source.Body.Variables)
        {var variable=new VariableDefinition(module.ImportReference(old.VariableType));variables.Add(old,variable);target.Body.Variables.Add(variable);}
        var map=new Dictionary<Instruction,Instruction>();
        foreach(var old in source.Body.Instructions){var item=Instruction.Create(OpCodes.Nop);item.OpCode=old.OpCode;item.Operand=old.Operand;map.Add(old,item);target.Body.Instructions.Add(item);}
        var road=module.Types.Single(t=>t.Name=="roadGenScript");var replay=module.Types.Single(t=>t.Name=="WorldGenerationReplay");
        foreach(var old in source.Body.Instructions)
        {
            var item=map[old];object operand=old.Operand;
            if(operand is Instruction)item.Operand=map[(Instruction)operand];
            else if(operand is Instruction[])item.Operand=((Instruction[])operand).Select(i=>map[i]).ToArray();
            else if(operand is VariableDefinition)item.Operand=variables[(VariableDefinition)operand];
            else if(operand is ParameterDefinition)item.Operand=target.Parameters[((ParameterDefinition)operand).Index];
            else if(operand is FieldReference)
            {var field=(FieldReference)operand;item.Operand=field.DeclaringType.Name=="roadGenScript"?road.Fields.Single(f=>f.Name==field.Name):module.ImportReference(field);}
            else if(operand is MethodReference)
            {var method=(MethodReference)operand;
                if(method.DeclaringType.Name=="roadGenScript")item.Operand=road.Methods.Single(m=>m.Name==method.Name && m.Parameters.Count==method.Parameters.Count);
                else if(method.DeclaringType.FullName=="TldMp.StableMap" && new[]{"RoadDependencyStamp","DependencyChanged","InvalidateProximityCaches","FlushProximityCaches"}.Contains(method.Name))
                    item.Operand=replay.Methods.Single(m=>m.Name==method.Name && m.Parameters.Count==method.Parameters.Count);
                else item.Operand=module.ImportReference(method);}
            else if(operand is TypeReference)item.Operand=module.ImportReference((TypeReference)operand);
        }
        foreach(var old in source.Body.ExceptionHandlers)
            target.Body.ExceptionHandlers.Add(new ExceptionHandler(old.HandlerType){TryStart=map[old.TryStart],TryEnd=old.TryEnd==null?null:map[old.TryEnd],HandlerStart=map[old.HandlerStart],HandlerEnd=old.HandlerEnd==null?null:map[old.HandlerEnd],CatchType=old.CatchType==null?null:module.ImportReference(old.CatchType)});
    }
    public static void Generate(roadGenScript road,Vector3d pos,bool neighbors){throw new Exception("Production scope was not injected");}
    static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
    public static int Run()
    {
        cachesInvalidated=false;cacheFlushes=0;
        var road=new roadGenScript();var needed=road.chunksNeeded;var removing=road.chunksToRemove;
        needed.Add(new Vector3d(10000,0,0));removing.Add(new Vector3d(20000,0,0));
        Generate(road,new Vector3d(0,0,0),true);
        Check(road.DataCalls==3 && road.SceneCalls==0,"Generation touched scene stages or omitted data stages");
        Check(road.Cells==9 && road.QueuedRemovals==0,"POI neighbors incomplete or live removals executed");
        Check(cachesInvalidated && cacheFlushes==1,"Generated road data did not invalidate and flush dependent lookup caches");
        Check(System.Object.ReferenceEquals(road.chunksNeeded,needed) && System.Object.ReferenceEquals(road.chunksToRemove,removing),"Live queue identity changed");
        Check(needed.Count==1 && removing.Count==1,"Live queue contents changed");
        road.Throw=true;bool failed=false;
        try{Generate(road,new Vector3d(0,0,0),false);}catch(InvalidOperationException){failed=true;}
        Check(failed && System.Object.ReferenceEquals(road.chunksNeeded,needed) && System.Object.ReferenceEquals(road.chunksToRemove,removing),"Exception lost live queues");
        Check(road.SceneCalls==0 && road.QueuedRemovals==0,"Failure path unloaded host scene");
        Console.WriteLine("PASS: production road-generation scope replay: neighbor prerequisites, no scene stages/removals, live queue contents/identity preserved on success and exception. Native generation/physics stand-ins.");
        return 0;
    }
}
