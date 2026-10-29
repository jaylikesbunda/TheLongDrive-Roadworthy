using System;
using UnityEngine;

namespace TldMp
{
    // Four narrow native fixes. Each policy is Unity-free so it can be tested offline.
    //   ParkedCarSleep:  carscript.Update skips the velocity write while the body sleeps
    //                    or when the value is unchanged, so parked cars can sleep.
    //   IdlePourPause:   tankcapscript.Update pauses an idle pour particle system with no
    //                    particles and resumes it when emission is requested again.
    //   WornDestroyed:   pickupable.OnDestroy drops a destroyed worn item's renderers from
    //                    the camera hide list; makeinvisible.PreRender/PostRender drop null
    //                    renderers before their loops.
    //   SliceGuard:      randomSlice.Slicc calls the slicer through SafeSlice, which logs one
    //                    failure and lets the native caller continue and destroy the object.
    internal static class ParkedCarSleepPolicy
    {
        // True when the velocity write must reach the body.
        internal static bool ShouldWriteVelocity(bool sleeping, float cx, float cy, float cz, float nx, float ny, float nz)
        {
            if (sleeping) return false;
            if (cx == nx && cy == ny && cz == nz) return false;
            return true;
        }
    }

    internal static class IdlePourPausePolicy
    {
        internal const int None = 0, Pause = 1, Resume = 2;

        internal static int Decide(bool emissionRequested, bool paused, int particleCount)
        {
            if (emissionRequested) return paused ? Resume : None;
            return !paused && particleCount == 0 ? Pause : None;
        }
    }

    internal static class WornItemGuard
    {
        // A hide-list entry is removed when its renderer is gone or belongs to the destroyed item.
        internal static bool ShouldDrop(bool rendererNull, bool belongsToItem)
        {
            return rendererNull || belongsToItem;
        }

        internal static void Destroyed(pickupable item)
        {
            var player = mainscript.s == null ? null : mainscript.s.player;
            var hide = player == null ? null : player.wearedRenderer;
            if (item == null || hide == null || hide.R == null || hide.R.Count == 0) return;
            // Items are destroyed constantly while the world streams; walk the small
            // hide list instead of collecting the item's renderers.
            Transform root = item.transform;
            for (int i = hide.R.Count - 1; i >= 0; i--)
            {
                Renderer r = hide.R[i];
                if (ShouldDrop(r == null, r != null && r.transform.IsChildOf(root))) hide.R.RemoveAt(i);
            }
        }

        internal static void PruneHide(makeinvisible hide)
        {
            if (hide == null || hide.R == null) return;
            hide.R.RemoveAll(r => ShouldDrop(r == null, false));
        }
    }

    internal static class SliceGuard
    {
        static bool logged;

        // True exactly once, for the first failure to report.
        internal static bool FirstFailure(ref bool alreadyLogged)
        {
            if (alreadyLogged) return false;
            alreadyLogged = true;
            return true;
        }

        internal static void Failed(Exception error)
        {
            if (!FirstFailure(ref logged)) return;
            UnityEngine.Debug.LogWarning("[TLD MP slice] slicer failed once; the native caller still destroys the object: " + error.GetType().Name + ": " + error.Message);
        }
    }
}
