using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Verifies callback brackets on the exact native roadGenScript.UpdPlaceRemove
// IL. It proves the patched native instruction stream is unchanged after the
// no-argument timing hooks are removed; it does not execute Unity callbacks.
public static class RoadCallbackInstrumentationTests
{
    sealed class PairSpec
    {
        public string Name, TargetType, TargetMethod, Begin, End;
        public int ExpectedCalls;
        public PairSpec(string name,string type,string method,string begin,string end,int count)
        { Name=name;TargetType=type;TargetMethod=method;Begin=begin;End=end;ExpectedCalls=count; }
    }
    static readonly PairSpec[] Specs = {
        new PairSpec("Instantiate","UnityEngine.Object","Instantiate","NativeRoadInstantiateBegin","NativeRoadInstantiateEnd",1),
        new PairSpec("Init","roadscript","Init","NativeRoadInitBegin","NativeRoadInitEnd",1),
        new PairSpec("Place","roadscript","Place","NativeRoadGeometryBegin","NativeRoadGeometryEnd",1),
        new PairSpec("PowerPole","roadscript","PowerPole","NativeRoadPoleBegin","NativeRoadPoleEnd",1),
        new PairSpec("Destroy","UnityEngine.Object","Destroy","NativeRoadDestroyBegin","NativeRoadDestroyEnd",2)
    };
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}

    public static int Main(string[] args)
    {
        try
        {
            if(args.Length!=2)throw new Exception("Usage: RoadCallbackInstrumentationTests baseline.dll candidate.dll");
            using(var before=AssemblyDefinition.ReadAssembly(args[0]))
            using(var after=AssemblyDefinition.ReadAssembly(args[1]))
            {
                var a=Find(before.MainModule);var b=Find(after.MainModule);
                Check(a.Body.Instructions.Count>0 && b.Body.Instructions.Count>0,"Native road method body missing");
                int expectedHookCount=0;
                foreach(var spec in Specs)expectedHookCount+=CheckPair(a,b,spec);
                CompareNativeBody(a,b);
                int callbackHooks=b.Body.Instructions.Count(i=>IsCallbackHook(i));
                Check(callbackHooks==expectedHookCount*2,"Unexpected road callback hook count: "+callbackHooks+" expected="+(expectedHookCount*2));
                Check(b.Body.Instructions.Count(i=>IsHook(Called(i),"NativeRoadPlaceBegin"))==1&&b.Body.Instructions.Count(i=>IsHook(Called(i),"NativeRoadPlaceEnd"))==1,"Existing whole-method road timing pair missing/duplicated");
                Console.WriteLine("PASS exact roadGenScript.UpdPlaceRemove callback instrumentation checks="+checks+" nativePairs="+expectedHookCount+" callbacks=Instantiate/Init/Place/PowerPole/Destroy; native body identical after stripping hooks");
            }
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }

    static MethodDefinition Find(ModuleDefinition m)
    {return m.GetTypes().Single(t=>t.FullName=="roadGenScript").Methods.Single(x=>x.Name=="UpdPlaceRemove"&&x.Parameters.Count==1&&x.Parameters[0].ParameterType.FullName=="System.Boolean");}
    static bool IsHook(MethodReference m,string name){return m!=null&&m.Name==name&&m.DeclaringType.FullName=="TldMp.StableGameBridge";}
    static MethodReference Called(Instruction i){return i!=null&&(i.OpCode==OpCodes.Call||i.OpCode==OpCodes.Callvirt)?i.Operand as MethodReference:null;}
    static bool IsTarget(Instruction i,PairSpec s){var m=Called(i);return m!=null&&m.Name==s.TargetMethod&&m.DeclaringType.FullName==s.TargetType;}
    static bool IsCallbackHook(Instruction i){var m=Called(i);return m!=null&&m.DeclaringType.FullName=="TldMp.StableGameBridge"&&Specs.Any(s=>m.Name==s.Begin||m.Name==s.End);}
    static bool IsBridgeHook(Instruction i){var m=Called(i);return IsCallbackHook(i)||IsHook(m,"NativeRoadPlaceBegin")||IsHook(m,"NativeRoadPlaceEnd");}

    static int CheckPair(MethodDefinition baseline,MethodDefinition candidate,PairSpec spec)
    {
        var oldCalls=baseline.Body.Instructions.Where(i=>IsTarget(i,spec)).ToArray();
        Check(oldCalls.Length==spec.ExpectedCalls,"Pristine native "+spec.Name+" call count changed: "+oldCalls.Length+" expected="+spec.ExpectedCalls);
        var hooks=candidate.Body.Instructions.Where(i=>IsHook(Called(i),spec.Begin)||IsHook(Called(i),spec.End)).ToArray();
        var begins=hooks.Where(i=>IsHook(Called(i),spec.Begin)).ToArray();var ends=hooks.Where(i=>IsHook(Called(i),spec.End)).ToArray();
        Check(begins.Length==oldCalls.Length&&ends.Length==oldCalls.Length,"Hook pair count differs for "+spec.Name+": begin="+begins.Length+" end="+ends.Length+" target="+oldCalls.Length);
        var targets=candidate.Body.Instructions.Where(i=>IsTarget(i,spec)).ToArray();
        Check(targets.Length==oldCalls.Length,"Patched native "+spec.Name+" call count changed");
        for(int n=0;n<targets.Length;n++)
        {
            var target=targets[n];int at=candidate.Body.Instructions.IndexOf(target);
            Check(at>0&&at+1<candidate.Body.Instructions.Count,"Callback bracket at method boundary: "+spec.Name);
            var begin=Called(candidate.Body.Instructions[at-1]);var end=Called(candidate.Body.Instructions[at+1]);
            Check(IsHook(begin,spec.Begin)&&IsHook(end,spec.End),"Expected adjacent Begin/native/End sequence for "+spec.Name+" at call "+n);
            Check(begin.Parameters.Count==0&&begin.ReturnType.FullName=="System.Void"&&end.Parameters.Count==0&&end.ReturnType.FullName=="System.Void","Road timing hook must be void with no arguments: "+spec.Name);
            Check(candidate.Body.Instructions[at-1].OpCode==OpCodes.Call&&candidate.Body.Instructions[at+1].OpCode==OpCodes.Call,"Road timing hook calls must be direct call instructions: "+spec.Name);
        }
        // Branch and switch edges must enter the Begin hook, not jump straight
        // to a native callback and silently bypass its timing bracket.
        var beginsSet=new HashSet<Instruction>(begins);
        var targetSet=new HashSet<Instruction>(targets);
        foreach(var i in candidate.Body.Instructions)
        {
            var one=i.Operand as Instruction;
            if(one!=null&&targetSet.Contains(one))Check(false,"Existing branch bypasses "+spec.Begin+" at a native "+spec.Name+" call");
            var many=i.Operand as Instruction[];
            if(many!=null)foreach(var x in many)if(targetSet.Contains(x))Check(false,"Existing switch bypasses "+spec.Begin+" at a native "+spec.Name+" call");
        }
        return oldCalls.Length;
    }

    static HashSet<Instruction> RoadLiveCacheInstructions(MethodDefinition after)
    {
        var stripped=new HashSet<Instruction>();
        foreach(var i in after.Body.Instructions)
        {
            var m=Called(i);
            if(m==null || m.DeclaringType.FullName!="TldMp.StableGameBridge")continue;
            if(m.Name=="RoadChunkDirty" || m.Name=="RoadChunkDestroyed")
            {
                stripped.Add(i);
                if(i.Previous!=null)stripped.Add(i.Previous);
            }
            else if(m.Name=="RoadChunkMayHaveLive")
            {
                stripped.Add(i);
                var road=i.Previous;var dcc=road==null?null:road.Previous;var self=dcc==null?null:dcc.Previous;
                if(road!=null)stripped.Add(road);
                if(dcc!=null)stripped.Add(dcc);
                if(self!=null)stripped.Add(self);
                if(i.Next!=null && (i.Next.OpCode==OpCodes.Brfalse || i.Next.OpCode==OpCodes.Brfalse_S))stripped.Add(i.Next);
            }
            else if(m.Name=="RoadChunkSkippable")
            {
                // ldarg.0; ldarg.0; ldfld dcc0; call; brtrue (shape enforced by VerifyStability).
                stripped.Add(i);
                var field=i.Previous;var receiver=field==null?null:field.Previous;var self=receiver==null?null:receiver.Previous;
                if(field!=null)stripped.Add(field);
                if(receiver!=null)stripped.Add(receiver);
                if(self!=null)stripped.Add(self);
                if(i.Next!=null && i.Next.OpCode==OpCodes.Brtrue)stripped.Add(i.Next);
            }
        }
        return stripped;
    }
    static void CompareNativeBody(MethodDefinition before,MethodDefinition after)
    {
        var roadCache=RoadLiveCacheInstructions(after);
        var ai=before.Body.Instructions.ToArray();var bi=after.Body.Instructions.Where(i=>!IsBridgeHook(i)&&!roadCache.Contains(i)).ToArray();
        Check(ai.Length==bi.Length,"Removing road timing hooks did not restore native instruction count: "+ai.Length+" vs "+bi.Length);
        var normA=new Dictionary<Instruction,int>();for(int i=0;i<ai.Length;i++)normA.Add(ai[i],i);
        var allB=after.Body.Instructions.ToArray();var nextNative=bi.Length;var actualB=new Dictionary<Instruction,int>();
        // Hook instructions are removed from the normalized stream. Any edge
        // targeting a Begin/End hook therefore resolves to the next native IL.
        for(int i=allB.Length-1;i>=0;i--)
        {
            if(!IsBridgeHook(allB[i]) && !roadCache.Contains(allB[i]))nextNative--;
            actualB[allB[i]]=nextNative;
        }
        for(int i=0;i<ai.Length;i++)
        {
            Check(SameOpcode(ai[i].OpCode,bi[i].OpCode),"Native opcode changed at stripped instruction "+i+": "+ai[i].OpCode+" vs "+bi[i].OpCode);
            Check(OperandEqual(ai[i].Operand,bi[i].Operand,normA,actualB),"Native operand/branch target changed at stripped instruction "+i+" ("+ai[i]+" vs "+bi[i]+")");
        }
        Check(before.Body.Variables.Count==after.Body.Variables.Count,"Road instrumentation changed local count");
        for(int i=0;i<before.Body.Variables.Count;i++)Check(before.Body.Variables[i].VariableType.FullName==after.Body.Variables[i].VariableType.FullName,"Road instrumentation changed local type "+i);
        Check(before.Body.ExceptionHandlers.Count==after.Body.ExceptionHandlers.Count,"Road instrumentation changed exception region count");
        for(int i=0;i<before.Body.ExceptionHandlers.Count;i++)
        {
            var x=before.Body.ExceptionHandlers[i];var y=after.Body.ExceptionHandlers[i];
            Check(x.HandlerType==y.HandlerType&&Index(x.TryStart,normA)==Index(y.TryStart,actualB)&&Index(x.TryEnd,normA)==Index(y.TryEnd,actualB)&&Index(x.HandlerStart,normA)==Index(y.HandlerStart,actualB)&&Index(x.HandlerEnd,normA)==Index(y.HandlerEnd,actualB),"Road instrumentation changed exception boundaries");
        }
    }
    static int Index(Instruction i,Dictionary<Instruction,int> map){return i==null?-1:map.ContainsKey(i)?map[i]:-2;}
    static bool SameOpcode(OpCode a,OpCode b)
    {
        if(a==b)return true;
        var map=new Dictionary<OpCode,OpCode>{{OpCodes.Br_S,OpCodes.Br},{OpCodes.Brfalse_S,OpCodes.Brfalse},{OpCodes.Brtrue_S,OpCodes.Brtrue},{OpCodes.Beq_S,OpCodes.Beq},{OpCodes.Bge_S,OpCodes.Bge},{OpCodes.Bgt_S,OpCodes.Bgt},{OpCodes.Ble_S,OpCodes.Ble},{OpCodes.Blt_S,OpCodes.Blt},{OpCodes.Bne_Un_S,OpCodes.Bne_Un},{OpCodes.Bge_Un_S,OpCodes.Bge_Un},{OpCodes.Bgt_Un_S,OpCodes.Bgt_Un},{OpCodes.Ble_Un_S,OpCodes.Ble_Un},{OpCodes.Blt_Un_S,OpCodes.Blt_Un},{OpCodes.Leave_S,OpCodes.Leave}};
        OpCode x;return map.TryGetValue(a,out x)&&x==b || map.TryGetValue(b,out x)&&x==a;
    }
    static bool OperandEqual(object a,object b,Dictionary<Instruction,int> na,Dictionary<Instruction,int> nb)
    {
        if(a==null||b==null)return a==b;
        var ia=a as Instruction;var ib=b as Instruction;if(ia!=null||ib!=null)return ia!=null&&ib!=null&&Index(ia,na)==Index(ib,nb);
        var aa=a as Instruction[];var ab=b as Instruction[];if(aa!=null||ab!=null)return aa!=null&&ab!=null&&aa.Length==ab.Length&&aa.Select(x=>Index(x,na)).SequenceEqual(ab.Select(x=>Index(x,nb)));
        var va=a as VariableDefinition;var vb=b as VariableDefinition;if(va!=null||vb!=null)return va!=null&&vb!=null&&va.Index==vb.Index&&va.VariableType.FullName==vb.VariableType.FullName;
        var pa=a as ParameterDefinition;var pb=b as ParameterDefinition;if(pa!=null||pb!=null)return pa!=null&&pb!=null&&pa.Index==pb.Index&&pa.ParameterType.FullName==pb.ParameterType.FullName;
        var ma=a as MethodReference;var mb=b as MethodReference;if(ma!=null||mb!=null)return ma!=null&&mb!=null&&ma.FullName==mb.FullName;
        var fa=a as FieldReference;var fb=b as FieldReference;if(fa!=null||fb!=null)return fa!=null&&fb!=null&&fa.FullName==fb.FullName;
        var ta=a as TypeReference;var tb=b as TypeReference;if(ta!=null||tb!=null)return ta!=null&&tb!=null&&ta.FullName==tb.FullName;
        return a.Equals(b);
    }
}
