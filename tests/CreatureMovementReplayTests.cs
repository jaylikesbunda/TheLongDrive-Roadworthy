using System;
using UnityEngine;

// Move is replaced with the real patched native IL by ExtractReplay. The
// stand-ins implement only vector math and the Rigidbody velocity boundary.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero {get{return new Vector3();}}
        public static Vector3 up {get{return new Vector3(0,1,0);}}
        public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator *(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}
        public static Vector3 Project(Vector3 a,Vector3 b){float d=b.x*b.x+b.y*b.y+b.z*b.z;return d==0?zero:b*((a.x*b.x+a.y*b.y+a.z*b.z)/d);}
        public static Vector3 ProjectOnPlane(Vector3 a,Vector3 b){return a-Project(a,b);}
        public static Vector3 ClampMagnitude(Vector3 a,float max){float s=a.x*a.x+a.y*a.y+a.z*a.z;return s>max*max?a*(max/(float)Math.Sqrt(s)):a;}
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t){return a+(b-a)*Math.Max(0,Math.Min(1,t));}
    }
    public class Rigidbody {public bool isKinematic;public Vector3 velocity {get;set;}}
    public static class Time {public static float deltaTime {get;set;}}
}
namespace TldMp
{
    public static class StableGameBridge
    {
        public static bool CreatureMoveGate(movecreaturescript c){if(!c.RB.isKinematic)return true;c.moveDir=Vector3.zero;return false;}
    }
}
public class movecreaturescript
{
    public Rigidbody RB;
    public float speed,acceleration,modifierValue;
    public bool modifier;
    public Vector3 moveDir;
    public Vector3 horizontalVelocity,verticalVelocity,horizontalDir;
    public void Move(){throw new Exception("Production Move IL was not extracted.");}
}
static class CreatureMovementReplayTests
{
    static int checks;
    static void Equal(Vector3 actual,Vector3 expected)
    {
        checks++;
        if(Math.Abs(actual.x-expected.x)>0.00001f || Math.Abs(actual.y-expected.y)>0.00001f || Math.Abs(actual.z-expected.z)>0.00001f)
            throw new Exception("Movement replay mismatch #"+checks+": "+actual.x+","+actual.y+","+actual.z);
    }
    static void Main()
    {
        var c=new movecreaturescript{RB=new Rigidbody(),speed=10,acceleration=20};
        Time.deltaTime=0;
        c.RB.velocity=new Vector3(3,2,7);c.Move();Equal(c.RB.velocity,new Vector3(3,2,7));
        Time.deltaTime=0.025f;
        c.RB.velocity=new Vector3(8,-3,12);c.Move();Equal(c.RB.velocity,new Vector3(4,-3,6));
        c.RB.velocity=new Vector3(12,-3,8);c.Move();Equal(c.RB.velocity,new Vector3(6,-3,4));
        c.RB.velocity=new Vector3(0,2,0);c.moveDir=new Vector3(0,0,1);c.Move();Equal(c.RB.velocity,new Vector3(0,2,5));Equal(c.moveDir,Vector3.zero);
        c.modifier=true;c.modifierValue=0.5f;c.RB.velocity=Vector3.zero;c.moveDir=new Vector3(0,0,1);c.Move();Equal(c.RB.velocity,new Vector3(0,0,2.5f));
        c.RB.isKinematic=true;c.RB.velocity=new Vector3(1,2,3);c.moveDir=new Vector3(1,0,0);c.Move();Equal(c.RB.velocity,new Vector3(1,2,3));Equal(c.moveDir,Vector3.zero);
        Console.WriteLine("PASS "+checks+" extracted production creature-movement checks");
    }
}
