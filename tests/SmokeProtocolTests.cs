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
        copy=(byte[])b.Clone();copy[1]=1;Check(!SmokeCodec.Valid(copy),"version 1 layout rejected");
        Check(b.Length==24 && b[1]==2 && b[23]==0,"v2 layout with zero urine");
        var urineBytes=SmokeCodec.Encode(42,99,2,3,12);
        ulong uo,us;uint uq;byte ui,uu;
        Check(SmokeCodec.Read(urineBytes,out uo,out us,out uq,out ui,out uu) && uo==42 && us==99 && uq==2 && ui==3 && uu==12,"urine roundtrip");
        copy=(byte[])urineBytes.Clone();copy[23]=16;Check(!SmokeCodec.Valid(copy),"bounded urine");
        copy=(byte[])urineBytes.Clone();copy[23]=255;Check(!SmokeCodec.Valid(copy),"bounded urine byte");
        Check(SmokeCodec.Read(SmokeCodec.Encode(42,99,2,3),out uo,out us,out uq,out ui) && ui==3,"five argument read keeps smoke intensity");
        bool threw=false;try{SmokeCodec.Encode(42,99,2,3,16);}catch(ArgumentException){threw=true;}Check(threw,"encode rejects urine above range");
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
        state.Clear();
        Check(state.Accept(7,99,1,0,12,0) && state.GetUrine(7,0)==12 && state.Get(7,0)==0,"urine stored beside smoke");
        Check(state.Accept(7,99,2,5,0,.5) && state.GetUrine(7,.5)==0 && state.Get(7,.5)==5,"urine stop keeps smoke");
        Check(state.Accept(7,99,3,0,9,1) && state.GetUrine(7,2.9)==9 && state.GetUrine(7,3)==0,"urine expires with the sample");
        Check(!state.Accept(7,99,3,0,4,1.1) && state.GetUrine(7,1.1)==9,"urine duplicate sequence rejected");
        Check(!state.Accept(7,99,4,0,16,1.2) && state.GetUrine(7,1.2)==9,"urine out of range rejected");
        Check(state.Accept(7,99,5,0,6,1.3) && state.GetUrine(7,1.3)==6,"urine sample replaced by newer sequence");
        state.Remove(7);Check(state.GetUrine(7,1.3)==0,"urine removed with peer");
        // Message type bytes used by the mod must stay distinct.
        Check(SmokeCodec.Type!=250 && SmokeCodec.Type!=251 && SmokeCodec.Type!=253,"smoke type collides with another mod channel");
        Console.WriteLine("Smoke protocol: "+count+" checks passed");
    }
}


