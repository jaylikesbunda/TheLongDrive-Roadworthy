using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TldMp;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Extracts the exact native randomFlyer.Upd method body (baseline or candidate)
// into Unity-shaped data-only stand-ins. This checks managed IL control flow
// and setter arguments; it does not emulate Unity's native physics engine.
public static class FlyerProductionBehaviorTests
{
    static ModuleDefinition target;
    static TypeDefinition sourceFlyer;
    static TypeDefinition destFlyer;
    static int checks;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new Exception("Usage: FlyerProductionBehaviorTests Assembly-CSharp.dll baseline|candidate");
            string role = args[1];
            string output = Path.Combine(Path.GetTempPath(), "NativeFlyerExtracted-" + Guid.NewGuid().ToString("N") + ".dll");
            using (var fixture = AssemblyDefinition.ReadAssembly(Assembly.GetExecutingAssembly().Location))
            using (var source = AssemblyDefinition.ReadAssembly(args[0]))
            {
                target = fixture.MainModule;
                sourceFlyer = source.MainModule.GetTypes().Single(t => t.FullName == "randomFlyer");
                destFlyer = target.GetTypes().Single(t => t.FullName == "randomFlyer");
                var src = sourceFlyer.Methods.Single(m => m.Name == "Upd" && m.Parameters.Count == 0);
                var dst = destFlyer.Methods.Single(m => m.Name == "Run" && m.Parameters.Count == 0);
                CopyBody(src, dst, role == "candidate");
                fixture.Name.Name = "NativeFlyerExtracted" + Guid.NewGuid().ToString("N");
                fixture.Write(output);
            }
            var extracted = Assembly.LoadFrom(output);
            var flyerType = extracted.GetType("randomFlyer", true);
            RunDynamic(flyerType);
            RunKinematic(flyerType, role == "candidate");
            RunTransitions(flyerType, role == "candidate");
            RunNullReceiver(flyerType);
            Console.WriteLine("PASS exact production randomFlyer.Upd IL role=" + role + " checks=" + checks + " (managed stand-in behavior only; no Unity/PhysX claim)");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }

    static void CopyBody(MethodDefinition src, MethodDefinition dst, bool candidate)
    {
        int guardCount = src.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "NoteFlyerKinematicSkipped");
        if (candidate && guardCount != 1) throw new Exception("Expected exactly one candidate kinematic-skip call, found " + guardCount);
        if (!candidate && guardCount != 0) throw new Exception("Baseline unexpectedly contains kinematic-skip call");
        dst.Body = new Mono.Cecil.Cil.MethodBody(dst) { InitLocals = src.Body.InitLocals, MaxStackSize = src.Body.MaxStackSize };
        var vars = new Dictionary<VariableDefinition, VariableDefinition>();
        foreach (var v in src.Body.Variables) { var nv = new VariableDefinition(MapType(v.VariableType)); vars.Add(v, nv); dst.Body.Variables.Add(nv); }
        var map = new Dictionary<Instruction, Instruction>();
        foreach (var old in src.Body.Instructions) { var copy = Instruction.Create(OpCodes.Nop); copy.OpCode = old.OpCode; dst.Body.Instructions.Add(copy); map.Add(old, copy); }
        foreach (var old in src.Body.Instructions) map[old].Operand = MapOperand(old.Operand, vars, map);
        Check(dst.Body.Instructions.Count == src.Body.Instructions.Count, "Extracted native body instruction count changed");
    }

    static TypeReference MapType(TypeReference t)
    {
        var by = t as ByReferenceType; if (by != null) return new ByReferenceType(MapType(by.ElementType));
        var ptr = t as PointerType; if (ptr != null) return new PointerType(MapType(ptr.ElementType));
        var arr = t as ArrayType; if (arr != null) return new ArrayType(MapType(arr.ElementType), arr.Rank);
        if (t.FullName == "randomFlyer") return destFlyer;
        var local = target.GetTypes().SingleOrDefault(x => x.FullName == t.FullName);
        return local ?? target.ImportReference(t);
    }

    static object MapOperand(object operand, Dictionary<VariableDefinition, VariableDefinition> vars, Dictionary<Instruction, Instruction> map)
    {
        if (operand == null) return null;
        var instruction = operand as Instruction; if (instruction != null) return map[instruction];
        var branches = operand as Instruction[]; if (branches != null) return branches.Select(b => map[b]).ToArray();
        var variable = operand as VariableDefinition; if (variable != null) return vars[variable];
        var typeReference = operand as TypeReference; if (typeReference != null) return MapType(typeReference);
        var fieldReference = operand as FieldReference;
        if (fieldReference != null)
        {
            var owner = MapType(fieldReference.DeclaringType);
            var td = owner as TypeDefinition;
            if (td != null)
            {
                var field = td.Fields.SingleOrDefault(x => x.Name == fieldReference.Name);
                if (field == null) throw new Exception("Fixture field not found for " + fieldReference.FullName);
                return field;
            }
            return target.ImportReference(fieldReference);
        }
        var methodReference = operand as MethodReference;
        if (methodReference != null)
        {
            var owner = MapType(methodReference.DeclaringType);
            var td = owner as TypeDefinition;
            if (td != null)
            {
                var candidates = td.Methods.Where(x => x.Name == methodReference.Name && x.Parameters.Count == methodReference.Parameters.Count).ToArray();
                if (candidates.Length != 1) throw new Exception("Fixture method lookup for " + methodReference.FullName + " found " + candidates.Length);
                return candidates[0];
            }
            return target.ImportReference(methodReference);
        }
        return operand;
    }

    static object New(Type t) { return Activator.CreateInstance(t); }
    static void Set(object o, string field, object value) { o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(o, value); }
    static object Get(object o, string field) { return o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(o); }
    static Type U(Type flyer, string name) { return flyer.Assembly.GetType("UnityEngine." + name, true); }
    static object V(Type flyer, float x, float y, float z) { return Activator.CreateInstance(U(flyer, "Vector3"), new object[] { x, y, z }); }
    static string Vec(object v) { var t = v.GetType(); return t.GetField("x").GetValue(v) + "," + t.GetField("y").GetValue(v) + "," + t.GetField("z").GetValue(v); }
    static string Quat(object q) { var t = q.GetType(); return t.GetField("x").GetValue(q) + "," + t.GetField("y").GetValue(q) + "," + t.GetField("z").GetValue(q) + "," + t.GetField("w").GetValue(q); }
    static float C(object v,string f) { return (float)v.GetType().GetField(f).GetValue(v); }
    static bool VE(object a,object b) { return C(a,"x")==C(b,"x") && C(a,"y")==C(b,"y") && C(a,"z")==C(b,"z"); }
    static object Clamp(Type ft,float x,float y,float z,float max) { float sq=x*x+y*y+z*z; if(sq>max*max){float k=max/(float)Math.Sqrt(sq);x*=k;y*=k;z*=k;}return V(ft,x,y,z); }
    static object Lerp(Type ft,object a,object b,float t) { t=Math.Max(0,Math.Min(1,t));return V(ft,C(a,"x")+(C(b,"x")-C(a,"x"))*t,C(a,"y")+(C(b,"y")-C(a,"y"))*t,C(a,"z")+(C(b,"z")-C(a,"z"))*t); }
    static void ResetBridge(Type ft) { ft.Assembly.GetType("TldMp.StableGameBridge",true).GetField("SkipCount").SetValue(null,0); }
    static int SkipCount(Type ft) { return (int)ft.Assembly.GetType("TldMp.StableGameBridge",true).GetField("SkipCount").GetValue(null); }
    static void Invoke(object flyer) { flyer.GetType().GetMethod("Run").Invoke(flyer, null); }
    static object Setup(Type flyerType,bool kinematic)
    {
        var f = Activator.CreateInstance(flyerType);
        var body = Activator.CreateInstance(U(flyerType,"Rigidbody")); Set(body,"isKinematic",kinematic); Set(body,"velocity",V(flyerType,4,-2,7));
        var target = Activator.CreateInstance(U(flyerType,"Transform")); Set(target,"position",V(flyerType,12,8,-3));
        Set(target,"rotation",Activator.CreateInstance(U(flyerType,"Quaternion"),new object[]{0f,0f,0f,1f}));
        var transform = Get(f,"transform"); Set(f, "rb", body); Set(f, "target", target);
        Set(transform,"position",V(flyerType,2,1,5)); Set(transform,"rotation",Activator.CreateInstance(U(flyerType,"Quaternion"),new object[]{0f,0f,0f,1f}));
        Set(f, "currentSpeed", 6f); Set(f, "velocityLerp", 0.35f); Set(f, "rotLerp", 0.2f);
        return f;
    }

    static void RunDynamic(Type flyerType)
    {
        ResetBridge(flyerType); var f = Setup(flyerType,false); var rb = Get(f, "rb");
        var expectedDir = Clamp(flyerType,10,7,-8,6f);
        var expected = Lerp(flyerType,V(flyerType,4,-2,7),expectedDir,0.016f*0.35f);
        Invoke(f);
        Check((int)Get(rb,"SetCalls") == 1 && (int)Get(rb,"SetAttempts") == 1, "Dynamic native body should invoke velocity setter exactly once");
        Check(VE(Get(rb,"velocity"),expected), "Dynamic setter did not receive the expected Lerp(previousVelocity, clampedDirection, deltaTime * velocityLerp) result");
        Check(SkipCount(flyerType) == 0, "Dynamic body incremented the kinematic-skip counter");
        var tr=Get(f,"transform"); Check((int)Get(tr,"RotationCalls")==1,"Nonzero direction should retain native rotation update");
        Check(VE(Get(f,"dir"),expectedDir), "Direction subtraction/clamp changed");
        Console.WriteLine("dynamicOut=" + Vec(Get(rb,"velocity")) + ";dir=" + Vec(Get(f, "dir")) + ";rot=" + Quat(Get(tr,"rotation")));
    }

    static void RunKinematic(Type flyerType, bool candidate)
    {
        ResetBridge(flyerType); var f = Setup(flyerType,true); var rb = Get(f, "rb"); var initial = Get(rb,"velocity");
        Invoke(f);
        Check(VE(Get(rb,"velocity"),initial), "Kinematic Unity stand-in setter should have no effect");
        Check((int)Get(rb,"SetAttempts") == (candidate ? 0 : 1), "Unexpected velocity-setter attempt count for kinematic role=" + candidate);
        Check((int)Get(rb,"SetCalls") == 0, "Kinematic setter must not change the body velocity");
        Check(SkipCount(flyerType) == (candidate ? 1 : 0), "Kinematic skip counter count differs");
        var tr=Get(f,"transform"); Check((int)Get(tr,"RotationCalls")==1,"Kinematic guard changed native rotation path");
        Check(VE(Get(f,"dir"),Clamp(flyerType,10,7,-8,6f)), "Kinematic guard changed direction calculation");
        Console.WriteLine("kinematicOut=" + Vec(Get(rb,"velocity")) + ";dir=" + Vec(Get(f, "dir")) + ";rot=" + Quat(Get(tr,"rotation")));
    }

    static void RunTransitions(Type flyerType, bool candidate)
    {
        ResetBridge(flyerType); var f = Setup(flyerType,false); var rb = Get(f, "rb");
        Invoke(f); var afterDynamic = Get(rb,"velocity");
        Set(rb,"isKinematic",true); Invoke(f); var afterKinematic = Get(rb,"velocity");
        Set(rb,"isKinematic",false); Invoke(f);
        Check(!VE(afterDynamic,V(flyerType,4,-2,7)), "Dynamic entry should calculate and assign new velocity");
        Check(VE(afterKinematic,afterDynamic), "Kinematic middle entry must preserve current body velocity");
        Check((int)Get(rb,"SetAttempts") == (candidate ? 2 : 3), "Dynamic/kinematic/dynamic transition setter count mismatch");
        Check(SkipCount(flyerType) == (candidate ? 1 : 0), "Transition skip counter should increment only once for candidate");
        Check((int)Get(Get(f,"transform"),"RotationCalls")==3,"Transition sequence changed rotation update cadence");
        Console.WriteLine("transitionOut=" + Vec(Get(rb,"velocity")));
    }

    static void RunNullReceiver(Type flyerType)
    {
        ResetBridge(flyerType); var f = Setup(flyerType,false); Set(f, "rb", null);
        Exception got = null; try { Invoke(f); } catch (TargetInvocationException e) { got = e.InnerException; }
        Check(got is NullReferenceException, "Null Rigidbody must preserve native receiver exception path");
        Check(SkipCount(flyerType) == 0, "Null receiver must not record a kinematic skip");
    }
}

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0, 0, 0); } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static bool operator !=(Vector3 a, Vector3 b) { return a.x != b.x || a.y != b.y || a.z != b.z; }
        public static bool operator ==(Vector3 a, Vector3 b) { return !(a != b); }
        public override bool Equals(object o) { return o is Vector3 && this == (Vector3)o; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
        public static Vector3 ClampMagnitude(Vector3 v, float max) { float sq = v.x*v.x + v.y*v.y + v.z*v.z; if (sq > max*max) { float scale = max / (float)Math.Sqrt(sq); return new Vector3(v.x*scale,v.y*scale,v.z*scale); } return v; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Math.Max(0, Math.Min(1, t)); return new Vector3(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t); }
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}
        public static Quaternion Identity { get { return new Quaternion(0,0,0,1); } }
        public static Quaternion LookRotation(Vector3 d) { return new Quaternion(d.x,d.y,d.z,1); }
        public static Quaternion Lerp(Quaternion a,Quaternion b,float t){t=Math.Max(0,Math.Min(1,t));return new Quaternion(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t,a.w+(b.w-a.w)*t);}
    }
    public class Component { public Transform transform = new Transform(); public Transform get_transform() { return transform; } }
    public class MonoBehaviour : Component { }
    public class Transform
    {
        public Vector3 position; public Quaternion rotation; public int RotationCalls;
        public Vector3 get_position() { return position; }
        public Quaternion get_rotation() { return rotation; }
        public void set_rotation(Quaternion q) { rotation=q; RotationCalls++; }
    }
    public class Rigidbody
    {
        public Vector3 velocity; public bool isKinematic; public int SetCalls,SetAttempts;
        public Vector3 get_velocity() { return velocity; }
        public bool get_isKinematic() { return isKinematic; }
        public void set_velocity(Vector3 v) { SetAttempts++; if(!isKinematic){SetCalls++;velocity=v;} }
    }
    public static class Time { public static float deltaTime=0.016f; public static float get_deltaTime(){return deltaTime;} }
}

namespace TldMp { public static class StableGameBridge { public static int SkipCount; public static void Reset(){SkipCount=0;} public static void NoteFlyerKinematicSkipped(){SkipCount++;} } }

public class randomFlyer : UnityEngine.MonoBehaviour
{
    public UnityEngine.Vector3 dir; public UnityEngine.Rigidbody rb; public UnityEngine.Transform start,target;
    public float currentSpeed,velocityLerp,rotLerp;
    public void Run() { }
}
