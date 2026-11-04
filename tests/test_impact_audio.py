"""Production impact pool replay; counted Unity boundary, no live mixer claim."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
class ImpactAudio(unittest.TestCase):
 def test_pool(self):
  root=Path(__file__).resolve().parents[1];source=(root/'runtime/StableGameBridge.cs').read_text()
  start=source.index('        // Pool only collision');end=source.index('        // Only sibling colliders',start)
  fixture=r'''
using System;using System.Collections.Generic;using System.Diagnostics;
struct Vector3{public float x,y,z;public float sqrMagnitude{get{return x*x+y*y+z*z;}}public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3{x=a.x-b.x,y=a.y-b.y,z=a.z-b.z};}}
struct Quaternion{public static Quaternion identity{get{return new Quaternion();}}}
class Transform{public Vector3 position,localScale;public void SetParent(Transform p,bool world){}public void SetPositionAndRotation(Vector3 p,Quaternion q){position=p;}}
class AudioClip{public float length=1;}
namespace UnityEngine.Audio{class AudioMixerGroup{}}
class AudioSource{public bool enabled;public AudioClip clip;public byte priority;public UnityEngine.Audio.AudioMixerGroup outputAudioMixerGroup;public float minDistance=.5f,maxDistance=200,volume,pitch,spatialBlend;public int Plays;public void Stop(){}public void Play(){Plays++;}}
class stimulscript{public bool DontStartCheckingAudioSources=true;public float Value;public void SetAudioValue(float volume){Value=volume;}}
class newAiScript{} class aiscript{} class carscript{} class fpscontroller{}
class GameObject{public int layer; public T GetComponentInParent<T>() where T:class{return null;} public static int Created;public bool Active,Destroyed;public Transform transform=new Transform();public AudioSource Audio=new AudioSource();public stimulscript Stimulus=new stimulscript();public GameObject(string name=""){Created++;}public void SetActive(bool x){Active=x;}public T GetComponent<T>() where T:class{return typeof(T)==typeof(AudioSource)?Audio as T:Stimulus as T;}}
namespace UnityEngine{class Object{public static int Instantiates;public static GameObject Instantiate(GameObject prefab,Transform parent,bool world){Instantiates++;return new GameObject();}public static void Destroy(GameObject g){g.Destroyed=true;g.Active=false;}}class Random{public static int Calls;public static float Range(float a,float b){Calls++;return 1.1f;}}}
class mainscript{public static mainscript s;public GameObject Audio=new GameObject();public GameObject player=new GameObject();public Transform transform=new Transform();public UnityEngine.Audio.AudioMixerGroup defGroup=new UnityEngine.Audio.AudioMixerGroup();public static int Native;public static float LastPitch;public static GameObject PlayClipAtPoint(AudioClip c,Vector3 p,float v,float pitch,UnityEngine.Audio.AudioMixerGroup g,float spat,byte priority){Native++;LastPitch=pitch;if(c==null)throw new ArgumentNullException();return new GameObject();}}
class Time{public static float time,unscaledTime;}
class ModDiagnostics{public static bool On;}
class Debug{public static void Log(object x){}public static void LogWarning(object x){}}
class Collision{public int contactCount;}
class Probe{
HELPERS
 static int checks;static void Check(bool x){checks++;if(!x)throw new Exception("assertion "+checks);}
 static void Main(){
  Check(!HasCollisionContacts(null));Check(!HasCollisionContacts(new Collision()));Check(HasCollisionContacts(new Collision{contactCount=1}));
  mainscript.s=new mainscript();var clip=new AudioClip();
  PickupImpactSound(clip,new Vector3(),.5f,7);Check(mainscript.Native==1);
  for(int i=0;i<3000 && impactCreated<ImpactCapacity;i++)TickImpactAudio(true,false);
  Check(impactCreated==2048 && impactFree.Count==2048);int created=GameObject.Created;
  var emitted=new List<GameObject>();
  for(int i=0;i<2048;i++){float volume=i==100?10:.5f;var obj=CarImpactSound(clip,new Vector3{x=i%10},volume,true,mainscript.s.defGroup,7);emitted.Add(obj);Check(obj.Active);Check(obj.Stimulus.Value==volume);}
  Check(GameObject.Created==created);Check(UnityEngine.Random.Calls==2048);Check(impactActive.Count==2048);Check(impactVoices==0);
  TickImpactAudio(false,true);Check(impactVoices==12);Check(emitted[100].Audio.Plays==1);Check(emitted[100].Audio.minDistance==4.5f && emitted[100].Audio.maxDistance==1800);
  int pending=0,stimuli=0;foreach(var e in impactPool){if(e.Pending)pending++;if(e.Object.Active)stimuli++;}Check(pending==0 && stimuli==2048);
  // Saturation reuses silent emitters with bounded work and unchanged RNG draws.
  CarImpactSound(clip,new Vector3(),1,true,mainscript.s.defGroup,7);Check(mainscript.Native==1 && UnityEngine.Random.Calls==2049 && impactReplaced==1);
  for(int i=0;i<10000;i++)CarImpactSound(clip,new Vector3(),1,true,mainscript.s.defGroup,7);
  Check(mainscript.Native==1 && GameObject.Created==created && impactActive.Count==2048 && impactVoices==12 && impactReplaced==10001 && UnityEngine.Random.Calls==12049);
  // Time does not advance merely because fixed-step callbacks repeat.
  for(int i=0;i<20;i++)TickImpactAudio(false,true);Check(impactVoices==12);
  Time.time=Time.unscaledTime=1.1f;TickImpactAudio(false,false);Check(impactActive.Count==0 && impactFree.Count==2048 && impactVoices==0);
  created=GameObject.Created;
  for(int batch=0;batch<5;batch++){
   for(int i=0;i<20;i++)PickupImpactSound(clip,new Vector3(),.5f,7);
   TickImpactAudio(false,true);Check(impactVoices<=48);Time.time+=.051f;Time.unscaledTime+=.051f;
  }
  Check(impactVoices==48);Check(GameObject.Created==created);Check(impactLimited>0);
  // Every reused source resets distances, pitch, clip and mixer independently.
  Time.time=Time.unscaledTime=3;TickImpactAudio(false,false);var reused=PickupImpactSound(clip,new Vector3(),.25f,4);TickImpactAudio(false,true);Check(reused.Audio.minDistance==.5f && reused.Audio.maxDistance==200 && reused.Audio.pitch==1 && reused.Audio.volume==.25f && reused.Audio.clip==clip);
  // World-owner changes retire old stimuli; a fresh pool is warmed for the new world.
  mainscript.s=new mainscript();TickImpactAudio(true,false);Check(!reused.Active && impactActive.Count==0 && impactCreated<=8);
  bool threw=false;try{PickupImpactSound(null,new Vector3(),1,7);}catch(ArgumentNullException){threw=true;}Check(threw);
  ClearImpactPool();Check(impactCreated==0 && impactActive.Count==0 && impactFree.Count==0);
  Console.WriteLine("PASS "+checks+" production impact-pool checks: reuse, per-event stimuli, priority selection, voice/time budgets, catch-up, bounded overflow/RNG, reset and lifetime.");
 }
}
'''.replace('HELPERS',source[start:source.index('        static long npcVehicleImpactCount',start)]+source[source.index('        public static int NpcImpactLayer',start):end])
  with tempfile.TemporaryDirectory(prefix='tld-impact-') as directory:
   code=Path(directory)/'probe.cs';exe=Path(directory)/'probe.exe';code.write_text(fixture)
   compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
   subprocess.run([str(compiler),'/nologo','/out:'+str(exe),str(code)],check=True)
   result=subprocess.run([str(exe)],capture_output=True,text=True)
   if result.returncode: self.fail(result.stdout+result.stderr)
   print(result.stdout)
if __name__=='__main__':unittest.main()
