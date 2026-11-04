using System;
using System.IO;
namespace TldMp
{
    // One physics-step snapshot for the articulated bus pair (BusFront +
    // joint-connected BusBack). The receiver authenticates the transport
    // source and binds Origin/Epoch to the live session before Accept:
    // upstream the driver is the transport source and sends Generation=0 with
    // RelaySession=0; the host issues a monotonically increasing per-bus
    // authority Generation and stamps it plus its own session before relaying.
    internal sealed class BusPairSample
    {
        internal ulong Origin, RelaySession, Epoch, Generation;
        internal uint ID, Sequence, ClockMs;
        internal int Prefab, Role;
        internal double X,Y,Z;
        internal readonly float[] FrontRotation=new float[4], RearOffset=new float[3], RearRotation=new float[4];
        internal readonly float[] FrontVelocity=new float[3],FrontAngular=new float[3],RearVelocity=new float[3],RearAngular=new float[3];
    }
    internal static class BusPairProtocol
    {
        internal const byte Type=250, Version=3;
        // 2 header + Origin8 + RelaySession8 + Epoch8 + ID4 + Prefab4 + Seq4 +
        // Clock4 + Role4 + global3*8 + rot4*4 + offset3*4 + rot4*4 + 4*(3*4) +
        // Generation8 (appended, relay-issued).
        internal const int Size=170;
        internal const int OriginOffset=2, RelaySessionOffset=10, IDOffset=26, GenerationOffset=162;
        internal const int MaxRole=6;
        static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        static bool Finite(double v){return !double.IsNaN(v)&&!double.IsInfinity(v);}
        static bool Vector(float[] v,float max){double n=0;for(int i=0;i<v.Length;i++){if(!Finite(v[i]))return false;n+=(double)v[i]*v[i];}return n<=max*max;}
        static bool Rotation(float[] q){double n=0;for(int i=0;i<4;i++){if(!Finite(q[i]))return false;n+=(double)q[i]*q[i];}return n>=.98 && n<=1.02;}
        internal static bool Valid(BusPairSample s)
        {
            return s!=null && s.Origin!=0 && s.Epoch!=0 && s.ID!=0 && s.Sequence!=0 &&
                s.Prefab!=0 && s.Role>=0 && s.Role<=MaxRole &&
                Finite(s.X)&&Finite(s.Y)&&Finite(s.Z) &&
                Rotation(s.FrontRotation)&&Rotation(s.RearRotation)&&Vector(s.RearOffset,80f) &&
                Vector(s.FrontVelocity,150f)&&Vector(s.RearVelocity,150f)&&Vector(s.FrontAngular,25f)&&Vector(s.RearAngular,25f);
        }
        static void Write(BinaryWriter w,float[] a){foreach(float v in a)w.Write(v);}
        static void Read(BinaryReader r,float[] a){for(int i=0;i<a.Length;i++)a[i]=r.ReadSingle();}
        internal static byte[] Encode(BusPairSample s)
        {
            if(!Valid(s))return null;
            using(var m=new MemoryStream(Size))using(var w=new BinaryWriter(m))
            {w.Write(Type);w.Write(Version);w.Write(s.Origin);w.Write(s.RelaySession);w.Write(s.Epoch);w.Write(s.ID);w.Write(s.Prefab);w.Write(s.Sequence);w.Write(s.ClockMs);w.Write(s.Role);w.Write(s.X);w.Write(s.Y);w.Write(s.Z);
             Write(w,s.FrontRotation);Write(w,s.RearOffset);Write(w,s.RearRotation);Write(w,s.FrontVelocity);Write(w,s.FrontAngular);Write(w,s.RearVelocity);Write(w,s.RearAngular);w.Write(s.Generation);
             if(m.Length!=Size)throw new InvalidDataException("bus pair wire size drift");return m.ToArray();}
        }
        internal static bool Decode(byte[] bytes,out BusPairSample s)
        {
            s=null;if(bytes==null||bytes.Length!=Size)return false;
            try{using(var m=new MemoryStream(bytes,false))using(var r=new BinaryReader(m)){
                if(r.ReadByte()!=Type||r.ReadByte()!=Version)return false;
                var v=new BusPairSample{Origin=r.ReadUInt64(),RelaySession=r.ReadUInt64(),Epoch=r.ReadUInt64(),ID=r.ReadUInt32(),Prefab=r.ReadInt32(),Sequence=r.ReadUInt32(),ClockMs=r.ReadUInt32(),Role=r.ReadInt32(),X=r.ReadDouble(),Y=r.ReadDouble(),Z=r.ReadDouble()};
                Read(r,v.FrontRotation);Read(r,v.RearOffset);Read(r,v.RearRotation);Read(r,v.FrontVelocity);Read(r,v.FrontAngular);Read(r,v.RearVelocity);Read(r,v.RearAngular);v.Generation=r.ReadUInt64();
                if(!Valid(v))return false;s=v;return true;}}
            catch(EndOfStreamException){return false;}
        }
        // Structural validation for the packet validator (type, framing, field
        // ranges). Authority is decided by the session layer, never here.
        internal static bool ValidPacket(byte[] bytes)
        {
            BusPairSample s;return Decode(bytes,out s);
        }
        // The host stamps its session and the issued authority generation onto
        // a validated upstream packet before relaying. Identity is never rewritten.
        internal static bool StampRelay(byte[] bytes,ulong relaySession,ulong generation)
        {
            if(bytes==null||bytes.Length!=Size||bytes[0]!=Type||bytes[1]!=Version||relaySession==0||generation==0)return false;
            Buffer.BlockCopy(BitConverter.GetBytes(relaySession),0,bytes,RelaySessionOffset,8);
            Buffer.BlockCopy(BitConverter.GetBytes(generation),0,bytes,GenerationOffset,8);
            return true;
        }
        internal static bool Newer(uint value,uint previous){return value!=previous && unchecked((int)(value-previous))>0;}
    }
}
