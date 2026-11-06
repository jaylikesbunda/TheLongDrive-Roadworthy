using System;
using TldMp.Experimental;

namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; }
    public struct Quaternion { public float x, y, z, w; }
    public class Transform { public Vector3 position; public Quaternion rotation; }
    public class Rigidbody { public Vector3 velocity; }
    public static class Debug { public static int Logs; public static string Last; public static void Log(object text) { Logs++; Last = text.ToString(); } }
}
public struct Vector3d { public double x, y, z; }
public class carscript { }
public class tosaveitemscript
{
    public carscript car;
    public UnityEngine.Rigidbody RB;
    public UnityEngine.Transform transform;
    public UnityEngine.Transform useThisForPos;
    public uint idInSave;
}
public static class mainscript
{
    public static Vector3d GlobalFromUnityPos(UnityEngine.Vector3 p) { return new Vector3d { x = p.x + 10000, y = p.y, z = p.z }; }
}
internal static class ExperimentalVehicleCaptureTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Vehicle capture invariant failed"); }
    public static int Main(string[] args)
    {
        bool probeOn = args.Length > 0 && args[0] == "probe";
        Environment.SetEnvironmentVariable("TLD_MP_VEHICLE_SHADOW_PROBE", probeOn ? "1" : null);
        VehiclePose pose;
        Check(!VehicleCapture.TryCapture(null, 1, 1, out pose));
        var item = new tosaveitemscript { car = new carscript(), RB = new UnityEngine.Rigidbody(), transform = new UnityEngine.Transform(), useThisForPos = new UnityEngine.Transform() };
        item.transform.position = new UnityEngine.Vector3 { x = 20 };
        item.useThisForPos.position = new UnityEngine.Vector3 { x = 2, y = 3, z = 4 };
        item.useThisForPos.rotation = new UnityEngine.Quaternion { w = 1 };
        item.RB.velocity = new UnityEngine.Vector3 { x = 5 };
        Check(!VehicleCapture.TryCapture(item, 0, 1, out pose));
        Check(VehicleCapture.TryCapture(item, 9, 3, out pose));
        Check(pose.Id == 9 && pose.Epoch == 3 && pose.X == 10002 && pose.Vx == 5);
        Check(item.transform.position.x == 20 && item.RB.velocity.x == 5);
        item.idInSave = 9;
        for (int i = 0; i < 100; i++) VehicleShadowProbe.Observe(item);
        Check(UnityEngine.Debug.Logs == (probeOn ? 64 : 0));
        if (probeOn) Check(UnityEngine.Debug.Last.Contains("global=10002,3,4") && UnityEngine.Debug.Last.Contains("anchorMinusRoot=-18,3,4"));
        Check(item.transform.position.x == 20 && item.RB.velocity.x == 5);
        item.RB = null; Check(!VehicleCapture.TryCapture(item, 9, 3, out pose));
        Console.WriteLine("PASS: read-only global vehicle capture with game stand-ins");
        return 0;
    }
}
