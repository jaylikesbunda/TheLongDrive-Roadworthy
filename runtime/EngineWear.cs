using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Pure decisions for EngineWear (no Unity objects, unit-tested).
    // Condition states are the game's own: 0 new, 1 used, 2 middle, 3 old, 4 rusty.
    public static class EngineWearPolicy
    {
        public const int MaxState = 4;
        // The wire brush restores a worn engine at normal speed down to "middle";
        // the last two steps to used and new take 4x and 8x as long. Swapping in a
        // better engine from a wreck stays the quick way to a good one.
        public const int SlowFrom = 2;

        // Wear per second at full load. About 4 hours of normal driving per
        // condition step, so a long trip costs one step, not the engine.
        public const float BaseRate = 1f / 14000f;
        // Running hot or short of oil wears far faster than driving.
        public const float OverheatRate = 1f / 600f;
        public const float LowOilRate = 1f / 900f;

        static float T(int state) { return Math.Min(Math.Max(state, 0), MaxState) / (float)MaxState; }
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        // Multipliers applied to the engine's own values. A rusty engine still
        // drives: about three quarters of the power, harder starts, more oil,
        // more heat and smoke.
        public static float Power(int state) { return Lerp(1f, 0.75f, T(state)); }
        public static float StartChance(int state) { return Lerp(1f, 0.6f, T(state)); }
        public static float OilUse(int state) { return Lerp(1f, 3f, T(state)); }
        public static float Heat(int state) { return Lerp(1f, 1.3f, T(state)); }
        public static float Smoke(int state) { return Lerp(1f, 3f, T(state)); }

        // Wear gained this frame. rpmFraction is rpm/maxRpm; overheat and lowOil are 0..1.
        public static float Wear(bool running, float rpmFraction, float overheat, float lowOil, float deltaTime)
        {
            if (!running || !(deltaTime > 0f)) return 0f;
            float load = Clamp01(rpmFraction);
            float rate = BaseRate * (0.3f + 1.7f * load * load);
            rate += OverheatRate * Clamp01(overheat);
            rate += LowOilRate * Clamp01(lowOil);
            return rate * deltaTime;
        }

        // A step of condition is lost once wear reaches 1; a rusty engine stays rusty.
        public static bool StepDown(float wear, int state) { return wear >= 1f && state < MaxState; }

        // The brush works on any worn engine that is switched off.
        public static bool CanBrush(bool optionOn, int state, bool running)
        {
            return optionOn && !running && state > 0;
        }

        // Share of the brush's progress kept for an engine in this state.
        public static float BrushSpeed(int state)
        {
            if (state > SlowFrom) return 1f;
            if (state == SlowFrom) return 0.25f;
            return 0.125f;
        }

        // Repair progress after scaling this frame's gain from the brush.
        public static float ScaleRepair(float previous, float current, int state)
        {
            if (float.IsNaN(previous) || float.IsNaN(current) || !(current > previous)) return current;
            return previous + (current - previous) * BrushSpeed(state);
        }

        // Radiators wear only while the engine runs: about 5 hours per step,
        // minutes when the engine is boiling.
        public const float RadiatorRate = 1f / 18000f;
        public const float RadiatorOverheatRate = 1f / 900f;

        public static float RadiatorWear(bool running, float overheat, float deltaTime)
        {
            if (!running || !(deltaTime > 0f)) return 0f;
            return (RadiatorRate + RadiatorOverheatRate * Clamp01(overheat)) * deltaTime;
        }

        // Tires wear with distance (about 200 km per step) and fast with
        // wheelspin or skids (about a minute of burnout per step). They stop at
        // "old" so worn tires still grip; a fresh set is the way back to new.
        public const float TireRatePerMetre = 1f / 200000f;
        public const float TireSlipRate = 1f / 60f;
        public const float TireSlipFrom = 0.3f;
        public const int TireFloor = 3;

        public static float TireWear(float speedMps, float slip, float deltaTime)
        {
            if (!(deltaTime > 0f)) return 0f;
            float speed = float.IsNaN(speedMps) || speedMps < 0f ? 0f : Math.Min(speedMps, 100f);
            float excess = Clamp01((Math.Abs(float.IsNaN(slip) ? 0f : slip) - TireSlipFrom) / (1f - TireSlipFrom));
            return (TireRatePerMetre * speed + TireSlipRate * excess) * deltaTime;
        }

        public static bool StepDown(float wear, int state, int floor) { return wear >= 1f && state < floor; }

        static float Clamp01(float v) { return float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v; }
    }

    // Part wear (Settings > Roadworthy > Part wear; option key "enginewear").
    // Engine: power, starting, oil use, heat and smoke follow the engine part's
    // condition, and running the engine slowly wears it (faster when overheating or
    // short of oil). Radiator and tires already lose cooling and grip with condition
    // in the base game; here they also wear: the radiator while the engine runs,
    // tires with distance and wheelspin. The wire brush restores engines and
    // radiators (slower on the last two steps); tires are replaced.
    // Cars are picked up from the one the player is in, then kept until destroyed.
    // Wear is only applied on the host or in single player; condition changes go
    // through the game's own synced Refresh, like the wire brush.
    public static class EngineWear
    {
        sealed class Part
        {
            internal partconditionscript Condition;
            internal gumiscript Tire;
            internal float Wear, LastRepair;
            internal int LastState = -1;
        }
        sealed class Car
        {
            internal carscript Script;
            internal float NextScan;
            internal readonly List<Part> Tires = new List<Part>();
            internal Part Radiator;
        }
        static readonly List<Car> cars = new List<Car>();
        public static long TireSteps, RadiatorSteps;

        sealed class Tracked
        {
            internal enginescript Engine;
            internal partconditionscript Condition;
            internal float Modifier, StartChance, OilUse, HeatMin, HeatMax, Smoke;
            internal float Wear, LastRepair;
            internal int Applied = -1, LastState = -1;
        }
        static readonly List<Tracked> tracked = new List<Tracked>();
        static readonly HashSet<int> known = new HashSet<int>();
        public static long Steps;

        public static string Summary()
        {
            int tires = 0; for (int i = 0; i < cars.Count; i++) tires += cars[i].Tires.Count;
            return "engineWear{tracked=" + tracked.Count + " steps=" + Steps + " cars=" + cars.Count + " tires=" + tires + " tireSteps=" + TireSteps + " radiatorSteps=" + RadiatorSteps + "}";
        }

        public static void Tick()
        {
            PatchOptions.Load();
            bool on = PatchOptions.EngineWear;
            if (on) Discover();
            if (tracked.Count == 0 && cars.Count == 0) return;
            float dt = Time.deltaTime;
            bool authority = syncScript.IsHostOrSingle();
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                var t = tracked[i];
                if (t.Engine == null || t.Condition == null) { tracked.RemoveAt(i); continue; }
                if (!on) { Restore(t); tracked.RemoveAt(i); continue; }
                int state = t.Condition.state;
                if (state != t.Applied) Apply(t, state);
                // Slow the brush on the last steps: scale the progress it made since last frame.
                if (state == t.LastState)
                {
                    float scaled = EngineWearPolicy.ScaleRepair(t.LastRepair, t.Condition.repairValue, state);
                    if (scaled != t.Condition.repairValue) t.Condition.repairValue = scaled;
                }
                t.LastState = state; t.LastRepair = t.Condition.repairValue;
                if (!authority || Time.timeScale == 0f) continue;
                var e = t.Engine;
                float rpmFraction = e.maxRpm > 0f ? e.rpm / e.maxRpm : 0f;
                t.Wear += EngineWearPolicy.Wear(e.running, rpmFraction, Overheat(e), e.OilWeak, dt);
                if (EngineWearPolicy.StepDown(t.Wear, state))
                {
                    t.Wear = 0f;
                    t.Condition.state = state + 1;
                    t.Condition.Refresh(true);
                    Steps++;
                    Debug.Log("[Roadworthy wear] engine " + e.name + " wore to condition " + (state + 1));
                }
                else if (t.Wear >= 1f) t.Wear = 1f;
            }
            if (!on) { cars.Clear(); known.Clear(); return; }
            bool wear = authority && Time.timeScale != 0f;
            for (int i = cars.Count - 1; i >= 0; i--)
            {
                var c = cars[i];
                if (c.Script == null) { cars.RemoveAt(i); continue; }
                if (Time.unscaledTime >= c.NextScan) Scan(c);
                TickCar(c, wear, dt);
            }
        }

        static float Overheat(enginescript e)
        {
            return e.maxOptimalTemp2 > e.maxOptimalTemp1 ? Mathf.InverseLerp(e.maxOptimalTemp1, e.maxOptimalTemp2, e.temp) : 0f;
        }

        static void TickCar(Car c, bool wear, float dt)
        {
            var car = c.Script;
            var engine = car.Engine;
            var r = c.Radiator;
            if (r != null && r.Condition == null) c.Radiator = r = null;
            if (r != null)
            {
                SlowBrush(r);
                if (wear && engine != null)
                {
                    r.Wear += EngineWearPolicy.RadiatorWear(engine.running, Overheat(engine), dt);
                    if (StepPart(r, EngineWearPolicy.MaxState))
                    {
                        RadiatorSteps++;
                        Debug.Log("[Roadworthy wear] radiator on " + car.name + " wore to condition " + r.Condition.state);
                    }
                }
            }
            if (c.Tires.Count == 0) return;
            float speed = 0f, slip = 0f;
            if (wear)
            {
                if (car.RB != null) speed = car.RB.velocity.magnitude;
                slip = WheelSlip(car);
            }
            for (int i = c.Tires.Count - 1; i >= 0; i--)
            {
                var p = c.Tires[i];
                if (p.Condition == null || p.Tire == null) { c.Tires.RemoveAt(i); continue; }
                // Grip follows the tire's condition; refresh it when the state changes (also on guests).
                if (p.Condition.state != p.LastState) { p.Tire.Refresh(); p.LastState = p.Condition.state; }
                if (!wear) continue;
                p.Wear += EngineWearPolicy.TireWear(speed, slip, dt);
                if (StepPart(p, EngineWearPolicy.TireFloor)) { p.Tire.Refresh(); TireSteps++; }
            }
        }

        // Average grounded slip across the car's wheels (0 when airborne).
        static float WheelSlip(carscript car)
        {
            var wheels = car.allWH;
            if (wheels == null) return 0f;
            float sum = 0f; int n = 0;
            for (int i = 0; i < wheels.Count; i++)
            {
                var w = wheels[i];
                WheelHit hit;
                if (w == null || !w.GetGroundHit(out hit)) continue;
                sum += Math.Max(Math.Abs(hit.forwardSlip), Math.Abs(hit.sidewaysSlip));
                n++;
            }
            return n == 0 ? 0f : sum / n;
        }

        // The brush's last two steps are slower on radiators too.
        static void SlowBrush(Part p)
        {
            int state = p.Condition.state;
            if (state == p.LastState)
            {
                float scaled = EngineWearPolicy.ScaleRepair(p.LastRepair, p.Condition.repairValue, state);
                if (scaled != p.Condition.repairValue) p.Condition.repairValue = scaled;
            }
            p.LastState = state; p.LastRepair = p.Condition.repairValue;
        }

        static bool StepPart(Part p, int floor)
        {
            int state = p.Condition.state;
            if (EngineWearPolicy.StepDown(p.Wear, state, floor))
            {
                p.Wear = 0f;
                p.Condition.state = state + 1;
                p.Condition.Refresh(true);
                p.LastState = state + 1; p.LastRepair = p.Condition.repairValue;
                return true;
            }
            if (p.Wear >= 1f) p.Wear = 1f;
            return false;
        }

        // Tires and radiators get swapped, so look again every few seconds.
        static void Scan(Car c)
        {
            c.NextScan = Time.unscaledTime + 3f;
            var car = c.Script;
            var rc = car.coolant == null ? null : car.coolant.condition;
            if (rc == null) c.Radiator = null;
            else if (c.Radiator == null || c.Radiator.Condition != rc) c.Radiator = new Part { Condition = rc, LastState = rc.state, LastRepair = rc.repairValue };
            var tires = car.GetComponentsInChildren<gumiscript>(false);
            for (int i = c.Tires.Count - 1; i >= 0; i--)
                if (c.Tires[i].Tire == null || Array.IndexOf(tires, c.Tires[i].Tire) < 0) c.Tires.RemoveAt(i);
            for (int i = 0; i < tires.Length; i++)
            {
                var g = tires[i];
                if (g == null || g.pc == null) continue;
                bool have = false;
                for (int j = 0; j < c.Tires.Count; j++) if (c.Tires[j].Tire == g) { have = true; break; }
                if (!have) c.Tires.Add(new Part { Tire = g, Condition = g.pc, LastState = g.pc.state });
            }
        }

        static void Discover()
        {
            var main = mainscript.s;
            var car = main == null || main.player == null ? null : main.player.Car;
            if (car == null) return;
            if (car.Engine != null) Track(car.Engine, null);
            for (int i = 0; i < cars.Count; i++) if (cars[i].Script == car) return;
            var c = new Car { Script = car };
            cars.Add(c);
            Scan(c);
        }

        internal static void Track(enginescript engine, partconditionscript condition)
        {
            if (engine == null || !PatchOptions.EngineWear || !known.Add(engine.GetInstanceID())) return;
            if (condition == null) condition = ConditionOf(engine);
            if (condition == null) return;
            tracked.Add(new Tracked
            {
                Engine = engine, Condition = condition,
                Modifier = engine.modifier, StartChance = engine.startChance, OilUse = engine.OilConsumptionModifier,
                HeatMin = engine.engineHeatGainMin, HeatMax = engine.engineHeatGainMax, Smoke = engine.defSmokeRate
            });
        }

        static void Apply(Tracked t, int state)
        {
            var e = t.Engine;
            e.modifier = t.Modifier * EngineWearPolicy.Power(state);
            e.startChance = t.StartChance * EngineWearPolicy.StartChance(state);
            e.OilConsumptionModifier = t.OilUse * EngineWearPolicy.OilUse(state);
            e.engineHeatGainMin = t.HeatMin * EngineWearPolicy.Heat(state);
            e.engineHeatGainMax = t.HeatMax * EngineWearPolicy.Heat(state);
            e.defSmokeRate = t.Smoke * EngineWearPolicy.Smoke(state);
            t.Applied = state;
        }

        static void Restore(Tracked t)
        {
            var e = t.Engine;
            if (e == null) return;
            e.modifier = t.Modifier; e.startChance = t.StartChance; e.OilConsumptionModifier = t.OilUse;
            e.engineHeatGainMin = t.HeatMin; e.engineHeatGainMax = t.HeatMax; e.defSmokeRate = t.Smoke;
        }

        // The engine's own condition: on the engine object, or the first one below it
        // that is not a child of another condition.
        internal static partconditionscript ConditionOf(enginescript engine)
        {
            var own = engine.GetComponent<partconditionscript>();
            if (own != null) return own;
            var all = engine.GetComponentsInChildren<partconditionscript>(true);
            for (int i = 0; i < all.Length; i++) if (!all[i].isChild) return all[i];
            return null;
        }

        // The engine a condition belongs to, if any (for the wire brush hook).
        internal static enginescript EngineOf(partconditionscript condition)
        {
            var engine = condition.GetComponentInParent<enginescript>();
            return engine != null && ConditionOf(engine) == condition ? engine : null;
        }

        // Called by the repair hook: may the brush work on this engine right now?
        internal static bool BrushAllowed(enginescript engine, partconditionscript condition)
        {
            return EngineWearPolicy.CanBrush(PatchOptions.EngineWear, condition.state, engine.running);
        }

        public static void Clear()
        {
            for (int i = 0; i < tracked.Count; i++) Restore(tracked[i]);
            tracked.Clear(); known.Clear(); cars.Clear();
        }
    }
}
