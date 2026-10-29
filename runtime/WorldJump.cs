using System;
using System.Globalization;
using UnityEngine;

namespace TldMp
{
    // Unity-free decisions for the [TLD MP world] diagnostics.
    internal static class WorldJumpPolicy
    {
        // Faster than any car; a frame that moves the player further than this
        // allows (plus JumpMetres of slack) is a jump, not driving.
        internal const double MaxSpeedMetresPerSecond = 120.0;
        internal const double JumpMetres = 25.0;
        internal const double MinLogSeconds = 0.5;

        internal static double Distance(double ax, double ay, double az, double bx, double by, double bz)
        {
            double dx = bx - ax, dy = by - ay, dz = bz - az;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        internal static bool IsJump(double movedMetres, double seconds)
        {
            if (double.IsNaN(movedMetres) || double.IsInfinity(movedMetres)) return true;
            if (!(seconds >= 0)) seconds = 0;
            return movedMetres > JumpMetres + MaxSpeedMetresPerSecond * seconds;
        }

        internal static bool OriginChanged(double ax, double ay, double az, double bx, double by, double bz)
        {
            return ax != bx || ay != by || az != bz;
        }
    }

    // Read-only: logs world recentres and frames where the local player moves
    // further (in global space) than driving allows. Never moves anything.
    internal static class WorldJump
    {
        static bool have;
        static double lastX, lastY, lastZ, originX, originY, originZ, nextLog;
        static float lastTime;
        static int recentres;

        internal static int Recentres { get { return recentres; } }

        // Set by the bridge (keeps this file free of the smoothing code for tests).
        internal static Func<tosaveitemscript, string> CarDescriber;

        internal static void Tick(bool connected)
        {
            var main = mainscript.s;
            if (main == null || main.player == null) { have = false; return; }
            Vector3 unity = main.player.transform.position;
            Vector3d global = mainscript.GlobalFromUnityPos(unity);
            Vector3d origin = main.visszarakva;
            float now = Time.realtimeSinceStartup;
            if (have)
            {
                if (WorldJumpPolicy.OriginChanged(originX, originY, originZ, origin.x, origin.y, origin.z))
                {
                    recentres++;
                    Debug.Log("[TLD MP world] t=" + F(now) + " recentre #" + recentres + " by=" +
                        F(WorldJumpPolicy.Distance(originX, originY, originZ, origin.x, origin.y, origin.z)) + "m lastMs=" + F(main.visszaTime));
                }
                double moved = WorldJumpPolicy.Distance(lastX, lastY, lastZ, global.x, global.y, global.z);
                if (WorldJumpPolicy.IsJump(moved, now - lastTime) && now >= nextLog)
                {
                    nextLog = now + WorldJumpPolicy.MinLogSeconds;
                    var seat = main.player.seat;
                    string car = seat != null && seat.Car != null ? seat.Car.name : "none";
                    string carState = "";
                    if (seat != null && seat.Car != null)
                    {
                        Transform carTransform = seat.Car.transform;
                        Vector3d carGlobal = mainscript.GlobalFromUnityPos(carTransform.position);
                        Rigidbody body = seat.Car.GetComponent<Rigidbody>();
                        carState = " carAt=(" + F(carGlobal.x) + "," + F(carGlobal.y) + "," + F(carGlobal.z) + ") playerToCar=" +
                            F(Vector3.Distance(unity, carTransform.position)) + "m carSpeed=" + (body == null ? "?" : F(body.velocity.magnitude)) +
                            " kinematic=" + (body != null && body.isKinematic) + " " + (CarDescriber == null ? "" : CarDescriber(seat.Car.GetComponent<tosaveitemscript>()));
                    }
                    Debug.Log("[TLD MP world] t=" + F(now) + " view jump moved=" + F(moved) + "m dt=" + F(now - lastTime) +
                        "s from=(" + F(lastX) + "," + F(lastY) + "," + F(lastZ) + ") to=(" + F(global.x) + "," + F(global.y) + "," + F(global.z) +
                        ") seated=" + (seat != null) + " car=" + car + " driver=" + (seat != null && seat.driverSeat0) +
                        " connected=" + connected + carState);
                }
            }
            have = true;
            lastX = global.x; lastY = global.y; lastZ = global.z;
            originX = origin.x; originY = origin.y; originZ = origin.z;
            lastTime = now;
        }

        static string F(double v) { return v.ToString("F1", CultureInfo.InvariantCulture); }
    }
}
