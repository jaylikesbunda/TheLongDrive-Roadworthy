using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        static readonly ItemRecoveryRequests itemRecovery=new ItemRecoveryRequests();
        static readonly Func<uint,bool> heldItemExists=HeldItemExists;
        static readonly Action<PendingItemQueue.Held> replayOneHeld=ReplayOneHeld;
        static readonly Action<uint> watchMissingItem=WatchMissingItem;
        static readonly Action<uint> askMissingItem=AskMissingItem;
        static readonly Func<uint,bool> pendingItemBlocked=pendingItems.Has;
        static bool replayingHeld;
        static bool HeldItemExists(uint id){tosaveitemscript item;return Find(id,out item);}
        static void WatchMissingItem(uint id){itemRecovery.Watch(id,Now);}
        static void AskMissingItem(uint id){syncScript.s.SendAskItemData(id);}
        static long replayTicks,replayMaxTicks;
        static int replayDeferred;

        static string TakeItemReplayHealth()
        {
            string text="itemReplay{pending="+pendingItems.Count+" bytes="+pendingItems.Bytes+" oldestSeconds="+
                pendingItems.OldestAge(Now).ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                " heldTotal="+pendingItems.HeldCount+" replayedTotal="+pendingItems.Replayed+" expiredTotal="+pendingItems.Expired+
                " droppedTotal="+pendingItems.Dropped+" recoveryTracked="+itemRecovery.Count+" requestsTotal="+itemRecovery.Sent+
                " exhaustedTotal="+itemRecovery.Exhausted+" deferredFrames="+replayDeferred+" ms="+NetStats.Ms(replayTicks)+" maxMs="+NetStats.Ms(replayMaxTicks)+"}";
            replayTicks=replayMaxTicks=0;replayDeferred=0;return text;
        }

        // True means defer this state exclusively to the bounded replay path.
        // Do not also start a native attachment coroutine for the same message.
        static bool NotePendingItem(CSteamID sender,byte[] bytes)
        {
            if(replayingHeld || bytes==null || bytes.Length<5)return false;
            uint id=StablePackets.Item(bytes);if(id==0)return false;
            if(bytes[0]==36){pendingItems.Remove(id);itemRecovery.Remove(id,Now);return false;}
            if(itemRecovery.Deleted(id,Now))return true;
            if(!PendingItemQueue.Holdable(bytes[0]))return false;
            uint parent=PendingItemQueue.Parent(bytes);
            if(HeldItemExists(id) && (parent==0 || HeldItemExists(parent)) && !pendingItems.Has(id))return false;
            if(parent!=0 && itemRecovery.Deleted(parent,Now))return true;
            if(!HeldItemExists(id))WatchMissingItem(id);
            if(parent!=0 && !HeldItemExists(parent))WatchMissingItem(parent);
            if(pendingItems.Hold(id,parent,(ulong)sender,bytes,Now))
                DiagLimited("item-held:"+id,"native-item held id="+id+" type="+bytes[0]+" parent="+parent+" pending="+pendingItems.Count,.25);
            else itemRecovery.Refresh(id,Now);
            return true;
        }

        static void ReplayPendingItems()
        {
            if(networkingScript.s==null || syncScript.s==null || node==null || !node.Ready)return;
            long start=Stopwatch.GetTimestamp();
            // At most 32 handlers and 128 item probes, with a 2 ms soft budget.
            // One native handler can itself exceed the budget; its cost is logged.
            pendingItems.Drain(heldItemExists,Now,128,32,delegate{return (Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency<2;},replayOneHeld,watchMissingItem);
            if(syncScript.s.sentAskStartStuff)itemRecovery.Tick(heldItemExists,Now,2,askMissingItem,pendingItemBlocked);
            long ticks=Stopwatch.GetTimestamp()-start;replayTicks+=ticks;if(ticks>replayMaxTicks)replayMaxTicks=ticks;
            if(pendingItems.Count>0)replayDeferred++;
        }

        static void ReplayOneHeld(PendingItemQueue.Held message)
        {
            if(networkingScript.s==null)return;
            replayingHeld=true;
            int previousType=DispatchType;DispatchType=message.Bytes[0];
            long start=Stopwatch.GetTimestamp();
            try
            {
                    byte type=message.Bytes[0];uint id=StablePackets.Item(message.Bytes);
                    if(itemRecovery.Deleted(id,Now))return;
                    // Only an identical slot relationship is already satisfied.
                    // A different parent/slot must run native reconciliation.
                    tosaveitemscript item;
                    if(type==42 && Find(id,out item) && item.attachable!=null && item.attachable.attached)
                    {
                        var live=new save_attachable(item.attachable,id);
                        if(AttachmentReplayPolicy.SameSlot(message.Bytes,live.attachType,live.indexType,live.parentid,live.poigenid,live.poiid.x,live.poiid.y,live.poiid.z,live.index))return;
                        // Native Craft adds to the new slot but does not remove
                        // an old slot's membership. For a proven ready item slot,
                        // leave the old mount through the game's own detach path.
                        if(message.Bytes.Length>=49 && BitConverter.ToInt32(message.Bytes,5)==1 && BitConverter.ToInt32(message.Bytes,9)==2)
                        {
                            tosaveitemscript parent;int index=BitConverter.ToInt32(message.Bytes,45);
                            if(Find(BitConverter.ToUInt32(message.Bytes,13),out parent) && parent.partslotscripts!=null && index>=0 && index<parent.partslotscripts.Count && parent.partslotscripts[index]!=null)
                            {
                                var attachment=item.attachable;bool previous=attachment.dontSendMulti;attachment.dontSendMulti=true;
                                try
                                {
                                    if(attachment.slot!=null)
                                    {
                                        if(attachment.slot.part()!=attachment || !attachment.slot.UnCraft(false))
                                        {itemRecovery.Refresh(id,Now);return;}
                                    }
                                    else attachment.Detach();
                                }
                                finally{attachment.dontSendMulti=previous;}
                            }
                        }
                    }
                    networkingScript.s.ProcessMessage(new CSteamID(message.Sender),message.Bytes,(msgType)type);
                    DiagLimited("item-replayed:"+id+":"+type,"native-item replayed id="+id+" type="+type,.25);
            }
            catch(Exception e){UnityEngine.Debug.LogWarning("[TLD MP v3] Held item message replay failed: "+e.Message);}
            finally{NoteHandler(message.Bytes,Stopwatch.GetTimestamp()-start);DispatchType=previousType;replayingHeld=false;}
        }
    }
}
