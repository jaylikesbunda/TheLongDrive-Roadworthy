using System;
using System.Collections.Generic;

namespace TldMp
{
    // Preserves syncScript.SyncStuff's batch boundary and dictionary-order semantics
    // while avoiding a new dictionary walk over already-processed entries each batch.
    internal sealed class SyncItemScheduler<TKey,TValue> where TValue:class
    {
        Dictionary<TKey,TValue>.Enumerator enumerator;
        Dictionary<TKey,TValue> source;
        bool hasEnumerator;
        uint progress;

        internal uint Progress { get { return progress; } }

        internal void BeginPass()
        {
            progress=0;
            hasEnumerator=false;
            source=null;
        }

        internal bool HasMore(int count)
        {
            return progress<(uint)count;
        }

        internal bool ProcessBatch(Dictionary<TKey,TValue> items,float syncItemCount,Action<TValue> update)
        {
            return ProcessBatch(items,delegate{return syncItemCount;},update);
        }

        internal bool ProcessBatch(Dictionary<TKey,TValue> items,Func<float> syncItemCount,Action<TValue> update)
        {
            if(!HasMore(items.Count))return false;
            uint processed=0;
            if((!hasEnumerator || !Object.ReferenceEquals(source,items)) && !StartAtProgress(items))return true;
            while(true)
            {
                bool moved;
                try { moved=enumerator.MoveNext(); }
                catch(InvalidOperationException)
                {
                    // The stock coroutine creates a fresh foreach at every batch.
                    // A stale cached enumerator is therefore recoverable only before
                    // this batch has processed an item; mid-batch mutation still
                    // throws exactly as the stock foreach does.
                    if(processed!=0)throw;
                    if(!StartAtProgress(items))return true;
                    continue;
                }
                if(!moved){hasEnumerator=false;return true;}
                TValue value=enumerator.Current.Value;
                // The caller owns null semantics. In Unity, `value != null` for a
                // UnityEngine.Object also rejects destroyed native objects, which
                // cannot be expressed by this generic cursor.
                update(value);
                processed++;
                progress++;
                if((float)processed>=syncItemCount())return true;
            }
        }

        bool StartAtProgress(Dictionary<TKey,TValue> items)
        {
            enumerator=items.GetEnumerator();
            source=items;
            hasEnumerator=true;
            for(uint skipped=0;skipped<progress;skipped++)
            {
                if(!enumerator.MoveNext())
                {
                    hasEnumerator=false;
                    return false;
                }
            }
            return true;
        }
    }
}
