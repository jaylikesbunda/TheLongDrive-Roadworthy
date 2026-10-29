using System;
using TldMp;
using UnityEngine;

internal static class NativeStoreTextTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        Check(NativeStoreText.Name(null) == "?", "a missing name is unknown");
        Check(NativeStoreText.Name("") == "?", "an empty name is unknown");
        Check(NativeStoreText.Name("Crate (Clone)") == "Crate", "the clone suffix is stripped");
        Check(NativeStoreText.Name("Tin Can") == "Tin_Can", "spaces become underscores");
        Check(NativeStoreText.Name("a\tb\nc") == "a_b_c", "other whitespace becomes an underscore");
        string longName = new string('x', 60);
        Check(NativeStoreText.Name(longName).Length == NativeStoreText.MaxNameLength, "a name is capped at forty characters");
        Check(NativeStoreText.Name("Unnamed").Length == 7, "a short name is unchanged");

        Check(NativeStoreText.Position(new Vector3(1.24f, 2f, -3.46f)) == "(1.2,2.0,-3.5)", "positions use one decimal and invariant punctuation");
        Check(NativeStoreText.Local(null, Vector3.zero) == "?", "a missing parent is unknown");

        Console.WriteLine("PASS: " + checks + " native store text assertions.");
        return 0;
    }
}
