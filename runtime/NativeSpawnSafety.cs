using System;

namespace TldMp
{
    // Small policy for protecting a local player while a native world restore
    // and the MP snapshot are both in flight. Unity-facing code owns the
    // actual Rigidbody writes; this class only decides when to hold/release
    // and preserves the pre-hold flags for abort/reset paths.
    public enum NativeSpawnSafetyAction
    {
        None,
        Hold,
        Release,
        Restore
    }

    public sealed class NativeSpawnSafety
    {
        bool active;
        bool supportSeen;
        bool physicsOpportunitySeen;
        bool capturedIsKinematic;
        bool capturedUseGravity;

        public bool Active { get { return active; } }
        public bool CapturedIsKinematic { get { return capturedIsKinematic; } }
        public bool CapturedUseGravity { get { return capturedUseGravity; } }
        public bool HasSpawnSupport { get { return supportSeen; } }
        public bool HasPhysicsOpportunity { get { return physicsOpportunitySeen; } }

        public void Begin(bool isKinematic, bool useGravity)
        {
            if (active) throw new InvalidOperationException("Abort the current spawn hold before replacing its player body.");
            capturedIsKinematic = isKinematic;
            capturedUseGravity = useGravity;
            supportSeen = false;
            physicsOpportunitySeen = false;
            active = true;
        }

        // supportAtIntendedSpawn must be measured at the chosen spawn point,
        // independently of the player's current (possibly already-fallen)
        // position. physicsOpportunityOccurred means a physics step completed
        // after support was first observed.
        public NativeSpawnSafetyAction Evaluate(
            bool joinPending,
            bool abortedOrReset,
            bool playerAlive,
            bool seated,
            bool supportAtIntendedSpawn,
            bool physicsOpportunityOccurred,
            bool live)
        {
            if (!active) return NativeSpawnSafetyAction.None;

            // Seat/death transitions belong to the native game. Stop holding
            // and restore the captured flags so those paths can proceed.
            if (abortedOrReset || !joinPending || !playerAlive || seated)
            {
                active = false;
                return NativeSpawnSafetyAction.Restore;
            }

            bool supportWasAlreadySeen = supportSeen;
            if (!supportAtIntendedSpawn)
            {
                supportSeen = false;
                physicsOpportunitySeen = false;
            }
            else
            {
                supportSeen = true;
                if (supportWasAlreadySeen && physicsOpportunityOccurred)
                    physicsOpportunitySeen = true;
            }

            if (supportSeen && physicsOpportunitySeen && live)
            {
                active = false;
                return NativeSpawnSafetyAction.Release;
            }

            return NativeSpawnSafetyAction.Hold;
        }

        public NativeSpawnSafetyAction Abort()
        {
            if (!active) return NativeSpawnSafetyAction.None;
            active = false;
            return NativeSpawnSafetyAction.Restore;
        }
    }
}
