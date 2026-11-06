using System;
using System.IO;
using System.Text;

namespace TldMp.Experimental
{
    // Development wire representation. Positions are global, never Unity's
    // shifted local coordinates. Admission/ownership and sequence checks occur
    // before a validated pose is applied to the game.
    internal sealed class VehiclePose
    {
        internal ulong Id, Epoch;
        internal double X, Y, Z;
        internal float Qx, Qy, Qz, Qw, Vx, Vy, Vz;
    }

    internal static class VehiclePoseCodec
    {
        internal const int Length = 8 + 8 + 3 * 8 + 7 * 4;
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        internal static bool Valid(VehiclePose p)
        {
            if (p == null || p.Id == 0 || p.Epoch == 0 || !Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) ||
                !Finite(p.Qx) || !Finite(p.Qy) || !Finite(p.Qz) || !Finite(p.Qw) ||
                !Finite(p.Vx) || !Finite(p.Vy) || !Finite(p.Vz)) return false;
            double norm = (double)p.Qx * p.Qx + (double)p.Qy * p.Qy + (double)p.Qz * p.Qz + (double)p.Qw * p.Qw;
            return norm >= 0.98 && norm <= 1.02;
        }
        internal static byte[] Write(VehiclePose p)
        {
            if (!Valid(p)) throw new ArgumentException("Invalid vehicle pose.");
            using (var stream = new MemoryStream(Length))
            using (var w = new BinaryWriter(stream, Encoding.UTF8))
            {
                w.Write(p.Id); w.Write(p.Epoch); w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
                w.Write(p.Qx); w.Write(p.Qy); w.Write(p.Qz); w.Write(p.Qw); w.Write(p.Vx); w.Write(p.Vy); w.Write(p.Vz);
                return stream.ToArray();
            }
        }
        internal static bool Read(byte[] bytes, out VehiclePose pose)
        {
            pose = null;
            if (bytes == null || bytes.Length != Length) return false;
            using (var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
            {
                var p = new VehiclePose { Id = r.ReadUInt64(), Epoch = r.ReadUInt64(), X = r.ReadDouble(), Y = r.ReadDouble(), Z = r.ReadDouble(),
                    Qx = r.ReadSingle(), Qy = r.ReadSingle(), Qz = r.ReadSingle(), Qw = r.ReadSingle(), Vx = r.ReadSingle(), Vy = r.ReadSingle(), Vz = r.ReadSingle() };
                if (!Valid(p)) return false;
                pose = p; return true;
            }
        }
    }
}
