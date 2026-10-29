using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
public static class TamperObserverExceptionBoundary
{
    public static int Main(string[] args)
    {
        if(args.Length!=3)return 2;
        var resolver=new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(args[2]);
        resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0])));
        using(var assembly=AssemblyDefinition.ReadAssembly(args[0],new ReaderParameters { AssemblyResolver=resolver }))
        {
            int changes=0;
            foreach(var type in assembly.MainModule.Types)
            {
                if(type.FullName!="poiGenScript")continue;
                foreach(var method in type.Methods)
                {
                    if(method.Name!="Upd" || method.Parameters.Count!=1 || !method.HasBody)continue;
                    foreach(var instruction in method.Body.Instructions)
                    {
                        var call=instruction.Operand as MethodReference;
                        if(instruction.OpCode!=OpCodes.Call || call==null || call.DeclaringType.FullName!="TldMp.StableGameBridge" || call.Name!="NativePoiEnd")continue;
                        var ret=instruction.Next;
                        if(ret==null || ret.OpCode!=OpCodes.Ret)throw new Exception("Unexpected observer return.");
                        foreach(var handler in method.Body.ExceptionHandlers)
                            if(changes==0 && handler.HandlerType==ExceptionHandlerType.Finally && handler.HandlerEnd==instruction){handler.HandlerEnd=ret;changes++;}
                    }
                }
            }
            if(changes!=1)throw new Exception("Expected one final observer boundary; found "+changes);
            assembly.Write(args[1]);
            Console.WriteLine("Created malformed-finally observer negative control.");
        }
        return 0;
    }
}
