using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.LowLevel;

namespace TldMp
{
    // Opt-in, inclusive phase timers around exact Unity PlayerLoop subsystem
    // nodes. The original nodes are copied intact and remain in their order.
    internal static class NativeLoopTimings
    {
        internal const int PhaseCount=6;
        internal static Action<int,int,long,long> PhaseObserved;
        internal static Action<int,int> FrameCompleted;
        internal const int Capacity=1024;
        internal static readonly string[] Labels=new string[]{"ScriptRunBehaviourUpdate","ScriptRunBehaviourFixedUpdate","PhysicsFixedUpdate","ScriptRunBehaviourLateUpdate","UpdateAllRenderers","FinishFrameRendering"};
        static readonly Type[] startTypes=new Type[]{typeof(UpdateStart),typeof(FixedScriptsStart),typeof(PhysicsStart),typeof(LateScriptsStart),typeof(RenderersStart),typeof(FinishRenderingStart)};
        static readonly Type[] endTypes=new Type[]{typeof(UpdateEnd),typeof(FixedScriptsEnd),typeof(PhysicsEnd),typeof(LateScriptsEnd),typeof(RenderersEnd),typeof(FinishRenderingEnd)};
        static readonly PlayerLoopSystem.UpdateFunction[] starts=new PlayerLoopSystem.UpdateFunction[]{StartUpdate,StartFixedScripts,StartPhysics,StartLateScripts,StartRenderers,StartFinishRendering};
        static readonly PlayerLoopSystem.UpdateFunction[] ends=new PlayerLoopSystem.UpdateFunction[]{EndUpdate,EndFixedScripts,EndPhysics,EndLateScripts,EndRenderers,EndFinishRendering};
        static readonly Type[] targetTypes=new Type[]{
            typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
            typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate),
            typeof(UnityEngine.PlayerLoop.FixedUpdate.PhysicsFixedUpdate),
            typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate),
            typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAllRenderers),
            typeof(UnityEngine.PlayerLoop.PostLateUpdate.FinishFrameRendering)};
        sealed class UpdateStart{} sealed class UpdateEnd{}
        sealed class FixedScriptsStart{} sealed class FixedScriptsEnd{}
        sealed class PhysicsStart{} sealed class PhysicsEnd{}
        sealed class LateScriptsStart{} sealed class LateScriptsEnd{}
        sealed class RenderersStart{} sealed class RenderersEnd{}
        sealed class FinishRenderingStart{} sealed class FinishRenderingEnd{}
        internal struct MarkerPair
        {
            internal Type Target,StartType,EndType;
            internal PlayerLoopSystem.UpdateFunction Start,End;
        }
        internal sealed class DoubleFrameBuffer
        {
            readonly double[,] values;readonly int[] frameIDs;
            internal DoubleFrameBuffer(int phases){values=new double[2,phases];frameIDs=new int[]{Int32.MinValue,Int32.MinValue};}
            internal void Touch(int frame)
            {int slot=frame&1;if(frameIDs[slot]==frame)return;frameIDs[slot]=frame;for(int i=0;i<values.GetLength(1);i++)values[slot,i]=0;}
            internal void Add(int frame,int phase,double value)
            {Touch(frame);values[frame&1,phase]+=value;}
            internal double Read(int frame,int phase)
            {int slot=frame&1;return frameIDs[slot]==frame?values[slot,phase]:0;}
            internal void Reset(){for(int s=0;s<2;s++){frameIDs[s]=Int32.MinValue;for(int i=0;i<values.GetLength(1);i++)values[s,i]=0;}}
        }
        internal sealed class SampleRing
        {
            readonly double[] values,scratch;int at,count;
            internal SampleRing(int capacity){values=new double[capacity];scratch=new double[capacity];}
            internal int Count{get{return count;}}
            internal void Add(double value){values[at]=value;at=(at+1)%values.Length;if(count<values.Length)count++;}
            internal void Clear(){Array.Clear(values,0,values.Length);Array.Clear(scratch,0,scratch.Length);at=count=0;}
            internal string Summary()
            {
                if(count==0)return "no samples";Array.Copy(values,0,scratch,0,count);Array.Sort(scratch,0,count);
                double p50=scratch[Math.Max(0,(int)Math.Ceiling(.50*count)-1)],p95=scratch[Math.Max(0,(int)Math.Ceiling(.95*count)-1)],max=scratch[count-1];
                return "p50/p95/max="+p50.ToString("F2",CultureInfo.InvariantCulture)+"/"+p95.ToString("F2",CultureInfo.InvariantCulture)+"/"+max.ToString("F2",CultureInfo.InvariantCulture)+"ms n="+count;
            }
        }

        static bool attempted,installed,enabled;
        static string failure;
        static string topology;
        static bool[] available,active;
        static long[] startsAt;
        static int[] activeFrame;
        static DoubleFrameBuffer frameBuffer;
        static SampleRing[] phaseSamples;
        static int[] lastSampledFrame;
        static int lastUpdateEnd=Int32.MinValue;

        static MarkerPair[] Pairs()
        {
            var pairs=new MarkerPair[PhaseCount];
            for(int i=0;i<PhaseCount;i++)pairs[i]=new MarkerPair{Target=targetTypes[i],StartType=startTypes[i],EndType=endTypes[i],Start=starts[i],End=ends[i]};
            return pairs;
        }
        static bool IsOwnedMarker(Type type)
        {for(int i=0;i<PhaseCount;i++)if(type==startTypes[i] || type==endTypes[i])return true;return false;}
        static PlayerLoopSystem Marker(Type type,PlayerLoopSystem.UpdateFunction callback)
        {return new PlayerLoopSystem{type=type,updateDelegate=callback};}
        static int Count(PlayerLoopSystem node,Type target)
        {
            int count=node.type==target?1:0;var children=node.subSystemList;
            if(children!=null)for(int i=0;i<children.Length;i++)count+=Count(children[i],target);
            return count;
        }
        static bool HasMarkers(PlayerLoopSystem node)
        {
            if(IsOwnedMarker(node.type))return true;
            var children=node.subSystemList;if(children!=null)for(int i=0;i<children.Length;i++)if(HasMarkers(children[i]))return true;
            return false;
        }
        static bool FindNode(PlayerLoopSystem node,string fullName,out PlayerLoopSystem found)
        {
            if(node.type!=null && node.type.FullName==fullName){found=node;return true;}
            var children=node.subSystemList;
            if(children!=null)for(int i=0;i<children.Length;i++)if(FindNode(children[i],fullName,out found))return true;
            found=default(PlayerLoopSystem);return false;
        }
        internal static bool FindNodeContains(PlayerLoopSystem node,string token,out PlayerLoopSystem found)
        {
            if(node.type!=null && node.type.FullName!=null && node.type.FullName.IndexOf(token,StringComparison.Ordinal)>=0){found=node;return true;}
            var children=node.subSystemList;
            if(children!=null)for(int i=0;i<children.Length;i++)if(FindNodeContains(children[i],token,out found))return true;
            found=default(PlayerLoopSystem);return false;
        }
        // One bounded description of the runtime tree the timers were installed
        // on. Managed marker types alone do not prove the live topology.
        internal static string DescribeTopology(PlayerLoopSystem root)
        {
            try
            {
                PlayerLoopSystem post;
                bool hasPost=FindNode(root,"UnityEngine.PlayerLoop.PostLateUpdate",out post);
                int postChildren=hasPost && post.subSystemList!=null?post.subSystemList.Length:0;
                int finishChildren=-1;
                var names=new StringBuilder();
                if(hasPost && post.subSystemList!=null)
                {
                    for(int i=0;i<post.subSystemList.Length;i++)
                    {
                        var child=post.subSystemList[i];
                        if(i!=0)names.Append(',');
                        names.Append(child.type==null?"<null>":child.type.Name);
                        if(child.type!=null && child.type.FullName=="UnityEngine.PlayerLoop.PostLateUpdate.FinishFrameRendering")
                            finishChildren=child.subSystemList==null?0:child.subSystemList.Length;
                    }
                }
                PlayerLoopSystem cameras;
                bool hasCameras=FindNodeContains(root,"PlayerRenderCameras",out cameras);
                return "playerLoopTopology{postLateUpdateChildren="+postChildren+
                    " finishFrameRenderingChildren="+finishChildren+
                    " playerRenderCameras="+(hasCameras?"present":"absent")+
                    " postLateUpdate=["+names.ToString()+"]}";
            }
            catch(Exception e)
            {
                return "playerLoopTopology{unavailable:"+e.GetType().Name+"}";
            }
        }
        static PlayerLoopSystem RewriteNode(PlayerLoopSystem node,MarkerPair[] pairs,int[] counts,out bool changed)
        {
            changed=false;var children=node.subSystemList;if(children==null || children.Length==0)return node;
            List<PlayerLoopSystem> output=null;
            for(int i=0;i<children.Length;i++)
            {
                bool childChanged;var child=RewriteNode(children[i],pairs,counts,out childChanged);int match=-1;
                if(!IsOwnedMarker(child.type))for(int p=0;p<pairs.Length;p++)if(counts[p]==1 && child.type==pairs[p].Target){match=p;break;}
                if((childChanged || match>=0) && output==null)
                {output=new List<PlayerLoopSystem>(children.Length+4);for(int prior=0;prior<i;prior++)output.Add(children[prior]);}
                if(output!=null)
                {
                    if(match>=0)output.Add(Marker(pairs[match].StartType,pairs[match].Start));
                    output.Add(child);
                    if(match>=0)output.Add(Marker(pairs[match].EndType,pairs[match].End));
                }
                changed|=childChanged || match>=0;
            }
            if(output!=null)node.subSystemList=output.ToArray();
            return node;
        }
        internal static PlayerLoopSystem Rewrite(PlayerLoopSystem root,MarkerPair[] pairs,out int[] counts,out bool changed)
        {
            counts=new int[pairs.Length];for(int i=0;i<pairs.Length;i++)counts[i]=Count(root,pairs[i].Target);
            return RewriteNode(root,pairs,counts,out changed);
        }
        static PlayerLoopSystem RemoveNode(PlayerLoopSystem node,Type[] ownedMarkers,out bool changed)
        {
            changed=false;var children=node.subSystemList;if(children==null || children.Length==0)return node;
            List<PlayerLoopSystem> output=null;
            for(int i=0;i<children.Length;i++)
            {
                if(IsOwnedMarker(children[i].type,ownedMarkers))
                {
                    if(output==null){output=new List<PlayerLoopSystem>(children.Length);for(int prior=0;prior<i;prior++)output.Add(children[prior]);}
                    changed=true;continue;
                }
                bool childChanged;var child=RemoveNode(children[i],ownedMarkers,out childChanged);
                if(childChanged && output==null){output=new List<PlayerLoopSystem>(children.Length);for(int prior=0;prior<i;prior++)output.Add(children[prior]);}
                if(output!=null)output.Add(child);changed|=childChanged;
            }
            if(output!=null)node.subSystemList=output.ToArray();
            return node;
        }
        static bool IsOwnedMarker(Type type,Type[] ownedMarkers)
        {for(int i=0;i<ownedMarkers.Length;i++)if(type==ownedMarkers[i])return true;return false;}
        internal static PlayerLoopSystem RemoveOwnedMarkers(PlayerLoopSystem root,out bool changed)
        {return RemoveNode(root,OwnedMarkerTypes(),out changed);}
        internal static PlayerLoopSystem RemoveOwnedMarkers(PlayerLoopSystem root,Type[] ownedMarkers,out bool changed)
        {return RemoveNode(root,ownedMarkers,out changed);}
        static Type[] OwnedMarkerTypes()
        {var result=new Type[PhaseCount*2];for(int i=0;i<PhaseCount;i++){result[i*2]=startTypes[i];result[i*2+1]=endTypes[i];}return result;}

        static void InitializeBuffers()
        {
            available=new bool[PhaseCount];active=new bool[PhaseCount];startsAt=new long[PhaseCount];activeFrame=new int[PhaseCount];
            frameBuffer=new DoubleFrameBuffer(PhaseCount);phaseSamples=new SampleRing[PhaseCount];lastSampledFrame=new int[PhaseCount];
            for(int i=0;i<PhaseCount;i++){phaseSamples[i]=new SampleRing(Capacity);lastSampledFrame[i]=Int32.MinValue;}
            lastUpdateEnd=Int32.MinValue;enabled=true;
        }
        internal static void TryInstall()
        {
            if(!FrameDiagnostics.Enabled || attempted)return;
            attempted=true;if(!enabled)InitializeBuffers();failure=null;
            try
            {
                var current=PlayerLoop.GetCurrentPlayerLoop();
                topology=DescribeTopology(current);
                if(HasMarkers(current))
                {
                    for(int i=0;i<PhaseCount;i++)available[i]=Count(current,startTypes[i])==1 && Count(current,endTypes[i])==1 && Count(current,targetTypes[i])==1;
                    installed=true;failure="existing owned markers detected; duplicate insertion skipped";return;
                }
                var pairs=Pairs();var counts=new int[PhaseCount];for(int i=0;i<PhaseCount;i++)counts[i]=Count(current,pairs[i].Target);
                for(int i=0;i<PhaseCount;i++)available[i]=counts[i]==1;
                bool changed;var rewritten=Rewrite(current,pairs,out counts,out changed);
                for(int i=0;i<PhaseCount;i++)available[i]=counts[i]==1;
                if(!changed){failure="no unique PlayerLoop targets found";return;}
                PlayerLoop.SetPlayerLoop(rewritten);installed=true;
                var missing=new List<string>();for(int i=0;i<PhaseCount;i++)if(!available[i])missing.Add(Labels[i]+" count="+counts[i]);
                if(missing.Count>0)failure="unavailable targets: "+String.Join(",",missing.ToArray());
            }
            catch(Exception e){failure="PlayerLoop API/setup failed: "+e.GetType().Name+": "+e.Message;installed=false;for(int i=0;i<PhaseCount;i++)available[i]=false;}
        }
        static void EnsureFrame(int frame)
        {frameBuffer.Touch(frame);}
        static void Start(int phase)
        {
            if(!enabled || !installed || !available[phase])return;
            int frame=Time.frameCount;EnsureFrame(frame);startsAt[phase]=Stopwatch.GetTimestamp();activeFrame[phase]=frame;active[phase]=true;
        }
        static void End(int phase)
        {
            if(!enabled || !installed || !available[phase] || !active[phase])return;
            int frame=Time.frameCount;long stop=Stopwatch.GetTimestamp();
            if(activeFrame[phase]==frame){frameBuffer.Add(frame,phase,(stop-startsAt[phase])*1000.0/Stopwatch.Frequency);var observer=PhaseObserved;if(observer!=null)observer(frame,phase,startsAt[phase],stop);if(phase==0)lastUpdateEnd=frame;}
            active[phase]=false;
        }
        static void StartUpdate(){Start(0);}static void EndUpdate(){End(0);}
        static void StartFixedScripts(){Start(1);}static void EndFixedScripts(){End(1);}
        static void StartPhysics(){Start(2);}static void EndPhysics(){End(2);}
        static void StartLateScripts(){Start(3);}static void EndLateScripts(){End(3);}
        static void StartRenderers(){Start(4);}static void EndRenderers(){End(4);}
        static void StartFinishRendering(){Start(5);}static void EndFinishRendering(){End(5);}

        // Called once at the start of each rendered Update. The previous frame is
        // complete by then; absent fixed-update callbacks intentionally contribute 0.
        internal static void SamplePreviousFrame(int currentFrame)
        {
            if(!enabled || currentFrame<=0)return;int completed=currentFrame-1;
            // A complete Update pair proves the loop actually executed. Without
            // it, missing callbacks must not masquerade as zero-cost frames.
            if(lastUpdateEnd!=completed)return;
            int configuredMask=0;for(int phase=0;phase<PhaseCount;phase++)if(available[phase])configuredMask|=1<<phase;
            var observer=FrameCompleted;if(observer!=null)observer(completed,configuredMask);
            for(int phase=0;phase<PhaseCount;phase++)
            {
                if(!available[phase] || lastSampledFrame[phase]==completed)continue;
                phaseSamples[phase].Add(frameBuffer.Read(completed,phase));
                lastSampledFrame[phase]=completed;
            }
        }
        static string Summary(int phase)
        {
            if(!available[phase])return "unavailable"+(String.IsNullOrEmpty(failure)?"":"("+failure+")");
            if(lastUpdateEnd<Time.frameCount-1)return "unavailable(no completed Update heartbeat)";
            return phaseSamples[phase].Summary();
        }
        internal static string Report()
        {
            if(!enabled)return "nativeLoopTimings{disabled}";
            string result="nativeLoopTimings{inclusive phase windows; do not sum as total CPU";
            for(int i=0;i<PhaseCount;i++)result+="; "+Labels[i]+"="+Summary(i);
            result+="; "+(topology??"playerLoopTopology{unavailable}");
            return result+"}";
        }
        internal static void ResetWindow()
        {if(phaseSamples!=null)for(int i=0;i<phaseSamples.Length;i++)phaseSamples[i].Clear();}
        internal static void Shutdown()
        {
            if(installed)
            {
                try{var current=PlayerLoop.GetCurrentPlayerLoop();bool changed;var cleaned=RemoveOwnedMarkers(current,out changed);if(changed)PlayerLoop.SetPlayerLoop(cleaned);}
                catch(Exception e){failure="PlayerLoop cleanup failed: "+e.GetType().Name+": "+e.Message;}
            }
            attempted=installed=false;lastUpdateEnd=Int32.MinValue;for(int i=0;i<PhaseCount;i++)if(available!=null){available[i]=false;active[i]=false;lastSampledFrame[i]=Int32.MinValue;}
            if(frameBuffer!=null)frameBuffer.Reset();if(phaseSamples!=null)for(int i=0;i<phaseSamples.Length;i++)phaseSamples[i].Clear();
        }
    }
}
