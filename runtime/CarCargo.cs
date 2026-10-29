using System;
using System.Collections.Generic;
using GeneszSteamP2PBase;
using UnityEngine;

namespace TldMp
{
    // Loose items riding in a moving car (glove-box contents once the lid opens,
    // anything dropped in the cab or bed) are native-synced in world space. A guest
    // sees the car slightly in the past (pose smoothing), so a world position from
    // the host landed metres ahead of the guest's car, and a guest's world position
    // landed metres behind the host's. While the car moves, those poses are sent
    // relative to the car's collider 0 using the game's own itemPosLocal
    // "collider anchor" form (index <= -200000), which every receiver applies
    // against its own view of the car. On a guest, host-owned loose items in a car
    // it is smoothing are also held in the car's frame between updates.
    internal static class CarCargoPolicy
    {
        internal const float MovingSpeed = 0.5f;
        internal const float MovingAngular = 0.2f;
        internal const float BoxPadding = 0.3f;
        internal const int ColliderAnchor = -200000;

        internal static bool Moving(float speed, float angularSpeed)
        {
            return speed > MovingSpeed || angularSpeed > MovingAngular;
        }

        // Point in the car's local frame against the car's local box (padded).
        internal static bool Inside(Vector3 local, Vector3 min, Vector3 max)
        {
            return local.x >= min.x - BoxPadding && local.x <= max.x + BoxPadding &&
                local.y >= min.y - BoxPadding && local.y <= max.y + BoxPadding &&
                local.z >= min.z - BoxPadding && local.z <= max.z + BoxPadding;
        }

        // A loose item: not stored, not fitted, not held, has a body.
        internal static bool Loose(bool hasBody, bool stored, bool attached, bool pickedUp, bool isCar)
        {
            return hasBody && !stored && !attached && !pickedUp && !isCar;
        }
    }

    internal static class CarCargo
    {
        const float CarCacheSeconds = 0.5f;
        const float HoldScanSeconds = 0.25f;

        sealed class CarBox
        {
            internal tosaveitemscript Car;
            internal Vector3 Min, Max;
        }

        sealed class Hold
        {
            internal tosaveitemscript Item, Car;
            internal Rigidbody Body;
            internal bool Kinematic, Gravity;
            internal Collider[] CarColliders, ItemColliders;
            internal bool[] Ignored;
        }

        static readonly List<CarBox> cars = new List<CarBox>();
        // A car's collider layout is fixed, so its box is computed once.
        static readonly Dictionary<int, CarBox> boxes = new Dictionary<int, CarBox>();
        static float carsAt = -1;
        static readonly Dictionary<uint, Hold> holds = new Dictionary<uint, Hold>();

        // The last received pose of each item, when it was anchored to a car's
        // collider 0 (CarCargoPolicy.ColliderAnchor). Any other pose clears it.
        sealed class Anchored { internal uint Car; internal Vector3 Pos, Rot; }
        static readonly Dictionary<uint, Anchored> anchored = new Dictionary<uint, Anchored>();
        const int MaxAnchored = 8192;

        internal static void NoteLocal(uint id, uint parent, int index, Vector3 pos, Vector3 rot)
        {
            if (index != CarCargoPolicy.ColliderAnchor) { anchored.Remove(id); return; }
            if (anchored.Count >= MaxAnchored && !anchored.ContainsKey(id)) anchored.Clear();
            anchored[id] = new Anchored { Car = parent, Pos = pos, Rot = rot };
        }

        internal static void NoteWorld(uint id) { anchored.Remove(id); }

        // A guest's own car mount let go of an item (a glove-box lid opened). Until
        // the host's next message for it (re-stored, car-relative or world), keep it
        // where it was in the car instead of letting it bounce around a car the
        // guest is smoothing. Only mount releases count, never items found lying in
        // a car's box, so a passing car can never scoop up items from the road.
        internal static void NoteReleased(pickupable p)
        {
            if (!enabled || failed || p == null || p.pickedUp || p.tosave == null || p.physlock == null) return;
            try
            {
                if (!syncScript.IsConnected() || syncScript.IsHost()) return;
                tosaveitemscript car = p.physlock.parentTosave;
                if (car == null || car.car == null || p.tosave.useThisForPos == null) return;
                Collider anchor = car.GetColliderFromIndex(0);
                if (anchor == null) return;
                if (anchored.Count >= MaxAnchored) anchored.Clear();
                anchored[p.tosave.idInSave] = new Anchored
                {
                    Car = car.idInSave,
                    Pos = anchor.transform.InverseTransformPoint(p.tosave.useThisForPos.position),
                    Rot = (Quaternion.Inverse(anchor.transform.rotation) * p.tosave.useThisForPos.rotation).eulerAngles
                };
                nextHoldScan = 0;
            }
            catch (Exception error) { Fail(error); }
        }
        static readonly List<uint> drop = new List<uint>();
        static float nextHoldScan;
        static readonly bool enabled = Environment.GetEnvironmentVariable("TLD_MP_CAR_CARGO") != "0";
        internal static long Rewritten, Held, Released;
        static bool failed;

        // ---- sending ----------------------------------------------------------
        // Returns a collider-anchored itemPosLocal for a loose item inside a moving
        // car, or null to keep the native world pose.
        internal static byte[] RelativePose(tosaveitemscript item)
        {
            if (!enabled || failed || item == null || item.P == null || item.useThisForPos == null) return null;
            try
            {
                if (!CarCargoPolicy.Loose(item.RB != null, item.P.physlock != null, item.attachable != null && item.attachable.attached,
                    item.P.pickedUp || item.mppickedup, item.car != null)) return null;
                RefreshCars();
                Vector3 at = item.useThisForPos.position;
                for (int i = 0; i < cars.Count; i++)
                {
                    tosaveitemscript car = cars[i].Car;
                    if (car == null || car.RB == null || car.car == null) continue;
                    if (!CarCargoPolicy.Moving(car.RB.velocity.magnitude, car.RB.angularVelocity.magnitude)) continue;
                    if (!CarCargoPolicy.Inside(car.transform.InverseTransformPoint(at), cars[i].Min, cars[i].Max)) continue;
                    Collider anchor = car.GetColliderFromIndex(0);
                    if (anchor == null) return null;
                    Vector3 local = anchor.transform.InverseTransformPoint(at);
                    Vector3 rot = (Quaternion.Inverse(anchor.transform.rotation) * item.useThisForPos.rotation).eulerAngles;
                    Rewritten++;
                    return DataByteConverterScript.GetBytes(msgType.itemPosLocal, item.idInSave, car.idInSave, CarCargoPolicy.ColliderAnchor, local, rot);
                }
            }
            catch (Exception error) { Fail(error); }
            return null;
        }

        static void RefreshCars()
        {
            if (carsAt >= 0 && Time.realtimeSinceStartup - carsAt < CarCacheSeconds) return;
            carsAt = Time.realtimeSinceStartup;
            cars.Clear();
            var items = savedatascript.s == null ? null : savedatascript.s.items;
            if (items == null) return;
            foreach (var pair in items)
            {
                tosaveitemscript item = pair.Value;
                if (item == null || item.car == null) continue;
                CarBox box;
                if (!boxes.TryGetValue(item.GetInstanceID(), out box))
                {
                    box = new CarBox { Car = item };
                    if (!LocalBox(item, out box.Min, out box.Max)) continue;
                    boxes[item.GetInstanceID()] = box;
                }
                cars.Add(box);
            }
        }

        // The car's solid colliders as a box in the car's own frame.
        static bool LocalBox(tosaveitemscript car, out Vector3 min, out Vector3 max)
        {
            min = Vector3.zero; max = Vector3.zero;
            bool any = false;
            Collider[] colliders = car.GetComponentsInChildren<Collider>();
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider col = colliders[c];
                if (col == null || col.isTrigger) continue;
                Bounds b = col.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 local = car.transform.InverseTransformPoint(corner);
                    if (!any) { min = local; max = local; any = true; }
                    else { min = Vector3.Min(min, local); max = Vector3.Max(max, local); }
                }
            }
            return any;
        }

        // ---- holding on a guest -------------------------------------------------
        // LateUpdate: keep host-owned loose items in a smoothed car at their last
        // received car-relative pose (gotSyncLocal against collider 0).
        internal static void HoldTick(Func<tosaveitemscript, bool> smoothed)
        {
            if (!enabled || failed) return;
            try
            {
                if (!syncScript.IsConnected() || syncScript.IsHost()) { ReleaseAll(); return; }
                if (Time.realtimeSinceStartup >= nextHoldScan)
                {
                    nextHoldScan = Time.realtimeSinceStartup + HoldScanSeconds;
                    Scan(smoothed);
                }
                foreach (var pair in holds)
                {
                    Hold h = pair.Value;
                    Anchored pose;
                    if (h.Item == null || h.Car == null || !anchored.TryGetValue(pair.Key, out pose)) continue;
                    Collider anchor = h.Car.GetColliderFromIndex(0);
                    if (anchor == null) continue;
                    h.Item.transform.SetPositionAndRotation(anchor.transform.TransformPoint(pose.Pos),
                        anchor.transform.rotation * Quaternion.Euler(pose.Rot));
                }
            }
            catch (Exception error) { Fail(error); }
        }

        static bool Holdable(tosaveitemscript item, out tosaveitemscript car, Func<tosaveitemscript, bool> smoothed)
        {
            car = null;
            Anchored pose;
            if (item == null || item.P == null || !anchored.TryGetValue(item.idInSave, out pose)) return false;
            if (!CarCargoPolicy.Loose(item.RB != null, item.P.physlock != null, item.attachable != null && item.attachable.attached,
                item.P.pickedUp || item.mppickedup, item.car != null)) return false;
            if (item.ThisOneInCharge()) return false;
            var items = savedatascript.s.items;
            if (!items.TryGetValue(pose.Car, out car) || car == null || car.car == null) return false;
            return smoothed(car);
        }

        static void Scan(Func<tosaveitemscript, bool> smoothed)
        {
            var items = savedatascript.s == null ? null : savedatascript.s.items;
            if (items == null) { ReleaseAll(); return; }
            drop.Clear();
            foreach (var pair in holds)
            {
                tosaveitemscript car;
                if (!Holdable(pair.Value.Item, out car, smoothed) || car != pair.Value.Car) drop.Add(pair.Key);
            }
            for (int i = 0; i < drop.Count; i++) Release(drop[i]);
            foreach (var pair in anchored)
            {
                tosaveitemscript item, car;
                if (holds.ContainsKey(pair.Key) || !items.TryGetValue(pair.Key, out item)) continue;
                if (Holdable(item, out car, smoothed)) Apply(item, car);
            }
        }

        static void Apply(tosaveitemscript item, tosaveitemscript car)
        {
            Rigidbody body = item.RB;
            var h = new Hold { Item = item, Car = car, Body = body, Kinematic = body.isKinematic, Gravity = body.useGravity };
            h.CarColliders = car.GetComponentsInChildren<Collider>();
            h.ItemColliders = item.GetComponentsInChildren<Collider>();
            h.Ignored = new bool[h.CarColliders.Length * h.ItemColliders.Length];
            int n = 0;
            for (int a = 0; a < h.CarColliders.Length; a++)
                for (int b = 0; b < h.ItemColliders.Length; b++, n++)
                {
                    Collider first = h.CarColliders[a], second = h.ItemColliders[b];
                    if (first == null || second == null) continue;
                    h.Ignored[n] = Physics.GetIgnoreCollision(first, second);
                    if (!h.Ignored[n]) Physics.IgnoreCollision(first, second, true);
                }
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
            holds[item.idInSave] = h;
            Held++;
        }

        static void Release(uint id)
        {
            Hold h;
            if (!holds.TryGetValue(id, out h)) return;
            holds.Remove(id);
            Released++;
            tosaveitemscript item = h.Item;
            // A pickup, store or attach owns the body's flags from here on.
            bool claimed = item == null || item.P == null || item.P.pickedUp || item.mppickedup || item.P.physlock != null ||
                (item.attachable != null && item.attachable.attached);
            if (h.Body != null && !claimed)
            {
                h.Body.isKinematic = h.Kinematic;
                h.Body.useGravity = h.Gravity;
            }
            int n = 0;
            for (int a = 0; a < h.CarColliders.Length; a++)
                for (int b = 0; b < h.ItemColliders.Length; b++, n++)
                {
                    Collider first = h.CarColliders[a], second = h.ItemColliders[b];
                    if (first != null && second != null && !h.Ignored[n]) Physics.IgnoreCollision(first, second, false);
                }
        }

        internal static void ReleaseAll()
        {
            drop.Clear();
            foreach (var pair in holds) drop.Add(pair.Key);
            for (int i = 0; i < drop.Count; i++) Release(drop[i]);
        }

        internal static void Clear()
        {
            ReleaseAll();
            cars.Clear();
            boxes.Clear();
            anchored.Clear();
            carsAt = -1;
        }

        static void Fail(Exception error)
        {
            if (failed) return;
            failed = true;
            try { ReleaseAll(); } catch (Exception) { }
            Debug.LogWarning("[TLD MP cargo] car-relative cargo disabled after error: " + error.GetType().Name + " " + error.Message);
        }

        internal static string TakeSummary()
        {
            string text = "carCargo{relative=" + Rewritten + " held=" + Held + " released=" + Released + " holding=" + holds.Count + "}";
            Rewritten = Held = Released = 0;
            return text;
        }
    }
}
