using System;

namespace TldMp
{
    public static class UrineLiquid
    {
        public const int FluidId = 7;

        // Only pass the active player's bladder. Native player prefabs can start
        // it with alcohol and water, and old saves retain those native entries.
        // Merge rather than merely retype: GetPercent returns the first entry.
        public static bool NormalizeBladder(mainscript.fluidcontainer bladder)
        {
            if (bladder == null || bladder.fluids == null || bladder.fluids.Count == 0) return false;
            double volume = 0;
            bool needsChange = bladder.fluids.Count != 1;
            for (int i = 0; i < bladder.fluids.Count; i++)
            {
                mainscript.fluid fluid = bladder.fluids[i];
                if (fluid == null || float.IsNaN(fluid.amount) || float.IsInfinity(fluid.amount)) return false;
                volume += fluid.amount;
                if ((int)fluid.type != FluidId || fluid.amount < 0) needsChange = true;
            }
            if (!needsChange) return false;
            if (double.IsNaN(volume) || double.IsInfinity(volume) || volume > float.MaxValue) return false;
            float amount = (float)Math.Max(0, volume);
            bladder.fluids.Clear();
            if (amount > 0) bladder.fluids.Add(new mainscript.fluid((mainscript.fluidenum)FluidId, amount));
            bladder.ChangeSync(amount);
            return true;
        }
    }
}
