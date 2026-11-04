using System;using System.IO;using System.Runtime.Serialization;using System.Runtime.Serialization.Formatters.Binary;using TldMp;
public static class WorldBiomeSaveTests
{
 public static void Main(string[] args)
 {
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new System.Reflection.AssemblyName(e.Name).Name;string path=name=="Assembly-CSharp"?args[1]:Path.Combine(args[0],name+".dll");return File.Exists(path)?System.Reflection.Assembly.LoadFrom(path):null;};
  System.Reflection.Assembly.LoadFrom(Path.GetFullPath(args[1]));Run();
 }
 static void Run(){int checks=0;for(int mode=0;mode<4;mode++){
  var d=(savedata)FormatterServices.GetUninitializedObject(typeof(savedata));
  d.properties=new saveproperties{seed=-123456789,version=WorldBiomePolicy.StampVersion("2024.11.26b",mode),driven=17.5f};
  using(var s=new MemoryStream()){var f=new BinaryFormatter();f.Serialize(s,d);s.Position=0;var loaded=(savedata)f.Deserialize(s);
   if(loaded.properties.seed!=-123456789 || loaded.properties.driven!=17.5f || WorldBiomePolicy.ModeFromVersion(loaded.properties.version)!=mode)throw new Exception("native save roundtrip");checks++;}
  var copy=new saveproperties();copy.Copy(d.properties);if(WorldBiomePolicy.ModeFromVersion(copy.version)!=mode)throw new Exception("native properties copy");checks++;
 }
 Console.WriteLine("PASS "+checks+" real native saveproperties binary and copy roundtrips, seed unchanged; no Unity native runtime.");}
}
