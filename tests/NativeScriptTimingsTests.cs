using System;
using TldMp;
class NativeScriptTimingsTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Main()
    {
        var t=new NativeScriptTimings();
        t.Begin(0,1,100);t.Begin(1,1,110);t.End(1,1,140);t.End(0,1,200);
        string s=t.ReportAndReset(1);
        Check(s.Contains("mainscript.Update=p50/p95=100.000/100.000ms total=100.00ms calls=1"),"outer timing");
        Check(s.Contains("mapSettingScript.Update=p50/p95=30.000/30.000ms total=30.00ms calls=1"),"nested labels stay separate");
        Check(t.ReportAndReset(1).Contains("mainscript.Update=unavailable"),"window reset");
        t.Begin(2,2,0);t.Begin(2,2,5);t.End(2,2,10);t.End(2,2,20);
        Check(t.ReportAndReset(1).Contains("total=20.00ms calls=1"),"same-label recursion outer-only");
        t.Begin(3,3,30);t.Begin(3,4,100);t.End(3,4,120);
        s=t.ReportAndReset(1);
        Check(s.Contains("total=20.00ms calls=1")&&s.Contains("incomplete=1"),"missing exit discarded on next frame");
        for(int i=0;i<2000;i++){t.Begin(0,i,10);t.End(0,i,20);}
        s=t.ReportAndReset(1);
        Check(s.Contains("total=20000.00ms calls=2000 samples=512"),"bounded ring with full total/call count");
        t.Begin(1,1,100);t.End(1,2,120);
        Check(t.ReportAndReset(1).Contains("mapSettingScript.Update=unavailable incomplete=1"),"cross-frame exit ignored");
        t.Begin(1,1,100);t.End(1,1,90);
        Check(t.ReportAndReset(1).Contains("mapSettingScript.Update=unavailable incomplete=1"),"negative timestamp rejected");
        t.Begin(-1,1,0);t.End(9,1,10);
        Check(t.ReportAndReset(1).Contains("mainscript.Update=unavailable"),"invalid label ignored");

        // New P0 labels and the shared window/frame denominator.
        t.SetWindowFrames(10);t.Begin(4,1,0);t.End(4,1,1000);
        s=t.ReportAndReset(0.001);
        Check(s.Contains("frames=10"),"window frame denominator missing");
        Check(s.Contains("objGenScript.UpdGen=p50/p95=1.000/1.000ms total=1.00ms calls=1 samples=1 perFrame=0.100ms"),"new label or per-frame denominator failed");

        // Aggregate slots have totals/calls but no percentile claim.
        t.AddAggregate(0,1000);s=t.ReportAndReset(0.001);
        Check(s.Contains("StableMap.RoadDependencyStamp(total)=total=1.00ms calls=1 avg=1.0000ms"),"aggregate stamp timing failed");
        t.AddAggregate(1,-5);Check(t.ReportAndReset(1).Contains("StableMap.PoiDependencyStamp(total)=unavailable"),"negative aggregate accepted");

        // Depth overflow is bounded and still records the outer span once.
        for(int i=0;i<9;i++)t.Begin(5,1,i*10);
        for(int i=0;i<9;i++)t.End(5,1,100+i*10);
        s=t.ReportAndReset(1);
        Check(s.Contains("objGenScript.UpdPlaceRemove=p50/p95=")&&s.Contains("calls=1")&&s.Contains("incomplete=1"),"depth overflow not bounded");

        // Same-frame skipped exit loses at most that call; the next frame recovers.
        t.Begin(6,1,0);t.Begin(6,1,10);t.End(6,1,20);
        t.Begin(6,2,30);t.End(6,2,40);
        s=t.ReportAndReset(1);
        Check(s.Contains("categoryClass.UpdPlaceRemove=p50/p95=10.000/10.000ms total=10.00ms calls=1")&&s.Contains("incomplete=1"),"same-frame skipped exit poisoned later samples");

        t.SetWindowFrames(0);t.Begin(7,1,0);t.End(7,1,10);
        Check(t.ReportAndReset(1).Contains("frames=1"),"invalid window frame count not normalised");

        // Three terrain instances occupy distinct fixed slots.
        t.Begin(10,1,0);t.End(10,1,10);t.Begin(11,1,20);t.End(11,1,40);t.Begin(12,1,50);t.End(12,1,70);
        s=t.ReportAndReset(1);
        Check(s.Contains("terrainGenScript.Upd[0]=p50/p95=10.000/10.000ms")&&
              s.Contains("terrainGenScript.Upd[1]=p50/p95=20.000/20.000ms")&&
              s.Contains("terrainGenScript.Upd[2]=p50/p95=20.000/20.000ms"),"multiple generator labels failed");
        Check(NativeScriptTimings.Names.Length==110 && NativeScriptTimings.ExtendedStart==54,
            "extended timing table must match the independent native-hook allowlist");
        Check(new System.Collections.Generic.HashSet<string>(NativeScriptTimings.Names).Count==110,
            "system timing labels must be unique");
        t.SetWindowFrames(4);
        for(int i=0;i<3;i++){t.Begin(54,101,i*20);t.End(54,101,i*20+10);}
        t.Begin(54,102,100);t.End(54,102,180);
        s=t.ReportAndReset(1);
        Check(s.Contains("fpscontroller.Update=p50/p95=10.000/80.000ms total=110.00ms calls=4 samples=4 perFrame=27.500ms maxCall=80.000ms peakFrame=80.000ms peakFrameId=102 maxCallsPerFrame=3"),
            "single-call max, worst frame and busiest frame must remain separate");
        Check(!s.Contains("mountStuff.Scan="),"inactive extended labels must not inflate logs");
        t.Begin(108,103,0);t.End(108,103,15);
        Check(t.ReportAndReset(1).Contains("newSaveScreenScript.ReadFiles=p50/p95=15.000/15.000ms"),
            "last extended slot must be observed");
        t.Begin(64,200,0);t.End(64,200,10000);
        for(int i=0;i<600;i++){t.Begin(64,201+i,0);t.End(64,201+i,1);}
        s=t.ReportAndReset(1);
        Check(s.Contains("maxCall=10000.000ms peakFrame=10000.000ms peakFrameId=200"),
            "window maxima must survive eviction from the percentile ring");
        t.Begin(54,900,100);
        t.ReportAndReset(1);
        t.End(54,900,140);
        s=t.ReportAndReset(1);
        Check(s.Contains("fpscontroller.Update=p50/p95=40.000/40.000ms total=40.00ms calls=1"),
            "report inside an active parent must preserve its eventual completion");
        t.Begin(54,901,0);t.Begin(54,901,10);t.End(54,901,15);t.End(54,901,30);
        Check(t.ReportAndReset(1).Contains("total=30.00ms calls=1 samples=1"),
            "extended recursive scopes must keep outer-only totals and counts");
        Console.WriteLine("PASS: "+checks+" bounded native script timing assertions");
    }
}
