using System;
using System.Collections.Generic;

namespace TldMp
{
    // Structural packet validation only: message type, exact framing, field
    // kinds and bounded array/string lengths. No owner, epoch, lease or session
    // rejection of native traffic. Symbols: i=int/u32, f=float, d=double,
    // b=bool, t=u64, y=byte, v=Vector3, g=Vector3d, c=Color, s=UTF16 string,
    // [x]=array.
    public static class StablePackets
    {
        // The native password exchange precedes Roadworthy's compatibility
        // handshake. Only its two native message types may bypass that gate.
        public static bool PasswordBootstrapAllowed(bool isHost,ulong own,ulong host,ulong target,bool member,byte[] bytes)
        {
            if(!member || bytes==null || bytes.Length==0 || !Valid(bytes))return false;
            return isHost ? target!=own && (bytes[0]==5 || bytes[0]==6)
                          : target==host && bytes[0]==5;
        }
        sealed class Cursor
        {
            readonly byte[] b;public int P=1;public Cursor(byte[] data){b=data;}
            void Take(int n){if(n<0 || n>b.Length-P)throw new ArgumentException();P+=n;}
            public int Int(){int p=P;Take(4);return BitConverter.ToInt32(b,p);}
            public int Fields(string spec,int start,int stop)
            {
                for(int i=start;i<stop;i++)
                {
                    char c=spec[i];int p=P;
                    switch(c)
                    {
                        case 'i':Take(4);break;
                        case 't':Take(8);break;
                        case 'y':Take(1);break;
                        case 'b':Take(1);if(b[p]>1)throw new ArgumentException();break;
                        case 'f':Take(4);if(!Numbers.Finite(BitConverter.ToSingle(b,p)))throw new ArgumentException();break;
                        case 'd':Take(8);if(!Numbers.Finite(BitConverter.ToDouble(b,p)))throw new ArgumentException();break;
                        case 'v':Fields("fff",0,3);break;
                        case 'g':Fields("ddd",0,3);break;
                        case 'c':Fields("ffff",0,4);break;
                        case 's':int len=Int();if(len<0 || len>16384)throw new ArgumentException();Take(len*2);break;
                        case '[':
                            int end=spec.IndexOf(']',i);if(end<0)throw new ArgumentException();int count=Int();if(count<0 || count>4096)throw new ArgumentException();
                            for(int j=0;j<count;j++)Fields(spec,i+1,end);i=end;break;
                        default:throw new ArgumentException();
                    }
                }
                return P;
            }
        }
        public static bool Valid(byte[] b)
        {
            if(b!=null && b.Length>0 && b[0]==VehicleMotionCodec.Type)return VehicleMotionCodec.Valid(b);
            if(b==null || b.Length==0 || b.Length>58000 || b[0]>48)return false;
            if((b[0]==10 || b[0]==18 || b[0]==40 || b[0]==41 || b[0]==45) && b.Length<3)return false;
            string spec;
            switch(b[0])
            {
                case 1:case 2:case 9:case 13:spec="";break;
                case 3:spec="ts";break;
                case 4:spec="t[f]i";break;
                case 5:spec="s";break;
                case 6:spec="y";break;
                case 7:spec="[t]";break;
                case 8:spec="issig";if(b.Length>4 && BitConverter.ToInt32(b,1)==2)spec+="bbiffii";break;
                case 10:spec="bbtgvf";if(b[1]!=0)spec+=b[2]!=0?"igi":"ii";break;
                case 11:spec="tvvvvvvvvvvvvbfy";break;
                case 12:spec="ti[b][c]";break;
                case 14:spec="f";break;
                case 15:spec="[i][f][i][f]";break;
                case 16:spec="iigv";break;
                case 17:spec="iiivv";break;
                case 18:spec="btivvi";if(b[1]!=0)spec+="vv";break;
                case 19:spec="ivv";break;
                case 20:spec="ivvi";break;
                case 21:case 36:case 46:spec="i";break;
                case 22:spec="ig";break;
                case 23:spec="iiiiiic";break;
                case 24:spec="is";break;
                case 25:spec="i[b][b]";break;
                case 26:case 31:spec="iii";break;
                case 27:spec="iifb";break;
                case 28:spec="iif";break;
                case 29:spec="tivv";break;
                case 30:spec="ibfbiii";break;
                case 32:spec="iic";break;
                case 33:spec="ifff";break;
                case 34:spec="ttfv";break;
                case 35:spec="viiffi";break;
                case 37:spec="iffffibbfffi";break;
                case 38:spec="if";break;
                case 39:spec="iigvi";break;
                case 40:spec=b[1]!=0?"bigifffif":"biifffif";break;
                case 41:spec=b[1]!=0?"bigi[i][f]":"bii[i][f]";break;
                case 42:spec="ii";if(b.Length>8 && BitConverter.ToInt32(b,5)!=0)spec+="iiigiggvv";break;
                case 43:spec="i[v][v][v][v]";break;
                case 44:spec="iiif";break;
                case 45:spec="bbibbb";if(b[1]!=0)spec+=b[2]!=0?"t":"g";break;
                case 47:spec="giif";break;
                case 48:spec="ii[b][c]";break;
                default:return false;
            }
            try {var c=new Cursor(b);return c.Fields(spec,0,spec.Length)==b.Length;}catch(ArgumentException){return false;}
        }
        // Object ID carried by a native message (diagnostics and transport
        // coalescing only; it grants no authority).
        public static uint Item(byte[] b)
        {
            switch(b[0])
            {
                case VehicleMotionCodec.Type:return VehicleMotionCodec.Item(b);
                case 18:return BitConverter.ToUInt32(b,10);
                case 29:return BitConverter.ToUInt32(b,9);
                case 40:case 41:return b[1]==0?BitConverter.ToUInt32(b,2):0;
                case 45:return BitConverter.ToUInt32(b,3);
                case 16:case 17:case 19:case 20:case 23:case 24:case 25:case 26:case 27:case 28:
                case 30:case 31:case 32:case 33:case 36:case 37:case 38:case 39:case 42:case 43:case 44:case 48:return BitConverter.ToUInt32(b,1);
                default:return 0;
            }
        }
        // The stock SendType policy: these message classes are sent unreliably.
        // Reliability is a transport property, never an authority decision.
        public static bool IsReliable(byte[] bytes)
        {
            if(bytes==null || bytes.Length==0)return true;
            switch(bytes[0])
            {
                case 10:case 11:case 14:case 16:case 17:case 18:case 19:case 20:case 29:case 39:case 43:case 46:case VehicleMotionCodec.Type:return false;
                default:return true;
            }
        }
        // Coalescing key for unreliable state: message type + object ID
        // (latest-value transport scheduling, no ownership meaning).
        // Send-queue coalescing key, only for unreliable types whose single message
        // carries the complete current state of one object: itemPosGlobal (16),
        // velocity (19), carVelocity (20), carPosGlobal (39). A newer one of these
        // makes a still-queued older one of the same type/object obsolete. Every
        // other type (partial or per-part state) returns 0 and is never merged.
        public static ulong LatestValueKey(byte[] bytes)
        {
            if(bytes==null || bytes.Length<5)return 0;
            switch(bytes[0])
            {
                case 16:case 19:case 20:case 39:case VehicleMotionCodec.Type:return StreamKey(bytes);
                default:return 0;
            }
        }
        public static ulong StreamKey(byte[] bytes)
        {
            if(bytes==null || bytes.Length<2)return 0;
            uint id=Item(bytes);
            return id==0?0:((ulong)bytes[0]<<32)|id;
        }
    }

    // One immutable sample contains the exact native pose and velocity payloads.
    // This transport envelope is decoded before the native dispatcher; native
    // serializers, authority checks and physics remain the application path.
    internal sealed class VehicleMotionSample
    {
        internal uint Sequence, ClockMs;
        internal bool Impact;
        internal byte[] Pose, Velocity;
        internal uint ID {get{return BitConverter.ToUInt32(Pose,1);}}
        internal int Role {get{return BitConverter.ToInt32(Pose,45);}}
        internal int Prefab {get{return BitConverter.ToInt32(Pose,5);}}
    }

    internal static class VehicleMotionCodec
    {
        internal const byte Type=253,Version=1;
        internal const int HeaderBytes=11,SampleBytes=91,MaxBytes=HeaderBytes+2*SampleBytes;
        internal const uint RedundancyMilliseconds=150;
        internal static bool ValidOrigin(bool isHost,ulong host,ulong source,ulong origin)
        {return origin!=0 && (isHost?source==origin:source==host);}

        internal static bool CanRepeat(VehicleMotionSample older,VehicleMotionSample current)
        {
            return older!=null && current!=null && older.ID==current.ID && older.Role==current.Role && older.Prefab==current.Prefab &&
                Numbers.Newer(current.Sequence,older.Sequence) && unchecked(current.ClockMs-older.ClockMs)<=RedundancyMilliseconds;
        }
        internal static byte[] Encode(ulong origin,VehicleMotionSample current,VehicleMotionSample previous)
        {
            bool repeat=CanRepeat(previous,current);
            var bytes=new byte[HeaderBytes+(repeat?2:1)*SampleBytes];bytes[0]=Type;bytes[1]=Version;bytes[2]=(byte)(repeat?2:1);
            Buffer.BlockCopy(BitConverter.GetBytes(origin),0,bytes,3,8);
            if(repeat)Write(bytes,HeaderBytes,previous);
            Write(bytes,HeaderBytes+(repeat?SampleBytes:0),current);
            return bytes;
        }
        static void Write(byte[] bytes,int at,VehicleMotionSample sample)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(sample.Sequence),0,bytes,at,4);
            Buffer.BlockCopy(BitConverter.GetBytes(sample.ClockMs),0,bytes,at+4,4);bytes[at+8]=(byte)(sample.Impact?1:0);
            Buffer.BlockCopy(sample.Pose,0,bytes,at+9,49);Buffer.BlockCopy(sample.Velocity,0,bytes,at+58,33);
        }
        internal static uint Item(byte[] bytes)
        {return bytes!=null && bytes.Length>=HeaderBytes+SampleBytes?BitConverter.ToUInt32(bytes,HeaderBytes+10):0;}
        internal static bool Valid(byte[] bytes)
        {ulong origin;VehicleMotionSample[] samples;return TryRead(bytes,out origin,out samples);}
        internal static bool TryRead(byte[] bytes,out ulong origin,out VehicleMotionSample[] samples)
        {
            origin=0;samples=null;
            if(bytes==null || bytes.Length<HeaderBytes || bytes[0]!=Type || bytes[1]!=Version || bytes[2]<1 || bytes[2]>2 ||
                bytes.Length!=HeaderBytes+bytes[2]*SampleBytes)return false;
            origin=BitConverter.ToUInt64(bytes,3);if(origin==0)return false;
            samples=new VehicleMotionSample[bytes[2]];
            for(int i=0;i<samples.Length;i++)
            {
                int at=HeaderBytes+i*SampleBytes;if(bytes[at+8]>1)return false;
                var sample=new VehicleMotionSample {Sequence=BitConverter.ToUInt32(bytes,at),ClockMs=BitConverter.ToUInt32(bytes,at+4),Impact=bytes[at+8]!=0,Pose=new byte[49],Velocity=new byte[33]};
                Buffer.BlockCopy(bytes,at+9,sample.Pose,0,49);Buffer.BlockCopy(bytes,at+58,sample.Velocity,0,33);
                if(sample.Sequence==0 || sample.Pose[0]!=39 || sample.Velocity[0]!=20 || !StablePackets.Valid(sample.Pose) || !StablePackets.Valid(sample.Velocity) ||
                    sample.ID==0 || sample.ID!=BitConverter.ToUInt32(sample.Velocity,1) || sample.Role!=BitConverter.ToInt32(sample.Velocity,29))return false;
                if(i>0 && !CanRepeat(samples[i-1],sample))return false;
                samples[i]=sample;
            }
            return true;
        }
    }

    internal sealed class VehicleMotionPairer
    {
        struct Key:IEquatable<Key>
        {
            internal ulong Scope;internal uint ID;
            public bool Equals(Key other){return Scope==other.Scope && ID==other.ID;}
            public override bool Equals(object other){return other is Key && Equals((Key)other);}
            public override int GetHashCode(){return Scope.GetHashCode()^(int)ID;}
        }
        sealed class State
        {internal byte[] Pose;internal int PoseFrame,SentFrame=-1;internal double Seen;internal VehicleMotionSample Previous;}
        internal const int Capacity=256;
        readonly Dictionary<Key,State> states=new Dictionary<Key,State>();
        readonly List<Key> expired=new List<Key>();
        internal long Combined,Repeated,SameFrameSkipped,Fallback;
        internal int Count {get{return states.Count;}}
        // scope 0 is broadcast; explicit target scopes never consume each
        // other's pose/velocity halves. Reliability overrides bypass this class.
        internal bool Offer(ulong scope,ulong origin,byte[] bytes,int frame,double now,uint clockMs,uint sequence,bool impact,out byte[] packet)
        {
            packet=null;
            if(bytes==null || (bytes.Length!=49 || bytes[0]!=39) && (bytes.Length!=33 || bytes[0]!=20))return false;
            uint id=BitConverter.ToUInt32(bytes,1);var key=new Key {Scope=scope,ID=id};State state;
            if(bytes[0]==39)
            {
                if(!states.TryGetValue(key,out state))
                {if(states.Count>=Capacity)return false;states.Add(key,state=new State());}
                state.Pose=(byte[])bytes.Clone();state.PoseFrame=frame;state.Seen=now;return true;
            }
            if(!states.TryGetValue(key,out state) || state.Pose==null || state.PoseFrame!=frame ||
                BitConverter.ToInt32(state.Pose,45)!=BitConverter.ToInt32(bytes,29))return false;
            byte[] pose=state.Pose;state.Pose=null;state.Seen=now;
            if(state.SentFrame==frame){SameFrameSkipped++;return true;}
            var current=new VehicleMotionSample {Sequence=sequence,ClockMs=clockMs,Impact=impact,Pose=pose,Velocity=(byte[])bytes.Clone()};
            bool repeat=VehicleMotionCodec.CanRepeat(state.Previous,current);
            packet=VehicleMotionCodec.Encode(origin,current,state.Previous);state.Previous=current;state.SentFrame=frame;
            Combined++;if(repeat)Repeated++;return true;
        }
        // A car without a velocity half must still publish its native pose.
        // Flush only after the native call stack/frame has had a chance to pair.
        internal void Flush(double now,Action<ulong,byte[]> send)
        {
            expired.Clear();
            foreach(var pair in states)
            {
                if(pair.Value.Pose!=null){send(pair.Key.Scope,pair.Value.Pose);pair.Value.Pose=null;Fallback++;}
                if(now-pair.Value.Seen>5)expired.Add(pair.Key);
            }
            foreach(var key in expired)states.Remove(key);
        }
        internal void Clear(){states.Clear();Combined=Repeated=SameFrameSkipped=Fallback=0;}
    }
}
