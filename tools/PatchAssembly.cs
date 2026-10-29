using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Generates a local experimental assembly. Never edits its input.
public static class PatchAssembly
{
    public const string OriginalHash = "CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000";
    const string BodyHelper = "TldMp_GetOrAddBody_v1";
    const string ExitHelper = "TldMp_LogSeatExit_v1";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
                throw new ArgumentException("Usage: PatchAssembly.exe INPUT OUTPUT");
            string input = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            if (String.Equals(input, output, StringComparison.OrdinalIgnoreCase) || File.Exists(output))
                throw new InvalidOperationException("Output must be a new file distinct from the input.");
            if (Hash(input) != OriginalHash)
                throw new InvalidOperationException("Unsupported game assembly hash. Refusing to patch.");
            using (var resolver = new DefaultAssemblyResolver())
            {
                resolver.AddSearchDirectory(Path.GetDirectoryName(input));
                using (var assembly = AssemblyDefinition.ReadAssembly(input,
                    new ReaderParameters { AssemblyResolver = resolver, InMemory = true }))
                {
                    Patch(assembly.MainModule);
                    assembly.Write(output);
                }
            }
            Console.WriteLine("Built experimental patch: " + output);
            Console.WriteLine("SHA256: " + Hash(output));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    public static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    static void Patch(ModuleDefinition module)
    {
        var joint = module.Types.Single(t => t.FullName == "jointHelpScript");
        var fps = module.Types.Single(t => t.FullName == "fpscontroller");
        if (joint.Methods.Any(m => m.Name == BodyHelper) || fps.Methods.Any(m => m.Name == ExitHelper))
            throw new InvalidOperationException("Assembly already patched.");
        var unfreeze = joint.Methods.Single(m => m.Name == "UnFreezeStart" && m.Parameters.Count == 0);
        var addCall = unfreeze.Body.Instructions.Single(i => {
            var g = i.Operand as GenericInstanceMethod;
            return g != null && g.Name == "AddComponent" &&
                g.GenericArguments.Count == 1 && g.GenericArguments[0].FullName == "UnityEngine.Rigidbody";
        });
        var addBody = (GenericInstanceMethod)addCall.Operand;
        var gameObject = addBody.DeclaringType;
        var rigidbody = addBody.GenericArguments[0];
        // Use the game's existing core-library scopes rather than importing Unity's
        // netstandard-forwarded System.Object signature as a new dependency.
        var getDefinition = new MethodReference("GetComponent", module.TypeSystem.Object, gameObject) { HasThis = true };
        var componentType = new GenericParameter("T", getDefinition);
        getDefinition.GenericParameters.Add(componentType);
        getDefinition.ReturnType = componentType;
        var getBody = new GenericInstanceMethod(getDefinition);
        getBody.GenericArguments.Add(rigidbody);
        var equality = (MethodReference)unfreeze.Body.Instructions.First(i => {
            var m = i.Operand as MethodReference;
            return m != null && m.Name == "op_Equality" && m.DeclaringType.FullName == "UnityEngine.Object";
        }).Operand;
        var debug = new TypeReference("UnityEngine", "Debug", module, gameObject.Scope);
        var log = new MethodReference("Log", module.TypeSystem.Void, debug) { HasThis = false };
        log.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));

        var helper = new MethodDefinition(BodyHelper,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, rigidbody);
        helper.Parameters.Add(new ParameterDefinition("owner", ParameterAttributes.None, gameObject));
        helper.Body.InitLocals = true;
        var body = new VariableDefinition(rigidbody);
        helper.Body.Variables.Add(body);
        var il = helper.Body.GetILProcessor();
        var create = Instruction.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, getBody);
        il.Emit(OpCodes.Stloc, body);
        il.Emit(OpCodes.Ldloc, body);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, equality); // Unity null semantics, including destroyed native objects.
        il.Emit(OpCodes.Brtrue, create);
        il.Emit(OpCodes.Ldstr, "[TLD MP experiment v1] Reusing existing ragdoll Rigidbody.");
        il.Emit(OpCodes.Call, log);
        il.Emit(OpCodes.Ldloc, body);
        il.Emit(OpCodes.Ret);
        il.Append(create);
        il.Emit(OpCodes.Callvirt, addBody);
        il.Emit(OpCodes.Ret);
        joint.Methods.Add(helper);
        addCall.OpCode = OpCodes.Call;
        addCall.Operand = helper;

        // Log only the branch that actually exits, not every CanGetOut request.
        var getOut = fps.Methods.Single(m => m.Name == "GetOut" && m.Parameters.Count == 2 &&
            m.Parameters[0].ParameterType.FullName == "UnityEngine.Vector3" &&
            m.Parameters[1].ParameterType.FullName == "System.Boolean");
        var exitStore = getOut.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld &&
            ((FieldReference)i.Operand).Name == "Bsitting");
        if (exitStore.Previous.OpCode != OpCodes.Ldc_I4_0)
            throw new InvalidOperationException("Unexpected seat exit code.");
        var exit = new MethodDefinition(ExitHelper,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
        exit.Parameters.Add(new ParameterDefinition("forced", ParameterAttributes.None, module.TypeSystem.Boolean));
        var eil = exit.Body.GetILProcessor();
        var ordinary = Instruction.Create(OpCodes.Ldstr, "[TLD MP experiment v1] Seat exit: input; call stack follows.");
        var writeLog = Instruction.Create(OpCodes.Call, log);
        eil.Emit(OpCodes.Ldarg_0);
        eil.Emit(OpCodes.Brfalse, ordinary);
        eil.Emit(OpCodes.Ldstr, "[TLD MP experiment v1] Seat exit: forced; call stack follows.");
        eil.Emit(OpCodes.Br, writeLog);
        eil.Append(ordinary);
        eil.Append(writeLog);
        var environment = new TypeReference("System", "Environment", module, module.TypeSystem.CoreLibrary);
        var stackTrace = new MethodReference("get_StackTrace", module.TypeSystem.String, environment) { HasThis = false };
        eil.Emit(OpCodes.Call, stackTrace);
        eil.Emit(OpCodes.Call, log);
        eil.Emit(OpCodes.Ret);
        fps.Methods.Add(exit);
        var gil = getOut.Body.GetILProcessor();
        var forced = Instruction.Create(OpCodes.Ldarg, getOut.Parameters[1]);
        gil.InsertAfter(exitStore, forced);
        gil.InsertAfter(forced, Instruction.Create(OpCodes.Call, exit));
        ExpandBranches(getOut);

        var hood = module.Types.Single(t => t.FullName == "GeneszSteamP2PBase.SteamP2PNetworkingUnderTheHoodScript");
        var init = hood.Methods.Single(m => m.Name == "Init" && m.Parameters.Count == 0);
        var callbacks = hood.Fields.Where(f => f.FieldType.FullName.StartsWith("Steamworks.Callback`1<")).ToArray();
        if (callbacks.Length != 7) throw new InvalidOperationException("Unexpected Steam callback layout.");
        var cleanup = new MethodDefinition("TldMp_DisposeCallbacks_v1",
            MethodAttributes.Private | MethodAttributes.HideBySig, module.TypeSystem.Void);
        var cil = cleanup.Body.GetILProcessor();
        foreach (var field in callbacks)
        {
            var next = Instruction.Create(OpCodes.Nop);
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Ldfld, field);
            cil.Emit(OpCodes.Brfalse, next);
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Ldfld, field);
            var dispose = new MethodReference("Dispose", module.TypeSystem.Void, field.FieldType) { HasThis = true };
            cil.Emit(OpCodes.Callvirt, dispose);
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Ldnull);
            cil.Emit(OpCodes.Stfld, field);
            cil.Append(next);
        }
        cil.Emit(OpCodes.Ret);
        hood.Methods.Add(cleanup);
        var iil = init.Body.GetILProcessor();
        var first = init.Body.Instructions[0];
        iil.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        iil.InsertBefore(first, Instruction.Create(OpCodes.Call, cleanup));
        ExpandBranches(init);
    }

    static void ExpandBranches(MethodDefinition method)
    {
        foreach (var i in method.Body.Instructions)
        {
            if (i.OpCode.OperandType != OperandType.ShortInlineBrTarget) continue;
            string fieldName = i.OpCode.Code.ToString();
            fieldName = fieldName.Substring(0, fieldName.Length - 2);
            i.OpCode = (OpCode)typeof(OpCodes).GetField(fieldName).GetValue(null);
        }
    }
}
