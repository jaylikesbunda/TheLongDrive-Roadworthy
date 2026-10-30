using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Verifies the native-restore patch scope against the v1 input: exactly the
// KEEP hooks may change, and every removed hook method must still match the
// pristine IL with no StableGameBridge call site left behind.
public static class VerifyStability
{
    static TypeReference LoadedType(MethodDefinition method,int argument)
    {
        if(argument==-1)
        {
            if(!method.HasThis)throw new Exception("Static method has no this argument: "+method.FullName);
            return method.DeclaringType;
        }
        if(argument<0 || argument>=method.Parameters.Count)throw new Exception("Invalid native argument index: "+method.FullName+" arg="+argument);
        return method.Parameters[argument].ParameterType;
    }
    static bool StackCompatible(TypeReference source,TypeReference target)
    {
        if(source.FullName==target.FullName)return true;
        try
        {
            var sourceDefinition=source.Resolve();
            if(sourceDefinition!=null && sourceDefinition.IsEnum)
            {
                var underlying=sourceDefinition.Fields.Single(f=>f.Name=="value__").FieldType;
                if(underlying.FullName==target.FullName)return true;
            }
            var targetDefinition=target.Resolve();
            if(targetDefinition!=null && targetDefinition.IsEnum)
            {
                var underlying=targetDefinition.Fields.Single(f=>f.Name=="value__").FieldType;
                if(underlying.FullName==source.FullName)return true;
            }
        }
        catch(AssemblyResolutionException){}
        return false;
    }
    static MethodDefinition BridgeDefinition(TypeDefinition bridge,MethodReference call)
    {
        if(call==null || call.DeclaringType.FullName!=bridge.FullName)throw new Exception("Hook call does not target StableGameBridge.");
        var matches=bridge.Methods.Where(m=>m.FullName==call.FullName).ToArray();
        if(matches.Length!=1)throw new Exception("Hook target is missing or ambiguous in runtime helper: "+call.FullName);
        if(call.HasThis || matches[0].HasThis)throw new Exception("Bridge hook must be static: "+call.FullName);
        if(!matches[0].IsPublic)throw new Exception("Bridge hook called from the native game assembly must be public: "+call.FullName);
        return matches[0];
    }
    static void ValidateHook(MethodDefinition native,MethodReference call,TypeDefinition bridge,int[] loads,bool returnedValueOnStack,string expectedReturn)
    {
        var hook=BridgeDefinition(bridge,call);
        int offset=returnedValueOnStack?1:0;
        if(hook.Parameters.Count!=loads.Length+offset)throw new Exception("Hook argument count mismatch: "+native.FullName+" -> "+call.FullName);
        if(returnedValueOnStack && native.ReturnType.FullName!=hook.Parameters[0].ParameterType.FullName)
            throw new Exception("Hook return-stack argument mismatch: "+native.FullName+" -> "+call.FullName);
        for(int i=0;i<loads.Length;i++)
        {
            var source=LoadedType(native,loads[i]);var target=hook.Parameters[i+offset].ParameterType;
            if(!StackCompatible(source,target))throw new Exception("Hook argument type mismatch: "+native.FullName+" argument "+loads[i]+" -> "+call.FullName+" expects "+target.FullName+" but loads "+source.FullName);
        }
        if(hook.ReturnType.FullName!=expectedReturn)throw new Exception("Hook return type mismatch: "+native.FullName+" -> "+call.FullName+" returns "+hook.ReturnType.FullName+", expected "+expectedReturn);
    }
    static bool IsBridge(Instruction i){return i.OpCode==OpCodes.Call && i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName=="TldMp.StableGameBridge";}
    static bool IsLoad(Instruction instruction,int argument,MethodDefinition method)
    {
        if(argument==-1)return instruction.OpCode==OpCodes.Ldarg_0;
        return instruction.OpCode==OpCodes.Ldarg && instruction.Operand is ParameterDefinition &&
            ((ParameterDefinition)instruction.Operand).Index==argument;
    }
    static void RedirectBranches(MethodDefinition method,Instruction from,Instruction to)
    {
        foreach(var i in method.Body.Instructions)
        {
            if(i.Operand==from)i.Operand=to;
            var targets=i.Operand as Instruction[];
            if(targets!=null)for(int n=0;n<targets.Length;n++)if(targets[n]==from)targets[n]=to;
        }
    }
    static void VerifyAndRestoreExceptionEnd(MethodDefinition native,MethodDefinition patched,Instruction nativeRet,Instruction first,Instruction stop)
    {
        if(native.Body.ExceptionHandlers.Count!=patched.Body.ExceptionHandlers.Count)
            throw new Exception("Observer changed exception-handler count: "+native.FullName);
        for(int i=0;i<native.Body.ExceptionHandlers.Count;i++)
        {
            var source=native.Body.ExceptionHandlers[i];var target=patched.Body.ExceptionHandlers[i];
            bool nativeTryAtRet=source.TryEnd==nativeRet;
            bool nativeHandlerAtRet=source.HandlerEnd==nativeRet;
            if((target.TryEnd==first)!=nativeTryAtRet || (target.HandlerEnd==first)!=nativeHandlerAtRet)
                throw new Exception("Exit observer crossed an exception-region end boundary: "+native.FullName);
            if(target.TryEnd==first)target.TryEnd=stop;
            if(target.HandlerEnd==first)target.HandlerEnd=stop;
        }
    }
    static void StripEntry(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string name,int[] loads)
    {
        var instructions=patched.Body.Instructions;
        if(instructions.Count<loads.Length+1)throw new Exception("Short entry hook: "+native.FullName);
        for(int i=0;i<loads.Length;i++)if(!IsLoad(instructions[i],loads[i],native))throw new Exception("Wrong entry hook load: "+native.FullName);
        var call=instructions[loads.Length];var target=call.Operand as MethodReference;
        if(call.OpCode!=OpCodes.Call || target==null || target.Name!=name || target.DeclaringType.FullName!="TldMp.StableGameBridge")
            throw new Exception("Wrong entry observer: "+native.FullName);
        ValidateHook(native,target,bridge,loads,false,"System.Void");
        for(int i=loads.Length;i>=0;i--)instructions.RemoveAt(i);
    }
    static void StripReturn(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string name,int[] loads,bool hasReturnValue,int expectedReturns)
    {
        var returns=patched.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
        var calls=patched.Body.Instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name==name).ToArray();
        if(returns.Length!=expectedReturns || calls.Length!=expectedReturns)
            throw new Exception("Wrong exact return-hook count for "+native.FullName+": returns="+returns.Length+", hooks="+calls.Length+", expected="+expectedReturns);
        foreach(var callInstruction in calls)
        {
            Instruction ret=callInstruction.Next;
            if(ret==null || ret.OpCode!=OpCodes.Ret)throw new Exception("Return observer is not on the return path: "+native.FullName);
            Instruction first=callInstruction;
            for(int i=loads.Length-1;i>=0;i--)
            {
                first=first.Previous;
                if(first==null || !IsLoad(first,loads[i],native))throw new Exception("Wrong return observer load: "+native.FullName);
            }
            ValidateHook(native,(MethodReference)callInstruction.Operand,bridge,loads,hasReturnValue,
                hasReturnValue?native.ReturnType.FullName:"System.Void");
            Instruction stop=callInstruction.Next;
            var nativeReturns=native.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
            var patchedReturns=patched.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
            int returnIndex=Array.IndexOf(patchedReturns,stop);
            if(returnIndex<0 || returnIndex>=nativeReturns.Length)throw new Exception("Cannot map observed return to native method: "+native.FullName);
            VerifyAndRestoreExceptionEnd(native,patched,nativeReturns[returnIndex],first,stop);
            RedirectBranches(patched,first,ret);
            for(Instruction i=first;i!=ret;)
            {Instruction next=i.Next;patched.Body.Instructions.Remove(i);i=next;}
        }
    }
    static void StripReturns(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string name,int expectedReturns)
    {
        StripReturns(native,patched,bridge,name,expectedReturns,new int[0]);
    }
    static void StripReturns(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string name,int expectedReturns,int[] loads)
    {
        var returns=patched.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
        var calls=patched.Body.Instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name==name).ToArray();
        int nativeReturns=native.Body.Instructions.Count(i=>i.OpCode==OpCodes.Ret);
        if(nativeReturns!=expectedReturns || returns.Length!=expectedReturns || calls.Length!=expectedReturns)
            throw new Exception("Wrong bounded return-hook count for "+native.FullName+": nativeReturns="+nativeReturns+", patchedReturns="+returns.Length+", hooks="+calls.Length+", expected="+expectedReturns);
        foreach(var call in calls)
        {
            if(call.Next==null || call.Next.OpCode!=OpCodes.Ret)throw new Exception("Return observer is not immediately before ret: "+native.FullName);
            Instruction first=call;
            for(int i=loads.Length-1;i>=0;i--)
            {
                first=first.Previous;
                if(first==null || !IsLoad(first,loads[i],native))throw new Exception("Wrong return observer load: "+native.FullName);
            }
            ValidateHook(native,(MethodReference)call.Operand,bridge,loads,false,"System.Void");
            Instruction stop=call.Next;
            var nativeReturnsForMap=native.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
            var patchedReturnsForMap=patched.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
            int returnIndex=Array.IndexOf(patchedReturnsForMap,stop);
            if(returnIndex<0 || returnIndex>=nativeReturnsForMap.Length)throw new Exception("Cannot map observed return to native method: "+native.FullName);
            VerifyAndRestoreExceptionEnd(native,patched,nativeReturnsForMap[returnIndex],first,stop);
            RedirectBranches(patched,first,stop);
            for(Instruction i=first;i!=stop;)
            {Instruction next=i.Next;patched.Body.Instructions.Remove(i);i=next;}
        }
    }
    static int[] ParseLoads(string value)
    {
        if(value==null || value.Length==0 || value=="-")return new int[0];
        return value.Split(',').Select(Int32.Parse).ToArray();
    }
    static void StripGuard(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string name,int[] loads)
    {
        var instructions=patched.Body.Instructions;
        int count=loads.Length+3;
        if(instructions.Count<count)throw new Exception("Short guard prefix: "+native.FullName);
        for(int i=0;i<loads.Length;i++)if(!IsLoad(instructions[i],loads[i],native))throw new Exception("Wrong guard load: "+native.FullName);
        var call=instructions[loads.Length];var target=call.Operand as MethodReference;
        if(call.OpCode!=OpCodes.Call || target==null || target.Name!=name || target.DeclaringType.FullName!=bridge.FullName)
            throw new Exception("Wrong guard observer: "+native.FullName);
        ValidateHook(native,target,bridge,loads,false,"System.Boolean");
        var branch=instructions[loads.Length+1];
        if(branch.OpCode!=OpCodes.Brtrue)throw new Exception("Bad guard branch: "+native.FullName);
        Instruction body=(Instruction)branch.Operand;
        if(instructions[loads.Length+2].OpCode!=OpCodes.Ret)
            throw new Exception("Bad guard false return: "+native.FullName);
        for(int i=0;i<count;i++)instructions.RemoveAt(0);
        if(instructions.Count==0 || instructions[0]!=body)throw new Exception("Guard target is not the entry observer: "+native.FullName);
    }
    static bool IsGenericCall(Instruction instruction,OpCode opcode,string methodName,string genericType)
    {
        var method=instruction.Operand as MethodReference;
        return instruction.OpCode==opcode && method!=null && method.Name==methodName &&
            method.DeclaringType is GenericInstanceType &&
            ((GenericInstanceType)method.DeclaringType).ElementType.FullName==genericType;
    }
    static Instruction CloneInstruction(Instruction source)
    {
        if(source.Operand==null)return Instruction.Create(source.OpCode);
        if(source.Operand is MethodReference)return Instruction.Create(source.OpCode,(MethodReference)source.Operand);
        if(source.Operand is FieldReference)return Instruction.Create(source.OpCode,(FieldReference)source.Operand);
        if(source.Operand is VariableDefinition)return Instruction.Create(source.OpCode,(VariableDefinition)source.Operand);
        if(source.Operand is TypeReference)return Instruction.Create(source.OpCode,(TypeReference)source.Operand);
        if(source.Operand is string)return Instruction.Create(source.OpCode,(string)source.Operand);
        if(source.Operand is Instruction)return Instruction.Create(source.OpCode,(Instruction)source.Operand);
        if(source.Operand is Instruction[])return Instruction.Create(source.OpCode,(Instruction[])source.Operand);
        if(source.Operand is sbyte)return Instruction.Create(source.OpCode,(sbyte)source.Operand);
        if(source.Operand is byte)return Instruction.Create(source.OpCode,(byte)source.Operand);
        if(source.Operand is int)return Instruction.Create(source.OpCode,(int)source.Operand);
        if(source.Operand is long)return Instruction.Create(source.OpCode,(long)source.Operand);
        if(source.Operand is float)return Instruction.Create(source.OpCode,(float)source.Operand);
        if(source.Operand is double)return Instruction.Create(source.OpCode,(double)source.Operand);
        throw new Exception("Unsupported instruction operand: "+source.Operand.GetType().Name);
    }
    static void StripNoisePerm(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge)
    {
        if(patched.Body.Variables.Count!=native.Body.Variables.Count+1)
            throw new Exception("Noise perm local count changed: "+native.FullName);
        var local=patched.Body.Variables[patched.Body.Variables.Count-1];
        if(local.VariableType.FullName!="System.Int32[]")throw new Exception("Noise perm local is not int[].");
        var list=patched.Body.Instructions;
        if(list.Count<2 || list[0].OpCode!=OpCodes.Call || !(list[0].Operand is MethodReference) ||
           list[1].OpCode!=OpCodes.Stloc || list[1].Operand!=local)
            throw new Exception("Noise perm prefix is malformed: "+native.FullName);
        var getPerm=(MethodReference)list[0].Operand;
        if(getPerm.DeclaringType.FullName!="NoiseS3D" || getPerm.Name!="get_perm" || getPerm.Parameters.Count!=0 ||
           getPerm.ReturnType.FullName!="System.Int32[]")
            throw new Exception("Noise perm getter signature changed: "+native.FullName);
        int edits=0;
        for(int i=2;i<list.Count;i++)
        {
            var instruction=list[i];
            if(instruction.OpCode==OpCodes.Ldloc && instruction.Operand==local)
            {instruction.OpCode=OpCodes.Call;instruction.Operand=getPerm;edits++;}
        }
        if(edits!=12)throw new Exception("Noise perm read count changed: "+native.FullName+" edits="+edits);
        list.RemoveAt(0);list.RemoveAt(0);
        patched.Body.Variables.RemoveAt(patched.Body.Variables.Count-1);
    }
    static void StripRoadLiveCache(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge)
    {
        var instructions=patched.Body.Instructions;
        var dirty=instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name=="RoadChunkDirty").ToArray();
        var guard=instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name=="RoadChunkMayHaveLive").ToArray();
        var destroyed=instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name=="RoadChunkDestroyed").ToArray();
        if(dirty.Length!=1 || guard.Length!=1 || destroyed.Length!=1)
            throw new Exception("Road live cache call count changed: "+native.FullName);
        var dirtyDefinition=BridgeDefinition(bridge,(MethodReference)dirty[0].Operand);
        if(dirtyDefinition.ReturnType.FullName!="System.Void" || dirtyDefinition.Parameters.Count!=1 ||
           dirtyDefinition.Parameters[0].ParameterType.FullName!="roadGenScript/roadChunk")
            throw new Exception("Road live cache dirty signature changed: "+native.FullName);
        var guardDefinition=BridgeDefinition(bridge,(MethodReference)guard[0].Operand);
        if(guardDefinition.ReturnType.FullName!="System.Boolean" || guardDefinition.Parameters.Count!=2 ||
           guardDefinition.Parameters[0].ParameterType.FullName!="roadGenScript/dotChunkClass" ||
           guardDefinition.Parameters[1].ParameterType.FullName!="roadGenScript/roadChunk")
            throw new Exception("Road live cache guard signature changed: "+native.FullName);
        var destroyedDefinition=BridgeDefinition(bridge,(MethodReference)destroyed[0].Operand);
        if(destroyedDefinition.ReturnType.FullName!="System.Void" || destroyedDefinition.Parameters.Count!=1 ||
           destroyedDefinition.Parameters[0].ParameterType.FullName!="roadGenScript/roadChunk")
            throw new Exception("Road live cache destroyed signature changed: "+native.FullName);

        var guardCall=guard[0];var guardRoad=guardCall.Previous;var guardDcc=guardRoad==null?null:guardRoad.Previous;
        var guardSelf=guardDcc==null?null:guardDcc.Previous;var guardBranch=guardCall.Next;
        if(guardSelf==null || guardSelf.OpCode!=OpCodes.Ldarg_0 ||
           guardDcc==null || guardDcc.OpCode!=OpCodes.Ldfld || !(guardDcc.Operand is FieldReference) ||
           ((FieldReference)guardDcc.Operand).Name!="dcc0" ||
           guardRoad==null || (guardRoad.OpCode!=OpCodes.Ldloc && guardRoad.OpCode!=OpCodes.Ldloc_S) || !(guardRoad.Operand is VariableDefinition) ||
           ((VariableDefinition)guardRoad.Operand).VariableType.FullName!="roadGenScript/roadChunk" ||
           guardBranch==null || (guardBranch.OpCode!=OpCodes.Brfalse && guardBranch.OpCode!=OpCodes.Brfalse_S))
            throw new Exception("Road live cache guard pattern changed: "+native.FullName);
        var target=guardBranch.Operand as Instruction;
        if(target==null || target.Previous==null || target.Previous.OpCode!=OpCodes.Endfinally ||
           target.OpCode!=OpCodes.Ldloc_1)
            throw new Exception("Road live cache guard target changed: "+native.FullName);

        var dirtyCall=dirty[0];var dirtyRoad=dirtyCall.Previous;
        if(dirtyRoad==null || (dirtyRoad.OpCode!=OpCodes.Ldloc && dirtyRoad.OpCode!=OpCodes.Ldloc_S) || !(dirtyRoad.Operand is VariableDefinition) ||
           ((VariableDefinition)dirtyRoad.Operand).VariableType.FullName!="roadGenScript/roadChunk" ||
           dirtyCall.Next==null)
            throw new Exception("Road live cache dirty pattern changed: "+native.FullName);
        var destroyedCall=destroyed[0];var destroyedRoad=destroyedCall.Previous;
        if(destroyedRoad==null || (destroyedRoad.OpCode!=OpCodes.Ldloc && destroyedRoad.OpCode!=OpCodes.Ldloc_S) || !(destroyedRoad.Operand is VariableDefinition) ||
           ((VariableDefinition)destroyedRoad.Operand).VariableType.FullName!="roadGenScript/roadChunk")
            throw new Exception("Road live cache destroyed pattern changed: "+native.FullName);

        instructions.Remove(guardSelf);instructions.Remove(guardDcc);instructions.Remove(guardRoad);
        instructions.Remove(guardCall);instructions.Remove(guardBranch);
        instructions.Remove(dirtyRoad);instructions.Remove(dirtyCall);
        instructions.Remove(destroyedRoad);instructions.Remove(destroyedCall);
        if(patched.Body.Variables.Count!=native.Body.Variables.Count || patched.Body.ExceptionHandlers.Count!=native.Body.ExceptionHandlers.Count)
            throw new Exception("Road live cache changed locals/handlers: "+native.FullName);
    }
    // Exactly one native callvirt Renderer::get_isVisible became
    // ldarg _cam; call MirrorVisible(Renderer, Camera).
    static void StripMirrorVisible(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge)
    {
        var nativeReads=native.Body.Instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="UnityEngine.Renderer" && ((MethodReference)i.Operand).Name=="get_isVisible").ToArray();
        if(nativeReads.Length!=1)throw new Exception("Native mirror isVisible anchor changed: "+native.FullName);
        var calls=patched.Body.Instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name=="MirrorVisible").ToArray();
        if(calls.Length!=1)throw new Exception("Mirror on-screen call count changed: "+native.FullName);
        if(patched.Body.Instructions.Any(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="UnityEngine.Renderer" && ((MethodReference)i.Operand).Name=="get_isVisible"))
            throw new Exception("Mirror on-screen left a native isVisible read: "+native.FullName);
        var definition=BridgeDefinition(bridge,(MethodReference)calls[0].Operand);
        if(definition.ReturnType.FullName!="System.Boolean" || definition.Parameters.Count!=2 ||
           definition.Parameters[0].ParameterType.FullName!="UnityEngine.Renderer" ||
           definition.Parameters[1].ParameterType.FullName!="UnityEngine.Camera")
            throw new Exception("Mirror on-screen signature changed: "+native.FullName);
        var call=calls[0];var load=call.Previous;
        if(load==null || !IsLoad(load,1,native) || native.Parameters[1].ParameterType.FullName!="UnityEngine.Camera")
            throw new Exception("Mirror on-screen does not pass _cam: "+native.FullName);
        if(patched.Body.Instructions.Any(i=>i.Operand==load || i.Operand==call))
            throw new Exception("Mirror on-screen instructions are branch targets: "+native.FullName);
        patched.Body.Instructions.Remove(load);
        call.OpCode=OpCodes.Callvirt;call.Operand=nativeReads[0].Operand;
    }
    // Exactly ldarg.0; ldfld chunksNeeded; call RoadDedupeNeeded(List<Vector3d>)
    // at method entry, and nowhere else.
    static void StripRoadDedupe(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge)
    {
        var instructions=patched.Body.Instructions;
        var calls=instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name=="RoadDedupeNeeded").ToArray();
        if(calls.Length!=1 || instructions.Count<3 || instructions[2]!=calls[0])
            throw new Exception("Road dedupe must be the single entry prefix: "+native.FullName);
        var definition=BridgeDefinition(bridge,(MethodReference)calls[0].Operand);
        if(definition.ReturnType.FullName!="System.Void" || definition.Parameters.Count!=1 ||
           definition.Parameters[0].ParameterType.FullName!="System.Collections.Generic.List`1<UnityEngine.Vector3d>")
            throw new Exception("Road dedupe signature changed: "+native.FullName);
        if(instructions[0].OpCode!=OpCodes.Ldarg_0 || instructions[1].OpCode!=OpCodes.Ldfld ||
           !(instructions[1].Operand is FieldReference) || ((FieldReference)instructions[1].Operand).Name!="chunksNeeded" ||
           ((FieldReference)instructions[1].Operand).DeclaringType.FullName!="roadGenScript")
            throw new Exception("Road dedupe prefix pattern changed: "+native.FullName);
        if(instructions.Any(i=>i.Operand==instructions[0] || i.Operand==instructions[1] || i.Operand==instructions[2]))
            throw new Exception("Road dedupe prefix is a branch target: "+native.FullName);
        instructions.RemoveAt(0);instructions.RemoveAt(0);instructions.RemoveAt(0);
    }
    // Exactly ldarg.0; ldarg.0; ldfld dcc0; call RoadChunkSkippable; brtrue T
    // immediately after the native TryGetValue(..., out dcc0) brfalse T.
    static void StripRoadFarSkip(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge)
    {
        var instructions=patched.Body.Instructions;
        var calls=instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name=="RoadChunkSkippable").ToArray();
        if(calls.Length!=1)throw new Exception("Road far skip call count changed: "+native.FullName);
        var definition=BridgeDefinition(bridge,(MethodReference)calls[0].Operand);
        if(definition.ReturnType.FullName!="System.Boolean" || definition.Parameters.Count!=2 ||
           definition.Parameters[0].ParameterType.FullName!="roadGenScript" ||
           definition.Parameters[1].ParameterType.FullName!="roadGenScript/dotChunkClass")
            throw new Exception("Road far skip signature changed: "+native.FullName);
        var call=calls[0];var field=call.Previous;var receiver=field==null?null:field.Previous;var self=receiver==null?null:receiver.Previous;
        var branch=call.Next;var missing=self==null?null:self.Previous;var tryGet=missing==null?null:missing.Previous;
        if(field==null || field.OpCode!=OpCodes.Ldfld || !(field.Operand is FieldReference) || ((FieldReference)field.Operand).Name!="dcc0" ||
           receiver==null || receiver.OpCode!=OpCodes.Ldarg_0 || self==null || self.OpCode!=OpCodes.Ldarg_0 ||
           branch==null || branch.OpCode!=OpCodes.Brtrue ||
           missing==null || (missing.OpCode!=OpCodes.Brfalse && missing.OpCode!=OpCodes.Brfalse_S) ||
           tryGet==null || !(tryGet.Operand is MethodReference) || ((MethodReference)tryGet.Operand).Name!="TryGetValue" ||
           branch.Operand!=missing.Operand)
            throw new Exception("Road far skip pattern changed: "+native.FullName);
        var inserted=new[]{self,receiver,field,call,branch};
        if(instructions.Any(i=>inserted.Contains(i.Operand as Instruction)))
            throw new Exception("Road far skip instructions are branch targets: "+native.FullName);
        foreach(var i in inserted)instructions.Remove(i);
    }
    static void StripRoadChunkKeep(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string hookName)
    {
        var nativeCalls=native.Body.Instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="System.Collections.Generic.List`1<UnityEngine.Vector3d>" &&
            ((MethodReference)i.Operand).Name=="Contains" && ((MethodReference)i.Operand).Parameters.Count==1).ToArray();
        if(nativeCalls.Length!=1)throw new Exception("Native road chunk Contains anchor changed: "+native.FullName);
        var calls=patched.Body.Instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name==hookName).ToArray();
        if(calls.Length!=1)throw new Exception("Wrong road chunk keep hook count: "+native.FullName);
        var call=calls[0];
        var keyCall=call.Previous;
        if(keyCall==null || !(keyCall.Operand is MethodReference) || ((MethodReference)keyCall.Operand).Name!="get_Key")
            throw new Exception("Road chunk keep lost its key anchor: "+native.FullName);
        var address=keyCall.Previous;
        var field=address==null?null:address.Previous;
        if(field==null || field.OpCode!=OpCodes.Ldfld || !(field.Operand is FieldReference) ||
           ((FieldReference)field.Operand).Name!="chunksNeeded")
            throw new Exception("Road chunk keep lost its chunksNeeded anchor: "+native.FullName);
        var thisLoad=field.Previous;
        if(thisLoad==null || thisLoad.OpCode!=OpCodes.Ldarg_0)
            throw new Exception("Road chunk keep lost its receiver anchor: "+native.FullName);
        var injected=thisLoad.Previous;
        if(injected==null || injected.OpCode!=OpCodes.Ldarg_0)
            throw new Exception("Road chunk keep is missing its injected receiver: "+native.FullName);
        var definition=BridgeDefinition(bridge,(MethodReference)call.Operand);
        if(definition.Parameters.Count!=3 || definition.ReturnType.FullName!="System.Boolean" ||
           definition.Parameters[0].ParameterType.FullName!="roadGenScript" ||
           definition.Parameters[1].ParameterType.FullName!="System.Collections.Generic.List`1<UnityEngine.Vector3d>" ||
           definition.Parameters[2].ParameterType.FullName!="UnityEngine.Vector3d")
            throw new Exception("Road chunk keep signature changed: "+native.FullName);
        patched.Body.Instructions.Remove(injected);
        call.OpCode=nativeCalls[0].OpCode;
        call.Operand=nativeCalls[0].Operand;
        if(patched.Body.Variables.Count!=native.Body.Variables.Count || patched.Body.ExceptionHandlers.Count!=native.Body.ExceptionHandlers.Count)
            throw new Exception("Road chunk keep changed locals/handlers: "+native.FullName);
    }
    static void StripCallSwap(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string hookName,int expected,
        string nativeType,string nativeName,int nativeParams,string expectedReturn,params string[] expectedParams)
    {
        if(patched.Body.Instructions.Count!=native.Body.Instructions.Count)
            throw new Exception("Call-swap changed instruction count: "+native.FullName);
        int found=0;
        for(int i=0;i<patched.Body.Instructions.Count;i++)
        {
            var pi=patched.Body.Instructions[i];
            if(!IsBridge(pi) || ((MethodReference)pi.Operand).Name!=hookName)continue;
            var ni=native.Body.Instructions[i];
            if((ni.OpCode!=OpCodes.Call && ni.OpCode!=OpCodes.Callvirt) || !(ni.Operand is MethodReference))
                throw new Exception("Call-swap lost its native target: "+native.FullName);
            var target=(MethodReference)ni.Operand;
            if(target.DeclaringType.FullName!=nativeType || target.Name!=nativeName || target.Parameters.Count!=nativeParams)
                throw new Exception("Call-swap native target changed: "+native.FullName);
            var definition=BridgeDefinition(bridge,(MethodReference)pi.Operand);
            if(definition.ReturnType.FullName!=expectedReturn || definition.Parameters.Count!=expectedParams.Length)
                throw new Exception("Call-swap signature changed: "+native.FullName);
            for(int p=0;p<expectedParams.Length;p++)
                if(definition.Parameters[p].ParameterType.FullName!=expectedParams[p])
                    throw new Exception("Call-swap parameter changed: "+native.FullName);
            pi.OpCode=ni.OpCode;pi.Operand=ni.Operand;found++;
        }
        if(found!=expected)throw new Exception("Call-swap count mismatch: "+native.FullName+" found="+found+" expected="+expected);
    }
    static void StripCarPoseStart(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string gateName,string startName)
    {
        var instructions=patched.Body.Instructions;
        if(instructions.Count<native.Body.Instructions.Count+6)
            throw new Exception("Car pose factory prefix missing: "+native.FullName);
        var item1=instructions[0];var gate=instructions[1];var branch=instructions[2];var item2=instructions[3];var start=instructions[4];var ret=instructions[5];
        if(item1.OpCode!=OpCodes.Ldarg_0 || item2.OpCode!=OpCodes.Ldarg_0 ||
           !IsBridge(gate) || ((MethodReference)gate.Operand).Name!=gateName ||
           !IsBridge(start) || ((MethodReference)start.Operand).Name!=startName ||
           branch.OpCode!=OpCodes.Brfalse || branch.Operand!=instructions[6] || ret.OpCode!=OpCodes.Ret)
            throw new Exception("Car pose factory control flow changed: "+native.FullName);
        var gateDef=BridgeDefinition(bridge,(MethodReference)gate.Operand);
        if(gateDef.ReturnType.FullName!="System.Boolean" || gateDef.Parameters.Count!=1 ||
           gateDef.Parameters[0].ParameterType.FullName!="tosaveitemscript")
            throw new Exception("Car pose gate signature changed: "+native.FullName);
        var startDef=BridgeDefinition(bridge,(MethodReference)start.Operand);
        if(startDef.ReturnType.FullName!="System.Collections.IEnumerator" || startDef.Parameters.Count!=1 ||
           startDef.Parameters[0].ParameterType.FullName!="tosaveitemscript")
            throw new Exception("Car pose coroutine signature changed: "+native.FullName);
        for(int i=0;i<6;i++)instructions.RemoveAt(0);
        if(patched.Body.Variables.Count!=native.Body.Variables.Count || patched.Body.ExceptionHandlers.Count!=native.Body.ExceptionHandlers.Count)
            throw new Exception("Car pose factory changed locals/handlers: "+native.FullName);
    }
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
    static void StripTerrainRoadDedupe(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string hookName)
    {
        var nativeRoads=native.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName=="mapSettingScript" &&
            ((MethodReference)i.Operand).Name=="RoadsNear" && ((MethodReference)i.Operand).Parameters.Count==1).ToArray();
        if(nativeRoads.Length!=1)throw new Exception("Native terrain RoadsNear anchor changed: "+native.FullName);
        var nativeLocal=StoredLocal(native,nativeRoads[0].Next);
        if(nativeLocal==null)throw new Exception("Native terrain RoadsNear store changed: "+native.FullName);
        var calls=patched.Body.Instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name==hookName).ToArray();
        if(calls.Length!=1)throw new Exception("Wrong terrain road dedupe hook count: "+native.FullName);
        var call=calls[0];var load=call.Previous;var store=call.Next;
        if(load==null || store==null || load.OpCode!=OpCodes.Ldloc || store.OpCode!=OpCodes.Stloc ||
           !(load.Operand is VariableDefinition) || load.Operand!=store.Operand)
            throw new Exception("Terrain road dedupe is not anchored on a local: "+native.FullName);
        var loadLocal=(VariableDefinition)load.Operand;
        if(loadLocal.Index!=nativeLocal.Index)
            throw new Exception("Terrain road dedupe is not anchored on the native RoadsNear local: "+native.FullName);
        if(load.Previous==null || StoredLocal(patched,load.Previous)!=load.Operand)
            throw new Exception("Terrain road dedupe is not immediately after RoadsNear: "+native.FullName);
        if(BranchTargetCount(patched,load)!=0 || BranchTargetCount(patched,call)!=0 || BranchTargetCount(patched,store)!=0)
            throw new Exception("Terrain road dedupe introduced a branch target: "+native.FullName);
        var definition=BridgeDefinition(bridge,(MethodReference)call.Operand);
        if(definition.Parameters.Count!=1 || definition.Parameters[0].ParameterType.FullName!=definition.ReturnType.FullName ||
           definition.Parameters[0].ParameterType.FullName!="System.Collections.Generic.List`1<roadGenScript/roadClass>")
            throw new Exception("Terrain road dedupe signature changed: "+native.FullName);
        patched.Body.Instructions.Remove(load);
        patched.Body.Instructions.Remove(call);
        patched.Body.Instructions.Remove(store);
    }
    static void StripPlaceOrder(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string name)
    {
        var calls=patched.Body.Instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name==name).ToArray();
        if(calls.Length!=1)throw new Exception("Wrong SortPlaceOrder hook count: "+native.FullName);
        var call=calls[0];
        Instruction load2=call.Previous;Instruction this2=load2==null?null:load2.Previous;
        Instruction load1=this2==null?null:this2.Previous;Instruction this1=load1==null?null:load1.Previous;
        if(this1==null || this1.OpCode!=OpCodes.Ldarg_0 || load1.OpCode!=OpCodes.Ldfld ||
           this2.OpCode!=OpCodes.Ldarg_0 || load2.OpCode!=OpCodes.Ldfld ||
           call.Next==null)throw new Exception("Bad SortPlaceOrder prologue: "+native.FullName);
        var placeField=native.DeclaringType.Fields.Single(f=>f.Name=="placeOrder");
        var firstField=native.DeclaringType.Fields.Single(f=>f.Name=="firstGenAround");
        if(!(load1.Operand is FieldReference) || ((FieldReference)load1.Operand).FullName!=placeField.FullName ||
           !(load2.Operand is FieldReference) || ((FieldReference)load2.Operand).FullName!=firstField.FullName)
            throw new Exception("SortPlaceOrder wiring uses the wrong fields: "+native.FullName);
        BridgeDefinition(bridge,(MethodReference)call.Operand);
        var firstGen=native.DeclaringType.Fields.Single(f=>f.Name=="firstGenAround");
        var place=native.DeclaringType.Fields.Single(f=>f.Name=="placeOrder");
        var stores=native.Body.Instructions.Where(i=>i.OpCode==OpCodes.Stfld && i.Operand is FieldReference &&
            ((FieldReference)i.Operand).FullName==firstGen.FullName).ToArray();
        if(stores.Length!=1)throw new Exception("Bad native firstGenAround store count: "+native.FullName);
        Instruction start=stores[0].Next;Instruction end=start;
        while(end!=null && !(end.OpCode==OpCodes.Stfld && end.Operand is FieldReference &&
            ((FieldReference)end.Operand).FullName==place.FullName))end=end.Next;
        if(end==null)throw new Exception("Bad native placeOrder sort chain: "+native.FullName);
        var after=call.Next;var il=patched.Body.GetILProcessor();
        for(Instruction i=start;i!=end.Next;i=i.Next)
        {
            Instruction clone=CloneInstruction(i);
            il.InsertBefore(after,clone);
        }
        Instruction stop=call.Next;
        for(Instruction i=this1;i!=stop;){Instruction next=i.Next;il.Remove(i);i=next;}
        if(patched.Body.Variables.Count!=native.Body.Variables.Count || patched.Body.ExceptionHandlers.Count!=native.Body.ExceptionHandlers.Count)
            throw new Exception("SortPlaceOrder changed locals/handlers: "+native.FullName);
    }
    static bool InTry(MethodDefinition method,Instruction instruction)
    {
        int index=method.Body.Instructions.IndexOf(instruction);
        foreach(var handler in method.Body.ExceptionHandlers)
        {
            int start=method.Body.Instructions.IndexOf(handler.TryStart);
            int end=method.Body.Instructions.IndexOf(handler.TryEnd);
            if(index>=start && index<end)return true;
        }
        return false;
    }
    static Instruction RecentFieldLoad(Instruction from,string fieldName)
    {
        Instruction i=from;
        for(int n=0;n<6 && i!=null;n++,i=i.Previous)
            if(i.OpCode==OpCodes.Ldfld && i.Operand is FieldReference && ((FieldReference)i.Operand).Name==fieldName)return i;
        return null;
    }
    static Instruction RecentLocalLoad(Instruction from,VariableDefinition local)
    {
        Instruction i=from;
        for(int n=0;n<6 && i!=null;n++,i=i.Previous)
            if(i.OpCode==OpCodes.Ldloc && i.Operand==local)return i;
        return null;
    }
    static void StripCategoryCleanupOptimization(MethodDefinition native,MethodDefinition patched)
    {
        var nativeNeeded=native.DeclaringType.Fields.Single(f=>f.Name=="chunksNeeded");
        var nativeRemove=native.DeclaringType.Fields.Single(f=>f.Name=="chunksToRemove");
        var vector=((GenericInstanceType)nativeNeeded.FieldType).GenericArguments[0];
        if(patched.Body.Variables.Count!=native.Body.Variables.Count+1)
            throw new Exception("Unexpected UpdGen cleanup local count.");
        var local=patched.Body.Variables[patched.Body.Variables.Count-1];
        var localType=local.VariableType as GenericInstanceType;
        if(localType==null || localType.ElementType.FullName!="System.Collections.Generic.HashSet`1" ||
            localType.GenericArguments.Count!=1 || localType.GenericArguments[0].FullName!=vector.FullName)
            throw new Exception("Wrong UpdGen cleanup HashSet local type.");
        var instructions=patched.Body.Instructions;
        var constructors=instructions.Where(i=>IsGenericCall(i,OpCodes.Newobj,".ctor","System.Collections.Generic.HashSet`1") &&
            ((MethodReference)i.Operand).DeclaringType.FullName==localType.FullName).ToArray();
        var containsCalls=instructions.Where(i=>IsGenericCall(i,OpCodes.Callvirt,"Contains","System.Collections.Generic.HashSet`1") &&
            ((MethodReference)i.Operand).DeclaringType.FullName==localType.FullName).ToArray();
        if(constructors.Length!=1 || containsCalls.Length!=1)
            throw new Exception("Wrong exact UpdGen cleanup HashSet instruction counts: ctor="+constructors.Length+", contains="+containsCalls.Length);
        var constructor=constructors[0];var contains=containsCalls[0];
        if(((MethodReference)constructor.Operand).Parameters.Count!=1 ||
            ((MethodReference)constructor.Operand).Parameters[0].ParameterType.FullName!="System.Collections.Generic.IEnumerable`1<!0>" ||
            ((MethodReference)contains.Operand).Parameters.Count!=1 ||
            ((MethodReference)contains.Operand).Parameters[0].ParameterType.FullName!="!0")
            throw new Exception("Wrong UpdGen cleanup HashSet constructor signature.");
        var receiver=RecentLocalLoad(contains,local);
        if(receiver==null)
            throw new Exception("HashSet.Contains does not use the injected local.");
        var clearCalls=instructions.Where(i=>IsGenericCall(i,OpCodes.Callvirt,"Clear","System.Collections.Generic.List`1")).ToArray();
        var anchors=clearCalls.Where(call=>call.Previous!=null && call.Previous.OpCode==OpCodes.Ldfld &&
            call.Previous.Operand is FieldReference && ((FieldReference)call.Previous.Operand).Name==nativeRemove.Name).ToArray();
        if(anchors.Length!=1)throw new Exception("Missing unique chunksToRemove.Clear cleanup anchor.");
        Instruction start=anchors[0].Next;
        if(start==null || start.OpCode!=OpCodes.Ldarg_0 || start.Next==null || start.Next.OpCode!=OpCodes.Ldfld ||
            !(((FieldReference)start.Next.Operand).Name==nativeNeeded.Name) || start.Next.Next!=constructor ||
            constructor.Next==null || constructor.Next.OpCode!=OpCodes.Stloc || constructor.Next.Operand!=local)
            throw new Exception("HashSet construction is not anchored immediately after chunksToRemove.Clear.");
        var nativeContains=native.Body.Instructions.Where(i=>IsGenericCall(i,OpCodes.Callvirt,"Contains","System.Collections.Generic.List`1") &&
            RecentFieldLoad(i,nativeNeeded.Name)!=null).ToArray();
        if(nativeContains.Length!=1)throw new Exception("Native UpdGen cleanup membership site is not unique.");
        receiver.OpCode=OpCodes.Ldarg_0;receiver.Operand=null;
        var loadField=Instruction.Create(OpCodes.Ldfld,patched.DeclaringType.Fields.Single(f=>f.Name==nativeNeeded.Name));
        instructions.Insert(instructions.IndexOf(receiver)+1,loadField);
        contains.Operand=nativeContains[0].Operand;
        Instruction[] injected={start,start.Next,constructor,constructor.Next};
        foreach(var instruction in injected)instructions.Remove(instruction);
        patched.Body.Variables.Remove(local);
    }
    static bool IsNamedBridge(Instruction i,string name)
    {var call=i==null?null:i.Operand as MethodReference;return IsBridge(i) && call.Name==name;}
    static int BranchTargetCount(MethodDefinition method,Instruction target)
    {
        int count=0;
        foreach(var i in method.Body.Instructions)
        {
            if(i.Operand==target)count++;
            var targets=i.Operand as Instruction[];if(targets!=null)count+=targets.Count(x=>x==target);
        }
        return count;
    }
    static void StripBracketCalls(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string beginName,string endName,string declaringType,string methodName,int parameterCount,int expectedCount)
    {
        Func<MethodDefinition,Instruction[]> targetCalls=delegate(MethodDefinition m){return m.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Call || i.OpCode==OpCodes.Callvirt) && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName==declaringType && ((MethodReference)i.Operand).Name==methodName &&
            ((MethodReference)i.Operand).Parameters.Count==parameterCount).ToArray();};
        var original=targetCalls(native);var actual=targetCalls(patched);
        var begins=patched.Body.Instructions.Where(i=>IsNamedBridge(i,beginName)).ToArray();
        var ends=patched.Body.Instructions.Where(i=>IsNamedBridge(i,endName)).ToArray();
        if(original.Length!=expectedCount || actual.Length!=expectedCount || begins.Length!=expectedCount || ends.Length!=expectedCount)
            throw new Exception("Wrong exact native call bracket count for "+native.FullName+" "+declaringType+"."+methodName+": source="+original.Length+", call="+actual.Length+", begin="+begins.Length+", end="+ends.Length+", expected="+expectedCount);
        for(int n=0;n<expectedCount;n++)
        {
            var call=actual[n];var begin=call.Previous;var end=call.Next;
            if(begin==null || end==null || begin!=begins[n] || end!=ends[n])throw new Exception("Native bracket is not Begin/call/End adjacent: "+native.FullName+" "+methodName);
            BridgeDefinition(bridge,(MethodReference)begin.Operand);BridgeDefinition(bridge,(MethodReference)end.Operand);
            ValidateHook(patched,(MethodReference)begin.Operand,bridge,new int[0],false,"System.Void");
            ValidateHook(patched,(MethodReference)end.Operand,bridge,new int[0],false,"System.Void");
            if(((MethodReference)original[n].Operand).FullName!=((MethodReference)call.Operand).FullName)
                throw new Exception("Bracketed native call signature changed: "+native.FullName+" "+methodName);
            if(BranchTargetCount(patched,call)!=0 || BranchTargetCount(patched,begin)!=BranchTargetCount(native,original[n]))
                throw new Exception("Branches do not enter native bracket through Begin: "+native.FullName+" "+methodName);
            for(int h=0;h<native.Body.ExceptionHandlers.Count;h++)
            {
                var source=native.Body.ExceptionHandlers[h];var target=patched.Body.ExceptionHandlers[h];
                if((source.TryStart==original[n])!=(target.TryStart==begin) || (source.HandlerStart==original[n])!=(target.HandlerStart==begin) ||
                   (source.TryEnd==original[n])!=(target.TryEnd==begin) || (source.HandlerEnd==original[n])!=(target.HandlerEnd==begin))
                    throw new Exception("Native call bracket changed an exception boundary: "+native.FullName+" "+methodName);
                if(target.TryStart==begin)target.TryStart=call;if(target.HandlerStart==begin)target.HandlerStart=call;
                if(target.TryEnd==begin)target.TryEnd=call;if(target.HandlerEnd==begin)target.HandlerEnd=call;
            }
            RedirectBranches(patched,begin,call);
            patched.Body.Instructions.Remove(begin);patched.Body.Instructions.Remove(end);
        }
    }
    static void StripBracketSet(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string specification)
    {
        if(String.IsNullOrEmpty(specification))throw new Exception("Missing native call-bracket specification: "+native.FullName);
        foreach(string item in specification.Split(';'))
        {
            string[] fields=item.Split(',');if(fields.Length!=6)throw new Exception("Bad native call-bracket specification: "+item);
            StripBracketCalls(native,patched,bridge,fields[0],fields[1],fields[2],fields[3],Int32.Parse(fields[4]),Int32.Parse(fields[5]));
        }
    }
    static void StripFlyerKinematicGuard(MethodDefinition native,MethodDefinition patched,TypeDefinition bridge,string hookName)
    {
        Func<MethodDefinition,Instruction[]> setters=delegate(MethodDefinition m){return m.Body.Instructions.Where(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name=="set_velocity" && ((MethodReference)i.Operand).DeclaringType.FullName=="UnityEngine.Rigidbody" &&
            ((MethodReference)i.Operand).Parameters.Count==1 && ((MethodReference)i.Operand).Parameters[0].ParameterType.FullName=="UnityEngine.Vector3").ToArray();};
        var originalSetters=setters(native);var patchedSetters=setters(patched);
        if(originalSetters.Length!=1 || patchedSetters.Length!=1)throw new Exception("Flyer velocity setter count changed.");
        var originalSetter=originalSetters[0];var setter=patchedSetters[0];
        if(((MethodReference)originalSetter.Operand).FullName!=((MethodReference)setter.Operand).FullName)throw new Exception("Flyer native setter signature changed.");
        if(patched.Body.Variables.Count!=native.Body.Variables.Count+1 || patched.Body.ExceptionHandlers.Count!=native.Body.ExceptionHandlers.Count || patched.Body.InitLocals!=native.Body.InitLocals)
            throw new Exception("Flyer kinematic guard changed locals or method initialization state.");
        var value=patched.Body.Variables[patched.Body.Variables.Count-1];
        if(value.VariableType.FullName!="UnityEngine.Vector3")throw new Exception("Flyer guard local is not the native velocity type.");
        var list=patched.Body.Instructions;int at=list.IndexOf(setter);
        if(at<8)throw new Exception("Flyer guard is missing before the native setter.");
        var injected=list.Skip(at-8).Take(8).ToArray();
        if(injected[0].OpCode!=OpCodes.Stloc || injected[0].Operand!=value || injected[1].OpCode!=OpCodes.Dup ||
           injected[2].OpCode!=OpCodes.Callvirt || !(injected[2].Operand is MethodReference) ||
           ((MethodReference)injected[2].Operand).Name!="get_isKinematic" || ((MethodReference)injected[2].Operand).ReturnType.FullName!="System.Boolean" ||
           ((MethodReference)injected[2].Operand).DeclaringType.FullName!="UnityEngine.Rigidbody" || ((MethodReference)injected[2].Operand).Parameters.Count!=0 ||
           injected[3].OpCode!=OpCodes.Brfalse || injected[3].Operand!=injected[7] ||
           !IsBridge(injected[4]) || ((MethodReference)injected[4].Operand).Name!=hookName || injected[5].OpCode!=OpCodes.Pop ||
           injected[6].OpCode!=OpCodes.Br || injected[6].Operand!=setter.Next || injected[7].OpCode!=OpCodes.Ldloc || injected[7].Operand!=value)
            throw new Exception("Flyer kinematic guard control flow or stack pattern changed.");
        ValidateHook(patched,(MethodReference)injected[4].Operand,bridge,new int[0],false,"System.Void");
        var nativeIndex=native.Body.Instructions.IndexOf(originalSetter);
        if(nativeIndex<0)throw new Exception("Missing stock flyer setter anchor.");
        var first=injected[0];
        for(int i=0;i<native.Body.ExceptionHandlers.Count;i++)
        {
            var source=native.Body.ExceptionHandlers[i];var target=patched.Body.ExceptionHandlers[i];
            if(target.TryStart==setter || target.HandlerStart==setter || target.TryEnd==setter || target.HandlerEnd==setter)
                throw new Exception("Flyer guard left an exception boundary at the setter instead of preserving its original range.");
            if((source.TryStart==originalSetter)!=(target.TryStart==first) || (source.HandlerStart==originalSetter)!=(target.HandlerStart==first) ||
               (source.TryEnd==originalSetter)!=(target.TryEnd==first) || (source.HandlerEnd==originalSetter)!=(target.HandlerEnd==first))
                throw new Exception("Flyer guard changed an exception-region boundary.");
            if(target.TryStart==first)target.TryStart=setter;if(target.HandlerStart==first)target.HandlerStart=setter;
            if(target.TryEnd==first)target.TryEnd=setter;if(target.HandlerEnd==first)target.HandlerEnd=setter;
        }
        foreach(var item in injected)list.Remove(item);
        patched.Body.Variables.RemoveAt(patched.Body.Variables.Count-1);
        if(VerifyPatch.Fingerprint(native)!=VerifyPatch.Fingerprint(patched))throw new Exception("Flyer kinematic edit changed native RHS, timers, rotation, or other stock IL.");
    }
    static MethodDefinition Find(Dictionary<string,TypeDefinition> types,string key)
    {
        int slash=key.LastIndexOf('/');if(slash<0)throw new Exception("Bad method key: "+key);
        string member=key.Substring(0,slash);int count=Int32.Parse(key.Substring(slash+1));
        int dot=member.LastIndexOf('.');if(dot<0)throw new Exception("Bad method key: "+key);
        string type=member.Substring(0,dot),name=member.Substring(dot+1);
        TypeDefinition t;if(!types.TryGetValue(type,out t))throw new Exception("Missing type in baseline: "+type);
        var found=t.Methods.Where(m=>m.Name==name && m.Parameters.Count==count).ToArray();
        if(found.Length!=1)throw new Exception("Missing or ambiguous baseline method: "+key+" ("+found.Length+")");
        return found[0];
    }

    public static int Main(string[] args)
    {
        try
        {
            string runtimePath=args.Length>2?args[2]:System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[1])),"Tld.Multiplayer.dll");
            using(var original=AssemblyDefinition.ReadAssembly(args[0]))using(var patched=AssemblyDefinition.ReadAssembly(args[1]))using(var runtime=AssemblyDefinition.ReadAssembly(runtimePath))
            {
                var bridge=VerifyPatch.AllTypes(runtime.MainModule.Types).Single(t=>t.FullName=="TldMp.StableGameBridge");
                var added=patched.MainModule.AssemblyReferences.Select(r=>r.Name).Except(original.MainModule.AssemblyReferences.Select(r=>r.Name)).ToArray();
                if(added.Length!=1 || added[0]!="Tld.Multiplayer")throw new Exception("Unexpected dependency set.");
                var before=VerifyPatch.AllTypes(original.MainModule.Types).ToDictionary(t=>t.FullName);var after=VerifyPatch.AllTypes(patched.MainModule.Types).ToDictionary(t=>t.FullName);
                if(!before.Keys.OrderBy(x=>x).SequenceEqual(after.Keys.OrderBy(x=>x)))throw new Exception("Changed type layout.");
                // Exactly the kept native methods. Anything else that differs is a
                // regression back toward a removed gameplay hook.
                var expected=new Dictionary<string,string>{
                    {"SteamP2PNetworkingUnderTheHoodScript.OwnID/0","R:LoopbackOwnID:"},
                    {"SteamP2PMenuHandlerScript.GetSpawnType/0","R:LoopbackSpawnType:"},
                    {"SteamP2PMenuHandlerScript.UpdLobbies/1","SW:GetLobbyVersion:Steamworks.SteamMatchmaking:GetLobbyData:2:System.String:Steamworks.CSteamID:System.String"},
                    {"SteamP2PNetworkingUnderTheHoodScript.OnCreatedLobby/1","SW:SetLobbyVersion:Steamworks.SteamMatchmaking:SetLobbyData:3:System.Boolean:Steamworks.CSteamID:System.String:System.String"},
                    {"syncScript.GetPhysicsLock/0","R:LoopbackPhysicsLock:"},
                    {"fpscontroller.RayStuff/0","WT:ResolveWheelPickup:"},
                    {"fpscontroller.Teleport/2","A:AfterNativeSpawnTeleport:-1"},
                    {"fpscontroller.GetIn/1","A:AfterNativeSeatEntry:-1,0"},
                    {"fpscontroller.GetOut/2","D:BeforeNativeSeatExit:-1,1|AfterNativeSeatExit:-1"},
                    {"syncScript.ItemPosLocal/5","W:WrapNativeLocalPose:0,1,2"},
                    {"syncScript.UpdAttachable/12","W:WrapNativeAttachment:0,1,2,3,4,7"},
                    {"syncScript.SyncStuff/0","R:SyncItems:-1"},
                    {"SteamP2PNetworkingUnderTheHoodScript.SendBytes/2","R:RouteBroadcast:0,1"},
                    {"SteamP2PNetworkingUnderTheHoodScript.SendBytesToTarget/3","R:RouteTarget:0,1,2"},
                    {"SteamP2PNetworkingUnderTheHoodScript.ReadMessage/0","R:ReadBudgeted:-1"},
                    {"SteamP2PNetworkingUnderTheHoodScript.ForwardMessage/3","G:AllowForward:"},
                    {"networkingScript.ProcessMessage/3","G:AllowIncoming:0,1"},
                    {"networkingScript.DebugLog/1","G:NetLog:0"},
                    {"savedatascript.GetNewKey/0","C:NewKey:-1"},
                    {"savedatascript.GetNewKeyIgnoreSave/0","C:NewKey:-1"},
                    {"SteamP2PNetworkingUnderTheHoodScript.Init/0","I:Bootstrap:"},
                    {"roadGenScript.Upd/1:System.Boolean","G:RoadUpdate:-1,0"},
                    {"roadGenScript.Upd/1:UnityEngine.Vector3d","G:RoadAt:-1,0"},
                    {"roadClass.Generate/1","E:RoadGenerated:0"},
                    {"poiGenScript.GenChunk/1","G:PoiChunk:-1,0"},
                    {"poiGenScript.AddPois/0","GT:PoiAdd:NativePoiNativeAddBegin:NativePoiNativeAddEnd:1:-:-"},
                    {"poiGenScript.AddPoi/4","E:BusGroundPlaced:0,1,2,3"},
                    {"poiScript.FStart/2","E:BusGroundSpawned:-1,0,1"},
                    {"poiGenScript.Spawn/1","BOV:NativePoiSpawnBegin:NativePoiSpawnEnd:1:-:-:NativePoiInstantiateBegin,NativePoiInstantiateEnd,UnityEngine.Object,Instantiate,3,1"},
                    {"categoryClass.UpdChunksNeeded/0","G:CategoryChunks:-1"},
                    {"categoryClass.UpdGen/1","GH:CategoryGen:-1,0"},
                    {"mainscript.Update/0","T:NativeMainBegin:NativeMainEnd:3:SetVersionLabel:TMPro.TMP_Text:set_text:1:System.Void:TMPro.TMP_Text:System.String"},
                    {"mapSettingScript.Update/0","T:NativeMapBegin:NativeMapEnd:2"},
                    {"objGenScript.Upd/1","T:NativeObjectsBegin:NativeObjectsEnd:1"},
                    {"itemPlaceRemoveScript.PlaceStuff/0","X:ItemSelectionBegin:ItemSelectionLookup:FP"},
                    {"itemPlaceRemoveScript.Upd/0","T:NativeItemsBegin:NativeItemsEnd:1"},
                    {"terrainGenScript.Upd/0","GT:Terrain:NativeTerrainBegin:NativeTerrainEnd:3:-1:-1"},
                    {"terrainGenScript.GenerateTileMesh/1","OVY:NativeTerrainMeshBegin:NativeTerrainMeshEnd:1:-1:-1:TerrainHoleBegin:TerrainHoleNeighbors:DedupeTerrainRoads"},
                    {"NoiseS3D.Noise/3","M:"},
                    {"roadGenScript.UpdChunksToRemove/0","K:RoadChunkKeep:RD"},
                    {"tosaveitemscript.MorePosUpd/0","CB:CarPoseNeedsBurst:CarPoseBurst"},
                    {"washtriggerscript.GotDirty/0","E:CarBloodDirty:-1"},
                    {"washtriggerscript.GotClean/0","E:CarBloodClean:-1"},
                    {"playerSyncClass.DropStuff/0","IC:1:"},
                    {"syncScript.UpdItemParent/2","IC:3:GetPlayerWithScript"},
                    {"syncScript.RecAskItemData/2","RM:"},
                    {"mountStuff.FreezeUpd/0","AS:"},
                    {"pickupable.PhysLock/1","E:NativeBeforePhysLock:-1,0"},
                    {"pickupable.JustUnPhysicsLock/0","E:NativeBeforeUnPhysicsLock:-1"},
                    {"poiScript.SpawnStuff/1","O:NativePoiStuffBegin:NativePoiStuffEnd:2:-:-"},
                    {"poiScript.ItemSpawn/0","O:NativePoiItemsBegin:NativePoiItemsEnd:1:-:-"},
                    {"poiScript.FixedSpawn/0","O:NativePoiFixedBegin:NativePoiFixedEnd:1:-:-"},
                    {"poiGenScript.RemovePoi/1","O:NativePoiRemoveBegin:NativePoiRemoveEnd:2:-:-"},
                    {"terrainGenScript.PlaceMesh/2","O:NativeTerrainPlaceMeshBegin:NativeTerrainPlaceMeshEnd:1:-:-"},
                    {"newTerrainTileScript.FStart/6","O:NativeTerrainTileStartBegin:NativeTerrainTileStartEnd:1:-:-"},
                    {"newTerrainTileScript.UpdMesh/1","O:NativeTerrainMeshUploadBegin:NativeTerrainMeshUploadEnd:1:-:-"},
                    {"terrainGenScript.DoPlace/0","OW:NativeTerrainPlaceBegin:NativeTerrainPlaceEnd:1:-:-:TerrainTile:terrainGenScript:Tile:3:System.Boolean:terrainGenScript:UnityEngine.Vector3d:System.Boolean:System.Boolean"},
                    {"terrainGenScript.CheckPlace/0","O:NativeTerrainCheckBegin:NativeTerrainCheckEnd:1:-:-"},
                    {"thumbleweedMain.Upd/0","O:NativeThumbleweedBegin:NativeThumbleweedEnd:2:-:-"},
                    {"mainscript.MirrorsUpdate/3","OM:NativeBehaviorMirrorsBegin:NativeBehaviorMirrorsEnd:2:-:-"},
                    {"roadscript.BakeCollider/0","RX:RoadBaked:1:-1"},
                    {"reflectionMainScript.MoveReflectionCam/2","O:NativeBehaviorReflectionBegin:NativeBehaviorReflectionEnd:1:-:-"},
                    {"tosaveitemscript.MultiUpd/0","OV:NativeBehaviorMultiBegin:NativeBehaviorMultiEnd:3:-:-:CarPoseMoreUpd:tosaveitemscript:MoreUpd:0:System.Boolean:tosaveitemscript"},
                    {"carscript.Update/0","OV:NativeBehaviorCarBegin:NativeBehaviorCarEnd:3:-:-:CarVelocitySet:UnityEngine.Rigidbody:set_velocity:1:System.Void:UnityEngine.Rigidbody:UnityEngine.Vector3"},
                    {"tankcapscript.Update/0","E:TankPourTick:-1"},
                    {"pickupable.OnDestroy/0","E:WornItemDestroyed:-1"},
                    {"makeinvisible.PreRender/0","E:HideListPrune:-1"},
                    {"makeinvisible.PostRender/0","E:HideListPrune:-1"},
                    {"randomSlice.Slicc/3","SW:SafeSlice:randomSlice:SSSLiceee:4:System.Void:randomSlice:System.Boolean:System.Boolean:UnityEngine.Vector3:UnityEngine.Vector3"},
                    {"audioBasedOnInteriorsensscript.Update/0","O:NativeBehaviorAudioBegin:NativeBehaviorAudioEnd:2:-:-"},
                    {"sensorScript.Update/0","O:NativeBehaviorSensorBegin:NativeBehaviorSensorEnd:1:-:-"},
                    {"movecreaturescript.Update/0","O:NativeBehaviorCreatureBegin:NativeBehaviorCreatureEnd:1:-:-"},
                    {"movecreaturescript.Move/0","G:CreatureMoveGate:-1"},
                    {"movecreaturescript.Jump/0","G:CreatureJumpGate:-1"},
                    {"tosaveitemscript.ThisOneInChargeCar/1","G:CarChargeGate:-1,0"},
                    {"newAiScript.Update/0","O:NativeBehaviorAiBegin:NativeBehaviorAiEnd:2:-:-"},
                    {"vultureAI.Update/0","O:NativeBehaviorVultureBegin:NativeBehaviorVultureEnd:1:-:-"},
                    {"randomFlyer.Update/0","O:NativeBehaviorFlyerBegin:NativeBehaviorFlyerEnd:1:-:-"},
                    {"attachablescript.Update/0","O:NativeBehaviorAttachablesBegin:NativeBehaviorAttachablesEnd:2:-:-"},
                    {"partconditionscript.Update/0","O:NativeBehaviorPartConditionsBegin:NativeBehaviorPartConditionsEnd:2:-:-"},
                    {"usablescript.Update/0","O:NativeBehaviorUsablesBegin:NativeBehaviorUsablesEnd:1:-:-"},
                    {"wheelgraphicsscript.Update/0","O:NativeBehaviorWheelGraphicsBegin:NativeBehaviorWheelGraphicsEnd:1:-:-"},
                    {"wheelgraphicsscript.Graphics/0","O:NativeWheelGraphicsBegin:NativeWheelGraphicsEnd:3:-:-"},
                    {"wheelgraphicsscript.WheelSize/0","O:NativeWheelSizeBegin:NativeWheelSizeEnd:1:-:-"},
                    {"syncScript.Update/0","O:NativeBehaviorSyncBegin:NativeBehaviorSyncEnd:1:-:-"},
                    {"mainscript.VisszaRakas/0","O:NativeVisszaRakasBegin:NativeVisszaRakasEnd:2:-:-"},
                    {"randomFlyer.Upd/0","F:NoteFlyerKinematicSkipped:"},
                    {"objGenScript.UpdGen/1","O:NativeObjGenBegin:NativeObjGenEnd:1:-:-"},
                    {"objGenScript.UpdPlaceRemove/1","O:NativeObjPlaceBegin:NativeObjPlaceEnd:1:-:-"},
                    {"categoryClass.UpdPlaceRemove/1","OS:NativeCategoryPlaceBegin:NativeCategoryPlaceEnd:1:-1:-:SortPlaceOrder"},
                    {"poiGenScript.Upd/1","OV:NativePoiBegin:NativePoiEnd:2:-:-:SpawnStuffAfterBusPlacement:poiScript:SpawnStuff:1:System.Void:poiScript:poiGenScript/poiClass"},
                    {"roadGenScript.UpdChunksNeeded/0","O:NativeRoadNeededBegin:NativeRoadNeededEnd:1:-:-"},
                    {"roadGenScript.UpdPlaceRemove/1:System.Boolean","OB:NativeRoadPlaceBegin:NativeRoadPlaceEnd:1:-:-:NativeRoadInstantiateBegin,NativeRoadInstantiateEnd,UnityEngine.Object,Instantiate,2,1;NativeRoadInitBegin,NativeRoadInitEnd,roadscript,Init,5,1;NativeRoadGeometryBegin,NativeRoadGeometryEnd,roadscript,Place,1,1;NativeRoadPoleBegin,NativeRoadPoleEnd,roadscript,PowerPole,1,1;NativeRoadDestroyBegin,NativeRoadDestroyEnd,UnityEngine.Object,Destroy,1,2:RC"},
                    {"terrainGenScript.CheckRemove/0","G:TerrainCheckRemove:-1"},
                    {"SteamP2PMenuHandlerScript.APressedMulti/0","E:PrewarmContentFingerprint:-1"},
                    {"poiClass.UpdTerrainAligns/2","A:TerrainAlignLookupRange:-1"},
                    {"alignClass..ctor/4","SW2:TerrainAlignLocal:UnityEngine.Transform:get_localPosition:0:UnityEngine.Vector3:UnityEngine.Transform"},
                    {"itemPlaceRemoveScript.RemoveStuff/0","SW:ItemStreamPosition:UnityEngine.Transform:get_position:0:UnityEngine.Vector3:UnityEngine.Transform"},
                    {"tosaveitemscript.DistUnFreezeRB/0","G:ItemUnfreezeGate:-1"},
                    {"Noises.Noise/1","N:LocalizeNoise:"},
                    {"syncScript.RecDmgPlayer/1","L:LocalIdentity:"},
                    {"playerSyncClass.GotUpd/9","E:RemotePlayerSample:-1,0,1,3"},
                    {"playerSyncClass.Upd/0","RX:RemotePlayerPose:3:-1"},
                    {"tosaveitemscript.GotSync/2","G:CarSyncGotSync:-1,0,1"},
                    {"menuhandler.FStart/0","SW:SetVersionLabel:TMPro.TMP_Text:set_text:1:System.Void:TMPro.TMP_Text:System.String"}
                };
                // Every method below must be untouched IL: its hook was removed and
                // the shipped native body restored.
                var removed=new[]{
                    "tosaveitemscript.ThisOneInCharge/0",
                    // ThisOneInChargeCar/1 carries one narrow guard again (CarChargeGate:
                    // a guest passenger never takes a car while the host is in range);
                    // the old lease/ownership system stays removed.
                    "seatscript.FreeCanSit/0","fpscontroller.Pickup/1","fpscontroller.Pickup/2","fpscontroller.Wear/1",
                    "mountStuff.Store/2","mountStuff.Unstore/0","mountStuff.QuickFreeze/1","pickupable.UnPhysicsLock/0","spawnInBoxScript.Spawn/0",
                    "syncScript.SmoothMove/3",
                    "carscript.UpdMulti/1","carscript.Balance/0","carscript.OnCollisionEnter/1","pickupable.OnCollisionEnter/1",
                    "carscript.DamageStuff/1","tosaveitemscript.OnDestroy/0",
                    "breakablescript.Break/0","breakablescript.OnCollisionEnter/1","breakablescript.OnTriggerEnter/1",
                    "savedatascript.AddedNew/1","tosaveitemscript.HasToSync/4","syncScript.RecItemPosPlayer/1",
                    "fpscontroller.Move/0","fpscontroller.PickedUpForceAdd/0","mpplayerscript.UpdCollider/2",
                    "syncScript.RecItemVelocity/1","syncScript.RecCarVelocity/1","syncScript.RecRagdoll/1",
                    "mainscript.CheckHoliday/1"
                };
                var kept=new HashSet<MethodDefinition>();
                // Snapshot the hook sites before verification reconstructs the
                // patched bodies; the reconstruction removes the hook prefix.
                var bridgeUsers=new List<MethodDefinition>();
                int hookSites=0;
                foreach(var t in after.Values)foreach(var m in t.Methods)if(m.HasBody)
                {int n=m.Body.Instructions.Count(IsBridge);if(n>0){bridgeUsers.Add(m);hookSites+=n;}}
                // Removed hooks: pristine IL, and no bridge call site anywhere in them.
                foreach(string key in removed)
                {
                    var am=Find(before,key);var bm=Find(after,key);
                    if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Removed hook still edits the native body: "+key);
                    if(bm.Body.Instructions.Any(IsBridge))throw new Exception("Removed hook still calls StableGameBridge: "+key);
                }
                int hookCount=expected.Count,changed=0,unchanged=0;
                foreach(var pair in before)
                {
                    var a=pair.Value;var b=after[pair.Key];if(!a.Fields.Select(f=>f.FullName).SequenceEqual(b.Fields.Select(f=>f.FullName)) || a.Methods.Count!=b.Methods.Count)throw new Exception("Type layout changed.");
                    foreach(var am in a.Methods)
                    {
                        var bm=b.Methods.Single(m=>m.FullName==am.FullName);if(VerifyPatch.Fingerprint(am)==VerifyPatch.Fingerprint(bm)){unchanged++;continue;}
                        changed++;kept.Add(bm);string key=a.Name+"."+am.Name+"/"+am.Parameters.Count,rule;if(a.Name=="roadGenScript" && am.Parameters.Count>0)key+=":"+am.Parameters[0].ParameterType.FullName;if(!expected.TryGetValue(key,out rule))throw new Exception("Unexpected edit: "+key);expected.Remove(key);
                        if(rule.StartsWith("WT:"))
                        {
                            var resolver=bm.Body.Instructions.Single(i=>IsBridge(i) && ((MethodReference)i.Operand).Name=="ResolveWheelPickup");
                            var gate=bm.Body.Instructions.Single(i=>IsBridge(i) && ((MethodReference)i.Operand).Name=="WheelPickupMountBlocked");
                            if(resolver.Previous.OpCode!=OpCodes.Ldarg_0 || (gate.Previous.OpCode!=OpCodes.Ldloc && gate.Previous.OpCode!=OpCodes.Ldloc_S))throw new Exception("Wheel targeting argument layout changed.");
                            var local=(VariableDefinition)gate.Previous.Operand;
                            if(local.VariableType.FullName!="pickupable")throw new Exception("Wheel gate target must be pickupable.");
                            var nativeLookup=am.Body.Instructions.Single(i=>i.Operand is GenericInstanceMethod && ((GenericInstanceMethod)i.Operand).Name=="GetComponentInParent" && ((GenericInstanceMethod)i.Operand).GenericArguments[0].FullName=="pickupable" && i.Previous.Operand is MethodReference && ((MethodReference)i.Previous.Operand).Name=="get_collider");
                            bm.Body.GetILProcessor().Remove(resolver.Previous);resolver.OpCode=nativeLookup.OpCode;resolver.Operand=nativeLookup.Operand;
                            bm.Body.GetILProcessor().Remove(gate.Previous);gate.OpCode=OpCodes.Ldfld;gate.Operand=am.Body.Instructions.Select(i=>i.Operand as FieldReference).First(f=>f!=null && f.Name=="BcanMount");
                            // The two partslot dismounts become a gated call so a
                            // shared pickup/dismount control cannot pull the tyre
                            // off while the rim is being picked up. Restore them.
                            var slotHookDef=BridgeDefinition(bridge,(MethodReference)bm.Body.Instructions.First(i=>IsBridge(i) && ((MethodReference)i.Operand).Name=="WheelSlotUnCraft").Operand);
                            if(slotHookDef.ReturnType.FullName!="System.Boolean" || slotHookDef.Parameters.Count!=2 ||
                                slotHookDef.Parameters[0].ParameterType.FullName!="partslotscript" ||
                                slotHookDef.Parameters[1].ParameterType.FullName!="fpscontroller")
                                throw new Exception("Wheel slot UnCraft hook signature changed.");
                            var nativeUnCraft=am.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Call||i.OpCode==OpCodes.Callvirt) && i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName=="partslotscript" && ((MethodReference)i.Operand).Name=="UnCraft" && ((MethodReference)i.Operand).Parameters.Count==0).ToArray();
                            var hookedUnCraft=bm.Body.Instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name=="WheelSlotUnCraft").ToArray();
                            if(nativeUnCraft.Length!=2 || hookedUnCraft.Length!=2)throw new Exception("Wheel slot UnCraft count changed: native="+nativeUnCraft.Length+" patched="+hookedUnCraft.Length);
                            for(int k=0;k<hookedUnCraft.Length;k++)
                            {
                                var h=hookedUnCraft[k];
                                if(h.Previous==null || h.Previous.OpCode!=OpCodes.Ldarg_0)throw new Exception("Wheel slot UnCraft argument layout changed.");
                                bm.Body.GetILProcessor().Remove(h.Previous);
                                h.OpCode=nativeUnCraft[k].OpCode;
                                h.Operand=nativeUnCraft[k].Operand;
                            }
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Wheel targeting changed other native instructions.");
                            continue;
                        }
                        if(rule.StartsWith("D:"))
                        {
                            var dual=rule.Substring(2).Split('|');if(dual.Length!=2)throw new Exception("Bad dual observer rule: "+key);
                            var beforeSpec=dual[0].Split(':');var afterHook=dual[1].Split(':');
                            int[] beforeLoads=beforeSpec[1]==""?new int[0]:beforeSpec[1].Split(',').Select(Int32.Parse).ToArray();
                            int[] afterLoads=afterHook[1]==""?new int[0]:afterHook[1].Split(',').Select(Int32.Parse).ToArray();
                            var seatExit=Find(after,key);
                            StripEntry(am,seatExit,bridge,beforeSpec[0],beforeLoads);
                            StripReturn(am,seatExit,bridge,afterHook[0],afterLoads,false,1);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(seatExit))throw new Exception("Seat-exit observers changed stock method body.");
                            continue;
                        }
                        string[] bits=rule.Split(':');string mode=bits[0],name=bits[1];int[] loads=(mode=="T" || mode=="O" || mode=="GT" || mode=="X" || mode=="P" || mode=="OS" || mode=="Y" || mode=="F" || mode=="BOV" || mode=="OB" || mode=="OVY" || mode=="M" || mode=="K" || mode=="CB" || mode=="IC" || mode=="RM" || mode=="AS" || mode=="OV" || mode=="OW" || mode=="SW" || mode=="SW2" || mode=="OM" || mode=="RX" || bits[2]=="")?new int[0]:bits[2].Split(',').Select(Int32.Parse).ToArray();
                        var instructions=bm.Body.Instructions;
                        if(mode=="BOV")
                        {
                            if(bits.Length!=7)throw new Exception("Bad POI spawn observer/bracket rule: "+key);
                            int returns=Int32.Parse(bits[3]);
                            StripEntry(am,bm,bridge,bits[1],ParseLoads(bits[4]));
                            StripReturn(am,bm,bridge,bits[2],ParseLoads(bits[5]),false,returns);
                            StripBracketSet(am,bm,bridge,bits[6]);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("POI spawn/Instantiate instrumentation changed native behavior: "+key);
                            continue;
                        }
                        if(mode=="OB")
                        {
                            if(bits.Length!=7 && !(bits.Length==8 && bits[7]=="RC"))throw new Exception("Bad native observer/bracket rule: "+key);
                            if(bits.Length==8){StripRoadFarSkip(am,bm,bridge);StripRoadLiveCache(am,bm,bridge);}
                            int returns=Int32.Parse(bits[3]);
                            StripEntry(am,bm,bridge,bits[1],ParseLoads(bits[4]));
                            StripReturns(am,bm,bridge,bits[2],returns,ParseLoads(bits[5]));
                            StripBracketSet(am,bm,bridge,bits[6]);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Native call bracket changed surrounding method behavior: "+key);
                            continue;
                        }
                        if(mode=="OVY")
                        {
                            if(bits.Length!=8 && bits.Length!=9)throw new Exception("Bad terrain mesh observer rule: "+key);
                            int returns=Int32.Parse(bits[3]);
                            StripEntry(am,bm,bridge,bits[1],ParseLoads(bits[4]));
                            StripEntry(am,bm,bridge,bits[6],new int[0]);
                            StripReturn(am,bm,bridge,bits[2],ParseLoads(bits[5]),false,returns);
                            var bridgeCalls=bm.Body.Instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name==bits[7]).ToArray();
                            var nativeCalls=am.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
                                ((MethodReference)i.Operand).DeclaringType.FullName=="mainscript" &&
                                ((MethodReference)i.Operand).Name=="GetConnectedVertices" && ((MethodReference)i.Operand).Parameters.Count==2).ToArray();
                            if(bridgeCalls.Length!=1 || nativeCalls.Length!=1)throw new Exception("Terrain-hole call count changed: "+key);
                            BridgeDefinition(bridge,(MethodReference)bridgeCalls[0].Operand);bridgeCalls[0].Operand=nativeCalls[0].Operand;
                            if(bits.Length==9)StripTerrainRoadDedupe(am,bm,bridge,bits[8]);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Terrain mesh observers changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="K")
                        {
                            if(bits.Length!=3 || bits[2]!="RD")throw new Exception("Bad road chunk rule: "+key);
                            StripRoadDedupe(am,bm,bridge);
                            StripRoadChunkKeep(am,bm,bridge,bits[1]);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Road chunk edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="IC")
                        {
                            if(bits.Length!=3)throw new Exception("Bad item/player call-safety rule: "+key);
                            StripCallSwap(am,bm,bridge,"IgnoreCollisionSafe",Int32.Parse(bits[1]),
                                "mpplayerscript","IgnoreCollision",2,"System.Void","mpplayerscript","UnityEngine.Transform","System.Boolean");
                            if(bits[2]=="GetPlayerWithScript")
                                StripCallSwap(am,bm,bridge,"GetPlayerWithScript",1,
                                    "syncScript","GetPlayerBySteamID",1,"syncScript/playerSyncClass","syncScript","Steamworks.CSteamID");
                            else if(bits[2]!="")throw new Exception("Bad item/player call-safety helper: "+key);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Call-safety edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="RM")
                        {
                            if(bits.Length!=2)throw new Exception("Bad ask/remove rule: "+key);
                            StripCallSwap(am,bm,bridge,"MayRemoveMissingItem",1,
                                "syncScript","SendRemoveItem",1,"System.Void","syncScript","System.UInt32");
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Ask/remove edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="AS")
                        {
                            // Exactly one FreezeUpd Store call swapped for the guest gate.
                            if(bits.Length!=2 || bits[1]!="")throw new Exception("Bad auto-store rule: "+key);
                            StripCallSwap(am,bm,bridge,"AutoStoreGate",1,
                                "mountStuff","Store",2,"System.Void","mountStuff","pickupable","System.Boolean");
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Auto-store gate changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="OW")
                        {
                            // Observer pair plus the DoPlace Tile-to-TerrainTile call swap. The observers
                            // are stripped first: StripCallSwap needs the stock instruction count.
                            if(bits.Length!=15)throw new Exception("Bad observer+terrain-tile rule: "+key);
                            int expectedReturns=Int32.Parse(bits[3]);
                            StripEntry(am,bm,bridge,bits[1],ParseLoads(bits[4]));
                            StripReturns(am,bm,bridge,bits[2],expectedReturns,ParseLoads(bits[5]));
                            StripCallSwap(am,bm,bridge,bits[6],1,bits[7],bits[8],Int32.Parse(bits[9]),bits[10],bits.Skip(11).ToArray());
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Observer+terrain-tile edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="OV")
                        {
                            // Observer pair plus one call swap: name, native type, native name, native param count, return, bridge params.
                            if(bits.Length!=12 && bits.Length!=13)throw new Exception("Bad observer+call-swap rule: "+key);
                            int expectedReturns=Int32.Parse(bits[3]);
                            StripEntry(am,bm,bridge,bits[1],ParseLoads(bits[4]));
                            StripReturns(am,bm,bridge,bits[2],expectedReturns,ParseLoads(bits[5]));
                            StripCallSwap(am,bm,bridge,bits[6],1,bits[7],bits[8],Int32.Parse(bits[9]),bits[10],bits.Skip(11).ToArray());
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Observer+call-swap edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="SW2")
                        {
                            if(bits.Length<7)throw new Exception("Bad double call-swap rule: "+key);
                            StripCallSwap(am,bm,bridge,bits[1],2,bits[2],bits[3],Int32.Parse(bits[4]),bits[5],bits.Skip(6).ToArray());
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Double call-swap edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="SW")
                        {
                            if(bits.Length<7)throw new Exception("Bad call-swap rule: "+key);
                            StripCallSwap(am,bm,bridge,bits[1],1,bits[2],bits[3],Int32.Parse(bits[4]),bits[5],bits.Skip(6).ToArray());
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Call-swap edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="CB")
                        {
                            if(bits.Length!=3)throw new Exception("Bad car pose rule: "+key);
                            StripCarPoseStart(am,bm,bridge,bits[1],bits[2]);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Car pose edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="M")
                        {
                            if(bits.Length!=2 || bits[1]!="")throw new Exception("Bad noise-perm rule: "+key);
                            StripNoisePerm(am,bm,bridge);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Noise-perm edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="F")
                        {
                            if(bits.Length!=3 || bits[2]!="")throw new Exception("Bad flyer kinematic rule: "+key);
                            StripFlyerKinematicGuard(am,bm,bridge,name);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Flyer kinematic guard changed stock behavior outside unsupported setter call: "+key);
                            continue;
                        }
                        if(mode=="X")
                        {
                            // Optional fourth field "FP": PlaceStuff's far PlaceOne call swap, stripped after the Begin pair.
                            if(bits.Length!=3 && !(bits.Length==4 && bits[3]=="FP"))throw new Exception("Bad item-selection rule: "+key);
                            var begins=instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name==bits[1]).ToArray();
                            var lookups=instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name==bits[2]).ToArray();
                            if(begins.Length!=1 || lookups.Length!=1)throw new Exception("Wrong item-selection hook count: "+key);
                            var begin=begins[0];
                            if(begin.Previous==null || begin.Previous.OpCode!=OpCodes.Ldarg_0)throw new Exception("Item-selection begin is not anchored after ldarg.0: "+key);
                            ValidateHook(am,(MethodReference)begin.Operand,bridge,new int[]{-1},false,"System.Void");
                            var lookup=lookups[0];
                            var lookupDefinition=BridgeDefinition(bridge,(MethodReference)lookup.Operand);
                            if(lookupDefinition.Parameters.Count!=3 || lookupDefinition.ReturnType.FullName!="System.Boolean")
                                throw new Exception("Item-selection lookup signature changed: "+key);
                            if(lookup.Next==null || (lookup.Next.OpCode!=OpCodes.Brfalse && lookup.Next.OpCode!=OpCodes.Brfalse_S))
                                throw new Exception("Item-selection lookup is not before its native brfalse: "+key);
                            var nativeCalls=am.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
                                ((MethodReference)i.Operand).Name=="IndexOfID" &&
                                ((MethodReference)i.Operand).DeclaringType.FullName=="savedatascript" &&
                                ((MethodReference)i.Operand).Parameters.Count==3).ToArray();
                            if(nativeCalls.Length!=2)throw new Exception("Item-selection baseline call count changed: "+key);
                            var nativeSelection=nativeCalls.Where(c=>InTry(am,c)).ToArray();
                            var nativeMutating=nativeCalls.Where(c=>!InTry(am,c)).ToArray();
                            if(nativeSelection.Length!=1 || nativeMutating.Length!=1)throw new Exception("Item-selection baseline call separation failed: "+key);
                            lookup.Operand=nativeSelection[0].Operand;
                            bm.Body.Instructions.Remove(begin.Previous);
                            bm.Body.Instructions.Remove(begin);
                            // Strip the far-place swap only after the Begin pair is gone: StripCallSwap
                            // requires the patched body to have the stock instruction count.
                            if(bits.Length==4)StripCallSwap(am,bm,bridge,"EnqueueFarPlace",1,"itemPlaceRemoveScript","PlaceOne",2,"tosaveitemscript","itemPlaceRemoveScript","System.UInt32","System.Int32");
                            var remaining=bm.Body.Instructions.Where(i=>i!=lookup && i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
                                ((MethodReference)i.Operand).Name=="IndexOfID" &&
                                ((MethodReference)i.Operand).DeclaringType.FullName=="savedatascript" &&
                                ((MethodReference)i.Operand).Parameters.Count==3).ToArray();
                            if(remaining.Length!=1 || ((MethodReference)remaining[0].Operand).FullName!=((MethodReference)nativeMutating[0].Operand).FullName)
                                throw new Exception("Item-selection mutating IndexOfID changed: "+key);
                            if(bm.Body.Variables.Count!=am.Body.Variables.Count || bm.Body.ExceptionHandlers.Count!=am.Body.ExceptionHandlers.Count)
                                throw new Exception("Item-selection changed locals/handlers: "+key);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Item-selection edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="Y")
                        {
                            if(bits.Length!=3)throw new Exception("Bad terrain-hole rule: "+key);
                            StripEntry(am,bm,bridge,bits[1],new int[0]);
                            var bridgeCalls=bm.Body.Instructions.Where(i=>IsBridge(i) && ((MethodReference)i.Operand).Name==bits[2]).ToArray();
                            var nativeCalls=am.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call && i.Operand is MethodReference &&
                                ((MethodReference)i.Operand).DeclaringType.FullName=="mainscript" &&
                                ((MethodReference)i.Operand).Name=="GetConnectedVertices" &&
                                ((MethodReference)i.Operand).Parameters.Count==2).ToArray();
                            if(bridgeCalls.Length!=1 || nativeCalls.Length!=1)throw new Exception("Terrain-hole call count changed: "+key);
                            BridgeDefinition(bridge,(MethodReference)bridgeCalls[0].Operand);
                            bridgeCalls[0].Operand=nativeCalls[0].Operand;
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Terrain-hole edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="OS")
                        {
                            if(bits.Length!=7)throw new Exception("Bad observer+sort rule: "+key);
                            int expectedReturns=Int32.Parse(bits[3]);
                            StripEntry(am,bm,bridge,bits[1],ParseLoads(bits[4]));
                            StripReturns(am,bm,bridge,bits[2],expectedReturns,ParseLoads(bits[5]));
                            StripPlaceOrder(am,bm,bridge,bits[6]);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Observer+sort edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="P")
                        {
                            if(bits.Length!=2)throw new Exception("Bad place-order rule: "+key);
                            StripPlaceOrder(am,bm,bridge,bits[1]);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Place-order edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="RX")
                        {
                            if(bits.Length!=4)throw new Exception("Bad return-observer rule: "+key);
                            StripReturns(am,bm,bridge,bits[1],Int32.Parse(bits[2]),ParseLoads(bits[3]));
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Return observer changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="OM")
                        {
                            if(bits.Length!=6)throw new Exception("Bad mirror observe rule: "+key);
                            StripMirrorVisible(am,bm,bridge);
                            StripEntry(am,bm,bridge,bits[1],ParseLoads(bits[4]));
                            StripReturns(am,bm,bridge,bits[2],Int32.Parse(bits[3]),ParseLoads(bits[5]));
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Mirror on-screen edit changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="O")
                        {
                            if(bits.Length!=6)throw new Exception("Bad Observe rule: "+key);
                            int expectedReturns=Int32.Parse(bits[3]);
                            int[] beginLoads=ParseLoads(bits[4]);int[] endLoads=ParseLoads(bits[5]);
                            StripEntry(am,bm,bridge,bits[1],beginLoads);
                            StripReturns(am,bm,bridge,bits[2],expectedReturns,endLoads);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Entry/exit observers changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="GT")
                        {
                            if(bits.Length!=7)throw new Exception("Bad guard+observer rule: "+key);
                            int expectedReturns=Int32.Parse(bits[4]);
                            int[] beginLoads=ParseLoads(bits[5]);int[] endLoads=ParseLoads(bits[6]);
                            StripGuard(am,bm,bridge,bits[1],new int[]{-1});
                            StripEntry(am,bm,bridge,bits[2],beginLoads);
                            StripReturns(am,bm,bridge,bits[3],expectedReturns,endLoads);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Guard+entry/exit observers changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="A")
                        {
                            StripReturn(am,bm,bridge,name,loads,false,1);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("After-return observer changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="W")
                        {
                            StripReturn(am,bm,bridge,name,loads,true,1);
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Iterator wrapper changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="T")
                        {
                            int expectedReturns=Int32.Parse(bits[3]);
                            if((bits.Length!=4 && bits.Length<10) || am.Body.Instructions.Count(i=>i.OpCode==OpCodes.Ret)!=expectedReturns)
                                throw new Exception("Native return layout changed for diagnostic phase hook: "+key);
                            StripEntry(am,bm,bridge,name,loads);
                            StripReturns(am,bm,bridge,bits[2],expectedReturns);
                            if(bits.Length>4)StripCallSwap(am,bm,bridge,bits[4],1,bits[5],bits[6],Int32.Parse(bits[7]),bits[8],bits.Skip(9).ToArray());
                            if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Diagnostic phase observers changed stock method body: "+key);
                            continue;
                        }
                        if(mode=="L")
                        {
                            var originalCall=am.Body.Instructions.Select(x=>x.Operand as MethodReference).FirstOrDefault(x=>x!=null && x.DeclaringType.FullName=="Steamworks.SteamUser" && x.Name=="GetSteamID");
                            if(originalCall==null)throw new Exception("Missing stock local identity call.");
                            int edits=0;foreach(var i in instructions)
                            {
                                var call=i.Operand as MethodReference;
                                if(call==null || call.DeclaringType.FullName!="TldMp.StableGameBridge")continue;
                                if(i.OpCode!=OpCodes.Call || call.Name!=name)throw new Exception("Unexpected identity hook.");
                                ValidateHook(am,call,bridge,new int[0],false,originalCall.ReturnType.FullName);
                                i.Operand=originalCall;edits++;
                            }
                            if(edits!=1 || VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Local identity changed beyond one comparison.");continue;
                        }
                        if(mode=="N")
                        {
                            int originalLocals=am.Body.Variables.Count;
                            if(bm.Body.Variables.Count!=originalLocals+3)throw new Exception("Wrong noise local count.");
                            string[] names={"r","d","s"};int edits=0;
                            for(int n=0;n<instructions.Count;n++)
                            {
                                var i=instructions[n];
                                if(i.OpCode==OpCodes.Pop && i.Next!=null && i.Next.OpCode==OpCodes.Ldloc && ((VariableDefinition)i.Next.Operand).Index>=originalLocals)
                                {int v=((VariableDefinition)i.Next.Operand).Index-originalLocals;i.OpCode=OpCodes.Ldfld;i.Operand=b.Fields.Single(f=>f.Name==names[v]);instructions.RemoveAt(n+1);edits++;}
                                else if(i.OpCode==OpCodes.Stloc && ((VariableDefinition)i.Operand).Index>=originalLocals)
                                {int v=((VariableDefinition)i.Operand).Index-originalLocals;if(i.Next.OpCode!=OpCodes.Pop)throw new Exception("Wrong noise store.");i.OpCode=OpCodes.Stfld;i.Operand=b.Fields.Single(f=>f.Name==names[v]);instructions.RemoveAt(n+1);edits++;}
                            }
                            for(int n=0;n<3;n++)bm.Body.Variables.RemoveAt(bm.Body.Variables.Count-1);
                            if(edits<6 || VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Noise math changed beyond localizing accumulators.");continue;
                        }
                        if(mode=="I")
                        {
                            var hook=instructions.Single(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName=="TldMp.StableGameBridge");
                            if(((MethodReference)hook.Operand).Name!=name || hook.Next.OpCode!=OpCodes.Ret)throw new Exception("Bad bootstrap.");
                            ValidateHook(am,(MethodReference)hook.Operand,bridge,new int[0],false,"System.Void");
                            instructions.Remove(hook.Next);hook.OpCode=OpCodes.Ret;hook.Operand=null;
                        }
                        else
                        {
                            int index=0;
                            Instruction hookInstruction=null;
                            Action<string> call=delegate(string n){var i=instructions[index++];var m=i.Operand as MethodReference;if(i.OpCode!=OpCodes.Call || m==null || m.Name!=n || m.DeclaringType.FullName!="TldMp.StableGameBridge")throw new Exception("Wrong hook: "+key);hookInstruction=i;};
                            Instruction target=null;
                            if(mode=="C")
                            {
                                call("UseOriginalIDs");
                                ValidateHook(am,(MethodReference)hookInstruction.Operand,bridge,new int[0],false,"System.Boolean");
                                var branch=instructions[index++];if(branch.OpCode!=OpCodes.Brtrue)throw new Exception("Bad native-first branch.");target=(Instruction)branch.Operand;
                            }
                            foreach(int arg in loads){var i=instructions[index++];if(arg==-1){if(i.OpCode!=OpCodes.Ldarg_0)throw new Exception("Wrong this load.");}else if(i.OpCode!=OpCodes.Ldarg || ((ParameterDefinition)i.Operand).Index!=arg)throw new Exception("Wrong argument.");}
                            call(name);
                            // An entry hook leaves nothing on the stack, even on a non-void native.
                            string expectedHookReturn=(mode=="G" || mode=="GH")?"System.Boolean":mode=="E"?"System.Void":am.ReturnType.FullName;
                            ValidateHook(am,(MethodReference)hookInstruction.Operand,bridge,loads,false,expectedHookReturn);
                            if(mode=="G" || mode=="GH"){var branch=instructions[index++];if(branch.OpCode!=OpCodes.Brtrue)throw new Exception("Bad guard.");target=(Instruction)branch.Operand;if(am.ReturnType.FullName=="System.Boolean" && instructions[index++].OpCode!=OpCodes.Ldc_I4_0)throw new Exception("Wrong rejected return value.");}
                            if(mode=="E")
                            {
                                if(index!=loads.Length+1)throw new Exception("Wrong entry-hook prefix.");
                                for(int n=0;n<index;n++)instructions.RemoveAt(0);
                                if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Entry instrumentation changed stock method body.");continue;
                            }
                            if(instructions[index++].OpCode!=OpCodes.Ret)throw new Exception("Bad return.");
                            if(mode=="R"){if(index!=instructions.Count)throw new Exception("Extra replacement instructions.");continue;}
                            if(instructions[index]!=target)throw new Exception("Guard does not enter original body.");for(int i=0;i<index;i++)instructions.RemoveAt(0);
                            if(mode=="GH")StripCategoryCleanupOptimization(am,bm);
                        }
                        if(VerifyPatch.Fingerprint(am)!=VerifyPatch.Fingerprint(bm))throw new Exception("Extra stock changes: "+key);
                    }
                }
                if(changed!=hookCount || expected.Count!=0)throw new Exception("Incomplete native-restore hooks: changed="+changed+", expected="+hookCount+", missing="+String.Join(",",expected.Keys.ToArray()));
                // Nothing outside the kept set referenced the helper in the patched file.
                var unexpectedUsers=bridgeUsers.Where(m=>!kept.Contains(m)).ToArray();
                if(unexpectedUsers.Length>0)throw new Exception("Unlisted native method calls StableGameBridge: "+String.Join(", ",unexpectedUsers.Select(m=>m.FullName).ToArray()));
                // 72 of the 74 kept methods call the bridge (160 sites: both NewKey
                // methods call UseOriginalIDs then NewKey; GetOut has two observers;
                // the P0 terrain observer adds two sites, terrain.Upd keeps its guard;
                // PlaceStuff adds the pass-local item-selection Begin/Lookup pair and the far-place enqueue swap;
                // UpdPlaceRemove adds the stable place-order sort; GenerateTileMesh adds
                // the terrain-hole entry/neighbor pair and the road-list dedupe call;
                // the four POI branch observers add their own entry/return brackets;
                // the car-pose factory adds its gate plus burst-start pair; the leave
                // safety path adds four collision calls and one player lookup; and
                // the ask/remove guard adds one call, and the road live cache adds three;
                // the guest auto-store gate and its two observers add three methods/calls;
                // the item stream anchor adds one call to RemoveStuff and the DistUnFreezeRB
                // gate adds one guard call (two new methods, two sites);
                // the road dedupe and far skip add one call each to already-hooked methods;
                // the mirror on-screen read adds one call to MirrorsUpdate and the road
                // bake adds BakeCollider with one return observer;
                // networkingScript.DebugLog adds one guard (NetLog) for the rate-limited log path.
                // Remote smoothing adds three methods and five call sites: playerSyncClass.GotUpd
                // entry sample (1), playerSyncClass.Upd return pose (one per each of its 3 returns),
                // tosaveitemscript.GotSync guard (1).
                // Bus-ground diagnostics add two entry observers (AddPoi, poiScript.FStart): two methods, two sites.
                // The terrain-align fix swaps two Transform.localPosition reads in poiGenScript.alignClass's constructor: one method, two sites.
                // mainscript.VisszaRakas timing observer adds one method and three sites (entry plus two returns).
                if(bridgeUsers.Count!=103 || hookSites!=212)throw new Exception("Hook coverage mismatch: methods="+bridgeUsers.Count+", sites="+hookSites);
                if(kept.Count!=hookCount || !kept.Contains(Find(after,"terrainGenSettings/Noises.Noise/1")))throw new Exception("Kept-method coverage mismatch: patched="+kept.Count+", expected="+hookCount);
                Console.WriteLine("PASS: "+hookCount+" kept native hook methods ("+hookSites+" hook call sites); "+unchanged+" baseline methods unchanged; "+removed.Length+" removed-hook methods byte-identical to the pristine input with zero StableGameBridge calls; no field/type/dependency layout changes.");
            }
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}



