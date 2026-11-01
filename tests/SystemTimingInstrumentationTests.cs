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
            },"Unexpected edit: savedatascript.SaveToData/1");
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
