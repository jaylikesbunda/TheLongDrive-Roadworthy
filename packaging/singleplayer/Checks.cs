using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Shared, patch-agnostic IL utilities used by both PatchV4 and VerifyV4.
public static class Checks
{
    // Expected input == the installed multiplayer assembly. Defaults to the build we
    // verified against; Install-All.ps1 overrides it from tldmp-v3-receipt.json so a
    // multiplayer update does not require rebuilding this patch.
    public static string V3Hash = "ADA62FD7C60481E6792393266E9CD31F7FE723366D216DAE575C722F8444A9EE";

    public static void SetBaseline(string hash)
    {
        if (hash == null) throw new ArgumentNullException("hash");
        hash = hash.Trim().ToUpperInvariant();
        if (hash.Length != 64) throw new ArgumentException("Baseline hash must be 64 hex characters.");
        for (int i = 0; i < hash.Length; i++)
        {
            char c = hash[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
            if (!hex) throw new ArgumentException("Baseline hash contains a non-hex character.");
        }
        V3Hash = hash;
    }

    public static string Hash(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (System.IO.FileStream stream = System.IO.File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    public static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition child in AllTypes(type.NestedTypes)) yield return child;
        }
    }

    public static string FieldShape(IEnumerable<FieldDefinition> fields)
    {
        StringBuilder text = new StringBuilder();
        foreach (FieldDefinition f in fields)
            text.Append(f.Name).Append(':').Append(f.FieldType.FullName).Append(':').Append(f.Attributes)
                .Append(':').Append(f.HasConstant ? Convert.ToString(f.Constant) : "").Append('|');
        return text.ToString();
    }

    // Instructions, locals, exception handlers, flags and max stack -- everything that
    // decides the emitted IL bytes. Method name/attributes are checked separately.
    public static string BodyFingerprint(MethodDefinition method)
    {
        MethodBody body = method.Body;
        StringBuilder text = new StringBuilder();
        text.Append("attrs=").Append(method.Attributes).Append(";impl=").Append(method.ImplAttributes);
        text.Append(";ret=").Append(method.ReturnType.FullName);
        text.Append(";params=");
        foreach (ParameterDefinition p in method.Parameters) text.Append(p.Name).Append(':').Append(p.ParameterType.FullName).Append(',');
        text.Append(";gparams=");
        foreach (GenericParameter g in method.GenericParameters) text.Append(g.Name).Append(',');
        if (body == null) return text.ToString();
        text.Append(";init=").Append(body.InitLocals);
        foreach (VariableDefinition v in body.Variables) text.Append(";local:").Append(v.VariableType.FullName);
        foreach (Instruction i in body.Instructions)
        {
            text.Append('\n').Append(i.OpCode.Name).Append(' ');
            object operand = i.Operand;
            if (operand is Instruction) text.Append("->").Append(Index(body, (Instruction)operand));
            else if (operand is Instruction[]) text.Append("->").Append(String.Join(",", ((Instruction[])operand).Select(x => Index(body, x).ToString()).ToArray()));
            else if (operand is VariableDefinition) text.Append("v").Append(((VariableDefinition)operand).Index);
            else if (operand is ParameterDefinition) text.Append("a").Append(((ParameterDefinition)operand).Index);
            else if (operand is MethodReference) text.Append(((MethodReference)operand).FullName);
            else if (operand is FieldReference) text.Append(((FieldReference)operand).FullName);
            else if (operand is TypeReference) text.Append(((TypeReference)operand).FullName);
            else if (operand != null) text.Append(operand.GetType().Name).Append(':').Append(operand);
        }
        foreach (ExceptionHandler h in body.ExceptionHandlers)
            text.Append(";eh:").Append(h.HandlerType).Append(':').Append(Index(body, h.TryStart)).Append(':').Append(Index(body, h.TryEnd))
                .Append(':').Append(Index(body, h.HandlerStart)).Append(':').Append(Index(body, h.HandlerEnd))
                .Append(':').Append(Index(body, h.FilterStart)).Append(':').Append(h.CatchType == null ? "" : h.CatchType.FullName);
        return text.ToString();
    }

    static int Index(MethodBody body, Instruction instruction)
    {
        if (instruction == null) return -1;
        return body.Instructions.IndexOf(instruction);
    }

    public static string Fingerprint(MethodDefinition method)
    {
        return method.FullName + "|" + BodyFingerprint(method);
    }

    public static void ExpandBranches(MethodDefinition method)
    {
        foreach (Instruction i in method.Body.Instructions)
        {
            if (i.OpCode.OperandType != OperandType.ShortInlineBrTarget) continue;
            string name = i.OpCode.Code.ToString();
            name = name.Substring(0, name.Length - 2);
            System.Reflection.FieldInfo field = typeof(OpCodes).GetField(name);
            if (field == null) throw new InvalidOperationException("No long-form branch for " + i.OpCode.Code + " in " + method.FullName);
            i.OpCode = (OpCode)field.GetValue(null);
        }
    }

    static IEnumerable<Instruction> Successors(Instruction instruction)
    {
        if (instruction.Operand is Instruction) yield return (Instruction)instruction.Operand;
        else if (instruction.Operand is Instruction[])
            foreach (Instruction target in (Instruction[])instruction.Operand) yield return target;
        FlowControl flow = instruction.OpCode.FlowControl;
        if (flow != FlowControl.Branch && flow != FlowControl.Return && flow != FlowControl.Throw &&
            instruction.Next != null) yield return instruction.Next;
    }

    // Real control-flow reachability. Instruction-count checks let dead blocks pass.
    public static HashSet<Instruction> Reachable(MethodDefinition method)
    {
        var reachable = new HashSet<Instruction>();
        if (method.Body == null || method.Body.Instructions.Count == 0) return reachable;
        var pending = new Stack<Instruction>();
        pending.Push(method.Body.Instructions[0]);
        while (pending.Count != 0)
        {
            Instruction instruction = pending.Pop();
            if (instruction == null || !reachable.Add(instruction)) continue;
            foreach (Instruction next in Successors(instruction)) pending.Push(next);
        }
        return reachable;
    }

    public static Dictionary<Instruction, List<Instruction>> Predecessors(MethodDefinition method)
    {
        var result = new Dictionary<Instruction, List<Instruction>>();
        foreach (Instruction instruction in method.Body.Instructions) result[instruction] = new List<Instruction>();
        foreach (Instruction instruction in method.Body.Instructions)
            foreach (Instruction next in Successors(instruction))
                if (next != null && result.ContainsKey(next)) result[next].Add(instruction);
        return result;
    }

    // Conservative maximum evaluation-stack depth. Used to set MaxStackSize on edited
    // methods (Cecil never recomputes it).
    public static int ComputeMaxStack(MethodDefinition method)
    {
        int depth = 0, max = 0;
        foreach (Instruction i in method.Body.Instructions)
        {
            if (i.OpCode.StackBehaviourPop == StackBehaviour.PopAll)
            {
                depth = 0;
                continue;
            }
            depth -= Pop(i);
            if (depth < 0) depth = 0;
            depth += Push(i);
            if (depth > max) max = depth;
            if (i.OpCode == OpCodes.Ret) depth = 0;
        }
        return max;
    }

    static int Pop(Instruction i)
    {
        switch (i.OpCode.StackBehaviourPop)
        {
            case StackBehaviour.Pop0: return 0;
            case StackBehaviour.Pop1:
            case StackBehaviour.Popi:
            case StackBehaviour.Popref: return 1;
            case StackBehaviour.Pop1_pop1:
            case StackBehaviour.Popi_pop1:
            case StackBehaviour.Popi_popi:
            case StackBehaviour.Popi_popi8:
            case StackBehaviour.Popi_popr4:
            case StackBehaviour.Popi_popr8:
            case StackBehaviour.Popref_pop1:
            case StackBehaviour.Popref_popi: return 2;
            case StackBehaviour.Popi_popi_popi:
            case StackBehaviour.Popref_popi_popi:
            case StackBehaviour.Popref_popi_popi8:
            case StackBehaviour.Popref_popi_popr4:
            case StackBehaviour.Popref_popi_popr8:
            case StackBehaviour.Popref_popi_popref: return 3;
            case StackBehaviour.Varpop:
                if (i.OpCode == OpCodes.Ret) return 0;
                MethodReference callee = i.Operand as MethodReference;
                if (callee != null)
                {
                    int count = callee.Parameters.Count;
                    if (callee.HasThis && i.OpCode != OpCodes.Newobj) count++;
                    return count;
                }
                CallSite site = i.Operand as CallSite;
                if (site != null)
                {
                    int count = site.Parameters.Count + 1;
                    if (site.HasThis) count++;
                    return count;
                }
                throw new InvalidOperationException("Unhandled varpop at " + i.OpCode);
            default: throw new InvalidOperationException("Unhandled stack behaviour " + i.OpCode.StackBehaviourPop + " for " + i.OpCode);
        }
    }

    static int Push(Instruction i)
    {
        switch (i.OpCode.StackBehaviourPush)
        {
            case StackBehaviour.Push0: return 0;
            case StackBehaviour.Push1:
            case StackBehaviour.Pushi:
            case StackBehaviour.Pushi8:
            case StackBehaviour.Pushr4:
            case StackBehaviour.Pushr8:
            case StackBehaviour.Pushref: return 1;
            case StackBehaviour.Push1_push1: return 2;
            case StackBehaviour.Varpush:
                MethodReference callee = i.Operand as MethodReference;
                if (callee != null) return callee.ReturnType.FullName == "System.Void" ? 0 : 1;
                CallSite site = i.Operand as CallSite;
                if (site != null) return site.ReturnType.FullName == "System.Void" ? 0 : 1;
                throw new InvalidOperationException("Unhandled varpush at " + i.OpCode);
            default: throw new InvalidOperationException("Unhandled stack behaviour " + i.OpCode.StackBehaviourPush + " for " + i.OpCode);
        }
    }

    public static MethodDefinition ExactlyOne(TypeDefinition type, string name, int parameterCount, string firstParameter)
    {
        MethodDefinition[] found = type.Methods.Where(m => m.Name == name && m.Parameters.Count == parameterCount &&
            (firstParameter == null || m.Parameters[0].ParameterType.FullName == firstParameter)).ToArray();
        if (found.Length != 1)
            throw new InvalidOperationException("Expected exactly one " + type.FullName + "." + name + "/" + parameterCount +
                " (" + firstParameter + "), found " + found.Length);
        return found[0];
    }

    public static TypeDefinition ExactlyOneType(ModuleDefinition module, string fullName)
    {
        TypeDefinition[] found = AllTypes(module.Types).Where(t => t.FullName == fullName).ToArray();
        if (found.Length != 1) throw new InvalidOperationException("Expected exactly one type " + fullName + ", found " + found.Length);
        return found[0];
    }

    // Build an instruction for any opcode/operand pair, including the null placeholders
    // used for branch targets that are patched up after the whole list exists.
    public static Instruction MakeInstruction(OpCode opcode, object operand)
    {
        switch (opcode.OperandType)
        {
            case OperandType.InlineNone: return Instruction.Create(opcode);
            case OperandType.ShortInlineI: return Instruction.Create(opcode, (sbyte)Convert.ToInt32(operand, CultureInfo.InvariantCulture));
            case OperandType.InlineI: return Instruction.Create(opcode, Convert.ToInt32(operand, CultureInfo.InvariantCulture));
            case OperandType.InlineI8: return Instruction.Create(opcode, Convert.ToInt64(operand, CultureInfo.InvariantCulture));
            case OperandType.ShortInlineR: return Instruction.Create(opcode, Convert.ToSingle(operand, CultureInfo.InvariantCulture));
            case OperandType.InlineR: return Instruction.Create(opcode, Convert.ToDouble(operand, CultureInfo.InvariantCulture));
            case OperandType.InlineString: return Instruction.Create(opcode, (string)operand);
            case OperandType.ShortInlineBrTarget:
            case OperandType.InlineBrTarget:
                return Instruction.Create(opcode, operand == null ? Instruction.Create(OpCodes.Nop) : (Instruction)operand);
            case OperandType.InlineSwitch: return Instruction.Create(opcode, operand == null ? new Instruction[0] : (Instruction[])operand);
            case OperandType.ShortInlineVar:
            case OperandType.InlineVar:
                if (operand is ParameterDefinition) return Instruction.Create(opcode, (ParameterDefinition)operand);
                return Instruction.Create(opcode, (VariableDefinition)operand);
            case OperandType.ShortInlineArg:
            case OperandType.InlineArg:
                return Instruction.Create(opcode, (ParameterDefinition)operand);
            case OperandType.InlineMethod:
            case OperandType.InlineTok:
                return Instruction.Create(opcode, (MethodReference)operand);
            case OperandType.InlineField: return Instruction.Create(opcode, (FieldReference)operand);
            case OperandType.InlineType: return Instruction.Create(opcode, (TypeReference)operand);
            case OperandType.InlineSig: return Instruction.Create(opcode, (CallSite)operand);
            default: throw new InvalidOperationException("Unhandled operand type " + opcode.OperandType + " for " + opcode);
        }
    }

    public static MethodReference PhysicsCheck(ModuleDefinition module, string name, MethodReference overlap)
    {
        // Build UnityEngine.Physics::Check*(...) on the *existing* UnityEngine.Physics
        // type reference so no new assembly reference is introduced.
        MethodReference check = new MethodReference(name, module.TypeSystem.Boolean, overlap.DeclaringType);
        check.HasThis = false;
        check.ExplicitThis = overlap.ExplicitThis;
        check.CallingConvention = overlap.CallingConvention;
        foreach (ParameterDefinition p in overlap.Parameters)
            check.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
        return check;
    }
}
