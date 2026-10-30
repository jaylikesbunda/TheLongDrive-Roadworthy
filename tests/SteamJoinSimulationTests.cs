using System;
using System.Text;
using TldMp;
using Steamworks;
public static class SteamJoinSimulationTests
{
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static byte[] Password(string text){byte[] b=new byte[5+text.Length*2];b[0]=5;Buffer.BlockCopy(BitConverter.GetBytes(text.Length),0,b,1,4);Buffer.BlockCopy(Encoding.Unicode.GetBytes(text),0,b,5,text.Length*2);return b;}
    sealed class Peer:IDisposable
    {
        public readonly ulong Id;public readonly SteamSdrTransport Socket;public readonly StableNode Node;public readonly ReliableOutbox Outbox;
        public bool Authenticated,Rejected,MapLoaded,Applied;public bool Member=true;public int Connected;public double Now;
        public Peer Other;
        public Peer(ulong id,string build)
        {
            Id=id;Simulation.Current=id;Socket=new SteamSdrTransport();Socket.AcceptPeer=delegate(ulong p){return Member && Other!=null && p==Other.Id;};
            Socket.PeerConnected+=delegate{Connected++;};Socket.ReceiveFailure+=delegate(Exception e){throw new Exception("Receive failed",e);};
            Node=new StableNode(id,10,999,id+123,build,100);Node.CanEnterWorld=delegate{return MapLoaded;};
            Outbox=new ReliableOutbox(delegate(ulong p,byte[] b,bool reliable){return Socket.Send(p,b,NetDeliveryLane.Critical,reliable);},delegate(ulong p,string e){throw new Exception(e);});
            Node.Send=delegate(ulong p,byte[] b,bool reliable){if(reliable)Outbox.Send(p,b,true,Now);else Socket.Send(p,b,NetDeliveryLane.Critical,false);};
            Node.Deliver=delegate(ulong p,byte[] b){Check(b[0]==8,"map bootstrap payload");MapLoaded=true;Node.HostWorldLoaded=true;Node.RequestSnapshot();};
            Node.SnapshotRequested=delegate(ulong p){Node.BeginSnapshot(p,new byte[]{9,8,7});};
            Node.ApplySnapshot=delegate(byte[] b){Check(b.Length==3 && b[0]==9,"snapshot byte equality");Applied=true;};
        }
        public void SendPassword(string text)
        {byte[] b=Password(text);Check(StablePackets.PasswordBootstrapAllowed(false,Id,10,10,true,b),"bootstrap policy admits password");Outbox.Send(10,b,true,Now);}
        public void Receive(ulong from,byte[] b)
        {
            // Native Expected gate: host accepts only passwords pre-auth;
            // client accepts only host password/kick pre-auth.
            if(!Member || from!=Other.Id)return;
            if(!Authenticated && b[0]!=5 && !(Id!=10 && b[0]==6))return;
            if(b[0]==254){Node.Receive(from,b,Now);if(Id==10 && Node.Peers.ContainsKey(from))Node.SendBootstrap(from,new byte[]{8,42});return;}
            Check(StablePackets.Valid(b),"valid native bootstrap packet");
            if(b[0]==6){Rejected=true;return;}
            string password=Encoding.Unicode.GetString(b,5,b.Length-5);
            if(Id==10){byte[] reply=password=="secret"?Password("1"):new byte[]{6,1};Check(StablePackets.PasswordBootstrapAllowed(true,10,10,from,true,reply),"host bootstrap reply policy");Outbox.Send(from,reply,true,Now);if(password=="secret")Authenticated=true;}
            else if(password=="1")Authenticated=true;
        }
        public void Tick(double now){Now=now;Simulation.Current=Id;Outbox.Flush(now);if(Id==10 || Authenticated)Node.Tick(now);Socket.Poll(Receive,256);}
        public void Dispose(){Simulation.Current=Id;Socket.Dispose();}
    }
    static void Run(bool correct,string build,bool refuse,bool member)
    {
        using(var host=new Peer(10,"same"))using(var client=new Peer(20,build)){
            host.Other=client;client.Other=host;host.Member=member;
            Simulation.Current=10;host.Socket.Listen(0);Simulation.Current=20;Check(client.Socket.Connect(10,0),"connection handle allocated");Simulation.Pump();
            Check(Simulation.InvalidAccepts==0,"client never accepts its outgoing connection");
            if(!member){Check(host.Connected==0 && client.Connected==0,"nonmember blocked before authentication");return;}
            Check(host.Connected==1 && client.Connected==1,"both endpoints configure and connect");
            if(refuse)Simulation.RefuseSends=3;
            client.SendPassword(correct?"secret":"wrong");
            for(int frame=0;frame<1000 && !client.Node.Ready && !client.Rejected && client.Node.Error==null;frame++){double t=frame*.02;client.Tick(t);host.Tick(t);Simulation.Pump();}
            if(!correct)Check(client.Rejected && !client.Authenticated && !client.Node.Compatible,"wrong password reaches rejection without handshake");
            else if(build!="same")Check(client.Node.Error!=null && !client.Node.Ready,"mismatched build rejected after authentication");
            else Check(client.Authenticated && client.Node.Compatible && client.MapLoaded && client.Applied && client.Node.Ready && host.Node.Peers[20].Ready,"password -> Hello/Welcome -> map bootstrap -> snapshot -> ACK/Live");
        }
        Check(Simulation.Allocations==0,"all native message allocations released");
    }
    public static int Main()
    {
        Run(true,"same",false,true);Run(true,"same",true,true);Run(false,"same",false,true);Run(true,"different",false,true);Run(true,"same",false,false);
        Check(Simulation.InvalidAccepts==0,"no invalid accepts in any scenario");Check(Simulation.Allocations==0,"no leaked messages");
        Console.WriteLine("PASS: "+checks+" Steam join simulation assertions using production SDR transport, reliable outbox, bootstrap policy and StableNode; success, backpressure, wrong password, build mismatch and nonmember rejection.");return 0;
    }
}
