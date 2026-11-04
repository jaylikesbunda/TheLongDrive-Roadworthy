using System;
namespace TldMp
{
    internal struct RabbitMotionSample
    {
        internal ulong Session;internal uint Sequence,Item;internal int Prefab;internal bool Active,Attacking,Roaring;
        internal double X,Y,Z;internal float Rx,Ry,Rz;
    }
    internal static class RabbitMotionCodec
    {
        internal const byte Type=251,Version=1;internal const int Size=59;
        static bool Fin(double n){return !Double.IsNaN(n)&&!Double.IsInfinity(n);}
        internal static bool Valid(RabbitMotionSample s)
        {
            return s.Session!=0 && s.Sequence!=0 && s.Item!=0 && s.Prefab>=0 && (s.Active||(!s.Attacking&&!s.Roaring)) &&
                Fin(s.X)&&Fin(s.Y)&&Fin(s.Z)&&Math.Abs(s.X)<=1e10&&Math.Abs(s.Y)<=1e10&&Math.Abs(s.Z)<=1e10&&
                Fin(s.Rx)&&Fin(s.Ry)&&Fin(s.Rz)&&Math.Abs(s.Rx)<=3600&&Math.Abs(s.Ry)<=3600&&Math.Abs(s.Rz)<=3600;
        }
        static void Put(byte[] b,int i,byte[] n){Buffer.BlockCopy(n,0,b,i,n.Length);}
        internal static byte[] Write(RabbitMotionSample s)
        {
            if(!Valid(s))throw new ArgumentException();var b=new byte[Size];b[0]=Type;b[1]=Version;b[2]=(byte)((s.Active?1:0)|(s.Attacking?2:0)|(s.Roaring?4:0));
            Put(b,3,BitConverter.GetBytes(s.Session));Put(b,11,BitConverter.GetBytes(s.Sequence));Put(b,15,BitConverter.GetBytes(s.Item));Put(b,19,BitConverter.GetBytes(s.Prefab));
            Put(b,23,BitConverter.GetBytes(s.X));Put(b,31,BitConverter.GetBytes(s.Y));Put(b,39,BitConverter.GetBytes(s.Z));
            Put(b,47,BitConverter.GetBytes(s.Rx));Put(b,51,BitConverter.GetBytes(s.Ry));Put(b,55,BitConverter.GetBytes(s.Rz));return b;
        }
        internal static bool Read(byte[] b,out RabbitMotionSample s)
        {
            s=default(RabbitMotionSample);if(b==null||b.Length!=Size||b[0]!=Type||b[1]!=Version||(b[2]&248)!=0)return false;
            s.Active=(b[2]&1)!=0;s.Attacking=(b[2]&2)!=0;s.Roaring=(b[2]&4)!=0;s.Session=BitConverter.ToUInt64(b,3);s.Sequence=BitConverter.ToUInt32(b,11);s.Item=BitConverter.ToUInt32(b,15);s.Prefab=BitConverter.ToInt32(b,19);
            s.X=BitConverter.ToDouble(b,23);s.Y=BitConverter.ToDouble(b,31);s.Z=BitConverter.ToDouble(b,39);
            s.Rx=BitConverter.ToSingle(b,47);s.Ry=BitConverter.ToSingle(b,51);s.Rz=BitConverter.ToSingle(b,55);return Valid(s);
        }
    }
}

