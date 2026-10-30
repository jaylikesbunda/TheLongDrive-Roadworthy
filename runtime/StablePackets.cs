using System;

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
                case 10:case 11:case 14:case 16:case 17:case 18:case 19:case 20:case 29:case 39:case 43:case 46:return false;
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
                case 16:case 19:case 20:case 39:return StreamKey(bytes);
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
}
