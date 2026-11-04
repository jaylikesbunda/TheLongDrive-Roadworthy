using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using TldMp;
using UnityEngine;

static class TerrainPadBlendTests
{
    static int checks;
    static void Check(bool value){checks++;if(!value)throw new Exception("Terrain pad check "+checks);}
    static poiGenScript.poiClass Poi(double x,double height,float inner,float outer)
    {
        var p=(poiGenScript.poiClass)FormatterServices.GetUninitializedObject(typeof(poiGenScript.poiClass));
        var a=(poiGenScript.alignClass)FormatterServices.GetUninitializedObject(typeof(poiGenScript.alignClass));
        a.gpos=new Vector3d(x,height,0);a.minRadius=inner;a.maxRadius=outer;a.maxEffectRange=outer;
        p.terrainAligns=new List<poiGenScript.alignClass>{a};return p;
    }
    static bool Same(double a,double b){return BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);}
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name;string p=name=="Assembly-CSharp"?args[1]:Path.Combine(args[0],name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
        Assembly.LoadFrom(Path.GetFullPath(args[1]));Run();
    }
    static void Run()
    {
        var a=Poi(-10,10,2,20);var b=Poi(10,30,2,20);var pois=new List<poiGenScript.poiClass>{a,b};
        for(int version=0;version<5;version++)
        {
            WorldPlacementPolicy.SetVersion(version);TerrainPadBlend.Prepare(a);Check(a.terrainAligns[0].maxRadius==20);
            for(int i=-200;i<=200;i++)
            {
                var p=new Vector3d(i*0.2,0,1);var stock=poiGenScript.TerrainAlignsValue(p,pois);var result=TerrainPadBlend.Value(p,pois);
                Check(Same(stock.height,result.height)&&Same(stock.lerp,result.lerp));
            }
        }
        WorldPlacementPolicy.SetVersion(5);TerrainPadBlend.Prepare(a);TerrainPadBlend.Prepare(b);
        Check(a.terrainAligns[0].maxRadius==24.5f && a.terrainAligns[0].minRadius==2 && a.terrainAligns[0].maxEffectRange==24.5f);
        TerrainPadBlend.Prepare(a);Check(a.terrainAligns[0].maxRadius==24.5f);
        var wide=Poi(0,10,10,100);TerrainPadBlend.Prepare(wide);Check(wide.terrainAligns[0].maxRadius==108);
        var zero=Poi(0,10,4,4);TerrainPadBlend.Prepare(zero);Check(zero.terrainAligns[0].maxRadius==4);
        var alone=new List<poiGenScript.poiClass>{a};
        for(int i=-100;i<=100;i++)
        {
            var p=new Vector3d(i,0,0);var stock=poiGenScript.TerrainAlignsValue(p,alone);var result=TerrainPadBlend.Value(p,alone);
            Check(Same(stock.height,result.height)&&Same(stock.lerp,result.lerp));
        }
        // Foundations remain exact, including where another shoulder reaches them.
        foreach(var p in new[]{new Vector3d(-10,0,0),new Vector3d(10,0,0),new Vector3d(-9,0,0)})
        {var stock=poiGenScript.TerrainAlignsValue(p,pois);var result=TerrainPadBlend.Value(p,pois);Check(Same(stock.height,result.height)&&Same(stock.lerp,result.lerp));}
        var left=TerrainPadBlend.Value(new Vector3d(-0.000001,0,0),pois);
        var right=TerrainPadBlend.Value(new Vector3d(0.000001,0,0),pois);
        Check(Math.Abs(left.height-right.height)<0.0001);Check(Math.Abs(TerrainPadBlend.Value(new Vector3d(0,0,0),pois).height-20)<0.000001);
        for(int i=-500;i<=500;i++)
        {
            var p=new Vector3d(i*0.05,0,0);var stock=poiGenScript.TerrainAlignsValue(p,pois);var result=TerrainPadBlend.Value(p,pois);
            Check(Same(stock.lerp,result.lerp));Check(result.lerp==0 || (result.height>=10 && result.height<=30));
        }
        pois.Reverse();Check(Math.Abs(TerrainPadBlend.Value(new Vector3d(0,0,0),pois).height-20)<0.000001);
        WorldPlacementPolicy.SetVersion(0);
        Parallel.For(0,1000,delegate(int i){WorldPlacementPolicy.BeginWorker(5);try{var h=TerrainPadBlend.Value(new Vector3d(0,0,0),pois).height;if(Math.Abs(h-20)>0.000001)throw new Exception("worker policy not captured");}finally{WorldPlacementPolicy.EndWorker();}});
        Check(WorldPlacementPolicy.EffectiveVersion==0);
        Console.WriteLine("PASS "+checks+" native terrain-pad checks plus 1000 captured-policy worker checks");
    }
}
