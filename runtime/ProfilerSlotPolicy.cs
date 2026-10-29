using System;
using Unity.Profiling;

namespace TldMp
{
    // Pure policy for the extra ProfilerRecorder slots. Kept free of Unity
    // runtime state so the unit/name matching can be unit-tested without a
    // player. Discovery still decides whether a counter exists.
    public static class ProfilerSlotPolicy
    {
        public const int FirstSlot = 11;
        public const int LastSlot = 23;
        public const int SlotCount = LastSlot - FirstSlot + 1;

        // One fixed label per extra slot. Slot indices are stable across builds
        // so old and new reports can be diffed.
        public static readonly string[] Labels = new string[]
        {
            "Camera.Render",
            "Render/Objects Culled",
            "Render/Shadow Casters",
            "Render.OpaqueGeometry",
            "Render.TransparentGeometry",
            "Render.Shadow",
            "Gfx.PresentFrame",
            "Gfx.WaitForRenderThread",
            "Gfx.ProcessDeviceOperations",
            "GC allocated bytes",
            "Render/Visible Batches",
            "Render/Shadow Draw Calls",
            "Gfx.WaitForGfxCommandsFromMainThread"
        };

        static bool Has(string value, string token)
        {
            return value != null && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool Matches(int slot, string category, string name, ProfilerMarkerDataUnit unit)
        {
            category = category ?? String.Empty;
            name = name ?? String.Empty;
            switch (slot)
            {
                case 11:
                    return unit == ProfilerMarkerDataUnit.TimeNanoseconds &&
                        ((Has(name, "camera") && Has(name, "render")) || Has(name, "camera.render"));
                case 12:
                    return unit == ProfilerMarkerDataUnit.Count &&
                        (Has(name, "objects culled") || Has(name, "objectsculled") ||
                         Has(name, "culled objects") || Has(name, "culledobjects"));
                case 13:
                    return unit == ProfilerMarkerDataUnit.Count &&
                        (Has(name, "shadow casters") || Has(name, "shadowcasters"));
                case 14:
                    return unit == ProfilerMarkerDataUnit.TimeNanoseconds &&
                        (Has(name, "opaquegeometry") || Has(name, "opaque geometry"));
                case 15:
                    return unit == ProfilerMarkerDataUnit.TimeNanoseconds &&
                        (Has(name, "transparentgeometry") || Has(name, "transparent geometry"));
                case 16:
                    return unit == ProfilerMarkerDataUnit.TimeNanoseconds &&
                        Has(category, "render") && Has(name, "shadow") &&
                        !Has(name, "casters") && !Has(name, "distance") && !Has(name, "draw call");
                case 17:
                    return unit == ProfilerMarkerDataUnit.TimeNanoseconds &&
                        (Has(name, "presentframe") || Has(name, "present frame"));
                case 18:
                    return unit == ProfilerMarkerDataUnit.TimeNanoseconds &&
                        (Has(name, "waitforrenderthread") || Has(name, "wait for render thread") ||
                         Has(name, "waitforgfxcommandsfromrenderthread") ||
                         Has(name, "wait for gfx commands from render thread"));
                case 19:
                    return unit == ProfilerMarkerDataUnit.TimeNanoseconds &&
                        (Has(name, "processdeviceoperations") || Has(name, "process device operations"));
                case 20:
                    return unit == ProfilerMarkerDataUnit.Bytes &&
                        (Has(category, "memory") || Has(category, "gc")) && Has(name, "alloc");
                case 21:
                    return unit == ProfilerMarkerDataUnit.Count &&
                        (Has(name, "visible batches") || Has(name, "visiblebatches"));
                case 22:
                    return unit == ProfilerMarkerDataUnit.Count &&
                        (Has(name, "shadow draw calls") || Has(name, "shadowdrawcalls"));
                case 23:
                    return unit == ProfilerMarkerDataUnit.TimeNanoseconds &&
                        (Has(name, "waitforgfxcommandsfrommainthread") ||
                         Has(name, "wait for gfx commands from main thread"));
                default:
                    return false;
            }
        }
    }
}
