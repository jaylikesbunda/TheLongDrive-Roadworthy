using System;

namespace TldMp
{
    // Unity-free pose samples for remote players and cars. Positions are plain
    // doubles in global space (independent of the world recentre); rotations are
    // quaternions stored as doubles so the math can be tested offline.
    internal struct RemotePose
    {
        internal double X, Y, Z;
        internal double Qx, Qy, Qz, Qw;
        internal bool HasMotion,Impact;
        internal double Vx,Vy,Vz,Wx,Wy,Wz;
    }

    internal enum RemoteAddResult { Accepted, OutOfOrder, Reset }

    // Per-entity ring of received samples with an adaptive render delay.
    // The caller supplies the receive time (seconds) and the clock.
    internal sealed class RemotePoseBuffer
    {
        // 32 samples keep the 250 ms maximum delay covered at 60 Hz.
        internal const int Capacity = 32;
        internal const double MinDelay = 0.06;
        internal const double MaxDelay = 0.25;
        internal const double MaxExtrapolateSeconds = 0.1;
        internal const double GapResetSeconds = 1.0;
        internal const double TeleportMetres = 50.0;
        internal const double MaxPredictionMetres=2.0,MaxPredictionSpeed=150.0,MaxPredictionSpin=20.0;
        const double Alpha = 0.1;

        readonly double[] times = new double[Capacity];
        readonly RemotePose[] poses = new RemotePose[Capacity];
        int start;
        int count;
        double interval;
        double jitter;
        bool haveInterval;

        internal int Dropped, Resets;
        // Distance from the newest kept sample to the last sample offered (diagnostics).
        internal double LastJumpMetres;
        // Set after a reset (teleport or long gap). The next TakeSnap moves the
        // entity straight to the new sample instead of letting the native lerp drag it.
        internal bool SnapPending;

        internal int Count { get { return count; } }

        internal double NewestTime { get { return count == 0 ? 0.0 : times[Index(count - 1)]; } }

        internal double Delay { get { return DelayFor(interval, jitter); } }

        internal static double DelayFor(double intervalSeconds, double jitterSeconds)
        {
            double delay = intervalSeconds * 1.5 + jitterSeconds * 2.0;
            if (delay < MinDelay) return MinDelay;
            if (delay > MaxDelay) return MaxDelay;
            return delay;
        }

        internal RemoteAddResult Add(double time, RemotePose pose)
        {
            return Add(time, pose, -1);
        }

        // lateness >= 0: time is the sender's send time on the local clock and
        // lateness how late it arrived; the delay then covers arrival lateness
        // instead of the spacing of arrivals.
        internal RemoteAddResult Add(double time, RemotePose pose, double lateness)
        {
            if (count > 0)
            {
                int newestIndex = Index(count - 1);
                double newest = times[newestIndex];
                if (time < newest)
                {
                    Dropped++;
                    return RemoteAddResult.OutOfOrder;
                }
                double gap = time - newest;
                LastJumpMetres = Distance(poses[newestIndex], pose);
                if (gap > GapResetSeconds || LastJumpMetres > TeleportMetres)
                {
                    count = 0;
                    start = 0;
                    Store(time, pose);
                    SnapPending = true;
                    Resets++;
                    return RemoteAddResult.Reset;
                }
                double deviation = Math.Abs(gap - interval);
                if (!haveInterval)
                {
                    interval = gap;
                    jitter = 0.0;
                    haveInterval = true;
                }
                else
                {
                    interval += Alpha * (gap - interval);
                    jitter += Alpha * ((lateness >= 0 ? lateness : deviation) - jitter);
                }
            }
            Store(time, pose);
            return RemoteAddResult.Accepted;
        }

        // Returns the newest sample once after a reset so the caller can snap to it.
        internal bool TakeSnap(out RemotePose pose)
        {
            pose = default(RemotePose);
            if (!SnapPending || count == 0) return false;
            SnapPending = false;
            pose = poses[Index(count - 1)];
            return true;
        }

        // renderTime is on the same clock as the sample times. Between samples the
        // pose is interpolated; past the newest sample it is extrapolated for at most
        // MaxExtrapolateSeconds and then held; before the oldest it is the oldest.
        internal const int SampleOldest = 0, SampleInterpolated = 1, SampleExtrapolated = 2, SampleHeld = 3;

        internal bool Sample(double renderTime, out RemotePose pose)
        {
            int kind;
            return Sample(renderTime, out pose, out kind);
        }

        // kind: SampleOldest (single/oldest), SampleInterpolated, SampleExtrapolated
        // (past the newest, within MaxExtrapolateSeconds) or SampleHeld (beyond it).
        internal bool Sample(double renderTime, out RemotePose pose, out int kind)
        {
            pose = default(RemotePose);
            kind = SampleOldest;
            if (count == 0) return false;
            int oldest = Index(0);
            int newest = Index(count - 1);
            if (count == 1 || renderTime <= times[oldest])
            {
                pose = poses[oldest];
                return true;
            }
            kind = renderTime <= times[newest] ? SampleInterpolated
                : (renderTime - times[newest] <= MaxExtrapolateSeconds ? SampleExtrapolated : SampleHeld);
            if (renderTime <= times[newest])
            {
                for (int i = 0; i < count - 1; i++)
                {
                    int a = Index(i);
                    int b = Index(i + 1);
                    if (renderTime <= times[b])
                    {
                        double span = times[b] - times[a];
                        double t = span > 0.0 ? (renderTime - times[a]) / span : 1.0;
                        pose = BlendMotion(poses[a], poses[b], t, span);
                        return true;
                    }
                }
                pose = poses[newest];
                return true;
            }
            pose = poses[newest];
            if(pose.HasMotion)
            {
                // An impact or implausible tangent gets no prediction. Regular
                // motion is limited by both time and distance, including spin.
                if(pose.Impact || !Predictable(pose)){kind=SampleHeld;return true;}
                double speed=Math.Sqrt(pose.Vx*pose.Vx+pose.Vy*pose.Vy+pose.Vz*pose.Vz);
                double ahead=Math.Min(renderTime-times[newest],MaxExtrapolateSeconds);
                if(speed>0)ahead=Math.Min(ahead,MaxPredictionMetres/speed);
                pose.X+=pose.Vx*ahead;pose.Y+=pose.Vy*ahead;pose.Z+=pose.Vz*ahead;
                RotateAhead(ref pose,ahead);return true;
            }
            int previous = Index(count - 2);
            double velocitySpan = times[newest] - times[previous];
            if (velocitySpan > 0.0)
            {
                double ahead = Math.Min(renderTime - times[newest], MaxExtrapolateSeconds);
                double k = ahead / velocitySpan;
                pose.X = poses[newest].X + (poses[newest].X - poses[previous].X) * k;
                pose.Y = poses[newest].Y + (poses[newest].Y - poses[previous].Y) * k;
                pose.Z = poses[newest].Z + (poses[newest].Z - poses[previous].Z) * k;
            }
            return true;
        }

        static bool Predictable(RemotePose p)
        {
            return Finite(p.Vx)&&Finite(p.Vy)&&Finite(p.Vz)&&Finite(p.Wx)&&Finite(p.Wy)&&Finite(p.Wz) &&
                p.Vx*p.Vx+p.Vy*p.Vy+p.Vz*p.Vz<=MaxPredictionSpeed*MaxPredictionSpeed &&
                p.Wx*p.Wx+p.Wy*p.Wy+p.Wz*p.Wz<=MaxPredictionSpin*MaxPredictionSpin;
        }
        static bool Finite(double v){return !Double.IsNaN(v)&&!Double.IsInfinity(v);}
        internal static RemotePose BlendMotion(RemotePose a,RemotePose b,double t,double span)
        {
            RemotePose result=Blend(a,b,t);
            if(!a.HasMotion || !b.HasMotion || a.Impact || b.Impact || !Predictable(a) || !Predictable(b) || span<=0 || span>.25)return result;
            result.X=Monotone(a.X,b.X,a.Vx,b.Vx,t,span);
            result.Y=Monotone(a.Y,b.Y,a.Vy,b.Vy,t,span);
            result.Z=Monotone(a.Z,b.Z,a.Vz,b.Vz,t,span);return result;
        }
        // Slope-limited Hermite: transmitted tangents improve smooth motion,
        // while each axis stays between its two authoritative endpoints.
        static double Monotone(double a,double b,double va,double vb,double t,double span)
        {
            double delta=b-a;if(Math.Abs(delta)<1e-9)return a;
            double slope=delta/span,alpha=va/slope,beta=vb/slope;
            if(alpha<0)alpha=0;if(beta<0)beta=0;
            double magnitude=alpha*alpha+beta*beta;
            if(magnitude>9){double scale=3/Math.Sqrt(magnitude);alpha*=scale;beta*=scale;}
            double t2=t*t,t3=t2*t;
            double value=(2*t3-3*t2+1)*a+(t3-2*t2+t)*delta*alpha+(-2*t3+3*t2)*b+(t3-t2)*delta*beta;
            return Math.Max(Math.Min(a,b),Math.Min(Math.Max(a,b),value));
        }
        static void RotateAhead(ref RemotePose p,double seconds)
        {
            double speed=Math.Sqrt(p.Wx*p.Wx+p.Wy*p.Wy+p.Wz*p.Wz);if(speed<1e-9)return;
            double half=speed*seconds*.5,s=Math.Sin(half)/speed;
            double x=p.Wx*s,y=p.Wy*s,z=p.Wz*s,w=Math.Cos(half);
            double qx=w*p.Qx+x*p.Qw+y*p.Qz-z*p.Qy,qy=w*p.Qy-x*p.Qz+y*p.Qw+z*p.Qx;
            double qz=w*p.Qz+x*p.Qy-y*p.Qx+z*p.Qw,qw=w*p.Qw-x*p.Qx-y*p.Qy-z*p.Qz;
            double norm=Math.Sqrt(qx*qx+qy*qy+qz*qz+qw*qw);
            if(norm>1e-9){p.Qx=qx/norm;p.Qy=qy/norm;p.Qz=qz/norm;p.Qw=qw/norm;}
        }

        // Linear position, normalized linear quaternion blend on the shortest path.
        internal static RemotePose Blend(RemotePose a, RemotePose b, double t)
        {
            var result = new RemotePose();
            result.X = a.X + (b.X - a.X) * t;
            result.Y = a.Y + (b.Y - a.Y) * t;
            result.Z = a.Z + (b.Z - a.Z) * t;
            double dot = a.Qx * b.Qx + a.Qy * b.Qy + a.Qz * b.Qz + a.Qw * b.Qw;
            double sign = dot < 0.0 ? -1.0 : 1.0;
            double qx = a.Qx + (sign * b.Qx - a.Qx) * t;
            double qy = a.Qy + (sign * b.Qy - a.Qy) * t;
            double qz = a.Qz + (sign * b.Qz - a.Qz) * t;
            double qw = a.Qw + (sign * b.Qw - a.Qw) * t;
            double norm = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (norm < 1e-12)
            {
                result.Qx = a.Qx; result.Qy = a.Qy; result.Qz = a.Qz; result.Qw = a.Qw;
                return result;
            }
            result.Qx = qx / norm;
            result.Qy = qy / norm;
            result.Qz = qz / norm;
            result.Qw = qw / norm;
            return result;
        }

        static double Distance(RemotePose a, RemotePose b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        int Index(int logical) { return (start + logical) % Capacity; }

        void Store(double time, RemotePose pose)
        {
            if (count == Capacity)
            {
                start = (start + 1) % Capacity;
                count--;
            }
            int slot = (start + count) % Capacity;
            times[slot] = time;
            poses[slot] = pose;
            count++;
        }
    }

    // Decisions shared by the Unity glue and the offline tests.
    internal static class RemoteSmoothPolicy
    {
        internal const int MaxBuffers = 64;
        internal const double StaleSeconds = 5.0;
        internal const double CarSilenceSeconds = 2.0;

        // Player smoothing is on unless TLD_MP_SMOOTH_PLAYERS=0.
        internal static bool PlayerEnabled(string value) { return value != "0"; }

        // Car smoothing is controlled by the saved option; the environment switch
        // can force it on or off for A/B tests.
        internal static bool CarEnabled(string value, bool option)
        {
            if (value == "0") return false;
            if (value == "1") return true;
            return option;
        }

        // True means the native GotSync body runs (the car is teleported as before).
        internal static bool CarGotSyncIsNative(bool enabled, bool isCar, bool inCharge)
        {
            return !enabled || !isCar || inCharge;
        }

        // True means a tracked car is dropped and no longer driven by the smoothing driver.
        // The game hands a car to the local machine when the remote role (driving,
        // shotgun, ...) is over 1 s old, so a front passenger takes charge of the
        // car after any small hitch and simulates it with nobody at the wheel. While
        // the remote driver's poses are still arriving (within CarSilenceSeconds)
        // the remote role is kept current. roles: notCar=0, nothing=1, real roles>1.
        internal static bool KeepRemoteRole(double secondsSinceSample, int remoteRole)
        {
            return remoteRole > 1 && secondsSinceSample >= 0 && secondsSinceSample <= CarSilenceSeconds;
        }

        internal const double DriftMetres = 30.0;

        internal static bool CarShouldRelease(bool destroyed, bool inCharge, double now, double lastSample)
        {
            return destroyed || inCharge || now - lastSample > CarSilenceSeconds;
        }
    }
}
