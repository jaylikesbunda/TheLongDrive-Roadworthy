using System;
using System.Collections.Generic;

namespace TldMp
{
    public enum NetDeliveryLane { Critical=0, Motion=1, Bulk=2 }

    // Bounded per-peer queues for independent delivery classes. Reliable FIFO
    // is preserved within each lane; callers keep protocol dependencies in
    // Critical. Unreliable state is latest-value coalesced by caller key.
    public sealed class NetDeliveryScheduler
    {
        sealed class Entry { public byte[][] Packets;public int Next;public ulong Key;public bool Reliable;public Entry Successor; }
        sealed class LaneState { public readonly Queue<Entry> Reliable=new Queue<Entry>(); public readonly Dictionary<ulong,Entry> Latest=new Dictionary<ulong,Entry>(); public readonly Queue<ulong> Order=new Queue<ulong>(); public int Bytes; }
        sealed class PeerState { public readonly LaneState[] Lanes={new LaneState(),new LaneState(),new LaneState()}; public int Bytes,Entries,Cursor; }
        readonly Dictionary<ulong,PeerState> peers=new Dictionary<ulong,PeerState>();
        readonly int peerBytes,peerEntries,packetBytes;
        public NetDeliveryScheduler(int peerBytes=2*1024*1024,int peerEntries=2048,int packetBytes=64*1024)
        {if(peerBytes<=0 || peerEntries<=0 || packetBytes<=0)throw new ArgumentOutOfRangeException("Invalid network queue bounds.");this.peerBytes=peerBytes;this.peerEntries=peerEntries;this.packetBytes=packetBytes;}
        // Unsent unreliable frames replaced by a newer frame with the same key.
        public long Coalesced;
        public int PeerCount { get { return peers.Count; } }
        public ulong[] Peers { get { var result=new ulong[peers.Count];peers.Keys.CopyTo(result,0);return result; } }
        public static ulong ItemStreamKey(byte packetType,uint itemId)
        {return itemId==0?0:0x1000000000000000UL|((ulong)packetType<<32)|itemId;}
        public int PendingBytes(ulong peer){PeerState p;return peers.TryGetValue(peer,out p)?p.Bytes:0;}
        public int PendingCount(ulong peer){PeerState p;return peers.TryGetValue(peer,out p)?p.Entries:0;}

        public bool Enqueue(ulong peer,NetDeliveryLane lane,bool reliable,ulong coalesceKey,byte[] data)
        {return EnqueueGroup(peer,lane,reliable,coalesceKey,new[]{data});}

        // Reserves every packet/byte of one immutable frame before making any
        // fragment visible to Drain. A partially sent frame is completed before
        // its replaceable successor is promoted. Only the unsent whole frame can
        // be replaced, so queue pressure cannot strand sibling fragments.
        public bool EnqueueGroup(ulong peer,NetDeliveryLane lane,bool reliable,ulong coalesceKey,byte[][] packets)
        {
            if(peer==0||packets==null||packets.Length==0||(int)lane<0||(int)lane>2||reliable&&packets.Length>1)return false;
            int groupBytes=0;for(int i=0;i<packets.Length;i++)
            {if(packets[i]==null||packets[i].Length==0||packets[i].Length>packetBytes||groupBytes>Int32.MaxValue-packets[i].Length)return false;groupBytes+=packets[i].Length;}
            PeerState p;if(!peers.TryGetValue(peer,out p))peers.Add(peer,p=new PeerState());LaneState l=p.Lanes[(int)lane];
            Entry prior=null;if(!reliable&&coalesceKey!=0)l.Latest.TryGetValue(coalesceKey,out prior);
            if(prior!=null)
            {
                Coalesced++;
                if(prior.Next==0)
                {
                    int priorBytes=RemainingBytes(prior),priorEntries=RemainingEntries(prior),delta=groupBytes-priorBytes,entryDelta=packets.Length-priorEntries;
                    if(delta>peerBytes-p.Bytes||entryDelta>peerEntries-p.Entries)return false;
                    p.Bytes+=delta;p.Entries+=entryDelta;l.Bytes+=delta;prior.Packets=ClonePackets(packets);prior.Next=0;prior.Successor=null;prior.Key=coalesceKey;return true;
                }
                // Keep the frame already partly accepted by transport immutable;
                // hold only the newest complete successor behind it.
                int oldBytes=prior.Successor==null?0:RemainingBytes(prior.Successor),oldEntries=prior.Successor==null?0:RemainingEntries(prior.Successor);
                int successorDelta=groupBytes-oldBytes,successorEntryDelta=packets.Length-oldEntries;
                if(successorDelta>peerBytes-p.Bytes||successorEntryDelta>peerEntries-p.Entries)return false;
                p.Bytes+=successorDelta;p.Entries+=successorEntryDelta;l.Bytes+=successorDelta;
                prior.Successor=new Entry{Packets=ClonePackets(packets),Key=coalesceKey,Reliable=false};return true;
            }
            if(p.Entries>peerEntries-packets.Length||groupBytes>peerBytes-p.Bytes)return false;
            var e=new Entry{Packets=ClonePackets(packets),Key=coalesceKey,Reliable=reliable};
            if(reliable)l.Reliable.Enqueue(e);
            else if(coalesceKey!=0){l.Latest.Add(coalesceKey,e);l.Order.Enqueue(coalesceKey);}
            else {e.Key=AllocateUnkeyed(l,e);l.Latest.Add(e.Key,e);l.Order.Enqueue(e.Key);}
            p.Entries+=packets.Length;p.Bytes+=groupBytes;l.Bytes+=groupBytes;return true;
        }
        static byte[][] ClonePackets(byte[][] packets)
        {var copy=new byte[packets.Length][];for(int i=0;i<packets.Length;i++)copy[i]=(byte[])packets[i].Clone();return copy;}
        static int RemainingEntries(Entry e){return e==null||e.Packets==null?0:e.Packets.Length-e.Next;}
        static int RemainingBytes(Entry e){if(e==null||e.Packets==null)return 0;int n=0;for(int i=e.Next;i<e.Packets.Length;i++)n+=e.Packets[i].Length;return n;}

        // Unkeyed unreliable packets receive private keys from the top of the
        // UInt64 space. Collision is checked against the bounded live map.
        ulong nextUnkeyed=UInt64.MaxValue;
        ulong AllocateUnkeyed(LaneState lane,Entry entry)
        {while(nextUnkeyed==0||lane.Latest.ContainsKey(nextUnkeyed))nextUnkeyed--;entry.Key=nextUnkeyed--;return entry.Key;}

        // Independent lane heads progress separately: a blocked reliable
        // critical packet never suppresses motion or independent bulk traffic.
        // A refusal leaves that lane head queued and stops only that lane.
        public int Drain(ulong peer,int maxPackets,int maxBytes,Func<ulong,NetDeliveryLane,byte[],bool,bool> send)
        {
            if(maxPackets<=0||maxBytes<=0||send==null)return 0;PeerState p;if(!peers.TryGetValue(peer,out p))return 0;
            int sent=0,bytes=0,blockedMask=0;
            while(sent<maxPackets)
            {
                bool progressed=false;
                for(int n=0;n<3&&sent<maxPackets;n++)
                {
                    int laneIndex=(p.Cursor+n)%3;if((blockedMask&(1<<laneIndex))!=0)continue;
                    LaneState l=p.Lanes[laneIndex];Entry e;bool reliable;
                    if(l.Reliable.Count>0){e=l.Reliable.Peek();reliable=true;}
                    else {e=PeekLatest(l);reliable=false;}
                    if(e==null){blockedMask|=1<<laneIndex;continue;}
                    byte[] packet=e.Packets[e.Next];if(packet.Length>maxBytes-bytes){blockedMask|=1<<laneIndex;continue;}
                    if(!send(peer,(NetDeliveryLane)laneIndex,packet,reliable)){blockedMask|=1<<laneIndex;continue;}
                    e.Next++;p.Bytes-=packet.Length;l.Bytes-=packet.Length;p.Entries--;sent++;bytes+=packet.Length;p.Cursor=(laneIndex+1)%3;progressed=true;
                    if(e.Next==e.Packets.Length)
                    {
                        if(reliable)l.Reliable.Dequeue();
                        else if(e.Successor!=null)
                        {e.Packets=e.Successor.Packets;e.Next=0;e.Successor=null;RotateLatest(l,e.Key);}
                        else RemoveLatest(l,e.Key);
                    }
                    break;
                }
                if(!progressed)break;
            }
            if(p.Entries==0)peers.Remove(peer);return sent;
        }
        Entry PeekLatest(LaneState l)
        {while(l.Order.Count>0){ulong key=l.Order.Peek();Entry e;if(l.Latest.TryGetValue(key,out e))return e;l.Order.Dequeue();}return null;}
        void RemoveLatest(LaneState l,ulong key)
        {if(!l.Latest.Remove(key))return;if(l.Order.Count>0&&l.Order.Peek()==key)l.Order.Dequeue();else RemoveQueuedKey(l.Order,key);}
        void RotateLatest(LaneState l,ulong key)
        {if(l.Order.Count>0&&l.Order.Peek()==key)l.Order.Dequeue();else RemoveQueuedKey(l.Order,key);l.Order.Enqueue(key);}
        static void RemoveQueuedKey(Queue<ulong> q,ulong key)
        {int n=q.Count;for(int i=0;i<n;i++){ulong x=q.Dequeue();if(x!=key)q.Enqueue(x);}}
        public void Drop(ulong peer){peers.Remove(peer);}
        public void Clear(){peers.Clear();nextUnkeyed=UInt64.MaxValue;}
    }
}
