using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TldMp
{
    // Rewrites TLDLoader 3.x and TLDLoader mods, built for the public game, to the
    // beta's names (TldLoaderCompatPolicy). Uses only Mono.Cecil, so the offline
    // gate runs it against the real TLDLoader.dll. Nothing here touches Unity.
    public sealed class TldLoaderTranslator
    {
        public sealed class Report
        {
            public byte[] Bytes;
            public int Rewrites;
            public readonly List<string> StrippedPatches = new List<string>();
            public readonly List<string> ObsoletePatches = new List<string>();
            // Game members the code still uses that the beta does not have, with users.
            public readonly SortedDictionary<string, SortedSet<string>> Missing = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            public bool LoadDllHooked, SceneHookRemoved, RegisterHooked;
            public readonly List<string> ReplacedBodies = new List<string>();
            // Harmony patches pinned to one beta overload (the name now has several).
            public readonly List<string> PinnedPatches = new List<string>();
            // Game members the beta lacks that a mod keeps working without: removed fields keep
            // per-object values, removed methods do nothing and return the default.
            public readonly SortedSet<string> Degraded = new SortedSet<string>(StringComparer.Ordinal);
        }

        sealed class Resolver : DefaultAssemblyResolver
        {
            readonly string gamePath;
            AssemblyDefinition game;
            public Resolver(string gamePath) { this.gamePath = gamePath; }
            public override AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                if (name.Name == TldLoaderCompatPolicy.GameAssembly)
                {
                    if (game == null)
                        game = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(gamePath)), new ReaderParameters { AssemblyResolver = this });
                    return game;
                }
                return base.Resolve(name);
            }
        }

        readonly Resolver resolver;
        readonly string runtimeName;
        readonly Version runtimeVersion;

        // gameAssembly: the game's Assembly-CSharp to check names against.
        // searchDirectories: where other references (0Harmony, Unity, TLDLoader) live.
        public TldLoaderTranslator(string gameAssembly, IEnumerable<string> searchDirectories, string runtimeAssemblyName, Version runtimeAssemblyVersion)
        {
            resolver = new Resolver(gameAssembly);
            foreach (var dir in resolver.GetSearchDirectories()) resolver.RemoveSearchDirectory(dir);
            foreach (var dir in searchDirectories) if (Directory.Exists(dir)) resolver.AddSearchDirectory(dir);
            runtimeName = runtimeAssemblyName;
            runtimeVersion = runtimeAssemblyVersion ?? new Version(0, 0, 0, 0);
        }

        public Report TranslateLoader(byte[] source) { return Translate(source, true); }
        public Report TranslateMod(byte[] source) { return Translate(source, false); }

        Report Translate(byte[] source, bool loader)
        {
            var report = new Report();
            translatingLoader = loader;
            var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(source), new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate });
            var module = assembly.MainModule;
            bool usesGame = module.AssemblyReferences.Any(r => r.Name == TldLoaderCompatPolicy.GameAssembly);
            TypeReference bridge = null;
            Func<TypeReference> bridgeType = delegate
            {
                if (bridge != null) return bridge;
                var scope = module.AssemblyReferences.FirstOrDefault(r => r.Name == runtimeName);
                if (scope == null) { scope = new AssemblyNameReference(runtimeName, runtimeVersion); module.AssemblyReferences.Add(scope); }
                return bridge = new TypeReference(TldLoaderCompatPolicy.BridgeNamespace, TldLoaderCompatPolicy.BridgeType, module, scope);
            };

            var types = module.GetTypes().ToList();
            var missingByType = new Dictionary<TypeDefinition, bool>();
            if (usesGame)
            {
                foreach (var type in types)
                    foreach (var method in type.Methods)
                        if (method.HasBody) report.Rewrites += RewriteBody(module, method, bridgeType);
                // After the bodies: their lookups use the public game's type names.
                // Snapshot the original names before rewriting any parent. A nested
                // reference's name and scope are derived from its declaring type.
                var aliases = module.GetTypeReferences().Where(r => IsGame(ScopeOf(r)))
                    .Select(r => new { Reference = r, Target = TldLoaderCompatPolicy.RenamedType(r.FullName) })
                    .Where(a => a.Target != null).ToArray();
                foreach (var alias in aliases)
                {
                    var reference = alias.Reference;
                    string renamed = alias.Target;
                    if (TldLoaderCompatPolicy.RenamedToRuntime(renamed))
                    {
                        // A stand-in for a deleted stable type: top level in Roadworthy's assembly.
                        string target = TldLoaderCompatPolicy.WithoutAssembly(renamed);
                        int last = target.LastIndexOf('.');
                        reference.DeclaringType = null;
                        reference.Namespace = target.Substring(0, last);
                        reference.Name = target.Substring(last + 1);
                        reference.Scope = bridgeType().Scope;
                        report.Rewrites++;
                        continue;
                    }
                    int dot = renamed.LastIndexOf('.');
                    if (dot > 0)
                    {
                        reference.Namespace = renamed.Substring(0, dot);
                        // Core-library targets stand in for removed classes; game targets keep their
                        // scope and kind (the coordinate structs map to beta structs).
                        if (TldLoaderCompatPolicy.RenamedToCoreLibrary(renamed)) { reference.Scope = module.TypeSystem.CoreLibrary; reference.IsValueType = false; }
                    }
                    else if (renamed != reference.Name) reference.Namespace = "";
                    reference.Name = dot > 0 ? renamed.Substring(dot + 1) : renamed;
                    report.Rewrites++;
                    if (renamed == "System.Object") objectAliases.Add(reference);
                }
                // A type renamed to System.Object is still a class reference in signatures, which
                // Mono does not match against the built-in object type: TldLoaderBridge.SavedIds
                // returning Dictionary<int, object> was "Method not found" in game.
                if (objectAliases.Count > 0) CanonicalObject(module, types);
                objectAliases.Clear();
                if (!loader) Degrade(module, types, bridgeType, report);
            }
            if (loader) PatchLoader(module, bridgeType, report);
            else
                foreach (var type in types)
                    PortHarmonyPatch(module, type, bridgeType, report);

            // Harmony classes whose target or body is gone are left out of PatchAll.
            foreach (var type in types)
                foreach (var method in type.Methods)
                    if (method.HasBody) CollectMissing(method, report, type, missingByType);
            foreach (var type in types)
            {
                if (type.DeclaringType != null && HasHarmonyPatch(type.DeclaringType)) continue;
                if (!HasHarmonyPatch(type)) continue;
                var target = PatchTarget(type);
                if (target == TldLoaderCompatPolicy.TargetStatus.Missing && RetireLegacyPhysicsPausePatch(type))
                {
                    StripHarmony(type);
                    report.ObsoletePatches.Add(type.FullName + " (beta pauses via Time.timeScale)");
                    continue;
                }
                bool bodyMissing = false;
                foreach (var pair in missingByType) if (pair.Value && (pair.Key == type || IsNestedIn(pair.Key, type))) bodyMissing = true;
                if (TldLoaderCompatPolicy.StripPatch(target, bodyMissing, loader && TldLoaderCompatPolicy.IsDroppedLoaderPatch(type.Name)))
                {
                    StripHarmony(type);
                    report.StrippedPatches.Add(type.FullName + (target == TldLoaderCompatPolicy.TargetStatus.Missing ? " (target missing)" : bodyMissing ? " (uses missing game code)" : " (known incompatible)"));
                    continue;
                }
                if (target != TldLoaderCompatPolicy.TargetStatus.Exists) continue;
                string unfit = FitPatch(module, type, report);
                if (unfit != null)
                {
                    StripHarmony(type);
                    report.StrippedPatches.Add(type.FullName + " (" + unfit + ")");
                }
            }

            var output = new MemoryStream();
            assembly.Write(output);
            report.Bytes = output.ToArray();
            return report;
        }

        static bool IsNestedIn(TypeDefinition type, TypeDefinition outer)
        {
            for (var t = type.DeclaringType; t != null; t = t.DeclaringType) if (t == outer) return true;
            return false;
        }

        static bool IsGame(IMetadataScope scope)
        {
            return scope != null && scope.Name == TldLoaderCompatPolicy.GameAssembly;
        }

        int RewriteBody(ModuleDefinition module, MethodDefinition method, Func<TypeReference> bridgeType)
        {
            int count = 0;
            var il = method.Body.GetILProcessor();
            foreach (var instruction in method.Body.Instructions.ToArray())
            {
                // gameObject.AddComponent<partscript/partslotscript>(): a part or slot built at run time
                // needs the beta's extra set-up before the player can mount anything on it.
                var added = instruction.Operand as GenericInstanceMethod;
                if (added != null && added.Name == "AddComponent" && added.GenericArguments.Count == 1 && added.DeclaringType.FullName == "UnityEngine.GameObject" &&
                    (instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Call) && IsGame(added.GenericArguments[0].Scope))
                {
                    string helperName = TldLoaderCompatPolicy.AddComponentHelper(added.GenericArguments[0].FullName);
                    if (helperName != null)
                    {
                        var helper = new MethodReference(helperName, added.GenericArguments[0], bridgeType()) { HasThis = false };
                        helper.Parameters.Add(new ParameterDefinition(added.DeclaringType));
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = helper;
                        count++;
                        continue;
                    }
                }
                var field = instruction.Operand as FieldReference;
                if (field != null && IsGame(field.DeclaringType.Scope))
                {
                    var code = instruction.OpCode.Code;
                    bool isStatic = code == Code.Ldsfld || code == Code.Stsfld || code == Code.Ldsflda;
                    bool isLoad = code == Code.Ldsfld || code == Code.Ldfld;
                    bool isStore = code == Code.Stsfld || code == Code.Stfld;
                    // Address-of (a method call on a value-type field): the helper's value in a temporary.
                    bool isAddress = code == Code.Ldflda || code == Code.Ldsflda;
                    var rule = TldLoaderCompatPolicy.ForField(field.DeclaringType.FullName, field.Name);
                    if (isAddress && rule.Kind == TldLoaderCompatPolicy.RewriteKind.CallHelper && TldLoaderCompatPolicy.CanApply(rule, true, isStatic))
                    {
                        var getter = new MethodReference(HelperFor(rule.Name), field.FieldType, bridgeType()) { HasThis = false };
                        if (!rule.Static) getter.Parameters.Add(new ParameterDefinition(field.DeclaringType));
                        var copy = new VariableDefinition(field.FieldType);
                        method.Body.Variables.Add(copy);
                        Splice(il, instruction, new List<Instruction> { il.Create(OpCodes.Call, getter), il.Create(OpCodes.Stloc, copy), il.Create(OpCodes.Ldloca, copy) });
                        count++;
                        continue;
                    }
                    if (!TldLoaderCompatPolicy.CanApply(rule, isLoad, isStatic, isStore))
                    {
                        // No rule: a field whose type only changed signedness is retyped.
                        var retyped = Retype(module, field, code, isStatic);
                        if (retyped != null) { instruction.Operand = retyped; count++; }
                        else if (!isStatic && (code == Code.Ldfld || code == Code.Stfld) && ConvertCollection(module, il, instruction, field, bridgeType)) count++;
                        continue;
                    }
                    if (rule.Kind == TldLoaderCompatPolicy.RewriteKind.RenameField)
                    {
                        instruction.Operand = new FieldReference(rule.Name, field.FieldType, field.DeclaringType);
                    }
                    else if (isStore)
                    {
                        var setter = new MethodReference(rule.SetName, module.TypeSystem.Void, bridgeType()) { HasThis = false };
                        if (!rule.Static) setter.Parameters.Add(new ParameterDefinition(field.DeclaringType));
                        setter.Parameters.Add(new ParameterDefinition(field.FieldType));
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = setter;
                    }
                    else
                    {
                        var helper = new MethodReference(HelperFor(rule.Name), field.FieldType, bridgeType()) { HasThis = false };
                        if (!rule.Static) helper.Parameters.Add(new ParameterDefinition(field.DeclaringType));
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = helper;
                    }
                    count++;
                    continue;
                }
                var called = instruction.Operand as MethodReference;
                if (called != null && IsGame(called.DeclaringType.Scope) &&
                    called.FullName == "System.Void tosaveitemscript::Claim(System.Boolean)" &&
                    (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                    instruction.Previous != null && instruction.Previous.OpCode == OpCodes.Ldc_I4_0 &&
                    !method.Body.Instructions.Any(i => i.Operand == instruction ||
                        (i.Operand is Instruction[] && ((Instruction[])i.Operand).Contains(instruction))))
                {
                    // The beta releases item authority with the end of native interaction.
                    // A literal release can flush the final pose. An arbitrary claim has no
                    // equivalent in beta's native ownership model.
                    var release = new MethodReference("ReleaseLegacyItem", module.TypeSystem.Void, bridgeType()) { HasThis = false };
                    release.Parameters.Add(new ParameterDefinition(called.DeclaringType));
                    release.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = release;
                    count++;
                    continue;
                }
                if (called != null && instruction.OpCode == OpCodes.Call && !called.HasThis &&
                    IsGame(called.DeclaringType.Scope) &&
                    called.FullName == "System.IO.MemoryStream NAudioPlayer::AudioMemStream(NAudio.Wave.WaveStream)")
                {
                    // A mod that supplies NAudio can pass its WaveStream without
                    // making every Roadworthy install depend on that assembly.
                    var optionalCodecCall = new MethodReference(called.Name, called.ReturnType, called.DeclaringType) { HasThis = false };
                    optionalCodecCall.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                    instruction.Operand = optionalCodecCall;
                    count++;
                    continue;
                }
                bool constructs = instruction.OpCode == OpCodes.Newobj;
                if (called != null && called.HasThis && !(called is GenericInstanceMethod) && IsGame(called.DeclaringType.Scope) &&
                    (constructs || instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt))
                {
                    string helperName = TldLoaderCompatPolicy.MethodHelper(called.DeclaringType.FullName, called.Name,
                        called.Parameters.Select(p => p.ParameterType.FullName).ToArray());
                    if (helperName == null && !constructs)
                    {
                        var retyped = Retype(module, called);
                        if (retyped != null) { instruction.Operand = retyped; count++; continue; }
                    }
                    if (helperName != null && (constructs || called.Name != ".ctor"))
                    {
                        var helper = new MethodReference(helperName, constructs ? called.DeclaringType : called.ReturnType, bridgeType()) { HasThis = false };
                        if (!constructs) helper.Parameters.Add(new ParameterDefinition(called.DeclaringType));
                        foreach (var parameter in called.Parameters) helper.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = helper;
                        count++;
                        continue;
                    }
                }
                if (called != null && !(called is GenericInstanceMethod) && IsGame(called.DeclaringType.Scope) &&
                    (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                    TldLoaderCompatPolicy.MethodHelper(called.DeclaringType.FullName, called.Name, called.Parameters.Select(p => p.ParameterType.FullName).ToArray()) == null &&
                    Adapt(module, method, instruction, called)) { count++; continue; }
                if (called != null && called.Name == "get_name" && called.HasThis && called.DeclaringType.FullName == "UnityEngine.SceneManagement.Scene")
                {
                    // Scene.name takes the scene's address; a static method with a ref
                    // Scene parameter has the same stack shape.
                    var helper = new MethodReference("SceneName", module.TypeSystem.String, bridgeType()) { HasThis = false };
                    helper.Parameters.Add(new ParameterDefinition(new ByReferenceType(called.DeclaringType)));
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = helper;
                    count++;
                }
            }
            return count;
        }

        readonly HashSet<TypeReference> objectAliases = new HashSet<TypeReference>();

        bool translatingLoader;

        // Mods read mainscript.M while the world is still loading (DbLoad); TLDLoader itself
        // treats a non-null M as "the world is ready".
        string HelperFor(string name) { return !translatingLoader && name == "MainScript" ? "ModMainScript" : name; }

        // Replaces the renamed System.Object references with the built-in object type in every
        // signature the module uses: member references, definitions, locals and type operands.
        void CanonicalObject(ModuleDefinition module, List<TypeDefinition> types)
        {
            var seen = new HashSet<MemberReference>();
            foreach (var type in types)
            {
                if (type.BaseType != null) type.BaseType = Canon(module, type.BaseType);
                foreach (var face in type.Interfaces) face.InterfaceType = Canon(module, face.InterfaceType);
                foreach (var field in type.Fields) field.FieldType = Canon(module, field.FieldType);
                foreach (var property in type.Properties) property.PropertyType = Canon(module, property.PropertyType);
                foreach (var evt in type.Events) evt.EventType = Canon(module, evt.EventType);
                foreach (var method in type.Methods)
                {
                    CanonMethod(module, method, seen);
                    if (!method.HasBody) continue;
                    foreach (var variable in method.Body.Variables) variable.VariableType = Canon(module, variable.VariableType);
                    foreach (var instruction in method.Body.Instructions)
                    {
                        var operandType = instruction.Operand as TypeReference;
                        if (operandType != null) { instruction.Operand = Canon(module, operandType); continue; }
                        var operandMethod = instruction.Operand as MethodReference;
                        if (operandMethod != null) { CanonMethod(module, operandMethod, seen); continue; }
                        var operandField = instruction.Operand as FieldReference;
                        if (operandField != null && !(operandField is FieldDefinition) && seen.Add(operandField))
                        {
                            operandField.FieldType = Canon(module, operandField.FieldType);
                            operandField.DeclaringType = Canon(module, operandField.DeclaringType);
                        }
                    }
                }
            }
        }

        void CanonMethod(ModuleDefinition module, MethodReference method, HashSet<MemberReference> seen)
        {
            if (!seen.Add(method)) return;
            var generic = method as GenericInstanceMethod;
            if (generic != null)
            {
                for (int i = 0; i < generic.GenericArguments.Count; i++) generic.GenericArguments[i] = Canon(module, generic.GenericArguments[i]);
                CanonMethod(module, generic.ElementMethod, seen);
                return;
            }
            method.ReturnType = Canon(module, method.ReturnType);
            foreach (var parameter in method.Parameters) parameter.ParameterType = Canon(module, parameter.ParameterType);
            if (!(method is MethodDefinition)) method.DeclaringType = Canon(module, method.DeclaringType);
        }

        TypeReference Canon(ModuleDefinition module, TypeReference type)
        {
            if (type == null) return null;
            if (objectAliases.Contains(type)) return module.TypeSystem.Object;
            var instance = type as GenericInstanceType;
            if (instance != null)
            {
                for (int i = 0; i < instance.GenericArguments.Count; i++) instance.GenericArguments[i] = Canon(module, instance.GenericArguments[i]);
                var element = Canon(module, instance.ElementType);
                if (element == instance.ElementType) return instance;
                var copy = new GenericInstanceType(element);
                foreach (var argument in instance.GenericArguments) copy.GenericArguments.Add(argument);
                return copy;
            }
            var array = type as ArrayType;
            if (array != null) { var element = Canon(module, array.ElementType); return element == array.ElementType ? type : new ArrayType(element, array.Rank); }
            var byRef = type as ByReferenceType;
            if (byRef != null) { var element = Canon(module, byRef.ElementType); return element == byRef.ElementType ? type : new ByReferenceType(element); }
            var pointer = type as PointerType;
            if (pointer != null) { var element = Canon(module, pointer.ElementType); return element == pointer.ElementType ? type : new PointerType(element); }
            return type;
        }

        static TypeReference Primitive(ModuleDefinition module, string fullName, TypeReference fallback)
        {
            var system = module.TypeSystem;
            switch (fullName)
            {
                case "System.Int32": return system.Int32;
                case "System.UInt32": return system.UInt32;
                case "System.Int64": return system.Int64;
                case "System.UInt64": return system.UInt64;
                case "System.Int16": return system.Int16;
                case "System.UInt16": return system.UInt16;
                case "System.Byte": return system.Byte;
                case "System.SByte": return system.SByte;
            }
            return fallback;
        }

        // A unique casing match can also be used when the CLR field type
        // stays the same. Preserve the existing signedness retyping.
        FieldReference Retype(ModuleDefinition module, FieldReference field, Code code, bool isStatic)
        {
            if (Exists(field)) return null;
            TypeDefinition type;
            try { type = field.DeclaringType.Resolve(); } catch (Exception) { return null; }
            if (type == null) return null;
            var beta = BetaField(type, field.Name);
            if (beta == null || beta.IsStatic != isStatic) return null;
            if ((code == Code.Stfld || code == Code.Stsfld) && beta.IsInitOnly) return null;
            if (!TldLoaderCompatPolicy.SameStackType(field.FieldType.FullName, beta.FieldType.FullName)) return null;
            return new FieldReference(beta.Name, Primitive(module, beta.FieldType.FullName, field.FieldType), field.DeclaringType);
        }

        static FieldDefinition BetaField(TypeDefinition type, string name)
        {
            var exact = type.Fields.FirstOrDefault(f => f.Name == name);
            if (exact != null) return exact;
            var matches = type.Fields.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        static TypeReference ArrayElement(TypeReference type) { var array = type as ArrayType; return array != null && array.Rank == 1 ? array.ElementType : null; }
        static TypeReference ListElement(TypeReference type)
        {
            var generic = type as GenericInstanceType;
            return generic != null && generic.ElementType.FullName == "System.Collections.Generic.List`1" && generic.GenericArguments.Count == 1 ? generic.GenericArguments[0] : null;
        }

        // A field that is T[] in the mod and List<T> on the beta (or the reverse). The
        // load/store becomes ldstr "<beta name>" plus a generic bridge call that converts.
        // The original instruction is reused for the ldstr so branch targets stay valid.
        bool ConvertCollection(ModuleDefinition module, ILProcessor il, Instruction instruction, FieldReference field, Func<TypeReference> bridgeType)
        {
            if (Exists(field)) return false;
            TypeDefinition type;
            try { type = field.DeclaringType.Resolve(); } catch (Exception) { return false; }
            if (type == null) return false;
            var beta = BetaField(type, field.Name);
            if (beta == null || beta.IsStatic || (instruction.OpCode.Code == Code.Stfld && beta.IsInitOnly)) return false;
            var modArray = ArrayElement(field.FieldType); var modList = ListElement(field.FieldType);
            var gameArray = ArrayElement(beta.FieldType); var gameList = ListElement(beta.FieldType);
            var shape = TldLoaderCompatPolicy.Shape(modArray == null ? null : modArray.FullName, modList == null ? null : modList.FullName,
                gameArray == null ? null : gameArray.FullName, gameList == null ? null : gameList.FullName);
            if (shape == TldLoaderCompatPolicy.CollectionShape.None) return false;
            bool load = instruction.OpCode.Code == Code.Ldfld;
            string name = shape == TldLoaderCompatPolicy.CollectionShape.ArrayInModListInGame
                ? (load ? "ListFieldAsArray" : "SetListFieldFromArray") : (load ? "ArrayFieldAsList" : "SetArrayFieldFromList");
            var helper = new MethodReference(name, module.TypeSystem.Void, bridgeType()) { HasThis = false };
            var t = new GenericParameter("T", helper);
            helper.GenericParameters.Add(t);
            TypeReference collection;
            if (shape == TldLoaderCompatPolicy.CollectionShape.ArrayInModListInGame) collection = new ArrayType(t);
            else { var list = new GenericInstanceType(((GenericInstanceType)field.FieldType).ElementType); list.GenericArguments.Add(t); collection = list; }
            helper.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            if (load) helper.ReturnType = collection;
            else helper.Parameters.Add(new ParameterDefinition(collection));
            helper.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            var call = new GenericInstanceMethod(helper);
            call.GenericArguments.Add(modArray ?? modList);
            instruction.OpCode = OpCodes.Ldstr;
            instruction.Operand = beta.Name;
            il.InsertAfter(instruction, il.Create(OpCodes.Call, call));
            return true;
        }

        // A game method the beta changed in a way the call site can absorb: the beta added
        // trailing arguments (each gets its declared default, else the type's zero value) and/or
        // now returns a value the public game's void method did not (it is dropped). The call
        // instruction keeps its place as the first new instruction, so branches into it still
        // land before the added arguments.
        bool Adapt(ModuleDefinition module, MethodDefinition caller, Instruction instruction, MethodReference called)
        {
            if (called.HasGenericParameters || Exists(called)) return false;
            TypeDefinition type;
            try { type = called.DeclaringType.Resolve(); } catch (Exception) { return false; }
            if (type == null) return false;
            var stableParameters = called.Parameters.Select(p => Renamed(p.ParameterType.FullName)).ToArray();
            MethodDefinition best = null;
            int[] bestMap = null;
            foreach (var beta in type.Methods)
            {
                if (beta.Name != called.Name || beta.HasThis != called.HasThis || beta.HasGenericParameters || beta.Parameters.Count < stableParameters.Length) continue;
                var map = TldLoaderCompatPolicy.AlignParameters(stableParameters, beta.Parameters.Select(p => p.ParameterType.FullName).ToArray());
                if (map == null) continue;
                if (!TldLoaderCompatPolicy.ReturnAdaptable(Renamed(called.ReturnType.FullName), beta.ReturnType.FullName)) continue;
                bool fillable = true;
                for (int k = 0; k < map.Length; k++)
                    if (map[k] < 0 && (beta.Parameters[k].ParameterType.IsByReference ||
                        beta.Parameters[k].ParameterType.IsPointer ||
                        (stableParameters.Length == 0 && !beta.Parameters[k].HasConstant)))
                        fillable = false;
                if (!fillable) continue;
                if (best == null || beta.Parameters.Count < best.Parameters.Count) { best = beta; bestMap = map; }
            }
            if (best == null || (best.Parameters.Count == stableParameters.Length && best.ReturnType.FullName == called.ReturnType.FullName)) return false;
            var il = caller.Body.GetILProcessor();
            var sequence = new List<Instruction>();
            bool trailing = true;
            for (int k = 0; k < stableParameters.Length; k++) if (bestMap[k] != k) trailing = false;
            if (trailing)
                for (int i = stableParameters.Length; i < best.Parameters.Count; i++) sequence.AddRange(DefaultValue(module, caller, best.Parameters[i]));
            else
            {
                // A parameter was added between the call's arguments: park the arguments in locals
                // and push them back in the beta's order with defaults in the gaps.
                var parked = new VariableDefinition[stableParameters.Length];
                for (int i = stableParameters.Length - 1; i >= 0; i--)
                {
                    parked[i] = new VariableDefinition(module.ImportReference(called.Parameters[i].ParameterType));
                    caller.Body.Variables.Add(parked[i]);
                    sequence.Add(il.Create(OpCodes.Stloc, parked[i]));
                }
                for (int k = 0; k < best.Parameters.Count; k++)
                    if (bestMap[k] >= 0) sequence.Add(il.Create(OpCodes.Ldloc, parked[bestMap[k]]));
                    else sequence.AddRange(DefaultValue(module, caller, best.Parameters[k]));
            }
            var call = il.Create(instruction.OpCode, module.ImportReference(best));
            sequence.Add(call);
            if (called.ReturnType.FullName == "System.Void" && best.ReturnType.FullName != "System.Void") sequence.Add(il.Create(OpCodes.Pop));
            instruction.OpCode = sequence[0].OpCode;
            instruction.Operand = sequence[0].Operand;
            var previous = instruction;
            for (int i = 1; i < sequence.Count; i++) { il.InsertAfter(previous, sequence[i]); previous = sequence[i]; }
            return true;
        }

        static string Renamed(string fullName)
        {
            string renamed = TldLoaderCompatPolicy.RenamedType(fullName);
            return renamed == null ? fullName : TldLoaderCompatPolicy.WithoutAssembly(renamed);
        }

        // Last resort for mods, after every rule: a game field the beta lacks keeps a value per
        // object in TldLoaderBridge (reads see the last write, else the default) and a game method
        // the beta lacks becomes a stub in the mod that logs once and returns the default. Without
        // this the whole mod method that names the member fails every time it runs, and TLDLoader
        // disables the mod after repeated errors. Address-of fields, constructors, generic
        // methods and members whose types are gone stay as they are (reported as missing).
        void Degrade(ModuleDefinition module, List<TypeDefinition> types, Func<TypeReference> bridgeType, Report report)
        {
            TypeDefinition stubs = null;
            var stubCache = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);
            foreach (var type in types.ToList())
                foreach (var method in type.Methods.Where(m => m.HasBody).ToList())
                {
                    var il = method.Body.GetILProcessor();
                    foreach (var instruction in method.Body.Instructions.ToArray())
                    {
                        var field = instruction.Operand as FieldReference;
                        if (field != null && IsGame(field.DeclaringType.Scope) && !Exists(field) && Usable(field.FieldType) && !IsValueTypeOwner(field))
                        {
                            string key = field.DeclaringType.FullName + "::" + field.Name;
                            var code = instruction.OpCode.Code;
                            bool address = code == Code.Ldflda || code == Code.Ldsflda;
                            string helper = code == Code.Ldfld || code == Code.Ldsfld || address ? "ShadowGet" : code == Code.Stfld ? "ShadowSet" : code == Code.Stsfld ? "ShadowSetStatic" : null;
                            if (helper == null) continue;
                            var element = new MethodReference(helper, module.TypeSystem.Void, bridgeType()) { HasThis = false };
                            var generic = new GenericParameter("T", element);
                            element.GenericParameters.Add(generic);
                            if (helper == "ShadowGet") { element.ReturnType = generic; element.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object)); }
                            else if (helper == "ShadowSet") { element.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object)); element.Parameters.Add(new ParameterDefinition(generic)); }
                            else element.Parameters.Add(new ParameterDefinition(generic));
                            element.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                            var call = new GenericInstanceMethod(element);
                            call.GenericArguments.Add(field.FieldType);
                            // The instruction stays first (branch targets): [ldnull] ldstr key; call.
                            var sequence = new List<Instruction>();
                            if (code == Code.Ldsfld || code == Code.Ldsflda) sequence.Add(il.Create(OpCodes.Ldnull));
                            sequence.Add(il.Create(OpCodes.Ldstr, key));
                            sequence.Add(il.Create(OpCodes.Call, call));
                            if (address)
                            {
                                // A copy: reads and method calls work; writes through the address are lost.
                                var copy = new VariableDefinition(field.FieldType);
                                method.Body.Variables.Add(copy);
                                sequence.Add(il.Create(OpCodes.Stloc, copy));
                                sequence.Add(il.Create(OpCodes.Ldloca, copy));
                            }
                            Splice(il, instruction, sequence);
                            report.Degraded.Add(key);
                            continue;
                        }
                        var called = instruction.Operand as MethodReference;
                        if (called == null || called is GenericInstanceMethod || called.HasGenericParameters || called.Name == ".ctor" || called.Name == ".cctor" ||
                            !IsGame(called.DeclaringType.Scope) || Exists(called) || (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)) continue;
                        if (!Usable(called.ReturnType) || called.Parameters.Any(p => !Usable(p.ParameterType)) || (called.HasThis && !Usable(called.DeclaringType)) ||
                            (called.HasThis && called.DeclaringType.IsValueType)) continue;
                        string stubKey = called.DeclaringType.FullName + "::" + called.Name + "(" + string.Join(",", called.Parameters.Select(p => p.ParameterType.FullName).ToArray()) + ")";
                        MethodDefinition stub;
                        if (!stubCache.TryGetValue(stubKey, out stub))
                        {
                            if (stubs == null)
                            {
                                stubs = new TypeDefinition("", "RoadworthyMissingGameMembers", TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract | TypeAttributes.Class, module.TypeSystem.Object);
                                module.Types.Add(stubs);
                            }
                            stub = new MethodDefinition("Stub" + stubCache.Count, MethodAttributes.Static | MethodAttributes.Assembly | MethodAttributes.HideBySig, called.ReturnType);
                            if (called.HasThis) stub.Parameters.Add(new ParameterDefinition("self", ParameterAttributes.None, called.DeclaringType));
                            foreach (var parameter in called.Parameters) stub.Parameters.Add(new ParameterDefinition(parameter.Name, ParameterAttributes.None, parameter.ParameterType));
                            var body = stub.Body.GetILProcessor();
                            var missing = new MethodReference("MissingCall", module.TypeSystem.Void, bridgeType()) { HasThis = false };
                            missing.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                            body.Append(body.Create(OpCodes.Ldstr, stubKey));
                            body.Append(body.Create(OpCodes.Call, missing));
                            if (called.ReturnType.FullName != "System.Void")
                            {
                                var result = new VariableDefinition(called.ReturnType);
                                stub.Body.Variables.Add(result);
                                stub.Body.InitLocals = true;
                                body.Append(body.Create(OpCodes.Ldloc, result));
                            }
                            body.Append(body.Create(OpCodes.Ret));
                            stubs.Methods.Add(stub);
                            stubCache[stubKey] = stub;
                        }
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = stub;
                        report.Degraded.Add(stubKey);
                    }
                }
        }

        // A type a stand-in can name: not a game type the beta lacks.
        bool Usable(TypeReference type)
        {
            var element = type;
            while (element is TypeSpecification && !(element is GenericInstanceType)) element = ((TypeSpecification)element).ElementType;
            var instance = element as GenericInstanceType;
            if (instance != null) return Usable(instance.ElementType) && instance.GenericArguments.All(Usable);
            if (element is GenericParameter) return false;
            return !IsGame(ScopeOf(element)) || Exists(element);
        }

        static bool IsValueTypeOwner(FieldReference field) { return field.DeclaringType.IsValueType; }

        // Replaces an instruction by a sequence, keeping the instruction object first.
        static void Splice(ILProcessor il, Instruction instruction, List<Instruction> sequence)
        {
            instruction.OpCode = sequence[0].OpCode;
            instruction.Operand = sequence[0].Operand;
            var previous = instruction;
            for (int i = 1; i < sequence.Count; i++) { il.InsertAfter(previous, sequence[i]); previous = sequence[i]; }
        }

        // Loads a parameter's declared default, else its type's zero value.
        static IEnumerable<Instruction> DefaultValue(ModuleDefinition module, MethodDefinition caller, ParameterDefinition parameter)
        {
            var type = parameter.ParameterType;
            object constant = parameter.HasDefault && parameter.HasConstant ? parameter.Constant : null;
            switch (type.FullName)
            {
                case "System.Boolean": case "System.Int32": case "System.UInt32": case "System.Int16": case "System.UInt16": case "System.Byte": case "System.SByte": case "System.Char":
                    yield return Instruction.Create(OpCodes.Ldc_I4, constant == null ? 0 : Convert.ToInt32(constant is bool ? ((bool)constant ? 1 : 0) : constant));
                    yield break;
                case "System.Int64": case "System.UInt64":
                    yield return Instruction.Create(OpCodes.Ldc_I8, constant == null ? 0L : Convert.ToInt64(constant));
                    yield break;
                case "System.Single":
                    yield return Instruction.Create(OpCodes.Ldc_R4, constant == null ? 0f : Convert.ToSingle(constant));
                    yield break;
                case "System.Double":
                    yield return Instruction.Create(OpCodes.Ldc_R8, constant == null ? 0d : Convert.ToDouble(constant));
                    yield break;
                case "System.String":
                    yield return constant is string ? Instruction.Create(OpCodes.Ldstr, (string)constant) : Instruction.Create(OpCodes.Ldnull);
                    yield break;
            }
            TypeDefinition definition = null;
            try { definition = type.Resolve(); } catch (Exception) { }
            if (definition != null && definition.IsEnum)
            {
                yield return Instruction.Create(OpCodes.Ldc_I4, constant == null ? 0 : Convert.ToInt32(constant));
                yield break;
            }
            if (!type.IsValueType && (definition == null || !definition.IsValueType))
            {
                yield return Instruction.Create(OpCodes.Ldnull);
                yield break;
            }
            // A struct: a zeroed local.
            var imported = module.ImportReference(type);
            var local = new VariableDefinition(imported);
            caller.Body.Variables.Add(local);
            caller.Body.InitLocals = true;
            yield return Instruction.Create(OpCodes.Ldloca, local);
            yield return Instruction.Create(OpCodes.Initobj, imported);
            yield return Instruction.Create(OpCodes.Ldloc, local);
        }

        // An unresolved method with a beta overload that differs only in signedness.
        MethodReference Retype(ModuleDefinition module, MethodReference method)
        {
            if (method is GenericInstanceMethod || method.HasGenericParameters || Exists(method)) return null;
            TypeDefinition type;
            try { type = method.DeclaringType.Resolve(); } catch (Exception) { return null; }
            if (type == null) return null;
            foreach (var beta in type.Methods)
            {
                if (beta.Name != method.Name || beta.Parameters.Count != method.Parameters.Count || beta.HasThis != method.HasThis || beta.HasGenericParameters) continue;
                if (!TldLoaderCompatPolicy.SameStackType(method.ReturnType.FullName, beta.ReturnType.FullName)) continue;
                bool fits = true;
                for (int i = 0; i < beta.Parameters.Count && fits; i++)
                    fits = TldLoaderCompatPolicy.SameStackType(method.Parameters[i].ParameterType.FullName, beta.Parameters[i].ParameterType.FullName);
                if (!fits) continue;
                var result = new MethodReference(method.Name, Primitive(module, beta.ReturnType.FullName, method.ReturnType), method.DeclaringType) { HasThis = method.HasThis, ExplicitThis = method.ExplicitThis, CallingConvention = method.CallingConvention };
                for (int i = 0; i < beta.Parameters.Count; i++)
                    result.Parameters.Add(new ParameterDefinition(Primitive(module, beta.Parameters[i].ParameterType.FullName, method.Parameters[i].ParameterType)));
                return result;
            }
            return null;
        }

        void PatchLoader(ModuleDefinition module, Func<TypeReference> bridgeType, Report report)
        {
            var loader = module.GetType("TLDLoader.ModLoader");
            if (loader == null) throw new InvalidDataException("TLDLoader.ModLoader is missing.");
            RedirectLoaderPaths(module, bridgeType);
            // Roadworthy reports menu/world changes itself; a scene reload must not
            // look like "reload the same save" (that branch loads SceneMenu).
            var init = loader.Methods.FirstOrDefault(m => m.Name == "InitMainMenu" && m.HasBody);
            if (init != null)
                foreach (var instruction in init.Body.Instructions)
                {
                    var called = instruction.Operand as MethodReference;
                    if (called != null && called.Name == "add_activeSceneChanged" && called.DeclaringType.Name == "SceneManager")
                    { instruction.OpCode = OpCodes.Pop; instruction.Operand = null; report.SceneHookRemoved = true; }
                }
            // Its own duplicate check looks for the wrong object name.
            var changed = loader.Methods.FirstOrDefault(m => m.Name == "SceneChanged" && m.HasBody);
            if (changed != null)
                foreach (var instruction in changed.Body.Instructions)
                    if (instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == "MSCLoader Info") instruction.Operand = "TLDLoader Info";
            // Its menu panel comes from Mods\Assets\TLDLoader_Core\core.unity3d. When that file is
            // missing the panel's Animator is null, and SceneChanged/StartLoadingMods stop halfway.
            MethodReference animatorHelper = null, setActiveHelper = null;
            foreach (var method in module.GetTypes().Where(t => t.Namespace != null && t.Namespace.StartsWith("TLDLoader")).SelectMany(t => t.Methods).Where(m => m.HasBody))
                foreach (var instruction in method.Body.Instructions)
                {
                    var called = instruction.Operand as MethodReference;
                    if (instruction.OpCode == OpCodes.Callvirt && called != null && called.Name == "SetActive" && called.DeclaringType.FullName == "UnityEngine.GameObject" && called.Parameters.Count == 1)
                    {
                        // Its menu toggles also show/hide panels from core.unity3d (null without it).
                        if (setActiveHelper == null)
                        {
                            setActiveHelper = new MethodReference("GameObjectSetActive", module.TypeSystem.Void, bridgeType()) { HasThis = false };
                            setActiveHelper.Parameters.Add(new ParameterDefinition(called.DeclaringType));
                            setActiveHelper.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
                        }
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = setActiveHelper;
                        report.Rewrites++;
                        continue;
                    }
                    if (instruction.OpCode != OpCodes.Callvirt || called == null || called.Name != "SetBool" || called.DeclaringType.FullName != "UnityEngine.Animator" ||
                        called.Parameters.Count != 2 || called.Parameters[0].ParameterType.FullName != "System.String") continue;
                    if (animatorHelper == null)
                    {
                        animatorHelper = new MethodReference("AnimatorSetBool", module.TypeSystem.Void, bridgeType()) { HasThis = false };
                        animatorHelper.Parameters.Add(new ParameterDefinition(called.DeclaringType));
                        animatorHelper.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                        animatorHelper.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
                    }
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = animatorHelper;
                    report.Rewrites++;
                }
            // The item framework's beta-native parts live in TldLoaderBridge.
            var modType = module.GetType("TLDLoader.Mod");
            var getId = modType == null ? null : modType.Methods.FirstOrDefault(m => m.Name == "get_ID" && m.Parameters.Count == 0);
            foreach (var type in module.GetTypes().ToList())
                foreach (var method in type.Methods)
                {
                    string target = TldLoaderCompatPolicy.ReplacedLoaderMethod(type.FullName, method.Name);
                    if (target == null || !method.HasBody || getId == null) continue;
                    ReplaceBody(module, method, target, getId, bridgeType);
                    report.ReplacedBodies.Add(type.Name + "." + method.Name);
                }
            var database = module.GetType("TLDLoader.ItemDatabase");
            var register = database == null ? null : database.Methods.FirstOrDefault(m => m.Name == "RegisterItem" && m.HasBody && m.Parameters.Count == 3);
            if (register != null && getId != null)
            {
                // After TLDLoader's own bookkeeping: give the prefab its beta prefab id.
                var hookRegister = new MethodReference("ModRegisterPrefab", module.TypeSystem.Void, bridgeType()) { HasThis = false };
                hookRegister.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                hookRegister.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                hookRegister.Parameters.Add(new ParameterDefinition(register.Parameters[1].ParameterType));
                var rip = register.Body.GetILProcessor();
                var tail = rip.Create(OpCodes.Ldarg_1);
                foreach (var instruction in register.Body.Instructions.ToArray())
                    if (instruction.OpCode == OpCodes.Ret) { instruction.OpCode = OpCodes.Br; instruction.Operand = tail; }
                rip.Append(tail);
                rip.Append(rip.Create(OpCodes.Callvirt, getId));
                rip.Append(rip.Create(OpCodes.Ldarg_3));
                rip.Append(rip.Create(OpCodes.Ldarg_2));
                rip.Append(rip.Create(OpCodes.Call, hookRegister));
                rip.Append(rip.Create(OpCodes.Ret));
                report.RegisterHooked = true;
            }
            // Every mod file goes through the translator before Assembly.LoadFrom.
            var load = loader.Methods.FirstOrDefault(m => m.Name == "LoadDLL" && m.HasBody && m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.String");
            if (load == null) throw new InvalidDataException("TLDLoader.ModLoader.LoadDLL(string) is missing.");
            var hook = new MethodReference("TranslateMod", module.TypeSystem.String, bridgeType()) { HasThis = false };
            hook.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            var il = load.Body.GetILProcessor();
            var first = load.Body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Call, hook));
            il.InsertBefore(first, il.Create(OpCodes.Starg, load.Parameters[0]));
            report.LoadDllHooked = true;
        }

        static void RedirectLoaderPaths(ModuleDefinition module, Func<TypeReference> bridgeType)
        {
            const string gameFolder = "\\TheLongDrive";
            MethodReference root = null;
            foreach (var method in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (var suffix in method.Body.Instructions)
            {
                if (suffix.OpCode != OpCodes.Ldstr) continue;
                string path = (string)suffix.Operand;
                if (!path.StartsWith(gameFolder + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                var folderPath = suffix.Previous;
                var folder = folderPath == null ? null : folderPath.Previous;
                var concat = suffix.Next == null ? null : suffix.Next.Operand as MethodReference;
                var call = folderPath == null ? null : folderPath.Operand as MethodReference;
                if (folder == null || folder.OpCode != OpCodes.Ldc_I4_5 ||
                    call == null || call.FullName != "System.String System.Environment::GetFolderPath(System.Environment/SpecialFolder)" ||
                    concat == null || concat.FullName != "System.String System.String::Concat(System.String,System.String)") continue;

                if (root == null) root = new MethodReference("LoaderDataRoot", module.TypeSystem.String, bridgeType()) { HasThis = false };
                folder.OpCode = OpCodes.Nop;
                folder.Operand = null;
                folderPath.Operand = root;
                suffix.Operand = path.Substring(gameFolder.Length);
            }
        }

        // body := bridge.target(this as object?, args...) with Mod arguments passed as their ID.
        static void ReplaceBody(ModuleDefinition module, MethodDefinition method, string target, MethodReference getId, Func<TypeReference> bridgeType)
        {
            bool returnsSelf = method.HasThis && method.ReturnType.FullName == method.DeclaringType.FullName;
            var helper = new MethodReference(target, returnsSelf ? module.TypeSystem.Void : method.ReturnType, bridgeType()) { HasThis = false };
            if (method.HasThis) helper.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            foreach (var p in method.Parameters)
                helper.Parameters.Add(new ParameterDefinition(p.ParameterType.FullName == "TLDLoader.Mod" ? module.TypeSystem.String : p.ParameterType));
            var body = method.Body;
            body.Instructions.Clear(); body.ExceptionHandlers.Clear(); body.Variables.Clear();
            var il = body.GetILProcessor();
            if (method.HasThis) il.Append(il.Create(OpCodes.Ldarg_0));
            foreach (var p in method.Parameters)
            {
                il.Append(il.Create(OpCodes.Ldarg, p));
                if (p.ParameterType.FullName == "TLDLoader.Mod") il.Append(il.Create(OpCodes.Callvirt, getId));
            }
            il.Append(il.Create(OpCodes.Call, helper));
            if (returnsSelf) il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ret));
        }

        bool Exists(MemberReference reference)
        {
            // Roadworthy's own assembly (bridge helpers, stand-ins) is checked by the gate instead.
            var declaring = reference is TypeReference ? (TypeReference)reference : reference.DeclaringType;
            if (declaring != null && ScopeOf(declaring) != null && ScopeOf(declaring).Name == runtimeName) return true;
            try
            {
                var field = reference as FieldReference;
                if (field != null) return field.Resolve() != null;
                var method = reference as MethodReference;
                if (method != null) return method.Resolve() != null;
                var type = reference as TypeReference;
                if (type != null) return type.Resolve() != null;
            }
            catch (Exception) { return false; }
            return true;
        }

        static IMetadataScope ScopeOf(MemberReference reference)
        {
            var type = reference as TypeReference ?? reference.DeclaringType;
            if (type == null) return null;
            while (type is TypeSpecification) type = ((TypeSpecification)type).ElementType;
            while (type.DeclaringType != null) type = type.DeclaringType;
            return type.Scope;
        }

        void CollectMissing(MethodDefinition method, Report report, TypeDefinition owner, Dictionary<TypeDefinition, bool> missingByType)
        {
            foreach (var instruction in method.Body.Instructions)
            {
                var reference = instruction.Operand as MemberReference;
                if (reference == null || reference is TypeDefinition || reference is MethodDefinition || reference is FieldDefinition) continue;
                if (!IsGame(ScopeOf(reference))) continue;
                if (Exists(reference)) continue;
                string key = reference is TypeReference ? reference.FullName : reference.DeclaringType.FullName + "::" + reference.Name;
                RecordMissing(report, missingByType, owner, method, key);
            }
        }

        static void RecordMissing(Report report, Dictionary<TypeDefinition, bool> missingByType, TypeDefinition owner, MethodDefinition method, string key)
        {
            SortedSet<string> users;
            if (!report.Missing.TryGetValue(key, out users)) report.Missing[key] = users = new SortedSet<string>(StringComparer.Ordinal);
            users.Add(owner.FullName + "." + method.Name);
            missingByType[owner] = true;
        }

        static bool IsHarmonyPatchAttribute(CustomAttribute attribute)
        {
            return attribute.AttributeType.Namespace == "HarmonyLib" && attribute.AttributeType.Name == "HarmonyPatch";
        }

        static bool HasHarmonyPatch(TypeDefinition type)
        {
            return type.CustomAttributes.Any(IsHarmonyPatchAttribute);
        }

        // These patches have equivalent beta entry points. Match their original target
        // and patch signature before changing a DLL, since newer releases may do this themselves.
        static void PortHarmonyPatch(ModuleDefinition module, TypeDefinition type, Func<TypeReference> bridge, Report report)
        {
            if (type.FullName == "WorldTweaker.Harmony.GetTerrainHeight_Postfix" ||
                type.FullName == "WorldTweaker.Harmony.GetTerrainHeightWorld_Postfix")
            {
                PortTerrainHeightPatch(module, type, report);
                return;
            }
            if (type.FullName == "TheLongWho.Harmony.Patch_Building_SpawnStuff")
            {
                PortBuildingSpawnPatch(module, type, bridge, report);
                return;
            }
            if (type.FullName != "Amt.PrepareSaveDataPatch" && type.FullName != "Amt.SavePatch" &&
                type.FullName != "AskJimmyEquippedAccessoryVanillaSaveFilterPatch" &&
                type.FullName != "WorldTweaker.Harmony.Patch_itemdatabase_ReturnItem" &&
                type.FullName != "WorldTweaker.Harmony.Patch_MainMenuScript_FStart") return;
            var attributes = type.CustomAttributes.Where(IsHarmonyPatchAttribute).Take(2).ToArray();
            if (attributes.Length != 1 || attributes[0].ConstructorArguments.Count != 2) return;
            var attribute = attributes[0];
            var target = attribute.ConstructorArguments[0].Value as TypeReference;
            var name = attribute.ConstructorArguments[1].Value as string;
            if (target == null || !IsGame(ScopeOf(target))) return;
            var replacement = target;
            if (type.FullName == "WorldTweaker.Harmony.Patch_MainMenuScript_FStart" &&
                (target.FullName == "mainmenuscript" || target.FullName == "menuhandler") && name == "PressedLoadScene")
                replacement = new TypeReference("", "menuhandler", module, ScopeOf(target));
            TypeDefinition gameType;
            try { gameType = replacement.Resolve(); } catch (AssemblyResolutionException) { return; }
            if (gameType == null) return;

            string patched;
            if (type.FullName == "WorldTweaker.Harmony.Patch_itemdatabase_ReturnItem" && target.FullName == "itemdatabase" && name == "ReturnItem")
            {
                var postfix = type.Methods.SingleOrDefault(m => m.Name == "Postfix" && m.IsStatic && m.HasBody &&
                    m.ReturnType.FullName == "System.Void" && m.Parameters.Select(p => p.Name)
                        .SequenceEqual(new[] { "__instance", "__result", "category", "id" }) &&
                    m.Parameters.Select(p => p.ParameterType.FullName)
                        .SequenceEqual(new[] { "itemdatabase", "UnityEngine.GameObject&", "System.Int32", "System.Int32" }));
                if (postfix == null) return;
                postfix.Parameters[0].Name = "database";
                postfix.Parameters[2].Name = "first";
                postfix.Parameters[3].Name = "second";
                replacement = bridge();
                patched = "ReturnItem";
            }
            else if (type.FullName == "WorldTweaker.Harmony.Patch_MainMenuScript_FStart" && replacement != target &&
                gameType.Methods.Count(m => m.Name == "PressedStart" && m.Parameters.Count == 0) == 1)
            {
                var prefix = type.Methods.SingleOrDefault(m => m.Name == "Prefix" && m.IsStatic && m.HasBody &&
                    m.ReturnType.FullName == "System.Boolean" && m.Parameters.Select(p => p.Name)
                        .SequenceEqual(new[] { "__instance", "s" }) && m.Parameters[1].ParameterType.FullName == "System.String");
                if (prefix == null || UsesSecondArgument(prefix)) return;
                prefix.Parameters.RemoveAt(1);
                patched = "PressedStart";
            }
            else if (type.FullName == "Amt.PrepareSaveDataPatch" && target.FullName == "savedatascript" && name == "RefreshData" &&
                gameType.Methods.Count(m => m.Name == "SaveToData" && m.Parameters.Count == 1 &&
                    m.Parameters[0].ParameterType.FullName == "savedata") == 1 &&
                type.Methods.Any(m => m.Name == "Postfix" && m.IsStatic && m.Parameters.Count == 0))
            {
                patched = "SaveToData";
            }
            else if (type.FullName == "AskJimmyEquippedAccessoryVanillaSaveFilterPatch" && target.FullName == "savedatascript" &&
                name == "AddToStuff" && gameType.Methods.Count(m => m.Name == "SaveToDictionary" && m.IsStatic &&
                    m.Parameters.Count == 2 && m.Parameters[0].Name == "_tosave" &&
                    m.Parameters[0].ParameterType.FullName == "tosaveitemscript" && m.Parameters[1].ParameterType.FullName == "itemDataClass") == 1)
            {
                var prefix = type.Methods.SingleOrDefault(m => m.Name == "Prefix" && m.IsStatic && m.HasBody &&
                    m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 2 &&
                    m.Parameters[0].Name == "_tosave" && m.Parameters[0].ParameterType.FullName == "tosaveitemscript" &&
                    m.Parameters[1].Name == "_dict");
                if (prefix == null || UsesSecondArgument(prefix)) return;
                prefix.Parameters.RemoveAt(1);
                patched = "SaveToDictionary";
            }
            else if (type.FullName == "Amt.SavePatch" && target.FullName == "newSaveScreenScript" && name == "Save" &&
                gameType.Methods.Count(m => m.Name == "Save" && m.Parameters.Count == 4) == 1 &&
                gameType.Fields.Any(f => f.Name == "savedName" && f.FieldType.FullName == "System.String"))
            {
                var postfix = type.Methods.SingleOrDefault(m => m.Name == "Postfix" && m.IsStatic && m.HasBody &&
                    m.ReturnType.FullName == "System.Void" && m.Parameters.Select(p => p.Name).SequenceEqual(new[] { "___saveDataPreName", "___savedName" }) &&
                    m.Parameters.All(p => p.ParameterType.FullName == "System.String"));
                var path = gameType.Module.GetType("pathscript");
                var prefix = path == null ? null : path.Fields.SingleOrDefault(f => f.Name == "saveDataPreName" && f.IsPublic && f.IsStatic &&
                    f.FieldType.FullName == "System.String");
                if (postfix == null || prefix == null || postfix.CustomAttributes.Any(a => a.AttributeType.Namespace == "HarmonyLib")) return;
                postfix.Name = "SaveSidecar";
                var wrapper = new MethodDefinition("Postfix", MethodAttributes.Static | MethodAttributes.Public, module.TypeSystem.Void);
                wrapper.Parameters.Add(new ParameterDefinition("___savedName", ParameterAttributes.None, module.TypeSystem.String));
                type.Methods.Add(wrapper);
                var il = wrapper.Body.GetILProcessor();
                il.Emit(OpCodes.Ldsfld, module.ImportReference(prefix));
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, postfix);
                il.Emit(OpCodes.Ret);
                patched = name;
            }
            else return;

            attribute.ConstructorArguments[0] = new CustomAttributeArgument(attribute.ConstructorArguments[0].Type, replacement);
            attribute.ConstructorArguments[1] = new CustomAttributeArgument(module.TypeSystem.String, patched);
            report.Rewrites++;
            report.PinnedPatches.Add(type.FullName + " -> " + replacement.FullName + "." + patched);
        }

        static bool UsesSecondArgument(MethodDefinition method)
        {
            return method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_1 || i.Operand == method.Parameters[1]);
        }

        static void PortBuildingSpawnPatch(ModuleDefinition module, TypeDefinition type, Func<TypeReference> bridge, Report report)
        {
            var attributes = type.CustomAttributes.Where(IsHarmonyPatchAttribute).Take(2).ToArray();
            if (attributes.Length != 1 || attributes[0].ConstructorArguments.Count != 2) return;
            var arguments = attributes[0].ConstructorArguments;
            var target = arguments[0].Value as TypeReference;
            if (target == null || target.FullName != "buildingscript" || !IsGame(ScopeOf(target)) ||
                (arguments[1].Value as string) != "SpawnStuff") return;
            var prefix = type.Methods.SingleOrDefault(m => m.Name == "Prefix" && m.IsStatic && m.HasBody &&
                m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1 && m.Parameters[0].Name == "__instance" &&
                m.Parameters[0].ParameterType.FullName == "TldMp.StableShells.buildingscript");
            if (prefix == null || prefix.CustomAttributes.Any(a => a.AttributeType.Namespace == "HarmonyLib")) return;

            var replacement = new TypeReference("", "poiScript", module, ScopeOf(target));
            TypeDefinition gameType;
            try { gameType = replacement.Resolve(); } catch (AssemblyResolutionException) { return; }
            var spawn = gameType == null ? null : gameType.Methods.SingleOrDefault(m => m.Name == "SpawnStuff" && !m.IsStatic &&
                m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1 &&
                m.Parameters[0].ParameterType.FullName == "poiGenScript/poiClass");
            if (spawn == null) return;

            prefix.Name = "RaiseLegacyBuildingSpawn";
            var wrapper = new MethodDefinition("Prefix", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            var record = module.ImportReference(spawn.Parameters[0].ParameterType);
            wrapper.Parameters.Add(new ParameterDefinition("__instance", ParameterAttributes.None, replacement));
            wrapper.Parameters.Add(new ParameterDefinition("__0", ParameterAttributes.None, record));
            var getBuilding = new MethodReference("BuildingBeforeItemSpawn", prefix.Parameters[0].ParameterType, bridge());
            getBuilding.Parameters.Add(new ParameterDefinition(replacement));
            getBuilding.Parameters.Add(new ParameterDefinition(record));
            type.Methods.Add(wrapper);
            var il = wrapper.Body.GetILProcessor();
            var discard = il.Create(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, getBuilding);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, discard);
            il.Emit(OpCodes.Call, prefix);
            il.Emit(OpCodes.Ret);
            il.Append(discard);
            il.Emit(OpCodes.Ret);

            arguments[0] = new CustomAttributeArgument(arguments[0].Type, replacement);
            report.Rewrites++;
            report.PinnedPatches.Add(type.FullName + " -> poiScript.SpawnStuff(poiClass)");
        }

        static bool RetireLegacyPhysicsPausePatch(TypeDefinition type)
        {
            string targetType, targetName;
            switch (type.FullName)
            {
                case "LongHaulOverhaul.Patches.PauseRBsPatch": targetType = "mainscript"; targetName = "PauseRBs"; break;
                case "LongHaulOverhaul.Patches.UnPauseRBsPatch": targetType = "mainscript"; targetName = "UnPauseRBs"; break;
                case "LongHaulOverhaul.Patches.SetCenterOfMassPausePatch": targetType = "setcenterofmass"; targetName = "Pause"; break;
                case "LongHaulOverhaul.Patches.SetCenterOfMassUnPausePatch": targetType = "setcenterofmass"; targetName = "UnPause"; break;
                default: return false;
            }
            var attributes = type.CustomAttributes.Where(IsHarmonyPatchAttribute).Take(2).ToArray();
            if (attributes.Length != 1 || attributes[0].ConstructorArguments.Count != 2) return false;
            var args = attributes[0].ConstructorArguments;
            var target = args[0].Value as TypeReference;
            return target != null && IsGame(ScopeOf(target)) && target.FullName == targetType &&
                (args[1].Value as string) == targetName && type.Methods.Any(m => m.Name == "Prefix" &&
                    m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Boolean");
        }

        static void PortTerrainHeightPatch(ModuleDefinition module, TypeDefinition type, Report report)
        {
            var attributes = type.CustomAttributes.Where(IsHarmonyPatchAttribute).Take(2).ToArray();
            if (attributes.Length != 1 || attributes[0].ConstructorArguments.Count != 3) return;
            var arguments = attributes[0].ConstructorArguments;
            var target = arguments[0].Value as TypeReference;
            string name = type.Name == "GetTerrainHeight_Postfix" ? "GetTerrainHeight" : "GetTerrainHeightWorld";
            var signature = arguments[2].Value as CustomAttributeArgument[];
            if (target == null || target.FullName != "TerrainGenerator" || !IsGame(ScopeOf(target)) ||
                (arguments[1].Value as string) != name || signature == null || signature.Length != 6 ||
                !(signature[0].Value is TypeReference) || ((TypeReference)signature[0].Value).FullName != "UnityEngine.Vector2" ||
                signature.Skip(1).Any(a => !(a.Value is TypeReference) || ((TypeReference)a.Value).FullName != "System.Boolean")) return;

            var postfix = type.Methods.SingleOrDefault(m => m.Name == "Postfix" && m.IsStatic && m.HasBody &&
                m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1 && m.Parameters[0].Name == "__result" &&
                m.Parameters[0].ParameterType.FullName == "System.Single&");
            if (postfix == null || postfix.CustomAttributes.Any(a => a.AttributeType.Namespace == "HarmonyLib")) return;

            var replacement = new TypeReference("", "terrainGenSettings", module, ScopeOf(target));
            TypeDefinition gameType;
            try { gameType = replacement.Resolve(); } catch (AssemblyResolutionException) { return; }
            var height = gameType == null ? null : gameType.Methods.SingleOrDefault(m => m.Name == "Height" &&
                m.ReturnType.FullName == "System.Double" && m.Parameters.Count == 3 &&
                m.Parameters[0].ParameterType.FullName == "UnityEngine.Vector3d" &&
                m.Parameters[1].ParameterType.FullName == "System.Collections.Generic.List`1<roadGenScript/roadClass>" &&
                m.Parameters[2].ParameterType.FullName == "System.Collections.Generic.List`1<poiGenScript/poiClass>");
            if (height == null) return;

            // Preserve the native double when the legacy float postfix leaves its input alone.
            postfix.Name = "AdjustLegacyHeight";
            var wrapper = new MethodDefinition("Postfix", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            wrapper.Parameters.Add(new ParameterDefinition("__result", ParameterAttributes.None, new ByReferenceType(module.TypeSystem.Double)));
            var input = new VariableDefinition(module.TypeSystem.Single);
            var adjusted = new VariableDefinition(module.TypeSystem.Single);
            wrapper.Body.Variables.Add(input);
            wrapper.Body.Variables.Add(adjusted);
            type.Methods.Add(wrapper);
            var il = wrapper.Body.GetILProcessor();
            var end = il.Create(OpCodes.Ret);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldind_R8);
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc, input);
            il.Emit(OpCodes.Stloc, adjusted);
            il.Emit(OpCodes.Ldloca, adjusted);
            il.Emit(OpCodes.Call, postfix);
            il.Emit(OpCodes.Ldloc, input);
            il.Emit(OpCodes.Ldloc, adjusted);
            il.Emit(OpCodes.Beq, end);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, adjusted);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Stind_R8);
            il.Append(end);

            arguments[0] = new CustomAttributeArgument(arguments[0].Type, replacement);
            arguments[1] = new CustomAttributeArgument(module.TypeSystem.String, "Height");
            arguments[2] = new CustomAttributeArgument(arguments[2].Type, height.Parameters.Select(p =>
                new CustomAttributeArgument(arguments[0].Type, module.ImportReference(p.ParameterType))).ToArray());
            report.Rewrites++;
            report.PinnedPatches.Add(type.FullName + " -> terrainGenSettings.Height(Vector3d, road list, POI list)");
        }

        TldLoaderCompatPolicy.TargetStatus PatchTarget(TypeDefinition type)
        {
            TypeReference target = null; string name = null; int methodType = 0; int parameterCount = -1;
            var attributes = type.CustomAttributes.Where(IsHarmonyPatchAttribute).ToList();
            foreach (var method in type.Methods) attributes.AddRange(method.CustomAttributes.Where(IsHarmonyPatchAttribute));
            try
            {
                foreach (var attribute in attributes)
                    foreach (var argument in attribute.ConstructorArguments)
                    {
                        if (argument.Value is TypeReference) { if (target == null) target = (TypeReference)argument.Value; }
                        else if (argument.Value is string) { if (name == null) name = (string)argument.Value; }
                        else if (argument.Value is CustomAttributeArgument[]) parameterCount = ((CustomAttributeArgument[])argument.Value).Length;
                        else if (argument.Type.Name == "MethodType" && argument.Value is int) methodType = (int)argument.Value;
                    }
            }
            catch (Exception) { return TldLoaderCompatPolicy.TargetStatus.Unknown; }
            // A TargetMethod/TargetMethods method decides at run time.
            if (type.Methods.Any(m => m.Name == "TargetMethod" || m.Name == "TargetMethods")) return TldLoaderCompatPolicy.TargetStatus.Unknown;
            if (target == null) return TldLoaderCompatPolicy.TargetStatus.Unknown;
            TypeDefinition definition;
            try { definition = target.Resolve(); } catch (Exception) { definition = null; }
            if (definition == null) return IsGame(ScopeOf(target)) ? TldLoaderCompatPolicy.TargetStatus.Missing : TldLoaderCompatPolicy.TargetStatus.Unknown;
            string member = TldLoaderCompatPolicy.PatchedMethodName(name, methodType);
            if (member == null) return TldLoaderCompatPolicy.TargetStatus.Unknown;
            for (var t = definition; t != null; )
            {
                if (t.Methods.Any(m => m.Name == member && (parameterCount < 0 || m.Parameters.Count == parameterCount))) return TldLoaderCompatPolicy.TargetStatus.Exists;
                try { t = t.BaseType == null ? null : t.BaseType.Resolve(); } catch (Exception) { return TldLoaderCompatPolicy.TargetStatus.Unknown; }
            }
            return TldLoaderCompatPolicy.TargetStatus.Missing;
        }

        // Harmony binds patch arguments by name, and a target name with several overloads and no
        // argument types is an error; either one makes PatchAll throw for the whole mod. The beta
        // added overloads and renamed parameters, so: pick the overload the patch fits (and pin
        // it in the attribute), or report why it cannot fit (the class is then left out).
        string FitPatch(ModuleDefinition module, TypeDefinition type, Report report)
        {
            var patchMethods = type.Methods.Where(IsPatchMethod).ToList();
            if (patchMethods.Count == 0 || patchMethods.Any(m => m.Name == "Transpiler" || m.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyTranspiler"))) return null;
            if (type.Methods.Any(m => m.CustomAttributes.Any(IsHarmonyPatchAttribute))) return null;
            var attributes = type.CustomAttributes.Where(IsHarmonyPatchAttribute).ToList();
            if (attributes.Count != 1) return null;
            var attribute = attributes[0];
            TypeReference targetType = null; string name = null; bool hasTypes = false, other = false;
            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Value is TypeReference && targetType == null) targetType = (TypeReference)argument.Value;
                else if (argument.Value is string && name == null) name = (string)argument.Value;
                else if (argument.Value is CustomAttributeArgument[]) hasTypes = true;
                else other = true;
            }
            if (targetType == null || name == null || other) return null;
            TypeDefinition definition;
            try { definition = targetType.Resolve(); } catch (Exception) { return null; }
            if (definition == null) return null;
            var candidates = new List<MethodDefinition>();
            for (var t = definition; t != null && candidates.Count == 0; )
            {
                candidates.AddRange(t.Methods.Where(m => m.Name == name));
                try { t = t.BaseType == null ? null : t.BaseType.Resolve(); } catch (Exception) { return null; }
            }
            if (candidates.Count == 0) return null;
            var fitting = candidates.Where(c => patchMethods.All(p => Fits(p, c, definition))).ToList();
            if (hasTypes || candidates.Count == 1)
            {
                if (hasTypes) return null;
                return fitting.Count == 1 ? null : "its arguments do not fit the beta's " + definition.Name + "." + name + ": " + Unfit(patchMethods, candidates[0], definition);
            }
            if (fitting.Count != 1)
                return fitting.Count == 0 ? "no overload of the beta's " + definition.Name + "." + name + " fits its arguments" : "the beta's " + definition.Name + "." + name + " has several overloads it fits";
            // Pin the overload: HarmonyPatch(Type, string, Type[]).
            MethodDefinition withTypes;
            try
            {
                var attributeType = attribute.AttributeType.Resolve();
                withTypes = attributeType == null ? null : attributeType.Methods.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 3 &&
                    m.Parameters[0].ParameterType.FullName == "System.Type" && m.Parameters[1].ParameterType.FullName == "System.String" && m.Parameters[2].ParameterType.FullName == "System.Type[]");
            }
            catch (Exception) { withTypes = null; }
            if (withTypes == null) return "the beta's " + definition.Name + "." + name + " has overloads and Harmony's attribute could not name one";
            var typeType = module.ImportReference(withTypes.Parameters[0].ParameterType);
            var pinned = new CustomAttribute(module.ImportReference(withTypes));
            pinned.ConstructorArguments.Add(new CustomAttributeArgument(typeType, targetType));
            pinned.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, name));
            var types = fitting[0].Parameters.Select(p => new CustomAttributeArgument(typeType, module.ImportReference(p.ParameterType))).ToArray();
            pinned.ConstructorArguments.Add(new CustomAttributeArgument(module.ImportReference(withTypes.Parameters[2].ParameterType), types));
            type.CustomAttributes[type.CustomAttributes.IndexOf(attribute)] = pinned;
            report.Rewrites++;
            report.PinnedPatches.Add(type.FullName + " -> " + definition.Name + "." + name + "(" + string.Join(", ", fitting[0].Parameters.Select(p => p.ParameterType.Name).ToArray()) + ")");
            return null;
        }

        static bool IsPatchMethod(MethodDefinition method)
        {
            if (method.IsConstructor) return false;
            if (method.Name == "Prefix" || method.Name == "Postfix" || method.Name == "Finalizer" || method.Name == "Transpiler") return true;
            return method.CustomAttributes.Any(a => a.AttributeType.Namespace == "HarmonyLib" &&
                (a.AttributeType.Name == "HarmonyPrefix" || a.AttributeType.Name == "HarmonyPostfix" || a.AttributeType.Name == "HarmonyFinalizer"));
        }

        static bool Fits(MethodDefinition patch, MethodDefinition target, TypeDefinition targetType)
        {
            return patch.Parameters.All(p => TldLoaderCompatPolicy.PatchArgumentFits(p.Name, target.IsStatic, target.ReturnType.FullName == "System.Void",
                target.Parameters.Select(tp => tp.Name).ToArray(), field => HasField(targetType, field)));
        }

        static string Unfit(List<MethodDefinition> patches, MethodDefinition target, TypeDefinition targetType)
        {
            var bad = patches.SelectMany(p => p.Parameters).Where(p => !TldLoaderCompatPolicy.PatchArgumentFits(p.Name, target.IsStatic, target.ReturnType.FullName == "System.Void",
                target.Parameters.Select(tp => tp.Name).ToArray(), field => HasField(targetType, field))).Select(p => p.Name).Distinct().ToArray();
            return string.Join(", ", bad);
        }

        static bool HasField(TypeDefinition type, string name)
        {
            for (var t = type; t != null; )
            {
                if (t.Fields.Any(f => f.Name == name)) return true;
                try { t = t.BaseType == null ? null : t.BaseType.Resolve(); } catch (Exception) { return true; }
            }
            return false;
        }

        static void StripHarmony(TypeDefinition type)
        {
            Strip(type.CustomAttributes);
            foreach (var method in type.Methods) Strip(method.CustomAttributes);
            foreach (var nested in type.NestedTypes) StripHarmony(nested);
        }

        static void Strip(Mono.Collections.Generic.Collection<CustomAttribute> attributes)
        {
            for (int i = attributes.Count - 1; i >= 0; i--)
                if (attributes[i].AttributeType.Namespace == "HarmonyLib") attributes.RemoveAt(i);
        }
    }
}
