using System;
using TldMp;

// Retained fundamental primitives: finite-number guards, sequence wrap
// arithmetic and the structural native-packet validator. The removed movement
// wire/authority/interpolation tests live in archive/NetCoreMovementTests.cs.
public static class NetCoreTests
{
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
    static void Primitives()
    {
        Check(Numbers.Finite(0) && Numbers.Finite(-1.5) && !Numbers.Finite(Double.NaN) && !Numbers.Finite(Double.PositiveInfinity),"Finite rejects NaN and infinities");
        Check(Numbers.Newer(0,uint.MaxValue),"sequence wrap is newer");
        Check(!Numbers.Newer(uint.MaxValue,0),"pre-wrap delayed sequence is older");
        Check(Numbers.Newer(2,1) && !Numbers.Newer(1,2),"plain sequence ordering");
    }
    static void StructuralValidation()
    {
        // Stock native item pose: type + id + prefab + Vector3d + Vector3 = 45.
        var pose=new byte[45];pose[0]=16;
        Buffer.BlockCopy(BitConverter.GetBytes((uint)5),0,pose,1,4);
        Buffer.BlockCopy(BitConverter.GetBytes(7),0,pose,5,4);
        Buffer.BlockCopy(BitConverter.GetBytes(1000.5),0,pose,9,8);
        Check(StablePackets.Valid(pose),"native 45-byte item pose is structurally valid");
        Check(StablePackets.Item(pose)==5,"object ID is read from the native offset");
        var truncated=new byte[44];truncated[0]=16;
        Check(!StablePackets.Valid(truncated),"truncated item pose rejected");
        var nanPose=(byte[])pose.Clone();Buffer.BlockCopy(BitConverter.GetBytes(Double.NaN),0,nanPose,9,8);
        Check(!StablePackets.Valid(nanPose),"non-finite position rejected before native allocation");
        // Stock car pose: type + id + prefab + Vector3d + Vector3 + rank = 49.
        var car=new byte[49];car[0]=39;Buffer.BlockCopy(BitConverter.GetBytes((uint)9),0,car,1,4);Buffer.BlockCopy(BitConverter.GetBytes(371),0,car,5,4);
        Check(StablePackets.Valid(car) && StablePackets.Item(car)==9,"native 49-byte car pose is valid");
        Check(!StablePackets.Valid(new byte[50]),"extra trailing byte rejected");
        var velocity=new byte[33];velocity[0]=20;Buffer.BlockCopy(BitConverter.GetBytes((uint)9),0,velocity,1,4);
        Check(StablePackets.Valid(velocity) && StablePackets.Item(velocity)==9,"native 33-byte car velocity is valid");
        Check(!StablePackets.Valid(null) && !StablePackets.Valid(new byte[0]) && !StablePackets.Valid(new byte[]{49}),"null/empty/unknown types rejected");
        Check(StablePackets.Valid(new byte[]{1}) && StablePackets.Valid(new byte[]{2}) && StablePackets.Valid(new byte[]{9}),"zero-payload control types are valid");
        var oversized=new byte[60000];oversized[0]=3;Check(!StablePackets.Valid(oversized),"oversized packet rejected");
    }
    static void ReliabilityClassification()
    {
        Check(!StablePackets.IsReliable(new byte[]{16,0,0,0,0}),"item poses are unreliable");
        Check(!StablePackets.IsReliable(new byte[]{39,0,0,0,0}),"car poses are unreliable");
        Check(!StablePackets.IsReliable(new byte[]{20,0,0,0,0}),"car velocity is unreliable");
        Check(StablePackets.IsReliable(new byte[]{37,0,0,0,0}),"controls are reliable");
        Check(StablePackets.IsReliable(new byte[]{42,0,0,0,0}),"attachable changes are reliable");
        Check(StablePackets.IsReliable(null) && StablePackets.IsReliable(new byte[0]),"empty data defaults to reliable control framing");
    }
    public static int Main()
    {
        try { Primitives();StructuralValidation();ReliabilityClassification();Console.WriteLine("PASS: "+checks+" retained fundamental/structural checks (finite guards, sequence wrap, native packet framing bounds and stock reliability classes).");return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
