using System;
using UnityEngine;

namespace TldMp
{
    public static partial class StableGameBridge
    {
        public static void RemoveNullSavedBuildings(savedatascript save)
        {
            var buildings=save.data.pois;
            if(buildings==null)return;
            int removed=buildings.RemoveAll(building=>building==null);
            if(removed>0)Debug.LogWarning("[Roadworthy save] Ignored "+removed+" empty building records.");
        }

        public static void RestoreBuildingUsable(save_usablescript saved,usablescript target)
        {
            if(saved==null || target==null)return;
            try{saved.Load(target);}
            catch(Exception error){if(!LogBuildingRestoreError("control",saved.ind,error))throw;}
        }

        public static void RestoreBuildingTank(save_tankscript saved,tankscript target)
        {
            if(saved==null || target==null)return;
            // The native loader clears the live tank before reading these arrays.
            if(saved.amount!=null && saved.amount.Length>0 && (saved.type==null || saved.type.Length<saved.amount.Length))
            {
                Debug.LogWarning("[Roadworthy save] Ignored incomplete building tank "+saved.ind+".");
                return;
            }
            try{saved.Load(target);}
            catch(Exception error){if(!LogBuildingRestoreError("tank",saved.ind,error))throw;}
        }

        static bool LogBuildingRestoreError(string component,int index,Exception error)
        {
            if(!(error is NullReferenceException || error is MissingReferenceException || error is IndexOutOfRangeException ||
                error is ArgumentException || error is InvalidOperationException))return false;
            Debug.LogWarning("[Roadworthy save] Could not restore building "+component+" "+index+". "+error);
            return true;
        }
    }
}
