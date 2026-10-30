using System;
using System.Collections.Generic;
using System.Linq;
using TldMp;

internal static class FeedbackPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        Folder();
        Naming();
        Debounce();
        Selection();
        InfoFormat();
        Console.WriteLine("PASS: " + checks + " feedback policy assertions; bundle path and name, 5 s debounce, 2 h / 50 MB diagnostic selection, info.txt content.");
        return 0;
    }

    static void Folder()
    {
        Check(FeedbackPolicy.FolderPath(@"C:\Users\player") == @"C:\Users\player\Documents\TheLongDrive\Feedback", "bundle folder is Documents\\TheLongDrive\\Feedback under the profile");
        bool threw = false;
        try { FeedbackPolicy.FolderPath(""); } catch (ArgumentException) { threw = true; }
        Check(threw, "an empty profile is refused");
    }

    static void Naming()
    {
        var name = FeedbackPolicy.FileName(new DateTime(2026, 3, 4, 5, 6, 7));
        Check(name == "tld-feedback-20260304-050607.zip", "zip name is tld-feedback-yyyyMMdd-HHmmss.zip");
        Check(FeedbackPolicy.PartialName(name) == name + ".partial", "partial name is the zip name plus .partial");
        Check(FeedbackPolicy.EntryName(true, "Player.log") == "unity-log/Player.log", "unity log entry sits under unity-log/");
        Check(FeedbackPolicy.EntryName(false, "host-snapshot.deflate") == "diagnostics/host-snapshot.deflate", "diagnostic entry sits under diagnostics/");
    }

    static void Debounce()
    {
        Check(FeedbackPolicy.ShouldTrigger(true, 100.0, double.NegativeInfinity), "the first press is accepted");
        Check(!FeedbackPolicy.ShouldTrigger(false, 100.0, double.NegativeInfinity), "no press, no bundle");
        Check(!FeedbackPolicy.ShouldTrigger(true, 104.9, 100.0), "a second press within 5 s is ignored");
        Check(FeedbackPolicy.ShouldTrigger(true, 105.0, 100.0), "a press exactly 5 s later is accepted");
    }

    static void Selection()
    {
        var now = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);
        var files = new List<FeedbackPolicy.FileCandidate>
        {
            new FeedbackPolicy.FileCandidate { Name = "old.deflate", Length = 10, LastWriteUtc = now.AddHours(-3) },
            new FeedbackPolicy.FileCandidate { Name = "fresh-a.txt", Length = 100, LastWriteUtc = now.AddMinutes(-10) },
            new FeedbackPolicy.FileCandidate { Name = "fresh-b.txt", Length = 200, LastWriteUtc = now.AddMinutes(-30) },
            new FeedbackPolicy.FileCandidate { Name = "edge.txt", Length = 1, LastWriteUtc = now.AddHours(-2) },
            new FeedbackPolicy.FileCandidate { Name = "..\\escape.txt", Length = 1, LastWriteUtc = now.AddMinutes(-1) },
            new FeedbackPolicy.FileCandidate { Name = "sub/child.txt", Length = 1, LastWriteUtc = now.AddMinutes(-1) },
            new FeedbackPolicy.FileCandidate { Name = "", Length = 1, LastWriteUtc = now.AddMinutes(-1) },
        };
        var chosen = FeedbackPolicy.SelectDiagnostics(files, now);
        Check(chosen.Select(f => f.Name).SequenceEqual(new[] { "fresh-a.txt", "fresh-b.txt", "edge.txt" }), "only files from the last 2 hours, newest first, no path-like names");

        // Cap: a file that would pass 50 MB is skipped, smaller newer files still fit.
        long big = FeedbackPolicy.MaxDiagnosticBytes - 1000;
        var capped = new List<FeedbackPolicy.FileCandidate>
        {
            new FeedbackPolicy.FileCandidate { Name = "big-new.bin", Length = big, LastWriteUtc = now.AddMinutes(-1) },
            new FeedbackPolicy.FileCandidate { Name = "too-big.bin", Length = 2000, LastWriteUtc = now.AddMinutes(-2) },
            new FeedbackPolicy.FileCandidate { Name = "small.txt", Length = 900, LastWriteUtc = now.AddMinutes(-3) },
            new FeedbackPolicy.FileCandidate { Name = "tiny.txt", Length = 100, LastWriteUtc = now.AddMinutes(-4) },
        };
        var cappedChosen = FeedbackPolicy.SelectDiagnostics(capped, now);
        long total = cappedChosen.Sum(f => f.Length);
        Check(total <= FeedbackPolicy.MaxDiagnosticBytes, "selected diagnostics never exceed 50 MB");
        Check(cappedChosen.Select(f => f.Name).SequenceEqual(new[] { "big-new.bin", "small.txt", "tiny.txt" }), "an oversize file is skipped and the rest still fit");

        Check(FeedbackPolicy.SelectDiagnostics(null, now).Count == 0, "no files gives an empty selection");
    }

    static void InfoFormat()
    {
        var text = FeedbackPolicy.InfoText("ABC123", "2022.3.10f1", "Windows 11", 1920, 1080, 2500.5f, false, "host, connected", "v0.1.2 Roadworthy", new DateTime(2026, 3, 4, 5, 6, 7));
        Check(text.Contains("build-identity: ABC123\n"), "info.txt names the build identity");
        Check(text.Contains("patch: v0.1.2 Roadworthy\n"), "info.txt names the patch version");
        Check(text.Contains("unity-version: 2022.3.10f1\n"), "info.txt names the Unity version");
        Check(text.Contains("os: Windows 11\n"), "info.txt names the OS");
        Check(text.Contains("screen: 1920x1080\n"), "info.txt names the screen size");
        Check(text.Contains("draw-distance: 2500.5\n"), "info.txt names the draw distance with invariant culture");
        Check(text.Contains("mirrors: off\n"), "info.txt names the mirrors setting");
        Check(text.Contains("session: host, connected\n"), "info.txt names the session role");
        Check(text.Contains("created-utc: 2026-03-04 05:06:07\n"), "info.txt is stamped");
        var blank = FeedbackPolicy.InfoText(null, null, null, 0, 0, 0f, true, null, null, new DateTime(2026, 1, 1));
        Check(blank.Contains("build-identity: unknown\n") && blank.Contains("patch: unknown\n") && blank.Contains("mirrors: on\n"), "missing values are shown as unknown");
    }
}
