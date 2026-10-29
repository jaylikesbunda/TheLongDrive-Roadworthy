using System;
using TldMp;

public static class NativeAttachmentGuardTests
{
    static int assertions;
    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static NativeAttachmentTicket Local(NativeAttachmentGuard guard, uint id, NativeLocalPoseKind kind,
        ulong frame, NativeAttachmentState state, ulong liveFrame, double now, double deadline)
    {
        NativeAttachmentTicket ticket;
        Check(guard.BeginLocalPose(id, kind, frame, state, liveFrame, now, deadline, out ticket), "local pose ticket begins");
        return ticket;
    }

    public static int Main()
    {
        var guard = new NativeAttachmentGuard();
        guard.BeginSession(71);

        // Two same-frame type17 messages are ordered latest-wins.
        NativeAttachmentTicket local1 = Local(guard, 100, NativeLocalPoseKind.StorageLock, 0x10001,
            NativeAttachmentState.World, 0, 1, 51);
        NativeAttachmentTicket local2 = Local(guard, 100, NativeLocalPoseKind.StorageLock, 0x10001,
            NativeAttachmentState.World, 0, 2, 52);
        Check(!guard.IsLocalPoseCurrent(local1, NativeAttachmentState.World, 0, 2.1),
            "new local pose invalidates older same-parent retry");
        Check(guard.IsLocalPoseCurrent(local2, NativeAttachmentState.World, 0, 2.1),
            "latest same-parent local pose remains current");
        var poseCompletionGuard = new NativeAttachmentGuard();
        poseCompletionGuard.BeginSession(7100);
        NativeAttachmentTicket oldPose = Local(poseCompletionGuard, 7100,
            NativeLocalPoseKind.StorageLock, 0x71001, NativeAttachmentState.World, 0, 1, 101);
        NativeAttachmentTicket latestPose = Local(poseCompletionGuard, 7100,
            NativeLocalPoseKind.StorageLock, 0x71001, NativeAttachmentState.World, 0, 2, 102);
        poseCompletionGuard.Complete(oldPose, 3);
        NativeAttachmentTicket posePruneTrigger = Local(poseCompletionGuard, 7101,
            NativeLocalPoseKind.StorageLock, 0x71011, NativeAttachmentState.World, 0, 40, 140);
        Check(poseCompletionGuard.TrackedItems == 2,
            "completion of an obsolete pose does not release the latest pose retention lease");

        // Same category, different mount: every attachable event advances the
        // topology generation and the frame identity protects final alignment.
        NativeAttachmentTicket mountA;
        Check(guard.BeginAttachmentUpdate(200, 0xA001, 3, 53, out mountA), "mount A ticket begins");
        Check(guard.IsAttachmentUpdateCurrent(mountA, true, 0xA001, 3.1), "mount A frame can finish");
        NativeAttachmentTicket mountB;
        Check(guard.BeginAttachmentUpdate(200, 0xB002, 3.2, 53.2, out mountB), "mount B ticket begins");
        Check(!guard.IsAttachmentUpdateCurrent(mountA, true, 0xA001, 3.3),
            "new same-category mount B invalidates mount A retry");
        Check(!guard.IsAttachmentUpdateCurrent(mountB, true, 0xA001, 3.3),
            "mount B cannot apply alignment on mount A frame");
        Check(guard.IsAttachmentUpdateCurrent(mountB, true, 0xB002, 3.3),
            "mount B may apply alignment on its matching frame");
        guard.Complete(mountA, 3.4);
        NativeAttachmentTicket mountBLocal = Local(guard, 200, NativeLocalPoseKind.MountedAnchor,
            0xB002, NativeAttachmentState.World, 0, 3.5, 53.5);
        Check(guard.IsLocalPoseCurrent(mountBLocal, NativeAttachmentState.Mounted, 0xB002, 3.6),
            "completion of an obsolete topology ticket leaves the replacement target marker intact");

        // A queued topology operation can outlive later routine pose traffic.
        // The parent was missing at the first retry; once it appears, the mount
        // still completes and a matching local frame can update its alignment.
        NativeAttachmentTicket queuedMount;
        Check(guard.BeginAttachmentUpdate(250, 0x2501, 4, 104, out queuedMount),
            "mount retry queues while its parent is missing");
        Check(guard.IsAttachmentUpdateCurrent(queuedMount, false, 0, 35),
            "unattached queued topology remains eligible to retry");
        NativeAttachmentTicket routinePose = Local(guard, 250, NativeLocalPoseKind.MountedAnchor,
            0x2501, NativeAttachmentState.World, 0, 36, 106);
        Check(guard.IsAttachmentUpdateCurrent(queuedMount, false, 0, 36.1),
            "later local pose traffic does not cancel queued topology retry");
        Check(guard.IsAttachmentUpdateCurrent(queuedMount, true, 0x2501, 37),
            "queued topology completes when parent appears on its target frame");
        Check(guard.IsLocalPoseCurrent(routinePose, NativeAttachmentState.Mounted, 0x2501, 37.1),
            "later local pose for the matching expected mounted frame remains valid");

        // Local anchors under a mounted wheel are valid when the native parent
        // and slot/collider frame match the itemPosLocal message.
        NativeAttachmentTicket mountedLocal = Local(guard, 300, NativeLocalPoseKind.MountedAnchor,
            0xC003, NativeAttachmentState.Mounted, 0xC003, 5, 55);
        Check(guard.IsLocalPoseCurrent(mountedLocal, NativeAttachmentState.Mounted, 0xC003, 5.1),
            "matching mounted slot/collider local update is allowed");
        Check(!guard.IsLocalPoseCurrent(mountedLocal, NativeAttachmentState.Mounted, 0xC004, 5.2),
            "mounted local update on a different frame is rejected");

        // Native negative-index local poses must still run while native item or
        // parent resolution is pending, preserving ItemPosLocal's ask/retry path.
        NativeAttachmentTicket unresolvedAnchor = Local(guard, 301, NativeLocalPoseKind.MountedAnchor,
            0xC101, NativeAttachmentState.Unknown, 0, 5.3, 55.3);
        Check(guard.IsLocalPoseCurrent(unresolvedAnchor, NativeAttachmentState.Unknown, 0, 5.4),
            "unknown mounted-anchor state keeps native missing-item/parent retry eligible");
        Check(guard.IsLocalPoseCurrent(unresolvedAnchor, NativeAttachmentState.World, 0, 5.5),
            "world state keeps native mounted-anchor retry eligible while awaiting attachment");
        Check(!guard.IsLocalPoseCurrent(unresolvedAnchor, NativeAttachmentState.Mounted, 0xC102, 5.6),
            "resolved mounted anchor cannot apply to an unrelated frame");

        NativeAttachmentTicket staleAnchor = Local(guard, 302, NativeLocalPoseKind.MountedAnchor,
            0xC201, NativeAttachmentState.World, 0, 5.7, 55.7);
        NativeAttachmentTicket replacementMount;
        Check(guard.BeginAttachmentUpdate(302, 0xC202, 5.8, 55.8, out replacementMount),
            "replacement topology begins while old anchor is pending");
        Check(!guard.IsLocalPoseCurrent(staleAnchor, NativeAttachmentState.Mounted, 0xC201, 5.9),
            "new topology invalidates old anchor rather than resurrecting its frame");

        // Any detach event invalidates a pending local retry. A real native
        // detach then permits current and later world movement.
        NativeAttachmentTicket beforeDetach = Local(guard, 400, NativeLocalPoseKind.StorageLock,
            0xD004, NativeAttachmentState.Stored, 0xD004, 6, 56);
        guard.InvalidateTopology(400, 6.1);
        guard.Observe(400, NativeAttachmentState.World, 0, 6.1);
        Check(!guard.IsLocalPoseCurrent(beforeDetach, NativeAttachmentState.World, 0, 6.2),
            "detach invalidates old local retry");
        Check(guard.AllowWorldPose(400, NativeAttachmentState.World, 0, 6.2),
            "world pose after native detach is allowed");

        NativeAttachmentTicket pendingMount;
        Check(guard.BeginAttachmentUpdate(401, 0xD101, 6.3, 56.3, out pendingMount),
            "target topology is recorded for its native mount retry");
        NativeAttachmentTicket currentMountPose = Local(guard, 401, NativeLocalPoseKind.MountedAnchor,
            0xD101, NativeAttachmentState.World, 0, 6.4, 56.4);
        Check(guard.IsLocalPoseCurrent(currentMountPose, NativeAttachmentState.Mounted, 0xD101, 6.5),
            "pending topology target authorizes its matching mounted local alignment");
        guard.InvalidateTopology(401, 6.6);
        Check(!guard.IsLocalPoseCurrent(currentMountPose, NativeAttachmentState.Mounted, 0xD101, 6.7),
            "genuine detach clears pending target and blocks old mounted alignment");
        Check(!guard.IsAttachmentUpdateCurrent(pendingMount, true, 0xD101, 6.7),
            "genuine detach prevents pending native mount retry from resurrecting attachment");

        // A stale global pose delivered before the native detach is discarded;
        // checking it must not mutate the native mounted state or detach item.
        guard.Observe(500, NativeAttachmentState.Mounted, 0x5001, 7);
        Check(!guard.AllowWorldPose(500, NativeAttachmentState.Mounted, 0x5001, 7.1),
            "old global pose before native detach is rejected");
        NativeAttachmentTicket mountedCurrent = Local(guard, 500, NativeLocalPoseKind.MountedAnchor,
            0x5001, NativeAttachmentState.Mounted, 0x5001, 7.2, 57.2);
        Check(guard.IsLocalPoseCurrent(mountedCurrent, NativeAttachmentState.Mounted, 0x5001, 7.3),
            "rejected global pose did not detach or disturb mounted item");
        guard.InvalidateTopology(500, 7.4);
        guard.Observe(500, NativeAttachmentState.World, 0, 7.4);
        Check(guard.AllowWorldPose(500, NativeAttachmentState.World, 0, 7.5),
            "global motion after actual native detach is allowed");

        // Applying a positive-index native storage pose can itself change World
        // to Stored. Observation updates its remembered state without canceling
        // the valid local ticket while it finishes in the matching physlock frame.
        NativeAttachmentTicket storage = Local(guard, 600, NativeLocalPoseKind.StorageLock,
            0x6001, NativeAttachmentState.World, 0, 8, 58);
        guard.Observe(600, NativeAttachmentState.Stored, 0x6001, 8.1);
        Check(guard.IsLocalPoseCurrent(storage, NativeAttachmentState.Stored, 0x6001, 8.2),
            "expected World-to-Stored completion in the same physlock frame is allowed");
        Check(!guard.IsLocalPoseCurrent(storage, NativeAttachmentState.Stored, 0x6002, 8.3),
            "storage completion on a different frame is rejected");

        // Retention follows the actual finite native retry deadline rather than
        // pruning a ticket at StateLifetime while its coroutine is still valid.
        NativeAttachmentTicket longRetry = Local(guard, 700, NativeLocalPoseKind.StorageLock,
            0x7001, NativeAttachmentState.World, 0, 9, 109);
        NativeAttachmentTicket longTopology;
        Check(guard.BeginAttachmentUpdate(701, 0x7011, 9, 109, out longTopology),
            "long topology retry queues");
        Check(guard.IsLocalPoseCurrent(longRetry, NativeAttachmentState.World, 0, 40),
            "local retry state survives past the 30-second idle prune interval");
        Check(guard.IsAttachmentUpdateCurrent(longTopology, false, 0, 40),
            "topology retry state survives past the 30-second idle prune interval");
        Check(!guard.IsLocalPoseCurrent(longRetry, NativeAttachmentState.World, 0, 110),
            "local retry expires at the native configured deadline");
        Check(!guard.IsAttachmentUpdateCurrent(longTopology, false, 0, 110),
            "topology retry expires at the native configured deadline");

        var completedLeaseGuard = new NativeAttachmentGuard();
        completedLeaseGuard.BeginSession(710);
        NativeAttachmentTicket completedLease;
        Check(completedLeaseGuard.BeginAttachmentUpdate(710, 0x7101, 1, 10000, out completedLease),
            "native retry with an externally bounded coroutine lease begins");
        completedLeaseGuard.Complete(completedLease, 2);
        NativeAttachmentTicket pruneTrigger = Local(completedLeaseGuard, 711,
            NativeLocalPoseKind.StorageLock, 0x7111, NativeAttachmentState.World, 0, 33, 10033);
        Check(completedLeaseGuard.TrackedItems == 1,
            "completed retry metadata is pruned after the idle retention window");

        NativeAttachmentTicket activeAfterOldCompletion;
        Check(completedLeaseGuard.BeginAttachmentUpdate(712, 0x7121, 34, 10034,
            out activeAfterOldCompletion), "replacement retry begins after completed metadata prune");
        NativeAttachmentTicket newerTopology;
        Check(completedLeaseGuard.BeginAttachmentUpdate(712, 0x7122, 35, 10035, out newerTopology),
            "newer retry replaces old topology generation");
        completedLeaseGuard.Complete(activeAfterOldCompletion, 36);
        NativeAttachmentTicket topologyProbe = Local(completedLeaseGuard, 712,
            NativeLocalPoseKind.MountedAnchor, 0x7122, NativeAttachmentState.World, 0, 37, 10037);
        Check(completedLeaseGuard.IsLocalPoseCurrent(topologyProbe, NativeAttachmentState.Mounted, 0x7122, 38),
            "late completion of old topology cannot clear the newer target marker");

        NativeAttachmentTicket oldSession = Local(guard, 800, NativeLocalPoseKind.StorageLock,
            0x8001, NativeAttachmentState.Stored, 0x8001, 11, 61);
        guard.BeginSession(72);
        Check(!guard.IsLocalPoseCurrent(oldSession, NativeAttachmentState.Stored, 0x8001, 11.1),
            "session replacement invalidates old retry");
        guard.Complete(oldSession, 11.2);
        guard.EndSession();
        Check(guard.AllowWorldPose(900, NativeAttachmentState.Mounted, 0, 12),
            "disconnected native path remains vanilla");

        Console.WriteLine("NativeAttachmentGuardTests passed: " + assertions + " assertions");
        return 0;
    }
}
