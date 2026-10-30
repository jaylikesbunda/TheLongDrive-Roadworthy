using System;
using TldMp;
using Steamworks;

public static class SteamConnectionDirectionTests
{
    public static int Main()
    {
        uint invalid=HSteamListenSocket.Invalid.m_HSteamListenSocket;
        uint listener=invalid==1?2u:1u,other=invalid==2?3u:2u;
        if(SteamSdrTransport.IsIncomingConnection(invalid,invalid,invalid))throw new Exception("Outgoing connections must not be accepted as incoming.");
        if(SteamSdrTransport.IsIncomingConnection(invalid,listener,invalid))throw new Exception("A client without a listener cannot accept incoming connections.");
        if(SteamSdrTransport.IsIncomingConnection(listener,invalid,invalid))throw new Exception("Outgoing event must not be accepted by the host listener.");
        if(SteamSdrTransport.IsIncomingConnection(listener,other,invalid))throw new Exception("An event for another listener must be ignored.");
        if(!SteamSdrTransport.IsIncomingConnection(listener,listener,invalid))throw new Exception("A matching host listener must accept incoming connections.");
        Console.WriteLine("PASS: Steam incoming/outgoing connection classification with the production callback predicate.");return 0;
    }
}
