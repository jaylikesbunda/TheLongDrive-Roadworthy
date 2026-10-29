using System;
using UnityEngine;

namespace TldMp
{
    // Captures the native root-to-sitPos offset after fpscontroller.GetIn has
    // completed, then describes narrowly-scoped repairs for an actually
    // occupied, still-live seat. The caller owns Unity mutations and must keep
    // native GetOut/death/seat-destruction paths intact.
    public sealed class NativeSeatSafety
    {
        [Flags]
        public enum Repair
        {
            None = 0,
            Parent = 1,
            Offset = 2,
            IndependentRigidbody = 4
        }

        public enum Reason
        {
            None = 0,
            ParentDrift = 1,
            RootOffsetDrift = 2,
            IndependentRigidbody = 3,
            InvalidRootOffset = 4
        }

        const int MaxDiagnosticReasons = 8;
        const float DefaultOffsetTolerance = 0.025f;

        bool captured;
        int playerId, seatId, sitPosId;
        Vector3 localOffset;
        int reportedReasons;
        int reportedExits;

        public bool HasSnapshot { get { return captured; } }
        public Vector3 CapturedLocalOffset { get { return localOffset; } }

        // Call immediately after native GetIn, after it has parented the root
        // and destroyed its own Rigidbody. Never establish a baseline from a
        // later, already-drifting pose.
        public bool Capture(bool genuinelySeated, int newPlayerId, int newSeatId,
            int newSitPosId, int currentParentId, bool hasIndependentRigidbody,
            Vector3 rootLocalOffset)
        {
            Clear();
            if (!genuinelySeated || newPlayerId == 0 || newSeatId == 0 ||
                newSitPosId == 0 || currentParentId != newSitPosId ||
                hasIndependentRigidbody || !IsFinite(rootLocalOffset))
                return false;

            captured = true;
            playerId = newPlayerId;
            seatId = newSeatId;
            sitPosId = newSitPosId;
            localOffset = rootLocalOffset;
            return true;
        }

        // A false seating/live-seat predicate invalidates the snapshot and
        // deliberately returns no repair. This prevents re-entry after any
        // legitimate native exit, death, or seat destruction.
        public Repair Evaluate(bool genuinelySeated, bool seatAndSitPosAlive,
            int currentPlayerId, int currentSeatId, int currentSitPosId,
            int currentParentId, bool hasIndependentRigidbody,
            Vector3 rootLocalOffset, out Reason reason)
        {
            reason = Reason.None;
            if (!genuinelySeated || !seatAndSitPosAlive)
            {
                Clear();
                return Repair.None;
            }
            if (!captured || currentPlayerId != playerId || currentSeatId != seatId ||
                currentSitPosId != sitPosId)
                return Repair.None;

            Repair repair = Repair.None;
            if (currentParentId != sitPosId)
            {
                repair |= Repair.Parent | Repair.Offset;
                reason = Reason.ParentDrift;
            }
            else if (!IsFinite(rootLocalOffset))
            {
                repair |= Repair.Offset;
                reason = Reason.InvalidRootOffset;
            }
            else if ((rootLocalOffset - localOffset).sqrMagnitude >
                DefaultOffsetTolerance * DefaultOffsetTolerance)
            {
                repair |= Repair.Offset;
                reason = Reason.RootOffsetDrift;
            }
            if (hasIndependentRigidbody)
            {
                repair |= Repair.IndependentRigidbody;
                if (reason == Reason.None) reason = Reason.IndependentRigidbody;
            }
            return repair;
        }

        // Bounded by distinct reason (at most three) and a hard session cap.
        // The bridge can use this to log both GetOut classification and drift
        // repairs without producing per-frame noise.
        public bool ShouldLog(Reason reason)
        {
            if (reason == Reason.None || CountBits(reportedReasons) >= MaxDiagnosticReasons)
                return false;
            int bit = 1 << (int)reason;
            if ((reportedReasons & bit) != 0) return false;
            reportedReasons |= bit;
            return true;
        }

        public bool ShouldLogExit(bool forced, bool useDown)
        {
            int category = forced ? 1 : (useDown ? 2 : 3);
            int bit = 1 << category;
            if ((reportedExits & bit) != 0 || CountBits(reportedExits) >= MaxDiagnosticReasons)
                return false;
            reportedExits |= bit;
            return true;
        }

        static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }

        public static string ExitReason(bool forced, bool useDown)
        {
            if (forced) return "forced";
            if (useDown) return "input";
            return "native";
        }

        public static string ReasonText(Reason reason)
        {
            switch (reason)
            {
                case Reason.ParentDrift: return "parent-drift";
                case Reason.RootOffsetDrift: return "root-offset-drift";
                case Reason.IndependentRigidbody: return "independent-rigidbody";
                case Reason.InvalidRootOffset: return "invalid-root-offset";
                default: return "none";
            }
        }

        public void Clear()
        {
            captured = false;
            playerId = seatId = sitPosId = 0;
            localOffset = Vector3.zero;
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !Single.IsNaN(value) && !Single.IsInfinity(value);
        }
    }
}
