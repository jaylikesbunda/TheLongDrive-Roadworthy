using System;
using UnityEngine;

namespace TldMp
{
    // mainscript.MirrorsUpdate renders a mirror camera when the mirror's
    // Renderer.isVisible is true. isVisible is true when ANY camera drew it last
    // frame, including shadow passes and the other mirror cameras, so mirrors
    // that are off screen keep rendering. This keeps the native test and adds a
    // frustum test against the player's view cameras: a mirror is only skipped
    // when it is outside every view frustum, i.e. its texture cannot appear in
    // the main view this frame. It never renders a mirror that native skips.
    // Disable with TLD_MP_MIRROR_ONSCREEN=0.
    internal static class MirrorOnScreen
    {
        internal const float BoundsMargin = 0.1f;
        internal static long Skipped, Passed;
        static readonly Plane[] planes = new Plane[6];
        static int featureState;

        internal static bool FeatureEnabled
        {
            get
            {
                if (featureState == 0) featureState = Environment.GetEnvironmentVariable("TLD_MP_MIRROR_ONSCREEN") == "0" ? 2 : 1;
                return featureState == 1;
            }
        }

        // Unity-free decision. Anything uncertain falls back to native.
        internal static bool Decide(bool nativeVisible, bool feature, bool vr, bool frustumKnown, bool inAnyFrustum)
        {
            if (!nativeVisible) return false;
            if (!feature || vr || !frustumKnown) return true;
            return inAnyFrustum;
        }

        internal static bool Visible(Renderer renderer, Camera camera)
        {
            // Same receiver and property as the native code (null still throws).
            bool native = renderer.isVisible;
            if (!native) return false;
            bool feature = FeatureEnabled, vr = false, known = false, inside = false;
            if (feature)
            {
                try
                {
                    vr = mainscript.IsVR();
                    if (!vr)
                    {
                        Bounds bounds = renderer.bounds;
                        bounds.Expand(BoundsMargin * 2f);
                        var main = mainscript.s;
                        Camera a = camera, b = null, c = null;
                        if (main != null && main.player != null) { b = main.player.Cam; c = main.player.mainCam; }
                        known = a != null || b != null || c != null;
                        inside = Inside(a, bounds) || Inside(b, bounds) || Inside(c, bounds);
                    }
                }
                catch (Exception) { known = false; }
            }
            bool result = Decide(native, feature, vr, known, inside);
            if (result) Passed++; else Skipped++;
            return result;
        }

        static bool Inside(Camera camera, Bounds bounds)
        {
            if (camera == null) return false;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            return GeometryUtility.TestPlanesAABB(planes, bounds);
        }
    }
}
