// Test-only Steam API boundary. Production SteamSdrTransport is compiled unchanged
// against this implementation. It models callback direction, message ownership,
// acceptance, lane setup, and temporary send refusal; it is not Valve's network.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace Steamworks
{
    public struct HSteamNetConnection { public uint m_HSteamNetConnection; public static readonly HSteamNetConnection Invalid=new HSteamNetConnection(); }
    public struct HSteamListenSocket { public uint m_HSteamListenSocket; public static readonly HSteamListenSocket Invalid=new HSteamListenSocket(); }
    public struct SteamNetworkingIdentity { ulong id; public void SetSteamID64(ulong value){id=value;} public ulong GetSteamID64(){return id;} }
    public enum EResult { k_EResultOK=1, Failed=2 }
    public enum ESteamNetworkingConnectionState { k_ESteamNetworkingConnectionState_Connecting=1,k_ESteamNetworkingConnectionState_Connected=3,k_ESteamNetworkingConnectionState_ClosedByPeer=4,k_ESteamNetworkingConnectionState_ProblemDetectedLocally=5,k_ESteamNetworkingConnectionState_Dead=-3 }
    public struct SteamNetConnectionInfo_t { public SteamNetworkingIdentity m_identityRemote; public ESteamNetworkingConnectionState m_eState; public HSteamListenSocket m_hListenSocket; public int m_eEndReason; public string m_szEndDebug; }
    public struct SteamNetConnectionStatusChangedCallback_t { public HSteamNetConnection m_hConn; public SteamNetConnectionInfo_t m_info; }
    public sealed class Callback<T>:IDisposable
    {
        internal Action<T> Handler; internal ulong Owner;
        public static Callback<T> Create(Action<T> handler){var c=new Callback<T>{Handler=handler,Owner=Simulation.Current};Simulation.Callbacks.Add(c);return c;}
        public void Dispose(){Simulation.Callbacks.Remove(this);}
    }
    public static class Constants { public const int k_cbMaxSteamNetworkingSocketsMessageSizeSend=524288,k_nSteamNetworkingSend_Reliable=8,k_nSteamNetworkingSend_UnreliableNoNagle=1,k_nSteamNetworkingSend_Unreliable=0; }
    [StructLayout(LayoutKind.Sequential)]
    public struct SteamNetworkingMessage_t
    {
        public IntPtr m_pData; public HSteamNetConnection m_conn; public int m_cbSize,m_nFlags; public ushort m_idxLane;
        public static SteamNetworkingMessage_t FromIntPtr(IntPtr p){return (SteamNetworkingMessage_t)Marshal.PtrToStructure(p,typeof(SteamNetworkingMessage_t));}
        public static void Release(IntPtr p){var m=FromIntPtr(p);Marshal.FreeHGlobal(m.m_pData);Marshal.FreeHGlobal(p);Simulation.Allocations--;}
    }
    public static class SteamNetworkingUtils
    {
        public static IntPtr AllocateMessage(int size){var p=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SteamNetworkingMessage_t)));Marshal.StructureToPtr(new SteamNetworkingMessage_t{m_pData=Marshal.AllocHGlobal(size),m_cbSize=size},p,false);Simulation.Allocations++;return p;}
    }
    public static class Simulation
    {
        public sealed class Socket {public ulong Owner,Remote;public uint Other,Listener;public bool Incoming,Connected,Closed,Configured;public Queue<IntPtr> Messages=new Queue<IntPtr>();}
        public static bool MissingLaneApi; public static int LegacySends; public static ulong Current; public static int Allocations,AcceptCalls,InvalidAccepts,RefuseSends; public static uint Next=1;
        public static readonly List<object> Callbacks=new List<object>();
        public static readonly Dictionary<uint,Socket> Sockets=new Dictionary<uint,Socket>();
        public static readonly Dictionary<ulong,uint> Listeners=new Dictionary<ulong,uint>();
        static readonly Queue<Action> Events=new Queue<Action>();
        public static void Status(uint handle,ESteamNetworkingConnectionState state)
        {
            var s=Sockets[handle];Events.Enqueue(delegate {
                var identity=new SteamNetworkingIdentity();identity.SetSteamID64(s.Remote);
                var change=new SteamNetConnectionStatusChangedCallback_t{m_hConn=new HSteamNetConnection{m_HSteamNetConnection=handle},m_info=new SteamNetConnectionInfo_t{m_identityRemote=identity,m_eState=state,m_hListenSocket=new HSteamListenSocket{m_HSteamListenSocket=s.Listener}}};
                foreach(var item in Callbacks.ToArray()){var c=item as Callback<SteamNetConnectionStatusChangedCallback_t>;if(c!=null && c.Owner==s.Owner){Current=s.Owner;c.Handler(change);}}
            });
        }
        public static void Pump(){int guard=0;while(Events.Count>0){if(++guard>1000)throw new Exception("Callback loop");Events.Dequeue()();}}
    }
    public static class SteamNetworkingSockets
    {
        public static HSteamListenSocket CreateListenSocketP2P(int port,int count,object options){uint id=Simulation.Next++;Simulation.Listeners[Simulation.Current]=id;return new HSteamListenSocket{m_HSteamListenSocket=id};}
        public static HSteamNetConnection ConnectP2P(ref SteamNetworkingIdentity identity,int port,int count,object options)
        {
            uint a=Simulation.Next++,b=Simulation.Next++;ulong remote=identity.GetSteamID64();uint listener;
            if(!Simulation.Listeners.TryGetValue(remote,out listener))return HSteamNetConnection.Invalid;
            Simulation.Sockets[a]=new Simulation.Socket{Owner=Simulation.Current,Remote=remote,Other=b};
            Simulation.Sockets[b]=new Simulation.Socket{Owner=remote,Remote=Simulation.Current,Other=a,Incoming=true,Listener=listener};
            Simulation.Status(a,ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting);
            Simulation.Status(b,ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting);
            return new HSteamNetConnection{m_HSteamNetConnection=a};
        }
        public static EResult AcceptConnection(HSteamNetConnection connection)
        {
            Simulation.AcceptCalls++;var s=Simulation.Sockets[connection.m_HSteamNetConnection];
            if(!s.Incoming || s.Owner!=Simulation.Current){Simulation.InvalidAccepts++;return EResult.Failed;}
            s.Connected=true;Simulation.Sockets[s.Other].Connected=true;
            Simulation.Status(connection.m_HSteamNetConnection,ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected);Simulation.Status(s.Other,ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected);return EResult.k_EResultOK;
        }
        public static EResult ConfigureConnectionLanes(HSteamNetConnection c,int count,int[] priorities,ushort[] weights){if(Simulation.MissingLaneApi)throw new EntryPointNotFoundException("ConfigureConnectionLanes");Simulation.Sockets[c.m_HSteamNetConnection].Configured=true;return EResult.k_EResultOK;}
        public static EResult SendMessageToConnection(HSteamNetConnection c,IntPtr data,uint length,int flags,out long number)
        {
            Simulation.LegacySends++;number=0;var s=Simulation.Sockets[c.m_HSteamNetConnection];
            if(Simulation.RefuseSends>0){Simulation.RefuseSends--;return EResult.Failed;}
            if(!s.Connected || s.Closed)return EResult.Failed;
            IntPtr p=SteamNetworkingUtils.AllocateMessage((int)length);var m=SteamNetworkingMessage_t.FromIntPtr(p);
            var bytes=new byte[length];Marshal.Copy(data,bytes,0,(int)length);Marshal.Copy(bytes,0,m.m_pData,(int)length);
            m.m_conn=c;m.m_nFlags=flags;Marshal.StructureToPtr(m,p,false);Simulation.Sockets[s.Other].Messages.Enqueue(p);number=1;return EResult.k_EResultOK;
        }
        public static void SendMessages(int count,IntPtr[] messages,long[] results)
        {
            for(int i=0;i<count;i++){
                var m=SteamNetworkingMessage_t.FromIntPtr(messages[i]);var s=Simulation.Sockets[m.m_conn.m_HSteamNetConnection];
                if(Simulation.RefuseSends>0){Simulation.RefuseSends--;results[i]=-1;SteamNetworkingMessage_t.Release(messages[i]);continue;}
                if(!s.Connected || s.Closed || !s.Configured){results[i]=-2;SteamNetworkingMessage_t.Release(messages[i]);continue;}
                var other=Simulation.Sockets[s.Other];other.Messages.Enqueue(messages[i]);results[i]=1;
            }
        }
        public static int ReceiveMessagesOnConnection(HSteamNetConnection c,IntPtr[] messages,int max){var s=Simulation.Sockets[c.m_HSteamNetConnection];if(s.Messages.Count==0)return 0;messages[0]=s.Messages.Dequeue();return 1;}
        public static bool CloseConnection(HSteamNetConnection c,int reason,string detail,bool linger){var s=Simulation.Sockets[c.m_HSteamNetConnection];s.Closed=true;while(s.Messages.Count>0)SteamNetworkingMessage_t.Release(s.Messages.Dequeue());return true;}
        public static bool CloseListenSocket(HSteamListenSocket c){return true;}
    }
}
