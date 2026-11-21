using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class EngineWearPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            string path = name == "Assembly-CSharp" ? args[1] : Path.Combine(args[0], name + ".dll");
            return File.Exists(path) ? Assembly.Load(File.ReadAllBytes(path)) : null;
        };
        Assembly.Load(File.ReadAllBytes(args[1]));
        return Run();
    }

    static int Run()
    {
        // Effects: a new engine is untouched, a rusty one is weaker but still drives.
        Check(EngineWearPolicy.Power(0) == 1f && EngineWearPolicy.StartChance(0) == 1f && EngineWearPolicy.OilUse(0) == 1f && EngineWearPolicy.Heat(0) == 1f && EngineWearPolicy.Smoke(0) == 1f, "a new engine runs exactly as the game made it");
        Check(EngineWearPolicy.Power(4) >= 0.7f && EngineWearPolicy.Power(4) < 1f, "a rusty engine keeps most of its power");
        Check(EngineWearPolicy.StartChance(4) >= 0.5f, "a rusty engine still starts most of the time");
        Check(EngineWearPolicy.Power(1) > EngineWearPolicy.Power(3) && EngineWearPolicy.OilUse(3) > EngineWearPolicy.OilUse(1), "effects get worse step by step");
        Check(EngineWearPolicy.Power(9) == EngineWearPolicy.Power(4) && EngineWearPolicy.Power(-2) == 1f, "out-of-range states are clamped");

        // Wear: slow from driving, fast when hot or short of oil, none when off.
        Check(EngineWearPolicy.Wear(false, 1f, 1f, 1f, 1f) == 0f, "a stopped engine does not wear");
        Check(EngineWearPolicy.Wear(true, 0.5f, 0f, 0f, -1f) == 0f, "a negative frame time does not wear");
        float cruise = EngineWearPolicy.Wear(true, 0.5f, 0f, 0f, 1f);
        float hours = 1f / cruise / 3600f;
        Check(hours > 3f && hours < 12f, "normal driving costs one condition step in a few hours, got " + hours);
        Check(EngineWearPolicy.Wear(true, 1f, 0f, 0f, 1f) > cruise, "high revs wear faster");
        Check(1f / EngineWearPolicy.Wear(true, 0.5f, 1f, 0f, 1f) < 20f * 60f, "overheating costs a step within minutes");
        Check(EngineWearPolicy.Wear(true, 0.5f, 0f, 1f, 1f) > cruise * 5f, "running dry wears much faster");
        Check(EngineWearPolicy.Wear(true, float.NaN, float.NaN, float.NaN, 1f) > 0f, "unknown readings count as idle, not as damage");
        Check(EngineWearPolicy.StepDown(1f, 3) && !EngineWearPolicy.StepDown(0.99f, 3) && !EngineWearPolicy.StepDown(5f, 4), "a step is lost at full wear and rusty is the floor");

        // Repair: switched-off worn engines only; the last two steps are slower.
        Check(EngineWearPolicy.CanBrush(true, 4, false) && EngineWearPolicy.CanBrush(true, 1, false), "a worn engine that is off can be brushed all the way");
        Check(!EngineWearPolicy.CanBrush(true, 3, true), "a running engine cannot be brushed");
        Check(!EngineWearPolicy.CanBrush(true, 0, false), "a new engine needs no brushing");
        Check(!EngineWearPolicy.CanBrush(false, 4, false), "with the option off the engine keeps its own repair rules");
        Check(EngineWearPolicy.BrushSpeed(4) == 1f && EngineWearPolicy.BrushSpeed(3) == 1f, "rusty to middle brushes at normal speed");
        Check(EngineWearPolicy.BrushSpeed(2) < EngineWearPolicy.BrushSpeed(3) && EngineWearPolicy.BrushSpeed(1) < EngineWearPolicy.BrushSpeed(2), "used and new take longer");
        Check(EngineWearPolicy.ScaleRepair(0.2f, 0.6f, 4) == 0.6f, "full-speed steps keep the brush's progress");
        Check(Math.Abs(EngineWearPolicy.ScaleRepair(0.2f, 0.6f, 2) - 0.3f) < 1e-6f, "the step to used keeps a quarter of the progress");
        Check(EngineWearPolicy.ScaleRepair(0.6f, 0.1f, 1) == 0.1f && EngineWearPolicy.ScaleRepair(float.NaN, 0.5f, 1) == 0.5f, "resets and unknown values pass through");

        // Radiator: wears only while the engine runs, fast when boiling.
        Check(EngineWearPolicy.RadiatorWear(false, 1f, 1f) == 0f, "a radiator does not wear with the engine off");
        float radHours = 1f / EngineWearPolicy.RadiatorWear(true, 0f, 1f) / 3600f;
        Check(radHours > 3f && radHours < 12f, "a radiator loses a step in a few hours of running, got " + radHours);
        Check(1f / EngineWearPolicy.RadiatorWear(true, 1f, 1f) < 20f * 60f, "a boiling engine costs the radiator a step within minutes");

        // Tires: distance and wheelspin, floor at old.
        Check(EngineWearPolicy.TireWear(0f, 0f, 1f) == 0f, "a parked car's tires do not wear");
        float km = 1f / EngineWearPolicy.TireWear(1f, 0f, 1f) / 1000f;
        Check(km > 100f && km < 400f, "tires lose a step every couple of hundred km, got " + km);
        Check(EngineWearPolicy.TireWear(20f, 0.2f, 1f) == EngineWearPolicy.TireWear(20f, 0f, 1f), "normal grip slip does not add wear");
        float burnout = 1f / EngineWearPolicy.TireWear(0f, 2f, 1f);
        Check(burnout > 30f && burnout < 180f, "a burnout costs a step in about a minute, got " + burnout);
        Check(EngineWearPolicy.TireWear(float.NaN, float.NaN, 1f) == 0f && EngineWearPolicy.TireWear(-5f, -0.1f, 1f) == 0f, "bad readings do not wear tires");
        Check(EngineWearPolicy.StepDown(1f, 2, EngineWearPolicy.TireFloor) && !EngineWearPolicy.StepDown(1f, 3, EngineWearPolicy.TireFloor), "tires stop wearing at old");

        // Option: off by default, saved and read back.
        PatchOptions.Parse("");
        Check(!PatchOptions.EngineWear, "engine wear defaults off");
        PatchOptions.Parse("enginewear=on\n");
        Check(PatchOptions.EngineWear && PatchOptions.Format().Contains("enginewear=on\n"), "engine wear can be turned on and is saved");
        PatchOptions.Parse("");
        Check(PatchOptions.NewBuildings, "new buildings default on for new worlds");
        PatchOptions.Parse("newbuildings=off\n");
        Check(!PatchOptions.NewBuildings && PatchOptions.Format().Contains("newbuildings=off\n"), "new buildings can be turned off and is saved");
        PatchOptions.Parse("enginewear=maybe\n");
        Check(!PatchOptions.EngineWear, "a bad value keeps the default");

        Console.WriteLine("PASS: " + checks + " part wear assertions; engine condition drives power, starting, oil, heat and smoke, engines, radiators and tires wear slowly and fast under abuse, tires stop at old, and the brush restores a switched-off engine with slower last steps.");
        return 0;
    }
}
