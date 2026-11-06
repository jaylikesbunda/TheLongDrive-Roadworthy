using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Staged diagnostic only. Never patch the input assembly or install this output.
public static class PatchVehicleShadowProbe
{
    static MethodDefinition Target(ModuleDefinition module)
    {
        var type = module.Types.Single(t => t.FullName == "tosaveitemscript");
        return type.Methods.Single(m => m.Name == "ForcePosUpd" && m.HasBody && m.HasThis &&
            m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 3 &&
            m.Parameters.All(p => p.ParameterType.FullName == "System.Boolean"));
    }
    static void Redirect(MethodDefinition method, Instruction from, Instruction to)
    {
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand == from) instruction.Operand = to;
            var targets = instruction.Operand as Instruction[];
            if (targets != null) for (int i = 0; i < targets.Length; i++) if (targets[i] == from) targets[i] = to;
        }
        foreach (var h in method.Body.ExceptionHandlers)
        {
            if (h.TryStart == from || h.HandlerStart == from || h.FilterStart == from) throw new Exception("Unexpected exception region start at return.");
            if (h.TryEnd == from) h.TryEnd = to;
            if (h.HandlerEnd == from) h.HandlerEnd = to;
        }
    }
    static void Verify(string baseline, string patched, string probePath)
    {
        using (var original = AssemblyDefinition.ReadAssembly(baseline))
        using (var output = AssemblyDefinition.ReadAssembly(patched))
        using (var probe = AssemblyDefinition.ReadAssembly(probePath))
        {
            var originalTypes = VerifyPatch.AllTypes(original.MainModule.Types).ToDictionary(t => t.FullName);
            var outputTypes = VerifyPatch.AllTypes(output.MainModule.Types).ToDictionary(t => t.FullName);
            var oldRefs = original.MainModule.AssemblyReferences.Select(r => r.FullName).ToArray();
            var newRefs = output.MainModule.AssemblyReferences.Select(r => r.FullName).ToArray();
            if (original.Name.FullName != output.Name.FullName || original.MainModule.Name != output.MainModule.Name ||
                !original.MainModule.Resources.Select(r => r.Name + ":" + r.ResourceType).SequenceEqual(output.MainModule.Resources.Select(r => r.Name + ":" + r.ResourceType)))
                throw new Exception("Assembly identity or resource list changed.");
            if (oldRefs.Any(r => !newRefs.Contains(r)) || newRefs.Length != oldRefs.Length + 1 ||
                output.MainModule.AssemblyReferences.Count(r => r.FullName == probe.Name.FullName) != 1)
                throw new Exception("Unexpected assembly-reference change.");
            if (!originalTypes.Keys.OrderBy(x => x).SequenceEqual(outputTypes.Keys.OrderBy(x => x))) throw new Exception("Type list changed.");
            var oldTarget = Target(original.MainModule); var newTarget = Target(output.MainModule);
            var ret = newTarget.Body.Instructions.Single(i => i.OpCode == OpCodes.Ret);
            var call = ret.Previous; var load = call == null ? null : call.Previous;
            var called = call == null ? null : call.Operand as MethodReference;
            if (call == null || call.OpCode != OpCodes.Call || load == null || load.OpCode != OpCodes.Ldarg_0 || called == null ||
                called.FullName != "System.Void TldMp.Experimental.VehicleShadowProbe::Observe(tosaveitemscript)") throw new Exception("Unexpected observer hook.");
            var scope = called.DeclaringType.Scope as AssemblyNameReference;
            if (scope == null || scope.FullName != probe.Name.FullName || newTarget.Body.MaxStackSize < 1)
                throw new Exception("Observer scope or target stack bound mismatch.");
            if (newTarget.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName == called.FullName) != 1) throw new Exception("Observer call count mismatch.");
            Redirect(newTarget, load, ret);
            newTarget.Body.GetILProcessor().Remove(load); newTarget.Body.GetILProcessor().Remove(call);
            if (VerifyPatch.Fingerprint(oldTarget) != VerifyPatch.Fingerprint(newTarget)) throw new Exception("Target differs after removing observer hook.");
            foreach (var pair in originalTypes)
            {
                var a = pair.Value; var b = outputTypes[pair.Key];
                if (a.Attributes != b.Attributes || (a.BaseType == null ? "" : a.BaseType.FullName) != (b.BaseType == null ? "" : b.BaseType.FullName) ||
                    !a.Interfaces.Select(x => x.InterfaceType.FullName).SequenceEqual(b.Interfaces.Select(x => x.InterfaceType.FullName)) ||
                    !a.Properties.Select(x => x.FullName).SequenceEqual(b.Properties.Select(x => x.FullName)) ||
                    !a.Events.Select(x => x.FullName).SequenceEqual(b.Events.Select(x => x.FullName)))
                    throw new Exception("Type metadata changed: " + pair.Key);
                if (!a.Fields.Select(x => x.FullName).SequenceEqual(b.Fields.Select(x => x.FullName))) throw new Exception("Fields changed: " + pair.Key);
                if (!a.Fields.Select(x => x.Attributes).SequenceEqual(b.Fields.Select(x => x.Attributes))) throw new Exception("Field attributes changed: " + pair.Key);
                var methods = b.Methods.ToDictionary(m => m.FullName);
                if (a.Methods.Count != b.Methods.Count) throw new Exception("Method count changed: " + pair.Key);
                foreach (var m in a.Methods)
                {
                    MethodDefinition copy;
                    if (!methods.TryGetValue(m.FullName, out copy) || VerifyPatch.Fingerprint(m) != VerifyPatch.Fingerprint(copy))
                        throw new Exception("Unexpected method change: " + m.FullName);
                }
            }
        }
    }
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 || Path.GetFullPath(args[0]) == Path.GetFullPath(args[2])) throw new Exception("Usage: PatchVehicleShadowProbe BASELINE PROBE OUTPUT (distinct paths)");
            if (File.Exists(args[2])) throw new Exception("Output already exists.");
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            using (var probe = AssemblyDefinition.ReadAssembly(args[1]))
            {
                if (assembly.MainModule.AssemblyReferences.Any(r => r.Name == probe.Name.Name))
                    throw new Exception("Input already references the observer assembly.");
                var method = Target(assembly.MainModule);
                method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 1);
                var observer = probe.MainModule.Types.Single(t => t.FullName == "TldMp.Experimental.VehicleShadowProbe").Methods.Single(m => m.Name == "Observe" && m.IsPublic && m.IsStatic && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "tosaveitemscript");
                var ret = method.Body.Instructions.Single(i => i.OpCode == OpCodes.Ret);
                foreach (var i in method.Body.Instructions)
                    if (i.OpCode.OperandType == OperandType.ShortInlineBrTarget) { string name = i.OpCode.Code.ToString(); i.OpCode = (OpCode)typeof(OpCodes).GetField(name.Substring(0, name.Length - 2)).GetValue(null); }
                var load = Instruction.Create(OpCodes.Ldarg_0);
                Redirect(method, ret, load);
                var il = method.Body.GetILProcessor();
                // Importing the probe method wholesale would import its parameter
                // as an Assembly-CSharp self-reference. Bind it to this module's
                // own target type instead.
                var hook = new MethodReference(observer.Name, assembly.MainModule.TypeSystem.Void,
                    assembly.MainModule.ImportReference(observer.DeclaringType));
                hook.HasThis = false;
                hook.Parameters.Add(new ParameterDefinition(method.DeclaringType));
                il.InsertBefore(ret, load); il.InsertBefore(ret, Instruction.Create(OpCodes.Call, hook));
                assembly.Write(args[2]);
            }
            Verify(args[0], args[2], args[1]);
            Console.WriteLine("PASS: staged method fingerprints match after removing the vehicle observer");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
