using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Cargo ghosting for the driving client (TLD_MP_CARGO_GHOST=1). The host
    // simulates cargo; the guest's copies are teleported into its own car every
    // update, so physics can fight the car. While the local player drives, cargo
    // owned by the host inside the local car is made kinematic, held in the car's
    // frame and collision-ignored against that car. Ownership, messages, saves and
    // host simulation are untouched. Off by default.
    //
    // The same class feeds the [TLD MP cameras] line with a cheap measurement:
    // host cargo snap distance per update and collisions between the car and
    // host-owned cargo.
    internal static class CargoGhost
    {
        const string Switch = "TLD_MP_CARGO_GHOST";
        const float ScanInterval = 0.25f;
        const float MeasureInterval = 0.5f;
        const float MaxBoundsPadding = 0.75f;

        sealed class Track
        {
            internal tosaveitemscript Item;
            internal Rigidbody Body;
            internal bool Kinematic, Gravity;
            internal Collider[] CarColliders;
            internal Collider[] ItemColliders;
            internal bool[] Ignored;
            internal Vector3 LocalPosition;
            internal Quaternion LocalRotation;
        }

        static readonly Dictionary<uint, Track> tracks = new Dictionary<uint, Track>();
        static readonly List<uint> drop = new List<uint>();
        static readonly List<tosaveitemscript> candidates = new List<tosaveitemscript>();
        static int featureState;
        static float nextScan, nextMeasure;
        static carscript measuredCar;
        static CargoCollisionProbe probe;
        static long ghosted, released;
        static long snapCount, collisionCount;
        static float snapMax, snapTotal, snapWorstSpeed;
        static bool failed;

        internal static bool Enabled
        {
            get
            {
                if (featureState == 0) featureState = Environment.GetEnvironmentVariable(Switch) == "1" ? 1 : 2;
                return featureState == 1;
            }
        }

        // Called once per main-loop frame, before the rest of the game's update.
        internal static void Frame()
        {
            if (failed) return;
            bool measure = FrameDiagnostics.Enabled;
            if (!Enabled && !measure) return;
            try
            {
                fpscontroller player = mainscript.s == null ? null : mainscript.s.player;
                carscript car = DrivingCar(player);
                if (car == null)
                {
                    ReleaseAll();
                    DetachProbe();
                    measuredCar = null;
                    return;
                }
                if (measure && (measuredCar != car || probe == null))
                {
                    DetachProbe();
                    measuredCar = car;
                    probe = car.gameObject.AddComponent<CargoCollisionProbe>();
                    probe.Car = car;
                }
                if (!Enabled)
                {
                    measuredCar = car;
                    if (measure && Time.realtimeSinceStartup >= nextMeasure) { nextMeasure = Time.realtimeSinceStartup + MeasureInterval; Scan(car, false); }
                    return;
                }
                if (Time.realtimeSinceStartup >= nextScan)
                {
                    nextScan = Time.realtimeSinceStartup + ScanInterval;
                    Scan(car, true);
                }
                Hold(car);
            }
            catch (Exception error)
            {
                failed = true;
                ReleaseAll();
                DetachProbe();
                Debug.LogWarning("[TLD MP cargo] ghosting disabled after error: " + error.GetType().Name + " " + error.Message);
            }
        }

        static carscript DrivingCar(fpscontroller player)
        {
            if (player == null || player.seat == null || !player.seat.driverSeat0) return null;
            carscript car = player.seat.Car;
            if (car == null || car != player.Car || car.tosave == null) return null;
            if (!syncScript.IsConnected() || syncScript.IsHost()) return null;
            // The car's own authority helper also returns false when this player is
            // not in that car, which keeps the seat test honest on reconnect.
            if (!car.tosave.ThisOneInChargeCar(tosaveitemscript.carWhatDoing.nothing)) return null;
            return car;
        }

        static void Scan(carscript car, bool ghost)
        {
            candidates.Clear();
            var items = savedatascript.s == null ? null : savedatascript.s.items;
            if (items == null) { ReleaseAll(); return; }
            Bounds bounds = CarBounds(car);
            foreach (var pair in items)
            {
                tosaveitemscript item = pair.Value;
                if (!IsHostCargo(item, car)) continue;
                if (ghost && !Inside(bounds, item.transform.position)) continue;
                candidates.Add(item);
            }
            if (FrameDiagnostics.Enabled) Measured = candidates.Count;
            if (!ghost) return;
            // Release items that left the car or are no longer host cargo.
            drop.Clear();
            foreach (var pair in tracks)
            {
                tosaveitemscript item = pair.Value.Item;
                if (item == null || !IsHostCargo(item, car) || !Inside(bounds, item.transform.position)) drop.Add(pair.Key);
            }
            for (int i = 0; i < drop.Count; i++) Release(drop[i]);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!tracks.ContainsKey(candidates[i].idInSave)) Apply(candidates[i], car);
            }
        }

        static bool IsHostCargo(tosaveitemscript item, carscript car)
        {
            if (item == null || item.car != car || item == car.tosave) return false;
            if (item.P == null || item.P.physlock != null || item.mpstored || item.mppickedup || item.P.pickedUp) return false;
            if (item.RB == null) return false;
            if (item.attachable != null && item.attachable.attached) return false;
            if (item.ThisOneInCharge()) return false;
            return true;
        }

        static Bounds CarBounds(carscript car)
        {
            Collider[] colliders = car.GetComponentsInChildren<Collider>();
            bool any = false;
            Bounds bounds = new Bounds(car.transform.position, Vector3.zero);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null || c.isTrigger) continue;
                if (!any) { bounds = c.bounds; any = true; }
                else bounds.Encapsulate(c.bounds);
            }
            if (!any) bounds = new Bounds(car.transform.position, new Vector3(2f, 2f, 4f));
            bounds.Expand(MaxBoundsPadding);
            return bounds;
        }

        static bool Inside(Bounds bounds, Vector3 position)
        {
            // A generous vertical pad: cargo can rest slightly under a wheel arch.
            return position.x >= bounds.min.x && position.x <= bounds.max.x &&
                position.y >= bounds.min.y - 2f && position.y <= bounds.max.y + 2f &&
                position.z >= bounds.min.z && position.z <= bounds.max.z;
        }

        static void Apply(tosaveitemscript item, carscript car)
        {
            Rigidbody body = item.RB;
            if (body == null) return;
            var track = new Track();
            track.Item = item;
            track.Body = body;
            track.Kinematic = body.isKinematic;
            track.Gravity = body.useGravity;
            track.CarColliders = car.GetComponentsInChildren<Collider>();
            track.ItemColliders = item.GetComponentsInChildren<Collider>();
            int pairs = track.CarColliders.Length * track.ItemColliders.Length;
            track.Ignored = new bool[pairs];
            int n = 0;
            for (int a = 0; a < track.CarColliders.Length; a++)
                for (int b = 0; b < track.ItemColliders.Length; b++)
                {
                    Collider first = track.CarColliders[a], second = track.ItemColliders[b];
                    if (first == null || second == null) { n++; continue; }
                    track.Ignored[n] = Physics.GetIgnoreCollision(first, second);
                    if (!track.Ignored[n]) Physics.IgnoreCollision(first, second, true);
                    n++;
                }
            track.LocalPosition = car.transform.InverseTransformPoint(item.transform.position);
            track.LocalRotation = Quaternion.Inverse(car.transform.rotation) * item.transform.rotation;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
            tracks[item.idInSave] = track;
            ghosted++;
        }

        static void Hold(carscript car)
        {
            foreach (var pair in tracks)
            {
                Track track = pair.Value;
                if (track.Item == null || track.Body == null) continue;
                Vector3 position = car.transform.TransformPoint(track.LocalPosition);
                Quaternion rotation = car.transform.rotation * track.LocalRotation;
                track.Item.transform.SetPositionAndRotation(position, rotation);
            }
        }

        static void ReleaseAll()
        {
            drop.Clear();
            foreach (var pair in tracks) drop.Add(pair.Key);
            for (int i = 0; i < drop.Count; i++) Release(drop[i]);
        }

        static void Release(uint id)
        {
            Track track;
            if (!tracks.TryGetValue(id, out track)) return;
            tracks.Remove(id);
            tosaveitemscript item = track.Item;
            if (track.Body != null)
            {
                track.Body.isKinematic = track.Kinematic;
                track.Body.useGravity = track.Gravity;
            }
            int n = 0;
            for (int a = 0; a < track.CarColliders.Length; a++)
                for (int b = 0; b < track.ItemColliders.Length; b++)
                {
                    Collider first = track.CarColliders[a], second = track.ItemColliders[b];
                    if (first != null && second != null && !track.Ignored[n]) Physics.IgnoreCollision(first, second, false);
                    n++;
                }
            released++;
        }

        static void DetachProbe()
        {
            if (probe != null && probe.gameObject != null) UnityEngine.Object.Destroy(probe);
            probe = null;
        }

        // Called from the GotSync guard for every synchronized item, before the
        // native teleport. Records how far the guest copy is moved per update.
        internal static void NoteSync(tosaveitemscript item, Vector3 target)
        {
            if (!FrameDiagnostics.Enabled && !Enabled) return;
            if (item == null || item.car == null) return;
            fpscontroller player = mainscript.s == null ? null : mainscript.s.player;
            if (player == null || player.seat == null || !player.seat.driverSeat0 || player.seat.Car != item.car) return;
            float distance = (item.transform.position - target).magnitude;
            if (float.IsNaN(distance) || float.IsInfinity(distance)) return;
            snapCount++;
            snapTotal += distance;
            if (distance > snapMax) snapMax = distance;
            if (item.RB != null)
            {
                float speed = item.RB.velocity.magnitude;
                if (speed > snapWorstSpeed) snapWorstSpeed = speed;
            }
        }

        internal static void NoteCollision(Collision collision)
        {
            if (collision == null || collision.collider == null) return;
            if (!FrameDiagnostics.Enabled) return;
            tosaveitemscript other = collision.collider.GetComponentInParent<tosaveitemscript>();
            if (other == null || other.car == null) return;
            fpscontroller player = mainscript.s == null ? null : mainscript.s.player;
            if (player == null || player.seat == null || player.seat.Car != other.car) return;
            if (other.ThisOneInCharge()) return;
            collisionCount++;
        }

        internal static int Measured;

        internal static string Summary()
        {
            string text = "cargoGhost{" + (Enabled ? "on" : "off") + " carItems=" + Measured + " ghosted=" + tracks.Count +
                " applied=" + ghosted + " released=" + released + " snapN=" + snapCount +
                " snapMax=" + F2(snapMax) + " snapAvg=" + F2(snapCount == 0 ? 0f : snapTotal / snapCount) +
                " collide=" + collisionCount + " worstSpeed=" + F2(snapWorstSpeed) + "}";
            Measured = 0;
            snapCount = 0;
            snapTotal = 0f;
            snapMax = 0f;
            snapWorstSpeed = 0f;
            collisionCount = 0;
            return text;
        }

        static string F2(float value) { return value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture); }
    }

    // Receives the local car's own collision callbacks. Added only on the driving
    // client and only while the measurement is active; it writes nothing.
    public sealed class CargoCollisionProbe : MonoBehaviour
    {
        internal carscript Car;

        void OnCollisionEnter(Collision collision)
        {
            CargoGhost.NoteCollision(collision);
        }
    }
}
