using System;
using System.Linq;
using UnityEngine.LowLevel;
using TldMp;

namespace TldMp { internal static class FrameDiagnostics { internal static bool Enabled {get{return true;}} } }

internal static class NativeLoopTimingsTests
{
    sealed class RootType{} sealed class GroupType{} sealed class TargetA{} sealed class TargetB{}
    sealed class Other{} sealed class StartA{} sealed class EndA{} sealed class StartB{} sealed class EndB{}
    static int calls;
    static void Original(){calls++;} static void MarkerStart(){calls+=10;} static void MarkerEnd(){calls+=100;}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static NativeLoopTimings.MarkerPair Pair(Type target,Type start,Type end)
    {return new NativeLoopTimings.MarkerPair{Target=target,StartType=start,EndType=end,Start=MarkerStart,End=MarkerEnd};}
    static PlayerLoopSystem Node(Type type,int pointer=0,PlayerLoopSystem[] children=null)
    {return new PlayerLoopSystem{type=type,updateDelegate=Original,updateFunction=new IntPtr(pointer),loopConditionFunction=new IntPtr(pointer+1000),subSystemList=children};}
    static int Find(PlayerLoopSystem[] nodes,Type type){for(int i=0;i<nodes.Length;i++)if(nodes[i].type==type)return i;return -1;}
    static int Count(PlayerLoopSystem node,Type type)
    {int n=node.type==type?1:0;if(node.subSystemList!=null)for(int i=0;i<node.subSystemList.Length;i++)n+=Count(node.subSystemList[i],type);return n;}
    static void Main()
    {
        var targetChildren=new[]{Node(typeof(Other),77)};
        var target=Node(typeof(TargetA),41,targetChildren);
        var root=Node(typeof(RootType),12,new[]{Node(typeof(Other),13),Node(typeof(GroupType),14,new[]{Node(typeof(Other),15),target,Node(typeof(Other),16)})});
        var pairs=new[]{Pair(typeof(TargetA),typeof(StartA),typeof(EndA)),Pair(typeof(TargetB),typeof(StartB),typeof(EndB))};
        int[] counts;bool changed;var rewritten=NativeLoopTimings.Rewrite(root,pairs,out counts,out changed);
        Check(changed && counts[0]==1 && counts[1]==0,"unique target discovery or missing-target report failed");
        var group=rewritten.subSystemList[1];var children=group.subSystemList;
        Check(children.Length==5 && children[0].type==typeof(Other) && children[1].type==typeof(StartA) && children[2].type==typeof(TargetA) && children[3].type==typeof(EndA) && children[4].type==typeof(Other),"paired markers changed native order");
        Check(children[2].updateDelegate==target.updateDelegate && children[2].updateFunction==target.updateFunction && children[2].loopConditionFunction==target.loopConditionFunction,"original function pointers/delegates changed");
        Check(Object.ReferenceEquals(children[2].subSystemList,targetChildren),"target's native children were replaced");
        Check(rewritten.subSystemList[0].updateDelegate==root.subSystemList[0].updateDelegate && rewritten.subSystemList[0].updateFunction==root.subSystemList[0].updateFunction,"unmatched subsystem changed");

        // Duplicate target types are ambiguous: leave them untouched and report the
        // phase unavailable rather than inserting markers around the wrong node.
        var duplicate=Node(typeof(RootType),0,new[]{Node(typeof(TargetA),51),Node(typeof(TargetA),52)});
        var duplicateChildren=duplicate.subSystemList;var unchanged=NativeLoopTimings.Rewrite(duplicate,new[]{pairs[0]},out counts,out changed);
        Check(!changed && counts[0]==2 && Object.ReferenceEquals(duplicateChildren,unchanged.subSystemList),"ambiguous targets were rewritten");

        // Teardown strips only private marker types and retains foreign loop edits.
        var ownStart=typeof(NativeLoopTimings).GetNestedType("UpdateStart",System.Reflection.BindingFlags.NonPublic);
        var ownEnd=typeof(NativeLoopTimings).GetNestedType("UpdateEnd",System.Reflection.BindingFlags.NonPublic);
        var withForeign=Node(typeof(RootType),0,new[]{Marker(ownStart),Node(typeof(Other),61),Marker(typeof(Other)),Marker(ownEnd)});
        var cleaned=NativeLoopTimings.RemoveOwnedMarkers(withForeign,out changed);
        Check(changed && cleaned.subSystemList.Length==2 && cleaned.subSystemList[0].type==typeof(Other) && cleaned.subSystemList[1].type==typeof(Other),"cleanup removed unrelated loop nodes or retained owned markers");

        // Two-frame buffering retains the previous complete frame while the current
        // frame is being written, includes zero for absent phases, and safely wraps.
        var frames=new NativeLoopTimings.DoubleFrameBuffer(2);frames.Add(20,1,1.25);frames.Add(20,1,2.75);
        Check(Math.Abs(frames.Read(20,1)-4)<.0001 && frames.Read(20,0)==0,"per-frame phase accumulation/zero failed");
        frames.Touch(21);Check(Math.Abs(frames.Read(20,1)-4)<.0001 && frames.Read(21,1)==0,"double buffer lost previous frame");
        frames.Add(22,0,9);Check(frames.Read(20,1)==0 && frames.Read(21,1)==0 && frames.Read(22,0)==9,"double buffer wrap failed");

        var ring=new NativeLoopTimings.SampleRing(3);ring.Add(1);ring.Add(2);ring.Add(3);ring.Add(4);
        string stats=ring.Summary();Check(ring.Count==3 && stats.Contains("p50/p95/max=3.00/4.00/4.00ms n=3"),"bounded sample ring wrap/percentiles failed");
        ring.Clear();Check(ring.Count==0 && ring.Summary()=="no samples","ring reset failed");
        var cameraRoot=Node(typeof(RootType),0,new[]{Node(typeof(PlayerRenderCameras),71),Node(typeof(Other),72)});
        string topology=NativeLoopTimings.DescribeTopology(cameraRoot);
        Check(topology.Contains("postLateUpdateChildren=0") && topology.Contains("finishFrameRenderingChildren=-1") && topology.Contains("playerRenderCameras=present"),"runtime topology description missed camera node");
        Check(NativeLoopTimings.DescribeTopology(Node(typeof(RootType))).Contains("playerRenderCameras=absent"),"runtime topology description invented camera node");
        Console.WriteLine("PASS: native loop tree rewrite preserves subsystem order/functions, rejects ambiguous targets, removes only owned markers, bounds frame/ring sampling, and describes the live topology.");
    }
    sealed class PlayerRenderCameras {}
    static PlayerLoopSystem Marker(Type type){return new PlayerLoopSystem{type=type,updateDelegate=MarkerStart};}
}
