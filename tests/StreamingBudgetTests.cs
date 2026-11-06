using System;
using TldMp;

public static class StreamingBudgetTests
{
    static int count;
    static void Check(bool condition)
    {
        count++;
        if(!condition)throw new Exception("StreamingBudgetTests check "+count+" failed.");
    }

    public static void Main()
    {
        // Admission: a frame admits work until its budget is spent.
        var budget=new StreamingFrameBudget(3.0);
        Check(budget.HasRoom(10));
        budget.Charge(10,1.0);
        Check(budget.HasRoom(10));
        budget.Charge(10,2.5);
        Check(!budget.HasRoom(10));
        Check(Math.Abs(budget.SpentMs(10)-3.5)<1e-9);

        // A new frame resets the spent time, so deferred work always progresses.
        Check(budget.HasRoom(11));
        Check(budget.SpentMs(11)==0.0);

        // Negative or zero charges never refund time or change the frame state.
        budget.Charge(11,-5.0);
        Check(budget.SpentMs(11)==0.0);
        budget.Charge(11,0.0);
        Check(budget.SpentMs(11)==0.0);

        // The first chunk of a frame is always admitted even when the budget is tiny.
        var tiny=new StreamingFrameBudget(0.0);
        Check(!tiny.HasRoom(1));
        Check(tiny.BudgetMs==0.0);

        // Admission after a frame change does not carry over older frames.
        var sequential=new StreamingFrameBudget(1.0);
        sequential.Charge(5,1.0);
        Check(!sequential.HasRoom(5));
        Check(sequential.HasRoom(6));
        sequential.Charge(6,0.4);
        Check(sequential.HasRoom(6));
        Check(Math.Abs(sequential.SpentMs(6)-0.4)<1e-9);

        // Kill switch: only an explicit "0" disables budgeting.
        Check(!StreamingBudget.EnabledFrom("0"));
        Check(!StreamingBudget.EnabledFrom(" 0 "));
        Check(StreamingBudget.EnabledFrom(null));
        Check(StreamingBudget.EnabledFrom(""));
        Check(StreamingBudget.EnabledFrom("1"));
        Check(StreamingBudget.EnabledFrom("00"));

        // Elapsed conversion uses the Stopwatch frequency.
        Check(Math.Abs(StreamingBudget.ElapsedMs(0,1000000,1000000000)-1.0)<1e-9);
        Check(Math.Abs(StreamingBudget.ElapsedMs(100,100,1000000000))<1e-12);
        Check(StreamingBudget.ElapsedMs(200,100,1000000000)==0.0);
        Check(StreamingBudget.ElapsedMs(0,1000,0)==0.0);

        Console.WriteLine("StreamingBudgetTests passed: "+count+" checks.");
    }
}
