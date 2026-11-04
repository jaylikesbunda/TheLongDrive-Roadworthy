using System;
using System.Collections.Generic;

namespace TldMp
{
    // A cosmetic latest-state stream. The session is the authenticated immediate
    // sender's session; a relay rewrites it while retaining the true origin.
    internal static class SmokeCodec
    {
        internal const byte Type=252, Version=1, MaxIntensity=15;
        internal const int Size=23;
        internal static byte[] Encode(ulong origin,ulong session,uint sequence,byte intensity)
        {
            if(origin==0 || session==0 || sequence==0 || intensity>MaxIntensity)throw new ArgumentException();
            var b=new byte[Size];b[0]=Type;b[1]=Version;
            Buffer.BlockCopy(BitConverter.GetBytes(origin),0,b,2,8);
            Buffer.BlockCopy(BitConverter.GetBytes(session),0,b,10,8);
            Buffer.BlockCopy(BitConverter.GetBytes(sequence),0,b,18,4);b[22]=intensity;return b;
        }
        internal static bool Read(byte[] b,out ulong origin,out ulong session,out uint sequence,out byte intensity)
        {
            origin=session=0;sequence=0;intensity=0;
            if(b==null || b.Length!=Size || b[0]!=Type || b[1]!=Version)return false;
            origin=BitConverter.ToUInt64(b,2);session=BitConverter.ToUInt64(b,10);sequence=BitConverter.ToUInt32(b,18);intensity=b[22];
            return origin!=0 && session!=0 && sequence!=0 && intensity<=MaxIntensity;
        }
        internal static bool Valid(byte[] b){ulong o,s;uint q;byte i;return Read(b,out o,out s,out q,out i);}
        internal static bool ValidSender(bool host,ulong sender,ulong hostId,ulong origin,ulong session,ulong expected)
        {return sender!=0 && origin!=0 && session!=0 && session==expected && (host?origin==sender:sender==hostId);}
        internal static byte Quantize(float v)
        {if(Single.IsNaN(v) || Single.IsInfinity(v) || v<=0)return 0;if(v>=1)return MaxIntensity;return (byte)Math.Max(1,(int)Math.Round(v*MaxIntensity));}
    }
    internal sealed class SmokeRelaySequences
    {
        readonly Dictionary<ulong,uint> sequences=new Dictionary<ulong,uint>();
        internal uint Next(ulong origin){uint n;sequences.TryGetValue(origin,out n);n++;if(n==0)n++;sequences[origin]=n;return n;}
        internal void Clear(){sequences.Clear();}
    }
    internal sealed class SmokeState
    {
        internal const double Refresh=.5, Expiry=2.0;
        internal sealed class Sample {internal ulong Session;internal uint Sequence;internal byte Intensity;internal double Seen;}
        readonly Dictionary<ulong,Sample> samples=new Dictionary<ulong,Sample>();
        internal bool Accept(ulong origin,ulong session,uint sequence,byte intensity,double now)
        {
            if(origin==0 || session==0 || sequence==0 || intensity>SmokeCodec.MaxIntensity || Double.IsNaN(now) || Double.IsInfinity(now))return false;
            Sample old;if(samples.TryGetValue(origin,out old))
            {if(old.Session==session && !Numbers.Newer(sequence,old.Sequence))return false;old.Session=session;old.Sequence=sequence;old.Intensity=intensity;old.Seen=now;}
            else samples.Add(origin,new Sample{Session=session,Sequence=sequence,Intensity=intensity,Seen=now});
            return true;
        }
        internal byte Get(ulong origin,double now)
        {Sample s;return samples.TryGetValue(origin,out s) && now>=s.Seen && now-s.Seen<Expiry?s.Intensity:(byte)0;}
        internal void Remove(ulong origin){samples.Remove(origin);}
        internal void Clear(){samples.Clear();}
    }
}


