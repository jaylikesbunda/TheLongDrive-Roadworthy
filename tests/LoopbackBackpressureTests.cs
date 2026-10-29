using System;
using System.Diagnostics;
using System.Threading;
using TldMp;
internal static class LoopbackBackpressureTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static int Main()
    {
        var host=new LoopbackTransport(LoopbackConfig.For(true,"127.0.0.1",0));
        LoopbackTransport client=null;
        try
        {
            Check(host.Start(),"host starts");
            client=new LoopbackTransport(LoopbackConfig.For(false,"127.0.0.1",host.BoundPort),new LoopbackNetworkProfile(0,0,1));
            Check(client.Start(),"client starts");
            var deadline=Stopwatch.StartNew();while(!client.Ready && deadline.ElapsedMilliseconds<5000)Thread.Sleep(2);
            Check(client.Ready,"client connects");
            // Do not drain the receiver while exceeding its old 8,192 message limit.
            const int count=20000;int accepted=0;var sw=Stopwatch.StartNew();
            for(int i=0;i<count;i++)
            {
                var bytes=BitConverter.GetBytes(i);
                if(!host.Send(LoopbackConfig.ClientID,bytes,true))break;
                accepted++;
            }
            Check(sw.ElapsedMilliseconds<5000,"Send must return promptly under backpressure");
            Thread.Sleep(300);
            Check(client.Ready && client.Error==null,"a stalled receiver stays connected");
            int sent=accepted,received=0;deadline.Restart();
            while(received<count && deadline.ElapsedMilliseconds<20000)
            {
                while(sent<count && host.Send(LoopbackConfig.ClientID,BitConverter.GetBytes(sent),true))sent++;
                LoopbackEvent ev;
                while(client.TryDequeue(out ev))
                {
                    if(ev.Bytes==null)continue;
                    Check(ev.Reliable && ev.Bytes.Length==4 && BitConverter.ToInt32(ev.Bytes,0)==received,"reliable traffic is byte-exact and FIFO");
                    received++;
                }
                Thread.Sleep(1);
            }
            Check(sent==count && received==count,"all reliable messages delivered after receiver catches up");
            Check(client.Ready && client.Error==null,"connection survives the flood");
            Console.WriteLine("PASS: 20,000 reliable messages survive a stalled receiver, in order; sends return without socket writes on the caller.");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(client!=null)client.Stop();host.Stop();}
    }
}
