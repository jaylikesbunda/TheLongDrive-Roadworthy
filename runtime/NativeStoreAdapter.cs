using UnityEngine;

namespace TldMp
{
    // Guest auto-store gate and storage transition diagnostics. Only the single
    // Store call inside mountStuff.FreezeUpd is routed here; Store, Unstore and
    // every other Store caller keep their stock bodies.
    public static partial class StableGameBridge
    {
        static bool nativeAutoStoreScan;

        static uint NativeStoreParent(mountStuff mount)
        {return mount==null || mount.parentTosave==null?0:mount.parentTosave.idInSave;}

        // Shared item/parent description: name, parent name, position in the
        // parent's space and world position. All reads are null-safe.
        static string NativeStoreItemText(pickupable item,mountStuff mount)
        {
            Transform parent=mount==null?null:mount.transform;
            Vector3 world=item==null?Vector3.zero:item.transform.position;
            return " name="+NativeStoreText.Name(item==null?null:item.name)+
                " parentName="+NativeStoreText.Name(mount==null?null:mount.name)+
                " local="+NativeStoreText.Local(parent,world)+
                " world="+NativeStoreText.Position(world);
        }

        public static void AutoStoreGate(mountStuff mount,pickupable item,bool ignoreRigidbody)
        {
            tosaveitemscript save=item==null?null:item.tosave;
            bool connected=syncScript.IsConnected();
            bool host=connected && syncScript.IsHost();
            bool inCharge=connected && !host && save!=null && save.ThisOneInCharge();
            if(!NativeStoreGate.AllowLocalAutoStore(connected,host,inCharge,false))
            {
                uint id=save==null?0:save.idInSave;
                DiagLimited("native-autostore-skipped:"+id,"native-autostore-skipped id="+id+" parent="+NativeStoreParent(mount)+
                    " index="+(mount==null?-1:mount.id)+NativeStoreItemText(item,mount),2);
                return;
            }
            nativeAutoStoreScan=true;
            try{mount.Store(item,ignoreRigidbody);}
            finally{nativeAutoStoreScan=false;}
        }

        // Entry observer on pickupable.PhysLock, which every native Store path calls.
        public static void NativeBeforePhysLock(pickupable item,mountStuff mount)
        {
            if(!DiagnosticMode || item==null || mount==null || item.physlock==mount)return;
            tosaveitemscript save=item.tosave;uint parent=NativeStoreParent(mount);
            bool net=save!=null && save.gotSyncLocal && save.gotSyncTime==Time.time && save.gotSyncLocalParentID==parent;
            NativeStoreSource source=NativeStoreGate.Classify(nativeAutoStoreScan,net);
            uint id=save==null?0:save.idInSave;
            bool connected=syncScript.IsConnected(),host=connected && syncScript.IsHost();
            DiagLimited("native-store:"+id,"native-store id="+id+" parent="+parent+" index="+mount.id+" source="+NativeStoreGate.Name(source)+
                " host="+host+" inCharge="+(save!=null && connected && save.ThisOneInCharge())+NativeStoreItemText(item,mount),.25);
        }

        // Entry observer on pickupable.JustUnPhysicsLock (UnPhysicsLock, Pick and
        // mountStuff.Unstore all release through it).
        public static void NativeBeforeUnPhysicsLock(pickupable item)
        {
            CarCargo.NoteReleased(item);
            if(!DiagnosticMode || item==null || item.physlock==null)return;
            tosaveitemscript save=item.tosave;uint id=save==null?0:save.idInSave;
            bool connected=syncScript.IsConnected(),host=connected && syncScript.IsHost();
            DiagLimited("native-unstore:"+id,"native-unstore id="+id+" parent="+NativeStoreParent(item.physlock)+" index="+item.physlock.id+
                " reason="+(item.pickedUp?"picked":"released")+" host="+host+" inCharge="+(save!=null && connected && save.ThisOneInCharge())+
                NativeStoreItemText(item,item.physlock),.25);
        }
    }
}
