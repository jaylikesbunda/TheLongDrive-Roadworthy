using System;
using System.IO;
using TldMp;
class PhaseTimingsTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Main()
    {
        var t=new PhaseTimingTable();
        Check(t.Record(PhaseTimings.RoadGenerate,2000,0.001,"r|0|0|0|0")==null,"sub-threshold call reported as spike");
        string spike=t.Record(PhaseTimings.RoadGenerate,9000,0.001,"r|0|0|0|0");
        Check(spike=="serve.road.generate ms=9.00 r|0|0|0|0","spike text: "+spike);
        t.Count(PhaseTimings.RoadBytes,1500);t.Count(PhaseTimings.RoadBytes,-4);
        string s=t.ReportAndReset(10,0.001);
        Check(s.Contains("serve.road.generate=total=11.00ms calls=2 avg=5.500ms max=9.000ms perFrame=1.100ms"),"phase totals: "+s);
        Check(s.Contains("client.CategoryGen=none"),"unused phase not reported as none");
        Check(s.Contains("serve.road.bytes=1500"),"negative counter accepted or count lost");
        Check(s.Contains("spikesLogged=1 spikesDropped=0"),"spike count");
        s=t.ReportAndReset(0,0.001);
        Check(s.Contains("frames=1") && s.Contains("serve.road.generate=none") && s.Contains("serve.road.bytes=0"),"window reset");
        for(int i=0;i<PhaseTimingTable.MaxSpikesPerWindow+5;i++)t.Record(PhaseTimings.ApplyRoad,20000,0.001,null);
        Check(t.ReportAndReset(1,0.001).Contains("spikesLogged=32 spikesDropped=5"),"spike lines not bounded");
        Check(t.Record(-1,10,1,null)==null && t.Record(PhaseTimingTable.Names.Length,10,1,null)==null && t.Record(0,-1,1,null)==null,"invalid record accepted");
        Check(t.ReportAndReset(1,1).Contains("client.CategoryGen=none"),"invalid record changed totals");
        Check(PhaseTimingTable.Names.Length==PhaseTimings.FingerprintWait+1 && PhaseTimingTable.CounterNames.Length==PhaseTimings.TerrainCacheHits+1,"slot constants out of step with names");
        // Static facade records nothing until the main thread is registered.
        Check(PhaseTimings.Begin()==0,"facade active before registration");

        string root=Path.Combine(Path.GetTempPath(),"tldmp-switch-"+Guid.NewGuid().ToString("N"));
        string managed=Path.Combine(Path.Combine(root,"TheLongDrive_Data"),"Managed");
        Directory.CreateDirectory(managed);
        try
        {
            Check(!ModDiagnostics.FlagPresent(managed),"flag reported without file");
            File.WriteAllText(Path.Combine(root,ModDiagnostics.FlagName),"");
            Check(ModDiagnostics.FlagPresent(managed),"flag beside the game exe not found");
            Check(!ModDiagnostics.FlagPresent(null) && !ModDiagnostics.FlagPresent(""),"empty path accepted");
        }
        finally{Directory.Delete(root,true);}
        Console.WriteLine("PASS: "+checks+" phase timing and diagnostics switch assertions");
    }
}
