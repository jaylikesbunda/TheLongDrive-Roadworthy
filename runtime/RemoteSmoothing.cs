using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Remote player smoothing. GotUpd records samples (entry observer); Upd is
    // overwritten with the interpolated pose after the native body has run.
    internal static class RemotePlayerSmoothing
    {
        static readonly bool enabled = RemoteSmoothPolicy.PlayerEnabled(Environment.GetEnvironmentVariable("TLD_MP_SMOOTH_PLAYERS"));
        static readonly Dictionary<ulong, RemotePoseBuffer> buffers = new Dictionary<ulong, RemotePoseBuffer>();
        static readonly List<ulong> stale = new List<ulong>();

        internal static void Sample(syncScript.playerSyncClass player, Vector3d pos, Vector3 rot, bool sitting)
        {
            if (!enabled || player == null) return;
            ulong id = player.steamID;
            if (sitting)
            {
                buffers.Remove(id);
                return;
            }
            double now = Time.realtimeSinceStartup;
            RemotePoseBuffer buffer;
            if (!buffers.TryGetValue(id, out buffer))
            {
                if (buffers.Count >= RemoteSmoothPolicy.MaxBuffers) Prune(now);
                if (buffers.Count >= RemoteSmoothPolicy.MaxBuffers) return;
                buffer = new RemotePoseBuffer();
                buffers[id] = buffer;
            }
            Quaternion q = Quaternion.Euler(rot);
            var pose = new RemotePose();
            pose.X = pos.x; pose.Y = pos.y; pose.Z = pos.z;
            pose.Qx = q.x; pose.Qy = q.y; pose.Qz = q.z; pose.Qw = q.w;
            double sampleTime, lateness;
            StableGameBridge.DispatchSendTime(now, out sampleTime, out lateness);
            buffer.Add(sampleTime, pose, lateness);
        }

        // Runs after native Upd. Only a free-standing, non-sitting player with a
        // live object is overwritten; every other path keeps the native pose.
        internal static void Apply(syncScript.playerSyncClass player)
        {
            if (!enabled || player == null || player.sitting || player.player == null) return;
            RemotePoseBuffer buffer;
            if (!buffers.TryGetValue(player.steamID, out buffer)) return;
            RemotePose pose;
            if (!buffer.TakeSnap(out pose))
            {
                if (buffer.Count < 2) return;
                if (!buffer.Sample(Time.realtimeSinceStartup - buffer.Delay, out pose)) return;
            }
            Transform t = player.player.transform;
            t.position = mainscript.UnityPosFromGlobal(new Vector3d(pose.X, pose.Y, pose.Z));
            t.rotation = new Quaternion((float)pose.Qx, (float)pose.Qy, (float)pose.Qz, (float)pose.Qw);
        }

        static void Prune(double now)
        {
            stale.Clear();
            foreach (var pair in buffers)
                if (now - pair.Value.NewestTime > RemoteSmoothPolicy.StaleSeconds) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) buffers.Remove(stale[i]);
        }
    }

    // Remote car smoothing. Off unless TLD_MP_SMOOTH_CARS=1. The native GotSync
    // teleport is skipped for cars this machine is not in charge of; RemoteCarDriver
    // moves them with Rigidbody.MovePosition/MoveRotation instead.
    internal static class RemoteCarSmoothing
    {
        sealed class Track
        {
            internal tosaveitemscript Item;
            internal RemotePoseBuffer Buffer;
            internal double LastSample;
            internal int RemoteRole;
            internal bool HasTarget;
            internal double TargetX, TargetY, TargetZ;
            internal int NativeSyncs;
        }

        static readonly Dictionary<int, Track> tracks = new Dictionary<int, Track>();
        static readonly List<int> dead = new List<int>();
        static int tracked, applied, released;
        static int sampledInterpolated, sampledExtrapolated, sampledHeld;

        // Smoothing-buffer outcomes since the last call: a run of extrapolated or
        // held samples means poses arrived too far apart for the render delay.
        internal static void TakeSampleCounts(out int interpolated, out int extrapolated, out int held)
        {
            interpolated = sampledInterpolated; extrapolated = sampledExtrapolated; held = sampledHeld;
            sampledInterpolated = sampledExtrapolated = sampledHeld = 0;
        }

        static bool Enabled
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("TLD_MP_SMOOTH_CARS");
                PatchOptions.Load();
                return RemoteSmoothPolicy.CarEnabled(env, PatchOptions.SmoothCars);
            }
        }

        // Guard on tosaveitemscript.GotSync. Returns true for the native body.
        internal static bool Gate(tosaveitemscript item, Vector3 pos, Vector3 rot)
        {
            if (!Enabled || item == null || item.car == null) return true;
            int id = item.GetInstanceID();
            bool inCharge = item.ThisOneInChargeCar();
            if (RemoteSmoothPolicy.CarGotSyncIsNative(true, true, inCharge))
            {
                Track owned;
                if (tracks.TryGetValue(id, out owned)) owned.NativeSyncs++;
                if (inCharge && tracks.Remove(id))
                {
                    released++;
                    Note("car " + item.name + " released: this machine took charge of it");
                }
                return true;
            }
            double now = Time.realtimeSinceStartup;
            Track track;
            if (!tracks.TryGetValue(id, out track))
            {
                if (tracks.Count >= RemoteSmoothPolicy.MaxBuffers) return true;
                track = new Track();
                track.Item = item;
                track.Buffer = new RemotePoseBuffer();
                tracks[id] = track;
                tracked++;
            }
            // UpdInWorld called MultiPosUpdate with the sender's role just before this.
            track.RemoteRole = (int)item.car.lastMultiWhatDoing;
            // Exactly GotSync's bookkeeping; only the teleport is withheld.
            item.gotSyncLocal = false;
            item.gotSyncLastPos = pos;
            item.gotSyncLastRot = rot;
            item.UpdGotSyncTime();
            if (track.LastSample > 0) NetStats.PoseGap(now - track.LastSample);
            // Buffered in global space: the game recentres the world (mainscript
            // visszarakva) and a Unity-space sample from before a recentre would
            // drag the car, and a seated passenger, kilometres back for a frame.
            Vector3d global = mainscript.GlobalFromUnityPos(pos);
            Quaternion q = Quaternion.Euler(rot);
            var pose = new RemotePose();
            pose.X = global.x; pose.Y = global.y; pose.Z = global.z;
            pose.Qx = q.x; pose.Qy = q.y; pose.Qz = q.z; pose.Qw = q.w;
            double gapSeconds = track.LastSample > 0 ? now - track.LastSample : 0;
            // Timed by the driver's send clock, not arrival (see SenderClock).
            double sampleTime, lateness;
            StableGameBridge.DispatchSendTime(now, out sampleTime, out lateness);
            RemoteAddResult result = track.Buffer.Add(sampleTime, pose, lateness);
            track.LastSample = now;
            if (result == RemoteAddResult.Reset && track.Buffer.LastJumpMetres > RemotePoseBuffer.TeleportMetres)
                Note("car " + item.name + " pose jumped " + track.Buffer.LastJumpMetres.ToString("F1") + "m after " +
                    (gapSeconds * 1000).ToString("F0") + "ms (message type " + StableGameBridge.DispatchType + ")");
            return false;
        }

        static double noteWindow;
        static int noteCount;

        // Rate-limited diagnostics: at most 20 lines per 5 s.
        static void Note(string text)
        {
            double now = Time.realtimeSinceStartup;
            if (now - noteWindow > 5) { noteWindow = now; noteCount = 0; }
            if (++noteCount > 20) return;
            Debug.Log("[TLD MP smooth] t=" + now.ToString("F2") + " " + text);
        }

        // FixedUpdate: drive every tracked car this machine does not own.
        internal static void Drive()
        {
            if (!Enabled)
            {
                if (tracks.Count != 0)
                {
                    released += tracks.Count;
                    tracks.Clear();
                }
                return;
            }
            if (tracks.Count == 0) return;
            double now = Time.realtimeSinceStartup;
            dead.Clear();
            foreach (var pair in tracks)
            {
                Track track = pair.Value;
                tosaveitemscript item = track.Item;
                bool destroyed = item == null;
                bool inCharge = !destroyed && item.ThisOneInChargeCar();
                if (RemoteSmoothPolicy.CarShouldRelease(destroyed, inCharge, now, track.LastSample))
                {
                    dead.Add(pair.Key);
                    released++;
                    Note("car released: " + (destroyed ? "destroyed" : inCharge ? "this machine took charge" : "no pose for " + ((now - track.LastSample) * 1000).ToString("F0") + "ms"));
                    continue;
                }
                if (RemoteSmoothPolicy.KeepRemoteRole(now - track.LastSample, track.RemoteRole))
                {
                    item.car.lastMultiWhatDoing = (tosaveitemscript.carWhatDoing)track.RemoteRole;
                    item.car.lastMultiWhatDoingTime = Time.realtimeSinceStartup;
                }
                Rigidbody body = item.RB;
                RemotePose pose;
                int kind;
                if (body == null || !track.Buffer.Sample(now - track.Buffer.Delay, out pose, out kind)) continue;
                track.HasTarget = true;
                track.TargetX = pose.X; track.TargetY = pose.Y; track.TargetZ = pose.Z;
                if (kind == RemotePoseBuffer.SampleInterpolated) sampledInterpolated++;
                else if (kind == RemotePoseBuffer.SampleExtrapolated) sampledExtrapolated++;
                else if (kind == RemotePoseBuffer.SampleHeld) sampledHeld++;
                body.MovePosition(mainscript.UnityPosFromGlobal(new Vector3d(pose.X, pose.Y, pose.Z)));
                body.MoveRotation(new Quaternion((float)pose.Qx, (float)pose.Qy, (float)pose.Qz, (float)pose.Qw));
                applied++;
            }
            for (int i = 0; i < dead.Count; i++) tracks.Remove(dead[i]);
        }

        // True while this machine smooths the car (it is driven elsewhere).
        internal static bool IsSmoothed(tosaveitemscript item)
        {
            return item != null && tracks.ContainsKey(item.GetInstanceID());
        }

        // Diagnostics for the view-jump log: is this car smoothed, and where was its
        // newest received pose (global) relative to where it is now.
        internal static string Describe(tosaveitemscript item)
        {
            if (item == null) return "item=none";
            Track track;
            if (!tracks.TryGetValue(item.GetInstanceID(), out track)) return "smoothed=False inCharge=" + item.ThisOneInChargeCar();
            RemotePose newest;
            string offset = "?";
            if (track.Buffer.Sample(double.MaxValue, out newest))
            {
                Vector3d g = mainscript.GlobalFromUnityPos(item.transform.position);
                double dx = g.x - newest.X, dy = g.y - newest.Y, dz = g.z - newest.Z;
                offset = Math.Sqrt(dx * dx + dy * dy + dz * dz).ToString("F1") + "m";
            }
            return "smoothed=True samples=" + track.Buffer.Count + " sinceSample=" + ((Time.realtimeSinceStartup - track.LastSample) * 1000).ToString("F0") +
                "ms carFromNewestPose=" + offset;
        }

        // LateUpdate diagnostics: a smoothed car that ended the frame far from where
        // the driver last put it was moved by something else this frame.
        internal static void CheckDrift()
        {
            if (tracks.Count == 0) return;
            foreach (var pair in tracks)
            {
                Track track = pair.Value;
                tosaveitemscript item = track.Item;
                if (item == null || !track.HasTarget) continue;
                Vector3d g = mainscript.GlobalFromUnityPos(item.transform.position);
                double dx = g.x - track.TargetX, dy = g.y - track.TargetY, dz = g.z - track.TargetZ;
                double drift = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (drift > RemoteSmoothPolicy.DriftMetres)
                {
                    Rigidbody body = item.RB;
                    Note("car " + item.name + " drifted " + drift.ToString("F1") + "m from its smoothed pose: at=(" + g.x.ToString("F1") + "," + g.y.ToString("F1") + "," + g.z.ToString("F1") +
                        ") target=(" + track.TargetX.ToString("F1") + "," + track.TargetY.ToString("F1") + "," + track.TargetZ.ToString("F1") + ") speed=" +
                        (body == null ? "?" : body.velocity.magnitude.ToString("F1")) + " kinematic=" + (body != null && body.isKinematic) +
                        " rbIsRoot=" + (body != null && body.transform == item.transform) + " parent=" + (item.transform.parent == null ? "none" : item.transform.parent.name) +
                        " inCharge=" + item.ThisOneInChargeCar() + " role=" + track.RemoteRole + " nativeSyncs=" + track.NativeSyncs);
                }
                track.NativeSyncs = 0;
            }
        }

        internal static string TakeSummary()
        {
            string text = "carSmooth{tracked=" + tracked + " applied=" + applied + " released=" + released + "}";
            tracked = 0;
            applied = 0;
            released = 0;
            return text;
        }
    }

    public sealed class RemoteCarDriver : MonoBehaviour
    {
        static bool reported;

        void FixedUpdate()
        {
            try
            {
                RemoteCarSmoothing.Drive();
            }
            catch (Exception e)
            {
                if (reported) return;
                reported = true;
                Debug.LogWarning("[TLD MP smooth] car driver disabled after error: " + e.GetType().Name);
            }
        }
    }

    public static partial class StableGameBridge
    {
        static bool remoteSmoothFailed;

        // Entry observer on syncScript.playerSyncClass.GotUpd.
        public static void RemotePlayerSample(syncScript.playerSyncClass player, Vector3d pos, Vector3 rot, bool sitting)
        {
            try { RemotePlayerSmoothing.Sample(player, pos, rot, sitting); }
            catch (Exception e) { RemoteSmoothFailed("player sample", e); }
        }

        // Return observer on syncScript.playerSyncClass.Upd.
        public static void RemotePlayerPose(syncScript.playerSyncClass player)
        {
            try { RemotePlayerSmoothing.Apply(player); }
            catch (Exception e) { RemoteSmoothFailed("player pose", e); }
        }

        // Guard on tosaveitemscript.GotSync. False withholds the native teleport.
        public static bool CarSyncGotSync(tosaveitemscript item, Vector3 pos, Vector3 rot)
        {
            CargoGhost.NoteSync(item, pos);
            try { return RemoteCarSmoothing.Gate(item, pos, rot); }
            catch (Exception e) { RemoteSmoothFailed("car sample", e); return true; }
        }

        public static string TakeCarSmoothSummary() { return RemoteCarSmoothing.TakeSummary(); }

        static void RemoteSmoothFailed(string where, Exception e)
        {
            if (remoteSmoothFailed) return;
            remoteSmoothFailed = true;
            Debug.LogWarning("[TLD MP smooth] " + where + " disabled after error: " + e.GetType().Name);
        }
    }
}
