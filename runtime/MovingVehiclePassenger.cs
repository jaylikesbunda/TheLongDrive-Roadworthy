using System;
using UnityEngine;

namespace TldMp
{
    // Changes only the velocity frame used by the local player's native Move.
    // It does not move the player or transfer ownership of the vehicle.
    public static class MovingVehiclePassenger
    {
        private static fpscontroller passenger;
        private static Rigidbody support, ownerBody, playerBody, bracketBody;
        private static tosaveitemscript vehicle;
        private static Vector3d previousGlobal;
        private static Quaternion previousRotation;
        private static Vector3 pointVelocity, bracketVelocity;
        private static bool previousValid, sampleValid, bracket;
        private static bool interpolationOwned;
        private static RigidbodyInterpolation previousInterpolation;
        private static float sampleTime;
        private static readonly bool Diagnostics = Environment.GetEnvironmentVariable("TLD_PASSENGER_DIAGNOSTICS") == "1";
        private static int samples, rejected, moves;
        private static float maxSpeed, nextReport;
        private static readonly RaycastHit[] supportHits = new RaycastHit[24];
        private static int noPlayer, noFloor, noVehicle, noAuthority, noSample, noMove;
        private static Vector3 supportPoint;
        private static float eventAt;private static int eventCount;
        private static void Note(string message)
        {
            if(!Diagnostics)return;
            if(Time.realtimeSinceStartup-eventAt>5f){eventAt=Time.realtimeSinceStartup;eventCount=0;}
            if(++eventCount<=4)Debug.Log(message);
        }

        public static void FixedTick(fpscontroller player, bool enabled)
        {
            if(Diagnostics&&enabled&&Time.realtimeSinceStartup>=nextReport)
            {
                Debug.Log("[TldMp.Passenger] active="+(passenger!=null)+" samples="+samples+" rejected="+rejected+" moves="+moves+" maxSupportPointSpeed="+maxSpeed.ToString("F2")+
                    " gates{player="+noPlayer+" floor="+noFloor+" vehicle="+noVehicle+" role="+noAuthority+" sample="+noSample+" move="+noMove+"}");
                samples=rejected=moves=noPlayer=noFloor=noVehicle=noAuthority=noSample=noMove=0;maxSpeed=0f;nextReport=Time.realtimeSinceStartup+10f;
            }
            if (!enabled || player == null || !player.loadedmove || player.RB == null || player.RB.isKinematic || player.Bsitting || player.died || player.ladder || player.griping || player.pushgripping ||
                player.grounded == null || player.grounded.selfCol == null || (player.input != null && player.input.jumpDown))
            { if(Diagnostics)noPlayer++; Reset(); return; }
            // Do not acquire a new support while the native ground sensor says
            // airborne. An already confirmed support may bridge a brief sensor gap.
            if (!player.grounded.sensed && passenger != player) { if(Diagnostics)noPlayer++; Reset(); return; }
            Rigidbody body; Vector3 contact;
            if (!FindSupport(player, out body, out contact)) { if(Diagnostics)noFloor++; Reset(); return; }
            tosaveitemscript item = body.GetComponentInParent<tosaveitemscript>();
            if (item == null)
            {
                ConfigurableJoint joint = body.GetComponent<ConfigurableJoint>();
                if (joint != null && joint.connectedBody != null) item = joint.connectedBody.GetComponentInParent<tosaveitemscript>();
            }
            if (item == null || item.car == null || item.RB == null) { if(Diagnostics)noVehicle++; Reset(); return; }
            if (item.ThisOneInChargeCar() || !BusPairReplica.Owns(item) || !BusPairReplica.OwnsBody(body)) { if(Diagnostics)noAuthority++; Reset(); return; }
            supportPoint = contact;
            if (passenger != player || support != body || vehicle != item || playerBody != player.RB || ownerBody != item.RB)
            {
                Reset(); passenger = player; support = body; vehicle = item; ownerBody = item.RB; playerBody = player.RB;
                Note("[TldMp.Passenger] paired support acquired=" + body.name + " owner=" + item.name);
            }
            Vector3d current = mainscript.GlobalFromUnityPos(body.position);
            Quaternion rotation = body.rotation;
            sampleValid = false;
            if (previousValid)
            {
                double dx = current.x - previousGlobal.x, dy = current.y - previousGlobal.y, dz = current.z - previousGlobal.z;
                float dt = Time.fixedDeltaTime;
                float angle = Quaternion.Angle(previousRotation, rotation);
                Vector3 local = Quaternion.Inverse(rotation) * (supportPoint - body.position);
                if (MovingVehiclePassengerPolicy.ValidSample(dx, dy, dz, dt, angle, local.sqrMagnitude))
                {
                    Vector3 movement = new Vector3((float)dx, (float)dy, (float)dz) + rotation * local - previousRotation * local;
                    Vector3 velocity = movement / dt;
                    if (MovingVehiclePassengerPolicy.ValidVelocity(velocity.sqrMagnitude))
                    {
                        pointVelocity = velocity; sampleValid = true; sampleTime = Time.time;
                        if (Diagnostics) { samples++; maxSpeed = Mathf.Max(maxSpeed, velocity.magnitude); }
                    }
                }
                if (!sampleValid && Diagnostics) { rejected++; noSample++; }
            }
            previousGlobal = current; previousRotation = rotation; previousValid = true;
            if(sampleValid&&support.interpolation==RigidbodyInterpolation.Interpolate)
            {
                if(!interpolationOwned){previousInterpolation=playerBody.interpolation;interpolationOwned=true;}
                playerBody.interpolation=RigidbodyInterpolation.Interpolate;
            }
            else RestoreInterpolation();
        }

        private static bool FindSupport(fpscontroller player, out Rigidbody body, out Vector3 point)
        {
            body = null; point = Vector3.zero;
            Collider sensor = player.grounded.selfCol;
            if (sensor == null || !sensor.enabled) return false;
            Bounds bounds = sensor.bounds;
            // Sensor bounds are exposed through render transforms when the
            // player interpolates. Cast from the rigidbody's physical pose.
            Vector3 physicalCenter = player.RB.position + player.RB.rotation *
                (Quaternion.Inverse(player.RB.transform.rotation) * (bounds.center - player.RB.transform.position));
            float radius = Mathf.Min(.08f, Mathf.Max(.02f, Mathf.Min(bounds.extents.x, bounds.extents.z) * .65f));
            Vector3 origin = physicalCenter + Vector3.up * (bounds.extents.y + .15f);
            float castDistance = bounds.extents.y * 2f + .30f;
            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, supportHits, castDistance, ~0, QueryTriggerInteraction.Ignore);
            if (count >= supportHits.Length) return false;
            float nearest = float.PositiveInfinity;
            Collider nearestSurface = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = supportHits[i]; Collider surface = hit.collider;
                if (surface == null || !surface.enabled || surface.isTrigger || surface.attachedRigidbody == player.RB ||
                    Vector3.Dot(hit.normal, Vector3.up) < .5f || hit.distance >= nearest) continue;
                float gap = physicalCenter.y - bounds.extents.y - hit.point.y;
                if (gap > .12f || gap < -.30f) continue;
                nearest = hit.distance; nearestSurface = surface; point = hit.point;
            }
            if (nearestSurface == null || nearestSurface.attachedRigidbody == null) return false;
            body = nearestSurface.attachedRigidbody;
            return true;
        }

        public static void BeginMove(fpscontroller player)
        {
            if (bracket || !sampleValid || player == null || player != passenger || player.RB != playerBody || playerBody == null || playerBody.isKinematic ||
                player.Bsitting || player.died || player.ladder || player.griping || player.pushgripping || player.grounded == null || player.grounded.selfCol == null ||
                (player.input != null && player.input.jumpDown) ||
                vehicle == null || vehicle.ThisOneInChargeCar() || !BusPairReplica.Owns(vehicle) || !BusPairReplica.OwnsBody(support) ||
                Time.time - sampleTime > Mathf.Max(.1f, 4f * Time.fixedDeltaTime)) { if(Diagnostics)noMove++; return; }
            Rigidbody hitBody; Vector3 contact;
            if (!FindSupport(player, out hitBody, out contact) || hitBody != support) { if(Diagnostics)noMove++; return; }
            Vector3 relative = playerBody.velocity - pointVelocity;
            if (relative.y > 1f) return;
            float limit=Mathf.Max(6f,player.FmaxSpeed+1f);
            if (relative.x * relative.x + relative.z * relative.z > limit*limit) return;
            bracketBody = playerBody; bracketVelocity = pointVelocity; bracket = true;
            bracketBody.velocity -= bracketVelocity;
            if (Diagnostics) moves++;
        }

        // Used only for velocity getters inside fpscontroller.Move. All non-vehicle
        // reads retain native behavior, including the native null dereference.
        public static Vector3 Velocity(Rigidbody body)
        {
            if (bracket && body != null && body != bracketBody && (body == support || body == ownerBody)) return Vector3.zero;
            return body.velocity;
        }

        public static void EndMove(fpscontroller player)
        {
            if (!bracket || player != passenger) return;
            if (bracketBody != null)
            {
                bracketBody.velocity += bracketVelocity;
            }
            bracket = false; bracketBody = null; bracketVelocity = Vector3.zero;
        }

        public static void Reset()
        {
            // A bracket cannot span a fixed step during normal Unity execution.
            if (bracket && bracketBody != null) bracketBody.velocity += bracketVelocity;
            RestoreInterpolation();
            if (Diagnostics && passenger != null) Note("[TldMp.Passenger] support released=" +
                (support == null ? "none" : support.name) + " paired=" + (vehicle != null && BusPairReplica.Owns(vehicle)));
            bracket = false; bracketBody = null; passenger = null; support = null; ownerBody = null;
            playerBody = null; vehicle = null; previousValid = sampleValid = false;
        }
        private static void RestoreInterpolation()
        {
            if(interpolationOwned&&playerBody!=null&&playerBody.interpolation==RigidbodyInterpolation.Interpolate)playerBody.interpolation=previousInterpolation;
            interpolationOwned=false;
        }
    }
}
