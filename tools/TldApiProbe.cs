using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TldMp;

// Generate stable-compiled call sites and run the actual mod translator over them.
// The probe DLL is inspected as metadata. It is never loaded or executed.
internal static class TldApiProbe
{
    sealed class TargetResolver : DefaultAssemblyResolver
    {
        internal AssemblyDefinition Game, Runtime;
        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            if (name.Name == "Assembly-CSharp") return Game;
            if (name.Name == Runtime.Name.Name) return Runtime;
            return base.Resolve(name);
        }
    }

    sealed class Probe
    {
        internal string Operation, Member, Status, Detail, BodyChange;
        internal MethodDefinition StableMethod;
        internal bool Public;
    }

    public static int Main(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine("Usage: TldApiProbe stable-assembly beta-assembly beta-managed roadworthy-runtime report-directory [mod DLLs...]");
            return 2;
        }
        using (var resolver = new TargetResolver())
        {
            foreach (string dir in resolver.GetSearchDirectories()) resolver.RemoveSearchDirectory(dir);
            resolver.AddSearchDirectory(args[2]);
            var reader = new ReaderParameters { AssemblyResolver = resolver };
            using (var stable = ModuleDefinition.ReadModule(args[0]))
            using (var source = ModuleDefinition.CreateModule("Roadworthy.ApiProbe", ModuleKind.Dll))
            using (var game = AssemblyDefinition.ReadAssembly(args[1], reader))
            using (var runtime = AssemblyDefinition.ReadAssembly(args[3], reader))
            {
                resolver.Game = game; resolver.Runtime = runtime;
                var type = new TypeDefinition("Roadworthy.Audit", "StableCalls", TypeAttributes.Class | TypeAttributes.Public, source.TypeSystem.Object);
                source.Types.Add(type);
                var probes = BuildProbes(stable, type, game.MainModule);
                var translator = new TldLoaderTranslator(args[1], new[] { args[2] }, runtime.Name.Name, runtime.Name.Version);
                using (var image = new MemoryStream())
                {
                    source.Write(image);
                    var translated = translator.TranslateMod(image.ToArray());
                    using (var result = ModuleDefinition.ReadModule(new MemoryStream(translated.Bytes), reader))
                        foreach (var method in result.GetType(type.FullName).Methods)
                            Inspect(method, probes[Int32.Parse(method.Name.Substring(1))]);
                }
                Write(args[4], probes, args.Skip(5));
            }
        }
        return 0;
    }

    static List<Probe> BuildProbes(ModuleDefinition stable, TypeDefinition output, ModuleDefinition beta)
    {
        var rows = new List<Probe>();
        var betaTypes = beta.GetTypes().ToDictionary(t => t.FullName);
        foreach (var type in stable.GetTypes().Where(t => !Generated(t.Name) && t.Name != "<Module>"))
        {
            if (type.HasGenericParameters)
            {
                rows.Add(new Probe { Member = type.FullName, Operation = "generic type", Public = Visible(type),
                    Status = "unprobed", Detail = "Open generic type and its members require closed instantiations." });
                continue;
            }
            var token = Add(output, rows, "type token", type.FullName, Visible(type));
            token.Body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, output.Module.ImportReference(type)));
            token.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
            token.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            if (type.IsEnum) { EnumValues(type, betaTypes, rows); continue; }

            foreach (var field in type.Fields.Where(f => !Generated(f.Name) && !f.IsLiteral))
            {
                foreach (string operation in field.IsInitOnly ? new[] { "read", "address" } : new[] { "read", "address", "write" })
                {
                    var method = Add(output, rows, operation, field.FullName, Visible(type) && field.IsPublic);
                    var il = method.Body.GetILProcessor();
                    if (!field.IsStatic) Argument(method, type, type.IsValueType);
                    if (operation == "write") Argument(method, field.FieldType, false);
                    var opcode = operation == "read" ? (field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld) :
                        operation == "address" ? (field.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda) :
                        field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld;
                    il.Emit(opcode, output.Module.ImportReference(field));
                    if (operation != "write") il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Ret);
                }
            }
            foreach (var member in type.Methods.Where(m => !Generated(m.Name) && m.Name != ".cctor"))
            {
                bool constructor = member.IsConstructor;
                if (member.HasGenericParameters || member.CallingConvention == MethodCallingConvention.VarArg)
                {
                    rows.Add(new Probe { Member = member.FullName, Operation = "method", Public = Visible(type) && member.IsPublic,
                        Status = "unprobed", Detail = "Open generic or variable-argument method." });
                    continue;
                }
                var method = Add(output, rows, constructor ? "construct" : "call", member.FullName, Visible(type) && member.IsPublic);
                rows[rows.Count - 1].StableMethod = member;
                var il = method.Body.GetILProcessor();
                if (member.HasThis && !constructor) Argument(method, type, type.IsValueType);
                foreach (var argument in member.Parameters) Argument(method, argument.ParameterType, false);
                il.Emit(constructor ? OpCodes.Newobj : !member.HasThis || type.IsValueType ? OpCodes.Call : OpCodes.Callvirt,
                    output.Module.ImportReference(member));
                if (constructor || member.ReturnType.MetadataType != MetadataType.Void) il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ret);
            }
        }
        return rows;
    }

    static bool Generated(string name) { return name.IndexOfAny(new[] { '<', '$' }) >= 0; }
    static bool Visible(TypeDefinition type) { return type.IsNested ? type.IsNestedPublic && Visible(type.DeclaringType) : type.IsPublic; }

    static MethodDefinition Add(TypeDefinition owner, List<Probe> probes, string operation, string member, bool visible)
    {
        var method = new MethodDefinition("P" + probes.Count, MethodAttributes.Public | MethodAttributes.Static, owner.Module.TypeSystem.Void);
        owner.Methods.Add(method);
        probes.Add(new Probe { Operation = operation, Member = member, Public = visible });
        return method;
    }

    static void Argument(MethodDefinition method, TypeReference type, bool byRef)
    {
        var argument = method.Module.ImportReference(type);
        var parameter = new ParameterDefinition(byRef ? new ByReferenceType(argument) : argument);
        method.Parameters.Add(parameter);
        method.Body.GetILProcessor().Emit(OpCodes.Ldarg, parameter);
    }

    static void EnumValues(TypeDefinition type, Dictionary<string, TypeDefinition> beta, List<Probe> rows)
    {
        TypeDefinition target;
        beta.TryGetValue(TldLoaderCompatPolicy.RenamedType(type.FullName) ?? type.FullName, out target);
        foreach (var field in type.Fields.Where(f => f.HasConstant))
        {
            var current = target == null ? null : target.Fields.FirstOrDefault(f => f.Name == field.Name && f.HasConstant);
            rows.Add(new Probe { Operation = "enum constant", Member = field.FullName, Public = Visible(type),
                Status = current != null && Object.Equals(field.Constant, current.Constant) ? "direct" : "semantic review",
                Detail = current == null ? "Beta value absent" : field.Constant + " -> " + current.Constant });
        }
    }

    static bool Shell(TypeReference type)
    {
        if (type == null) return false;
        var generic = type as GenericInstanceType;
        if (generic != null && generic.GenericArguments.Any(Shell)) return true;
        return type.GetElementType().Namespace == "TldMp.StableShells";
    }

    static void Inspect(MethodDefinition method, Probe probe)
    {
        string fallback = null, target = null;
        MethodDefinition targetMethod = null;
        foreach (var instruction in method.Body.Instructions)
        {
            var reference = instruction.Operand as MemberReference;
            if (reference == null) continue;
            var owner = reference as TypeReference ?? reference.DeclaringType;
            if (Shell(owner)) fallback = "Compatibility shell";
            if (owner.FullName == "RoadworthyMissingGameMembers" || owner.FullName == "TldMp.TldLoaderBridge" && reference.Name.StartsWith("Shadow"))
                fallback = reference.FullName;
            try
            {
                var field = reference as FieldReference;
                var call = reference as MethodReference;
                var namedType = reference as TypeReference;
                var found = field != null ? (IMemberDefinition)field.Resolve() : call != null ? call.Resolve() : (IMemberDefinition)namedType.Resolve();
                if (found == null) throw new InvalidDataException("Unresolved");
                target = reference.FullName;
                targetMethod = found as MethodDefinition;
                if (field != null)
                {
                    bool staticOpcode = instruction.OpCode == OpCodes.Ldsfld || instruction.OpCode == OpCodes.Stsfld || instruction.OpCode == OpCodes.Ldsflda;
                    var definition = (FieldDefinition)found;
                    if (definition.IsStatic != staticOpcode) throw new InvalidDataException("Field staticness mismatch");
                    if (probe.Operation == "write" && definition.IsInitOnly) throw new InvalidDataException("Target is readonly");
                    if (probe.Operation == "address" && definition.FieldType.FullName != field.FieldType.FullName)
                        throw new InvalidDataException("Field address type mismatch");
                }
                if (probe.Public && !DefinedHere(found, method.Module) && !Accessible(found))
                    throw new InvalidDataException("Target visibility reduced");
            }
            catch (Exception e)
            {
                probe.Status = "unresolved";
                probe.Detail = reference.FullName + " (" + e.Message + ")";
                return;
            }
        }
        if (probe.Operation == "address" && method.Body.Variables.Count > 0 && fallback == null)
            fallback = "Address is a temporary copy. Writes through it do not update beta state.";
        probe.Status = fallback != null ? "stand-in" : target == probe.Member ? "direct" : "rewritten";
        probe.Detail = fallback ?? target ?? "";
        if (probe.Status == "direct" && probe.StableMethod != null && targetMethod != null &&
            !SameBody(probe.StableMethod, targetMethod)) probe.BodyChange = "IL differs";
    }

    // Metadata tokens vary between releases. Compare instructions by symbol
    // and branch destination so an unchanged signature can still reveal drift.
    static bool SameBody(MethodDefinition stable, MethodDefinition beta)
    {
        if (stable.HasBody != beta.HasBody) return false;
        if (!stable.HasBody) return true;
        var a = stable.Body.Instructions;
        var b = beta.Body.Instructions;
        if (a.Count != b.Count ||
            !stable.Body.Variables.Select(v => v.VariableType.FullName).SequenceEqual(beta.Body.Variables.Select(v => v.VariableType.FullName)) ||
            stable.Body.ExceptionHandlers.Count != beta.Body.ExceptionHandlers.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].OpCode.Code != b[i].OpCode.Code ||
                OperandKey(a[i].Operand, stable.Body) != OperandKey(b[i].Operand, beta.Body)) return false;
        for (int i = 0; i < stable.Body.ExceptionHandlers.Count; i++)
        {
            var x = stable.Body.ExceptionHandlers[i];
            var y = beta.Body.ExceptionHandlers[i];
            if (x.HandlerType != y.HandlerType || OperandKey(x.CatchType, stable.Body) != OperandKey(y.CatchType, beta.Body) ||
                OperandKey(x.TryStart, stable.Body) != OperandKey(y.TryStart, beta.Body) ||
                OperandKey(x.TryEnd, stable.Body) != OperandKey(y.TryEnd, beta.Body) ||
                OperandKey(x.HandlerStart, stable.Body) != OperandKey(y.HandlerStart, beta.Body) ||
                OperandKey(x.HandlerEnd, stable.Body) != OperandKey(y.HandlerEnd, beta.Body) ||
                OperandKey(x.FilterStart, stable.Body) != OperandKey(y.FilterStart, beta.Body)) return false;
        }
        return true;
    }

    static string OperandKey(object operand, MethodBody body)
    {
        if (operand == null) return null;
        var instruction = operand as Instruction;
        if (instruction != null) return body.Instructions.IndexOf(instruction).ToString();
        var targets = operand as Instruction[];
        if (targets != null) return string.Join(",", targets.Select(i => body.Instructions.IndexOf(i)));
        var parameter = operand as ParameterDefinition;
        if (parameter != null) return parameter.Index.ToString();
        var local = operand as VariableDefinition;
        if (local != null) return local.Index.ToString();
        var member = operand as MemberReference;
        if (member != null) return member.FullName;
        var formatted = operand as IFormattable;
        return formatted == null ? operand.ToString() : formatted.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
    }

    static bool DefinedHere(IMemberDefinition member, ModuleDefinition module)
    {
        var type = member as TypeDefinition ?? member.DeclaringType;
        return type.Module == module;
    }

    static bool Accessible(IMemberDefinition member)
    {
        var type = member as TypeDefinition;
        if (type != null) return Visible(type);
        var field = member as FieldDefinition;
        var method = member as MethodDefinition;
        return Visible(member.DeclaringType) && (field != null ? field.IsPublic : method != null && method.IsPublic);
    }

    static void Write(string directory, List<Probe> rows, IEnumerable<string> mods)
    {
        var users = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var file in mods)
        using (var module = ModuleDefinition.ReadModule(file))
            {
                var name = Path.GetFileName(file);
                foreach (var reference in module.GetTypeReferences().Where(r => GameReference(r)))
                    AddUser(users, "type token", reference.FullName, name);
                foreach (var instruction in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions))
                {
                    var reference = instruction.Operand as MemberReference;
                    if (reference == null || !GameReference(reference)) continue;
                    var method = reference as MethodReference;
                    var generic = method as GenericInstanceMethod;
                    if (generic != null) method = generic.ElementMethod;
                    if (method != null) AddUser(users, instruction.OpCode == OpCodes.Newobj ? "construct" : "call", method.FullName, name);
                    if (reference is FieldReference)
                    {
                        var code = instruction.OpCode.Code;
                        if (code == Code.Stfld || code == Code.Stsfld) AddUser(users, "write", reference.FullName, name);
                        else if (code == Code.Ldflda || code == Code.Ldsflda) AddUser(users, "address", reference.FullName, name);
                        else AddUser(users, "read", reference.FullName, name);
                    }
                }
            }
        Directory.CreateDirectory(directory);
        Func<Probe, string> used = row => users.ContainsKey(row.Operation + "|" + row.Member)
            ? string.Join("; ", users[row.Operation + "|" + row.Member]) : "";
        File.WriteAllLines(Path.Combine(directory, "api.csv"), new[] { "Operation,Stable signature,Public,Outcome,Detail,Mod references,Body change" }.Concat(
            rows.Select(row => string.Join(",", new[] { row.Operation, row.Member, row.Public.ToString(), row.Status, row.Detail, used(row), row.BodyChange }.Select(Csv)))));
        var summary = new StringBuilder("# Stable-to-beta translator probe\n\n");
        summary.AppendLine("| Outcome | Operations | Public | Referenced by corpus |");
        summary.AppendLine("|---|---:|---:|---:|");
        foreach (var group in rows.GroupBy(r => r.Status).OrderBy(g => g.Key))
            summary.AppendLine(string.Format("| {0} | {1} | {2} | {3} |", group.Key, group.Count(), group.Count(r => r.Public), group.Count(r => used(r).Length != 0)));
        summary.AppendLine("\nCounts are call-site operations. A field can contribute a read, write and address operation.");
        summary.AppendLine("The probe checks emitted references. It does not execute gameplay or infer enum constants already inlined into a mod.");
        var bodyChanges = rows.Where(r => r.BodyChange != null).ToList();
        summary.AppendLine(bodyChanges.Count + " directly bound methods have different IL, including " +
            bodyChanges.Count(r => used(r).Length != 0) + " referenced by the corpus. These are review candidates, not compatibility failures.");
        summary.AppendLine("\n## Corpus operations with unresolved references");
        foreach (var row in rows.Where(r => r.Status == "unresolved" && used(r).Length != 0))
            summary.AppendLine("- `" + row.Member + "` (" + row.Operation + ") — " + used(row));
        File.WriteAllText(Path.Combine(directory, "summary.md"), summary.ToString());
        Console.Write(summary);
    }

    static bool GameReference(MemberReference reference)
    {
        var type = (reference as TypeReference ?? reference.DeclaringType).GetElementType();
        return type.Scope != null && type.Scope.Name == "Assembly-CSharp";
    }

    static void AddUser(Dictionary<string, SortedSet<string>> users, string operation, string member, string mod)
    {
        string key = operation + "|" + member;
        SortedSet<string> names;
        if (!users.TryGetValue(key, out names)) users[key] = names = new SortedSet<string>(StringComparer.Ordinal);
        names.Add(mod);
    }

    static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
}
