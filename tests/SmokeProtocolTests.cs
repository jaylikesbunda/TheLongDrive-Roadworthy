using System;
using TldMp;
internal static class SmokeProtocolTests
{
    static int count;
    static void Check(bool value,string message){count++;if(!value)throw new Exception(message);}
    static void Main()
    {
        ulong origin,session;uint seq;byte intensity;
        var b=SmokeCodec.Encode(42,99,1,15);
        Check(SmokeCodec.Read(b,out origin,out session,out seq,out intensity) && origin==42 && session==99 && seq==1 && intensity==15,"roundtrip");
        Check(!SmokeCodec.Valid(new byte[22]),"short");
        var copy=(byte[])b.Clone();copy[22]=16;Check(!SmokeCodec.Valid(copy),"bounded intensity");
        copy=(byte[])b.Clone();copy[1]=0;Check(!SmokeCodec.Valid(copy),"version");
        Check(SmokeCodec.Quantize(float.NaN)==0 && SmokeCodec.Quantize(-1)==0 && SmokeCodec.Quantize(2)==15 && SmokeCodec.Quantize(.01f)==1,"quantize");
        Check(SmokeCodec.ValidSender(true,42,10,42,99,99),"host admission");
        Check(!SmokeCodec.ValidSender(true,42,10,43,99,99),"spoofed origin");
        Check(!SmokeCodec.ValidSender(true,42,10,42,98,99),"old session");
        Check(SmokeCodec.ValidSender(false,10,10,42,77,77),"host relay");
        Check(!SmokeCodec.ValidSender(false,11,10,42,77,77),"nonhost relay");
        Check(!SmokeCodec.ValidSender(false,10,10,42,76,77),"old host session");
        var relay=new SmokeRelaySequences();Check(relay.Next(42)==1 && relay.Next(42)==2 && relay.Next(43)==1 && relay.Next(42)==3,"relay ordering across origin sessions");
        var state=new SmokeState();
        Check(state.Accept(42,99,10,15,0),"first");
        Check(!state.Accept(42,99,10,0,.1),"duplicate");
        Check(!state.Accept(42,99,9,0,.2),"stale");
        Check(state.Get(42,1.99)==15 && state.Get(42,2)==0,"expiry");
        Check(state.Accept(42,99,11,7,2.1) && state.Get(42,3.9)==7,"refresh");
        Check(state.Accept(42,99,12,0,4) && state.Get(42,4)==0,"stop");
        state.Remove(42);Check(state.Accept(42,99,1,2,5),"new peer incarnation");
        state.Clear();Check(state.Accept(42,99,100,1,5.5) && state.Accept(42,100,1,2,5.6),"authenticated peer session replacement");
        state.Clear();Check(state.Accept(42,99,uint.MaxValue,1,6) && state.Accept(42,99,1,2,7),"wrap");
        Check(!state.Accept(0,99,1,1,0) && !state.Accept(3,99,0,1,0) && !state.Accept(3,99,1,16,0),"invalid state");
        Console.WriteLine("Smoke protocol: "+count+" checks passed");
    }
}


