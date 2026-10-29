using System;
using System.IO;

namespace TldMp
{
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
