using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

// The pass-3 invariant checker. Shared by VerifyV4.exe and by PatchV4.exe's
// pre-commit self-check, so the patcher can never emit a file the verifier rejects.
public static class Invariants
{
    const string StableType = "TldStable";

    public static bool Verify(string v3Path, string v4Path, out string failure)
    {
        failure = null;
        try { Run(v3Path, v4Path); return true; }
        catch (Exception error) { failure = error.Message; return false; }
    }

    public static string Run(string v3Path, string v4Path)
    {
        using (AssemblyDefinition before = AssemblyDefinition.ReadAssembly(v3Path))
        using (AssemblyDefinition after = AssemblyDefinition.ReadAssembly(v4Path))
        {
            ModuleDefinition v3 = before.MainModule;
            ModuleDefinition v4 = after.MainModule;
            string summary = Check(v3, v4);
            return summary;
        }
    }

    static string Check(ModuleDefinition v3, ModuleDefinition v4)
    {
        // ---- assembly references: identical set (names + versions), order-insensitive
        string[] referencesBefore = v3.AssemblyReferences.Select(r => r.FullName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        string[] referencesAfter = v4.AssemblyReferences.Select(r => r.FullName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!referencesBefore.SequenceEqual(referencesAfter))
            throw new Exception("Assembly reference set changed. Added: [" +
                String.Join("; ", referencesAfter.Except(referencesBefore).ToArray()) + "] removed: [" +
                String.Join("; ", referencesBefore.Except(referencesAfter).ToArray()) + "]");

        // ---- type set == v3 + TldStable
        Dictionary<string, TypeDefinition> typesBefore = Checks.AllTypes(v3.Types).ToDictionary(t => t.FullName);
        Dictionary<string, TypeDefinition> typesAfter = Checks.AllTypes(v4.Types).ToDictionary(t => t.FullName);
        string[] gone = typesBefore.Keys.Except(typesAfter.Keys).ToArray();
        string[] fresh = typesAfter.Keys.Except(typesBefore.Keys).ToArray();
        if (gone.Length != 0) throw new Exception("Types removed: " + String.Join(", ", gone));
        if (fresh.Length != 1 || fresh[0] != StableType)
            throw new Exception("Unexpected added types: [" + String.Join(", ", fresh) + "]");
        if (typesAfter.Count != typesBefore.Count + 1) throw new Exception("Type count changed unexpectedly.");
        if (v4.AssemblyReferences.Count == 0) throw new Exception("Empty reference table.");

        // ---- fields: byte-identical lists for every pre-existing type
        foreach (KeyValuePair<string, TypeDefinition> entry in typesBefore)
        {
            string fieldsBefore = Checks.FieldShape(entry.Value.Fields);
            string fieldsAfter = Checks.FieldShape(typesAfter[entry.Key].Fields);
            if (fieldsBefore != fieldsAfter) throw new Exception("Field list changed: " + entry.Key);
        }
        TypeDefinition stable = typesAfter[StableType];
        if (stable.Fields.Count != 0) throw new Exception("TldStable declares fields.");

        // ---- interfaces: unchanged except the two contracted IEquatable additions
        string[] equatableHosts = new string[] { "UnityEngine.Vector3d", "UnityEngine.Vector2d" };
        foreach (KeyValuePair<string, TypeDefinition> entry in typesBefore)
        {
            List<string> interfaceBefore = entry.Value.Interfaces.Select(i => i.InterfaceType.FullName).OrderBy(x => x, StringComparer.Ordinal).ToList();
            List<string> interfaceAfter = typesAfter[entry.Key].Interfaces.Select(i => i.InterfaceType.FullName).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (equatableHosts.Contains(entry.Key))
            {
                interfaceBefore.Add("System.IEquatable`1<" + entry.Key + ">");
                interfaceBefore.Sort(StringComparer.Ordinal);
                if (!interfaceBefore.SequenceEqual(interfaceAfter))
                    throw new Exception(entry.Key + " interfaces are not exactly the v3 set plus IEquatable<" + entry.Key + ">.");
            }
            else if (!interfaceBefore.SequenceEqual(interfaceAfter))
            {
                throw new Exception("Interfaces changed on " + entry.Key + ".");
            }
        }

        // ---- methods
        HashSet<string> pendingEdits = new HashSet<string>(new string[]
        {
            Key("pathscript", "SaveBinary", 3),
            Key("pickupable", "FixedUpdate", 0),
            Key("fpscontroller", "overlap", 3),
            Key("savedatascript", "AddToFarStuff", 1),
            Key("poiGenScript", "AddPois", 0),
            Key("settingsscript", "RefreshInMain", 1)
        });
        int unchanged = 0, changed = 0, added = 0;

        foreach (KeyValuePair<string, TypeDefinition> entry in typesBefore)
        {
            TypeDefinition previous = entry.Value;
            TypeDefinition current = typesAfter[entry.Key];
            Dictionary<string, MethodDefinition> byName = current.Methods.ToDictionary(m => m.FullName);
            foreach (MethodDefinition method in previous.Methods)
            {
                MethodDefinition candidate;
                if (!byName.TryGetValue(method.FullName, out candidate))
                    throw new Exception("Method removed: " + method.FullName);
                if (Checks.Fingerprint(method) == Checks.Fingerprint(candidate)) { unchanged++; continue; }

                string key = Key(entry.Key, method.Name, method.Parameters.Count);
                if (!pendingEdits.Remove(key)) throw new Exception("Unexpected modification: " + method.FullName);
                changed++;
                CheckEdit(v3, entry.Key, method, candidate);
            }

            HashSet<string> known = new HashSet<string>(previous.Methods.Select(m => m.FullName));
            foreach (MethodDefinition method in current.Methods)
            {
                if (known.Contains(method.FullName)) continue;
                added++;
                CheckAddition(v3, entry.Key, method);
            }
        }
        if (pendingEdits.Count != 0) throw new Exception("Missing contracted edits: [" + String.Join(", ", pendingEdits.ToArray()) + "]");
        if (changed != 6) throw new Exception("Expected 6 modified methods, found " + changed);
        if (added != 2) throw new Exception("Expected 2 added methods on existing types (Vector3d/Vector2d Equals), found " + added);

        CheckStable(stable);

        return "PASS: v3->v4 exact (pass 3; streamed-prop registration disabled in the native-restore candidate). " + unchanged + " methods unchanged, 6 edited, 5 added; " +
               "assembly references, type set and field lists identical; TldStable verified.";
    }

    static string Key(string type, string method, int parameters)
    {
        return type + "." + method + "/" + parameters;
    }

    static void CheckEdit(ModuleDefinition v3, string typeName, MethodDefinition previous, MethodDefinition current)
    {
        MethodDefinition expected;
        switch (Key(typeName, previous.Name, previous.Parameters.Count))
        {
            case "pathscript.SaveBinary/3":
                expected = Expected.SaveBinary(previous);
                if (current.Body.Variables.Count != 0) throw new Exception("SaveBinary kept locals.");
                foreach (Instruction i in current.Body.Instructions)
                {
                    MethodReference callee = i.Operand as MethodReference;
                    if (callee != null && callee.DeclaringType.FullName.StartsWith("System.IO.FileStream"))
                        throw new Exception("SaveBinary still constructs/uses FileStream.");
                }
                foreach (Instruction i in current.Body.Instructions)
                {
                    MethodReference callee = i.Operand as MethodReference;
                    if (callee != null && callee.DeclaringType.FullName.StartsWith("System.Runtime.Serialization"))
                        throw new Exception("SaveBinary still serialises directly.");
                }
                break;
            case "pickupable.FixedUpdate/0":
                expected = Expected.Overlap(previous, "OverlapSphere", "CheckSphere");
                RequireCheck(current, "CheckSphere", "OverlapSphere");
                break;
            case "fpscontroller.overlap/3":
                expected = Expected.Overlap(previous, "OverlapCapsule", "CheckCapsule");
                RequireCheck(current, "CheckCapsule", "OverlapCapsule");
                if (previous.Body.Instructions.Any(i => i.OpCode == OpCodes.Ret) == false) throw new Exception("Unexpected overlap shape.");
                break;
            case "savedatascript.AddToFarStuff/1":
                expected = Expected.AddToFarStuff(previous);
                RequireAppendHoist(current);
                break;
            case "poiGenScript.AddPois/0":
                expected = Expected.AddPois(previous);
                Checks.ExpandBranches(expected);
                RequireGroundHeightFix(current);
                break;
            case "settingsscript.RefreshInMain/1":
                expected = Expected.RefreshSettings(previous);
                break;
            default:
                throw new Exception("Unhandled edit: " + current.FullName);
        }

        if (Checks.BodyFingerprint(expected) != Checks.BodyFingerprint(current))
            throw new Exception("IL of " + current.FullName + " does not match the contracted shape.");
        if (current.Body.Instructions.Any(i => i.OpCode.OperandType == OperandType.ShortInlineBrTarget))
            throw new Exception("Short branch left in " + current.FullName + " (ExpandBranches not applied).");
        int computed = Checks.ComputeMaxStack(current);
        if (current.Body.MaxStackSize < computed)
            throw new Exception("MaxStackSize too small in " + current.FullName + " (" + current.Body.MaxStackSize + " < " + computed + ").");
    }

    // AddPois must no longer contain the "if (flag)" guard: a brtrue immediately after a
    // single ldloc, targeting the same instruction as the preceding brfalse.
    static void RequireGroundHeightFix(MethodDefinition method)
    {
        Instruction[] code = method.Body.Instructions.ToArray();
        for (int i = 0; i + 2 < code.Length; i++)
        {
            if (code[i].OpCode != OpCodes.Brfalse && code[i].OpCode != OpCodes.Brfalse_S) continue;
            Instruction target = code[i].Operand as Instruction;
            if (target == null) continue;
            if (!IsLoadLocal(code[i + 1])) continue;
            if (code[i + 2].OpCode != OpCodes.Brtrue && code[i + 2].OpCode != OpCodes.Brtrue_S) continue;
            if (code[i + 2].Operand == target)
                throw new Exception("AddPois still skips the ground re-sample for road-aligned POIs.");
        }

        bool samplesGround = false;
        foreach (Instruction instruction in method.Body.Instructions)
        {
            MethodReference callee = instruction.Operand as MethodReference;
            if (callee != null && callee.Name == "RoadsNear") { samplesGround = true; break; }
        }
        if (!samplesGround) throw new Exception("AddPois lost its RoadsNear ground sample.");
    }

    static bool IsLoadLocal(Instruction instruction)
    {
        return instruction.OpCode == OpCodes.Ldloc || instruction.OpCode == OpCodes.Ldloc_S ||
               instruction.OpCode == OpCodes.Ldloc_0 || instruction.OpCode == OpCodes.Ldloc_1 ||
               instruction.OpCode == OpCodes.Ldloc_2 || instruction.OpCode == OpCodes.Ldloc_3;
    }

    static void CheckAddition(ModuleDefinition v3, string typeName, MethodDefinition added)
    {
        if (typeName == "UnityEngine.Vector3d" || typeName == "UnityEngine.Vector2d")
        {
            TypeDefinition type = Checks.AllTypes(v3.Types).Single(t => t.FullName == typeName);
            if (added.Name != "Equals" || added.Parameters.Count != 1 ||
                added.Parameters[0].ParameterType.FullName != typeName)
                throw new Exception("Unexpected method added to " + typeName + ": " + added.FullName);
            if (added.IsStatic)
                throw new Exception("Equals(" + typeName + ") must be an instance method.");
            if ((added.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                throw new Exception("Equals(" + typeName + ") must be public.");
            if (!added.IsVirtual || !added.IsFinal || !added.IsNewSlot || added.IsStatic)
                throw new Exception("Equals(" + typeName + ") must be public virtual final newslot (implicit interface implementation).");
            MethodDefinition expected = Expected.VectorEquals(type, typeName == "UnityEngine.Vector3d" ? new string[] { "x", "y", "z" } : new string[] { "x", "y" });
            if (Checks.BodyFingerprint(expected) != Checks.BodyFingerprint(added))
                throw new Exception("IL of " + added.FullName + " does not match the contracted shape.");
            if (added.Body.Instructions.Any(i => i.OpCode.OperandType == OperandType.ShortInlineBrTarget))
                throw new Exception("Short branch left in " + added.FullName + ".");
            if (added.Body.MaxStackSize < Checks.ComputeMaxStack(added))
                throw new Exception("MaxStackSize too small in " + added.FullName + ".");
            return;
        }
        throw new Exception("Unexpected method added to " + typeName + ": " + added.FullName);
    }

    static void RequireCheck(MethodDefinition method, string required, string forbidden)
    {
        int requiredCount = 0, forbiddenCount = 0, lengths = 0;
        foreach (Instruction i in method.Body.Instructions)
        {
            if (i.OpCode == OpCodes.Ldlen) lengths++;
            MethodReference callee = i.Operand as MethodReference;
            if (callee == null || callee.DeclaringType.FullName != "UnityEngine.Physics" && callee.DeclaringType.FullName != "TldMp.NativePhysicsQueries") continue;
            if (callee.Name == required) requiredCount++;
            if (callee.Name == forbidden) forbiddenCount++;
        }
        if (requiredCount != 1) throw new Exception("Expected one Physics." + required + " in " + method.FullName + ", found " + requiredCount);
        if (forbiddenCount != 0) throw new Exception("Physics." + forbidden + " still called from " + method.FullName);
        if (lengths != 0) throw new Exception("ldlen survived in " + method.FullName);
    }

    static void RequireAppendHoist(MethodDefinition method)
    {
        IList<Instruction> instructions = method.Body.Instructions;
        int append = -1, enumerator = -1, addToChunk = 0;
        for (int i = 0; i < instructions.Count; i++)
        {
            MethodReference callee = instructions[i].Operand as MethodReference;
            if (callee == null) continue;
            if (instructions[i].OpCode == OpCodes.Callvirt && callee.Name == "Append" && callee.DeclaringType.FullName == "itemDataClass")
            {
                if (append >= 0) throw new Exception("AddToFarStuff(itemDataClass) still appends more than once.");
                append = i;
            }
            if (callee.Name == "GetEnumerator") enumerator = i;
            if (callee.Name == "AddToChunk") addToChunk++;
        }
        if (append < 0) throw new Exception("AddToFarStuff(itemDataClass) lost its Append call.");
        if (enumerator < 0) throw new Exception("AddToFarStuff(itemDataClass) lost its foreach.");
        if (append > enumerator) throw new Exception("Append is still inside the foreach.");
        if (addToChunk != 1) throw new Exception("AddToChunk call count changed in AddToFarStuff(itemDataClass).");
        if (append < 4) throw new Exception("Append is not at the head of AddToFarStuff(itemDataClass).");
        if (instructions[append - 4].OpCode != OpCodes.Ldarg_0 ||
            instructions[append - 3].OpCode != OpCodes.Ldfld ||
            instructions[append - 2].OpCode != OpCodes.Ldfld ||
            instructions[append - 1].OpCode != OpCodes.Ldarg_1 ||
            instructions[append + 1].OpCode != OpCodes.Ldarg_1)
            throw new Exception("Append hoist is not the contracted sequence.");
        MethodReference target = instructions[append].Operand as MethodReference;
        if (!target.DeclaringType.FullName.EndsWith("itemDataClass")) throw new Exception("Append target changed.");
    }

    static void RequirePropRegistration(MethodDefinition method)
    {
        IList<Instruction> instructions = method.Body.Instructions;
        // The dead 1200 m gate is gone: no distance measurement and no magic threshold.
        foreach (Instruction instruction in instructions)
        {
            MethodReference callee = instruction.Operand as MethodReference;
            if (callee != null && callee.DeclaringType.FullName == "UnityEngine.Vector3d" &&
                callee.Name == "get_sqrMagnitude")
                throw new Exception("UpdPlaceRemove still contains a dead draw-distance gate.");
            if (instruction.OpCode == OpCodes.Ldc_R8 && instruction.Operand is double &&
                (double)instruction.Operand == 1440000.0)
                throw new Exception("UpdPlaceRemove still contains the dead 1200 m threshold.");
        }

        int registrations = 0, instantiate = -1;
        Instruction registration = null;
        for (int i = 0; i < instructions.Count; i++)
        {
            MethodReference callee = instructions[i].Operand as MethodReference;
            if (callee == null) continue;
            if (callee.DeclaringType.FullName == "UnityEngine.Object" && callee.Name == "Instantiate") instantiate = i;
            if (callee.DeclaringType.FullName == "TldMp.PropTiers" && callee.Name == "Register")
            { registration = instructions[i]; registrations++; }
        }
        if (registrations != 1 || registration == null)
            throw new Exception("UpdPlaceRemove must call PropTiers.Register exactly once, found " + registrations + ".");
        if (instantiate < 0)
            throw new Exception("UpdPlaceRemove lost its Instantiate call.");

        // Real control-flow check: a no-op/unreachable registration or gate must fail.
        HashSet<Instruction> reachable = Checks.Reachable(method);
        if (!reachable.Contains(registration))
            throw new Exception("UpdPlaceRemove's PropTiers.Register call is unreachable.");
        if (!reachable.Contains(instructions[instantiate]))
            throw new Exception("UpdPlaceRemove's Instantiate call is unreachable.");
        if (instructions.IndexOf(registration) <= instantiate)
            throw new Exception("PropTiers.Register is not after Instantiate.");
        Dictionary<Instruction, List<Instruction>> predecessors = Checks.Predecessors(method);
        List<Instruction> beforeRegister = predecessors[registration];
        FieldReference loaded = beforeRegister.Count == 1 ? beforeRegister[0].Operand as FieldReference : null;
        if (beforeRegister.Count != 1 || beforeRegister[0].OpCode != OpCodes.Ldfld || loaded == null || loaded.Name != "obj")
            throw new Exception("PropTiers.Register is not fed directly by objClass.obj.");
    }

    static void CheckStable(TypeDefinition stable)
    {
        if (stable.IsPublic) throw new Exception("TldStable must be internal.");
        if (!stable.IsAbstract || !stable.IsSealed || !stable.IsClass) throw new Exception("TldStable must be an abstract sealed (static) class.");
        if (stable.BaseType == null || stable.BaseType.FullName != "System.Object") throw new Exception("TldStable must derive from System.Object.");
        if (stable.Methods.Count != 3) throw new Exception("TldStable must declare exactly SaveAtomic, Marker and Configure.");
        if (stable.HasNestedTypes) throw new Exception("TldStable must not declare nested types.");
        if (stable.Properties.Count != 0 || stable.Events.Count != 0) throw new Exception("TldStable must not declare properties/events.");

        MethodDefinition save = stable.Methods.SingleOrDefault(m => m.Name == "SaveAtomic");
        MethodDefinition marker = stable.Methods.SingleOrDefault(m => m.Name == "Marker");
        MethodDefinition configure = stable.Methods.SingleOrDefault(m => m.Name == "Configure");
        if (save == null || marker == null || configure == null) throw new Exception("TldStable.SaveAtomic/Marker/Configure missing.");
        if (!save.IsPublic || !save.IsStatic || save.ReturnType.FullName != "System.Void" || save.Parameters.Count != 2 ||
            save.Parameters[0].ParameterType.FullName != "System.String" || save.Parameters[1].ParameterType.FullName != "System.Object")
            throw new Exception("TldStable.SaveAtomic signature changed.");
        if (!marker.IsPublic || !marker.IsStatic || marker.ReturnType.FullName != "System.Void" || marker.Parameters.Count != 0)
            throw new Exception("TldStable.Marker signature changed.");
        if (marker.Body.Instructions.Count != 1 || marker.Body.Instructions[0].OpCode != OpCodes.Ret)
            throw new Exception("TldStable.Marker must be a bare ret.");

        // Pass 5: graphics preset -- exact contract shape plus the startup attribute.
        if (!configure.IsPublic || !configure.IsStatic || configure.ReturnType.FullName != "System.Void" ||
            configure.Parameters.Count != 0)
            throw new Exception("TldStable.Configure signature changed.");
        MethodDefinition expectedConfigure = Expected.Configure(stable.Module);
        if (Checks.BodyFingerprint(expectedConfigure) != Checks.BodyFingerprint(configure))
            throw new Exception("IL of TldStable.Configure does not match the contracted shape.");
        MethodReference presetCall = configure.Body.Instructions.Count == 2 ? configure.Body.Instructions[0].Operand as MethodReference : null;
        if (configure.Body.Instructions.Count != 2 || configure.Body.Instructions[0].OpCode != OpCodes.Call ||
            presetCall == null || presetCall.Name != "ApplyGraphicsPreset" || presetCall.HasThis ||
            presetCall.DeclaringType == null || presetCall.DeclaringType.FullName != "TldMp.StableGameBridge" ||
            presetCall.Parameters.Count != 0 || presetCall.ReturnType.FullName != "System.Void" ||
            configure.Body.Instructions[1].OpCode != OpCodes.Ret)
            throw new Exception("TldStable.Configure does not call the runtime graphics preset exactly once.");
        foreach (Instruction instruction in configure.Body.Instructions)
        {
            MethodReference callee = instruction.Operand as MethodReference;
            if (callee != null && callee.DeclaringType != null && callee.DeclaringType.FullName == "UnityEngine.QualitySettings")
                throw new Exception("TldStable.Configure still writes QualitySettings directly.");
        }
        if (configure.CustomAttributes.Count != 1 ||
            configure.CustomAttributes[0].AttributeType.FullName != "UnityEngine.RuntimeInitializeOnLoadMethodAttribute")
            throw new Exception("TldStable.Configure is not tagged [RuntimeInitializeOnLoadMethod].");
        CustomAttribute initialize = configure.CustomAttributes[0];
        // Read the raw signature blob rather than ConstructorArguments: resolving an
        // enum-typed argument would require the Unity reference assemblies to be present
        // next to the candidate file. Blob = prolog 01 00, int32 0 (AfterSceneLoad), 00 00.
        if (initialize.Constructor.Parameters.Count != 1 ||
            initialize.Constructor.Parameters[0].ParameterType.FullName != "UnityEngine.RuntimeInitializeLoadType")
            throw new Exception("TldStable.Configure must use [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType)].");
        byte[] blob = initialize.GetBlob();
        byte[] expectedBlob = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        if (blob == null || blob.Length != expectedBlob.Length)
            throw new Exception("TldStable.Configure's attribute argument blob has the wrong length.");
        for (int i = 0; i < blob.Length; i++)
            if (blob[i] != expectedBlob[i])
                throw new Exception("TldStable.Configure must run at RuntimeInitializeLoadType.AfterSceneLoad (0).");
        if (Checks.ComputeMaxStack(configure) > configure.Body.MaxStackSize)
            throw new Exception("MaxStackSize too small in TldStable.Configure.");
        if (configure.Body.Instructions.Any(i => i.OpCode.OperandType == OperandType.ShortInlineBrTarget))
            throw new Exception("Short branch left in TldStable.Configure.");

        HashSet<string> calls = new HashSet<string>();
        foreach (Instruction i in save.Body.Instructions)
        {
            MethodReference callee = i.Operand as MethodReference;
            if (callee != null) calls.Add(callee.DeclaringType.FullName + "::" + callee.Name + "/" + callee.Parameters.Count);
        }
        string[] required = new string[]
        {
            "System.IO.FileStream::.ctor/4",
            "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::Serialize/2",
            "System.IO.FileStream::Flush/1",
            "System.IO.File::Exists/1",
            "System.IO.File::Replace/3",
            "System.IO.File::Move/2",
            "System.IO.File::Delete/1",
            "TldMp.StableGameBridge::AfterNativeSaveCommitted/2",
            "UnityEngine.Debug::LogError/1"
        };
        foreach (string call in required)
            if (!calls.Contains(call)) throw new Exception("TldStable.SaveAtomic is missing " + call + ".");
        var saveObservers=save.Body.Instructions.Where(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName=="TldMp.StableGameBridge").ToArray();
        if(saveObservers.Length!=1)throw new Exception("SaveAtomic requires one successful-save observer.");
        var observer=saveObservers[0];var callback=(MethodReference)observer.Operand;
        if(observer.OpCode!=OpCodes.Call || callback.Name!="AfterNativeSaveCommitted" || callback.HasThis || callback.ReturnType.FullName!="System.Void" ||
            callback.Parameters.Count!=2 || callback.Parameters[0].ParameterType.FullName!="System.String" || callback.Parameters[1].ParameterType.FullName!="System.Object" ||
            observer.Previous==null || observer.Previous.OpCode!=OpCodes.Ldarg_1 || observer.Previous.Previous==null || observer.Previous.Previous.OpCode!=OpCodes.Ldarg_0)
            throw new Exception("Successful-save observer signature or arguments changed.");
        var committedStore=observer.Previous.Previous.Previous;
        if(committedStore==null || !(committedStore.OpCode==OpCodes.Stloc || committedStore.OpCode==OpCodes.Stloc_0 || committedStore.OpCode==OpCodes.Stloc_1 ||
            committedStore.OpCode==OpCodes.Stloc_2 || committedStore.OpCode==OpCodes.Stloc_3) || committedStore.Previous==null || committedStore.Previous.OpCode!=OpCodes.Ldc_I4_1)
            throw new Exception("Save observer must follow committed=true after the native file replacement.");
        if (save.Body.Instructions.Any(i => i.OpCode.OperandType == OperandType.ShortInlineBrTarget))
            throw new Exception("Short branch left in TldStable.SaveAtomic.");
        if (!save.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Catch && h.CatchType != null && h.CatchType.FullName == "System.Exception"))
            throw new Exception("TldStable.SaveAtomic has no exception guard.");
        if (!save.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally))
            throw new Exception("TldStable.SaveAtomic has no finally (temp cleanup).");
        if (save.Body.Instructions.Count < 40) throw new Exception("TldStable.SaveAtomic is suspiciously small.");
        if (save.Body.MaxStackSize < Checks.ComputeMaxStack(save))
            throw new Exception("MaxStackSize too small in TldStable.SaveAtomic.");
    }
}



