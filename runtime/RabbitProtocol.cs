using System;
using System.IO;

namespace TldMp
{
    internal struct RabbitPoint{public double X,Y,Z;}
    internal struct RabbitVector{public float X,Y,Z;}
    internal struct RabbitRotation{public float X,Y,Z,W;}
    internal enum RabbitCommandKind:byte{Shot=1,Melee=2,Grab=3,Hold=4,Drop=5}
    internal struct RabbitCommand
    {
        public RabbitCommandKind Kind;
        public uint Item,Tool;
        public ulong Token;
        public int Index;
        public RabbitPoint Position;
        public RabbitVector Direction,Extents;
        public RabbitRotation Rotation;
    }
    internal struct RabbitMetadata
    {
        public uint Item;
        public int Prefab;
        public byte Flags;
        public float Size,Health,ShootHealth,ExplosionHealth;
        public ulong Holder,HolderSession,Lease,Request;
        public int Part;
    }
    internal static class RabbitProtocol
    {
        const byte Version=1;
        const byte Removed=1,Alive=2,HasEars=4,HasEyes=8,Ragdoll=16;
        static bool Fin(double v){return !double.IsNaN(v)&&!double.IsInfinity(v);}
        static bool Fin(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        static void Point(BinaryWriter w,RabbitPoint p){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
        static void Vector(BinaryWriter w,RabbitVector v){w.Write(v.X);w.Write(v.Y);w.Write(v.Z);}
        static void Rotation(BinaryWriter w,RabbitRotation r){w.Write(r.X);w.Write(r.Y);w.Write(r.Z);w.Write(r.W);}
        static RabbitPoint Point(BinaryReader r){return new RabbitPoint{X=r.ReadDouble(),Y=r.ReadDouble(),Z=r.ReadDouble()};}
        static RabbitVector Vector(BinaryReader r){return new RabbitVector{X=r.ReadSingle(),Y=r.ReadSingle(),Z=r.ReadSingle()};}
        static RabbitRotation Rotation(BinaryReader r){return new RabbitRotation{X=r.ReadSingle(),Y=r.ReadSingle(),Z=r.ReadSingle(),W=r.ReadSingle()};}

        static void Check(bool ok,string what){if(!ok)throw new ArgumentException(what);}
        static void PointOk(RabbitPoint p,string n){Check(Fin(p.X)&&Fin(p.Y)&&Fin(p.Z),n);}
        static void VecOk(RabbitVector v,string n){Check(Fin(v.X)&&Fin(v.Y)&&Fin(v.Z),n);}
        static void RotOk(RabbitRotation r,string n)
        {
            Check(Fin(r.X)&&Fin(r.Y)&&Fin(r.Z)&&Fin(r.W),n);
            double s=(double)r.X*r.X+(double)r.Y*r.Y+(double)r.Z*r.Z+(double)r.W*r.W;
            Check(s>0&&!double.IsInfinity(s),n);
        }
        static void Validate(RabbitCommand c)
        {
            switch(c.Kind)
            {
                case RabbitCommandKind.Shot:
                    Check(c.Tool>0,"Tool");Check(c.Token>0,"Token");Check(c.Index>=0,"Index");
                    PointOk(c.Position,"Position");
                    Check(Fin(c.Direction.X)&&Fin(c.Direction.Y)&&Fin(c.Direction.Z)&&((double)c.Direction.X*c.Direction.X+(double)c.Direction.Y*c.Direction.Y+(double)c.Direction.Z*c.Direction.Z)>0,"Direction");
                    break;
                case RabbitCommandKind.Melee:
                    Check(c.Tool>0,"Tool");Check(c.Token>0,"Token");PointOk(c.Position,"Position");
                    Check(Fin(c.Extents.X)&&c.Extents.X>0&&Fin(c.Extents.Y)&&c.Extents.Y>0&&Fin(c.Extents.Z)&&c.Extents.Z>0,"Extents");
                    RotOk(c.Rotation,"Rotation");
                    break;
                case RabbitCommandKind.Grab:
                    Check(c.Item>0,"Item");Check(c.Token>0,"Token");Check(c.Index>=0&&c.Index<=127,"Index");PointOk(c.Position,"Position");
                    break;
                case RabbitCommandKind.Hold:
                    Check(c.Item>0,"Item");Check(c.Token>0,"Token");PointOk(c.Position,"Position");RotOk(c.Rotation,"Rotation");
                    break;
                case RabbitCommandKind.Drop:
                    Check(c.Item>0,"Item");Check(c.Token>0,"Token");VecOk(c.Direction,"Direction");
                    break;
                default:throw new ArgumentException("Kind");
            }
        }
        internal static byte[] WriteCommand(RabbitCommand c)
        {
            Validate(c);
            using(var m=new MemoryStream())
            using(var w=new BinaryWriter(m))
            {
                w.Write(Version);w.Write((byte)c.Kind);
                switch(c.Kind)
                {
                    case RabbitCommandKind.Shot:w.Write(c.Tool);w.Write(c.Token);w.Write(c.Index);Point(w,c.Position);Vector(w,c.Direction);break;
                    case RabbitCommandKind.Melee:w.Write(c.Tool);w.Write(c.Token);Point(w,c.Position);Vector(w,c.Extents);Rotation(w,c.Rotation);break;
                    case RabbitCommandKind.Grab:w.Write(c.Item);w.Write(c.Token);w.Write(c.Index);Point(w,c.Position);break;
                    case RabbitCommandKind.Hold:w.Write(c.Item);w.Write(c.Token);Point(w,c.Position);Rotation(w,c.Rotation);break;
                    case RabbitCommandKind.Drop:w.Write(c.Item);w.Write(c.Token);Vector(w,c.Direction);break;
                }
                return m.ToArray();
            }
        }
        static int CmdLen(byte kind)
        {
            switch(kind){case 1:return 54;case 2:return 66;case 3:return 42;case 4:return 54;case 5:return 26;default:return -1;}
        }
        internal static bool TryReadCommand(byte[] bytes,out RabbitCommand c)
        {
            c=default(RabbitCommand);
            if(bytes==null||bytes.Length<2||bytes.Length>128||bytes[0]!=Version)return false;
            if(CmdLen(bytes[1])<0||bytes.Length!=CmdLen(bytes[1]))return false;
            try
            {
                using(var r=new BinaryReader(new MemoryStream(bytes)))
                {
                    r.ReadByte();var kind=(RabbitCommandKind)r.ReadByte();
                    var cmd=new RabbitCommand{Kind=kind};
                    switch(kind)
                    {
                        case RabbitCommandKind.Shot:cmd.Tool=r.ReadUInt32();cmd.Token=r.ReadUInt64();cmd.Index=r.ReadInt32();cmd.Position=Point(r);cmd.Direction=Vector(r);break;
                        case RabbitCommandKind.Melee:cmd.Tool=r.ReadUInt32();cmd.Token=r.ReadUInt64();cmd.Position=Point(r);cmd.Extents=Vector(r);cmd.Rotation=Rotation(r);break;
                        case RabbitCommandKind.Grab:cmd.Item=r.ReadUInt32();cmd.Token=r.ReadUInt64();cmd.Index=r.ReadInt32();cmd.Position=Point(r);break;
                        case RabbitCommandKind.Hold:cmd.Item=r.ReadUInt32();cmd.Token=r.ReadUInt64();cmd.Position=Point(r);cmd.Rotation=Rotation(r);break;
                        case RabbitCommandKind.Drop:cmd.Item=r.ReadUInt32();cmd.Token=r.ReadUInt64();cmd.Direction=Vector(r);break;
                        default:return false;
                    }
                    Validate(cmd);c=cmd;return true;
                }
            }
            catch(ArgumentException){return false;}
            catch(IOException){return false;}
        }
        static void Validate(RabbitMetadata s)
        {
            Check((s.Flags&~(byte)31)==0,"Flags");
            Check(s.Item>0,"Item");Check(s.Prefab>=0,"Prefab");
            bool removed=(s.Flags&Removed)!=0;
            if(removed)
            {
                Check(s.Flags==Removed,"Flags");
                Check(s.Size==0&&s.Health==0&&s.ShootHealth==0&&s.ExplosionHealth==0&&s.Holder==0&&s.HolderSession==0&&s.Lease==0&&s.Request==0&&s.Part==-1,"Removed");
                return;
            }
            Check(Fin(s.Size)&&s.Size>=0,"Size");
            Check(Fin(s.Health)&&Fin(s.ShootHealth)&&Fin(s.ExplosionHealth),"Health");
            if(s.Holder==0){Check(s.HolderSession==0&&s.Lease==0&&s.Request==0&&s.Part==-1,"Holder");}
            else{Check(s.HolderSession>0&&s.Lease>0&&s.Request>0&&s.Part>=0&&s.Part<=127,"Holder");}
        }
        internal static byte[] WriteMetadata(RabbitMetadata s)
        {
            Validate(s);
            using(var m=new MemoryStream())
            using(var w=new BinaryWriter(m))
            {
                w.Write(Version);w.Write(s.Flags);w.Write(s.Item);w.Write(s.Prefab);
                if((s.Flags&Removed)!=0)return m.ToArray();
                w.Write(s.Size);w.Write(s.Health);w.Write(s.ShootHealth);w.Write(s.ExplosionHealth);
                w.Write(s.Holder);w.Write(s.HolderSession);w.Write(s.Lease);w.Write(s.Request);w.Write(s.Part);
                return m.ToArray();
            }
        }
        internal static bool TryReadMetadata(byte[] bytes,out RabbitMetadata s)
        {
            s=default(RabbitMetadata);
            if(bytes==null||bytes.Length<10||bytes.Length>128||bytes[0]!=Version)return false;
            byte flags=bytes[1];
            int len=(flags&Removed)!=0?10:62;
            if(bytes.Length!=len)return false;
            try
            {
                using(var r=new BinaryReader(new MemoryStream(bytes)))
                {
                    r.ReadByte();r.ReadByte();
                    var m=new RabbitMetadata{Flags=flags,Item=r.ReadUInt32(),Prefab=r.ReadInt32()};
                    if((flags&Removed)!=0){m.Part=-1;}
                    else
                    {
                        m.Size=r.ReadSingle();m.Health=r.ReadSingle();m.ShootHealth=r.ReadSingle();m.ExplosionHealth=r.ReadSingle();
                        m.Holder=r.ReadUInt64();m.HolderSession=r.ReadUInt64();m.Lease=r.ReadUInt64();m.Request=r.ReadUInt64();m.Part=r.ReadInt32();
                    }
                    Validate(m);s=m;return true;
                }
            }
            catch(ArgumentException){return false;}
            catch(IOException){return false;}
        }
    }
}
