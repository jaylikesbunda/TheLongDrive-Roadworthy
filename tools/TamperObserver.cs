using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Negative-control helper: remove one diagnostic exit observer from a copy of
// the candidate v3. The real verifier must reject the tampered copy. This tool
// never writes to its input.
public static class TamperObserver
{
    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 7) throw new ArgumentException("Usage: TamperObserver <input.dll> <output.dll> <typeFullName> <methodName> <parameterCount> <hookName> <managedDir>");
            string output = System.IO.Path.GetFullPath(args[1]);
            if (System.IO.File.Exists(output)) throw new InvalidOperationException("Refusing to overwrite: " + output);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(System.IO.Path.GetFullPath(args[6]));
            resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0])));
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver }))
            {
                var type = AllTypes(assembly.MainModule.Types).Single(t => t.FullName == args[2]);
                int parameterCount = Int32.Parse(args[4]);
                var method = type.Methods.Single(m => m.Name == args[3] && m.Parameters.Count == parameterCount);
                Instruction call = method.Body.Instructions.FirstOrDefault(i =>
                    i.OpCode == OpCodes.Call && i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name == args[5] &&
                    ((MethodReference)i.Operand).DeclaringType.FullName == "TldMp.StableGameBridge");
                if (call == null) throw new InvalidOperationException("Observer call not found: " + args[5]);
                method.Body.Instructions.Remove(call);
                assembly.Write(output);
            }
            Console.WriteLine("Tampered copy written: " + output);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
