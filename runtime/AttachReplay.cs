using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using GeneszSteamP2PBase;

namespace TldMp
{
    // Replays native per-item state messages that arrived before their item or
    // parent existed (see PendingItemQueue). Replays go back through the stock
    // networkingScript.ProcessMessage, so every incoming gate and native receiver
    // runs exactly as for a live message.
    public static partial class StableGameBridge
    {
        static readonly PendingItemQueue pendingItems=new PendingItemQueue();
        static readonly Func<uint,bool> heldItemExists=HeldItemExists;
        static bool replayingHeld;
        static bool HeldItemExists(uint id){tosaveitemscript item;return Find(id,out item);}

        static void NotePendingItem(CSteamID sender,byte[] bytes)
        {
            if(replayingHeld || bytes==null || bytes.Length<5 || !PendingItemQueue.Holdable(bytes[0]))return;
            uint id=StablePackets.Item(bytes);if(id==0)return;
            uint parent=PendingItemQueue.Parent(bytes);
            if(HeldItemExists(id) && (parent==0 || HeldItemExists(parent)))
            {
                // Older held state for this item goes first; the stock receiver
                // then applies this newer message as usual.
                if(pendingItems.Has(id))ReplayHeld(pendingItems.Take(id));
                return;
            }
            if(pendingItems.Hold(id,parent,(ulong)sender,bytes,Now))
                DiagLimited("item-held:"+id,"native-item held id="+id+" type="+bytes[0]+" parent="+parent+" pending="+pendingItems.Count,.25);
        }

        static void ReplayPendingItems()
        {
            if(pendingItems.Items==0 || networkingScript.s==null)return;
            ReplayHeld(pendingItems.TakeReady(heldItemExists,Now));
        }

        static void ReplayHeld(List<PendingItemQueue.Held> held)
        {
            if(held==null || networkingScript.s==null)return;
            replayingHeld=true;
            try
            {
                foreach(var message in held)
                {
                    byte type=message.Bytes[0];uint id=StablePackets.Item(message.Bytes);
                    // A native retry or item resync may already have mounted the part.
                    tosaveitemscript item;
                    if(type==42 && BitConverter.ToInt32(message.Bytes,5)!=0 && Find(id,out item) && item.attachable!=null && item.attachable.attached)continue;
                    try
                    {
                        networkingScript.s.ProcessMessage(new CSteamID(message.Sender),message.Bytes,(msgType)type);
                        DiagLimited("item-replayed:"+id+":"+type,"native-item replayed id="+id+" type="+type,.25);
                    }
                    catch(Exception e){Debug.LogWarning("[TLD MP v3] Held item message replay failed for "+id+" type "+type+": "+e.Message);}
                }
            }
            finally{replayingHeld=false;}
        }
    }
}
