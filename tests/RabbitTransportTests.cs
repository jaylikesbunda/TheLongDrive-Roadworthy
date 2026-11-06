using System;
using System.Collections.Generic;
using TldMp;
public static class RabbitTransportTests
{
    static int checks;
    static void Check(bool ok,string what){checks++;if(!ok)throw new Exception("FAIL: "+what);}
    sealed class Packet{public ulong From,To;public byte[] Bytes;public bool Reliable;}
    sealed class Lab
    {
        public StableNode Host,Client;public double Now;
        public int Requests,States,Applies,Delivers;
        public ulong ReqPeer;public byte[] ReqData,StateData;
        public readonly List<string> Order=new List<string>();
        public readonly List<Packet> Queue=new List<Packet>();
        public bool CanEnter=true;
        public Lab()
        {
            Host=new StableNode(10,10,999,101,"r",0);
            Host.Send=delegate(ulong to,byte[] b,bool r){Queue.Add(new Packet{From=10,To=to,Bytes=b,Reliable=r});};
            Host.SnapshotRequested=delegate(ulong p){Host.BeginSnapshot(p,new byte[]{1,2,3});};
            Host.MapRequested=delegate(ulong p,string k){return new byte[]{7,7,7,7};};
            Host.RabbitRequestReceived=delegate(ulong p,byte[] d){Requests++;ReqPeer=p;ReqData=(byte[])d.Clone();Order.Add("req"+d[0]);};
            Host.RabbitStateReceived=delegate(byte[] d){States+=1000;};
            Host.Deliver=delegate(ulong p,byte[] d){Delivers++;};
            WireClient(new StableNode(20,10,999,202,"r",0));
            Client.Tick(0);Pump();
            Client.RequestSnapshot();Pump();
            Check(Client.Ready&&Client.Error==null,"baseline join: "+(Client.Error??"not ready"));
        }
        public void WireClient(StableNode c)
        {
            Client=c;
            Client.Send=delegate(ulong to,byte[] b,bool r){Queue.Add(new Packet{From=20,To=to,Bytes=b,Reliable=r});};
            Client.ApplySnapshot=delegate(byte[] b){Applies++;Order.Add("apply");};
            Client.CanEnterWorld=delegate{return CanEnter;};
            Client.RabbitRequestReceived=delegate(ulong p,byte[] d){Requests+=100;};
            Client.RabbitStateReceived=delegate(byte[] d){States++;StateData=(byte[])d.Clone();Order.Add("state"+d[0]+(Client.Ready?":ready":":joining"));};
            Client.Deliver=delegate(ulong p,byte[] d){Delivers++;};
        }
        public void Pump()
        {
            int g=0;
            while(Queue.Count>0){if(++g>10000)throw new Exception("loop");var p=Queue[0];Queue.RemoveAt(0);if(p.To==10)Host.Receive(p.From,p.Bytes,Now);else Client.Receive(p.From,p.Bytes,Now);if(Client.Ready&&Order.Count>0&&Order[Order.Count-1]!="live")Order.Add("live");}
        }
        public StableMessage Msg(StableOp op,ulong lobby,ulong session,ulong origin,ulong epoch,ulong seq,string key,byte[] data)
        {return new StableMessage{Op=op,Lobby=lobby,Session=session,Origin=origin,Epoch=epoch,Sequence=seq,Key=key,Data=data??new byte[0]};}
        public int RabbitPackets(bool reliable)
        {int n=0;foreach(var p in Queue){var m=StableMessage.Decode(p.Bytes);if(m!=null&&((int)m.Op==23||(int)m.Op==24)&&p.Reliable==reliable)n++;}return n;}
    }
    static void RequestFlow()
    {
        var l=new Lab();
        Check(l.Client.SendRabbitRequest(new byte[]{9,9}),"guest request accepted");
        l.Pump();
        Check(l.Requests==1&&l.ReqPeer==20&&l.ReqData.Length==2&&l.ReqData[0]==9,"host callback once with payload");
        Check(l.States==0,"request not relayed as state");
        Check(l.Delivers==0,"request never reaches native Deliver");
        l.Client.SendRabbitRequest(new byte[]{1,2,3});
        l.Pump();
        Check(l.Requests==2&&l.ReqData[0]==1,"second request seq increments");
        int rx=l.Requests;
        l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,202,20,202,2,"",new byte[]{7}).Encode(),l.Now);
        Check(l.Requests==rx,"replayed seq2 rejected");
        l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,202,20,202,0,"",new byte[]{7}).Encode(),l.Now);
        Check(l.Requests==rx,"seq0 rejected");
        l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,202,20,203,10,"",new byte[]{7}).Encode(),l.Now);
        Check(l.Requests==rx,"wrong epoch rejected");
        l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,202,30,202,10,"",new byte[]{7}).Encode(),l.Now);
        Check(l.Requests==rx,"foreign origin rejected");
        l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,202,20,202,10,"",new byte[257]).Encode(),l.Now);
        Check(l.Requests==rx,"257-byte request rejected");
        l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,202,20,202,10,"k",new byte[]{7}).Encode(),l.Now);
        Check(l.Requests==rx,"nonempty key rejected");
        l.Host.Receive(20,l.Msg(StableOp.RabbitState,999,202,20,202,10,"",new byte[]{7}).Encode(),l.Now);
        Check(l.States==0,"host ignores guest-origin state");
        Check(l.Requests==rx,"state-op not a request");
    }
    static void StateFlow()
    {
        var l=new Lab();
        l.Host.PublishRabbitState(new byte[]{5,5,5,5});
        l.Pump();
        Check(l.States==1&&l.StateData.Length==4,"guest receives host state");
        Check(!l.Host.SendRabbitRequest(new byte[]{1}),"host cannot send request");
        Check(!l.Client.PublishRabbitState(new byte[]{1}),"guest cannot publish state");
        int sx=l.States;
        l.Client.Receive(10,l.Msg(StableOp.RabbitState,999,202,20,101,10,"",new byte[]{8}).Encode(),l.Now);
        Check(l.States==sx,"guest rejects non-host-origin state");
        l.Client.Receive(10,l.Msg(StableOp.RabbitState,999,101,10,101,1,"",new byte[]{6}).Encode(),l.Now);
        Check(l.States==sx,"replayed state seq rejected");
        l.Client.Receive(10,l.Msg(StableOp.RabbitState,999,101,10,101,0,"",new byte[]{6}).Encode(),l.Now);
        Check(l.States==sx,"seq0 state rejected");
        l.Client.Receive(10,l.Msg(StableOp.RabbitState,998,101,10,101,5,"",new byte[]{6}).Encode(),l.Now);
        Check(l.States==sx,"foreign lobby rejected");
        l.Client.Receive(10,l.Msg(StableOp.RabbitState,999,101,10,102,5,"",new byte[]{6}).Encode(),l.Now);
        Check(l.States==sx,"wrong epoch rejected");
        l.Client.Receive(10,l.Msg(StableOp.RabbitState,999,101,10,101,5,"k",new byte[]{6}).Encode(),l.Now);
        Check(l.States==sx,"nonempty key rejected");
        l.Client.Receive(10,l.Msg(StableOp.RabbitState,999,101,10,101,5,"",new byte[16385]).Encode(),l.Now);
        Check(l.States==sx,"16385-byte state rejected");
        l.Client.Receive(10,l.Msg(StableOp.RabbitState,999,101,10,101,5,"",new byte[0]).Encode(),l.Now);
        Check(l.States==sx,"empty state rejected");
        l.Client.Receive(20,l.Msg(StableOp.RabbitState,999,101,10,101,5,"",new byte[]{6}).Encode(),l.Now);
        Check(l.States==sx,"sender==own rejected");
        l.Client.Receive(10,l.Msg(StableOp.RabbitState,999,101,10,101,5,"",new byte[16384]).Encode(),l.Now);
        Check(l.States==sx+1&&l.StateData.Length==16384,"16384-byte state accepted");
        l.Client.Receive(10,l.Msg(StableOp.RabbitRequest,999,101,10,101,9,"",new byte[]{9}).Encode(),l.Now);
        Check(l.States==sx+1,"guest ignores host-origin request");
    }
    static void Boundaries()
    {
        var l=new Lab();
        Check(!l.Client.SendRabbitRequest(null),"null request rejected");
        Check(!l.Client.SendRabbitRequest(new byte[0]),"empty request rejected");
        Check(!l.Client.SendRabbitRequest(new byte[257]),"257-byte send rejected");
        Check(l.Client.SendRabbitRequest(new byte[256]),"256-byte send accepted");
        Check(l.Client.SendRabbitRequest(new byte[]{7}),"1-byte send accepted");
        Check(!l.Host.PublishRabbitState(null),"null state rejected");
        Check(!l.Host.PublishRabbitState(new byte[0]),"empty state rejected");
        Check(!l.Host.PublishRabbitState(new byte[16385]),"16385 state rejected");
        Check(l.Host.PublishRabbitState(new byte[16384]),"16384 state accepted");
        Check(l.Host.PublishRabbitState(new byte[]{4}),"1-byte state accepted");
        l.Queue.Clear();
        Check(l.Client.SendRabbitRequest(new byte[]{8})&&l.RabbitPackets(true)==1,"request wire is reliable");
        l.Pump();
        l.Queue.Clear();
        l.Host.PublishRabbitState(new byte[]{8});
        Check(l.RabbitPackets(true)==1,"state wire is reliable");
        l.Pump();
        var mut=new byte[]{3,3,3};
        l.Host.PublishRabbitState(mut);mut[0]=99;
        l.Pump();
        Check(l.StateData!=null&&l.StateData[0]==3,"publish freezes payload");
        var over=typeof(StableNode).GetField("rabbitRequestSeq",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        over.SetValue(l.Client,ulong.MaxValue);
        Check(!l.Client.SendRabbitRequest(new byte[]{1}),"request seq overflow fail-closed");
        var over2=typeof(StableNode).GetField("rabbitStateSeq",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        over2.SetValue(l.Host,ulong.MaxValue);
        Check(!l.Host.PublishRabbitState(new byte[]{1}),"state seq overflow fail-closed");
    }
    static void JournalOrdering()
    {
        var l=new Lab();
        l.Now=5;l.Client.Tick(5);
        l.Order.Clear();
        l.CanEnter=false;
        l.Client.RequestSnapshot();l.Pump();
        Check(l.Applies>=1&&!l.Client.Ready,"snapshot applied, world gated");
        var a=new byte[]{1,0,0};var b=new byte[]{2,0,0};var c=new byte[]{3,0,0};
        l.Host.PublishRabbitState(a);a[0]=55;
        l.Host.PublishRabbitState(b);
        l.Host.PublishRabbitState(c);
        l.Pump();
        Check(l.States==0,"no state callback while host syncing");
        l.CanEnter=true;l.Client.Tick(l.Now+1);l.Pump();
        Check(l.States==3,"journaled states delivered once");
        int ai=l.Order.IndexOf("apply"),l1=l.Order.IndexOf("state1:joining"),l2=l.Order.IndexOf("state2:joining"),l3=l.Order.IndexOf("state3:joining"),lv=l.Order.IndexOf("live");
        Check(ai>=0&&l1>ai&&l2>l1&&l3>l2&&lv>l3,"order: apply -> states(while joining) -> live");
        Check(l.Order[l.Order.Count-1]=="live","Ready after replayed journal");
    }
    static void ReconnectReset()
    {
        var l=new Lab();
        l.Client.SendRabbitRequest(new byte[]{1});l.Pump();
        Check(l.Requests==1,"pre-reset request");
        l.Host.RemovePeer(20);
        var c2=new StableNode(20,10,999,303,"r",0);
        l.WireClient(c2);
        c2.Tick(l.Now+1);l.Pump();
        c2.RequestSnapshot();l.Pump();
        Check(c2.Ready&&c2.Error==null,"rejoin on session 303");
        Check(c2.SendRabbitRequest(new byte[]{2})&&l.Queue.Count>0,"fresh peer accepts seq1");
        l.Pump();
        Check(l.Requests==2&&l.ReqData[0]==2,"seq1 on new session accepted");
        l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,202,20,202,5,"",new byte[]{9}).Encode(),l.Now);
        Check(l.Requests==2,"old-session request rejected");
        c2.SendRabbitRequest(new byte[]{3});l.Pump();
        Check(l.Requests==3,"seq2 on new session accepted");
        l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,303,20,303,1,"",new byte[]{9}).Encode(),l.Now);
        Check(l.Requests==3,"replayed seq1 on new session rejected");
    }
    static void NotReadyAndError()
    {
        var l=new Lab();
        var fresh=new StableNode(30,10,999,404,"r",0);
        var q2=new List<Packet>();
        fresh.Send=delegate(ulong to,byte[] b,bool r){q2.Add(new Packet{From=30,To=to,Bytes=b,Reliable=r});};
        Check(!fresh.SendRabbitRequest(new byte[]{1}),"unjoined guest cannot request");
        Check(q2.Count==0,"unjoined guest emits nothing");
        l.Now=5;l.Client.Tick(5);
        l.CanEnter=false;
        l.Client.RequestSnapshot();l.Pump();
        Check(l.Applies>=1,"sync in progress");
        int rx=l.Requests;
        l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,202,20,202,9,"",new byte[]{7}).Encode(),l.Now);
        Check(l.Requests==rx,"host rejects request while peer syncing (not ready)");
        l.CanEnter=true;l.Client.Tick(l.Now+1);l.Pump();
        Check(l.Client.Ready,"re-ready after sync");
        int rx2=l.Requests;
        l.Host.RabbitRequestReceived=delegate(ulong p,byte[] d){l.Requests++;l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,202,20,202,19,"",new byte[]{8}).Encode(),l.Now);};
        l.Host.Receive(20,l.Msg(StableOp.RabbitRequest,999,202,20,202,19,"",new byte[]{8}).Encode(),l.Now);
        Check(l.Requests==rx2+1,"record-before-callback: reentrant replay same packet no recursion");
        l.Client.Stop("halt");
        Check(!l.Client.SendRabbitRequest(new byte[]{1}),"errored guest cannot request");
        l.Host.Stop("halt");
        Check(!l.Host.PublishRabbitState(new byte[]{1}),"errored host cannot publish");
    }
    static void Decode()
    {
        var l=new Lab();
        var good=l.Msg(StableOp.RabbitRequest,999,202,20,202,7,"",new byte[]{9,9}).Encode();
        Check(StableMessage.Decode(good)!=null,"valid request decodes");
        var trunc=new byte[good.Length-3];Buffer.BlockCopy(good,0,trunc,0,trunc.Length);
        Check(StableMessage.Decode(trunc)==null,"truncated rejects");
        var badVer=(byte[])good.Clone();badVer[5]=4;
        Check(StableMessage.Decode(badVer)==null,"bad version rejects");
        Check(StableMessage.Decode(l.Msg((StableOp)30,999,101,10,101,1,"",new byte[]{1}).Encode())==null,"unknown op 30 rejects");
        // 26-28 are the mode-gated experimental world-join frames: inside the range now.
        Check(StableMessage.Decode(l.Msg(StableOp.ExpSnapshotBegin,999,101,10,101,1,"",new byte[36]).Encode())!=null,"experimental snapshot begin decodes");
        Check(StableMessage.Decode(l.Msg(StableOp.ExpSnapshotPart,999,101,10,101,1,"",new byte[]{1}).Encode())!=null,"experimental snapshot part decodes");
        Check(StableMessage.Decode(l.Msg(StableOp.ExpSnapshotEnd,999,101,10,101,1,"",new byte[0]).Encode())!=null,"experimental snapshot end decodes");
        Check(StableMessage.Decode(l.Msg(StableOp.ExpItemEvent,999,101,10,101,1,"",new byte[]{1}).Encode())!=null,"experimental item event decodes");
        Check(StableMessage.Decode(l.Msg(StableOp.RabbitState,999,101,10,101,1,"",new byte[]{1}).Encode())!=null,"op 24 decodes");
    }
    public static int Main(string[] a)
    {
        RequestFlow();StateFlow();Boundaries();JournalOrdering();ReconnectReset();NotReadyAndError();Decode();
        Console.WriteLine("PASS "+checks+" rabbit transport checks");
        return 0;
    }
}
