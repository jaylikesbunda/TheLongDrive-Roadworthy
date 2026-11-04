using System;
using System.Diagnostics;
using UnityEngine;

namespace TldMp
{
    // The prefab wrapper is temporary; this component belongs on BusFront.
    public sealed class BusArticulation : MonoBehaviour
    {
        private Transform front, back;
        private dinamiccolliderforbus left, right, frontLeft, frontRight;
        private bool ready;
        private static readonly bool Diagnostics = Environment.GetEnvironmentVariable("TLD_BUS_DIAGNOSTICS") == "1";
        private int samples, writes, interpolationChanges;
        private float maxGap, maxAngular, maxShapeDelta;
        private long ticks;
        private float nextReport;

        public static void Install(carscript car)
        {
            if (car == null || car.transform.name != "BusFront") return;
            BusArticulation driver = car.GetComponent<BusArticulation>();
            if (driver == null) driver = car.gameObject.AddComponent<BusArticulation>();
            driver.front = car.transform;
            if (Diagnostics) UnityEngine.Debug.Log("[TldMp.Bus] controller installed on BusFront; waiting for verified joint");
        }

        private static Transform FindBack(tosaveitemscript owner)
        {
            if (owner == null || owner.transform.name != "BusFront" || owner.partslotscripts == null) return null;
            foreach (partslotscript slot in owner.partslotscripts)
            {
                if (slot == null) continue;
                Transform t = slot.transform;
                while (t != null && t.name != "BusBack") t = t.parent;
                if (t == null) continue;
                ConfigurableJoint joint = t.GetComponent<ConfigurableJoint>();
                Rigidbody frontBody = owner.GetComponent<Rigidbody>();
                if (joint != null && frontBody != null && joint.connectedBody == frontBody) return t;
            }
            return null;
        }

        // Native attachment discovery walks only the hierarchy; BusBack is a
        // sibling/root, while its slots are explicitly owned by BusFront.
        public static bool TryAttachedOwner(attachablescript part, out tosaveitemscript owner)
        {
            owner = null;
            if (part == null || !part.attached || part.point == null || part.slot == null) return false;
            partslotscript slot = part.slot;
            tosaveitemscript candidate = slot.tosaveitem;
            if (candidate == null || candidate.partslotscripts == null || !candidate.partslotscripts.Contains(slot)) return false;
            Transform rear = FindBack(candidate);
            if (rear == null)
            {
                BusArticulation existing = candidate.GetComponent<BusArticulation>();
                if (existing != null && existing.back != null && existing.back.GetComponent<ConfigurableJoint>() == null)
                    rear = existing.back;
            }
            if (rear == null) return false;
            Transform t = slot.transform;
            while (t != null && t != rear) t = t.parent;
            if (t == null) return false;
            owner = candidate;
            return true;
        }

        private static dinamiccolliderforbus FindConnector(Transform rear, string name)
        {
            Transform child = rear.Find(name);
            return child == null ? null : child.GetComponent<dinamiccolliderforbus>();
        }

        // Prefix on the native Update. Until a valid live articulation has been found,
        // retain native behavior; unrelated bus door colliders always retain it.
        public static bool IsManagedCollider(dinamiccolliderforbus collider)
        {
            if (collider == null) return false;
            BusArticulation driver = collider.GetComponentInParent<BusArticulation>();
            if (driver == null && collider.cols != null)
                foreach (dinamiccolliderforbus.col entry in collider.cols)
                {
                    if (entry == null) continue;
                    if (entry.t1 != null) driver = entry.t1.GetComponentInParent<BusArticulation>();
                    if (driver == null && entry.t2 != null) driver = entry.t2.GetComponentInParent<BusArticulation>();
                    if (driver != null) break;
                }
            return driver != null && driver.isActiveAndEnabled && driver.ready &&
                (collider == driver.left || collider == driver.right || collider == driver.frontLeft || collider == driver.frontRight);
        }

        private void FixedUpdate()
        {
            long begin = Diagnostics ? Stopwatch.GetTimestamp() : 0;
            ready = false;
            if (front == null) return;
            Transform liveBack = FindBack(front.GetComponent<tosaveitemscript>());
            if (liveBack == null) return;
            if (back != liveBack && Diagnostics) UnityEngine.Debug.Log("[TldMp.Bus] verified rear carriage acquired");
            back = liveBack;
            left = FindConnector(back, "DinamicColliderLeft");
            right = FindConnector(back, "DinamicColliderRight");
            frontLeft = FindConnector(front, "DinamicColliderLeft (1)");
            frontRight = FindConnector(front, "DinamicColliderRight (1)");
            Rigidbody frontBody = front.GetComponent<Rigidbody>();
            Rigidbody backBody = back.GetComponent<Rigidbody>();
            ConfigurableJoint joint = back.GetComponent<ConfigurableJoint>();
            if (frontBody == null || backBody == null || joint == null || joint.connectedBody != frontBody) return;
            ready = true;
            if (frontBody.interpolation != RigidbodyInterpolation.Interpolate) { frontBody.interpolation = RigidbodyInterpolation.Interpolate; interpolationChanges++; }
            if (backBody.interpolation != RigidbodyInterpolation.Interpolate) { backBody.interpolation = RigidbodyInterpolation.Interpolate; interpolationChanges++; }
            Resize(left);
            Resize(right);
            Resize(frontLeft);
            Resize(frontRight);
            if (!Diagnostics) return;
            Vector3 a = BodyPoint(backBody, joint.transform.TransformPoint(joint.anchor));
            Vector3 b = BodyPoint(frontBody, frontBody.transform.TransformPoint(joint.connectedAnchor));
            maxGap = Mathf.Max(maxGap, Vector3.Distance(a, b));
            maxAngular = Mathf.Max(maxAngular, (backBody.angularVelocity - frontBody.angularVelocity).magnitude);
            samples++;
            ticks += Stopwatch.GetTimestamp() - begin;
            if (Time.realtimeSinceStartup >= nextReport)
            {
                UnityEngine.Debug.Log("[TldMp.Bus] samples=" + samples + " maxAnchorGap=" + maxGap.ToString("F5") +
                    " maxRelativeAngularRadSec=" + maxAngular.ToString("F3") + " colliderWrites=" + writes +
                    " maxColliderDelta=" + maxShapeDelta.ToString("F5") + " interpolationChanges=" + interpolationChanges +
                    " front=" + frontBody.interpolation + " rear=" + backBody.interpolation +
                    " fixedWorkMs=" + (ticks * 1000.0 / Stopwatch.Frequency).ToString("F3"));
                samples = writes = interpolationChanges = 0; maxGap = maxAngular = maxShapeDelta = 0f; ticks = 0;
                nextReport = Time.realtimeSinceStartup + 10f;
            }
        }

        private static Vector3 BodyPoint(Rigidbody body, Vector3 renderedPoint)
        {
            return body.position + body.rotation * (Quaternion.Inverse(body.transform.rotation) * (renderedPoint - body.transform.position));
        }

        private static Vector3 PhysicalPoint(Transform point)
        {
            Rigidbody body = point.GetComponentInParent<Rigidbody>();
            return body == null ? point.position : BodyPoint(body, point.position);
        }

        private void Resize(dinamiccolliderforbus connector)
        {
            if (connector == null || !connector.isActiveAndEnabled || connector.cols == null) return;
            foreach (dinamiccolliderforbus.col entry in connector.cols)
            {
                if (entry == null || entry.collider == null || entry.t1 == null || entry.t2 == null) continue;
                float length = Vector3.Distance(PhysicalPoint(entry.t1), PhysicalPoint(entry.t2));
                Vector3 size = entry.collider.size;
                float delta = Mathf.Abs(size.z - length);
                if (!BusArticulationPolicy.ShouldResize(size.z, length)) continue;
                size.z = length;
                entry.collider.size = size;
                if (Diagnostics) { writes++; maxShapeDelta = Mathf.Max(maxShapeDelta, delta); }
            }
        }
    }
}
