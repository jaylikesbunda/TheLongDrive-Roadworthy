using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using TldMp;

// Compares the stable branch's Assembly-CSharp (what TLDLoader mods are built for) with the
// beta's, member by member, and says for each difference whether the TLDLoader translator
// already covers it (TldLoaderCompatPolicy rules, signedness retyping, array/list shapes,
// case-only renames). Members that the given mods actually use are marked.
// args: <stable Assembly-CSharp> <beta Assembly-CSharp> <report.md> [mod or loader dlls...]
internal static class TldStableDiff
{
    sealed class Row { public string Kind, Type, Member, Stable, Beta, Status; public bool Used, Public; }

    public static int Main(string[] args)
    {
        var stable = ModuleDefinition.ReadModule(args[0]);
        var beta = ModuleDefinition.ReadModule(args[1]);
        var betaTypes = beta.GetTypes().GroupBy(t => t.FullName).ToDictionary(g => g.Key, g => g.First());
        var used = UsedMembers(args.Skip(3));
        var rows = new List<Row>();
        int typesCompared = 0, membersCompared = 0;

        foreach (var type in stable.GetTypes())
        {
            if (Generated(type.Name) || type.Name == "<Module>") continue;
            string renamed = TldLoaderCompatPolicy.RenamedType(type.FullName);
            string betaName = renamed != null && !renamed.Contains(".") ? renamed : type.FullName;
            TypeDefinition other;
            if (!betaTypes.TryGetValue(betaName, out other))
            {
                bool typeUsed = used.Contains("T:" + type.FullName);
                rows.Add(new Row { Kind = "type", Type = type.FullName, Member = "", Stable = Describe(type), Beta = "-", Used = typeUsed, Public = type.IsPublic || type.IsNestedPublic,
                    Status = renamed != null ? "covered: becomes " + renamed : "MISSING" });
                continue;
            }
            typesCompared++;
            if (type.IsEnum) { CompareEnum(type, other, used, rows); continue; }

            foreach (var field in type.Fields)
            {
                if (Generated(field.Name)) continue;
                membersCompared++;
                string key = "F:" + type.FullName + "::" + field.Name;
                var match = other.Fields.FirstOrDefault(f => f.Name == field.Name);
                var rule = TldLoaderCompatPolicy.ForField(type.FullName, field.Name);
                if (match != null && Same(field.FieldType, match.FieldType) && match.IsStatic == field.IsStatic) continue;
                var row = new Row { Kind = "field", Type = type.FullName, Member = field.Name, Stable = (field.IsStatic ? "static " : "") + field.FieldType.FullName,
                    Beta = match == null ? "-" : (match.IsStatic ? "static " : "") + match.FieldType.FullName, Used = used.Contains(key), Public = field.IsPublic };
                if (rule.Kind != TldLoaderCompatPolicy.RewriteKind.None) row.Status = "covered: " + rule.Kind + " " + rule.Name + (rule.SetName != null ? " / " + rule.SetName : "");
                else if (match == null)
                {
                    var caseOnly = other.Fields.FirstOrDefault(f => string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase));
                    row.Status = caseOnly != null ? "covered: case-only rename to " + caseOnly.Name : (Inherited(other, field.Name) ? "covered: now inherited" : "MISSING");
                }
                else if (match.IsStatic != field.IsStatic) row.Status = "STATIC CHANGED";
                else if (TldLoaderCompatPolicy.SameStackType(field.FieldType.FullName, match.FieldType.FullName)) row.Status = "covered: signedness retype";
                else if (CollectionShape(field.FieldType, match.FieldType) != null) row.Status = "covered: " + CollectionShape(field.FieldType, match.FieldType);
                else row.Status = "TYPE CHANGED";
                rows.Add(row);
            }

            foreach (var method in type.Methods)
            {
                if (Generated(method.Name) || method.Name.StartsWith("<")) continue;
                membersCompared++;
                string sig = Sig(method);
                string key = "M:" + type.FullName + "::" + sig;
                var exact = other.Methods.FirstOrDefault(m => m.Name == method.Name && Sig(m) == sig && m.IsStatic == method.IsStatic);
                if (exact != null && Same(exact.ReturnType, method.ReturnType)) continue;
                var sameName = other.Methods.Where(m => m.Name == method.Name).ToList();
                string helper = TldLoaderCompatPolicy.MethodHelper(type.FullName, method.Name, method.Parameters.Select(p => p.ParameterType.FullName).ToArray());
                var row = new Row { Kind = "method", Type = type.FullName, Member = sig, Stable = (method.IsStatic ? "static " : "") + method.ReturnType.FullName,
                    Beta = sameName.Count == 0 ? "-" : string.Join(" | ", sameName.Select(m => (m.IsStatic ? "static " : "") + m.ReturnType.Name + " " + Sig(m)).ToArray()),
                    Used = used.Contains(key), Public = method.IsPublic };
                if (helper != null) row.Status = "covered: helper " + helper;
                else if (exact != null) row.Status = TldLoaderCompatPolicy.SameStackType(method.ReturnType.FullName, exact.ReturnType.FullName) ? "covered: signedness retype" : "RETURN CHANGED";
                else if (sameName.Any(m => m.Parameters.Count == method.Parameters.Count && m.IsStatic == method.IsStatic &&
                    TldLoaderCompatPolicy.SameStackType(m.ReturnType.FullName, method.ReturnType.FullName) &&
                    m.Parameters.Select(p => p.ParameterType.FullName).Zip(method.Parameters.Select(p => p.ParameterType.FullName), TldLoaderCompatPolicy.SameStackType).All(x => x)))
                    row.Status = "covered: signedness retype";
                else if (InheritedMethod(other, method.Name, sig)) row.Status = "covered: now inherited";
                else if (sameName.Any(m => Adaptable(method, m))) row.Status = "covered: call adapted (beta defaults for added arguments, dropped result)";
                else row.Status = sameName.Count == 0 ? "MISSING" : "SIGNATURE CHANGED";
                rows.Add(row);
            }
        }

        Write(args[2], rows, typesCompared, membersCompared, args.Skip(3).Select(Path.GetFileName).ToArray());
        var open = rows.Where(r => !r.Status.StartsWith("covered")).ToList();
        Console.WriteLine("Stable vs beta: " + typesCompared + " types and " + membersCompared + " members compared; " + rows.Count + " differences, " + open.Count + " not covered (" +
            open.Count(r => r.Used) + " used by the given mods). Report: " + args[2]);
        return 0;
    }

    static void CompareEnum(TypeDefinition stable, TypeDefinition beta, HashSet<string> used, List<Row> rows)
    {
        var values = beta.Fields.Where(f => f.HasConstant).ToDictionary(f => f.Name, f => Convert.ToInt64(f.Constant));
        foreach (var field in stable.Fields.Where(f => f.HasConstant))
        {
            long stableValue = Convert.ToInt64(field.Constant), betaValue;
            bool has = values.TryGetValue(field.Name, out betaValue);
            if (has && betaValue == stableValue) continue;
            rows.Add(new Row { Kind = "enum", Type = stable.FullName, Member = field.Name, Stable = stableValue.ToString(), Beta = has ? betaValue.ToString() : "-",
                Used = used.Contains("E:" + stable.FullName), Public = true, Status = has ? "VALUE CHANGED" : "MISSING" });
        }
    }

    // Members of Assembly-CSharp that the given assemblies reference. Enum use is per type:
    // mods compile enum values into constants, so any use of the enum type counts.
    static HashSet<string> UsedMembers(IEnumerable<string> files)
    {
        var used = new HashSet<string>();
        foreach (string file in files)
        {
            ModuleDefinition module;
            try { module = ModuleDefinition.ReadModule(file); } catch (Exception) { continue; }
            foreach (var type in module.GetTypeReferences().Where(t => t.Scope != null && t.Scope.Name == "Assembly-CSharp"))
            {
                used.Add("T:" + type.FullName);
                used.Add("E:" + type.FullName);
            }
            foreach (var member in module.GetMemberReferences())
            {
                if (member.DeclaringType == null || member.DeclaringType.Scope == null) continue;
                var declaring = member.DeclaringType is GenericInstanceType ? ((GenericInstanceType)member.DeclaringType).ElementType : member.DeclaringType;
                if (declaring.Scope.Name != "Assembly-CSharp") continue;
                if (member is FieldReference) used.Add("F:" + declaring.FullName + "::" + member.Name);
                var method = member as MethodReference;
                if (method != null) used.Add("M:" + declaring.FullName + "::" + Sig(method));
            }
        }
        return used;
    }

    static string Sig(MethodReference method)
    {
        return method.Name + "(" + string.Join(",", method.Parameters.Select(p => Norm(p.ParameterType.FullName)).ToArray()) + ")";
    }

    // Parameter types compared through the policy's type renames.
    static string Norm(string fullName)
    {
        string bare = fullName.TrimEnd('&', '[', ']');
        string renamed = TldLoaderCompatPolicy.RenamedType(bare);
        return renamed == null ? fullName : renamed + fullName.Substring(bare.Length);
    }

    static bool Same(TypeReference a, TypeReference b) { return Norm(a.FullName) == Norm(b.FullName); }

    static string CollectionShape(TypeReference stable, TypeReference beta)
    {
        string sa = Element(stable, true), sl = Element(stable, false), ba = Element(beta, true), bl = Element(beta, false);
        var shape = TldLoaderCompatPolicy.Shape(sa, sl, ba, bl);
        return shape == TldLoaderCompatPolicy.CollectionShape.None ? null : "array/list conversion (" + shape + ")";
    }

    static string Element(TypeReference type, bool array)
    {
        if (array) { var a = type as ArrayType; return a == null || a.Rank != 1 ? null : Norm(a.ElementType.FullName); }
        var g = type as GenericInstanceType;
        return g == null || g.ElementType.FullName != "System.Collections.Generic.List`1" ? null : Norm(g.GenericArguments[0].FullName);
    }

    // Mirrors TldLoaderTranslator.Adapt.
    static bool Adaptable(MethodDefinition stable, MethodDefinition beta)
    {
        if (beta.HasThis != stable.HasThis || beta.HasGenericParameters || stable.HasGenericParameters || beta.Parameters.Count < stable.Parameters.Count) return false;
        for (int i = 0; i < stable.Parameters.Count; i++)
            if (!TldLoaderCompatPolicy.SameStackType(Norm(stable.Parameters[i].ParameterType.FullName), beta.Parameters[i].ParameterType.FullName)) return false;
        if (beta.Parameters.Skip(stable.Parameters.Count).Any(p => p.ParameterType.IsByReference || p.ParameterType.IsPointer)) return false;
        return TldLoaderCompatPolicy.ReturnAdaptable(Norm(stable.ReturnType.FullName), beta.ReturnType.FullName);
    }

    static bool Inherited(TypeDefinition type, string field)
    {
        for (var t = Base(type); t != null; t = Base(t)) if (t.Fields.Any(f => f.Name == field)) return true;
        return false;
    }

    static bool InheritedMethod(TypeDefinition type, string name, string sig)
    {
        for (var t = Base(type); t != null; t = Base(t)) if (t.Methods.Any(m => m.Name == name && Sig(m) == sig)) return true;
        return false;
    }

    static TypeDefinition Base(TypeDefinition type)
    {
        try { return type.BaseType == null || type.BaseType.Scope == null || type.BaseType.Scope.Name != type.Module.Assembly.Name.Name ? null : type.BaseType.Resolve(); }
        catch (Exception) { return null; }
    }

    static bool Generated(string name) { return name.Contains("<") || name.Contains("$"); }

    static string Describe(TypeDefinition type)
    {
        return (type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsInterface ? "interface" : "class") + ", " + type.Fields.Count + " fields, " + type.Methods.Count + " methods";
    }

    static void Write(string path, List<Row> rows, int types, int members, string[] mods)
    {
        var text = new StringBuilder();
        text.AppendLine("# Stable vs beta Assembly-CSharp");
        text.AppendLine();
        text.AppendLine(types + " types and " + members + " members compared. Mods checked for use: " + (mods.Length == 0 ? "none" : string.Join(", ", mods)) + ".");
        text.AppendLine();
        text.AppendLine("| Status | Count | Used by checked mods |");
        text.AppendLine("|---|---|---|");
        foreach (var group in rows.GroupBy(r => r.Status.StartsWith("covered") ? "covered" : r.Status).OrderBy(g => g.Key))
            text.AppendLine("| " + group.Key + " | " + group.Count() + " | " + group.Count(r => r.Used) + " |");
        foreach (var section in new[] { true, false })
        {
            text.AppendLine();
            text.AppendLine(section ? "## Not covered, used by the checked mods" : "## Not covered, not used by the checked mods (public only)");
            text.AppendLine();
            text.AppendLine("| Kind | Type | Member | Stable | Beta | Status |");
            text.AppendLine("|---|---|---|---|---|---|");
            foreach (var row in rows.Where(r => !r.Status.StartsWith("covered") && r.Used == section && (section || r.Public)).OrderBy(r => r.Type).ThenBy(r => r.Kind).ThenBy(r => r.Member))
                text.AppendLine("| " + row.Kind + " | " + row.Type + " | " + Escape(row.Member) + " | " + Escape(row.Stable) + " | " + Escape(row.Beta) + " | " + row.Status + " |");
        }
        text.AppendLine();
        text.AppendLine("## Covered");
        text.AppendLine();
        foreach (var row in rows.Where(r => r.Status.StartsWith("covered")).OrderBy(r => r.Type))
            text.AppendLine("- " + row.Kind + " " + row.Type + (row.Member.Length > 0 ? "::" + row.Member : "") + ": " + row.Status + (row.Used ? " (used)" : ""));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, text.ToString());
    }

    static string Escape(string text) { return text.Replace("|", "\\|"); }
}
