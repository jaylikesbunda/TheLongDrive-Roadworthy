using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Adversarial checks against the real patch verifier, not a second permissive
// approximation of it. Every mutation is written only to the test directory.
static class SystemTimingInstrumentationTests
{
    static int checks;
    static string baseline, candidate, runtime, verifier, output;
    static DefaultAssemblyResolver resolver;
    static AssemblyDefinition Read(string path)
    {
        return AssemblyDefinition.ReadAssembly(path,new ReaderParameters{AssemblyResolver=resolver});
    }

    static MethodDefinition Find(AssemblyDefinition a,string type,string name,int count)
    {
        return a.MainModule.GetTypes().Single(t=>t.FullName==type).Methods
            .Single(m=>m.Name==name && m.Parameters.Count==count);
    }
    static bool Hook(Instruction i,string name)
    {
        var m=i.Operand as MethodReference;
        return m!=null && m.DeclaringType.FullName=="TldMp.StableGameBridge" && m.Name==name;
    }
    static void Run(string file,string helper,bool pass,string reason)
    {
        var start=new ProcessStartInfo(verifier,
            "\""+baseline+"\" \""+file+"\" \""+helper+"\"");
        start.UseShellExecute=false;start.RedirectStandardOutput=true;start.RedirectStandardError=true;
        using(var p=Process.Start(start))
        {
            string text=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();p.WaitForExit();
            File.WriteAllText(Path.Combine(output,Path.GetFileName(file)+".verify.log"),text);
            if((p.ExitCode==0)!=pass || (!pass && text.IndexOf(reason,StringComparison.Ordinal)<0))
                throw new Exception("Unexpected verifier result for "+file+": "+text);
            checks++;Console.WriteLine("PASS "+Path.GetFileName(file)+" "+(pass?"accepted":"rejected: "+reason));
        }
    }
    static void Mutate(string name,Action<AssemblyDefinition> edit,string reason)
    {
        string path=Path.Combine(output,name+".dll");
        using(var a=Read(candidate)){edit(a);a.Write(path);}
        Run(path,runtime,false,reason);
    }
    static int Main(string[] args)
    {
        try
        {
            if(args.Length!=6)throw new Exception("Expected baseline, candidate, runtime, verifier, fresh output directory and game Managed directory.");
            baseline=Path.GetFullPath(args[0]);candidate=Path.GetFullPath(args[1]);
            runtime=Path.GetFullPath(args[2]);verifier=Path.GetFullPath(args[3]);output=Path.GetFullPath(args[4]);
            resolver=new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetFullPath(args[5]));
            resolver.AddSearchDirectory(Path.GetDirectoryName(candidate));
            resolver.AddSearchDirectory(Path.GetDirectoryName(runtime));
            if(Directory.Exists(output))throw new Exception("Output directory already exists: "+output);
            Directory.CreateDirectory(output);
            Run(candidate,runtime,true,"");
            Mutate("missing-terrain-pad-blend",delegate(AssemblyDefinition a) {
                var call=Find(a,"terrainGenSettings","Height",3).Body.Instructions.First(i=>Hook(i,"TerrainPadValue"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Call-swap count mismatch");
            Mutate("missing-rabbit-contact-gate",delegate(AssemblyDefinition a) {
                var call=Find(a,"breakablescript","OnCollisionEnter",1).Body.Instructions.First(i=>Hook(i,"RabbitImpactGate"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong guard observer");
            Mutate("missing-rabbit-body-forwarding",delegate(AssemblyDefinition a) {
                var call=Find(a,"ragdollactivatescript","SetRagdol",1).Body.Instructions.First(i=>Hook(i,"ConfigureRabbitImpact"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong exact return-hook count");
            Mutate("missing-creature-axis-fix",delegate(AssemblyDefinition a) {
                var field=Find(a,"movecreaturescript","Move",0).Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldfld && i.Operand is FieldReference && ((FieldReference)i.Operand).DeclaringType.FullName=="UnityEngine.Vector3" && ((FieldReference)i.Operand).Name=="z");
                var old=(FieldReference)field.Operand;field.Operand=new FieldReference("y",old.FieldType,old.DeclaringType);
            },"Creature horizontal-axis correction missing or ambiguous.");
            foreach(var type in new[]{"pickupable","carscript","breakablescript"}) {
                string owner=type;
                Mutate("missing-contact-guard-"+owner,delegate(AssemblyDefinition a) {
                    var m=Find(a,owner,"OnCollisionEnter",1);
                    var call=m.Body.Instructions.First(i=>Hook(i,"HasCollisionContacts"));call.OpCode=OpCodes.Nop;call.Operand=null;
                },"Wrong guard observer");
            }
            Mutate("missing-biome-binding",delegate(AssemblyDefinition a) {
                var m=Find(a,"terrainGenSettings","FStart",1);
                var call=m.Body.Instructions.First(i=>Hook(i,"RegisterTerrainBiome"));call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong exact return-hook count");
            Mutate("missing-biome-color",delegate(AssemblyDefinition a) {
                var m=Find(a,"terrainGenSettings","Biome",2);
                var call=m.Body.Instructions.First(i=>Hook(i,"ApplyWorldBiome"));call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong exact return-hook count");
            Mutate("missing-biome-load",delegate(AssemblyDefinition a) {
                var m=Find(a,"menuhandler","LoadMap",1);
                var call=m.Body.Instructions.First(i=>Hook(i,"ConfigureWorldBiome"));call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong entry observer");
            Mutate("missing-biome-save",delegate(AssemblyDefinition a) {
                var m=Find(a,"savedatascript","RefreshProperties",1);
                var call=m.Body.Instructions.First(i=>Hook(i,"StampBiomeVersion"));call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong exact return-hook count");
            Mutate("missing-debris-wrapper",delegate(AssemblyDefinition a) {
                var m=Find(a,"breakablescript","Break",0);
                m.Body.Instructions.First(i=>Hook(i,"InstantiateBreakDebris")).Operand=m;
            },"Call-swap count mismatch");
            Mutate("changed-slice-removal",delegate(AssemblyDefinition a) {
                var m=Find(a,"randomSlice","Slicc",3);
                m.Body.Instructions.First(i=>i.OpCode==OpCodes.Nop).OpCode=OpCodes.Ldc_I4_0;
            },"Invalid duplicate slice component removal.");
            Mutate("missing-debris-mode-exit",delegate(AssemblyDefinition a) {
                var m=Find(a,"tosaveitemscript","FInit",0);
                var call=m.Body.Instructions.First(i=>Hook(i,"DebrisItemInitialized"));call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong bounded return-hook count");
            Mutate("wrong-slot",delegate(AssemblyDefinition a) {
                Find(a,"fpscontroller","Update",0).Body.Instructions[0].Operand=55;
            },"Invalid system timing bracket");
            Mutate("missing-exit",delegate(AssemblyDefinition a) {
                var m=Find(a,"pickupable","OnCollisionEnter",1);
                var call=m.Body.Instructions.First(i=>Hook(i,"NativeSystemEnd"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Extra/missing system timing calls");
            Mutate("bypass-exit",delegate(AssemblyDefinition a) {
                var m=Find(a,"fpscontroller","Move",0);
                m.Body.GetILProcessor().InsertBefore(m.Body.Instructions[2],
                    Instruction.Create(OpCodes.Br,m.Body.Instructions.First(i=>i.OpCode==OpCodes.Ret)));
            },"Branch bypasses system timing bracket");
            Mutate("changed-native-body",delegate(AssemblyDefinition a) {
                var m=Find(a,"mountStuff","Scan",0);
                var value=m.Body.Instructions.First(i=>i.OpCode==OpCodes.Ldc_R4);
                value.Operand=(float)value.Operand+0.125f;
            },"Storage scan native fallback changed.");
            Mutate("changed-exception-region",delegate(AssemblyDefinition a) {
                var m=Find(a,"savedatascript","SaveToData",1);
                m.Body.ExceptionHandlers[0].TryStart=m.Body.Instructions[0];
            },"Entry instrumentation changed stock method body");
            Mutate("missing-rabbit-hearing",delegate(AssemblyDefinition a) {
                var call=Find(a,"sensoryscript","Scan",0).Body.Instructions.First(i=>Hook(i,"RabbitHearingFalloff"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Rabbit hearing hook count changed");
            Mutate("omitted-rabbit-hearing-receiver",delegate(AssemblyDefinition a) {
                var m=Find(a,"sensoryscript","Scan",0);
                var call=m.Body.Instructions.First(i=>Hook(i,"RabbitHearingFalloff"));
                m.Body.Instructions.Remove(call.Previous);
            },"Rabbit hearing receiver changed");
            Mutate("wrong-rabbit-hearing-target",delegate(AssemblyDefinition a) {
                var call=Find(a,"sensoryscript","Scan",0).Body.Instructions.First(i=>Hook(i,"RabbitHearingFalloff"));
                var mr=(MethodReference)call.Operand;
                call.Operand=new MethodReference("NpcVehicleVelocity",mr.ReturnType,mr.DeclaringType){HasThis=false};
            },"Rabbit hearing hook count changed");
            Mutate("missing-rabbit-flee",delegate(AssemblyDefinition a) {
                var call=Find(a,"aiscript","Update",0).Body.Instructions.First(i=>Hook(i,"RabbitStartFlee"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Call-swap count mismatch");
            Mutate("missing-ragdoll-velocity",delegate(AssemblyDefinition a) {
                var call=Find(a,"syncScript","RecRagdoll",1).Body.Instructions.First(i=>Hook(i,"RagdollVelocity"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Call-swap count mismatch");
            Mutate("missing-ragdoll-angular",delegate(AssemblyDefinition a) {
                var call=Find(a,"syncScript","RecRagdoll",1).Body.Instructions.First(i=>Hook(i,"RagdollAngularVelocity"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Call-swap count mismatch");
            Mutate("missing-rabbit-pose-guard",delegate(AssemblyDefinition a) {
                var call=Find(a,"ragdollchildpos","UpdPos",0).Body.Instructions.First(i=>Hook(i,"BeforeRabbitPose"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong hook");
            Mutate("missing-rabbit-bracket-entry",delegate(AssemblyDefinition a) {
                var call=Find(a,"ragdollactivatescript","SetRagdol",1).Body.Instructions.First(i=>Hook(i,"BeforeRabbitRagdoll"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong entry observer");
            Mutate("missing-rabbit-trigger",delegate(AssemblyDefinition a) {
                var call=Find(a,"playerdeathtriggerscript","OnTriggerStay",1).Body.Instructions.First(i=>Hook(i,"RabbitTriggerGuard"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong guard observer");
            Mutate("missing-rabbit-ai-guard",delegate(AssemblyDefinition a) {
                var call=Find(a,"aiscript","Update",0).Body.Instructions.First(i=>Hook(i,"RabbitAiGuard"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong guard observer");
            Mutate("missing-rabbit-bracket-end",delegate(AssemblyDefinition a) {
                var call=Find(a,"pickupable","SetCollider",1).Body.Instructions.First(i=>Hook(i,"AfterRabbitCollider"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong bounded return-hook count");
            Mutate("missing-rabbit-unfreeze",delegate(AssemblyDefinition a) {
                var call=Find(a,"jointHelpScript","UnFreezeStart",0).Body.Instructions.First(i=>Hook(i,"RabbitUnfreeze"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong bounded return-hook count");
            Mutate("missing-rabbit-rot",delegate(AssemblyDefinition a) {
                var call=Find(a,"movecreaturescript","Rot",0).Body.Instructions.First(i=>Hook(i,"RabbitRotGuard"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong hook");
            Mutate("missing-rabbit-nyul",delegate(AssemblyDefinition a) {
                var call=Find(a,"nyulscript","Update",0).Body.Instructions.First(i=>Hook(i,"RabbitNyulGuard"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong hook");
            Mutate("missing-spawn-gate",delegate(AssemblyDefinition a) {
                var call=Find(a,"aispawn","Spawn",1).Body.Instructions.First(i=>Hook(i,"RabbitSpawnGate"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong guard observer");
            Mutate("missing-grass-mesh-changed",delegate(AssemblyDefinition a) {
                var call=Find(a,"newTerrainTileScript","UpdMesh",1).Body.Instructions.First(i=>Hook(i,"BiomeGrassMeshChanged"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong bounded return-hook count");
            Mutate("missing-rabbit-init",delegate(AssemblyDefinition a) {
                var call=Find(a,"tosaveitemscript","FInit",0).Body.Instructions.First(i=>Hook(i,"AfterRabbitInit"));
                call.OpCode=OpCodes.Nop;call.Operand=null;
            },"Wrong bounded return-hook count");
            string badRuntime=Path.Combine(output,"bad-label-runtime.dll");
            using(var a=Read(runtime))
            {
                var init=Find(a,"TldMp.NativeScriptTimings",".cctor",0);
                init.Body.Instructions.First(i=>i.OpCode==OpCodes.Ldstr &&
                    (string)i.Operand=="fpscontroller.Update").Operand="wrong-label";
                a.Write(badRuntime);
            }
            Run(candidate,badRuntime,false,"System timing label/slot mismatch");
            Console.WriteLine("PASS system timing instrumentation checks="+checks);
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
