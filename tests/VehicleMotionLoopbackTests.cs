using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TldMp;

// Actual localhost framing/fault queues carrying the new motion envelope.
// Scene/PhysX remain outside this test; production ingress IL is tested separately.
internal static class VehicleMotionLoopbackTests
{
    static int checks;
    static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    static void Put(byte[] b,int at,byte[] value){Buffer.BlockCopy(value,0,b,at,value.Length);}
    static VehicleMotionSample Sample(uint sequence,VehicleImpactPolicy impact)
    {
        double now=sequence*.02;float velocity=sequence<45?20:0;float position=Math.Min(sequence,45)*.4f;
        impact.Observe(now,velocity,0,0,0,0,0);
        var pose=new byte[49];pose[0]=39;Put(pose,1,BitConverter.GetBytes(7u));Put(pose,5,BitConverter.GetBytes(12));Put(pose,9,BitConverter.GetBytes((double)position));Put(pose,45,BitConverter.GetBytes(6));
        var vel=new byte[33];vel[0]=20;Put(vel,1,BitConverter.GetBytes(7u));Put(vel,5,BitConverter.GetBytes(velocity));Put(vel,29,BitConverter.GetBytes(6));
        return new VehicleMotionSample {Sequence=sequence,ClockMs=1000+sequence*20,Pose=pose,Velocity=vel,Impact=now<impact.BurstUntil};
    }
    static void Run(LoopbackNetworkProfile profile)
    {
        var host=new LoopbackTransport(LoopbackConfig.For(true,"127.0.0.1",0));LoopbackTransport client=null;
        try
        {
            Check(host.Start(),"loopback motion listener failed");
            client=new LoopbackTransport(LoopbackConfig.For(false,"127.0.0.1",host.BoundPort),profile);
            Check(client.Start(),"loopback motion client failed");
            var watch=Stopwatch.StartNew();while((!client.Ready || !host.Ready) && watch.Elapsed.TotalSeconds<5)Thread.Sleep(1);
            Check(client.Ready && host.Ready,"loopback motion socket handshake failed");
            var filter=new PoseSequenceFilter();var clock=new SenderClock();var buffer=new RemotePoseBuffer();var impact=new VehicleImpactPolicy();
            uint newest=0,next=1;int packets=0,impacts=0,smooth=0;VehicleMotionSample previous=null;byte[] last=null;double nextSend=0,resend=0;
            watch.Restart();
            while(watch.Elapsed.TotalSeconds<6 && (next<=100 || newest<100))
            {
                double now=watch.Elapsed.TotalSeconds;
                if(next<=100 && now>=nextSend)
                {
                    var sample=Sample(next,impact);last=VehicleMotionCodec.Encode(LoopbackConfig.HostID,sample,previous);previous=sample;
                    Check(last.Length<=VehicleMotionCodec.MaxBytes && host.Send(LoopbackConfig.ClientID,last,false),"motion snapshot failed bounded socket admission");
                    next++;nextSend=now+.02;
                }
                else if(next>100 && newest<100 && now>=resend)
                {Check(host.Send(LoopbackConfig.ClientID,last,false),"stationary refresh failed");resend=now+.1;}
                LoopbackEvent message;
                while(client.TryDequeue(out message))
                {
                    if(message.Bytes==null)continue;packets++;
                    Check(message.From==LoopbackConfig.HostID && !message.Reliable,"motion peer or unreliable metadata changed across localhost");
                    ulong origin;VehicleMotionSample[] samples;Check(VehicleMotionCodec.TryRead(message.Bytes,out origin,out samples),"localhost lost combined motion framing");
                    double late;clock.Map(origin,samples[samples.Length-1].ClockMs,now,out late);
                    foreach(var sample in samples)
                    {
                        if(!filter.Accept(origin,PoseSequence.Key(sample.Pose),sample.Sequence))continue;
                        Check(sample.Sequence>newest,"localhost reorder moved a car backward in sequence");newest=sample.Sequence;
                        float speed=BitConverter.ToSingle(sample.Velocity,5);
                        Check(speed==(sample.Sequence<45?20:0),"pre/post-impact velocity separated from its pose");
                        var p=new RemotePose {X=BitConverter.ToDouble(sample.Pose,9),Qw=1,HasMotion=true,Vx=speed,Impact=sample.Impact};
                        double time=clock.Map(origin,sample.ClockMs,now,out late);buffer.Add(time,p,late);if(sample.Impact)impacts++;
                    }
                }
                RemotePose rendered;
                if(buffer.Sample(now-buffer.Delay,out rendered))
                {Check(!Double.IsNaN(rendered.X) && rendered.X>=0 && rendered.X<=20.001,"localhost prediction escaped its bounded authoritative trajectory");smooth++;}
                Thread.Sleep(1);
            }
            Check(newest==100 && packets>0 && smooth>0 && impacts>0 && filter.Stale>0,"localhost motion, impact flags, repeated-sample rejection or final convergence missing");
            RemotePose final;buffer.Sample(Double.MaxValue,out final);Check(Math.Abs(final.X-18)<.001,"localhost peers did not converge to the stopped car position");
            Console.WriteLine("PASS: actual loopback motion RTT="+profile.RoundTripMs+"ms jitter="+profile.JitterMs+"ms loss="+profile.LossPercent+"%; packets="+packets+" processedThrough="+newest+" impactSamples="+impacts+" staleRepeated="+filter.Stale);
        }
        finally{if(client!=null)client.Stop();host.Stop();}
    }
    static void Main()
    {
        Run(new LoopbackNetworkProfile(0,0,1907));Run(new LoopbackNetworkProfile(200,0,1907,40));Run(new LoopbackNetworkProfile(200,5,1907,40));
        Console.WriteLine("PASS: "+checks+" actual TCP loopback vehicle assertions; atomic snapshots, motion metadata, loss/jitter/reordering, impact flags, velocity-aware bounded smoothing and stationary convergence. No Unity/PhysX/Steam.");
    }
}
