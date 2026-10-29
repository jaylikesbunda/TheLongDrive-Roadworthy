using System;
using TldMp;

static class SlowFrameDiagnosticsTests
{
    static int assertions;

    static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }

    static bool Has(string value, string part) { return value.IndexOf(part, StringComparison.Ordinal) >= 0; }

    static SlowFrameDiagnostics Create(int capacity = 16)
    {
        return new SlowFrameDiagnostics(new string[] { "Map", "POI", "Road" },
            new string[] { "Update", "FixedScripts", "Physics", "Late", "Renderers", "FinishRendering" }, capacity);
    }

    static void EnvelopeAndInclusiveScopes()
    {
        var d = Create();
        d.AddPhase(7, 0, 100, 150);
        d.AddPhase(7, 1, 160, 180);
        d.AddPhase(7, 1, 185, 200);
        d.AddPhase(7, 2, 210, 250);
        d.Begin(7, 0, 105);
        d.Begin(7, 1, 120);
        d.End(7, 1, 150);
        d.End(7, 0, 170);
        d.CompleteFrame(7, 7);
        string report = d.ReportAndReset(1000, 3, 100);
        Check(Has(report, "frame=7 envelopeMs=150.00"), "Envelope must run from earliest observed start to latest observed end.");
        Check(Has(report, "FixedScripts=35.00ms/2"), "Repeated fixed-phase invocations must be summed and counted.");
        Check(Has(report, "Map total=65.00ms selfObs=35.00ms"), "Parent self time must subtract the observed child interval only.");
        Check(Has(report, "POI total=30.00ms selfObs=30.00ms"), "Child inclusive/self totals must be retained separately.");
        Check(!Has(report, "GPU") && Has(report, "partial observed PlayerLoop envelope"), "Report must identify partial CPU envelope and avoid implying GPU coverage.");
    }

    static void AvailabilityAndThreshold()
    {
        var d = Create();
        d.AddPhase(1, 0, 0, 40);
        d.CompleteFrame(1, 1); // Fixed and Render markers are unavailable.
        d.AddPhase(2, 0, 100, 120);
        d.CompleteFrame(2, 63); // Fixed configured but no callbacks: measured zero; later phases are unexpectedly absent.
        string report = d.ReportAndReset(1000, 3, 30);
        Check(Has(report, "frame=1 envelopeMs=40.00"), "Slow available frame should be included.");
        Check(Has(report, "FixedScripts=unavailable"), "A missing marker must be reported as unavailable.");
        Check(!Has(report, "frame=2"), "Completed frames below threshold must not be reported.");
        Check(Has(report, "slowFrames{"), "Report should remain valid when some completed frames are below threshold.");

        d.AddPhase(3, 0, 200, 240);
        d.CompleteFrame(3, 63);
        report = d.ReportAndReset(1000, 3, 30);
        Check(Has(report, "FixedScripts=0.00ms/0") && Has(report, "Physics=0.00ms/0"),
            "Configured fixed phases with no callback are valid zeroes, not unavailable.");
        Check(Has(report, "Renderers=unavailable(no callback)") && Has(report, "FinishRendering=unavailable(no callback)"),
            "Configured non-fixed phases without callbacks must be unavailable, not false zeroes.");
    }

    static void TopKAndDeterministicTies()
    {
        var d = Create();
        d.AddPhase(4, 0, 0, 50); d.CompleteFrame(4, 1);
        d.AddPhase(3, 0, 100, 150); d.CompleteFrame(3, 1);
        d.AddPhase(5, 0, 170, 210); d.CompleteFrame(5, 1);
        d.AddPhase(6, 0, 200, 280); d.CompleteFrame(6, 1);
        string report = d.ReportAndReset(1000, 2, 1);
        int first = report.IndexOf("frame=6", StringComparison.Ordinal);
        int second = report.IndexOf("frame=3", StringComparison.Ordinal);
        Check(first >= 0 && second > first, "Top K must be ordered by descending envelope, then ascending frame ID for ties.");
        Check(!Has(report, "frame=4") && !Has(report, "frame=5"), "Frames outside top K must be omitted without replacing the current last item.");
        Check(!Has(d.ReportAndReset(1000), "frame="), "Completed rows must be consumed to prevent duplicate reports.");
    }

    static void MismatchAndCrossFrameDiscard()
    {
        var d = Create();
        d.Begin(10, 0, 1);
        d.Begin(10, 1, 5);
        d.End(10, 0, 9); // LIFO mismatch discards both open spans.
        d.End(10, 1, 12);
        d.Begin(11, 2, 20);
        d.End(11, 2, 25);
        d.AddPhase(11, 0, 0, 40);
        d.CompleteFrame(11, 1);
        string report = d.ReportAndReset(1000, 3, 30);
        Check(Has(report, "incompleteScopes=2"), "Mismatched unwind must account for and discard all open spans.");
        Check(Has(report, "Road total=5.00ms"), "A discarded scope must not poison a later frame's fresh scope.");

        d.Begin(12, 0, 30);
        d.Begin(13, 2, 31); // Cross-frame begin invalidates frame 12 scope.
        d.End(13, 2, 35);
        d.AddPhase(13, 0, 100, 140);
        d.CompleteFrame(13, 1);
        report = d.ReportAndReset(1000, 3, 30);
        Check(Has(report, "incompleteScopes=1"), "Cross-frame scope transition must discard and count unfinished spans.");
        Check(Has(report, "Road total=4.00ms"), "Valid scope after cross-frame discard must be retained.");
    }

    static void RingCollisionAndResetPreserveIncomplete()
    {
        var d = Create(2);
        d.AddPhase(20, 0, 0, 40); d.CompleteFrame(20, 1);
        d.AddPhase(22, 0, 100, 140); d.CompleteFrame(22, 1); // Same ring slot overwrites frame 20.
        d.AddPhase(21, 1, 150, 155); // Not complete yet; ReportAndReset must preserve it.
        string report = d.ReportAndReset(1000, 3, 30);
        Check(!Has(report, "frame=20") && Has(report, "frame=22"), "Ring collision must not leak an old frame key.");
        Check(Has(report, "evictedCompleted=1 evictedIncomplete=0"), "Completed frame replacement must be counted separately.");
        d.AddPhase(21, 0, 200, 240); d.CompleteFrame(21, 3);
        report = d.ReportAndReset(1000, 3, 30);
        Check(Has(report, "frame=21"), "An incomplete row must remain available until its completion event.");
        d.AddPhase(30, 0, 0, 40); d.CompleteFrame(30, 1); d.Reset();
        Check(!Has(d.ReportAndReset(1000, 3, 1), "frame="), "Reset must clear all retained rows and scopes.");

        var incomplete = Create(2);
        incomplete.AddPhase(40, 0, 0, 10); // Valid, but not completed.
        incomplete.AddPhase(42, 0, 20, 30); // Same slot evicts frame 40 before completion.
        report = incomplete.ReportAndReset(1000, 3, 1);
        Check(Has(report, "evictedIncomplete=1"), "Uncompleted frame replacement must be counted separately.");
    }

    static void CompletingPreviousFramePreservesCurrentScope()
    {
        var d = Create();
        d.AddPhase(100, 0, 0, 40);
        d.Begin(101, 0, 10);
        d.CompleteFrame(100, 1);
        d.End(101, 0, 25);
        d.AddPhase(101, 0, 50, 90);
        d.CompleteFrame(101, 1);
        string report = d.ReportAndReset(1000, 3, 30);
        Check(Has(report, "frame=100 envelopeMs=40.00"), "Completing the previous frame should retain its own phase row.");
        Check(Has(report, "frame=101 envelopeMs=40.00") && Has(report, "Map total=15.00ms"),
            "Previous-frame completion must not discard a live scope in the current frame.");
    }

    static void OverflowSuppressesOldEndsUntilNextFrame()
    {
        var d = Create();
        for (int i = 0; i < 64; i++) d.Begin(200, 0, i);
        d.Begin(200, 1, 64); // Overflow discards the open stack and suppresses frame 200 observer events.
        for (int i = 0; i < 64; i++) d.End(200, 0, 100 + i);
        d.End(200, 1, 200);
        d.Begin(201, 2, 210);
        d.End(201, 2, 215);
        d.AddPhase(201, 0, 0, 40); d.CompleteFrame(201, 1);
        string report = d.ReportAndReset(1000, 3, 30);
        Check(Has(report, "incompleteScopes=64"), "Overflow must discard and count the existing open stack.");
        Check(Has(report, "Road total=5.00ms"), "Fresh scopes on the next frame must be recorded after old exits are suppressed.");
        Check(!Has(report, "Map total="), "Overflowed scope ends must not be misattributed to a fresh parent.");
    }

    static void SelfTimeNeverNegative()
    {
        var d = Create();
        d.Begin(300, 0, 10);
        d.Begin(300, 1, 0);
        d.End(300, 1, 15);
        d.End(300, 0, 20);
        d.AddPhase(300, 0, 0, 40); d.CompleteFrame(300, 1);
        string report = d.ReportAndReset(1000, 3, 30);
        Check(Has(report, "Map total=10.00ms selfObs=0.00ms"), "Observed child spans must never produce negative self time.");
    }

    static int Main()
    {
        EnvelopeAndInclusiveScopes();
        AvailabilityAndThreshold();
        TopKAndDeterministicTies();
        MismatchAndCrossFrameDiscard();
        RingCollisionAndResetPreserveIncomplete();
        CompletingPreviousFramePreservesCurrentScope();
        OverflowSuppressesOldEndsUntilNextFrame();
        SelfTimeNeverNegative();
        Console.WriteLine("SlowFrameDiagnosticsTests passed assertions=" + assertions);
        return 0;
    }
}
