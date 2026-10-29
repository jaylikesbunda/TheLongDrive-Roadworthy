using System;
using System.IO;
using System.Net;
using TldMp;

public static class LoopbackTests
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static void Throws<T>(Action action,string reason) where T:Exception
    {
        checks++;
        try{action();}
        catch(T){return;}
        catch(Exception e){throw new Exception(reason+" (wrong exception: "+e.GetType().Name+")");}
        throw new Exception(reason);
    }
    sealed class Trickle:Stream
    {
        readonly byte[] data;readonly int chunk;int at;
        public Trickle(byte[] data,int chunk){this.data=data;this.chunk=chunk;}
        public override bool CanRead{get{return true;}}
        public override bool CanSeek{get{return false;}}
        public override bool CanWrite{get{return false;}}
        public override long Length{get{return data.Length;}}
        public override long Position{get{return at;}set{throw new NotSupportedException();}}
        public override int Read(byte[] buffer,int offset,int count)
        {
            if(at>=data.Length)return 0;
            int n=Math.Min(Math.Min(count,chunk),data.Length-at);
            Array.Copy(data,at,buffer,offset,n);at+=n;return n;
        }
        public override void Flush(){}
        public override long Seek(long offset,SeekOrigin origin){throw new NotSupportedException();}
        public override void SetLength(long value){throw new NotSupportedException();}
        public override void Write(byte[] buffer,int offset,int count){throw new NotSupportedException();}
    }
    static void Frames()
    {
        var random=new Random(1907);
        foreach(int size in new int[]{1,4,17,LoopbackFrames.MaxPayload})
        {
            var payload=new byte[size];random.NextBytes(payload);
            var frame=LoopbackFrames.Encode(payload,0,size);
            Check(frame.Length==size+4,"Frame length matches payload.");
            Check(frame[0]==(byte)size && frame[1]==(byte)(size>>8) && frame[2]==(byte)(size>>16) && frame[3]==(byte)(size>>24),"4-byte little-endian length prefix.");
            using(var stream=new MemoryStream(frame))
            {
                byte[] decoded;Check(LoopbackFrames.Read(stream,out decoded),"Frame decodes.");
                Check(decoded.Length==size,"Decoded size matches.");
                for(int i=0;i<size;i++)if(decoded[i]!=payload[i])throw new Exception("Frame payload round trip.");
                checks++;
            }
        }
        // Two frames through a stream that returns at most one byte per read.
        var first=new byte[]{9,8,7};var second=new byte[300];new Random(11).NextBytes(second);
        var joined=new byte[4+first.Length+4+second.Length];
        var a=LoopbackFrames.Encode(first,0,first.Length);var b=LoopbackFrames.Encode(second,0,second.Length);
        Buffer.BlockCopy(a,0,joined,0,a.Length);Buffer.BlockCopy(b,0,joined,a.Length,b.Length);
        using(var stream=new Trickle(joined,1))
        {
            byte[] one,two,three;
            Check(LoopbackFrames.Read(stream,out one) && one.Length==first.Length,"Partial header/body reads complete first frame.");
            Check(LoopbackFrames.Read(stream,out two) && two.Length==second.Length,"Partial reads complete second frame.");
            Check(!LoopbackFrames.Read(stream,out three),"Clean end of stream at a frame boundary.");
            for(int i=0;i<second.Length;i++)if(two[i]!=second[i])throw new Exception("Second frame exact after trickle.");
        }
        Throws<InvalidDataException>(delegate
        {
            var bad=new byte[]{1,0,1,0}; // 65537 > MaxPayload
            using(var stream=new MemoryStream(bad)){byte[] ignored;LoopbackFrames.Read(stream,out ignored);}
        },"Oversized frame is rejected before allocation.");
        Throws<InvalidDataException>(delegate
        {
            var bad=new byte[]{0,0,0,0};
            using(var stream=new MemoryStream(bad)){byte[] ignored;LoopbackFrames.Read(stream,out ignored);}
        },"Zero-length frame is rejected.");
        Throws<InvalidDataException>(delegate
        {
            var bad=new byte[]{5,0,0,0,1,2}; // truncated body
            using(var stream=new MemoryStream(bad)){byte[] ignored;LoopbackFrames.Read(stream,out ignored);}
        },"Truncated frame body is rejected.");
        Throws<InvalidDataException>(delegate
        {
            var bad=new byte[]{5,0}; // truncated header
            using(var stream=new MemoryStream(bad)){byte[] ignored;LoopbackFrames.Read(stream,out ignored);}
        },"Truncated frame header is rejected.");
        Throws<ArgumentOutOfRangeException>(delegate{LoopbackFrames.Encode(new byte[2],0,3);},"Encode rejects a short source.");
        Throws<ArgumentOutOfRangeException>(delegate{LoopbackFrames.Encode(new byte[LoopbackFrames.MaxPayload+1],0,LoopbackFrames.MaxPayload+1);},"Encode rejects oversized payload.");
        Console.WriteLine("PASS: framing round trip (1..65536 bytes), one-byte partial reads, clean EOF, oversized/zero/truncated rejection.");
    }
    static void Identity()
    {
        Check(!LoopbackConfig.Parse(null).Enabled && !LoopbackConfig.Parse("").Enabled && !LoopbackConfig.Parse("   ").Enabled,"Unset environment is inert.");
        var host=LoopbackConfig.Parse("host:27050");
        Check(host.Enabled && host.Host,"Host role parsed.");
        Check(host.Own==LoopbackConfig.HostID && host.Peer==LoopbackConfig.ClientID && host.HostIdentity==LoopbackConfig.HostID,"Host identity mapping.");
        Check(host.Port==27050 && host.Lobby==LoopbackConfig.LobbyID && host.Lobby!=0,"Host port and synthetic lobby.");
        var client=LoopbackConfig.Parse("client:127.0.0.1:27050");
        Check(client.Enabled && !client.Host,"Client role parsed.");
        Check(client.Own==LoopbackConfig.ClientID && client.Peer==LoopbackConfig.HostID && client.HostIdentity==LoopbackConfig.HostID,"Client identity mapping.");
        Check(client.Endpoint().Equals(IPAddress.Loopback) && client.Port==27050,"Client endpoint is IPv4 loopback.");
        Check(LoopbackConfig.Parse("CLIENT:localhost:1").Enabled && LoopbackConfig.Parse("Host:2").Enabled,"Role matching is case-insensitive.");
        Check(LoopbackConfig.For(true,"127.0.0.1",0).Port==0,"Explicit test construction allows an ephemeral host port.");
        string[] bad={"host","client:127.0.0.1","server:27050","host:0","host:65536","host:abc","client::27050","host:1:2","client:127.0.0.1:27050:1"};
        foreach(string value in bad)
        {
            var parsed=LoopbackConfig.Parse(value);
            Check(!parsed.Enabled && parsed.Error!=null,"Rejected: "+value);
        }
        Check(LoopbackConfig.Parse("host:1").Own!=LoopbackConfig.Parse("client:127.0.0.1:1").Own,"Host and client never share an identity.");
        Console.WriteLine("PASS: identity mapping and environment parsing (host/client/unset/invalid).");
    }
    public static int Main()
    {
        try{Frames();Identity();NetworkFaults();Jitter();Console.WriteLine("PASS: "+checks+" loopback framing/identity/network assertions.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void NetworkFaults()
    {
        var profile=new LoopbackNetworkProfile(200,0,1907);
        var outgoing=new LoopbackFaultQueue(profile,1);var incoming=new LoopbackFaultQueue(profile,2);
        byte[] bytes;bool reliable;
        outgoing.Add(new byte[]{1},true,0);
        Check(!outgoing.Take(.099,out bytes,out reliable),"Outgoing one-way delay is 100ms");
        Check(outgoing.Take(.1,out bytes,out reliable) && reliable,"Outgoing delivered at 100ms");
        incoming.Add(bytes,true,.1);
        Check(!incoming.Take(.199,out bytes,out reliable),"Round trip cannot arrive before 200ms");
        Check(incoming.Take(.2,out bytes,out reliable),"200ms RTT is not incorrectly doubled");
        var lossy=new LoopbackFaultQueue(new LoopbackNetworkProfile(200,100,7),1);
        lossy.Add(new byte[]{1},false,0);Check(!lossy.Take(10,out bytes,out reliable) && lossy.Dropped==1,"Unreliable packet can be lost");
        lossy.Add(new byte[]{2},true,0);lossy.Add(new byte[]{3},true,.01);
        Check(!lossy.Take(.299,out bytes,out reliable),"Reliable loss adds retransmission delay");
        Check(lossy.Take(.301,out bytes,out reliable) && bytes[0]==2 && reliable,"Reliable packet is eventually delivered");
        Check(lossy.Take(.32,out bytes,out reliable) && bytes[0]==3,"Reliable FIFO preserved");
        var randomLoss=new LoopbackFaultQueue(new LoopbackNetworkProfile(0,1,44),1);
        for(int i=0;i<10000;i++)randomLoss.Add(new byte[]{1},false,0);
        Check(randomLoss.Dropped>60 && randomLoss.Dropped<140,"Seeded 1% loss exercised");
        Throws<ArgumentOutOfRangeException>(delegate{new LoopbackNetworkProfile(-1,1,0);},"Negative RTT refused");
        Throws<ArgumentException>(delegate{LoopbackNetworkProfile.Parse("bad","1","0");},"Invalid profile refused");
        Console.WriteLine("PASS: deterministic 200ms RTT, 1% loss, reliable retransmission/order and profile validation; simulated application packets, not Steam/OS packet emulation.");
    }
    static void Jitter()
    {
        // jitter=0 (3-arg and explicit 4-arg) keeps the old 100ms one-way timing exactly.
        var plain=new LoopbackFaultQueue(new LoopbackNetworkProfile(200,0,1907),1);
        var zero=new LoopbackFaultQueue(new LoopbackNetworkProfile(200,0,1907,0),1);
        byte[] bytes;bool reliable;
        plain.Add(new byte[]{1},false,0);zero.Add(new byte[]{1},false,0);
        Check(!plain.Take(.099,out bytes,out reliable) && !zero.Take(.099,out bytes,out reliable),"Zero jitter: nothing before 100ms");
        Check(plain.Take(.1,out bytes,out reliable) && zero.Take(.1,out bytes,out reliable),"Zero jitter: delivered at 100ms");
        Throws<ArgumentOutOfRangeException>(delegate{new LoopbackNetworkProfile(200,0,1,2001);},"Jitter above 2000ms refused");
        Throws<ArgumentOutOfRangeException>(delegate{new LoopbackNetworkProfile(200,0,1,-1);},"Negative jitter refused");
        Throws<ArgumentException>(delegate{LoopbackNetworkProfile.Parse("200","0","1","bad");},"Invalid jitter text refused");
        Check(LoopbackNetworkProfile.Parse("200","0","1907","40").JitterMs==40,"Jitter parsed from its own variable");
        Check(LoopbackNetworkProfile.Parse("200","0","1907",null).JitterMs==0,"Missing jitter means zero");

        // Every packet arrives within [rtt/2, rtt/2+jitter]; probe at 0.5ms steps.
        const int count=200;const double step=.0005;
        var jittered=new LoopbackFaultQueue(new LoopbackNetworkProfile(200,0,1907,40),1);
        for(int i=0;i<count;i++)jittered.Add(new byte[]{(byte)i},false,0);
        var arrival=new double[count];for(int i=0;i<count;i++)arrival[i]=-1;
        int delivered=0;
        for(double now=0;now<=.3 && delivered<count;now+=step)
        {
            while(jittered.Take(now,out bytes,out reliable))
            {
                Check(bytes[0]<count && arrival[bytes[0]]<0,"Each jittered packet is delivered exactly once");
                arrival[bytes[0]]=now;delivered++;
            }
        }
        Check(delivered==count,"Every jittered packet is delivered within 300ms");
        bool spread=false;
        for(int i=0;i<count;i++)
        {
            Check(arrival[i]>=.1-1e-9 && arrival[i]<=.14+step+1e-9,"Jittered one-way delay stays within rtt/2 .. rtt/2+jitter");
            if(Math.Abs(arrival[i]-arrival[0])>.002)spread=true;
        }
        Check(spread,"Jitter actually varies the delay");

        // Reliable packets keep their send order even when jitter would reorder them.
        var reliableQueue=new LoopbackFaultQueue(new LoopbackNetworkProfile(200,0,7,100),2);
        for(int i=0;i<50;i++)reliableQueue.Add(new byte[]{(byte)i},true,i*.001);
        int expected=0;
        for(double now=0;now<=1.0 && expected<50;now+=step)
        {
            while(reliableQueue.Take(now,out bytes,out reliable))
            {
                Check(reliable && bytes[0]==expected,"Reliable order preserved under jitter");
                expected++;
            }
        }
        Check(expected==50,"Every reliable packet delivered under jitter");
        Console.WriteLine("PASS: jitter=0 keeps old timing; jittered one-way delay within [rtt/2, rtt/2+jitter]; reliable FIFO order preserved under jitter; jitter parsing validated.");
    }
}
