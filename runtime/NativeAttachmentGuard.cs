using System;
using System.Collections.Generic;

namespace TldMp
{
    // These are observations of native state, not multiplayer ownership states.
    public enum NativeAttachmentState : byte
    {
        Unknown = 0,
        World = 1,
        Mounted = 2,
        Stored = 3
    }

    // msgType.itemPosLocal includes both storage locks and mounted slot/collider
    // anchors. They have different valid native state and frame checks.
    public enum NativeLocalPoseKind : byte
    {
        StorageLock = 0,
        MountedAnchor = 1
    }

    public struct NativeAttachmentTicket
    {
        internal uint ItemID;
        internal ulong Session;
        internal ulong PoseGeneration;
        internal ulong TopologyGeneration;
        internal ulong Frame;
        internal double Deadline;
        internal NativeLocalPoseKind Kind;
        internal bool IsTopology;
        internal bool Valid;
    }

    public sealed class NativeAttachmentGuard
    {
        const int MaxItems = 4096;
        const double StateLifetime = 30.0;

        sealed class State
        {
            public NativeAttachmentState Attachment;
            public ulong Frame;
            public ulong PoseGeneration;
            public ulong TopologyGeneration;
            public double Seen;
            public double PoseUntil;
            public double TopologyUntil;
            public ulong PendingTopologyFrame;
            public ulong PendingTopologyGeneration;
            public double PendingTopologyUntil;
            public bool HasPendingTopologyFrame;
        }

        readonly Dictionary<uint, State> items = new Dictionary<uint, State>();
        ulong session;
        ulong generation;

        public ulong Session { get { return session; } }
        public int TrackedItems { get { return items.Count; } }

        // Use a fresh identity for each native multiplayer/world session.
        public void BeginSession(ulong identity)
        {
            if (identity == 0) throw new ArgumentOutOfRangeException("identity");
            session = identity;
            items.Clear();
            generation++;
        }

        public void EndSession()
        {
            session = 0;
            items.Clear();
            generation++;
        }

        State Get(uint id, double now, bool create)
        {
            State state;
            if (items.TryGetValue(id, out state))
            {
                state.Seen = now;
                return state;
            }
            Prune(now);
            if (!create || items.Count >= MaxItems) return null;
            state = new State
            {
                Attachment = NativeAttachmentState.Unknown,
                PoseGeneration = ++generation,
                TopologyGeneration = ++generation,
                Seen = now
            };
            items[id] = state;
            return state;
        }

        void Prune(double now)
        {
            if (items.Count == 0) return;
            var expired = new List<uint>();
            foreach (var pair in items)
            {
                State state = pair.Value;
                double activeUntil = Math.Max(state.PoseUntil, state.TopologyUntil);
                if (now >= state.Seen && now - state.Seen > StateLifetime && now > activeUntil)
                    expired.Add(pair.Key);
            }
            for (int i = 0; i < expired.Count; i++) items.Remove(expired[i]);
        }

        // Start a new native itemPosLocal operation. It supersedes only earlier
        // pose retries; it deliberately leaves a queued UpdAttachable retry live.
        public bool BeginLocalPose(uint id, NativeLocalPoseKind kind, ulong frame,
            NativeAttachmentState liveState, ulong liveFrame, double now, double deadline,
            out NativeAttachmentTicket ticket)
        {
            ticket = new NativeAttachmentTicket();
            if (!ValidRequest(id, now, deadline)) return false;
            State state = Get(id, now, true);
            if (state == null) return false;
            state.PoseGeneration = ++generation;
            state.PoseUntil = deadline;
            ObserveState(state, liveState, liveFrame);
            ticket.ItemID = id;
            ticket.Session = session;
            ticket.PoseGeneration = state.PoseGeneration;
            ticket.TopologyGeneration = state.TopologyGeneration;
            ticket.Frame = frame;
            ticket.Deadline = deadline;
            ticket.Kind = kind;
            ticket.Valid = true;
            return true;
        }

        // A new type42 attachment or detach operation invalidates both older
        // attachment retries and local poses, regardless of state category.
        public void InvalidateTopology(uint id, double now)
        {
            if (session == 0 || id == 0) return;
            State state = Get(id, now, true);
            if (state == null) return;
            state.TopologyGeneration = ++generation;
            state.PoseGeneration = ++generation;
            state.TopologyUntil = now;
            state.PoseUntil = now;
            state.HasPendingTopologyFrame = false;
            state.PendingTopologyUntil = now;
        }

        // A new UpdAttachable retry ticket is independent of later local poses.
        // Its creation itself is a topology event and retires both older lanes.
        public bool BeginAttachmentUpdate(uint id, ulong targetFrame, double now,
            double deadline, out NativeAttachmentTicket ticket)
        {
            ticket = new NativeAttachmentTicket();
            if (!ValidRequest(id, now, deadline)) return false;
            InvalidateTopology(id, now);
            State state = Get(id, now, true);
            if (state == null) return false;
            state.TopologyUntil = deadline;
            state.PendingTopologyFrame = targetFrame;
            state.PendingTopologyGeneration = state.TopologyGeneration;
            state.PendingTopologyUntil = deadline;
            state.HasPendingTopologyFrame = true;
            ticket.ItemID = id;
            ticket.Session = session;
            ticket.PoseGeneration = state.PoseGeneration;
            ticket.TopologyGeneration = state.TopologyGeneration;
            ticket.Frame = targetFrame;
            ticket.Deadline = deadline;
            ticket.Kind = NativeLocalPoseKind.MountedAnchor;
            ticket.IsTopology = true;
            ticket.Valid = true;
            return true;
        }

        static bool ValidRequest(uint id, double now, double deadline)
        {
            return id != 0 && !Double.IsNaN(now) && !Double.IsInfinity(now) && now >= 0 &&
                !Double.IsNaN(deadline) && !Double.IsInfinity(deadline) && deadline >= now;
        }

        // Record the native state after an event or from a live read. Observation
        // does not advance either generation, so a ticket's own World->Stored or
        // World->Mounted completion can be observed without canceling itself.
        public void Observe(uint id, NativeAttachmentState attachment, ulong frame, double now)
        {
            if (session == 0 || id == 0) return;
            State state = Get(id, now, true);
            if (state == null) return;
            ObserveState(state, attachment, frame);
        }

        static void ObserveState(State state, NativeAttachmentState attachment, ulong frame)
        {
            state.Attachment = attachment;
            state.Frame = frame;
        }

        // Global poses do not alter attachment state. A stale world packet cannot
        // detach an item because this decision only reads native state.
        public bool AllowWorldPose(uint id, NativeAttachmentState liveState, ulong liveFrame, double now)
        {
            if (session == 0 || id == 0) return true;
            Observe(id, liveState, liveFrame, now);
            return liveState != NativeAttachmentState.Mounted && liveState != NativeAttachmentState.Stored;
        }

        // Check before every ItemPosLocal retry/apply. StorageLock permits its
        // expected World->Stored completion in the same physlock frame.
        // MountedAnchor keeps native retries alive while the item or parent is
        // not yet known/mounted. Once mounted, apply only on the exact live frame
        // or on the target frame of the current pending native topology retry.
        public bool IsLocalPoseCurrent(NativeAttachmentTicket ticket, NativeAttachmentState liveState,
            ulong liveFrame, double now)
        {
            if (!Current(ticket, now) || ticket.IsTopology) return false;
            State state = Get(ticket.ItemID, now, false);
            if (!Matches(ticket, state, false)) return false;
            ObserveState(state, liveState, liveFrame);
            if (ticket.Kind == NativeLocalPoseKind.StorageLock)
                return liveState != NativeAttachmentState.Mounted &&
                    (liveState != NativeAttachmentState.Stored || liveFrame == ticket.Frame);
            if (liveState == NativeAttachmentState.Unknown || liveState == NativeAttachmentState.World)
                return true;
            if (liveState != NativeAttachmentState.Mounted) return false;
            return liveFrame == ticket.Frame ||
                (state.HasPendingTopologyFrame &&
                 state.PendingTopologyGeneration == ticket.TopologyGeneration &&
                 state.PendingTopologyFrame == ticket.Frame &&
                 now <= state.PendingTopologyUntil);
        }

        // Check before each UpdAttachable retry and before final local alignment.
        // A later local pose leaves this ticket valid; a later topology event does not.
        public bool IsAttachmentUpdateCurrent(NativeAttachmentTicket ticket, bool attached,
            ulong liveFrame, double now)
        {
            if (!Current(ticket, now) || !ticket.IsTopology) return false;
            State state = Get(ticket.ItemID, now, false);
            if (!Matches(ticket, state, true)) return false;
            if (attached) ObserveState(state, NativeAttachmentState.Mounted, liveFrame);
            return !attached || liveFrame == ticket.Frame;
        }

        // Release only the retry lease owned by this exact operation. A native
        // coroutine may finish after its nominal multiplayer timeout, so this
        // deliberately validates identity/generation without checking Deadline.
        public void Complete(NativeAttachmentTicket ticket, double now)
        {
            if (!ticket.Valid || session == 0 || ticket.Session != session || ticket.ItemID == 0 ||
                Double.IsNaN(now) || Double.IsInfinity(now) || now < 0) return;
            State state = Get(ticket.ItemID, now, false);
            if (state == null || state.TopologyGeneration != ticket.TopologyGeneration) return;
            if (ticket.IsTopology)
            {
                state.TopologyUntil = now;
                if (state.HasPendingTopologyFrame &&
                    state.PendingTopologyGeneration == ticket.TopologyGeneration &&
                    state.PendingTopologyFrame == ticket.Frame)
                {
                    state.HasPendingTopologyFrame = false;
                    state.PendingTopologyUntil = now;
                }
            }
            else if (state.PoseGeneration == ticket.PoseGeneration)
            {
                state.PoseUntil = now;
            }
        }

        bool Matches(NativeAttachmentTicket ticket, State state, bool topology)
        {
            if (state == null || state.TopologyGeneration != ticket.TopologyGeneration) return false;
            return topology || state.PoseGeneration == ticket.PoseGeneration;
        }

        bool Current(NativeAttachmentTicket ticket, double now)
        {
            if (!ticket.Valid || session == 0 || ticket.Session != session ||
                Double.IsNaN(now) || Double.IsInfinity(now) || now < 0 || now > ticket.Deadline) return false;
            return items.ContainsKey(ticket.ItemID);
        }
    }
}
