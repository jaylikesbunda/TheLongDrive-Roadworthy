using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TldMp
{
    // Unity-free rules for the F11 feedback bundle: where it goes, what it is called,
    // which diagnostic files are included, the debounce and the info.txt text.
    internal static class FeedbackPolicy
    {
        internal const double DebounceSeconds=5.0;
        internal const long MaxDiagnosticBytes=50L*1024*1024;
        internal static readonly TimeSpan DiagnosticWindow=TimeSpan.FromHours(2);
        internal const string InfoEntry="info.txt";

        internal struct FileCandidate
        {
            public string Name;public long Length;public DateTime LastWriteUtc;
        }

        internal static string FolderPath(string userProfile)
        {
            if(String.IsNullOrEmpty(userProfile))throw new ArgumentException("userProfile");
            return Path.Combine(userProfile,"Documents","TheLongDrive","Feedback");
        }

        // tld-feedback-yyyyMMdd-HHmmss.zip, in the local time of the player.
        internal static string FileName(DateTime localTime)
        {
            return "tld-feedback-"+localTime.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+".zip";
        }

        // Written first under this name and renamed when complete, so a half-written
        // bundle never looks finished.
        internal static string PartialName(string zipName)
        {
            return zipName+".partial";
        }

        // True on a fresh F11 press that is outside the debounce window.
        // lastAccepted is the time of the last accepted press, or double.NegativeInfinity.
        internal static bool ShouldTrigger(bool pressed,double now,double lastAccepted)
        {
            if(!pressed)return false;
            return now-lastAccepted>=DebounceSeconds;
        }

        internal static string EntryName(bool fromUnityLog,string fileName)
        {
            return (fromUnityLog?"unity-log/":"diagnostics/")+fileName;
        }

        // Files changed in the last two hours, newest first, skipping any file that
        // would take the total past the cap. Names with path parts are refused.
        internal static List<FileCandidate> SelectDiagnostics(IEnumerable<FileCandidate> files,DateTime utcNow)
        {
            var result=new List<FileCandidate>();
            long total=0;
            var recent=(files??new FileCandidate[0]).Where(f=>f.Name!=null && f.Name.Length>0 && f.Name.IndexOfAny(new[]{'/','\\'})<0 && f.Length>=0 && utcNow-f.LastWriteUtc<=DiagnosticWindow)
                .OrderByDescending(f=>f.LastWriteUtc).ThenBy(f=>f.Name,StringComparer.Ordinal);
            foreach(var file in recent)
            {
                if(file.Length>MaxDiagnosticBytes-total)continue;
                total+=file.Length;result.Add(file);
            }
            return result;
        }

        // info.txt body. Values are supplied by the Unity side; this only formats them.
        internal static string InfoText(string buildIdentity,string unityVersion,string operatingSystem,int screenWidth,int screenHeight,float drawDistance,bool mirrors,string session,string patchDisplay,DateTime utcNow)
        {
            var sb=new StringBuilder();
            sb.Append("The Long Drive feedback bundle").Append('\n');
            sb.Append("created-utc: ").Append(utcNow.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("build-identity: ").Append(buildIdentity??"unknown").Append('\n');
            sb.Append("patch: ").Append(String.IsNullOrEmpty(patchDisplay)?"unknown":patchDisplay.Trim()).Append('\n');
            sb.Append("unity-version: ").Append(unityVersion??"unknown").Append('\n');
            sb.Append("os: ").Append(operatingSystem??"unknown").Append('\n');
            sb.Append("screen: ").Append(screenWidth.ToString(CultureInfo.InvariantCulture)).Append('x').Append(screenHeight.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("draw-distance: ").Append(drawDistance.ToString("0.###",CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("mirrors: ").Append(mirrors?"on":"off").Append('\n');
            sb.Append("session: ").Append(session??"unknown").Append('\n');
            return sb.ToString();
        }
    }
}
