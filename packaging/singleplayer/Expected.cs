using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Rebuilds the *expected* v4 body of every edited member from the pristine v3 method,
// independently of PatchV4's implementation. VerifyV4 compares BodyFingerprint of these
// synthetic methods with the bodies found in the candidate v4 file.
public static class Expected
{
    public const string StableType = "TldStable";

    public static MethodDefinition SaveBinary(MethodDefinition source)
    {
        MethodReference unduck = source.Body.Instructions.Select(i => i.Operand as MethodReference)
            .FirstOrDefault(m => m != null && m.Name == "UnDuckStringBecauseItIsDuckedByDefault");
        if (unduck == null) throw new InvalidOperationException("v3 SaveBinary lost its UnDuck call.");
        ModuleDefinition module = source.Module;

        MethodDefinition expected = Clone(source);
        Reset(expected);
        expected.Body.InitLocals = false;

        TypeReference stable = new TypeReference("", StableType, module, module.TypeSystem.CoreLibrary);
        MethodReference saveAtomic = new MethodReference("SaveAtomic", module.TypeSystem.Void, stable);
        saveAtomic.HasThis = false;
        saveAtomic.Parameters.Add(new ParameterDefinition("path", ParameterAttributes.None, module.TypeSystem.String));
        saveAtomic.Parameters.Add(new ParameterDefinition("obj", ParameterAttributes.None, module.TypeSystem.Object));

        ILProcessor il = expected.Body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Call, unduck));
        il.Append(Instruction.Create(OpCodes.Starg, expected.Parameters[0]));
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldarg_1));
        il.Append(Instruction.Create(OpCodes.Box, expected.GenericParameters[0]));
        il.Append(Instruction.Create(OpCodes.Call, saveAtomic));
        il.Append(Instruction.Create(OpCodes.Ret));
        expected.Body.MaxStackSize = Math.Max(Checks.ComputeMaxStack(expected), 4);
        return expected;
    }

    public static MethodDefinition VectorEquals(TypeDefinition type, string[] fields)
    {
        MethodDefinition expected = new MethodDefinition("Equals",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig |
            MethodAttributes.NewSlot, type.Module.TypeSystem.Boolean);
        expected.Parameters.Add(new ParameterDefinition("other", ParameterAttributes.None, type));
        MethodReference doubleEquals = DoubleEquals(type);
        Instruction failed = Instruction.Create(OpCodes.Ldc_I4_0);
        ILProcessor il = expected.Body.GetILProcessor();
        foreach (string name in fields)
        {
            FieldDefinition field = type.Fields.Single(f => f.Name == name && !f.IsStatic);
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldflda, field));
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Ldfld, field));
            il.Append(Instruction.Create(OpCodes.Call, doubleEquals));
            il.Append(Instruction.Create(OpCodes.Brfalse, failed));
        }
        il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
        il.Append(Instruction.Create(OpCodes.Ret));
        il.Append(failed);
        il.Append(Instruction.Create(OpCodes.Ret));
        expected.Body.InitLocals = false;
        expected.Body.MaxStackSize = 2;
        return expected;
    }

    public static MethodDefinition Overlap(MethodDefinition source, string from, string to)
    {
        MethodDefinition expected = Clone(source);
        IList<Instruction> instructions = expected.Body.Instructions;
        Instruction[] calls = instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name == from && (((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Physics" || ((MethodReference)i.Operand).DeclaringType.FullName == "TldMp.NativePhysicsQueries")).ToArray();
        if (calls.Length != 1) throw new InvalidOperationException("v3 " + source.FullName + " no longer has one Physics." + from + ".");
        Instruction call = calls[0];
        call.Operand = Checks.PhysicsCheck(source.Module, to, (MethodReference)call.Operand);

        Instruction ldlen = call.Next;
        if (ldlen == null || ldlen.OpCode != OpCodes.Ldlen)
            throw new InvalidOperationException("v3 " + source.FullName + " no longer reads Length after Physics." + from + ".");
        ldlen.OpCode = OpCodes.Nop;
        ldlen.Operand = null;

        Instruction second = ldlen.Next;
        if (second != null && second.OpCode == OpCodes.Ldc_I4_0 && second.Next != null &&
            (second.Next.OpCode == OpCodes.Cgt_Un || second.Next.OpCode == OpCodes.Ceq))
        {
            second.OpCode = OpCodes.Nop;
            second.Operand = null;
            second.Next.OpCode = OpCodes.Nop;
            second.Next.Operand = null;
        }

        expected.Body.MaxStackSize = Math.Max(Checks.ComputeMaxStack(expected), source.Body.MaxStackSize);
        Checks.ExpandBranches(expected);
        return expected;
    }

    public static MethodDefinition AddToFarStuff(MethodDefinition source)
    {
        MethodDefinition expected = Clone(source);
        IList<Instruction> instructions = expected.Body.Instructions;
        int appendIndex = -1;
        for (int i = 0; i < instructions.Count; i++)
        {
            MethodReference callee = instructions[i].Operand as MethodReference;
            if (instructions[i].OpCode == OpCodes.Callvirt && callee != null && callee.Name == "Append" &&
                callee.DeclaringType.FullName == "itemDataClass") { appendIndex = i; break; }
        }
        if (appendIndex < 4) throw new InvalidOperationException("v3 AddToFarStuff(itemDataClass) has no in-loop Append.");

        Instruction self = instructions[appendIndex - 4];
        Instruction dataField = instructions[appendIndex - 3];
        Instruction itemField = instructions[appendIndex - 2];
        Instruction itemArg = instructions[appendIndex - 1];
        Instruction appendCall = instructions[appendIndex];
        if (self.OpCode != OpCodes.Ldarg_0 || dataField.OpCode != OpCodes.Ldfld || itemField.OpCode != OpCodes.Ldfld ||
            itemArg.OpCode != OpCodes.Ldarg_1)
            throw new InvalidOperationException("v3 AddToFarStuff(itemDataClass) append prologue changed.");

        ILProcessor il = expected.Body.GetILProcessor();
        Instruction first = instructions[0];
        il.InsertBefore(first, Instruction.Create(self.OpCode));
        il.InsertBefore(first, Instruction.Create(dataField.OpCode, (FieldReference)dataField.Operand));
        il.InsertBefore(first, Instruction.Create(itemField.OpCode, (FieldReference)itemField.Operand));
        il.InsertBefore(first, Instruction.Create(itemArg.OpCode));
        il.InsertBefore(first, Instruction.Create(appendCall.OpCode, (MethodReference)appendCall.Operand));
        foreach (Instruction instruction in new Instruction[] { self, dataField, itemField, itemArg, appendCall })
        {
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
        }

        expected.Body.MaxStackSize = Math.Max(Checks.ComputeMaxStack(expected), source.Body.MaxStackSize);
        Checks.ExpandBranches(expected);
        return expected;
    }

    // Pass 5: register streamed props with PropTiers after the prefab is stored.
    // The former 1200 m per-object gate was dead IL and has been removed.
    public static MethodDefinition UpdPlaceRemove(MethodDefinition source)
    {
        MethodDefinition expected = Clone(source);
        IList<Instruction> instructions = expected.Body.Instructions;
        ModuleDefinition module = source.Module;

        TypeDefinition objClass = Checks.ExactlyOneType(module, "objGenScript/objClass");
        FieldReference objField = objClass.Fields.Single(f => f.Name == "obj");

        int instantiate = -1;
        for (int i = 0; i < instructions.Count; i++)
            if (CallIs(instructions[i], "UnityEngine.Object", "Instantiate")) { instantiate = i; break; }
        if (instantiate < 0) throw new InvalidOperationException("v3 UpdPlaceRemove no longer instantiates props.");

        int gate = -1;
        MethodReference getValue = null;
        for (int i = 0; i + 5 < instructions.Count && i < instantiate; i++)
        {
            if (instructions[i].OpCode != OpCodes.Ldloca_S && instructions[i].OpCode != OpCodes.Ldloca) continue;
            if (!CallIs(instructions[i + 1], "System.Collections.Generic.KeyValuePair`2", "get_Value")) continue;
            if (instructions[i + 2].OpCode != OpCodes.Ldfld ||
                ((FieldReference)instructions[i + 2].Operand).FullName != objField.FullName) continue;
            if (instructions[i + 3].OpCode != OpCodes.Ldnull) continue;
            if (!CallIs(instructions[i + 4], "UnityEngine.Object", "op_Equality")) continue;
            if (instructions[i + 5].OpCode != OpCodes.Brfalse && instructions[i + 5].OpCode != OpCodes.Brfalse_S) continue;
            gate = i;
            getValue = (MethodReference)instructions[i + 1].Operand;
        }
        if (gate < 0)
            throw new InvalidOperationException("v3 UpdPlaceRemove lost its obj == null guard before Instantiate.");

        VariableDefinition pair = (VariableDefinition)instructions[gate].Operand;
        ILProcessor il = expected.Body.GetILProcessor();

        Instruction storedObject = null;
        for (int i = instantiate + 1; i < instructions.Count; i++)
        {
            FieldReference field = instructions[i].Operand as FieldReference;
            if (instructions[i].OpCode == OpCodes.Stfld && field != null && field.FullName == objField.FullName)
            { storedObject = instructions[i]; break; }
        }
        if (storedObject == null) throw new InvalidOperationException("v3 Instantiate result is not stored in objClass.obj.");
        MethodReference register = PropTierRegister(module);
        il.InsertAfter(storedObject, Instruction.Create(OpCodes.Ldloca_S, pair));
        il.InsertAfter(storedObject.Next, Instruction.Create(OpCodes.Call, getValue));
        il.InsertAfter(storedObject.Next.Next, Instruction.Create(OpCodes.Ldfld, objField));
        il.InsertAfter(storedObject.Next.Next.Next, Instruction.Create(OpCodes.Call, register));

        expected.Body.MaxStackSize = Math.Max(Checks.ComputeMaxStack(expected), source.Body.MaxStackSize);
        Checks.ExpandBranches(expected);
        return expected;
    }

    static MethodReference PropTierRegister(ModuleDefinition module)
    {
        AssemblyNameReference runtime = module.AssemblyReferences.SingleOrDefault(a => a.Name == "Tld.Multiplayer");
        if (runtime == null) throw new InvalidOperationException("v3 assembly has no Tld.Multiplayer reference.");
        TypeReference tiers = new TypeReference("TldMp", "PropTiers", module, runtime);
        TypeReference gameObject = module.GetTypeReferences().First(t => t.FullName == "UnityEngine.GameObject");
        MethodReference register = new MethodReference("Register", module.TypeSystem.Void, tiers);
        register.HasThis = false;
        register.Parameters.Add(new ParameterDefinition("root", ParameterAttributes.None, gameObject));
        return register;
    }

    // Pass 5: TldStable.Configure() -- graphics preset, run once per
    // scene load through [RuntimeInitializeOnLoadMethod(AfterSceneLoad)].
    public static MethodDefinition Configure(ModuleDefinition module)
    {
        TypeReference attribute = module.GetTypeReferences().First(t => t.FullName == "UnityEngine.RuntimeInitializeOnLoadMethodAttribute");
        TypeReference loadType = module.GetTypeReferences().First(t => t.FullName == "UnityEngine.RuntimeInitializeLoadType");
        AssemblyNameReference runtime = module.AssemblyReferences.First(a => a.Name == "Tld.Multiplayer");
        TypeReference bridge = new TypeReference("TldMp", "StableGameBridge", module, runtime);
        MethodReference apply = new MethodReference("ApplyGraphicsPreset", module.TypeSystem.Void, bridge);
        apply.HasThis = false;

        MethodDefinition expected = new MethodDefinition("Configure",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
        ILProcessor il = expected.Body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Call, apply));
        il.Append(Instruction.Create(OpCodes.Ret));
        expected.Body.InitLocals = false;
        expected.Body.MaxStackSize = 1;

        MethodReference constructor = new MethodReference(".ctor", module.TypeSystem.Void, attribute);
        constructor.HasThis = true;
        constructor.Parameters.Add(new ParameterDefinition("loadType", ParameterAttributes.None, loadType));
        CustomAttribute runtimeInitialize = new CustomAttribute(constructor);
        runtimeInitialize.ConstructorArguments.Add(new CustomAttributeArgument(loadType, 0));
        expected.CustomAttributes.Add(runtimeInitialize);
        return expected;
    }

    public static MethodDefinition RefreshSettings(MethodDefinition source)
    {
        MethodDefinition expected = Clone(source);
        TypeReference stable = new TypeReference("", StableType, source.Module, source.Module.TypeSystem.CoreLibrary);
        MethodReference configure = new MethodReference("Configure", source.Module.TypeSystem.Void, stable);
        configure.HasThis = false;
        ILProcessor il = expected.Body.GetILProcessor();
        foreach (Instruction ret in expected.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
        {
            ret.OpCode = OpCodes.Call;
            ret.Operand = configure;
            il.InsertAfter(ret, Instruction.Create(OpCodes.Ret));
        }
        expected.Body.MaxStackSize = Math.Max(Checks.ComputeMaxStack(expected), source.Body.MaxStackSize);
        Checks.ExpandBranches(expected);
        return expected;
    }

    static MethodReference IntQualitySetter(ModuleDefinition module, TypeReference quality, string name)
    {
        MethodReference setter = new MethodReference(name, module.TypeSystem.Void, quality);
        setter.HasThis = false;
        setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
        return setter;
    }

    static bool CallIs(Instruction instruction, string declaringType, string name)
    {
        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) return false;
        MethodReference callee = instruction.Operand as MethodReference;
        if (callee == null || callee.Name != name || callee.DeclaringType == null) return false;
        if (callee.DeclaringType.FullName == declaringType) return true;
        return callee.DeclaringType.GetElementType().FullName == declaringType;
    }

    static MethodReference DoubleEquals(TypeDefinition type)
    {
        MethodDefinition equalsObject = type.Methods.Single(m => m.Name == "Equals" && m.Parameters.Count == 1 &&
            m.Parameters[0].ParameterType.FullName == "System.Object");
        return equalsObject.Body.Instructions.Select(i => i.Operand as MethodReference)
            .First(m => m != null && m.Name == "Equals" && m.DeclaringType.FullName == "System.Double" && m.Parameters.Count == 1);
    }

    // v4 AddPois: the "if (flag)" guard before the ground re-sample is removed, so road-aligned
    // POIs sample terrain at their final (laterally offset) position.
    public static MethodDefinition AddPois(MethodDefinition source)
    {
        MethodDefinition expected = Clone(source);
        MethodBody body = expected.Body;
        Instruction[] code = body.Instructions.ToArray();
        for (int i = 0; i + 2 < code.Length; i++)
        {
            if (code[i].OpCode != OpCodes.Brfalse && code[i].OpCode != OpCodes.Brfalse_S) continue;
            Instruction target = code[i].Operand as Instruction;
            if (target == null) continue;
            if (!IsLoadLocal(code[i + 1])) continue;
            if (code[i + 2].OpCode != OpCodes.Brtrue && code[i + 2].OpCode != OpCodes.Brtrue_S) continue;
            if (code[i + 2].Operand != target) continue;

            bool roadsNear = false;
            for (int j = i + 3; j < code.Length && j < i + 16; j++)
            {
                MethodReference callee = code[j].Operand as MethodReference;
                if (callee != null && callee.Name == "RoadsNear" && callee.DeclaringType != null &&
                    callee.DeclaringType.FullName == "mapSettingScript") { roadsNear = true; break; }
            }
            if (!roadsNear) continue;

            ILProcessor il = body.GetILProcessor();
            il.Remove(code[i + 1]);
            il.Remove(code[i + 2]);
            body.MaxStackSize = Math.Max(Checks.ComputeMaxStack(expected), 1);
            return expected;
        }
        throw new InvalidOperationException("v3 AddPois ground-height guard not found.");
    }

    static bool IsLoadLocal(Instruction instruction)
    {
        return instruction.OpCode == OpCodes.Ldloc || instruction.OpCode == OpCodes.Ldloc_S ||
               instruction.OpCode == OpCodes.Ldloc_0 || instruction.OpCode == OpCodes.Ldloc_1 ||
               instruction.OpCode == OpCodes.Ldloc_2 || instruction.OpCode == OpCodes.Ldloc_3;
    }

    static void Reset(MethodDefinition method)
    {
        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();
    }

    static MethodDefinition Clone(MethodDefinition source)
    {
        MethodDefinition clone = new MethodDefinition(source.Name, source.Attributes, source.ReturnType);
        clone.ImplAttributes = source.ImplAttributes;
        clone.HasThis = source.HasThis;
        clone.ExplicitThis = source.ExplicitThis;
        clone.CallingConvention = source.CallingConvention;
        foreach (ParameterDefinition p in source.Parameters)
            clone.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
        foreach (GenericParameter g in source.GenericParameters)
            clone.GenericParameters.Add(new GenericParameter(g.Name, clone));

        MethodBody body = clone.Body;
        body.InitLocals = source.Body.InitLocals;
        body.MaxStackSize = source.Body.MaxStackSize;
        foreach (VariableDefinition v in source.Body.Variables)
            body.Variables.Add(new VariableDefinition(v.VariableType));

        Dictionary<Instruction, Instruction> map = new Dictionary<Instruction, Instruction>();
        List<KeyValuePair<Instruction, object>> pending = new List<KeyValuePair<Instruction, object>>();
        foreach (Instruction i in source.Body.Instructions)
        {
            object operand = i.Operand;
            if (operand is Instruction || operand is Instruction[])
            {
                Instruction copy = operand is Instruction[]
                    ? Instruction.Create(i.OpCode, new Instruction[0])
                    : Instruction.Create(i.OpCode, Instruction.Create(OpCodes.Nop));
                pending.Add(new KeyValuePair<Instruction, object>(copy, operand));
                map.Add(i, copy);
                body.Instructions.Add(copy);
                continue;
            }
            if (operand is VariableDefinition) operand = body.Variables[((VariableDefinition)operand).Index];
            else if (operand is ParameterDefinition) operand = clone.Parameters[((ParameterDefinition)operand).Index];
            Instruction made = Checks.MakeInstruction(i.OpCode, operand);
            map.Add(i, made);
            body.Instructions.Add(made);
        }
        foreach (KeyValuePair<Instruction, object> entry in pending)
        {
            Instruction[] many = entry.Value as Instruction[];
            if (many != null)
            {
                Instruction[] mapped = new Instruction[many.Length];
                for (int n = 0; n < many.Length; n++) mapped[n] = map[many[n]];
                entry.Key.Operand = mapped;
            }
            else
            {
                entry.Key.Operand = map[(Instruction)entry.Value];
            }
        }
        foreach (ExceptionHandler h in source.Body.ExceptionHandlers)
            body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType)
            {
                TryStart = h.TryStart == null ? null : map[h.TryStart],
                TryEnd = h.TryEnd == null ? null : map[h.TryEnd],
                HandlerStart = h.HandlerStart == null ? null : map[h.HandlerStart],
                HandlerEnd = h.HandlerEnd == null ? null : map[h.HandlerEnd],
                FilterStart = h.FilterStart == null ? null : map[h.FilterStart],
                CatchType = h.CatchType
            });
        return clone;
    }
}

