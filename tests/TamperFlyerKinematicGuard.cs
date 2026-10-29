using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Negative control for the production verifier: make the dynamic branch skip
// the reloaded velocity value and arrive at set_velocity with an invalid stack.
public static class TamperFlyerKinematicGuard
{
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length!=3)throw new ArgumentException("usage: input output managed-directory");
            var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(args[2]);
            using(var assembly=AssemblyDefinition.ReadAssembly(args[0],new ReaderParameters{AssemblyResolver=resolver}))
            {
                var method=assembly.MainModule.GetTypes().Single(t=>t.FullName=="randomFlyer").Methods.Single(m=>m.Name=="Upd" && m.Parameters.Count==0);
                var setter=method.Body.Instructions.Single(i=>i.OpCode==OpCodes.Callvirt && i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name=="set_velocity" && ((MethodReference)i.Operand).DeclaringType.FullName=="UnityEngine.Rigidbody");
                var branch=method.Body.Instructions.Single(i=>i.OpCode==OpCodes.Brfalse && i.Operand is Instruction &&
                    ((Instruction)i.Operand).OpCode==OpCodes.Ldloc && ((VariableDefinition)((Instruction)i.Operand).Operand).VariableType.FullName=="UnityEngine.Vector3");
                branch.Operand=setter;
                assembly.Write(args[1]);
            }
            Console.WriteLine("Wrote malformed flyer kinematic-branch negative control.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
