using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TldMp;

public static class ContentFingerprintTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}

    public static int Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"tld-content-fingerprint-"+Guid.NewGuid().ToString("N"));
        try
        {
            string managed=Path.Combine(root,"Managed"),assets=Path.Combine(root,"Game_Data"),custom=Path.Combine(root,"Maps");
            Directory.CreateDirectory(managed);Directory.CreateDirectory(assets);Directory.CreateDirectory(Path.Combine(custom,"Nested"));
            Write(Path.Combine(managed,"plugin-a.dll"),"included assembly bytes");
            Write(Path.Combine(managed,"Assembly-CSharp.dll"),"excluded native assembly");
            Write(Path.Combine(managed,"Tld.Multiplayer.dll"),"excluded changing helper bytes");
            Write(Path.Combine(assets,"scene.assets"),"included asset bytes");
            Write(Path.Combine(assets,"globalgamemanagers"),"included global manager bytes");
            Write(Path.Combine(assets,"ignored.txt"),"not selected");
            Write(Path.Combine(custom,"road-map.json"),"included custom map bytes");
            Write(Path.Combine(custom,"Nested","POI.BIN"),"included nested bytes");

            const string identity="test-build-identity";
            string expected=LegacyFingerprint(managed,assets,custom,identity);
            string actual=ContentFingerprint.Compute(managed,assets,custom,identity);
            Check(actual==expected,"Synchronous helper digest differs from the legacy canonical fingerprint");

            var job=ContentFingerprint.Start(managed,assets,custom,identity);
            string ready=null,error=null;var timer=Stopwatch.StartNew();
            while(!job.TryGetResult(out ready,out error) && timer.ElapsedMilliseconds<30000)Thread.Sleep(1);
            Check(ready!=null && error==null,"Background fingerprint job did not complete successfully");
            Check(ready==expected,"Background fingerprint changed the canonical digest");
            double completedWork=job.WorkMilliseconds;Thread.Sleep(150);
            Check(completedWork>=0 && job.WorkMilliseconds==completedWork,
                "Completed worker duration changed while the result waited to be polled");

            var abandoned=ContentFingerprint.Start(managed,assets,custom,identity);
            var replacement=ContentFingerprint.Start(managed,assets,custom,identity+"-new-session");
            string abandonedValue=null,abandonedError=null,replacementValue=null,replacementError=null;
            Check(Wait(abandoned,out abandonedValue,out abandonedError) && Wait(replacement,out replacementValue,out replacementError),"Overlapping session fingerprint jobs did not finish independently");
            Check(abandonedError==null&&replacementError==null&&abandonedValue==expected&&replacementValue!=expected,
                "A late result from an older session contaminated a newer fingerprint job");

            var failed=ContentFingerprint.Start(Path.Combine(root,"missing-managed"),assets,custom,identity);
            string failedValue=null,failedError=null;
            Check(Wait(failed,out failedValue,out failedError)&&failedValue==null&&!String.IsNullOrEmpty(failedError),
                "Filesystem failure was not reported as a completed error result");
            double failedWork=failed.WorkMilliseconds;Thread.Sleep(100);
            Check(failedWork>=0 && failed.WorkMilliseconds==failedWork,
                "Failed worker duration was not captured at completion");

            Write(Path.Combine(managed,"Tld.Multiplayer.dll"),"helper changed but is intentionally excluded");
            Check(ContentFingerprint.Compute(managed,assets,custom,identity)==expected,"Excluded helper DLL changed the fingerprint");
            Write(Path.Combine(custom,"Nested","POI.BIN"),"changed nested bytes");
            string changed=ContentFingerprint.Compute(managed,assets,custom,identity);
            Check(changed!=expected,"Recursive custom-map content change was not reflected");
            Check(changed==LegacyFingerprint(managed,assets,custom,identity),"Changed custom-map digest differs from legacy path handling");

            Console.WriteLine("PASS: "+checks+" content fingerprint checks (legacy digest parity, async completion, exclusions and recursive map content).");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    static void Write(string path,string value){File.WriteAllBytes(path,Encoding.UTF8.GetBytes(value));}
    static bool Wait(ContentFingerprint.Job job,out string value,out string error)
    {
        var timer=Stopwatch.StartNew();value=null;error=null;
        while(!job.TryGetResult(out value,out error) && timer.ElapsedMilliseconds<30000)Thread.Sleep(1);
        return job.TryGetResult(out value,out error);
    }

    // Independent replay of the exact pre-async implementation.
    static string LegacyFingerprint(string managed,string assets,string custom,string identity)
    {
        var files=new List<string>();
        foreach(var file in Directory.GetFiles(managed,"*.dll"))if(Path.GetFileName(file)!="Assembly-CSharp.dll" && Path.GetFileName(file)!="Tld.Multiplayer.dll")files.Add(file);
        foreach(string file in Directory.GetFiles(assets,"*.assets"))files.Add(file);
        if(File.Exists(Path.Combine(assets,"globalgamemanagers")))files.Add(Path.Combine(assets,"globalgamemanagers"));
        if(Directory.Exists(custom))files.AddRange(Directory.GetFiles(custom,"*",SearchOption.AllDirectories));
        var records=new List<string>();var text=new StringBuilder("TLD-STABLE-V3-1\n"+identity+"\nCF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000\n");
        foreach(var file in files)
        {
            string relative=file.StartsWith(custom,StringComparison.OrdinalIgnoreCase)?"Maps/"+file.Substring(custom.Length).Replace('\\','/'):Path.GetFileName(file);
            using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))records.Add(relative.ToLowerInvariant()+":"+BitConverter.ToString(sha.ComputeHash(stream)));
        }
        records.Sort(StringComparer.Ordinal);foreach(string record in records)text.Append(record).Append('\n');
        return StableNode.Hash(Encoding.UTF8.GetBytes(text.ToString()));
    }
}
