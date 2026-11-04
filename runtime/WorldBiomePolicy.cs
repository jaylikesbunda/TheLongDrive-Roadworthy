using System;
using System.Threading;
using UnityEngine;

namespace TldMp
{
    // Session mode is immutable during a world. Workers carry their captured mode
    // so a retired job cannot observe the next world's selection.
    public static class WorldBiomePolicy
    {
        static volatile int mode;
        [ThreadStatic] static int workerModePlusOne;
        public static int Mode { get { return mode; } }
        public static void SetMode(int value)
        {
            if(value<0 || value>3)throw new ArgumentOutOfRangeException("value");
            mode=value;
        }
        public static void BeginWorker(int captured)
        {
            if(captured<0 || captured>3 || workerModePlusOne!=0)throw new InvalidOperationException("Invalid or nested biome worker scope");
            workerModePlusOne=captured+1;
        }
        public static void EndWorker(){workerModePlusOne=0;}
        public static bool HasWorkerMode { get { return workerModePlusOne!=0; } }
        public static Color Apply(Color original)
        {return ApplyMode(original,workerModePlusOne==0?Mode:workerModePlusOne-1);}
        public static Color ApplyMode(Color original,int selected)
        {
            switch(selected)
            {
                case 1:return new Color(1,0,0,1); // Desert.
                case 2:return new Color(0,0,1,1); // Winter.
                case 3:return new Color(0,1,0,1); // Grass; native Grass() requires exact channels.
                default:return original;            // Mixed: exact native color.
            }
        }
        const string VersionMarker = " [Roadworthy biome: ";
        public static string StampVersion(string version,int selected)
        {
            if(selected<0 || selected>3)throw new ArgumentOutOfRangeException("selected");
            version=version??"";
            int old=version.LastIndexOf(VersionMarker,StringComparison.Ordinal);
            if(old>=0)version=version.Substring(0,old);
            if(selected==0)return version;
            return version+VersionMarker+(selected==1?"Desert":selected==2?"Winter":"Grass")+"]";
        }
        public static int ModeFromVersion(string version)
        {
            if(string.IsNullOrEmpty(version))return 0;
            int index=version.LastIndexOf(VersionMarker,StringComparison.Ordinal);
            if(index<0)return 0;
            string suffix=version.Substring(index+VersionMarker.Length);
            if(suffix=="Desert]")return 1;
            if(suffix=="Winter]")return 2;
            if(suffix=="Grass]")return 3;
            throw new System.IO.InvalidDataException("Unsupported or damaged Roadworthy biome save setting.");
        }
        public static int PackConfig(int calendar,int selected)
        {
            if((calendar & ~15)!=0 || selected<0 || selected>3)throw new ArgumentOutOfRangeException();
            return calendar | (selected<<4);
        }
        public static bool TryUnpackConfig(int config,out int calendar,out int selected)
        {
            calendar=config&15;selected=(config>>4)&3;
            return (config & ~63)==0;
        }
    }
}
