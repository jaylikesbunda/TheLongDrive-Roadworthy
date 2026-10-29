using System;
using TldMp;
using Unity.Profiling;

internal static class ProfilerSlotPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    static void Main()
    {
        Check(ProfilerSlotPolicy.SlotCount == 13, "unexpected extra-slot count");
        Check(ProfilerSlotPolicy.Matches(11, "Render", "Camera.Render", ProfilerMarkerDataUnit.TimeNanoseconds), "Camera.Render not matched");
        Check(!ProfilerSlotPolicy.Matches(11, "Render", "Camera.Render", ProfilerMarkerDataUnit.Count), "Camera.Render accepted wrong unit");
        Check(ProfilerSlotPolicy.Matches(12, "Render", "Objects Culled", ProfilerMarkerDataUnit.Count), "Objects Culled count not matched");
        Check(!ProfilerSlotPolicy.Matches(12, "Render", "Objects Culled", ProfilerMarkerDataUnit.TimeNanoseconds), "Objects Culled accepted wrong unit");
        Check(ProfilerSlotPolicy.Matches(13, "Render", "Shadow Casters", ProfilerMarkerDataUnit.Count), "Shadow Casters not matched");
        Check(ProfilerSlotPolicy.Matches(14, "Render", "Render.OpaqueGeometry", ProfilerMarkerDataUnit.TimeNanoseconds), "OpaqueGeometry not matched");
        Check(ProfilerSlotPolicy.Matches(15, "Render", "Render.TransparentGeometry", ProfilerMarkerDataUnit.TimeNanoseconds), "TransparentGeometry not matched");
        Check(ProfilerSlotPolicy.Matches(16, "Render", "Render.Shadow", ProfilerMarkerDataUnit.TimeNanoseconds), "Shadow pass not matched");
        Check(!ProfilerSlotPolicy.Matches(16, "Render", "Shadow Casters", ProfilerMarkerDataUnit.TimeNanoseconds), "Shadow Casters matched the pass slot");
        Check(!ProfilerSlotPolicy.Matches(16, "Render", "Shadow Distance", ProfilerMarkerDataUnit.TimeNanoseconds), "Shadow Distance matched the pass slot");
        Check(ProfilerSlotPolicy.Matches(17, "Render", "Gfx.PresentFrame", ProfilerMarkerDataUnit.TimeNanoseconds), "PresentFrame not matched");
        Check(ProfilerSlotPolicy.Matches(18, "Render", "Gfx.WaitForRenderThread", ProfilerMarkerDataUnit.TimeNanoseconds), "WaitForRenderThread not matched");
        Check(ProfilerSlotPolicy.Matches(19, "Render", "Gfx.ProcessDeviceOperations", ProfilerMarkerDataUnit.TimeNanoseconds), "ProcessDeviceOperations not matched");
        Check(ProfilerSlotPolicy.Matches(20, "Memory", "GC Allocated In Frame", ProfilerMarkerDataUnit.Bytes), "allocated bytes not matched");
        Check(!ProfilerSlotPolicy.Matches(20, "Memory", "GC Allocated In Frame", ProfilerMarkerDataUnit.Count), "allocated bytes accepted wrong unit");
        Check(ProfilerSlotPolicy.Matches(21, "Render", "Visible Batches", ProfilerMarkerDataUnit.Count), "Visible Batches not matched");
        Check(ProfilerSlotPolicy.Matches(22, "Render", "Shadow Draw Calls", ProfilerMarkerDataUnit.Count), "Shadow Draw Calls not matched");
        Check(ProfilerSlotPolicy.Matches(23, "Render", "Gfx.WaitForGfxCommandsFromMainThread", ProfilerMarkerDataUnit.TimeNanoseconds), "main-thread command wait not matched");
        Check(!ProfilerSlotPolicy.Matches(10, "Render", "Camera.Render", ProfilerMarkerDataUnit.TimeNanoseconds), "base slots handled by policy");
        Check(!ProfilerSlotPolicy.Matches(11, "Render", null, ProfilerMarkerDataUnit.TimeNanoseconds), "null name matched");
        Console.WriteLine("PASS: " + checks + " profiler slot/unit policy assertions");
    }
}
