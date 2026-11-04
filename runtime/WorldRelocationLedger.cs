using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TldMp
{
    // World-owned, not scene-owned. The version string already persists world
    // generation policy; append a bounded canonical payload to that metadata.
    public static class WorldRelocationLedger
    {
        const string Prefix = " [Roadworthy relocation: ";
        const int MaxRecords = 4096, MaxPayloadBytes = 1048576;
        struct Key : IEquatable<Key>
        {
            public int kind, generator, category; public double x,y,z;
            public bool Equals(Key b){return kind==b.kind && generator==b.generator && category==b.category && x==b.x && y==b.y && z==b.z;}
            public override bool Equals(object b){return b is Key && Equals((Key)b);}
            public override int GetHashCode(){unchecked{return (((((kind*397)^generator)*397^category)*397^x.GetHashCode())*397^y.GetHashCode())*397^z.GetHashCode();}}
        }
        public struct Record
        {
            public Vector3d position; public Vector3 rotation; public bool aligned; public int type, seed;
        }
        static Dictionary<Key,Record> records = new Dictionary<Key,Record>();
        static Key MakeKey(int kind,int gen,int category,Vector3d id){return new Key{kind=kind,generator=gen,category=category,x=id.x,y=id.y,z=id.z};}
        static bool Finite(double n){return !double.IsNaN(n) && !double.IsInfinity(n);}
        static bool Valid(Vector3d p){return Finite(p.x) && Finite(p.y) && Finite(p.z);}
        static bool Valid(Vector3 p){return Finite(p.x) && Finite(p.y) && Finite(p.z);}
        public static void Clear(){records.Clear();}
        public static int Count {get{return records.Count;}}
        public static bool TryGet(int kind,int gen,int category,Vector3d id,out Record value){return records.TryGetValue(MakeKey(kind,gen,category,id),out value);}
        public static bool TryRecord(int kind,int gen,int category,Vector3d id,Record value)
        {
            if ((kind!=1 && kind!=2) || !Valid(id) || !Valid(value.position) || !Valid(value.rotation)) throw new InvalidDataException("Invalid POI relocation.");
            Key key=MakeKey(kind,gen,category,id);Record existing;
            if(records.TryGetValue(key,out existing))throw new InvalidOperationException("Duplicate POI relocation.");
            if(records.Count>=MaxRecords)return false;
            records.Add(key,value);return true;
        }
        public static void Remove(int kind,int gen,int category,Vector3d id){records.Remove(MakeKey(kind,gen,category,id));}
        static int Compare(Key a,Key b){int n=a.kind.CompareTo(b.kind);if(n!=0)return n;n=a.generator.CompareTo(b.generator);if(n!=0)return n;n=a.category.CompareTo(b.category);if(n!=0)return n;n=a.x.CompareTo(b.x);if(n!=0)return n;n=a.y.CompareTo(b.y);return n!=0?n:a.z.CompareTo(b.z);}
        static string Encode()
        {
            var keys=new List<Key>(records.Keys);keys.Sort(Compare);
            using(var ms=new MemoryStream())using(var w=new BinaryWriter(ms))
            {
                w.Write((byte)1);w.Write(keys.Count);
                foreach(Key k in keys){Record v=records[k];w.Write(k.kind);w.Write(k.generator);w.Write(k.category);w.Write(k.x);w.Write(k.y);w.Write(k.z);w.Write(v.position.x);w.Write(v.position.y);w.Write(v.position.z);w.Write(v.rotation.x);w.Write(v.rotation.y);w.Write(v.rotation.z);w.Write((byte)(v.aligned?1:0));w.Write(v.type);w.Write(v.seed);}
                w.Flush();if(ms.Length>MaxPayloadBytes)throw new InvalidDataException("POI relocation ledger too large.");return Convert.ToBase64String(ms.ToArray());
            }
        }
        static Dictionary<Key,Record> Decode(string encoded)
        {
            if(encoded.Length>((MaxPayloadBytes+2)/3)*4)throw new InvalidDataException("POI relocation ledger too large.");
            byte[] bytes;try{bytes=Convert.FromBase64String(encoded);}catch(FormatException e){throw new InvalidDataException("Damaged POI relocation ledger.",e);}
            if(bytes.Length>MaxPayloadBytes || Convert.ToBase64String(bytes)!=encoded)throw new InvalidDataException("Noncanonical POI relocation ledger.");
            var result=new Dictionary<Key,Record>();
            try{using(var r=new BinaryReader(new MemoryStream(bytes,false)))
            {
                if(r.ReadByte()!=1)throw new InvalidDataException("Unsupported POI relocation ledger.");int count=r.ReadInt32();
                if(count<0 || count>MaxRecords || bytes.Length!=5+count*81)throw new InvalidDataException("Invalid POI relocation count.");
                Key prev=new Key();bool havePrev=false;
                for(int i=0;i<count;i++){
                    var k=new Key{kind=r.ReadInt32(),generator=r.ReadInt32(),category=r.ReadInt32(),x=r.ReadDouble(),y=r.ReadDouble(),z=r.ReadDouble()};
                    var v=new Record{position=new Vector3d(r.ReadDouble(),r.ReadDouble(),r.ReadDouble()),rotation=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle())};
                    byte aligned=r.ReadByte();v.type=r.ReadInt32();v.seed=r.ReadInt32();v.aligned=aligned==1;
                    if((k.kind!=1 && k.kind!=2)||!Finite(k.x)||!Finite(k.y)||!Finite(k.z)||!Valid(v.position)||!Valid(v.rotation)||aligned>1 || (havePrev && Compare(prev,k)>=0))throw new InvalidDataException("Invalid or duplicate POI relocation.");
                    result.Add(k,v);prev=k;havePrev=true;
                }
            }}catch(EndOfStreamException e){throw new InvalidDataException("Truncated POI relocation ledger.",e);}
            return result;
        }
        static int FindMarker(string version,out int end)
        {
            end=-1;int start=version.IndexOf(Prefix,StringComparison.Ordinal);
            if(start<0){if(version.IndexOf("[Roadworthy relocation",StringComparison.Ordinal)>=0)throw new InvalidDataException("Damaged POI relocation marker.");return -1;}
            if(version.IndexOf("[Roadworthy relocation",StringComparison.Ordinal)!=start+1)throw new InvalidDataException("Damaged or duplicate POI relocation marker.");
            end=version.IndexOf(']',start+Prefix.Length);
            if(end<0 || version.IndexOf("[Roadworthy relocation",start+Prefix.Length,StringComparison.Ordinal)>=0)throw new InvalidDataException("Damaged or duplicate POI relocation marker.");
            return start;
        }
        public static void Restore(string version)
        {
            version=version??"";int end;int start=FindMarker(version,out end);
            records=start<0?new Dictionary<Key,Record>():Decode(version.Substring(start+Prefix.Length,end-start-Prefix.Length));
        }
        public static string Stamp(string version)
        {
            version=version??"";int end;int start=FindMarker(version,out end);
            if(start>=0){Decode(version.Substring(start+Prefix.Length,end-start-Prefix.Length));version=version.Remove(start,end-start+1);}
            if(records.Count==0)return version;
            int biome=version.IndexOf(" [Roadworthy biome: ",StringComparison.Ordinal);
            string marker=Prefix+Encode()+"]";return biome<0?version+marker:version.Insert(biome,marker);
        }
    }
}
