using System;
using System.IO;
using System.Threading.Tasks;
using TldMp;
public static class WorldPlacementPolicyTests
{
 static int checks; static void Check(bool ok){checks++;if(!ok)throw new Exception("check "+checks);}
 static void Invalid(string s){bool failed=false;try{WorldPlacementPolicy.ReadVersion(s);}catch(InvalidDataException){failed=true;}Check(failed);}
 public static void Main(){
  Check(WorldPlacementPolicy.ReadVersion(null)==0);Check(WorldPlacementPolicy.ReadVersion("2024.11.26b")==0);
  for(int biome=0;biome<4;biome++)for(int placement=0;placement<=5;placement++){
   string a=WorldBiomePolicy.StampVersion("2024",biome);a=WorldPlacementPolicy.StampVersion(a,placement);
   Check(WorldPlacementPolicy.ReadVersion(a)==placement);Check(WorldBiomePolicy.ModeFromVersion(a)==biome);
   string b=WorldPlacementPolicy.StampVersion("2024",placement);b=WorldBiomePolicy.StampVersion(b,biome);
   Check(WorldPlacementPolicy.ReadVersion(b)==placement);Check(WorldBiomePolicy.ModeFromVersion(b)==biome);
   Check(WorldPlacementPolicy.StampVersion(a,placement)==a);
   string cleared=WorldPlacementPolicy.StampVersion(a,0);Check(WorldPlacementPolicy.ReadVersion(cleared)==0);Check(WorldBiomePolicy.ModeFromVersion(cleared)==biome);
  }
  Invalid("x [Roadworthy placement: 6]");Invalid("x [Roadworthy placement: 0]");Invalid("x [Roadworthy placement: ]");
  Invalid("x [Roadworthy placement: 1");Invalid("x [Roadworthy placement: 1] [Roadworthy placement: 1]");
  Invalid("x [Roadworthy placement:1]");Invalid("x [Roadworthy placement 1]");Invalid("x [Roadworthy placement: 01]");Invalid("x [Roadworthy placement: +1]");
  for(int p=0;p<=5;p++)for(int b=0;b<4;b++)for(int c=0;c<16;c++){
   int a,bb,pp;int packed=WorldPlacementPolicy.PackConfig(c,b,p);Check(WorldPlacementPolicy.TryUnpackConfig(packed,out a,out bb,out pp) && a==c && bb==b && pp==p);
   for(int bit=9;bit<31;bit++)Check(!WorldPlacementPolicy.TryUnpackConfig(packed | (1<<bit),out a,out bb,out pp));
   Check(!WorldPlacementPolicy.TryUnpackConfig(packed | int.MinValue,out a,out bb,out pp));
  }
  for(int low=0;low<64;low++){int a,b,p;Check(WorldPlacementPolicy.TryUnpackConfig(low | 192,out a,out b,out p));}
  for(int reserved=6;reserved<=7;reserved++)for(int low=0;low<64;low++){int a,b,p;Check(!WorldPlacementPolicy.TryUnpackConfig(low | (reserved<<6),out a,out b,out p));}
  WorldPlacementPolicy.SetVersion(0);WorldPlacementPolicy.BeginWorker(0);WorldPlacementPolicy.SetVersion(1);Check(WorldPlacementPolicy.EffectiveVersion==0);
  bool nested=false;try{WorldPlacementPolicy.BeginWorker(1);}catch(InvalidOperationException){nested=true;}Check(nested);WorldPlacementPolicy.EndWorker();Check(WorldPlacementPolicy.EffectiveVersion==1);
  Parallel.For(0,1000,delegate(int i){int expected=i%6;WorldPlacementPolicy.BeginWorker(expected);try{if(WorldPlacementPolicy.EffectiveVersion!=expected)throw new Exception("worker isolation");}finally{WorldPlacementPolicy.EndWorker();}});
  WorldPlacementPolicy.SetVersion(0);Console.WriteLine("PASS "+checks+" placement codec and worker checks");
 }
}

