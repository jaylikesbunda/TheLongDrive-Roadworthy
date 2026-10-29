using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TldMp;
using UnityEngine;

public static class PlacementCenterParityTests
{
    static int checks;

    sealed class TracedCenters:IList<Vector3d>
    {
        readonly List<Vector3d> values;
        readonly List<int> reads=new List<int>();
        internal TracedCenters(IList<Vector3d> source)
        {values=source==null?null:new List<Vector3d>(source);}
        internal IList<int> Reads{get{return reads;}}
        internal void ClearReads(){reads.Clear();}
        public Vector3d this[int index]
        {get{reads.Add(index);return values[index];}set{values[index]=value;}}
        public int Count{get{return values==null?0:values.Count;}}
        public bool IsReadOnly{get{return false;}}
        public IEnumerator<Vector3d> GetEnumerator()
        {for(int i=0;i<Count;i++)yield return this[i];}
        IEnumerator IEnumerable.GetEnumerator(){return GetEnumerator();}
        public int IndexOf(Vector3d value){return values.IndexOf(value);}
        public void Insert(int index,Vector3d value){values.Insert(index,value);}
        public void RemoveAt(int index){values.RemoveAt(index);}
        public void Add(Vector3d value){values.Add(value);}
        public void Clear(){values.Clear();}
        public bool Contains(Vector3d value){return values.Contains(value);}
        public void CopyTo(Vector3d[] array,int arrayIndex){values.CopyTo(array,arrayIndex);}
        public bool Remove(Vector3d value){return values.Remove(value);}
    }

    static void Check(bool condition,string reason)
    {
        checks++;
        if(!condition)throw new Exception(reason);
    }

    // This is the prior InAnyRadius body over a captured view of genArounds:
    // it preserves the old ordered loop and the historical float -> double ->
    // float radius conversion around WithinRadius.
    static bool Original(Vector3d position,IList<Vector3d> centers,float currentDrawDist)
    {
        if(centers==null)return true;
        double radius=currentDrawDist;
        foreach(Vector3d center in centers)
            if(StableMap.WithinRadius(position,center,(float)radius))return true;
        return false;
    }

    static void Compare(Vector3d position,IList<Vector3d> centers,float radius,string label)
    {
        if(centers==null)
        {
            bool oldMissing=Original(position,null,radius);
            bool newMissing=StableMap.WithinAnyRadius(position,null,radius);
            Check(oldMissing==newMissing,label+" changed missing-map-setting fallback");
            return;
        }
        var traced=new TracedCenters(centers);
        bool before=Original(position,traced,radius);
        int[] oldReads=new List<int>(traced.Reads).ToArray();
        traced.ClearReads();
        bool after=StableMap.WithinAnyRadius(position,traced,radius);
        int[] newReads=new List<int>(traced.Reads).ToArray();
        Check(before==after,label+" changed placement membership");
        Check(oldReads.Length==newReads.Length,label+" changed the number of radius checks");
        for(int i=0;i<oldReads.Length;i++)Check(oldReads[i]==newReads[i],label+" changed radius-check order");
    }

    public static int Main()
    {
        var centers=new List<Vector3d>
        {
            new Vector3d(0,100,0),
            new Vector3d(1000,-100,1000),
            new Vector3d(0,100,0) // active-player duplicates are preserved
        };
        Compare(new Vector3d(300,0,400),centers,500f,"exact boundary with duplicates");
        Compare(new Vector3d(300.001,0,400),centers,500f,"just outside boundary");
        Compare(new Vector3d(1000,0,1000),centers,0f,"zero-radius center");
        Compare(new Vector3d(4,0,0),new List<Vector3d>(),10f,"no active centers");
        Compare(new Vector3d(4,0,0),null,10f,"missing map-setting fallback");

        var nonFinite=new List<Vector3d>
        {
            new Vector3d(Double.NaN,0,0),
            new Vector3d(Double.PositiveInfinity,0,0),
            new Vector3d(0,0,Double.NegativeInfinity)
        };
        Compare(new Vector3d(0,0,0),nonFinite,100f,"non-finite centers");
        Compare(new Vector3d(0,0,0),centers,Single.NaN,"non-finite radius");
        Compare(new Vector3d(0,0,0),centers,Single.PositiveInfinity,"infinite radius");

        var random=new System.Random(904213);
        for(int sample=0;sample<4096;sample++)
        {
            int count=random.Next(0,9);
            var activeCenters=new List<Vector3d>(count+2);
            for(int i=0;i<count;i++)
            {
                var center=new Vector3d(random.Next(-10000,10001)/10.0,
                    random.Next(-100,101),random.Next(-10000,10001)/10.0);
                activeCenters.Add(center);
                if((i%3)==0)activeCenters.Add(center);
            }
            var position=new Vector3d(random.Next(-20000,20001)/10.0,
                random.Next(-1000,1001),random.Next(-20000,20001)/10.0);
            float radius=(float)random.Next(0,20001)/10f;
            Compare(position,activeCenters,radius,"deterministic randomized sample "+sample);
        }

        // Stable place-order parity against the native LINQ key selector and
        // OrderBy stable sort. Ties must retain the original dictionary order.
        var randomOrder=new System.Random(90210);
        for(int sample=0;sample<300;sample++)
        {
            int count=sample==0?0:(sample==1?1:randomOrder.Next(2,80));
            var order=new List<Vector3d>(count);
            for(int i=0;i<count;i++)
            {
                if(sample==2)order.Add(new Vector3d(i%3,0,0));
                else if(sample==3 && i%7==0)order.Add(new Vector3d(double.NaN,0,0));
                else if(sample==4 && i%5==0)order.Add(new Vector3d(-0.0,0,0));
                else order.Add(new Vector3d(randomOrder.Next(-4,5),randomOrder.Next(-1,2),randomOrder.Next(-4,5)));
            }
            var origin=sample==5?new Vector3d(1,2,3):new Vector3d(randomOrder.Next(-4,5),randomOrder.Next(-1,2),randomOrder.Next(-4,5));
            var expected=order.OrderBy(x=>(x-origin).sqrMagnitude).ToList();
            var actual=new List<Vector3d>(order);
            StableMap.SortPlaceOrder(actual,origin);
            Check(actual.Count==expected.Count,"place-order count changed");
            for(int i=0;i<actual.Count;i++)
            {
                Check(BitConverter.DoubleToInt64Bits(actual[i].x)==BitConverter.DoubleToInt64Bits(expected[i].x) &&
                      BitConverter.DoubleToInt64Bits(actual[i].y)==BitConverter.DoubleToInt64Bits(expected[i].y) &&
                      BitConverter.DoubleToInt64Bits(actual[i].z)==BitConverter.DoubleToInt64Bits(expected[i].z),
                      "place-order sequence differs from stable OrderBy");
            }
        }
        Console.WriteLine("PASS: placement center radius parity ("+checks+" old/new comparisons; exact radius boundaries, duplicate/order-preserving center lists, non-finite inputs, randomized cases, and stable place-order parity).");
        return 0;
    }
}
