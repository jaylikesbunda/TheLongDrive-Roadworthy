using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace TldMp
{
    // Compact, typed wire encoding for road manifests. Roads are the largest
    // map payloads and their DTO shape is fixed; keeping POI/object payloads on
    // StableJson avoids duplicating their evolving state formats.
    public static class StableRoadCodec
    {
        const int MaxExpandedBytes = 4 * 1024 * 1024;
        const int MaxStringBytes = 256;
        const int MaxRoadDots = 100000;
        const int MaxRoadLines = 100000;
        const int MaxRoadElements = 250000;
        const int MaxInnerChunks = 100000;
        const int MaxIndices = 1000000;
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("TLDRD\x01");

        public static bool HasHeader(byte[] bytes)
        {
            if (bytes == null || bytes.Length < Magic.Length) return false;
            for (int i = 0; i < Magic.Length; i++) if (bytes[i] != Magic[i]) return false;
            return true;
        }

        public static bool HasMagic(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 5 && bytes[0] == (byte)'T' && bytes[1] == (byte)'L' &&
                bytes[2] == (byte)'D' && bytes[3] == (byte)'R' && bytes[4] == (byte)'D';
        }

        public static byte[] Write(StableMap.Chunk chunk)
        {
            if (chunk == null || chunk.Road == null || chunk.Key == null || chunk.Key.Length > MaxStringBytes ||
                !chunk.Key.StartsWith("r|", StringComparison.Ordinal) || chunk.Pois == null || chunk.Pois.Count != 0 ||
                chunk.Objects == null || chunk.Objects.Count != 0 || !chunk.PoisComplete)
                throw new InvalidOperationException("Invalid road manifest.");
            using (var raw = new MemoryStream())
            using (var writer = new BinaryWriter(raw, Encoding.UTF8, true))
            {
                writer.Write(chunk.Version); writer.Write(chunk.Seed); WriteString(writer, chunk.Key); WriteVector3d(writer, chunk.Position);
                WriteRoad(writer, chunk.Road);
                writer.Flush();
                if (raw.Length > MaxExpandedBytes) throw new InvalidOperationException("Expanded road payload exceeds transfer limit.");
                using (var compressed = new MemoryStream())
                {
                    raw.Position = 0;
                    using (var deflate = new DeflateStream(compressed, CompressionMode.Compress, true)) raw.CopyTo(deflate);
                    byte[] body = compressed.ToArray();
                    using (var packed = new MemoryStream())
                    {
                        packed.Write(Magic, 0, Magic.Length);
                        WriteInt(packed, (int)raw.Length); WriteInt(packed, body.Length); WriteUInt(packed, Hash(raw));
                        packed.Write(body, 0, body.Length);
                        if (packed.Length > StableNode.MaxSnapshotBytes) throw new InvalidOperationException("Road payload exceeds transfer limit.");
                        return packed.ToArray();
                    }
                }
            }
        }

        public static StableMap.Chunk Read(byte[] bytes)
        {
            if (!HasHeader(bytes)) throw new InvalidOperationException("Road payload marker/version mismatch.");
            const int headerLength = 18;
            if (bytes.Length < headerLength) throw new InvalidOperationException("Truncated road payload.");
            int size = BitConverter.ToInt32(bytes, Magic.Length);
            if (size < 0 || size > MaxExpandedBytes) throw new InvalidOperationException("Expanded road payload exceeds transfer limit.");
            int compressedLength = BitConverter.ToInt32(bytes, Magic.Length + 4);
            uint expectedHash = BitConverter.ToUInt32(bytes, Magic.Length + 8);
            if (compressedLength < 0 || compressedLength != bytes.Length - headerLength) throw new InvalidOperationException("Road payload compressed length mismatch.");
            using (var input = new MemoryStream(bytes, headerLength, compressedLength, false))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var raw = new MemoryStream(size))
            {
                byte[] buffer = new byte[8192]; int n;
                while ((n = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (raw.Length + n > size) throw new InvalidOperationException("Road payload length mismatch.");
                    raw.Write(buffer, 0, n);
                }
                if (raw.Length != size) throw new InvalidOperationException("Truncated road payload.");
                if (Hash(raw) != expectedHash) throw new InvalidOperationException("Road payload checksum mismatch.");
                raw.Position = 0;
                using (var reader = new BinaryReader(raw, Encoding.UTF8, true))
                {
                    var chunk = new StableMap.Chunk { Version = reader.ReadInt32(), Seed = reader.ReadInt32(), Key = ReadString(reader), Position = ReadVector3d(reader) };
                    if (chunk.Version != 1 || chunk.Key == null || !chunk.Key.StartsWith("r|", StringComparison.Ordinal))
                        throw new InvalidOperationException("Road payload identity mismatch.");
                    chunk.Road = ReadRoad(reader);
                    if (raw.Position != raw.Length) throw new InvalidOperationException("Trailing road payload data.");
                    return chunk;
                }
            }
        }

        static void WriteRoad(BinaryWriter w, StableMap.Road r)
        {
            if (r.Dots == null || r.Lines == null || r.Inner == null) throw new InvalidOperationException("Required road lists are missing.");
            w.Write(r.Seed); w.Write(r.Inside); w.Write(r.U); w.Write(r.R); WriteInts(w, r.Main);
            WriteCount(w, r.Dots.Count, MaxRoadDots);
            foreach (var d in r.Dots)
            { if (d == null || d.Connected == null) throw new InvalidOperationException("Invalid road dot."); WriteVector3d(w, d.Pos); WriteInts(w, d.Connected); }
            WriteCount(w, r.Lines.Count, MaxRoadLines);
            int elements = 0;
            foreach (var l in r.Lines)
            {
                if (l == null || l.Elements == null) throw new InvalidOperationException("Invalid road line.");
                WriteVector3d(w, l.Self); w.Write(l.ID); WriteVector3d(w, l.C1); WriteVector3d(w, l.C2);
                w.Write(l.I1); w.Write(l.I2); WriteVector3d(w, l.Start); WriteVector3d(w, l.End); WriteFiniteSingle(w, l.Min); WriteFiniteSingle(w, l.Max); w.Write(l.Type);
                WriteCount(w, l.Elements.Count, MaxRoadElements - elements);
                foreach (var e in l.Elements)
                {
                    if (e == null || e.Bones == null) throw new InvalidOperationException("Invalid road element.");
                    elements++; w.Write(e.Biome); w.Write(e.Road); WriteVector3ds(w, e.Bones, MaxIndices);
                }
            }
            WriteCount(w, r.Inner.Count, MaxInnerChunks);
            foreach (var inner in r.Inner)
            { if (inner == null || inner.Roads == null) throw new InvalidOperationException("Invalid inner road chunk."); WriteVector3d(w, inner.Pos); WriteRoads(w, inner.Roads); }
        }

        static StableMap.Road ReadRoad(BinaryReader r)
        {
            var road = new StableMap.Road { Seed = r.ReadInt32(), Inside = r.ReadBoolean(), U = r.ReadBoolean(), R = r.ReadBoolean(), Main = ReadInts(r, MaxIndices) };
            int count = ReadRequiredCount(r, MaxRoadDots); for (int i = 0; i < count; i++) road.Dots.Add(new StableMap.Dot { Pos = ReadVector3d(r), Connected = ReadRequiredInts(r, MaxIndices) });
            count = ReadRequiredCount(r, MaxRoadLines); int elements = 0;
            for (int i = 0; i < count; i++)
            {
                var line = new StableMap.Line { Self = ReadVector3d(r), ID = r.ReadInt32(), C1 = ReadVector3d(r), C2 = ReadVector3d(r), I1 = r.ReadInt32(), I2 = r.ReadInt32(), Start = ReadVector3d(r), End = ReadVector3d(r), Min = ReadFiniteSingle(r), Max = ReadFiniteSingle(r), Type = r.ReadInt32() };
                int ec = ReadRequiredCount(r, MaxRoadElements - elements); for (int j = 0; j < ec; j++)
                { elements++; line.Elements.Add(new StableMap.Element { Biome = r.ReadInt32(), Road = r.ReadInt32(), Bones = ReadRequiredVector3ds(r, MaxIndices) }); }
                road.Lines.Add(line);
            }
            count = ReadRequiredCount(r, MaxInnerChunks); for (int i = 0; i < count; i++) road.Inner.Add(new StableMap.Inner { Pos = ReadVector3d(r), Roads = ReadRoads(r, MaxIndices) });
            return road;
        }

        static void WriteInts(BinaryWriter w, List<int> values)
        { WriteCount(w, values == null ? -1 : values.Count, MaxIndices); if (values != null) foreach (int value in values) w.Write(value); }
        static List<int> ReadInts(BinaryReader r, int max)
        { int count = ReadCount(r, max); if (count < 0) return null; EnsureRemaining(r, (long)count * 4); var result = new List<int>(count); for (int i = 0; i < count; i++) result.Add(r.ReadInt32()); return result; }
        static List<int> ReadRequiredInts(BinaryReader r, int max) { int count = ReadRequiredCount(r, max); EnsureRemaining(r, (long)count * 4); var result = new List<int>(count); for (int i = 0; i < count; i++) result.Add(r.ReadInt32()); return result; }
        static void WriteRoads(BinaryWriter w, List<roadGenScript.roadChunk> values)
        { WriteCount(w, values.Count, MaxIndices); foreach (var v in values) { if (v == null || v.roadElements == null) throw new InvalidOperationException("Invalid inner road reference."); w.Write(v.roadId); WriteInts(w, v.roadElements); } }
        static List<roadGenScript.roadChunk> ReadRoads(BinaryReader r, int max)
        { int count = ReadRequiredCount(r, max); EnsureRemaining(r, (long)count * 8); var result = new List<roadGenScript.roadChunk>(count); for (int i = 0; i < count; i++) result.Add(new roadGenScript.roadChunk { roadId = r.ReadInt32(), roadElements = ReadRequiredInts(r, MaxIndices) }); return result; }
        static void WriteVector3ds(BinaryWriter w, List<Vector3d> values, int max)
        { WriteCount(w, values.Count, max); foreach (var value in values) WriteVector3d(w, value); }
        static List<Vector3d> ReadRequiredVector3ds(BinaryReader r, int max) { int count = ReadRequiredCount(r, max); EnsureRemaining(r, (long)count * 24); var result = new List<Vector3d>(count); for (int i = 0; i < count; i++) result.Add(ReadVector3d(r)); return result; }
        static void WriteVector3d(BinaryWriter w, Vector3d v) { if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z)) throw new InvalidOperationException("Non-finite road coordinate."); w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3d ReadVector3d(BinaryReader r) { double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble(); if (!Finite(x) || !Finite(y) || !Finite(z)) throw new InvalidOperationException("Non-finite road coordinate."); return new Vector3d(x, y, z); }
        static float ReadFiniteSingle(BinaryReader r) { float value = r.ReadSingle(); if (Single.IsNaN(value) || Single.IsInfinity(value)) throw new InvalidOperationException("Non-finite road value."); return value; }
        static void WriteFiniteSingle(BinaryWriter w, float value) { if (Single.IsNaN(value) || Single.IsInfinity(value)) throw new InvalidOperationException("Non-finite road value."); w.Write(value); }
        static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        static uint Hash(Stream stream)
        {
            long oldPosition = stream.Position; stream.Position = 0; uint hash = 2166136261;
            int value; while ((value = stream.ReadByte()) >= 0) { hash ^= (byte)value; hash *= 16777619; }
            stream.Position = oldPosition; return hash;
        }
        static void WriteInt(Stream stream, int value) { byte[] bytes = BitConverter.GetBytes(value); stream.Write(bytes, 0, bytes.Length); }
        static void WriteUInt(Stream stream, uint value) { byte[] bytes = BitConverter.GetBytes(value); stream.Write(bytes, 0, bytes.Length); }
        static void WriteString(BinaryWriter w, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); if (bytes.Length > MaxStringBytes) throw new InvalidOperationException("Road identity exceeds limit."); w.Write(bytes.Length); w.Write(bytes); }
        static string ReadString(BinaryReader r) { int count = r.ReadInt32(); if (count < 0 || count > MaxStringBytes) throw new InvalidOperationException("Road identity exceeds limit."); EnsureRemaining(r, count); return Encoding.UTF8.GetString(r.ReadBytes(count)); }
        static void WriteCount(BinaryWriter w, int count, int max) { if (count < -1 || count > max) throw new InvalidOperationException("Road list count exceeds limit."); w.Write(count); }
        static int ReadCount(BinaryReader r, int max) { int count = r.ReadInt32(); if (count < -1 || count > max) throw new InvalidOperationException("Road list count exceeds limit."); return count; }
        static int ReadRequiredCount(BinaryReader r, int max) { int count = ReadCount(r, max); if (count < 0) throw new InvalidOperationException("Required road list is missing."); return count; }
        static void EnsureRemaining(BinaryReader r, long needed) { if (needed < 0 || needed > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidOperationException("Truncated road payload."); }
    }
}
