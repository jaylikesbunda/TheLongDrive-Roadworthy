using System;
using System.Collections.Generic;

namespace TldMp
{
    // Bookkeeping only. The caller validates native geometry and performs pickup/drop.
    internal sealed class RabbitCarryLeases
    {
        internal sealed class Lease
        {
            internal readonly uint Item;
            internal readonly int Part;
            internal readonly ulong Peer, Session, Request, Token;
            internal readonly object ItemIdentity, RootIdentity, PartIdentity;
            internal double Deadline, LastTouch;
            internal Lease(uint item,int part,ulong peer,ulong session,ulong request,ulong token,
                object itemIdentity,object rootIdentity,object partIdentity,double deadline)
            {
                Item=item;Part=part;Peer=peer;Session=session;Request=request;Token=token;
                ItemIdentity=itemIdentity;RootIdentity=rootIdentity;PartIdentity=partIdentity;Deadline=deadline;LastTouch=deadline-Timeout;
            }
        }
        sealed class PeerState { internal ulong Session,Watermark,Pending; internal double PendingAt; internal bool Ready; }
        const double Timeout=3.0;
        readonly Dictionary<ulong,PeerState> peers=new Dictionary<ulong,PeerState>();
        readonly Dictionary<ulong,Lease> byPeer=new Dictionary<ulong,Lease>();
        readonly Dictionary<uint,Lease> byItem=new Dictionary<uint,Lease>();
        ulong nextToken;
        static bool TimeOk(double now){return !double.IsNaN(now)&&!double.IsInfinity(now)&&now>=0&&now<=double.MaxValue-Timeout;}

        // Feed only authenticated peer/session observations. Returned leases must be dropped by the caller.
        internal Lease Observe(ulong peer,ulong session,bool ready)
        {
            if(peer==0||session==0)throw new ArgumentException("peer or session");
            PeerState p;
            if(!peers.TryGetValue(peer,out p))peers.Add(peer,p=new PeerState{Session=session});
            Lease revoked=null;
            if(p.Session!=session)
            {
                revoked=ReleasePeer(peer);p.Session=session;p.Watermark=0;p.Pending=0;
            }
            p.Ready=ready;
            if(!ready){p.Pending=0;if(revoked==null)revoked=ReleasePeer(peer);}
            return revoked;
        }
        internal Lease Disconnect(ulong peer)
        {
            // Retain the watermark until an authenticated new session is observed.
            PeerState p;if(peers.TryGetValue(peer,out p)){p.Ready=false;p.Pending=0;}
            return ReleasePeer(peer);
        }
        // Call before native validation, so a failed geometry check cannot be replayed later.
        internal bool BeginRequest(ulong peer,ulong session,ulong request,double now)
        {
            PeerState p;
            if(peer==0||session==0||request==0||!TimeOk(now)||!peers.TryGetValue(peer,out p)||
                p.Session!=session||!p.Ready||request<=p.Watermark)return false;
            p.Watermark=request;p.Pending=request;p.PendingAt=now;return true;
        }
        internal void CancelRequest(ulong peer,ulong session,ulong request)
        {
            PeerState p;
            if(peers.TryGetValue(peer,out p)&&p.Session==session&&p.Pending==request)p.Pending=0;
        }
        internal bool TryReserve(ulong peer,ulong session,ulong request,uint item,int part,
            object itemIdentity,object rootIdentity,object partIdentity,double now,out Lease lease)
        {
            lease=null;PeerState p;
            if(peer==0||session==0||request==0||!TimeOk(now)||!peers.TryGetValue(peer,out p)||
                p.Session!=session||!p.Ready||p.Pending!=request)return false;
            p.Pending=0;
            if(now<p.PendingAt||now>=p.PendingAt+Timeout)return false;
            if(item==0||part<0||part>127||itemIdentity==null||rootIdentity==null||partIdentity==null||
                byPeer.ContainsKey(peer)||byItem.ContainsKey(item)||nextToken==ulong.MaxValue)return false;
            var token=++nextToken;
            lease=new Lease(item,part,peer,session,request,token,itemIdentity,rootIdentity,partIdentity,now+Timeout);
            byPeer.Add(peer,lease);byItem.Add(item,lease);return true;
        }
        internal bool TryGetPeer(ulong peer,out Lease lease){return byPeer.TryGetValue(peer,out lease);}
        internal bool TryGetItem(uint item,out Lease lease){return byItem.TryGetValue(item,out lease);}
        internal bool Refresh(ulong peer,ulong session,uint item,ulong token,double now)
        {
            PeerState p;Lease l;
            if(!TimeOk(now)||!peers.TryGetValue(peer,out p)||!p.Ready||p.Session!=session||
                !byPeer.TryGetValue(peer,out l)||l.Session!=session||l.Item!=item||l.Token!=token||now<l.LastTouch||now>=l.Deadline)return false;
            l.LastTouch=now;l.Deadline=now+Timeout;return true;
        }
        internal Lease Release(ulong peer,ulong session,uint item,ulong token)
        {
            Lease l;
            if(!byPeer.TryGetValue(peer,out l)||l.Session!=session||l.Item!=item||l.Token!=token)return null;
            return ReleasePeer(peer);
        }
        internal Lease ReleasePeer(ulong peer)
        {
            Lease l;if(!byPeer.TryGetValue(peer,out l))return null;
            byPeer.Remove(peer);byItem.Remove(l.Item);return l;
        }
        internal void Expire(double now,List<Lease> released)
        {
            if(released==null)throw new ArgumentNullException("released");
            if(!TimeOk(now))throw new ArgumentOutOfRangeException("now");
            var expired=new List<ulong>();
            foreach(var pair in byPeer)if(now>=pair.Value.Deadline)expired.Add(pair.Key);
            foreach(var peer in expired)released.Add(ReleasePeer(peer));
        }
        internal void ReleaseAll(List<Lease> released)
        {
            if(released==null)throw new ArgumentNullException("released");
            foreach(var pair in byPeer)released.Add(pair.Value);
            byPeer.Clear();byItem.Clear();
            foreach(var p in peers.Values){p.Ready=false;p.Pending=0;}
        }
    }
}
