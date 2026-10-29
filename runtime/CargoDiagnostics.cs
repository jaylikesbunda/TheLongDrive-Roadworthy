using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TldMp
{
    // Opt-in, bounded observation of wheel-like pickupables near the local car.
    // This class only reads game state; it never changes transforms or physics.
    public static class CargoDiagnostics
    {
        const int MaxTracked = 64;
        const int MaxPendingPacketIDs = 64;
        const int MaxLogLines = 300;
        const float SampleInterval = .25f;
        const float ReportInterval = 3f;
        const float Radius = 30f;

        sealed class Track
        {
            public tosaveitemscript Item;
            public float SeenAt, LastLogAt;
            public int ParentID, LockID, CarID;
            public Vector3 Position, LocalPosition, CarRelativePosition;
            public bool HasSample, Rigidbody, Kinematic, Picked, Stored, Wheel, Tire, ParentedToCar;
            public int[] Packets = new int[8];
            public ulong LastPeer;
            public byte LastType;
            public bool LastIncoming;
        }

        static readonly Dictionary<uint, Track> tracks = new Dictionary<uint, Track>();
        static readonly Dictionary<uint, int[]> pendingPackets = new Dictionary<uint, int[]>();
        static readonly List<uint> stale = new List<uint>();
        static float nextSample, nextReport;
        static int written;
        static bool warned;
        static int kinematicWarnings;
        static readonly bool enabled = ModDiagnostics.On;

        static CargoDiagnostics()
        {
            if (enabled) Application.logMessageReceived += OnLogMessage;
        }

        static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (condition == null || condition.IndexOf("velocity of a kinematic body is not supported", StringComparison.OrdinalIgnoreCase) < 0)
                return;
            if (kinematicWarnings < Int32.MaxValue) kinematicWarnings++;
            if (warned) return;
            warned = true;
            if (written >= MaxLogLines) return;
            written++;
            // Unity's native PhysX warning commonly supplies an empty managed stack.
            Debug.Log("[TLD cargo diag] first kinematic velocity warning; managedStack=" +
                (String.IsNullOrEmpty(stackTrace) ? "<empty from Unity>" : stackTrace));
        }
        public static int TakeKinematicWarnings()
        {
            int value = kinematicWarnings;
            kinematicWarnings = 0;
            return value;
        }

        static string F(float v) { return v.ToString("F2", CultureInfo.InvariantCulture); }
        static int InstanceID(UnityEngine.Object value) { return value == null ? 0 : value.GetInstanceID(); }

        static bool IsWheelLike(tosaveitemscript item)
        {
            return item != null && item.P != null && (item.P.wheel != null || item.P.gumi != null);
        }

        static string ParentPath(Transform t)
        {
            if (t == null) return "<root>";
            string result = t.name;
            int depth = 0;
            while (t.parent != null && depth++ < 12)
            {
                t = t.parent;
                result = t.name + "/" + result;
            }
            return result;
        }

        static string CarInfo(tosaveitemscript item)
        {
            if (item == null || item.car == null) return "none";
            return "id=" + item.idInSave + ",name=" + item.name;
        }

        static Track GetTrack(tosaveitemscript item)
        {
            Track track;
            if (tracks.TryGetValue(item.idInSave, out track) && track.Item == item) return track;
            if (tracks.Count >= MaxTracked) return null;
            track = new Track { Item = item };
            int[] counts;
            if (pendingPackets.TryGetValue(item.idInSave, out counts))
            {
                Array.Copy(counts, track.Packets, Math.Min(counts.Length, track.Packets.Length));
                pendingPackets.Remove(item.idInSave);
            }
            tracks[item.idInSave] = track;
            return track;
        }

        static void Log(Track track, string reason, tosaveitemscript nearCar)
        {
            if (written >= MaxLogLines) return;
            var item = track.Item;
            if (item == null) return;
            var p = item.transform;
            var rb = item.RB != null ? item.RB : item.GetComponent<Rigidbody>();
            string packetSummary = "";
            for (int i = 0; i < track.Packets.Length; i++)
            {
                if (track.Packets[i] == 0) continue;
                if (packetSummary.Length != 0) packetSummary += ",";
                packetSummary += (i < 4 ? "out" : "in") + (16 + (i % 4)) + "=" + track.Packets[i];
                track.Packets[i] = 0;
            }
            written++;
            track.LastLogAt = Time.unscaledTime;
            Debug.Log("[TLD cargo diag] " + reason + " id=" + item.idInSave +
                " prefab=" + item.id + " name=" + item.name +
                " kind=" + (item.P.wheel != null ? "wheel" : "tire") +
                " parent=" + ParentPath(p.parent) + " parentId=" + InstanceID(p.parent) +
                " physlock=" + (item.P.physlock == null ? "none" : item.P.physlock.name) +
                " lockId=" + InstanceID(item.P.physlock) + " mpstored=" + item.mpstored +
                " held=" + item.mppickedup + " picked=" + item.P.pickedUp +
                " rb=" + (rb != null) + " kinematic=" + (rb != null && rb.isKinematic) +
                " velocity=" + (rb == null ? "none" : rb.velocity.ToString("F2")) +
                " angular=" + (rb == null ? "none" : rb.angularVelocity.ToString("F2")) +
                " ownCar={" + CarInfo(nearCar) + "}" +
                " pos=" + p.position.ToString("F2") + " local=" + p.localPosition.ToString("F2") +
                " relativeCar=" + track.CarRelativePosition.ToString("F2") + " parentedToCar=" + track.ParentedToCar +
                " events=" + (packetSummary.Length == 0 ? "none" : packetSummary) +
                " lastPacket=" + (track.LastIncoming ? "in" : "out") + (track.LastType == 0 ? "-" : track.LastType.ToString()) +
                " peer=" + track.LastPeer);
        }

        public static void Tick()
        {
            if (!enabled) return;
            float now = Time.unscaledTime;
            if (now < nextSample) return;
            nextSample = now + SampleInterval;

            var player = mainscript.s == null ? null : mainscript.s.player;
            var localCar = player == null ? null : player.Car;
            if (localCar == null || savedatascript.s == null || savedatascript.s.items == null)
            {
                if (now >= nextReport) nextReport = now + ReportInterval;
                return;
            }

            Vector3 center = localCar.transform.position;
            stale.Clear();
            foreach (var pair in tracks)
                if (pair.Value.Item == null || now - pair.Value.SeenAt > 6f) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) tracks.Remove(stale[i]);

            int inspected = 0;
            foreach (var pair in savedatascript.s.items)
            {
                var item = pair.Value;
                if (!IsWheelLike(item)) continue;
                if ((item.transform.position - center).sqrMagnitude > Radius * Radius) continue;
                var track = GetTrack(item);
                if (track == null) continue;
                inspected++;
                track.SeenAt = now;
                var p = item.transform;
                var rb = item.RB != null ? item.RB : item.GetComponent<Rigidbody>();
                bool isWheel = item.P.wheel != null;
                bool tire = item.P.gumi != null;
                int parentID = InstanceID(p.parent);
                int lockID = InstanceID(item.P.physlock);
                int carID = InstanceID(localCar);
                bool kinematic = rb != null && rb.isKinematic;
                bool stored = item.mpstored;
                bool picked = item.P.pickedUp || item.mppickedup;
                Vector3 position = p.position;
                Vector3 localPosition = p.localPosition;
                bool parentedToCar = p.IsChildOf(localCar.transform);
                Vector3 carRelativePosition = localCar.transform.InverseTransformPoint(position);
                // The bridge invariant: a live mount lock owns parenting and has
                // no dynamic body. Any mismatch is a bug worth observing even
                // though it does not change behavior here.
                bool storageDesync = item.P.physlock != null && (p.parent != item.P.physlock.transform || rb != null);
                string reason = null;
                if (!track.HasSample) reason = storageDesync ? "storage-desync" : "observed";
                else
                {
                    bool parentJump = parentID == track.ParentID && parentID != 0 && (localPosition - track.LocalPosition).sqrMagnitude > .75f * .75f;
                    // Use the same local car frame for loose and parented cargo so
                    // normal forward travel is removed from the jump comparison.
                    bool carrierJump = carID == track.CarID &&
                        (carRelativePosition - track.CarRelativePosition).sqrMagnitude > 5f * 5f;
                    bool changed = parentID != track.ParentID || lockID != track.LockID || carID != track.CarID ||
                        (rb != null) != track.Rigidbody || kinematic != track.Kinematic || stored != track.Stored ||
                        picked != track.Picked || isWheel != track.Wheel || tire != track.Tire;
                    if (storageDesync) reason = "storage-desync";
                    else if (parentJump || carrierJump) reason = "jump";
                    else if (changed) reason = "state-change";
                }
                track.ParentID = parentID; track.LockID = lockID; track.CarID = carID;
                track.Position = position; track.LocalPosition = localPosition;
                track.CarRelativePosition = carRelativePosition; track.ParentedToCar = parentedToCar;
                track.Rigidbody = rb != null; track.Kinematic = kinematic; track.Stored = stored;
                track.Picked = picked; track.Wheel = isWheel; track.Tire = tire; track.HasSample = true;
                if (reason != null) Log(track, reason, localCar.tosave);
            }

            if (now >= nextReport)
            {
                nextReport = now + ReportInterval;
                foreach (var track in tracks.Values)
                    if (track.Item != null && now - track.LastLogAt >= ReportInterval - SampleInterval)
                        Log(track, "periodic", localCar.tosave);
            }
        }

        // Record counts for global/local/held/velocity item messages without
        // logging every packet. Per-item summaries are emitted in the 3 s sample.
        public static void NotePacket(bool incoming, byte type, uint id, ulong peer)
        {
            if (!enabled || type < 16 || type > 19) return;
            Track track;
            if (tracks.TryGetValue(id, out track))
            {
                int index = (incoming ? 4 : 0) + type - 16;
                if (track.Packets[index] < Int32.MaxValue) track.Packets[index]++;
                track.LastIncoming = incoming; track.LastType = type; track.LastPeer = peer;
                return;
            }
            if (pendingPackets.Count >= MaxPendingPacketIDs && !pendingPackets.ContainsKey(id)) return;
            int[] counts;
            if (!pendingPackets.TryGetValue(id, out counts)) pendingPackets[id] = counts = new int[8];
            int pendingIndex = (incoming ? 4 : 0) + type - 16;
            if (counts[pendingIndex] < Int32.MaxValue) counts[pendingIndex]++;
        }
    }
}

