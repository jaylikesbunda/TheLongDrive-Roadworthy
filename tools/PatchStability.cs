using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Native-restore patch scope. Independent infrastructure, narrow native-state
// observers, and deferred-item iterator wrappers stay hooked. Gameplay authority,
// movement writers, storage ownership, collision/damage and breakable hooks
// remain removed (except the guest auto-store gate on the FreezeUpd scan call);
// observed/wrapped native bodies retain their stock operations.
public static class PatchStability
{
    static ModuleDefinition module;static TypeDefinition bridge;
    static TypeReference Signature(TypeReference t)
    {
        if(t is ArrayType)return new ArrayType(Signature(((ArrayType)t).ElementType));
        var generic=t as GenericInstanceType;
        if(generic!=null)
        {
            var result=new GenericInstanceType(Signature(generic.ElementType));
            foreach(var argument in generic.GenericArguments)result.GenericArguments.Add(Signature(argument));
            return result;
        }
        var scope=t.Scope as AssemblyNameReference;
        if(scope!=null && scope.Name==module.Assembly.Name.Name)return module.GetTypes().Single(x=>x.FullName==t.FullName);
        return module.ImportReference(t);
    }
    static MethodReference Hook(string name)
    {
        var m=bridge.Methods.Single(x=>x.Name==name);var r=new MethodReference(name,Signature(m.ReturnType),module.ImportReference(bridge)){HasThis=false};
        foreach(var p in m.Parameters)r.Parameters.Add(new ParameterDefinition(Signature(p.ParameterType)));return r;
    }
    static MethodDefinition Method(string type,string name,int count,string firstType=null)
    {
        var t=module.GetTypes().Single(x=>x.FullName==type);
        var found=t.Methods.Where(m=>m.Name==name && m.Parameters.Count==count && (firstType==null || m.Parameters[0].ParameterType.Name==firstType || m.Parameters[0].ParameterType.FullName==firstType)).ToArray();
        if(found.Length!=1)throw new Exception("Expected one method: "+type+"."+name+"/"+count+" "+firstType+"; found "+found.Length);return found[0];
    }
    static void Loads(ILProcessor il,MethodDefinition m,Instruction first,int[] args)
    {foreach(int a in args)il.InsertBefore(first,a==-1?Instruction.Create(OpCodes.Ldarg_0):Instruction.Create(OpCodes.Ldarg,m.Parameters[a]));}
    static void Guard(MethodDefinition m,string name,params int[] args)
    {
        var il=m.Body.GetILProcessor();var first=m.Body.Instructions[0];Loads(il,m,first,args);il.InsertBefore(first,Instruction.Create(OpCodes.Call,Hook(name)));il.InsertBefore(first,Instruction.Create(OpCodes.Brtrue,first));
        if(m.ReturnType.FullName=="System.Boolean")il.InsertBefore(first,Instruction.Create(OpCodes.Ldc_I4_0));il.InsertBefore(first,Instruction.Create(OpCodes.Ret));
    }
    static void Entry(MethodDefinition m,string name,params int[] args)
    {
        var hook=Hook(name);
        if(hook.ReturnType.FullName!="System.Void" || hook.Parameters.Count!=args.Length)
            throw new Exception("Entry hook signature mismatch: "+m.FullName+" -> "+hook.FullName);
        for(int i=0;i<args.Length;i++)
        {
            int arg=args[i];
            if(arg < -1 || arg>=m.Parameters.Count || arg==-1&&!m.HasThis)
                throw new Exception("Invalid entry hook argument: "+name);
            var source=arg==-1?m.DeclaringType:m.Parameters[arg].ParameterType;
            if(source.FullName!=hook.Parameters[i].ParameterType.FullName)
                throw new Exception("Entry hook argument type mismatch: "+name+" argument "+i);
        }
        var il=m.Body.GetILProcessor();var first=m.Body.Instructions[0];Loads(il,m,first,args);il.InsertBefore(first,Instruction.Create(OpCodes.Call,hook));
    }
    static void RedirectRetBranches(MethodDefinition m,Instruction ret,Instruction entry)
    {
        foreach(var i in m.Body.Instructions)
        {
            if(i.Operand==ret)i.Operand=entry;
            var targets=i.Operand as Instruction[];
            if(targets!=null)for(int n=0;n<targets.Length;n++)if(targets[n]==ret)targets[n]=entry;
        }
    }
    // EH end markers are exclusive. When an observer is inserted before a ret
    // that also serves as TryEnd/HandlerEnd, keep the observer outside that
    // protected region by moving the boundary to its first injected instruction.
    static void MoveExceptionEndBoundaries(MethodDefinition m,Instruction oldEnd,Instruction newEnd)
    {
        foreach(var handler in m.Body.ExceptionHandlers)
        {
            if(handler.TryEnd==oldEnd)handler.TryEnd=newEnd;
            if(handler.HandlerEnd==oldEnd)handler.HandlerEnd=newEnd;
        }
    }
    // Insert a postcondition observer on every return. Retarget existing branch
    // edges to the first injected load so early-return paths cannot skip it.
    static void AfterReturn(MethodDefinition m,string name,params int[] args)
    {
        if(m.ReturnType.FullName!="System.Void")throw new Exception("After-return hook requires void native method: "+m.FullName);
        var hook=Hook(name);
        if(hook.ReturnType.FullName!="System.Void" || hook.Parameters.Count!=args.Length)
            throw new Exception("After-return hook signature mismatch: "+m.FullName+" -> "+hook.FullName);
        var il=m.Body.GetILProcessor();var returns=m.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
        if(returns.Length!=1)throw new Exception("Unexpected native return count for observer: "+m.FullName+" count="+returns.Length);
        foreach(var ret in returns)
        {
            Instruction entry=null;
            foreach(int arg in args)
            {
                var load=arg==-1?Instruction.Create(OpCodes.Ldarg_0):Instruction.Create(OpCodes.Ldarg,m.Parameters[arg]);
                if(entry==null)entry=load;il.InsertBefore(ret,load);
            }
            if(entry==null)throw new Exception("After-return hook has no observed receiver: "+m.FullName);
            il.InsertBefore(ret,Instruction.Create(OpCodes.Call,hook));RedirectRetBranches(m,ret,entry);MoveExceptionEndBoundaries(m,ret,entry);
        }
    }
    static void AfterReturns(MethodDefinition m,string name,int expectedReturns)
    {
        if(m.ReturnType.FullName!="System.Void")throw new Exception("After-return hook requires void native method: "+m.FullName);
        var hook=Hook(name);
        if(hook.ReturnType.FullName!="System.Void" || hook.Parameters.Count!=0)
            throw new Exception("After-return hook signature mismatch: "+m.FullName+" -> "+hook.FullName);
        var il=m.Body.GetILProcessor();var returns=m.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
        if(returns.Length!=expectedReturns)throw new Exception("Unexpected native return count for bounded observer: "+m.FullName+" count="+returns.Length+" expected="+expectedReturns);
        foreach(var ret in returns)
        {
            var call=Instruction.Create(OpCodes.Call,hook);il.InsertBefore(ret,call);RedirectRetBranches(m,ret,call);MoveExceptionEndBoundaries(m,ret,call);
        }
    }
    // Bounded exit observer that also forwards selected native arguments
    // (used for per-instance terrain labels). Every original return path is
    // covered; the guard's injected false-return is added later and has no
    // separate exit observer.
    static void AfterReturns(MethodDefinition m,string name,int expectedReturns,int[] args)
    {
        if(m.ReturnType.FullName!="System.Void")throw new Exception("After-return hook requires void native method: "+m.FullName);
        var hook=Hook(name);
        if(hook.ReturnType.FullName!="System.Void" || hook.Parameters.Count!=args.Length)
            throw new Exception("After-return hook signature mismatch: "+m.FullName+" -> "+hook.FullName);
        var il=m.Body.GetILProcessor();var returns=m.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
        if(returns.Length!=expectedReturns)throw new Exception("Unexpected native return count for bounded observer: "+m.FullName+" count="+returns.Length+" expected="+expectedReturns);
        for(int a=0;a<args.Length;a++)
        {
            int arg=args[a];
            var source=arg==-1?m.DeclaringType:m.Parameters[arg].ParameterType;
            if(source.FullName!=hook.Parameters[a].ParameterType.FullName)
                throw new Exception("After-return hook argument type mismatch: "+name+" argument "+a);
        }
        foreach(var ret in returns)
        {
            Instruction entry=null;
            foreach(int arg in args)
            {
                var load=arg==-1?Instruction.Create(OpCodes.Ldarg_0):Instruction.Create(OpCodes.Ldarg,m.Parameters[arg]);
                if(entry==null)entry=load;
                il.InsertBefore(ret,load);
            }
            var call=Instruction.Create(OpCodes.Call,hook);
            il.InsertBefore(ret,call);
            RedirectRetBranches(m,ret,entry??call);MoveExceptionEndBoundaries(m,ret,entry??call);
        }
    }
    // Exit observer for a method with a non-void return. The native result
    // remains on the evaluation stack while the no-result bridge observer runs.
    static void AfterReturnsValue(MethodDefinition m,string name,int expectedReturns,params int[] args)
    {
        if(m.ReturnType.FullName=="System.Void")throw new Exception("Value-preserving observer requires a native return value: "+m.FullName);
        var hook=Hook(name);
        if(hook.ReturnType.FullName!="System.Void" || hook.Parameters.Count!=args.Length)
            throw new Exception("Value-preserving observer signature mismatch: "+m.FullName+" -> "+hook.FullName);
        for(int a=0;a<args.Length;a++)
        {
            int arg=args[a];var source=arg==-1?m.DeclaringType:m.Parameters[arg].ParameterType;
            if(source.FullName!=hook.Parameters[a].ParameterType.FullName)
                throw new Exception("Value-preserving observer argument type mismatch: "+name+" argument "+a);
        }
        var il=m.Body.GetILProcessor();var returns=m.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
        if(returns.Length!=expectedReturns)throw new Exception("Unexpected native return count for value-preserving observer: "+m.FullName+" count="+returns.Length+" expected="+expectedReturns);
        foreach(var ret in returns)
        {
            Instruction entry=null;
            foreach(int arg in args)
            {
                var load=arg==-1?Instruction.Create(OpCodes.Ldarg_0):Instruction.Create(OpCodes.Ldarg,m.Parameters[arg]);
                if(entry==null)entry=load;il.InsertBefore(ret,load);
            }
            var call=Instruction.Create(OpCodes.Call,hook);il.InsertBefore(ret,call);
            RedirectRetBranches(m,ret,entry??call);MoveExceptionEndBoundaries(m,ret,entry??call);
        }
    }
    static void Observe(MethodDefinition m,string begin,string end,int expectedReturns,int[] beginArgs,int[] endArgs)
    {
        Entry(m,begin,beginArgs);
        AfterReturns(m,end,expectedReturns,endArgs);
    }
    static void ObserveValue(MethodDefinition m,string begin,string end,int expectedReturns,int[] beginArgs,int[] endArgs)
    {
        Entry(m,begin,beginArgs);
        AfterReturnsValue(m,end,expectedReturns,endArgs);
    }
    static void BracketCalls(MethodDefinition m,string declaringType,string methodName,int parameterCount,int expectedCount,string beginName,string endName)
    {
        var begin=Hook(beginName);var end=Hook(endName);
        if(begin.ReturnType.FullName!="System.Void" || begin.Parameters.Count!=0 || end.ReturnType.FullName!="System.Void" || end.Parameters.Count!=0)
            throw new Exception("Native call bracket hooks must be parameterless void: "+m.FullName);
        var calls=m.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Call || i.OpCode==OpCodes.Callvirt) && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName==declaringType && ((MethodReference)i.Operand).Name==methodName &&
            ((MethodReference)i.Operand).Parameters.Count==parameterCount).ToArray();
        if(calls.Length!=expectedCount)throw new Exception("Unexpected native call-site count for "+m.FullName+" "+declaringType+"."+methodName+"/"+parameterCount+": "+calls.Length+" expected "+expectedCount);
        var il=m.Body.GetILProcessor();
        foreach(var call in calls)
        {
            var next=call.Next;if(next==null)throw new Exception("Native bracketed call has no continuation: "+m.FullName);
            var entry=Instruction.Create(OpCodes.Call,begin);
            il.InsertBefore(call,entry);
            RedirectRetBranches(m,call,entry);
            foreach(var handler in m.Body.ExceptionHandlers)
            {
                if(handler.TryStart==call)handler.TryStart=entry;
                if(handler.HandlerStart==call)handler.HandlerStart=entry;
                if(handler.TryEnd==call)handler.TryEnd=entry;
                if(handler.HandlerEnd==call)handler.HandlerEnd=entry;
            }
            il.InsertAfter(call,Instruction.Create(OpCodes.Call,end));
        }
    }
    static void InsertBefore(MethodDefinition m,Instruction anchor,string name,params int[] args)
    {
        var il=m.Body.GetILProcessor();
        Loads(il,m,anchor,args);
        il.InsertBefore(anchor,Instruction.Create(OpCodes.Call,Hook(name)));
    }
    static bool InTry(MethodDefinition m,Instruction instruction)
    {
        int index=m.Body.Instructions.IndexOf(instruction);
        foreach(var handler in m.Body.ExceptionHandlers)
        {
            int start=m.Body.Instructions.IndexOf(handler.TryStart);
            int end=m.Body.Instructions.IndexOf(handler.TryEnd);
            if(index>=start && index<end)return true;
        }
        return false;
    }
    static bool NearField(Instruction from,string type,string field)
    {
        Instruction i=from;
        for(int n=0;n<12 && i!=null;n++,i=i.Previous)
            if(i.OpCode==OpCodes.Ldfld && i.Operand is FieldReference &&
               ((FieldReference)i.Operand).Name==field &&
               ((FieldReference)i.Operand).DeclaringType.FullName==type)return true;
        return false;
    }
    static void OptimizeItemSelection()
    {
        var method=Method("itemPlaceRemoveScript","PlaceStuff",0);
        var calls=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name=="IndexOfID" &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="savedatascript" &&
            ((MethodReference)i.Operand).Parameters.Count==3).ToArray();
        if(calls.Length!=2)throw new Exception("Expected two native PlaceStuff IndexOfID calls; found "+calls.Length);
        var mutating=calls.Where(c=>NearField(c,"itemPlaceRemoveScript","cid")).ToArray();
        var selectionCalls=calls.Where(c=>!NearField(c,"itemPlaceRemoveScript","cid")).ToArray();
        if(mutating.Length!=1 || selectionCalls.Length!=1)
            throw new Exception("Could not separate PlaceStuff selection and mutating IndexOfID calls.");
        if(!InTry(method,selectionCalls[0]))
            throw new Exception("PlaceStuff selection IndexOfID is not inside its native try region.");
        var enumerators=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name=="GetEnumerator" &&
            ((MethodReference)i.Operand).DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<UnityEngine.Vector2d>")).ToArray();
        if(enumerators.Length!=1)throw new Exception("Expected one PlaceStuff placeChunks enumerator; found "+enumerators.Length);
        var field=enumerators[0].Previous;
        if(field==null || field.OpCode!=OpCodes.Ldfld || !(field.Operand is FieldReference) ||
           ((FieldReference)field.Operand).Name!="placeChunks" || ((FieldReference)field.Operand).DeclaringType.FullName!="itemPlaceRemoveScript")
            throw new Exception("Unexpected PlaceStuff placeChunks enumerator anchor.");
        var receiver=field.Previous;
        if(receiver==null || receiver.OpCode!=OpCodes.Ldarg_0)
            throw new Exception("Unexpected PlaceStuff placeChunks receiver.");
        InsertBefore(method,receiver,"ItemSelectionBegin",-1);
        selectionCalls[0].Operand=Hook("ItemSelectionLookup");
        if(!NearField(mutating[0],"itemPlaceRemoveScript","cid"))
            throw new Exception("PlaceStuff mutating IndexOfID call was disturbed.");
    }
    static void OptimizePlaceOrder()
    {
        var method=Method("objGenScript/categoryClass","UpdPlaceRemove",1);
        var firstGen=method.DeclaringType.Fields.Single(f=>f.Name=="firstGenAround");
        var place=method.DeclaringType.Fields.Single(f=>f.Name=="placeOrder");
        var firstStores=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Stfld && i.Operand is FieldReference &&
            ((FieldReference)i.Operand).FullName==firstGen.FullName).ToArray();
        if(firstStores.Length!=1)throw new Exception("Expected one firstGenAround store in UpdPlaceRemove; found "+firstStores.Length);
        var start=firstStores[0].Next;
        if(start==null)throw new Exception("UpdPlaceRemove has no prologue after firstGenAround.");
        Instruction end=start;bool hasOrderBy=false,hasToList=false;
        while(end!=null)
        {
            var callee=end.Operand as MethodReference;
            if(callee!=null && callee.Name=="OrderBy")hasOrderBy=true;
            if(callee!=null && callee.Name=="ToList")hasToList=true;
            if(end.OpCode==OpCodes.Stfld && end.Operand is FieldReference && ((FieldReference)end.Operand).FullName==place.FullName)break;
            end=end.Next;
        }
        if(end==null || !hasOrderBy || !hasToList)
            throw new Exception("Unexpected UpdPlaceRemove placeOrder sort prologue.");
        var after=end.Next;
        if(after==null)throw new Exception("UpdPlaceRemove sort prologue has no successor.");
        var il=method.Body.GetILProcessor();
        for(Instruction i=start;i!=after;)
        {Instruction next=i.Next;il.Remove(i);i=next;}
        il.InsertBefore(after,Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(after,Instruction.Create(OpCodes.Ldfld,place));
        il.InsertBefore(after,Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(after,Instruction.Create(OpCodes.Ldfld,firstGen));
        il.InsertBefore(after,Instruction.Create(OpCodes.Call,Hook("SortPlaceOrder")));
    }
    // Preserve the original flyer velocity expression and setter for dynamic bodies.
    // Unity documents Rigidbody.velocity writes on kinematic bodies as unsupported/no-op;
    // the RHS remains evaluated before this guard, including native null behavior.
    static void OptimizeFlyerKinematicVelocity()
    {
        var method=Method("randomFlyer","Upd",0);
        var setters=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name=="set_velocity" &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="UnityEngine.Rigidbody" &&
            ((MethodReference)i.Operand).Parameters.Count==1 &&
            ((MethodReference)i.Operand).Parameters[0].ParameterType.FullName=="UnityEngine.Vector3").ToArray();
        if(setters.Length!=1)throw new Exception("Expected one native randomFlyer.Rigidbody.velocity setter; found "+setters.Length);
        var setter=setters[0];var setterRef=(MethodReference)setter.Operand;
        var bridgeHook=Hook("NoteFlyerKinematicSkipped");
        if(bridgeHook.ReturnType.FullName!="System.Void" || bridgeHook.Parameters.Count!=0)
            throw new Exception("Flyer kinematic diagnostic hook must be parameterless void.");
        var methodBody=method.Body;
        var value=new VariableDefinition(module.ImportReference(setterRef.Parameters[0].ParameterType));
        methodBody.Variables.Add(value);
        var il=methodBody.GetILProcessor();
        var afterSetter=setter.Next;
        if(afterSetter==null)throw new Exception("Native flyer velocity setter has no continuation.");
        var loadValue=Instruction.Create(OpCodes.Ldloc,value);
        il.InsertBefore(setter,Instruction.Create(OpCodes.Stloc,value));
        il.InsertBefore(setter,Instruction.Create(OpCodes.Dup));
        var isKinematic=new MethodReference("get_isKinematic",module.TypeSystem.Boolean,module.ImportReference(setterRef.DeclaringType)){HasThis=true};
        il.InsertBefore(setter,Instruction.Create(OpCodes.Callvirt,isKinematic));
        il.InsertBefore(setter,Instruction.Create(OpCodes.Brfalse,loadValue));
        il.InsertBefore(setter,Instruction.Create(OpCodes.Call,bridgeHook));
        il.InsertBefore(setter,Instruction.Create(OpCodes.Pop));
        il.InsertBefore(setter,Instruction.Create(OpCodes.Br,afterSetter));
        il.InsertBefore(setter,loadValue);
        var firstInjected=setter.Previous;
        for(int n=0;n<7;n++)firstInjected=firstInjected.Previous;
        foreach(var handler in methodBody.ExceptionHandlers)
        {
            if(handler.TryStart==setter)handler.TryStart=firstInjected;
            if(handler.HandlerStart==setter)handler.HandlerStart=firstInjected;
            if(handler.TryEnd==setter)handler.TryEnd=firstInjected;
            if(handler.HandlerEnd==setter)handler.HandlerEnd=firstInjected;
        }
    }
    // Distant terrain worker: the stock Tile(Vector3d,bool,bool) call in DoPlace
    // becomes the static bridge with the same stack shape (this, gpos, regen, async)
    // and the same bool break signal. The bridge runs the stock Tile for anything
    // it does not own, so the call count and instruction count are unchanged.
    static void TerrainTileWorker()
    {
        var method=Method("terrainGenScript","DoPlace",0);
        var calls=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="terrainGenScript" &&
            ((MethodReference)i.Operand).Name=="Tile" && ((MethodReference)i.Operand).Parameters.Count==3).ToArray();
        if(calls.Length!=1)throw new Exception("Expected one DoPlace Tile call; found "+calls.Length);
        calls[0].Operand=Hook("TerrainTile");
    }
    static void OptimizeTerrainHoles()
    {
        var method=Method("terrainGenScript","GenerateTileMesh",1);
        var calls=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="mainscript" &&
            ((MethodReference)i.Operand).Name=="GetConnectedVertices" &&
            ((MethodReference)i.Operand).Parameters.Count==2).ToArray();
        if(calls.Length!=1)throw new Exception("Expected one GenerateTileMesh GetConnectedVertices call; found "+calls.Length);
        Entry(method,"TerrainHoleBegin");
        calls[0].Operand=Hook("TerrainHoleNeighbors");
    }
    // RoadsNear appends a roadClass once per inner chunk that holds one of its
    // elements. Collapse later reference-equal entries before the per-vertex
    // nearest-road scan. First occurrence order is preserved; NearestPointOnRoadBone
    // uses a strict '<' so a repeated object could never win over its first entry.
    static VariableDefinition StoredLocal(MethodDefinition method,Instruction instruction)
    {
        if(instruction==null)return null;
        if(instruction.OpCode==OpCodes.Stloc || instruction.OpCode==OpCodes.Stloc_S)return (VariableDefinition)instruction.Operand;
        if(instruction.OpCode==OpCodes.Stloc_0)return method.Body.Variables[0];
        if(instruction.OpCode==OpCodes.Stloc_1)return method.Body.Variables[1];
        if(instruction.OpCode==OpCodes.Stloc_2)return method.Body.Variables[2];
        if(instruction.OpCode==OpCodes.Stloc_3)return method.Body.Variables[3];
        return null;
    }
    static void OptimizeTerrainRoads()
    {
        var method=Method("terrainGenScript","GenerateTileMesh",1);
        var calls=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="mapSettingScript" &&
            ((MethodReference)i.Operand).Name=="RoadsNear" &&
            ((MethodReference)i.Operand).Parameters.Count==1).ToArray();
        if(calls.Length!=1)throw new Exception("Expected one GenerateTileMesh RoadsNear call; found "+calls.Length);
        var store=calls[0].Next;
        var local=StoredLocal(method,store);
        if(local==null)throw new Exception("GenerateTileMesh RoadsNear result is not stored in a local.");
        var il=method.Body.GetILProcessor();
        var load=Instruction.Create(OpCodes.Ldloc,local);
        var call=Instruction.Create(OpCodes.Call,Hook("DedupeTerrainRoads"));
        var save=Instruction.Create(OpCodes.Stloc,local);
        il.InsertAfter(store,load);
        il.InsertAfter(load,call);
        il.InsertAfter(call,save);
    }
    // NoiseS3D.Noise reads the static perm property 32 times. The getter performs
    // a null check and a field load every time; the array is invariant inside the
    // call. Bind it to one local after a single getter call (which still performs
    // the lazy SetupNoise), then read the local. Same array, same indices, same math.
    static void OptimizeTerrainNoisePerm()
    {
        var method=Method("NoiseS3D","Noise",3);
        if(method.Body.ExceptionHandlers.Count!=0)throw new Exception("NoiseS3D.Noise now has exception handlers.");
        var calls=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="NoiseS3D" &&
            ((MethodReference)i.Operand).Name=="get_perm" &&
            ((MethodReference)i.Operand).Parameters.Count==0).ToArray();
        if(calls.Length!=12)throw new Exception("Unexpected NoiseS3D perm call count: "+calls.Length);
        var getPerm=(MethodReference)calls[0].Operand;
        var local=new VariableDefinition(module.ImportReference(getPerm.ReturnType));
        method.Body.Variables.Add(local);
        var il=method.Body.GetILProcessor();
        var first=method.Body.Instructions[0];
        il.InsertBefore(first,Instruction.Create(OpCodes.Call,getPerm));
        il.InsertBefore(first,Instruction.Create(OpCodes.Stloc,local));
        foreach(var call in calls){call.OpCode=OpCodes.Ldloc;call.Operand=local;}
    }
    // Squared-distance selection was measured neutral (road scan) or
    // inconsistent (ClosestNoY) in isolated production-IL benchmarks, so it is
    // not included. The measured terrain wins are the noise-kernel lookup
    // removal and the road-list dedupe above.
    // UpdChunksToRemove marks every chunk outside chunksNeeded. UpdChunks then
    // removes those cells and immediately re-adds the same 5x5 border through
    // UpdDotChunk, regenerating their dots every frame. The policy keeps only
    // empty, flag-clear border cells whose seed still matches the map; cells
    // with roads, inner chunks, objects or stale seeds keep the native path.
    static void OptimizeRoadChunkChurn()
    {
        var method=Method("roadGenScript","UpdChunksToRemove",0);
        var contains=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="System.Collections.Generic.List`1<UnityEngine.Vector3d>" &&
            ((MethodReference)i.Operand).Name=="Contains" && ((MethodReference)i.Operand).Parameters.Count==1).ToArray();
        if(contains.Length!=1)throw new Exception("Expected one UpdChunksToRemove chunksNeeded.Contains; found "+contains.Length);
        var call=contains[0];
        var keyCall=call.Previous;
        if(keyCall==null || !(keyCall.Operand is MethodReference) || ((MethodReference)keyCall.Operand).Name!="get_Key")
            throw new Exception("Unexpected UpdChunksToRemove key anchor.");
        var address=keyCall.Previous;
        var field=address==null?null:address.Previous;
        if(field==null || field.OpCode!=OpCodes.Ldfld || !(field.Operand is FieldReference) ||
           ((FieldReference)field.Operand).Name!="chunksNeeded")
            throw new Exception("Unexpected UpdChunksToRemove chunksNeeded anchor.");
        var thisLoad=field.Previous;
        if(thisLoad==null || thisLoad.OpCode!=OpCodes.Ldarg_0)
            throw new Exception("Unexpected UpdChunksToRemove receiver anchor.");
        var il=method.Body.GetILProcessor();
        il.InsertBefore(thisLoad,Instruction.Create(OpCodes.Ldarg_0));
        call.OpCode=OpCodes.Call;
        call.Operand=Hook("RoadChunkKeep");
    }
    // MorePosUpd's native factory still builds the stock iterator. For a car
    // that is locally pose-authoritative, carrying a remote player and moving,
    // return a runtime coroutine with a higher extra count instead. The stock
    // iterator (and therefore every non-burst item) is untouched.
    // No physics, authority, seat or wire change.
    static void OptimizeCarPoseStart()
    {
        var method=Method("tosaveitemscript","MorePosUpd",0);
        if(method.ReturnType.FullName!="System.Collections.IEnumerator")
            throw new Exception("Unexpected MorePosUpd return type: "+method.ReturnType.FullName);
        var il=method.Body.GetILProcessor();
        var first=method.Body.Instructions[0];
        il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first,Instruction.Create(OpCodes.Call,Hook("CarPoseNeedsBurst")));
        il.InsertBefore(first,Instruction.Create(OpCodes.Brfalse,first));
        il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first,Instruction.Create(OpCodes.Call,Hook("CarPoseBurst")));
        il.InsertBefore(first,Instruction.Create(OpCodes.Ret));
    }
    // A timed-out player is destroyed but its dictionary entry remains. The
    // native timeout would then dereference the destroyed mpplayerscript in the
    // next DropStuff/UpdItemParent pass. Route those calls through a null-safe
    // helper; authority, item flags and the native drop order are unchanged.
    static void OptimizePlayerLeave()
    {
        var drop=Method("syncScript/playerSyncClass","DropStuff",0);
        ReplaceIgnoreCollision(drop,1);
        var parent=Method("syncScript","UpdItemParent",2);
        ReplaceIgnoreCollision(parent,3);
        var get=parent.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Call || i.OpCode==OpCodes.Callvirt) &&
            i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName=="syncScript" &&
            ((MethodReference)i.Operand).Name=="GetPlayerBySteamID" && ((MethodReference)i.Operand).Parameters.Count==1).ToArray();
        if(get.Length!=1)throw new Exception("Unexpected UpdItemParent player lookup count: "+get.Length);
        get[0].OpCode=OpCodes.Call;get[0].Operand=Hook("GetPlayerWithScript");
    }
    static void ReplaceIgnoreCollision(MethodDefinition method,int expected)
    {
        var calls=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="mpplayerscript" &&
            ((MethodReference)i.Operand).Name=="IgnoreCollision" && ((MethodReference)i.Operand).Parameters.Count==2).ToArray();
        if(calls.Length!=expected)throw new Exception("Unexpected IgnoreCollision count in "+method.FullName+": "+calls.Length);
        foreach(var call in calls){call.OpCode=OpCodes.Call;call.Operand=Hook("IgnoreCollisionSafe");}
    }
    // A host replies to a stale askItemData with removeItem even when the item
    // exists but is held/stored/worn by another player. Keep the native reply
    // only when the host's item table genuinely lacks the id.
    static void OptimizeItemAsk()
    {
        var method=Method("syncScript","RecAskItemData",2);
        var calls=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="syncScript" &&
            ((MethodReference)i.Operand).Name=="SendRemoveItem" && ((MethodReference)i.Operand).Parameters.Count==1).ToArray();
        if(calls.Length!=1)throw new Exception("Unexpected RecAskItemData remove count: "+calls.Length);
        calls[0].OpCode=OpCodes.Call;calls[0].Operand=Hook("MayRemoveMissingItem");
    }
    // RemoveStuff measures each item's distance from its own transform. A parented
    // item (car part, stored item) is measured from its root instead, so one stray
    // child cannot remove or freeze its whole group. Only that single Transform.position
    // read changes; remove/freeze/unfreeze branches keep their stock bodies.
    // Terrain-align helpers nested under a child of the POI prefab would be placed from that child's
    // origin, not the POI root. Only the two Transform.localPosition reads in the alignClass constructor
    // change; the bridge returns the stock value for every helper whose parent is the root.
    static void OptimizeTerrainAlignNested()
    {
        var method=Method("poiGenScript/alignClass",".ctor",4);
        var reads=method.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Call || i.OpCode==OpCodes.Callvirt) && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="UnityEngine.Transform" && ((MethodReference)i.Operand).Name=="get_localPosition").ToArray();
        if(reads.Length!=2)throw new Exception("Terrain align: expected two Transform.localPosition reads in alignClass constructor, found "+reads.Length);
        foreach(var read in reads){read.OpCode=OpCodes.Call;read.Operand=Hook("TerrainAlignLocal");}
    }
    static void OptimizeItemStreamAnchor()
    {
        var method=Method("itemPlaceRemoveScript","RemoveStuff",0);
        var reads=method.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Call || i.OpCode==OpCodes.Callvirt) && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="UnityEngine.Transform" && ((MethodReference)i.Operand).Name=="get_position").ToArray();
        if(reads.Length!=1)throw new Exception("Item stream anchor: expected one Transform.position read in RemoveStuff, found "+reads.Length);
        reads[0].OpCode=OpCodes.Call;reads[0].Operand=Hook("ItemStreamPosition");
    }
    // PlaceStuff places far items synchronously, once a second, in one frame. Its
    // single PlaceOne call (result popped) is routed to EnqueueFarPlace, which
    // records the id; the bridge drains the queue over frames with revalidation.
    // The call swap keeps the stack identical: this, uint id, int index -> result.
    static void OptimizeItemPlaceSpread()
    {
        var method=Method("itemPlaceRemoveScript","PlaceStuff",0);
        var calls=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="itemPlaceRemoveScript" &&
            ((MethodReference)i.Operand).Name=="PlaceOne" && ((MethodReference)i.Operand).Parameters.Count==2).ToArray();
        if(calls.Length!=1)throw new Exception("Item place spread: expected one PlaceOne call in PlaceStuff, found "+calls.Length);
        if(calls[0].Next==null || calls[0].Next.OpCode!=OpCodes.Pop)throw new Exception("Item place spread: PlaceOne result is not popped.");
        calls[0].Operand=Hook("EnqueueFarPlace");
    }
    // FreezeUpd auto-stores loose items on every peer, but only the host
    // broadcasts the store. Route its single Store call through a guest gate;
    // Store itself and its network/load/spawn callers keep their stock bodies.
    static void OptimizeAutoStore()
    {
        var method=Method("mountStuff","FreezeUpd",0);
        var calls=method.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Call || i.OpCode==OpCodes.Callvirt) && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="mountStuff" &&
            ((MethodReference)i.Operand).Name=="Store" && ((MethodReference)i.Operand).Parameters.Count==2).ToArray();
        if(calls.Length!=1)throw new Exception("Unexpected FreezeUpd Store count: "+calls.Length);
        calls[0].OpCode=OpCodes.Call;calls[0].Operand=Hook("AutoStoreGate");
        Entry(Method("pickupable","PhysLock",1),"NativeBeforePhysLock",-1,0);
        Entry(Method("pickupable","JustUnPhysicsLock",0),"NativeBeforeUnPhysicsLock",-1);
    }
    // The far branch of UpdPlaceRemove null-checks every element of every far
    // road chunk each frame. A per-chunk live count lets it skip chunks proven
    // empty; unknown or changed chunks still run the native loop, and every
    // near instantiation invalidates the count. No field, authority or wire change.
    static void OptimizeRoadLiveCache()
    {
        var method=Method("roadGenScript","UpdPlaceRemove",1);
        var il=method.Body.GetILProcessor();
        var instructions=method.Body.Instructions;
        var nearStore=instructions.FirstOrDefault(i=>(i.OpCode==OpCodes.Stloc || i.OpCode==OpCodes.Stloc_S) && i.Operand is VariableDefinition &&
            ((VariableDefinition)i.Operand).VariableType.FullName=="roadGenScript/roadChunk");
        if(nearStore==null)throw new Exception("Road live cache: near road local missing.");
        var nearRoad=(VariableDefinition)nearStore.Operand;
        il.InsertAfter(nearStore,Instruction.Create(OpCodes.Ldloc,nearRoad));
        il.InsertAfter(nearStore.Next,Instruction.Create(OpCodes.Call,Hook("RoadChunkDirty")));

        var enumerators=instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name=="GetEnumerator" &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="System.Collections.Generic.List`1<System.Int32>").ToArray();
        if(enumerators.Length!=3)throw new Exception("Road live cache: expected three element enumerators, found "+enumerators.Length);
        var farLoad=enumerators[1].Previous==null?null:enumerators[1].Previous.Previous;
        if(farLoad==null || (farLoad.OpCode!=OpCodes.Ldloc && farLoad.OpCode!=OpCodes.Ldloc_S) || ((VariableDefinition)farLoad.Operand).VariableType.FullName!="roadGenScript/roadChunk")
            throw new Exception("Road live cache: far road local missing.");
        var farRoad=(VariableDefinition)farLoad.Operand;
        Instruction endFinally=null;
        for(var i=enumerators[1];i!=null;i=i.Next)if(i.OpCode==OpCodes.Endfinally){endFinally=i;break;}
        if(endFinally==null || endFinally.Next==null)throw new Exception("Road live cache: far loop end missing.");
        var target=endFinally.Next;
        if(target.OpCode!=OpCodes.Ldloc_1)
            throw new Exception("Road live cache: far loop target changed.");
        var dccField=instructions.Select(i=>i.Operand as FieldReference).FirstOrDefault(f=>f!=null && f.Name=="dcc0" && f.DeclaringType.FullName=="roadGenScript");
        if(dccField==null)throw new Exception("Road live cache: dcc0 field missing.");
        il.InsertBefore(farLoad,Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(farLoad,Instruction.Create(OpCodes.Ldfld,dccField));
        il.InsertBefore(farLoad,Instruction.Create(OpCodes.Ldloc,farRoad));
        il.InsertBefore(farLoad,Instruction.Create(OpCodes.Call,Hook("RoadChunkMayHaveLive")));
        il.InsertBefore(farLoad,Instruction.Create(OpCodes.Brfalse,target));

        var destroys=instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="UnityEngine.Object" && ((MethodReference)i.Operand).Name=="Destroy").ToArray();
        if(destroys.Length!=2)throw new Exception("Road live cache: expected two Destroy calls, found "+destroys.Length);
        var after=destroys[0].Next;
        if(after==null)throw new Exception("Road live cache: Destroy has no successor.");
        var load=Instruction.Create(OpCodes.Ldloc,farRoad);
        il.InsertAfter(after,load);
        il.InsertAfter(load,Instruction.Create(OpCodes.Call,Hook("RoadChunkDestroyed")));
    }
    // GetChunksNeeded repeats every road chunk once per genAround, so with two
    // players UpdPlaceRemove walks each chunk twice. Every road consumer of
    // chunksNeeded is ContainsKey/flag gated, so removing exact duplicates at
    // UpdChunksToRemove entry (before any consumer reads the list) is a no-op
    // for output. Separately, a chunk whose cells are all beyond roadPlaceDist
    // and which has no live element only runs a do-nothing far branch; skip it
    // with the same continue target as the native missing-chunk test.
    static void OptimizeRoadFarSkip()
    {
        var remove=Method("roadGenScript","UpdChunksToRemove",0);
        var needed=remove.DeclaringType.Fields.Single(f=>f.Name=="chunksNeeded");
        var ril=remove.Body.GetILProcessor();var first=remove.Body.Instructions[0];
        if(remove.Body.Instructions.Any(i=>i.Operand==first) || remove.Body.ExceptionHandlers.Any(h=>h.TryStart==first || h.HandlerStart==first))
            throw new Exception("Road dedupe: UpdChunksToRemove entry is a branch or handler target.");
        ril.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));
        ril.InsertBefore(first,Instruction.Create(OpCodes.Ldfld,needed));
        ril.InsertBefore(first,Instruction.Create(OpCodes.Call,Hook("RoadDedupeNeeded")));

        var method=Method("roadGenScript","UpdPlaceRemove",1);
        var instructions=method.Body.Instructions;
        var tries=instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name=="TryGetValue" &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="System.Collections.Generic.Dictionary`2<UnityEngine.Vector3d,roadGenScript/dotChunkClass>").ToArray();
        if(tries.Length!=1)throw new Exception("Road far skip: expected one chunk TryGetValue, found "+tries.Length);
        var address=tries[0].Previous;
        if(address==null || address.OpCode!=OpCodes.Ldflda || ((FieldReference)address.Operand).Name!="dcc0")
            throw new Exception("Road far skip: TryGetValue does not write dcc0.");
        var missing=tries[0].Next;
        if(missing==null || (missing.OpCode!=OpCodes.Brfalse && missing.OpCode!=OpCodes.Brfalse_S) || !(missing.Operand is Instruction))
            throw new Exception("Road far skip: missing-chunk branch changed.");
        var resume=missing.Next;
        if(resume==null || instructions.Any(i=>i.Operand==resume) || method.Body.ExceptionHandlers.Any(h=>h.TryStart==resume || h.HandlerStart==resume))
            throw new Exception("Road far skip: chunk body entry is a branch or handler target.");
        // The patcher never re-expands branch macros; a short branch spanning
        // the insertion could overflow, so refuse that shape outright.
        int at=instructions.IndexOf(resume);
        foreach(var b in instructions.Where(i=>i.OpCode.OperandType==OperandType.ShortInlineBrTarget))
        {
            int from=instructions.IndexOf(b),to=instructions.IndexOf((Instruction)b.Operand);
            if((from<at && at<=to) || (to<at && at<=from))throw new Exception("Road far skip: short branch spans insertion at "+b.Offset);
        }
        var il=method.Body.GetILProcessor();
        il.InsertBefore(resume,Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(resume,Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(resume,Instruction.Create(OpCodes.Ldfld,(FieldReference)address.Operand));
        il.InsertBefore(resume,Instruction.Create(OpCodes.Call,Hook("RoadChunkSkippable")));
        il.InsertBefore(resume,Instruction.Create(OpCodes.Brtrue,(Instruction)missing.Operand));
    }
    // MirrorsUpdate gates each mirror render on Renderer.isVisible, which any
    // camera (shadow pass, other mirrors) can set. Route that one read through
    // MirrorVisible(renderer, _cam): it keeps the native result and only adds a
    // view-frustum test, so it can skip a render but never add one.
    static void OptimizeMirrorOnScreen()
    {
        var method=Method("mainscript","MirrorsUpdate",3);
        if(method.Parameters[1].ParameterType.FullName!="UnityEngine.Camera")throw new Exception("Mirror on-screen: _cam parameter changed.");
        var reads=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="UnityEngine.Renderer" &&
            ((MethodReference)i.Operand).Name=="get_isVisible").ToArray();
        if(reads.Length!=1)throw new Exception("Mirror on-screen: expected one isVisible read, found "+reads.Length);
        var il=method.Body.GetILProcessor();
        il.InsertBefore(reads[0],Instruction.Create(OpCodes.Ldarg,method.Parameters[1]));
        reads[0].OpCode=OpCodes.Call;reads[0].Operand=Hook("MirrorVisible");
    }
    // Parked cars: the one Rigidbody.velocity write in carscript.Update goes through
    // CarVelocitySet, which skips it while the body sleeps or the value is unchanged.
    static void OptimizeParkedCarSleep()
    {
        var method=Method("carscript","Update",0);
        var setters=method.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Callvirt || i.OpCode==OpCodes.Call) && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name=="set_velocity" && ((MethodReference)i.Operand).DeclaringType.FullName=="UnityEngine.Rigidbody").ToArray();
        if(setters.Length!=1)throw new Exception("Expected one carscript.Update Rigidbody.velocity setter; found "+setters.Length);
        setters[0].OpCode=OpCodes.Call;setters[0].Operand=Hook("CarVelocitySet");
    }
    // Idle pour: the pour particle system is paused/resumed from one entry observer.
    static void OptimizeIdlePourPause()
    {
        Entry(Method("tankcapscript","Update",0),"TankPourTick",-1);
    }
    // A destroyed worn item leaves the camera hide list; null renderers are pruned
    // before the PreRender/PostRender loops run.
    static void OptimizeWornDestroyed()
    {
        Entry(Method("pickupable","OnDestroy",0),"WornItemDestroyed",-1);
        Entry(Method("makeinvisible","PreRender",0),"HideListPrune",-1);
        Entry(Method("makeinvisible","PostRender",0),"HideListPrune",-1);
    }
    // The shared slicer call in randomSlice.Slicc goes through SafeSlice, which
    // catches and logs one failure; the native caller still destroys the object.
    static void OptimizeSliceGuard()
    {
        var method=Method("randomSlice","Slicc",3);
        var calls=method.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Callvirt || i.OpCode==OpCodes.Call) && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="randomSlice" && ((MethodReference)i.Operand).Name=="SSSLiceee" &&
            ((MethodReference)i.Operand).Parameters.Count==4).ToArray();
        if(calls.Length!=1)throw new Exception("Expected one randomSlice.Slicc SSSLiceee call; found "+calls.Length);
        calls[0].OpCode=OpCodes.Call;calls[0].Operand=Hook("SafeSlice");
    }
    // The top-right label is the only place the game prints Application.version
    // with a leading "v". Replace the TextMeshPro set_text call in the two sites
    // with the bridge helper, which writes PatchInfo.Display instead. The native
    // string expression still runs; only the label's text changes.
    static void PatchVersionLabel()
    {
        PatchVersionLabelSite(Method("mainscript","Update",0));
        PatchVersionLabelSite(Method("menuhandler","FStart",0));
    }
    static void PatchVersionLabelSite(MethodDefinition method)
    {
        var sites=method.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Call || i.OpCode==OpCodes.Callvirt) &&
            i.Operand is MethodReference && ((MethodReference)i.Operand).Name=="set_text" &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="TMPro.TMP_Text" && IsVersionLabel(i)).ToArray();
        if(sites.Length!=1)throw new Exception("Version label: expected one TVersion set_text in "+method.FullName+", found "+sites.Length);
        sites[0].OpCode=OpCodes.Call;sites[0].Operand=Hook("SetVersionLabel");
    }
    static bool IsVersionLabel(Instruction call)
    {
        Instruction concat=call.Previous;
        Instruction space=concat==null?null:concat.Previous;
        Instruction version=space==null?null:space.Previous;
        Instruction literal=version==null?null:version.Previous;
        Instruction field=literal==null?null:literal.Previous;
        if(field==null || field.OpCode!=OpCodes.Ldfld || !(field.Operand is FieldReference) ||
            ((FieldReference)field.Operand).Name!="TVersion")return false;
        return concat.OpCode==OpCodes.Call && ((MethodReference)concat.Operand).Name=="Concat" &&
            space.OpCode==OpCodes.Ldstr && (string)space.Operand==" " &&
            version.OpCode==OpCodes.Call && ((MethodReference)version.Operand).Name=="get_version" &&
            literal.OpCode==OpCodes.Ldstr && (string)literal.Operand=="v";
    }
    // roadscript.BakeCollider bakes the placed road's final pose once. After it
    // returns, RoadBaked draws that baked mesh with a MeshRenderer and disables
    // the per-frame skinned renderer when the copy is provably identical.
    static void OptimizeRoadBake()
    {
        var method=Method("roadscript","BakeCollider",0);
        var returns=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
        if(returns.Length!=1)throw new Exception("Road bake: expected one return in BakeCollider, found "+returns.Length);
        var ret=returns[0];
        if(method.Body.Instructions.Any(i=>i.Operand==ret) || method.Body.ExceptionHandlers.Count!=0)
            throw new Exception("Road bake: BakeCollider return is a branch target or inside a handler.");
        var il=method.Body.GetILProcessor();
        il.InsertBefore(ret,Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret,Instruction.Create(OpCodes.Call,Hook("RoadBaked")));
    }
    // Native iterator methods keep their exact body; only their returned
    // IEnumerator is passed through a runtime wrapper before each ret.
    static void WrapReturn(MethodDefinition m,string name,params int[] args)
    {
        if(m.ReturnType.FullName!="System.Collections.IEnumerator")throw new Exception("Iterator wrapper requires IEnumerator: "+m.FullName);
        var hook=Hook(name);
        if(hook.ReturnType.FullName!=m.ReturnType.FullName || hook.Parameters.Count!=args.Length+1 || hook.Parameters[0].ParameterType.FullName!=m.ReturnType.FullName)
            throw new Exception("Iterator wrapper signature mismatch: "+m.FullName+" -> "+hook.FullName);
        var il=m.Body.GetILProcessor();var returns=m.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
        if(returns.Length!=1)throw new Exception("Unexpected native return count for iterator wrapper: "+m.FullName+" count="+returns.Length);
        foreach(var ret in returns)
        {
            Instruction entry=null;
            foreach(int arg in args)
            {
                var load=arg==-1?Instruction.Create(OpCodes.Ldarg_0):Instruction.Create(OpCodes.Ldarg,m.Parameters[arg]);
                if(entry==null)entry=load;il.InsertBefore(ret,load);
            }
            if(entry==null)throw new Exception("Iterator wrapper has no native arguments: "+m.FullName);
            il.InsertBefore(ret,Instruction.Create(OpCodes.Call,hook));RedirectRetBranches(m,ret,entry);
        }
    }
    static void Replace(MethodDefinition m,string name,params int[] args)
    {m.Body=new MethodBody(m);var il=m.Body.GetILProcessor();var ret=Instruction.Create(OpCodes.Ret);il.Append(ret);Loads(il,m,ret,args);il.InsertBefore(ret,Instruction.Create(OpCodes.Call,Hook(name)));}
    // Native body first: the stock implementation runs offline/unconnected and
    // only a live session takes the helper path (object-ID collision prevention).
    static void ConnectedReplacement(MethodDefinition m,string name,params int[] args)
    {
        var il=m.Body.GetILProcessor();var first=m.Body.Instructions[0];
        il.InsertBefore(first,Instruction.Create(OpCodes.Call,Hook("UseOriginalIDs")));
        il.InsertBefore(first,Instruction.Create(OpCodes.Brtrue,first));
        Loads(il,m,first,args);il.InsertBefore(first,Instruction.Create(OpCodes.Call,Hook(name)));
        il.InsertBefore(first,Instruction.Create(OpCodes.Ret));
    }
    static void LocalizeNoise()
    {
        var method=Method("terrainGenSettings/Noises","Noise",1);var il=method.Body.GetILProcessor();
        foreach(string name in new[]{"r","d","s"})
        {
            var local=new VariableDefinition(module.TypeSystem.Double);method.Body.Variables.Add(local);
            var uses=method.Body.Instructions.Where(i=>i.Operand is FieldReference && ((FieldReference)i.Operand).DeclaringType.FullName==method.DeclaringType.FullName && ((FieldReference)i.Operand).Name==name).ToArray();
            if(uses.Length<2)throw new Exception("Unexpected noise accumulator layout: "+name);
            foreach(var i in uses)
            {
                if(i.OpCode==OpCodes.Ldfld){i.OpCode=OpCodes.Pop;i.Operand=null;il.InsertAfter(i,Instruction.Create(OpCodes.Ldloc,local));}
                else if(i.OpCode==OpCodes.Stfld){i.OpCode=OpCodes.Stloc;i.Operand=local;il.InsertAfter(i,Instruction.Create(OpCodes.Pop));}
                else throw new Exception("Unexpected noise field address access.");
            }
        }
    }
    static void FixLocalIdentity()
    {
        int count=0;foreach(var i in Method("syncScript","RecDmgPlayer",1).Body.Instructions)
        {
            var call=i.Operand as MethodReference;
            if(i.OpCode!=OpCodes.Call || call==null || call.DeclaringType.FullName!="Steamworks.SteamUser" || call.Name!="GetSteamID")continue;
            i.Operand=Hook("LocalIdentity");count++;
        }
        if(count!=1)throw new Exception("Unexpected local identity comparison.");
    }
    static Instruction RecentFieldLoad(Instruction from,FieldDefinition field)
    {
        Instruction i=from;
        for(int n=0;n<6 && i!=null;n++,i=i.Previous)
            if(i.OpCode==OpCodes.Ldfld && i.Operand is FieldReference && ((FieldReference)i.Operand).FullName==field.FullName)return i;
        return null;
    }
    static void OptimizeCategoryCleanup()
    {
        var method=Method("objGenScript/categoryClass","UpdGen",1);
        var needed=method.DeclaringType.Fields.Single(f=>f.Name=="chunksNeeded");
        var remove=method.DeclaringType.Fields.Single(f=>f.Name=="chunksToRemove");
        var vector=((GenericInstanceType)needed.FieldType).GenericArguments[0];
        var hashSetDefinition=module.ImportReference(typeof(HashSet<>));
        var enumerableDefinition=module.ImportReference(typeof(IEnumerable<>));
        var hashSetType=new GenericInstanceType(hashSetDefinition);hashSetType.GenericArguments.Add(vector);
        var hashSetParameter=hashSetDefinition.GenericParameters.Single();
        var enumerableType=new GenericInstanceType(enumerableDefinition);enumerableType.GenericArguments.Add(hashSetParameter);
        var ctor=new MethodReference(".ctor",module.TypeSystem.Void,hashSetType){HasThis=true};
        ctor.Parameters.Add(new ParameterDefinition(enumerableType));
        var contains=new MethodReference("Contains",module.TypeSystem.Boolean,hashSetType){HasThis=true};
        contains.Parameters.Add(new ParameterDefinition(hashSetParameter));
        var local=new VariableDefinition(hashSetType);method.Body.Variables.Add(local);
        var instructions=method.Body.Instructions;var il=method.Body.GetILProcessor();
        var clearCalls=instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name=="Clear" && ((MethodReference)i.Operand).DeclaringType is GenericInstanceType &&
            ((MethodReference)i.Operand).DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<")).ToArray();
        var anchors=clearCalls.Where(call=>call.Previous!=null && call.Previous.OpCode==OpCodes.Ldfld &&
            call.Previous.Operand is FieldReference && ((FieldReference)call.Previous.Operand).FullName==remove.FullName).ToArray();
        if(anchors.Length!=1)throw new Exception("Expected unique chunksToRemove.Clear anchor in UpdGen; found "+anchors.Length);
        var containsCalls=instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name=="Contains" && ((MethodReference)i.Operand).DeclaringType is GenericInstanceType &&
            ((GenericInstanceType)((MethodReference)i.Operand).DeclaringType).ElementType.FullName=="System.Collections.Generic.List`1" &&
            ((GenericInstanceType)((MethodReference)i.Operand).DeclaringType).GenericArguments.Count==1 &&
            ((GenericInstanceType)((MethodReference)i.Operand).DeclaringType).GenericArguments[0].FullName==vector.FullName).ToArray();
        var targetCalls=containsCalls.Where(call=>RecentFieldLoad(call,needed)!=null).ToArray();
        if(targetCalls.Length!=1)throw new Exception("Expected unique chunksNeeded cleanup membership site in UpdGen; found "+targetCalls.Length);
        var anchor=anchors[0];
        Instruction[] init={Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,needed),
            Instruction.Create(OpCodes.Newobj,ctor),Instruction.Create(OpCodes.Stloc,local)};
        Instruction cursor=anchor;foreach(var instruction in init){il.InsertAfter(cursor,instruction);cursor=instruction;}
        var neededLoad=RecentFieldLoad(targetCalls[0],needed);
        if(neededLoad.Previous==null || neededLoad.Previous.OpCode!=OpCodes.Ldarg_0)
            throw new Exception("Unexpected chunksNeeded receiver in cleanup membership expression.");
        var receiver=neededLoad.Previous;receiver.OpCode=OpCodes.Ldloc;receiver.Operand=local;
        instructions.Remove(neededLoad);
        targetCalls[0].Operand=contains;
    }
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length!=4 || File.Exists(args[2]))throw new Exception("Usage: PatchStability V1 RUNTIME NEW_OUTPUT GAME_MANAGED");
            using(var sha=SHA256.Create())using(var s=File.OpenRead(args[0]))if(BitConverter.ToString(sha.ComputeHash(s)).Replace("-","")!="ACF994A64E82564E471B487A9CE31853894880D142370C9204592047AB217165")throw new Exception("Unrecognized v1 baseline.");
            using(var resolver=new DefaultAssemblyResolver())
            {
                resolver.AddSearchDirectory(args[3]);resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
                using(var game=AssemblyDefinition.ReadAssembly(args[0],new ReaderParameters{AssemblyResolver=resolver}))
                using(var runtime=AssemblyDefinition.ReadAssembly(args[1],new ReaderParameters{AssemblyResolver=resolver}))
                {
                    module=game.MainModule;bridge=runtime.MainModule.Types.Single(t=>t.FullName=="TldMp.StableGameBridge");
                    string hood="GeneszSteamP2PBase.SteamP2PNetworkingUnderTheHoodScript";
                    // Test transport identity: stock values whenever loopback is inactive.
                    Replace(Method(hood,"OwnID",0),"LoopbackOwnID");
                    Replace(Method("SteamP2PMenuHandlerScript","GetSpawnType",0),"LoopbackSpawnType");
                    Replace(Method("syncScript","GetPhysicsLock",0),"LoopbackPhysicsLock");
                    // Transport adapters and the incremental item scheduler.
                    Replace(Method("syncScript","SyncStuff",0),"SyncItems",-1);
                    Replace(Method(hood,"SendBytes",2),"RouteBroadcast",0,1);
                    Replace(Method(hood,"SendBytesToTarget",3),"RouteTarget",0,1,2);
                    Replace(Method(hood,"ReadMessage",0),"ReadBudgeted",-1);
                    // Pure relay de-duplication: suppress only the echo of a message
                    // that is currently being dispatched locally. No authority.
                    Guard(Method(hood,"ForwardMessage",3),"AllowForward");
                    // Structural packet validation only. The runtime guard must not
                    // reject native traffic for owner/epoch/lease reasons.
                    Guard(Method("GeneszSteamP2PBase.networkingScript","ProcessMessage",3),"AllowIncoming",0,1);
                    // Log-flood fix: NetLog always returns false, so the stock DebugLog body is
                    // skipped and StableGameBridge.NetLog performs the rate-limited equivalent.
                    Guard(Method("GeneszSteamP2PBase.networkingScript","DebugLog",1),"NetLog",0);
                    // Preserve native bodies while invalidating deferred movement
                    // around actual teleport and seat-entry/exit transitions.
                    AfterReturn(Method("fpscontroller","Teleport",2,"UnityEngine.Vector3"),"AfterNativeSpawnTeleport",-1);
                    AfterReturn(Method("fpscontroller","GetIn",1,"seatscript"),"AfterNativeSeatEntry",-1,0);
                    Entry(Method("fpscontroller","GetOut",2,"UnityEngine.Vector3"),"BeforeNativeSeatExit",-1,1);
                    AfterReturn(Method("fpscontroller","GetOut",2,"UnityEngine.Vector3"),"AfterNativeSeatExit",-1);
                    // Keep both stock retry loops; guard every return path's
                    // IEnumerator so each MoveNext checks its native ticket.
                    WrapReturn(Method("syncScript","ItemPosLocal",5,"System.UInt32"),"WrapNativeLocalPose",0,1,2);
                    WrapReturn(Method("syncScript","UpdAttachable",12,"tosaveitemscript"),"WrapNativeAttachment",0,1,2,3,4,7);
                    // Native ID allocation offline; collision-free allocation in a session.
                    ConnectedReplacement(Method("savedatascript","GetNewKey",0),"NewKey",-1);
                    ConnectedReplacement(Method("savedatascript","GetNewKeyIgnoreSave",0),"NewKey",-1);
                    // World streaming: native distances, placement flags, LODs and cleanup.
                    Guard(Method("roadGenScript","Upd",1,"System.Boolean"),"RoadUpdate",-1,0);
                    Guard(Method("roadGenScript","Upd",1,"Vector3d"),"RoadAt",-1,0);
                    Entry(Method("roadGenScript/roadClass","Generate",1,"roadGenScript"),"RoadGenerated",0);
                    Guard(Method("poiGenScript","GenChunk",1),"PoiChunk",-1,0);
                    // Observe only after the native path is admitted by the guard.
                    // Guard is inserted before this prefix, so a rejected return
                    // never opens an unmatched native timing span.
                    Observe(Method("poiGenScript","AddPois",0),"NativePoiNativeAddBegin","NativePoiNativeAddEnd",1,new int[0],new int[0]);
                    Guard(Method("poiGenScript","AddPois",0),"PoiAdd",-1);
                    // Diagnostics only: read-only bus-stop ground logging at placement and spawn.
                    Entry(module.GetTypes().Single(x=>x.FullName=="poiGenScript").Methods.Single(m=>m.Name=="AddPoi" && m.Parameters.Count==4 && m.Parameters[2].ParameterType.FullName=="System.Int32"),"BusGroundPlaced",0,1,2,3);
                    Entry(Method("poiScript","FStart",2),"BusGroundSpawned",-1,0,1);
                    ObserveValue(Method("poiGenScript","Spawn",1,"poiClass"),"NativePoiSpawnBegin","NativePoiSpawnEnd",1,new int[0],new int[0]);
                    BracketCalls(Method("poiGenScript","Spawn",1,"poiClass"),"UnityEngine.Object","Instantiate",3,1,"NativePoiInstantiateBegin","NativePoiInstantiateEnd");
                    // The 92.97 ms frame 5331 stall is inside poiGenScript.Upd but
                    // outside the timed AddPois/Spawn paths. Split the remaining
                    // branches without changing any decision or callback.
                    Observe(Method("poiScript","SpawnStuff",1,"poiClass"),"NativePoiStuffBegin","NativePoiStuffEnd",2,new int[0],new int[0]);
                    Observe(Method("poiScript","ItemSpawn",0),"NativePoiItemsBegin","NativePoiItemsEnd",1,new int[0],new int[0]);
                    Observe(Method("poiScript","FixedSpawn",0),"NativePoiFixedBegin","NativePoiFixedEnd",1,new int[0],new int[0]);
                    Observe(Method("poiGenScript","RemovePoi",1,"poiClass"),"NativePoiRemoveBegin","NativePoiRemoveEnd",2,new int[0],new int[0]);
                    Guard(Method("objGenScript/categoryClass","UpdChunksNeeded",0),"CategoryChunks",-1);
                    Guard(Method("objGenScript/categoryClass","UpdGen",1),"CategoryGen",-1,0);
                    OptimizeCategoryCleanup();
                    Entry(Method("mainscript","Update",0),"NativeMainBegin");
                    AfterReturns(Method("mainscript","Update",0),"NativeMainEnd",3);
                    Entry(Method("mapSettingScript","Update",0),"NativeMapBegin");
                    AfterReturns(Method("mapSettingScript","Update",0),"NativeMapEnd",2);
                    Entry(Method("objGenScript","Upd",1),"NativeObjectsBegin");
                    AfterReturns(Method("objGenScript","Upd",1),"NativeObjectsEnd",1);
                    Entry(Method("itemPlaceRemoveScript","Upd",0),"NativeItemsBegin");
                    AfterReturns(Method("itemPlaceRemoveScript","Upd",0),"NativeItemsEnd",1);
                    OptimizeItemSelection();
                    OptimizePlaceOrder();
                    OptimizeTerrainHoles();
                    OptimizeTerrainRoads();
                    OptimizeTerrainNoisePerm();
                    OptimizeRoadChunkChurn();
                    OptimizeRoadLiveCache();
                    OptimizeRoadFarSkip();
                    OptimizeMirrorOnScreen();
                    OptimizeRoadBake();
                    OptimizeCarPoseStart();
                    OptimizePlayerLeave();
                    OptimizeItemAsk();
                    OptimizeAutoStore();
                    OptimizeParkedCarSleep();
                    OptimizeIdlePourPause();
                    OptimizeWornDestroyed();
                    OptimizeSliceGuard();
                    // Item streaming: parented items are measured from their root, and a
                    // frozen item stays frozen while no ground is loaded under it (20 s cap).
                    OptimizeItemStreamAnchor();
                    OptimizeTerrainAlignNested();
                    AfterReturn(Method("poiGenScript/poiClass","UpdTerrainAligns",2),"TerrainAlignLookupRange",-1);
                    // Far item placement is spread over frames; PlaceStuff keeps its selection and removal.
                    OptimizeItemPlaceSpread();
                    Guard(Method("tosaveitemscript","DistUnFreezeRB",0),"ItemUnfreezeGate",-1);
                    // Kinematic creatures: skip velocity writes Unity rejects (rabbits are re-enabled).
                    Guard(Method("movecreaturescript","Move",0),"CreatureMoveGate",-1);
                    Guard(Method("movecreaturescript","Jump",0),"CreatureJumpGate",-1);
                    Guard(Method("tosaveitemscript","ThisOneInChargeCar",1),"CarChargeGate",-1,0);
                    // Remote smoothing. GotUpd and the car GotSync guard only record or withhold
                    // their own bookkeeping; Upd overwrites the pose after the native body runs.
                    Entry(Method("syncScript/playerSyncClass","GotUpd",9),"RemotePlayerSample",-1,0,1,3);
                    AfterReturns(Method("syncScript/playerSyncClass","Upd",0),"RemotePlayerPose",3,new int[]{-1});
                    Guard(Method("tosaveitemscript","GotSync",2),"CarSyncGotSync",-1,0,1);
                    // P0 diagnostic observers. These are entry/exit timing only;
                    // the native bodies and their decision paths are unchanged.
                    // Bounded behavior-entry counters for the unattributed script bucket.
                    // Inclusive per-behaviour timings. These are the same call
                    // sites as the old entry counters plus their return hooks;
                    // the begin hooks still increment the behaviours{} counts.
                    Observe(Method("mainscript","MirrorsUpdate",3),"NativeBehaviorMirrorsBegin","NativeBehaviorMirrorsEnd",2,new int[0],new int[0]);
                    Observe(Method("reflectionMainScript","MoveReflectionCam",2),"NativeBehaviorReflectionBegin","NativeBehaviorReflectionEnd",1,new int[0],new int[0]);
                    Observe(Method("tosaveitemscript","MultiUpd",0),"NativeBehaviorMultiBegin","NativeBehaviorMultiEnd",3,new int[0],new int[0]);
                    Observe(Method("carscript","Update",0),"NativeBehaviorCarBegin","NativeBehaviorCarEnd",3,new int[0],new int[0]);
                    Observe(Method("audioBasedOnInteriorsensscript","Update",0),"NativeBehaviorAudioBegin","NativeBehaviorAudioEnd",2,new int[0],new int[0]);
                    Observe(Method("sensorScript","Update",0),"NativeBehaviorSensorBegin","NativeBehaviorSensorEnd",1,new int[0],new int[0]);
                    Observe(Method("movecreaturescript","Update",0),"NativeBehaviorCreatureBegin","NativeBehaviorCreatureEnd",1,new int[0],new int[0]);
                    Observe(Method("newAiScript","Update",0),"NativeBehaviorAiBegin","NativeBehaviorAiEnd",2,new int[0],new int[0]);
                    Observe(Method("vultureAI","Update",0),"NativeBehaviorVultureBegin","NativeBehaviorVultureEnd",1,new int[0],new int[0]);
                    Observe(Method("randomFlyer","Update",0),"NativeBehaviorFlyerBegin","NativeBehaviorFlyerEnd",1,new int[0],new int[0]);
                    Observe(Method("attachablescript","Update",0),"NativeBehaviorAttachablesBegin","NativeBehaviorAttachablesEnd",2,new int[0],new int[0]);
                    Observe(Method("partconditionscript","Update",0),"NativeBehaviorPartConditionsBegin","NativeBehaviorPartConditionsEnd",2,new int[0],new int[0]);
                    Observe(Method("usablescript","Update",0),"NativeBehaviorUsablesBegin","NativeBehaviorUsablesEnd",1,new int[0],new int[0]);
                    Observe(Method("wheelgraphicsscript","Update",0),"NativeBehaviorWheelGraphicsBegin","NativeBehaviorWheelGraphicsEnd",1,new int[0],new int[0]);
                    // Split the wheel Update into its two stock calls for the optimisation pass.
                    Observe(Method("wheelgraphicsscript","Graphics",0),"NativeWheelGraphicsBegin","NativeWheelGraphicsEnd",3,new int[0],new int[0]);
                    Observe(Method("wheelgraphicsscript","WheelSize",0),"NativeWheelSizeBegin","NativeWheelSizeEnd",1,new int[0],new int[0]);
                    Observe(Method("syncScript","Update",0),"NativeBehaviorSyncBegin","NativeBehaviorSyncEnd",1,new int[0],new int[0]);
                    // Floating-origin shift timing only; no behaviour change.
                    Observe(Method("mainscript","VisszaRakas",0),"NativeVisszaRakasBegin","NativeVisszaRakasEnd",2,new int[0],new int[0]);
                    OptimizeFlyerKinematicVelocity();
                    Observe(Method("objGenScript","UpdGen",1),"NativeObjGenBegin","NativeObjGenEnd",1,new int[0],new int[0]);
                    Observe(Method("objGenScript","UpdPlaceRemove",1),"NativeObjPlaceBegin","NativeObjPlaceEnd",1,new int[0],new int[0]);
                    Observe(Method("objGenScript/categoryClass","UpdPlaceRemove",1),"NativeCategoryPlaceBegin","NativeCategoryPlaceEnd",1,new int[]{-1},new int[0]);
                    var poiUpd=Method("poiGenScript","Upd",1);
                    var spawnStuffCall=poiUpd.Body.Instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName=="poiScript" && ((MethodReference)i.Operand).Name=="SpawnStuff").ToArray();
                    if(spawnStuffCall.Length!=1)throw new Exception("Expected one SpawnStuff call");
                    spawnStuffCall[0].OpCode=OpCodes.Call;spawnStuffCall[0].Operand=Hook("SpawnStuffAfterBusPlacement");
                    Observe(poiUpd,"NativePoiBegin","NativePoiEnd",2,new int[0],new int[0]);
                    Observe(Method("roadGenScript","UpdChunksNeeded",0),"NativeRoadNeededBegin","NativeRoadNeededEnd",1,new int[0],new int[0]);
                    Observe(Method("roadGenScript","UpdPlaceRemove",1),"NativeRoadPlaceBegin","NativeRoadPlaceEnd",1,new int[0],new int[0]);
                    BracketCalls(Method("roadGenScript","UpdPlaceRemove",1),"UnityEngine.Object","Instantiate",2,1,"NativeRoadInstantiateBegin","NativeRoadInstantiateEnd");
                    BracketCalls(Method("roadGenScript","UpdPlaceRemove",1),"roadscript","Init",5,1,"NativeRoadInitBegin","NativeRoadInitEnd");
                    BracketCalls(Method("roadGenScript","UpdPlaceRemove",1),"roadscript","Place",1,1,"NativeRoadGeometryBegin","NativeRoadGeometryEnd");
                    BracketCalls(Method("roadGenScript","UpdPlaceRemove",1),"roadscript","PowerPole",1,1,"NativeRoadPoleBegin","NativeRoadPoleEnd");
                    BracketCalls(Method("roadGenScript","UpdPlaceRemove",1),"UnityEngine.Object","Destroy",1,2,"NativeRoadDestroyBegin","NativeRoadDestroyEnd");
                    // Observe before Guard: the guard is inserted ahead of the
                    // entry observer, so a guard short-circuit is never timed as
                    // native Upd work.
                    Observe(Method("terrainGenScript","Upd",0),"NativeTerrainBegin","NativeTerrainEnd",3,new int[]{-1},new int[]{-1});
                    Guard(Method("terrainGenScript","Upd",0),"Terrain",-1);
                    Observe(Method("terrainGenScript","DoPlace",0),"NativeTerrainPlaceBegin","NativeTerrainPlaceEnd",1,new int[0],new int[0]);
                    TerrainTileWorker();
                    Observe(Method("terrainGenScript","CheckPlace",0),"NativeTerrainCheckBegin","NativeTerrainCheckEnd",1,new int[0],new int[0]);
                    ObserveValue(Method("terrainGenScript","GenerateTileMesh",1,"Vector3d"),"NativeTerrainMeshBegin","NativeTerrainMeshEnd",1,new int[]{-1},new int[]{-1});
                    Observe(Method("terrainGenScript","PlaceMesh",2,"Vector3d"),"NativeTerrainPlaceMeshBegin","NativeTerrainPlaceMeshEnd",1,new int[0],new int[0]);
                    Observe(Method("newTerrainTileScript","FStart",6,"System.String"),"NativeTerrainTileStartBegin","NativeTerrainTileStartEnd",1,new int[0],new int[0]);
                    Observe(Method("newTerrainTileScript","UpdMesh",1,"UnityEngine.Mesh"),"NativeTerrainMeshUploadBegin","NativeTerrainMeshUploadEnd",1,new int[0],new int[0]);
                    Observe(Method("thumbleweedMain","Upd",0),"NativeThumbleweedBegin","NativeThumbleweedEnd",2,new int[0],new int[0]);
                    Guard(Method("terrainGenScript","CheckRemove",0),"TerrainCheckRemove",-1);
                    Entry(Method("SteamP2PMenuHandlerScript","APressedMulti",0),"PrewarmContentFingerprint",-1);
                    // Terrain noise race fix and the loopback damage identity.
                    LocalizeNoise();
                    FixLocalIdentity();
                    PatchVersionLabel();
                    // Transport bootstrap and Steam callback de-duplication.
                    var init=Method(hood,"Init",0);var il=init.Body.GetILProcessor();foreach(var ret in init.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray()){ret.OpCode=OpCodes.Call;ret.Operand=Hook("Bootstrap");il.InsertAfter(ret,Instruction.Create(OpCodes.Ret));}
                    // Injected instructions can move branch targets out of short range.
                    // Widening is limited to methods this patch actually touches.
                    foreach(var t in module.GetTypes())foreach(var m in t.Methods.Where(x=>x.HasBody && (x.Body.Instructions.Any(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName==bridge.FullName) || x.DeclaringType.FullName=="terrainGenSettings/Noises" && x.Name=="Noise")))
                    foreach(var i in m.Body.Instructions)if(i.OpCode.OperandType==OperandType.ShortInlineBrTarget){string field=i.OpCode.Code.ToString();i.OpCode=(OpCode)typeof(OpCodes).GetField(field.Substring(0,field.Length-2)).GetValue(null);}
                    int sites=0,hooked=0;
                    foreach(var t in module.GetTypes())foreach(var m in t.Methods.Where(x=>x.HasBody))
                    {
                        int n=m.Body.Instructions.Count(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName==bridge.FullName);
                        if(n>0){sites+=n;hooked++;}
                    }
                    // Native profiler seam observers add no gameplay control or state.
                    // Noises.Noise remains the only changed method without a bridge call.
                    if(hooked!=98 || sites!=203)throw new Exception("Unexpected hook scope: methods="+hooked+" sites="+sites);
                    game.Write(args[2]);Console.WriteLine("Built native-restore assembly: "+args[2]+" ("+hooked+" hooked methods, "+sites+" bridge call sites)");
                }
            }
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}


