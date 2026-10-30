using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TldMp;
using UnityEngine;
using Steamworks;
using GeneszSteamP2PBase;

// Structural validation against the ACTUAL game serializer: every native
// message type and branch variant must pass exact framing, and every truncation
// or extra byte must be rejected before native handlers allocate or mutate.
public static class StableWireProductionTests
{
    static int checks;
    static void Check(bool b,string reason){checks++;if(!b)throw new Exception(reason);}
    static readonly List<byte[]> packets=new List<byte[]>();
    static void Packet(int type,params object[] values)
    {
        var args=new object[values.Length+1];args[0]=(msgType)type;Array.Copy(values,0,args,1,values.Length);
        var b=DataByteConverterScript.GetBytes(args);Check(StablePackets.Valid(b),"Production serializer rejected for type "+type+", size="+b.Length);packets.Add(b);
        for(int i=0;i<b.Length;i++){var truncated=new byte[i];Buffer.BlockCopy(b,0,truncated,0,i);Check(!StablePackets.Valid(truncated),"Truncated production type "+type+" accepted at "+i);}
        var extended=new byte[b.Length+1];Buffer.BlockCopy(b,0,extended,0,b.Length);Check(!StablePackets.Valid(extended),"Extra payload accepted for type "+type);
    }
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name;string p=name=="Assembly-CSharp"?args[1]:Path.Combine(args[0],name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
        try{Assembly.LoadFrom(Path.GetFullPath(args[1]));return Run();}catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static int Run()
    {
        var sid=new CSteamID(20);var v=new Vector3(1,2,3);var g=new Vector3d(100000.25,4,-200000.5);var color=new Color(.1f,.2f,.3f,1);
        Packet(1);Packet(2);Packet(3,sid,"hi \u263a");Packet(4,sid,new float[]{.1f,-.2f},1);Packet(5,"password");Packet(6,kickType.wrongPassword);Packet(7,new CSteamID[]{sid});
        Packet(8,0,"mapID","",123,g);Packet(8,1,"","bspname",123,g);Packet(8,2,"","image",123,g,true,false,0,1f,100f,256,256);
        Packet(9);Packet(10,false,false,sid,g,v,.5f);Packet(10,true,false,sid,g,v,.5f,(uint)5,0);Packet(10,true,true,sid,g,v,.5f,0,g,0);
        Packet(11,sid,v,v,v,v,v,v,v,v,v,v,v,v,false,.1f,(byte)0);
        Packet(12,sid,1,new bool[]{true,false},new Color[]{color,color});Packet(13);Packet(14,4f);Packet(15,new int[]{1},new float[]{1},new int[]{2},new float[]{2});
        Packet(16,(uint)5,1,g,v);Packet(17,(uint)5,(uint)6,0,v,v);Packet(18,false,sid,(uint)5,v,v,0);Packet(18,true,sid,(uint)5,v,v,0,v,v);
        Packet(19,(uint)5,v,v);Packet(20,(uint)5,v,v,6);Packet(21,(uint)5);Packet(22,0,g);
        Packet(23,(uint)5,0,5,4,3,2,color);Packet(24,(uint)5,"ABC123");Packet(25,(uint)5,new bool[]{true},new bool[]{false});
        Packet(26,(uint)5,0,2);Packet(27,(uint)5,0,.5f,false);Packet(28,(uint)5,0,.5f);Packet(29,sid,(uint)5,v,v);
        Packet(30,(uint)5,false,.5f,true,1,2,3);Packet(31,(uint)5,0,1);Packet(32,(uint)5,0,color);Packet(33,(uint)5,1f,2f,3f);
        Packet(34,sid,new CSteamID(30),1f,v);Packet(35,v,0,1,.5f,1f,0);Packet(36,(uint)5);
        Packet(37,(uint)5,-.5f,0f,0f,.75f,1,true,false,3000f,0f,0f,6);Packet(38,(uint)5,.5f);Packet(39,(uint)5,1,g,v,6);
        Packet(40,false,(uint)5,0,.1f,.2f,.3f,0,.5f);Packet(40,true,0,g,0,.1f,.2f,.3f,0,.5f);
        Packet(41,false,(uint)5,0,new int[]{1,2},new float[]{1,2});Packet(41,true,0,g,0,new int[]{1,2},new float[]{1,2});
        Packet(42,(uint)5,0);Packet(42,(uint)5,1,0,(uint)6,0,g,0,g,g,v,v);
        Packet(43,(uint)5,new Vector3[]{v},new Vector3[]{v},new Vector3[]{v},new Vector3[]{v});Packet(44,(uint)5,0,1,.5f);
        Packet(45,false,false,(uint)5,true,false,false);Packet(45,true,false,(uint)5,true,false,false,g);Packet(45,true,true,(uint)5,true,false,false,(ulong)20);
        Packet(46,(uint)5);Packet(47,g,0,1,.5f);Packet(48,(uint)5,1,new bool[]{true},new Color[]{color});
        foreach(var b in packets)
        {
            int t=b[0];
            if(t==16 || t==17 || t==18 || t==19 || t==20 || t==23 || t==24 || t==25 || t==26 || t==27 || t==28 || t==29 || t==30 || t==31 || t==32 || t==33 || t==36 || t==37 || t==38 || t==39 || t==42 || t==43 || t==44 || t==48)Check(StablePackets.Item(b)==5,"Correct object ID offset: "+t);
        }
        var nan=DataByteConverterScript.GetBytes(msgType.carValues,(uint)5,Single.NaN,0f,0f,0f,0,false,false,0f,0f,0f,6);Check(!StablePackets.Valid(nan),"Nonfinite input rejected before handlers.");
        var badBool=DataByteConverterScript.GetBytes(msgType.itemPosGlobal,(uint)5,1,g,v);badBool[0]=18;Check(!StablePackets.Valid(badBool),"A non-bool flag byte is rejected structurally.");
        Console.WriteLine("PASS: "+checks+" checks against the ACTUAL game serializer, covering every active native message type and branch variants, full truncation/extra-byte rejection, object ID offsets and non-finite rejection. No Unity native calls.");return 0;
    }
}
