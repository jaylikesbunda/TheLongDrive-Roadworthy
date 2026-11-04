using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TldMp
{
    public static class SurvivalRates
    {
        sealed class Sickness {public Sickness(){} internal float remaining;}
        static readonly ConditionalWeakTable<survivalscript,Sickness> sickness=new ConditionalWeakTable<survivalscript,Sickness>();
        public static void Clear(survivalscript s){if(s!=null)sickness.Remove(s);}
        public static bool IsSick(survivalscript s)
        {Sickness state;return s!=null && sickness.TryGetValue(s,out state) && state.remaining>0;}
        public static void AddSickness(survivalscript s,float amount)
        {
            if(s==null || !(amount>0) || float.IsInfinity(amount) || float.IsNaN(amount) ||
                !(s.carFluidsDamage>0) || float.IsInfinity(s.carFluidsDamage) || float.IsNaN(s.carFluidsDamage))return;
            var state=sickness.GetOrCreateValue(s);
            state.remaining=(float)Math.Min(120.0,state.remaining+(double)amount*s.carFluidsDamage*30.0);
        }
        static float SicknessMultiplier(survivalscript s)
        {
            Sickness state;
            if(!sickness.TryGetValue(s,out state) || !(state.remaining>0))return 1f;
            state.remaining=Math.Max(0,state.remaining-Math.Max(0,Math.Min(Time.deltaTime,1f)));
            return 2f;
        }
        static bool Running(survivalscript s)
        {
            var p=s.player;
            if(p==null || p.sat || p.input==null || !p.input.moving || !p.Brun || p.RB==null ||
                (mainscript.s!=null && mainscript.s.sleeping)) return false;
            Vector3 velocity=p.RB.velocity;
            if(p.interiorRB!=null)velocity-=p.interiorRB.velocity;
            return velocity.x*velocity.x+velocity.z*velocity.z>0.0625f;
        }
        static float Modifier(float value)
        {return float.IsNaN(value)||float.IsInfinity(value)||value<1f?1f:value;}
        public static float Food(survivalscript s)
        {s.running=Running(s);return s.foodLoss*(s.running?Modifier(s.runFoodLossModifier):1f);}
        public static float Water(survivalscript s)
        {s.running=Running(s);return s.waterLoss*(s.running?Modifier(s.runWaterLossModifier):1f)*SicknessMultiplier(s);}
    }
}
