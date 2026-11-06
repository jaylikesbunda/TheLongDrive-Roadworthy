using System;

namespace TldMp
{
    // Frame-time budget for world streaming work that can be split across
    // frames. Pure logic only (no Unity types) so the decisions are unit-testable.
    public sealed class StreamingFrameBudget
    {
        readonly double budgetMs;
        int frame=int.MinValue;double spentMs;

        public StreamingFrameBudget(double budgetMs){this.budgetMs=budgetMs;}

        public double BudgetMs{get{return budgetMs;}}

        // A new frame number resets the spent time. Work is admitted while the
        // frame has room, so the chunk that crosses the budget finishes and the
        // remainder waits for the next frame. Every frame starts with a full budget,
        // so deferred work always makes progress.
        public bool HasRoom(int frameNumber){Sync(frameNumber);return spentMs<budgetMs;}

        public void Charge(int frameNumber,double ms){Sync(frameNumber);if(ms>0)spentMs+=ms;}

        public double SpentMs(int frameNumber){Sync(frameNumber);return spentMs;}

        void Sync(int frameNumber){if(frameNumber!=frame){frame=frameNumber;spentMs=0;}}
    }

    public static class StreamingBudget
    {
        public const double DefaultPoiBudgetMs=3.0;

        // Kill switch: TLD_MP_STREAM_BUDGET=0 restores the synchronous native path.
        // Unset or any other value keeps budgeting on.
        public static bool EnabledFrom(string value){return value==null || value.Trim()!="0";}

        public static double ElapsedMs(long startTicks,long endTicks,long frequency)
        {
            if(frequency<=0 || endTicks<startTicks)return 0;
            return (endTicks-startTicks)*1000.0/frequency;
        }
    }
}
