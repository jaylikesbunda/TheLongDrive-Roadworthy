using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TldMp
{
    // One camera keyframe. In car space X/Y/Z are metres right/up/forward of the
    // car and Yaw/Pitch/Roll are degrees relative to its heading; in world space
    // they are relative to the session's world anchor. Hold is the time in
    // seconds from this key to the next one (NaN = the shot's default).
    internal struct CamKey
    {
        public float X, Y, Z, Yaw, Pitch, Roll, Fov, Hold;
    }

    internal enum ShotSpace { World = 0, Car = 1 }

    // A repeatable camera move: keyframes plus how to play and record them.
    internal sealed class CamShot
    {
        public ShotSpace Space = ShotSpace.Car;
        // Aim at the car (plus LookOffset, car space) instead of the keys' angles.
        public bool LookAtCar;
        public float LookX, LookY = 0.8f, LookZ;
        // Car space follows only the car's heading (horizon stays level).
        public bool Level = true;
        public float Segment = 4f;
        // Fraction of the shot spent speeding up and slowing down (0 = linear).
        public float Ease = 0.25f;
        public float TimeScale = 1f;
        public int Fps = 60;
        public int SuperSize = 1;
        public bool Record;
        public readonly List<CamKey> Keys = new List<CamKey>();
    }

    // Pure decisions for PromoCamera (no Unity objects, unit-tested).
    internal static class PromoCameraPolicy
    {
        public const float MinSpeed = 0.5f, MaxSpeed = 200f, DefaultSpeed = 8f;
        public const float MinFov = 10f, MaxFov = 120f;
        public const float MinDistance = 1.5f, MaxDistance = 60f;
        public const float MinSegment = 0.5f, MaxSegment = 60f;
        public const int MinFps = 24, MaxFps = 120;
        static readonly float[] SlowSteps = { 1f, 0.5f, 0.25f, 0.1f };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Frame-rate independent exponential smoothing toward a target.
        public static float Damp(float current, float target, float sharpness, float dt)
        {
            if (float.IsNaN(current) || float.IsInfinity(current)) return target;
            if (!(dt > 0f) || !(sharpness > 0f)) return current;
            float k = 1f - (float)Math.Exp(-sharpness * dt);
            return current + (target - current) * k;
        }

        // Same as Damp but takes the short way round for angles in degrees.
        public static float DampAngle(float current, float target, float sharpness, float dt)
        {
            if (float.IsNaN(current) || float.IsInfinity(current)) return target;
            return Damp(current, current + DeltaAngle(current, target), sharpness, dt);
        }

        // Signed smallest difference b - a, in (-180, 180].
        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }

        // Wheel steps change speed by 25 % each, clamped.
        public static float StepSpeed(float speed, float steps)
        {
            if (float.IsNaN(speed) || !(speed > 0f)) speed = DefaultSpeed;
            if (float.IsNaN(steps)) steps = 0f;
            return Clamp(speed * (float)Math.Pow(1.25, steps), MinSpeed, MaxSpeed);
        }

        public static float ClampFov(float fov) { return float.IsNaN(fov) ? 60f : Clamp(fov, MinFov, MaxFov); }
        public static float ClampPitch(float pitch) { return float.IsNaN(pitch) ? 0f : Clamp(pitch, -89f, 89f); }
        public static float ClampDistance(float d) { return float.IsNaN(d) ? 6f : Clamp(d, MinDistance, MaxDistance); }

        // 1 -> 0.5 -> 0.25 -> 0.1 -> 1. Anything else starts again at 0.5.
        public static float NextTimeScale(float current)
        {
            for (int i = 0; i < SlowSteps.Length; i++)
                if (Math.Abs(SlowSteps[i] - current) < 0.001f) return SlowSteps[(i + 1) % SlowSteps.Length];
            return SlowSteps[1];
        }

        public static float ClampTimeScale(float scale) { return float.IsNaN(scale) ? 1f : Clamp(scale, 0.05f, 1f); }

        // Camera time step that ignores slow motion but follows a fixed capture
        // rate, so camera moves look the same live and in a recorded clip.
        public static float CameraDelta(float deltaTime, float timeScale)
        {
            if (!(deltaTime > 0f)) return 0f;
            float dt = timeScale > 0.001f ? deltaTime / timeScale : deltaTime;
            return dt > 0.25f ? 0.25f : dt;
        }

        // Offset of an orbit camera from its target (x right, y up, z forward).
        public static void OrbitOffset(float yaw, float pitch, float distance, out float x, out float y, out float z)
        {
            double yr = yaw * Math.PI / 180.0, pr = ClampPitch(pitch) * Math.PI / 180.0;
            double flat = Math.Cos(pr) * distance;
            x = (float)(-Math.Sin(yr) * flat);
            y = (float)(Math.Sin(pr) * distance);
            z = (float)(-Math.Cos(yr) * flat);
        }

        public static float SegmentOf(CamShot shot, int index)
        {
            float hold = shot.Keys[index].Hold;
            float s = float.IsNaN(hold) || !(hold > 0f) ? shot.Segment : hold;
            return Clamp(s, MinSegment, MaxSegment);
        }

        // Seconds from the first key to the last.
        public static float Duration(CamShot shot)
        {
            if (shot == null || shot.Keys.Count < 2) return 0f;
            float total = 0f;
            for (int i = 0; i < shot.Keys.Count - 1; i++) total += SegmentOf(shot, i);
            return total;
        }

        // Speed up over the first `ease` of the shot, glide, slow down over the
        // last `ease`. Continuous velocity, no stop at the inner keys.
        public static float EaseTime(float u, float ease)
        {
            if (!(u > 0f)) return 0f;
            if (u >= 1f) return 1f;
            if (float.IsNaN(ease) || !(ease > 0f)) return u;
            float r = ease > 0.5f ? 0.5f : ease;
            float v = 1f / (1f - r);
            if (u < r) return 0.5f * v * u * u / r;
            if (u > 1f - r) { float w = 1f - u; return 1f - 0.5f * v * w * w / r; }
            return 0.5f * v * r + v * (u - r);
        }

        // Smooth (Catmull-Rom) sample of the shot `t` seconds in.
        public static CamKey Sample(CamShot shot, float t)
        {
            var keys = shot.Keys;
            if (keys.Count == 0) return new CamKey { Fov = 60f, Hold = float.NaN };
            if (keys.Count == 1) return keys[0];
            float duration = Duration(shot);
            float u = EaseTime(duration > 0f ? t / duration : 1f, shot.Ease) * duration;
            int seg = 0;
            while (seg < keys.Count - 2 && u > SegmentOf(shot, seg)) { u -= SegmentOf(shot, seg); seg++; }
            float s = Clamp(u / SegmentOf(shot, seg), 0f, 1f);
            CamKey p1 = keys[seg], p2 = keys[seg + 1];
            CamKey p0 = seg > 0 ? keys[seg - 1] : p1;
            CamKey p3 = seg + 2 < keys.Count ? keys[seg + 2] : p2;
            // Unwrap angles so each step goes the short way round.
            float y1 = p1.Yaw, y0 = y1 - DeltaAngle(p0.Yaw, p1.Yaw), y2 = y1 + DeltaAngle(p1.Yaw, p2.Yaw), y3 = y2 + DeltaAngle(p2.Yaw, p3.Yaw);
            var k = new CamKey();
            k.X = CatmullRom(p0.X, p1.X, p2.X, p3.X, s);
            k.Y = CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, s);
            k.Z = CatmullRom(p0.Z, p1.Z, p2.Z, p3.Z, s);
            k.Yaw = CatmullRom(y0, y1, y2, y3, s);
            k.Pitch = ClampPitch(CatmullRom(p0.Pitch, p1.Pitch, p2.Pitch, p3.Pitch, s));
            float r1 = p1.Roll, r0 = r1 - DeltaAngle(p0.Roll, p1.Roll), r2 = r1 + DeltaAngle(r1, p2.Roll), r3 = r2 + DeltaAngle(p2.Roll, p3.Roll);
            k.Roll = CatmullRom(r0, r1, r2, r3, s);
            k.Fov = ClampFov(CatmullRom(p0.Fov, p1.Fov, p2.Fov, p3.Fov, s));
            k.Hold = float.NaN;
            return k;
        }

        public static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        // ---- Shot files -------------------------------------------------

        // Shot names are file names: letters, digits, '-' and '_' only.
        public static bool ValidName(string name)
        {
            if (String.IsNullOrEmpty(name) || name.Length > 48) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_')) return false;
            }
            return true;
        }

        public static CamShot Parse(string text, out string error)
        {
            error = null;
            var shot = new CamShot();
            if (String.IsNullOrEmpty(text)) return shot;
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0) continue;
                string lower = line.ToLowerInvariant();
                if (lower.StartsWith("key ") || lower.StartsWith("key\t"))
                {
                    string[] f = Split(line.Substring(4));
                    float[] v = new float[8];
                    bool ok = f.Length >= 6 && f.Length <= 8;
                    for (int i = 0; ok && i < f.Length; i++) ok = float.TryParse(f[i], NumberStyles.Float, Inv, out v[i]) && !float.IsInfinity(v[i]) && !float.IsNaN(v[i]);
                    if (!ok) { if (error == null) error = "line " + (n + 1) + ": key needs x y z yaw pitch roll [fov] [seconds]"; continue; }
                    shot.Keys.Add(new CamKey
                    {
                        X = v[0], Y = v[1], Z = v[2], Yaw = v[3], Pitch = ClampPitch(v[4]), Roll = v[5],
                        Fov = f.Length >= 7 ? ClampFov(v[6]) : 60f,
                        Hold = f.Length >= 8 && v[7] > 0f ? Clamp(v[7], MinSegment, MaxSegment) : float.NaN
                    });
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) { if (error == null) error = "line " + (n + 1) + ": expected key or name=value"; continue; }
                string name = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim().ToLowerInvariant();
                float num; bool on;
                switch (name)
                {
                    case "space":
                        if (value == "car") shot.Space = ShotSpace.Car;
                        else if (value == "world") shot.Space = ShotSpace.World;
                        else if (error == null) error = "line " + (n + 1) + ": space is car or world";
                        break;
                    case "look":
                        shot.LookAtCar = value == "car";
                        break;
                    case "lookoffset":
                    {
                        string[] f = Split(value);
                        float a, b, c;
                        if (f.Length == 3 && float.TryParse(f[0], NumberStyles.Float, Inv, out a) && float.TryParse(f[1], NumberStyles.Float, Inv, out b) && float.TryParse(f[2], NumberStyles.Float, Inv, out c))
                        { shot.LookX = a; shot.LookY = b; shot.LookZ = c; }
                        else if (error == null) error = "line " + (n + 1) + ": lookoffset needs x y z";
                        break;
                    }
                    case "level":
                        if (PatchOptions.TryOnOff(value, out on)) shot.Level = on;
                        break;
                    case "segment":
                        if (TryNum(value, out num)) shot.Segment = Clamp(num, MinSegment, MaxSegment);
                        break;
                    case "ease":
                        if (value == "linear") shot.Ease = 0f;
                        else if (value == "smooth") shot.Ease = 0.25f;
                        else if (TryNum(value, out num)) shot.Ease = Clamp(num, 0f, 0.5f);
                        break;
                    case "timescale":
                        if (TryNum(value, out num)) shot.TimeScale = ClampTimeScale(num);
                        break;
                    case "fps":
                        if (TryNum(value, out num)) shot.Fps = (int)Clamp((float)Math.Round(num), MinFps, MaxFps);
                        break;
                    case "supersize":
                        if (TryNum(value, out num)) shot.SuperSize = (int)Clamp((float)Math.Round(num), 1f, 4f);
                        break;
                    case "record":
                        if (PatchOptions.TryOnOff(value, out on)) shot.Record = on;
                        break;
                    default:
                        if (error == null) error = "line " + (n + 1) + ": unknown setting " + name;
                        break;
                }
            }
            return shot;
        }

        public static string Format(CamShot shot)
        {
            var b = new StringBuilder();
            b.Append("# Roadworthy camera shot. key = x y z yaw pitch roll fov [seconds to next key]\n");
            b.Append("# car space: metres right/up/forward of the car, angles relative to its heading.\n");
            b.Append("space=").Append(shot.Space == ShotSpace.Car ? "car" : "world").Append('\n');
            b.Append("look=").Append(shot.LookAtCar ? "car" : "keys").Append('\n');
            b.Append("lookoffset=").Append(N(shot.LookX)).Append(' ').Append(N(shot.LookY)).Append(' ').Append(N(shot.LookZ)).Append('\n');
            b.Append("level=").Append(shot.Level ? "on" : "off").Append('\n');
            b.Append("segment=").Append(N(shot.Segment)).Append('\n');
            b.Append("ease=").Append(N(shot.Ease)).Append('\n');
            b.Append("timescale=").Append(N(shot.TimeScale)).Append('\n');
            b.Append("fps=").Append(shot.Fps.ToString(Inv)).Append('\n');
            b.Append("supersize=").Append(shot.SuperSize.ToString(Inv)).Append('\n');
            b.Append("record=").Append(shot.Record ? "on" : "off").Append('\n');
            for (int i = 0; i < shot.Keys.Count; i++)
            {
                CamKey k = shot.Keys[i];
                b.Append("key ").Append(N(k.X)).Append(' ').Append(N(k.Y)).Append(' ').Append(N(k.Z)).Append(' ')
                 .Append(N(k.Yaw)).Append(' ').Append(N(k.Pitch)).Append(' ').Append(N(k.Roll)).Append(' ').Append(N(k.Fov));
                if (!float.IsNaN(k.Hold) && k.Hold > 0f) b.Append(' ').Append(N(k.Hold));
                b.Append('\n');
            }
            return b.ToString();
        }

        // ---- Remote commands ----------------------------------------------

        static readonly string[] Bare = { "free", "orbit", "off", "rec", "stop", "snap", "clear", "status", "menu", "sit", "quit" };
        static readonly string[] Named = { "play", "record", "save", "load" };
        static readonly string[] Numeric = { "slowmo", "fov" };

        // "verb [argument...]"; false for an unknown verb or a bad argument.
        // `arg` is the rest of the line (single-spaced) or null.
        public static bool ParseCommand(string line, out string verb, out string arg)
        {
            verb = null; arg = null;
            if (line == null) return false;
            string[] f = Split(line.Trim());
            if (f.Length == 0) return false;
            string v = f[0].ToLowerInvariant();
            string a = f.Length > 1 ? String.Join(" ", f, 1, f.Length - 1) : null;
            float num;
            bool ok;
            if (Array.IndexOf(Bare, v) >= 0) ok = f.Length == 1;
            // play/record take an optional shot name (none = the keys being edited); save/load need one.
            else if (Array.IndexOf(Named, v) >= 0) ok = f.Length == 1 ? (v == "play" || v == "record") : f.Length == 2 && ValidName(a);
            else if (Array.IndexOf(Numeric, v) >= 0) ok = f.Length == 2 && TryNum(a, out num);
            else if (v == "hud") ok = f.Length == 2 && (a == "on" || a == "off" || a == "text");
            else if (v == "newworld") { int seed; ok = f.Length == 2 && int.TryParse(a, NumberStyles.Integer, Inv, out seed); }
            else if (v == "time") ok = f.Length == 2 && (a == "run" || TryNum(a, out num) && num >= 0f && num <= 1f);
            else if (v == "camera") { float[] pose; ok = TryCameraPose(a, out pose); }
            else if (v == "teleport") { double x, z; ok = TryTeleport(a, out x, out z); }
            else if (v == "biomescan") { int r, s; ok = TryScan(a, out r, out s); }
            else ok = false;
            if (!ok) return false;
            verb = v; arg = a;
            return true;
        }

        // "x y z yaw pitch [roll] [fov]" in the game's global coordinates.
        public static bool TryCameraPose(string text, out float[] pose)
        {
            pose = null;
            if (text == null) return false;
            string[] f = Split(text);
            if (f.Length < 5 || f.Length > 7) return false;
            var v = new float[7];
            v[5] = 0f; v[6] = float.NaN;
            for (int i = 0; i < f.Length; i++) if (!TryNum(f[i], out v[i])) return false;
            v[4] = ClampPitch(v[4]);
            if (!float.IsNaN(v[6])) v[6] = ClampFov(v[6]);
            pose = v;
            return true;
        }

        // "x z" in the game's global coordinates (the player is dropped onto the ground there).
        public static bool TryTeleport(string text, out double x, out double z)
        {
            x = z = 0;
            if (text == null) return false;
            string[] f = Split(text);
            if (f.Length != 2) return false;
            if (!double.TryParse(f[0], NumberStyles.Float, Inv, out x) || !double.TryParse(f[1], NumberStyles.Float, Inv, out z)) return false;
            return !double.IsNaN(x) && !double.IsInfinity(x) && !double.IsNaN(z) && !double.IsInfinity(z) && Math.Abs(x) < 1e7 && Math.Abs(z) < 1e7;
        }

        public const int MaxScanCells = 160000;

        // "radius step" in metres for biomescan; at most MaxScanCells samples.
        public static bool TryScan(string text, out int radius, out int step)
        {
            radius = step = 0;
            if (text == null) return false;
            string[] f = Split(text);
            if (f.Length != 2 || !int.TryParse(f[0], NumberStyles.Integer, Inv, out radius) || !int.TryParse(f[1], NumberStyles.Integer, Inv, out step)) return false;
            if (radius < 100 || radius > 50000 || step < 10 || step > 5000) return false;
            long side = 2L * (radius / step) + 1;
            return side * side <= MaxScanCells;
        }

        // Biome map letter: G = native grass (the exact colour the base game draws
        // blades on), S = snow, D = desert, g = green but not exact (no blades in the
        // base game), '.' = anything else. Same thresholds as terrainGenSettings.GetBiomeInt.
        public static char BiomeLetter(float r, float g, float b, float a)
        {
            if (r == 0f && g == 1f && b == 0f && a == 1f) return 'G';
            if (b > 0.33f) return 'S';
            if (r > 0.33f) return 'D';
            if (g > 0.5f) return 'g';
            return '.';
        }

        // A native-grass cell with desert next to it (4-neighbourhood): where the base
        // game's hard grass edge and Roadworthy's gradual thinning differ the most.
        public static bool GrassBorder(char[,] map, int x, int z)
        {
            if (map[x, z] != 'G') return false;
            int w = map.GetLength(0), h = map.GetLength(1);
            return x > 0 && map[x - 1, z] == 'D' || x < w - 1 && map[x + 1, z] == 'D' || z > 0 && map[x, z - 1] == 'D' || z < h - 1 && map[x, z + 1] == 'D';
        }

        // Time of day as the game's day-cycle fraction (0 = start of morning).
        // Returns the offset to add to napszakvaltakozas.tekeres.
        public static float TimeShift(float currentT, float cycleLength, float fraction)
        {
            if (!(cycleLength > 0f) || float.IsNaN(currentT) || float.IsNaN(fraction)) return 0f;
            float f = fraction < 0f ? 0f : fraction > 1f ? 1f : fraction;
            return f * cycleLength - currentT;
        }

        public static string FrameName(int index) { return "frame_" + index.ToString("D6", Inv) + ".jpg"; }

        public static string ClipFolder(DateTime now, string shot)
        {
            return now.ToString("yyyy-MM-dd_HHmmss", Inv) + (ValidName(shot) ? "_" + shot : "");
        }

        public static string FfmpegArguments(int fps)
        {
            int f = (int)Clamp(fps, MinFps, MaxFps);
            return "-y -loglevel error -framerate " + f.ToString(Inv) + " -i frame_%06d.jpg -c:v libx264 -preset slow -crf 16 -pix_fmt yuv420p -movflags +faststart clip.mp4";
        }

        // Writes waiting on the disk thread before capture waits for them.
        public static bool MustWaitForWrites(int pending) { return pending >= 90; }

        static bool TryNum(string s, out float v)
        {
            v = 0f;
            return s != null && float.TryParse(s, NumberStyles.Float, Inv, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
        }

        // The driver's seat of the closest car within reach wins; passenger seats only when no driver seat is near.
        public static int PickSeat(float[] distances, bool[] driver, bool[] inCar, float reach)
        {
            int best = -1; float bestScore = float.PositiveInfinity;
            for (int i = 0; i < distances.Length; i++)
            {
                if (!inCar[i] || float.IsNaN(distances[i]) || distances[i] > reach) continue;
                float score = distances[i] + (driver[i] ? 0f : 1000f);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        static string[] Split(string s) { return s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); }
        static string N(float v) { return v.ToString("0.###", Inv); }
        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}
