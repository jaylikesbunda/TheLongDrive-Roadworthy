using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TldMp
{
    // Replacement for Collision.relativeVelocity in breakablescript's collision
    // damage calculation. Keep all threshold, health and death logic native.
    public static class NpcVehicleImpact
    {
        const float MaxScale = 4f;

        // Animated rabbit head/body colliders belong to separate, kinematic bone
        // bodies. Unity delivers their contacts to those bodies, not the root
        // breakablescript. Bind its patched native callback once; forwarding must
        // retain native thresholds, DamageFrom, death, ragdoll and save updates.
        public static void ConfigureRabbit(ragdollactivatescript ragdoll)
        {
            if (ragdoll == null || ragdoll.GetComponent<nyulscript>() == null) return;
            var victim = ragdoll.GetComponent<breakablescript>();
            if (victim == null || ragdoll.RB == null || ragdoll.animColliders == null) return;
            var state = victim.GetComponent<RabbitVehicleImpactState>();
            if (state == null) state = victim.gameObject.AddComponent<RabbitVehicleImpactState>();
            state.Bind(victim, ragdoll);
            foreach (var collider in ragdoll.animColliders)
            {
                if (collider == null || collider.isTrigger) continue;
                var body = collider.attachedRigidbody;
                if (body == null || body == ragdoll.RB) continue;
                var receiver = body.GetComponent<RabbitVehicleImpactReceiver>();
                if (receiver == null) receiver = body.gameObject.AddComponent<RabbitVehicleImpactReceiver>();
                receiver.State = state;
                receiver.enabled = !ragdoll.ragdoll;
            }
        }

        public static bool AdmitRabbitCollision(breakablescript victim, Collision collision)
        {
            if (victim == null || (victim.AI == null && victim.newAI == null) || collision == null || collision.rigidbody == null ||
                collision.rigidbody.GetComponentInParent<carscript>() == null) return true;
            var state = victim.GetComponent<RabbitVehicleImpactState>();
            if (state == null || victim.noCollider || victim.destroyed) return true;
            if (!syncScript.IsHostOrSingle()) return false;
            // A harmless root contact must not consume a damaging bone contact.
            if (victim.onlyDecreaseAfterAttackIfAbove && RelativeVelocity(collision, victim).magnitude <= victim.onlyDecreaseAfterAttackIfAboveHealth) return true;
            return state.Admit(collision.rigidbody);
        }
        public static Vector3 RelativeVelocity(Collision collision, breakablescript victim)
        {
            Vector3 velocity = collision.relativeVelocity;
            if (victim == null || (victim.AI == null && victim.newAI == null)) return velocity;
            Rigidbody vehicle = collision.rigidbody;
            Rigidbody creature = victim.GetComponent<Rigidbody>();
            if (vehicle == null || creature == null || vehicle.GetComponentInParent<carscript>() == null) return velocity;
            float vehicleMass = vehicle.mass, creatureMass = creature.mass;
            if (float.IsNaN(vehicleMass) || float.IsInfinity(vehicleMass) || vehicleMass <= 0f ||
                float.IsNaN(creatureMass) || float.IsInfinity(creatureMass) || creatureMass <= 0f) return velocity;
            double ratio = Math.Sqrt((double)vehicleMass / creatureMass);
            float scale = (float)Math.Max(1.0, Math.Min(MaxScale, ratio));
            return velocity * scale;
        }
    }

    public sealed class RabbitVehicleImpactState : MonoBehaviour
    {
        internal breakablescript Victim;
        internal ragdollactivatescript Ragdoll;
        Action<Collision> nativeCollision;
        readonly List<int> vehicles = new List<int>(4);
        float step = float.NaN;
        static float nextReport;
        static long forwarded;

        internal void Bind(breakablescript victim, ragdollactivatescript ragdoll)
        {
            if (Victim == victim && nativeCollision != null) { Ragdoll = ragdoll; return; }
            var method = typeof(breakablescript).GetMethod("OnCollisionEnter", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("breakablescript.OnCollisionEnter");
            nativeCollision = (Action<Collision>)Delegate.CreateDelegate(typeof(Action<Collision>), victim, method);
            Victim = victim;
            Ragdoll = ragdoll;
            vehicles.Clear();
            step = float.NaN;
        }

        internal bool Admit(Rigidbody vehicle)
        {
            // Only authority changes health; repeat contacts from the root and
            // several bones in the same physics step represent one impact.
            if (!syncScript.IsHostOrSingle()) return false;
            if (step != Time.fixedTime) { step = Time.fixedTime; vehicles.Clear(); }
            int id = vehicle.GetInstanceID();
            if (vehicles.Contains(id) || vehicles.Count >= 16) return false;
            vehicles.Add(id);
            return true;
        }

        internal void Forward(Collision collision)
        {
            if (Victim == null || Victim.destroyed || Victim.noCollider ||
                Ragdoll == null || Ragdoll.ragdoll || nativeCollision == null ||
                collision == null || collision.contactCount == 0 || collision.rigidbody == null ||
                collision.rigidbody.GetComponentInParent<carscript>() == null) return;
            float before = Victim.health;
            nativeCollision(collision);
            forwarded++;
            if (Time.unscaledTime < nextReport) return;
            nextReport = Time.unscaledTime + 2f;
            Debug.Log("[Roadworthy rabbit impact] forwarded=" + forwarded + " speed=" + collision.relativeVelocity.magnitude +
                " health_before=" + before + " health_after=" + Victim.health + " destroyed=" + Victim.destroyed +
                " ragdoll=" + Ragdoll.ragdoll);
        }
    }

    public sealed class RabbitVehicleImpactReceiver : MonoBehaviour
    {
        internal RabbitVehicleImpactState State;
        void OnCollisionEnter(Collision collision)
        {
            // Unity also sends collisions to disabled behaviours.
            if (enabled && State != null) State.Forward(collision);
        }
    }
}
