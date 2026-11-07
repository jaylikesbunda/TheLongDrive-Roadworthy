using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Four narrow native fixes. Each policy is Unity-free so it can be tested offline.
    //   ParkedCarSleep:  carscript.Update skips the velocity write while the body sleeps
    //                    or when the value is unchanged, so parked cars can sleep.
    //   IdlePourPause:   tankcapscript.Update pauses an idle pour particle system with no
    //                    particles and resumes it when emission is requested again.
    //   WornDestroyed:   pickupable.OnDestroy drops a destroyed worn item's renderers from
    //                    the camera hide list; makeinvisible.PreRender/PostRender drop null
    //                    renderers before their loops.
    //   SliceGuard:      randomSlice.Slicc calls the slicer through SafeSlice, which logs one
    //                    failure and lets the native caller continue and destroy the object.
    //   RadiatorRepair:  partconditionscript.CanRepair lets radiators use the metal repair
    //                    table (wire brush and the other metal tools) while the option is on.
    //   TrailerHitch:    attachgripscript.FStart links the beta trailer grip to its trailer, and
    //                    attachablescript reads the grip's pickupable where the trailer has none.
    //                    TrailerGripWatch logs each wired grip's attach/detach state changes.
    internal static class ParkedCarSleepPolicy
    {
        // True when the velocity write must reach the body.
        internal static bool ShouldWriteVelocity(bool sleeping, float cx, float cy, float cz, float nx, float ny, float nz)
        {
            if (sleeping) return false;
            if (cx == nx && cy == ny && cz == nz) return false;
            return true;
        }
    }

    internal static class IdlePourPausePolicy
    {
        internal const int None = 0, Pause = 1, Resume = 2;

        internal static int Decide(bool emissionRequested, bool paused, int particleCount)
        {
            if (emissionRequested) return paused ? Resume : None;
            return !paused && particleCount == 0 ? Pause : None;
        }
    }

    internal static class WornItemGuard
    {
        // A hide-list entry is removed when its renderer is gone or belongs to the destroyed item.
        internal static bool ShouldDrop(bool rendererNull, bool belongsToItem)
        {
            return rendererNull || belongsToItem;
        }

        internal static void Destroyed(pickupable item)
        {
            var player = mainscript.s == null ? null : mainscript.s.player;
            var hide = player == null ? null : player.wearedRenderer;
            if (item == null || hide == null || hide.R == null || hide.R.Count == 0) return;
            // Items are destroyed constantly while the world streams; walk the small
            // hide list instead of collecting the item's renderers.
            Transform root = item.transform;
            for (int i = hide.R.Count - 1; i >= 0; i--)
            {
                Renderer r = hide.R[i];
                if (ShouldDrop(r == null, r != null && r.transform.IsChildOf(root))) hide.R.RemoveAt(i);
            }
        }

        internal static void PruneHide(makeinvisible hide)
        {
            if (hide == null || hide.R == null) return;
            hide.R.RemoveAll(r => ShouldDrop(r == null, false));
        }
    }

    internal static class SliceGuard
    {
        static bool logged;

        // True exactly once, for the first failure to report.
        internal static bool FirstFailure(ref bool alreadyLogged)
        {
            if (alreadyLogged) return false;
            alreadyLogged = true;
            return true;
        }

        internal static void Failed(Exception error)
        {
            if (!FirstFailure(ref logged)) return;
            UnityEngine.Debug.LogWarning("[TLD MP slice] slicer failed once; the native caller still destroys the object: " + error.GetType().Name + ": " + error.Message);
        }
    }

    internal static class RadiatorRepairPolicy
    {
        // itemdatabase.repairProperty.metal and the wire brush's cleaning type.
        internal const int Metal = 4;
        internal const int WireBrush = 1;

        // A radiator borrows the metal table only while the option is on and only
        // when its own table does not already accept the wire brush.
        internal static bool UseMetal(bool optionOn, bool isRadiator, bool nativeAcceptsBrush)
        {
            return optionOn && isRadiator && !nativeAcceptsBrush;
        }

        internal static bool AcceptsTool(bool[][] table, int tool)
        {
            if (table == null) return false;
            for (int s = 0; s < table.Length; s++)
                if (table[s] != null && tool < table[s].Length && table[s][tool]) return true;
            return false;
        }
    }

    // Runs before every native CanRepair check. Radiators are recognised once per
    // part (coolantTankscript.condition points at them) and cached, so the hot
    // path while a tool is aimed is one dictionary lookup.
    internal static class RadiatorRepair
    {
        sealed class Seen { public itemdatabase.repairProperty Original; public bool Radiator; }
        static readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();

        internal static void BeforeCanRepair(partconditionscript condition)
        {
            if (condition == null) return;
            int id = condition.GetInstanceID();
            Seen entry;
            if (!seen.TryGetValue(id, out entry))
            {
                if (seen.Count > 4096) seen.Clear();
                entry = new Seen { Original = condition.RepairProperty, Radiator = IsRadiator(condition) };
                seen[id] = entry;
            }
            if (!entry.Radiator) return;
            PatchOptions.Load();
            bool metal = RadiatorRepairPolicy.UseMetal(PatchOptions.RadiatorRepair, true, NativeAcceptsBrush(entry.Original));
            var want = metal ? (itemdatabase.repairProperty)RadiatorRepairPolicy.Metal : entry.Original;
            if (condition.RepairProperty != want) condition.RepairProperty = want;
        }

        static bool NativeAcceptsBrush(itemdatabase.repairProperty property)
        {
            var db = itemdatabase.s;
            int index = (int)property;
            if (db == null || db.repairProperties == null || index < 0 || index >= db.repairProperties.Length || db.repairProperties[index] == null) return false;
            return RadiatorRepairPolicy.AcceptsTool(db.repairProperties[index].b, RadiatorRepairPolicy.WireBrush);
        }

        static bool IsRadiator(partconditionscript condition)
        {
            var cooler = condition.GetComponentInParent<coolantTankscript>();
            if (cooler != null && cooler.condition == condition) return true;
            cooler = condition.GetComponentInChildren<coolantTankscript>(true);
            return cooler != null && cooler.condition == condition;
        }
    }
    // Beta trailers: attachgripscript spawns a separate grip object (pickupable +
    // attachablescript, no tosaveitemscript). Since parts and attachables share one
    // script, pickup and attach go through grip.tosave and attachable.tosave.P, which
    // the grip never gets, so the grip can be pushed but not grabbed or hitched.
    // The grip is linked to its trailer's save record and the attachable code reads
    // the grip's pickupable where the trailer has none.
    internal static class TrailerHitchPolicy
    {
        internal static bool ShouldWire(bool hasGrip, bool hasTrailerSave)
        {
            return hasGrip && hasTrailerSave;
        }

        // The native P always wins; the grip stands in only for a wired trailer without one.
        internal static bool UseGrip(bool hasNativeP, bool wiredGrip)
        {
            return !hasNativeP && wiredGrip;
        }
    }

    internal static class TrailerHitch
    {
        static readonly Dictionary<int, pickupable> grips = new Dictionary<int, pickupable>();

        internal static void Wire(attachgripscript holder)
        {
            if (holder == null) return;
            GameObject grip = holder.Grip;
            tosaveitemscript trailer = holder.tosaveitem;
            if (!TrailerHitchPolicy.ShouldWire(grip != null, trailer != null)) return;
            pickupable p = grip.GetComponent<pickupable>();
            attachablescript a = grip.GetComponent<attachablescript>();
            if (p == null || a == null) return;
            if (a.tosave == null) a.tosave = trailer;
            if (p.tosave == null)
            {
                // Only a tosaveitemscript ever starts its pickupable; the grip has none.
                if (!p.started) p.FStart(trailer);
                else p.tosave = trailer;
            }
            if (grips.Count > 1024) Prune();
            grips[trailer.GetInstanceID()] = p;
            if (grip.GetComponent<TrailerGripWatch>() == null)
            {
                TrailerGripWatch watch = grip.AddComponent<TrailerGripWatch>();
                watch.Attachable = a;
                watch.Trailer = trailer;
            }
            Debug.Log("[TLD trailer] wired grip " + grip.name + " to trailer " + trailer.name + " id=" + trailer.idInSave);
        }

        // Replaces each tosaveitemscript.P read inside the attachable methods.
        internal static pickupable P(tosaveitemscript save)
        {
            if ((object)save == null) throw new NullReferenceException();
            pickupable native = save.P;
            if (grips.Count == 0) return native;
            pickupable grip;
            bool wired = grips.TryGetValue(save.GetInstanceID(), out grip) && grip != null;
            return TrailerHitchPolicy.UseGrip(native != null, wired) ? grip : native;
        }

        static void Prune()
        {
            var dead = new List<int>();
            foreach (var pair in grips) if (pair.Value == null) dead.Add(pair.Key);
            for (int i = 0; i < dead.Count; i++) grips.Remove(dead[i]);
        }
    }

    // Diagnostics only, added to each wired trailer grip. Polls the attachable every
    // frame and logs when attached, joint or attach point root changes, plus a
    // rate-limited warning when an attached grip drifts far from its attach point.
    public sealed class TrailerGripWatch : MonoBehaviour
    {
        const float FarDistance = 0.5f;
        internal attachablescript Attachable;
        internal tosaveitemscript Trailer;
        bool attached;
        bool joint;
        Transform root;
        float nextFarLog;

        void Update()
        {
            attachablescript a = Attachable;
            if (a == null || Trailer == null) return;
            bool nowAttached = a.attached;
            bool nowJoint = a.J != null;
            Transform nowRoot = a.point != null ? a.point.root : null;
            if (nowAttached != attached)
            {
                attached = nowAttached;
                Report(a, nowAttached ? "attached=true" : "attached=false");
            }
            if (nowJoint != joint)
            {
                joint = nowJoint;
                Report(a, nowJoint ? "joint=created" : "joint=destroyed");
            }
            if (nowRoot != root)
            {
                root = nowRoot;
                Report(a, "point root=" + (nowRoot != null ? nowRoot.name : "none"));
            }
            if (nowAttached && a.point != null && Time.time >= nextFarLog)
            {
                float distance = Vector3.Distance(a.transform.position, a.point.position);
                if (distance > FarDistance)
                {
                    nextFarLog = Time.time + 1f;
                    Report(a, "far from attach point while attached");
                }
            }
        }

        void Report(attachablescript a, string what)
        {
            float speed = a.RB != null ? a.RB.velocity.magnitude : 0f;
            float distance = a.point != null ? Vector3.Distance(a.transform.position, a.point.position) : -1f;
            Debug.Log("[TLD trailer] t=" + Time.time.ToString("F2") + " frame=" + Time.frameCount +
                " trailer=" + Trailer.name + " id=" + Trailer.idInSave +
                " car=" + (a.point != null ? a.point.root.name : "none") +
                " " + what + " speed=" + speed.ToString("F2") + " m/s dist=" + distance.ToString("F2") + " m");
        }
    }
}
