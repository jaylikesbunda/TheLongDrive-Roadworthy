using System;
using System.Collections.Generic;
using System.Text;

namespace TldMp
{
    // Unity-free rate limiter for the stock networkingScript.DebugLog flood
    // ("Unknown Message Type...", "Received: itemPosLocal..."). Messages are keyed
    // by their text with digits removed, so numbered variants share one budget.
    // Each key may log `limit` messages per `window` seconds. When a window rolls
    // over with suppressed messages, Offer reports one summary line for them.
    internal sealed class NetLogLimiter
    {
        internal const int DefaultLimit=5;
        internal const double DefaultWindow=10.0;
        // Bounds the table when messages carry unbounded non-digit text.
        internal const int MaxKeys=1024;

        sealed class Bucket
        {
            public double Start;public int Shown;public int Suppressed;
        }

        readonly int limit;readonly double window;
        readonly Dictionary<string,Bucket> buckets=new Dictionary<string,Bucket>();
        internal int TrackedKeys{get{return buckets.Count;}}

        internal NetLogLimiter(int limit,double window)
        {
            if(limit<1 || Double.IsNaN(window) || window<=0)throw new ArgumentOutOfRangeException("limiter");
            this.limit=limit;this.window=window;
        }

        // Digits are removed so "Length: 12" and "Length: 480" share a key.
        internal static string Key(string message)
        {
            if(String.IsNullOrEmpty(message))return "";
            var sb=new StringBuilder(message.Length);
            foreach(char c in message)if(!Char.IsDigit(c))sb.Append(c);
            return sb.ToString();
        }

        // True when the message should be logged now. summary is non-null only when
        // a window rolled over and earlier messages with this key were suppressed;
        // the caller logs it before the message.
        internal bool Offer(string message,double now,out string summary)
        {
            summary=null;
            string key=Key(message);
            Bucket bucket;
            if(!buckets.TryGetValue(key,out bucket))
            {
                if(buckets.Count>=MaxKeys)buckets.Clear();
                bucket=new Bucket{Start=now};buckets[key]=bucket;
            }
            if(now-bucket.Start>=window)
            {
                if(bucket.Suppressed>0)summary="[TLD MP net-log] suppressed "+bucket.Suppressed+" similar messages: "+key;
                bucket.Start=now;bucket.Shown=0;bucket.Suppressed=0;
            }
            if(bucket.Shown<limit){bucket.Shown++;return true;}
            bucket.Suppressed++;
            return false;
        }
    }
}
