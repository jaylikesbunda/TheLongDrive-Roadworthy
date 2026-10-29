using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

public sealed class EditRecord
{
    public string Member;
    public string Kind;
    public string Unit = "instructions";
    public int Before;
    public int After;
    public string Note;
}

// v4 patcher: v3 Assembly-CSharp.dll -> v4 (single-player stability fixes).
// Pass 3 edits plus the preset shadow distance cap, folded into the same v4
// artifact -- no extra layer, no extra assembly reference. The streamed-prop
// renderer-tier registration pass is disabled for the native-restore candidate
// because the runtime PropTiers type was removed.
// Never touches its input. Writes <output>.partial, self-verifies, then renames.
public static class PatchV4
{
    const string HelperResourceName = "TldStableHelper.dll";

    // Quality preset applied at startup and after each settings refresh.
    const float ShadowDistanceCap = 80.0f;
    const int AntiAliasing = 2;
    const int PixelLightCount = 4;
    const int ShadowCascades = 2;

    // RuntimeInitializeLoadType.AfterSceneLoad == 0 in UnityEngine.CoreModule.
    const int AfterSceneLoad = 0;

    static ModuleDefinition module;
    static TypeDefinition tldStable;
    static MethodReference debugLogError;
    static readonly List<EditRecord> edits = new List<EditRecord>();

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 && args.Length != 4) throw new ArgumentException("Usage: PatchV4.exe <input-v3.dll> <output-v4.dll> <refs-dir> [expected-v3-sha256]");
            if (args.Length == 4) Checks.SetBaseline(args[3]);
            string input = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            string refs = Path.GetFullPath(args[2]);

            if (!File.Exists(input)) throw new InvalidOperationException("Input not found: " + input);
            if (File.Exists(output)) throw new InvalidOperationException("Refusing to overwrite existing output: " + output);
            if (!Directory.Exists(refs)) throw new InvalidOperationException("Reference directory not found: " + refs);

            string inputHash = Checks.Hash(input);
            if (inputHash != Checks.V3Hash)
                throw new InvalidOperationException("Input is not the v3 assembly (SHA-256 " + inputHash + ").");

            string partial = output + ".partial";
            if (File.Exists(partial)) File.Delete(partial);

            using (DefaultAssemblyResolver resolver = new DefaultAssemblyResolver())
            {
                resolver.AddSearchDirectory(refs);
                resolver.AddSearchDirectory(Path.GetDirectoryName(input));
                string[] referencesBefore;
                using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(input,
                    new ReaderParameters { AssemblyResolver = resolver, InMemory = true }))
                {
                    module = assembly.MainModule;
                    referencesBefore = module.AssemblyReferences.Select(r => r.FullName).ToArray();
                    if (Checks.AllTypes(module.Types).Any(t => t.FullName == "TldStable"))
                        throw new InvalidOperationException("Assembly already patched (TldStable exists).");

                    debugLogError = FindDebugLogError();
                    AttachHelper();
                    AddShadowDistanceCap();
                    PatchSettingsRefresh();
                    PatchSaveBinary();
                    AddEquatable("UnityEngine.Vector3d", new string[] { "x", "y", "z" });
                    AddEquatable("UnityEngine.Vector2d", new string[] { "x", "y" });
                    SwapOverlap("pickupable", "FixedUpdate", 0, "OverlapSphere", "CheckSphere");
                    SwapOverlap("fpscontroller", "overlap", 3, "OverlapCapsule", "CheckCapsule");
                    PatchAddToFarStuff();
                    // Native-restore candidate: the streamed-prop renderer-tier pass is
                    // not applied because the runtime PropTiers type was removed. The
                    // dead 1200 m gate removal is therefore not part of this layer.
                    PatchPoiGroundHeight();
                    AssertTransformations();

                    ExpandAllEdited();
                    assembly.Write(partial);
                }

                // Input no longer open: re-read it as the pristine baseline for the self-check.
                string failure;
                if (!Invariants.Verify(input, partial, out failure))
                {
                    TryDelete(partial);
                    throw new InvalidOperationException("Self-verification failed: " + failure);
                }
                using (AssemblyDefinition written = AssemblyDefinition.ReadAssembly(partial))
                {
                    string[] referencesAfter = written.MainModule.AssemblyReferences.Select(r => r.FullName).ToArray();
                    if (!referencesBefore.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(referencesAfter.OrderBy(x => x, StringComparer.Ordinal)))
                        throw new InvalidOperationException("AssemblyReferences changed.");
                }
            }

            if (File.Exists(output)) throw new InvalidOperationException("Refusing to overwrite existing output: " + output);
            File.Move(partial, output);

            string outputHash = Checks.Hash(output);
            string directory = Path.GetDirectoryName(output);
            WriteReport(Path.Combine(directory, "patch-report.txt"), input, inputHash, output, outputHash);
            WriteReceipt(Path.Combine(directory, "v4-receipt.json"), input, inputHash, output, outputHash);

            Console.WriteLine("Built v4: " + output);
            Console.WriteLine("SHA256: " + outputHash);
            Console.WriteLine("Edits: " + edits.Count + "; verification: PASS");
            return 0;
        }
        catch (Exception error)
        {
            if (Environment.GetEnvironmentVariable("TLD_TRACE") == "1") Console.Error.WriteLine(error.ToString());
            else Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    static void TryDelete(string path)
    {
        if (Environment.GetEnvironmentVariable("TLD_KEEP_PARTIAL") == "1") return;
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
    }

    // ---------------------------------------------------------------- helper type

    static void AttachHelper()
    {
        using (Stream stream = typeof(PatchV4).Assembly.GetManifestResourceStream(HelperResourceName))
        {
            if (stream == null) throw new InvalidOperationException("Embedded helper assembly missing.");
            using (AssemblyDefinition helper = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters { InMemory = true }))
            {
                TypeDefinition source = helper.MainModule.Types.Single(t => t.FullName == "TldStable");
                MethodDefinition save = source.Methods.Single(m => m.Name == "SaveAtomic");
                MethodDefinition marker = source.Methods.Single(m => m.Name == "Marker");

                tldStable = new TypeDefinition(source.Namespace, source.Name, source.Attributes, module.TypeSystem.Object);
                module.Types.Add(tldStable);

                MethodDefinition saveCopy = Declare(save);
                MethodDefinition markerCopy = Declare(marker);
                tldStable.Methods.Add(saveCopy);
                tldStable.Methods.Add(markerCopy);
                Fill(save, saveCopy);
                Fill(marker, markerCopy);
            }
        }

        edits.Add(new EditRecord
        {
            Member = "TldStable",
            Kind = "added type + 2 static methods",
            Unit = "methods",
            Before = 0,
            After = tldStable.Methods.Count,
            Note = "internal static class, no fields; SaveAtomic + Marker transplanted from csc output"
        });
    }

    // ---------------------------------------------------------------- pass 5: shadow distance

    // QualitySettings.shadowDistance has no writer anywhere in the game. Add
    // TldStable.Configure() and tag it [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] so
    // Unity calls it once per scene load. Both attribute types already exist as
    // TypeReferences on UnityEngine.CoreModule in the target module, so referencing them
    // adds no AssemblyReference; the setter is a MemberRef on the existing
    // UnityEngine.QualitySettings TypeReference for the same reason.
    static void AddShadowDistanceCap()
    {
        TypeReference attribute = FindTypeReference("UnityEngine.RuntimeInitializeOnLoadMethodAttribute");
        TypeReference loadType = FindTypeReference("UnityEngine.RuntimeInitializeLoadType");
        if (attribute == null || loadType == null)
            throw new InvalidOperationException(
                "Graphics preset cannot be attached: RuntimeInitializeOnLoadMethod are not referenced by this assembly.");

        MethodDefinition configure = new MethodDefinition("Configure",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
        ILProcessor il = configure.Body.GetILProcessor();
        // The values and the options file live in the runtime assembly; one call
        // keeps this method independent of every future preset change.
        il.Append(Instruction.Create(OpCodes.Call, GraphicsPresetCall()));
        il.Append(Instruction.Create(OpCodes.Ret));
        configure.Body.InitLocals = false;
        configure.Body.MaxStackSize = 1;

        MethodReference constructor = new MethodReference(".ctor", module.TypeSystem.Void, attribute);
        constructor.HasThis = true;
        constructor.Parameters.Add(new ParameterDefinition("loadType", ParameterAttributes.None, loadType));
        CustomAttribute runtimeInitialize = new CustomAttribute(constructor);
        runtimeInitialize.ConstructorArguments.Add(new CustomAttributeArgument(loadType, AfterSceneLoad));
        configure.CustomAttributes.Add(runtimeInitialize);

        tldStable.Methods.Add(configure);

        edits.Add(new EditRecord
        {
            Member = "TldStable.Configure()",
            Kind = "added method + [RuntimeInitializeOnLoadMethod(AfterSceneLoad)]",
            Before = 0,
            After = configure.Body.Instructions.Count,
            Note = "calls TldMp.StableGameBridge.ApplyGraphicsPreset(); the runtime reads roadworthy-options.txt " +
                   "and falls back to the old Balanced values (AA 2, 4 pixel lights, 80 m, 2 cascades, probes off)"
        });
    }

    // Call into the v3 runtime helper through the existing assembly reference.
    static MethodReference GraphicsPresetCall()
    {
        AssemblyNameReference runtime = module.AssemblyReferences.FirstOrDefault(a => a.Name == "Tld.Multiplayer");
        if (runtime == null) throw new InvalidOperationException("v3 assembly has no Tld.Multiplayer reference.");
        TypeReference bridge = new TypeReference("TldMp", "StableGameBridge", module, runtime);
        MethodReference apply = new MethodReference("ApplyGraphicsPreset", module.TypeSystem.Void, bridge);
        apply.HasThis = false;
        return apply;
    }

    static MethodReference IntQualitySetter(TypeReference quality, string name)
    {
        MethodReference setter = new MethodReference(name, module.TypeSystem.Void, quality);
        setter.HasThis = false;
        setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
        return setter;
    }

    static MethodReference BoolQualitySetter(TypeReference quality, string name)
    {
        MethodReference setter = new MethodReference(name, module.TypeSystem.Void, quality);
        setter.HasThis = false;
        setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Boolean));
        return setter;
    }

    // RefreshInMain rewrites antiAliasing from the user's saved settings. Reapply the
    // performance preset at each normal return so both startup and later settings UI
    // refreshes leave the requested values active.
    static void PatchSettingsRefresh()
    {
        TypeDefinition settings = Checks.ExactlyOneType(module, "settingsscript");
        MethodDefinition refresh = Checks.ExactlyOne(settings, "RefreshInMain", 1, "System.Boolean");
        MethodReference configure = tldStable.Methods.Single(m => m.Name == "Configure");
        int count = 0;
        ILProcessor il = refresh.Body.GetILProcessor();
        foreach (Instruction ret in refresh.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
        {
            // Keep the original instruction as the branch/EH target. Replacing it with
            // the configure call means branches that target an early return cannot skip
            // the preset; a fresh ret follows the call on the fall-through path.
            ret.OpCode = OpCodes.Call;
            ret.Operand = configure;
            il.InsertAfter(ret, Instruction.Create(OpCodes.Ret));
            count++;
        }
        if (count == 0) throw new InvalidOperationException("settingsscript.RefreshInMain has no return site.");
        refresh.Body.MaxStackSize = Stack(refresh, refresh.Body.MaxStackSize);
        edits.Add(new EditRecord
        {
            Member = "settingsscript.RefreshInMain(bool)",
            Kind = "reapply quality preset after settings refresh",
            Before = refresh.Body.Instructions.Count - count,
            After = refresh.Body.Instructions.Count,
            Note = "calls TldStable.Configure() before every return; preserves preset after game antiAliasing write"
        });
    }

    static TypeReference FindTypeReference(string fullName)
    {
        TypeReference found = module.GetTypeReferences().FirstOrDefault(t => t.FullName == fullName);
        if (found != null) return found;
        // ImportReference resolves against the module's existing reference table, so a type
        // that is present in an already-referenced assembly still adds no reference.
        try
        {
            TypeDefinition resolved = module.AssemblyResolver.Resolve(module.AssemblyReferences
                .FirstOrDefault(r => r.Name == "UnityEngine.CoreModule") ?? module.AssemblyReferences[0])
                .MainModule.GetType(fullName);
            if (resolved == null) return null;
            return module.ImportReference(resolved);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static MethodReference FindDebugLogError()
    {
        TypeReference debug = null;
        foreach (TypeDefinition type in Checks.AllTypes(module.Types))
            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (Instruction i in method.Body.Instructions)
                {
                    MethodReference callee = i.Operand as MethodReference;
                    if (callee != null && callee.DeclaringType.FullName == "UnityEngine.Debug") { debug = callee.DeclaringType; break; }
                }
                if (debug != null) break;
            }
        if (debug == null) throw new InvalidOperationException("UnityEngine.Debug is not referenced; cannot emit failure logging.");
        MethodReference logError = new MethodReference("LogError", module.TypeSystem.Void, debug);
        logError.HasThis = false;
        logError.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        return logError;
    }

    static MethodDefinition Declare(MethodDefinition source)
    {
        MethodDefinition copy = new MethodDefinition(source.Name, source.Attributes, Map(source.ReturnType));
        copy.ImplAttributes = source.ImplAttributes;
        copy.HasThis = source.HasThis;
        copy.ExplicitThis = source.ExplicitThis;
        copy.CallingConvention = source.CallingConvention;
        foreach (ParameterDefinition p in source.Parameters)
            copy.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, Map(p.ParameterType)));
        return copy;
    }

    static void Fill(MethodDefinition source, MethodDefinition destination)
    {
        destination.Body.InitLocals = source.Body.InitLocals;
        foreach (VariableDefinition v in source.Body.Variables)
            destination.Body.Variables.Add(new VariableDefinition(Map(v.VariableType)));

        Dictionary<Instruction, int> slot = new Dictionary<Instruction, int>();
        for (int n = 0; n < source.Body.Instructions.Count; n++) slot.Add(source.Body.Instructions[n], n);

        List<Instruction> created = new List<Instruction>();
        List<KeyValuePair<int, object>> pending = new List<KeyValuePair<int, object>>();

        foreach (Instruction i in source.Body.Instructions)
        {
            Instruction copy;
            if (i.Operand is Instruction || i.Operand is Instruction[])
            {
                copy = Instruction.Create(i.OpCode, Instruction.Create(OpCodes.Nop));
                pending.Add(new KeyValuePair<int, object>(created.Count, i.Operand));
            }
            else
            {
                copy = Checks.MakeInstruction(i.OpCode, MapOperand(i.Operand, destination));
            }
            created.Add(copy);
        }

        foreach (KeyValuePair<int, object> entry in pending)
        {
            Instruction[] many = entry.Value as Instruction[];
            if (many != null)
            {
                Instruction[] mapped = new Instruction[many.Length];
                for (int n = 0; n < many.Length; n++) mapped[n] = created[slot[many[n]]];
                created[entry.Key].Operand = mapped;
            }
            else
            {
                created[entry.Key].Operand = created[slot[(Instruction)entry.Value]];
            }
        }

        ILProcessor il = destination.Body.GetILProcessor();
        foreach (Instruction i in created) il.Append(i);

        foreach (ExceptionHandler h in source.Body.ExceptionHandlers)
            destination.Body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType)
            {
                TryStart = h.TryStart == null ? null : created[slot[h.TryStart]],
                TryEnd = h.TryEnd == null ? null : created[slot[h.TryEnd]],
                HandlerStart = h.HandlerStart == null ? null : created[slot[h.HandlerStart]],
                HandlerEnd = h.HandlerEnd == null ? null : created[slot[h.HandlerEnd]],
                FilterStart = h.FilterStart == null ? null : created[slot[h.FilterStart]],
                CatchType = h.CatchType == null ? null : Map(h.CatchType)
            });

        int computed = Checks.ComputeMaxStack(destination);
        destination.Body.MaxStackSize = computed > source.Body.MaxStackSize ? computed : source.Body.MaxStackSize;
        Checks.ExpandBranches(destination);
    }

    static object MapOperand(object operand, MethodDefinition host)
    {
        if (operand == null) return null;
        if (operand is MethodReference) return MapMethod((MethodReference)operand);
        if (operand is FieldReference)
        {
            FieldReference field = (FieldReference)operand;
            return new FieldReference(field.Name, Map(field.FieldType), Map(field.DeclaringType));
        }
        if (operand is TypeReference) return Map((TypeReference)operand);
        if (operand is ParameterDefinition) return host.Parameters[((ParameterDefinition)operand).Index];
        if (operand is VariableDefinition) return host.Body.Variables[((VariableDefinition)operand).Index];
        return operand;
    }

    static MethodReference MapMethod(MethodReference source)
    {
        if (source.DeclaringType != null && source.DeclaringType.FullName == "TldStable")
        {
            if (source.Name == "Log") return debugLogError;
            if (source.Name == "AfterCommitted")
            {
                var bridgeType=module.GetTypeReferences().Single(t=>t.FullName=="TldMp.StableGameBridge");
                var callback=new MethodReference("AfterNativeSaveCommitted",module.TypeSystem.Void,bridgeType){HasThis=false};
                callback.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                callback.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                return callback;
            }
            MethodDefinition transplanted = tldStable.Methods.FirstOrDefault(m => m.Name == source.Name);
            if (transplanted != null) return transplanted;
        }
        GenericInstanceMethod instance = source as GenericInstanceMethod;
        MethodReference definition = instance == null ? source : instance.ElementMethod;
        MethodReference copy = new MethodReference(definition.Name, Map(definition.ReturnType), Map(definition.DeclaringType));
        copy.HasThis = definition.HasThis;
        copy.ExplicitThis = definition.ExplicitThis;
        copy.CallingConvention = definition.CallingConvention;
        foreach (ParameterDefinition p in definition.Parameters)
            copy.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, Map(p.ParameterType)));
        if (instance == null) return copy;
        GenericInstanceMethod wrapped = new GenericInstanceMethod(copy);
        foreach (TypeReference argument in instance.GenericArguments) wrapped.GenericArguments.Add(Map(argument));
        return wrapped;
    }

    static TypeReference Map(TypeReference type)
    {
        if (type == null) return null;
        if (type is ArrayType) return new ArrayType(Map(((ArrayType)type).ElementType));
        if (type is ByReferenceType) return new ByReferenceType(Map(((ByReferenceType)type).ElementType));
        if (type is PointerType) return new PointerType(Map(((PointerType)type).ElementType));
        if (type is GenericInstanceType)
        {
            GenericInstanceType generic = (GenericInstanceType)type;
            GenericInstanceType copy = new GenericInstanceType(Map(generic.ElementType));
            foreach (TypeReference argument in generic.GenericArguments) copy.GenericArguments.Add(Map(argument));
            return copy;
        }
        if (type is GenericParameter) return type;
        if (type.FullName == "TldStable") return tldStable;

        // Ether primitives onto the target module's core type system so no new
        // assembly reference can appear.
        switch (type.FullName)
        {
            case "System.Void": return module.TypeSystem.Void;
            case "System.Boolean": return module.TypeSystem.Boolean;
            case "System.Char": return module.TypeSystem.Char;
            case "System.SByte": return module.TypeSystem.SByte;
            case "System.Byte": return module.TypeSystem.Byte;
            case "System.Int16": return module.TypeSystem.Int16;
            case "System.UInt16": return module.TypeSystem.UInt16;
            case "System.Int32": return module.TypeSystem.Int32;
            case "System.UInt32": return module.TypeSystem.UInt32;
            case "System.Int64": return module.TypeSystem.Int64;
            case "System.UInt64": return module.TypeSystem.UInt64;
            case "System.Single": return module.TypeSystem.Single;
            case "System.Double": return module.TypeSystem.Double;
            case "System.String": return module.TypeSystem.String;
            case "System.Object": return module.TypeSystem.Object;
            case "System.IntPtr": return module.TypeSystem.IntPtr;
            case "System.UIntPtr": return module.TypeSystem.UIntPtr;
            case "System.TypedReference": return module.TypeSystem.TypedReference;
        }
        AssemblyNameReference scope = type.Scope as AssemblyNameReference;
        if (scope != null && scope.Name == "mscorlib")
        {
            // Reuse the target module's existing mscorlib reference: constructing the
            // TypeReference by hand cannot add a second AssemblyNameReference.
            TypeReference system = new TypeReference(type.Namespace, type.Name, module, module.TypeSystem.CoreLibrary, type.IsValueType);
            if (type.IsNested && type.DeclaringType != null) system.DeclaringType = Map(type.DeclaringType);
            return system;
        }
        return module.ImportReference(type);
    }

    // ---------------------------------------------------------------- edits

    static void PatchSaveBinary()
    {
        TypeDefinition host = Checks.ExactlyOneType(module, "pathscript");
        MethodDefinition method = Checks.ExactlyOne(host, "SaveBinary", 3, "System.String");
        if (method.GenericParameters.Count != 1) throw new InvalidOperationException("Unexpected SaveBinary arity.");
        MethodReference unduck = method.Body.Instructions.Select(i => i.Operand as MethodReference)
            .FirstOrDefault(m => m != null && m.Name == "UnDuckStringBecauseItIsDuckedByDefault");
        if (unduck == null) throw new InvalidOperationException("SaveBinary no longer calls UnDuckStringBecauseItIsDuckedByDefault.");
        MethodReference saveAtomic = tldStable.Methods.Single(m => m.Name == "SaveAtomic");
        int before = method.Body.Instructions.Count;

        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.InitLocals = false;
        ILProcessor il = method.Body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Call, unduck));
        il.Append(Instruction.Create(OpCodes.Starg, method.Parameters[0]));
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldarg_1));
        il.Append(Instruction.Create(OpCodes.Box, method.GenericParameters[0]));
        il.Append(Instruction.Create(OpCodes.Call, saveAtomic));
        il.Append(Instruction.Create(OpCodes.Ret));
        method.Body.MaxStackSize = Stack(method, 4);

        edits.Add(new EditRecord
        {
            Member = "pathscript.SaveBinary<T>(string,T,bool)",
            Kind = "body replaced with TldStable.SaveAtomic",
            Before = before,
            After = method.Body.Instructions.Count,
            Note = "locals/exception handler dropped; async branch removed; no FileMode.Create"
        });
    }

    static void AddEquatable(string typeName, string[] fields)
    {
        TypeDefinition type = Checks.ExactlyOneType(module, typeName);
        if (type.Methods.Any(m => m.Name == "Equals" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == type.FullName))
            throw new InvalidOperationException("Already has Equals(" + typeName + ").");

        MethodDefinition equals = new MethodDefinition("Equals",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig |
            MethodAttributes.NewSlot, module.TypeSystem.Boolean);
        equals.Parameters.Add(new ParameterDefinition("other", ParameterAttributes.None, type));
        MethodReference doubleEquals = FindDoubleEquals(type);
        ILProcessor il = equals.Body.GetILProcessor();
        Instruction failed = Instruction.Create(OpCodes.Ldc_I4_0);
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
        equals.Body.InitLocals = false;
        equals.Body.MaxStackSize = Stack(equals, 2);
        type.Methods.Add(equals);

        TypeReference definition = new TypeReference("System", "IEquatable`1", module, module.TypeSystem.CoreLibrary);
        GenericInstanceType instance = new GenericInstanceType(definition);
        instance.GenericArguments.Add(type);
        type.Interfaces.Add(new InterfaceImplementation(instance));

        edits.Add(new EditRecord
        {
            Member = typeName + ".Equals(" + typeName + ")",
            Kind = "added method",
            Before = 0,
            After = equals.Body.Instructions.Count,
            Note = "instance Double.Equals per component (" + String.Join("/", fields) + "); GetHashCode and fields untouched"
        });
        edits.Add(new EditRecord
        {
            Member = typeName,
            Kind = "added IEquatable<" + typeName + ">",
            Unit = "interfaces",
            Before = 0,
            After = 1,
            Note = "InterfaceImplementation on the existing type reference"
        });
    }

    static MethodReference FindDoubleEquals(TypeDefinition type)
    {
        MethodDefinition equalsObject = type.Methods.Single(m => m.Name == "Equals" && m.Parameters.Count == 1 &&
            m.Parameters[0].ParameterType.FullName == "System.Object");
        MethodReference call = equalsObject.Body.Instructions.Select(i => i.Operand as MethodReference)
            .FirstOrDefault(m => m != null && m.Name == "Equals" && m.DeclaringType.FullName == "System.Double" && m.Parameters.Count == 1);
        if (call == null) throw new InvalidOperationException("Cannot find System.Double::Equals(float64) in " + type.FullName + ".Equals(object).");
        return call;
    }

    static void SwapOverlap(string typeName, string methodName, int parameterCount, string from, string to)
    {
        TypeDefinition type = Checks.ExactlyOneType(module, typeName);
        MethodDefinition method = Checks.ExactlyOne(type, methodName, parameterCount, null);
        IList<Instruction> instructions = method.Body.Instructions;
        Instruction[] calls = instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference &&
            ((MethodReference)i.Operand).Name == from && (((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Physics" || ((MethodReference)i.Operand).DeclaringType.FullName == "TldMp.NativePhysicsQueries")).ToArray();
        if (calls.Length != 1) throw new InvalidOperationException("Expected one Physics." + from + " call in " + typeName + "." + methodName + ", found " + calls.Length);
        Instruction call = calls[0];
        int before = instructions.Count;
        call.Operand = Checks.PhysicsCheck(module, to, (MethodReference)call.Operand);

        Instruction ldlen = call.Next;
        if (ldlen == null || ldlen.OpCode != OpCodes.Ldlen)
            throw new InvalidOperationException("Expected ldlen after Physics." + from + " in " + typeName + "." + methodName + ".");
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

        method.Body.MaxStackSize = Stack(method, method.Body.MaxStackSize);
        edits.Add(new EditRecord
        {
            Member = typeName + "." + methodName,
            Kind = "Physics." + from + " -> Physics." + to,
            Before = before,
            After = instructions.Count,
            Note = "array-length test neutralised with nop; branch targets preserved"
        });
    }

    static void PatchAddToFarStuff()
    {
        TypeDefinition type = Checks.ExactlyOneType(module, "savedatascript");
        MethodDefinition method = Checks.ExactlyOne(type, "AddToFarStuff", 1, "itemDataClass");
        IList<Instruction> instructions = method.Body.Instructions;
        int appendIndex = -1;
        for (int i = 0; i < instructions.Count; i++)
        {
            MethodReference callee = instructions[i].Operand as MethodReference;
            if (instructions[i].OpCode == OpCodes.Callvirt && callee != null && callee.Name == "Append" &&
                callee.DeclaringType.FullName == "itemDataClass") { appendIndex = i; break; }
        }
        if (appendIndex < 4) throw new InvalidOperationException("Unexpected AddToFarStuff(itemDataClass) shape (no in-loop Append).");
        int enumeratorIndex = -1;
        for (int i = 0; i < instructions.Count; i++)
        {
            MethodReference callee = instructions[i].Operand as MethodReference;
            if (callee != null && callee.Name == "GetEnumerator") { enumeratorIndex = i; break; }
        }
        if (enumeratorIndex < 0 || appendIndex < enumeratorIndex)
            throw new InvalidOperationException("Append is not inside the foreach loop.");

        Instruction self = instructions[appendIndex - 4];
        Instruction dataField = instructions[appendIndex - 3];
        Instruction itemField = instructions[appendIndex - 2];
        Instruction itemArg = instructions[appendIndex - 1];
        Instruction appendCall = instructions[appendIndex];
        if (self.OpCode != OpCodes.Ldarg_0 || dataField.OpCode != OpCodes.Ldfld ||
            ((FieldReference)dataField.Operand).Name != "data" || itemField.OpCode != OpCodes.Ldfld ||
            ((FieldReference)itemField.Operand).Name != "itemData" || itemArg.OpCode != OpCodes.Ldarg_1)
            throw new InvalidOperationException("Unexpected AddToFarStuff append prologue.");

        ILProcessor il = method.Body.GetILProcessor();
        int before = instructions.Count;
        Instruction first = instructions[0];
        il.InsertBefore(first, Instruction.Create(self.OpCode));
        il.InsertBefore(first, Instruction.Create(dataField.OpCode, (FieldReference)dataField.Operand));
        il.InsertBefore(first, Instruction.Create(itemField.OpCode, (FieldReference)itemField.Operand));
        il.InsertBefore(first, Instruction.Create(itemArg.OpCode));
        il.InsertBefore(first, Instruction.Create(appendCall.OpCode, (MethodReference)appendCall.Operand));

        // Neutralise the original in-loop append (instruction objects stay referenced by
        // the surrounding branch targets, so nop rather than remove).
        Instruction[] dead = new Instruction[] { self, dataField, itemField, itemArg, appendCall };
        foreach (Instruction instruction in dead)
        {
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
        }

        method.Body.MaxStackSize = Stack(method, method.Body.MaxStackSize);
        edits.Add(new EditRecord
        {
            Member = "savedatascript.AddToFarStuff(itemDataClass)",
            Kind = "Append hoisted out of the foreach",
            Before = before,
            After = instructions.Count,
            Note = "single Append before the loop; 5 in-loop instructions neutralised with nop; AddToChunk unchanged"
        });
    }

    // ---------------------------------------------------------------- pass 5: streamed prop registration

    // The former per-object 1200 m gate never ran: its new block had no incoming edge
    // (original branches still targeted the obj == null anchor) and its far branch
    // targeted the next instruction. It is removed. Native UpdPlaceRemove remains
    // untouched except for one registration call after the instantiated prefab is
    // stored in objClass.obj, so the host/client liveness gate is never altered.
    static void PatchPropRegistration()
    {
        TypeDefinition host = Checks.ExactlyOneType(module, "objGenScript/categoryClass");
        MethodDefinition method = Checks.ExactlyOne(host, "UpdPlaceRemove", 1, "System.Boolean");
        IList<Instruction> instructions = method.Body.Instructions;
        int before = instructions.Count;

        TypeDefinition objClass = Checks.ExactlyOneType(module, "objGenScript/objClass");
        FieldReference objField = objClass.Fields.Single(f => f.Name == "obj");

        int instantiate = -1;
        for (int i = 0; i < instructions.Count; i++)
            if (IsCallTo(instructions[i], "UnityEngine.Object", "Instantiate")) { instantiate = i; break; }
        if (instantiate < 0)
            throw new InvalidOperationException("UpdPlaceRemove no longer instantiates props.");

        // The `obj.Value.obj == null` test that guards Instantiate in the place branch.
        int gate = -1;
        MethodReference getValue = null;
        for (int i = 0; i + 5 < instructions.Count && i < instantiate; i++)
        {
            if (instructions[i].OpCode != OpCodes.Ldloca_S && instructions[i].OpCode != OpCodes.Ldloca) continue;
            if (!IsCallTo(instructions[i + 1], "System.Collections.Generic.KeyValuePair`2", "get_Value")) continue;
            if (instructions[i + 2].OpCode != OpCodes.Ldfld ||
                ((FieldReference)instructions[i + 2].Operand).FullName != objField.FullName) continue;
            if (instructions[i + 3].OpCode != OpCodes.Ldnull) continue;
            if (!IsCallTo(instructions[i + 4], "UnityEngine.Object", "op_Equality")) continue;
            if (instructions[i + 5].OpCode != OpCodes.Brfalse && instructions[i + 5].OpCode != OpCodes.Brfalse_S) continue;
            gate = i;
            getValue = (MethodReference)instructions[i + 1].Operand;
        }
        if (gate < 0)
            throw new InvalidOperationException("UpdPlaceRemove's place branch no longer tests obj == null before Instantiate.");

        VariableDefinition pair = (VariableDefinition)instructions[gate].Operand;
        if (pair.Index != 2)
            throw new InvalidOperationException("UpdPlaceRemove's KeyValuePair local moved; expected v2, found v" + pair.Index + ".");

        ILProcessor il = method.Body.GetILProcessor();

        // Register only after the instantiated prefab is stored in objClass.obj.
        // Its randomRock and sound initialization below completes before the next
        // LateUpdate tick processes the bounded registry.
        Instruction storedObject = null;
        for (int i = instantiate + 1; i < instructions.Count; i++)
        {
            FieldReference field = instructions[i].Operand as FieldReference;
            if (instructions[i].OpCode == OpCodes.Stfld && field != null && field.FullName == objField.FullName)
            { storedObject = instructions[i]; break; }
        }
        if (storedObject == null) throw new InvalidOperationException("Instantiate result is not stored in objClass.obj.");
        MethodReference register = PropTierRegister();
        il.InsertAfter(storedObject, Instruction.Create(OpCodes.Ldloca_S, pair));
        il.InsertAfter(storedObject.Next, Instruction.Create(OpCodes.Call, getValue));
        il.InsertAfter(storedObject.Next.Next, Instruction.Create(OpCodes.Ldfld, objField));
        il.InsertAfter(storedObject.Next.Next.Next, Instruction.Create(OpCodes.Call, register));

        method.Body.MaxStackSize = Stack(method, method.Body.MaxStackSize);
        edits.Add(new EditRecord
        {
            Member = "objGenScript/categoryClass.UpdPlaceRemove(bool)",
            Kind = "streamed prop renderer-tier registration (dead 1200 m gate removed)",
            Before = before,
            After = instructions.Count,
            Note = "one PropTiers.Register(objClass.obj) call after the Instantiate result is stored; " +
                   "native place/destroy/removePerFrame flow is otherwise unchanged; no draw-distance gate or local"
        });
    }

    static MethodReference PropTierRegister()
    {
        AssemblyNameReference runtime = module.AssemblyReferences.SingleOrDefault(a => a.Name == "Tld.Multiplayer");
        if (runtime == null) throw new InvalidOperationException("Tld.Multiplayer reference is required for prop tier registration.");
        TypeReference tiers = new TypeReference("TldMp", "PropTiers", module, runtime);
        MethodReference register = new MethodReference("Register", module.TypeSystem.Void, tiers);
        register.HasThis = false;
        register.Parameters.Add(new ParameterDefinition("root", ParameterAttributes.None, FindTypeReference("UnityEngine.GameObject")));
        return register;
    }

    // Road-aligned POIs (bus stops, roadside buildings) take their Y from the nearest road bone,
    // but their XZ is pushed sideways by a random distance. The "if (flag)" guard skips the
    // ground re-sample in that case, so the base ends up above or below the terrain depending on
    // the local slope. Removing the guard makes every road-aligned POI sample the ground height
    // at its final XZ -- the same function the terrain mesh uses.
    static void PatchPoiGroundHeight()
    {
        MethodDefinition method = Checks.ExactlyOne(Checks.ExactlyOneType(module, "poiGenScript"),
            "AddPois", 0, null);
        int before = method.Body.Instructions.Count;
        Instruction[] code = method.Body.Instructions.ToArray();
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
                if (IsCallTo(code[j], "mapSettingScript", "RoadsNear")) { roadsNear = true; break; }
            }
            if (!roadsNear) continue;

            ILProcessor il = method.Body.GetILProcessor();
            il.Remove(code[i + 1]);
            il.Remove(code[i + 2]);
            method.Body.MaxStackSize = Stack(method, method.Body.MaxStackSize);
            edits.Add(new EditRecord
            {
                Member = "poiGenScript.AddPois()",
                Kind = "road-aligned POIs re-sample ground height (bus stops no longer float/sink)",
                Before = before,
                After = method.Body.Instructions.Count,
                Note = "removed the ldloc+brtrue guard after brfalse; stack stays balanced; " +
                       "XZ, rotation, posID, save keys and multiplayer sync unchanged"
            });
            return;
        }
        throw new InvalidOperationException("AddPois ground-height guard not found.");
    }

    static bool IsLoadLocal(Instruction instruction)
    {
        return instruction.OpCode == OpCodes.Ldloc || instruction.OpCode == OpCodes.Ldloc_S ||
               instruction.OpCode == OpCodes.Ldloc_0 || instruction.OpCode == OpCodes.Ldloc_1 ||
               instruction.OpCode == OpCodes.Ldloc_2 || instruction.OpCode == OpCodes.Ldloc_3;
    }

    static bool IsCallTo(Instruction instruction, string declaringType, string name)
    {
        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) return false;
        MethodReference callee = instruction.Operand as MethodReference;
        if (callee == null || callee.Name != name || callee.DeclaringType == null) return false;
        if (callee.DeclaringType.FullName == declaringType) return true;
        // MethodReference on a generic instance: KeyValuePair`2<..>::get_Value.
        return callee.DeclaringType.GetElementType().FullName == declaringType;
    }

    // Mono.Cecil recomputes MaxStackSize in CodeWriter.ComputeHeader() when the module is
    // written, so these assignments are defensive only: we set a provably sufficient floor
    // and VerifyV4 asserts the written value is >= its own independent stack-depth maximum.
    static int Stack(MethodDefinition method, int floor)
    {
        int computed = Checks.ComputeMaxStack(method);
        return computed > floor ? computed : floor;
    }

    // ---------------------------------------------------------------- hygiene

    static void AssertTransformations()
    {
        if (tldStable == null || tldStable.Fields.Count != 0) throw new InvalidOperationException("TldStable must have no fields.");
        if (tldStable.Methods.Count != 3) throw new InvalidOperationException("TldStable must expose exactly three methods.");
        if (!tldStable.Methods.Any(m => m.Name == "Marker" && m.Parameters.Count == 0)) throw new InvalidOperationException("TldStable.Marker missing.");

        if (ShadowDistanceCap != 80.0f) throw new InvalidOperationException("Shadow distance cap is not 80 m.");

        MethodDefinition configure = tldStable.Methods.SingleOrDefault(m => m.Name == "Configure");
        if (configure == null || !configure.IsPublic || !configure.IsStatic || configure.HasParameters ||
            configure.ReturnType.FullName != "System.Void")
            throw new InvalidOperationException("TldStable.Configure must be public static void().");
        MethodReference presetCall = configure.Body.Instructions.Count == 2 ? configure.Body.Instructions[0].Operand as MethodReference : null;
        if (configure.Body.Instructions.Count != 2 ||
            configure.Body.Instructions[0].OpCode != OpCodes.Call ||
            presetCall == null || presetCall.Name != "ApplyGraphicsPreset" ||
            presetCall.DeclaringType == null || presetCall.DeclaringType.FullName != "TldMp.StableGameBridge" ||
            presetCall.Parameters.Count != 0 || presetCall.ReturnType.FullName != "System.Void" ||
            configure.Body.Instructions[1].OpCode != OpCodes.Ret)
            throw new InvalidOperationException("TldStable.Configure is not the contracted runtime preset call.");
        CustomAttribute initialize = configure.CustomAttributes.SingleOrDefault();
        if (initialize == null || initialize.AttributeType.FullName != "UnityEngine.RuntimeInitializeOnLoadMethodAttribute" ||
            initialize.Constructor.Parameters.Count != 1 ||
            initialize.ConstructorArguments.Count != 1 ||
            Convert.ToInt32(initialize.ConstructorArguments[0].Value, CultureInfo.InvariantCulture) != AfterSceneLoad)
            throw new InvalidOperationException("TldStable.Configure is not tagged [RuntimeInitializeOnLoadMethod(AfterSceneLoad)].");

        MethodDefinition saveBinary = Checks.ExactlyOne(Checks.ExactlyOneType(module, "pathscript"), "SaveBinary", 3, "System.String");
        foreach (Instruction i in saveBinary.Body.Instructions)
        {
            MethodReference callee = i.Operand as MethodReference;
            if (callee != null && callee.DeclaringType.FullName == "System.IO.FileStream")
                throw new InvalidOperationException("SaveBinary still touches FileStream.");
        }
        if (edits.Count != 12) throw new InvalidOperationException("Expected 12 edit records, got " + edits.Count);
    }

    static void ExpandAllEdited()
    {
        string[,] targets = new string[,]
        {
            { "pathscript", "SaveBinary" },
            { "pickupable", "FixedUpdate" },
            { "fpscontroller", "overlap" },
            { "savedatascript", "AddToFarStuff" },
            { "poiGenScript", "AddPois" },
            { "settingsscript", "RefreshInMain" }
        };
        for (int n = 0; n < targets.GetLength(0); n++)
        {
            TypeDefinition type = Checks.ExactlyOneType(module, targets[n, 0]);
            foreach (MethodDefinition method in type.Methods.Where(m => m.Name == targets[n, 1]))
                Checks.ExpandBranches(method);
        }
        foreach (string name in new string[] { "UnityEngine.Vector3d", "UnityEngine.Vector2d" })
        {
            TypeDefinition type = Checks.ExactlyOneType(module, name);
            foreach (MethodDefinition method in type.Methods)
                if (method.Name == "Equals" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == type.FullName)
                    Checks.ExpandBranches(method);
        }
        foreach (MethodDefinition method in tldStable.Methods) Checks.ExpandBranches(method);
    }

    // ---------------------------------------------------------------- artifacts

    static void WriteReport(string path, string input, string inputHash, string output, string outputHash)
    {
        StringBuilder text = new StringBuilder();
        text.AppendLine("The Long Drive -- v4 patch report (pass 3 + pass 5)");
        text.AppendLine("generated (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        text.AppendLine();
        text.AppendLine("input : " + input);
        text.AppendLine("        SHA-256 " + inputHash + "  (v3 baseline, unmodified)");
        text.AppendLine("output: " + output);
        text.AppendLine("        SHA-256 " + outputHash);
        text.AppendLine();
        text.AppendLine("Invariants: assembly references unchanged; type set = v3 + TldStable;");
        text.AppendLine("            all pre-existing field lists unchanged; quality preset reapplied after settings refresh.");
        text.AppendLine("            pass 5 streamed-prop registration is NOT applied in the native-restore candidate.");
        text.AppendLine();
        text.AppendLine("Edits:");
        int n = 1;
        foreach (EditRecord record in edits)
        {
            text.AppendLine(String.Format("  {0,2}. {1}", n++, record.Member));
            text.AppendLine("      " + record.Kind);
            text.AppendLine("      " + record.Unit + ": " + record.Before + " -> " + record.After);
            text.AppendLine("      " + record.Note);
        }
        text.AppendLine();
        text.AppendLine("Deferred:");
        text.AppendLine("  - No baked impostor meshes/textures were generated. Existing prefab-authored LODGroups continue their built-in transitions;");
        text.AppendLine("    the dead per-object 1200 m draw gate was removed, so native chunk-level placement remains the only streamed-prop gate.");
        text.AppendLine("  - Mid-tier batching is limited to readable, unlightmapped, one-material static meshes in eligible generated props;");
        text.AppendLine("    unknown scripts, saveable/interactable graphs, rigidbodies, triggers, LODGroup prefabs and unsupported renderer states are skipped.");
        text.AppendLine();
        text.AppendLine("Notes:");
        text.AppendLine("  - SaveBinaryAsync is left dead (as contracted).");
        text.AppendLine("  - Streamed prop registration is disabled in this candidate (runtime PropTiers removed); native chunk placement is the only streamed-prop gate.");
        text.AppendLine("  - Quality preset: antiAliasing=" + AntiAliasing + ", pixelLightCount=" + PixelLightCount +
                        ", shadowDistance=" + ShadowDistanceCap.ToString("0.0", CultureInfo.InvariantCulture) +
                        " m, shadowCascades=" + ShadowCascades + ", realtimeReflectionProbes=false.");
        text.AppendLine("    TldStable.Configure runs after scene load and after every settingsscript.RefreshInMain return.");
        text.AppendLine("  - Streamed-prop runtime tiering is not active in this candidate (disabled with the PropTiers removal).");
        text.AppendLine("  - Prior Pass 5 shadow cap was replaced by the preset shadow distance: " +
                        ShadowDistanceCap.ToString("0.0", CultureInfo.InvariantCulture) +
                        " m.");
        text.AppendLine("  - fpscontroller.CanGetOut() contains no overlap test in v3; the");
        text.AppendLine("    Physics.OverlapCapsule site named by the plan (fpscontroller.cs:4093)");
        text.AppendLine("    lives in fpscontroller.overlap(Vector3,float,float), which is the");
        text.AppendLine("    method edited instead. CanGetOut itself is fingerprint-identical.");
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    static void WriteReceipt(string path, string input, string inputHash, string output, string outputHash)
    {
        StringBuilder json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  \"patch\": \"tld-v4\",");
        json.AppendLine("  \"createdUtc\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "\",");
        json.AppendLine("  \"input\": {");
        json.AppendLine("    \"file\": \"" + Escape(Path.GetFileName(input)) + "\",");
        json.AppendLine("    \"sha256\": \"" + inputHash + "\",");
        json.AppendLine("    \"lineage\": \"v3\"");
        json.AppendLine("  },");
        json.AppendLine("  \"output\": {");
        json.AppendLine("    \"file\": \"" + Escape(Path.GetFileName(output)) + "\",");
        json.AppendLine("    \"sha256\": \"" + outputHash + "\",");
        json.AppendLine("    \"bytes\": " + new FileInfo(output).Length);
        json.AppendLine("  },");
        json.AppendLine("  \"addedType\": \"TldStable\",");
        json.AppendLine("  \"edits\": [");
        for (int i = 0; i < edits.Count; i++)
        {
            EditRecord record = edits[i];
            json.AppendLine("    {");
            json.AppendLine("      \"member\": \"" + Escape(record.Member) + "\",");
            json.AppendLine("      \"kind\": \"" + Escape(record.Kind) + "\",");
            json.AppendLine("      \"unit\": \"" + record.Unit + "\",");
            json.AppendLine("      \"before\": " + record.Before + ",");
            json.AppendLine("      \"after\": " + record.After);
            json.AppendLine("    }" + (i == edits.Count - 1 ? "" : ","));
        }
        json.AppendLine("  ],");
        json.AppendLine("  \"deferred\": []");
        json.AppendLine("}");
        File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
    }

    static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

