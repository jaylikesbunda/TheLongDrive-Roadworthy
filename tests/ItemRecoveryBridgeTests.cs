// Compile the production AttachReplay adapter unchanged against a small native
// API boundary. Tests exercise deferral, recovery, deletion, and slot cleanup;
// Unity/PhysX joint construction is outside this fixture.
using System;
using System.Collections.Generic;
using TldMp;
namespace Steamworks { public struct CSteamID { public ulong Value; public CSteamID(ulong id){Value=id;} public static explicit operator ulong(CSteamID id){return id.Value;} } }
namespace UnityEngine { public static class Debug { public static void LogWarning(string text){throw new Exception(text);} } }
namespace GeneszSteamP2PBase { public class Boundary { } }
public enum msgType { attachable=42 }
public class tosaveitemscript
{
    public uint idInSave;
    public attachablescript attachable;
    public List<partslotscript> partslotscripts=new List<partslotscript>();
}
public class partslotscript
{
    public tosaveitemscript Parent;public int Index;public attachablescript Part;
    public attachablescript part(){return Part;}
    public bool UnCraft(bool fallOff){if(Part!=null)Part.Detach();return true;}
}
public class attachablescript
{
    public bool attached,dontSendMulti;public partslotscript slot;public int Detaches;
    public void Detach(){Detaches++;if(slot!=null && slot.Part==this)slot.Part=null;slot=null;attached=false;}
}
public class save_attachable
{
    public int attachType,indexType,poigenid,index;public uint parentid;public Point poiid;
    public struct Point {public double x,y,z;}
    public save_attachable(attachablescript item,uint id)
    {if(item.attached && item.slot!=null){attachType=1;indexType=2;parentid=item.slot.Parent.idInSave;index=item.slot.Index;}}
}
public class syncScript
{
    public static syncScript s=new syncScript();public bool sentAskStartStuff=true;public List<uint> Asked=new List<uint>();
    public void SendAskItemData(uint id){Asked.Add(id);}
}
public class networkingScript
{
    public static networkingScript s=new networkingScript();
    public void ProcessMessage(Steamworks.CSteamID sender,byte[] bytes,msgType type)
    {if(!StableGameBridge.Defer(sender,bytes))StableGameBridge.Apply(bytes);}
}
namespace TldMp
{
    // Only ID extraction is stood in; wire parsing itself has packet tests.
    public static class StablePackets
    {public static uint Item(byte[] b){return b[0]==40 || b[0]==41?BitConverter.ToUInt32(b,2):BitConverter.ToUInt32(b,1);}}
    internal static class NetStats {internal static string Ms(long ticks){return ticks.ToString();}}
    public static partial class StableGameBridge
    {
        sealed class Node {internal bool Ready=true;}
        static Node node=new Node();static double Now;internal static int DispatchType=-1;
        static readonly Dictionary<uint,tosaveitemscript> items=new Dictionary<uint,tosaveitemscript>();
        public static readonly List<byte[]> Applied=new List<byte[]>();
        static bool Find(uint id,out tosaveitemscript item){return items.TryGetValue(id,out item);}
        static void DiagLimited(string key,string text,double interval){}
        static void NoteHandler(byte[] bytes,long ticks){}
        public static bool Defer(Steamworks.CSteamID sender,byte[] bytes){return NotePendingItem(sender,bytes);}
        public static void Step(double time){Now=time;ReplayPendingItems();}
        public static int Pending {get{return pendingItems.Count;}}
        public static void ResetTest(){items.Clear();pendingItems.Clear();itemRecovery.Clear();Applied.Clear();syncScript.s.Asked.Clear();Now=0;}
        public static tosaveitemscript Add(uint id,int slots)
        {
            var item=new tosaveitemscript{idInSave=id,attachable=new attachablescript()};items[id]=item;
            for(int i=0;i<slots;i++)item.partslotscripts.Add(new partslotscript{Parent=item,Index=i});return item;
        }
        public static void Apply(byte[] bytes)
        {
            uint id=StablePackets.Item(bytes);Applied.Add(bytes);
            if(bytes[0]==16){if(!items.ContainsKey(id))Add(id,0);return;}
            if(bytes[0]==36){items.Remove(id);return;}
            if(bytes[0]!=42)return;
            var a=items[id].attachable;
            if(BitConverter.ToInt32(bytes,5)==0){a.Detach();return;}
            var slot=items[BitConverter.ToUInt32(bytes,13)].partslotscripts[BitConverter.ToInt32(bytes,45)];
            // Like native Craft, this deliberately does not clean an old slot.
            a.slot=slot;a.attached=true;slot.Part=a;
        }
    }
}
internal static class ItemRecoveryBridgeTests
{
    static int checks;
    static void Check(bool ok,string what){checks++;if(!ok)throw new Exception(what);}
    static byte[] Packet(byte type,uint id){var b=new byte[5];b[0]=type;Buffer.BlockCopy(BitConverter.GetBytes(id),0,b,1,4);return b;}
    static byte[] Attach(uint id,uint parent,int slot)
    {
        byte[] b=new byte[121];b[0]=42;
        Buffer.BlockCopy(BitConverter.GetBytes(id),0,b,1,4);Buffer.BlockCopy(BitConverter.GetBytes(1),0,b,5,4);
        Buffer.BlockCopy(BitConverter.GetBytes(2),0,b,9,4);Buffer.BlockCopy(BitConverter.GetBytes(parent),0,b,13,4);
        Buffer.BlockCopy(BitConverter.GetBytes(slot),0,b,45,4);return b;
    }
    static void Receive(byte[] b){networkingScript.s.ProcessMessage(new Steamworks.CSteamID(7),b,(msgType)b[0]);}
    static void Main()
    {
        StableGameBridge.ResetTest();Receive(Attach(10,20,0));
        Check(StableGameBridge.Pending==1 && StableGameBridge.Applied.Count==0,"missing attachment also ran the native receiver");
        StableGameBridge.Step(0);
        Check(syncScript.s.Asked.Contains(10) && syncScript.s.Asked.Contains(20) && syncScript.s.Asked.Count==2,"missing parent/child definitions not requested within budget");
        var child=StableGameBridge.Add(10,0);var parent=StableGameBridge.Add(20,2);
        // A newer state cannot bypass the earlier attachment waiting in FIFO.
        Receive(Packet(23,10));Check(StableGameBridge.Applied.Count==0,"newer state overtook queued attachment");
        for(int i=0;i<10 && StableGameBridge.Pending>0;i++)StableGameBridge.Step(.1+i*.01);
        Check(child.attachable.slot==parent.partslotscripts[0] && StableGameBridge.Applied.Count==2 && StableGameBridge.Applied[0][0]==42 && StableGameBridge.Applied[1][0]==23,"delayed creation did not converge in order");

        StableGameBridge.ResetTest();Receive(Packet(23,11));Receive(Packet(36,11));Receive(Packet(16,11));
        for(int i=0;i<3;i++)StableGameBridge.Step(i);
        Check(StableGameBridge.Pending==0 && syncScript.s.Asked.Count==0 && StableGameBridge.Applied.Count==1,"deletion left queued state or resurrected a removed definition");

        // A wheel is already mounted in a wrong slot when its held correction
        // becomes ready. Native detach must clear the old membership first.
        StableGameBridge.ResetTest();Receive(Attach(12,21,1));
        child=StableGameBridge.Add(12,0);parent=StableGameBridge.Add(21,2);
        child.attachable.slot=parent.partslotscripts[0];child.attachable.attached=true;parent.partslotscripts[0].Part=child.attachable;
        for(int i=0;i<10 && StableGameBridge.Pending>0;i++)StableGameBridge.Step(i*.01);
        Check(child.attachable.slot==parent.partslotscripts[1] && parent.partslotscripts[0].Part==null && child.attachable.Detaches==1,"wrong old slot membership survived reconciliation");
        Check(!child.attachable.dontSendMulti,"native no-echo flag leaked out of replay");

        StableGameBridge.ResetTest();Receive(Attach(13,22,0));child=StableGameBridge.Add(13,0);parent=StableGameBridge.Add(22,1);
        child.attachable.slot=parent.partslotscripts[0];child.attachable.attached=true;parent.partslotscripts[0].Part=child.attachable;
        for(int i=0;i<10 && StableGameBridge.Pending>0;i++)StableGameBridge.Step(i*.01);
        Check(child.attachable.Detaches==0 && StableGameBridge.Applied.Count==0,"identical mount rebuilt a healthy native joint");
        Console.WriteLine("PASS: "+checks+" production item recovery adapter assertions (native API stand-ins; no Unity/PhysX).");
    }
}
