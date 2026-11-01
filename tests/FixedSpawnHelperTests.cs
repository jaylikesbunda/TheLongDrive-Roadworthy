using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class FixedSpawnHelperTests
{
    static int checks;
    static void Check(bool ok, string what) { checks++; if (!ok) throw new Exception("FAIL: " + what); }
    static MethodDefinition Method(AssemblyDefinition asm, string type, string name, int parameters)
    {
        return asm.MainModule.GetType(type).Methods.Single(m => m.Name == name && m.Parameters.Count == parameters);
    }
    static bool Calls(MethodDefinition m, string name)
    {
        return m.Body.Instructions.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference && ((MethodReference)i.Operand).Name == name);
    }
    public static int Main(string[] args)
    {
        try
        {
            var runtime = AssemblyDefinition.ReadAssembly(args[0]);
            var patched = AssemblyDefinition.ReadAssembly(args[1]);
            var bridge = runtime.MainModule.GetType("TldMp.StableGameBridge");
            var helper = bridge.Methods.Single(m => m.Name == "FixedSpawnFStart");
            Check(helper.ReturnType.FullName == "System.Void" && helper.Parameters.Count == 1 && helper.Parameters[0].ParameterType.Name == "fixedSpawnScript", "helper signature changed");

            var fstart = Method(patched, "fixedSpawnScript", "FStart", 0);
            var calls = fstart.Body.Instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference &&
                ((MethodReference)i.Operand).Name == "FixedSpawnFStart").ToArray();
            Check(calls.Length == 1 && calls[0].Previous != null && calls[0].Previous.OpCode == OpCodes.Ldarg_0,
                "patched FStart does not delegate to the pooled-list helper");
            Check(!Calls(fstart, "GetComponentsInChildren"), "array queries survived the replacement");

            var queries = helper.Body.Instructions.Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                i.Operand is GenericInstanceMethod && ((GenericInstanceMethod)i.Operand).Name == "GetComponentsInChildren").ToArray();
            Check(queries.Length == 3, "helper does not contain exactly three list queries");
            string[] expected = { "UnityEngine.Rigidbody", "tosaveitemscript", "randomTypeSelector" };
            for (int i = 0; i < 3; i++)
            {
                var g = (GenericInstanceMethod)queries[i].Operand;
                Check(g.GenericArguments.Count == 1 && g.GenericArguments[0].FullName == expected[i], "query " + i + " type changed");
                Check(g.Parameters.Count == 2, "query " + i + " is not the (bool,List<T>) overload");
            }
            int firstFStart = helper.Body.Instructions.IndexOf(helper.Body.Instructions.First(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference &&
                ((MethodReference)i.Operand).DeclaringType != null && ((MethodReference)i.Operand).DeclaringType.Name == "tosaveitemscript" && ((MethodReference)i.Operand).Name == "FStart"));
            int selectorQuery = helper.Body.Instructions.IndexOf(queries[2]);
            Check(firstFStart >= 0 && selectorQuery > firstFStart, "selector query no longer follows the item FStart loop");
            int setActive = helper.Body.Instructions.IndexOf(helper.Body.Instructions.First(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference &&
                ((MethodReference)i.Operand).DeclaringType != null && ((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.GameObject" && ((MethodReference)i.Operand).Name == "SetActive"));
            int bodyQuery = helper.Body.Instructions.IndexOf(queries[0]);
            Check(setActive >= 0 && bodyQuery > setActive, "starting-car activation no longer precedes the rigidbody query");
            Check(helper.Body.ExceptionHandlers.Count >= 3, "pooled-list phases lost their finally protection");
            Check(helper.Body.Instructions.Count(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Clear" &&
                ((MethodReference)i.Operand).DeclaringType.FullName.StartsWith("System.Collections.Generic.List")) >= 3,
                "returned lists are not cleared before being pooled");

            string[] fenceSites = {
                "savedatascript:SaveToData:1:BeforeNativeSaveData",
                "newSaveScreenScript:Save:4:BeforeNativeSaveScreen",
                "kaposztaleves:SaveSelected:0:BeforeNativeExport",
                "savedatascript:SaveToDictionary:2:RequireConstructionSettled",
                "savedatascript:SetDataFromSave:0:ResetConstruction"
            };
            foreach (var site in fenceSites)
            {
                var parts = site.Split(':');
                var m = Method(patched, parts[0], parts[1], Int32.Parse(parts[2]));
                Check(Calls(m, parts[3]), "missing fence " + parts[3] + " in " + parts[0] + "." + parts[1]);
            }
            var save = Method(patched, "savedatascript", "SaveToData", 1);
            Check(Calls(save, "BeforeNativeSaveData") && !Calls(save, "AfterNativeSaveData"), "SaveToData fence wiring changed");
            var fixedSpawn = Method(patched, "poiScript", "FixedSpawn", 0);
            Check(Calls(fixedSpawn, "NativePoiFixedInstantiateBegin") && Calls(fixedSpawn, "NativePoiFixedInstantiateEnd"),
                "FixedSpawn instantiate bracket missing");
            var spawnPoi = bridge.Methods.Single(m => m.Name == "SpawnPoiCoordinated");
            int beginFrameIdx = -1, firstExecIdx = -1;
            for (int i = 0; i < spawnPoi.Body.Instructions.Count; i++)
            {
                var ins = spawnPoi.Body.Instructions[i];
                if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) continue;
                var mr = ins.Operand as MethodReference;
                if (mr == null) continue;
                if (mr.Name == "BeginFrame") beginFrameIdx = i;
                else if (mr.Name == "RunSynchronous" || mr.Name == "RunUnadmittedAtomic") { if (firstExecIdx < 0) firstExecIdx = i; }
            }
            Check(beginFrameIdx >= 0, "SpawnPoiCoordinated missing Construction.BeginFrame");
            Check(firstExecIdx < 0 || beginFrameIdx < firstExecIdx, "BeginFrame must precede RunSynchronous/RunUnadmittedAtomic");
            Console.WriteLine("PASS: fixed-spawn/fence production IL " + checks + " checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
