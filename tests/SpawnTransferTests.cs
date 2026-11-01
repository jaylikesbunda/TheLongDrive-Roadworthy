using System;
using System.Collections.Generic;
using TldMp;

public static class SpawnTransferTests
{
    sealed class Packet {public ulong From,To;public byte[] Bytes;public bool Reliable;}
    sealed class Lab
    {
        public Dictionary<ulong,StableNode> Nodes=new Dictionary<ulong,StableNode>();public List<Packet> Queue=new List<Packet>();
        public double Now;
        public Lab()
        {
            foreach(ulong id in new ulong[]{10,20,30})Add(id,"same-build");
            Nodes[10].SnapshotRequested=delegate(ulong id){Nodes[10].BeginSnapshot(id,new byte[]{1,2,3});};
        }
        public StableNode Add(ulong id,string build)
        {
            var n=new StableNode(id,10,999,id+123,build,100);Nodes[id]=n;
            n.Send=delegate(ulong target,byte[] bytes,bool reliable){Queue.Add(new Packet {From=id,To=target,Bytes=bytes,Reliable=reliable});};
            n.ApplySnapshot=delegate(byte[] data){};
            return n;
        }
        public void Pump(){int guard=0;while(Queue.Count>0){if(++guard>20000)throw new Exception("loop");var p=Queue[0];Queue.RemoveAt(0);if(Nodes.ContainsKey(p.To))Nodes[p.To].Receive(p.From,p.Bytes,Now);}}
        public void PumpExcept(Func<Packet,bool> drop)
        {
            int guard=0;
            while(Queue.Count>0)
            {
                if(++guard>20000)throw new Exception("loop");
                var p=Queue[0];Queue.RemoveAt(0);if(drop(p))continue;
                if(Nodes.ContainsKey(p.To))Nodes[p.To].Receive(p.From,p.Bytes,Now);
            }
        }
        public void Connect(){foreach(var n in Nodes.Values)n.Tick(Now);Pump();Nodes[20].RequestSnapshot();Nodes[30].RequestSnapshot();Pump();}
        public int DropWhere(Func<Packet,bool> pred)
        {
            int removed=0;for(int i=Queue.Count-1;i>=0;i--)if(pred(Queue[i])){Queue.RemoveAt(i);removed++;}
            Pump();return removed;
        }
        public int CountWhere(Func<Packet,bool> pred)
        {
            int count=0;foreach(var p in Queue)if(pred(p))count++;return count;
        }
    }
    static StableOp Op(Packet p){var m=StableMessage.Decode(p.Bytes);return m==null?(StableOp)0:m.Op;}
    static int checks;
    static void Check(bool ok,string what){checks++;if(!ok)throw new Exception("FAIL: "+what);}
    static byte[] Payload(int seed,int length)
    {
        var bytes=new byte[length];var rnd=new Random(seed);rnd.NextBytes(bytes);return bytes;
    }
    static int ChunkOffset(Packet p)
    {
        var m=StableMessage.Decode(p.Bytes);return m==null||m.Op!=StableOp.SpawnChunk?-1:BitConverter.ToInt32(m.Data,4);
    }
    static byte[] BigChunk(ulong seq,int total)
    {
        var m=new StableMessage{Op=StableOp.SpawnChunk,Lobby=999,Session=0,Origin=0,Epoch=0,Sequence=seq,
            Data=new byte[40+StableNode.SpawnChunkPayload]};
        Buffer.BlockCopy(BitConverter.GetBytes(total),0,m.Data,0,4);
        return m.Encode();
    }
    public static int Main()
    {
        try
        {
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
                int received=0,acked=0;
                host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){received++;return true;};
                client.SpawnAcknowledged=delegate(ulong sequence){acked++;};
                Check(client.SendSpawn(1,Payload(1,58000)),"58000 refused");
                lab.Pump();
                Check(lab.CountWhere(delegate(Packet p){return Op(p)==StableOp.Spawn;})==0,"58000 used chunk path");
                Check(received==1 && acked==1,"58000 native path broken");
                Check(lab.CountWhere(delegate(Packet p){return Op(p)==StableOp.SpawnChunk;})==0,"58000 emitted chunks");
            }
            {
                var lab=new Lab();lab.Connect();var client=lab.Nodes[20];
                Check(client.SendSpawn(1,Payload(2,58001)),"58001 refused");
                Check(lab.CountWhere(delegate(Packet p){return Op(p)==StableOp.SpawnChunk;})==1,"58001 did not emit first chunk");
                var lab2=new Lab();lab2.Connect();var client2=lab2.Nodes[20];
                Check(client2.SendSpawn(1,Payload(3,StableNode.MaxSpawnBytes)),"16MiB refused");
                Check(!client2.SendSpawn(1,Payload(4,StableNode.MaxSpawnBytes+1)),"16MiB+1 accepted");
            }
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
                var bytes=Payload(5,120000);int calls=0;byte[] got=null;int acked=0;
                host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){calls++;got=data;return true;};
                client.SpawnAcknowledged=delegate(ulong sequence){if(sequence==7)acked++;};
                Check(client.SendSpawn(7,bytes),"large transaction refused");
                lab.Pump();
                Check(calls==1 && got!=null && got.Length==bytes.Length,"payload not reconstructed");
                for(int i=0;i<bytes.Length;i++)if(got[i]!=bytes[i])throw new Exception("payload byte mismatch at "+i);
                Check(acked==1,"final SpawnAck missing");
            }
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
                var bytes=Payload(6,120000);int calls=0;
                host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){calls++;return true;};
                client.SendSpawn(9,bytes);
                lab.PumpExcept(delegate(Packet p){return Op(p)==StableOp.SpawnChunk && ChunkOffset(p)==48000;});
                Check(calls==0,"apply ran before all chunks");
                Check(client.SendSpawn(9,bytes),"idempotent retry refused");
                lab.Pump();
                Check(calls==1,"transaction not completed after retry");
            }
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
                var bytes=Payload(7,120000);int calls=0,acked=0;
                host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){calls++;return true;};
                client.SpawnAcknowledged=delegate(ulong sequence){acked++;};
                client.SendSpawn(11,bytes);
                lab.PumpExcept(delegate(Packet p){return Op(p)==StableOp.SpawnAck;});
                Check(calls==1 && acked==0,"premature or missing commit");
                Check(client.SendSpawn(11,bytes),"last-chunk retry refused");
                lab.Pump();
                Check(calls==1,"duplicate commit ran SpawnReceived again");
                Check(acked==1,"lost final ack not recovered");
            }
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
                int calls=0;host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){calls++;return true;};
                var bytes=Payload(8,60000);
                client.SendSpawn(13,bytes);
                var p=lab.Queue.Find(q=>Op(q)==StableOp.SpawnChunk);
                var m=StableMessage.Decode(p.Bytes);Buffer.BlockCopy(BitConverter.GetBytes(-1),0,m.Data,0,4);p.Bytes=m.Encode();
                lab.Pump();
                Check(calls==0,"corrupt total applied");
                var bad=new StableMessage{Op=StableOp.SpawnChunk,Lobby=999,Session=143,Origin=20,Epoch=143,Sequence=13,
                    Data=new byte[40+100]};
                Buffer.BlockCopy(BitConverter.GetBytes(60000),0,bad.Data,0,4);Buffer.BlockCopy(BitConverter.GetBytes(1),0,bad.Data,4,4);
                host.Receive(20,bad.Encode(),lab.Now);
                Check(calls==0,"non-multiple offset applied");
            }
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
                var bytes=Payload(9,120000);int calls=0;
                host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){calls++;return true;};
                client.SendSpawn(15,bytes);
                Packet first=null;int firstAt=-1;
                for(int i=0;i<lab.Queue.Count;i++)if(Op(lab.Queue[i])==StableOp.SpawnChunk && ChunkOffset(lab.Queue[i])==0){first=lab.Queue[i];firstAt=i;break;}
                lab.Pump();
                Check(calls==1,"transaction did not complete");
                host.Receive(20,first.Bytes,lab.Now); // duplicate earlier offset, same bytes -> re-ack only
                Check(calls==1,"duplicate chunk ran the callback again");
            }
            {
                var lab=new Lab();lab.Connect();var client=lab.Nodes[20];
                client.SendSpawn(17,Payload(10,58000));
                var stale=new StableMessage{Op=StableOp.SpawnAck,Lobby=999,Session=101,Origin=10,Epoch=999,Sequence=17,Data=new byte[0]};
                int acked=0;client.SpawnAcknowledged=delegate(ulong sequence){acked++;};
                client.Receive(10,stale.Encode(),lab.Now);
                Check(acked==0,"stale-session SpawnAck released transaction");
            }
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];
                host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){return true;};
                var peer20=lab.Nodes[10].Peers[20];var peer30=lab.Nodes[10].Peers[30];
                var a=BigChunk(21,StableNode.MaxSpawnBytes);
                var am=StableMessage.Decode(a);am.Session=peer20.Session;am.Origin=20;am.Epoch=peer20.Session;
                host.Receive(20,am.Encode(),lab.Now);
                var b=BigChunk(31,StableNode.MaxSpawnBytes);
                var bm=StableMessage.Decode(b);bm.Session=peer30.Session;bm.Origin=30;bm.Epoch=peer30.Session;
                host.Receive(30,bm.Encode(),lab.Now);
                Check(peer30.SpawnIn==null,"aggregate cap allowed a second 16MiB allocation");
            }
            // Apply callback rejects the complete payload, then a last-chunk
            // duplicate retries only the callback: no byte transfer restarts.
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
                var bytes=Payload(11,120000);int calls=0;
                host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){return ++calls>1;};
                client.SendSpawn(19,bytes);
                lab.Pump();
                Check(calls==1 && lab.CountWhere(delegate(Packet p){return Op(p)==StableOp.SpawnAck;})==0,"rejected apply acked");
                var m=new StableMessage{Op=StableOp.SpawnChunk,Lobby=999,Origin=20,Sequence=19,Data=new byte[40+24000]};
                var peer20=host.Peers[20];m.Session=peer20.Session;m.Epoch=peer20.Session;
                Buffer.BlockCopy(BitConverter.GetBytes(120000),0,m.Data,0,4);
                Buffer.BlockCopy(BitConverter.GetBytes(96000),0,m.Data,4,4);
                byte[] wholeHash;using(var sha=new System.Security.Cryptography.SHA256Managed())wholeHash=sha.ComputeHash(bytes);
                Buffer.BlockCopy(wholeHash,0,m.Data,8,32);
                Buffer.BlockCopy(bytes,96000,m.Data,40,24000);
                host.Receive(20,m.Encode(),lab.Now);
                Check(calls==2,"last-chunk duplicate retried callback only");
                int acks=0;lab.Pump();foreach(var p in lab.Queue)if(Op(p)==StableOp.SpawnAck)acks++;
                // SpawnAck emitted during the receive -> drain queue
                Check(lab.Queue.Count==0,"queue drained");
            }
            // Small spawn refused while a chunk transfer is inflight; sequence 0 refused.
            {
                var lab=new Lab();lab.Connect();var client=lab.Nodes[20];
                Check(client.SendSpawn(21,Payload(12,120000)),"large spawn refused");
                Check(!client.SendSpawn(22,new byte[]{1,2}),"small spawn allowed during inflight");
                Check(!client.SendSpawn(0,new byte[]{1}),"sequence zero accepted");
            }
            // Committed small transaction: a duplicate with different bytes is
            // rejected, identical bytes re-ack without the callback.
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
                int calls=0;
                host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){calls++;return true;};
                client.SendSpawn(23,new byte[]{1,2,3});
                lab.Pump();
                Check(calls==1,"small commit");
                var peer20=host.Peers[20];
                var dup=new StableMessage{Op=StableOp.Spawn,Lobby=999,Session=peer20.Session,Origin=20,Epoch=peer20.Session,Sequence=23,Data=new byte[]{9,9,9}};
                int before=lab.Queue.Count;
                host.Receive(20,dup.Encode(),lab.Now);
                Check(lab.Queue.Count==before,"mismatched duplicate seq acked");
                var same=new StableMessage{Op=StableOp.Spawn,Lobby=999,Session=peer20.Session,Origin=20,Epoch=peer20.Session,Sequence=23,Data=new byte[]{1,2,3}};
                host.Receive(20,same.Encode(),lab.Now);
                Check(lab.Queue.Count==before+1 && calls==1,"identical duplicate re-acked without callback");
            }
            // Stale sequence below the committed watermark never re-applies.
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
                int calls=0;
                host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){calls++;return true;};
                client.SendSpawn(40,new byte[]{1});lab.Pump();
                var peer20=host.Peers[20];
                var old=new StableMessage{Op=StableOp.Spawn,Lobby=999,Session=peer20.Session,Origin=20,Epoch=peer20.Session,Sequence=5,Data=new byte[]{1}};
                host.Receive(20,old.Encode(),lab.Now);
                Check(calls==1,"below-watermark sequence re-applied");
            }
            // Lost final SpawnAck: idempotent retry of the same sequence+bytes
            // resends the native envelope; the guest ACK event still fires once.
            {
                var lab=new Lab();lab.Connect();var host=lab.Nodes[10];var client=lab.Nodes[20];
                int calls=0,acks=0;
                host.SpawnReceived=delegate(ulong source,ulong sequence,byte[] data){calls++;return true;};
                client.SpawnAcknowledged=delegate(ulong sequence){acks++;};
                var payload=new byte[]{7,8,9};
                Check(client.SendSpawn(70,payload),"first small send");
                lab.PumpExcept(delegate(Packet p){return Op(p)==StableOp.SpawnAck;});
                Check(calls==1 && acks==0,"ack lost, mutation once");
                Check(client.SendSpawn(70,payload),"same seq+bytes retry resends");
                lab.Pump();
                Check(calls==1 && acks==1,"retry delivered final ack once");
                Check(client.SendSpawn(71,new byte[]{1}),"next seq after ACK accepted");
                lab.Pump();
                Check(calls==2,"next transaction applies normally");
            }
            // While a small spawn awaits its ACK, a changed payload or a large
            // transfer must be refused.
            {
                var lab=new Lab();lab.Connect();var client=lab.Nodes[20];
                Check(client.SendSpawn(72,new byte[]{1,2}),"small send");
                Check(!client.SendSpawn(72,new byte[]{9,9}),"changed bytes for pending seq refused");
                Check(!client.SendSpawn(73,new byte[]{3}),"new seq while pending refused");
                Check(!client.SendSpawn(74,Payload(13,120000)),"large while small pending refused");
            }
            Console.WriteLine("PASS: spawn transfer "+checks+" checks (envelope bounds, chunked window, lost/duplicate/malformed chunks, stale ack, aggregate cap).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
