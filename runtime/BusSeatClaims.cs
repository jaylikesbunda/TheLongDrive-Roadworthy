using System;
using System.Collections.Generic;
using Steamworks;
namespace TldMp
{
    // Canonical seat claims observed from syncScript.playerSyncClass.GotUpd
    // (native playerPos carries sitting/sitAtPoi/itemID/seatID). A claim binds
    // one remote player to the exact native seat it occupies; only the bus's
    // driverSeat0 claim is a driving authority. Claims carry the session the
    // bridge verified at observe time, so a reconnect's same playerSyncClass
    // never reuses a stale claim. Timestamps are local receive times because
    // native lastTime lags one update.
    internal static class BusSeatClaims
    {
        sealed class Claim { internal syncScript.playerSyncClass Self; internal ulong Session; internal uint ItemID; internal int SeatID; internal double At; }
        static readonly Dictionary<CSteamID,Claim> claims=new Dictionary<CSteamID,Claim>();
        internal const double Fresh=2.0;
        internal static void Observe(syncScript.playerSyncClass self,bool sitting,bool sitAtPoi,uint itemID,int seatID,ulong session,double now)
        {
            if(self==null||self.steamID==0||double.IsNaN(now)||double.IsInfinity(now))return;
            var id=new CSteamID(self.steamID);
            if(!sitting||sitAtPoi||itemID==0||seatID<0||session==0){claims.Remove(id);return;}
            claims[id]=new Claim{Self=self,Session=session,ItemID=itemID,SeatID=seatID,At=now};
        }
        internal static void ClearPeer(ulong steamID){if(steamID!=0)claims.Remove(new CSteamID(steamID));}
        // True only while a fresh canonical claim seats steamID on THIS bus's
        // driver seat under the given live session: the claimed seatscript is
        // driverSeat0 wired to the same carscript, the claim still maps to the
        // live playerSyncClass, the actor object is active and natively seated.
        internal static bool DriverOf(ulong steamID,tosaveitemscript item,ulong session,double now)
        {
            if(steamID==0||session==0||item==null||item.car==null||item.seats==null||syncScript.s==null||syncScript.s.players==null||
               double.IsNaN(now)||double.IsInfinity(now))return false;
            Claim c;
            if(!claims.TryGetValue(new CSteamID(steamID),out c)||c.Session!=session||now<c.At||now-c.At>Fresh)return false;
            syncScript.playerSyncClass p;
            if(!syncScript.s.players.TryGetValue(new CSteamID(steamID),out p)||!ReferenceEquals(p,c.Self)||
               p.player==null||!p.player.activeInHierarchy||!p.sitting)return false;
            if(c.ItemID!=item.idInSave||c.SeatID<0||c.SeatID>=item.seats.Length)return false;
            var seat=item.seats[c.SeatID];
            return seat!=null&&seat.driverSeat0&&seat.Car==item.car;
        }
        internal static void Clear(){claims.Clear();}
    }
}
