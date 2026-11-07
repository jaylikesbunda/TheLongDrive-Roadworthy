using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GeneszSteamP2PBase;
using Steamworks;
using TldMp.Experimental;
using UnityEngine;
using Debug=TldMp.RoadworthyLog;

namespace TldMp
{
    internal static class RoadworthyLog
    {
        internal static void Log(object message){DiagnosticFileLog.Log(message);}
        internal static void LogWarning(object message){UnityEngine.Debug.LogWarning(message);}
        internal static void LogError(object message){UnityEngine.Debug.LogError(message);}
    }
    // Native-restore bridge. The native synchronization, movement, collision and
    // interaction paths are the only active gameplay path; this class contains
    // transport adapters, the world manifest/join handshake, structural packet
    // validation, object-ID collision prevention and diagnostics only.
    public static partial class StableGameBridge
    {
        internal static StableNode Node{get{return node;}}
        // True while a Roadworthy session object exists, whichever backend is active.
        internal static bool InSession{get{return node!=null;}}
        internal static bool ExperimentalSession{get{return experimentalActive;}}
        internal static bool WorldScene(){return WorldSceneAvailable();}
        static StableNode node;static Driver driver;static string fingerprint,lastError;static ContentFingerprint.Job fingerprintJob;static double fingerprintRetryAt;
        static bool experimentalActive;static double nextExperimentalResync;static long experimentalMotionSuppressed,experimentalMotionRejected;
        // Mode-gated experimental join (see ExperimentalJoin). Off by default: without
        // TLD_MP_EXPERIMENTAL_JOIN=1 the native Begin/Part/End transfer is untouched,
        // and the gate additionally requires the experimental backend and loopback.
        static bool experimentalJoinEnabled;
        static readonly ExperimentalJoinReceiver experimentalJoin=new ExperimentalJoinReceiver();
        // Host-only live vehicle authority. Guests keep the flat grant table.
        static VehicleAuthority vehicleAuthority;
        // Phase 4 item-authority gate (docs/MULTIPLAYER_ITEM_AUTHORITY_INVENTORY.md).
        // Off by default: without TLD_MP_EXPERIMENTAL_ITEMS=1 every native item
        // message takes exactly the native path. Fixed for the life of a session,
        // host only; the lookup is the host's car-group member table.
        static bool itemGateEnabled;static long itemGateRejected;static double nextItemGateLog;static ItemMemberLookup itemGateLookup;
        static bool playerGateEnabled,breakableGateEnabled;static long playerGateRejected,breakableGateRejected;static double nextAuthorityGateLog;static BreakableAuthority breakableAuthority;static PlayerReplication playerReplication;
        static bool experimentalItemRelayEnabled;static readonly Dictionary<uint,ulong> itemRelayEventIds=new Dictionary<uint,ulong>();static ExperimentalItemRelay.Receiver itemRelayReceiver;
        static readonly Dictionary<uint,ulong> vehicleGroupOwner=new Dictionary<uint,ulong>();
        static readonly Queue<uint> vehicleGroupQueue=new Queue<uint>();
        static readonly HashSet<uint> vehicleGroupQueued=new HashSet<uint>();
        static bool dispatching,capturing,captureBounded,captureOverflow;static long captureByteCount;static List<byte[]> captured;
        static readonly Dictionary<ulong,byte[]> lastPlayers=new Dictionary<ulong,byte[]>();
        static readonly Dictionary<uint,MessageState> lastItems=new Dictionary<uint,MessageState>();
        static readonly ReliableOutbox outbox=new ReliableOutbox(TryEnvelope,TransportFailed);
        // Map/snapshot chunks ride their own bounded reliable outbox and the
        // Bulk lane so a multi-megabyte transfer cannot stall transport traffic.
        static readonly ReliableOutbox bulkOutbox=new ReliableOutbox(TryBulkEnvelope,TransportFailed,8*1024*1024,16*1024*1024,8192,8,256*1024,2);
        static readonly NetDeliveryScheduler delivery=new NetDeliveryScheduler();
        static ISteamTransport steamTransport;static ulong transportLobby,transportHost;static double nextSdrRetry;static int deliveryPeerCursor;const int TransportVirtualPort=0;
        static bool hostWorldEvidence,restartSteamJoin,steamJoinRetried;
        // A guest keeps retrying a failed initial Steam connection for this long from the
        // first attempt (host listener not up yet, lobby list lag, relay route setup).
        const double SteamJoinWindowSeconds=60.0,SteamJoinRetrySeconds=3.0;
        static double steamJoinStarted;static int steamJoinAttempts;
        static double nextPulse,nextFailure;static int packetErrors;
        static readonly Dictionary<string,double> diagnosticNext=new Dictionary<string,double>();
        static double nextJoinDiagnostic;
        static double nextSyncHealth;
        static int healthMaxPending,healthMaxPendingBytes;
        static bool loopbackMode;static double loopbackNextRequest;
        internal static bool RabbitLoopbackTesting{get{return loopbackMode;}}
        static bool loopbackBootstrapReceived;static mapSettingScript loopbackPreviousMap;

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
        // Per-item families (cargo-suppress, native-mounted-pose) fire once per item per
        // tick. They are windowed into one summary line instead of one line per id.
        // Logging only: nothing here reads or writes gameplay state.
        static readonly DiagnosticFamilyWindow diagFamilies=new DiagnosticFamilyWindow(5);
        static void EmitDiagAggregate(string summary){Debug.Log("[TLD MP diag] peer="+Own+" t="+Now.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" "+summary);}
        static void DiagAggregateFlush(){if(DiagnosticMode)diagFamilies.Tick(Now,EmitDiagAggregate);}
        static void DiagLimited(string key,string text,double every)
        {
            if(!DiagnosticMode)return;
            if(DiagnosticFamilyWindow.Aggregated(key)){diagFamilies.Note(key,text,Now,EmitDiagAggregate);return;}
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
        // Save/test-data isolation. The patched pathscript.path() calls this. When
        // TLD_MP_SAVE_ROOT is set, every derived save/settings/map/log path moves
        // under it; when unset the native behavior is byte-for-byte unchanged.
        public static string RedirectSaveRoot()
        {
            string root=Environment.GetEnvironmentVariable("TLD_MP_SAVE_ROOT");
            if(!String.IsNullOrEmpty(root))return root;
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/TheLongDrive";
        }
        // Backend is a per-session choice. The environment variable is the
        // development override; otherwise the Misc toggle persisted in the
        // Roadworthy options file decides.
        static bool ExperimentalSelected()
        {
            if(Environment.GetEnvironmentVariable("TLD_MP_EXPERIMENTAL")=="1")return true;
            PatchOptions.Load();
            return PatchOptions.ExperimentalMultiplayer;
        }
        static ulong Own {get{return loopbackMode?Loopback.Own:(ulong)SteamP2PNetworkingUnderTheHoodScript.OwnID();}}
        static bool Find(uint id,out tosaveitemscript item){item=null;return savedatascript.s!=null && savedatascript.s.items!=null && savedatascript.s.items.TryGetValue(id,out item) && item!=null;}
        // Join signature: Roadworthy version + mod build + game content + transport, as separate
        // fields so a mismatch can be named (JoinCompatibility.Describe). The debris CCD mode is
        // local physics only and is deliberately not part of it.
        static string ComposeSignature(string gameContent)
        {
            string build=StableNode.Hash(System.Text.Encoding.UTF8.GetBytes(BuildIdentity.Sources));
            return JoinCompatibility.Compose(PatchInfo.Version,build,gameContent,UseSdrTransport?"sdr":"p2p");
        }
        static void EnsureFingerprintStart()
        {
            if(fingerprint!=null || fingerprintJob!=null || Now<fingerprintRetryAt)return;
            try
            {
                string managed=Path.GetDirectoryName(typeof(syncScript).Assembly.Location);
                string assets=Directory.GetParent(managed).FullName;
                string custom=pathscript.mapPath();string identity="game-content"; // game files only; the mod build, version and transport are separate signature fields (ComposeSignature)
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
            fingerprint=ComposeSignature(result);fingerprintRetryAt=0;
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
            fingerprint=ComposeSignature(result);fingerprintRetryAt=0;
            JoinDiagnostic("Fingerprint workMs="+workMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
        }
        static void Reset()
        {
            experimentalActive=false;ExperimentalBackend.Reset();vehicleAuthority=null;vehicleGroupOwner.Clear();vehicleGroupQueue.Clear();vehicleGroupQueued.Clear();nextExperimentalResync=0;experimentalMotionSuppressed=experimentalMotionRejected=0;
            itemGateEnabled=false;itemGateRejected=0;nextItemGateLog=0;itemGateLookup=null;
            playerGateEnabled=breakableGateEnabled=false;playerGateRejected=breakableGateRejected=0;nextAuthorityGateLog=0;breakableAuthority=null;playerReplication=null;
            experimentalItemRelayEnabled=false;itemRelayEventIds.Clear();itemRelayReceiver=null;
            experimentalJoinEnabled=false;experimentalJoin.Cancel();
            RabbitFlowDiagnostics.Reset();rabbitRuntimeLogged=false;
            RabbitMetadataSync.Reset();
            MovingVehiclePassenger.Reset();
            ResetBusPair();RabbitContact.Reset();
            RabbitMotionReset();
            RabbitPlayers.Reset();
            SmokeReset();
            ResetNativeSafety();
            lastPlayers.Clear();lastItems.Clear();pendingCarBlood.Clear();diagnosticNext.Clear();loopbackBootstrapReceived=false;loopbackPreviousMap=null;
            if(steamTransport!=null){steamTransport.Dispose();steamTransport=null;}transportLobby=transportHost=0;nextSdrRetry=0;deliveryPeerCursor=0;delivery.Clear();hostWorldEvidence=restartSteamJoin=steamJoinRetried=false;
            outbox.Clear();bulkOutbox.Clear();node=null;StableMap.Configure(null);nextPulse=0;lastError=null;
            nextSyncHealth=0;healthMaxPending=healthMaxPendingBytes=0;ClearSpawnHandoff();
            selection.Clear();
            RoadLiveCache.Clear();RoadFarSkip.Clear();FarPlaceQueue.Clear();pendingItems.Clear();itemRecovery.Clear();TerrainWorker.Clear();poseFilter.Clear();senderClock.Clear();PlacementSkip.Clear();liveUsables.Clear();CarCargo.Clear();
            vehiclePairs.Clear();vehicleClock.Clear();ClearVehicleImpacts();RemoteCarSmoothing.Clear();dispatchVehicleMotion=null;applyingVehicleMotion=false;
            constructionWorldSave=null;constructionWorldMap=constructionWorldData=null;string constructionResetReason;Construction.ResetWorld(NextWorldGeneration(),out constructionResetReason);
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
            // A menu-time backend change takes effect before any peer joins. Once
            // a join is in progress or a peer is present, the session is fixed.
            if(node!=null && ExperimentalSelected()!=experimentalActive && !node.Joining &&
                node.Peers.Count==0 && (node.IsHost || !node.Ready))
                Reset();
            bool retrying=false;
            if(node!=null && node.Lobby==lobby && node.Host==host)
            {
                if(!loopbackMode)
                {
                    if(!restartSteamJoin){if(node.Error==null)EnsureSteamTransport(lobby,host);return;}
                    if(Now<nextSdrRetry)return;
                    retrying=true;
                }
                else if(node.Error==null)return;
            }
            if(!loopbackMode && node==null && Now<nextSdrRetry)return;
            // Startup can poll while hashing continues off-thread. ID allocation
            // keeps its explicit barrier in NewKey, so it never uses a partial hash.
            if(!EnsureFingerprint())return;
            Reset();
            steamJoinRetried=retrying;
            if(!retrying){steamJoinStarted=Now;steamJoinAttempts=1;}else steamJoinAttempts++;
            if(!loopbackMode && !EnsureSteamTransport(lobby,host))return;
            // The backend is fixed for the life of a session. A settings change
            // while connected applies to the next session only.
            experimentalActive=ExperimentalSelected();
            if(experimentalActive && !vehicleMotionEnabled)
            {
                experimentalActive=false;
                Debug.LogError("[TLD MP experimental] The experimental backend requires TLD_MP_VEHICLE_SNAPSHOTS=1; staying on native multiplayer.");
            }
            ulong session;do{session=BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0);}while(session==0);
            string signature=experimentalActive?fingerprint+ExperimentalBackend.SignatureTag:fingerprint;
            var created=new StableNode(Own,host,lobby,session,signature,HighestID());
            node=created;
            // The item gate is separately opt-in until non-driver interactions
            // have a tested ownership/handoff path. Other preview gates retain
            // their existing per-session switches.
            experimentalJoinEnabled=experimentalActive && loopbackMode &&
                Environment.GetEnvironmentVariable("TLD_MP_EXPERIMENTAL_JOIN")!="0";
            itemGateEnabled=ExperimentalBackend.UseItemGate(experimentalActive,
                Environment.GetEnvironmentVariable("TLD_MP_EXPERIMENTAL_ITEMS"));
            playerGateEnabled=experimentalActive &&
                Environment.GetEnvironmentVariable("TLD_MP_EXPERIMENTAL_PLAYERS")!="0";
            breakableGateEnabled=experimentalActive &&
                Environment.GetEnvironmentVariable("TLD_MP_EXPERIMENTAL_BREAKABLES")!="0";
            if(experimentalActive && node.IsHost)
            {
                // One lease/cargo group per car, bounded to the session entity cap.
                // A guest never builds this: it only applies the host's grants.
                vehicleAuthority=new VehicleAuthority(Own,4096,4096,1.5);
                vehicleAuthority.Warning=delegate(string text){Debug.LogWarning(text);};
                // One owner per breakable/prop: first writer claims, the host can
                // always override, and a removed prop is a tombstone forever.
                breakableAuthority=new BreakableAuthority(Own,8192,8192,1.5);
                // Seat/player ledger: real seat claims from the native GotUpd
                // observer, released on leave and disconnect. Observational only;
                // native sitting is not blocked yet.
                playerReplication=new PlayerReplication(Own,64,2.0);
            }
            // Host -> guest item interaction/state relay. Host actions are tagged
            // with a monotonic event id and applied once by the guest; guest
            // actions stay native and the host is the arbiter.
            experimentalItemRelayEnabled=experimentalActive &&
                Environment.GetEnvironmentVariable("TLD_MP_EXPERIMENTAL_RELAY")!="0";
            if(experimentalItemRelayEnabled)
            {
                if(node.IsHost)itemRelayEventIds.Clear();
                else itemRelayReceiver=new ExperimentalItemRelay.Receiver(node.Host,4096,4096,64);
            }
            if(itemGateEnabled && vehicleAuthority!=null)itemGateLookup=new ItemMemberLookup(vehicleAuthority.TryMemberOwner);
            BeginNativeSafetySession(session);
            node.Send=SendEnvelope;node.Deliver=Deliver;node.ApplySnapshot=TimedApplyWorld;node.SnapshotRequested=experimentalJoinEnabled?(Action<ulong>)ExperimentalSendSnapshot:TimedSendWorld;
            node.Notice=delegate(string text){Debug.LogWarning("[TLD MP v3] "+text);};
            node.Trace=JoinDiagnostic;
            node.DeferMapWork=true;
            node.WatermarkChanged=delegate(uint high){if(savedatascript.s!=null && savedatascript.s.data!=null)savedatascript.s.data.lastID=Math.Max(savedatascript.s.data.lastID,high);};
            node.PeerLeft=delegate(ulong peer){SmokePeerLeft(peer);spawnLedgers.Remove(peer);RemovePlayer(peer);};node.CarBloodReceived=ApplyCarBlood;node.AuthorityReceived=ExperimentalBackend.ApplyGrant;node.SpawnReceived=ApplySpawn;node.SpawnAcknowledged=AckSpawn;node.SpawnProgress=delegate(ulong transaction,int next){spawnAttempts=0;};
            if(experimentalActive)
                Debug.Log("[TLD MP experimental] backend=experimental host="+node.IsHost+" signature="+ExperimentalBackend.SignatureTag+
                    " item-owner-gate="+(itemGateEnabled?"on":"off (native interaction fallback)")+
                    " enabled=join,players,vehicle-ownership,breakables blocked=native-mixed-peers(auto-rejected),vehicle-handoff");
            UnityEngine.Debug.Log("[TLD MP backend] "+(experimentalActive?"experimental":"native")+" host="+node.IsHost+(loopbackMode?" loopback":" steam"));
            if(experimentalJoinEnabled)UnityEngine.Debug.Log("[TLD MP experimental] join=external-snapshot transfer=ExpSnapshotBegin/Part/End; the native Begin/Part/End transfer is not used in this session");
            if(itemGateEnabled)UnityEngine.Debug.Log("[TLD MP experimental] item-owner gate on (host filters; loose/unknown items stay native)");
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
            FixedPropConstruction.StartAssetCheck();ApplyLoopback();WorldJump.CarDescriber=RemoteCarSmoothing.Describe;
            // Hash the game content on a worker as soon as networking exists, so a join
            // finds the result ready instead of freezing the main thread while it waits.
            EnsureFingerprintStart();
            if(Connected)EnsureFingerprint();
            if(driver==null){var g=new GameObject("TLD Multiplayer Stability v3");UnityEngine.Object.DontDestroyOnLoad(g);driver=g.AddComponent<Driver>();MenuProbe.StartIfRequested(g);g.AddComponent<MenuLayout>();g.AddComponent<PatchOptionsMenu>();g.AddComponent<FeedbackHotkey>();g.AddComponent<DrunkCameraSway>();g.AddComponent<RemoteCarDriver>();if(ModDiagnostics.On)g.AddComponent<LateUpdateProbe>();SteamSelfTest.StartIfRequested(g);JoinPopupPreview.StartIfRequested(g);}
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
        // ---- paired bus stream (host OR client as actual driver) --------------
        sealed class BusPairAuthority{internal ulong Origin,Session;internal uint ID;internal tosaveitemscript Item;}
        sealed class BusPairUpstream{internal ulong Session;internal readonly Dictionary<uint,uint> Seq=new Dictionary<uint,uint>();}
        static readonly Dictionary<uint,BusPairAuthority> busPairAuthorities=new Dictionary<uint,BusPairAuthority>();
        static readonly List<uint> busPairAuthorityScratch=new List<uint>();
        static readonly Dictionary<uint,ulong> busPairGenerations=new Dictionary<uint,ulong>();
        static readonly Dictionary<ulong,BusPairUpstream> busPairUpstream=new Dictionary<ulong,BusPairUpstream>();
        static readonly SenderClock busPairClock=new SenderClock();
        static long busPairReceived,busPairAccepted,busPairRejected,busPairMalformed,busPairRelayed,busPairSent,busPairOwnerLost;
        static uint busPairSequence;static double busPairNextSend,busPairNextSummary;const double BusPairSendInterval=.05;
        static ulong PairClockKey(ulong origin,ulong epoch){return origin^(epoch*0x9E3779B97F4A7C15UL);}
        // The single issuer for authority generations; never wraps and is the
        // same path for host captures and guest upstream admissions.
        static bool GrantBusPairGeneration(uint id,out ulong generation)
        {
            ulong next;if(!busPairGenerations.TryGetValue(id,out next))next=0;
            if(next==ulong.MaxValue){generation=0;return false;}
            busPairGenerations[id]=++next;generation=next;return true;
        }
        static void ResetBusPair()
        {
            BusPairReplica.Clear();BusSeatClaims.Clear();busPairAuthorities.Clear();busPairAuthorityScratch.Clear();
            busPairGenerations.Clear();busPairUpstream.Clear();busPairClock.Clear();
            busPairReceived=busPairAccepted=busPairRejected=busPairMalformed=busPairRelayed=busPairSent=busPairOwnerLost=0;
            busPairSequence=0;busPairNextSend=busPairNextSummary=0;
        }
        internal static string TakeBusPairSummary()
        {
            string text="busPair{received="+busPairReceived+" accepted="+busPairAccepted+" rejected="+busPairRejected+" malformed="+busPairMalformed+
                " relayed="+busPairRelayed+" sent="+busPairSent+" ownerLost="+busPairOwnerLost+" authorities="+busPairAuthorities.Count+"}";
            busPairReceived=busPairAccepted=busPairRejected=busPairMalformed=busPairRelayed=busPairSent=busPairOwnerLost=0;
            return text+" "+BusPairReplica.TakeSummary();
        }
        // Host-only: the session-bound driver record is re-validated every fixed
        // step. Seat expiry, readiness loss, session change or a replaced item
        // revokes the replica immediately instead of waiting on packet TTL.
        static void RevokeBusPairPeer(ulong peer)
        {
            busPairAuthorityScratch.Clear();
            foreach(var pair in busPairAuthorities)if(pair.Value.Origin==peer)busPairAuthorityScratch.Add(pair.Key);
            foreach(var id in busPairAuthorityScratch){busPairAuthorities.Remove(id);BusPairReplica.Forget(id);busPairOwnerLost++;}
            busPairUpstream.Remove(peer);BusSeatClaims.ClearPeer(peer);
        }
        static void ValidateBusPairAuthorities()
        {
            if(busPairAuthorities.Count==0||node==null)return;
            busPairAuthorityScratch.Clear();
            foreach(var pair in busPairAuthorities)
            {
                var a=pair.Value;StablePeer peer;
                bool valid=a.Item!=null&&a.Item.idInSave==a.ID&&a.Item.gameObject.activeInHierarchy;
                if(!valid){busPairAuthorityScratch.Add(pair.Key);continue;}
                if(a.Origin==Own)
                {
                    var p=mainscript.s==null?null:mainscript.s.player;
                    if(p==null||p.seat==null||!p.seat.driverSeat0||p.seat.Car!=a.Item.car)busPairAuthorityScratch.Add(pair.Key);
                    continue;
                }
                if(!node.Peers.TryGetValue(a.Origin,out peer)||!peer.Ready||peer.Session!=a.Session||
                   !BusSeatClaims.DriverOf(a.Origin,a.Item,a.Session,Now))
                    busPairAuthorityScratch.Add(pair.Key);
            }
            foreach(var id in busPairAuthorityScratch){busPairAuthorities.Remove(id);BusPairReplica.Forget(id);busPairOwnerLost++;}
        }
        static void TickBusPair()
        {
            if(node==null||!node.Compatible){BusPairReplica.FixedTick(false,Now);return;}
            if(node.IsHost)ValidateBusPairAuthorities();
            if(DiagnosticMode&&Now>=busPairNextSummary){busPairNextSummary=Now+10;Debug.Log("[TLD MP "+TakeBusPairSummary()+" "+RabbitContact.TakeSummary()+" rabbitOverlapContacts="+RabbitPlayers.OverlapContacts+"]");}
            // Bounded discovery: only the actual local driver seat can originate
            // a stream, and only for a canonical articulated BusFront pair.
            var player=mainscript.s==null?null:mainscript.s.player;
            var seat=player==null?null:player.seat;
            var item=seat!=null&&seat.driverSeat0&&seat.Car!=null?seat.Car.tosave:null;
            if(item!=null&&item.car!=null&&item.GetComponent<BusArticulation>()!=null&&Now>=busPairNextSend)
            {
                var sample=BusPairReplica.Capture(item,Own,node.Session,++busPairSequence,ClockMs());
                if(sample!=null)
                {
                    if(node.IsHost)
                    {
                        // Host issues its own authority generation on transition.
                        BusPairAuthority auth;
                        if(!busPairAuthorities.TryGetValue(sample.ID,out auth)||auth.Origin!=Own||auth.Session!=node.Session)
                        {
                            ulong gen;if(!GrantBusPairGeneration(sample.ID,out gen))goto skip;
                            busPairAuthorities[sample.ID]=new BusPairAuthority{Origin=Own,Session=node.Session,ID=sample.ID,Item=item};
                            sample.Generation=gen;
                        }
                        else sample.Generation=busPairGenerations[sample.ID];
                        sample.RelaySession=node.Session;
                    }
                    var bytes=BusPairProtocol.Encode(sample);
                    if(bytes!=null&&node.PublishNative(bytes)){busPairSent++;busPairNextSend=Now+BusPairSendInterval;}
                }
            }
            skip:
            BusPairReplica.FixedTick(Connected,Now);
        }
        static bool ReceiveBusPair(ulong source,byte[] bytes)
        {
            if(bytes==null||bytes.Length==0||bytes[0]!=BusPairProtocol.Type)return false;
            busPairReceived++;
            if(!WorldSceneAvailable()||node==null||!node.Compatible){busPairRejected++;return true;}
            if(loopbackMode){if(source!=Loopback.Peer){busPairRejected++;return true;}}
            else if(!SteamP2PNetworkingUnderTheHoodScript.Expected(new CSteamID(source),msgType.carPosGlobal)){busPairRejected++;return true;}
            BusPairSample sample;
            if(!BusPairProtocol.Decode(bytes,out sample)){packetErrors++;busPairMalformed++;return true;}
            tosaveitemscript item;
            if(!Find(sample.ID,out item)||item==null||item.id!=sample.Prefab){busPairRejected++;return true;}
            double lateness,mapped;
            if(node.IsHost)
            {
                // Upstream: the transport source must BE the claiming driver,
                // bound to its live peer session and canonical driver seat.
                StablePeer peer;
                if(source!=sample.Origin||source==Own||!node.Peers.TryGetValue(source,out peer)||!peer.Ready||peer.Session!=sample.Epoch||
                   !BusSeatClaims.DriverOf(sample.Origin,item,sample.Epoch,Now))
                {busPairRejected++;return true;}
                // Per-origin session watermarks survive seat/ready lapses but
                // never cross a genuine peer-session boundary.
                BusPairUpstream up;
                if(!busPairUpstream.TryGetValue(source,out up))busPairUpstream[source]=up=new BusPairUpstream();
                if(up.Session!=peer.Session){up.Session=peer.Session;up.Seq.Clear();}
                uint seen;if(up.Seq.TryGetValue(sample.ID,out seen)&&!BusPairProtocol.Newer(sample.Sequence,seen)){busPairRejected++;return true;}
                // Legitimate driver transition (new origin or new session for
                // this bus) issues the next monotonic authority generation.
                BusPairAuthority auth;
                if(!busPairAuthorities.TryGetValue(sample.ID,out auth)||auth.Origin!=source||auth.Session!=peer.Session)
                {
                    ulong gen;if(!GrantBusPairGeneration(sample.ID,out gen)){busPairRejected++;return true;}
                    busPairAuthorities[sample.ID]=auth=new BusPairAuthority{Origin=source,Session=peer.Session,ID=sample.ID,Item=item};
                }
                sample.Generation=busPairGenerations[sample.ID];
                mapped=busPairClock.Map(PairClockKey(sample.Origin,sample.Epoch),sample.ClockMs,Now,out lateness);
                // Only samples admitted with ordered valid authority relay.
                if(!BusPairReplica.Accept(sample,item,mapped,Now)){busPairRejected++;return true;}
                up.Seq[sample.ID]=sample.Sequence;
                if(BusPairProtocol.StampRelay(bytes,node.Session,sample.Generation))
                {foreach(var p in node.Peers)if(p.Key!=source)node.PublishNativeTo(p.Key,bytes);busPairRelayed++;}
                busPairAccepted++;
                return true;
            }
            // Downstream: only the host may relay; the session stamp plus the
            // issued generation bind the frame to the current authority.
            if(!node.Ready||source!=node.Host||sample.Origin==Own||sample.RelaySession!=node.HostSession||sample.Generation==0){busPairRejected++;return true;}
            mapped=busPairClock.Map(PairClockKey(sample.Origin,sample.Epoch),sample.ClockMs,Now,out lateness);
            if(BusPairReplica.Accept(sample,item,mapped,Now))busPairAccepted++;else busPairRejected++;
            return true;
        }
        // Native RecCarVelocity writes front-body velocity directly; while the
        // paired replica owns the bus that write must not compete.
        public static bool BusCarVelocityAllowed(byte[] bytes)
        {
            if(!Connected||bytes==null||bytes.Length<5||bytes[0]!=20||savedatascript.s==null||savedatascript.s.items==null)return true;
            tosaveitemscript item;
            if(!savedatascript.s.items.TryGetValue(BitConverter.ToUInt32(bytes,1),out item)||item==null)return true;
            return !BusPairReplica.Owns(item);
        }
        // GotUpd entry observer: native playerPos seat claims feed canonical
        // driver binding for the pair authority (sitting/itemID/seatID args).
        public static void BusSeatClaim(syncScript.playerSyncClass player,bool sitting,bool sitAtPoi,uint itemID,int seatID)
        {
            try
            {
                if(player==null||node==null)return;
                var id=new CSteamID(player.steamID);
                syncScript.playerSyncClass canonical;
                StablePeer peer;
                ulong session=0;
                if(syncScript.s!=null&&syncScript.s.players.TryGetValue(id,out canonical)&&ReferenceEquals(canonical,player)&&
                   node.Peers.TryGetValue((ulong)id,out peer)&&peer.Ready&&peer.Session!=0)session=peer.Session;
                BusSeatClaims.Observe(player,sitting,sitAtPoi,itemID,seatID,session,Now);
                if(playerReplication!=null)
                {
                    ulong who=(ulong)player.steamID;
                    if(sitting && !sitAtPoi && itemID!=0 && seatID>=0)
                    {
                        ulong seatEpoch;
                        playerReplication.TrySeat(who,itemID,seatID,Now,node!=null && node.IsHost,out seatEpoch);
                    }
                    else playerReplication.ReleaseSeat(who,itemID,seatID);
                }
            }
            catch(Exception){}
        }
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
            uint carId=StablePackets.Item(bytes);
            tosaveitemscript item;
            if(!Find(carId,out item) || item.car==null || !item.ThisOneInChargeCar())return false;
            if(vehicleAuthority!=null)
            {
                // The host's native controller is authoritative for its own car. This
                // records the local lease; it must never suppress publishing.
                vehicleAuthority.TouchLocal(carId,(int)item.CarWhatDoing(),Now);
                QueueVehicleGroupIfStale(carId);
            }
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
            bool accepted=false,allAllowed=true;
            foreach(var sample in samples)
            {
                // Experimental backend: only the host-issued owner may write a car.
                // Non-owner samples are consumed but neither applied nor relayed.
                if(node.IsHost && !ExperimentalAllowsPeer(source,sample.ID,sample.Role))
                {
                    experimentalMotionRejected++;
                    allAllowed=false;
                    continue;
                }
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
            if(node.IsHost && accepted && allAllowed)foreach(var peer in node.Peers)if(peer.Key!=source)node.PublishNativeTo(peer.Key,bytes);
            return true;
        }
        // Single policy entry point for peer car-motion admission. Replay
        // fixtures provide a stand-in with the same signature.
        static bool ExperimentalAllowsPeer(ulong source,uint carId,int role)
        {
            // Preserve the native path exactly when experimental authority is off.
            if(vehicleAuthority==null)return true;
            tosaveitemscript local;
            if(Find(carId,out local) && local!=null && local.car!=null &&
                ExperimentalBackend.ProtectLocalCar((int)local.CarWhatDoing(),local.ThisOneInChargeCar((tosaveitemscript.carWhatDoing)role)))return false;
            if(!vehicleAuthority.AllowPeer(source,carId,role,Now))return false;
            QueueVehicleGroupIfStale(carId);
            return true;
        }
        // Host only. Ownership changes go out immediately and the whole table is
        // re-broadcast every two seconds so a late or reconnected guest converges.
        static void FlushExperimentalAuthority()
        {
            uint[] ids;ulong[] owners;ulong[] epochs;
            bool host=vehicleAuthority!=null;
            bool changed=host?vehicleAuthority.TakeChanges(out ids,out owners,out epochs):ExperimentalBackend.TakeChanges(out ids,out owners,out epochs);
            if(changed)PublishAuthority(ids,owners,epochs);
            if(Now>=nextExperimentalResync)
            {
                nextExperimentalResync=Now+2;
                bool all=host?vehicleAuthority.TakeAll(out ids,out owners,out epochs):ExperimentalBackend.TakeAll(out ids,out owners,out epochs);
                if(all)PublishAuthority(ids,owners,epochs);
            }
        }
        // Cargo/parts group for the authority. Only live, verified relationships are
        // included:
        //   fitted parts  item.attachable.attached -> attachablescript.AttachedToTosave()
        //   stored cargo  item.P.physlock (mountStuff) -> physlock.parentTosave
        // Both chains are walked up to MaxVehicleGroupChain hops until they reach
        // the car, so a part fitted to a part or cargo inside a container stored in
        // the car is still included. Excluded and counted as such: loose/world items
        // (no mount, no attachment), held or equipped items (P.pickedUp, mppickedup,
        // mpequipped, mpweared), worn items (wear), player outfits (outfit) and the
        // car itself. The save-data parent id graph (save_attachable.parentid) is
        // not used here: only live components are.
        static void QueueVehicleGroupIfStale(uint carId)
        {
            if(vehicleAuthority==null || carId==0)return;
            ulong owner,epoch;
            if(!vehicleAuthority.TryOwner(carId,out owner,out epoch))return;
            ulong built;
            if(vehicleGroupOwner.TryGetValue(carId,out built) && built==owner)return;
            if(vehicleGroupQueued.Count>=512 || !vehicleGroupQueued.Add(carId))return;
            vehicleGroupQueue.Enqueue(carId);
        }
        // One car per tick so the scan is bounded and never stalls the pulse.
        static void TickVehicleGroup()
        {
            if(vehicleAuthority==null || vehicleGroupQueue.Count==0)return;
            uint carId=vehicleGroupQueue.Dequeue();vehicleGroupQueued.Remove(carId);
            tosaveitemscript car;
            if(!Find(carId,out car) || car==null || car.car==null)return;
            uint[] members=VehicleGroupMembers(carId);
            if(!vehicleAuthority.RebuildGroup(carId,members))return;
            ulong owner,epoch;
            if(vehicleAuthority.TryOwner(carId,out owner,out epoch))vehicleGroupOwner[carId]=owner;
            if(DiagnosticMode)DiagLimited("vehicle-group:"+carId,"vehicle-group car="+carId+" members="+members.Length+" "+vehicleAuthority.Describe(carId)+" "+vehicleAuthority.Summary(),5);
        }
        const int MaxVehicleGroupScan=8192,MaxVehicleGroupChain=8;
        static uint[] VehicleGroupMembers(uint carId)
        {
            var members=new List<uint>();
            var items=savedatascript.s==null?null:savedatascript.s.items;
            if(items==null)return members.ToArray();
            int scanned=0;
            foreach(var pair in items)
            {
                if(++scanned>MaxVehicleGroupScan)break;
                tosaveitemscript item=pair.Value;
                if(item==null || item.idInSave==0 || item.idInSave==carId)continue;
                if(!VehicleGroupMember(item,carId))continue;
                if(members.Count>=VehicleAuthority.MaxGroupMembers)break;
                members.Add(item.idInSave);
            }
            members.Sort();
            return members.ToArray();
        }
        static bool VehicleGroupMember(tosaveitemscript item,uint carId)
        {
            if(item==null || item.idInSave==0)return false;
            if(item.wear!=null || item.outfit!=null)return false;
            if(item.mppickedup || item.mpequipped || item.mpweared)return false;
            if(item.P!=null && item.P.pickedUp)return false;
            var attach=item.attachable;
            if(attach!=null && attach.attached && ChainReachesCar(attach.AttachedToTosave(),carId))return true;
            if(item.P!=null && item.P.physlock!=null && ChainReachesCar(item.P.physlock.parentTosave,carId))return true;
            return false;
        }
        static bool ChainReachesCar(tosaveitemscript start,uint carId)
        {
            tosaveitemscript cursor=start;
            for(int depth=0;depth<MaxVehicleGroupChain;depth++)
            {
                if(cursor==null)return false;
                if(cursor.idInSave==carId)return true;
                // A held or worn intermediate breaks the ownership chain.
                if(cursor.P!=null && (cursor.P.pickedUp || cursor.mppickedup || cursor.mpequipped || cursor.mpweared))return false;
                tosaveitemscript next=null;
                if(cursor.P!=null && cursor.P.physlock!=null)next=cursor.P.physlock.parentTosave;
                else if(cursor.attachable!=null && cursor.attachable.attached)next=cursor.attachable.AttachedToTosave();
                if(next==cursor)return false;
                cursor=next;
            }
            return false;
        }
        static void PublishAuthority(uint[] ids,ulong[] owners,ulong[] epochs)
        {
            if(node==null || ids==null)return;
            for(int i=0;i<ids.Length;i++)node.PublishAuthority(ids[i],owners[i],epochs[i]);
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
            return message!=null && (message.Op==StableOp.MapPart || message.Op==StableOp.MapRequest || message.Op==StableOp.Begin || message.Op==StableOp.Part || message.Op==StableOp.End || message.Op==StableOp.Ack || message.Op==StableOp.SpawnChunk ||
                message.Op==StableOp.ExpSnapshotBegin || message.Op==StableOp.ExpSnapshotPart || message.Op==StableOp.ExpSnapshotEnd);
        }
        static void Classify(byte[] bytes,out NetDeliveryLane lane)
        {
            lane=NetDeliveryLane.Motion;
            if(bytes==null || bytes.Length==0)return;
            if(bytes[0]==254)
            {
                StableMessage message=StableMessage.Decode(bytes);
                lane=message!=null && (message.Op==StableOp.MapPart || message.Op==StableOp.MapRequest || message.Op==StableOp.Begin || message.Op==StableOp.Part || message.Op==StableOp.End || message.Op==StableOp.Ack || message.Op==StableOp.SpawnChunk ||
                    message.Op==StableOp.ExpSnapshotBegin || message.Op==StableOp.ExpSnapshotPart || message.Op==StableOp.ExpSnapshotEnd)?NetDeliveryLane.Bulk:NetDeliveryLane.Critical;
                return;
            }
            if(bytes[0]==8){lane=NetDeliveryLane.Bulk;return;}
            if(StablePackets.IsReliable(bytes))lane=NetDeliveryLane.Critical;
        }
        // Default: SteamNetworkingSockets (SDR). The one-account Steam self-test (2026-11-06)
        // confirmed listen/connect/accept/callbacks/send work on the game's steam_api64.dll
        // despite the binding (v012) vs DLL (v008) version gap.
        // TLD_MP_TRANSPORT=p2p selects vanilla's ISteamNetworking v006 P2P as a fallback
        // (simulation-tested only). Both peers must use the same transport (fingerprint).
        static readonly bool UseSdrTransport=!String.Equals(Environment.GetEnvironmentVariable("TLD_MP_TRANSPORT"),"p2p",StringComparison.OrdinalIgnoreCase);
        static bool EnsureSteamTransport(ulong lobby,ulong host)
        {
            if(loopbackMode)return true;
            if(steamTransport!=null && transportLobby==lobby && transportHost==host)
            {
                steamTransport.AcceptPeer=delegate(ulong peer){return IsLobbyTransportPeer(lobby,peer);};
                if(Own!=host && !steamTransport.HasConnection(host) && Now>=nextSdrRetry)
                {nextSdrRetry=Now+2;steamTransport.Connect(host,TransportVirtualPort);}
                return true;
            }
            if(Now<nextSdrRetry)return false;
            if(steamTransport!=null)steamTransport.Dispose();
            steamTransport=null;transportLobby=transportHost=0;
            try
            {
                steamTransport=UseSdrTransport?(ISteamTransport)new SteamSdrTransport():new SteamP2PTransport();transportLobby=lobby;transportHost=host;
                Debug.Log("[TLD MP SDR] transport="+(UseSdrTransport?"sdr (SteamNetworkingSockets)":"p2p (ISteamNetworking v006, as vanilla)")+" host="+(Own==host));
                steamTransport.Trace=delegate(string message){Debug.Log("[TLD MP SDR] "+message);};
                steamTransport.ReceiveFailure+=delegate(Exception error){Debug.LogError("[TLD MP SDR] transport failure: "+error);};
                steamTransport.AcceptPeer=delegate(ulong peer){return IsLobbyTransportPeer(lobby,peer);};
                steamTransport.PeerDisconnected+=OnSteamPeerDisconnected;
                if(Own==host)steamTransport.Listen(TransportVirtualPort);
                // The native password exchange itself needs this connection.
                else{nextSdrRetry=Now+2;steamTransport.Connect(host,TransportVirtualPort);}
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
            RevokeBusPairPeer(peer);
            outbox.Drop(peer);bulkOutbox.Drop(peer);delivery.Drop(peer);
            if(node==null)return;
            string message="SDR peer disconnected ("+reason+"): "+(detail??"");
            HandleTransportLoss(peer,message);
            Debug.LogWarning("[TLD MP v3] Peer "+peer+" "+message);
        }
        static void HandleTransportLoss(ulong peer,string message)
        {
            if(node.IsHost)node.RemovePeer(peer);
            else if(peer==node.Host && node.Error==null)
            {
                // A failed first connection can race the host's listener or world load.
                // Restart on the game update with a fresh session so a retired Hello
                // from the old connection cannot block the new handshake.
                if(!node.Compatible && Now-steamJoinStarted<SteamJoinWindowSeconds)
                {
                    restartSteamJoin=true;nextSdrRetry=Now+SteamJoinRetrySeconds;
                    Debug.Log("[TLD MP join] Retrying initial Steam connection (attempt "+(steamJoinAttempts+1)+", "+
                        (Now-steamJoinStarted).ToString("F0",System.Globalization.CultureInfo.InvariantCulture)+" s of "+SteamJoinWindowSeconds+" s)");
                }
                else node.Stop(message);
            }
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
            if(loopbackMode){if(node.IsHost)node.RemovePeer(peer);else node.Stop(reason);}
            else HandleTransportLoss(peer,reason);
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
        // ---- item authority gate --------------------------------------------------
        // Host only, one decision per native item message, default off. The host is
        // the single arbiter: a guest never filters its own sends and trusts the
        // host relay instead. Only the native item types the gate models are ever
        // refused; bootstrap/handshake (8/9), snapshot (253/254) and every other
        // type pass through untouched. A refused message is never buffered,
        // relayed or applied.
        static bool ExperimentalItemAllowed(ulong sender,byte[] bytes)
        {
            if(!itemGateEnabled || node==null || !node.IsHost || vehicleAuthority==null)return true;
            ExperimentalItemGate.GateResult result=ExperimentalItemGate.Check(sender,bytes,itemGateLookup);
            if(result==ExperimentalItemGate.GateResult.Allow)return true;
            itemGateRejected++;
            if(Now>=nextItemGateLog)
            {
                nextItemGateLog=Now+1;
                Debug.LogWarning("[TLD MP experimental] item-gate result="+result+" type="+(bytes!=null && bytes.Length>0?bytes[0].ToString():"?")+
                    " entity="+GateEntity(bytes)+" sender="+sender+" "+vehicleAuthority.Summary());
            }
            return false;
        }
        static uint GateEntity(byte[] bytes)
        {
            try{return bytes==null || bytes.Length<5?0:StablePackets.Item(bytes);}
            catch(Exception){return 0;}
        }
        // Host only. Player messages 10/11/12/18 describe the sender's own body;
        // the payload identity must match the authenticated sender. A guest trusts
        // the host relay, so this gate runs on the host only.
        static bool ExperimentalPlayerAllowed(ulong sender,byte[] bytes)
        {
            if(!playerGateEnabled || node==null || !node.IsHost)return true;
            if(PlayerIdentityPolicy.Check(sender,bytes)!=PlayerIdentityResult.RejectIdentity)return true;
            playerGateRejected++;
            AuthorityGateLog("player",bytes,sender);
            return false;
        }
        // Host only. First writer owns a breakable, the host can always override,
        // and a removed prop is a tombstone forever. This stops two peers breaking
        // the same prop and losing concurrent damage.
        static bool ExperimentalBreakableAllowed(ulong sender,byte[] bytes)
        {
            if(!breakableGateEnabled || node==null || !node.IsHost || breakableAuthority==null)return true;
            if(bytes==null || bytes.Length==0 || !BreakablePolicy.IsBreakableMessage(bytes[0]))return true;
            uint entity;
            if(!BreakablePolicy.TryEntity(bytes,out entity))return true;
            BreakableDecision decision=breakableAuthority.AllowWrite(sender,entity,BreakablePolicy.KindOf(bytes[0]),Now);
            if(decision==BreakableDecision.Accepted)return true;
            breakableGateRejected++;
            if(Now>=nextAuthorityGateLog)
            {
                nextAuthorityGateLog=Now+1;
                Debug.LogWarning("[TLD MP experimental] breakable-gate result="+decision+" type="+bytes[0]+" entity="+entity+" sender="+sender+" "+breakableAuthority.Summary());
            }
            return false;
        }
        static void AuthorityGateLog(string gate,byte[] bytes,ulong sender)
        {
            if(Now<nextAuthorityGateLog)return;
            nextAuthorityGateLog=Now+1;
            Debug.LogWarning("[TLD MP experimental] "+gate+"-gate rejected type="+(bytes!=null && bytes.Length>0?bytes[0].ToString():"?")+" sender="+sender);
        }
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
            if(capturing){if(captureBounded){if(captureOverflow||captured==null||captured.Count>=65536||captureByteCount+bytes.Length>StableNode.MaxSpawnBytes)captureOverflow=true;else{captureByteCount+=bytes.Length;captured.Add((byte[])bytes.Clone());}}else captured.Add((byte[])bytes.Clone());return;}
            if(node!=null && !node.IsHost && RoutePasswordBootstrap(node.Host,bytes))return;
            if(SuppressJoiningPlayerPosition(bytes))return;
            // Suppress only the echo of a message currently being applied from
            // the world snapshot. Native repair/ask replies are still allowed.
            if(dispatching && !RepairRequest(bytes[0]))return;
            ObserveNativeSafetyOutgoing(bytes);
            if(hood.forceOneSendTarget){RouteTarget(hood.oneSendTarget,bytes,mode);return;}
            if(node==null)return;
            if(!AllowRabbitState(Own,bytes))return;
            if(bytes[0]==8)
            {
                if(node.IsHost)
                {
                    foreach(ulong peer in new List<ulong>(node.Peers.Keys))node.SendBootstrap(peer,bytes);
                }
                return;
            }
            if(bytes[0]==9){if(!node.IsHost && !node.Joining && !node.AwaitingSnapshot && (!loopbackMode || WorldSceneAvailable()))node.RequestSnapshot();return;}
            // Host egress: never publish a native write for an item another peer owns.
            if(!ExperimentalItemAllowed(Own,bytes))return;
            if(!ExperimentalBreakableAllowed(Own,bytes))return;
            byte[] protectedBytes=ProtectCargoPose(bytes);
            if(protectedBytes!=null && TryRelayHostItem(protectedBytes,0,false))return;
            bool forced=hood.forceReliable || mode==EP2PSend.k_EP2PSendReliable || mode==EP2PSend.k_EP2PSendReliableWithBuffering;
            if(protectedBytes!=null && CaptureVehicleMotion(0,protectedBytes,forced))return;
            if(protectedBytes!=null)node.PublishNative(protectedBytes,hood.forceReliable || mode==EP2PSend.k_EP2PSendReliable || mode==EP2PSend.k_EP2PSendReliableWithBuffering);
        }
        public static void RouteTarget(CSteamID target,byte[] bytes,EP2PSend mode)
        {
            if(!Connected || bytes==null || bytes.Length==0)return;Ensure();
            if(capturing){if(captureBounded){if(captureOverflow||captured==null||captured.Count>=65536||captureByteCount+bytes.Length>StableNode.MaxSpawnBytes)captureOverflow=true;else{captureByteCount+=bytes.Length;captured.Add((byte[])bytes.Clone());}}else captured.Add((byte[])bytes.Clone());return;}
            if(RoutePasswordBootstrap((ulong)target,bytes))return;
            if(SuppressJoiningPlayerPosition(bytes))return;
            if(dispatching && !RepairRequest(bytes[0]))return;
            ObserveNativeSafetyOutgoing(bytes);
            if(node==null)return;
            if(!AllowRabbitState(Own,bytes))return;
            if(bytes[0]==8)
            {
                // Targeted map packets are the native join callback. A later
                // callback must not reload the map we already sent for this session.
                if(WorldSceneAvailable() && node.SendBootstrap((ulong)target,bytes,true))
                    Debug.Log("[TLD MP join] Sent host world to peer="+(ulong)target);
                return;
            }
            if(bytes[0]==9){if(!node.IsHost && !node.Joining && !node.AwaitingSnapshot && (!loopbackMode || WorldSceneAvailable()))node.RequestSnapshot();return;}
            // Host egress: same rule as the broadcast path, before any rewrite.
            if(!ExperimentalItemAllowed(Own,bytes))return;
            if(!ExperimentalBreakableAllowed(Own,bytes))return;
            byte[] protectedBytes=ProtectCargoPose(bytes);
            if(protectedBytes==null)return;
            if(TryRelayHostItem(protectedBytes,(ulong)target,true))return;
            bool forceReliable=networkingScript.s.hood.forceReliable || mode==EP2PSend.k_EP2PSendReliable || mode==EP2PSend.k_EP2PSendReliableWithBuffering;
            if(CaptureVehicleMotion((ulong)target,protectedBytes,forceReliable))return;
            if(node.IsHost)node.PublishNativeTo((ulong)target,protectedBytes,forceReliable);
            else node.PublishNative(protectedBytes,forceReliable);
        }
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
                return false;
            }
            if(dispatching)return AllowRabbitState((ulong)sender,bytes);
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
            if(!AllowRabbitState((ulong)sender,bytes))return false;
            if(!AllowNativeSafetyPacket(bytes))return false;
            // Host ingress: a write for an item this peer does not own is dropped
            // before it can be buffered, replayed or dispatched.
            if(!ExperimentalItemAllowed((ulong)sender,bytes))return false;
            if(!ExperimentalPlayerAllowed((ulong)sender,bytes))return false;
            if(!ExperimentalBreakableAllowed((ulong)sender,bytes))return false;
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
            return role+(Connected?", connected":", not connected")+(loopbackMode?", loopback":"")+(experimentalActive?", experimental":"")+(experimentalActive?"":" (native)");
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
            if(!AllowRabbitState(origin,bytes))return;
            if(UnsetPaint(bytes))return;
            // Host ingress for replayed/queued traffic, identical to AllowIncoming
            // and placed before NoteCargoPose so a refused pose leaves no hint.
            if(!ExperimentalItemAllowed(origin,bytes))return;
            if(!ExperimentalPlayerAllowed(origin,bytes))return;
            if(!ExperimentalBreakableAllowed(origin,bytes))return;
            NoteCargoPose(bytes);
            if(!AllowNativeSafetyPacket(bytes))return;
            if(node!=null && !node.IsHost && origin==node.Host && bytes[0]==8)
            {
                hostWorldEvidence=true;
                Debug.Log("[TLD MP join] Received host world");
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
        public static float MountainMinRoad(objGenScript.categoryClass category){return LargeRockClearance.NativeMinDistance(category);}
        public static void AddGeneratedMountain(Dictionary<Vector3d,objGenScript.objClass> objects,Vector3d key,objGenScript.objClass obj,objGenScript.categoryClass category){LargeRockClearance.AddGenerated(objects,key,obj,category);}
        public static List<roadGenScript.roadClass> LargeRockRoadsNear(Vector3d position,objGenScript.categoryClass category){return LargeRockClearance.RoadsNear(position,category);}
        public static List<roadGenScript.roadClass> TerrainRoadsNear(Vector3d position,terrainGenScript terrain)
        {return WorldPlacementPolicy.EffectiveVersion==0?mapSettingScript.RoadsNear(position):RoadTerrainClearance.RoadsNearTile(position,terrain.tileSize,terrain.vertexDist);}
        public static double TerrainHeight(terrainGenSettings setting,Vector3d position,List<roadGenScript.roadClass> roads,List<poiGenScript.poiClass> pois)
        {return RoadTerrainClearance.Height(setting,position,roads,pois,WorldPlacementPolicy.EffectiveVersion>0,WorldPlacementPolicy.RoadClearance);}
        public static Vector3d PoiIdentitySnap(Vector3d position,float round,poiGenScript gen)
        {return PoiSlopePlacement.ResolveIdentity(position,round,gen);}
        public static poiGenScript.poiClass AddRandomPoi(poiGenScript gen,Vector3d pos,Vector3 rot,int type,bool aligned)
        {return PoiSlopePlacement.AddRandomPoi(gen,pos,rot,(itemdatabase.b)type,aligned);}
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
        public static void TerrainAlignLookupRange(poiGenScript.poiClass poi){TerrainPadBlend.Prepare(poi);foreach(var align in poi.terrainAligns){var d=align.gpos-poi.pos;poi.maxAlignEffectRange=TerrainAlignPolicy.LookupRange(poi.maxAlignEffectRange,d.x,d.z,align.maxEffectRange);}}
        public static bool BiomeGrassDecision(newTerrainTileScript tile){return GrassTransition.HasGrass(tile);}
        public static void BiomeGrassApplied(newTerrainTileScript tile,bool enabled){GrassTransition.Apply(tile,enabled);}
        public static void BiomeGrassMeshChanged(newTerrainTileScript tile){GrassTransition.MeshChanged(tile);}
        public static float RabbitHearingFalloff(float from,float to,float squaredDistance,sensoryscript senses){return RabbitAi.HearingFalloff(from,to,squaredDistance,senses);}
        public static Coroutine RabbitStartFlee(MonoBehaviour owner,IEnumerator routine){return RabbitAi.StartFlee(owner,routine);}
        public static void RabbitMotionObserve(aiscript ai){RabbitFlowDiagnostics.Observe(ai);RabbitLocomotion.Observe(ai);RabbitContact.ObserveRabbit(ai==null?null:ai.nyul);}
        public static void RabbitMotionAnimate(nyulscript rabbit,float speed){RabbitLocomotion.Animate(rabbit,speed);}
        static void BindRabbitPlayers(){RabbitPlayers.Bind(Connected,WorldSceneAvailable(),mainscript.s==null?null:mainscript.s.player,savedatascript.s,node);}
        public static void RabbitScanComplete(sensoryscript senses){RabbitFlowDiagnostics.Scan();BindRabbitPlayers();RabbitPlayers.Scan(senses);}
        public static void RabbitPlayerContact(playerdeathtriggerscript trigger,Collider contact){RabbitFlowDiagnostics.Contact(trigger);BindRabbitPlayers();RabbitPlayers.Damage(trigger,contact);}
        static bool rabbitRuntimeConfigured,rabbitRuntimeLogged;
        // Attack entry additionally requires a resolved canonical target that is
        // geometrically inside the actual native hit volume; SeeTarget's angle,
        // range, roar and cooldown remain native.
        public static bool RabbitAttackGate(nyulscript nyul){return RabbitNyulGuard(nyul)&&RabbitCombatReach.AllowAttack(nyul);}
        static void EnsureRabbitRuntime()
        {
            if(!rabbitRuntimeConfigured||RabbitMotionGuard==null||RabbitLocomotion.Diagnostics==null||RabbitCombatReach.Resolve==null)
            {
                RabbitLocomotion.Diagnostics=delegate(string message){if(loopbackMode||ModDiagnostics.On)Debug.Log(message);};
                RabbitGuestAnimation.Install();RabbitMotionGuard=RabbitLocomotion.NativeMove;rabbitRuntimeConfigured=true;
                RabbitPlayers.RemoteRequestObserved=RabbitFlowDiagnostics.RemoteRequest;
                RabbitMetadataSync.MotionStreamOwnsLivingPoses=true;
                RabbitCombatReach.Resolve=RabbitPlayers.CombatTarget;
            }
            if(!rabbitRuntimeLogged&&(loopbackMode||ModDiagnostics.On))
            {Debug.Log("[TLD MP rabbit flow] runtime callbacks configured");rabbitRuntimeLogged=true;}
        }
        public static void AfterBusStart(carscript car){BusArticulation.Install(car);}
        public static tosaveitemscript BusAttachedOwner(attachablescript part){tosaveitemscript owner;return BusArticulation.TryAttachedOwner(part,out owner)?owner:null;}
        public static bool BusRagdollAllowed(byte[] bytes)
        {
            return BusRearAuthority.Allow(bytes,Connected,savedatascript.s);
        }
        public static Vector3 PassengerVelocity(Rigidbody body){return MovingVehiclePassenger.Velocity(body);}
        public static void PassengerMove(fpscontroller player)
        {
            try{MovingVehiclePassenger.BeginMove(player);}catch(Exception){MovingVehiclePassenger.Reset();}
            try{player.Move();}finally{MovingVehiclePassenger.EndMove(player);}
        }
        static void TickMovingPassenger()
        {
            try{MovingVehiclePassenger.FixedTick(mainscript.s==null?null:mainscript.s.player,Connected&&node!=null&&node.Ready&&!node.IsHost);}
            catch(Exception){MovingVehiclePassenger.Reset();}
        }
        public static bool BusColliderNativeUpdate(dinamiccolliderforbus collider){return !BusArticulation.IsManagedCollider(collider);}
        public static poiGenScript.terrainAlignValueClass TerrainPadValue(Vector3d position,List<poiGenScript.poiClass> pois){return TerrainPadBlend.Value(position,pois);}
        public static Vector3 TerrainAlignLocal(Transform t){var v=TerrainAlignPolicy.UsesRootRelative(t.parent!=null,t.parent==t.root)?t.root.InverseTransformPoint(t.position):t.localPosition;if(t.parent!=null && TerrainAlignPolicy.LevelStartingBusMarker(t.root.name,t.parent.name))v.y=0f;return v;}
        public static Vector3 ItemStreamPosition(Transform t){return ItemStreamPolicy.UsesRootAnchor(t.parent!=null)?t.root.position:t.position;}
        // Guard on tosaveitemscript.DistUnFreezeRB. A kinematic item is kept frozen while no
        // collider lies below it (terrain not loaded yet), for at most ItemSettlePolicy.MaxHoldSeconds.
        static readonly RaycastHit[] itemSettleHits=new RaycastHit[32];
        static readonly ItemSettlePolicy itemSettle=new ItemSettlePolicy();
        public static bool ItemUnfreezeGate(tosaveitemscript item)
        {
            if(item==null)return true;
            if(IsRabbitGuest && RabbitReplicas.IsRabbitItem(item))return false;
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
            var admission=FarPlaceQueue.Admit(id);
            if(admission==SpawnAdmission.CapacityRefused)
            {
                var identity=NativeSpawnAdapter.FarPlaceIdentity(spawnWorldGeneration,id);
                tosaveitemscript placed=null;string atomicReason;
                Construction.BeginFrame(UnityEngine.Time.frameCount);
                if(Construction.RunUnadmittedAtomic(identity,delegate{placed=script.PlaceOne(id,index);},out atomicReason))return placed;
                NoteConstructionRefusal(SpawnAdmission.CapacityRefused,atomicReason,identity);
                return null;
            }
            if(admission!=SpawnAdmission.Admitted)NoteConstructionRefusal(admission,null,NativeSpawnAdapter.FarPlaceIdentity(spawnWorldGeneration,id));
            return null;
        }
        public static string TakeFarPlaceSummary(){return FarPlaceQueue.TakeSummary();}
        public static string TakeSpawnWireSummary()
        {
            int inBytes=node==null?0:node.SpawnIncomingBytes,inHigh=node==null?0:node.SpawnIncomingHigh;
            return "spawnwire{corePending="+inBytes+" coreHigh="+inHigh+" applyPending="+spawnLedgerBytes+" applyHigh="+spawnLedgerHigh+"}";
        }
        public static string TakeTerrainWorkerSummary(){return TerrainWorker.TakeSummary();}
        internal static void FlushFarPlace(){ConstructionFence("world-capture");}
        internal static bool GetConstructionFailureForPoi(poiGenScript.poiClass poi,out string reason)
        {
            reason=null;
            if(poi==null)return false;
            var identity=NativeSpawnAdapter.PoiIdentity(spawnWorldGeneration,poi.posID.x,poi.posID.y,poi.posID.z,poi.id,poi);
            return Construction.FailedReason(identity,out reason);
        }
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
        // New numeric identity is appended; all seven native ordinals stay intact.
        internal const mainscript.fluidenum UrineFluid=(mainscript.fluidenum)7;
        public static void RegisterUrineFluid(mainscript main)
        {
            if(main.fluids.Length>7)
            {
                if(main.fluids[7]==null || main.fluids[7].type!=UrineFluid || main.fluids[7].lname!=ls.w.piss)throw new InvalidOperationException("Conflicting fluid identity 7.");
                return;
            }
            if(main.fluids.Length!=7)throw new InvalidOperationException("Unsupported native fluid table.");
            var table=new mainscript.fluidtype[8];Array.Copy(main.fluids,table,7);
            table[7]=new mainscript.fluidtype{name="Piss",lname=ls.w.piss,type=UrineFluid,color=new Color(0.85f,0.72f,0.10f,1f),burnColor=new Color(0,0,0,0)};
            main.fluids=table;
        }
        static void ConvertBladderWater(mainscript.fluidcontainer bladder)
        {UrineLiquid.NormalizeBladder(bladder);}
        public static float SurvivalFoodRate(survivalscript survival){return SurvivalRates.Food(survival);}
        public static float SurvivalWaterRate(survivalscript survival){return SurvivalRates.Water(survival);}
        public static void ResetSurvivalSickness(survivalscript survival){SurvivalRates.Clear(survival);DrunkEffects.Clear(survival);}
        public static void ConvertWaterToUrine(survivalscript survival,float amount)
        {
            survival.water=Mathf.Clamp(survival.water-amount,0f,survival.maxWater);
            var bladder=survival.player.piss.Tank.F;ConvertBladderWater(bladder);
            bladder.ChangeOne(amount*survival.waterToPissRatio,UrineFluid);
        }
        public static void BladderOverflow(mainscript.fluidcontainer bladder,float amount,bool useSleep)
        {
            ConvertBladderWater(bladder);
            bladder.ChangeOne(amount*(useSleep?mainscript.s.sleepModifier:1f),UrineFluid);
        }
        static bool ContainsUrine(tankscript tank)
        {return tank!=null && tank.F.GetPercent(UrineFluid)>0f;}
        struct ChunkSortEntry {internal Vector3d Position;internal double Distance;internal int Order;}
        sealed class ChunkSortScratch
        {
            internal ChunkSortEntry[] Entries=new ChunkSortEntry[0];
        }
        sealed class ChunkEntryComparer:IComparer<ChunkSortEntry>
        {
            internal static readonly ChunkEntryComparer Instance=new ChunkEntryComparer();
            public int Compare(ChunkSortEntry a,ChunkSortEntry b)
            {int c=a.Distance.CompareTo(b.Distance);return c!=0?c:a.Order.CompareTo(b.Order);}
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<mapSettingScript,ChunkSortScratch> chunkScratch=new System.Runtime.CompilerServices.ConditionalWeakTable<mapSettingScript,ChunkSortScratch>();
        public static List<Vector3d> GetChunksNeededWithoutLinq(mapSettingScript map,float chunkSize,int edge)
        {
            // Return a fresh list: road callers remove entries from it.
            var result=new List<Vector3d>();
            for(int i=0;i<map.genArounds.Count;i++)
                for(int j=-edge/2;j<=edge/2;j++)for(int k=-edge/2;k<=edge/2;k++)
                    result.Add(mainscript.GetChunkPos(map.genArounds[i].upos,k,j,chunkSize));
            if(map.genArounds.Count==0)return result;
            Vector3d orderTo=mainscript.GlobalFromUnityPos(map.genArounds[0].upos);
            var scratch=chunkScratch.GetOrCreateValue(map);int count=result.Count;
            var entries=count>8192?new ChunkSortEntry[count]:scratch.Entries;
            if(entries.Length<count){entries=new ChunkSortEntry[count];scratch.Entries=entries;}
            for(int i=0;i<count;i++)entries[i]=new ChunkSortEntry{Position=result[i],Distance=(result[i]-orderTo).sqrMagnitude,Order=i};
            Array.Sort(entries,0,count,ChunkEntryComparer.Instance);
            for(int i=0;i<count;i++)result[i]=entries[i].Position;
            return result;
        }
        public static void SetWheelVisualPosition(Transform target,Vector3 value)
        {
            var current=target.position;
            if(current.x!=value.x || current.y!=value.y || current.z!=value.z)target.position=value;
        }
        static readonly Predicate<mainscript.fluid> spentTankFluid=delegate(mainscript.fluid fluid){return fluid.amount<=0f;};
        public static void PruneSpentTankFluids(tankscript tank,bool dontSync)
        {tank.F.fluids.RemoveAll(spentTankFluid);}
        public static void ParticleDrinkTransfer(mainscript.fluidcontainer receiver,mainscript.fluidcontainer donor,float amount,bool direct,bool filter,List<mainscript.fluidenum> drinkFilter,tankcapparticledetectionscript detector,tankcapscript destination)
        {ParticleFluidDrinking.Transfer(receiver,donor,amount,direct,filter,drinkFilter,detector,destination);}
        static double urineConsumedMl,urineHydration,urineDamage;
        static long urineTraceNext;
        public static void TraceUrineDrink(float consumed,float hydration,float damage)
        {
            urineConsumedMl+=consumed*1000.0;urineHydration+=hydration;urineDamage+=damage;
            long now=DateTime.UtcNow.Ticks;if(now<urineTraceNext)return;urineTraceNext=now+TimeSpan.TicksPerSecond*2;
            Debug.Log("[Roadworthy urine] cumulative consumed_ml="+urineConsumedMl.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" hydration="+urineHydration.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" damage="+urineDamage.ToString("F4",System.Globalization.CultureInfo.InvariantCulture));
        }
        static fpscontroller fluidDrinker;
        public static float BadFluidAmount(mainscript.fluidcontainer container)
        {float total=0;for(int i=0;i<container.fluids.Count;i++){var f=container.fluids[i];int type=(int)f.type;if(type==0 || type==1 || type==5)total+=f.amount;}return total;}
        public static void NoteBadFluidDrink(survivalscript survival,float amount){SurvivalRates.AddSickness(survival,amount);}
        static float UrineAmount(mainscript.fluidcontainer container)
        {float total=0;for(int i=0;i<container.fluids.Count;i++)if(container.fluids[i].type==UrineFluid)total+=container.fluids[i].amount;return total;}
        public static void DrinkTransfer(mainscript.fluidcontainer receiver,mainscript.fluidcontainer donor,float amount,bool direct,bool filter,List<mainscript.fluidenum> drinkFilter)
        {
            var player=fluidDrinker;fluidDrinker=null;
            float before=player==null?0:UrineAmount(donor),mouthBefore=player==null?0:UrineAmount(receiver);
            float toxicBefore=player==null?0:BadFluidAmount(donor);
            float alcohol=player==null?0:DrunkEffects.AlcoholDrunk(donor,amount);
            float milk=player==null?0:DrunkEffects.MilkDrunk(donor,amount);
            receiver.pour(donor,amount,direct,filter,drinkFilter);
            if(player==null)return;
            NoteBadFluidDrink(player.survival,Mathf.Max(0f,toxicBefore-BadFluidAmount(donor)));
            DrunkEffects.NoteAlcohol(player.survival,alcohol);
            DrunkEffects.NoteMilk(player.survival,milk);
            float swallowed=Mathf.Max(0f,before-UrineAmount(donor));
            float retained=Mathf.Max(0f,UrineAmount(receiver)-mouthBefore);
            if(retained>0f)receiver.ChangeOne(-retained,UrineFluid);
            if(swallowed>0f){player.survival.Drink(swallowed*0.25f);player.survival.Damage(swallowed*player.survival.maxHp*0.02f,Vector3.zero);TraceUrineDrink(swallowed,swallowed*0.25f,swallowed*player.survival.maxHp*0.02f);}
        }
        sealed class FluidPromptState
        {
            internal fpscontroller Controller;
            internal tankcapscript Cap;
            internal int Frame;
            internal bool Reverse;
        }
        static readonly Dictionary<tankscript,FluidPromptState> fluidPromptStates=new Dictionary<tankscript,FluidPromptState>();
        static bool fluidReverse;
        static int fluidToggleFrame=-1;
        static bool PrepareFluidPrompt(fpscontroller player,tankscript held,tankcapscript cap)
        {
            if(player==null || held==null || cap==null || cap==player.drink || cap.Tank==null || cap.Tank==held || cap.valve<=0f || mainscript.s==null || !mainscript.s.crsrLocked)return false;
            bool forward=!held.noFillFrom && !cap.Tank.noFillThis;
            bool reverse=!cap.Tank.noFillFrom && !held.noFillThis;
            if(!forward && !reverse)return false;
            int frame=Time.frameCount;
            FluidPromptState state;
            if(!fluidPromptStates.TryGetValue(held,out state))
            {
                if(fluidPromptStates.Count>=64)fluidPromptStates.Clear();
                state=new FluidPromptState();fluidPromptStates.Add(held,state);
            }
            if(state.Controller!=player || state.Cap!=cap || frame-state.Frame>1)
            {state.Controller=player;state.Cap=cap;state.Reverse=false;}
            state.Frame=frame;
            if(reverse && forward && fluidToggleFrame!=frame && UnityEngine.Input.GetKeyDown(KeyCode.Semicolon))
            {state.Reverse=!state.Reverse;fluidToggleFrame=frame;}
            if(!forward)state.Reverse=true;
            if(!reverse)state.Reverse=false;
            fluidReverse=state.Reverse;
            player.LMB=(fluidReverse?"Drain into held container":ls.l.g(ls.w.fill))+((forward && reverse)?" [;: reverse flow]":"");
            player.selectedCap=cap;player.drank=true;player.noLMBUse=true;player.BLMB=true;
            return true;
        }
        public static void FluidFillPrompt(fpscontroller player,pickupable item,bool hand)
        {
            if(player==null || item==null || item.tank==null)return;
            // DrinkFill consumes the physically picked-up tank when both paths
            // run, so its prompt owns the toggle for this frame.
            if(hand && player.pickedUp!=null && player.pickedUp!=item && player.pickedUp.tank!=null)return;
            var cap=player.pointedCap;
            if(cap==null && player.lookedCap!=null)cap=player.lookedCap;
            if(PrepareFluidPrompt(player,item.tank,cap))player.filled=true;
        }
        // Guard returns true to retain the complete stock forward/drinking path.
        public static bool NativeFluidFillAllowed(fpscontroller player,tankscript held,tankcapscript cap,bool drinking)
        {
            fluidDrinker=drinking?player:null;
            if(drinking || cap==null || cap==player.drink)return true;
            if(!PrepareFluidPrompt(player,held,cap) || !fluidReverse)return true;
            if(held.noFillThis || cap.Tank.noFillFrom || !player.input.lmb || !mainscript.s.crsrLocked || cap.valve<=0f || !(held.F.GetAmount()<held.F.maxC) || !(cap.Tank.F.GetAmount()>0f))return false;
            float amount=mainscript.s.pourSpeed*cap.valve*Time.deltaTime*cap.flowA;
            if(cap.Tank.F.GetPercent(mainscript.fluidenum.oil)>0.25f && held.slowOil)
            {
                float current=held.F.GetAmount();
                if(current>0.05f && held.F.GetPercent(mainscript.fluidenum.oil)<1f)amount=Mathf.Clamp(amount*current*mainscript.s.slowOilModifier,0.0001f,amount);
            }
            held.F.pour(cap.Tank.F,amount,true,false,null);
            cap.Filling();cap.PlayFillSound();held.Upd();cap.Tank.Upd();
            return false;
        }
        public static void TankPourTick(tankcapscript tank){if(tank!=null && mainscript.s!=null && mainscript.s.player!=null && mainscript.s.player.piss==tank && tank.Tank!=null)ConvertBladderWater(tank.Tank.F);if(tank==null || !tank.started || tank.ps==null)return;int action=IdlePourPausePolicy.Decide(tank.em.enabled,tank.ps.isPaused,tank.ps.particleCount);if(action==IdlePourPausePolicy.Pause)tank.ps.Pause();else if(action==IdlePourPausePolicy.Resume)tank.ps.Play();}
        public static void WornItemDestroyed(pickupable item){WornItemGuard.Destroyed(item);}
        public static void RadiatorRepairCheck(partconditionscript condition){try{RadiatorRepair.BeforeCanRepair(condition);}catch(Exception){}}
        public static void TrailerGripStarted(attachgripscript grip){try{TrailerHitch.Wire(grip);TrailerSync.RegisterGrip(grip);}catch(Exception){}}
        public static pickupable TrailerGripP(tosaveitemscript save){return TrailerHitch.P(save);}
        public static void HideListPrune(makeinvisible hide){WornItemGuard.PruneHide(hide);}
        public static void SafeSlice(randomSlice slice,bool darabForce,bool darabAlsoVagas,Vector3 terrainNormal,Vector3 lossyScale){try{slice.SSSLiceee(darabForce,darabAlsoVagas,terrainNormal,lossyScale);}catch(Exception error){SliceGuard.Failed(error);}finally{SliceGeometryEnd();SlicePairsEnd();}}
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
                    TrailerSync.ForwardPoses(item);
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
        public static bool CreatureMoveGate(movecreaturescript creature){if(RabbitReplicas.GuestMove(creature))return false;return RabbitLocomotion.NativeMove(creature)&&WildlifeWeather.CreatureMove(creature);}
        public static bool CreatureJumpGate(movecreaturescript creature){if(RabbitReplicas.GuestMove(creature))return false;return RabbitLocomotion.NativeJump(creature)&&WildlifeWeather.CreatureJump(creature);}
        // Guard on tosaveitemscript.ThisOneInChargeCar(carWhatDoing). False = this
        // machine is not in charge (CarOwnershipPolicy); true = the native rule.
        // Return filter on tosaveitemscript.ThisOneInCharge: a hitched trailer follows its towing car.
        public static bool TrailerChargeFilter(bool native,tosaveitemscript trailer){return TrailerSync.ChargeFilter(native,trailer);}
        // Guard on syncScript.UpdInWorld: false drops a pose for a hitched trailer this machine owns.
        public static bool TrailerInboundGate(uint id){return TrailerSync.AcceptInbound(id);}
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
        public static void RoadGenerated(roadGenScript road){RoadStateEpoch.Bump();RoadLiveCache.Clear();RoadFarSkip.Clear();if(Connected)StableMap.RoadGenerated(road);}
        // Road-state epoch entry hooks (runtime/RoadStateEpoch.cs). Each hook runs before
        // the native body, so a bump always precedes the write it guards.
        public static void RoadDotChunkAdding(roadGenScript road,Vector3d cell){if(road==null || road.chunks==null || !road.chunks.ContainsKey(cell))RoadStateEpoch.Bump();}
        public static void RoadChunksRemoving(roadGenScript road){if(road==null || road.chunksToRemove==null || road.chunksToRemove.Count>0)RoadStateEpoch.Bump();}
        public static void RoadConnectionsStarting(roadGenScript road){RoadStateEpoch.Bump();}
        public static void RoadStateReset(roadGenScript road){RoadStateEpoch.Bump();}
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
        // One capture/encode/roundtrip check for the experimental transfer. SendWorld
        // below keeps its own inline copy: the world-load IL replay transplants that
        // method body, so it may only call the fixture's existing seam methods.
        internal static byte[] CaptureWorldBytes()
        {
            var frame=CaptureWorld();
            byte[] bytes=StableJson.Write(frame);
            var check=StableJson.Read<WorldFrame>(bytes);
            if(check.Active.Count!=frame.Active.Count || check.Pois.Count!=frame.Pois.Count || check.PlayerMessages.Count!=frame.PlayerMessages.Count)
                throw new InvalidOperationException("Snapshot serialization lost world/player records.");
            JoinDiagnostic("Snapshot JSON roundtrip active="+check.Active.Count+"/"+frame.Active.Count+" pois="+check.Pois.Count+"/"+frame.Pois.Count+" players="+check.PlayerMessages.Count+"/"+frame.PlayerMessages.Count);
            return bytes;
        }
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
        // Why the host cannot capture its world right now, or null when it can.
        static string WorldCaptureBlocked()
        {
            return node==null?"node=null":!node.IsHost?"not host":mainscript.s==null?"mainscript=null":savedatascript.s==null?"savedatascript=null":menuhandler.s==null?"menuhandler=null":menuhandler.s.currentMainMap==null?"currentMainMap=null":savedatascript.s.data==null?"save.data=null":null;
        }
        // ---- experimental join transfer (mode-gated; see ExperimentalJoin) --------
        // Same native capture/encode/apply, different framing. The host opens the join
        // journal barrier with BeginExternalSync and streams ExpSnapshotBegin/Part/End
        // frames; the guest acks with the native Ack so the journal replay and Live are
        // the untouched native code.
        static void ExperimentalSendSnapshot(ulong peer)
        {long start=PhaseTimings.Begin();try{ExperimentalSendWorld(peer);}finally{PhaseTimings.End(PhaseTimings.SnapshotSend,start,"experimental capture+encode+frames");}}
        static void ExperimentalSendWorld(ulong peer)
        {
            JoinDiagnostic("ExperimentalSendWorld entry target="+peer+" "+MapDiagnostic());
            string blocked=WorldCaptureBlocked();
            if(blocked!=null){JoinDiagnostic("ExperimentalSendWorld blocked: "+blocked);return;}
            if(!WorldSceneAvailable()){JoinDiagnostic("ExperimentalSendWorld blocked: world scene unavailable");return;}
            byte[] bytes=CaptureWorldBytes();
            if(bytes.Length<=0 || bytes.Length>StableNode.MaxSnapshotBytes)
            {
                // Abort, do not fall back: a half-experimental transfer would mix two
                // protocols for one peer.
                Debug.LogError("[TLD MP experimental] join: world snapshot is "+bytes.Length+" bytes; the bounded transfer limit is "+StableNode.MaxSnapshotBytes+" bytes. Transfer aborted.");
                return;
            }
            byte[] hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(bytes);
            ulong transferId=node.BeginExternalSync(peer);
            if(transferId==0){JoinDiagnostic("ExperimentalSendWorld blocked: no peer/journal for "+peer);return;}
            int frames=0;
            bool emitted=ExperimentalJoin.EmitSnapshot(bytes,hash,delegate(StableOp op,byte[] data)
            {
                var message=new StableMessage {Op=op,Lobby=node.Lobby,Session=node.Session,Origin=node.Own,Epoch=transferId,Data=data};
                SendEnvelope(peer,message.Encode(),true);frames++;
            });
            if(!emitted){Debug.LogError("[TLD MP experimental] join: snapshot framing refused bytes="+bytes.Length+"; transfer aborted.");return;}
            JoinDiagnostic("ExperimentalSendWorld sent bytes="+bytes.Length+" transfer="+transferId+" frames="+frames+" parts="+ExperimentalJoin.PartCount(bytes.Length));
        }
        // Host egress for interaction/state types only. Host-local actions get a
        // monotonic per-entity event id and are sent as ExpItemEvent; syncing peers
        // still receive the native bytes through the join journal. Motion poses and
        // guest-originated re-broadcasts stay native. Returns true when the caller
        // must suppress the native send.
        static bool TryRelayHostItem(byte[] bytes,ulong target,bool targeted)
        {
            if(!experimentalItemRelayEnabled || node==null || !node.IsHost || dispatching || DispatchType!=-1)return false;
            if(bytes==null || bytes.Length==0 || !ExperimentalItemRelay.IsRelayType(bytes[0]))return false;
            uint entity=StablePackets.Item(bytes);
            if(entity==0)return false;
            ulong eventId=NextItemRelayId(entity);
            if(eventId==0)return false;
            byte[] data=ExperimentalItemRelay.Encode(entity,eventId,Own,bytes);
            if(data==null)return false;
            var message=new StableMessage { Op=StableOp.ExpItemEvent,Lobby=node.Lobby,Session=node.Session,Origin=Own,Epoch=node.Session,Key="",Data=data };
            byte[] frame=message.Encode();
            bool any=false;
            if(targeted)
            {
                StablePeer peer;
                if(!node.Peers.TryGetValue(target,out peer))return false;
                any=true;
                if(peer.Ready)SendEnvelopeCore(target,frame,true,false);
                else node.PublishNativeTo(target,bytes);
            }
            else
            {
                foreach(var pair in node.Peers)
                {
                    any=true;
                    if(pair.Value.Ready)SendEnvelopeCore(pair.Key,frame,true,false);
                    else node.PublishNativeTo(pair.Key,bytes);
                }
            }
            // No peer is a valid host-only state: suppress the native send so the
            // event-id sequence remains the one authoritative stream.
            return any || target==0;
        }
        static ulong NextItemRelayId(uint entity)
        {
            ulong id;
            if(!itemRelayEventIds.TryGetValue(entity,out id))
            {
                if(itemRelayEventIds.Count>=8192)return 0;
                id=0;
            }
            if(id==ulong.MaxValue)return 0;
            id++;
            itemRelayEventIds[entity]=id;
            return id;
        }
        // Guest only. Consumes ExpItemEvent frames before native dispatch, applies
        // each event once through ItemEventStream (ordering + dependency buffering),
        // and then runs the native handler with dispatching=true so it cannot echo.
        static bool ReceiveExperimentalItemRelay(ulong sender,byte[] frame)
        {
            if(!experimentalItemRelayEnabled || itemRelayReceiver==null || node==null || node.IsHost)return false;
            if(frame==null || frame.Length==0 || frame[0]!=254)return false;
            var m=StableMessage.Decode(frame);
            if(m==null || m.Op!=StableOp.ExpItemEvent)return false;
            if(sender!=node.Host || m.Origin!=node.Host || m.Session!=node.HostSession || m.Epoch!=node.HostSession)return true;
            byte[][] ready;
            if(!itemRelayReceiver.Accept(m.Data,Now,out ready))return true;
            if(ready!=null)for(int i=0;i<ready.Length;i++)DispatchRelayedItem(ready[i]);
            return true;
        }
        static void DispatchRelayedItem(byte[] payload)
        {
            if(payload==null || payload.Length==0 || networkingScript.s==null)return;
            bool previous=dispatching;ulong priorSender=dispatchSender;int priorType=DispatchType;
            dispatching=true;dispatchSender=node.Host;DispatchType=payload[0];
            try{networkingScript.s.ProcessMessage(new CSteamID(node.Host),payload,(msgType)payload[0]);}
            catch(Exception e){packetErrors++;if(Now>=nextFailure){nextFailure=Now+5;Debug.LogError("[TLD MP experimental] relayed item handler failed: "+e);}}
            finally{dispatching=previous;dispatchSender=priorSender;DispatchType=priorType;}
        }
        // Guest only. Consumes the experimental join frames before native dispatch, so
        // they never reach node.Receive or the stock handler. Returns true for every
        // frame that belongs to this protocol, valid or not: malformed frames are
        // bounded and dropped, never forwarded.
        static bool ReceiveExperimentalJoin(ulong sender,byte[] frame)
        {
            if(!experimentalJoinEnabled || node==null || node.IsHost)return false;
            if(frame==null || frame.Length==0 || frame[0]!=254)return false;
            var m=StableMessage.Decode(frame);
            if(m==null || !ExperimentalJoin.IsSnapshotOp(m.Op))return false;
            if(!node.Compatible || node.HostSession==0 || sender!=node.Host || m.Origin!=node.Host || m.Session!=node.HostSession || m.Epoch==0)
            {
                JoinDiagnostic("Experimental join frame dropped: envelope mismatch op="+m.Op+" sender="+sender);
                return true;
            }
            ulong transferId=m.Epoch;
            if(m.Op==StableOp.ExpSnapshotBegin)
            {
                if(!experimentalJoin.Begin(transferId,m.Data))
                {
                    node.AbandonExternalReceive();
                    JoinDiagnostic("Experimental join Begin refused transfer="+transferId+" data="+m.Data.Length);
                    return true;
                }
                node.BeginExternalReceive(transferId);
                JoinDiagnostic("Experimental join Begin transfer="+transferId+" bytes="+experimentalJoin.Total);
                return true;
            }
            if(m.Op==StableOp.ExpSnapshotPart)
            {
                if(!experimentalJoin.Part(transferId,m.Data))
                {
                    node.AbandonExternalReceive();
                    JoinDiagnostic("Experimental join Part refused transfer="+transferId+" data="+m.Data.Length);
                    return true;
                }
                node.NoteExternalProgress();
                return true;
            }
            // ExpSnapshotEnd
            if(m.Data.Length!=0)
            {
                experimentalJoin.Cancel();node.AbandonExternalReceive();
                JoinDiagnostic("Experimental join End refused transfer="+transferId+" data="+m.Data.Length);
                return true;
            }
            byte[] snapshot;
            if(!experimentalJoin.End(transferId,out snapshot) || snapshot==null)
            {
                node.AbandonExternalReceive();
                JoinDiagnostic("Experimental join End incomplete transfer="+transferId+" (size/hash); retrying after the request window");
                return true;
            }
            try{TimedApplyWorld(snapshot);}
            catch(Exception e){node.Stop("World snapshot apply failed: "+e.Message);return true;}
            node.AwaitExternalLive(transferId);
            node.SendExternalAck(transferId);
            JoinDiagnostic("Experimental join snapshot applied bytes="+snapshot.Length+" transfer="+transferId);
            return true;
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
                foreach(var item in frame.Items.items)save.AddToChunk(item.transform.pos,item.id);
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
                if(ReceiveRabbitMotion(source,b))return;
                if(ReceiveSmoke(source,b))return;
                if(ReceiveBusPair(source,b))return;
                if(ReceiveVehicleMotion(source,b))return;
                if(ReceiveExperimentalItemRelay(source,b))return;
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
                    RevokeBusPairPeer(item.From);ExperimentalBackend.ForgetPeer(item.From);
                    if(vehicleAuthority!=null)vehicleAuthority.ForgetPeer(item.From,Now);
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
                // Experimental join frames are consumed here, before the native dispatch,
                // so they never reach node.Receive or the stock handler. Always false
                // unless the gate is on, which leaves the native path untouched.
                if(ReceiveExperimentalJoin(item.From,item.Bytes))continue;
                if(ReceiveRabbitMotion(item.From,item.Bytes))continue;
                if(ReceiveSmoke(item.From,item.Bytes))continue;
                if(ReceiveBusPair(item.From,item.Bytes))continue;
                if(ReceiveVehicleMotion(item.From,item.Bytes))continue;
                if(ReceiveExperimentalItemRelay(item.From,item.Bytes))continue;
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
        static void SendJoinMap(ulong peer){syncScript.s.SendMapStuff(new CSteamID(peer));}
        internal static void Pulse()
        {
            pulseDrainTicks=pulseTickTicks=0;
            Ensure();CargoDiagnostics.Tick();TickCarBlood();GroundDiagnostics.TickBusPlacement();if(DiagnosticMode)GroundDiagnostics.Tick();if(node==null || restartSteamJoin)return;ReplayPendingItems();TickSpawnHandoff();
            long drainStart=Stopwatch.GetTimestamp();
            TickVehicleImpactBursts();vehiclePairs.Flush(Now,PublishVehiclePacket);SettleLiveUsables();DrainDeliveryQueues();outbox.Flush(Now);bulkOutbox.Flush(Now);
            pulseDrainTicks=Stopwatch.GetTimestamp()-drainStart;
            // The native lobby callback can run before the fingerprint and Hello
            // handshake complete. Send the current world once each peer is ready
            // to receive it, even when that callback has already fired.
            if(node.IsHost && WorldSceneAvailable() && syncScript.s!=null)
                node.SendPendingBootstraps(SendJoinMap);
            if(!node.IsHost && !loopbackMode && !SteamP2PNetworkingUnderTheHoodScript.HasCorrectPassword())return;
            if(!node.IsHost && !node.HostWorldLoaded && (loopbackMode?WorldSceneAvailable():hostWorldEvidence))node.HostWorldLoaded=true;
            long tickStart=Stopwatch.GetTimestamp();node.Tick(Now);pulseTickTicks=Stopwatch.GetTimestamp()-tickStart;
            if(experimentalActive && node.IsHost && node.Error==null){if(vehicleAuthority!=null)TickVehicleGroup();FlushExperimentalAuthority();}
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
                    (!loopbackMode || Loopback.Transport==null?"":" "+Loopback.Transport.TakeQueueHealth())+
                    (experimentalActive?" experimental{"+(vehicleAuthority!=null?vehicleAuthority.Summary():ExperimentalBackend.Summary())+" suppressed="+experimentalMotionSuppressed+" rejected="+experimentalMotionRejected+"}":"")+
                    (itemGateEnabled?" items{rejected="+itemGateRejected+"}":"")+
                    (experimentalItemRelayEnabled?(itemRelayReceiver!=null?" "+itemRelayReceiver.Summary():" relay{host}"):"")+
                    (playerGateEnabled?" players{rejected="+playerGateRejected+"}":"")+
                    (playerReplication!=null?" "+playerReplication.Summary():"")+
                    (breakableGateEnabled?" breakables{rejected="+breakableGateRejected+"}":""));
                healthMaxPending=healthMaxPendingBytes=0;
            }
            if((loopbackMode || ModDiagnostics.On) && Now>=nextJoinDiagnostic)
            {
                nextJoinDiagnostic=Now+5;
                JoinDiagnostic("state host="+node.IsHost+" compatible="+node.Compatible+" ready="+node.Ready+" phase="+node.JoinStatus+" stage="+node.JoinStage+" reason="+node.JoinStageReason+" "+MapDiagnostic());
            }
            if(node.Error!=null && lastError!=node.Error){lastError=node.Error;Debug.LogError("[TLD MP v3] "+node.Error);if(!node.IsHost && !node.Ready && !loopbackMode)JoinFailurePopup.Show(JoinCompatibility.Friendly(node.Error));}
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
            ExperimentalBackend.ForgetPeer(peer);
            if(breakableAuthority!=null)breakableAuthority.ForgetPeer(peer,Now);
            if(playerReplication!=null){ulong[] dropped;playerReplication.ForgetPeer(peer,Now,out dropped);}
            BusSeatClaims.ClearPeer(peer);
            // Host only: a departed peer keeps no vehicle or cargo authority.
            if(vehicleAuthority!=null)vehicleAuthority.ForgetPeer(peer,Now);
            outbox.Drop(peer);bulkOutbox.Drop(peer);delivery.Drop(peer);poseFilter.Forget(peer);senderClock.Forget(peer);liveUsables.Drop(peer);
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
        public static void NativeSystemBegin(int slot){FrameDiagnostics.NativeBegin(slot);}
        public static void NativeSystemEnd(int slot){FrameDiagnostics.NativeEnd(slot);}
        public static void NativeMainBegin(){SurvivalHud.Begin();RenderSwitches.MainFrame();CargoGhost.Frame();FrameDiagnostics.RegisterMainThread();FrameDiagnostics.NativeBegin(0);}
        public static void NativeMainEnd(){SurvivalHud.End();FrameDiagnostics.NativeEnd(0);}
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
            if(GroundDiagnostics.TryBusPlacement(script) || GroundDiagnostics.HouseSpawnReady(script))SpawnPoiCoordinated(script,poi);
        }
        internal static readonly SpawnCoordinator Construction=new SpawnCoordinator(delegate{return (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency*1000.0;});
        static long spawnWorldGeneration=1;
        internal static long SpawnWorldGeneration{get{return spawnWorldGeneration;}}
        static savedatascript constructionWorldSave;
        static object constructionWorldMap,constructionWorldData;
        static int spawnNativeCallDepth;
        static string lastConstructionRefusal;
        internal static void EnsureWorldGeneration()
        {
            var save=savedatascript.s;var map=menuhandler.s==null?null:menuhandler.s.currentMainMap;var data=save==null?null:save.data;
            if(save==constructionWorldSave && (object)map==constructionWorldMap && (object)data==constructionWorldData)return;
            var candidate=spawnWorldGeneration+1;
            string reason;
            if(!Construction.ResetWorld(candidate,out reason))throw new InvalidOperationException("[TLD MP construction] world reset blocked: "+reason);
            if(constructionWorldSave!=null && node!=null && node.Peers.Count>0)node.RetirePeersForWorldReplacement();
            spawnWorldGeneration=candidate;
            constructionWorldSave=save;constructionWorldMap=map;constructionWorldData=data;
        }
        static long NextWorldGeneration(){return ++spawnWorldGeneration;}
        static void NoteConstructionRefusal(SpawnAdmission status,string reason,SpawnJobKey identity)
        {
            var text=status+(reason==null?"":" "+reason);
            if(text==lastConstructionRefusal)return;lastConstructionRefusal=text;
            Debug.LogWarning("[TLD MP construction] admission refused: "+text+" source="+identity.Source+" key="+identity.Key);
        }
        static void SpawnPoiCoordinated(poiScript script,poiGenScript.poiClass poi)
        {
            EnsureWorldGeneration();
            Construction.BeginFrame(UnityEngine.Time.frameCount);
            var identity=NativeSpawnAdapter.PoiIdentity(spawnWorldGeneration,poi.posID.x,poi.posID.y,poi.posID.z,poi.id,poi);
            var preflight=Construction.Preflight(identity);
            if(preflight!=SpawnAdmission.Admitted){NoteConstructionRefusal(preflight,null,identity);return;}
            var job=NativeSpawnAdapter.PoiJob(identity,new SpawnUnit(null,delegate{RunNativePoiSpawn(script,poi);}));
            string reason;var admission=Construction.TryAdmit(job,out reason);
            if(admission==SpawnAdmission.CapacityRefused)
            {
                string atomicReason;
                if(!Construction.RunUnadmittedAtomic(identity,delegate{RunNativePoiSpawn(script,poi);},out atomicReason))
                    NoteConstructionRefusal(SpawnAdmission.CapacityRefused,atomicReason,identity);
                return;
            }
            if(admission!=SpawnAdmission.Admitted){NoteConstructionRefusal(admission,reason,identity);return;}
            Construction.RunSynchronous(job);
        }
        static void RunNativePoiSpawn(poiScript script,poiGenScript.poiClass poi)
        {
            bool spawnedItems=poi.spawnedItems,spawnedFixed=poi.spawnedFixed;
            spawnNativeCallDepth++;
            try{script.SpawnStuff(poi);}
            catch{poi.spawnedItems=spawnedItems;poi.spawnedFixed=spawnedFixed;throw;}
            finally{spawnNativeCallDepth--;}
        }
        static void ConstructionFence(string reason)
        {
            if(spawnApplyDepth>0 || spawnNativeCallDepth>0)throw new InvalidOperationException("[TLD MP construction] "+reason+" inside an active construction mutation");
            EnsureWorldGeneration();Construction.CompletePendingConstruction(reason,"read");
            string applyReason;
            if(!VerifyPendingSpawnConstruction(out applyReason))throw new InvalidOperationException("[TLD MP construction] "+reason+": "+applyReason);
        }
        sealed class TerrainBiomeBinding {internal readonly int Mode;internal TerrainBiomeBinding(int mode){Mode=mode;}}
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<terrainGenSettings,TerrainBiomeBinding> terrainBiomeBindings=new System.Runtime.CompilerServices.ConditionalWeakTable<terrainGenSettings,TerrainBiomeBinding>();
        static int terrainBiomeEpoch;
        [ThreadStatic] static terrainGenSettings cachedBiomeTerrain;
        [ThreadStatic] static TerrainBiomeBinding cachedBiomeBinding;
        [ThreadStatic] static int cachedBiomeEpoch;
        public static void RegisterTerrainBiome(terrainGenSettings setting)
        {
            if((object)setting==null)return;
            lock(terrainBiomeBindings)
            {
                TerrainBiomeBinding old;int mode=WorldBiomePolicy.Mode;
                if(terrainBiomeBindings.TryGetValue(setting,out old) && old.Mode==mode)return;
                terrainBiomeBindings.Remove(setting);terrainBiomeBindings.Add(setting,new TerrainBiomeBinding(mode));
                System.Threading.Interlocked.Increment(ref terrainBiomeEpoch);
            }
        }
        public static void SetWorldBiomeMode(int mode)
        {
            if(WorldBiomePolicy.Mode!=mode)TerrainWorker.Clear();
            WorldBiomePolicy.SetMode(mode);
            StableMap.RefreshBiomeConfig();
        }
        public static void SetWorldPlacementVersion(int value)
        {if(WorldPlacementPolicy.Version!=value)TerrainWorker.Clear();WorldPlacementPolicy.SetVersion(value);StableMap.RefreshBiomeConfig();}
        public static void ConfigureWorldBiome()
        {
            var scene=DataFromMenuScript.s;
            if(scene==null || scene.mainmenu){WorldRelocationLedger.Restore(null);SetWorldPlacementVersion(0);SetWorldBiomeMode(0);return;}
            if(scene.multi){WorldRelocationLedger.Restore(null);return;} // HostConfig was received before native map bootstrap.
            int mode=0;
            int placement=0;
            if(!scene.load)WorldRelocationLedger.Restore(null);
            if(scene.load)
            {
                if(savedatascript.s==null || savedatascript.s.data==null || savedatascript.s.data.properties==null)
                    throw new InvalidOperationException("Cannot determine the saved world biome.");
                mode=WorldBiomePolicy.ModeFromVersion(savedatascript.s.data.properties.version);
                placement=WorldPlacementPolicy.ReadVersion(savedatascript.s.data.properties.version);
                WorldRelocationLedger.Restore(savedatascript.s.data.properties.version);
            }
            else if(scene.newgame && gamemodesettingsscript.s!=null && gamemodesettingsscript.s.S!=null)
            {
                mode=gamemodesettingsscript.s.S.biome;
                placement=WorldPlacementPolicy.CurrentVersion;
                if(mode<0 || mode>3)throw new InvalidOperationException("Invalid selected world biome.");
            }
            SetWorldPlacementVersion(placement);
            SetWorldBiomeMode(mode);
            if(ModDiagnostics.On)Debug.Log("[TLD MP biome] world mode="+mode+" seed="+scene.seed+" source="+(scene.load?"save":scene.newgame?"new":"default"));
        }
        public static string SaveVersionLabel(string version)
        {
            if(version==null)return null;
            const string marker=" [Roadworthy relocation: ";
            int start=version.IndexOf(marker,StringComparison.Ordinal);
            if(start<0)return version;
            int end=version.IndexOf(']',start+marker.Length);
            return end<0?version.Substring(0,start)+" [relocation data damaged]":version.Remove(start,end-start+1);
        }
        public static void StampBiomeVersion(saveproperties properties)
        {if(properties!=null)properties.version=WorldRelocationLedger.Stamp(WorldPlacementPolicy.StampVersion(WorldBiomePolicy.StampVersion(properties.version,WorldBiomePolicy.Mode),WorldPlacementPolicy.Version));}
        public static Color ApplyWorldBiome(Color original,terrainGenSettings setting)
        {
            if(WorldBiomePolicy.HasWorkerMode)return WorldBiomePolicy.Apply(original);
            if((object)setting==null)return WorldBiomePolicy.Apply(original);
            int epoch=System.Threading.Volatile.Read(ref terrainBiomeEpoch);
            if(!object.ReferenceEquals(cachedBiomeTerrain,setting) || cachedBiomeEpoch!=epoch)
            {
                TerrainBiomeBinding binding;terrainBiomeBindings.TryGetValue(setting,out binding);
                cachedBiomeTerrain=setting;cachedBiomeBinding=binding;cachedBiomeEpoch=epoch;
            }
            return cachedBiomeBinding==null?WorldBiomePolicy.Apply(original):WorldBiomePolicy.ApplyMode(original,cachedBiomeBinding.Mode);
        }
        public static void BeforeNativeSaveData(){ConstructionFence("savedatascript.SaveToData");}
        public static void BeforeNativeSaveScreen(){ConstructionFence("newSaveScreenScript.Save");}
        public static void BeforeNativeExport(){ConstructionFence("construction export");}
        public static void RequireConstructionSettled()
        {
            if(spawnNativeCallDepth>0 || spawnApplyDepth>0 || Construction.MutationActive || Construction.HasFailedCurrent)
                throw new InvalidOperationException("[TLD MP construction] savedatascript.SaveToDictionary during an uncommitted or failed construction mutation");
        }
        public static void ResetConstruction()
        {
            var candidate=spawnWorldGeneration+1;
            string reason;
            if(!Construction.ResetWorld(candidate,out reason))throw new InvalidOperationException("[TLD MP construction] "+reason);
            spawnWorldGeneration=candidate;
            if(node!=null)node.RetirePeersForWorldReplacement();
            spawnLedgers.Clear();spawnCommitted.Clear();spawnLedgerBytes=0;
            announcedSpawns.Clear();spawnIDs=null;spawnPayload=null;
            if(node!=null)node.ClearSpawnTransfers();
        }
        static readonly Stack<List<Rigidbody>> fixedBodyLists=new Stack<List<Rigidbody>>();
        static readonly Stack<List<tosaveitemscript>> fixedItemLists=new Stack<List<tosaveitemscript>>();
        static readonly Stack<List<randomTypeSelector>> fixedSelectorLists=new Stack<List<randomTypeSelector>>();

        // Arrays are scratch only: every native entry is overwritten before use.
        // Nested category selection rents separate buffers until its caller exits.
        static readonly Dictionary<int,Stack<int[]>> propWeights=new Dictionary<int,Stack<int[]>>();
        static int[] RentPropWeights(int length)
        {
            Stack<int[]> pool;if(propWeights.TryGetValue(length,out pool) && pool.Count>0)return pool.Pop();
            return new int[length];
        }
        static void ReturnPropWeights(int[] value)
        {
            if(value==null || value.Length>1024)return;
            Stack<int[]> pool;if(!propWeights.TryGetValue(value.Length,out pool))
            {if(propWeights.Count>=64)return;propWeights.Add(value.Length,pool=new Stack<int[]>());}
            if(pool.Count<4)pool.Push(value);
        }
	public static int SelectPropCategory(int categoryId)
	{
        int[] array=null,array2=null;try
        {
		int num = 0;
		if (itemdatabase.s.categories[categoryId].items != null)
		{
			array = RentPropWeights(itemdatabase.s.categories[categoryId].items.Length);
			if (itemdatabase.s.categories[categoryId].itemchances == null)
			{
				for (int i = 0; i < array.Length; i++)
				{
					array[i] = 1;
				}
			}
			else
			{
				for (int j = 0; j < array.Length; j++)
				{
					array[j] = itemdatabase.s.categories[categoryId].itemchances[j];
				}
			}
			for (int k = 0; k < itemdatabase.s.categories[categoryId].items.Length; k++)
			{
				num = (array[k] = num + array[k]);
			}
		}
		int num2 = 0;
		if (itemdatabase.s.categories[categoryId].categories != null)
		{
			array2 = RentPropWeights(itemdatabase.s.categories[categoryId].categories.Length);
			if (itemdatabase.s.categories[categoryId].categorychances == null)
			{
				for (int l = 0; l < array2.Length; l++)
				{
					array2[l] = 1;
				}
			}
			else
			{
				for (int m = 0; m < array2.Length; m++)
				{
					array2[m] = itemdatabase.s.categories[categoryId].categorychances[m];
				}
			}
			for (int n = 0; n < itemdatabase.s.categories[categoryId].categories.Length; n++)
			{
				num2 = (array2[n] = num2 + array2[n]);
			}
		}
		int num3 = UnityEngine.Random.Range(1, num + num2 + 1);
		if (num3 <= num)
		{
			if (itemdatabase.s.categories[categoryId].dependOnCar)
			{
				if (UnityEngine.Random.Range(0, 100) < 25 && itemdatabase.s.carTypeToCarID[(int)mainscript.s.currentCar] < itemdatabase.s.categories[categoryId].items.Length)
				{
					return itemdatabase.s.categories[categoryId].items[itemdatabase.s.carTypeToCarID[(int)mainscript.s.currentCar]];
				}
				int num4 = 0;
				int num5;
				do
				{
					num5 = UnityEngine.Random.Range(0, itemdatabase.s.categories[categoryId].items.Length);
					num4++;
				}
				while (num5 == itemdatabase.s.carTypeToCarID[(int)mainscript.s.currentCar] && num4 < 10);
				return itemdatabase.s.categories[categoryId].items[num5];
			}
			for (int num6 = 0; num6 < array.Length; num6++)
			{
				if (num3 <= array[num6])
				{
					return itemdatabase.s.categories[categoryId].items[num6];
				}
			}
		}
		else
		{
			if (itemdatabase.s.categories[categoryId].dependOnCar)
			{
				if (UnityEngine.Random.Range(0, 100) < 25 && itemdatabase.s.carTypeToCarID[(int)mainscript.s.currentCar] < itemdatabase.s.categories[categoryId].categories.Length)
				{
					return itemdatabase.s.categories[categoryId].categories[itemdatabase.s.carTypeToCarID[(int)mainscript.s.currentCar]];
				}
				int num7 = 0;
				int num8;
				do
				{
					num8 = UnityEngine.Random.Range(0, itemdatabase.s.categories[categoryId].categories.Length);
					num7++;
				}
				while (num8 == itemdatabase.s.carTypeToCarID[(int)mainscript.s.currentCar] && num7 < 10);
				return itemdatabase.s.categories[categoryId].categories[num8];
			}
			for (int num9 = 0; num9 < array2.Length; num9++)
			{
				if (num3 <= array2[num9])
				{
					return SelectPropCategory(itemdatabase.s.categories[categoryId].categories[num9]);
				}
			}
		}
		return 0;
	        }finally{ReturnPropWeights(array);ReturnPropWeights(array2);}
}
        public static GameObject ConstructFixedProps(GameObject source,Vector3 position,Quaternion rotation,Transform parent)
        {return FixedPropConstruction.Instantiate(source,position,rotation,parent);}
        public static void FixedSpawnFStart(fixedSpawnScript script)
        {
            if(script.useStartCar)
            {
                int startCar=(int)mainscript.s.StartCar;
                if(startCar>=0 && startCar<script.startCars.Count && script.startCars[startCar]!=null)
                    script.startCars[startCar].SetActive(true);
            }
            var bodies=fixedBodyLists.Count==0?new List<Rigidbody>():fixedBodyLists.Pop();
            try
            {
                script.GetComponentsInChildren<Rigidbody>(false,bodies);
                for(int i=0;i<bodies.Count;i++)bodies[i].isKinematic=true;
            }
            finally{bodies.Clear();if(fixedBodyLists.Count<8 && bodies.Capacity<=1024)fixedBodyLists.Push(bodies);}
            var items=fixedItemLists.Count==0?new List<tosaveitemscript>():fixedItemLists.Pop();
            try
            {
                script.GetComponentsInChildren<tosaveitemscript>(false,items);
                for(int i=0;i<items.Count;i++)items[i].FStart();
            }
            finally{items.Clear();if(fixedItemLists.Count<8 && items.Capacity<=1024)fixedItemLists.Push(items);}
            var selectors=fixedSelectorLists.Count==0?new List<randomTypeSelector>():fixedSelectorLists.Pop();
            try
            {
                script.GetComponentsInChildren<randomTypeSelector>(false,selectors);
                for(int i=0;i<selectors.Count;i++)selectors[i].FStart();
            }
            finally{selectors.Clear();if(fixedSelectorLists.Count<8 && selectors.Capacity<=1024)fixedSelectorLists.Push(selectors);}
        }
        public static void NativePoiSpawnBegin(){FrameDiagnostics.NativePoiSpawnBegin();}
        public static void NativePoiSpawnEnd(){FrameDiagnostics.NativePoiSpawnEnd();}
        public static void NativePoiStuffBegin(){FrameDiagnostics.NativePoiStuffBegin();}
        public static void NativePoiStuffEnd(){FrameDiagnostics.NativePoiStuffEnd();}
        public static void NativePoiItemsBegin(){FrameDiagnostics.NativePoiItemsBegin();}
        public static void NativePoiItemsEnd(){FrameDiagnostics.NativePoiItemsEnd();}
        public static void NativePoiFixedBegin(){FrameDiagnostics.NativePoiFixedBegin();}
        public static void NativePoiFixedEnd(){FrameDiagnostics.NativePoiFixedEnd();}
        sealed class BreakProbeRow
        {
            internal string Name; internal int Frame,G0,G1,G2; internal long Start;
            internal readonly long[] Stages=new long[15],Starts=new long[15];
        }
        static readonly BreakProbeRow[] breakProbeRows=MakeBreakProbeRows();
        static BreakProbeRow[] MakeBreakProbeRows(){var rows=new BreakProbeRow[8];for(int i=0;i<rows.Length;i++)rows[i]=new BreakProbeRow();return rows;}
        static readonly string[] breakStageNames=new[]{"State","SeatQuery","SeatExit","ComponentQuery","Drop","LegacyDeath","Death","Register","Bury","Audio","ReflectionQuery","ReflectionOff","Slice","Destroy","Instantiate"};
        static int breakProbeDepth,breakProbeFrame=-1,breakLastFrame=-100,breakProbeLogs,breakProbeSuppressed;
        static double breakProbeWindow;
        public static void BreakProbeBegin(breakablescript item)
        {
            if(!ModDiagnostics.On)return;
            try
            {
                int frame=Time.frameCount;
                if(frame!=breakProbeFrame){breakProbeDepth=0;breakProbeSuppressed=0;breakProbeFrame=frame;}
                if(breakProbeDepth==breakProbeRows.Length){breakProbeSuppressed++;return;}
                var row=breakProbeRows[breakProbeDepth++];row.Frame=frame;
                row.Name=item==null?"null":item.name+" slice="+item.sliceOnDestroy+" cuts="+item.sliceCuts+" alreadyDestroyed="+item.destroyed;
                Array.Clear(row.Stages,0,15);Array.Clear(row.Starts,0,15);
                row.G0=GC.CollectionCount(0);row.G1=GC.CollectionCount(1);row.G2=GC.CollectionCount(2);row.Start=Stopwatch.GetTimestamp();
                breakLastFrame=frame;
            }catch { }
        }
        static void BreakProbeStageBegin(int stage)
        {if(ModDiagnostics.On && breakProbeDepth>0 && breakProbeSuppressed==0)breakProbeRows[breakProbeDepth-1].Starts[stage]=Stopwatch.GetTimestamp();}
        static void BreakProbeStageEnd(int stage)
        {if(ModDiagnostics.On && breakProbeDepth>0 && breakProbeSuppressed==0){var row=breakProbeRows[breakProbeDepth-1];long start=row.Starts[stage];if(start!=0){row.Stages[stage]+=Stopwatch.GetTimestamp()-start;row.Starts[stage]=0;}}}
        public static void BreakProbeEnd()
        {
            if(!ModDiagnostics.On)return;
            if(breakProbeSuppressed>0){breakProbeSuppressed--;return;}
            if(breakProbeDepth==0)return;
            var row=breakProbeRows[--breakProbeDepth];long elapsed=Stopwatch.GetTimestamp()-row.Start;
            try
            {
                double now=Time.realtimeSinceStartup;
                if(now<breakProbeWindow || now-breakProbeWindow>=5){breakProbeWindow=now;breakProbeLogs=0;}
                // Keep all slow breaks; cap fast-break details independently.
                bool slow=elapsed*1000.0/Stopwatch.Frequency>=3;
                if(!slow && breakProbeLogs>=8)return;
                if(breakProbeLogs++>=32)return;
                var text=new StringBuilder("[TLD MP break-detail] frame=").Append(row.Frame).Append(" object=").Append(row.Name)
                    .Append(" totalMs=").Append((elapsed*1000.0/Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" gcDelta=").Append(GC.CollectionCount(0)-row.G0).Append('/').Append(GC.CollectionCount(1)-row.G1).Append('/').Append(GC.CollectionCount(2)-row.G2);
                long measured=0;
                for(int i=0;i<15;i++){measured+=row.Stages[i];text.Append(' ').Append(breakStageNames[i]).Append("Ms=").Append((row.Stages[i]*1000.0/Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture));}
                text.Append(" remainderMs=").Append((Math.Max(0,elapsed-measured)*1000.0/Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)).Append(" stages inclusive; limit=32/5s");
                Debug.Log(text.ToString());
            }catch { }
            finally{row.Name=null;}
        }
        public static void BreakStateBegin(){BreakProbeStageBegin(0);}
        public static void BreakStateEnd(){BreakProbeStageEnd(0);}
        public static void BreakSeatQueryBegin(){BreakProbeStageBegin(1);}
        public static void BreakSeatQueryEnd(){BreakProbeStageEnd(1);}
        public static void BreakSeatExitBegin(){BreakProbeStageBegin(2);}
        public static void BreakSeatExitEnd(){BreakProbeStageEnd(2);}
        public static void BreakComponentQueryBegin(){BreakProbeStageBegin(3);}
        public static void BreakComponentQueryEnd(){BreakProbeStageEnd(3);}
        public static void BreakDropBegin(){BreakProbeStageBegin(4);}
        public static void BreakDropEnd(){BreakProbeStageEnd(4);}
        public static void BreakLegacyDeathBegin(){BreakProbeStageBegin(5);}
        public static void BreakLegacyDeathEnd(){BreakProbeStageEnd(5);}
        public static void BreakDeathBegin(){BreakProbeStageBegin(6);}
        public static void BreakDeathEnd(){BreakProbeStageEnd(6);}
        public static void BreakRegisterBegin(){BreakProbeStageBegin(7);}
        public static void BreakRegisterEnd(){BreakProbeStageEnd(7);}
        public static void BreakBuryBegin(){BreakProbeStageBegin(8);}
        public static void BreakBuryEnd(){BreakProbeStageEnd(8);}
        public static void BreakAudioBegin(){BreakProbeStageBegin(9);}
        public static void BreakAudioEnd(){BreakProbeStageEnd(9);}
        public static void BreakReflectionQueryBegin(){BreakProbeStageBegin(10);}
        public static void BreakReflectionQueryEnd(){BreakProbeStageEnd(10);}
        public static void BreakReflectionOffBegin(){BreakProbeStageBegin(11);}
        public static void BreakReflectionOffEnd(){BreakProbeStageEnd(11);}
        public static void BreakSliceBegin(){BreakProbeStageBegin(12);}
        public static void BreakSliceEnd(){BreakProbeStageEnd(12);}
        public static void BreakDestroyBegin(){BreakProbeStageBegin(13);}
        public static void BreakDestroyEnd(){BreakProbeStageEnd(13);}
        static readonly int[] breakPhaseFrames=new int[16];
        static readonly long[,] breakPhaseTicks=new long[16,6];
        static readonly int[,] breakPhaseCalls=new int[16,6];
        static int breakFrameGc0,breakFrameGc1,breakFrameGc2,breakFrameLogs;
        static double breakFrameWindow;
        static bool breakFrameGcStarted;
        internal static void BreakProbePhase(int frame,int phase,long start,long end)
        {
            int index=frame&15;
            if(breakPhaseFrames[index]!=frame){breakPhaseFrames[index]=frame;for(int i=0;i<6;i++){breakPhaseTicks[index,i]=0;breakPhaseCalls[index,i]=0;}}
            breakPhaseTicks[index,phase]+=end-start;breakPhaseCalls[index,phase]++;
        }
        internal static void BreakProbeFrame(int frame)
        {
            try
            {
                int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2),index=frame&15;
                if(!breakFrameGcStarted){breakFrameGc0=g0;breakFrameGc1=g1;breakFrameGc2=g2;breakFrameGcStarted=true;}
                double now=Time.realtimeSinceStartup;
                if(now<breakFrameWindow || now-breakFrameWindow>=5){breakFrameWindow=now;breakFrameLogs=0;}
                bool near=frame>=breakLastFrame && frame-breakLastFrame<=3;
                if(breakFrameLogs<32 && breakPhaseFrames[index]==frame && (near || breakPhaseTicks[index,0]*1000.0/Stopwatch.Frequency>=25 || breakPhaseTicks[index,2]*1000.0/Stopwatch.Frequency>=10))
                {
                    breakFrameLogs++;
                    var text=new StringBuilder("[TLD MP break-frame] frame=").Append(frame).Append(" lastBreakFrame=").Append(breakLastFrame)
                        .Append(" gcSincePreviousFrameComplete=").Append(g0-breakFrameGc0).Append('/').Append(g1-breakFrameGc1).Append('/').Append(g2-breakFrameGc2);
                    for(int i=0;i<6;i++)text.Append(' ').Append(NativeLoopTimings.Labels[i]).Append('=').Append((breakPhaseTicks[index,i]*1000.0/Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)).Append("ms/").Append(breakPhaseCalls[index,i]);
                    Debug.Log(text.ToString());
                }
                breakFrameGc0=g0;breakFrameGc1=g1;breakFrameGc2=g2;
            }catch { }
        }
        public static void AddIgnoredCollider(List<Collider> list,Collider collider)
        {if(!list.Contains(collider))list.Add(collider);}
        public static void SetIgnoredCollider(Collider a,Collider b,bool ignore)
        {
            // Inspect native state: disabling a collider can erase its ignore flags.
            if(a==null || b==null || a==b || Physics.GetIgnoreCollision(a,b)!=ignore)Physics.IgnoreCollision(a,b,ignore);
        }
        // Pool only collision call sites whose native return is discarded. Hearing
        // stimuli retain one emitter per event until saturation; overload replaces
        // a silent emitter instead of allocating unbounded native sound objects.
        const int ImpactCapacity=2048,ImpactVoiceLimit=48,ImpactStartsPerWindow=12;
        sealed class ImpactEmitter
        {
            internal GameObject Object;internal AudioSource Audio;internal stimulscript Stimulus;
            internal AudioClip Clip;internal UnityEngine.Audio.AudioMixerGroup Group;
            internal float End,Volume,Pitch,Min,Max,Score;internal byte Priority;
            internal bool Pending,Voiced;
        }
        static readonly ImpactEmitter[] impactPool=new ImpactEmitter[ImpactCapacity];
        static readonly Stack<int> impactFree=new Stack<int>(ImpactCapacity);
        static readonly List<int> impactActive=new List<int>(ImpactCapacity);
        static mainscript impactOwner;static GameObject impactRoot,impactPrefab;
        static int impactCreated,impactVoices,impactStarts,impactRequests,impactFallback,impactPlayed,impactLimited;
        static float impactWindow,impactReport;static bool impactFailed;
        static int impactReplaced,impactVictimCursor;
        static void ClearImpactPool()
        {
            for(int i=0;i<impactCreated;i++)
            {var e=impactPool[i];if(e!=null && e.Object!=null)UnityEngine.Object.Destroy(e.Object);impactPool[i]=null;}
            if(impactRoot!=null)UnityEngine.Object.Destroy(impactRoot);
            impactFree.Clear();impactActive.Clear();impactCreated=impactVoices=impactStarts=0;
            impactOwner=null;impactPrefab=null;impactRoot=null;
        }
        static void ImpactFailure(Exception error)
        {if(!impactFailed){impactFailed=true;Debug.LogWarning("[TLD MP impact-audio] pool fallback: "+error.GetType().Name+": "+error.Message);}}
        static void ReleaseImpact(int activeIndex)
        {
            int id=impactActive[activeIndex];var e=impactPool[id];
            if(e.Voiced)impactVoices--;
            e.Pending=e.Voiced=false;e.Clip=null;e.Group=null;
            if(e.Object!=null)
            {
                e.Audio.Stop();e.Audio.clip=null;e.Audio.enabled=false;e.Object.SetActive(false);
                if(impactRoot!=null)e.Object.transform.SetParent(impactRoot.transform,false);
                impactFree.Push(id);
            }
            int last=impactActive.Count-1;impactActive[activeIndex]=impactActive[last];impactActive.RemoveAt(last);
        }
        static void TickImpactAudio(bool warm,bool flush)
        {
            try
            {
                if(impactOwner!=mainscript.s || (impactOwner!=null && impactPrefab!=impactOwner.Audio))ClearImpactPool();
                if(warm && impactOwner==null && mainscript.s!=null && mainscript.s.Audio!=null)
                {
                    impactOwner=mainscript.s;impactPrefab=impactOwner.Audio;
                    impactRoot=new GameObject("Roadworthy inactive impact pool");impactRoot.SetActive(false);
                    impactRoot.transform.SetParent(impactOwner.transform,false);
                    impactWindow=Time.unscaledTime;impactReport=Time.unscaledTime;
                }
                float now=Time.time;
                for(int i=impactActive.Count-1;i>=0;i--)
                {var e=impactPool[impactActive[i]];if(e.Object==null || now>=e.End)ReleaseImpact(i);}
                if(warm && impactRoot!=null && impactCreated<ImpactCapacity)
                {
                    long start=Stopwatch.GetTimestamp();
                    for(int count=0;count<8 && impactCreated<ImpactCapacity;count++)
                    {
                        GameObject obj=UnityEngine.Object.Instantiate(impactPrefab,impactRoot.transform,false);
                        try
                        {
                            var audio=obj.GetComponent<AudioSource>();var stimulus=obj.GetComponent<stimulscript>();
                            if(audio==null || stimulus==null || !stimulus.DontStartCheckingAudioSources)throw new InvalidOperationException("Unexpected impact prefab");
                            obj.SetActive(false);audio.enabled=false;
                            impactPool[impactCreated]=new ImpactEmitter{Object=obj,Audio=audio,Stimulus=stimulus,Min=audio.minDistance,Max=audio.maxDistance};
                            impactFree.Push(impactCreated++);
                        }
                        catch{UnityEngine.Object.Destroy(obj);throw;}
                        if((Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency>=1)break;
                    }
                }
                if(flush)
                {
                    float clock=Time.unscaledTime;
                    if(clock<impactWindow || clock-impactWindow>=0.05f){impactWindow=clock;impactStarts=0;}
                    while(impactStarts<ImpactStartsPerWindow && impactVoices<ImpactVoiceLimit)
                    {
                        ImpactEmitter best=null;
                        for(int i=0;i<impactActive.Count;i++)
                        {var e=impactPool[impactActive[i]];if(e.Pending && (best==null || e.Score>best.Score))best=e;}
                        if(best==null)break;
                        best.Pending=false;
                        var audio=best.Audio;
                        audio.priority=best.Priority;audio.outputAudioMixerGroup=best.Group;audio.clip=best.Clip;
                        audio.minDistance=best.Min;audio.maxDistance=best.Max;
                        if(best.Volume>1){audio.volume=1;audio.minDistance*=best.Volume-1;audio.maxDistance*=best.Volume-1;}
                        else audio.volume=best.Volume;
                        audio.pitch=best.Pitch;audio.spatialBlend=1;audio.enabled=true;audio.Play();
                        best.Voiced=true;impactVoices++;impactStarts++;impactPlayed++;
                    }
                    // No delayed sound backlog: unselected emitters remain AI stimuli.
                    for(int i=0;i<impactActive.Count;i++)
                    {var e=impactPool[impactActive[i]];if(e.Pending){e.Pending=false;impactLimited++;}}
                }
                if(warm && ModDiagnostics.On && Time.unscaledTime-impactReport>=5)
                {
                    impactReport=Time.unscaledTime;
                    Debug.Log("[TLD MP impact-audio] requests="+impactRequests+" nativeFallback="+impactFallback+" played="+impactPlayed+" budgeted="+impactLimited+" replacedSilent="+impactReplaced+" emitters="+impactActive.Count+" voices="+impactVoices+" warmed="+impactCreated+" capacity="+ImpactCapacity+" voiceLimit="+ImpactVoiceLimit+" startsPer50ms="+ImpactStartsPerWindow+" stimuli=bounded-overload");
                    impactRequests=impactFallback=impactPlayed=impactLimited=impactReplaced=0;
                }
            }
            catch(Exception e){ImpactFailure(e);}
        }
        static GameObject PlayImpact(AudioClip clip,Vector3 position,float volume,float pitch,UnityEngine.Audio.AudioMixerGroup group,byte priority)
        {
            impactRequests++;
            if(clip!=null && impactOwner!=null && impactOwner==mainscript.s && impactPrefab==impactOwner.Audio && impactFree.Count==0 && impactActive.Count>ImpactVoiceLimit && !float.IsNaN(volume) && !float.IsInfinity(volume))
            {
                // At most 48 emitters can be voiced: 49 inspected entries must
                // include a silent one. Work stays bounded even during catch-up.
                int victim=-1;float score=float.PositiveInfinity;
                for(int n=0;n<=ImpactVoiceLimit;n++)
                {
                    int index=(impactVictimCursor+n)%impactActive.Count;
                    var candidate=impactPool[impactActive[index]];
                    if(!candidate.Voiced && candidate.Score<score){victim=index;score=candidate.Score;}
                }
                if(victim>=0){ReleaseImpact(victim);impactReplaced++;impactVictimCursor=(victim+1)%impactActive.Count;}
            }
            if(clip!=null && impactOwner!=null && impactOwner==mainscript.s && impactPrefab==impactOwner.Audio && impactFree.Count>0 && !float.IsNaN(volume) && !float.IsInfinity(volume))
            {
                int id=impactFree.Pop();var e=impactPool[id];
                try
                {
                    if(e.Object==null)throw new InvalidOperationException("Destroyed pooled emitter");
                    e.Object.transform.SetParent(null,false);e.Object.transform.SetPositionAndRotation(position,Quaternion.identity);
                    e.Object.transform.localScale=impactPrefab.transform.localScale;
                    e.Clip=clip;e.Group=group;e.Volume=volume;e.Pitch=pitch;e.Priority=priority;
                    e.End=Time.time+clip.length;e.Pending=true;e.Voiced=false;
                    float distance=0;
                    if(impactOwner.player!=null)distance=(position-impactOwner.player.transform.position).sqrMagnitude;
                    e.Score=volume/(1+0.01f*distance);
                    e.Stimulus.SetAudioValue(volume);e.Object.SetActive(true);impactActive.Add(id);
                    return e.Object;
                }
                catch(Exception error){if(e.Object!=null)UnityEngine.Object.Destroy(e.Object);ImpactFailure(error);}
            }
            // Cold-start/invalid-prefab fallback retains native behavior.
            impactFallback++;
            return mainscript.PlayClipAtPoint(clip,position,volume,pitch,group,1f,priority);
        }
        static long npcVehicleImpactCount,npcVehicleImpactNext;
        public static void ConfigureRabbitImpact(ragdollactivatescript ragdoll){NpcVehicleImpact.ConfigureRabbit(ragdoll);RabbitReplicas.EndMutation(ragdoll);RabbitContact.ObserveRabbit(ragdoll==null?null:ragdoll.GetComponent<nyulscript>());}
        public static bool RabbitImpactGate(breakablescript victim,Collision collision){return NpcVehicleImpact.AdmitRabbitCollision(victim,collision);}
        public static Vector3 NpcVehicleVelocity(Collision collision,breakablescript victim)
        {
            Vector3 original=collision.relativeVelocity;
            Vector3 result=NpcVehicleImpact.RelativeVelocity(collision,victim);
            if(result.sqrMagnitude>original.sqrMagnitude)
            {
                npcVehicleImpactCount++;
                long now=DateTime.UtcNow.Ticks;
                if(now>=npcVehicleImpactNext)
                {
                    npcVehicleImpactNext=now+TimeSpan.TicksPerSecond*2;
                    Debug.Log("[Roadworthy NPC impact] cumulative_scaled="+npcVehicleImpactCount+" native_speed="+original.magnitude.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" adjusted_speed="+result.magnitude.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return result;
        }
        public static int NpcImpactLayer(GameObject hit)
        {
            int layer=hit.layer;
            if(layer==15 || layer==17)
            {
                if(hit.GetComponentInParent<newAiScript>()!=null || hit.GetComponentInParent<aiscript>()!=null)return layer;
                if(hit.GetComponentInParent<carscript>()!=null || hit.GetComponentInParent<fpscontroller>()!=null)return 0;
            }
            return layer;
        }
        public static bool HasCollisionContacts(Collision collision)
        {return collision!=null && collision.contactCount>0;}
        public static GameObject PickupImpactSound(AudioClip clip,Vector3 position,float volume,byte priority)
        {return PlayImpact(clip,position,volume,1,mainscript.s.defGroup,priority);}
        public static GameObject CarImpactSound(AudioClip clip,Vector3 position,float volume,bool randomPitch,UnityEngine.Audio.AudioMixerGroup group,byte priority)
        {
            float pitch=randomPitch?UnityEngine.Random.Range(0.85f,1.15f):1;
            return PlayImpact(clip,position,volume,pitch,group,priority);
        }
        // Only sibling colliders from one audited crate break are delayed. External
        // contacts remain enabled. A global queue bounds both work and retained pairs.
        struct CrateContact
        {
            internal Collider A,B;
            internal float Ready,Deadline;
        }
        static readonly List<CrateContact> crateContacts=new List<CrateContact>(8192);
        static readonly List<Collider> crateColliderScratch=new List<Collider>(64);
        static int crateContactCursor;
        static bool crateContactFailed;
        static void PrepareCrateDebris(GameObject group)
        {
            if(group==null || (group.name!="crateGib(Clone)" && group.name!="R_crateGib(Clone)"))return;
            long started=Stopwatch.GetTimestamp();
            try
            {
                group.GetComponentsInChildren<Collider>(false,crateColliderScratch);
                int count=crateColliderScratch.Count;
                // Reject unexpected prefab layouts before changing any pair.
                if(count<2 || count>64 || crateContacts.Count+count*(count-1)/2>8192)return;
                for(int i=0;i<count;i++)
                {
                    Collider col=crateColliderScratch[i];
                    if(!(col is BoxCollider) || col.attachedRigidbody==null)return;
                    var item=col.attachedRigidbody.GetComponent<tosaveitemscript>();
                    if(item==null || item.id<280 || item.id>291 || item.car!=null || item.attachable!=null || item.attachgrip!=null)return;
                }
                float ready=Time.fixedTime+2*Time.fixedDeltaTime,deadline=Time.fixedTime+1;
                int added=0;
                for(int i=0;i<count;i++)for(int j=i+1;j<count;j++)
                {
                    Collider a=crateColliderScratch[i],b=crateColliderScratch[j];
                    if(a==null || b==null || !a.enabled || !b.enabled || a.isTrigger || b.isTrigger ||
                        a.attachedRigidbody==null || b.attachedRigidbody==null || a.attachedRigidbody==b.attachedRigidbody || Physics.GetIgnoreCollision(a,b))continue;
                    // Add first: if the native call throws, cleanup still owns the pair.
                    crateContacts.Add(new CrateContact{A=a,B=b,Ready=ready,Deadline=deadline});
                    Physics.IgnoreCollision(a,b,true);added++;
                }
                if(ModDiagnostics.On)Debug.Log("[TLD MP crate-contacts] colliders="+count+" deferredPairs="+added+" queued="+crateContacts.Count+" setupMs="+((Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" graceSteps=2 deadlineSeconds=1 checksPerStep=256 restoresPerStep=128 overlapRestoresPerStep=8");
            }
            catch(Exception e){CrateContactFailure(e);}
            finally{crateColliderScratch.Clear();}
        }
        static void CrateContactFailure(Exception e)
        {if(!crateContactFailed){crateContactFailed=true;Debug.LogWarning("[TLD MP crate-contacts] "+e.GetType().Name+": "+e.Message);}}
        static bool CratePairSeparated(Collider a,Collider b)
        {
            if(!a.enabled || !b.enabled || !a.gameObject.activeInHierarchy || !b.gameObject.activeInHierarchy)return true;
            if(!a.bounds.Intersects(b.bounds))return true;
            Vector3 direction;float distance;
            return !Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out direction,out distance);
        }
        static void TickCrateContacts()
        {
            int checks=Math.Min(256,crateContacts.Count),restores=0,overlapRestores=0;
            float now=Time.fixedTime;
            while(checks-->0 && crateContacts.Count>0 && restores<128)
            {
                if(crateContactCursor>=crateContacts.Count)crateContactCursor=0;
                CrateContact pair=crateContacts[crateContactCursor];bool remove=false;
                try
                {
                    if(pair.A==null || pair.B==null)remove=true;
                    else if(now>=pair.Ready)
                    {
                        bool separated=CratePairSeparated(pair.A,pair.B);
                        if(separated || (now>=pair.Deadline && overlapRestores<8))
                        {
                            Physics.IgnoreCollision(pair.A,pair.B,false);restores++;remove=true;
                            if(!separated)overlapRestores++;
                        }
                    }
                }
                catch(Exception e){CrateContactFailure(e);}
                if(remove){int last=crateContacts.Count-1;crateContacts[crateContactCursor]=crateContacts[last];crateContacts.RemoveAt(last);}
                else crateContactCursor++;
            }
        }
        static void ClearCrateContacts()
        {
            for(int i=0;i<crateContacts.Count;i++)
            {var pair=crateContacts[i];try{if(pair.A!=null && pair.B!=null)Physics.IgnoreCollision(pair.A,pair.B,false);}catch(Exception e){CrateContactFailure(e);}}
            crateContacts.Clear();crateContactCursor=0;
        }
        // The native slicer only reads these arrays until SliceMesh returns. Refill
        // reusable exact-length buffers on every call, including mutable meshes.
        static readonly List<Vector3> sliceVertexList=new List<Vector3>();
        static readonly List<Vector2> sliceUvList=new List<Vector2>();
        static readonly List<int> sliceIndexList=new List<int>();
        static readonly Dictionary<int,Vector3[]> sliceVertexBuffers=new Dictionary<int,Vector3[]>();
        static readonly Dictionary<int,Vector2[]> sliceUvBuffers=new Dictionary<int,Vector2[]>();
        static readonly Dictionary<int,int[]> sliceIndexBuffers=new Dictionary<int,int[]>();
        static T[] SliceBuffer<T>(List<T> source,Dictionary<int,T[]> buffers)
        {
            T[] result;int count=source.Count;
            if(!buffers.TryGetValue(count,out result))
            {
                result=new T[count];
                if(count<=32768 && buffers.Count<8)buffers.Add(count,result);
            }
            source.CopyTo(result);
            if(source.Capacity>65536){source.Clear();source.Capacity=0;}
            return result;
        }
        public static Vector3[] SliceVertices(Mesh mesh)
        {if(mesh==null)return mesh.vertices;mesh.GetVertices(sliceVertexList);return SliceBuffer(sliceVertexList,sliceVertexBuffers);}
        public static Vector2[] SliceUv(Mesh mesh)
        {if(mesh==null)return mesh.uv;mesh.GetUVs(0,sliceUvList);return SliceBuffer(sliceUvList,sliceUvBuffers);}
        public static int[] SliceTriangles(Mesh mesh)
        {
            if(mesh==null || mesh.subMeshCount!=1)return mesh.triangles;
            mesh.GetTriangles(sliceIndexList,0,true);return SliceBuffer(sliceIndexList,sliceIndexBuffers);
        }
        static readonly List<float> sliceSortKeys=new List<float>(16);
        public static List<Mesh> SortSliceMeshes(List<Mesh> meshes)
        {
            sliceSortKeys.Clear();
            try
            {
                for(int i=0;i<meshes.Count;i++)sliceSortKeys.Add(meshes[i].bounds.size.magnitude);
                // Stable insertion sort: native OrderBy keeps ties in input order.
                for(int i=1;i<meshes.Count;i++)
                {
                    Mesh mesh=meshes[i];float key=sliceSortKeys[i];int j=i;
                    while(j>0 && Comparer<float>.Default.Compare(sliceSortKeys[j-1],key)>0)
                    {meshes[j]=meshes[j-1];sliceSortKeys[j]=sliceSortKeys[j-1];j--;}
                    meshes[j]=mesh;sliceSortKeys[j]=key;
                }
                return meshes;
            }
            finally{sliceSortKeys.Clear();}
        }
        static readonly Dictionary<Vector3,Vector3> slicePoints=new Dictionary<Vector3,Vector3>();
        static readonly Dictionary<Vector3,float> sliceSides=new Dictionary<Vector3,float>();
        static bool slicePointsActive;
        static int slicePointTransform;
        static readonly HashSet<ulong> sliceCollisionPairs=new HashSet<ulong>();
        static bool slicePairsActive;
        public static void SliceGeometryBegin(){sliceSides.Clear();slicePoints.Clear();slicePointsActive=true;slicePointTransform=0;}
        public static void SliceGeometryEnd(){slicePointsActive=false;sliceSides.Clear();slicePoints.Clear();slicePointTransform=0;}
        public static float SlicePlaneSide(ref MeshSlicer.CustomPlane plane,Vector3 point)
        {
            if(!slicePointsActive)return plane.GetSide(point);
            float side;
            if(sliceSides.TryGetValue(point,out side))return side;
            side=plane.GetSide(point);
            if(sliceSides.Count<8192)sliceSides.Add(point,side);
            return side;
        }
        public static Vector3 SliceTransformPoint(Transform transform,Vector3 point)
        {
            if(!slicePointsActive || transform==null)return transform.TransformPoint(point);
            int id=transform.GetInstanceID();
            if(slicePointTransform==0)slicePointTransform=id;
            if(id!=slicePointTransform)return transform.TransformPoint(point);
            Vector3 result;
            if(slicePoints.TryGetValue(point,out result))return result;
            result=transform.TransformPoint(point);
            if(slicePoints.Count<8192)slicePoints.Add(point,result);
            return result;
        }
        public static void SlicePairsBegin(){sliceCollisionPairs.Clear();slicePairsActive=true;}
        public static void SlicePairsEnd(){slicePairsActive=false;sliceCollisionPairs.Clear();}
        public static void SliceIgnoreCollision(Collider a,Collider b)
        {
            if(!slicePairsActive || a==null || b==null || a==b){Physics.IgnoreCollision(a,b);return;}
            uint first=unchecked((uint)a.GetInstanceID()),second=unchecked((uint)b.GetInstanceID());
            ulong key=first<second?((ulong)first<<32)|second:((ulong)second<<32)|first;
            if(sliceCollisionPairs.Contains(key))return;
            FrameDiagnostics.NativeBegin(130);
            try{Physics.IgnoreCollision(a,b);}
            finally{FrameDiagnostics.NativeEnd(130);}
            if(sliceCollisionPairs.Count<32768)sliceCollisionPairs.Add(key);
        }
        public static void DebrisNormalsBegin(){FrameDiagnostics.NativeBegin(126);}
        public static void DebrisNormalsEnd(){FrameDiagnostics.NativeEnd(126);}
        public static void DebrisBoundsBegin(){FrameDiagnostics.NativeBegin(127);}
        public static void DebrisBoundsEnd(){FrameDiagnostics.NativeEnd(127);}
        public static void DebrisConvexBegin(){FrameDiagnostics.NativeBegin(128);}
        public static void DebrisConvexEnd(){FrameDiagnostics.NativeEnd(128);}
        public static void DebrisColliderMeshBegin(){FrameDiagnostics.NativeBegin(129);}
        public static void DebrisColliderMeshEnd(){FrameDiagnostics.NativeEnd(129);}
        public static void DebrisComponentsBegin(){FrameDiagnostics.NativeBegin(131);}
        public static void DebrisComponentsEnd(){FrameDiagnostics.NativeEnd(131);}
        // Captured once per process so physics mode and the session fingerprint cannot diverge.
        static readonly bool debrisSpeculative=ReadDebrisCcdMode();
        static bool debrisCcdLogged,debrisCcdFailed;
        static readonly Stack<List<tosaveitemscript>> debrisItemScratch=new Stack<List<tosaveitemscript>>();
        static bool ReadDebrisCcdMode()
        {
            try
            {
                string managed=Path.GetDirectoryName(typeof(StableGameBridge).Assembly.Location);
                return File.Exists(Path.Combine(Directory.GetParent(managed).Parent.FullName,"tldmp-debris-speculative.on"));
            }catch{return false;}
        }
        internal static bool IsComparisonDebris(int prefabId)
        {return prefabId==69 || (prefabId>=280 && prefabId<=291);}
        internal static bool IsSolverDebris(int id)
        {return id==67 || id==68 || id==69 || (id>=203 && id<=210) || (id>=280 && id<=291) || (id>=322 && id<=332);}
        static void SetDebrisSolver(Rigidbody body)
        {
            if(body==null)return;
            if(body.solverIterations!=8)body.solverIterations=8;
            if(body.solverVelocityIterations!=2)body.solverVelocityIterations=2;
        }
        public static void DebrisItemInitialized(tosaveitemscript item)
        {
            if(item==null || !IsSolverDebris(item.id) || item.car!=null || item.attachable!=null || item.attachgrip!=null)return;
            try
            {
                Rigidbody body=item.RB;
                if(body==null)body=item.GetComponent<Rigidbody>();
                SetDebrisSolver(body);
                DebrisContactFilter.Register(body);
                if(debrisSpeculative && IsComparisonDebris(item.id))SetComparisonDebrisMode(body);
            }catch(Exception e){DebrisCcdFailure(e);}
        }
        static void SetComparisonDebrisMode(Rigidbody body)
        {
            if(body==null || body.collisionDetectionMode!=CollisionDetectionMode.ContinuousDynamic)return;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            if(!debrisCcdLogged){debrisCcdLogged=true;Debug.Log("[TLD MP debris-ccd] comparison=speculative; cactus/crate fragments and runtime slices only; all peers require matching mode; restart to switch.");}
        }
        static void DebrisCcdFailure(Exception error)
        {if(!debrisCcdFailed){debrisCcdFailed=true;Debug.LogWarning("[TLD MP debris-ccd] comparison setup failed; "+error.GetType().Name+": "+error.Message);}}
        static GameObject CreateBreakDebris(GameObject prefab,Vector3 position,Quaternion rotation)
        {
            GameObject result=UnityEngine.Object.Instantiate(prefab,position,rotation);
            // Pair exclusions replace the former sibling-contact restoration queue.
            if(result==null)return result;
            List<tosaveitemscript> items=debrisItemScratch.Count==0?new List<tosaveitemscript>():debrisItemScratch.Pop();
            try
            {
                result.GetComponentsInChildren<tosaveitemscript>(true,items);
                for(int i=0;i<items.Count;i++)DebrisItemInitialized(items[i]);
            }
            catch(Exception e){DebrisCcdFailure(e);}
            finally{items.Clear();if(debrisItemScratch.Count<4)debrisItemScratch.Push(items);}
            return result;
        }
        public static void SetSliceCollisionMode(Rigidbody body,CollisionDetectionMode requested)
        {
            SetDebrisSolver(body);
            DebrisContactFilter.Register(body);
            body.collisionDetectionMode=debrisSpeculative && requested==CollisionDetectionMode.ContinuousDynamic?CollisionDetectionMode.ContinuousSpeculative:requested;
        }
        static double debrisLogWindow;
        // Bounded pair exclusions run on the main thread before simulation.
        static class DebrisContactFilter
        {
            const int Capacity=256,ChecksPerFrame=2048;
            struct Job{internal int A,B,GA,GB;}
            sealed class Entry{internal Collider Col;internal Rigidbody Body;internal int Generation;internal bool Active;}
            static readonly Entry[] entries=new Entry[Capacity];
            static readonly bool[,] owned=new bool[Capacity,Capacity],queued=new bool[Capacity,Capacity];
            static readonly Queue<Job> jobs=new Queue<Job>(Capacity*Capacity/2);
            static readonly HashSet<Rigidbody> pending=new HashSet<Rigidbody>();
            static readonly List<Collider> scratch=new List<Collider>();
            static int frame=-1,checks,changed,overflow;static long spent;static float report;
            static bool failed;
            internal static void Register(Rigidbody body){if(body!=null && pending.Count<Capacity)pending.Add(body);}
            static bool Active(Entry e){return e!=null && e.Col!=null && e.Col.enabled && !e.Col.isTrigger && e.Col.gameObject.activeInHierarchy && e.Body!=null && e.Col.attachedRigidbody==e.Body && !e.Body.isKinematic;}
            static void Row(int slot)
            {
                for(int other=0;other<Capacity;other++)
                {
                    if(other==slot || entries[other]==null)continue;
                    int a=Math.Min(slot,other),b=Math.Max(slot,other);
                    if(queued[a,b])continue;
                    queued[a,b]=true;jobs.Enqueue(new Job{A=a,B=b,GA=entries[a].Generation,GB=entries[b].Generation});
                }
            }
            static void Add(Collider col,Rigidbody body)
            {
                if(col==null || col.isTrigger || col.attachedRigidbody!=body)return;
                int free=-1;
                for(int i=0;i<Capacity;i++)
                {
                    if(entries[i]!=null && object.ReferenceEquals(entries[i].Col,col))return;
                    if(free<0 && (entries[i]==null || entries[i].Col==null))free=i;
                }
                if(free<0 || jobs.Count>=Capacity*Capacity/2){overflow++;return;}
                int generation=entries[free]==null?1:entries[free].Generation+1;
                // The previous collider is destroyed: PhysX already discarded its pairs.
                for(int i=0;i<Capacity;i++){owned[Math.Min(i,free),Math.Max(i,free)]=false;queued[Math.Min(i,free),Math.Max(i,free)]=false;}
                entries[free]=new Entry{Col=col,Body=body,Generation=generation};
                entries[free].Active=Active(entries[free]);Row(free);
            }
            internal static void Tick()
            {
                if(failed)return;
                try
                {
                    foreach(var body in pending)
                    {if(body==null)continue;scratch.Clear();body.GetComponentsInChildren<Collider>(true,scratch);foreach(var col in scratch)Add(col,body);}
                    pending.Clear();scratch.Clear();
                    for(int i=0;i<Capacity;i++)
                    {var e=entries[i];if(e==null)continue;bool active=Active(e);if(active!=e.Active){e.Active=active;Row(i);}}
                    if(frame!=Time.frameCount){frame=Time.frameCount;checks=0;spent=0;}
                    long start=Stopwatch.GetTimestamp();
                    while(jobs.Count>0 && checks<ChecksPerFrame && (spent+Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency<2)
                    {
                        var job=jobs.Dequeue();checks++;
                        var a=entries[job.A];var b=entries[job.B];
                        if(a==null || b==null || a.Generation!=job.GA || b.Generation!=job.GB)continue;
                        queued[job.A,job.B]=false;
                        bool valid=a.Col!=null && b.Col!=null;
                        bool desired=valid && a.Active && b.Active && a.Body!=b.Body;
                        if(valid)
                        {
                            bool ignored=Physics.GetIgnoreCollision(a.Col,b.Col);
                            if(desired && !ignored){owned[job.A,job.B]=true;Physics.IgnoreCollision(a.Col,b.Col,true);changed++;}
                            else if(!desired && owned[job.A,job.B]){if(ignored)Physics.IgnoreCollision(a.Col,b.Col,false);owned[job.A,job.B]=false;}
                        }
                        else owned[job.A,job.B]=false;
                    }
                    spent+=Stopwatch.GetTimestamp()-start;
                    if(ModDiagnostics.On && Time.unscaledTime-report>=5)
                    {report=Time.unscaledTime;Debug.Log("[TLD MP debris-pairs] excluded="+changed+" queued="+jobs.Count+" capacitySkips="+overflow+" checksPerFrame="+ChecksPerFrame+" softBudgetMs=2 policy=debris-only");changed=overflow=0;}
                }
                catch(Exception e){Clear();failed=true;Debug.LogWarning("[TLD MP debris-pairs] native fallback: "+e.GetType().Name+": "+e.Message);}
            }
            internal static void Clear()
            {
                for(int a=0;a<Capacity;a++)for(int b=a+1;b<Capacity;b++)
                {
                    if(owned[a,b] && entries[a]!=null && entries[b]!=null && entries[a].Col!=null && entries[b].Col!=null)Physics.IgnoreCollision(entries[a].Col,entries[b].Col,false);
                    owned[a,b]=queued[a,b]=false;
                }
                Array.Clear(entries,0,entries.Length);jobs.Clear();pending.Clear();scratch.Clear();frame=-1;checks=0;spent=0;
            }
        }
        // End debris contact filter.
        public static GameObject BuildBoxSlice(GameObject original,Transform originalTransform,Mesh mesh)
        {
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            var fragment=new GameObject();
            fragment.transform.SetPositionAndRotation(originalTransform.position,originalTransform.rotation);
            fragment.transform.localScale=original.transform.lossyScale;
            fragment.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=fragment.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.sharedMaterial=original.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            fragment.AddComponent<randomSlice>().dedestroy=true;
            var box=fragment.AddComponent<BoxCollider>();
            var bounds=mesh.bounds;box.center=bounds.center;
            var size=bounds.size;
            box.size=new Vector3(Mathf.Max(size.x,0.001f),Mathf.Max(size.y,0.001f),Mathf.Max(size.z,0.001f));
            return fragment;
        }
        static int debrisLogCount;
        public static GameObject InstantiateBreakDebris(GameObject prefab,Vector3 position,Quaternion rotation)
        {
            if(!ModDiagnostics.On)return CreateBreakDebris(prefab,position,rotation);
            int frame=Time.frameCount;
            int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
            long start=Stopwatch.GetTimestamp();bool completed=false;
            FrameDiagnostics.NativeBegin(125);
            BreakProbeStageBegin(14);
            try
            {
                GameObject result=CreateBreakDebris(prefab,position,rotation);
                completed=true;return result;
            }
            finally
            {
                long end=Stopwatch.GetTimestamp();
                BreakProbeStageEnd(14);
                FrameDiagnostics.NativeEnd(125);
                // Diagnostics must preserve the native return and exception behavior.
                try
                {
                    double ms=(end-start)*1000.0/Stopwatch.Frequency;
                    int d0=GC.CollectionCount(0)-gc0,d1=GC.CollectionCount(1)-gc1,d2=GC.CollectionCount(2)-gc2;
                    if(ms>=3 || d0!=0 || d1!=0 || d2!=0 || !completed)
                    {
                        double now=Time.realtimeSinceStartup;
                        if(now<debrisLogWindow || now-debrisLogWindow>=5){debrisLogWindow=now;debrisLogCount=0;}
                        if(debrisLogCount++<16)Debug.Log("[TLD MP debris-spike] t="+now.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+
                            " frame="+frame+" prefab="+(prefab==null?"null":prefab.name)+" instantiateMs="+ms.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+
                            " gcDelta="+d0+"/"+d1+"/"+d2+" completed="+completed+" at="+position+" limit=16/5s");
                    }
                }
                catch { }
            }
        }
        public static void NativePoiFixedInstantiateBegin(){FrameDiagnostics.NativePoiFixedInstantiateBegin();}
        public static void NativePoiFixedInstantiateEnd(){FrameDiagnostics.NativePoiFixedInstantiateEnd();}
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
        // Fresh hierarchy query on every call, but reuse the backing list. Nested
        // calls get a separate list so the native summation order stays intact.

        static readonly System.Reflection.FieldInfo storageSensed=typeof(mountStuff).GetField("lastSensed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        static readonly System.Reflection.FieldInfo storageDropTime=typeof(mountStuff).GetField("neededDropTime",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        static readonly System.Reflection.FieldInfo storageParent=typeof(mountStuff).GetField("colParentTransform",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        static readonly Stack<Collider[]> storageBuffers=new Stack<Collider[]>();
        static readonly Stack<HashSet<int>> storageSets=new Stack<HashSet<int>>();
        static bool StorageContains(Collider[] previous,HashSet<int> membership,Collider collider)
        {
            if(collider!=null)return membership.Contains(collider.GetInstanceID());
            foreach(var old in previous)if(collider==old)return true;
            return false;
        }
        public static bool StorageScanGate(mountStuff mount)
        {
            if(storageParent==null)return true;
            var parent=(Transform)storageParent.GetValue(mount);
            var col=mount.col;
            var center=parent.position+parent.TransformDirection(col.transform.localPosition+col.center);
            var size=new Vector3(col.size.x*col.transform.localScale.x,col.size.y*col.transform.localScale.y,col.size.z*col.transform.localScale.z)*.5f;
            var buffer=storageBuffers.Count==0?new Collider[64]:storageBuffers.Pop();
            try
            {
                int count;
                while(true)
                {
                    count=Physics.OverlapBoxNonAlloc(center,size,buffer,col.transform.rotation,mainscript.s.mountLayerMask);
                    if(count<buffer.Length)break;
                    if(buffer.Length>=4096){mount.cols=Physics.OverlapBox(center,size,col.transform.rotation,mainscript.s.mountLayerMask);return false;}
                    buffer=new Collider[buffer.Length*2];
                }
                // Reuse only a byte-for-byte identical snapshot. A changed
                // result gets its own exact-length array, preserving lastcols.
                bool same=mount.cols!=null && mount.cols.Length==count;
                for(int i=0;same && i<count;i++)same=object.ReferenceEquals(mount.cols[i],buffer[i]);
                if(!same){var snapshot=new Collider[count];Array.Copy(buffer,snapshot,count);mount.cols=snapshot;}
                return false;
            }
            finally{Array.Clear(buffer,0,buffer.Length);if(storageBuffers.Count<4)storageBuffers.Push(buffer);}
        }
        public static bool StorageFreezeGate(mountStuff mount)
        {
            if(storageSensed==null || storageDropTime==null)return true;

		storageSensed.SetValue(mount,Time.time);
		mount.lastcols = mount.cols;
		mount.Scan();
            var previous=mount.lastcols;
            var membership=storageSets.Count==0?new HashSet<int>():storageSets.Pop();
            try
            {
            if(previous!=null)foreach(var c in previous)if(c!=null)membership.Add(c.GetInstanceID());
		for (int i = 0; i < mount.cols.Length; i++)
		{
            bool flag=StorageContains(previous,membership,mount.cols[i]);
			if (!flag || (!(mount.cols[i].transform.root != mount.transform.root) && !(mount.cols[i].GetComponentInParent<tosaveitemscript>() != mount.parentTosave)))
			{
				continue;
			}
			if (mount.sType == mountStuff.storageType.trunk)
			{
				carscript componentInParent = mount.cols[i].GetComponentInParent<carscript>();
				if (componentInParent != null && componentInParent != mount.GetComponentInParent<carscript>())
				{
					componentInParent.Lock(_lock: true);
					if (!mount.storedcars.Contains(componentInParent))
					{
						mount.storedcars.Add(componentInParent);
					}
				}
			}
			layerScript component = mount.cols[i].GetComponent<layerScript>();
			pickupable pickupable2;
			if (component != null)
			{
				pickupable2 = component.p;
				if (pickupable2 == null)
				{
					pickupable2 = component.GetComponentInParent<pickupable>();
					pickupable2 = component.p;
				}
			}
			else
			{
				pickupable2 = mount.cols[i].GetComponent<pickupable>();
			}
			if (pickupable2 != null && pickupable2.tosave != null && !pickupable2.dontMount && !pickupable2.pickedUp && pickupable2.physlock == null && !pickupable2.CheckDontMountTillPickup() && (pickupable2.tosave.attachable == null || (pickupable2.tosave.attachable != null && !pickupable2.crafted)) && (pickupable2.tosave.attachable == null || (pickupable2.tosave.attachable != null && !pickupable2.tosave.attachable.attached)) && (!syncScript.IsConnected() || (syncScript.IsConnected() && pickupable2.tosave != null && !pickupable2.tosave.mppickedup && !pickupable2.tosave.mpequipped && !pickupable2.tosave.mpstored && !pickupable2.tosave.mpweared)) && Time.time - pickupable2.DroppedTime >= (float)storageDropTime.GetValue(null))
			{
				AutoStoreGate(mount,pickupable2,false);
			}
		}
	
            return false;
            }
            finally{membership.Clear();if(storageSets.Count<4)storageSets.Push(membership);}
        }
        static readonly Stack<List<massScript>> massLists = new Stack<List<massScript>>();
        public static void WheelRadiusIfChanged(WheelCollider wheel,float value){if(wheel.radius!=value)wheel.radius=value;}
        public static void WheelMassIfChanged(WheelCollider wheel,float value){if(wheel.mass!=value)wheel.mass=value;}
        static bool SameWheelFriction(WheelFrictionCurve a,WheelFrictionCurve b)
        {return a.extremumSlip==b.extremumSlip && a.extremumValue==b.extremumValue && a.asymptoteSlip==b.asymptoteSlip && a.asymptoteValue==b.asymptoteValue && a.stiffness==b.stiffness;}
        public static void WheelForwardIfChanged(WheelCollider wheel,WheelFrictionCurve value){if(!SameWheelFriction(wheel.forwardFriction,value))wheel.forwardFriction=value;}
        public static void WheelSidewaysIfChanged(WheelCollider wheel,WheelFrictionCurve value){if(!SameWheelFriction(wheel.sidewaysFriction,value))wheel.sidewaysFriction=value;}
        public static float MassWithoutArray(massScript item)
        {
            var list=massLists.Count==0?new List<massScript>():massLists.Pop();
            try
            {
                item.GetComponentsInChildren<massScript>(false,list);
                float total=0f;
                for(int i=0;i<list.Count;i++)total+=list[i].OwnPlusMass();
                return total;
            }
            finally{list.Clear();if(massLists.Count<8)massLists.Push(list);}
        }
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
        internal static long DiagnosticStageBegin(){return UpdateCoverage.Begin();}
        internal static void DiagnosticStageEnd(string name,long start){UpdateCoverage.End(name,start);}
        // Fixed storage; diagnostics-only, with bounded delayed output. No per-call allocations.
        static class UpdateCoverage
        {
            struct Row{internal string Name;internal int Frame;internal double Ms;internal int G0,G1,G2;}
            static readonly Row[] rows=new Row[64];static int write,count,dropped,emitted;
            static double window;static int depth;
            static readonly long[] starts=new long[64];
            static readonly int[,] collections=new int[64,3];
            internal static long Begin(){if(!ModDiagnostics.On || depth==64)return 0;collections[depth,0]=GC.CollectionCount(0);collections[depth,1]=GC.CollectionCount(1);collections[depth,2]=GC.CollectionCount(2);long start=Stopwatch.GetTimestamp();starts[depth++]=start;return start;}
            internal static void End(string name,long start)
            {
                if(start==0)return;
                if(depth==0 || starts[depth-1]!=start){depth=0;dropped++;return;}
                depth--;
                double ms=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
                if(ms<10)return;
                if(count==rows.Length){dropped++;return;}
                rows[write]=new Row{Name=name,Frame=Time.frameCount,Ms=ms,G0=GC.CollectionCount(0)-collections[depth,0],G1=GC.CollectionCount(1)-collections[depth,1],G2=GC.CollectionCount(2)-collections[depth,2]};
                write=(write+1)%rows.Length;count++;
            }
            internal static void Flush()
            {
                if(!ModDiagnostics.On)return;
                double now=Time.realtimeSinceStartup;
                if(now<window || now-window>=5){window=now;emitted=0;}
                if(count==0 || emitted>=8)return;
                var row=rows[(write-count+rows.Length)%rows.Length];count--;emitted++;
                Debug.Log("[TLD MP update-coverage] frame="+row.Frame+" stage="+row.Name+" ms="+row.Ms.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" gcDelta="+row.G0+"/"+row.G1+"/"+row.G2+" dropped="+dropped+" thresholdMs=10 delayed=true");
                // No self-reporting loop: enqueue cost is not logged recursively.
            }
        }
        [DefaultExecutionOrder(10000)]public sealed class Driver:MonoBehaviour
        {
            void Awake(){EnsureRabbitRuntime();FrameDiagnostics.RegisterMainThread();}
            void Update()
            {
                DiagAggregateFlush();
                EnsureRabbitRuntime();RabbitFlowDiagnostics.Tick(loopbackMode||ModDiagnostics.On,node!=null&&node.IsHost,savedatascript.s,Now);
                RabbitAttackDiagnostics.Tick(loopbackMode||ModDiagnostics.On,node,savedatascript.s,mainscript.s==null?null:mainscript.s.player,Now);
                UpdateCoverage.Flush();
                long constructionStage=UpdateCoverage.Begin();try{FixedPropConstruction.Tick();}finally{UpdateCoverage.End("Bridge.FixedPropPreparation",constructionStage);}
                long stage=UpdateCoverage.Begin();try{TickImpactAudio(true,false);}finally{UpdateCoverage.End("Bridge.ImpactAudioWarm",stage);}
                stage=UpdateCoverage.Begin();try{FrameDiagnostics.BeginFrame();}finally{UpdateCoverage.End("Diagnostics.BeginFrameAndReport",stage);}
                stage=UpdateCoverage.Begin();try{FarPlaceQueue.Drain();}finally{UpdateCoverage.End("Bridge.FarPlaceDrain",stage);}
                FrameDiagnostics.PulseBegin();
                stage=UpdateCoverage.Begin();try{Pulse();}finally{UpdateCoverage.End("Bridge.Pulse",stage);}
                stage=UpdateCoverage.Begin();try{TickNativeSpawnSafety(false);}finally{UpdateCoverage.End("Bridge.SpawnSafety",stage);}
                FrameDiagnostics.PulseEnd();
                stage=UpdateCoverage.Begin();
                try{try{GroundDiagnostics.TickLoadingOverlay(!Connected || (node!=null && (node.IsHost || (node.Ready && spawnFinished))));}catch(Exception e){GroundDiagnostics.LoadingOverlayFailed(e);}}
                finally{UpdateCoverage.End("Bridge.LoadingOverlay",stage);}
                stage=UpdateCoverage.Begin();try{TickNewsChangelog();}finally{UpdateCoverage.End("Bridge.News",stage);}
                stage=UpdateCoverage.Begin();try{WildlifeWeather.Tick(Connected);}finally{UpdateCoverage.End("Bridge.Wildlife",stage);}
                stage=UpdateCoverage.Begin();try{TickPlayerRate();}finally{UpdateCoverage.End("Bridge.PlayerRate",stage);}
                RabbitMetadataSync.Tick(Connected&&PatchOptions.Rabbits,WorldSceneAvailable(),mainscript.s==null?null:mainscript.s.player,savedatascript.s,node,Now);
                RabbitReplicas.Tick(IsRabbitGuest);
            }
            void FixedUpdate(){TickBusPair();RabbitContact.FixedTick(Connected,Now);TickMovingPassenger();DebrisContactFilter.Tick();TickImpactAudio(false,false);TickCrateContacts();TickJoinPhysics();TickNativeSpawnSafety(true);TickVehicleImpactSamples();TickVehicleImpactBursts();RabbitReplicas.Tick(IsRabbitGuest);}
            void LateUpdate()
            {
                // Driver runs last (order 10000): first measure every other LateUpdate since the probe.
                LateUpdateProbe.ReportOthers();
                // No try/finally: RabbitReplicas.Tick must stay the last call (RabbitNetworkGuardTests).
                // An escaping exception leaves the stage open; UpdateCoverage.End then drops it.
                long lateStage=UpdateCoverage.Begin();
                RabbitMotionTick();SmokeTick();TickImpactAudio(false,true);TickNativeSeatSafety();TickJoinPhysics();try{RemoteCarSmoothing.CheckDrift();CarCargo.HoldTick(RemoteCarSmoothing.IsSmoothed);WorldJump.Tick(Connected);PartsCensus.Tick(Connected);}catch(Exception e){if(!worldJumpFailed){worldJumpFailed=true;Debug.LogWarning("[TLD MP world] diagnostics disabled after error: "+e.GetType().Name);}}
                UpdateCoverage.End("Bridge.LateUpdate",lateStage);
                RabbitReplicas.Tick(IsRabbitGuest);
            }
            void OnDestroy(){FixedPropConstruction.Clear();DebrisContactFilter.Clear();ClearImpactPool();ClearCrateContacts();FrameDiagnostics.Shutdown();if(loopbackMode)Loopback.Stop();Reset();}
        }
        // Diagnostics only (added only when ModDiagnostics.On). Runs first in LateUpdate, so the
        // time until Driver.LateUpdate (which runs last) is the cost of every other script's
        // LateUpdate: game scripts, the grass shader's texture upload and TLDLoader. It also
        // records whether a grass TextureUpdater had a Texture2D.Apply pending this frame.
        // Read-only: it never changes any game object.
        [DefaultExecutionOrder(-32000)]public sealed class LateUpdateProbe:MonoBehaviour
        {
            static long start;static int startFrame=-1;
            static int grassCount,grassPending,grassMaxPixels;
            static StixGames.GrassShader.TextureUpdater[] grass;static float nextScan;
            static readonly System.Reflection.FieldInfo applyField=typeof(StixGames.GrassShader.TextureUpdater).GetField("apply",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            static float window;static int emitted;
            void LateUpdate()
            {
                if(!ModDiagnostics.On)return;
                float now=Time.realtimeSinceStartup;
                if(grass==null || now>=nextScan){grass=UnityEngine.Object.FindObjectsOfType<StixGames.GrassShader.TextureUpdater>();nextScan=now+10f;}
                grassCount=grass.Length;grassPending=0;grassMaxPixels=0;
                if(applyField!=null)
                    for(int i=0;i<grass.Length;i++)
                    {
                        var updater=grass[i];if(updater==null)continue;
                        object value=applyField.GetValue(updater);
                        if(value is bool && (bool)value)
                        {
                            grassPending++;var texture=updater.targetTexture;
                            if(texture!=null)grassMaxPixels=Math.Max(grassMaxPixels,texture.width*texture.height);
                        }
                    }
                startFrame=Time.frameCount;start=Stopwatch.GetTimestamp();
            }
            internal static void ReportOthers()
            {
                if(!ModDiagnostics.On || startFrame!=Time.frameCount)return;
                double ms=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
                if(ms<10)return;
                float now=Time.realtimeSinceStartup;
                if(now-window>=5){window=now;emitted=0;}
                if(emitted>=8)return;
                emitted++;
                Debug.Log("[TLD MP late-probe] frame="+Time.frameCount+" otherLateUpdateMs="+ms.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+
                    " grassUpdaters="+grassCount+" grassApplyPending="+grassPending+" grassMaxPixels="+grassMaxPixels+" gc0="+GC.CollectionCount(0));
            }
        }
        static readonly HashSet<uint> announcedSpawns=new HashSet<uint>();
        sealed class SpawnApplyLedger
        {
            public ulong Session,Sequence;public byte[] Hash;public List<byte[]> Packets;
            public HashSet<uint> Definitions;public int NextPacket,WireBytes;public bool Completed,Published;
            public string Failure,PendingReason;
        }
        static readonly Dictionary<ulong,SpawnApplyLedger> spawnLedgers=new Dictionary<ulong,SpawnApplyLedger>();
        static readonly Dictionary<ulong,ulong> spawnCommitted=new Dictionary<ulong,ulong>();
        static int spawnApplyDepth,spawnLedgerBytes,spawnLedgerHigh;
        static List<uint> spawnIDs;
        static byte[] spawnPayload;
        static ulong spawnSequence;
        static double nextSpawnScan,nextSpawnSend;
        static int spawnAttempts;
        static void ClearSpawnHandoff()
        {announcedSpawns.Clear();spawnLedgers.Clear();spawnLedgerBytes=0;spawnIDs=null;spawnPayload=null;nextSpawnScan=nextSpawnSend=0;spawnAttempts=0;if(node!=null)node.ClearSpawnTransfers();}
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
            try{ConstructionFence("spawn-capture");}
            catch(Exception e){Debug.LogWarning("[TLD MP spawn] capture skipped: "+e.Message);nextSpawnScan=Now+10;return;}
            var items=new List<tosaveitemscript>();
            foreach(var pair in savedatascript.s.items)
                if(pair.Key>=node.FirstID && pair.Key<=node.LastID && !announcedSpawns.Contains(pair.Key) && pair.Value!=null && pair.Value.gameObject.activeInHierarchy)items.Add(pair.Value);
            if(items.Count==0)return;
            if(items.Count>8192){Debug.LogError("[TLD MP spawn] more than 8192 unannounced items ("+items.Count+"); transaction refused, will rescan.");nextSpawnScan=Now+10;return;}
            // Capture definitions first. Ordinary SyncAll suppresses these for
            // loose guest parts, so attachments alone cannot create the objects.
            var packets=new List<byte[]>();var ids=new List<uint>();
            bool previous=capturing,prevOverflow=captureOverflow,prevBounded=captureBounded;var prior=captured;long prevBytes=captureByteCount;
            capturing=true;captureBounded=true;captureOverflow=false;captureByteCount=0;captured=packets;
            bool overflow;
            try
            {
                foreach(var item in items){ids.Add(item.idInSave);syncScript.s.SendItemPosGlobal(item.idInSave,item.id,mainscript.GlobalFromUnityPos(item.useThisForPos.position),item.useThisForPos.eulerAngles);}
                foreach(var item in items)item.SyncAll();
            }
            finally{overflow=captureOverflow;captureByteCount=prevBytes;captureOverflow=prevOverflow;captureBounded=prevBounded;capturing=previous;captured=prior;}
            using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream))
            {
                // Keep only definitions and persistent state for this transaction.
                var allowed=new HashSet<uint>(ids);var selected=new List<byte[]>();
                foreach(var packet in packets)
                {
                    if(packet.Length==0 || !allowed.Contains(StablePackets.Item(packet)))continue;
                    if(packet[0]==16 || packet[0]==17 || PendingItemQueue.Holdable(packet[0]))selected.Add(packet);
                }
                if(overflow){Debug.LogError("[TLD MP spawn] capture exceeded the bounded context; transaction refused, will rescan.");nextSpawnScan=Now+10;return;}
                if(selected.Count>65536){Debug.LogError("[TLD MP spawn] "+selected.Count+" packets exceeds 65536; transaction refused, will rescan.");nextSpawnScan=Now+10;return;}
                long projected=8;
                foreach(var packet in selected){projected+=4+packet.Length;if(projected>StableNode.MaxSpawnBytes)break;}
                if(projected>StableNode.MaxSpawnBytes){Debug.LogError("[TLD MP spawn] handoff projected "+projected+" bytes exceeds 16MiB; transaction refused, will rescan.");nextSpawnScan=Now+10;return;}
                writer.Write(ids.Count);writer.Write(selected.Count);
                foreach(var packet in selected){writer.Write(packet.Length);writer.Write(packet);}
                spawnPayload=stream.ToArray();spawnIDs=ids;spawnSequence++;spawnAttempts=0;nextSpawnSend=Now;
            }
            Debug.Log("[TLD MP spawn] captured transaction="+spawnSequence+" items="+ids.Count+" bytes="+spawnPayload.Length);
        }
        static bool BytesEqual(byte[] a,byte[] b)
        {
            if(a==null || b==null || a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;
            return true;
        }
        static bool SpawnHashMatches(byte[] hash,byte[] bytes)
        {
            byte[] other;using(var sha=new System.Security.Cryptography.SHA256Managed())other=sha.ComputeHash(bytes);
            for(int i=0;i<32;i++)if(hash[i]!=other[i])return false;
            return true;
        }
        static bool ApplySpawn(ulong source,ulong sequence,byte[] bytes)
        {
            StablePeer peer;if(node==null || !node.IsHost || !node.Peers.TryGetValue(source,out peer) || bytes==null || bytes.Length>StableNode.MaxSpawnBytes)return false;
            SpawnApplyLedger ledger;
            if(spawnLedgers.TryGetValue(source,out ledger))
            {
                if(ledger.Session==peer.Session && ledger.Sequence==sequence)
                {
                    if(ledger.Completed)return SpawnHashMatches(ledger.Hash,bytes);
                    if(ledger.Failure!=null || !SpawnHashMatches(ledger.Hash,bytes))return false;
                    return ApplySpawnLedger(source,peer,ledger);
                }
                ulong committed;
                if(spawnCommitted.TryGetValue(source,out committed) && sequence<=committed && committed==ledger.Sequence && ledger.Session==peer.Session)
                    return sequence==committed;
                if(!ledger.Completed || ledger.Failure!=null)return false;
                spawnLedgers.Remove(source);ReleaseLedger(ledger);
            }
            ulong watermark;
            if(spawnCommitted.TryGetValue(source,out watermark) && sequence<=watermark)
            {
                if(sequence==watermark)return true;
                return false;
            }
            if(spawnLedgerBytes+bytes.Length>StableNode.MaxSpawnBytes)return false;
            ledger=ParseSpawnLedger(source,peer,sequence,bytes);
            if(ledger==null)return false;
            ledger.WireBytes=bytes.Length;
            spawnLedgers[source]=ledger;spawnLedgerBytes+=bytes.Length;
            if(spawnLedgerBytes>spawnLedgerHigh)spawnLedgerHigh=spawnLedgerBytes;
            return ApplySpawnLedger(source,peer,ledger);
        }
        static void ReleaseLedger(SpawnApplyLedger ledger)
        {
            if(ledger!=null){spawnLedgerBytes=Math.Max(0,spawnLedgerBytes-ledger.WireBytes);ledger.WireBytes=0;}
        }
        static SpawnApplyLedger ParseSpawnLedger(ulong source,StablePeer peer,ulong sequence,byte[] bytes)
        {
            var packets=new List<byte[]>();var definitions=new HashSet<uint>();
            try
            {
                using(var stream=new System.IO.MemoryStream(bytes,false))using(var reader=new System.IO.BinaryReader(stream))
                {
                    int count=reader.ReadInt32(),length=reader.ReadInt32();
                    if(count<1 || count>8192 || length<count || length>65536)return null;
                    for(int i=0;i<length;i++)
                    {
                        int size=reader.ReadInt32();if(size<5 || size>stream.Length-stream.Position)return null;
                        var packet=reader.ReadBytes(size);
                        if(!StablePackets.Valid(packet))return null;
                        byte type=packet[0];
                        if(type!=16 && type!=17 && !PendingItemQueue.Holdable(type))return null;
                        uint id=StablePackets.Item(packet);
                        if(id<peer.First || id>peer.Last)return null;
                        if(itemRecovery.Deleted(id,Now))return null;
                        if(i<count){if(type!=16 || !definitions.Add(id))return null;}
                        else if(!definitions.Contains(id))return null;
                        if(!ValidateData(packet))return null;
                        packets.Add(packet);
                    }
                    if(stream.Position!=stream.Length)return null;
                }
            }
            catch(Exception e){Debug.LogWarning("[TLD MP spawn] parse failed source="+source+" transaction="+sequence+" error="+e);return null;}
            foreach(var packet in packets)
            {
                byte type=packet[0];uint parent=0;
                if(type==42 && packet.Length>17 && BitConverter.ToInt32(packet,5)==1)parent=BitConverter.ToUInt32(packet,13);
                else if(type==17 && packet.Length>9)parent=BitConverter.ToUInt32(packet,5);
                if(parent!=0 && !definitions.Contains(parent) && !HeldItemExists(parent))return null;
            }
            byte[] hash;using(var sha=new System.Security.Cryptography.SHA256Managed())hash=sha.ComputeHash(bytes);
            return new SpawnApplyLedger {Session=peer.Session,Sequence=sequence,Hash=hash,Packets=packets,Definitions=definitions};
        }
        static bool ApplySpawnLedger(ulong source,StablePeer peer,SpawnApplyLedger ledger)
        {
            spawnApplyDepth++;
            try
            {
                // All input is checked before native creation. Existing IDs are
                // updated by native UpdItem, never instantiated a second time.
                while(ledger.NextPacket<ledger.Packets.Count)
                {
                    var packet=ledger.Packets[ledger.NextPacket];
                    try
                    {
                        if(packet[0]==16 || packet[0]==17)Deliver(source,packet);
                        else
                        {
                            bool previous=dispatching;dispatching=true;
                            bool ok;
                            try{ok=TryReplayOneHeld(new PendingItemQueue.Held {Bytes=packet,Sender=source});}
                            finally{dispatching=previous;}
                            if(!ok){ledger.Failure="held replay failed at packet "+ledger.NextPacket;return false;}
                        }
                    }
                    catch(Exception e){ledger.Failure="native handler threw at packet "+ledger.NextPacket+": "+e.GetType().Name;return false;}
                    ledger.NextPacket++;
                }
            }
            finally{spawnApplyDepth--;}
            return VerifySpawnLedger(source,ledger);
        }
        static bool VerifySpawnLedger(ulong source,SpawnApplyLedger ledger)
        {
            if(ledger.Completed)return true;
            foreach(uint id in ledger.Definitions)if(!HeldItemExists(id)){ledger.PendingReason="definition "+id+" not materialized yet";return false;}
            foreach(var packet in ledger.Packets)
            {
                if(packet[0]==42 && !VerifyAttachmentPacket(packet)){ledger.PendingReason="attachment not settled id="+StablePackets.Item(packet);return false;}
                if(packet[0]==17 && !VerifyStoredPacket(packet)){ledger.PendingReason="storage not settled id="+StablePackets.Item(packet);return false;}
            }
            ledger.PendingReason=null;
            ledger.Completed=true;
            if(!ledger.Published)
            {
                ledger.Published=true;
                foreach(var packet in ledger.Packets)node.PublishNative(packet,true);
            }
            ledger.Packets=null;ledger.Definitions=null;
            ReleaseLedger(ledger);
            spawnCommitted[source]=ledger.Sequence;
            Debug.Log("[TLD MP spawn] applied source="+source+" transaction="+ledger.Sequence);
            return true;
        }
        static bool VerifyAttachmentPacket(byte[] packet)
        {
            if(packet.Length<9)return false;
            tosaveitemscript item;
            if(!Find(StablePackets.Item(packet),out item) || item.attachable==null)return false;
            int attachType=BitConverter.ToInt32(packet,5);
            if(attachType==0)return !item.attachable.attached;
            if(packet.Length<97)return false;
            var live=new save_attachable(item.attachable,item.idInSave);
            int indexType=BitConverter.ToInt32(packet,9);
            if(attachType==1)
            {
                if(!item.attachable.attached || live.attachType!=1)return false;
                if(live.indexType!=indexType || live.parentid!=BitConverter.ToUInt32(packet,13) || live.index!=BitConverter.ToInt32(packet,45))return false;
                return CloseD(packet,49,live.lpos,.001);
            }
            if(attachType==2)
            {
                if(!item.attachable.attached || live.attachType!=2)return false;
                if(live.indexType!=indexType || live.poigenid!=BitConverter.ToInt32(packet,17) ||
                    live.poiid.x!=BitConverter.ToDouble(packet,21) || live.poiid.y!=BitConverter.ToDouble(packet,29) || live.poiid.z!=BitConverter.ToDouble(packet,37) ||
                    live.index!=BitConverter.ToInt32(packet,45))return false;
                return CloseD(packet,49,live.lpos,.001);
            }
            if(attachType==3 || attachType==4)
            {
                if(!item.attachable.attached || live.attachType!=4)return false;
                return CloseD(packet,73,live.gpos,.001);
            }
            return false;
        }
        static bool CloseD(byte[] packet,int offset,UnityEngine.Vector3d live,double tolerance)
        {
            return Math.Abs(BitConverter.ToDouble(packet,offset)-live.x)<=tolerance &&
                Math.Abs(BitConverter.ToDouble(packet,offset+8)-live.y)<=tolerance &&
                Math.Abs(BitConverter.ToDouble(packet,offset+16)-live.z)<=tolerance;
        }
        static bool VerifyStoredPacket(byte[] packet)
        {
            if(packet.Length<37)return false;
            tosaveitemscript child,parent;
            if(!Find(StablePackets.Item(packet),out child))return false;
            if(!Find(BitConverter.ToUInt32(packet,5),out parent))return false;
            int index=BitConverter.ToInt32(packet,9);
            if(index>=0)
            {
                if(child.P==null || child.P.physlock==null || parent.physLocks==null || index>=parent.physLocks.Count)return false;
                var record=parent.physLocks[index];
                if(record==null || child.P.physlock!=record)return false;
                for(int i=0;i<record.stored.Count;i++)if(record.stored[i]!=null && record.stored[i].tosave==child)return true;
                return false;
            }
            if(!child.gotSyncLocal || child.gotSyncLocalParentID!=parent.idInSave)return false;
            if(index<=-200000)return parent.GetColliderFromIndex(-index-200000)!=null;
            if(index<=-100000)return parent.partslotscripts!=null && -index-100000<parent.partslotscripts.Count;
            return false;
        }
        internal static bool VerifyPendingSpawnConstruction(out string reason)
        {
            reason=null;
            foreach(var pair in spawnLedgers)
            {
                var ledger=pair.Value;
                if(ledger.Completed && ledger.Failure==null)continue;
                StablePeer peer;
                if(node==null || !node.Peers.TryGetValue(pair.Key,out peer) || peer.Session!=ledger.Session)
                {reason="spawn apply ledger "+pair.Key+": peer or session replaced";return false;}
                if(ledger.Failure!=null){reason="spawn apply failed peer="+pair.Key+" seq="+ledger.Sequence+": "+ledger.Failure;return false;}
                if(ledger.NextPacket>0 || ledger.Packets.Count>0)
                {
                    if(!ApplySpawnLedger(pair.Key,peer,ledger) || !ledger.Completed)
                    {reason="spawn apply incomplete peer="+pair.Key+" seq="+ledger.Sequence+" at="+ledger.NextPacket+"/"+ledger.Packets.Count+(ledger.Failure==null?"":" "+ledger.Failure);return false;}
                }
            }
            return true;
        }
    }
}

namespace TldMp
{
    // Windowed summary for per-item diagnostic families. Each window emits one
    // line per family: total count, distinct keys (ids) and up to three sample
    // lines. Diagnostics only; it never decides anything for gameplay.
    internal sealed class DiagnosticFamilyWindow
    {
        internal const int SampleLimit=3;
        sealed class Family
        {
            internal int Count;
            internal readonly HashSet<string> Keys=new HashSet<string>();
            internal readonly List<string> Samples=new List<string>(SampleLimit);
        }
        readonly double seconds;
        double windowEnd;
        readonly Dictionary<string,Family> families=new Dictionary<string,Family>();

        internal DiagnosticFamilyWindow(double seconds)
        {
            if(seconds<=0)throw new ArgumentOutOfRangeException("seconds");
            this.seconds=seconds;
        }

        internal static bool Aggregated(string key)
        {
            return key.StartsWith("cargo-suppress:",StringComparison.Ordinal) || key.StartsWith("native-mounted-pose:",StringComparison.Ordinal);
        }

        internal void Note(string key,string text,double now,Action<string> emit)
        {
            if(now>=windowEnd){Take(now,emit);}
            int colon=key.IndexOf(':');
            string family=colon<0?key:key.Substring(0,colon);
            Family row;
            if(!families.TryGetValue(family,out row)){row=new Family();families[family]=row;}
            row.Count++;row.Keys.Add(key);
            if(row.Samples.Count<SampleLimit)row.Samples.Add(text);
        }

        // Called once per frame. Emits nothing when no family was noted in the window.
        internal void Tick(double now,Action<string> emit)
        {
            if(families.Count>0 && now>=windowEnd)Take(now,emit);
        }

        void Take(double now,Action<string> emit)
        {
            foreach(var pair in families)
            {
                var row=pair.Value;
                emit("aggregate family="+pair.Key+" window="+seconds.ToString("F0",System.Globalization.CultureInfo.InvariantCulture)+"s count="+row.Count+" ids="+row.Keys.Count+" samples="+String.Join(" | ",row.Samples.ToArray()));
            }
            families.Clear();
            windowEnd=now+seconds;
        }
    }
}

namespace TldMp
{
    // Shows a failed multiplayer join to the player with the game's own
    // "Wrong Password or Timed Out" window (same styling and close behaviour),
    // then leaves the lobby the way the native wrong-password kick does.
    // A clone is used so the native window and its localisation stay untouched.
    internal static class JoinFailurePopup
    {
        static GameObject current;
        internal static void Show(string message){Show(message,true);}
        internal static void Show(string message,bool leaveLobby)
        {
            try
            {
                var menu=networkingScript.Menu();
                if(menu==null || menu.windowWrongPassword==null){Debug.LogWarning("[TLD MP join] Couldn't show the join failure window: "+message);return;}
                menu.menuOpen=true;menu.SetWindowFocused(true);menu.OpenRefresh();menu.APressedRefresh();
                GameObject source=menu.windowWrongPassword;source.SetActive(false);
                if(current!=null)UnityEngine.Object.Destroy(current);
                GameObject clone=UnityEngine.Object.Instantiate(source,source.transform.parent,false);
                clone.SetActive(false);clone.name="Roadworthy join failure";
                Debug.Log("[TLD MP join] Join failure window layout: "+Describe(clone.transform,0));
                // The label may be UI.Text or TextMeshPro, with or without a languagetext
                // localiser. Take the graphic with a writable text property that currently
                // holds the longest text (the message, not a button caption).
                Component target=null;int longest=-1;
                foreach(var graphic in clone.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                {
                    string labelText=GetText(graphic);if(labelText==null)continue;
                    if(labelText.Length>longest){longest=labelText.Length;target=graphic;}
                }
                bool set=false;
                if(target!=null)
                {
                    // Remove every localiser on or above the label so OnEnable cannot restore the native text.
                    foreach(var localiser in target.GetComponentsInParent<languagetext>(true))UnityEngine.Object.DestroyImmediate(localiser);
                    foreach(var localiser in clone.GetComponentsInChildren<languagetext>(true))
                    {
                        object bound=localiser.text;
                        if(bound==null){var tmpField=typeof(languagetext).GetField("tmp");bound=tmpField==null?null:tmpField.GetValue(localiser);}
                        if(ReferenceEquals(bound,target))UnityEngine.Object.DestroyImmediate(localiser);
                    }
                    SetText(target,message);set=true;
                    Widen(clone,target);
                }
                if(!set)Debug.LogWarning("[TLD MP join] The join failure window has no label to replace; showing it unchanged.");
                clone.AddComponent<JoinFailureCloser>();
                clone.SetActive(true);current=clone;
                Debug.Log("[TLD MP join] Join failed: "+message);
                var hood=networkingScript.s!=null?networkingScript.s.hood:null;
                if(leaveLobby && hood!=null)hood.Disconnect("Couldn't join: "+message);
            }
            catch(Exception e){Debug.LogWarning("[TLD MP join] Join failure window failed ("+e.GetType().Name+": "+e.Message+"): "+message);}
        }
        static string GetText(Component component)
        {
            var property=component.GetType().GetProperty("text");
            if(property==null || property.PropertyType!=typeof(string) || !property.CanWrite || !property.CanRead)return null;
            return property.GetValue(component,null) as string ?? "";
        }
        // Widen the window: every fixed-width rect at least 40% as wide as the window
        // (its background and the label) becomes 2.2x wider and 1.5x taller. Stretched
        // children follow their parent. Small rects (buttons, icons) keep their size.
        static void Widen(GameObject window,Component label)
        {
            var root=window.transform as RectTransform;if(root==null)return;
            float reference=Mathf.Max(root.rect.width,1f);
            foreach(var rect in window.GetComponentsInChildren<RectTransform>(true))
            {
                bool fixedWidth=Mathf.Approximately(rect.anchorMin.x,rect.anchorMax.x);
                if(!fixedWidth || rect.rect.width<reference*0.4f)continue;
                bool fixedHeight=Mathf.Approximately(rect.anchorMin.y,rect.anchorMax.y);
                rect.sizeDelta=new Vector2(rect.sizeDelta.x*2.2f,fixedHeight?rect.sizeDelta.y*1.5f:rect.sizeDelta.y);
            }
        }
        static string Describe(Transform node,int depth)
        {
            if(depth>4)return "";
            var parts=new List<string>();
            foreach(var component in node.GetComponents<Component>())if(component!=null && !(component is Transform))parts.Add(component.GetType().Name);
            var rect=node as RectTransform;
            string size=rect==null?"":" "+rect.rect.width.ToString("F0")+"x"+rect.rect.height.ToString("F0");
            var text=new System.Text.StringBuilder("{"+node.name+size+" ["+String.Join(",",parts.ToArray())+"]");
            for(int i=0;i<node.childCount && i<12;i++)text.Append(Describe(node.GetChild(i),depth+1));
            return text.Append("}").ToString();
        }
        static void SetText(Component target,string message)
        {
            var text=target as UnityEngine.UI.Text;
            if(text!=null)
            {
                text.text=message;
                // HorizontalWrapMode.Wrap=0, VerticalWrapMode.Truncate=1 (TextRenderingModule is not referenced).
                SetEnum(text,"horizontalOverflow",0);SetEnum(text,"verticalOverflow",1);
                text.resizeTextForBestFit=true;text.resizeTextMinSize=Math.Min(10,text.fontSize);text.resizeTextMaxSize=Math.Max(text.fontSize,10);
                return;
            }
            var type=target.GetType();
            var property=type.GetProperty("text");if(property!=null && property.CanWrite)property.SetValue(target,message,null);
            var autoSize=type.GetProperty("enableAutoSizing");if(autoSize!=null && autoSize.CanWrite)autoSize.SetValue(target,true,null);
            var size=type.GetProperty("fontSize");var min=type.GetProperty("fontSizeMin");var max=type.GetProperty("fontSizeMax");
            if(size!=null && min!=null && max!=null && size.PropertyType==typeof(float)){float current=(float)size.GetValue(target,null);if(min.CanWrite)min.SetValue(target,Mathf.Min(10f,current),null);if(max.CanWrite)max.SetValue(target,current,null);}
            var wrap=type.GetProperty("enableWordWrapping");if(wrap!=null && wrap.CanWrite)wrap.SetValue(target,true,null);
        }
        static void SetEnum(object target,string name,int value)
        {
            var property=target.GetType().GetProperty(name);
            if(property!=null && property.CanWrite && property.PropertyType.IsEnum)property.SetValue(target,Enum.ToObject(property.PropertyType,value),null);
        }
    }
    // Closes the cloned window like the native one (any key), or with its own button.
    internal sealed class JoinFailureCloser:MonoBehaviour
    {
        float shown;
        void Start()
        {
            shown=Time.unscaledTime;
            foreach(var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
                button.onClick.AddListener(delegate{if(this!=null)Destroy(gameObject);});
        }
        void Update(){if(Time.unscaledTime-shown>0.3f && Input.anyKeyDown)Destroy(gameObject);}
    }
    // Preview only: TLD_MP_JOIN_POPUP_PREVIEW=1 or "tldmp-join-popup-preview.on" next to
    // TheLongDrive.exe shows the join-failure window once on the main menu with a sample
    // message, without touching any lobby. Delete the file to stop it.
    internal sealed class JoinPopupPreview:MonoBehaviour
    {
        const string Flag="tldmp-join-popup-preview.on";
        internal static void StartIfRequested(GameObject host)
        {
            bool requested=Environment.GetEnvironmentVariable("TLD_MP_JOIN_POPUP_PREVIEW")=="1";
            if(!requested)
                try
                {
                    string managed=Path.GetDirectoryName(typeof(JoinPopupPreview).Assembly.Location);
                    requested=File.Exists(Path.Combine(Directory.GetParent(Directory.GetParent(managed).FullName).FullName,Flag));
                }
                catch{}
            if(requested)host.AddComponent<JoinPopupPreview>();
        }
        void Update()
        {
            if(Time.realtimeSinceStartup<8f)return;
            SteamP2PMenuHandlerScript menu=null;
            try{menu=networkingScript.Menu();}catch{}
            if(menu==null || menu.windowWrongPassword==null)return;
            Debug.Log("[TLD MP join] Showing the join failure window preview");
            JoinFailurePopup.Show(JoinCompatibility.Describe(
                JoinCompatibility.Compose("0.2.0-pre2","PREVIEW-HOST","PREVIEW","sdr"),
                JoinCompatibility.Compose(PatchInfo.Version,"PREVIEW-YOU","PREVIEW","sdr")),false);
            enabled=false;
        }
    }
}
