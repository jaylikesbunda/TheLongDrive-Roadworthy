using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Mono.Cecil;

// JIT compilation only: never invoke a game method or initialize a Unity player.
public static class MonoRuntimeJitProbe
{
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibrary(string file);
    [DllImport("kernel32")]static extern uint SetErrorMode(uint mode);
    const string Mono="mono-2.0-bdwgc.dll";
    [DllImport(Mono,CallingConvention=CallingConvention.Cdecl)]static extern void mono_set_dirs(string assemblyDir,string configDir);
    [DllImport(Mono,CallingConvention=CallingConvention.Cdecl)]static extern void mono_set_assemblies_path(string path);
    [DllImport(Mono,CallingConvention=CallingConvention.Cdecl)]static extern void mono_config_parse(string file);
    [DllImport(Mono,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr mono_jit_init_version(string name,string version);
    [DllImport(Mono,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr mono_domain_assembly_open(IntPtr domain,string file);
    [DllImport(Mono,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr mono_assembly_get_image(IntPtr assembly);
    [DllImport(Mono,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr mono_get_method(IntPtr image,uint token,IntPtr klass);
    [DllImport(Mono,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr mono_compile_method(IntPtr method);
    static void Collect(TypeDefinition type,List<MethodDefinition> methods)
    {
        foreach(var method in type.Methods)
        {
            if(!method.HasBody || method.HasGenericParameters || type.HasGenericParameters || type.FullName=="TldStable")continue;
            bool selected=type.FullName=="mapSettingScript" && method.Name=="UpdInstant" || type.FullName=="poiGenScript" && method.Name=="Upd" || type.FullName=="roadGenScript" && method.Name=="UpdPlaceRemove" || type.FullName=="terrainGenScript" && method.Name=="CheckPlace" || type.FullName=="terrainGenSettings/Noises" && method.Name=="Noise" || type.FullName=="NoiseS3D" && method.Name=="Noise" && method.Parameters.Count==3;
            foreach(var instruction in method.Body.Instructions)
            {
                var call=instruction.Operand as MethodReference;
                if(call!=null && call.DeclaringType.FullName=="TldMp.StableGameBridge")selected=true;
            }
            if(selected)methods.Add(method);
        }
        foreach(var nested in type.NestedTypes)Collect(nested,methods);
    }
    public static int Main(string[] args)
    {
        if(args.Length!=2){Console.Error.WriteLine("Usage: MonoRuntimeJitProbe gameDirectory assemblyPath");return 2;}
        SetErrorMode(0x0001|0x0002);
        string game=Path.GetFullPath(args[0]),assemblyPath=Path.GetFullPath(args[1]);
        var methods=new List<MethodDefinition>();
        using(var assembly=AssemblyDefinition.ReadAssembly(assemblyPath))
        {
            foreach(var type in assembly.MainModule.Types)Collect(type,methods);
            methods.Sort(delegate(MethodDefinition a,MethodDefinition b){return StringComparer.Ordinal.Compare(a.FullName,b.FullName);});
            string dll=Path.Combine(game,"MonoBleedingEdge\\EmbedRuntime\\"+Mono);
            if(LoadLibrary(dll)==IntPtr.Zero)throw new Exception("Could not load shipped Mono: "+Marshal.GetLastWin32Error());
            return Compile(game,assemblyPath,methods);
        }
    }
    // Separate method prevents P/Invoke binding before the explicit LoadLibrary.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Compile(string game,string assemblyPath,List<MethodDefinition> methods)
    {
        string managed=Path.Combine(game,"TheLongDrive_Data\\Managed");
        mono_set_dirs(managed,Path.Combine(game,"MonoBleedingEdge\\etc"));
        mono_set_assemblies_path(Path.GetDirectoryName(assemblyPath)+";"+managed);
        mono_config_parse(null);
        IntPtr domain=mono_jit_init_version("tld-compile-only-probe","v4.0.30319");
        if(domain==IntPtr.Zero)throw new Exception("Shipped Mono initialization failed.");
        IntPtr assembly=mono_domain_assembly_open(domain,assemblyPath);
        if(assembly==IntPtr.Zero)throw new Exception("Shipped Mono assembly load failed.");
        IntPtr image=mono_assembly_get_image(assembly);
        foreach(var method in methods)
        {
            Console.WriteLine("JIT "+method.FullName);Console.Out.Flush();
            IntPtr nativeMethod=mono_get_method(image,method.MetadataToken.ToUInt32(),IntPtr.Zero);
            if(nativeMethod==IntPtr.Zero)throw new Exception("Method resolution failed: "+method.FullName);
            if(mono_compile_method(nativeMethod)==IntPtr.Zero)throw new Exception("Method compilation failed: "+method.FullName);
            Console.WriteLine("JIT OK "+method.FullName);Console.Out.Flush();
        }
        Console.WriteLine("PASS: shipped Unity Mono compiled "+methods.Count+" selected native methods without executing game code.");
        return 0;
    }
}
