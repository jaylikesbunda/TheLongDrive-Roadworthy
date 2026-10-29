using System;
using TldMp;

public static class NativeSpawnSafetyTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception("FAIL: " + message);
    }

    static void WaitsForSupportThenPhysicsThenLive()
    {
        var safety = new NativeSpawnSafety();
        safety.Begin(false, true, true);
        Check(safety.Evaluate(true, false, true, false, false, true, true) == NativeSpawnSafetyAction.Hold,
            "a physics step and Live cannot release before support at the intended spawn");
        Check(!safety.HasSpawnSupport && !safety.HasPhysicsOpportunity,
            "unsupported spawn position is not treated as ready");
        Check(safety.Evaluate(true, false, true, false, true, true, true) == NativeSpawnSafetyAction.Hold,
            "support first observed in this tick still needs a later physics opportunity");
        Check(safety.Evaluate(true, false, true, false, true, true, false) == NativeSpawnSafetyAction.Hold,
            "physics opportunity is recorded while waiting for Live");
        Check(safety.HasPhysicsOpportunity, "post-support physics opportunity was recorded");
        Check(safety.Evaluate(true, false, true, false, false, true, true) == NativeSpawnSafetyAction.Hold,
            "support disappearing before Live clears collider and physics readiness");
        Check(!safety.HasSpawnSupport && !safety.HasPhysicsOpportunity,
            "lost support is not retained as stale readiness");
        Check(safety.Evaluate(true, false, true, false, true, true, true) == NativeSpawnSafetyAction.Hold,
            "returning support requires a fresh physics opportunity");
        Check(safety.Evaluate(true, false, true, false, true, true, false) == NativeSpawnSafetyAction.Hold,
            "fresh physics opportunity is recorded before Live");
        Check(safety.Evaluate(true, false, true, false, true, false, true) == NativeSpawnSafetyAction.Release,
            "Live releases after support and a physics opportunity");
        Check(!safety.Active, "release ends the temporary hold");
    }

    static void AbortRestoresCapturedFlags()
    {
        var safety = new NativeSpawnSafety();
        safety.Begin(true, false, true);
        Check(safety.Evaluate(true, false, true, false, false, false, false) == NativeSpawnSafetyAction.Hold,
            "pending join requests the hold");
        Check(safety.CapturedIsKinematic && !safety.CapturedUseGravity,
            "pre-hold Rigidbody flags are retained for abort/reset restoration");
        Check(safety.CapturedDetectCollisions,
            "pre-hold contact state is retained so the release restores it");
        Check(safety.Evaluate(false, false, true, false, false, false, false) == NativeSpawnSafetyAction.Restore,
            "join cancellation restores the pre-hold flags");
        Check(!safety.Active, "cancel ends the hold");
        Check(safety.Abort() == NativeSpawnSafetyAction.None, "repeated abort is harmless");
    }

    static void NativeTransitionsRemainInControl()
    {
        var safety = new NativeSpawnSafety();
        safety.Begin(false, true, true);
        Check(safety.Evaluate(true, false, true, true, false, false, false) == NativeSpawnSafetyAction.Restore,
            "entering a seat ends the temporary hold and restores flags");

        safety.Begin(false, true, true);
        Check(safety.Evaluate(true, false, false, false, false, false, false) == NativeSpawnSafetyAction.Restore,
            "native death ends the temporary hold and restores flags");

        Check(safety.Evaluate(true, false, true, false, false, false, false) == NativeSpawnSafetyAction.None,
            "inactive policy cannot reassert a movement lock");
    }

    static void BodyReplacementAndReconnectResetCapture()
    {
        var safety = new NativeSpawnSafety();
        safety.Begin(false, true, true);
        Check(safety.Evaluate(true, false, true, false, false, false, false) == NativeSpawnSafetyAction.Hold,
            "first body enters the hold");
        bool rejectedReplacement = false;
        try { safety.Begin(true, false, true); }
        catch (InvalidOperationException) { rejectedReplacement = true; }
        Check(rejectedReplacement, "replacement cannot overwrite flags captured for the old body");
        Check(safety.Abort() == NativeSpawnSafetyAction.Restore,
            "body replacement first restores the old body's captured flags");

        safety.Begin(true, false, false);
        Check(safety.Evaluate(false, false, true, false, false, false, false) == NativeSpawnSafetyAction.Restore,
            "reconnect cancellation restores the new body's own captured flags");
        Check(safety.CapturedIsKinematic && !safety.CapturedUseGravity && !safety.CapturedDetectCollisions,
            "new connection captured its replacement body's flags independently, including disabled contacts");
    }

    public static void Main()
    {
        WaitsForSupportThenPhysicsThenLive();
        AbortRestoresCapturedFlags();
        NativeTransitionsRemainInControl();
        BodyReplacementAndReconnectResetCapture();
        Console.WriteLine("PASS: Native spawn safety policy (" + checks + " checks).");
    }
}
