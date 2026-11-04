using System;
using System.IO;
using TldMp;

public static class RabbitProtocolTests
{
    static int checks;
    static void Check(bool ok,string w){checks++;if(!ok)throw new Exception("FAIL: "+w);}
    static RabbitPoint P(double x,double y,double z){return new RabbitPoint{X=x,Y=y,Z=z};}
    static RabbitVector V(float x,float y,float z){return new RabbitVector{X=x,Y=y,Z=z};}
    static RabbitRotation R(float x,float y,float z,float w){return new RabbitRotation{X=x,Y=y,Z=z,W=w};}
    static RabbitCommand Cmd(RabbitCommandKind k){return new RabbitCommand{Kind=k,Item=1,Tool=1,Token=1,Index=0,Position=P(1,2,3),Direction=V(0,0,1),Extents=V(1,1,1),Rotation=R(0,0,0,1)};}
    static RabbitMetadata Meta(){return new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1.5f,Health=10,ShootHealth=-3,ExplosionHealth=2,Part=-1};}
    static byte[] Mut(byte[] b,int off,byte v){var c=(byte[])b.Clone();c[off]=v;return c;}
    static void ReadFail(byte[] b,string w){RabbitCommand c;Check(!RabbitProtocol.TryReadCommand(b,out c)&&c.Equals(default(RabbitCommand)),w);}
    static void ReadFailM(byte[] b,string w){RabbitMetadata m;Check(!RabbitProtocol.TryReadMetadata(b,out m)&&m.Equals(default(RabbitMetadata)),w);}
    static void WriteFail(RabbitCommand c,string w){try{RabbitProtocol.WriteCommand(c);Check(false,w);}catch(ArgumentException){}}
    static void WriteFailM(RabbitMetadata s,string w){try{RabbitProtocol.WriteMetadata(s);Check(false,w);}catch(ArgumentException){}}

    public static int Main()
    {
        var shot=RabbitProtocol.WriteCommand(new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=7,Token=ulong.MaxValue,Index=3,Position=P(1e9+.125,-2e9,3),Direction=V(-0.5f,0.1f,0.9f)});
        Check(shot.Length==54,"shot len");
        RabbitCommand c;
        Check(RabbitProtocol.TryReadCommand(shot,out c)&&c.Kind==RabbitCommandKind.Shot&&c.Tool==7&&c.Token==ulong.MaxValue&&c.Index==3&&c.Position.X==1e9+.125&&c.Position.Y==-2e9&&c.Position.Z==3&&c.Direction.X==-0.5f&&c.Direction.Y==0.1f&&c.Direction.Z==0.9f,"shot roundtrip");
        var melee=RabbitProtocol.WriteCommand(new RabbitCommand{Kind=RabbitCommandKind.Melee,Tool=11,Token=1,Position=P(5,6,7),Extents=V(1,2,3),Rotation=R(0.1f,0.2f,0.3f,0.4f)});
        Check(melee.Length==66,"melee len");
        Check(RabbitProtocol.TryReadCommand(melee,out c)&&c.Kind==RabbitCommandKind.Melee&&c.Tool==11&&c.Token==1&&c.Position.X==5&&c.Position.Y==6&&c.Position.Z==7&&c.Extents.X==1&&c.Extents.Y==2&&c.Extents.Z==3&&c.Rotation.X==0.1f&&c.Rotation.Y==0.2f&&c.Rotation.Z==0.3f&&c.Rotation.W==0.4f,"melee roundtrip");
        var grab=RabbitProtocol.WriteCommand(new RabbitCommand{Kind=RabbitCommandKind.Grab,Item=13,Token=9,Index=127,Position=P(4,5,6)});
        Check(grab.Length==42,"grab len");
        Check(RabbitProtocol.TryReadCommand(grab,out c)&&c.Item==13&&c.Index==127&&c.Token==9&&c.Position.X==4&&c.Position.Y==5&&c.Position.Z==6,"grab roundtrip");
        var hold=RabbitProtocol.WriteCommand(new RabbitCommand{Kind=RabbitCommandKind.Hold,Item=15,Token=2,Position=P(-1,-2,-3),Rotation=R(0.5f,0.5f,0.5f,0.5f)});
        Check(hold.Length==54,"hold len");
        Check(RabbitProtocol.TryReadCommand(hold,out c)&&c.Item==15&&c.Token==2&&c.Position.X==-1&&c.Position.Y==-2&&c.Position.Z==-3&&c.Rotation.X==0.5f&&c.Rotation.Y==0.5f&&c.Rotation.Z==0.5f&&c.Rotation.W==0.5f,"hold roundtrip");
        var drop=RabbitProtocol.WriteCommand(new RabbitCommand{Kind=RabbitCommandKind.Drop,Item=17,Token=3,Direction=V(1.5f,-2.5f,0)});
        Check(drop.Length==26,"drop len");
        Check(RabbitProtocol.TryReadCommand(drop,out c)&&c.Item==17&&c.Token==3&&c.Direction.X==1.5f&&c.Direction.Y==-2.5f&&c.Direction.Z==0,"drop roundtrip");
        Check(RabbitProtocol.WriteCommand(new RabbitCommand{Kind=RabbitCommandKind.Drop,Item=1,Token=1,Direction=V(0,0,0)}).Length==26,"drop zero dir");
        Check(RabbitProtocol.WriteCommand(new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=1,Token=1,Index=int.MaxValue,Position=P(0,0,0),Direction=V(1,0,0)}).Length==54,"shot max index");
        Check(RabbitProtocol.WriteCommand(new RabbitCommand{Kind=RabbitCommandKind.Grab,Item=1,Token=1,Index=0,Position=P(0,0,0)}).Length==42,"grab idx0");
        Check(RabbitProtocol.WriteCommand(new RabbitCommand{Kind=RabbitCommandKind.Melee,Tool=1,Token=1,Position=P(0,0,0),Extents=V(1,1,1),Rotation=R(1,2,3,4)}).Length==66,"nonunit quat");

        var shotBytes=RabbitProtocol.WriteCommand(new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=0x01020304,Token=0x0102030405060708,Index=0x01020304,Position=P(1.5,2.5,3.5),Direction=V(4.5f,5.5f,6.5f)});
        Check(shotBytes[0]==1&&shotBytes[1]==1,"ver/kind");
        var known=new MemoryStream();
        var w=new BinaryWriter(known);
        w.Write((byte)1);w.Write((byte)1);w.Write(0x01020304u);w.Write(0x0102030405060708ul);w.Write(0x01020304);w.Write(1.5);w.Write(2.5);w.Write(3.5);w.Write(4.5f);w.Write(5.5f);w.Write(6.5f);
        var expect=known.ToArray();
        Check(expect.Length==54,"known len");
        for(int i=0;i<54;i++)Check(shotBytes[i]==expect[i],"byte"+i);

        ReadFail(null,"null cmd");ReadFail(new byte[0],"empty cmd");ReadFail(new byte[]{1},"1b cmd");ReadFail(new byte[129],"129b cmd");
        foreach(RabbitCommandKind k in new[]{RabbitCommandKind.Shot,RabbitCommandKind.Melee,RabbitCommandKind.Grab,RabbitCommandKind.Hold,RabbitCommandKind.Drop})
        {
            var good=RabbitProtocol.WriteCommand(Cmd(k));
            for(int i=0;i<good.Length;i++)ReadFail(Sub(good,i),"trunc "+k+i);
            ReadFail(Pad(good),"trail "+k);
            ReadFail(Mut(good,0,9),"ver "+k);
        }
        ReadFail(new byte[]{1,0},"kind0");ReadFail(new byte[]{1,6},"kind6");ReadFail(new byte[]{1,255},"kind255");

        int[] shotPos={18,26,34},shotDir={42,46,50};
        int[] meleePos={14,22,30},meleeExt={38,42,46},meleeRot={50,54,58,62};
        int[] grabPos={18,26,34};
        int[] holdPos={14,22,30},holdRot={38,42,46,50};
        int[] dropDir={14,18,22};
        foreach(var t in new[]{new{K=RabbitCommandKind.Shot,Offs=shotPos},new{K=RabbitCommandKind.Melee,Offs=meleePos},new{K=RabbitCommandKind.Grab,Offs=grabPos},new{K=RabbitCommandKind.Hold,Offs=holdPos}})
        {
            var good=RabbitProtocol.WriteCommand(Cmd(t.K));
            foreach(int o in t.Offs)foreach(byte[] bad in new[]{new byte[]{0,0,0,0,0,0,0xF8,0x7F},new byte[]{0,0,0,0,0,0,0xF0,0x7F},new byte[]{0,0,0,0,0,0,0xF0,0xFF}})
                ReadFail(W(good,o,bad),"finite "+t.K+o);
        }
        foreach(var t in new[]{new{K=RabbitCommandKind.Shot,Offs=shotDir},new{K=RabbitCommandKind.Melee,Offs=meleeExt},new{K=RabbitCommandKind.Melee,Offs=meleeRot},new{K=RabbitCommandKind.Hold,Offs=holdRot},new{K=RabbitCommandKind.Drop,Offs=dropDir}})
        {
            var good=RabbitProtocol.WriteCommand(Cmd(t.K));
            foreach(int o in t.Offs)foreach(byte[] bad in new[]{new byte[]{0,0,0xC0,0x7F},new byte[]{0,0,0x80,0x7F},new byte[]{0,0,0x80,0xFF}})
                ReadFail(W(good,o,bad),"finite "+t.K+o);
        }
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=0,Token=1,Index=0,Position=P(0,0,0),Direction=V(1,0,0)},"tool0");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=1,Token=0,Index=0,Position=P(0,0,0),Direction=V(1,0,0)},"token0");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=1,Token=1,Index=-1,Position=P(0,0,0),Direction=V(1,0,0)},"idx-1");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=1,Token=1,Index=0,Position=P(0,0,0),Direction=V(0,0,0)},"zerodir");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=1,Token=1,Index=0,Position=P(double.NaN,0,0),Direction=V(1,0,0)},"posNaN");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=1,Token=1,Index=0,Position=P(double.PositiveInfinity,0,0),Direction=V(1,0,0)},"posInf");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Shot,Tool=1,Token=1,Index=0,Position=P(0,0,0),Direction=V(float.NaN,0,0)},"dirNaN");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Grab,Item=1,Token=1,Index=-1,Position=P(0,0,0)},"grab-1");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Grab,Item=1,Token=1,Index=128,Position=P(0,0,0)},"grab128");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Melee,Tool=1,Token=1,Position=P(0,0,0),Extents=V(0,1,1),Rotation=R(0,0,0,1)},"ext0");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Melee,Tool=1,Token=1,Position=P(0,0,0),Extents=V(-1,1,1),Rotation=R(0,0,0,1)},"ext-1");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Melee,Tool=1,Token=1,Position=P(0,0,0),Extents=V(1,1,1),Rotation=R(0,0,0,0)},"zeroquat");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Hold,Item=1,Token=1,Position=P(0,0,0),Rotation=R(0,0,0,0)},"hold quat0");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Drop,Item=0,Token=1,Direction=V(0,0,0)},"drop item0");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Grab,Item=0,Token=1,Index=0,Position=P(0,0,0)},"grab item0");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Melee,Tool=0,Token=1,Position=P(0,0,0),Extents=V(1,1,1),Rotation=R(0,0,0,1)},"melee tool0");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Grab,Item=1,Token=0,Index=0,Position=P(0,0,0)},"grab token0");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Hold,Item=1,Token=0,Position=P(0,0,0),Rotation=R(0,0,0,1)},"hold token0");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Drop,Item=1,Token=0,Direction=V(0,0,0)},"drop token0");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Melee,Tool=1,Token=1,Position=P(double.NegativeInfinity,0,0),Extents=V(1,1,1),Rotation=R(0,0,0,1)},"melee posInf");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Melee,Tool=1,Token=1,Position=P(0,0,0),Extents=V(float.PositiveInfinity,1,1),Rotation=R(0,0,0,1)},"melee extInf");
        WriteFail(new RabbitCommand{Kind=RabbitCommandKind.Hold,Item=1,Token=1,Position=P(0,0,0),Rotation=R(float.NaN,0,0,1)},"hold rotNaN");

        var alive=RabbitProtocol.WriteMetadata(Meta());
        Check(alive.Length==62,"meta len");
        RabbitMetadata m;
        Check(RabbitProtocol.TryReadMetadata(alive,out m)&&m.Item==1&&m.Prefab==0&&m.Flags==2&&m.Size==1.5f&&m.Health==10&&m.ShootHealth==-3&&m.ExplosionHealth==2&&m.Holder==0&&m.HolderSession==0&&m.Lease==0&&m.Request==0&&m.Part==-1,"meta roundtrip");
        var smallest=Meta();smallest.Size=0;
        Check(RabbitProtocol.TryReadMetadata(RabbitProtocol.WriteMetadata(smallest),out m)&&m.Size==0,"native smallest size zero roundtrip");
        var held=RabbitProtocol.WriteMetadata(new RabbitMetadata{Item=6,Prefab=1,Flags=(byte)(2|8),Size=1,Health=5,ShootHealth=2,ExplosionHealth=0,Holder=7,HolderSession=8,Lease=9,Request=10,Part=127});
        Check(RabbitProtocol.TryReadMetadata(held,out m)&&m.Holder==7&&m.HolderSession==8&&m.Lease==9&&m.Request==10&&m.Part==127,"meta held");
        Check(RabbitProtocol.WriteMetadata(new RabbitMetadata{Item=6,Prefab=1,Flags=(byte)(2|8),Size=1,Health=5,ShootHealth=2,ExplosionHealth=0,Holder=7,HolderSession=8,Lease=9,Request=10,Part=0}).Length==62,"meta held part0");
        var removed=RabbitProtocol.WriteMetadata(new RabbitMetadata{Item=8,Prefab=2,Flags=1,Part=-1});
        Check(removed.Length==10,"meta removed len");
        Check(RabbitProtocol.TryReadMetadata(removed,out m)&&m.Flags==1&&m.Item==8&&m.Prefab==2&&m.Part==-1,"meta removed");
        Check(RabbitProtocol.WriteMetadata(new RabbitMetadata{Item=1,Prefab=0,Flags=0,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1}).Length==62,"flag0");
        Check(RabbitProtocol.WriteMetadata(new RabbitMetadata{Item=1,Prefab=0,Flags=4,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1}).Length==62,"flag4");
        Check(RabbitProtocol.WriteMetadata(new RabbitMetadata{Item=1,Prefab=0,Flags=8,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1}).Length==62,"flag8");
        Check(RabbitProtocol.WriteMetadata(new RabbitMetadata{Item=1,Prefab=0,Flags=16,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1}).Length==62,"flag16");
        Check(RabbitProtocol.WriteMetadata(new RabbitMetadata{Item=1,Prefab=0,Flags=(byte)(2|4|8|16),Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1}).Length==62,"flags combo");
        ReadFailM(null,"meta null");ReadFailM(new byte[]{1},"meta 1b");ReadFailM(new byte[129],"meta 129");
        ReadFailM(Mut(alive,0,9),"meta ver");
        for(int i=0;i<alive.Length;i++)ReadFailM(Sub(alive,i),"meta trunc"+i);
        for(int i=0;i<removed.Length;i++)ReadFailM(Sub(removed,i),"meta rem trunc"+i);
        ReadFailM(Pad(alive),"meta trail");ReadFailM(Pad(removed),"meta rem trail");
        ReadFailM(Mut(alive,1,3),"meta rem+alive");
        ReadFailM(Mut(alive,1,32),"meta flag32");ReadFailM(Mut(alive,1,128),"meta flag128");ReadFailM(Mut(alive,1,255),"meta flag255");
        ReadFailM(Mut(removed,1,3),"rem flag3");ReadFailM(Mut(removed,1,32),"rem flag32");
        ReadFailM(Mut(removed,2,0),"rem item0");ReadFailM(W(removed,6,new byte[]{0xFF,0xFF,0xFF,0xFF}),"rem prefab-1");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=-1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1},"size-1");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=float.NaN,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1},"sizeNaN");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=float.PositiveInfinity,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1},"sizeInf");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=float.NaN,ShootHealth=0,ExplosionHealth=0,Part=-1},"hNaN");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=float.PositiveInfinity,ShootHealth=0,ExplosionHealth=0,Part=-1},"hInf");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=float.NegativeInfinity,ShootHealth=0,ExplosionHealth=0,Part=-1},"h-Inf");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=float.NaN,ExplosionHealth=0,Part=-1},"sNaN");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=float.PositiveInfinity,Part=-1},"eInf");
        WriteFailM(new RabbitMetadata{Item=0,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1},"item0");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=-1,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1},"prefab-1");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=32,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1},"flag32");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=3,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1},"flag3");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Holder=1,HolderSession=0,Lease=0,Request=0,Part=-1},"hsess0");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Holder=1,HolderSession=1,Lease=0,Request=0,Part=-1},"hlease0");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Holder=1,HolderSession=1,Lease=1,Request=0,Part=-1},"hreq0");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Holder=1,HolderSession=1,Lease=1,Request=1,Part=-1},"hpart-1");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Holder=1,HolderSession=1,Lease=1,Request=1,Part=128},"hpart128");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Holder=0,HolderSession=1,Lease=0,Request=0,Part=-1},"n0 sess");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Holder=0,HolderSession=0,Lease=1,Request=0,Part=-1},"n0 lease");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Holder=0,HolderSession=0,Lease=0,Request=1,Part=-1},"n0 req");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=2,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Holder=0,HolderSession=0,Lease=0,Request=0,Part=0},"n0 part0");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=1,Size=1,Health=0,ShootHealth=0,ExplosionHealth=0,Part=-1},"rem size");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=1,Health=1,ShootHealth=0,ExplosionHealth=0,Part=-1},"rem health");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=1,ShootHealth=1,ExplosionHealth=0,Part=-1},"rem shoot");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=1,ExplosionHealth=1,Part=-1},"rem expl");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=1,Holder=1,Part=-1},"rem holder");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=1,HolderSession=1,Part=-1},"rem hsess");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=1,Lease=1,Part=-1},"rem lease");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=1,Request=1,Part=-1},"rem req");
        WriteFailM(new RabbitMetadata{Item=1,Prefab=0,Flags=1,Part=0},"rem part0");
        int[] metaFloats={10,14,18,22};
        foreach(int o in metaFloats)foreach(byte[] bad in new[]{new byte[]{0,0,0xC0,0x7F},new byte[]{0,0,0x80,0x7F},new byte[]{0,0,0x80,0xFF}})
            ReadFailM(W(alive,o,bad),"meta finite"+o);
        ReadFailM(Mut(alive,26,0xFF),"meta holder ff");
        ReadFailM(Mut(alive,34,0xFF),"meta sess ff");

        Console.WriteLine("PASS "+checks+" rabbit protocol checks");
        return 0;
    }
    static byte[] Sub(byte[] b,int n){var r=new byte[n];Array.Copy(b,r,n);return r;}
    static byte[] Pad(byte[] b){var r=new byte[b.Length+1];Array.Copy(b,r,b.Length);r[b.Length]=0xFF;return r;}
    static byte[] W(byte[] b,int off,byte[] v){var c=(byte[])b.Clone();Array.Copy(v,0,c,off,v.Length);return c;}
}
