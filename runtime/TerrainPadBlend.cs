using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TldMp
{
    // Managed POI planning data only: the same height rule is used by collider
    // terrain and distant workers. Never move the foundation or terrain holes.
    public static class TerrainPadBlend
    {
        sealed class Extended { }
        static readonly ConditionalWeakTable<poiGenScript.alignClass,Extended> extended = new ConditionalWeakTable<poiGenScript.alignClass,Extended>();

        public static void Prepare(poiGenScript.poiClass poi)
        {
            if (WorldPlacementPolicy.EffectiveVersion < 5 || poi == null || poi.terrainAligns == null) return;
            foreach (var pad in poi.terrainAligns)
            {
                if (pad == null || !Finite(pad.minRadius) || !Finite(pad.maxRadius) || pad.minRadius < 0 || pad.maxRadius <= pad.minRadius) continue;
                Extended value;
                if (extended.TryGetValue(pad,out value)) continue;
                // Widen only the sloping shoulder, by at most eight metres.
                float extra = (float)Math.Min(8.0,(pad.maxRadius-pad.minRadius)*0.25);
                pad.maxRadius += extra;
                pad.maxEffectRange += extra;
                extended.Add(pad,new Extended());
            }
        }

        static bool Finite(double v){return !double.IsNaN(v) && !double.IsInfinity(v);}
        static double Weight(Vector3d position,poiGenScript.alignClass pad)
        {
            double x=position.x-pad.gpos.x,z=position.z-pad.gpos.z;
            double distance=Math.Sqrt(x*x+z*z);
            return Mathd.InverseLerp(-1,1,Mathd.Sin(Mathd.Lerp(-90,90,
                Mathd.InverseLerp(pad.maxRadius,pad.minRadius,distance))*0.01745329));
        }

        public static poiGenScript.terrainAlignValueClass Value(Vector3d position,List<poiGenScript.poiClass> pois)
        {
            var native=poiGenScript.TerrainAlignsValue(position,pois);
            if (WorldPlacementPolicy.EffectiveVersion < 5 || native == null ||
                !(native.lerp > 0) || native.lerp >= 1 || pois == null) return native;
            // Keep native full-strength foundations exact. Outside the cores,
            // smoothly favour the strongest pad instead of switching winners.
            double sum=0,height=0,remaining=1-native.lerp;
            int contributors=0;
            foreach (var poi in pois)
            {
                if (poi == null || poi.terrainAligns == null) continue;
                foreach (var pad in poi.terrainAligns)
                {
                    if (pad == null) continue;
                    double dx=position.x-pad.gpos.x,dz=position.z-pad.gpos.z;
                    if (pad.minRadius>=0 && dx*dx+dz*dz<=pad.minRadius*pad.minRadius) return native;
                    double w=Weight(position,pad);
                    if (!(w > 0) || !Finite(w) || !Finite(pad.gpos.y)) continue;
                    // The native sine constant can leave a core just below 1.
                    double ratio=w/native.lerp;
                    double fade=remaining/Math.Max(remaining,1-w);
                    double influence=ratio*ratio*ratio*ratio*fade*fade;
                    sum+=influence;height+=influence*pad.gpos.y;contributors++;
                }
            }
            if (contributors > 1 && sum > 0 && Finite(height)) native.height=height/sum;
            return native;
        }
    }
}
