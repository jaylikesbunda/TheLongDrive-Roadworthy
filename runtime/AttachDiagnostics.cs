using System;

namespace TldMp
{
    // Diagnostics only: describe native attachable (type 42) traffic and the
    // resulting local mount. Slot names on both machines expose a slot index that
    // means different things on host and guest. Nothing here changes a packet.
    public static partial class StableGameBridge
    {
        // attachable: uint id, int attachType; when attached also int indexType,
        // uint parent, int poigenid, Vector3d poiid, int index, ... (index at byte 45).
        static void NoteAttachablePacket(string direction,byte[] b)
        {
            if(!DiagnosticMode || b==null || b.Length<9 || b[0]!=(byte)msgType.attachable)return;
            try
            {
                uint id=BitConverter.ToUInt32(b,1);int attachType=BitConverter.ToInt32(b,5);
                string text="native-attach "+direction+" id="+id+" item="+AttachItemName(id)+" attachType="+attachType;
                if(attachType!=0 && b.Length>=49)
                {
                    int indexType=BitConverter.ToInt32(b,9);uint parent=BitConverter.ToUInt32(b,13);int index=BitConverter.ToInt32(b,45);
                    text+=" indexType="+indexType+" parent="+parent+" parentItem="+AttachItemName(parent)+" index="+index+" localSlot="+AttachSlotName(parent,indexType,index);
                }
                else text+=" detach";
                DiagLimited("native-attach:"+direction+":"+id,text,.25);
            }
            catch(Exception e){DiagLimited("native-attach-error","native-attach diagnostic failed: "+e.GetType().Name,5);}
        }

        // Called when a wrapped native attachment retry loop ends.
        static void NoteAttachOutcome(uint id)
        {
            if(!DiagnosticMode)return;
            try
            {
                tosaveitemscript item;
                if(!Find(id,out item)){DiagLimited("native-attach-done:"+id,"native-attach done id="+id+" item=missing",.25);return;}
                var attachable=item.attachable;
                bool attached=attachable!=null && attachable.attached;
                string slot=attachable==null || attachable.slot==null?"none":attachable.slot.name;
                string parent="none";
                if(attachable!=null && attachable.attached){var owner=attachable.AttachedToTosave();if(owner!=null)parent=owner.idInSave+":"+owner.name;}
                DiagLimited("native-attach-done:"+id,"native-attach done id="+id+" item="+item.name+" attached="+attached+" slot="+slot+" parent="+parent,.25);
            }
            catch(Exception e){DiagLimited("native-attach-error","native-attach diagnostic failed: "+e.GetType().Name,5);}
        }

        static string AttachItemName(uint id)
        {
            tosaveitemscript item;
            return Find(id,out item)?item.name:"missing";
        }

        static string AttachSlotName(uint parent,int indexType,int index)
        {
            if(indexType!=2)return "n/a";
            tosaveitemscript item;
            if(!Find(parent,out item))return "parent-missing";
            if(item.partslotscripts==null)return "no-slots";
            if(index<0 || index>=item.partslotscripts.Count)return "out-of-range/"+item.partslotscripts.Count;
            var slot=item.partslotscripts[index];
            return (slot==null?"null":slot.name)+"/"+item.partslotscripts.Count;
        }
    }
}
