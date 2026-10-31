using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using GeneszSteamP2PBase;
using Steamworks;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace TldMp
{
    // Native-restore bridge. The native synchronization, movement, collision and
    // interaction paths are the only active gameplay path; this class contains
    // transport adapters, the world manifest/join handshake, structural packet
    // validation, object-ID collision prevention and diagnostics only.
    public static partial class StableGameBridge
    {
        static StableNode node;static Driver driver;static string fingerprint,lastError;static ContentFingerprint.Job fingerprintJob;static double fingerprintRetryAt;
        static bool dispatching,capturing;static List<byte[]> captured;
        static readonly Dictionary<ulong,byte[]> maps=new Dictionary<ulong,byte[]>();
        static readonly Dictionary<ulong,byte[]> lastPlayers=new Dictionary<ulong,byte[]>();
        static readonly Dictionary<uint,MessageState> lastItems=new Dictionary<uint,MessageState>();
        static readonly ReliableOutbox outbox=new ReliableOutbox(TryEnvelope,TransportFailed);
        // Map/snapshot chunks ride their own bounded reliable outbox and the
        // Bulk lane so a multi-megabyte transfer cannot stall transport traffic.
        static readonly ReliableOutbox bulkOutbox=new ReliableOutbox(TryBulkEnvelope,TransportFailed,8*1024*1024,16*1024*1024,8192,8,256*1024,2);
        static readonly NetDeliveryScheduler delivery=new NetDeliveryScheduler();
        static SteamSdrTransport steamTransport;static ulong transportLobby,transportHost;static double nextSdrRetry;static int deliveryPeerCursor;const int TransportVirtualPort=0;
        static bool hostWorldEvidence;
        static double nextPulse,nextFailure;static int packetErrors;
        static readonly Dictionary<string,double> diagnosticNext=new Dictionary<string,double>();
        static double nextJoinDiagnostic;
        static double nextSyncHealth;
        static int healthMaxPending,healthMaxPendingBytes;
        static bool loopbackMode;static double loopbackNextRequest;
        static bool loopbackBootstrapReceived;static mapSettingScript loopbackPreviousMap;
        static readonly HashSet<ulong> loopbackBootstrapSent=new HashSet<ulong>();

        static bool WorldSceneAvailable()
        {
            return menuhandler.s!=null && menuhandler.s.currentMainMap!=null && menuhandler.s.currentMainMap.started &&
                mainscript.s!=null && mainscript.s.player!=null && savedatascript.s!=null && savedatascript.s.data!=null &&
                (!loopbackMode || Loopback.IsHost || (loopbackBootstrapReceived && !ReferenceEquals(menuhandler.s.currentMainMap,loopbackPreviousMap)));
        }
        internal static void JoinDiagnostic(string text)
        {
            if(loopbackMode || ModDiagnostics.On)
                Debug.Log("[TLD MP join] peer="+Own+" t="+Now.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" "+text);
        }
        internal static string MapDiagnostic()
        {
            var map=menuhandler.s==null?null:menuhandler.s.currentMainMap;
            return map==null?"map=null":"map="+map.name+" seed="+map.seed+" genArounds="+(map.genArounds==null?-1:map.genArounds.Count);
        }
        static bool DiagnosticMode {get{return loopbackMode || ModDiagnostics.On;}}
        static void DiagLimited(string key,string text,double every)
        {
            if(!DiagnosticMode)return;
            double next;if(diagnosticNext.TryGetValue(key,out next) && Now<next)return;
            if(diagnosticNext.Count>512)diagnosticNext.Clear();
            diagnosticNext[key]=Now+Math.Max(.05,every);
            Debug.Log("[TLD MP diag] peer="+Own+" t="+Now.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" "+text);
        }
        [Serializable] public sealed class ActiveItem {public uint ID;public bool Active;}
        [Serializable] public sealed class PoiState {public int Generator;public save_poi State;}
        [Serializable] public sealed class MessageState {public ulong Origin;public string Payload;}
        [Serializable] public sealed class WorldFrame
        {
            public int Version=3;public string Fingerprint;public float Time;
            public itemDataClass Items=new itemDataClass();public List<ActiveItem> Active=new List<ActiveItem>();
            public List<PoiState> Pois=new List<PoiState>();
            public List<MessageState> PlayerMessages=new List<MessageState>();
        }
        internal static double Now {get{return Time.realtimeSinceStartup;}}
        static bool Connected {get{return networkingScript.s!=null && networkingScript.s.hood!=null && networkingScript.s.hood.isConnected;}}
        static ulong Own {get{return loopbackMode?Loopback.Own:(ulong)SteamP2PNetworkingUnderTheHoodScript.OwnID();}}
        static bool Find(uint id,out tosaveitemscript item){item=null;return savedatascript.s!=null && savedatascript.s.items!=null && savedatascript.s.items.TryGetValue(id,out item) && item!=null;}
        static void EnsureFingerprintStart()
        {
            if(fingerprint!=null || fingerprintJob!=null || Now<fingerprintRetryAt)return;
            try
            {
                string managed=Path.GetDirectoryName(typeof(syncScript).Assembly.Location);
                string assets=Directory.GetParent(managed).FullName;
                string custom=pathscript.mapPath();string identity=BuildIdentity.Sources;
                fingerprintJob=ContentFingerprint.Start(managed,assets,custom,identity);
                JoinDiagnostic("Fingerprint hashing started asynchronously");
            }
            catch(Exception e)
            {
                fingerprintRetryAt=Now+5;Debug.LogError("[TLD MP v3] Could not prepare compatibility fingerprint: "+e.Message);
            }
        }
        static bool EnsureFingerprint()
        {
            if(fingerprint!=null)return true;
            if(Now<fingerprintRetryAt)return false;
            EnsureFingerprintStart();
            if(fingerprintJob==null)return false;
            string result,error;if(!fingerprintJob.TryGetResult(out result,out error))return false;
            double workMilliseconds=fingerprintJob.WorkMilliseconds;
            fingerprintJob=null;
            if(error!=null)
            {
                fingerprintRetryAt=Now+5;Debug.LogError("[TLD MP v3] Compatibility fingerprint failed: "+error);return false;
            }
            fingerprint=result;fingerprintRetryAt=0;
            JoinDiagnostic("Fingerprint workMs="+workMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }
        static void FinishFingerprintJob()
        {
            if(fingerprint!=null)return;
            if(fingerprintJob==null)
            {
                if(EnsureFingerprint())return;
                if(fingerprintJob==null)
                    throw new InvalidOperationException("Compatibility fingerprint is not available for multiplayer item ID allocation.");
            }
            if(fingerprint!=null)return;
            long waitStart=PhaseTimings.Begin();
            string result,error;fingerprintJob.WaitForResult(out result,out error);
            PhaseTimings.End(PhaseTimings.FingerprintWait,waitStart,"blocking main-thread wait");
            double workMilliseconds=fingerprintJob.WorkMilliseconds;fingerprintJob=null;
            if(error!=null)throw new InvalidOperationException("Compatibility fingerprint failed before item ID allocation: "+error);
            fingerprint=result;fingerprintRetryAt=0;
            JoinDiagnostic("Fingerprint workMs="+workMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
        }
        static void Reset()
        {
            ResetNativeSafety();
            maps.Clear();lastPlayers.Clear();lastItems.Clear();pendingCarBlood.Clear();diagnosticNext.Clear();loopbackBootstrapSent.Clear();loopbackBootstrapReceived=false;loopbackPreviousMap=null;
            if(steamTransport!=null){steamTransport.Dispose();steamTransport=null;}transportLobby=transportHost=0;nextSdrRetry=0;deliveryPeerCursor=0;delivery.Clear();hostWorldEvidence=false;
            outbox.Clear();bulkOutbox.Clear();node=null;StableMap.Configure(null);nextPulse=0;lastError=null;
            nextSyncHealth=0;healthMaxPending=healthMaxPendingBytes=0;ClearSpawnHandoff();
            selection.Clear();
            RoadLiveCache.Clear();RoadFarSkip.Clear();FarPlaceQueue.Clear();pendingItems.Clear();itemRecovery.Clear();TerrainWorker.Clear();poseFilter.Clear();senderClock.Clear();PlacementSkip.Clear();liveUsables.Clear();CarCargo.Clear();
            vehiclePairs.Clear();vehicleClock.Clear();ClearVehicleImpacts();RemoteCarSmoothing.Clear();dispatchVehicleMotion=null;applyingVehicleMotion=false;
        }
        static void Ensure()
        {
            if(!Connected){if(node!=null)Reset();return;}
            ulong lobby,host;
            if(loopbackMode)
            {
                if(!Loopback.Ready){if(node!=null)Reset();return;}
                lobby=Loopback.Lobby;host=Loopback.HostID;
            }
            else
            {
                lobby=(ulong)networkingScript.s.hood.ConnectedLobbyID;host=(ulong)SteamMatchmaking.GetLobbyOwner(networkingScript.s.hood.ConnectedLobbyID);
                if(lobby==0 || host==0 || Own==0)return;
            }
            if(node!=null && node.Lobby==lobby && node.Host==host)
            {
                if(!loopbackMode && !EnsureSteamTransport(lobby,host))return;
                if(!loopbackMode || node.Error==null)return;
                Reset();
            }
            // Startup can poll while hashing continues off-thread. ID allocation
            // keeps its explicit barrier in NewKey, so it never uses a partial hash.
            if(!EnsureFingerprint())return;
            Reset();
            if(!loopbackMode && !EnsureSteamTransport(lobby,host))return;
            ulong session;do{session=BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0);}while(session==0);
            var created=new StableNode(Own,host,lobby,session,fingerprint,HighestID());
            node=created;
            BeginNativeSafetySession(session);
            node.Send=SendEnvelope;node.Deliver=Deliver;node.ApplySnapshot=TimedApplyWorld;node.SnapshotRequested=TimedSendWorld;
            node.Notice=delegate(string text){Debug.LogWarning("[TLD MP v3] "+text);};
            node.Trace=JoinDiagnostic;
            node.DeferMapWork=true;
            node.WatermarkChanged=delegate(uint high){if(savedatascript.s!=null && savedatascript.s.data!=null)savedatascript.s.data.lastID=Math.Max(savedatascript.s.data.lastID,high);};
            node.PeerLeft=delegate(ulong peer){appliedSpawns.Remove(peer);RemovePlayer(peer);};node.CarBloodReceived=ApplyCarBlood;node.SpawnReceived=ApplySpawn;node.SpawnAcknowledged=AckSpawn;
            StableMap.Configure(node);
            if(loopbackMode && !node.IsHost)
            {
                // Welcome carries the host configuration. Chain, rather than replace,
                // StableMap's handler: the requested snapshot is the stock Resync the
                // Steam join path uses.
                var prior=node.ConfigReceived;
                node.ConfigReceived=delegate(byte[] config)
                {
                    if(prior!=null)prior(config);
                    var current=node;
                    if(current==null || current.IsHost || !current.Compatible || current.Ready || current.Joining || current.AwaitingSnapshot)return;
                    if(WorldSceneAvailable()){loopbackNextRequest=Now+2;current.RequestSnapshot();}
                };
                if(syncScript.s!=null)syncScript.s.sentAskStartStuff=true;
                loopbackNextRequest=0;
            }
            if(node.IsHost && savedatascript.s!=null && savedatascript.s.data!=null)savedatascript.s.data.lastID=Math.Max(savedatascript.s.data.lastID,node.LastID);
        }
        static bool EnsureWorldSession()
        {
            if(node==null)Ensure();
            return node!=null;
        }
        static void ApplyLoopback()
        {
            if(!Loopback.Active)return;
            if(!SteamManager.Initialized)
            {
                Debug.LogError("[TLD MP loopback] SteamAPI is not initialised in this process; loopback mode was not engaged. Launch both instances through Steam.");
                return;
            }
            if(!Loopback.Start())
            {
                Debug.LogError("[TLD MP loopback] "+(Loopback.Error==null?"Could not start the loopback transport.":Loopback.Error));
                return;
            }
            var hood=networkingScript.s==null?null:networkingScript.s.hood;
            if(hood==null)return;
            hood.isConnected=true;hood.isHost=Loopback.IsHost;hood.ConnectedLobbyID=new CSteamID(Loopback.Lobby);
            hood.hasCorrectPassword=true;hood.sentOnJoin=true;
            loopbackMode=true;
            Application.runInBackground=true;
            Debug.Log("[TLD MP loopback] "+(Loopback.IsHost?"host":"client")+" engaged as CSteamID "+Loopback.Own+" lobby "+Loopback.Lobby+". Transport is localhost-only and test-only.");
            if(!Loopback.IsHost)Debug.Log("[TLD MP loopback] "+Loopback.Transport.NetworkDescription);
        }
        public static void Bootstrap()
        {
            ApplyLoopback();WorldJump.CarDescriber=RemoteCarSmoothing.Describe;
            // Hash the game content on a worker as soon as networking exists, so a join
            // finds the result ready instead of freezing the main thread while it waits.
            EnsureFingerprintStart();
            if(Connected)EnsureFingerprint();
            if(driver==null){var g=new GameObject("TLD Multiplayer Stability v3");UnityEngine.Object.DontDestroyOnLoad(g);driver=g.AddComponent<Driver>();MenuProbe.StartIfRequested(g);g.AddComponent<MenuLayout>();g.AddComponent<PatchOptionsMenu>();g.AddComponent<FeedbackHotkey>();g.AddComponent<RemoteCarDriver>();}
            Ensure();Debug.Log("[TLD MP v3] Native synchronization path active; checked world manifest/join. All peers require this exact build.");
        }
        public static void PrewarmContentFingerprint(SteamP2PMenuHandlerScript menu)
        {
            if(menu!=null && !menu.menuOpen)EnsureFingerprint();
        }
        static readonly PoseSequenceCounter poseCounter=new PoseSequenceCounter();
        static readonly PoseSequenceFilter poseFilter=new PoseSequenceFilter();
        static readonly SenderClock senderClock=new SenderClock();
        static readonly SenderClock vehicleClock=new SenderClock();
        static readonly VehicleMotionPairer vehiclePairs=new VehicleMotionPairer();
        static VehicleMotionSample dispatchVehicleMotion;
        static bool applyingVehicleMotion;
        static readonly bool vehicleMotionEnabled=Environment.GetEnvironmentVariable("TLD_MP_VEHICLE_SNAPSHOTS")!="0";
        // Sender and send clock of the received message being handled (0 = none).
        static ulong dispatchSender;static uint dispatchSequence;
        // Send time of the message being handled, on the local clock.
        internal static bool DispatchSendTime(double localNow,out double time,out double lateness)
        {
            time=localNow;lateness=-1;
            if(dispatchVehicleMotion!=null)
            {time=vehicleClock.Map(dispatchSender,dispatchVehicleMotion.ClockMs,localNow,out lateness);return true;}
            if(dispatchSequence==PoseSequence.Unordered)return false;
            time=senderClock.Map(dispatchSender,dispatchSequence,localNow,out lateness);return true;
        }
        static uint ClockMs(){return (uint)(DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond);}
        internal static ulong MotionSender {get{return dispatchSender;}}
        internal static bool DispatchVehicleVelocity(uint id,out Vector3 velocity,out Vector3 angular,out bool impact)
        {
            velocity=Vector3.zero;angular=Vector3.zero;impact=false;var frame=dispatchVehicleMotion;if(frame==null || frame.ID!=id)return false;
            velocity=new Vector3(BitConverter.ToSingle(frame.Velocity,5),BitConverter.ToSingle(frame.Velocity,9),BitConverter.ToSingle(frame.Velocity,13));
            angular=new Vector3(BitConverter.ToSingle(frame.Velocity,17),BitConverter.ToSingle(frame.Velocity,21),BitConverter.ToSingle(frame.Velocity,25));
            impact=frame.Impact;return true;
        }
        sealed class VehicleImpactTrack
        {internal tosaveitemscript Item;internal int Role;internal double Seen;internal VehicleImpactPolicy Policy=new VehicleImpactPolicy();}
        static readonly Dictionary<int,VehicleImpactTrack> vehicleImpacts=new Dictionary<int,VehicleImpactTrack>();
        static readonly List<int> impactKeys=new List<int>();
        static int impactCursor,impactSendFrame=-1,impactSendsThisFrame;
        static long vehicleImpactTriggers,vehicleImpactSent;
        static long vehicleMotionReceived,vehicleMotionProcessed,vehicleAdmissionRejected,vehicleSceneRejected,vehicleMalformed,vehicleControlIgnored;
        static void ClearVehicleImpacts(){vehicleImpacts.Clear();impactKeys.Clear();impactCursor=0;impactSendFrame=-1;vehicleImpactTriggers=vehicleImpactSent=0;vehicleMotionReceived=vehicleMotionProcessed=vehicleAdmissionRejected=vehicleSceneRejected=vehicleMalformed=vehicleControlIgnored=0;}
        static void RegisterVehicleImpact(tosaveitemscript item)
        {
            int key=item.GetInstanceID(),role=(int)item.CarWhatDoing();VehicleImpactTrack track;
            if(!vehicleImpacts.TryGetValue(key,out track))
            {if(vehicleImpacts.Count>=64)return;vehicleImpacts.Add(key,track=new VehicleImpactTrack {Item=item,Role=role});}
            if(track.Role!=role){track.Role=role;track.Policy=new VehicleImpactPolicy();}
            track.Seen=Now;
        }
        static bool VehicleInImpactBurst(tosaveitemscript item)
        {VehicleImpactTrack track;return vehicleImpacts.TryGetValue(item.GetInstanceID(),out track) && Now<track.Policy.BurstUntil;}
        static void NoteVehicleSent(tosaveitemscript item)
        {VehicleImpactTrack track;if(vehicleImpacts.TryGetValue(item.GetInstanceID(),out track))track.Policy.LastSent=Now;}
        static void TickVehicleImpactSamples()
        {
            if(!vehicleMotionEnabled || node==null || !node.Ready || !WorldSceneAvailable())return;
            impactKeys.Clear();
            foreach(var pair in vehicleImpacts)
            {
                try
                {
                    var track=pair.Value;var item=track.Item;
                    if(item==null || Now-track.Seen>5 || !item.ThisOneInChargeCar()){impactKeys.Add(pair.Key);continue;}
                    var body=item.RB;if(body==null)continue;
                    Vector3 v=body.velocity,w=body.angularVelocity;
                    if(track.Policy.Observe(Now,v.x,v.y,v.z,w.x,w.y,w.z))vehicleImpactTriggers++;
                }
                catch(Exception e){impactKeys.Add(pair.Key);DiagLimited("impact-sample:"+pair.Key,"impact observer retired after "+e.GetType().Name,5);}
            }
            foreach(int key in impactKeys)vehicleImpacts.Remove(key);
        }
        static void TickVehicleImpactBursts()
        {
            if(!vehicleMotionEnabled || node==null || !node.Ready || vehicleImpacts.Count==0 || !WorldSceneAvailable())return;
            if(impactSendFrame!=Time.frameCount){impactSendFrame=Time.frameCount;impactSendsThisFrame=0;}
            impactKeys.Clear();impactKeys.AddRange(vehicleImpacts.Keys);int count=impactKeys.Count;
            int first=impactCursor%count;
            for(int i=0;i<count && impactSendsThisFrame<4;i++)
            {
                int index=(first+i)%count;var track=vehicleImpacts[impactKeys[index]];var item=track.Item;
                try
                {
                    if(item==null || !track.Policy.Due(Now) || !item.ThisOneInChargeCar())continue;
                    track.Policy.LastSent=Now;impactSendsThisFrame++;vehicleImpactSent++;impactCursor=(index+1)%count;
                    item.ForcePosUpd(false,false,false);
                }
                catch(Exception e){track.Policy.BurstUntil=Now;DiagLimited("impact-send:"+impactKeys[index],"impact burst stopped after "+e.GetType().Name,5);}
            }
        }
        static bool CaptureVehicleMotion(ulong scope,byte[] bytes,bool reliable)
        {
            if(!vehicleMotionEnabled || reliable || bytes==null || (bytes[0]!=39 && bytes[0]!=20))return false;
            tosaveitemscript item;
            if(!Find(StablePackets.Item(bytes),out item) || item.car==null || !item.ThisOneInChargeCar())return false;
            RegisterVehicleImpact(item);
            uint clock=ClockMs();byte[] packet;
            if(!vehiclePairs.Offer(scope,Own,bytes,Time.frameCount,Now,clock,bytes[0]==20?poseCounter.Next(clock):0,VehicleInImpactBurst(item),out packet))return false;
            if(packet!=null){PublishVehiclePacket(scope,packet);NoteVehicleSent(item);}
            return true;
        }
        static void PublishVehiclePacket(ulong scope,byte[] bytes)
        {
            if(node==null)return;
            if(scope!=0 && node.IsHost)node.PublishNativeTo(scope,bytes);
            else node.PublishNative(bytes);
        }
        // The envelope's origin is trusted only from the host, or when it equals
        // the authenticated sending client. Native messages keep their true
        // driver identity when the host relays the complete envelope onward.
        static bool ReceiveVehicleMotion(ulong source,byte[] bytes)
        {
            if(bytes==null || bytes.Length==0 || bytes[0]!=VehicleMotionCodec.Type)return false;
            vehicleMotionReceived++;
            if(!WorldSceneAvailable()){vehicleSceneRejected++;return true;}
            if(node==null || !node.Compatible){vehicleAdmissionRejected++;return true;}
            // Loopback uses synthetic IDs/lobby and is authenticated by its
            // localhost endpoint plus the StableNode handshake, not Steam lobby
            // membership. Match the existing ReadLoopback native-message path.
            if(loopbackMode){if(source!=Loopback.Peer){vehicleAdmissionRejected++;return true;}}
            else if(!SteamP2PNetworkingUnderTheHoodScript.Expected(new CSteamID(source),msgType.carPosGlobal)){vehicleAdmissionRejected++;return true;}
            if(node.IsHost && !node.Peers.ContainsKey(source)){vehicleAdmissionRejected++;return true;}
            ulong origin;VehicleMotionSample[] samples;
            if(!VehicleMotionCodec.TryRead(bytes,out origin,out samples) || !VehicleMotionCodec.ValidOrigin(node.IsHost,node.Host,source,origin))
            {packetErrors++;vehicleMalformed++;return true;}
            if(origin==Own)return true;
            foreach(var sample in samples)if(!ValidateData(sample.Pose) || !ValidateData(sample.Velocity)){packetErrors++;vehicleMalformed++;return true;}
            // Establish the newest clock offset before mapping its repeated
            // predecessor, so a first packet retains the samples' true spacing.
            double ignored;vehicleClock.Map(origin,samples[samples.Length-1].ClockMs,Now,out ignored);
            bool accepted=false;
            foreach(var sample in samples)
            {
                if(!poseFilter.Accept(origin,PoseSequence.Key(sample.Pose),sample.Sequence)){NetStats.SequenceStale++;continue;}
                poseFilter.Accept(origin,PoseSequence.Key(sample.Velocity),sample.Sequence);
                NetStats.SequencePassed++;
                tosaveitemscript local;
                // A local driver retains control through stale packets from a
                // previous driver; remote pose and velocity are skipped together.
                if(Find(sample.ID,out local) && local.car!=null &&
                    (local.CarWhatDoing()==tosaveitemscript.carWhatDoing.driving || local.ThisOneInChargeCar((tosaveitemscript.carWhatDoing)sample.Role))){vehicleControlIgnored++;continue;}
                accepted=true;
                var previous=dispatchVehicleMotion;ulong priorSender=dispatchSender;uint priorSequence=dispatchSequence;
                bool priorApplying=applyingVehicleMotion;int priorType=DispatchType;
                dispatchVehicleMotion=sample;dispatchSender=origin;dispatchSequence=sample.Sequence;applyingVehicleMotion=true;
                try
                {
                    DispatchType=39;long start=Stopwatch.GetTimestamp();
                    networkingScript.s.ProcessMessage(new CSteamID(origin),sample.Pose,msgType.carPosGlobal);NoteHandler(sample.Pose,Stopwatch.GetTimestamp()-start);
                    DispatchType=20;start=Stopwatch.GetTimestamp();
                    networkingScript.s.ProcessMessage(new CSteamID(origin),sample.Velocity,msgType.carVelocity);NoteHandler(sample.Velocity,Stopwatch.GetTimestamp()-start);
                    vehicleMotionProcessed++;
                }
                catch(Exception e){packetErrors++;if(Now>=nextFailure){nextFailure=Now+5;Debug.LogError("[TLD MP v3] Vehicle snapshot handler failed: "+e);}}
                finally{dispatchVehicleMotion=previous;dispatchSender=priorSender;dispatchSequence=priorSequence;applyingVehicleMotion=priorApplying;DispatchType=priorType;}
            }
            if(node.IsHost && accepted)foreach(var peer in node.Peers)if(peer.Key!=source)node.PublishNativeTo(peer.Key,bytes);
            return true;
        }
        // Removes the sequence trailer; false drops the message (malformed, or a
        // position/velocity older than one already applied for the same object).
        static bool AcceptSequenced(ulong sender,ref byte[] bytes)
        {
            byte[] payload;uint sequence;bool stamped;
            if(!PoseSequence.TryUnstamp(bytes,out payload,out sequence,out stamped)){packetErrors++;return false;}
            if(stamped && !poseFilter.Accept(sender,PoseSequence.Key(payload),sequence)){NetStats.SequenceStale++;return false;}
            if(stamped)NetStats.SequencePassed++;
            dispatchSender=sender;dispatchSequence=stamped?sequence:PoseSequence.Unordered;
            bytes=payload;return true;
        }
        static readonly LiveUsableSender liveUsables=new LiveUsableSender();
        static readonly bool liveUsablesEnabled=Environment.GetEnvironmentVariable("TLD_MP_LIVE_USABLES")!="0";
        static readonly List<KeyValuePair<ulong,byte[]>> settleScratch=new List<KeyValuePair<ulong,byte[]>>();
        internal static string TakeLiveUsableSummary(){return liveUsables.TakeSummary();}
        // Sends the resting state of usables that stopped moving (LiveUsableSender).
        static void SettleLiveUsables()
        {
            settleScratch.Clear();liveUsables.TakeSettles(Now,settleScratch);
            for(int i=0;i<settleScratch.Count;i++)SendEnvelopeCore(settleScratch[i].Key,settleScratch[i].Value,true,false);
        }
        static void SendEnvelope(ulong peer,byte[] bytes,bool reliable){SendEnvelopeCore(peer,bytes,reliable,true);}
        static void SendEnvelopeCore(ulong peer,byte[] bytes,bool reliable,bool mayGoLive)
        {
            if(!Connected || bytes==null || bytes.Length==0)return;
            ulong liveKey=0;
            if(mayGoLive && reliable && liveUsablesEnabled && bytes[0]==LiveUsablePolicy.Type && liveUsables.Offer(peer,bytes,Now,out liveKey))reliable=false;
            else liveKey=0;
            bytes=PoseSequence.Stamp(bytes,reliable?PoseSequence.Unordered:poseCounter.Next(ClockMs()));
            if(reliable){if(IsBulkEnvelope(bytes))bulkOutbox.Send(peer,bytes,true,Now);else outbox.Send(peer,bytes,true,Now);return;}
            NetDeliveryLane lane;
            if(liveKey!=0)lane=NetDeliveryLane.Motion;else Classify(bytes,out lane);
            // Latest-value types replace their own still-queued older frame.
            ulong key=liveKey!=0?liveKey:lane==NetDeliveryLane.Motion && NetStats.CoalesceEnabled?StablePackets.LatestValueKey(bytes):0;
            NetStats.Offered(bytes);
            delivery.Enqueue(peer,lane,false,key,bytes);
        }
        static bool TryEnvelope(ulong peer,byte[] bytes,bool reliable)
        {
            if(!Connected)return false;
            if(loopbackMode)
            {
                if(!Loopback.Send(peer,bytes,reliable))return false;
                networkingScript.s.hood.dataOut+=bytes.Length;return true;
            }
            if(steamTransport==null || !steamTransport.Send(peer,bytes,NetDeliveryLane.Critical,reliable))return false;
            networkingScript.s.hood.dataOut+=bytes.Length;return true;
        }
        static bool TryBulkEnvelope(ulong peer,byte[] bytes,bool reliable)
        {
            if(!Connected)return false;
            if(loopbackMode)
            {
                if(!Loopback.Send(peer,bytes,reliable))return false;
                networkingScript.s.hood.dataOut+=bytes.Length;return true;
            }
            if(steamTransport==null || !steamTransport.Send(peer,bytes,NetDeliveryLane.Bulk,reliable))return false;
            networkingScript.s.hood.dataOut+=bytes.Length;return true;
        }
        static bool IsBulkEnvelope(byte[] bytes)
        {
            if(bytes==null || bytes.Length==0 || bytes[0]!=254)return bytes!=null && bytes.Length>0 && bytes[0]==8;
            StableMessage message=StableMessage.Decode(bytes);
            return message!=null && (message.Op==StableOp.MapPart || message.Op==StableOp.MapRequest || message.Op==StableOp.Begin || message.Op==StableOp.Part || message.Op==StableOp.End || message.Op==StableOp.Ack);
        }
        static void Classify(byte[] bytes,out NetDeliveryLane lane)
        {
            lane=NetDeliveryLane.Motion;
            if(bytes==null || bytes.Length==0)return;
            if(bytes[0]==254)
            {
                StableMessage message=StableMessage.Decode(bytes);
                lane=message!=null && (message.Op==StableOp.MapPart || message.Op==StableOp.MapRequest || message.Op==StableOp.Begin || message.Op==StableOp.Part || message.Op==StableOp.End || message.Op==StableOp.Ack)?NetDeliveryLane.Bulk:NetDeliveryLane.Critical;
                return;
            }
            if(bytes[0]==8){lane=NetDeliveryLane.Bulk;return;}
            if(StablePackets.IsReliable(bytes))lane=NetDeliveryLane.Critical;
        }
        static bool EnsureSteamTransport(ulong lobby,ulong host)
        {
            if(loopbackMode)return true;
            if(steamTransport!=null && transportLobby==lobby && transportHost==host)
            {
                steamTransport.AcceptPeer=delegate(ulong peer){return IsLobbyTransportPeer(lobby,peer);};
                if(Own!=host && !steamTransport.HasConnection(host))steamTransport.Connect(host,TransportVirtualPort);
                return true;
            }
            if(Now<nextSdrRetry)return false;
            if(steamTransport!=null)steamTransport.Dispose();
            steamTransport=null;transportLobby=transportHost=0;
            try
            {
                steamTransport=new SteamSdrTransport();transportLobby=lobby;transportHost=host;
                steamTransport.Trace=delegate(string message){Debug.Log("[TLD MP SDR] "+message);};
                steamTransport.ReceiveFailure+=delegate(Exception error){Debug.LogError("[TLD MP SDR] transport failure: "+error);};
                steamTransport.AcceptPeer=delegate(ulong peer){return IsLobbyTransportPeer(lobby,peer);};
                steamTransport.PeerDisconnected+=OnSteamPeerDisconnected;
                if(Own==host)steamTransport.Listen(TransportVirtualPort);
                // The native password exchange itself needs this connection.
                else steamTransport.Connect(host,TransportVirtualPort);
                return true;
            }
            catch(Exception e)
            {
                if(steamTransport!=null)steamTransport.Dispose();steamTransport=null;transportLobby=transportHost=0;nextSdrRetry=Now+2;
                Debug.LogError("[TLD MP v3] Steam Datagram Relay setup failed; multiplayer transport will retry: "+e.Message);return false;
            }
        }
        static bool IsLobbyTransportPeer(ulong lobby,ulong peer)
        {
            if(lobby==0 || peer==0 || networkingScript.s==null || networkingScript.s.hood==null ||
                (ulong)networkingScript.s.hood.ConnectedLobbyID!=lobby)return false;
            var lobbyID=new CSteamID(lobby);int count=SteamMatchmaking.GetNumLobbyMembers(lobbyID);
            for(int i=0;i<count;i++)if((ulong)SteamMatchmaking.GetLobbyMemberByIndex(lobbyID,i)==peer)return true;
            return false;
        }
        static void OnSteamPeerDisconnected(ulong peer,int reason,string detail)
        {
            outbox.Drop(peer);bulkOutbox.Drop(peer);delivery.Drop(peer);
            if(node==null)return;
            string message="SDR peer disconnected ("+reason+"): "+(detail??"");
            if(node.IsHost)node.RemovePeer(peer);else if(peer==node.Host)node.Stop(message);
            Debug.LogWarning("[TLD MP v3] Peer "+peer+" "+message);
        }
        static void DrainDeliveryQueues()
        {
            var peers=delivery.Peers;if(peers.Length==0){deliveryPeerCursor=0;return;}
            int packetsLeft=32,bytesLeft=1024*1024;int first=deliveryPeerCursor%peers.Length;
            for(int i=0;i<peers.Length && packetsLeft>0 && bytesLeft>0;i++)
            {
                int peerIndex=(first+i)%peers.Length;ulong peer=peers[peerIndex];int bytesSent=0;
                NetStats.Pending(delivery.PendingCount(peer),delivery.PendingBytes(peer));
                healthMaxPending=Math.Max(healthMaxPending,delivery.PendingCount(peer));
                healthMaxPendingBytes=Math.Max(healthMaxPendingBytes,delivery.PendingBytes(peer));
                int sent=delivery.Drain(peer,Math.Min(8,packetsLeft),bytesLeft,delegate(ulong target,NetDeliveryLane lane,byte[] bytes,bool reliable)
                {
                    bool admitted;
                    if(loopbackMode)admitted=Loopback.Send(target,bytes,reliable);
                    else admitted=steamTransport!=null && steamTransport.Send(target,bytes,lane,reliable);
                    if(admitted && networkingScript.s!=null && networkingScript.s.hood!=null)networkingScript.s.hood.dataOut+=bytes.Length;
                    if(admitted){bytesSent+=bytes.Length;NetStats.Sent(bytes);}
                    return admitted;
                });
                packetsLeft-=sent;bytesLeft-=bytesSent;deliveryPeerCursor=(peerIndex+1)%peers.Length;
            }
        }
        public static CSteamID LoopbackOwnID()
        {
            if(loopbackMode)return new CSteamID(Loopback.Own);
            return SteamP2PNetworkingUnderTheHoodScript.IsSteam()?SteamUser.GetSteamID():new CSteamID(0UL);
        }
        public static CSteamID LocalIdentity()
        {
            if(loopbackMode)return new CSteamID(Loopback.Own);
            return SteamUser.GetSteamID();
        }
        public static SteamP2PMenuHandlerScript.startSpawnType LoopbackSpawnType()
        {
            if(loopbackMode)return SteamP2PMenuHandlerScript.startSpawnType.nearHost;
            return (SteamP2PMenuHandlerScript.startSpawnType)int.Parse(SteamMatchmaking.GetLobbyData(networkingScript.s.hood.ConnectedLobbyID,"spawntype"));
        }
        public static int LoopbackPhysicsLock()
        {
            if(loopbackMode)return settingsscript.s==null?0:settingsscript.s.S.IPhysicsLock;
            return int.Parse(SteamMatchmaking.GetLobbyData(networkingScript.s.hood.ConnectedLobbyID,"physicslock"));
        }
        static void TransportFailed(ulong peer,string reason)
        {
            delivery.Drop(peer);bulkOutbox.Drop(peer);if(steamTransport!=null)steamTransport.Close(peer,reason);
            if(node==null)return;Debug.LogError("[TLD MP v3] Peer "+peer+": "+reason);
            if(node.IsHost)node.RemovePeer(peer);else node.Stop(reason);
        }
        // ---- transport adapters ---------------------------------------------------
        // Native message bytes, reliability and order are preserved. The only
        // transformation is the cargo-protection exception below: a position-only
        // update for an object that native attachment/physlock state says is
        // stored/mounted is re-framed as the native parent-local message (or
        // dropped when no native parent is resolvable), so it can never turn a
        // stored object into a free world body. No parenting/Rigidbody writes.
        static byte[] ProtectCargoPose(byte[] bytes)
        {
            if(bytes==null || bytes.Length==0 || bytes[0]!=(byte)msgType.itemPosGlobal)return bytes;
            uint id=BitConverter.ToUInt32(bytes,1);tosaveitemscript item;
            if(!Find(id,out item) || item.P==null || item.useThisForPos==null)return bytes;
            bool stored=item.P.physlock!=null;
            bool attached=item.attachable!=null && item.attachable.attached;
            var hood=networkingScript.s==null?null:networkingScript.s.hood;
            bool dataReply=hood!=null && hood.forceReliable && hood.forceOneSendTarget;
            CargoPoseDisposition disposition=CargoPosePolicy.Classify(stored,attached,dataReply);
            if(disposition==CargoPoseDisposition.World)
            {
                // A loose item riding in a moving car goes car-relative (CarCargo).
                if(!dataReply){byte[] relative=CarCargo.RelativePose(item);if(relative!=null)return relative;}
                return bytes;
            }
            if(disposition==CargoPoseDisposition.LocalStorage)
            {
                tosaveitemscript parent;Transform parentTransform;int index;
                item.GetLocalStuff(out parent,out parentTransform,out index);
                if(parent!=null && parentTransform!=null && index!=-1)
                {
                    Vector3 local=parentTransform.InverseTransformPoint(item.useThisForPos.position);
                    Vector3 rot=(Quaternion.Inverse(parentTransform.rotation)*item.useThisForPos.rotation).eulerAngles;
                    return DataByteConverterScript.GetBytes(msgType.itemPosLocal,item.idInSave,parent.idInSave,index,local,rot);
                }
                DiagLimited("cargo-suppress:"+id,"cargo-suppress id="+id+" reason=unresolved-parent",.25);
            }
            else DiagLimited("cargo-suppress:"+id,"cargo-suppress id="+id+" reason=attached",.25);
            return null;
        }
        static bool RepairRequest(byte type){return type==21 || type==22 || type==46 || type==12 || type==13;}
        static bool RoutePasswordBootstrap(ulong target,byte[] bytes)
        {
            if(loopbackMode || node==null || bytes==null || bytes.Length==0)return false;
            byte type=bytes[0];
            if(type!=(byte)msgType.password && type!=(byte)msgType.kick)return false;
            // Only the client may submit a password, and only the host may
            // answer or reject. Steam lobby membership is checked on both ends.
            if(!StablePackets.PasswordBootstrapAllowed(node.IsHost,Own,node.Host,target,IsLobbyTransportPeer(node.Lobby,target),bytes))return true;
            SendEnvelopeCore(target,bytes,true,false);return true;
        }
        public static void RouteBroadcast(byte[] bytes,EP2PSend mode)
        {
            if(!Connected || bytes==null || bytes.Length==0)return;Ensure();
            var hood=networkingScript.s.hood;
            if(capturing){captured.Add((byte[])bytes.Clone());return;}
            if(node!=null && !node.IsHost && RoutePasswordBootstrap(node.Host,bytes))return;
            if(SuppressJoiningPlayerPosition(bytes))return;
            // Suppress only the echo of a message currently being applied from
            // the world snapshot. Native repair/ask replies are still allowed.
            if(dispatching && !RepairRequest(bytes[0]))return;
            ObserveNativeSafetyOutgoing(bytes);
            if(hood.forceOneSendTarget){RouteTarget(hood.oneSendTarget,bytes,mode);return;}
            if(node==null)return;
            if(bytes[0]==8)
            {
                if(node.IsHost)
                {
                    if(loopbackMode){QueueMap(Loopback.Peer,bytes);return;}
                    for(int i=0;i<SteamMatchmaking.GetNumLobbyMembers(hood.ConnectedLobbyID);i++){ulong id=(ulong)SteamMatchmaking.GetLobbyMemberByIndex(hood.ConnectedLobbyID,i);if(id!=Own)QueueMap(id,bytes);}
                }
                return;
            }
            if(bytes[0]==9){if(!node.IsHost && !node.Joining && !node.AwaitingSnapshot && (!loopbackMode || WorldSceneAvailable()))node.RequestSnapshot();return;}
            byte[] protectedBytes=ProtectCargoPose(bytes);
            bool forced=hood.forceReliable || mode==EP2PSend.k_EP2PSendReliable || mode==EP2PSend.k_EP2PSendReliableWithBuffering;
            if(protectedBytes!=null && CaptureVehicleMotion(0,protectedBytes,forced))return;
            if(protectedBytes!=null)node.PublishNative(protectedBytes,hood.forceReliable || mode==EP2PSend.k_EP2PSendReliable || mode==EP2PSend.k_EP2PSendReliableWithBuffering);
        }
        public static void RouteTarget(CSteamID target,byte[] bytes,EP2PSend mode)
        {
            if(!Connected || bytes==null || bytes.Length==0)return;Ensure();
            if(capturing){captured.Add((byte[])bytes.Clone());return;}
            if(RoutePasswordBootstrap((ulong)target,bytes))return;
            if(SuppressJoiningPlayerPosition(bytes))return;
            if(dispatching && !RepairRequest(bytes[0]))return;
            ObserveNativeSafetyOutgoing(bytes);
            if(node==null)return;
            if(bytes[0]==8){QueueMap((ulong)target,bytes);return;}
            if(bytes[0]==9){if(!node.IsHost && !node.Joining && !node.AwaitingSnapshot && (!loopbackMode || WorldSceneAvailable()))node.RequestSnapshot();return;}
            byte[] protectedBytes=ProtectCargoPose(bytes);
            if(protectedBytes==null)return;
            bool forceReliable=networkingScript.s.hood.forceReliable || mode==EP2PSend.k_EP2PSendReliable || mode==EP2PSend.k_EP2PSendReliableWithBuffering;
            if(CaptureVehicleMotion((ulong)target,protectedBytes,forceReliable))return;
            if(node.IsHost)node.PublishNativeTo((ulong)target,protectedBytes,forceReliable);
            else node.PublishNative(protectedBytes,forceReliable);
        }
        static void QueueMap(ulong peer,byte[] bytes)
        {if(node==null || !node.IsHost)return;if(node.Peers.ContainsKey(peer))node.SendBootstrap(peer,bytes);else maps[peer]=(byte[])bytes.Clone();}
        // ---- structural validation ------------------------------------------------
        // Lengths, types, embedded IDs and bounded allocations only. Native
        // messages are never rejected for owner/epoch/lease/session reasons.
        static bool ValidateData(byte[] bytes)
        {
            if(!StablePackets.Valid(bytes))return false;
            int type=bytes[0];
            if(type==16 || type==39)
            {
                int prefab=BitConverter.ToInt32(bytes,5);GameObject template;
                if(!itemdatabase.s.Item(prefab,out template))return false;
                uint id=BitConverter.ToUInt32(bytes,1);
                tosaveitemscript item;
                if(Find(id,out item) && item.id!=prefab)return false;
                int ix;if(savedatascript.s!=null && savedatascript.s.data!=null && savedatascript.IndexOfID(savedatascript.s.data.itemData.items,id,out ix) && savedatascript.s.data.itemData.items[ix].prefabID!=prefab)return false;
            }
            if(type==37)
            {
                if(Math.Abs(BitConverter.ToSingle(bytes,5))>1.01f)return false;
                for(int i=9;i<=17;i+=4){float x=BitConverter.ToSingle(bytes,i);if(x<0 || x>1.01f)return false;}
                tosaveitemscript item;if(Find(BitConverter.ToUInt32(bytes,1),out item) && item.car==null)return false;
            }
            if(type==41)
            {
                int p=bytes[1]!=0?34:10;int count=BitConverter.ToInt32(bytes,p);int second=p+4+count*4;
                if(second+4>bytes.Length || BitConverter.ToInt32(bytes,second)!=count)return false;
                for(int i=0;i<count;i++)if(BitConverter.ToSingle(bytes,second+4+i*4)<0)return false;
            }
            if(type==43)
            {
                int p=5,n=-1;for(int i=0;i<4;i++){int count=BitConverter.ToInt32(bytes,p);if(n!=-1 && n!=count)return false;n=count;p+=4+count*12;}
            }
            return true;
        }
        // Purely observational: the newest remote player/held state rides the
        // join snapshot so a late joiner sees peers' current authoritative state.
        static void ObserveNative(ulong origin,byte[] bytes)
        {
            if(bytes==null || bytes.Length==0)return;
            byte type=bytes[0];
            if(type==10)
            {
                if(lastPlayers.Count<64 || lastPlayers.ContainsKey(origin))lastPlayers[origin]=(byte[])bytes.Clone();
                return;
            }
            if(type==17 || type==18)
            {
                uint id=StablePackets.Item(bytes);if(id==0)return;
                if(lastItems.Count>=4096 && !lastItems.ContainsKey(id))return;
                lastItems[id]=new MessageState {Origin=origin,Payload=Convert.ToBase64String(bytes)};return;
            }
            if(type==36 || type==16 || type==39)lastItems.Remove(StablePackets.Item(bytes));
        }
        public static bool AllowIncoming(CSteamID sender,byte[] bytes)
        {
            if(bytes==null || bytes.Length==0)return false;
            if(bytes[0]==254)
            {
                Ensure();if(node==null)return false;
                node.Receive((ulong)sender,bytes,Now);
                byte[] map;if(node.IsHost && node.Peers.ContainsKey((ulong)sender) && maps.TryGetValue((ulong)sender,out map)){maps.Remove((ulong)sender);node.SendBootstrap((ulong)sender,map);}
                return false;
            }
            if(dispatching)return true;
            if(!Connected)return false;
            Ensure();
            // While the fingerprint is still being computed there is no
            // authenticated session; never pass native traffic to the game.
            if(node==null)return false;
            if(!ValidateData(bytes))
            {
                Debug.LogWarning("[TLD MP v3] Rejected structurally invalid native packet type "+(bytes.Length>0?bytes[0].ToString():"?")+" length="+bytes.Length+
                    (DiagnosticMode && bytes.Length<=96?" hex="+BitConverter.ToString(bytes):""));
                return false;
            }
            if(!AllowNativeSafetyPacket(bytes))return false;
            if(UnsetPaint(bytes))return false;
            if(NotePendingItem(sender,bytes))return false;
            NoteCargoPose(bytes);
            ObserveNative((ulong)sender,bytes);
            if(bytes.Length>0 && bytes[0]>=16 && bytes[0]<=19)CargoDiagnostics.NotePacket(true,bytes[0],StablePackets.Item(bytes),(ulong)sender);
            return true;
        }
        public static bool AllowForward(){return !dispatching && !applyingVehicleMotion;}
        // Replaces stock networkingScript.DebugLog via a Guard hook. The hook always
        // returns false, so the native body never runs; NetLogWrite performs the same
        // effects (Debug.Log, console append, chat scroll, newMessage, NewTime) with a
        // per-key rate limit and a console cap, so message floods cannot freeze the game.
        static readonly NetLogLimiter netLogLimiter=new NetLogLimiter(NetLogLimiter.DefaultLimit,NetLogLimiter.DefaultWindow);
        const int NetConsoleCapTrigger=16000,NetConsoleCapKeep=12000;
        public static bool NetLog(string message)
        {
            string text=message??"";string summary;
            if(!netLogLimiter.Offer(text,Now,out summary))return false;
            if(summary!=null)NetLogWrite(summary);
            NetLogWrite(text);
            return false;
        }
        static void NetLogWrite(string text)
        {
            Debug.Log(text);
            // Same console effects as the stock body. If the menu is not available yet
            // the line is still in the Unity log, exactly as the stock path logs it first.
            try
            {
                var menu=networkingScript.Menu();
                var console=menu.console;
                console.text=console.text+"\n"+SteamP2PNetworkingUnderTheHoodScript.GetTimeString()+": "+text;
                if(console.text.Length>NetConsoleCapTrigger)console.text=console.text.Substring(console.text.Length-NetConsoleCapKeep);
                menu.StartCoroutine(menu.DoChatDownScroll());
                menu.newMessage=true;
                menu.NewTime();
            }
            catch(Exception){}
        }
        // Role line for the F9 feedback bundle's info.txt.
        internal static string SessionRole()
        {
            string role=node==null?"no session":(node.IsHost?"host":"client");
            return role+(Connected?", connected":", not connected")+(loopbackMode?", loopback":"");
        }
        // One delivery to the stock handler per native message. Never re-applied
        // twice; the guard only suppresses the echo of the same message.
        // color (32): uint id, int index, Color rgba. See PaintPolicy.
        static bool UnsetPaint(byte[] bytes)
        {
            if(bytes==null || bytes.Length<25 || bytes[0]!=(byte)msgType.color)return false;
            if(!PaintPolicy.IsUnsetPaint(BitConverter.ToSingle(bytes,9),BitConverter.ToSingle(bytes,13),BitConverter.ToSingle(bytes,17),BitConverter.ToSingle(bytes,21)))return false;
            unsetPaintSkipped++;return true;
        }
        internal static long unsetPaintSkipped;
        // itemPosLocal (17): uint id, uint parent, int index, Vector3 pos, Vector3 rot.
        static void NoteCargoPose(byte[] bytes)
        {
            if(bytes==null || bytes.Length<5)return;
            if(bytes[0]==(byte)msgType.itemPosGlobal){CarCargo.NoteWorld(BitConverter.ToUInt32(bytes,1));return;}
            if(bytes[0]!=(byte)msgType.itemPosLocal || bytes.Length<37)return;
            CarCargo.NoteLocal(BitConverter.ToUInt32(bytes,1),BitConverter.ToUInt32(bytes,5),BitConverter.ToInt32(bytes,9),
                new Vector3(BitConverter.ToSingle(bytes,13),BitConverter.ToSingle(bytes,17),BitConverter.ToSingle(bytes,21)),
                new Vector3(BitConverter.ToSingle(bytes,25),BitConverter.ToSingle(bytes,29),BitConverter.ToSingle(bytes,33)));
        }
        static void Deliver(ulong origin,byte[] bytes)
        {
            if(!ValidateData(bytes))return;
            if(UnsetPaint(bytes))return;
            NoteCargoPose(bytes);
            if(!AllowNativeSafetyPacket(bytes))return;
            if(node!=null && !node.IsHost && origin==node.Host && bytes[0]==8)
            {
                hostWorldEvidence=true;
                if(loopbackMode){loopbackPreviousMap=menuhandler.s==null?null:menuhandler.s.currentMainMap;loopbackBootstrapReceived=true;}
            }
            bool previous=dispatching;dispatching=true;
            var hood=networkingScript.s.hood;
            bool reliable=hood.forceReliable,one=hood.forceOneSendTarget;var target=hood.oneSendTarget;
            try{networkingScript.s.ProcessMessage(new CSteamID(origin),bytes,(msgType)bytes[0]);}
            finally{dispatching=previous;hood.forceReliable=reliable;hood.forceOneSendTarget=one;hood.oneSendTarget=target;}
        }
        // ---- ID collision prevention ---------------------------------------------
        static uint HighestID()
        {
            uint max=0;var save=savedatascript.s;if(save==null)return max;
            if(save.data!=null){max=save.data.lastID;if(save.data.itemData!=null)foreach(var i in save.data.itemData.items)max=Math.Max(max,i.id);}
            if(save.items!=null)foreach(var id in save.items.Keys)max=Math.Max(max,id);return max;
        }
        public static uint NewKey(savedatascript save)
        {
            Ensure();
            if(node==null && Connected){FinishFingerprintJob();Ensure();}
            if(node==null)throw new InvalidOperationException("Network ID allocation called without a connected session.");
            return node.AllocateID(delegate(uint id){if(save.items.ContainsKey(id))return true;int ix;return savedatascript.IndexOfID(save.data.itemData.items,id,out ix);});
        }
        public static bool UseOriginalIDs(){if(!Connected)return true;if(node==null)Ensure();return false;}
        // ---- saved-item selection (read-only phase only) -------------------------
        static readonly ItemIndexTable selection=new ItemIndexTable();
        const int SelectionMinItems=64;
        const int SelectionMinLookups=4;
        public static void ItemSelectionBegin(itemPlaceRemoveScript self)
        {
            selection.Clear();
            try
            {
                var save=savedatascript.s;
                if(self==null || save==null || save.data==null || save.data.itemData==null)return;
                var items=save.data.itemData.items;
                if(items==null || items.Count<SelectionMinItems)return;
                var chunks=save.itemChunks;
                if(chunks==null)return;
                var placeChunks=self.placeChunks;
                if(placeChunks==null || placeChunks.Count==0)return;
                long lookups=0;
                for(int i=0;i<placeChunks.Count;i++)
                {
                    List<uint> ids;
                    if(chunks.TryGetValue(placeChunks[i],out ids) && ids!=null)lookups+=ids.Count;
                }
                if(lookups<SelectionMinLookups)return;
                selection.Build(items);
            }
            catch{selection.Clear();}
        }
        public static bool ItemSelectionLookup(List<save_item> items,uint id,out int index)
        {
            int value;
            if(selection.TryGet(items,id,out value)){index=value;return value>=0;}
            selection.Clear();
            return savedatascript.IndexOfID(items,id,out index);
        }
        public static void SortPlaceOrder(List<Vector3d> order,Vector3d origin){StableMap.SortPlaceOrder(order,origin);}
        public static void TerrainHoleBegin(){TerrainHoleAdjacency.Begin();}
        public static List<int> TerrainHoleNeighbors(int[] triangles,int vertex){return TerrainHoleAdjacency.Get(triangles,vertex);}
        public static List<roadGenScript.roadClass> DedupeTerrainRoads(List<roadGenScript.roadClass> roads){return TerrainRoadSelection.Dedupe(roads);}
        // ---- world streaming hooks ------------------------------------------------
        public static bool RoadUpdate(roadGenScript road,bool more){if(road!=null && !road.isActiveAndEnabled){RoadLiveCache.Clear();RoadFarSkip.Clear();}if(!Connected)return true;return EnsureWorldSession() && StableMap.RoadUpdate(road,more);}
        public static bool RoadChunkKeep(roadGenScript road,List<Vector3d> needed,Vector3d cell){return RoadChunkPolicy.Keep(road,needed,cell);}
        public static void RoadChunkDirty(roadGenScript.roadChunk road){RoadLiveCache.Dirty(road);}
        public static bool RoadChunkMayHaveLive(roadGenScript.dotChunkClass chunk,roadGenScript.roadChunk road){return RoadLiveCache.MayHaveLive(chunk,road);}
        public static void RoadChunkDestroyed(roadGenScript.roadChunk road){RoadLiveCache.Destroyed(road);}
        public static void RoadDedupeNeeded(List<Vector3d> needed){RoadFarSkip.Dedupe(needed);}
        public static bool RoadChunkSkippable(roadGenScript road,roadGenScript.dotChunkClass chunk){return RoadFarSkip.Skippable(road,chunk);}
        public static void RoadBaked(roadscript road){RoadBake.AfterBake(road);}
        public static void CarVelocitySet(Rigidbody body,Vector3 value){if(body!=null && !ParkedCarSleepPolicy.ShouldWriteVelocity(body.IsSleeping(),body.velocity.x,body.velocity.y,body.velocity.z,value.x,value.y,value.z))return;body.velocity=value;}
        // itemPlaceRemoveScript.RemoveStuff measures distance from this point instead of the
        // item's own transform position. Parented items use their root (see ItemStreamPolicy).
        // A nested terrain-align helper is measured from the prefab root; direct children keep the stock localPosition.
        public static void TerrainAlignLookupRange(poiGenScript.poiClass poi){foreach(var align in poi.terrainAligns){var d=align.gpos-poi.pos;poi.maxAlignEffectRange=TerrainAlignPolicy.LookupRange(poi.maxAlignEffectRange,d.x,d.z,align.maxEffectRange);}}
        public static Vector3 TerrainAlignLocal(Transform t){var v=TerrainAlignPolicy.UsesRootRelative(t.parent!=null,t.parent==t.root)?t.root.InverseTransformPoint(t.position):t.localPosition;if(t.parent!=null && TerrainAlignPolicy.LevelStartingBusMarker(t.root.name,t.parent.name))v.y=0f;return v;}
        public static Vector3 ItemStreamPosition(Transform t){return ItemStreamPolicy.UsesRootAnchor(t.parent!=null)?t.root.position:t.position;}
        // Guard on tosaveitemscript.DistUnFreezeRB. A kinematic item is kept frozen while no
        // collider lies below it (terrain not loaded yet), for at most ItemSettlePolicy.MaxHoldSeconds.
        static readonly RaycastHit[] itemSettleHits=new RaycastHit[32];
        static readonly ItemSettlePolicy itemSettle=new ItemSettlePolicy();
        public static bool ItemUnfreezeGate(tosaveitemscript item)
        {
            if(item==null)return true;
            int id=item.GetInstanceID();
            if(item.RB==null || !item.RB.isKinematic){itemSettle.Forget(id);return true;}
            pickupable p=item.P;
            bool owned=item.buried || (p!=null && (p.inRightHand || p.physlock!=null)) || (item.attachable!=null && item.attachable.attached);
            bool ground=!owned && ItemHasGround(item);
            return !itemSettle.ShouldHold(id,owned,ground,Time.realtimeSinceStartup);
        }
        public static string TakeItemSettleSummary(){return itemSettle.TakeSummary();}
        // PlaceStuff's far PlaceOne call lands here. The id is recorded only: the
        // index in this pass goes stale, so FarPlaceQueue re-reads it on drain.
        public static tosaveitemscript EnqueueFarPlace(itemPlaceRemoveScript script,uint id,int index)
        {
            // Same receiver/arguments as the native call; null script still throws as native does.
            if(FarPlaceQueue.MustPlaceNow(script))return script.PlaceOne(id,index);
            FarPlaceQueue.Enqueue(id);return null;
        }
        public static string TakeFarPlaceSummary(){return FarPlaceQueue.TakeSummary();}
        public static string TakeTerrainWorkerSummary(){return TerrainWorker.TakeSummary();}
        internal static void FlushFarPlace(){FarPlaceQueue.FlushAll();}
        static bool ItemHasGround(tosaveitemscript item)
        {
            Transform self=item.transform;
            Vector3 center=self.position;
            Collider[] colliders=item.GetComponentsInChildren<Collider>();
            if(colliders.Length>0)
            {
                Bounds bounds=colliders[0].bounds;
                for(int i=1;i<colliders.Length;i++)bounds.Encapsulate(colliders[i].bounds);
                center=bounds.center;
            }
            Vector3 origin=center+Vector3.up*1f;
            int count=Physics.RaycastNonAlloc(origin,Vector3.down,itemSettleHits,200f,~0,QueryTriggerInteraction.Ignore);
            RaycastHit[] hits=itemSettleHits;
            // NonAlloc does not guarantee which hits fit; a dense column must not hide the ground.
            if(count==hits.Length){hits=Physics.RaycastAll(origin,Vector3.down,200f,~0,QueryTriggerInteraction.Ignore);count=hits.Length;}
            for(int i=0;i<count;i++)
            {
                Collider hit=hits[i].collider;
                if(hit==null || !hit.enabled || hit.transform.IsChildOf(self))continue;
                if(hit.GetComponentInParent<fpscontroller>()!=null || hit.GetComponentInParent<mpplayerscript>()!=null)continue;
                return true;
            }
            return false;
        }
        public static void TankPourTick(tankcapscript tank){if(tank==null || !tank.started || tank.ps==null)return;int action=IdlePourPausePolicy.Decide(tank.em.enabled,tank.ps.isPaused,tank.ps.particleCount);if(action==IdlePourPausePolicy.Pause)tank.ps.Pause();else if(action==IdlePourPausePolicy.Resume)tank.ps.Play();}
        public static void WornItemDestroyed(pickupable item){WornItemGuard.Destroyed(item);}
        public static void HideListPrune(makeinvisible hide){WornItemGuard.PruneHide(hide);}
        public static void SafeSlice(randomSlice slice,bool darabForce,bool darabAlsoVagas,Vector3 terrainNormal,Vector3 lossyScale){try{slice.SSSLiceee(darabForce,darabAlsoVagas,terrainNormal,lossyScale);}catch(Exception error){SliceGuard.Failed(error);}}
        public static bool MirrorVisible(Renderer renderer,Camera camera){return MirrorOnScreen.Visible(renderer,camera);}
        public static bool CarPoseNeedsBurst(tosaveitemscript item){return CarPosePolicy.NeedsBurst(item);}
        public static bool CarPoseMoreUpd(tosaveitemscript item){return item.MoreUpd() || CarPosePolicy.NeedsBurst(item);}
        // Runs instead of MorePosUpd while a remote player rides in this moving car.
        // The native burst covers one sync interval and then waits for the item
        // sweep to reach the car again, which left ~1 s holes in the passenger's
        // view. This stream sends at the target rate until the next sweep starts a
        // newer stream for the same car (which ends this one), so the rate stays
        // the target rate with no gap and no doubling.
        static readonly Dictionary<int,int> carStreams=new Dictionary<int,int>();
        static int carStreamGeneration;
        public static System.Collections.IEnumerator CarPoseBurst(tosaveitemscript item)
        {
            if(item==null) yield break;
            int key=item.GetInstanceID();
            int generation=++carStreamGeneration;
            carStreams[key]=generation;
            var wait=new WaitForSeconds(CarPosePolicy.StreamSpacing(CarPosePolicy.TargetHz));
            float start=Time.time;
            try
            {
                while(true)
                {
                    yield return wait;
                    int newest;
                    bool isNewest=carStreams.TryGetValue(key,out newest) && newest==generation;
                    if(item==null || !CarPosePolicy.ContinueStream(isNewest,item.ThisOneInCharge(),CarPosePolicy.NeedsBurst(item),Time.time-start)) yield break;
                    item.ForcePosUpd(false,false,false);
                }
            }
            finally
            {
                int newest;
                if(carStreams.TryGetValue(key,out newest) && newest==generation) carStreams.Remove(key);
            }
        }
        public static void MayRemoveMissingItem(syncScript self,uint id)
        {
            savedatascript save=savedatascript.s;
            bool exists=save!=null && save.items!=null && save.items.ContainsKey(id);
            int savedIndex;
            bool savedRecord=save!=null && save.data!=null && save.data.itemData!=null && save.data.itemData.items!=null &&
                savedatascript.IndexOfID(save.data.itemData.items,id,out savedIndex);
            bool clientReserved=false;
            if(node!=null && node.IsHost)
                foreach(var peer in node.Peers.Values)
                    if(id>=peer.First && id<=peer.Last){clientReserved=true;break;}
            if(!ItemAskSafety.MayRemove(exists,savedRecord,clientReserved))
            {
                if(!exists)DiagLimited("item-ask-preserved:"+id,"native missing-item delete suppressed id="+id+" saved="+savedRecord+" clientReserved="+clientReserved,5);
                return;
            }
            if(self!=null)self.SendRemoveItem(id);
        }
        public static void IgnoreCollisionSafe(mpplayerscript script,Transform target,bool ignore){PlayerLeaveSafety.IgnoreCollision(script,target,ignore);}
        internal static long DeliveryCoalesced{get{return delivery.Coalesced;}}
        public static bool CreatureMoveGate(movecreaturescript creature){return WildlifeWeather.CreatureMove(creature);}
        public static bool CreatureJumpGate(movecreaturescript creature){return WildlifeWeather.CreatureJump(creature);}
        // Guard on tosaveitemscript.ThisOneInChargeCar(carWhatDoing). False = this
        // machine is not in charge (CarOwnershipPolicy); true = the native rule.
        public static bool CarChargeGate(tosaveitemscript item,tosaveitemscript.carWhatDoing remote)
        {
            try
            {
                if(item==null || item.car==null || !Connected || node==null || node.IsHost)return true;
                int local=(int)item.CarWhatDoing();
                if(local!=CarOwnershipPolicy.Shotgun)return true;
                if(!CarOwnershipPolicy.VetoLocalCharge(true,false,local,HostDistance(item.transform.position)))return true;
                item.currentWhatDoing=local;   // the native body records it before deciding
                return false;
            }
            catch(Exception){return true;}
        }
        static double HostDistance(Vector3 at)
        {
            if(node==null || syncScript.s==null || syncScript.s.players==null)return -1;
            syncScript.playerSyncClass host;
            if(!syncScript.s.players.TryGetValue(new CSteamID(node.Host),out host) || host==null || host.player==null)return -1;
            return Vector3.Distance(host.player.transform.position,at);
        }
        public static syncScript.playerSyncClass GetPlayerWithScript(syncScript owner,CSteamID id){return PlayerLeaveSafety.PlayerWithScript(owner,id);}
        // A timed-out player leaves its entry behind after the proxy is destroyed.
        // Dropping those entries at update boundaries prevents later item/player
        // events from dereferencing the destroyed script. Called only outside the
        // native syncScript.Update foreach, so the dictionary is never mutated
        // while it is being enumerated.
        public static void PrunePlayers()
        {
            syncScript sync=syncScript.s;
            if(sync==null || sync.players==null || sync.players.Count==0)return;
            List<CSteamID> dead=null;
            foreach(var entry in sync.players)
            {
                syncScript.playerSyncClass player=entry.Value;
                bool hasProxy=player!=null && player.player!=null;
                bool hasScript=player!=null && player.mpscript!=null;
                if(!PlayerLeaveSafety.ShouldPrune(hasProxy,hasScript))continue;
                if(dead==null)dead=new List<CSteamID>();
                dead.Add(entry.Key);
                if(hasProxy){player.DropStuff();UnityEngine.Object.Destroy(player.player);}
            }
            if(dead!=null)for(int i=0;i<dead.Count;i++)sync.players.Remove(dead[i]);
        }
        public static bool RoadAt(roadGenScript road,Vector3d pos){if(!Connected)return true;return EnsureWorldSession() && StableMap.RoadAt(road,pos);}
        public static void RoadGenerated(roadGenScript road){RoadLiveCache.Clear();RoadFarSkip.Clear();if(Connected)StableMap.RoadGenerated(road);}
        public static bool PoiChunk(poiGenScript gen,Vector3d pos){if(!Connected)return true;return EnsureWorldSession() && StableMap.PoiChunk(gen,pos);}
        public static bool PoiAdd(poiGenScript gen){if(!Connected)return true;return EnsureWorldSession() && StableMap.PoiAdd(gen);}
        public static bool CategoryChunks(objGenScript.categoryClass cat){if(!Connected)return true;return EnsureWorldSession() && StableMap.CategoryChunks(cat);}
        public static bool CategoryGen(objGenScript.categoryClass cat,bool more){if(!Connected)return true;return EnsureWorldSession() && StableMap.CategoryGen(cat,more);}
        public static bool Terrain(terrainGenScript terrain)
        {
            string report=TerrainWorkDiagnostics.TryReport(terrain);if(report!=null)JoinDiagnostic(report);
            if(!Connected){StableMap.ProtectColliderGrid(terrain);return true;}
            return EnsureWorldSession() && StableMap.Terrain(terrain);
        }
        public static bool TerrainCheckRemove(terrainGenScript terrain){return TerrainMembership.CheckRemove(terrain);}
        // Replaces the synchronous Tile call in terrainGenScript.DoPlace. Distant
        // terrain without colliders is built on a worker; everything else runs stock.
        public static bool TerrainTile(terrainGenScript terrain,Vector3d gpos,bool regen,bool async){return TerrainWorker.Tile(terrain,gpos,regen,async);}
        // ---- snapshot / join manifest --------------------------------------------
        static WorldFrame CaptureWorld()
        {
            // Queued far items must be live before the item table is copied.
            FlushFarPlace();
            var frame=new WorldFrame {Fingerprint=fingerprint,Time=napszakvaltakozas.s!=null?napszakvaltakozas.s.t:0};
            frame.Items.Copy(savedatascript.s.data.itemData);
            foreach(var pair in savedatascript.s.items)
            {
                var item=pair.Value;if(item==null)continue;savedatascript.SaveToDictionary(item,frame.Items);
                frame.Active.Add(new ActiveItem {ID=pair.Key,Active=item.gameObject.activeInHierarchy});
            }
            var map=menuhandler.s.currentMainMap;
            for(int i=0;i<map.poiGens.Count;i++)
            {
                var gen=map.poiGens[i];foreach(var chunk in gen.chunks.Values)foreach(var p in chunk.pois.Values)
                    if(p.pobj!=null || p.save!=null)frame.Pois.Add(new PoiState {Generator=i,State=new save_poi(p)});
                foreach(var p in gen.poisFromSave.Values)frame.Pois.Add(new PoiState {Generator=i,State=new save_poi(p)});
            }
            // Capture stock player state on this main-thread boundary, including the
            // current host; remote pose cache is journaled separately below.
            capturing=true;captured=new List<byte[]>();
            try
            {
                if(mainscript.s.player!=null)syncScript.s.SendPlayerPos(mainscript.s.player);
                foreach(var pair in lastPlayers)frame.PlayerMessages.Add(new MessageState {Origin=pair.Key,Payload=Convert.ToBase64String(pair.Value)});
                foreach(var b in captured)frame.PlayerMessages.Add(new MessageState {Origin=Own,Payload=Convert.ToBase64String(b)});
                foreach(var held in lastItems.Values)frame.PlayerMessages.Add(held);
                foreach(var item in savedatascript.s.items.Values)if(item!=null && item.ThisOneInChargeItem())
                {
                    captured.Clear();item.ForcePosUpd(true,false,false);
                    foreach(var b in captured)if(b[0]==18 || b[0]==17)frame.PlayerMessages.Add(new MessageState {Origin=Own,Payload=Convert.ToBase64String(b)});
                }
            }
            finally{capturing=false;captured=null;}
            return frame;
        }
        // Snapshot timing lives outside SendWorld/ApplyWorld: the world replay
        // extracts those two methods and requires them free of other mod types.
        static void TimedSendWorld(ulong peer)
        {long start=PhaseTimings.Begin();try{SendWorld(peer);}finally{PhaseTimings.End(PhaseTimings.SnapshotSend,start,"capture+encode+validate");}}
        static void TimedApplyWorld(byte[] bytes)
        {long start=PhaseTimings.Begin();try{pendingCarBlood.Clear();pendingItems.Clear();itemRecovery.Clear();vehiclePairs.Clear();vehicleClock.Clear();ClearVehicleImpacts();RemoteCarSmoothing.Clear();ApplyWorld(bytes);}finally{PhaseTimings.End(PhaseTimings.SnapshotApply,start,bytes==null?null:"bytes="+bytes.Length);}}
        static void SendWorld(ulong peer)
        {
            JoinDiagnostic("SendWorld entry target="+peer+" "+MapDiagnostic());
            string blocked=node==null?"node=null":!node.IsHost?"not host":mainscript.s==null?"mainscript=null":savedatascript.s==null?"savedatascript=null":menuhandler.s==null?"menuhandler=null":menuhandler.s.currentMainMap==null?"currentMainMap=null":savedatascript.s.data==null?"save.data=null":null;
            if(blocked!=null){JoinDiagnostic("SendWorld blocked: "+blocked);return;}
            var frame=CaptureWorld();byte[] bytes=StableJson.Write(frame);
            var check=StableJson.Read<WorldFrame>(bytes);
            if(check.Active.Count!=frame.Active.Count || check.Pois.Count!=frame.Pois.Count || check.PlayerMessages.Count!=frame.PlayerMessages.Count)
                throw new InvalidOperationException("Snapshot serialization lost world/player records.");
            node.BeginSnapshot(peer,bytes);
            JoinDiagnostic("Snapshot JSON roundtrip active="+check.Active.Count+"/"+frame.Active.Count+" pois="+check.Pois.Count+"/"+frame.Pois.Count+" players="+check.PlayerMessages.Count+"/"+frame.PlayerMessages.Count);
            if(loopbackMode){string dump=Environment.GetEnvironmentVariable("TLD_MP_DIAGNOSTIC_DIR");if(!String.IsNullOrEmpty(dump))try{Directory.CreateDirectory(dump);File.WriteAllBytes(Path.Combine(dump,"host-snapshot.deflate"),bytes);}catch(Exception e){JoinDiagnostic("Snapshot dump failed: "+e.Message);}}
            JoinDiagnostic("SendWorld sent bytes="+bytes.Length+" items="+frame.Items.items.Count+" active="+frame.Active.Count+" pois="+frame.Pois.Count);
        }
        static void ApplyWorld(byte[] bytes)
        {
            JoinDiagnostic("ApplyWorld entry bytes="+bytes.Length+" "+MapDiagnostic());
            var frame=StableJson.Read<WorldFrame>(bytes);
            if(frame==null || frame.Version!=3 || frame.Fingerprint!=fingerprint || frame.Items==null || frame.Items.items==null || frame.Active==null || frame.Pois==null)throw new InvalidOperationException("World snapshot format/content mismatch.");
            var ids=new HashSet<uint>();var definitions=new Dictionary<uint,save_item>();
            foreach(var item in frame.Items.items)
            {GameObject prefab;if(item==null || item.id==0 || !ids.Add(item.id) || !itemdatabase.s.Item(item.prefabID,out prefab))throw new InvalidOperationException("Invalid or duplicate snapshot object.");definitions[item.id]=item;}
            foreach(var active in frame.Active)if(!definitions.ContainsKey(active.ID))throw new InvalidOperationException("Snapshot object missing its definition.");
            foreach(var p in frame.Pois)if(p.Generator<0 || p.Generator>=menuhandler.s.currentMainMap.poiGens.Count || p.State==null)throw new InvalidOperationException("Snapshot POI mismatch.");
            var stagedMessages=new List<KeyValuePair<ulong,byte[]>>();
            foreach(var message in frame.PlayerMessages)
            {
                byte[] b=Convert.FromBase64String(message.Payload);
                if(!StablePackets.Valid(b) || (b[0]!=10 && b[0]!=17 && b[0]!=18))throw new InvalidOperationException("Invalid snapshot player/item state.");
                stagedMessages.Add(new KeyValuePair<ulong,byte[]>(message.Origin,b));
            }
            BeforeNativeSafetyWorldReplacement();
            var save=savedatascript.s;
            foreach(var pair in new List<KeyValuePair<uint,tosaveitemscript>>(save.items))
                {
                    if(!definitions.ContainsKey(pair.Key) || pair.Value!=null && pair.Value.id!=definitions[pair.Key].prefabID)
                    {if(pair.Value!=null)UnityEngine.Object.Destroy(pair.Value.gameObject);save.items.Remove(pair.Key);}
                }
                save.data.itemData=frame.Items;save.itemChunks.Clear();
                foreach(var item in frame.Items.items)save.AddToChunk(mainscript.UnityPosFromGlobal(item.transform.pos),item.id);
                foreach(var active in frame.Active)
                {
                    save_item definition=definitions[active.ID];tosaveitemscript item;
                    if(!Find(active.ID,out item)){item=save.InitAfterSpawn(save.SpawnItem(definition),active.ID);item.dontSyncOnSpawn=true;}
                    item.useThisForPos.SetPositionAndRotation(mainscript.UnityPosFromGlobal(definition.transform.pos),Quaternion.Euler(definition.transform.rot.Load()));
                    item.loaded=false;save.LoadStuff(save.data,active.ID,true);item.gameObject.SetActive(active.Active);
                }
                foreach(var p in frame.Pois)
                {
                    var gen=menuhandler.s.currentMainMap.poiGens[p.Generator];bool found=false;
                    foreach(var chunk in gen.chunks.Values){poiGenScript.poiClass poi;if(chunk.pois.TryGetValue(p.State.posID,out poi)){p.State.Load(poi);if(poi.pobj!=null)p.State.Load(poi.pobj);found=true;break;}}
                    if(!found)gen.poisFromSave[p.State.posID]=p.State;
                }
                if(napszakvaltakozas.s!=null)napszakvaltakozas.s.tekeres+=frame.Time-napszakvaltakozas.s.t;
                foreach(var message in stagedMessages)if(message.Key!=Own)Deliver(message.Key,message.Value);
            JoinDiagnostic("ApplyWorld exit items="+frame.Items.items.Count+" active="+frame.Active.Count+" pois="+frame.Pois.Count+" "+MapDiagnostic());
        }
        // ---- receive / frame ------------------------------------------------------
        public static void ReadBudgeted(SteamP2PNetworkingUnderTheHoodScript hood)
        {
            if(loopbackMode){ReadLoopback(hood);return;}
            if(steamTransport==null)return;
            steamTransport.Poll(delegate(ulong source,byte[] b)
            {
                hood.dataIn+=b.Length;NetStats.Received(b);var sender=new CSteamID(source);
                if(ReceiveVehicleMotion(source,b))return;
                if(!AcceptSequenced(source,ref b))return;
                if(!SteamP2PNetworkingUnderTheHoodScript.Expected(sender,(msgType)b[0]))return;
                long handlerStart=Stopwatch.GetTimestamp();DispatchType=b[0];
                bool previousReliable=hood.forceReliable;
                // Sequence zero marks reliable one-off state. Preserve it if
                // the host's native dispatcher relays a repair pose onward.
                if(dispatchSequence==PoseSequence.Unordered && PoseSequence.Stamped(b[0]))hood.forceReliable=true;
                try{networkingScript.s.ProcessMessage(sender,b,(msgType)b[0]);}
                catch(Exception e){packetErrors++;if(Now>=nextFailure){nextFailure=Now+5;Debug.LogError("[TLD MP v3] Handler failure "+packetErrors+": "+e);}}
                finally{hood.forceReliable=previousReliable;DispatchType=-1;dispatchSequence=PoseSequence.Unordered;}
                NoteHandler(b,Stopwatch.GetTimestamp()-handlerStart);
            },128);
        }
        static void ReadLoopback(SteamP2PNetworkingUnderTheHoodScript hood)
        {
            var transport=Loopback.Transport;if(transport==null)return;
            long start=Stopwatch.GetTimestamp();
            for(int count=0;count<128 && (Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency<.004;count++)
            {
                LoopbackEvent item;if(!transport.TryDequeue(out item))break;
                if(item.Disconnected)
                {
                    outbox.Drop(item.From);bulkOutbox.Drop(item.From);delivery.Drop(item.From);
                    if(node!=null)
                    {
                        if(node.IsHost)node.RemovePeer(item.From);else node.Stop("Loopback peer disconnected. Reconnect when the host is available.");
                    }
                    Debug.LogWarning("[TLD MP loopback] Peer "+item.From+" disconnected.");
                    continue;
                }
                if(item.Connected || item.Bytes==null || item.Bytes.Length==0)continue;
                hood.dataIn+=item.Bytes.Length;NetStats.Received(item.Bytes);
                if(ReceiveVehicleMotion(item.From,item.Bytes))continue;
                byte[] received=item.Bytes;if(!AcceptSequenced(item.From,ref received))continue;item.Bytes=received;
                // Same entry point Steam uses: AllowIncoming then the stock dispatch.
                long handlerStart=Stopwatch.GetTimestamp();DispatchType=item.Bytes[0];
                bool previousReliable=hood.forceReliable;
                if(dispatchSequence==PoseSequence.Unordered && PoseSequence.Stamped(item.Bytes[0]))hood.forceReliable=true;
                try{networkingScript.s.ProcessMessage(new CSteamID(item.From),item.Bytes,(msgType)item.Bytes[0]);}
                catch(Exception e){packetErrors++;if(Now>=nextFailure){nextFailure=Now+5;Debug.LogError("[TLD MP v3] Handler failure "+packetErrors+": "+e);}}
                finally{hood.forceReliable=previousReliable;DispatchType=-1;dispatchSequence=PoseSequence.Unordered;}
                NoteHandler(item.Bytes,Stopwatch.GetTimestamp()-handlerStart);
            }
        }
        // Per-type handler cost for the net line; a single slow message is logged.
        static void NoteHandler(byte[] bytes,long ticks)
        {
            if(NetStats.Handled(bytes,ticks))
                Debug.Log("[TLD MP net] slow handler type="+bytes[0]+" ("+(msgType)bytes[0]+") bytes="+bytes.Length+" ms="+NetStats.Ms(ticks));
        }
        static bool worldJumpFailed;
        // Native type of the received message being handled, or -1 (diagnostics).
        internal static int DispatchType=-1;
        static float nativePlayerInterval=-1;
        // Applies the player position rate option to syncScript.playerPosSyncTime;
        // the game's own value is kept and restored when the option is off.
        static void TickPlayerRate()
        {
            var sync=syncScript.s;if(sync==null)return;
            if(nativePlayerInterval<0 || (sync.playerPosSyncTime!=PlayerRatePolicy.Interval(nativePlayerInterval,true) && sync.playerPosSyncTime!=nativePlayerInterval))nativePlayerInterval=sync.playerPosSyncTime;
            PatchOptions.Load();
            float wanted=PlayerRatePolicy.Interval(nativePlayerInterval,PatchOptions.HighRate);
            if(sync.playerPosSyncTime!=wanted)sync.playerPosSyncTime=wanted;
        }
        static long pulseDrainTicks,pulseTickTicks;
        // Logs any bridge Update over SlowUpdateMs with where the time went.
        static void NoteSlowUpdate(long a,long b,long c,long d,long e)
        {
            double total=(e-a)*1000.0/Stopwatch.Frequency;
            if(!NetStats.IsSlowUpdate(total))return;
            Debug.Log("[TLD MP net] slow bridge update ms="+NetStats.Ms(e-a)+" farPlace="+NetStats.Ms(b-a)+" pulse="+NetStats.Ms(c-b)+
                " (drain="+NetStats.Ms(pulseDrainTicks)+" nodeTick="+NetStats.Ms(pulseTickTicks)+") spawnSafety="+NetStats.Ms(d-c)+" wildlife="+NetStats.Ms(e-d));
        }
        internal static void Pulse()
        {
            pulseDrainTicks=pulseTickTicks=0;
            Ensure();CargoDiagnostics.Tick();TickCarBlood();GroundDiagnostics.TickBusPlacement();if(DiagnosticMode)GroundDiagnostics.Tick();if(node==null)return;ReplayPendingItems();TickSpawnHandoff();
            long drainStart=Stopwatch.GetTimestamp();
            TickVehicleImpactBursts();vehiclePairs.Flush(Now,PublishVehiclePacket);SettleLiveUsables();DrainDeliveryQueues();outbox.Flush(Now);bulkOutbox.Flush(Now);
            pulseDrainTicks=Stopwatch.GetTimestamp()-drainStart;
            if(loopbackMode && node.IsHost && WorldSceneAvailable() && syncScript.s!=null)
            {
                foreach(ulong peer in new List<ulong>(node.Peers.Keys))
                {
                    if(loopbackBootstrapSent.Contains(peer))continue;
                    syncScript.s.SendMapStuff(new CSteamID(peer));
                    loopbackBootstrapSent.Add(peer);
                    JoinDiagnostic("sent native map bootstrap to peer="+peer);
                }
            }
            if(!node.IsHost && !loopbackMode && !SteamP2PNetworkingUnderTheHoodScript.HasCorrectPassword())return;
            if(!node.IsHost && !node.HostWorldLoaded && (loopbackMode?WorldSceneAvailable():hostWorldEvidence))node.HostWorldLoaded=true;
            long tickStart=Stopwatch.GetTimestamp();node.Tick(Now);pulseTickTicks=Stopwatch.GetTimestamp()-tickStart;
            if(node.Compatible && Now>=nextSyncHealth)
            {
                nextSyncHealth=Now+5;
                Debug.Log("[TLD MP sync-health] t="+Now.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" host="+node.IsHost+" ready="+node.Ready+
                    " sendQueue{peak="+healthMaxPending+" peakBytes="+healthMaxPendingBytes+" reliable="+outbox.PendingPackets+" reliableBytes="+outbox.PendingBytes+
                    " bulk="+bulkOutbox.PendingPackets+" bulkBytes="+bulkOutbox.PendingBytes+"} "+TakeItemReplayHealth()+
                    " vehicleMotion{combinedTotal="+vehiclePairs.Combined+" repeatedTotal="+vehiclePairs.Repeated+" sameFrameSkippedTotal="+vehiclePairs.SameFrameSkipped+" fallbackTotal="+vehiclePairs.Fallback+
                    " impactTriggersTotal="+vehicleImpactTriggers+" impactSentTotal="+vehicleImpactSent+" tracked="+vehicleImpacts.Count+"}"+
                    " vehicleReceive{receivedTotal="+vehicleMotionReceived+" processedTotal="+vehicleMotionProcessed+" admissionRejectedTotal="+vehicleAdmissionRejected+" sceneRejectedTotal="+vehicleSceneRejected+" malformedTotal="+vehicleMalformed+" controlIgnoredTotal="+vehicleControlIgnored+"}"+
                    (steamTransport==null?"":" "+steamTransport.TakeHealthSummary())+
                    (!loopbackMode || Loopback.Transport==null?"":" "+Loopback.Transport.TakeQueueHealth()));
                healthMaxPending=healthMaxPendingBytes=0;
            }
            if((loopbackMode || ModDiagnostics.On) && Now>=nextJoinDiagnostic)
            {
                nextJoinDiagnostic=Now+5;
                JoinDiagnostic("state host="+node.IsHost+" compatible="+node.Compatible+" ready="+node.Ready+" phase="+node.JoinStatus+" stage="+node.JoinStage+" reason="+node.JoinStageReason+" "+MapDiagnostic());
            }
            if(node.Error!=null && lastError!=node.Error){lastError=node.Error;Debug.LogError("[TLD MP v3] "+node.Error);}
            if(loopbackMode && !node.IsHost && node.Compatible && !node.Joining && !node.Ready && WorldSceneAvailable() && Now>=loopbackNextRequest)
            {loopbackNextRequest=Now+2;node.RequestSnapshot();}
            if(Now<nextPulse)return;nextPulse=Now+.05;
            // Actual lobby departure is detected by the node's peer timeout; the
            // host also removes peers that are no longer lobby members.
            if(node.IsHost && !loopbackMode && networkingScript.s!=null)
            {
                var members=new HashSet<ulong>();for(int i=0;i<SteamMatchmaking.GetNumLobbyMembers(networkingScript.s.hood.ConnectedLobbyID);i++)members.Add((ulong)SteamMatchmaking.GetLobbyMemberByIndex(networkingScript.s.hood.ConnectedLobbyID,i));
                foreach(ulong peer in new List<ulong>(node.Peers.Keys))if(!members.Contains(peer))node.RemovePeer(peer);
            }
        }
        static void RemovePlayer(ulong peer)
        {
            outbox.Drop(peer);bulkOutbox.Drop(peer);delivery.Drop(peer);loopbackBootstrapSent.Remove(peer);poseFilter.Forget(peer);senderClock.Forget(peer);liveUsables.Drop(peer);
            vehicleClock.Forget(peer);
            if(syncScript.s==null || syncScript.s.players==null)return;
            lastPlayers.Remove(peer);
            syncScript.playerSyncClass p;if(syncScript.s.players.TryGetValue(new CSteamID(peer),out p))
            {
                // The native timeout path drops held/worn items before destroying
                // the proxy. A lobby departure must do the same or destroying the
                // proxy deletes the items and broadcasts their removal.
                if(p.player!=null){p.DropStuff();UnityEngine.Object.Destroy(p.player);}
                syncScript.s.players.Remove(new CSteamID(peer));
            }
        }
        public static IEnumerator SyncItems(syncScript self)
        {
            WaitForSeconds interval=new WaitForSeconds(self.syncItemInterwall);
            var scheduler=new SyncItemScheduler<uint,tosaveitemscript>();
            Func<float> itemBatchSize=delegate{return self.syncItemCount;};
            Action<tosaveitemscript> updateItem=delegate(tosaveitemscript item){if(item!=null)item.MultiUpd();};
            while(true)
            {
                // Match the stock coroutine: after yielding while disconnected it
                // still falls through into this iteration's item sweep.
                if(!syncScript.IsConnected())yield return interval;
                scheduler.BeginPass();
                while(scheduler.HasMore(savedatascript.s.items.Count))
                {
                    var items=savedatascript.s.items;
                    scheduler.ProcessBatch(items,itemBatchSize,updateItem);
                    yield return null;
                }
                yield return interval;
            }
        }
        public static void NativeMainBegin(){RenderSwitches.MainFrame();CargoGhost.Frame();FrameDiagnostics.RegisterMainThread();FrameDiagnostics.NativeBegin(0);}
        public static void NativeMainEnd(){FrameDiagnostics.NativeEnd(0);}
        public static void NativeMapBegin(){FrameDiagnostics.RegisterMainThread();FrameDiagnostics.NativeBegin(1);}
        public static void NativeMapEnd(){FrameDiagnostics.NativeEnd(1);}
        public static void NativeObjectsBegin(){FrameDiagnostics.NativeBegin(2);}
        public static void NativeObjectsEnd(){FrameDiagnostics.NativeEnd(2);}
        public static void NativeItemsBegin(){FrameDiagnostics.NativeBegin(3);}
        public static void NativeItemsEnd(){FrameDiagnostics.NativeEnd(3);}
        public static void NoteMirrorsUpdate(){FrameDiagnostics.NoteBehavior(0);}
        public static void NoteReflectionMove(){FrameDiagnostics.NoteBehavior(1);}
        public static void NoteMultiUpd(){FrameDiagnostics.NoteBehavior(2);}
        public static void NoteCarUpdate(){FrameDiagnostics.NoteBehavior(3);}
        public static void NoteAudioInterior(){FrameDiagnostics.NoteBehavior(4);}
        public static void NoteSensorUpdate(){FrameDiagnostics.NoteBehavior(5);}
        public static void NoteMoveCreature(){FrameDiagnostics.NoteBehavior(6);}
        public static void NoteAiUpdate(){FrameDiagnostics.NoteBehavior(7);}
        public static void NoteVultureUpdate(){FrameDiagnostics.NoteBehavior(8);}
        public static void NoteFlyerUpdate(){FrameDiagnostics.NoteBehavior(9);}
        public static void NativeBehaviorMirrorsBegin(){RenderSwitches.MirrorsBegin(mainscript.s);FrameDiagnostics.NativeBehaviorMirrorsBegin();}
        public static void NativeBehaviorMirrorsEnd(){RenderSwitches.MirrorsEnd();FrameDiagnostics.NativeBehaviorMirrorsEnd();}
        public static void NativeBehaviorReflectionBegin(){FrameDiagnostics.NativeBehaviorReflectionBegin();}
        public static void NativeBehaviorReflectionEnd(){FrameDiagnostics.NativeBehaviorReflectionEnd();}
        public static void NativeBehaviorMultiBegin(){FrameDiagnostics.NativeBehaviorMultiBegin();}
        public static void NativeBehaviorMultiEnd(){FrameDiagnostics.NativeBehaviorMultiEnd();}
        public static void NativeBehaviorCarBegin(){FrameDiagnostics.NativeBehaviorCarBegin();}
        public static void NativeBehaviorCarEnd(){FrameDiagnostics.NativeBehaviorCarEnd();}
        public static void NativeBehaviorAudioBegin(){FrameDiagnostics.NativeBehaviorAudioBegin();}
        public static void NativeBehaviorAudioEnd(){FrameDiagnostics.NativeBehaviorAudioEnd();}
        public static void NativeBehaviorSensorBegin(){FrameDiagnostics.NativeBehaviorSensorBegin();}
        public static void NativeBehaviorSensorEnd(){FrameDiagnostics.NativeBehaviorSensorEnd();}
        public static void NativeBehaviorCreatureBegin(){FrameDiagnostics.NativeBehaviorCreatureBegin();}
        public static void NativeBehaviorCreatureEnd(){FrameDiagnostics.NativeBehaviorCreatureEnd();}
        public static void NativeBehaviorAiBegin(){FrameDiagnostics.NativeBehaviorAiBegin();}
        public static void NativeBehaviorAiEnd(){FrameDiagnostics.NativeBehaviorAiEnd();}
        public static void NativeBehaviorVultureBegin(){FrameDiagnostics.NativeBehaviorVultureBegin();}
        public static void NativeBehaviorVultureEnd(){FrameDiagnostics.NativeBehaviorVultureEnd();}
        public static void NativeBehaviorFlyerBegin(){FrameDiagnostics.NativeBehaviorFlyerBegin();}
        public static void NativeBehaviorFlyerEnd(){FrameDiagnostics.NativeBehaviorFlyerEnd();}
        public static void NativeBehaviorAttachablesBegin(){FrameDiagnostics.NativeBehaviorAttachablesBegin();}
        public static void NativeBehaviorAttachablesEnd(){FrameDiagnostics.NativeBehaviorAttachablesEnd();}
        public static void NativeBehaviorPartConditionsBegin(){FrameDiagnostics.NativeBehaviorPartConditionsBegin();}
        public static void NativeBehaviorPartConditionsEnd(){FrameDiagnostics.NativeBehaviorPartConditionsEnd();}
        public static void NativeBehaviorUsablesBegin(){FrameDiagnostics.NativeBehaviorUsablesBegin();}
        public static void NativeBehaviorUsablesEnd(){FrameDiagnostics.NativeBehaviorUsablesEnd();}
        public static void NativeBehaviorWheelGraphicsBegin(){FrameDiagnostics.NativeBehaviorWheelGraphicsBegin();}
        public static void NativeBehaviorWheelGraphicsEnd(){FrameDiagnostics.NativeBehaviorWheelGraphicsEnd();}
        public static void NativeBehaviorSyncBegin(){PrunePlayers();FrameDiagnostics.NativeBehaviorSyncBegin();}
        public static void NativeBehaviorSyncEnd(){FrameDiagnostics.NativeBehaviorSyncEnd();PrunePlayers();}
        static Vector3d recenterOrigin;
        public static void NativeVisszaRakasBegin(){FrameDiagnostics.NativeVisszaRakasBegin();if(mainscript.s!=null)recenterOrigin=mainscript.s.visszarakva;}
        public static void NativeVisszaRakasEnd()
        {
            FrameDiagnostics.NativeVisszaRakasEnd();
            var main=mainscript.s;
            if(main==null || main.visszarakva==recenterOrigin)return;
            var map=menuhandler.s==null?null:menuhandler.s.currentMainMap;
            if(map==null || map.genArounds==null)return;
            // The native streaming loop refreshes these cached Unity positions
            // on its next Update. Other coroutines can run first after a shift.
            foreach(var around in map.genArounds)
                if(around!=null && around.t!=null)around.upos=around.t.position;
        }
        public static void NativeWheelGraphicsBegin(){FrameDiagnostics.NativeWheelGraphicsBegin();}
        public static void NativeWheelGraphicsEnd(){FrameDiagnostics.NativeWheelGraphicsEnd();}
        public static void NativeWheelSizeBegin(){FrameDiagnostics.NativeWheelSizeBegin();}
        public static void NativeWheelSizeEnd(){FrameDiagnostics.NativeWheelSizeEnd();}
        // v4 (packaging/singleplayer) calls this after every committed native save.
        // It must exist or Mono rejects the whole patched save method. The breakable
        // sidecar it once wrote is not part of this build, so it does nothing.
        public static void AfterNativeSaveCommitted(string normalizedPath,object savedObject){}
        public static void NoteFlyerKinematicSkipped(){FrameDiagnostics.NoteBehavior(10);}
        public static void NativeObjGenBegin(){FrameDiagnostics.NativeObjGenBegin();}
        public static void NativeObjGenEnd(){FrameDiagnostics.NativeObjGenEnd();}
        public static void NativeObjPlaceBegin(){FrameDiagnostics.NativeObjPlaceBegin();}
        public static void NativeObjPlaceEnd(){FrameDiagnostics.NativeObjPlaceEnd();}
        public static void NativeCategoryPlaceBegin(objGenScript.categoryClass category){PlacementSkip.Begin(category);FrameDiagnostics.NativeCategoryPlaceBegin(category);}
        public static void NativeCategoryPlaceEnd(){FrameDiagnostics.NativeCategoryPlaceEnd();try{PlacementSkip.End();}catch(Exception){PlacementSkip.Clear();}}
        public static void NativePoiBegin(){FrameDiagnostics.NativePoiBegin();}
        public static void NativePoiEnd(){FrameDiagnostics.NativePoiEnd();}
        public static void NativeRoadNeededBegin(){FrameDiagnostics.NativeRoadNeededBegin();}
        public static void NativeRoadNeededEnd(){FrameDiagnostics.NativeRoadNeededEnd();}
        public static void NativeRoadPlaceBegin(){FrameDiagnostics.NativeRoadPlaceBegin();}
        public static void NativeRoadPlaceEnd(){FrameDiagnostics.NativeRoadPlaceEnd();}
        public static void NativeTerrainBegin(terrainGenScript terrain){FrameDiagnostics.NativeTerrainBegin(terrain);}
        public static void NativeTerrainEnd(terrainGenScript terrain){FrameDiagnostics.NativeTerrainEnd(terrain);}
        public static void NativeTerrainPlaceBegin(){FrameDiagnostics.NativeTerrainPlaceBegin();}
        public static void NativeTerrainPlaceEnd(){FrameDiagnostics.NativeTerrainPlaceEnd();}
        public static void NativeTerrainCheckBegin(){FrameDiagnostics.NativeTerrainCheckBegin();}
        public static void NativeTerrainCheckEnd(){FrameDiagnostics.NativeTerrainCheckEnd();}
        public static void NativeThumbleweedBegin(){FrameDiagnostics.NativeThumbleweedBegin();}
        public static void NativeThumbleweedEnd(){FrameDiagnostics.NativeThumbleweedEnd();}
        static readonly Dictionary<uint,bool> pendingCarBlood=new Dictionary<uint,bool>();
        static bool applyingCarBlood;static float nextCarBloodRetry;
        public static void CarBloodDirty(washtriggerscript wash){CarBloodChanged(wash,true);}
        public static void CarBloodClean(washtriggerscript wash){CarBloodChanged(wash,false);}
        static void CarBloodChanged(washtriggerscript wash,bool dirty)
        {
            if(applyingCarBlood || !Connected || node==null || !node.Ready || wash==null)return;
            var car=wash.GetComponentInParent<carscript>();
            if(car==null || car.blood!=wash || car.tosave==null || car.tosave.idInSave==0)return;
            node.PublishCarBlood(car.tosave.idInSave,dirty);
        }
        static bool TryApplyCarBlood(uint id,bool dirty)
        {
            tosaveitemscript item;
            if(!Find(id,out item) || item==null || item.car==null || item.car.blood==null)return false;
            var blood=item.car.blood;
            if(blood.ON==dirty)return true;
            applyingCarBlood=true;
            try{if(dirty)blood.GotDirty();else blood.GotClean();}
            finally{applyingCarBlood=false;}
            return true;
        }
        static void ApplyCarBlood(uint id,bool dirty)
        {
            if(TryApplyCarBlood(id,dirty)){pendingCarBlood.Remove(id);return;}
            if(pendingCarBlood.Count<1024 || pendingCarBlood.ContainsKey(id))pendingCarBlood[id]=dirty;
        }
        static void TickCarBlood()
        {
            if(pendingCarBlood.Count==0 || Time.unscaledTime<nextCarBloodRetry)return;
            nextCarBloodRetry=Time.unscaledTime+.5f;
            foreach(var pair in new List<KeyValuePair<uint,bool>>(pendingCarBlood))
                if(TryApplyCarBlood(pair.Key,pair.Value))pendingCarBlood.Remove(pair.Key);
        }
        public static void NativePoiNativeAddBegin(){FrameDiagnostics.NativePoiNativeAddBegin();}
        public static void NativePoiNativeAddEnd(){FrameDiagnostics.NativePoiNativeAddEnd();}
        public static void NativePoiInstantiateBegin(){FrameDiagnostics.NativePoiInstantiateBegin();}
        public static void NativePoiInstantiateEnd(){FrameDiagnostics.NativePoiInstantiateEnd();}
        // Pickup and dismount use different controls. Resolve only an actual
        // tyre-slot relationship; never walk arbitrary parents or detach a rim.
        static pickupable LooseWheelAssembly(pickupable target)
        {
            if(target==null)return null;
            pickupable rim=target;
            if(target.gumi!=null && target.tosave!=null && target.tosave.attachable!=null)
            {
                var tyre=target.tosave.attachable;var slot=tyre.slot;
                if(!tyre.attached || slot==null || !slot.hasPart(tyre))return null;
                rim=slot.tosaveitem==null?slot.pick:slot.tosaveitem.P;
            }
            if(rim==null || rim.wheel==null || rim.tosave==null || rim.pickedUp || rim.physlock!=null)return null;
            var fitted=rim.tosave.attachable;
            if(fitted!=null && (fitted.attached || fitted.slot!=null))return null;
            var tyreSlot=rim.wheel.gumi;
            if(tyreSlot==null || !tyreSlot.hasPart())return null;
            foreach(var part in tyreSlot.parts)
            {
                if(part==null || !part.attached || part.slot!=tyreSlot || part.tosave==null || part.tosave.P==null || part.tosave.P.gumi==null)continue;
                if(target==rim || part.tosave.P==target)return rim;
            }
            return null;
        }
        static pickupable VisibleWheelPart(pickupable rim,pickupable fallback,fpscontroller player)
        {
            if(player.Cam==null || rim.wheel.felniRadius<=0)return fallback;
            // Wheel and tyre meshes can be unreadable and their physics capsules
            // cross the hub. Project the existing pickup ray onto the rim plane
            // in the wheel's local coordinates, where the axle is local X.
            var origin=rim.transform.InverseTransformPoint(player.Cam.transform.position);
            var direction=rim.transform.InverseTransformVector(player.Cam.transform.forward);
            if(Math.Abs(direction.x)<1e-5f)return fallback;
            float distance=-origin.x/direction.x;
            if(distance<0 || distance>player.FrayRange)return fallback;
            float y=origin.y+direction.y*distance,z=origin.z+direction.z*distance;
            float radius=rim.wheel.felniRadius;
            if(y*y+z*z>radius*radius)return fallback;
            return rim;
        }
        public static pickupable ResolveWheelPickup(Collider collider,fpscontroller player)
        {
            var target=collider==null?null:collider.GetComponentInParent<pickupable>();
            if(player==null || player.input==null || player.input.mountDown || player.input.mountUp)return target;
            var rim=LooseWheelAssembly(target);
            return rim==null?target:VisibleWheelPart(rim,target,player);
        }
        public static bool WheelPickupMountBlocked(fpscontroller player,pickupable target)
        {
            // Publish both actions after the native tyre interaction has run.
            // Leaving BcanMount set hides GCCanPickup, even when pickup passes.
            // Pickup and mount/dismount may share one key, so an active pickup
            // control wins over a mount press on the same frame.
            if(player.input!=null && ((!player.input.mountDown && !player.input.mountUp) || player.input.pickup) &&
                player.pickedUp==null && !player.Bpickedup && !player.BAttach && !player.canusetube && !player.nowMounted &&
                target!=null && target.wheel!=null && LooseWheelAssembly(target)!=null && target.tosave.attachable!=null && target.CanPickup() &&
                !target.cantPickupWhileMount && player.CanPickupSit(target))
            {
                player.BcanMount=false;
                player.BcanDetach=false;
            }
            return player.BcanMount;
        }
        // Pickup and dismount can share one control. When the loose rim behind a
        // fitted tyre is what the player is aiming at, the pickup action owns the
        // press; the native partslot UnCraft must not also run and pull the tyre
        // off. Every other slot keeps its native dismount.
        public static bool WheelSlotUnCraft(partslotscript slot,fpscontroller player)
        {
            try
            {
                if(slot!=null && player!=null && player.input!=null && player.input.pickup)
                {
                    var part=slot.part();
                    var target=part==null||part.tosave==null?null:part.tosave.P;
                    var rim=LooseWheelAssembly(target);
                    if(rim!=null && VisibleWheelPart(rim,null,player)==rim)return false;
                }
            }
            catch(Exception){}
            return slot==null?false:slot.UnCraft();
        }
        public static void BusGroundPlaced(Vector3d pos,Vector3 rot,int id,bool aligned){if(DiagnosticMode)GroundDiagnostics.Placed(pos,rot,id,aligned);}
        public static void BusGroundSpawned(poiScript script,int genId,Vector3d posID){GroundDiagnostics.QueueBusPlacement(script);if(DiagnosticMode)GroundDiagnostics.Spawned(script,genId,posID);}
        // A house's props must never be lost to a failing roadside search, but
        // they must also not be left behind on the road. They wait for the bus
        // stop to reach its roadside spot, or for the deadline.
        public static void SpawnStuffAfterBusPlacement(poiScript script,poiGenScript.poiClass poi)
        {
            if(GroundDiagnostics.TryBusPlacement(script) || GroundDiagnostics.HouseSpawnReady(script))script.SpawnStuff(poi);
        }
        public static void NativePoiSpawnBegin(){FrameDiagnostics.NativePoiSpawnBegin();}
        public static void NativePoiSpawnEnd(){FrameDiagnostics.NativePoiSpawnEnd();}
        public static void NativePoiStuffBegin(){FrameDiagnostics.NativePoiStuffBegin();}
        public static void NativePoiStuffEnd(){FrameDiagnostics.NativePoiStuffEnd();}
        public static void NativePoiItemsBegin(){FrameDiagnostics.NativePoiItemsBegin();}
        public static void NativePoiItemsEnd(){FrameDiagnostics.NativePoiItemsEnd();}
        public static void NativePoiFixedBegin(){FrameDiagnostics.NativePoiFixedBegin();}
        public static void NativePoiFixedEnd(){FrameDiagnostics.NativePoiFixedEnd();}
        public static void NativePoiRemoveBegin(){FrameDiagnostics.NativePoiRemoveBegin();}
        public static void NativePoiRemoveEnd(){FrameDiagnostics.NativePoiRemoveEnd();}
        public static void NativeTerrainMeshBegin(terrainGenScript terrain){FrameDiagnostics.NativeTerrainMeshBegin(terrain);}
        public static void NativeTerrainMeshEnd(terrainGenScript terrain){FrameDiagnostics.NativeTerrainMeshEnd(terrain);}
        public static void NativeTerrainPlaceMeshBegin(){FrameDiagnostics.NativeTerrainPlaceMeshBegin();}
        public static void NativeTerrainPlaceMeshEnd(){FrameDiagnostics.NativeTerrainPlaceMeshEnd();}
        public static void NativeTerrainTileStartBegin(){FrameDiagnostics.NativeTerrainTileStartBegin();}
        public static void NativeTerrainTileStartEnd(){FrameDiagnostics.NativeTerrainTileStartEnd();}
        public static void NativeTerrainMeshUploadBegin(){FrameDiagnostics.NativeTerrainMeshUploadBegin();}
        public static void NativeTerrainMeshUploadEnd(){FrameDiagnostics.NativeTerrainMeshUploadEnd();}
        public static void NativeRoadInstantiateBegin(){FrameDiagnostics.NativeRoadInstantiateBegin();}
        public static void NativeRoadInstantiateEnd(){FrameDiagnostics.NativeRoadInstantiateEnd();}
        public static void NativeRoadInitBegin(){FrameDiagnostics.NativeRoadInitBegin();}
        public static void NativeRoadInitEnd(){FrameDiagnostics.NativeRoadInitEnd();}
        public static void NativeRoadGeometryBegin(){FrameDiagnostics.NativeRoadGeometryBegin();}
        public static void NativeRoadGeometryEnd(){FrameDiagnostics.NativeRoadGeometryEnd();}
        public static void NativeRoadPoleBegin(){FrameDiagnostics.NativeRoadPoleBegin();}
        public static void NativeRoadPoleEnd(){FrameDiagnostics.NativeRoadPoleEnd();}
        public static void NativeRoadDestroyBegin(){FrameDiagnostics.NativeRoadDestroyBegin();}
        public static void NativeRoadDestroyEnd(){FrameDiagnostics.NativeRoadDestroyEnd();}
        [DefaultExecutionOrder(10000)]public sealed class Driver:MonoBehaviour
        {
            void Awake(){FrameDiagnostics.RegisterMainThread();}
            void Update()
            {
                long a=Stopwatch.GetTimestamp();FrameDiagnostics.BeginFrame();FarPlaceQueue.Drain();
                long b=Stopwatch.GetTimestamp();FrameDiagnostics.PulseBegin();Pulse();
                long c=Stopwatch.GetTimestamp();TickNativeSpawnSafety(false);FrameDiagnostics.PulseEnd();
                try{GroundDiagnostics.TickLoadingOverlay(!Connected || (node!=null && (node.IsHost || (node.Ready && spawnFinished))));}catch(Exception e){GroundDiagnostics.LoadingOverlayFailed(e);}
                long d=Stopwatch.GetTimestamp();WildlifeWeather.Tick(Connected);TickPlayerRate();
                NoteSlowUpdate(a,b,c,d,Stopwatch.GetTimestamp());
            }
            void FixedUpdate(){TickJoinPhysics();TickNativeSpawnSafety(true);TickVehicleImpactSamples();TickVehicleImpactBursts();}
            void LateUpdate(){TickNativeSeatSafety();TickJoinPhysics();try{RemoteCarSmoothing.CheckDrift();CarCargo.HoldTick(RemoteCarSmoothing.IsSmoothed);WorldJump.Tick(Connected);PartsCensus.Tick(Connected);}catch(Exception e){if(!worldJumpFailed){worldJumpFailed=true;Debug.LogWarning("[TLD MP world] diagnostics disabled after error: "+e.GetType().Name);}}}
            void OnDestroy(){FrameDiagnostics.Shutdown();if(loopbackMode)Loopback.Stop();Reset();}
        }
        static readonly HashSet<uint> announcedSpawns=new HashSet<uint>();
        static readonly Dictionary<ulong,ulong> appliedSpawns=new Dictionary<ulong,ulong>();
        static List<uint> spawnIDs;
        static byte[] spawnPayload;
        static ulong spawnSequence;
        static double nextSpawnScan,nextSpawnSend;
        static int spawnAttempts;
        static void ClearSpawnHandoff()
        {announcedSpawns.Clear();appliedSpawns.Clear();spawnIDs=null;spawnPayload=null;spawnSequence=0;nextSpawnScan=nextSpawnSend=0;spawnAttempts=0;}
        static void AckSpawn(ulong sequence)
        {
            if(spawnPayload==null || sequence!=spawnSequence)return;
            foreach(uint id in spawnIDs)announcedSpawns.Add(id);
            Debug.Log("[TLD MP spawn] acknowledged transaction="+sequence+" items="+spawnIDs.Count);
            spawnPayload=null;spawnIDs=null;
        }
        static void TickSpawnHandoff()
        {
            if(node==null || node.IsHost || !node.Ready || !WorldSceneAvailable() || savedatascript.s==null)return;
            if(spawnPayload!=null)
            {
                if(Now<nextSpawnSend)return;
                if(spawnAttempts>=5){Debug.LogError("[TLD MP spawn] host did not acknowledge transaction="+spawnSequence+"; reconnect before spawning more items.");nextSpawnSend=Double.MaxValue;return;}
                spawnAttempts++;nextSpawnSend=Now+2;node.SendSpawn(spawnSequence,spawnPayload);return;
            }
            if(Now<nextSpawnScan)return;nextSpawnScan=Now+.25;
            var items=new List<tosaveitemscript>();
            foreach(var pair in savedatascript.s.items)
                if(pair.Key>=node.FirstID && pair.Key<=node.LastID && !announcedSpawns.Contains(pair.Key) && pair.Value!=null && pair.Value.gameObject.activeInHierarchy)items.Add(pair.Value);
            if(items.Count==0)return;
            if(items.Count>128){Debug.LogError("[TLD MP spawn] more than 128 unannounced items; handoff refused.");nextSpawnScan=Double.MaxValue;return;}
            // Capture definitions first. Ordinary SyncAll suppresses these for
            // loose guest parts, so attachments alone cannot create the objects.
            var packets=new List<byte[]>();var ids=new List<uint>();
            bool previous=capturing;var prior=captured;capturing=true;captured=packets;
            try
            {
                foreach(var item in items){ids.Add(item.idInSave);syncScript.s.SendItemPosGlobal(item.idInSave,item.id,mainscript.GlobalFromUnityPos(item.useThisForPos.position),item.useThisForPos.eulerAngles);}
                foreach(var item in items)item.SyncAll();
            }
            finally{capturing=previous;captured=prior;}
            using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream))
            {
                // Keep only definitions and persistent state for this transaction.
                var allowed=new HashSet<uint>(ids);var selected=new List<byte[]>();
                foreach(var packet in packets)if(packet.Length>0 && (packet[0]==16 || PendingItemQueue.Holdable(packet[0])) && allowed.Contains(StablePackets.Item(packet)))selected.Add(packet);
                if(selected.Count>1024)throw new InvalidOperationException("Spawn handoff packet limit exceeded.");
                writer.Write(ids.Count);writer.Write(selected.Count);
                foreach(var packet in selected){writer.Write(packet.Length);writer.Write(packet);}
                if(stream.Length>58000){Debug.LogError("[TLD MP spawn] handoff exceeds 58000 bytes; refused.");nextSpawnScan=Double.MaxValue;return;}
                spawnPayload=stream.ToArray();spawnIDs=ids;spawnSequence++;spawnAttempts=0;nextSpawnSend=Now;
            }
            Debug.Log("[TLD MP spawn] captured transaction="+spawnSequence+" items="+ids.Count+" bytes="+spawnPayload.Length);
        }
        static bool ApplySpawn(ulong source,ulong sequence,byte[] bytes)
        {
            ulong prior;if(appliedSpawns.TryGetValue(source,out prior) && sequence<=prior)return true;
            StablePeer peer;if(node==null || !node.IsHost || !node.Peers.TryGetValue(source,out peer) || bytes==null || bytes.Length>58000)return false;
            var packets=new List<byte[]>();var definitions=new HashSet<uint>();
            try
            {
                using(var stream=new System.IO.MemoryStream(bytes,false))using(var reader=new System.IO.BinaryReader(stream))
                {
                    int count=reader.ReadInt32(),length=reader.ReadInt32();if(count<1 || count>128 || length<count || length>1024)return false;
                    for(int i=0;i<length;i++)
                    {
                        int size=reader.ReadInt32();if(size<5 || size>stream.Length-stream.Position)return false;
                        var packet=reader.ReadBytes(size);if(!StablePackets.Valid(packet) || (packet[0]!=16 && !PendingItemQueue.Holdable(packet[0])))return false;
                        uint id=StablePackets.Item(packet);if(id<peer.First || id>peer.Last)return false;
                        if(i<count){if(packet[0]!=16 || !definitions.Add(id))return false;}else if(!definitions.Contains(id))return false;
                        uint parent=PendingItemQueue.Parent(packet);if(parent!=0 && !definitions.Contains(parent) && !HeldItemExists(parent))return false;
                        if(!ValidateData(packet))return false;packets.Add(packet);
                    }
                    if(stream.Position!=stream.Length)return false;
                }
                // All input is checked before native creation. Existing IDs are
                // updated by native UpdItem, never instantiated a second time.
                foreach(var packet in packets)
                {
                    if(packet[0]==16)Deliver(source,packet);
                    else
                    {
                        bool previous=dispatching;dispatching=true;
                        try{ReplayOneHeld(new PendingItemQueue.Held {Bytes=packet,Sender=source});}
                        finally{dispatching=previous;}
                    }
                }
                foreach(uint id in definitions)if(!HeldItemExists(id))return false;
                foreach(var packet in packets)if(packet[0]==42)
                {
                    tosaveitemscript item;if(!Find(StablePackets.Item(packet),out item) || item.attachable==null)return false;
                    var live=new save_attachable(item.attachable,item.idInSave);
                    if(BitConverter.ToInt32(packet,5)==0){if(item.attachable.attached)return false;continue;}
                    if(BitConverter.ToInt32(packet,9)!=2)return false;
                    if(!AttachmentReplayPolicy.SameSlot(packet,live.attachType,live.indexType,live.parentid,live.poigenid,live.poiid.x,live.poiid.y,live.poiid.z,live.index))return false;
                }
                appliedSpawns[source]=sequence;
                foreach(var packet in packets)node.PublishNative(packet,true);
                Debug.Log("[TLD MP spawn] applied source="+source+" transaction="+sequence+" items="+definitions.Count);
                return true;
            }
            catch(Exception e){Debug.LogWarning("[TLD MP spawn] apply failed source="+source+" transaction="+sequence+" error="+e);return false;}
        }
    }
}

