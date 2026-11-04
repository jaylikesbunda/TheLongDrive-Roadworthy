using System;using System.Threading.Tasks;using TldMp;using UnityEngine;
public static class WorldBiomePolicyTests
{
 static int checks;static void Check(bool ok){checks++;if(!ok)throw new Exception("check "+checks);}
 static void Equal(Color a,Color b){Check(a.r==b.r && a.g==b.g && a.b==b.b && a.a==b.a);}
 public static void Main(){
  var original=new Color(.2f,.3f,.4f,.5f);WorldBiomePolicy.SetMode(0);Equal(WorldBiomePolicy.Apply(original),original);
  WorldBiomePolicy.SetMode(1);Equal(WorldBiomePolicy.Apply(original),new Color(1,0,0,1));
  WorldBiomePolicy.SetMode(2);Equal(WorldBiomePolicy.Apply(original),new Color(0,0,1,1));
  WorldBiomePolicy.SetMode(3);Equal(WorldBiomePolicy.Apply(original),new Color(0,1,0,1));
  for(int m=0;m<4;m++)for(int c=0;c<16;c++){int cal,mode;Check(WorldBiomePolicy.TryUnpackConfig(WorldBiomePolicy.PackConfig(c,m),out cal,out mode) && cal==c && mode==m);}
  int cc,mm;Check(!WorldBiomePolicy.TryUnpackConfig(64,out cc,out mm));
  for(int m=0;m<4;m++){string v=WorldBiomePolicy.StampVersion("2024.11.26b",m);Check(WorldBiomePolicy.ModeFromVersion(v)==m);Check(WorldBiomePolicy.StampVersion(v,m)==v);}
  Check(WorldBiomePolicy.ModeFromVersion("2024.11.26b")==0);
  bool invalid=false;try{WorldBiomePolicy.ModeFromVersion("2024 [Roadworthy biome: Broken]");}catch(System.IO.InvalidDataException){invalid=true;}Check(invalid);
  WorldBiomePolicy.SetMode(3);WorldBiomePolicy.BeginWorker(1);WorldBiomePolicy.SetMode(2);Equal(WorldBiomePolicy.Apply(original),new Color(1,0,0,1));
  bool threw=false;try{WorldBiomePolicy.BeginWorker(0);}catch(InvalidOperationException){threw=true;}Check(threw);WorldBiomePolicy.EndWorker();Equal(WorldBiomePolicy.Apply(original),new Color(0,0,1,1));
  WorldBiomePolicy.SetMode(0);Parallel.For(0,1000,delegate(int i){int mode=i%4;WorldBiomePolicy.BeginWorker(mode);try{var x=WorldBiomePolicy.Apply(original);if(mode==0 && x.r!=original.r || mode==1 && x.r!=1 || mode==2 && x.b!=1 || mode==3 && x.g!=1)throw new Exception("worker isolation");}finally{WorldBiomePolicy.EndWorker();}});
  Console.WriteLine("PASS "+checks+" biome policy checks and 1000 concurrent captured-mode checks.");
 }
}
