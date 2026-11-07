using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TldMp
{
    // Cargo protection exception: a position-only world pose on an object whose
    // NATIVE attachment/physlock state says it is stored or mounted is never a
    // free-body update. Stored objects are re-expressed as the native
    // parent-local message; attached objects suppress the world pose (their own
    // attach/detach message is authoritative). No parenting or Rigidbody writes.
    public enum CargoPoseDisposition { World, LocalStorage, Suppress }
    public static class CargoPosePolicy
    {
        // dataReply: the native reply to one peer's item/POI data request
        // (SetSendUnusualToOneReliable). Its world poses are how that peer learns
        // which parts and cargo exist, so they pass unchanged as in vanilla; the
        // receiver still ignores world poses for parts it already has mounted.
        public static CargoPoseDisposition Classify(bool stored,bool attached,bool dataReply)
        {
            if(dataReply)return CargoPoseDisposition.World;
            return Classify(stored,attached);
        }
        public static CargoPoseDisposition Classify(bool stored,bool attached)
        {
            if(stored)return CargoPoseDisposition.LocalStorage;
            if(attached)return CargoPoseDisposition.Suppress;
            return CargoPoseDisposition.World;
        }
    }

    // Session, handshake, liveness, world-manifest transfer and join journal.
    // There is no ownership/lease/epoch authority here: native gameplay bytes are
    // relayed as opaque transport payloads and native handlers decide everything.
    // 26-28 are the mode-gated experimental world-snapshot frames. They are only
    // ever produced when TLD_MP_EXPERIMENTAL_JOIN=1 and the experimental backend is
    // active; a native session never sends or decodes them as anything else.
    public enum StableOp:byte { Hello=1, Welcome=2, Data=7, Begin=8, Part=9, End=10, Ack=11, Live=12, Reject=13, Resync=14, MapRequest=15, MapPart=16, Heartbeat=17, CarBlood=18, Spawn=19, SpawnAck=20, SpawnChunk=21, SpawnChunkAck=22, RabbitRequest=23, RabbitState=24, Authority=25, ExpSnapshotBegin=26, ExpSnapshotPart=27, ExpSnapshotEnd=28, ExpItemEvent=29 }

    public sealed class StableMessage
    {
        public StableOp Op; public ulong Lobby,Session,Origin,Epoch,Sequence,WorldRevision;
        public string Key="";public byte[] Data=new byte[0];
        public byte[] Encode()
        {
            using(var stream=new MemoryStream()) using(var w=new BinaryWriter(stream,Encoding.UTF8))
            { w.Write((byte)254);w.Write((uint)0x33444C54);w.Write((byte)3);w.Write((byte)Op);w.Write(Lobby);w.Write(Session);w.Write(Origin);w.Write(Epoch);w.Write(Sequence);w.Write(WorldRevision);w.Write(Key);w.Write(Data.Length);w.Write(Data);return stream.ToArray(); }
        }
        public static StableMessage Decode(byte[] bytes)
        {
            if(bytes==null || bytes.Length>60000) return null;
            try { using(var s=new MemoryStream(bytes,false)) using(var r=new BinaryReader(s,Encoding.UTF8))
            {
                if(r.ReadByte()!=254 || r.ReadUInt32()!=0x33444C54 || r.ReadByte()!=3) return null;
                var m=new StableMessage { Op=(StableOp)r.ReadByte(),Lobby=r.ReadUInt64(),Session=r.ReadUInt64(),Origin=r.ReadUInt64(),Epoch=r.ReadUInt64(),Sequence=r.ReadUInt64(),WorldRevision=r.ReadUInt64(),Key=r.ReadString() };
                int n=r.ReadInt32();if(m.Key.Length>256 || n<0 || n>58000 || n!=s.Length-s.Position || (int)m.Op<1 || (int)m.Op>(int)StableOp.ExpItemEvent) return null;
                m.Data=r.ReadBytes(n);return m;
            }} catch(Exception e) { if(e is IOException || e is ArgumentException || e is OverflowException) return null;throw; }
        }
    }
    public sealed class StablePeer
    {
        // Journal entries remember reliability because the bounded journal may only
        // discard or coalesce non-reliable traffic. Reliable entries stay untouched.
        public sealed class Entry
        {
            public byte[] Bytes;public bool Reliable;public int Size;public ulong Key;
        }
        public ulong Session;public bool Ready,Syncing;internal bool BootstrapSent;public uint First,Last;public ulong LastRabbitRequest;
        // Host only: the in-flight transfer is the mode-gated experimental one. A dead
        // experimental transfer may be superseded by a fresh request; a native one
        // keeps the existing per-peer coalescing.
        public bool ExternalSync;
        public double Seen;public ulong Transfer;public List<Entry> Journal=new List<Entry>();public int JournalBytes;
        public bool JournalOverflowNoticed;
        public StableNode.SpawnIn SpawnIn;
        public readonly Dictionary<ulong,StableNode.SpawnDone> SpawnDone=new Dictionary<ulong,StableNode.SpawnDone>();
        public readonly Queue<ulong> SpawnDoneOrder=new Queue<ulong>();
        public ulong SpawnDoneWatermark;
    }
    public sealed class StableNode
    {
        public const double PeerTimeout=20,HeartbeatInterval=.5;
        public const int MaxSnapshotBytes=32*1024*1024,MaxJournalBytes=8*1024*1024;
        public readonly ulong Own,Host,Lobby,Session;
        public bool IsHost {get{return Own==Host;}}
        public bool Ready {get;private set;}
        public bool Compatible {get;private set;}
        // True only between a snapshot request and the host's Begin.
        public bool AwaitingSnapshot {get{return joinPhase==JoinPhase.Requested;}}
        // True after Begin until Live/Ready.
        public bool Joining {get{return joinPhase==JoinPhase.Receiving || joinPhase==JoinPhase.WaitingWorld || joinPhase==JoinPhase.AwaitingLive;}}
        public string Error {get;private set;}
        public uint FirstID,LastID;uint nextID,watermark;
        public ulong HostSession {get;private set;}
        public readonly Dictionary<ulong,StablePeer> Peers=new Dictionary<ulong,StablePeer>();
        readonly Dictionary<ulong,HashSet<ulong>> retired=new Dictionary<ulong,HashSet<ulong>>();
        readonly string fingerprint;
        ulong transfer;double clock,nextHello,nextHeartbeat,lastHost;
        double nextSnapshotRequest;string bootstrapHash;
        bool helloStarted;double helloTime;
        MemoryStream snapshot;ulong snapshotID;byte[] snapshotHash;int snapshotSize;
        public Action<ulong,byte[],bool> Send;
        public Action<ulong,byte[]> Deliver;
        public Action<byte[]> ApplySnapshot;
        public Action<ulong> SnapshotRequested;
        public Action<uint,bool> CarBloodReceived;
        // Experimental backend only: host-issued vehicle motion ownership.
        public Action<uint,ulong,ulong> AuthorityReceived;
        public Action<ulong,byte[]> RabbitRequestReceived;
        public Action<byte[]> RabbitStateReceived;
        public Func<ulong,ulong,byte[],bool> SpawnReceived;
        public Action<ulong> SpawnAcknowledged;
        public Action<ulong,int> SpawnProgress;
        public const int MaxSpawnBytes=16*1024*1024,SpawnChunkPayload=48000;
        public sealed class SpawnIn { public ulong Transaction;public int Total;public byte[] Hash;public byte[] Bytes;public int Received;public bool Verified,SettledRejected; }
        public sealed class SpawnDone { public int Total;public byte[] Hash; }
        const int MaxSpawnDone=16;
        sealed class SpawnOut { public ulong Transaction;public byte[] Bytes;public byte[] Hash;public int Total;public int Next;public int ExpectedAck;public bool AwaitingFinal; }
        SpawnOut spawnOut;
        ulong spawnSmallPending;byte[] spawnSmallBytes;
        public int SpawnIncomingHigh;
        int spawnIncomingBytes;
        public int SpawnIncomingBytes { get { return spawnIncomingBytes; } }
        public bool SendSpawn(ulong sequence,byte[] bytes)
        {
            if(IsHost || !Ready || sequence==0 || bytes==null)return false;
            if(bytes.Length<=58000)
            {
                if(spawnOut!=null)return false;
                if(spawnSmallPending!=0)
                {
                    if(sequence!=spawnSmallPending || !ReferenceEquals(bytes,spawnSmallBytes))return false;
                    var retry=Message(StableOp.Spawn,"",bytes);retry.Sequence=sequence;retry.Epoch=Session;
                    Emit(Host,retry,true);return true;
                }
                var m=Message(StableOp.Spawn,"",bytes);m.Sequence=sequence;m.Epoch=Session;
                spawnSmallPending=sequence;spawnSmallBytes=bytes;
                Emit(Host,m,true);return true;
            }
            if(spawnSmallPending!=0)return false;
            if(bytes.Length>MaxSpawnBytes)return false;
            if(spawnOut!=null)
            {
                if(spawnOut.Transaction!=sequence || !ReferenceEquals(spawnOut.Bytes,bytes))return false;
                EmitSpawnChunk(spawnOut.AwaitingFinal);return true;
            }
            byte[] hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(bytes);
            spawnOut=new SpawnOut {Transaction=sequence,Bytes=bytes,Hash=hash,Total=bytes.Length};
            EmitSpawnChunk(false);return true;
        }
        void EmitSpawnChunk(bool lastAgain)
        {
            var t=spawnOut;if(t==null||t.Total==0)return;
            int offset=lastAgain?((t.Total-1)/SpawnChunkPayload)*SpawnChunkPayload:t.Next;
            int n=Math.Min(SpawnChunkPayload,t.Total-offset);
            var payload=new byte[40+n];
            Buffer.BlockCopy(BitConverter.GetBytes(t.Total),0,payload,0,4);
            Buffer.BlockCopy(BitConverter.GetBytes(offset),0,payload,4,4);
            Buffer.BlockCopy(t.Hash,0,payload,8,32);
            Buffer.BlockCopy(t.Bytes,offset,payload,40,n);
            var m=Message(StableOp.SpawnChunk,"",payload);m.Sequence=t.Transaction;m.Epoch=Session;
            t.ExpectedAck=offset+n;
            Emit(Host,m,true);
        }
        void AckSpawnChunk(StableMessage m)
        {
            if(!Ready || m.Origin!=Host || m.Epoch!=Session || m.Data.Length!=4)return;
            var t=spawnOut;if(t==null || t.Transaction!=m.Sequence)return;
            int next=BitConverter.ToInt32(m.Data,0);
            if(next!=t.ExpectedAck)return;
            if(t.AwaitingFinal)return;
            t.Next=next;
            if(SpawnProgress!=null)SpawnProgress(t.Transaction,t.Next);
            if(t.Next<t.Total){EmitSpawnChunk(false);return;}
            t.AwaitingFinal=true;
        }
        void EmitSpawnChunkAck(ulong peer,StablePeer p,ulong transaction,int next)
        {
            var ack=Message(StableOp.SpawnChunkAck,"",BitConverter.GetBytes(next));ack.Sequence=transaction;ack.Epoch=p.Session;Emit(peer,ack,true);
        }
        void EmitSpawnAck(ulong peer,StablePeer p,ulong transaction)
        {
            var ack=Message(StableOp.SpawnAck,"",new byte[0]);ack.Sequence=transaction;ack.Epoch=p.Session;Emit(peer,ack,true);
        }
        static bool Equal(byte[] a,byte[] b)
        {
            if(a==null || b==null || a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;
            return true;
        }
        void ReleaseSpawnIn(StablePeer p)
        {
            if(p.SpawnIn==null)return;spawnIncomingBytes-=p.SpawnIn.Total;p.SpawnIn=null;
        }
        void StoreSpawnDone(StablePeer p,ulong transaction,int total,byte[] hash)
        {
            if(p.SpawnDone.Count>=MaxSpawnDone)
            {
                ulong old=p.SpawnDoneOrder.Dequeue();
                if(p.SpawnDone.Remove(old) && Notice!=null)Notice("Spawn transaction ledger evicted seq="+old);
            }
            if(transaction>p.SpawnDoneWatermark)p.SpawnDoneWatermark=transaction;
            p.SpawnDone[transaction]=new SpawnDone {Total=total,Hash=hash};p.SpawnDoneOrder.Enqueue(transaction);
        }
        void FinishSpawnIn(ulong sender,StablePeer p,SpawnIn t)
        {
            SpawnDone prior;
            if(p.SpawnDone.TryGetValue(t.Transaction,out prior)){ReleaseSpawnIn(p);EmitSpawnAck(sender,p,t.Transaction);return;}
            if(!t.Verified)
            {
                byte[] hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(t.Bytes);
                if(!Equal(hash,t.Hash)){ReleaseSpawnIn(p);TraceJoin("SpawnChunk checksum mismatch seq="+t.Transaction);return;}
                t.Verified=true;
            }
            if(SpawnReceived!=null && SpawnReceived(sender,t.Transaction,t.Bytes))
            {
                StoreSpawnDone(p,t.Transaction,t.Total,t.Hash);
                ReleaseSpawnIn(p);
                EmitSpawnAck(sender,p,t.Transaction);
            }
            else t.SettledRejected=true;
        }
        void ReceiveSpawnChunk(ulong sender,StablePeer p,StableMessage m)
        {
            var d=m.Data;if(!p.Ready || m.Origin!=sender || m.Epoch!=p.Session || m.Sequence==0 || d==null || d.Length<40)return;
            int total=BitConverter.ToInt32(d,0),offset=BitConverter.ToInt32(d,4);
            if(total<1 || total>MaxSpawnBytes || offset<0 || offset>total || offset%SpawnChunkPayload!=0)return;
            int expected=Math.Min(SpawnChunkPayload,total-offset);
            if(expected<=0 || d.Length!=40+expected)return;
            var hash=new byte[32];Buffer.BlockCopy(d,8,hash,0,32);
            SpawnDone done;
            if(p.SpawnDone.TryGetValue(m.Sequence,out done))
            {
                if(done.Total!=total || !Equal(done.Hash,hash))return;
                EmitSpawnChunkAck(sender,p,m.Sequence,offset+expected);
                if(offset+expected==total)EmitSpawnAck(sender,p,m.Sequence);
                return;
            }
            if(m.Sequence<=p.SpawnDoneWatermark)return;
            var t=p.SpawnIn;
            if(t!=null && t.Transaction!=m.Sequence)return;
            if(t==null)
            {
                if(offset!=0 || spawnIncomingBytes+total>MaxSpawnBytes)return;
                t=new SpawnIn {Transaction=m.Sequence,Total=total,Hash=hash,Bytes=new byte[total]};
                p.SpawnIn=t;spawnIncomingBytes+=total;if(spawnIncomingBytes>SpawnIncomingHigh)SpawnIncomingHigh=spawnIncomingBytes;
            }
            else if(t.Total!=total || !Equal(t.Hash,hash))return;
            if(t.SettledRejected)
            {
                int last=((t.Total-1)/SpawnChunkPayload)*SpawnChunkPayload;
                if(offset!=last)return;
                for(int i=0;i<expected;i++)if(t.Bytes[offset+i]!=d[40+i])return;
                t.SettledRejected=false;
                FinishSpawnIn(sender,p,t);
                return;
            }
            if(offset==t.Received)
            {
                Buffer.BlockCopy(d,40,t.Bytes,offset,expected);t.Received+=expected;
                EmitSpawnChunkAck(sender,p,m.Sequence,t.Received);
                if(t.Received==t.Total)FinishSpawnIn(sender,p,t);
                return;
            }
            if(offset<t.Received)
            {
                for(int i=0;i<expected;i++)if(t.Bytes[offset+i]!=d[40+i])return;
                EmitSpawnChunkAck(sender,p,m.Sequence,t.Received);
            }
        }
        public Action<string> Notice;
        public Action<uint> WatermarkChanged;
        public Action<ulong> PeerLeft;
        public byte[] HostConfig=new byte[0];public Action<byte[]> ConfigReceived;
        public Func<ulong,string,byte[]> MapRequested;
        public Action<string,byte[]> MapApplied;
        // Production dispatches generation/apply at the frame boundary instead
        // of doing several native world operations inside one receive batch.
        public bool DeferMapWork;
        public Func<bool> CanEnterWorld;
        enum JoinPhase {None,Requested,Receiving,WaitingWorld,AwaitingLive,Live}
        public Action<string> Trace;
        public string JoinStatus {get{return joinPhase.ToString();}}
        void TraceJoin(string text){if(Trace!=null)Trace(text);}
        JoinPhase joinPhase;
        // Bounded join stages. Entering Requested/Receiving/WaitingWorld/
        // AwaitingLive arms a deadline that only snapshot-stage progress
        // (Begin/Part/End/Ack/map bytes) refreshes; a transport heartbeat keeps
        // the peer alive but never extends a stalled join.
        public const double JoinStageTimeout=30;
        double joinStageProgress;bool joinStageArmed;
        bool hostWorldLoaded;
        public bool HostWorldLoaded
        {
            get{return hostWorldLoaded;}
            set
            {
                if(value && !hostWorldLoaded && joinPhase==JoinPhase.Requested)JoinStageProgress(clock);
                hostWorldLoaded=value;
            }
        }
        public string JoinStage {get{return joinPhase==JoinPhase.Requested && !hostWorldLoaded ? "WaitingHostLoad" : joinPhase.ToString();}}
        public string JoinStageReason {get{return joinPhase==JoinPhase.Requested && !hostWorldLoaded ? "waiting for the host's native world to load" : joinStageArmed ? StageReason(joinPhase) : "";}}
        static string StageReason(JoinPhase phase)
        {
            if(phase==JoinPhase.Requested)return "no snapshot transfer started";
            if(phase==JoinPhase.Receiving)return "snapshot transfer stopped";
            if(phase==JoinPhase.WaitingWorld)return "the world never became enterable";
            return "Live confirmation never arrived";
        }
        void JoinStageProgress(double now)
        {
            if(joinPhase!=JoinPhase.Requested && joinPhase!=JoinPhase.Receiving && joinPhase!=JoinPhase.WaitingWorld && joinPhase!=JoinPhase.AwaitingLive){joinStageArmed=false;return;}
            joinStageProgress=now;joinStageArmed=true;
        }
        ulong joinTransfer;
        const int MapRequestWindow=24;const double MapProgressTimeout=5.0,MapReplySuppression=4.0;
        const int MapServeBatch=4;const double MapServeBudgetMs=8.0;
        sealed class MapTransfer {public ulong Revision;public int Size;public string Hash;public MemoryStream Bytes=new MemoryStream();}
        sealed class MapRequestState {public double LastProgress;public int Attempts;public bool Queued;}
        readonly Dictionary<string,MapTransfer> mapTransfers=new Dictionary<string,MapTransfer>();
        readonly Dictionary<string,MapRequestState> mapRequests=new Dictionary<string,MapRequestState>();
        readonly Dictionary<string,double> mapReplies=new Dictionary<string,double>();
        sealed class MapJob {public ulong Peer,Session;public string Key,ReplyKey;}
        sealed class MapApplyJob {public string Key;public byte[] Data;public MapRequestState Request;}
        readonly Queue<MapJob> mapWorldJobs=new Queue<MapJob>(),mapDecorationJobs=new Queue<MapJob>();
        readonly HashSet<string> queuedMapReplies=new HashSet<string>();
        readonly Queue<MapApplyJob> mapApplyJobs=new Queue<MapApplyJob>();
        const int MaxQueuedMapJobs=256;
        int mapBudget;double nextMapBudget;
        public StableNode(ulong own,ulong host,ulong lobby,ulong session,string signature,uint highestID)
        {
            if(own==0 || host==0 || lobby==0 || session==0) throw new ArgumentException("Nonzero network identities required.");
            Own=own;Host=host;Lobby=lobby;Session=session;fingerprint=signature;watermark=Math.Max(highestID,0x7fffffffu);
            Ready=Compatible=IsHost;HostSession=IsHost?Session:0;joinPhase=IsHost?JoinPhase.Live:JoinPhase.None;
            if(IsHost) Allocate(Own,null);
        }
        StableMessage Message(StableOp op,string key,byte[] data)
        {return new StableMessage { Op=op,Lobby=Lobby,Session=Session,Origin=Own,Key=key??"",Data=data??new byte[0] };}
        void Emit(ulong target,StableMessage m,bool reliable) {if(Send!=null) Send(target,m.Encode(),reliable);}
        void EmitNative(ulong target,byte[] bytes,bool reliable) {if(Send!=null) Send(target,bytes,reliable);}
        void Reject(ulong peer,ulong session,string reason)
        {var m=Message(StableOp.Reject,"",Text(reason));m.Epoch=session;Emit(peer,m,true);}
        static byte[] Text(string s){return Encoding.UTF8.GetBytes(s);}
        public static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
        void Allocate(ulong peer,StablePeer p)
        {
            if(watermark>UInt32.MaxValue-65536) throw new InvalidOperationException("Network item ID space exhausted.");
            uint first=watermark+1;watermark+=65536;
            if(peer==Own){FirstID=nextID=first;LastID=watermark;}else{p.First=first;p.Last=watermark;}
            if(WatermarkChanged!=null) WatermarkChanged(watermark);
        }
        public uint AllocateID(Func<uint,bool> exists)
        {
            if(!Compatible)throw new InvalidOperationException("Wait for multiplayer compatibility handshake before spawning objects.");
            while(nextID!=0 && nextID<=LastID && exists(nextID))nextID++;
            if(nextID==0 || nextID>LastID)throw new InvalidOperationException("This session's reserved item ID block is exhausted. Reconnect before spawning more objects.");
            return nextID++;
        }
        // ---- native byte relay (transport only) --------------------------------
        // Outgoing native messages are emitted with their stock reliability and
        // FIFO order. A peer that is still receiving the world snapshot has them
        // journaled and replayed after Ack, so no live gameplay byte is lost.
        public bool PublishNative(byte[] bytes)
        {return PublishNative(bytes,false);}
        // Item-data repair replies deliberately force even the creation pose
        // reliable. Preserve that native override through the relay/join journal.
        public bool PublishNative(byte[] bytes,bool forceReliable)
        {
            if(bytes==null || bytes.Length==0 || bytes.Length>58000 || Error!=null || !Compatible)return false;
            bool reliable=forceReliable || StablePackets.IsReliable(bytes);
            if(IsHost)
            {
                foreach(var pair in Peers)SendNativeTo(pair.Key,pair.Value,bytes,reliable);
                return true;
            }
            EmitNative(Host,bytes,reliable);return true;
        }
        // Reliable car blood state, journaled alongside native messages while a
        // peer is receiving a world snapshot. No duplicate crash/damage event.
        public bool PublishCarBlood(uint id,bool dirty)
        {return PublishCarBlood(id,dirty,0);}
        // except keeps a host relay from echoing the update back to the peer that
        // sent it; that peer already applied the state locally.
        public bool PublishCarBlood(uint id,bool dirty,ulong except)
        {
            if(id==0 || Error!=null || !Compatible || (!IsHost && !Ready))return false;
            byte[] data=new byte[5];Buffer.BlockCopy(BitConverter.GetBytes(id),0,data,0,4);data[4]=(byte)(dirty?1:0);
            var message=Message(StableOp.CarBlood,"",data);
            if(IsHost)
            {
                byte[] encoded=message.Encode();
                foreach(var pair in Peers)if(pair.Key!=except)SendNativeTo(pair.Key,pair.Value,encoded,true);
            }
            else Emit(Host,message,true);
            return true;
        }
        static bool ValidCarBlood(StableMessage m)
        {return m.Key=="" && m.Data.Length==5 && BitConverter.ToUInt32(m.Data,0)!=0 && m.Data[4]<=1;}
        // Experimental backend only. The host broadcasts authoritative vehicle
        // motion ownership; native peers never reach this because the mode tag in
        // the compatibility signature rejects them at Hello.
        public bool PublishAuthority(uint carId,ulong owner,ulong epoch)
        {
            if(!IsHost || !Compatible || Error!=null || carId==0 || owner==0 || epoch==0)return false;
            var data=new byte[20];
            Buffer.BlockCopy(BitConverter.GetBytes(carId),0,data,0,4);
            Buffer.BlockCopy(BitConverter.GetBytes(owner),0,data,4,8);
            Buffer.BlockCopy(BitConverter.GetBytes(epoch),0,data,12,8);
            var message=Message(StableOp.Authority,"",data);message.Epoch=Session;
            byte[] encoded=message.Encode();
            foreach(var pair in Peers)SendNativeTo(pair.Key,pair.Value,encoded,true);
            return true;
        }
        ulong rabbitRequestSeq,rabbitStateSeq,lastHostRabbitState;
        public bool SendRabbitRequest(byte[] bytes)
        {
            if(IsHost || !Ready || !Compatible || Error!=null || bytes==null || bytes.Length<1 || bytes.Length>256)return false;
            if(rabbitRequestSeq==ulong.MaxValue)return false;
            var message=Message(StableOp.RabbitRequest,"",bytes);
            message.Epoch=Session;message.Sequence=++rabbitRequestSeq;
            Emit(Host,message,true);
            return true;
        }
        public bool PublishRabbitState(byte[] bytes)
        {
            if(!IsHost || !Compatible || Error!=null || bytes==null || bytes.Length<1 || bytes.Length>16384)return false;
            if(rabbitStateSeq==ulong.MaxValue)return false;
            var data=(byte[])bytes.Clone();
            var message=Message(StableOp.RabbitState,"",data);
            message.Epoch=Session;message.Sequence=++rabbitStateSeq;
            byte[] encoded=message.Encode();
            foreach(var pair in Peers)SendNativeTo(pair.Key,pair.Value,encoded,true);
            return true;
        }
        public bool PublishNativeTo(ulong peer,byte[] bytes)
        {return PublishNativeTo(peer,bytes,false);}
        public bool PublishNativeTo(ulong peer,byte[] bytes,bool forceReliable)
        {
            if(!IsHost || bytes==null || bytes.Length==0 || bytes.Length>58000 || Error!=null)return false;
            StablePeer p;if(!Peers.TryGetValue(peer,out p))return false;
            SendNativeTo(peer,p,bytes,forceReliable || StablePackets.IsReliable(bytes));return true;
        }
        void SendNativeTo(ulong peer,StablePeer p,byte[] bytes,bool reliable)
        {
            if(p.Syncing){Journal(peer,p,bytes,reliable);return;}
            if(p.Ready)EmitNative(peer,bytes,reliable);
        }
        void Journal(ulong peer,StablePeer p,byte[] bytes,bool reliable)
        {
            int size=bytes.Length;
            ulong key=reliable?0:StablePackets.StreamKey(bytes);
            if(!reliable && key!=0)
            {
                // Motion is a latest-value stream. Keep at most one queued
                // non-reliable entry per type/object so a slow snapshot cannot
                // grow the journal without bound. Reliable entries stay ordered.
                for(int i=0;i<p.Journal.Count;i++)
                {
                    var old=p.Journal[i];
                    if(old.Reliable || old.Key!=key)continue;
                    p.JournalBytes-=old.Size;p.Journal.RemoveAt(i);break;
                }
            }
            while(p.JournalBytes+size>MaxJournalBytes)
            {
                int victim=-1;
                for(int i=0;i<p.Journal.Count;i++)if(!p.Journal[i].Reliable){victim=i;break;}
                if(victim<0)break;
                p.JournalBytes-=p.Journal[victim].Size;p.Journal.RemoveAt(victim);
            }
            if(p.JournalBytes+size>MaxJournalBytes && !p.JournalOverflowNoticed)
            {
                p.JournalOverflowNoticed=true;
                if(Notice!=null)Notice("Reliable join journal for peer "+peer+" exceeded "+MaxJournalBytes+" bytes; keeping ordered messages and continuing the join.");
            }
            p.Journal.Add(new StablePeer.Entry {Bytes=(byte[])bytes.Clone(),Reliable=reliable,Size=size,Key=key});p.JournalBytes+=size;
        }
        // ---- join -----------------------------------------------------------------
        public void RequestSnapshot()
        {
            if(!Compatible || IsHost || clock<nextSnapshotRequest)return;
            nextSnapshotRequest=clock+1;Ready=false;
            if(joinPhase!=JoinPhase.Requested){joinPhase=JoinPhase.Requested;joinTransfer=0;JoinStageProgress(clock);}
            Emit(Host,Message(StableOp.Resync,"",null),true);
        }
        public void Stop(string error){Fail(error);}
        public void Tick(double now)
        {
            if(!Numbers.Finite(now) || Error!=null)return;clock=now;
            if(now>=nextMapBudget)
            {
                nextMapBudget=now+.05;mapBudget=0;
                if(IsHost && mapReplies.Count>0)
                {
                    var expired=new List<string>();foreach(var reply in mapReplies)if(now-reply.Value>=MapProgressTimeout)expired.Add(reply.Key);
                    foreach(var key in expired)mapReplies.Remove(key);
                }
            }
            if(DeferMapWork)PumpMapWork();
            if(Error!=null)return;
            if(joinStageArmed && hostWorldLoaded && now-joinStageProgress>=JoinStageTimeout)
            {Fail("Join timed out in stage "+joinPhase+": "+StageReason(joinPhase)+" (no snapshot-stage progress for "+JoinStageTimeout+" s).");return;}
            if(!IsHost && joinPhase==JoinPhase.WaitingWorld && (CanEnterWorld==null || CanEnterWorld()))
            {joinPhase=JoinPhase.AwaitingLive;JoinStageProgress(now);TraceJoin("CanEnterWorld=true; Ack sent transfer="+snapshotID);var ack=Message(StableOp.Ack,"",null);ack.Epoch=snapshotID;Emit(Host,ack,true);}
            if(!IsHost && !Compatible && now>=nextHello)
            {
                if(helloStarted && now-helloTime>30){Fail("Compatibility handshake timed out. Check the host patch/build and reconnect.");return;}
                if(!helloStarted){helloStarted=true;helloTime=now;}
                nextHello=now+1;Emit(Host,Message(StableOp.Hello,"",Text(fingerprint)),true);
            }
            if(!IsHost && Compatible && now-lastHost>(Ready?PeerTimeout:180)) {Fail("Host heartbeat timed out. Reconnect to establish a session again.");return;}
            if(now<nextHeartbeat)return;nextHeartbeat=now+HeartbeatInterval;
            if(IsHost)
            {
                var gone=new List<ulong>();foreach(var p in Peers)if(now-p.Value.Seen>(p.Value.Ready?PeerTimeout:180))gone.Add(p.Key);
                foreach(var peer in gone)RemovePeer(peer);
                foreach(var p in Peers)if(p.Value.Ready)Emit(p.Key,Message(StableOp.Heartbeat,"",null),false);
            }
            else if(Compatible)Emit(Host,Message(StableOp.Heartbeat,"",null),true);
        }
        public void RemovePeer(ulong peer)
        {
            StablePeer p;if(!Peers.TryGetValue(peer,out p))return;
            HashSet<ulong> set;if(!retired.TryGetValue(peer,out set))retired.Add(peer,set=new HashSet<ulong>());set.Add(p.Session);
            ReleaseSpawnIn(p);p.SpawnDone.Clear();p.SpawnDoneOrder.Clear();
            Peers.Remove(peer);if(PeerLeft!=null)PeerLeft(peer);
        }
        void Fail(string error)
        {
            Error=error;Ready=false;Compatible=false;joinPhase=JoinPhase.None;
            if(snapshot!=null)snapshot.Dispose();snapshot=null;
            foreach(var map in mapTransfers.Values)map.Bytes.Dispose();mapTransfers.Clear();mapRequests.Clear();mapReplies.Clear();
            mapWorldJobs.Clear();mapDecorationJobs.Clear();queuedMapReplies.Clear();mapApplyJobs.Clear();
            foreach(var pair in Peers)ReleaseSpawnIn(pair.Value);spawnOut=null;spawnSmallPending=0;spawnSmallBytes=null;spawnIncomingBytes=0;
            joinStageArmed=false;joinTransfer=0;
        }
        public void ClearSpawnTransfers()
        {
            foreach(var pair in Peers){ReleaseSpawnIn(pair.Value);pair.Value.SpawnDone.Clear();pair.Value.SpawnDoneOrder.Clear();}
            spawnOut=null;spawnSmallPending=0;spawnSmallBytes=null;
        }
        public void RetirePeersForWorldReplacement()
        {
            var keys=new List<ulong>(Peers.Keys);
            foreach(var key in keys)RemovePeer(key);
            if(keys.Count>0 && Notice!=null)Notice("peer sessions retired for world replacement; reconnect required");
        }
        void AcceptBootstrap(StableMessage m)
        {
            if(m.Data.Length==0 || m.Data[0]!=8)return;
            string hash=Hash(m.Data);if(bootstrapHash==hash)return;
            if(bootstrapHash!=null){Fail("Host changed worlds. Reconnect for a fresh world handover.");return;}
            bootstrapHash=hash;Ready=false;if(Deliver!=null)Deliver(Host,m.Data);
        }
        public void Receive(ulong sender,byte[] bytes,double now)
        {
            clock=now;if(Error!=null)return;var m=StableMessage.Decode(bytes);if(m==null || m.Lobby!=Lobby || sender==Own)return;
            if(IsHost)
            {
                if(m.Op==StableOp.Hello)
                {
                    HashSet<ulong> old;if(m.Session==0 || (retired.TryGetValue(sender,out old) && old.Contains(m.Session)))return;
                    {string remote=Encoding.UTF8.GetString(m.Data);if(remote!=fingerprint){Reject(sender,m.Session,JoinCompatibility.Describe(fingerprint,remote));return;}}
                    StablePeer peer;if(Peers.TryGetValue(sender,out peer) && peer.Session!=m.Session){RemovePeer(sender);peer=null;}
                    if(peer==null){if(Peers.Count>=64)return;peer=new StablePeer {Session=m.Session,Seen=now};Allocate(sender,peer);Peers[sender]=peer;}
                    peer.Seen=now;byte[] ids=PackIDs(peer.First,peer.Last);var config=new byte[ids.Length+HostConfig.Length];Buffer.BlockCopy(ids,0,config,0,8);Buffer.BlockCopy(HostConfig,0,config,8,HostConfig.Length);var welcome=Message(StableOp.Welcome,"",config);welcome.Epoch=m.Session;Emit(sender,welcome,true);
                    return;
                }
                StablePeer p;if(!Peers.TryGetValue(sender,out p) || p.Session!=m.Session || m.Origin!=sender)return;p.Seen=now;
                if(m.Op==StableOp.Heartbeat)return;
                if(m.Op==StableOp.Authority)return;
                if(m.Op==StableOp.Spawn)
                {
                    if(!p.Ready || m.Sequence==0 || m.Epoch!=p.Session)return;
                    SpawnDone done;
                    if(p.SpawnDone.TryGetValue(m.Sequence,out done))
                    {
                        if(done.Total!=m.Data.Length)return;
                        byte[] h;using(var sha=SHA256.Create())h=sha.ComputeHash(m.Data);
                        if(!Equal(done.Hash,h))return;
                        EmitSpawnAck(sender,p,m.Sequence);return;
                    }
                    if(m.Sequence<=p.SpawnDoneWatermark || p.SpawnIn!=null)return;
                    if(SpawnReceived!=null && SpawnReceived(sender,m.Sequence,m.Data))
                    {
                        byte[] h;using(var sha=SHA256.Create())h=sha.ComputeHash(m.Data);
                        StoreSpawnDone(p,m.Sequence,m.Data.Length,h);
                        EmitSpawnAck(sender,p,m.Sequence);
                    }
                    return;
                }
                if(m.Op==StableOp.SpawnChunk){ReceiveSpawnChunk(sender,p,m);return;}
                if(m.Op==StableOp.SpawnChunkAck)return;
                if(m.Op==StableOp.CarBlood)
                {
                    if(!p.Ready || !ValidCarBlood(m))return;
                    uint id=BitConverter.ToUInt32(m.Data,0);bool dirty=m.Data[4]!=0;
                    if(CarBloodReceived!=null)CarBloodReceived(id,dirty);
                    PublishCarBlood(id,dirty,sender);return;
                }
                if(m.Op==StableOp.RabbitRequest)
                {
                    if(!p.Ready || m.Key!="" || m.Epoch!=p.Session || m.Data.Length<1 || m.Data.Length>256 || m.Sequence==0 || m.Sequence<=p.LastRabbitRequest)return;
                    p.LastRabbitRequest=m.Sequence;
                    if(RabbitRequestReceived!=null)RabbitRequestReceived(sender,m.Data);
                    return;
                }
                if(m.Op==StableOp.RabbitState)return;
                if(m.Op==StableOp.MapRequest)
                {
                    // Join prerequisites arrive as a burst of road/POI keys. Queue
                    // every one (the queue and the per-tick serve budget bound the
                    // work); dropping the tail here made the client wait for its
                    // five-second retry and stretched joins by tens of seconds.
                    if(m.Key.Length==0 || MapRequested==null)return;
                    string replyKey=sender+"|"+p.Session+"|"+m.Key;
                    double previous;if(mapReplies.TryGetValue(replyKey,out previous) && now-previous<MapReplySuppression)
                    {TraceJoin("MapRequest suppressed key="+m.Key);return;}
                    mapBudget++;
                    if(DeferMapWork)
                    {
                        if(queuedMapReplies.Count>=MaxQueuedMapJobs || !queuedMapReplies.Add(replyKey))return;
                        var job=new MapJob {Peer=sender,Session=p.Session,Key=m.Key,ReplyKey=replyKey};
                        (m.Key.StartsWith("o|",StringComparison.Ordinal)?mapDecorationJobs:mapWorldJobs).Enqueue(job);
                    }
                    else ServeMap(new MapJob {Peer=sender,Session=p.Session,Key=m.Key,ReplyKey=replyKey});
                    return;
                }
                if(m.Op==StableOp.Resync)
                {
                    // A native transfer in flight still coalesces duplicate requests.
                    // An experimental transfer can be superseded: a guest that refused
                    // the frames drops back to Requested and re-requests, and the host
                    // must be able to serve it again instead of blocking until timeout.
                    if(p.Syncing && !p.ExternalSync)return;
                    if(SnapshotRequested!=null)SnapshotRequested(sender);return;
                }
                if(m.Op==StableOp.Ack)
                {
                    if(!p.Syncing || m.Epoch!=p.Transfer)return;
                    TraceJoin("Ack received peer="+sender+" transfer="+p.Transfer+" journal="+p.Journal.Count);
                    foreach(var entry in p.Journal)EmitNative(sender,entry.Bytes,entry.Reliable);
                    p.Journal.Clear();p.JournalBytes=0;p.JournalOverflowNoticed=false;p.Syncing=false;p.ExternalSync=false;p.Ready=true;
                    var live=Message(StableOp.Live,"",null);live.Epoch=p.Transfer;Emit(sender,live,true);TraceJoin("Live sent peer="+sender+" transfer="+p.Transfer);
                    return;
                }
                if(m.Op==StableOp.Data){AcceptBootstrap(m);return;}
                return;
            }
            if(sender!=Host || m.Session==0)return;
            if(m.Op==StableOp.Reject){if(m.Epoch==Session && (HostSession==0 || m.Session==HostSession))Fail(Encoding.UTF8.GetString(m.Data));return;}
            if(m.Op==StableOp.Welcome)
            {
                if(m.Epoch!=Session || m.Data.Length<8 || m.Data.Length>4096)return;
                if(HostSession!=0 && HostSession!=m.Session){Fail("Host changed during the session. Reconnect.");return;}
                if(!Compatible){FirstID=nextID=BitConverter.ToUInt32(m.Data,0);LastID=BitConverter.ToUInt32(m.Data,4);}
                if(FirstID==0 || LastID<FirstID)return;HostSession=m.Session;Compatible=true;lastHost=now;
                if(ConfigReceived!=null){var config=new byte[m.Data.Length-8];Buffer.BlockCopy(m.Data,8,config,0,config.Length);ConfigReceived(config);}return;
            }
            if(!Compatible || m.Session!=HostSession)return;lastHost=now;
            if(m.Op==StableOp.Heartbeat)return;
            if(m.Op==StableOp.Authority)
            {
                if(m.Origin!=Host || m.Epoch!=HostSession || m.Key!="" || m.Data.Length!=20)return;
                uint carId=BitConverter.ToUInt32(m.Data,0);ulong owner=BitConverter.ToUInt64(m.Data,4),epoch=BitConverter.ToUInt64(m.Data,12);
                if(carId==0 || owner==0 || epoch==0)return;
                if(AuthorityReceived!=null)AuthorityReceived(carId,owner,epoch);
                return;
            }
            if(m.Op==StableOp.SpawnAck)
            {
                if(Ready && m.Origin==Host && m.Epoch==Session && m.Sequence!=0 && m.Data.Length==0)
                {
                    if(spawnOut!=null && spawnOut.Transaction==m.Sequence)spawnOut=null;
                    if(m.Sequence==spawnSmallPending){spawnSmallPending=0;spawnSmallBytes=null;}
                    if(SpawnAcknowledged!=null)SpawnAcknowledged(m.Sequence);
                }
                return;
            }
            if(m.Op==StableOp.SpawnChunkAck){AckSpawnChunk(m);return;}
            if(m.Op==StableOp.SpawnChunk)return;
            if(m.Op==StableOp.CarBlood)
            {
                if(ValidCarBlood(m) && CarBloodReceived!=null)CarBloodReceived(BitConverter.ToUInt32(m.Data,0),m.Data[4]!=0);
                return;
            }
            if(m.Op==StableOp.RabbitState)
            {
                if(m.Origin!=Host || m.Session!=HostSession || !Compatible || m.Epoch!=HostSession || m.Key!="" || m.Data.Length<1 || m.Data.Length>16384 || !(Joining || Ready) || m.Sequence==0 || m.Sequence<=lastHostRabbitState)return;
                lastHostRabbitState=m.Sequence;
                if(RabbitStateReceived!=null)RabbitStateReceived(m.Data);
                return;
            }
            if(m.Op==StableOp.RabbitRequest)return;
            if(m.Op==StableOp.MapPart){ReceiveMap(m);return;}
            if(m.Op==StableOp.Data){AcceptBootstrap(m);return;}
            if(m.Op==StableOp.Begin)
            {
                if(m.Data.Length!=40 || m.Epoch<=snapshotID)return;
                int size=BitConverter.ToInt32(m.Data,0),count=BitConverter.ToInt32(m.Data,4);
                if(size<0 || size>MaxSnapshotBytes || count<0 || count>2048){Fail("Invalid world transfer size.");return;}
                if(snapshot!=null)snapshot.Dispose();snapshot=new MemoryStream();snapshotID=m.Epoch;joinTransfer=m.Epoch;snapshotSize=size;snapshotHash=new byte[32];Buffer.BlockCopy(m.Data,8,snapshotHash,0,32);Ready=false;joinPhase=JoinPhase.Receiving;
                hostWorldLoaded=true;JoinStageProgress(now);return;
            }
            if(m.Op==StableOp.Part)
            {if(joinPhase!=JoinPhase.Receiving || snapshot==null || m.Epoch!=snapshotID || m.Sequence!=(ulong)snapshot.Length || snapshot.Length+m.Data.Length>snapshotSize)return;snapshot.Write(m.Data,0,m.Data.Length);JoinStageProgress(now);return;}
            if(m.Op==StableOp.End)
            {
                if(joinPhase!=JoinPhase.Receiving || snapshot==null || m.Epoch!=snapshotID)return;byte[] data=snapshot.ToArray();snapshot.Dispose();snapshot=null;
                if(data.Length!=snapshotSize || Hash(data)!=BitConverter.ToString(snapshotHash).Replace("-","")){Fail("World transfer incomplete. Reconnect to retry.");return;}
                try {if(ApplySnapshot!=null)ApplySnapshot(data);}catch(Exception e){Fail("World snapshot apply failed: "+e.Message);return;}
                if(CanEnterWorld!=null && !CanEnterWorld()){joinPhase=JoinPhase.WaitingWorld;JoinStageProgress(now);TraceJoin("CanEnterWorld=false; WaitingWorld transfer="+snapshotID);return;}
                joinPhase=JoinPhase.AwaitingLive;JoinStageProgress(now);TraceJoin("CanEnterWorld=true; Ack sent transfer="+snapshotID);var ack=Message(StableOp.Ack,"",null);ack.Epoch=snapshotID;Emit(Host,ack,true);return;
            }
            if(m.Op==StableOp.Live)
            {
                if(joinPhase==JoinPhase.AwaitingLive && joinTransfer!=0 && m.Epoch==joinTransfer && snapshot==null)
                {Ready=true;joinPhase=JoinPhase.Live;joinStageArmed=false;joinTransfer=0;TraceJoin("Live received transfer="+snapshotID);}
                return;
            }
        }
        static byte[] PackIDs(uint first,uint last){var b=new byte[8];Buffer.BlockCopy(BitConverter.GetBytes(first),0,b,0,4);Buffer.BlockCopy(BitConverter.GetBytes(last),0,b,4,4);return b;}
        public void CancelMap(string key)
        {
            mapRequests.Remove(key);MapTransfer transferState;
            if(mapTransfers.TryGetValue(key,out transferState)){transferState.Bytes.Dispose();mapTransfers.Remove(key);}
        }
        void ServeMap(MapJob job)
        {
            StablePeer peer;
            if(MapRequested==null || !Peers.TryGetValue(job.Peer,out peer) || peer.Session!=job.Session)return;
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            byte[] data;
            try{data=MapRequested(job.Peer,job.Key);}
            catch(Exception e){if(Notice!=null)Notice("World manifest failed for "+job.Key+": "+e.Message);return;}
            double elapsed=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            TraceJoin("MapRequest served key="+job.Key+" bytes="+(data==null?-1:data.Length)+" workMs="+elapsed.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
            if(data!=null){mapReplies[job.ReplyKey]=clock;SendMap(job.Peer,job.Key,data);}
        }
        void ApplyMap(MapApplyJob job)
        {
            MapRequestState request;
            if(!mapRequests.TryGetValue(job.Key,out request) || !Object.ReferenceEquals(request,job.Request))return;
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if(MapApplied!=null)MapApplied(job.Key,job.Data);mapRequests.Remove(job.Key);
                double elapsed=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                TraceJoin("MapPart applied key="+job.Key+" bytes="+job.Data.Length+" workMs="+elapsed.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
            }
            catch(Exception e){Fail("World generation reconciliation failed: "+e.Message);}
        }
        void PumpMapWork()
        {
            if(IsHost)
            {
                // Join prerequisites arrive together. Serve a small time-budgeted
                // batch per tick so the client does not wait one frame per key,
                // while an expensive first road manifest still bounds the frame.
                long start=System.Diagnostics.Stopwatch.GetTimestamp();
                int served=0;
                while((mapWorldJobs.Count>0 || mapDecorationJobs.Count>0) && served<MapServeBatch)
                {
                    var job=(mapWorldJobs.Count>0?mapWorldJobs:mapDecorationJobs).Dequeue();queuedMapReplies.Remove(job.ReplyKey);
                    StablePeer peer;if(!Peers.TryGetValue(job.Peer,out peer) || peer.Session!=job.Session)continue;
                    ServeMap(job);served++;
                    double elapsed=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                    if(elapsed>=MapServeBudgetMs)break;
                }
            }
            else
            {
                while(mapApplyJobs.Count>0)
                {
                    var job=mapApplyJobs.Dequeue();MapRequestState request;
                    if(!mapRequests.TryGetValue(job.Key,out request) || !Object.ReferenceEquals(request,job.Request))continue;
                    ApplyMap(job);break;
                }
            }
        }
        public void RequestMap(string key)
        {
            if(IsHost || !Compatible || Error!=null || key==null || key.Length==0 || key.Length>256 || mapBudget>=MapRequestWindow)return;
            MapRequestState state;
            if(mapRequests.TryGetValue(key,out state))
            {
                if(state.Queued)return;
                if(clock-state.LastProgress<MapProgressTimeout)return;
                MapTransfer stale;if(mapTransfers.TryGetValue(key,out stale)){stale.Bytes.Dispose();mapTransfers.Remove(key);}
                state.LastProgress=clock;state.Attempts++;
            }
            else
            {
                if(mapRequests.Count>=MapRequestWindow)return;
                state=new MapRequestState {LastProgress=clock,Attempts=1};mapRequests.Add(key,state);
            }
            mapBudget++;
            Emit(Host,Message(StableOp.MapRequest,key,null),true);
            TraceJoin("MapRequest sent key="+key+" attempt="+state.Attempts);
        }
        void SendMap(ulong peer,string key,byte[] bytes)
        {
            if(bytes.Length>4*1024*1024){StablePeer p;if(Peers.TryGetValue(peer,out p))Reject(peer,p.Session,"Generated world chunk exceeds transfer limit.");return;}
            ulong id=++transfer;
            for(int offset=0;offset<Math.Max(1,bytes.Length);offset+=48000)
            {
                int n=Math.Min(48000,bytes.Length-offset);var part=new byte[n+(offset==0?36:0)];
                if(offset==0){Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length),0,part,0,4);using(var sha=SHA256.Create())Buffer.BlockCopy(sha.ComputeHash(bytes),0,part,4,32);}
                Buffer.BlockCopy(bytes,offset,part,offset==0?36:0,n);var m=Message(StableOp.MapPart,key,part);m.Epoch=id;m.Sequence=(ulong)offset;Emit(peer,m,true);
            }
        }
        void ReceiveMap(StableMessage m)
        {
            MapRequestState request;if(!mapRequests.TryGetValue(m.Key,out request) || request.Queued)return;MapTransfer t;
            if(m.Sequence==0)
            {
                if(m.Data.Length<36)return;int size=BitConverter.ToInt32(m.Data,0);if(size<0 || size>4*1024*1024)return;
                if(mapTransfers.TryGetValue(m.Key,out t)){if(m.Epoch<=t.Revision)return;t.Bytes.Dispose();}
                if(mapTransfers.Count>=MapRequestWindow && !mapTransfers.ContainsKey(m.Key))return;
                var hash=new byte[32];Buffer.BlockCopy(m.Data,4,hash,0,32);t=new MapTransfer {Revision=m.Epoch,Size=size,Hash=BitConverter.ToString(hash).Replace("-","")};mapTransfers[m.Key]=t;request.LastProgress=clock;JoinStageProgress(clock);
            }
            if(!mapTransfers.TryGetValue(m.Key,out t) || m.Epoch!=t.Revision || m.Sequence!=(ulong)t.Bytes.Length)return;
            int start=m.Sequence==0?36:0,n=m.Data.Length-start;if(t.Bytes.Length+n>t.Size)return;t.Bytes.Write(m.Data,start,n);request.LastProgress=clock;JoinStageProgress(clock);
            if(t.Bytes.Length!=t.Size)return;byte[] data=t.Bytes.ToArray();t.Bytes.Dispose();mapTransfers.Remove(m.Key);
            if(Hash(data)!=t.Hash){request.LastProgress=clock;TraceJoin("MapPart checksum mismatch key="+m.Key+"; retry after progress timeout");return;}
            var job=new MapApplyJob {Key=m.Key,Data=data,Request=request};
            if(DeferMapWork){request.Queued=true;mapApplyJobs.Enqueue(job);}else ApplyMap(job);
        }
        internal void SendPendingBootstraps(Action<ulong> sendMap)
        {
            if(!IsHost)return;
            List<ulong> pending=null;
            foreach(var pair in Peers)
            {
                if(pair.Value.BootstrapSent)continue;
                if(pending==null)pending=new List<ulong>();
                pending.Add(pair.Key);
            }
            if(pending!=null)foreach(ulong peer in pending)sendMap(peer);
        }
        public bool SendBootstrap(ulong peer,byte[] bytes,bool initial=false)
        {
            StablePeer p;
            if(!IsHost || !Peers.TryGetValue(peer,out p) || bytes==null || bytes.Length==0 || bytes.Length>58000 || bytes[0]!=8 || (initial && p.BootstrapSent))return false;
            p.BootstrapSent=true;
            Emit(peer,Message(StableOp.Data,"bootstrap:map",bytes),true);
            return true;
        }
        public void BeginSnapshot(ulong peer,byte[] bytes)
        {
            if(!IsHost || bytes.Length>MaxSnapshotBytes)throw new InvalidOperationException("World snapshot exceeds bounded transfer size.");
            StablePeer p;if(!Peers.TryGetValue(peer,out p))return;
            // The snapshot is the world baseline. Live native bytes produced after
            // this point are journaled and replayed after Ack in original order.
            p.Ready=false;p.Syncing=true;p.ExternalSync=false;p.Transfer=++transfer;p.Journal.Clear();p.JournalBytes=0;p.JournalOverflowNoticed=false;
            TraceJoin("BeginSnapshot peer="+peer+" transfer="+p.Transfer+" bytes="+bytes.Length);
            var header=new byte[40];Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length),0,header,0,4);Buffer.BlockCopy(BitConverter.GetBytes((bytes.Length+47999)/48000),0,header,4,4);
            using(var sha=SHA256.Create())Buffer.BlockCopy(sha.ComputeHash(bytes),0,header,8,32);
            var begin=Message(StableOp.Begin,"",header);begin.Epoch=p.Transfer;Emit(peer,begin,true);
            for(int offset=0;offset<bytes.Length;offset+=48000){int n=Math.Min(48000,bytes.Length-offset);var part=new byte[n];Buffer.BlockCopy(bytes,offset,part,0,n);var m=Message(StableOp.Part,"",part);m.Epoch=p.Transfer;m.Sequence=(ulong)offset;Emit(peer,m,true);}
            var end=Message(StableOp.End,"",null);end.Epoch=p.Transfer;Emit(peer,end,true);
        }
        // ---- experimental external snapshot (mode-gated) ---------------------------
        // The experimental join reuses the native capture/apply path and the native
        // join journal; only the world-snapshot transfer itself differs. Every entry
        // point below is host- or guest-only and never sends a native Begin/Part/End,
        // so a session that does not use these methods (the default) is unchanged.
        // Host: opens the journal barrier for one peer and returns the transfer id the
        // snapshot frames carry. Returns 0 when the peer is unknown.
        public ulong BeginExternalSync(ulong peer)
        {
            if(!IsHost)return 0;
            StablePeer p;if(!Peers.TryGetValue(peer,out p))return 0;
            // The snapshot is still the world baseline: ready is withdrawn and native
            // bytes produced from now on are journaled for the post-Ack replay.
            p.Ready=false;p.Syncing=true;p.ExternalSync=true;p.Transfer=++transfer;
            p.Journal.Clear();p.JournalBytes=0;p.JournalOverflowNoticed=false;
            TraceJoin("BeginExternalSync peer="+peer+" transfer="+p.Transfer);
            return p.Transfer;
        }
        // Guest: a snapshot chunk arrived; refresh the join-stage deadline without
        // changing the phase.
        public void NoteExternalProgress()
        {
            if(IsHost)return;
            JoinStageProgress(clock);
        }
        // Guest: the experimental Begin frame for transferId arrived. Any native
        // snapshot assembly is cancelled (a stale native End can no longer apply) and
        // the join moves into Receiving, which makes Joining true so the guest stops
        // re-requesting.
        public void BeginExternalReceive(ulong transferId)
        {
            if(IsHost || !Compatible || transferId==0)return;
            if(snapshot!=null){snapshot.Dispose();snapshot=null;}
            snapshotID=transferId;snapshotHash=null;snapshotSize=0;
            joinTransfer=transferId;joinPhase=JoinPhase.Receiving;Ready=false;
            // The host had a world to capture, so the waiting-for-host-load stage is over.
            hostWorldLoaded=true;
            JoinStageProgress(clock);
            TraceJoin("External snapshot receiving transfer="+transferId);
        }
        // Guest: reassembly failed or the frames stopped making progress. Returns to
        // the normal request stage so RequestSnapshot retries after its own window
        // instead of leaving the join stuck in Receiving until the stage deadline.
        public void AbandonExternalReceive()
        {
            if(IsHost || joinPhase!=JoinPhase.Receiving)return;
            joinPhase=JoinPhase.Requested;joinTransfer=0;Ready=false;
            JoinStageProgress(clock);
            TraceJoin("External snapshot abandoned transfer="+snapshotID);
        }
        // Guest: the snapshot applied; only the host's Live confirmation is missing.
        public void AwaitExternalLive(ulong transferId)
        {
            if(IsHost || transferId==0)return;
            joinTransfer=transferId;joinPhase=JoinPhase.AwaitingLive;
            JoinStageProgress(clock);
            TraceJoin("External snapshot applied transfer="+transferId);
        }
        // Guest: the native Ack (Epoch=transfer) makes the host replay its join journal
        // and send Live; the host-side Ack branch is reused unchanged.
        public void SendExternalAck(ulong transferId)
        {
            if(IsHost || !Compatible || transferId==0)return;
            var ack=Message(StableOp.Ack,"",null);ack.Epoch=transferId;
            Emit(Host,ack,true);
            TraceJoin("External Ack sent transfer="+transferId);
        }
    }
    // Join compatibility signature: "RW2|ver=<Roadworthy version>|build=<mod build>|game=<game content>|net=<transport>"
    // plus optional flags (e.g. the experimental "exp-mp-1" tag). Only these must match:
    // extra DLLs, local Maps and local physics options are deliberately not part of it.
    // Describe() turns a mismatch into one plain sentence that the host sends with Reject.
    public static class JoinCompatibility
    {
        public const string Prefix="RW2";
        public static string Compose(string version,string build,string game,string net)
        {return Prefix+"|ver="+version+"|build="+Short(build)+"|game="+Short(game)+"|net="+net;}
        static string Short(string hash){return hash==null?"":hash.Length>16?hash.Substring(0,16):hash;}
        internal static Dictionary<string,string> Parse(string signature)
        {
            var fields=new Dictionary<string,string>(StringComparer.Ordinal);if(signature==null)return fields;
            string[] parts=signature.Split('|');
            for(int i=1;i<parts.Length;i++)
            {
                if(parts[i].Length==0)continue;int eq=parts[i].IndexOf('=');
                if(eq<0)fields["flag:"+parts[i]]="on";else fields[parts[i].Substring(0,eq)]=parts[i].Substring(eq+1);
            }
            return fields;
        }
        static string Get(Dictionary<string,string> fields,string key){string value;return fields.TryGetValue(key,out value)?value:"?";}
        public static string Describe(string host,string guest)
        {
            var h=Parse(host);
            if(guest==null || !guest.StartsWith(Prefix+"|",StringComparison.Ordinal))
                return "The host runs Roadworthy v"+Get(h,"ver")+" and you run an older version. Install the same Roadworthy release on both PCs.";
            var g=Parse(guest);
            if(Get(h,"ver")!=Get(g,"ver"))return "Roadworthy versions differ: host v"+Get(h,"ver")+", you v"+Get(g,"ver")+". Install the same release on both PCs.";
            if(Get(h,"build")!=Get(g,"build"))return "Roadworthy builds differ (both v"+Get(h,"ver")+"). Reinstall the same release on both PCs.";
            if(Get(h,"game")!=Get(g,"game"))return "Your game files differ from the host's. Update the game on both PCs, or use Steam's Verify integrity of game files.";
            if(Get(h,"net")!=Get(g,"net"))return "Network transport settings differ (host "+Get(h,"net")+", you "+Get(g,"net")+"). Remove TLD_MP_TRANSPORT on both PCs.";
            foreach(var key in h.Keys)if(key.StartsWith("flag:",StringComparison.Ordinal) && !g.ContainsKey(key))return "The host has New Buggy multiplayer on and you do not. Use the same setting on both PCs.";
            foreach(var key in g.Keys)if(key.StartsWith("flag:",StringComparison.Ordinal) && !h.ContainsKey(key))return "You have New Buggy multiplayer on and the host does not. Use the same setting on both PCs.";
            return "Multiplayer compatibility check failed. Install the same Roadworthy release on both PCs.";
        }
        // Short, player-facing text for a guest's join failure (shown in the native popup).
        public static string Friendly(string error)
        {
            if(String.IsNullOrEmpty(error))return "Couldn't join the host.";
            if(error.IndexOf("Patch/game content mismatch",StringComparison.Ordinal)>=0)
                return "The host runs a different Roadworthy release. Install the same release on both PCs.";
            if(error.IndexOf("SDR peer disconnected",StringComparison.Ordinal)>=0 || error.IndexOf("No answer from host",StringComparison.Ordinal)>=0 ||
               error.IndexOf("Steam P2P session failed",StringComparison.Ordinal)>=0 || error.IndexOf("No traffic from peer",StringComparison.Ordinal)>=0)
                return "Couldn't connect to the host over Steam. Check you are both online, then try joining again.";
            return error.Length>200?error.Substring(0,200)+"...":error;
        }
    }

}


