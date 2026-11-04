using System;
using System.IO;

namespace TldMp
{
    // No Unity API or file I/O on callers. The single background worker owns disk.
    internal static class DiagnosticFileLog
    {
        const int MaxLines=256,MaxChars=2*1024*1024,MaxLine=128*1024;
        static readonly object gate=new object();
        static readonly System.Collections.Generic.Queue<string> queue=new System.Collections.Generic.Queue<string>();
        static readonly System.Threading.AutoResetEvent wake=new System.Threading.AutoResetEvent(false);
        static readonly string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TheLongDrive-Roadworthy","diagnostics");
        internal static readonly string CurrentPath=Path.Combine(directory,"runtime-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff",System.Globalization.CultureInfo.InvariantCulture)+".log");
        static readonly System.Threading.Thread worker=Start();
        static int chars,dropped;static volatile bool failed;
        static System.Threading.Thread Start()
        {
            var thread=new System.Threading.Thread(WriteLoop);thread.IsBackground=true;thread.Name="Roadworthy diagnostic writer";thread.Start();return thread;
        }
        internal static void Log(object message)
        {
            if(failed)return;
            string text=message as string ?? (message==null?"":message.ToString());
            if(text.Length>MaxLine){System.Threading.Interlocked.Increment(ref dropped);return;}
            if(!System.Threading.Monitor.TryEnter(gate)){System.Threading.Interlocked.Increment(ref dropped);return;}
            try
            {
                if(queue.Count>=MaxLines || text.Length>MaxChars-chars){System.Threading.Interlocked.Increment(ref dropped);return;}
                queue.Enqueue(text);chars+=text.Length;
            }
            finally{System.Threading.Monitor.Exit(gate);}
            wake.Set();
        }
        static void WriteLoop()
        {
            try
            {
                Directory.CreateDirectory(directory);
                // Bound retained sessions; all paths originate in this dedicated directory.
                var old=new DirectoryInfo(directory).GetFiles("runtime-*.log");
                Array.Sort(old,(a,b)=>b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for(int i=7;i<old.Length;i++)try{old[i].Delete();}catch(IOException){}catch(UnauthorizedAccessException){}
                using(var stream=new FileStream(CurrentPath,FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))
                using(var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false)))
                {
                    writer.WriteLine("Roadworthy asynchronous diagnostics; records retain their own frame/time. Queue cap=256 lines/2M characters; session cap=32MiB. No Unity log callback.");writer.Flush();
                    long written=0;
                    while(true)
                    {
                        wake.WaitOne(250);int lost=System.Threading.Interlocked.Exchange(ref dropped,0);
                        if(lost!=0)writer.WriteLine("[diagnostic-writer] dropped="+lost);
                        for(int n=0;n<MaxLines;n++)
                        {
                            string line;
                            lock(gate){if(queue.Count==0)break;line=queue.Dequeue();chars-=line.Length;}
                            writer.WriteLine(line);written+=System.Text.Encoding.UTF8.GetByteCount(line)+2;
                            if(written>=32*1024*1024){writer.WriteLine("[diagnostic-writer] session size cap reached; further records disabled.");writer.Flush();failed=true;lock(gate){queue.Clear();chars=0;}return;}
                        }
                        writer.Flush();
                    }
                }
            }
            catch{failed=true;lock(gate){queue.Clear();chars=0;}}
        }
    }
    // Diagnostics are on when TLD_MP_DIAGNOSTICS=1 or when the game folder holds
    // tldmp-diagnostics.on (next to TheLongDrive.exe). The file form lets a
    // Steam launch enable profiling without a system environment variable.
    // Read once; delete the file and restart the game to turn it off.
    public static class ModDiagnostics
    {
        public const string FlagName = "tldmp-diagnostics.on";
        public static readonly bool On = Read();

        static bool Read()
        {
            if (Environment.GetEnvironmentVariable("TLD_MP_DIAGNOSTICS") == "1") return true;
            try
            {
                string managed = Path.GetDirectoryName(typeof(ModDiagnostics).Assembly.Location);
                return FlagPresent(managed);
            }
            catch { return false; }
        }

        // The runtime lives in <game>\TheLongDrive_Data\Managed.
        internal static bool FlagPresent(string managedDirectory)
        {
            if (String.IsNullOrEmpty(managedDirectory)) return false;
            DirectoryInfo data = Directory.GetParent(managedDirectory);
            DirectoryInfo game = data == null ? null : data.Parent;
            return game != null && File.Exists(Path.Combine(game.FullName, FlagName));
        }
    }
}
