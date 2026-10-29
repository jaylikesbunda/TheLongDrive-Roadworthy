using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class VerifyPatch
{
    public static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var child in AllTypes(type.NestedTypes)) yield return child;
        }
    }
    public static string Fingerprint(MethodDefinition method)
    {
        var text = new StringBuilder();
        text.Append(method.FullName).Append('|').Append(method.Attributes).Append('|').Append(method.ImplAttributes);
        if (!method.HasBody) return text.ToString();
        var body = method.Body;
        text.Append('|').Append(body.InitLocals);
        foreach (var v in body.Variables) text.Append('|').Append(v.VariableType.FullName);
        Func<Instruction, int> index = i => i == null ? -1 : body.Instructions.IndexOf(i);
        foreach (var i in body.Instructions)
        {
            string opcode = i.OpCode.Name;
            if (i.Operand is Instruction && opcode.EndsWith(".s")) opcode = opcode.Substring(0, opcode.Length - 2);
            text.Append('\n').Append(opcode).Append(' ');
            var operand = i.Operand;
            if (operand is Instruction) text.Append(index((Instruction)operand));
            else if (operand is Instruction[]) text.Append(String.Join(",", ((Instruction[])operand).Select(x => index(x).ToString())));
            else if (operand is VariableDefinition) text.Append("local:").Append(((VariableDefinition)operand).Index);
            else if (operand is ParameterDefinition) text.Append("arg:").Append(((ParameterDefinition)operand).Index);
            else if (operand is MemberReference) text.Append(((MemberReference)operand).FullName);
            else if (operand is float) text.Append("f:").Append(BitConverter.ToInt32(BitConverter.GetBytes((float)operand),0).ToString("X8",CultureInfo.InvariantCulture));
            else if (operand is double) text.Append("d:").Append(BitConverter.DoubleToInt64Bits((double)operand).ToString("X16",CultureInfo.InvariantCulture));
            else if (operand is int) text.Append("i:").Append(((int)operand).ToString(CultureInfo.InvariantCulture));
            else if (operand is long) text.Append("l:").Append(((long)operand).ToString(CultureInfo.InvariantCulture));
            else if (operand is sbyte) text.Append("sb:").Append(((sbyte)operand).ToString(CultureInfo.InvariantCulture));
            else if (operand is byte) text.Append("b:").Append(((byte)operand).ToString(CultureInfo.InvariantCulture));
            else if (operand is string) text.Append("s:").Append((string)operand);
            else text.Append(operand);
        }
        foreach (var h in body.ExceptionHandlers)
            text.Append('|').Append(h.HandlerType).Append(':').Append(index(h.TryStart)).Append(':').Append(index(h.TryEnd))
                .Append(':').Append(index(h.HandlerStart)).Append(':').Append(index(h.HandlerEnd)).Append(':').Append(index(h.FilterStart))
                .Append(':').Append(h.CatchType == null ? "" : h.CatchType.FullName);
        return text.ToString();
    }
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new Exception("Usage: VerifyPatch ORIGINAL PATCHED");
            using (var original = AssemblyDefinition.ReadAssembly(args[0]))
            using (var patched = AssemblyDefinition.ReadAssembly(args[1]))
            {
                var om = original.MainModule;
                var pm = patched.MainModule;
                if (!om.AssemblyReferences.Select(r => r.FullName).SequenceEqual(pm.AssemblyReferences.Select(r => r.FullName)))
                    throw new Exception("Assembly dependencies changed. Added: " + String.Join("; ", pm.AssemblyReferences.Select(r => r.FullName).Except(om.AssemblyReferences.Select(r => r.FullName))) + "; removed: " + String.Join("; ", om.AssemblyReferences.Select(r => r.FullName).Except(pm.AssemblyReferences.Select(r => r.FullName))));
                var ot = AllTypes(om.Types).ToDictionary(t => t.FullName);
                var pt = AllTypes(pm.Types).ToDictionary(t => t.FullName);
                if (!ot.Keys.OrderBy(x => x).SequenceEqual(pt.Keys.OrderBy(x => x))) throw new Exception("Type layout changed.");
                string[] helpers = { "TldMp_GetOrAddBody_v1", "TldMp_LogSeatExit_v1", "TldMp_DisposeCallbacks_v1" };
                int unchanged = 0, changed = 0, additions = 0;
                foreach (var entry in ot)
                {
                    var a = entry.Value;
                    var b = pt[entry.Key];
                    if (!a.Fields.Select(f => f.FullName).SequenceEqual(b.Fields.Select(f => f.FullName))) throw new Exception("Fields changed: " + a.FullName);
                    var methods = a.Methods.ToDictionary(m => m.FullName);
                    foreach (var bm in b.Methods)
                    {
                        MethodDefinition am;
                        if (!methods.TryGetValue(bm.FullName, out am))
                        {
                            if (!helpers.Contains(bm.Name)) throw new Exception("Unexpected method: " + bm.FullName);
                            additions++;
                            continue;
                        }
                        if (Fingerprint(am) == Fingerprint(bm)) { unchanged++; continue; }
                        changed++;
                        if (a.FullName == "jointHelpScript" && am.Name == "UnFreezeStart")
                        {
                            var call = bm.Body.Instructions.Single(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == helpers[0]);
                            var old = am.Body.Instructions.Single(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "AddComponent");
                            call.OpCode = old.OpCode;
                            call.Operand = old.Operand;
                        }
                        else if (a.FullName == "fpscontroller" && am.Name == "GetOut")
                        {
                            var call = bm.Body.Instructions.Single(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == helpers[1]);
                            if (call.Previous.OpCode != OpCodes.Ldarg || ((ParameterDefinition)call.Previous.Operand).Index != 1)
                                throw new Exception("Wrong seat diagnostic argument.");
                            bm.Body.Instructions.Remove(call.Previous);
                            bm.Body.Instructions.Remove(call);
                        }
                        else if (a.FullName == "GeneszSteamP2PBase.SteamP2PNetworkingUnderTheHoodScript" && am.Name == "Init")
                        {
                            if (bm.Body.Instructions[0].OpCode != OpCodes.Ldarg_0 ||
                                ((MethodReference)bm.Body.Instructions[1].Operand).Name != helpers[2])
                                throw new Exception("Unexpected callback cleanup hook.");
                            bm.Body.Instructions.RemoveAt(0);
                            bm.Body.Instructions.RemoveAt(0);
                        }
                        else throw new Exception("Unexpected modification: " + bm.FullName);
                        if (Fingerprint(am) != Fingerprint(bm)) throw new Exception("Additional changes detected: " + bm.FullName);
                    }
                    if (b.Methods.Count != a.Methods.Count + b.Methods.Count(m => helpers.Contains(m.Name))) throw new Exception("Method count mismatch.");
                }
                if (changed != 3 || additions != 3) throw new Exception("Unexpected patch scope.");
                Console.WriteLine("PASS: " + unchanged + " methods unchanged; exactly 3 original methods hooked and 3 helpers added. Dependencies and fields unchanged.");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
