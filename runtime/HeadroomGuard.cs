using System;
using UnityEngine;

namespace TldMp
{
    // Pure decisions for HeadroomGuard (no Unity objects, unit-tested).
    public static class HeadroomPolicy
    {
        // Room kept between the near-plane corners and the car body.
        public const float Margin = 0.02f;
        // Never cast a sphere bigger than this (a wide FOV on a big near plane).
        public const float MaxRadius = 0.3f;
        // After the obstacle is gone the camera eases back out this fast (m/s);
        // pulling in is always immediate so the view never enters the body.
        public const float ReleaseSpeed = 1.5f;

        // Distance from the camera to the corners of its near plane, plus Margin.
        public static float Radius(float near, float verticalFovDegrees, float aspect)
        {
            if (!(near > 0f) || float.IsNaN(verticalFovDegrees) || !(aspect > 0f)) return MaxRadius;
            float h = (float)Math.Tan(Math.Min(Math.Max(verticalFovDegrees, 1f), 170f) * Math.PI / 360.0);
            float w = h * aspect;
            float r = near * (float)Math.Sqrt(1.0 + h * h + w * w) + Margin;
            return r < MaxRadius ? r : MaxRadius;
        }

        // How far from the resting head the camera may be this frame.
        // free: distance to the first obstacle along the way (infinity if none).
        public static float Allowed(float previous, float free, float deltaTime)
        {
            if (float.IsNaN(free)) free = float.PositiveInfinity;
            if (float.IsNaN(previous)) previous = float.PositiveInfinity;
            float eased = float.IsPositiveInfinity(previous) ? previous : previous + ReleaseSpeed * Math.Max(0f, deltaTime);
            return free < eased ? free : eased;
        }
    }

    // Seated in a vehicle, the game moves the first-person head off the seat's
    // resting head point by the lean input (up to the lean distance) and by the
    // driving head bob (speeding up, braking, bumps). Neither checks the car
    // body, so the camera can end up inside the roof, a pillar or the door frame
    // and the view fills with the inside of the mesh. After the game has placed
    // the camera, a sphere the size of the near plane is swept from the resting
    // head point to the camera; if it meets the car's own colliders the camera
    // is drawn back to just in front of them. The game resets the camera rig to
    // the head every frame while seated, so nothing accumulates. If the resting
    // head point itself already touches the body that collider is left alone
    // (the game's own pose is kept). TLD_MP_HEADROOM=0 turns this off.
    public static class HeadroomGuard
    {
        static readonly bool DisabledByEnvironment = Environment.GetEnvironmentVariable("TLD_MP_HEADROOM") == "0";
        static readonly RaycastHit[] hits = new RaycastHit[16];
        static float allowed = float.PositiveInfinity;
        public static long Pulled;

        public static string Summary() { return "headroom{pulled=" + Pulled + "}"; }

        public static void Tick()
        {
            if (DisabledByEnvironment) return;
            var main = mainscript.s;
            var p = main == null ? null : main.player;
            if (p == null || !p.Bsitting || p.died || p.Bbinoculars || p.Cam == null || p.CamParent == null || p.TStandHeadPos == null
                || mainscript.IsVR() || p.otherView()) { allowed = float.PositiveInfinity; return; }
            var body = p.parentRigidbody;
            if (body == null) { allowed = float.PositiveInfinity; return; }

            var cam = p.Cam;
            Vector3 anchor = p.TStandHeadPos.position, target = cam.transform.position, way = target - anchor;
            float distance = way.magnitude;
            float free = float.PositiveInfinity;
            if (distance > 0.001f)
            {
                Vector3 dir = way / distance;
                float radius = HeadroomPolicy.Radius(cam.nearClipPlane, cam.fieldOfView, cam.aspect);
                int n = Physics.SphereCastNonAlloc(anchor, radius, dir, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = hits[i];
                    // distance 0 means the sphere already touched it at rest: keep the game's pose.
                    if (h.collider == null || h.distance <= 0f || h.collider.attachedRigidbody != body) continue;
                    if (h.distance < free) free = h.distance;
                }
                Array.Clear(hits, 0, n);
            }
            allowed = HeadroomPolicy.Allowed(allowed, free, Time.deltaTime);
            if (!(allowed < distance)) return;
            Vector3 pulled = anchor + way * (allowed / distance);
            p.CamParent.position += pulled - target;
            Pulled++;
        }

        public static void Clear() { allowed = float.PositiveInfinity; Pulled = 0; }
    }
}
