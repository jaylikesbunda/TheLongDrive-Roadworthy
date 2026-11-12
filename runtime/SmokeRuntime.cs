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
        // Remote urine stream: a cosmetic particle system and looping pour sound on the
        // remote head anchor. It is built only from the local cap's visual settings and
        // carries no tankcapscript, so it can never pour or change a bladder.
        sealed class UrineEffect {internal GameObject Object;internal ParticleSystem Particle;internal AudioSource Sound;internal Transform Anchor;internal GameObject Player;}
        static readonly Color UrineStreamColor=new Color(0.85f,0.72f,0.10f,1f);
        static readonly SmokeState smokeState=new SmokeState();
        // Host relay sequence survives individual peer reconnects for this host session.
        static readonly SmokeRelaySequences smokeRelaySequences=new SmokeRelaySequences();
        static readonly Dictionary<ulong,SmokeEffect> smokeEffects=new Dictionary<ulong,SmokeEffect>();
        static readonly Dictionary<ulong,UrineEffect> urineEffects=new Dictionary<ulong,UrineEffect>();
        static uint smokeSequence;static byte smokeLast;static byte urineLast;static bool smokeSent;static double smokeNext;

        static void SmokeReset()
        {
            foreach(var e in smokeEffects.Values)if(e.Object!=null)UnityEngine.Object.Destroy(e.Object);
            foreach(var e in urineEffects.Values)if(e.Object!=null)UnityEngine.Object.Destroy(e.Object);
            smokeEffects.Clear();urineEffects.Clear();smokeState.Clear();smokeRelaySequences.Clear();smokeSequence=0;smokeLast=0;urineLast=0;smokeSent=false;smokeNext=0;
        }
        static void SmokePeerLeft(ulong peer)
        {
            smokeState.Remove(peer);SmokeEffect e;if(smokeEffects.TryGetValue(peer,out e))
            {if(e.Object!=null)UnityEngine.Object.Destroy(e.Object);smokeEffects.Remove(peer);}
            UrineEffect u;if(urineEffects.TryGetValue(peer,out u))
            {if(u.Object!=null)UnityEngine.Object.Destroy(u.Object);urineEffects.Remove(peer);}
        }
        static bool ReceiveSmoke(ulong source,byte[] bytes)
        {
            if(bytes==null || bytes.Length==0 || bytes[0]!=SmokeCodec.Type)return false;
            if(node==null || !node.Compatible || !node.Ready || node.Error!=null || !WorldSceneAvailable())return true;
            if(loopbackMode){if(source!=Loopback.Peer)return true;}
            else if(!SteamP2PNetworkingUnderTheHoodScript.Expected(new CSteamID(source),msgType.carPosGlobal))return true;
            ulong origin,session;uint sequence;byte intensity,urine;
            if(!SmokeCodec.Read(bytes,out origin,out session,out sequence,out intensity,out urine))return true;
            ulong expected;
            if(node.IsHost){StablePeer peer;if(!node.Peers.TryGetValue(source,out peer) || !peer.Ready)return true;expected=peer.Session;}
            else expected=node.HostSession;
            if(!SmokeCodec.ValidSender(node.IsHost,source,node.Host,origin,session,expected) || origin==Own)return true;
            if(!smokeState.Accept(origin,session,sequence,intensity,urine,Now))return true;
            if(node.IsHost)
            {
                uint relaySequence=smokeRelaySequences.Next(origin);
                byte[] relay=SmokeCodec.Encode(origin,node.Session,relaySequence,intensity,urine);
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
            if(node==null || !node.Compatible || !node.Ready || node.Error!=null || !Connected || !WorldSceneAvailable())
            {
                foreach(var e in smokeEffects.Values)if(e.Particle!=null){var off=e.Particle.emission;off.enabled=false;}
                foreach(var u in urineEffects.Values)StopUrine(u);
                return;
            }
            var local=mainscript.s==null?null:mainscript.s.player;
            ParticleSystem source=local==null?null:local.smokeParticle;
            tankcapscript urineSource=local==null?null:local.piss;
            byte current=0;
            if(source!=null && local.maxSmokeParticleRate>0 && source.gameObject.activeInHierarchy)
            {
                var emission=source.emission;
                if(emission.enabled && source.isPlaying)current=SmokeCodec.Quantize(emission.rateOverTime.constantMax/local.maxSmokeParticleRate);
            }
            // Urine uses the local stream's own emission, which the native cap only
            // enables while it is actually pouring (tilted, valve open, bladder not empty).
            byte urine=0;
            if(urineSource!=null && urineSource.ps!=null && urineSource.maxRate>0 && urineSource.ps.gameObject.activeInHierarchy)
            {
                var urineEmission=urineSource.ps.emission;
                if(urineEmission.enabled && urineSource.ps.isPlaying)urine=SmokeCodec.Quantize(urineEmission.rateOverTime.constantMax/urineSource.maxRate);
            }
            if(!smokeSent || current!=smokeLast || urine!=urineLast || ((current|urine)!=0 && Now>=smokeNext))
            {
                smokeSequence++;if(smokeSequence==0)smokeSequence++;
                byte[] packet=SmokeCodec.Encode(Own,node.Session,smokeSequence,current,urine);
                if(node.PublishNative(packet)){smokeSent=true;smokeLast=current;urineLast=urine;smokeNext=Now+SmokeState.Refresh;}
            }
            var present=new HashSet<ulong>();
            var urinePresent=new HashSet<ulong>();
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
                // Urine follows the head anchor, which the smoke lip anchor does not guarantee.
                if(player.mpscript.THead!=null)
                {
                    urinePresent.Add(id);
                    TickUrine(id,player.player,player.mpscript.THead,local==null?null:local.piss,smokeState.GetUrine(id,Now));
                }
            }
            var gone=new List<ulong>();foreach(var pair in smokeEffects)if(!present.Contains(pair.Key))gone.Add(pair.Key);
            foreach(ulong id in gone){var effect=smokeEffects[id];if(effect.Object!=null)UnityEngine.Object.Destroy(effect.Object);smokeEffects.Remove(id);}
            var goneUrine=new List<ulong>();foreach(var pair in urineEffects)if(!urinePresent.Contains(pair.Key))goneUrine.Add(pair.Key);
            foreach(ulong id in goneUrine){var effect=urineEffects[id];if(effect.Object!=null)UnityEngine.Object.Destroy(effect.Object);urineEffects.Remove(id);}
        }
        static void TickUrine(ulong id,GameObject player,Transform head,tankcapscript template,byte value)
        {
            UrineEffect effect;
            if(urineEffects.TryGetValue(id,out effect) && (effect.Player!=player || effect.Anchor!=head || effect.Object==null))
            {if(effect.Object!=null)UnityEngine.Object.Destroy(effect.Object);urineEffects.Remove(id);effect=null;}
            bool usable=template!=null && template.ps!=null;
            if(effect==null && value!=0 && usable){effect=CreateUrineEffect(template,player,head);urineEffects[id]=effect;}
            if(effect==null)return;
            float level=value/(float)SmokeCodec.MaxIntensity;
            if(effect.Particle!=null)
            {
                var emission=effect.Particle.emission;emission.enabled=value!=0;
                emission.rateOverTime=level*(usable?Mathf.Max(0,template.maxRate):0);
                if(value!=0 && !effect.Particle.isPlaying)effect.Particle.Play();
            }
            if(effect.Sound!=null)
            {
                if(value!=0 && effect.Sound.clip!=null)
                {
                    effect.Sound.volume=level*(usable && template.maxVolume>0?template.maxVolume:1f);
                    if(!effect.Sound.isPlaying)effect.Sound.Play();
                }
                else if(effect.Sound.isPlaying)effect.Sound.Stop();
            }
        }
        static void StopUrine(UrineEffect effect)
        {
            if(effect.Particle!=null){var off=effect.Particle.emission;off.enabled=false;}
            if(effect.Sound!=null && effect.Sound.isPlaying)effect.Sound.Stop();
        }
        static UrineEffect CreateUrineEffect(tankcapscript template,GameObject player,Transform head)
        {
            // Same rule as the smoke replica: particle system, renderer and audio source only.
            var go=new GameObject("Roadworthy remote urine stream");
            try{
            go.transform.SetParent(head,false);
            // Start just in front of the face and aim forward and slightly down, the
            // direction the native cap points when the player looks down.
            go.transform.localPosition=new Vector3(0,-.05f,.15f);
            go.transform.localRotation=Quaternion.Euler(15f,0f,0f);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var src=template.ps.main;var dst=ps.main;
            dst.loop=true;dst.playOnAwake=false;
            if(mainscript.s!=null && mainscript.s.TCustomSpaceMain!=null)
            {dst.simulationSpace=ParticleSystemSimulationSpace.Custom;dst.customSimulationSpace=mainscript.s.TCustomSpaceMain;}
            else dst.simulationSpace=ParticleSystemSimulationSpace.Local;
            dst.startLifetime=src.startLifetime;dst.startSpeed=src.startSpeed;dst.startSize=src.startSize;dst.startRotation=src.startRotation;
            dst.startColor=UrineStreamColor;dst.gravityModifier=src.gravityModifier;dst.maxParticles=src.maxParticles;
            var shape=ps.shape;var sourceShape=template.ps.shape;shape.enabled=sourceShape.enabled;
            if(sourceShape.enabled){shape.shapeType=sourceShape.shapeType;shape.angle=sourceShape.angle;shape.radius=sourceShape.radius;}
            var renderer=ps.GetComponent<ParticleSystemRenderer>();var original=template.ps.GetComponent<ParticleSystemRenderer>();
            if(renderer!=null && original!=null){renderer.sharedMaterial=original.sharedMaterial;renderer.renderMode=original.renderMode;}
            var emission=ps.emission;emission.enabled=false;ps.Play();
            AudioSource sound=null;
            var pour=template.SSource;
            if(pour!=null && pour.clip!=null)
            {
                sound=go.AddComponent<AudioSource>();
                sound.clip=pour.clip;sound.loop=true;sound.playOnAwake=false;sound.volume=0;
                sound.spatialBlend=pour.spatialBlend;sound.minDistance=pour.minDistance;sound.maxDistance=pour.maxDistance;
                sound.rolloffMode=pour.rolloffMode;sound.outputAudioMixerGroup=pour.outputAudioMixerGroup;
            }
            return new UrineEffect{Object=go,Particle=ps,Sound=sound,Player=player,Anchor=head};
            }catch{UnityEngine.Object.Destroy(go);throw;}
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








