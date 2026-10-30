using System;
using System.Collections.Generic;
using TldMp;

internal static class VehicleMotionTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Put(byte[] b,int offset,byte[] value){Buffer.BlockCopy(value,0,b,offset,value.Length);}
    static byte[] Pose(uint id,float x,int role)
    {
        var b=new byte[49];b[0]=39;Put(b,1,BitConverter.GetBytes(id));Put(b,5,BitConverter.GetBytes(12));
        Put(b,9,BitConverter.GetBytes((double)x));Put(b,45,BitConverter.GetBytes(role));return b;
    }
    static byte[] Velocity(uint id,float speed,int role)
    {var b=new byte[33];b[0]=20;Put(b,1,BitConverter.GetBytes(id));Put(b,5,BitConverter.GetBytes(speed));Put(b,29,BitConverter.GetBytes(role));return b;}
    static VehicleMotionSample Frame(uint sequence,uint clock,float position,float velocity,int role)
    {return new VehicleMotionSample {Sequence=sequence,ClockMs=clock,Pose=Pose(7,position,role),Velocity=Velocity(7,velocity,role)};}
    static void Codec()
    {
        var previous=Frame(1,100,1,10,6);var current=Frame(2,150,2,20,6);
        byte[] bytes=VehicleMotionCodec.Encode(88,current,previous);ulong origin;VehicleMotionSample[] read;
        Check(bytes.Length==VehicleMotionCodec.MaxBytes && VehicleMotionCodec.TryRead(bytes,out origin,out read) && origin==88,"combined snapshot roundtrip");
        VehicleMotionCodec.TryRead(bytes,out origin,out read);
        Check(read.Length==2 && read[0].Sequence==1 && read[1].Sequence==2 && BitConverter.ToDouble(read[1].Pose,9)==2 && BitConverter.ToSingle(read[1].Velocity,5)==20,"position and velocity came from different samples");
        Check(StablePackets.Valid(bytes) && !StablePackets.IsReliable(bytes) && StablePackets.Item(bytes)==7 && StablePackets.LatestValueKey(bytes)!=0,"combined traffic classification");
        for(int n=0;n<bytes.Length;n++){var shortBytes=new byte[n];Buffer.BlockCopy(bytes,0,shortBytes,0,n);Check(!VehicleMotionCodec.Valid(shortBytes),"truncated snapshot accepted");}
        var extra=new byte[bytes.Length+1];Buffer.BlockCopy(bytes,0,extra,0,bytes.Length);Check(!VehicleMotionCodec.Valid(extra),"extra framing accepted");
        var bad=(byte[])bytes.Clone();bad[2]=3;Check(!VehicleMotionCodec.Valid(bad),"unbounded redundant samples accepted");
        bad=(byte[])bytes.Clone();Put(bad,VehicleMotionCodec.HeaderBytes+58+1,BitConverter.GetBytes(999u));Check(!VehicleMotionCodec.Valid(bad),"different car's velocity accepted");
        bad=(byte[])bytes.Clone();Put(bad,VehicleMotionCodec.HeaderBytes+58+5,BitConverter.GetBytes(Single.NaN));Check(!VehicleMotionCodec.Valid(bad),"non-finite velocity accepted");
        current=Frame(3,160,3,20,2);
        Check(VehicleMotionCodec.Encode(88,current,previous).Length==VehicleMotionCodec.HeaderBytes+VehicleMotionCodec.SampleBytes,"previous authority role crossed the handover");
        current=Frame(3,400,3,20,6);
        Check(VehicleMotionCodec.Encode(88,current,previous).Length==VehicleMotionCodec.HeaderBytes+VehicleMotionCodec.SampleBytes,"old sample repeated after a long stall");
        Check(VehicleMotionCodec.ValidOrigin(true,10,88,88) && !VehicleMotionCodec.ValidOrigin(true,10,88,99),"client could spoof another driver's snapshot origin");
        Check(VehicleMotionCodec.ValidOrigin(false,10,10,88) && !VehicleMotionCodec.ValidOrigin(false,10,99,88),"client accepted a relayed snapshot from a non-host");
    }
    static void Pairing()
    {
        var pairer=new VehicleMotionPairer();byte[] packet;byte[] pose=Pose(7,1,6);
        Check(pairer.Offer(0,88,pose,1,0,100,0,false,out packet) && packet==null,"pose was emitted before its velocity");pose[9]=99;
        Check(!pairer.Offer(20,88,Velocity(7,10,6),1,0,100,1,false,out packet),"different target consumed a pending pose");
        Check(pairer.Offer(0,88,Velocity(7,10,6),1,0,100,1,false,out packet) && packet!=null,"native pose and velocity did not pair");
        ulong origin;VehicleMotionSample[] read;VehicleMotionCodec.TryRead(packet,out origin,out read);
        Check(BitConverter.ToDouble(read[0].Pose,9)==1,"pending pose aliases mutable native buffer");
        pairer.Offer(0,88,Pose(7,2,6),1,0,101,0,false,out packet);
        Check(pairer.Offer(0,88,Velocity(7,20,6),1,0,101,2,false,out packet) && packet==null,"impact and normal stream doubled the same frame");
        pairer.Offer(0,88,Pose(7,3,6),2,.05,150,0,false,out packet);
        pairer.Offer(0,88,Velocity(7,30,6),2,.05,150,3,true,out packet);
        VehicleMotionCodec.TryRead(packet,out origin,out read);
        Check(read.Length==2 && read[1].Impact && read[1].Sequence==3,"latest complete frame did not carry bounded redundancy/impact");
        pairer.Offer(0,88,Pose(8,5,6),3,.1,200,0,false,out packet);int fallback=0;
        pairer.Flush(.1,delegate(ulong scope,byte[] b){fallback++;Check(b[0]==39 && BitConverter.ToUInt32(b,1)==8,"unpaired fallback changed native pose");});
        Check(fallback==1,"car without a velocity packet stopped syncing");
        pairer.Clear();for(uint id=1;id<=VehicleMotionPairer.Capacity;id++)Check(pairer.Offer(0,88,Pose(id,0,6),1,0,100,0,false,out packet),"capacity filled too early");
        Check(!pairer.Offer(0,88,Pose(9999,0,6),1,0,100,0,false,out packet) && pairer.Count==VehicleMotionPairer.Capacity,"pair cache exceeded bound instead of falling back to native");
        pairer.Flush(6,delegate{});Check(pairer.Count==0,"idle pairing history never pruned");
    }
    sealed class Delivery {internal double Time;internal byte[] Bytes;}
    static void LossOrderingAndRedundancy()
    {
        var pairer=new VehicleMotionPairer();var deliveries=new List<Delivery>();var random=new Random(813);
        for(uint i=1;i<=600;i++)
        {
            byte[] packet;double now=i*.02;uint clock=1000+i*20;
            pairer.Offer(0,88,Pose(7,i*.2f,6),(int)i,now,clock,0,false,out packet);
            pairer.Offer(0,88,Velocity(7,10,6),(int)i,now,clock,i,false,out packet);
            Check(packet.Length<=VehicleMotionCodec.MaxBytes,"redundancy packet grew with history");
            if(i%9==3)continue;double arrival=now+.1+random.NextDouble()*.08;
            deliveries.Add(new Delivery {Time=arrival,Bytes=packet});
            if(i%10==4)deliveries.Add(new Delivery {Time=arrival+.03,Bytes=packet});
        }
        deliveries.Sort(delegate(Delivery a,Delivery b){return a.Time.CompareTo(b.Time);});
        var filter=new PoseSequenceFilter();var clockMap=new SenderClock();uint newest=0;int recovered=0;
        foreach(var delivery in deliveries)
        {
            ulong origin;VehicleMotionSample[] samples;Check(VehicleMotionCodec.TryRead(delivery.Bytes,out origin,out samples),"lost/reordered wire corrupted");
            double late;clockMap.Map(origin,samples[samples.Length-1].ClockMs,delivery.Time,out late);
            foreach(var sample in samples)
            {
                if(!filter.Accept(origin,PoseSequence.Key(sample.Pose),sample.Sequence))continue;
                Check(Numbers.Newer(sample.Sequence,newest),"old vehicle state applied after newer state");newest=sample.Sequence;
                Check(BitConverter.ToUInt32(sample.Pose,1)==BitConverter.ToUInt32(sample.Velocity,1) && sample.Role==BitConverter.ToInt32(sample.Velocity,29),"non-atomic native state under loss");
                if(sample!=samples[samples.Length-1])recovered++;
            }
        }
        Check(newest==600 && recovered>0 && filter.Stale>0,"loss, duplication and reorder did not converge to newest state");
    }
    static RemotePose Motion(double x,double vx,bool impact)
    {return new RemotePose {X=x,Qw=1,HasMotion=true,Vx=vx,Impact=impact};}
    static void Prediction()
    {
        var b=new RemotePoseBuffer();b.Add(0,Motion(0,20,false));b.Add(.05,Motion(1,20,false));RemotePose p;int kind;
        b.Sample(1,out p,out kind);Check(Math.Abs(p.X-3)<1e-9,"prediction exceeded the two metre/time bound");
        var impact=new RemotePoseBuffer();impact.Add(0,Motion(0,20,false));impact.Add(.05,Motion(1,100,true));
        impact.Sample(.1,out p,out kind);Check(p.X==1 && kind==RemotePoseBuffer.SampleHeld,"impact extrapolated through its obstacle");
        var a=Motion(0,150,false);var c=Motion(1,-150,false);
        for(int i=0;i<=1000;i++){p=RemotePoseBuffer.BlendMotion(a,c,i/1000.0,.05);Check(p.X>=0 && p.X<=1,"velocity tangent overshot authoritative endpoints");}
        a=Motion(0,0,false);c=Motion(1,40,false);p=RemotePoseBuffer.BlendMotion(a,c,.5,.05);
        Check(p.X<.5 && p.X>=0,"interpolation ignored transmitted acceleration tangents");
        b=new RemotePoseBuffer();a=Motion(0,0,false);a.Wy=2;b.Add(0,a);b.Add(.05,a);b.Sample(.15,out p);
        Check(Math.Abs(p.Qy-Math.Sin(.1))<1e-9 && Math.Abs(p.Qy*p.Qy+p.Qw*p.Qw-1)<1e-9,"bounded angular prediction is not normalized world-space spin");
        var policy=new VehicleImpactPolicy();Check(!policy.Observe(0,30,0,0,0,0,0),"first physics sample triggered an impact");
        Check(policy.Observe(.02,0,0,0,0,0,0) && policy.Due(.02),"abrupt authoritative stop did not trigger immediate burst");policy.LastSent=.02;
        Check(!policy.Due(.025) && policy.Due(.04),"impact send spacing ignored");
        Check(!policy.Observe(.04,30,0,0,0,0,0),"one impact caused an unlimited trigger storm");
        Check(!policy.Due(.4),"short impact burst never stopped");
        Check(!policy.Observe(1,0,0,0,0,0,0),"long frame hitch was mistaken for a measured collision");
    }
    static void Main(){Codec();Pairing();LossOrderingAndRedundancy();Prediction();Console.WriteLine("PASS: "+checks+" vehicle motion assertions; atomic native snapshots, bounded redundancy, loss/reorder convergence, monotone interpolation and limited impact prediction/bursts (no Unity/PhysX).");}
}
