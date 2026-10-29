using System;
using TldMp;
using UnityEngine;

internal static class NativeSeatSafetyTests
{
    static int assertions;

    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    static void Main()
    {
        var safety = new NativeSeatSafety();
        Vector3 nativeOffset = new Vector3(0.01f, 0.2f, -0.03f);
        Check(safety.Capture(true, 10, 20, 30, 30, false, nativeOffset),
            "capture accepts the native parent and Rigidbody-free seated state");
        Check(safety.CapturedLocalOffset == nativeOffset,
            "capture preserves the root local offset exactly");

        NativeSeatSafety.Reason reason;
        var repair = safety.Evaluate(true, true, 10, 20, 30, 30, false,
            nativeOffset, out reason);
        Check(repair == NativeSeatSafety.Repair.None && reason == NativeSeatSafety.Reason.None,
            "valid native seated state is left alone");

        repair = safety.Evaluate(true, true, 10, 20, 30, 99, false,
            nativeOffset, out reason);
        Check((repair & NativeSeatSafety.Repair.Parent) != 0 &&
            (repair & NativeSeatSafety.Repair.Offset) != 0 &&
            reason == NativeSeatSafety.Reason.ParentDrift,
            "wrong root parent requests reparent and restoration to captured offset");

        repair = safety.Evaluate(true, true, 10, 20, 30, 30, true,
            nativeOffset, out reason);
        Check((repair & NativeSeatSafety.Repair.IndependentRigidbody) != 0 &&
            reason == NativeSeatSafety.Reason.IndependentRigidbody,
            "only a genuinely seated matching player requests removal of its independent body");

        repair = safety.Evaluate(true, true, 10, 20, 30, 30, false,
            nativeOffset + new Vector3(0.1f, 0f, 0f), out reason);
        Check(repair == NativeSeatSafety.Repair.Offset &&
            reason == NativeSeatSafety.Reason.RootOffsetDrift,
            "root drift beyond tolerance requests offset restoration");

        repair = safety.Evaluate(false, true, 10, 20, 30, 99, true,
            nativeOffset, out reason);
        Check(repair == NativeSeatSafety.Repair.None && !safety.HasSnapshot,
            "native exit clears state and cannot reattach or recreate physics");

        Check(!safety.Capture(true, 10, 20, 30, 99, false, nativeOffset),
            "invalid parent cannot seed a guessed recovery offset");
        Check(!safety.Capture(true, 10, 20, 30, 30, true, nativeOffset),
            "body presence cannot seed a recovery baseline");
        Check(!safety.Capture(true, 10, 20, 30, 30, false,
            new Vector3(Single.NaN, 0f, 0f)) && !safety.HasSnapshot,
            "non-finite native offset cannot seed a recovery baseline");

        safety.Capture(true, 10, 20, 30, 30, false, nativeOffset);
        repair = safety.Evaluate(true, true, 10, 20, 30, 30, false,
            new Vector3(0f, Single.PositiveInfinity, 0f), out reason);
        Check(repair == NativeSeatSafety.Repair.Offset &&
            reason == NativeSeatSafety.Reason.InvalidRootOffset,
            "non-finite observed root offset requests restoration from the valid captured offset");

        Check(NativeSeatSafety.ExitReason(false, true) == "input" &&
            NativeSeatSafety.ExitReason(true, false) == "forced" &&
            NativeSeatSafety.ExitReason(false, false) == "native",
            "exit diagnostics distinguish input, forced, and other native paths");
        Check(safety.ShouldLogExit(false, true) && !safety.ShouldLogExit(false, true) &&
            safety.ShouldLogExit(true, false) && safety.ShouldLogExit(false, false),
            "exit diagnostics emit once per bounded native exit category");
        Check(safety.ShouldLog(NativeSeatSafety.Reason.ParentDrift) &&
            !safety.ShouldLog(NativeSeatSafety.Reason.ParentDrift) &&
            safety.ShouldLog(NativeSeatSafety.Reason.RootOffsetDrift) &&
            safety.ShouldLog(NativeSeatSafety.Reason.IndependentRigidbody) &&
            safety.ShouldLog(NativeSeatSafety.Reason.InvalidRootOffset) &&
            !safety.ShouldLog(NativeSeatSafety.Reason.None),
            "drift diagnostics emit once per bounded reason");

        safety.Capture(true, 10, 20, 30, 30, false, nativeOffset);
        repair = safety.Evaluate(true, false, 10, 20, 30, 99, false,
            nativeOffset, out reason);
        Check(repair == NativeSeatSafety.Repair.None && !safety.HasSnapshot,
            "destroyed seat invalidates the snapshot without a repair");

        Console.WriteLine("PASS: " + assertions + " native passenger seat safety assertions");
    }
}
