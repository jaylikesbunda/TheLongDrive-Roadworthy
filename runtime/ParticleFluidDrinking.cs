using System;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    // Only the five-argument pour in Tankolas' drink-cap branch calls this.
    // Particles already left the source, so the native non-direct pour must
    // remain the sole transfer and the source must not be debited again.
    public static class ParticleFluidDrinking
    {
        static float UrineAmount(mainscript.fluidcontainer container)
        {
            float amount=0;
            for(int i=0;i<container.fluids.Count;i++)
                if(container.fluids[i].type==StableGameBridge.UrineFluid)amount+=container.fluids[i].amount;
            return amount;
        }
        public static void Transfer(mainscript.fluidcontainer receiver,mainscript.fluidcontainer donor,
            float amount,bool direct,bool filter,List<mainscript.fluidenum> drinkFilter,
            tankcapparticledetectionscript detector,tankcapscript destination)
        {
            bool mouth=!direct && destination!=null && destination.isDrinkCap && destination.player!=null &&
                destination.Tank!=null && detector!=null && detector.T!=null && detector.T.Tank!=null &&
                destination.Tank!=detector.T.Tank && receiver==destination.Tank.F && donor==detector.T.Tank.F;
            float before=mouth?UrineAmount(receiver):0f;
            float toxicBefore=mouth?StableGameBridge.BadFluidAmount(receiver):0f;
            float alcohol=mouth?DrunkEffects.AlcoholDrunk(donor,amount):0f;
            float milk=mouth?DrunkEffects.MilkDrunk(donor,amount):0f;
            receiver.pour(donor,amount,direct,filter,drinkFilter);
            if(!mouth)return;
            StableGameBridge.NoteBadFluidDrink(destination.player.survival,Mathf.Max(0f,StableGameBridge.BadFluidAmount(receiver)-toxicBefore));
            DrunkEffects.NoteAlcohol(destination.player.survival,alcohol);
            DrunkEffects.NoteMilk(destination.player.survival,milk);
            float consumed=Mathf.Max(0f,UrineAmount(receiver)-before);
            if(consumed<=0f)return;
            receiver.ChangeOne(-consumed,StableGameBridge.UrineFluid);
            destination.player.survival.Drink(consumed*0.25f);
            destination.player.survival.Damage(consumed*destination.player.survival.maxHp*0.02f,Vector3.zero);
            StableGameBridge.TraceUrineDrink(consumed,consumed*0.25f,consumed*destination.player.survival.maxHp*0.02f);
        }
    }
}
