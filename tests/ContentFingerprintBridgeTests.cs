using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class ContentFingerprintBridgeTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static bool Calls(MethodDefinition method,string declaringType,string name)
    {return method.Body.Instructions.Select(i=>i.Operand as MethodReference).Any(m=>m!=null&&m.DeclaringType.FullName==declaringType&&m.Name==name);}

    static int CallIndex(MethodDefinition method,string declaringType,string name)
    {
        for(int i=0;i<method.Body.Instructions.Count;i++)
        {var call=method.Body.Instructions[i].Operand as MethodReference;if(call!=null&&call.DeclaringType.FullName==declaringType&&call.Name==name)return i;}
        return -1;
    }

    public static int Main(string[] args)
    {
        try
        {
            var assembly=AssemblyDefinition.ReadAssembly(args[0]);
            var bridge=assembly.MainModule.Types.Single(t=>t.FullName=="TldMp.StableGameBridge");
            var helper=assembly.MainModule.Types.Single(t=>t.FullName=="TldMp.ContentFingerprint");
            var ensure=bridge.Methods.Single(m=>m.Name=="Ensure");
            var start=bridge.Methods.Single(m=>m.Name=="Bootstrap");
            var poll=bridge.Methods.Single(m=>m.Name=="EnsureFingerprint");
            var reset=bridge.Methods.Single(m=>m.Name=="Reset");
            var newKey=bridge.Methods.Single(m=>m.Name=="NewKey");
            var wait=bridge.Methods.Single(m=>m.Name=="FinishFingerprintJob");

            int applyLoopback=CallIndex(start,"TldMp.StableGameBridge","ApplyLoopback");
            int bootstrapFingerprint=CallIndex(start,"TldMp.StableGameBridge","EnsureFingerprint");
            Check(applyLoopback>=0&&bootstrapFingerprint>applyLoopback&&Calls(start,"TldMp.StableGameBridge","get_Connected"),
                "Bootstrap does not restrict prewarming to an already-engaged opt-in session");
            var menuPrewarm=bridge.Methods.Single(m=>m.Name=="PrewarmContentFingerprint");
            int menuFlag=-1,menuFingerprint=CallIndex(menuPrewarm,"TldMp.StableGameBridge","EnsureFingerprint");
            for(int i=0;i<menuPrewarm.Body.Instructions.Count;i++)
            {var field=menuPrewarm.Body.Instructions[i].Operand as FieldReference;if(field!=null&&field.Name=="menuOpen")menuFlag=i;}
            Check(menuFingerprint>menuFlag&&menuFlag>=0,"Menu entry does not prewarm only on the opening toggle");
            Check(Calls(poll,"TldMp.ContentFingerprint/Job","TryGetResult")&&!Calls(poll,"TldMp.ContentFingerprint/Job","WaitForResult"),
                "Fingerprint polling itself remains nonblocking");
            int metric=CallIndex(poll,"TldMp.ContentFingerprint/Job","get_WorkMilliseconds"),clearJob=-1;
            for(int i=0;i<poll.Body.Instructions.Count;i++)
            {var field=poll.Body.Instructions[i].Operand as FieldReference;if(field!=null&&field.Name=="fingerprintJob"&&poll.Body.Instructions[i].OpCode==OpCodes.Stsfld)clearJob=i;}
            Check(metric>=0&&clearJob>metric,"Worker duration is captured before the completed job reference is cleared");
            Check(!reset.Body.Instructions.Any(i=>i.Operand is FieldReference &&
                (((FieldReference)i.Operand).Name=="fingerprint"||((FieldReference)i.Operand).Name=="fingerprintJob")),
                "Session reset invalidates the content-only cache or late job");

            int fingerprintCall=CallIndex(ensure,"TldMp.StableGameBridge","EnsureFingerprint");
            int nodeCtor=CallIndex(ensure,"TldMp.StableNode",".ctor");
            bool falseReturnsBeforeNode=false;
            for(int i=fingerprintCall+1;i<nodeCtor;i++)
            {
                var instruction=ensure.Body.Instructions[i];
                if(instruction.OpCode.FlowControl==FlowControl.Cond_Branch && instruction.Operand is Instruction)
                {
                    var target=(Instruction)instruction.Operand;int targetIndex=ensure.Body.Instructions.IndexOf(target);
                    if(targetIndex>i && ensure.Body.Instructions.Skip(i+1).Take(targetIndex-i-1).Any(x=>x.OpCode==OpCodes.Ret))falseReturnsBeforeNode=true;
                }
            }
            Check(fingerprintCall>=0&&nodeCtor>fingerprintCall&&falseReturnsBeforeNode,
                "Session node creation is behind the successful fingerprint-ready branch");

            foreach(string hook in new[]{"RoadUpdate","RoadAt","PoiChunk","PoiAdd","CategoryChunks","CategoryGen","Terrain"})
            {
                var method=bridge.Methods.Single(m=>m.Name==hook);
                int gate=CallIndex(method,"TldMp.StableGameBridge","EnsureWorldSession");
                int map=method.Body.Instructions.Select((ins,index)=>new {ins,index})
                    .Where(x=>x.ins.Operand is MethodReference && ((MethodReference)x.ins.Operand).DeclaringType.FullName=="TldMp.StableMap" && ((MethodReference)x.ins.Operand).Name==hook)
                    .Select(x=>x.index).DefaultIfEmpty(-1).First();
                bool falseSkipsMap=false;
                for(int i=gate+1;i<map;i++)
                {
                    var instruction=method.Body.Instructions[i];
                    if(instruction.OpCode.FlowControl==FlowControl.Cond_Branch && instruction.Operand is Instruction)
                    {
                        int target=method.Body.Instructions.IndexOf((Instruction)instruction.Operand);
                        if(target>map && target<method.Body.Instructions.Count &&
                            method.Body.Instructions[target].OpCode==OpCodes.Ldc_I4_0 &&
                            target+1<method.Body.Instructions.Count && method.Body.Instructions[target+1].OpCode==OpCodes.Ret)falseSkipsMap=true;
                    }
                }
                Check(gate>=0&&map>gate&&falseSkipsMap,hook+" does not fail closed before native map work while the session is pending");
            }

            // Removed replacement machinery must be absent from the runtime.
            foreach(string removed in new[]{"AllowBreakableBreak","RecordBreakableCollision","RecordBreakableTrigger","OwnsItem","OwnsCar","MayEnter","SeatFree","MayPickup","MayStore","MayUnstore","MayUnfreeze","UpdateControls","SimulateBalance","ReceiveItemPose","WrapLocal","WrapSmooth","WrapAttachment","RecordCarCrash","ObserveItemAdded"})
                Check(!bridge.Methods.Any(m=>m.Name==removed),"removed bridge method still present: "+removed);
            foreach(string type in new[]{"TldMp.StableLease","TldMp.StableShadowTable","TldMp.PushAuthority","TldMp.BreakableAuthority","TldMp.CarCrashAuthority","TldMp.DriverImpactRelay","TldMp.PropTiers","TldMp.PropTiersPolicy","TldMp.ClockSync","TldMp.ItemWire","TldMp.VehicleWire","TldMp.SnapshotBuffer","TldMp.AuthorityGate","TldMp.GameBridge"})
                Check(!assembly.MainModule.Types.Any(t=>t.FullName==type),"removed type still present: "+type);
            Check(Calls(newKey,"TldMp.StableGameBridge","FinishFingerprintJob")&&Calls(newKey,"TldMp.StableNode","AllocateID")&&
                !newKey.Body.Instructions.Any(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).Name.Contains("GetNewKey")),
                "Compatibility-critical ID creation uses only the host-reserved node allocator");
            Check(Calls(wait,"TldMp.ContentFingerprint/Job","WaitForResult")&&Calls(ensure,"TldMp.StableGameBridge","FinishFingerprintJob"),
                "A compatibility barrier finishes the already-running job before world/session startup");

            var job=helper.NestedTypes.Single(t=>t.Name=="Job");var run=job.Methods.Single(m=>m.Name=="Run");
            Check(Calls(run,"TldMp.ContentFingerprint","Compute"),"Worker job does not execute the canonical digest function");
            Check(!run.Body.Instructions.Any(i=>i.Operand is MethodReference &&
                (((MethodReference)i.Operand).DeclaringType.Namespace=="UnityEngine"||((MethodReference)i.Operand).DeclaringType.FullName=="TldMp.StableGameBridge"||
                 ((MethodReference)i.Operand).DeclaringType.FullName=="TldMp.StableNode")),
                "Background worker contains no Unity calls or session/node mutation");
            var checkRemove=bridge.Methods.Single(m=>m.Name=="TerrainCheckRemove");
            Check(Calls(checkRemove,"TldMp.TerrainMembership","CheckRemove"),"Terrain membership hook is not connected to the guarded fast path");

            Console.WriteLine("PASS: "+checks+" fingerprint lifecycle/IL safety checks (startup gate, polling, stale reset, map hooks, ID fallback, worker isolation).");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}

