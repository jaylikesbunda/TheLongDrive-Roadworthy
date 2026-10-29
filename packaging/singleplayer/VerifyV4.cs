using System;
using System.IO;

// VerifyV4 <v3.dll> <v4.dll>
// Exit 0 = the candidate is exactly v3 plus the contracted pass-3 edits.
public static class VerifyV4
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 && args.Length != 3)
            {
                Console.Error.WriteLine("Usage: VerifyV4.exe <v3.dll> <v4.dll> [expected-v3-sha256]");
                return 2;
            }
            if (args.Length == 3) Checks.SetBaseline(args[2]);
            string before = Path.GetFullPath(args[0]);
            string after = Path.GetFullPath(args[1]);
            if (!File.Exists(before)) { Console.Error.WriteLine("v3 not found: " + before); return 2; }
            if (!File.Exists(after)) { Console.Error.WriteLine("v4 not found: " + after); return 2; }
            string hash = Checks.Hash(before);
            if (hash != Checks.V3Hash)
            {
                Console.Error.WriteLine("Refusing: first argument is not the v3 assembly (SHA-256 " + hash + ").");
                return 1;
            }
            Console.WriteLine(Invariants.Run(before, after));
            Console.WriteLine("v3 SHA-256: " + hash);
            Console.WriteLine("v4 SHA-256: " + Checks.Hash(after));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
    }
}
