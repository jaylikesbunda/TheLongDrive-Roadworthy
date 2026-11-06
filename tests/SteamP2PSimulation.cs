// Test-only ISteamNetworking v006 boundary for SteamP2PTransport. Compiled together
// with SteamSocketSimulation.cs (which provides Callback<T> and Simulation.Current).
// Models Steam's session rules: a sender implicitly accepts its target; a receiver's
// packets are held until it calls AcceptP2PSessionWithUser, after a
// P2PSessionRequest_t callback. It is not Valve's network.
using System;
using System.Collections.Generic;
namespace Steamworks
{
    public struct CSteamID
    {
        public ulong m_SteamID;
        public CSteamID(ulong id){m_SteamID=id;}
        public static explicit operator ulong(CSteamID id){return id.m_SteamID;}
    }
    public enum EP2PSend { k_EP2PSendUnreliable=0,k_EP2PSendUnreliableNoDelay=1,k_EP2PSendReliable=2,k_EP2PSendReliableWithBuffering=3 }
    public struct P2PSessionRequest_t { public CSteamID m_steamIDRemote; }
    public struct P2PSessionConnectFail_t { public CSteamID m_steamIDRemote; public byte m_eP2PSessionError; }
    public struct P2PSessionState_t
    {
        public byte m_bConnectionActive,m_bConnecting,m_eP2PSessionError,m_bUsingRelay;
        public int m_nBytesQueuedForSend,m_nPacketsQueuedForSend;public uint m_nRemoteIP;public ushort m_nRemotePort;
    }
    public static class P2PSimulation
    {
        public sealed class Packet { public ulong From; public byte[] Data; public int Channel; public EP2PSend Mode; }
        static readonly Dictionary<string,Queue<Packet>> inbox=new Dictionary<string,Queue<Packet>>();
        static readonly Dictionary<string,List<Packet>> held=new Dictionary<string,List<Packet>>();
        static readonly HashSet<string> accepted=new HashSet<string>(),requested=new HashSet<string>();
        static readonly Queue<Action> events=new Queue<Action>();
        public static int Requests,ReliableSends,UnreliableSends,Oversized;
        public static readonly HashSet<ulong> Offline=new HashSet<ulong>();
        static string Key(ulong a,ulong b){return a+">"+b;}
        static string Box(ulong owner,int channel){return owner+"#"+channel;}
        public static void Reset()
        {inbox.Clear();held.Clear();accepted.Clear();requested.Clear();events.Clear();Offline.Clear();Requests=ReliableSends=UnreliableSends=Oversized=0;}
        internal static bool Send(ulong from,ulong to,byte[] data,uint length,EP2PSend mode,int channel)
        {
            if(to==0 || length==0 || length>data.Length)return false;
            bool reliable=mode==EP2PSend.k_EP2PSendReliable || mode==EP2PSend.k_EP2PSendReliableWithBuffering;
            if(!reliable && length>1200){Oversized++;return false;}
            if(reliable)ReliableSends++;else UnreliableSends++;
            accepted.Add(Key(from,to)); // sending to someone accepts their replies
            if(Offline.Contains(to) || Offline.Contains(from))return true; // admitted, never delivered
            var copy=new byte[length];Buffer.BlockCopy(data,0,copy,0,(int)length);
            var packet=new Packet{From=from,Data=copy,Channel=channel,Mode=mode};
            if(accepted.Contains(Key(to,from))){Queue(to).Enqueue(packet);return true;}
            List<Packet> list;if(!held.TryGetValue(Key(to,from),out list)){list=new List<Packet>();held[Key(to,from)]=list;}
            list.Add(packet);
            if(requested.Add(Key(to,from)))
            {
                Requests++;
                events.Enqueue(delegate{
                    foreach(var item in Simulation.Callbacks.ToArray())
                    {var c=item as Callback<P2PSessionRequest_t>;if(c!=null && c.Owner==to){Simulation.Current=to;c.Handler(new P2PSessionRequest_t{m_steamIDRemote=new CSteamID(from)});}}
                });
            }
            return true;
        }
        static Queue<Packet> Queue(ulong owner){return QueueFor(owner);}
        static Queue<Packet> QueueFor(ulong owner)
        {Queue<Packet> q;if(!inbox.TryGetValue(owner+"",out q)){q=new Queue<Packet>();inbox[owner+""]=q;}return q;}
        internal static bool Accept(ulong owner,ulong remote)
        {
            accepted.Add(Key(owner,remote));requested.Remove(Key(owner,remote));
            List<Packet> list;if(held.TryGetValue(Key(owner,remote),out list)){foreach(var p in list)QueueFor(owner).Enqueue(p);held.Remove(Key(owner,remote));}
            return true;
        }
        internal static Packet Peek(ulong owner,int channel){foreach(var p in QueueFor(owner))if(p.Channel==channel)return p;return null;}
        internal static Packet Take(ulong owner,int channel)
        {
            var q=QueueFor(owner);Packet found=null;var rest=new List<Packet>();
            while(q.Count>0){var p=q.Dequeue();if(found==null && p.Channel==channel)found=p;else rest.Add(p);}
            foreach(var p in rest)q.Enqueue(p);return found;
        }
        public static void Pump(){int guard=0;while(events.Count>0){if(++guard>1000)throw new Exception("P2P callback loop");events.Dequeue()();}}
        public static int Pending(ulong owner){return QueueFor(owner).Count;}
    }
    public static class SteamNetworking
    {
        public static bool SendP2PPacket(CSteamID steamIDRemote,byte[] pubData,uint cubData,EP2PSend eP2PSendType,int nChannel=0)
        {return P2PSimulation.Send(Simulation.Current,(ulong)steamIDRemote,pubData,cubData,eP2PSendType,nChannel);}
        public static bool IsP2PPacketAvailable(out uint pcubMsgSize,int nChannel=0)
        {var p=P2PSimulation.Peek(Simulation.Current,nChannel);pcubMsgSize=p==null?0u:(uint)p.Data.Length;return p!=null;}
        public static bool ReadP2PPacket(byte[] pubDest,uint cubDest,out uint pcubMsgSize,out CSteamID psteamIDRemote,int nChannel=0)
        {
            var p=P2PSimulation.Take(Simulation.Current,nChannel);
            if(p==null){pcubMsgSize=0;psteamIDRemote=new CSteamID(0);return false;}
            uint n=(uint)Math.Min(p.Data.Length,(int)cubDest);Buffer.BlockCopy(p.Data,0,pubDest,0,(int)n);
            pcubMsgSize=n;psteamIDRemote=new CSteamID(p.From);return true;
        }
        public static bool AcceptP2PSessionWithUser(CSteamID steamIDRemote){return P2PSimulation.Accept(Simulation.Current,(ulong)steamIDRemote);}
        public static bool GetP2PSessionState(CSteamID steamIDRemote,out P2PSessionState_t state)
        {state=new P2PSessionState_t{m_bConnectionActive=1};return true;}
    }
}
