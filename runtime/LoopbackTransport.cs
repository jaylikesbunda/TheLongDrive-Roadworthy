using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TldMp
{
    // Test-only transport for two game instances on one machine. The environment
    // variable is read exactly once when this type is first touched. With it
    // unset there is no config object, no socket and no thread.
    public sealed class LoopbackConfig
    {
        public const ulong HostID=1,ClientID=2,LobbyID=0x4C4F4F5000000001UL;
        public readonly bool Enabled,Host;public readonly string Address;public readonly int Port;
        public readonly ulong Own,Peer,HostIdentity,Lobby;public readonly string Error;
        LoopbackConfig(bool enabled,bool host,string address,int port,string error)
        {
            Enabled=enabled;Host=host;Address=address;Port=port;Error=error;
            if(!enabled)return;
            Own=host?HostID:ClientID;Peer=host?ClientID:HostID;HostIdentity=HostID;Lobby=LobbyID;
        }
        // host:27050 or client:127.0.0.1:27050
        public static LoopbackConfig Parse(string text)
        {
            if(text==null || text.Trim().Length==0)return new LoopbackConfig(false,false,null,0,null);
            string value=text.Trim();string[] parts=value.Split(':');
            if(parts.Length<2 || parts.Length>3)return Invalid("Expected host:PORT or client:HOST:PORT.");
            bool host=String.Equals(parts[0],"host",StringComparison.OrdinalIgnoreCase);
            bool client=String.Equals(parts[0],"client",StringComparison.OrdinalIgnoreCase);
            if(!host && !client)return Invalid("Unknown loopback role. Use host or client.");
            if(host && parts.Length!=2)return Invalid("Host syntax is host:PORT.");
            if(client && parts.Length!=3)return Invalid("Client syntax is client:HOST:PORT.");
            int port;
            if(!Int32.TryParse(parts[parts.Length-1],out port) || port<1 || port>65535)return Invalid("Loopback port must be 1..65535.");
            string address=client?parts[1]:null;
            if(client && (address==null || address.Length==0))return Invalid("Client address is missing.");
            return new LoopbackConfig(true,host,address,port,null);
        }
        static LoopbackConfig Invalid(string error){return new LoopbackConfig(false,false,null,0,error);}
        // Explicit construction for tests: port 0 binds an ephemeral host port.
        public static LoopbackConfig For(bool host,string address,int port)
        {
            if(port<0 || port>65535)throw new ArgumentOutOfRangeException("port");
            return new LoopbackConfig(true,host,address,port,null);
        }
        public IPAddress Endpoint()
        {
            IPAddress parsed;
            if(Address!=null && IPAddress.TryParse(Address,out parsed) && parsed.AddressFamily==AddressFamily.InterNetwork)return parsed;
            return IPAddress.Loopback;
        }
    }
    // One framed message per packet: 4-byte little-endian length plus payload.
    // The reader owns the stream; callers only see whole payloads.
    public static class LoopbackFrames
    {
        public const int MaxPayload=64*1024;
        public static byte[] Encode(byte[] payload,int offset,int count)
        {
            if(payload==null)throw new ArgumentNullException("payload");
            if(count<=0 || count>MaxPayload || offset<0 || offset>payload.Length-count)throw new ArgumentOutOfRangeException("count");
            var frame=new byte[4+count];
            frame[0]=(byte)count;frame[1]=(byte)(count>>8);frame[2]=(byte)(count>>16);frame[3]=(byte)(count>>24);
            Buffer.BlockCopy(payload,offset,frame,4,count);return frame;
        }
        public static void Write(Stream stream,byte[] payload,int count)
        {
            var frame=Encode(payload,0,count);stream.Write(frame,0,frame.Length);stream.Flush();
        }
        // False means a clean end of stream at a frame boundary. A truncated or
        // oversized frame is a protocol error and closes the connection.
        public static bool Read(Stream stream,out byte[] payload)
        {
            payload=null;var header=new byte[4];int read=ReadExact(stream,header,4);
            if(read==0)return false;
            if(read!=4)throw new InvalidDataException("Truncated loopback frame header.");
            int length=header[0]|(header[1]<<8)|(header[2]<<16)|(header[3]<<24);
            if(length<=0 || length>MaxPayload)throw new InvalidDataException("Invalid loopback frame length "+length+".");
            var body=new byte[length];
            if(ReadExact(stream,body,length)!=length)throw new InvalidDataException("Truncated loopback frame body.");
            payload=body;return true;
        }
        static int ReadExact(Stream stream,byte[] buffer,int count)
        {
            int total=0;
            while(total<count)
            {
                int n=stream.Read(buffer,total,count-total);
                if(n<=0)break;total+=n;
            }
            return total;
        }
    }
    public sealed class LoopbackEvent
    {
        public ulong From;public byte[] Bytes;public bool Connected,Disconnected,Reliable;
    }
    public sealed class LoopbackNetworkProfile
    {
        public readonly int RoundTripMs,Seed,JitterMs;public readonly double LossPercent;
        public bool Enabled{get{return RoundTripMs!=0 || LossPercent!=0 || JitterMs!=0;}}
        public LoopbackNetworkProfile(int roundTripMs,double lossPercent,int seed):this(roundTripMs,lossPercent,seed,0){}
        // Jitter adds a uniform random extra one-way delay in [0, jitterMs] per packet.
        public LoopbackNetworkProfile(int roundTripMs,double lossPercent,int seed,int jitterMs)
        {
            if(roundTripMs<0 || roundTripMs>5000 || Double.IsNaN(lossPercent) || lossPercent<0 || lossPercent>100 || jitterMs<0 || jitterMs>2000)throw new ArgumentOutOfRangeException("network profile");
            RoundTripMs=roundTripMs;LossPercent=lossPercent;Seed=seed;JitterMs=jitterMs;
        }
        public static LoopbackNetworkProfile Parse(string rtt,string loss,string seed)
        {return Parse(rtt,loss,seed,null);}
        public static LoopbackNetworkProfile Parse(string rtt,string loss,string seed,string jitter)
        {
            int ms=0,s=1907,jitterMs=0;double percent=0;
            if(!String.IsNullOrEmpty(rtt) && !Int32.TryParse(rtt,out ms) ||
               !String.IsNullOrEmpty(loss) && !Double.TryParse(loss,NumberStyles.Float,CultureInfo.InvariantCulture,out percent) ||
               !String.IsNullOrEmpty(seed) && !Int32.TryParse(seed,out s) ||
               !String.IsNullOrEmpty(jitter) && !Int32.TryParse(jitter,out jitterMs))throw new ArgumentException("Invalid loopback RTT/loss/seed/jitter.");
            return new LoopbackNetworkProfile(ms,percent,s,jitterMs);
        }
    }
    // Application-packet impairment model, driven by a monotonic clock. There
    // are no sleeps on Unity's thread. Reliable loss models retransmission and
    // ordered head-of-line delay; unreliable loss discards the accepted packet.
    public sealed class LoopbackFaultQueue
    {
        sealed class Pending {public byte[] Bytes;public bool Reliable;public double At;public long Order;}
        readonly LoopbackNetworkProfile profile;readonly Random random;
        readonly List<Pending> pending=new List<Pending>();long order;double reliableAt;int queuedBytes;
        public int Dropped,Retransmissions;
        public LoopbackFaultQueue(LoopbackNetworkProfile profile,int direction)
        {this.profile=profile;random=new Random(unchecked(profile.Seed+direction*7919));}
        public bool Add(byte[] bytes,bool reliable,double now)
        {
            bool lost=random.NextDouble()*100<profile.LossPercent;
            if(lost && !reliable){Dropped++;return true;}
            if(queuedBytes>64*1024*1024-bytes.Length)return false;
            double at=now+profile.RoundTripMs/2000.0;
            // Jitter is drawn only when enabled so jitter=0 keeps the old timing and random sequence.
            if(profile.JitterMs>0)at+=random.NextDouble()*profile.JitterMs/1000.0;
            if(lost){Retransmissions++;at+=Math.Max(.1,profile.RoundTripMs/1000.0);}
            if(reliable){at=Math.Max(at,reliableAt);reliableAt=at;}
            pending.Add(new Pending {Bytes=(byte[])bytes.Clone(),Reliable=reliable,At=at,Order=++order});queuedBytes+=bytes.Length;return true;
        }
        public bool Take(double now,out byte[] bytes,out bool reliable)
        {
            int best=-1;
            for(int i=0;i<pending.Count;i++)if(pending[i].At<=now && (best<0 || pending[i].At<pending[best].At || pending[i].At==pending[best].At && pending[i].Order<pending[best].Order))best=i;
            if(best<0){bytes=null;reliable=false;return false;}
            var item=pending[best];pending.RemoveAt(best);queuedBytes-=item.Bytes.Length;bytes=item.Bytes;reliable=item.Reliable;return true;
        }
        public void Clear(){pending.Clear();queuedBytes=0;reliableAt=0;}
    }
    // The reader thread only enqueues. Gameplay, the stable node and Unity stay
    // on the main thread and drain through StableGameBridge.ReadBudgeted.
    public sealed class LoopbackTransport
    {
        readonly LoopbackConfig config;readonly object gate=new object(),sendGate=new object();
        readonly Queue<LoopbackEvent> queue=new Queue<LoopbackEvent>();
        readonly Queue<byte[]> sendQueue=new Queue<byte[]>();
        const int MaxQueuedMessages=8192,MaxSendBytes=64*1024*1024;
        int sendBytes;
        Thread thread;TcpListener listener;TcpClient clientConnection;NetworkStream stream;
        volatile bool running,ready;string error;int boundPort;
        readonly LoopbackNetworkProfile profile;readonly LoopbackFaultQueue incoming,outgoing;
        static double Clock{get{return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;}}
        public LoopbackTransport(LoopbackConfig config):this(config,null){}
        public LoopbackTransport(LoopbackConfig config,LoopbackNetworkProfile network)
        {
            this.config=config;
            profile=config==null || config.Host?new LoopbackNetworkProfile(0,0,1907):network??LoopbackNetworkProfile.Parse(Environment.GetEnvironmentVariable("TLD_MP_RTT_MS"),Environment.GetEnvironmentVariable("TLD_MP_LOSS_PERCENT"),Environment.GetEnvironmentVariable("TLD_MP_NET_SEED"),Environment.GetEnvironmentVariable("TLD_MP_JITTER_MS"));
            if(profile.Enabled){incoming=new LoopbackFaultQueue(profile,1);outgoing=new LoopbackFaultQueue(profile,2);}
        }
        public string NetworkDescription{get{return "RTT="+profile.RoundTripMs+"ms jitter="+profile.JitterMs+"ms loss="+profile.LossPercent.ToString(CultureInfo.InvariantCulture)+"% per direction; reliable loss adds retransmission delay";}}
        public bool Ready{get{return ready;}}
        public int BoundPort{get{return boundPort;}}
        public string Error{get{lock(gate)return error;}}
        public bool Start()
        {
            if(config==null || !config.Enabled)return false;
            if(thread!=null)return true;
            running=true;
            if(config.Host)
            {
                try
                {
                    listener=new TcpListener(IPAddress.Loopback,config.Port);
                    listener.Start();
                    boundPort=((IPEndPoint)listener.LocalEndpoint).Port;
                    ready=true;
                }
                catch(Exception e)
                {
                    SetError("Could not listen on 127.0.0.1:"+config.Port+": "+e.Message);
                    running=false;if(listener!=null)try{listener.Stop();}catch{}listener=null;return false;
                }
            }
            thread=new Thread(Run);thread.IsBackground=true;thread.Name="TLD loopback "+config.Port;thread.Start();return true;
        }
        public void Stop()
        {
            running=false;ready=false;
            try{if(listener!=null)listener.Stop();}catch{}
            TcpClient c;NetworkStream s;lock(gate){c=clientConnection;s=stream;clientConnection=null;stream=null;Monitor.PulseAll(gate);}
            try{if(s!=null)s.Close();}catch{}
            try{if(c!=null)c.Close();}catch{}
            var t=thread;thread=null;
            if(t!=null && t!=Thread.CurrentThread)try{t.Join(1000);}catch{}
            lock(gate){queue.Clear();sendQueue.Clear();sendBytes=0;if(incoming!=null){incoming.Clear();outgoing.Clear();}Monitor.PulseAll(gate);}
        }
        public bool Send(ulong peer,byte[] bytes)
        {return Send(peer,bytes,true);}
        public bool Send(ulong peer,byte[] bytes,bool reliable)
        {
            if(bytes==null || bytes.Length==0 || bytes.Length>LoopbackFrames.MaxPayload-2 || peer!=config.Peer)return false;
            var wire=new byte[bytes.Length+2];wire[0]=167;wire[1]=(byte)(reliable?1:0);Buffer.BlockCopy(bytes,0,wire,2,bytes.Length);
            lock(gate)
            {
                if(!running || stream==null)return false;
                bool accepted;
                if(outgoing!=null)accepted=outgoing.Add(wire,reliable,Clock);
                else
                {
                    accepted=sendQueue.Count<MaxQueuedMessages && sendBytes<=MaxSendBytes-wire.Length;
                    if(accepted){sendQueue.Enqueue(wire);sendBytes+=wire.Length;}
                }
                if(accepted)Monitor.PulseAll(gate);
                return accepted;
            }
        }
        bool Write(NetworkStream target,byte[] bytes)
        {
            try{lock(sendGate)LoopbackFrames.Write(target,bytes,bytes.Length);return true;}
            catch(Exception e){if(running)SetError("Loopback send failed: "+e.Message);return false;}
        }
        // Socket writes can block under TCP backpressure. This worker is the only
        // writer for the connection and never runs on Unity's game thread.
        void WriteLoop(NetworkStream target)
        {
            while(running)
            {
                byte[] bytes=null;bool reliable;
                lock(gate)
                {
                    if(!ReferenceEquals(stream,target))return;
                    if(outgoing!=null)outgoing.Take(Clock,out bytes,out reliable);
                    else if(sendQueue.Count>0){bytes=sendQueue.Dequeue();sendBytes-=bytes.Length;}
                    if(bytes==null){Monitor.Wait(gate,10);continue;}
                }
                if(!Write(target,bytes)){try{target.Close();}catch{}return;}
            }
        }
        public bool TryDequeue(out LoopbackEvent item)
        {
            lock(gate)
            {
                byte[] bytes;bool reliable;
                if(incoming!=null && incoming.Take(Clock,out bytes,out reliable))
                {item=new LoopbackEvent {From=config.Peer,Bytes=bytes,Reliable=reliable};Monitor.PulseAll(gate);return true;}
                if(queue.Count==0){item=null;return false;}
                item=queue.Dequeue();Monitor.PulseAll(gate);return true;
            }
        }
        void Run(){if(config.Host)RunHost();else RunClient();}
        void RunHost()
        {
            try
            {
                while(running)
                {
                    TcpClient c;
                    try{c=listener.AcceptTcpClient();}
                    catch(Exception){if(!running)break;throw;}
                    if(!running){Close(c);break;}
                    bool busy;
                    lock(gate)
                    {
                        busy=clientConnection!=null;
                        if(!busy)
                        {
                            c.NoDelay=true;clientConnection=c;stream=c.GetStream();Monitor.PulseAll(gate);
                        }
                    }
                    // Only two real instances are supported. A third connection is
                    // closed instead of sharing the client identity.
                    if(busy){Close(c);continue;}
                    Enqueue(new LoopbackEvent {From=config.Peer,Connected=true});
                    Serve(c);
                    if(!running)break;
                }
            }
            catch(Exception e){if(running)SetError("Loopback listener failed: "+e.Message);}
            finally{ready=false;}
        }
        void RunClient()
        {
            while(running)
            {
                try
                {
                    var c=new TcpClient();
                    c.NoDelay=true;
                    c.Connect(config.Endpoint(),config.Port);
                    lock(gate){clientConnection=c;stream=c.GetStream();Monitor.PulseAll(gate);}
                    ready=true;Enqueue(new LoopbackEvent {From=config.Peer,Connected=true});
                    Serve(c);
                    ready=false;
                }
                catch(Exception)
                {
                    ready=false;
                    if(!running)break;
                }
                // The host may not be up yet. Retry until the process stops or the
                // socket connects; the stable handshake starts after that.
                for(int i=0;i<10 && running;i++)Thread.Sleep(100);
            }
            ready=false;
        }
        void Serve(TcpClient c)
        {
            Thread writer=null;
            try
            {
                NetworkStream s;lock(gate)s=stream;
                if(s==null)return;
                writer=new Thread(delegate(){WriteLoop(s);});writer.IsBackground=true;writer.Name="TLD loopback writer";writer.Start();
                while(running)
                {
                    byte[] payload;
                    if(!LoopbackFrames.Read(s,out payload))break;
                    if(payload.Length<3 || payload[0]!=167 || payload[1]>1)throw new InvalidDataException("Loopback wire version/reliability mismatch.");
                    var bytes=new byte[payload.Length-2];Buffer.BlockCopy(payload,2,bytes,0,bytes.Length);
                    if(incoming!=null)
                    {lock(gate){while(running && !incoming.Add(bytes,payload[1]!=0,Clock))Monitor.Wait(gate,100);}}
                    else Enqueue(new LoopbackEvent {From=config.Peer,Bytes=bytes,Reliable=payload[1]!=0});
                }
            }
            catch(Exception e){if(running)SetError("Loopback read failed: "+e.Message);}
            finally
            {
                lock(gate){clientConnection=null;stream=null;sendQueue.Clear();sendBytes=0;if(incoming!=null){incoming.Clear();outgoing.Clear();}Monitor.PulseAll(gate);}
                Close(c);
                if(writer!=null && writer!=Thread.CurrentThread)writer.Join(1000);
                if(!running)ready=false;
                if(running)Enqueue(new LoopbackEvent {From=config.Peer,Disconnected=true});
            }
        }
        void Enqueue(LoopbackEvent item)
        {
            lock(gate)
            {
                // The reader waits for the main thread to drain the bounded queue.
                // TCP applies backpressure to the separate socket writer.
                while(running && queue.Count>=MaxQueuedMessages)Monitor.Wait(gate,100);
                if(running)queue.Enqueue(item);
            }
        }
        void SetError(string text){lock(gate){if(error==null)error=text;}}
        static void Close(TcpClient c){if(c==null)return;try{c.Close();}catch{}}
    }
    public static class Loopback
    {
        static readonly string setting=Environment.GetEnvironmentVariable("TLD_MP_LOOPBACK");
        public static readonly LoopbackConfig Config=String.IsNullOrEmpty(setting)?null:LoopbackConfig.Parse(setting);
        public static readonly bool Active=Config!=null && Config.Enabled;
        static LoopbackTransport transport;static string startError;
        public static LoopbackTransport Transport{get{return transport;}}
        public static bool IsHost{get{return Active && Config.Host;}}
        public static ulong Own{get{return Active?Config.Own:0;}}
        public static ulong HostID{get{return Config==null?0:Config.HostIdentity;}}
        public static ulong Peer{get{return Config==null?0:Config.Peer;}}
        public static ulong Lobby{get{return Config==null?0:Config.Lobby;}}
        public static string Error
        {
            get
            {
                string text=Config==null?null:Config.Error;
                if(text==null)text=startError;
                if(text==null && transport!=null)text=transport.Error;
                return text;
            }
        }
        public static bool Ready{get{return transport!=null && transport.Ready;}}
        public static bool Send(ulong peer,byte[] bytes){var value=transport;return value!=null && value.Send(peer,bytes);}
        public static bool Send(ulong peer,byte[] bytes,bool reliable){var value=transport;return value!=null && value.Send(peer,bytes,reliable);}
        public static bool Start()
        {
            if(!Active || Config.Error!=null)return false;
            if(transport!=null)return true;
            LoopbackTransport value;
            try{value=new LoopbackTransport(Config);}catch(Exception e){startError=e.Message;return false;}
            if(!value.Start()){startError=value.Error;return false;}
            transport=value;return true;
        }
        public static void Stop(){var value=transport;transport=null;if(value!=null)value.Stop();}
    }
}
