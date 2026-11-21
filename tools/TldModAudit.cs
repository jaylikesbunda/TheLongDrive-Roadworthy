using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using TldMp;

// For each TLDLoader mod: every game (Assembly-CSharp) member it uses, as the stable branch names
// it, and what the translator makes of it on the beta: unchanged (exists as is), mapped (a rule,
// helper, adapter or rename), stood in (a removed member: per-object value or do-nothing stub) or
// missing (left as is; that mod method fails). The totals across mods rank what to cover next.
// args: <Managed dir> <beta Assembly-CSharp> <stable Assembly-CSharp> <report.md> <mod dll>...
internal static class TldModAudit
{
    enum Outcome { Unchanged, Mapped, StoodIn, Missing, Ignored }

    public static int Main(string[] args)
    {
        string managed = args[0], betaPath = args[1], reportPath = args[3];
        var beta = ModuleDefinition.ReadModule(betaPath);
        var betaTypes = beta.GetTypes().GroupBy(t => t.FullName).ToDictionary(g => g.Key, g => g.First());
        var betaReads = ReadFields(beta);
        using (var stable = ModuleDefinition.ReadModule(args[2]))
            return Audit(args.Skip(4).ToArray(), managed, betaPath, reportPath, betaTypes, betaReads, ReadFields(stable),
                new HashSet<string>(stable.GetTypes().SelectMany(t => t.Fields).Select(f => f.DeclaringType.FullName + "::" + f.Name), StringComparer.Ordinal));
    }

    static int Audit(string[] files, string managed, string betaPath, string reportPath,
        Dictionary<string, TypeDefinition> betaTypes, HashSet<string> betaReads, HashSet<string> stableReads, HashSet<string> stableFields)
    {
        var translator = new TldLoaderTranslator(betaPath, new[] { managed }, "Tld.Multiplayer", new Version(0, 0, 0, 0));
        var gapUsers = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var unchangedWrites = new SortedSet<string>(StringComparer.Ordinal);
        var text = new StringBuilder();
        text.AppendLine("# TLDLoader mod audit (stable-built mods on the beta)");
        text.AppendLine();
        text.AppendLine("| Mod | Game members used | Unchanged | Mapped | Stood in | Missing | Written but ignored by the beta | Harmony left out |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        var details = new StringBuilder();
        foreach (string file in files)
        {
            ModuleDefinition module;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); module = ModuleDefinition.ReadModule(new MemoryStream(bytes)); }
            catch (Exception e) { details.AppendLine("- " + Path.GetFileName(file) + ": not readable (" + e.Message + ")"); continue; }
            if (!module.AssemblyReferences.Any(r => r.Name == "Assembly-CSharp")) { details.AppendLine("- " + Path.GetFileName(file) + ": uses no game code"); continue; }
            TldLoaderTranslator.Report report;
            try { report = translator.TranslateMod(bytes); }
            catch (Exception e) { details.AppendLine("- " + Path.GetFileName(file) + ": translation failed (" + e.GetType().Name + ": " + e.Message + ")"); continue; }
            var used = Used(module);
            var counts = new Dictionary<Outcome, int> { { Outcome.Unchanged, 0 }, { Outcome.Mapped, 0 }, { Outcome.StoodIn, 0 }, { Outcome.Missing, 0 }, { Outcome.Ignored, 0 } };
            var written = Written(module);
            HashSet<string> translatedWrites;
            using (var translated = ModuleDefinition.ReadModule(new MemoryStream(report.Bytes)))
                translatedWrites = Written(translated);
            var gaps = new List<string>();
            foreach (var member in used)
            {
                var outcome = Classify(member, report, betaTypes);
                if (outcome == Outcome.Unchanged && member.IsField && written.Contains(member.Display) && !betaReads.Contains(member.Display))
                {
                    if (!translatedWrites.Contains(member.Display))
                        outcome = Outcome.Mapped;
                    else if (stableFields.Contains(member.Display) && !stableReads.Contains(member.Display))
                        unchangedWrites.Add(member.Display + " (" + Path.GetFileName(file) + ")");
                    else
                        outcome = Outcome.Ignored;
                }
                // A helper field with a getter but no setter keeps the mod's store in the mod
                // and the game never sees it. Report it instead of calling it mapped.
                if (outcome == Outcome.Mapped && member.IsField && written.Contains(member.Display))
                {
                    var rule = TldLoaderCompatPolicy.ForField(member.Type, member.Name);
                    if (rule.Kind == TldLoaderCompatPolicy.RewriteKind.CallHelper && rule.SetName == null &&
                        translatedWrites.Contains(member.Display)) outcome = Outcome.Ignored;
                }
                counts[outcome]++;
                if (outcome == Outcome.StoodIn || outcome == Outcome.Missing || outcome == Outcome.Ignored)
                {
                    string key = (outcome == Outcome.Missing ? "missing " : outcome == Outcome.Ignored ? "written, never read by the beta " : "stood in ") + member.Display;
                    gaps.Add(key);
                    SortedSet<string> users;
                    if (!gapUsers.TryGetValue(key, out users)) gapUsers[key] = users = new SortedSet<string>(StringComparer.Ordinal);
                    users.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            text.AppendLine("| " + Path.GetFileName(file) + " | " + used.Count + " | " + counts[Outcome.Unchanged] + " | " + counts[Outcome.Mapped] + " | " + counts[Outcome.StoodIn] + " | " +
                counts[Outcome.Missing] + " | " + counts[Outcome.Ignored] + " | " + report.StrippedPatches.Count + " |");
            details.AppendLine("### " + Path.GetFileName(file));
            details.AppendLine();
            if (gaps.Count == 0) details.AppendLine("Everything it uses exists on the beta or is mapped.");
            foreach (string gap in gaps.OrderBy(g => g)) details.AppendLine("- " + gap);
            foreach (string patch in report.StrippedPatches) details.AppendLine("- Harmony patch left out: " + patch);
            foreach (string patch in report.ObsoletePatches) details.AppendLine("- Harmony patch obsolete: " + patch);
            foreach (string pinned in report.PinnedPatches) details.AppendLine("- Harmony patch pinned: " + pinned);
            details.AppendLine();
        }
        text.AppendLine();
        text.AppendLine("## Gaps across mods (most used first)");
        text.AppendLine();
        foreach (var pair in gapUsers.OrderByDescending(p => p.Value.Count).ThenBy(p => p.Key))
            text.AppendLine("- " + pair.Key + " (" + pair.Value.Count + "): " + string.Join(", ", pair.Value.ToArray()));
        text.AppendLine();
        text.AppendLine("## Mod writes with no direct game-code reads in either build");
        text.AppendLine();
        text.AppendLine("These fields still exist. The archived stable and beta game assemblies have no direct IL reads, so the audit does not count them as new regressions. Mod reads and reflection can still use them.");
        text.AppendLine();
        foreach (var write in unchangedWrites) text.AppendLine("- " + write);
        text.AppendLine();
        text.AppendLine("## Per mod");
        text.AppendLine();
        text.Append(details);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
        File.WriteAllText(reportPath, text.ToString());
        Console.WriteLine("Audited " + files.Length + " mod file(s); " + gapUsers.Count + " distinct gaps. Report: " + reportPath);
        return 0;
    }

    static HashSet<string> ReadFields(ModuleDefinition module)
    {
        return new HashSet<string>(
            module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Where(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldfld || i.OpCode.Code == Mono.Cecil.Cil.Code.Ldsfld ||
                    i.OpCode.Code == Mono.Cecil.Cil.Code.Ldflda || i.OpCode.Code == Mono.Cecil.Cil.Code.Ldsflda)
                .Select(i => i.Operand as FieldReference).Where(f => f != null)
                .Select(f => f.DeclaringType.FullName + "::" + f.Name), StringComparer.Ordinal);
    }

    sealed class Member { public string Display, Type, Name; public bool IsField; public string[] Parameters; public bool HasThis; }

    static List<Member> Used(ModuleDefinition module)
    {
        var result = new Dictionary<string, Member>(StringComparer.Ordinal);
        foreach (var reference in module.GetMemberReferences())
        {
            var declaring = reference.DeclaringType is GenericInstanceType ? ((GenericInstanceType)reference.DeclaringType).ElementType : reference.DeclaringType;
            if (declaring == null || declaring.Scope == null || declaring.Scope.Name != "Assembly-CSharp") continue;
            var field = reference as FieldReference;
            var method = reference as MethodReference;
            var member = new Member { Type = declaring.FullName, Name = reference.Name, IsField = field != null };
            if (method != null)
            {
                member.Parameters = method.Parameters.Select(p => p.ParameterType.FullName).ToArray();
                member.HasThis = method.HasThis;
                member.Display = declaring.FullName + "::" + method.Name + "(" + string.Join(",", member.Parameters) + ")";
            }
            else member.Display = declaring.FullName + "::" + reference.Name;
            result[member.Display] = member;
        }
        return result.Values.ToList();
    }

    static HashSet<string> Written(ModuleDefinition module)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in module.GetTypes())
            foreach (var m in t.Methods.Where(x => x.HasBody))
                foreach (var i in m.Body.Instructions)
                {
                    var f = i.Operand as FieldReference;
                    if (f == null || f.DeclaringType.Scope == null || f.DeclaringType.Scope.Name != "Assembly-CSharp") continue;
                    if (i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld || i.OpCode.Code == Mono.Cecil.Cil.Code.Stsfld) result.Add(f.DeclaringType.FullName + "::" + f.Name);
                }
        return result;
    }

    static Outcome Classify(Member member, TldLoaderTranslator.Report report, Dictionary<string, TypeDefinition> betaTypes)
    {
        string bare = member.Type + "::" + member.Name;
        string stableType = member.Type;
        string renamedType = RenamedTypeName(stableType);
        bool typeRenamed = renamedType != stableType;
        bool shell = ShellType(stableType);
        string renamedBare = renamedType + "::" + member.Name;
        string renamedDisplay = member.IsField ? renamedBare :
            renamedType + "::" + member.Name + "(" + string.Join(",", member.Parameters) + ")";

        // The translator records a stand-in under the stable type (these are the names a mod
        // sees), but a rule may have already retargeted the reference. Check both.
        if (HasKey(report.Degraded, bare) || HasKey(report.Degraded, member.Display) ||
            HasKey(report.Degraded, renamedBare) || HasKey(report.Degraded, renamedDisplay)) return Outcome.StoodIn;
        if (Missing(report, bare) || Missing(report, member.Display) ||
            Missing(report, renamedBare) || Missing(report, renamedDisplay)) return Outcome.Missing;

        TypeDefinition type;
        if (!betaTypes.TryGetValue(renamedType, out type))
        {
            // Deleted stable types with a runtime stand-in keep their fields and calls in the
            // mod, but nothing on the game side consumes them.
            if (shell) return Outcome.StoodIn;
            var missingRule = member.IsField
                ? TldLoaderCompatPolicy.ForField(stableType, member.Name)
                : default(TldLoaderCompatPolicy.FieldRewrite);
            if (typeRenamed && !member.IsField && TldLoaderCompatPolicy.MethodHelper(stableType, member.Name, member.Parameters) != null)
                return Outcome.Mapped;
            if (member.IsField && missingRule.Kind != TldLoaderCompatPolicy.RewriteKind.None) return Outcome.Mapped;
            return Outcome.Missing;
        }

        if (member.IsField)
        {
            var exactField = type.Fields.FirstOrDefault(f => f.Name == member.Name);
            if (exactField != null) return typeRenamed ? Outcome.Mapped : Outcome.Unchanged;
            // A unique case-only rename is retyped by the translator (physlocks -> physLocks).
            var caseMatches = type.Fields.Where(f => string.Equals(f.Name, member.Name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (caseMatches.Length == 1) return typeRenamed || caseMatches[0].Name != member.Name ? Outcome.Mapped : Outcome.Unchanged;
            var rule = TldLoaderCompatPolicy.ForField(stableType, member.Name);
            return rule.Kind == TldLoaderCompatPolicy.RewriteKind.None ? Outcome.Missing : Outcome.Mapped;
        }

        bool exact = type.Methods.Any(m => m.Name == member.Name && m.Parameters.Count == member.Parameters.Length &&
            m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(member.Parameters));
        if (exact) return typeRenamed ? Outcome.Mapped : Outcome.Unchanged;
        // Same name but a different signature/body is adapted or routed to a helper.
        if (type.Methods.Any(m => m.Name == member.Name) ||
            TldLoaderCompatPolicy.MethodHelper(stableType, member.Name, member.Parameters) != null) return Outcome.Mapped;
        return Outcome.Missing;
    }

    static string RenamedTypeName(string type)
    {
        var renamed = TldLoaderCompatPolicy.RenamedType(type);
        return renamed == null ? type : TldLoaderCompatPolicy.WithoutAssembly(renamed);
    }

    static bool ShellType(string type)
    {
        var renamed = TldLoaderCompatPolicy.RenamedType(type);
        return renamed != null && TldLoaderCompatPolicy.RenamedToRuntime(renamed);
    }

    static bool HasKey(System.Collections.Generic.IEnumerable<string> keys, string key) { return keys != null && keys.Contains(key); }

    static bool Missing(TldLoaderTranslator.Report report, string key)
    {
        return report.Missing.Keys.Any(k => k == key || k.StartsWith(key + "(", StringComparison.Ordinal));
    }

}
