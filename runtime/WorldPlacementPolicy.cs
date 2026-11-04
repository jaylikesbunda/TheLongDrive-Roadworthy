using System;
using System.IO;

namespace TldMp
{
    // Zero means the original placement rules, including for unmarked saves.
    public static class WorldPlacementPolicy
    {
        public const int CurrentVersion=5;
        public const double RoadClearance=0.15;
        const string Marker=" [Roadworthy placement: ";
        static volatile int version;
        [ThreadStatic] static int workerVersionPlusOne;
        public static int Version {get{return version;}}
        public static int EffectiveVersion {get{return workerVersionPlusOne==0?version:workerVersionPlusOne-1;}}
        public static void SetVersion(int value)
        {if(value<0 || value>CurrentVersion)throw new InvalidDataException("Unsupported world placement version.");version=value;}
        public static void BeginWorker(int captured)
        {if(captured<0 || captured>CurrentVersion || workerVersionPlusOne!=0)throw new InvalidOperationException("Invalid placement worker scope.");workerVersionPlusOne=captured+1;}
        public static void EndWorker(){workerVersionPlusOne=0;}
        public static int ReadVersion(string value)
        {
            if(value==null)return 0;
            int start=value.IndexOf(Marker,StringComparison.Ordinal);if(start<0){if(value.IndexOf("[Roadworthy placement",StringComparison.Ordinal)>=0)throw new InvalidDataException("Damaged placement save marker.");return 0;}
            if(value.IndexOf(Marker,start+Marker.Length,StringComparison.Ordinal)>=0)throw new InvalidDataException("Duplicate placement save marker.");
            int end=value.IndexOf(']',start+Marker.Length);
            if(end<0)throw new InvalidDataException("Unsupported or damaged world placement save marker.");
            string profile=value.Substring(start+Marker.Length,end-start-Marker.Length);
            if(profile=="1")return 1;
            if(profile=="2")return 2;
            if(profile=="3")return 3;
            if(profile=="4")return 4;
            if(profile=="5")return 5;
            throw new InvalidDataException("Unsupported or damaged world placement save marker.");
        }
        public static string StampVersion(string value,int selected)
        {
            if(selected<0 || selected>CurrentVersion)throw new ArgumentOutOfRangeException("selected");
            value=value??"";
            int old=value.IndexOf(Marker,StringComparison.Ordinal);
            if(old>=0){ReadVersion(value);int end=value.IndexOf(']',old);value=value.Remove(old,end-old+1);}
            if(selected==0)return value;
            int biome=value.IndexOf(" [Roadworthy biome: ",StringComparison.Ordinal);
            string stamp=Marker+selected+"]";
            return biome<0?value+stamp:value.Insert(biome,stamp);
        }
        public static int PackConfig(int calendar,int biome,int selected)
        {if(selected<0 || selected>CurrentVersion)throw new ArgumentOutOfRangeException("selected");return WorldBiomePolicy.PackConfig(calendar,biome)|(selected<<6);}
        public static bool TryUnpackConfig(int packed,out int calendar,out int biome,out int selected)
        {selected=(packed>>6)&7;return WorldBiomePolicy.TryUnpackConfig(packed&63,out calendar,out biome) && (packed&~511)==0 && selected<=CurrentVersion;}
    }
}


