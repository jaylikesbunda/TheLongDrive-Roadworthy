using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Steamworks;

// The bodies marked "extracted" are replaced with actual game IL by ExtractReplay.
// Unity native services and the network transport are stand-ins; Steam callbacks are real.
namespace UnityEngine
{
    public class Object
    {
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !ReferenceEquals(a, b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class Component : Object
    {
        public GameObject gameObject { get; set; }
        public T GetComponentInParent<T>() where T : class { return null; }
    }
    public class MonoBehaviour : Component { }
    public enum CollisionDetectionMode { Discrete, Continuous, ContinuousDynamic, ContinuousSpeculative }
    public enum RigidbodyInterpolation { None, Interpolate, Extrapolate }
    public class Rigidbody : Component
    {
        public CollisionDetectionMode collisionDetectionMode { get; set; }
        public RigidbodyInterpolation interpolation { get; set; }
        public bool useGravity { get; set; }
        public bool isKinematic { get; set; }
    }
    public class GameObject : Object
    {
        public Rigidbody Existing;
        public int Adds;
        public T GetComponent<T>() where T : class { return Existing as T; }
        public T AddComponent<T>() where T : Component, new()
        {
            Adds++;
            if (Existing != null) return null; // Unity rejects a second Rigidbody.
            var result = new T { gameObject = this };
            Existing = result as Rigidbody;
            return result;
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
    }
    public struct Quaternion
    {
        public Vector3 EulerValue;
        public static Quaternion Euler(Vector3 value) { return new Quaternion { EulerValue = value }; }
    }
    public class Transform : Component
    {
        public Transform parent;
        public Vector3 localPosition;
        public Vector3 position { get { return parent == null ? localPosition : parent.position + localPosition; } set { localPosition = value; } }
        public Quaternion rotation { get; set; }
    }
    public static class Time { public static float time { get; set; } }
    public static class Debug
    {
        public static List<string> Messages = new List<string>();
        public static void Log(object message) { Messages.Add(Convert.ToString(message)); }
    }
}

public enum msgType : byte { test }
public class massScript : UnityEngine.MonoBehaviour { public void Upd() { } }
public class mainscript
{
    public static UnityEngine.RigidbodyInterpolation RBI() { return UnityEngine.RigidbodyInterpolation.Interpolate; }
}
public class jointHelpScript : UnityEngine.MonoBehaviour
{
    public UnityEngine.Rigidbody RB;
    public bool freezed = true;
    public jointHelpScript() { gameObject = new UnityEngine.GameObject(); }
    public void UnFreezeStart() { throw new Exception("Not extracted"); }
}
public class tosaveitemscript : UnityEngine.MonoBehaviour
{
    public bool gotSyncLocal;
    public UnityEngine.Vector3 gotSyncLastPos, gotSyncLastRot;
    public UnityEngine.Transform useThisForPos = new UnityEngine.Transform();
    public float gotSyncTime;
    public void GotSync(UnityEngine.Vector3 pos, UnityEngine.Vector3 rot) { throw new Exception("Not extracted"); }
    public void UpdGotSyncTime() { throw new Exception("Not extracted"); }
}

namespace Steamworks
{
    // Only the network calls are replaced. Callback<T> and its dispatcher come from
    // the game's unmodified com.rlabrecque.steamworks.net.dll.
    public static class SteamNetworking
    {
        public static Queue<byte[]> Packets = new Queue<byte[]>();
        public static bool IsP2PPacketAvailable(out uint size, int channel = 0)
        {
            size = Packets.Count == 0 ? 0 : (uint)Packets.Peek().Length;
            return Packets.Count != 0;
        }
        public static bool ReadP2PPacket(byte[] dest, uint max, out uint read, out CSteamID remote, int channel = 0)
        {
            var packet = Packets.Dequeue();
            Buffer.BlockCopy(packet, 0, dest, 0, packet.Length);
            read = (uint)packet.Length;
            remote = new CSteamID(123);
            return true;
        }
    }
    public static class SteamFriends { public static string GetFriendPersonaName(CSteamID id) { return "Offline peer"; } }
}
namespace GeneszSteamP2PBase
{
    public class networkingScript
    {
        public static networkingScript s = new networkingScript();
        public static jointHelpScript Body;
        public static int Processed;
        public void ProcessMessage(CSteamID id, byte[] bytes, msgType type)
        {
            if (bytes.Length > 1 && bytes[1] == 1) Body.UnFreezeStart();
            Processed++;
        }
        public static void DebugLog(string message) { }
    }
    public class SteamP2PNetworkingUnderTheHoodScript : UnityEngine.MonoBehaviour
    {
        public Callback<LobbyCreated_t> CallbackLobbyCreated;
        public Callback<LobbyMatchList_t> CallbackLobbyListed;
        public Callback<LobbyChatUpdate_t> CallbackLobbyChanged;
        public Callback<LobbyEnter_t> CallbackLobbyEnter;
        public Callback<LobbyDataUpdate_t> CallbackLobbyDataUpdate;
        public Callback<P2PSessionRequest_t> P2PSessionRequest;
        public Callback<P2PSessionConnectFail_t> P2PSessionConnectFailCallback;
        public int dataIn, debugRecMessages, EnterEvents, MicRefreshes;
        public static bool IsSteam() { return true; }
        public void VoiceChatUpdMic() { MicRefreshes++; }
        public static bool Expected(CSteamID id, msgType type) { return true; }
        public void OnCreatedLobby(LobbyCreated_t value) { }
        public void OnListedLobbies(LobbyMatchList_t value) { }
        public void OnLobbyChange(LobbyChatUpdate_t value) { }
        public void OnLobbyEnter(LobbyEnter_t value) { EnterEvents++; }
        public void OnLobbyDataUpdate(LobbyDataUpdate_t value) { }
        public void OnP2PSessionRequest(P2PSessionRequest_t value) { }
        public void OnP2PSessionConnectFail(P2PSessionConnectFail_t value) { }
        public void Init() { throw new Exception("Not extracted"); }
        public void ReadMessage() { throw new Exception("Not extracted"); }
    }
}
public static class ReplayMain
{
    static int checks;
    static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    static jointHelpScript ExistingBody()
    {
        var result = new jointHelpScript();
        result.gameObject.Existing = new UnityEngine.Rigidbody();
        return result;
    }
    static IDictionary Registry()
    {
        var type = typeof(Callback<LobbyEnter_t>).Assembly.GetType("Steamworks.CallbackDispatcher", true);
        return (IDictionary)type.GetField("m_registeredCallbacks", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    }
    public static int Main(string[] args)
    {
        try
        {
            bool patched = args.Contains("--patched");
            var originalBody = ExistingBody();
            bool threw = false;
            try { originalBody.UnFreezeStart(); } catch (NullReferenceException) { threw = true; }
            Assert(threw == !patched, "Duplicate-body regression result differs from expectation.");
            if (patched)
            {
                Assert(ReferenceEquals(originalBody.RB, originalBody.gameObject.Existing), "Existing body must be reused.");
                Assert(originalBody.gameObject.Adds == 0 && originalBody.RB.useGravity, "Reuse must preserve original body initialization.");
                originalBody.UnFreezeStart();
                Assert(originalBody.gameObject.Adds == 0, "Repeat unfreeze must be safe.");
            }
            var missing = new jointHelpScript();
            missing.UnFreezeStart();
            Assert(missing.RB != null && missing.gameObject.Adds == 1, "Normal creation must still work.");
            var inactive = new jointHelpScript { freezed = false };
            inactive.UnFreezeStart();
            Assert(inactive.RB == null && inactive.gameObject.Adds == 0, "Non-frozen path must not change.");
            Console.WriteLine(patched ? "FIXED: production unfreeze IL reuses existing body." : "REPRODUCED: production unfreeze IL throws on duplicate body.");

            var hood = new GeneszSteamP2PBase.SteamP2PNetworkingUnderTheHoodScript();
            GeneszSteamP2PBase.networkingScript.Body = ExistingBody();
            SteamNetworking.Packets.Enqueue(new byte[] { 0, 1 });
            SteamNetworking.Packets.Enqueue(new byte[] { 0, 0 });
            SteamNetworking.Packets.Enqueue(new byte[] { 0, 0 });
            threw = false;
            try { hood.ReadMessage(); } catch (NullReferenceException) { threw = true; }
            Assert(threw == !patched, "Packet processing exception regression mismatch.");
            Assert(SteamNetworking.Packets.Count == (patched ? 0 : 2), "Packet queue remainder mismatch.");
            Assert(GeneszSteamP2PBase.networkingScript.Processed == (patched ? 3 : 0), "Processed packet count mismatch.");
            Console.WriteLine(patched ? "FIXED: all 3 synthetic packets drained using production receiver IL." : "REPRODUCED: unfreeze exception aborts production receiver; 2 packets remain.");

            for (int i = 0; i < 5; i++) hood.Init();
            int registrations = 0;
            var lobbyCallbacks = new List<object>();
            foreach (DictionaryEntry entry in Registry())
            {
                foreach (object callback in (IEnumerable)entry.Value)
                {
                    registrations++;
                    if (callback.GetType() == typeof(Callback<LobbyEnter_t>)) lobbyCallbacks.Add(callback);
                }
            }
            Assert(registrations == (patched ? 7 : 35), "Steam callback registration count mismatch.");
            Assert(hood.MicRefreshes == 5, "Keep refreshing microphone on each initialization.");
            var entered = new LobbyEnter_t { m_EChatRoomEnterResponse = 1, m_ulSteamIDLobby = 123 };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(LobbyEnter_t)));
            try
            {
                Marshal.StructureToPtr(entered, ptr, false);
                foreach (object callback in lobbyCallbacks)
                    callback.GetType().GetMethod("OnRunCallback", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(callback, new object[] { ptr });
            }
            finally { Marshal.FreeHGlobal(ptr); }
            Assert(hood.EnterEvents == (patched ? 1 : 5), "Lobby event dispatch multiplicity mismatch.");
            Console.WriteLine((patched ? "FIXED" : "REPRODUCED") + ": 5 initializations -> " + registrations + " live real Steam callbacks; one lobby event -> " + hood.EnterEvents + " handlers.");

            var car = new tosaveitemscript();
            var passenger = new UnityEngine.Transform { parent = car.useThisForPos, localPosition = new UnityEngine.Vector3(1, 0, 0) };
            UnityEngine.Time.time = 10;
            car.GotSync(new UnityEngine.Vector3(10, 0, 0), new UnityEngine.Vector3());
            Assert(passenger.position.x == 11, "Passenger should follow parent translation.");
            UnityEngine.Time.time = 11;
            car.GotSync(new UnityEngine.Vector3(4, 0, 0), new UnityEngine.Vector3());
            Assert(passenger.position.x == 5 && car.gotSyncTime == 11, "Receive-order snap reproduction mismatch.");
            Console.WriteLine("OPEN: production GotSync IL accepts a stale pose and snaps car/passenger backwards 6 metres in the translation stand-in.");
            Console.WriteLine("PASS: " + checks + " checks (" + (patched ? "patched" : "baseline") + "). No Steam session or Unity native runtime used.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
