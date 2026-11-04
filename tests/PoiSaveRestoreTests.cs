using System;
using System.Collections.Generic;
using TldMp;

public static class PoiSaveRestoreTests
{
    static void Check(bool condition,string message)
    {
        if(!condition)throw new Exception(message);
    }

    public static void Main()
    {
        var first=new save_poi();
        var second=new save_poi();
        var save=new savedatascript();
        save.data.pois=new List<save_poi>{first,null,second,null};
        StableGameBridge.RemoveNullSavedBuildings(save);
        Check(save.data.pois.Count==2 && save.data.pois[0]==first && save.data.pois[1]==second,"valid records keep their order and identity");
        StableGameBridge.RemoveNullSavedBuildings(save);
        save.data.pois=null;
        StableGameBridge.RemoveNullSavedBuildings(save);
        Check(UnityEngine.Debug.Warnings==1,"empty records produce one warning for the save");

        var control=new usablescript();
        var savedControl=new save_usablescript();
        StableGameBridge.RestoreBuildingUsable(savedControl,control);
        StableGameBridge.RestoreBuildingUsable(null,control);
        StableGameBridge.RestoreBuildingUsable(savedControl,null);
        Check(control.Applied==1,"healthy controls use the native loader, missing components are skipped");
        savedControl.Failure=new InvalidOperationException("control unavailable");
        StableGameBridge.RestoreBuildingUsable(savedControl,control);
        StableGameBridge.RestoreBuildingUsable(new save_usablescript(),control);
        Check(control.Applied==2 && UnityEngine.Debug.Warnings==2,"a failed control does not prevent the next control restoring");

        var tank=new tankscript{Amount=42};
        StableGameBridge.RestoreBuildingTank(new save_tankscript{amount=new[]{3f},type=null},tank);
        StableGameBridge.RestoreBuildingTank(new save_tankscript{amount=new[]{3f,4f},type=new[]{0}},tank);
        Check(tank.Amount==42 && tank.Applied==0,"incomplete fluid arrays never reach the loader that clears the live tank");
        StableGameBridge.RestoreBuildingTank(new save_tankscript{amount=new[]{3f},type=new[]{0,1}},tank);
        Check(tank.Amount==3 && tank.Applied==1,"healthy fluid arrays keep native behavior, including extra unused types");
        StableGameBridge.RestoreBuildingTank(new save_tankscript(),tank);
        Check(tank.Amount==0 && tank.Applied==2,"an empty saved tank still clears the live tank");

        var unexpected=new NotSupportedException("unexpected runtime failure");
        try
        {
            StableGameBridge.RestoreBuildingUsable(new save_usablescript{Failure=unexpected},control);
            throw new Exception("unexpected errors must remain visible");
        }
        catch(NotSupportedException error){Check(error==unexpected,"unexpected error preserved");}
        Console.WriteLine("PASS: building restore preserves healthy records and isolates missing or malformed components.");
    }
}

namespace UnityEngine
{
    public class MissingReferenceException : Exception {}
    public static class Debug
    {
        public static int Warnings;
        public static void LogWarning(object message){Warnings++;}
    }
}

public sealed class savedatascript {public readonly savedata data=new savedata();}
public sealed class savedata {public List<save_poi> pois;}
public sealed class save_poi {}
public sealed class usablescript {public int Applied;}
public sealed class tankscript {public int Applied;public float Amount;}
public sealed class save_usablescript
{
    public int ind;
    public Exception Failure;
    public void Load(usablescript target)
    {
        if(Failure!=null)throw Failure;
        target.Applied++;
    }
}
public sealed class save_tankscript
{
    public int ind;
    public float[] amount;
    public int[] type;
    public void Load(tankscript target)
    {
        target.Amount=0;
        if(amount!=null)foreach(float value in amount)target.Amount+=value;
        target.Applied++;
    }
}
