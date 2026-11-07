using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Runtime side of hitched trailers: they follow their towing car (decisions live in
    // TrailerSyncPolicy). Owner = in charge of the towing car. Non-owners drop the owner's
    // global poses on the inbound path and smooth the ones they accept; the owner sends the
    // trailer pose in the car's burst tick.
    internal static class TrailerSync
    {
        const int MaxWired = 1024;

        sealed class Wired
        {
            internal tosaveitemscript Trailer;
            internal attachablescript Grip;
            internal int Frame = -1;
            internal tosaveitemscript Car;
        }

        sealed class Track
        {
            internal tosaveitemscript Item;
            internal RemotePoseBuffer Buffer;
            internal double LastSample;
        }

        // Every trailer wired by TrailerHitch, keyed by the trailer's instance id.
        static readonly Dictionary<int, Wired> registry = new Dictionary<int, Wired>();
        static readonly Dictionary<int, Track> tracks = new Dictionary<int, Track>();
        static readonly List<int> dead = new List<int>();

        // Called after TrailerHitch.Wire: registers the grip's trailer when the grip carries an attachable.
        internal static void RegisterGrip(attachgripscript holder)
        {
            if (holder == null || holder.Grip == null || holder.tosaveitem == null) return;
            attachablescript grip = holder.Grip.GetComponent<attachablescript>();
            if (grip != null) Register(holder.tosaveitem, grip);
        }

        // Called for each wired trailer grip.
        internal static void Register(tosaveitemscript trailer, attachablescript grip)
        {
            if (trailer == null || grip == null) return;
            int id = trailer.GetInstanceID();
            Wired entry;
            if (!registry.TryGetValue(id, out entry))
            {
                if (registry.Count >= MaxWired) Prune();
                if (registry.Count >= MaxWired) return;
                entry = new Wired();
                registry[id] = entry;
            }
            entry.Trailer = trailer;
            entry.Grip = grip;
            entry.Frame = -1;
            entry.Car = null;
        }

        static void Prune()
        {
            dead.Clear();
            foreach (var pair in registry) if (pair.Value.Trailer == null || pair.Value.Grip == null) dead.Add(pair.Key);
            for (int i = 0; i < dead.Count; i++) registry.Remove(dead[i]);
        }

        // Owner of a hitched trailer = owner of its towing car (TrailerSyncPolicy.CarOwner). The
        // native ThisOneInChargeCar is false on every peer while the car is parked, so the host
        // owns a parked car, as native items do. Calling it first refreshes the 1 s timeout of
        // lastMultiWhatDoing before it is read.
        static bool TowingCarOwnedHere(tosaveitemscript car)
        {
            bool local = car.ThisOneInChargeCar();
            carscript towed = car.car;
            bool remoteActive = towed != null
                && towed.lastMultiWhatDoing != tosaveitemscript.carWhatDoing.nothing
                && Time.realtimeSinceStartup - towed.lastMultiWhatDoingTime <= 1f;
            return TrailerSyncPolicy.CarOwner(local, remoteActive, syncScript.IsHostOrSingle());
        }

        // The towing car of a hitched, wired trailer; null otherwise. Cached per frame.
        internal static tosaveitemscript TowingCar(tosaveitemscript trailer)
        {
            if (trailer == null || registry.Count == 0) return null;
            Wired entry;
            if (!registry.TryGetValue(trailer.GetInstanceID(), out entry) || entry.Grip == null) return null;
            int frame = Time.frameCount;
            if (entry.Frame != frame)
            {
                entry.Frame = frame;
                entry.Car = null;
                attachablescript grip = entry.Grip;
                if (grip.attached && grip.point != null)
                {
                    tosaveitemscript car = grip.AttachedToTosave();
                    if (car != null && car.car != null) entry.Car = car;
                }
            }
            return entry.Car;
        }

        // Return filter on tosaveitemscript.ThisOneInCharge. Unhitched items keep the native answer.
        internal static bool ChargeFilter(bool native, tosaveitemscript trailer)
        {
            try
            {
                tosaveitemscript car = TowingCar(trailer);
                bool hitched = car != null;
                return TrailerSyncPolicy.InCharge(hitched, native, hitched && TowingCarOwnedHere(car));
            }
            catch (Exception)
            {
                return native;
            }
        }

        // Guard on syncScript.UpdInWorld for items without a car. False drops the pose
        // when this machine owns the hitched trailer.
        internal static bool AcceptInbound(uint id)
        {
            try
            {
                if (registry.Count == 0) return true;
                savedatascript save = savedatascript.s;
                tosaveitemscript item;
                if (save == null || save.items == null || !save.items.TryGetValue(id, out item) || item == null) return true;
                tosaveitemscript car = TowingCar(item);
                bool hitched = car != null;
                return TrailerSyncPolicy.AcceptPose(hitched, hitched && TowingCarOwnedHere(car));
            }
            catch (Exception)
            {
                return true;
            }
        }

        static bool SmoothingOn()
        {
            string env = Environment.GetEnvironmentVariable("TLD_MP_SMOOTH_CARS");
            PatchOptions.Load();
            return RemoteSmoothPolicy.CarEnabled(env, PatchOptions.SmoothCars);
        }

        // Called from RemoteCarSmoothing.Gate for items without a car. False withholds the
        // native teleport and buffers the pose for Drive.
        internal static bool Gate(tosaveitemscript item, Vector3 pos, Vector3 rot)
        {
            if (registry.Count == 0 && tracks.Count == 0) return true;
            int id = item.GetInstanceID();
            tosaveitemscript car = TowingCar(item);
            bool hitched = car != null;
            bool owned = hitched && TowingCarOwnedHere(car);
            if (!TrailerSyncPolicy.Smoothed(hitched, owned, SmoothingOn()))
            {
                tracks.Remove(id);
                return true;
            }
            // Exactly GotSync's bookkeeping; only the teleport is withheld.
            item.gotSyncLocal = false;
            item.gotSyncLastPos = pos;
            item.gotSyncLastRot = rot;
            item.UpdGotSyncTime();
            double now = Time.realtimeSinceStartup;
            Track track;
            if (!tracks.TryGetValue(id, out track))
            {
                if (tracks.Count >= RemoteSmoothPolicy.MaxBuffers) return true;
                track = new Track();
                track.Item = item;
                track.Buffer = new RemotePoseBuffer();
                tracks[id] = track;
            }
            // Buffered in global space, as the car path does, so a world recentre cannot move it.
            Vector3d global = mainscript.GlobalFromUnityPos(pos);
            Quaternion q = Quaternion.Euler(rot);
            var pose = new RemotePose();
            pose.X = global.x; pose.Y = global.y; pose.Z = global.z;
            pose.Qx = q.x; pose.Qy = q.y; pose.Qz = q.z; pose.Qw = q.w;
            double sampleTime, lateness;
            StableGameBridge.DispatchSendTime(now, out sampleTime, out lateness);
            track.Buffer.Add(sampleTime, pose, lateness);
            track.LastSample = now;
            return false;
        }

        // FixedUpdate: move each buffered trailer that this machine does not own.
        internal static void Drive()
        {
            if (tracks.Count == 0) return;
            bool smoothing = SmoothingOn();
            double now = Time.realtimeSinceStartup;
            dead.Clear();
            foreach (var pair in tracks)
            {
                Track track = pair.Value;
                tosaveitemscript item = track.Item;
                if (item == null) { dead.Add(pair.Key); continue; }
                tosaveitemscript car = TowingCar(item);
                bool hitched = car != null;
                bool owned = hitched && TowingCarOwnedHere(car);
                if (!TrailerSyncPolicy.Smoothed(hitched, owned, smoothing) || now - track.LastSample > RemoteSmoothPolicy.StaleSeconds)
                {
                    dead.Add(pair.Key);
                    continue;
                }
                Rigidbody body = item.RB;
                RemotePose pose;
                if (body == null || !track.Buffer.Sample(now - track.Buffer.Delay, out pose)) continue;
                body.MovePosition(mainscript.UnityPosFromGlobal(new Vector3d(pose.X, pose.Y, pose.Z)));
                body.MoveRotation(new Quaternion((float)pose.Qx, (float)pose.Qy, (float)pose.Qz, (float)pose.Qw));
            }
            for (int i = 0; i < dead.Count; i++) tracks.Remove(dead[i]);
        }

        // Called after each car pose in the car's burst stream. Sends a hitched trailer
        // owned here at the same tick, through the native global (non-car) pose path.
        internal static void ForwardPoses(tosaveitemscript car)
        {
            if (car == null || registry.Count == 0) return;
            try
            {
                foreach (Wired entry in registry.Values)
                {
                    tosaveitemscript trailer = entry.Trailer;
                    if (trailer == null || TowingCar(trailer) != car) continue;
                    if (!TrailerSyncPolicy.ForwardWithCar(true, trailer.ThisOneInCharge(), true)) continue;
                    trailer.ForcePosUpd(false, false, false);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
