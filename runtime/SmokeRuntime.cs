using System;
using System.Collections.Generic;
using Steamworks;
using GeneszSteamP2PBase;
using UnityEngine;

namespace TldMp
{
    public static partial class StableGameBridge
    {
        sealed class SmokeEffect {internal GameObject Object;internal ParticleSystem Particle;internal Transform Anchor;internal GameObject Player;}
        static readonly SmokeState smokeState=new SmokeState();
        // Host relay sequence survives individual peer reconnects for this host session.
        static readonly SmokeRelaySequences smokeRelaySequences=new SmokeRelaySequences();
        static readonly Dictionary<ulong,SmokeEffect> smokeEffects=new Dictionary<ulong,SmokeEffect>();
        static uint smokeSequence;static byte smokeLast;static bool smokeSent;static double smokeNext;

        static void SmokeReset()
        {
            foreach(var e in smokeEffects.Values)if(e.Object!=null)UnityEngine.Object.Destroy(e.Object);
            smokeEffects.Clear();smokeState.Clear();smokeRelaySequences.Clear();smokeSequence=0;smokeLast=0;smokeSent=false;smokeNext=0;
        }
        static void SmokePeerLeft(ulong peer)
        {
            smokeState.Remove(peer);SmokeEffect e;if(smokeEffects.TryGetValue(peer,out e))
            {if(e.Object!=null)UnityEngine.Object.Destroy(e.Object);smokeEffects.Remove(peer);}
        }
        static bool ReceiveSmoke(ulong source,byte[] bytes)
        {
            if(bytes==null || bytes.Length==0 || bytes[0]!=SmokeCodec.Type)return false;
            if(node==null || !node.Compatible || !node.Ready || node.Error!=null || !WorldSceneAvailable())return true;
            if(loopbackMode){if(source!=Loopback.Peer)return true;}
            else if(!SteamP2PNetworkingUnderTheHoodScript.Expected(new CSteamID(source),msgType.carPosGlobal))return true;
            ulong origin,session;uint sequence;byte intensity;
            if(!SmokeCodec.Read(bytes,out origin,out session,out sequence,out intensity))return true;
            ulong expected;
            if(node.IsHost){StablePeer peer;if(!node.Peers.TryGetValue(source,out peer) || !peer.Ready)return true;expected=peer.Session;}
            else expected=node.HostSession;
            if(!SmokeCodec.ValidSender(node.IsHost,source,node.Host,origin,session,expected) || origin==Own)return true;
            if(!smokeState.Accept(origin,session,sequence,intensity,Now))return true;
            if(node.IsHost)
            {
                uint relaySequence=smokeRelaySequences.Next(origin);
                byte[] relay=SmokeCodec.Encode(origin,node.Session,relaySequence,intensity);
                foreach(var peer in node.Peers)if(peer.Key!=source && peer.Value.Ready)node.PublishNativeTo(peer.Key,relay);
            }
            return true;
        }
        static double smokeWarning;
        static void SmokeTick()
        {
            try{SmokeTickCore();}catch(Exception e){if(Now>=smokeWarning){smokeWarning=Now+5;Debug.LogWarning("[TLD MP smoke] Cosmetic update failed: "+e.GetType().Name);}}
        }
        static void SmokeTickCore()
        {
            if(node==null || !node.Compatible || !node.Ready || node.Error!=null || !Connected || !WorldSceneAvailable()){foreach(var e in smokeEffects.Values)if(e.Particle!=null){var off=e.Particle.emission;off.enabled=false;}return;}
            var local=mainscript.s==null?null:mainscript.s.player;
            ParticleSystem source=local==null?null:local.smokeParticle;
            byte current=0;
            if(source!=null && local.maxSmokeParticleRate>0 && source.gameObject.activeInHierarchy)
            {
                var emission=source.emission;
                if(emission.enabled && source.isPlaying)current=SmokeCodec.Quantize(emission.rateOverTime.constantMax/local.maxSmokeParticleRate);
            }
            if(!smokeSent || current!=smokeLast || (current!=0 && Now>=smokeNext))
            {
                smokeSequence++;if(smokeSequence==0)smokeSequence++;
                byte[] packet=SmokeCodec.Encode(Own,node.Session,smokeSequence,current);
                if(node.PublishNative(packet)){smokeSent=true;smokeLast=current;smokeNext=Now+SmokeState.Refresh;}
            }
            var present=new HashSet<ulong>();
            if(syncScript.s!=null && syncScript.s.players!=null)
            foreach(var pair in syncScript.s.players)
            {
                ulong id=(ulong)pair.Key;var player=pair.Value;
                if(id==Own || player==null || player.player==null || player.mpscript==null)continue;
                Transform anchor=player.mpscript.lipT!=null?player.mpscript.lipT:player.mpscript.THead;
                if(anchor==null)continue;
                present.Add(id);SmokeEffect effect;
                if(smokeEffects.TryGetValue(id,out effect) && (effect.Player!=player.player || effect.Anchor!=anchor || effect.Object==null))
                {if(effect.Object!=null)UnityEngine.Object.Destroy(effect.Object);smokeEffects.Remove(id);effect=null;}
                byte value=source==null?(byte)0:smokeState.Get(id,Now);
                if(effect==null && value!=0 && source!=null){effect=CreateSmokeEffect(source,player.player,anchor,player.mpscript.lipT==null);smokeEffects[id]=effect;}
                if(effect!=null && effect.Particle!=null)
                {
                    var emission=effect.Particle.emission;emission.enabled=value!=0;
                    emission.rateOverTime=(value/(float)SmokeCodec.MaxIntensity)*(local==null?0:Mathf.Max(0,local.maxSmokeParticleRate));
                    if(value!=0 && !effect.Particle.isPlaying)effect.Particle.Play();
                }
            }
            var gone=new List<ulong>();foreach(var pair in smokeEffects)if(!present.Contains(pair.Key))gone.Add(pair.Key);
            foreach(ulong id in gone){var effect=smokeEffects[id];if(effect.Object!=null)UnityEngine.Object.Destroy(effect.Object);smokeEffects.Remove(id);}
        }
        static SmokeEffect CreateSmokeEffect(ParticleSystem template,GameObject player,Transform anchor,bool headFallback)
        {
            // Construct only a particle system and renderer: no copied player scripts,
            // colliders, or gameplay components can run on the cosmetic replica.
            var go=new GameObject("Roadworthy remote cigarette smoke");
            try{
            go.transform.SetParent(anchor,false);
            if(headFallback)go.transform.localPosition=new Vector3(0,0,.12f);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var src=template.main;var dst=ps.main;
            dst.loop=true;dst.playOnAwake=false;
            // The native world space follows floating-origin shifts while staying
            // independent of the head and vehicle between shifts.
            if(mainscript.s!=null && mainscript.s.TCustomSpaceMain!=null)
            {dst.simulationSpace=ParticleSystemSimulationSpace.Custom;dst.customSimulationSpace=mainscript.s.TCustomSpaceMain;}
            else dst.simulationSpace=ParticleSystemSimulationSpace.Local;
            dst.startLifetime=src.startLifetime;dst.startSpeed=src.startSpeed;dst.startSize=src.startSize;dst.startRotation=src.startRotation;
            dst.startColor=src.startColor;dst.gravityModifier=src.gravityModifier;dst.maxParticles=src.maxParticles;
            var shape=ps.shape;var sourceShape=template.shape;shape.enabled=sourceShape.enabled;
            if(sourceShape.enabled){shape.shapeType=sourceShape.shapeType;shape.angle=sourceShape.angle;shape.radius=sourceShape.radius;}
            var renderer=ps.GetComponent<ParticleSystemRenderer>();var original=template.GetComponent<ParticleSystemRenderer>();
            if(renderer!=null && original!=null){renderer.sharedMaterial=original.sharedMaterial;renderer.renderMode=original.renderMode;}
            var emission=ps.emission;emission.enabled=false;ps.Play();
            return new SmokeEffect{Object=go,Particle=ps,Player=player,Anchor=anchor};
            }catch{UnityEngine.Object.Destroy(go);throw;}
        }
    }
}








