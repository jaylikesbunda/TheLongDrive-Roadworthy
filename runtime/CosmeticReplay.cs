using System;
using System.Collections.Generic;
using GeneszSteamP2PBase;
using UnityEngine;

namespace TldMp
{
    // Cosmetic replays for remote actions the native netcode already synchronises.
    // Each replay reads state the native handler has just applied and only plays a
    // sound or particle here. Nothing in this file writes food, car, fluid or item state.
    public static partial class StableGameBridge
    {
        struct FoodProbe {internal bool Valid;internal uint Item;internal int Before;}
        static readonly CosmeticRateLimiter cosmeticLimiter=new CosmeticRateLimiter();
        static double cosmeticWarning;

        // Called before the native dispatch of a received message. Only food state
        // updates are recorded: the bite index before the handler runs.
        static FoodProbe ProbeFood(byte[] bytes)
        {
            var probe=new FoodProbe();
            try
            {
                if(bytes==null || bytes.Length==0 || bytes[0]!=(byte)msgType.food || savedatascript.s==null)return probe;
                uint item;int index;float hp;bool ate;
                DataByteConverterScript.Receive<uint,int,float,bool>(bytes,out item,out index,out hp,out ate);
                tosaveitemscript holder;
                if(!savedatascript.s.items.TryGetValue(item,out holder) || holder==null || holder.food==null)return probe;
                probe.Valid=true;probe.Item=item;probe.Before=holder.food.index;
            }
            catch(Exception e){CosmeticWarn("food probe",e);}
            return probe;
        }

        // Called after the native dispatch. A rise in the bite index is a bite the
        // remote player made: play the same crunch and crumb effects the eater gets.
        static void ReplayFood(FoodProbe probe,ulong source)
        {
            if(!probe.Valid)return;
            try
            {
                if(source==0 || source==Own || savedatascript.s==null || mainscript.s==null)return;
                tosaveitemscript holder;
                if(!savedatascript.s.items.TryGetValue(probe.Item,out holder) || holder==null || holder.food==null)return;
                var food=holder.food;
                if(!CosmeticPolicy.FoodBite(probe.Before,food.index,food.continous))return;
                if(!cosmeticLimiter.Allow(source,Time.unscaledTime))return;
                var s=mainscript.s;
                Transform at=food.particlePos!=null?food.particlePos:food.transform;
                AudioClip clip=null;float volume=0;
                if(food.sound==ediblescript.soundType.eat && s.soundEat!=null && s.soundEat.Length>0)
                {clip=s.soundEat[UnityEngine.Random.Range(0,s.soundEat.Length)];volume=s.eatVolume;}
                else if(food.sound==ediblescript.soundType.crunchy && s.soundEatCrunchy!=null && s.soundEatCrunchy.Length>0)
                {clip=s.soundEatCrunchy[UnityEngine.Random.Range(0,s.soundEatCrunchy.Length)];volume=s.eatCrunchyVolume;}
                if(clip!=null)mainscript.PlayClipAtPoint(clip,at.position,volume);
                if(food.hasParticle)
                {
                    int r=UnityEngine.Random.Range(food.minParticle,food.maxParticle+1);
                    if(r>0)s.FoodParticle(food.particleType,at.position,at.forward,r);
                }
            }
            catch(Exception e){CosmeticWarn("food replay",e);}
        }

        static void CosmeticTick()
        {
            if(!Connected)return;
            try{CosmeticIgnition.Tick();}
            catch(Exception e){CosmeticWarn("ignition click",e);}
        }
        static void CosmeticReset()
        {
            cosmeticLimiter.Clear();CosmeticIgnition.Clear();
        }
        static void CosmeticPeerLeft(ulong peer)
        {
            cosmeticLimiter.Remove(peer);
        }
        static void CosmeticWarn(string what,Exception e)
        {
            if(Now>=cosmeticWarning){cosmeticWarning=Now+5;Debug.LogWarning("[TLD MP cosmetic] "+what+" failed: "+e.GetType().Name);}
        }
    }

    // Remote cars: the native ignition change is applied by UpdMultiIgnition without a
    // sound. A watch on each car synced from a peer plays the click on the change edge.
    internal static class CosmeticIgnition
    {
        sealed class Watch {internal carscript Car;internal bool Last;}
        const int Capacity = 16;
        static readonly List<Watch> watches = new List<Watch>();

        // Called from the remote car sync path for every car a peer has synchronised.
        internal static void Note(tosaveitemscript item)
        {
            try
            {
                if(item==null)return;
                carscript car=item.GetComponentInParent<carscript>();
                if(car==null)return;
                foreach(var w in watches)if(w.Car==car)return;
                if(watches.Count>=Capacity)watches.RemoveAt(0);
                watches.Add(new Watch{Car=car,Last=car.ignition});
            }
            catch(Exception)
            {
            }
        }

        internal static void Tick()
        {
            for(int i=watches.Count-1;i>=0;i--)
            {
                var w=watches[i];
                if(w.Car==null){watches.RemoveAt(i);continue;}
                bool current=w.Car.ignition;
                if(CosmeticPolicy.IgnitionClick(w.Last,current,w.Car.isPlayerDriving) && w.Car.SSounds!=null && w.Car.turnedRadioSOund!=null)
                    w.Car.SSounds.PlayOneShot(w.Car.turnedRadioSOund,1f);
                w.Last=current;
            }
        }

        internal static void Clear()
        {
            watches.Clear();
        }
    }
}
