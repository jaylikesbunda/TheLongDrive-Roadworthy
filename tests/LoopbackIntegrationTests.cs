using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TldMp;

// Two production StableNode peers over real loopback sockets in one process.
// No Unity, Steam, serializer or lobby code participates: this exercises the
// transport framing, handshake, snapshot, journal replay and post-join traffic.
public static class LoopbackIntegrationTests
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static bool Same(byte[] a,byte[] b)
    {
        if(a==null || b==null || a.Length!=b.Length)return false;
        for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;
        return true;
    }
    static void WaitReady(LoopbackTransport transport)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);
        while(!transport.Ready && DateTime.UtcNow<deadline)Thread.Sleep(5);
        Check(transport.Ready,"Loopback socket became ready.");
    }
    const int MaxWaitIterations=10000; // 10 seconds of wall time with the 1ms pump cadence
    public static int Main()
    {
        LoopbackTransport hostTransport=null,clientTransport=null;
        try
        {
            var hostConfig=LoopbackConfig.For(true,"127.0.0.1",0);
            hostTransport=new LoopbackTransport(hostConfig);
            Check(hostTransport.Start(),"Host listener starts.");
            Check(hostTransport.BoundPort>0,"Host bound an ephemeral port.");
            // Keep the physical socket localhost, but route both application
            // directions through the client's deterministic fault queue. This
            // exercises latency, loss and reliability metadata end to end.
            var profile=new LoopbackNetworkProfile(200,1,1907);
            clientTransport=new LoopbackTransport(LoopbackConfig.For(false,"127.0.0.1",hostTransport.BoundPort),profile);
            Check(clientTransport.Start(),"Client transport starts.");
            WaitReady(clientTransport);

            ulong lobby=LoopbackConfig.LobbyID;
            var host=new StableNode(LoopbackConfig.HostID,LoopbackConfig.HostID,lobby,101,"loopback-test",100);
            var client=new StableNode(LoopbackConfig.ClientID,LoopbackConfig.HostID,lobby,202,"loopback-test",100);
            var world=new byte[300000];new Random(4409).NextBytes(world);
            var order=new List<string>();var received=new List<byte[]>();var hostReceived=new List<byte[]>();
            var mapExpected=new Dictionary<string,byte[]>();var mapApplied=new HashSet<string>();
            int journaled=0,reliableToHost=0,reliableToClient=0,unreliableToHost=0;double now=0;bool requested=false;var stopwatch=Stopwatch.StartNew();

            host.Send=delegate(ulong peer,byte[] bytes,bool reliable){hostTransport.Send(peer,bytes,reliable);};
            client.Send=delegate(ulong peer,byte[] bytes,bool reliable){clientTransport.Send(peer,bytes,reliable);};
            host.SnapshotRequested=delegate(ulong peer)
            {
                host.BeginSnapshot(peer,world);
                // Produced after Begin and before the client ACK: this must be
                // journaled, not sent alongside the live stream.
                host.PublishNative(new byte[]{41,1,2,3});
                host.PublishNative(new byte[]{42,4,5,6});
                StablePeer state;if(host.Peers.TryGetValue(peer,out state))journaled=state.Journal.Count;
            };
            client.ApplySnapshot=delegate(byte[] bytes){order.Add("snapshot");Check(Same(bytes,world),"Snapshot transfer is byte-exact.");};
            client.Deliver=delegate(ulong origin,byte[] bytes)
            {
                Check(origin==host.Own,"Client only receives host-originated stable data here.");
                order.Add("event:"+bytes[0]);received.Add(bytes);
            };
            host.Deliver=delegate(ulong origin,byte[] bytes)
            {
                Check(origin==client.Own,"Host sees the client identity.");
                hostReceived.Add(bytes);
            };
            host.MapRequested=delegate(ulong peer,string key)
            {
                Check(peer==client.Own && mapExpected.ContainsKey(key),"Host serves only requested map keys to the joining peer.");
                return mapExpected[key];
            };
            client.MapApplied=delegate(string key,byte[] bytes)
            {
                Check(mapExpected.ContainsKey(key) && Same(bytes,mapExpected[key]),"Map transfer is byte-exact under queued reliable traffic.");
                mapApplied.Add(key);
            };

            Action pump=delegate
            {
                LoopbackEvent item;
                while(hostTransport.TryDequeue(out item))
                {
                    if(item.Bytes==null)continue;
                    if(item.Reliable)reliableToHost++;else unreliableToHost++;
                    if(item.Bytes[0]==254)host.Receive(item.From,item.Bytes,now);else host.Deliver(item.From,item.Bytes);
                }
                while(clientTransport.TryDequeue(out item))
                {
                    if(item.Bytes==null)continue;
                    if(item.Reliable)reliableToClient++;
                    if(item.Bytes[0]==254)client.Receive(item.From,item.Bytes,now);else client.Deliver(item.From,item.Bytes);
                }
            };

            for(int i=0;i<MaxWaitIterations && !(client.Compatible && client.Ready);i++)
            {
                now=stopwatch.Elapsed.TotalSeconds;
                host.Tick(now);client.Tick(now);pump();
                if(client.Compatible && !requested){requested=true;client.RequestSnapshot();}
                Thread.Sleep(1);
            }
            Check(client.Compatible,"Client completed the compatibility handshake.");
            Check(reliableToHost>0 && reliableToClient>0,"Reliable framing metadata survives the real socket in both directions.");
            Check(client.Ready,"Client reached Live after the snapshot and journal.");
            Check(!client.AwaitingSnapshot && !client.Joining,"Client is no longer waiting for a snapshot.");
            Check(host.Peers.ContainsKey(client.Own) && host.Peers[client.Own].Ready,"Host marked the joining peer Ready.");
            Check(journaled>=2,"Post-Begin messages were journaled during the join.");
            Check(order.Count==3 && order[0]=="snapshot" && order[1]=="event:41" && order[2]=="event:42","Snapshot applied before journaled events, in order.");
            Check(received.Count==2 && received[0][0]==41 && received[1][0]==42,"Journal payloads are identical and ordered.");

            host.PublishNative(new byte[]{43,7});
            host.PublishNative(new byte[]{44,8});
            for(int i=0;i<MaxWaitIterations && order.Count<5;i++){now=stopwatch.Elapsed.TotalSeconds;host.Tick(now);client.Tick(now);pump();Thread.Sleep(1);}
            Check(order.Count==5 && order[3]=="event:43" && order[4]=="event:44","Reliable ordered messages after join arrive in order.");

            var owned=new byte[]{19,5,0,0,0,99};
            Check(client.PublishNative(owned),"Client publishes native gameplay bytes to the host.");
            for(int i=0;i<MaxWaitIterations && hostReceived.Count==0;i++){now=stopwatch.Elapsed.TotalSeconds;host.Tick(now);client.Tick(now);pump();Thread.Sleep(1);}
            Check(hostReceived.Count==1 && Same(hostReceived[0],owned),"Host received the ordered native message unchanged.");
            Check(unreliableToHost>0,"Unreliable framing metadata survives the real socket.");

            // Exercise the production map-part/checksum path while many reliable
            // world events share the same ordered stream. Each map is larger than
            // one transport frame and all traffic crosses the 200ms/1% profile.
            for(int map=0;map<4;map++)
            {
                string key="r|0|5|"+map+"|0";var bytes=new byte[220000];new Random(7000+map).NextBytes(bytes);mapExpected.Add(key,bytes);
                client.RequestMap(key);
            }
            const int burst=128;
            for(int i=0;i<burst;i++)
                Check(host.PublishNative(new byte[]{(byte)i,(byte)(i>>8),0x5a}),"Host queues reliable world event during map replies.");
            int beforeBurst=order.Count;
            int targetMaps=mapExpected.Count;
            for(int i=0;i<MaxWaitIterations && (order.Count<beforeBurst+burst || mapApplied.Count<targetMaps);i++)
            {now=stopwatch.Elapsed.TotalSeconds;host.Tick(now);client.Tick(now);pump();Thread.Sleep(1);}
            Check(mapApplied.Count==targetMaps,"All four concurrent multi-frame map replies completed.");
            Check(order.Count==beforeBurst+burst,"Every reliable world event arrived during map replies.");
            for(int i=0;i<burst;i++)
            {
                var payload=received[received.Count-burst+i];
                Check(payload.Length==3 && payload[0]==(byte)i && payload[1]==(byte)(i>>8) && payload[2]==0x5a,"Reliable world events retain exact FIFO order through map traffic.");
            }

            Console.WriteLine("PASS: "+checks+" loopback socket assertions; 200ms RTT/1% per-direction application impairment, reliability metadata, handshake, byte-exact snapshot, deferred journal, concurrent multi-frame map replies with SHA-256 verification, 128 ordered world events and native client-to-host publish over real localhost TCP.");
            hostTransport.Stop();hostTransport=null;
            clientTransport.Stop();clientTransport=null;
            return 0;
        }
        catch(Exception e)
        {
            Console.Error.WriteLine(e);
            if(hostTransport!=null)hostTransport.Stop();
            if(clientTransport!=null)clientTransport.Stop();
            return 1;
        }
    }
}

