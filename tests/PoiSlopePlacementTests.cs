using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using TldMp;
public static class PoiSlopePlacementTests
{
 static int checks; static void Check(bool x){checks++;if(!x)throw new Exception("check "+checks);}
 public static void Main(string[] args){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name;string path=name=="Assembly-CSharp"?args[1]:Path.Combine(args[0],name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;};
  Run();
 }
 [MethodImpl(MethodImplOptions.NoInlining)] static void Run(){
  Check(PoiSlopePlacement.FootprintRadius(5,10,2)==25);
  Check(PoiSlopePlacement.FootprintRadius(0,-1,2)==0);
  Check(PoiSlopePlacement.IsAcceptable(0,8,4));
  Check(!PoiSlopePlacement.IsAcceptable(0,8.01,4));
  Check(!PoiSlopePlacement.IsAcceptable(0,1,6));
  Check(!PoiSlopePlacement.IsAcceptable(double.NaN,1,0));
  Check(!PoiSlopePlacement.IsAcceptable(0,double.PositiveInfinity,0));
  double x,z; PoiSlopePlacement.CandidateOffset(0,20,out x,out z);Check(x==0 && z==0);
  for(int i=1;i<PoiSlopePlacement.CandidateCount;i++){double x1,z1;PoiSlopePlacement.CandidateOffset(i,20,out x,out z);PoiSlopePlacement.CandidateOffset(i,20,out x1,out z1);Check(x==x1 && z==z1);Check(Math.Sqrt(x*x+z*z)<=20.0001);}
  Console.WriteLine("PASS "+checks+" POI slope policy checks");
 }
}
