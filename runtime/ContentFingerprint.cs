using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace TldMp
{
    // Computes the compatibility digest from roots captured on Unity's main
    // thread. The worker touches only managed filesystem and hashing APIs.
    public static class ContentFingerprint
    {
        public sealed class Job
        {
            readonly string managed,assets,custom,buildIdentity;
            readonly object gate=new object();
            readonly ManualResetEvent finished=new ManualResetEvent(false);
            bool complete;
            string result,error;
            double workMilliseconds;

            internal Job(string managedPath,string assetsPath,string customPath,string identity)
            {managed=managedPath;assets=assetsPath;custom=customPath;buildIdentity=identity;}

            internal void Run()
            {
                string value=null,failure=null;
                long started=Stopwatch.GetTimestamp();
                try{value=Compute(managed,assets,custom,buildIdentity);}
                catch(Exception e){failure=e.ToString();}
                double elapsed=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
                lock(gate){result=value;error=failure;workMilliseconds=elapsed;complete=true;}finished.Set();
            }

            internal void Fail(string message)
            {lock(gate){error=message;complete=true;}finished.Set();}

            public bool TryGetResult(out string value,out string failure)
            {
                lock(gate){value=complete?result:null;failure=complete?error:null;return complete;}
            }
            public double WorkMilliseconds{get{lock(gate){return workMilliseconds;}}}

            // Reserved for compatibility-critical ID allocation if it races the
            // early-boot worker. Regular session startup only polls TryGetResult.
            public void WaitForResult(out string value,out string failure)
            {finished.WaitOne();TryGetResult(out value,out failure);}
        }

        public static Job Start(string managed,string assets,string custom,string buildIdentity)
        {
            var job=new Job(managed,assets,custom,buildIdentity);
            if(!ThreadPool.QueueUserWorkItem(delegate(object state){((Job)state).Run();},job))
                job.Fail("ThreadPool rejected the fingerprint work item.");
            return job;
        }

        // Kept separately callable for deterministic parity tests.
        public static string Compute(string managed,string assets,string custom,string buildIdentity)
        {
            var files=new List<string>();
            foreach(var file in Directory.GetFiles(managed,"*.dll"))
                if(Path.GetFileName(file)!="Assembly-CSharp.dll" && Path.GetFileName(file)!="Tld.Multiplayer.dll")files.Add(file);
            foreach(string file in Directory.GetFiles(assets,"*.assets"))files.Add(file);
            string global=Path.Combine(assets,"globalgamemanagers");if(File.Exists(global))files.Add(global);
            if(Directory.Exists(custom))files.AddRange(Directory.GetFiles(custom,"*",SearchOption.AllDirectories));

            var records=new List<string>();var text=new StringBuilder("TLD-STABLE-V3-1\n"+buildIdentity+"\nCF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000\n");
            foreach(var file in files)
            {
                string relative=file.StartsWith(custom,StringComparison.OrdinalIgnoreCase)?"Maps/"+file.Substring(custom.Length).Replace('\\','/'):Path.GetFileName(file);
                using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))records.Add(relative.ToLowerInvariant()+":"+BitConverter.ToString(sha.ComputeHash(stream)));
            }
            records.Sort(StringComparer.Ordinal);foreach(string record in records)text.Append(record).Append('\n');
            return StableNode.Hash(Encoding.UTF8.GetBytes(text.ToString()));
        }
    }
}
