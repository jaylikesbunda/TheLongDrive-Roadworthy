using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public static class TerrainProductionTests
{
    public static int Main(string[] args)
    {
        try
        {
            var selected=Assembly.Load(File.ReadAllBytes(args[1]));
            AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e)
            {string name=new AssemblyName(e.Name).Name;if(name=="Assembly-CSharp")return selected;string p=Path.Combine(args[0],name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
            return Run(args[2]=="patched",selected);
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(bool patched,Assembly selected)
    {
        if(typeof(terrainGenSettings).Assembly!=selected)throw new Exception("Wrong terrain assembly loaded.");
        // Legal constant noise settings require no Unity native math/noise calls.
        // Exactly 64 unit layers times a unit mask must ALWAYS equal 64, even if
        // mesh workers and road/biome evaluation call the SAME array entry.
        var shared=new terrainGenSettings.Noises[1];shared[0].noises=new terrainGenSettings.noiseSetting[64];
        for(int i=0;i<10;i++)if(shared[0].Noise(new Vector3d(i,0,i))!=64)throw new Exception("Sequential terrain reference changed.");
        int mismatches=0;int workers=Math.Min(16,Math.Max(2,Environment.ProcessorCount));
        Parallel.For(0,workers,new ParallelOptions{MaxDegreeOfParallelism=workers},delegate(int worker)
        {for(int i=0;i<25000;i++){double h=shared[0].Noise(new Vector3d(i,worker,-i));if(h!=64)Interlocked.Increment(ref mismatches);}});
        if(patched && mismatches!=0)throw new Exception("Patched terrain noise still races: "+mismatches);
        Console.WriteLine((patched?"PASS":"BASELINE")+": actual production terrain noise, "+(workers*25000)+" concurrent calls; "+mismatches+" incorrect heights. "+(patched?"All matched the sequential reference.":mismatches>0?"Shared accumulator race reproduced without native calls.":"Race not observed on this run; code still has shared accumulators."));return 0;
    }
}
