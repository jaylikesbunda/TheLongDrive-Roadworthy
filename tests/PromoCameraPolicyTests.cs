using System;
using TldMp;

internal static class PromoCameraPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Near(float a, float b, float eps = 1e-3f) { return Math.Abs(a - b) <= eps; }

    public static int Main()
    {
        Smoothing();
        Speeds();
        Orbit();
        Easing();
        Paths();
        ShotFiles();
        Commands();
        Options();
        Console.WriteLine("PromoCameraPolicyTests passed (" + checks + " checks)");
        return 0;
    }

    static void Smoothing()
    {
        Check(PromoCameraPolicy.Damp(0f, 10f, 5f, 0f) == 0f, "no time, no movement");
        float half = PromoCameraPolicy.Damp(0f, 10f, 5f, 0.1f);
        Check(half > 0f && half < 10f, "damping moves part way");
        float twoSteps = PromoCameraPolicy.Damp(PromoCameraPolicy.Damp(0f, 10f, 5f, 0.05f), 10f, 5f, 0.05f);
        Check(Near(half, twoSteps), "damping does not depend on the frame rate");
        Check(PromoCameraPolicy.Damp(float.NaN, 3f, 5f, 0.1f) == 3f, "a broken value snaps to the target");
        Check(Near(PromoCameraPolicy.DeltaAngle(350f, 10f), 20f) && Near(PromoCameraPolicy.DeltaAngle(10f, 350f), -20f), "angles take the short way");
        float a = PromoCameraPolicy.DampAngle(350f, 10f, 50f, 1f);
        Check(Near(a, 370f, 0.01f), "angle damping crosses 360 instead of spinning back");
        Check(Near(PromoCameraPolicy.CameraDelta(0.005f, 0.25f), 0.02f, 1e-6f), "slow motion does not slow the camera");
        Check(PromoCameraPolicy.CameraDelta(0.016f, 0f) == 0.016f && PromoCameraPolicy.CameraDelta(1f, 1f) == 0.25f && PromoCameraPolicy.CameraDelta(-1f, 1f) == 0f, "camera time is bounded");
    }

    static void Speeds()
    {
        Check(Near(PromoCameraPolicy.StepSpeed(8f, 1f), 10f), "one wheel step is 25 percent faster");
        Check(Near(PromoCameraPolicy.StepSpeed(10f, -1f), 8f), "and back");
        Check(PromoCameraPolicy.StepSpeed(150f, 10f) == PromoCameraPolicy.MaxSpeed && PromoCameraPolicy.StepSpeed(1f, -10f) == PromoCameraPolicy.MinSpeed, "speed is clamped");
        Check(PromoCameraPolicy.StepSpeed(float.NaN, 0f) == PromoCameraPolicy.DefaultSpeed, "a broken speed resets");
        Check(PromoCameraPolicy.ClampFov(1f) == PromoCameraPolicy.MinFov && PromoCameraPolicy.ClampFov(500f) == PromoCameraPolicy.MaxFov && PromoCameraPolicy.ClampFov(float.NaN) == 60f, "fov is clamped");
        Check(PromoCameraPolicy.ClampPitch(120f) == 89f && PromoCameraPolicy.ClampPitch(-120f) == -89f, "pitch never flips over");
        Check(PromoCameraPolicy.NextTimeScale(1f) == 0.5f && PromoCameraPolicy.NextTimeScale(0.5f) == 0.25f && PromoCameraPolicy.NextTimeScale(0.25f) == 0.1f && PromoCameraPolicy.NextTimeScale(0.1f) == 1f, "slow motion cycles");
        Check(PromoCameraPolicy.NextTimeScale(0.7f) == 0.5f, "an odd time scale starts the cycle");
        Check(PromoCameraPolicy.ClampTimeScale(0f) == 0.05f && PromoCameraPolicy.ClampTimeScale(3f) == 1f, "time scale never pauses or speeds up the game");
    }

    static void Orbit()
    {
        float x, y, z;
        PromoCameraPolicy.OrbitOffset(0f, 0f, 5f, out x, out y, out z);
        Check(Near(x, 0f) && Near(y, 0f) && Near(z, -5f), "yaw 0 sits behind the car");
        PromoCameraPolicy.OrbitOffset(90f, 0f, 5f, out x, out y, out z);
        Check(Near(x, -5f) && Near(z, 0f), "yaw 90 swings to the side");
        PromoCameraPolicy.OrbitOffset(0f, 30f, 10f, out x, out y, out z);
        Check(Near(y, 5f) && Near((float)Math.Sqrt(x * x + y * y + z * z), 10f), "pitch raises the camera at a fixed distance");
        Check(PromoCameraPolicy.ClampDistance(0f) == PromoCameraPolicy.MinDistance && PromoCameraPolicy.ClampDistance(1000f) == PromoCameraPolicy.MaxDistance, "orbit distance is clamped");
    }

    static void Easing()
    {
        Check(PromoCameraPolicy.EaseTime(0f, 0.25f) == 0f && PromoCameraPolicy.EaseTime(1f, 0.25f) == 1f, "ease keeps the ends");
        Check(Near(PromoCameraPolicy.EaseTime(0.5f, 0.25f), 0.5f), "ease is symmetric");
        Check(PromoCameraPolicy.EaseTime(0.4f, 0f) == 0.4f, "zero ease is linear");
        float last = 0f;
        for (int i = 1; i <= 1000; i++)
        {
            float u = i / 1000f, e = PromoCameraPolicy.EaseTime(u, 0.25f);
            Check(e >= last, "ease never goes backwards");
            Check(e - last < 0.0015f, "ease has no jumps");
            last = e;
        }
        Check(PromoCameraPolicy.EaseTime(0.05f, 0.25f) < 0.05f, "ease starts slow");
        Check(PromoCameraPolicy.EaseTime(2f, 0.25f) == 1f && PromoCameraPolicy.EaseTime(-1f, 0.25f) == 0f, "ease clamps time");
    }

    static CamShot ThreeKeys()
    {
        var s = new CamShot { Segment = 2f, Ease = 0f };
        s.Keys.Add(new CamKey { X = 0f, Y = 1f, Z = -5f, Yaw = 350f, Pitch = 5f, Roll = 0f, Fov = 60f, Hold = float.NaN });
        s.Keys.Add(new CamKey { X = 5f, Y = 2f, Z = 0f, Yaw = 10f, Pitch = 0f, Roll = 0f, Fov = 50f, Hold = 4f });
        s.Keys.Add(new CamKey { X = 0f, Y = 1f, Z = 5f, Yaw = 30f, Pitch = -5f, Roll = 10f, Fov = 40f, Hold = float.NaN });
        return s;
    }

    static void Paths()
    {
        var s = ThreeKeys();
        Check(Near(PromoCameraPolicy.Duration(s), 6f), "duration adds the default and per-key segment times");
        Check(PromoCameraPolicy.Duration(new CamShot()) == 0f, "an empty shot has no duration");
        CamKey start = PromoCameraPolicy.Sample(s, 0f), mid = PromoCameraPolicy.Sample(s, 2f), end = PromoCameraPolicy.Sample(s, 6f);
        Check(Near(start.X, 0f) && Near(start.Z, -5f) && Near(start.Fov, 60f), "the path starts on the first key");
        Check(Near(mid.X, 5f) && Near(mid.Y, 2f) && Near(mid.Fov, 50f), "the path passes through the inner key");
        Check(Near(end.Z, 5f) && Near(end.Roll, 10f) && Near(end.Fov, 40f), "the path ends on the last key");
        Check(Near(PromoCameraPolicy.Sample(s, 99f).Z, 5f), "sampling past the end holds the last key");
        CamKey turn = PromoCameraPolicy.Sample(s, 1f);
        Check(turn.Yaw > 355f && turn.Yaw < 365f, "yaw crosses north the short way, got " + turn.Yaw);
        Vector prev = new Vector(PromoCameraPolicy.Sample(s, 0f));
        for (int i = 1; i <= 600; i++)
        {
            Vector next = new Vector(PromoCameraPolicy.Sample(s, i * 0.01f));
            Check(prev.Distance(next) < 0.2f, "the path is continuous at t=" + (i * 0.01f));
            prev = next;
        }
        var one = new CamShot();
        one.Keys.Add(new CamKey { X = 1f, Fov = 70f });
        Check(PromoCameraPolicy.Sample(one, 3f).X == 1f, "a single key is a still shot");
        Check(PromoCameraPolicy.Sample(new CamShot(), 1f).Fov == 60f, "no keys gives a neutral key");
    }

    struct Vector
    {
        public float X, Y, Z;
        public Vector(CamKey k) { X = k.X; Y = k.Y; Z = k.Z; }
        public float Distance(Vector o) { float dx = X - o.X, dy = Y - o.Y, dz = Z - o.Z; return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz); }
    }

    static void ShotFiles()
    {
        var s = ThreeKeys();
        s.Space = ShotSpace.Car; s.LookAtCar = true; s.LookY = 1.2f; s.TimeScale = 0.5f; s.Fps = 30; s.SuperSize = 2; s.Record = true; s.Level = false;
        string error;
        var back = PromoCameraPolicy.Parse(PromoCameraPolicy.Format(s), out error);
        Check(error == null, "a formatted shot parses cleanly: " + error);
        Check(back.Space == ShotSpace.Car && back.LookAtCar && Near(back.LookY, 1.2f) && back.TimeScale == 0.5f && back.Fps == 30 && back.SuperSize == 2 && back.Record && !back.Level && back.Segment == 2f && back.Ease == 0f, "settings round-trip");
        Check(back.Keys.Count == 3 && Near(back.Keys[1].X, 5f) && back.Keys[1].Hold == 4f && float.IsNaN(back.Keys[0].Hold) && Near(back.Keys[2].Fov, 40f), "keys round-trip");
        var hand = PromoCameraPolicy.Parse("# hand written\r\nspace = world\nlook=car  # aim at the car\nkey 1 2 3 4 5 6\nkey 1 2 3 4 5 6 70 2.5\nease=smooth\nfps=500\nsupersize=9\ntimescale=0\n", out error);
        Check(error == null, "comments, CRLF and spacing are fine: " + error);
        Check(hand.Space == ShotSpace.World && hand.LookAtCar && hand.Keys.Count == 2 && hand.Keys[0].Fov == 60f && hand.Keys[1].Hold == 2.5f && hand.Ease == 0.25f, "a short hand-written shot parses");
        Check(hand.Fps == PromoCameraPolicy.MaxFps && hand.SuperSize == 4 && hand.TimeScale == 0.05f, "out-of-range settings are clamped");
        var bad = PromoCameraPolicy.Parse("key 1 2\nkey a b c d e f\nwobble=3\nkey 1 2 3 4 200 6\n", out error);
        Check(error != null && error.StartsWith("line 1"), "the first bad line is reported");
        Check(bad.Keys.Count == 1 && bad.Keys[0].Pitch == 89f, "good keys survive bad ones and pitch is clamped");
        Check(PromoCameraPolicy.Parse("key 1 2 3 4 5 6 60 NaN\n", out error).Keys.Count == 0 && error != null, "NaN is rejected");
        Check(PromoCameraPolicy.Parse(null, out error).Keys.Count == 0 && error == null, "no text is an empty shot");
        Check(PromoCameraPolicy.ValidName("flyby_2") && PromoCameraPolicy.ValidName("a-b"), "plain names are valid");
        Check(!PromoCameraPolicy.ValidName("..\\x") && !PromoCameraPolicy.ValidName("a/b") && !PromoCameraPolicy.ValidName("") && !PromoCameraPolicy.ValidName(null) && !PromoCameraPolicy.ValidName(new string('a', 49)), "path tricks are not names");
    }

    static void Commands()
    {
        string v, a;
        Check(PromoCameraPolicy.ParseCommand("  free ", out v, out a) && v == "free" && a == null, "bare verb");
        Check(PromoCameraPolicy.ParseCommand("RECORD flyby", out v, out a) && v == "record" && a == "flyby", "verb with shot name");
        Check(PromoCameraPolicy.ParseCommand("play", out v, out a) && v == "play" && a == null, "play without a name plays the edited keys");
        Check(!PromoCameraPolicy.ParseCommand("save", out v, out a) && !PromoCameraPolicy.ParseCommand("load", out v, out a), "save and load need a name");
        Check(!PromoCameraPolicy.ParseCommand("play ../../evil", out v, out a), "bad shot names are refused");
        Check(PromoCameraPolicy.ParseCommand("slowmo 0.25", out v, out a) && a == "0.25" && !PromoCameraPolicy.ParseCommand("slowmo fast", out v, out a), "numbers are checked");
        Check(PromoCameraPolicy.ParseCommand("hud off", out v, out a) && PromoCameraPolicy.ParseCommand("hud text", out v, out a) && !PromoCameraPolicy.ParseCommand("hud maybe", out v, out a), "hud takes on, off or text");
        Check(!PromoCameraPolicy.ParseCommand("format c:", out v, out a) && !PromoCameraPolicy.ParseCommand("", out v, out a) && !PromoCameraPolicy.ParseCommand(null, out v, out a), "unknown commands are refused");
        Check(!PromoCameraPolicy.ParseCommand("stop now", out v, out a) && !PromoCameraPolicy.ParseCommand("play a b", out v, out a), "extra words are refused");
        Check(PromoCameraPolicy.ParseCommand("newworld -12345", out v, out a) && v == "newworld" && a == "-12345", "a new world takes a whole-number seed");
        Check(!PromoCameraPolicy.ParseCommand("newworld", out v, out a) && !PromoCameraPolicy.ParseCommand("newworld 1.5", out v, out a) && !PromoCameraPolicy.ParseCommand("newworld 99999999999", out v, out a), "bad seeds are refused");
        Check(PromoCameraPolicy.ParseCommand("time 0.35", out v, out a) && PromoCameraPolicy.ParseCommand("time run", out v, out a), "time holds a fraction or runs");
        Check(!PromoCameraPolicy.ParseCommand("time 2", out v, out a) && !PromoCameraPolicy.ParseCommand("time", out v, out a), "time is a day fraction");
        Check(PromoCameraPolicy.ParseCommand("menu", out v, out a) && PromoCameraPolicy.ParseCommand("sit", out v, out a) && PromoCameraPolicy.ParseCommand("quit", out v, out a), "automation verbs");
        Check(PromoCameraPolicy.ParseCommand("camera 10.5 3 -20 90 10", out v, out a) && a == "10.5 3 -20 90 10", "camera takes a pose, rest of line kept");
        float[] pose;
        Check(PromoCameraPolicy.TryCameraPose("1 2 3 45 120 5 300", out pose) && pose[4] == 89f && pose[5] == 5f && pose[6] == PromoCameraPolicy.MaxFov, "camera pose is clamped");
        Check(PromoCameraPolicy.TryCameraPose("1 2 3 45 0", out pose) && pose[5] == 0f && float.IsNaN(pose[6]), "roll and fov are optional");
        Check(!PromoCameraPolicy.TryCameraPose("1 2 3", out pose) && !PromoCameraPolicy.TryCameraPose("1 2 3 4 x", out pose) && !PromoCameraPolicy.ParseCommand("camera", out v, out a), "short or bad poses are refused");
        Check(Near(PromoCameraPolicy.TimeShift(100f, 1000f, 0.5f), 400f) && Near(PromoCameraPolicy.TimeShift(900f, 1000f, 0.1f), -800f), "time shift lands on the wanted fraction");
        Check(PromoCameraPolicy.TimeShift(100f, 0f, 0.5f) == 0f && Near(PromoCameraPolicy.TimeShift(0f, 1000f, 7f), 1000f), "time shift is bounded");
        double tx, tz;
        Check(PromoCameraPolicy.ParseCommand("teleport -2852.5 1328", out v, out a) && PromoCameraPolicy.TryTeleport(a, out tx, out tz) && tx == -2852.5 && tz == 1328, "teleport takes a global x z");
        Check(!PromoCameraPolicy.ParseCommand("teleport 1", out v, out a) && !PromoCameraPolicy.ParseCommand("teleport 1 2 3", out v, out a) && !PromoCameraPolicy.ParseCommand("teleport NaN 2", out v, out a) && !PromoCameraPolicy.ParseCommand("teleport 1e9 2", out v, out a), "bad teleports are refused");
        int sr, ss;
        Check(PromoCameraPolicy.ParseCommand("biomescan 20000 250", out v, out a) && PromoCameraPolicy.TryScan(a, out sr, out ss) && sr == 20000 && ss == 250, "biome scan takes radius and step");
        Check(!PromoCameraPolicy.ParseCommand("biomescan 50000 10", out v, out a) && !PromoCameraPolicy.ParseCommand("biomescan 50 10", out v, out a) && !PromoCameraPolicy.ParseCommand("biomescan 1000", out v, out a), "huge, tiny or incomplete scans are refused");
        Check(PromoCameraPolicy.BiomeLetter(0f, 1f, 0f, 1f) == 'G' && PromoCameraPolicy.BiomeLetter(0f, 0.99f, 0f, 1f) == 'g', "only the exact native grass colour counts as grass");
        Check(PromoCameraPolicy.BiomeLetter(0.5f, 0f, 0f, 0f) == 'D' && PromoCameraPolicy.BiomeLetter(0.5f, 0f, 0.5f, 0f) == 'S' && PromoCameraPolicy.BiomeLetter(0.1f, 0.2f, 0.1f, 0f) == '.', "snow wins over desert, as in the game");
        var grid = new char[3, 3] { { 'D', 'D', 'D' }, { 'G', 'G', 'D' }, { 'G', 'G', 'G' } };
        Check(PromoCameraPolicy.GrassBorder(grid, 1, 0) && PromoCameraPolicy.GrassBorder(grid, 1, 1) && !PromoCameraPolicy.GrassBorder(grid, 2, 0) && !PromoCameraPolicy.GrassBorder(grid, 0, 0), "borders are grass cells next to desert");
        var dist = new[] { 5f, 3f, 2f, 80f, float.NaN };
        var driver = new[] { true, false, true, true, true };
        var inCar = new[] { true, true, false, true, true };
        Check(PromoCameraPolicy.PickSeat(dist, driver, inCar, 60f) == 0, "the closest free driver seat wins over a nearer passenger seat");
        Check(PromoCameraPolicy.PickSeat(new[] { 3f, 10f }, new[] { false, false }, new[] { true, true }, 60f) == 0, "a passenger seat when no driver seat is near");
        Check(PromoCameraPolicy.PickSeat(new[] { 70f }, new[] { true }, new[] { true }, 60f) == -1, "nothing out of reach");
        Check(PromoCameraPolicy.FrameName(7) == "frame_000007.jpg", "frames sort by name");
        Check(PromoCameraPolicy.ClipFolder(new DateTime(2026, 11, 15, 9, 5, 3), "flyby") == "2026-11-15_090503_flyby", "clip folders are dated");
        Check(PromoCameraPolicy.ClipFolder(new DateTime(2026, 11, 15, 9, 5, 3), "../x") == "2026-11-15_090503", "a bad name is left out of the folder");
        string args = PromoCameraPolicy.FfmpegArguments(60);
        Check(args.Contains("-framerate 60 ") && args.Contains("frame_%06d.jpg") && args.EndsWith("clip.mp4") && args.Contains("yuv420p"), "ffmpeg makes a widely playable MP4");
        Check(PromoCameraPolicy.FfmpegArguments(1000).Contains("-framerate 120 "), "ffmpeg frame rate is clamped");
        Check(!PromoCameraPolicy.MustWaitForWrites(10) && PromoCameraPolicy.MustWaitForWrites(90), "capture waits only when the disk falls far behind");
    }

    static void Options()
    {
        PatchOptions.Parse(null);
        Check(!PatchOptions.PromoCamera && PatchOptions.Format().Contains("promocamera=off\n"), "the camera tools are off by default and saved as off");
        PatchOptions.Parse("promocamera=on\n");
        Check(PatchOptions.PromoCamera, "the camera tools can be turned on");
        PatchOptions.Parse(PatchOptions.Format());
        Check(PatchOptions.PromoCamera, "the on choice round-trips");
        PatchOptions.Parse("promocamera=maybe\n");
        Check(!PatchOptions.PromoCamera, "a bad value keeps them off");
        PatchOptions.ResetToDefaults();
        Check(!PatchOptions.PromoCamera, "reset turns them off");
    }
}
