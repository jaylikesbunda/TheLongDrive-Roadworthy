using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace TldMp
{
    // The game ships Steamworks.NET built for a newer Steam SDK than its
    // steam_api64.dll. Steamworks.NET asks the Steam client for
    // SteamNetworkingSockets012 / SteamNetworkingUtils004, but the DLL's flat
    // functions index the function tables of SteamNetworkingSockets008 /
    // SteamNetworkingUtils003 (the only versions named in the DLL). Calls whose
    // table slot moved between those versions land in the wrong function.
    // Request the versions the DLL was built for, the same way Steamworks.NET
    // does, and put them into its context. The game itself never uses these
    // two interfaces; its own multiplayer uses ISteamNetworking. The approach
    // follows TLD-RevaMP's SteamCompat (LGPL), written independently here.
    //
    // The pointers live in Steamworks.NET's context, which SteamAPI.Init
    // rewrites, so Apply checks them on every call and re-pins when needed.
    internal static class SteamInterfaceCompat
    {
        internal const string SocketsVersion="SteamNetworkingSockets008",UtilsVersion="SteamNetworkingUtils003";
        static FieldInfo socketsField,utilsField;static bool resolved,failed,relayRequested;
        static IntPtr pinnedSockets,pinnedUtils;

        [DllImport("steam_api64",EntryPoint="SteamInternal_FindOrCreateUserInterface",CallingConvention=CallingConvention.Cdecl)]
        static extern IntPtr FindOrCreateUserInterface(int user,[MarshalAs(UnmanagedType.LPStr)]string version);

        // True when Steamworks.NET now uses the DLL's own interface versions.
        internal static bool Apply()
        {
            if(failed)return false;
            try
            {
                if(!resolved)
                {
                    resolved=true;
                    var context=typeof(SteamAPI).Assembly.GetType("Steamworks.CSteamAPIContext");
                    const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static;
                    socketsField=context==null?null:context.GetField("m_pSteamNetworkingSockets",flags);
                    utilsField=context==null?null:context.GetField("m_pSteamNetworkingUtils",flags);
                    if(socketsField==null || utilsField==null)
                    {failed=true;Debug.LogWarning("[TLD MP steam-compat] Steamworks.NET context fields not found; using its default interface versions");return false;}
                }
                IntPtr currentSockets=(IntPtr)socketsField.GetValue(null),currentUtils=(IntPtr)utilsField.GetValue(null);
                if(currentSockets==IntPtr.Zero || currentUtils==IntPtr.Zero)return false; // Steam not initialised yet.
                if(currentSockets==pinnedSockets && currentUtils==pinnedUtils)return true;
                int user=SteamAPI.GetHSteamUser().m_HSteamUser;
                IntPtr sockets=FindOrCreateUserInterface(user,SocketsVersion),utils=FindOrCreateUserInterface(user,UtilsVersion);
                if(sockets==IntPtr.Zero || utils==IntPtr.Zero)
                {failed=true;Debug.LogWarning("[TLD MP steam-compat] Steam client has no "+SocketsVersion+"/"+UtilsVersion+"; using Steamworks.NET's default versions");return false;}
                socketsField.SetValue(null,sockets);utilsField.SetValue(null,utils);
                pinnedSockets=sockets;pinnedUtils=utils;
                Debug.Log("[TLD MP steam-compat] networking interfaces pinned to "+SocketsVersion+"/"+UtilsVersion);
                return true;
            }
            catch(Exception e)
            {
                failed=true;
                Debug.LogWarning("[TLD MP steam-compat] Could not pin networking interface versions: "+e.GetType().Name+" "+e.Message);
                return false;
            }
        }

        // Start Steam's relay network once, as soon as Steam is up, so the first
        // P2P connection does not wait for relay configuration and ping data
        // (that wait can outlast Steam's initial connection timeout).
        internal static void WarmUp()
        {
            if(relayRequested || !SteamManager.Initialized)return;
            relayRequested=true;
            Apply();
            try{SteamNetworkingUtils.InitRelayNetworkAccess();Debug.Log("[TLD MP steam-compat] relay network access requested");}
            catch(Exception e){Debug.LogWarning("[TLD MP steam-compat] InitRelayNetworkAccess failed: "+e.GetType().Name+" "+e.Message);}
        }
    }
}
