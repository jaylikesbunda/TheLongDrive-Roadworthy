using System;
using TldMp;
internal static class RabbitMotionTests
{
    static int count;static void Check(bool v,string name){count++;if(!v)throw new Exception(name);}
    static RabbitMotionSample Sample(){return new RabbitMotionSample{Session=99,Sequence=1,Item=123,Prefab=4,Active=true,X=123456789.25,Y=-200,Z=8,Rx=10,Ry=340,Rz=1};}
    static void Main()
    {
        var sample=Sample();var b=RabbitMotionCodec.Write(sample);RabbitMotionSample got;
        Check(b.Length==59&&RabbitMotionCodec.Read(b,out got)&&got.Item==123&&got.Session==99&&got.Active&&got.X==sample.X&&got.Ry==340,"roundtrip global position");
        Check(!RabbitMotionCodec.Read(new byte[58],out got),"truncated");
        var corrupt=(byte[])b.Clone();corrupt[2]=8;Check(!RabbitMotionCodec.Read(corrupt,out got),"reserved flag rejected");
        corrupt=(byte[])b.Clone();corrupt[2]=2;Check(!RabbitMotionCodec.Read(corrupt,out got),"inactive action rejected");
        sample.Attacking=true;sample.Roaring=true;Check(RabbitMotionCodec.Read(RabbitMotionCodec.Write(sample),out got)&&got.Attacking&&got.Roaring,"action flags");sample=Sample();
        corrupt=(byte[])b.Clone();Buffer.BlockCopy(BitConverter.GetBytes(double.NaN),0,corrupt,23,8);Check(!RabbitMotionCodec.Read(corrupt,out got),"nan");
        corrupt=(byte[])b.Clone();Buffer.BlockCopy(BitConverter.GetBytes(float.PositiveInfinity),0,corrupt,47,4);Check(!RabbitMotionCodec.Read(corrupt,out got),"infinite rotation");
        sample.X=1e11;Check(!RabbitMotionCodec.Valid(sample),"bounded position");sample=Sample();sample.Prefab=-1;Check(!RabbitMotionCodec.Valid(sample),"prefab");
        sample=Sample();sample.Active=false;Check(RabbitMotionCodec.Read(RabbitMotionCodec.Write(sample),out got)&&!got.Active,"stop transition");
        Check(Numbers.Newer(2,1)&&!Numbers.Newer(1,2)&&!Numbers.Newer(2,2),"sequence ordering");
        var poses=new RemotePoseBuffer();var a=new RemotePose{X=100000000,Qw=1};var c=new RemotePose{X=100000010,Qw=1};poses.Add(1,a);poses.Add(1.1,c);RemotePose p;
        Check(poses.Sample(1.05,out p)&&Math.Abs(p.X-100000005)<.001,"global interpolation");
        Check(poses.Add(.9,a)==RemoteAddResult.OutOfOrder,"old sample discarded");
        Check(poses.Sample(2,out p)&&p.X<100000021,"bounded prediction");
        poses.Add(4,a);Check(poses.TakeSnap(out p)&&p.X==a.X,"gap reset");
        Console.WriteLine("Rabbit motion protocol: "+count+" checks passed");
    }
}

