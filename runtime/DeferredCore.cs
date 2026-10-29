using System;
using System.Collections;
using System.Collections.Generic;

namespace TldMp
{
    // Position retries and attachment retries have separate lifetimes: normal
    // parent-local position polling must not cancel a pending attachment event.
    public sealed class DeferredVersions
    {
        sealed class State {public ulong Position,Attachment;}
        readonly Dictionary<uint,State> states=new Dictionary<uint,State>();ulong revision;
        public ulong Generation {get;private set;}
        public ulong Version(uint id,bool attachment)
        {State s;return states.TryGetValue(id,out s)?attachment?s.Attachment:s.Position:0;}
        public void Observe(uint id,byte type)
        {
            bool pos=type>=16 && type<=18 || type==36 || type==39 || type==42;
            bool attach=type==18 || type==36 || type==42;
            if(!pos && !attach)return;State s;
            if(!states.TryGetValue(id,out s))
            {if(states.Count>=65536)throw new InvalidOperationException("Deferred object state limit reached.");states[id]=s=new State();}
            if(pos)s.Position=++revision;if(attach)s.Attachment=++revision;
        }
        public void Invalidate(uint id){Observe(id,36);}
        public void Clear(){states.Clear();++revision;++Generation;}
    }
    // Every MoveNext, including the first, rechecks the captured session,
    // ownership epoch, object identity and operation version through valid.
    // ready may defer native work without permitting its fallback attachment.
    public sealed class GuardedEnumerator : IEnumerator,IDisposable
    {
        readonly IEnumerator inner;readonly Func<bool> valid,ready;
        readonly Func<IEnumerator,bool> step;readonly object wait;readonly int maxWaits;
        int waits;bool finished,waiting;
        public GuardedEnumerator(IEnumerator inner,Func<bool> valid,Func<IEnumerator,bool> step,Func<bool> ready,object wait,int maxWaits)
        {this.inner=inner;this.valid=valid;this.step=step;this.ready=ready;this.wait=wait;this.maxWaits=Math.Max(1,maxWaits);}
        public object Current {get{return finished?null:waiting?wait:inner.Current;}}
        public bool MoveNext()
        {
            if(finished)return false;
            try
            {
                if(!valid()){Dispose();return false;}
                if(ready!=null && !ready())
                {if(++waits>=maxWaits){Dispose();return false;}waiting=true;return true;}
                waiting=false;if(step(inner))return true;Dispose();return false;
            }
            catch{Dispose();throw;}
        }
        public void Reset(){throw new NotSupportedException();}
        public void Dispose(){if(finished)return;finished=true;var d=inner as IDisposable;if(d!=null)d.Dispose();}
    }
    public static class AttachmentTarget
    {
        public static bool Ready(int indexType,int index,bool collider,int slots,bool slot)
        {return indexType==0 || indexType==1 && index>=0 && collider || indexType==2 && index>=0 && index<slots && slot;}
    }
    // A reliable Steam send can be refused before it enters the transport.
    // Retain that message and later reliable messages in order until accepted.
    public sealed class ReliableOutbox
    {
        sealed class Pending
        {public Queue<byte[]> Packets=new Queue<byte[]>();public int Bytes;public double Progress;}
        readonly Dictionary<ulong,Pending> peers=new Dictionary<ulong,Pending>();
        readonly Dictionary<ulong,string> failures=new Dictionary<ulong,string>();
        readonly Func<ulong,byte[],bool,bool> send;readonly Action<ulong,string> failed;
        readonly int peerLimit,totalLimit,packetLimit,flushPacketLimit,flushByteLimit,flushPerPeerLimit;int bytes,packets;
        public int PendingPackets {get{return packets;}}
        public int PendingBytes {get{return bytes;}}
        public ReliableOutbox(Func<ulong,byte[],bool,bool> send,Action<ulong,string> failed,int peerLimit=48*1024*1024,int totalLimit=64*1024*1024,int packetLimit=8192,
            int flushPacketLimit=32,int flushByteLimit=1024*1024,int flushPerPeerLimit=4)
        {this.send=send;this.failed=failed;this.peerLimit=peerLimit;this.totalLimit=totalLimit;this.packetLimit=packetLimit;this.flushPacketLimit=flushPacketLimit;this.flushByteLimit=flushByteLimit;this.flushPerPeerLimit=flushPerPeerLimit;}
        public bool Send(ulong peer,byte[] data,bool reliable,double now)
        {
            if(failures.ContainsKey(peer))return false;Pending p;
            // Unreliable state is independent of the reliable FIFO. A blocked
            // reliable head must never prevent current motion from progressing.
            if(!reliable)return send(peer,data,false);
            if(!peers.TryGetValue(peer,out p))
            {if(send(peer,data,true))return true;peers[peer]=p=new Pending {Progress=now};}
            if(data.Length>peerLimit-p.Bytes || data.Length>totalLimit-bytes || packets>=packetLimit)
            {Fail(peer,"Reliable send backlog exceeded its bounded queue. Reconnect.");return false;}
            p.Packets.Enqueue((byte[])data.Clone());p.Bytes+=data.Length;bytes+=data.Length;packets++;return true;
        }
        void Fail(ulong peer,string reason){Drop(peer);failures[peer]=reason;}
        public void Drop(ulong peer)
        {Pending p;if(peers.TryGetValue(peer,out p)){bytes-=p.Bytes;packets-=p.Packets.Count;peers.Remove(peer);}failures.Remove(peer);}
        public void Clear(){peers.Clear();failures.Clear();bytes=packets=0;}
        public void Flush(double now)
        {
            int sent=0,total=0;foreach(var peer in new List<ulong>(peers.Keys))
            {
                Pending p;if(!peers.TryGetValue(peer,out p))continue;
                if(now-p.Progress>20){Fail(peer,"Reliable transport made no progress for 20 seconds. Reconnect.");continue;}
                for(int n=0;n<flushPerPeerLimit && p.Packets.Count>0 && sent<flushPacketLimit && total<flushByteLimit;n++)
                {
                    var data=p.Packets.Peek();if(!send(peer,data,true))break;
                    p.Packets.Dequeue();p.Bytes-=data.Length;bytes-=data.Length;packets--;p.Progress=now;sent++;total+=data.Length;
                }
                if(p.Packets.Count==0)peers.Remove(peer);
            }
            // Notify outside Send: a host callback may remove a peer while the
            // protocol's broadcast loop is enumerating its peer dictionary.
            var errors=new List<KeyValuePair<ulong,string>>(failures);failures.Clear();
            foreach(var error in errors)if(failed!=null)failed(error.Key,error.Value);
        }
    }
}
