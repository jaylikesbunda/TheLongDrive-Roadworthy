using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace TldMp
{
    // F11 feedback bundle. Created by StableGameBridge.Bootstrap. The main thread only
    // reads settings and paths; a background thread writes the zip. Rules (name,
    // debounce, diagnostic selection, info.txt text) live in FeedbackPolicy.
    public sealed class FeedbackHotkey:MonoBehaviour
    {
        const string DiagnosticDirVariable="TLD_MP_DIAGNOSTIC_DIR";
        sealed class Job {public string LogPath,DiagnosticDir,Info;public DateTime Local,Utc;}
        readonly object gate=new object();
        double lastAccepted=double.NegativeInfinity;
        volatile bool busy;bool inputUnavailable;string finished;

        void Update()
        {
            if(inputUnavailable)return;
            bool pressed;
            try{pressed=UnityEngine.Input.GetKeyDown(KeyCode.F11);}
            catch(InvalidOperationException){inputUnavailable=true;return;}
            string message=TakeFinished();
            if(message!=null)Debug.Log(message);
            if(!FeedbackPolicy.ShouldTrigger(pressed,UnityEngine.Time.realtimeSinceStartup,lastAccepted))return;
            if(busy){Debug.Log("[TLD MP feedback] A feedback bundle is still being written.");return;}
            lastAccepted=UnityEngine.Time.realtimeSinceStartup;
            var job=Snapshot();
            busy=true;
            var thread=new Thread(delegate(){Write(job);});
            thread.IsBackground=true;thread.Name="TLD feedback bundle";thread.Start();
        }

        // Main thread: copy the values the worker needs. Nothing here touches the disk.
        static Job Snapshot()
        {
            var local=DateTime.Now;var utc=DateTime.UtcNow;
            string logPath=null;
            try{logPath=UnityEngine.Application.consoleLogPath;}catch(Exception){}
            float drawDistance=0;bool mirrors=false;
            try{var s=settingsscript.s;if(s!=null && s.S!=null){drawDistance=s.S.FDrawDistance;mirrors=s.S.BMirrors;}}catch(Exception){}
            string info=FeedbackPolicy.InfoText(BuildIdentity.Sources,UnityEngine.Application.unityVersion,UnityEngine.SystemInfo.operatingSystem,
                UnityEngine.Screen.width,UnityEngine.Screen.height,drawDistance,mirrors,StableGameBridge.SessionRole(),PatchInfo.Display,utc);
            return new Job{LogPath=logPath,DiagnosticDir=Environment.GetEnvironmentVariable(DiagnosticDirVariable),Info=info,Local=local,Utc=utc};
        }

        // Worker thread.
        void Write(Job job)
        {
            string result;
            try{result=WriteBundle(job);}
            catch(Exception e){result="[TLD MP feedback] Could not write the feedback bundle: "+e.GetType().Name+": "+e.Message;}
            lock(gate)finished=result;
            busy=false;
        }

        string TakeFinished()
        {
            lock(gate){string value=finished;finished=null;return value;}
        }

        static string WriteBundle(Job job)
        {
            string profile=Environment.GetEnvironmentVariable("USERPROFILE");
            if(String.IsNullOrEmpty(profile))profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string folder=FeedbackPolicy.FolderPath(profile);
            Directory.CreateDirectory(folder);
            string zipName=FeedbackPolicy.FileName(job.Local);
            string partial=Path.Combine(folder,FeedbackPolicy.PartialName(zipName));
            string finalPath=Path.Combine(folder,zipName);
            using(var file=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.Read))
            using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
            {
                if(!String.IsNullOrEmpty(job.LogPath) && File.Exists(job.LogPath))
                    AddFile(zip,FeedbackPolicy.EntryName(true,Path.GetFileName(job.LogPath)),job.LogPath);
                if(!String.IsNullOrEmpty(job.DiagnosticDir) && Directory.Exists(job.DiagnosticDir))
                {
                    var candidates=new List<FeedbackPolicy.FileCandidate>();
                    foreach(var info in new DirectoryInfo(job.DiagnosticDir).GetFiles())
                        candidates.Add(new FeedbackPolicy.FileCandidate{Name=info.Name,Length=info.Length,LastWriteUtc=info.LastWriteTimeUtc});
                    foreach(var pick in FeedbackPolicy.SelectDiagnostics(candidates,job.Utc))
                        AddFile(zip,FeedbackPolicy.EntryName(false,pick.Name),Path.Combine(job.DiagnosticDir,pick.Name));
                }
                using(var writer=new StreamWriter(zip.CreateEntry(FeedbackPolicy.InfoEntry).Open(),new System.Text.UTF8Encoding(false)))writer.Write(job.Info);
            }
            File.Move(partial,finalPath);
            return "[TLD MP feedback] Saved "+finalPath;
        }

        // Open with FileShare.ReadWrite so the running game's log can be read. A file
        // that disappears or is locked is skipped; the rest of the bundle still ships.
        static void AddFile(ZipArchive zip,string entryName,string path)
        {
            try
            {
                using(var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                using(var target=zip.CreateEntry(entryName).Open())
                    source.CopyTo(target);
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
    }
}
