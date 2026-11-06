namespace TldMp.Experimental
{
    internal enum SequenceResult { Accepted, DuplicateOrOld, Gap, Invalid }

    // One gate per admitted sender and per channel. A new connection gets new
    // gates; never share reliable-event and disposable-motion counters.
    internal sealed class SequenceGate
    {
        readonly bool contiguous;
        ulong last;
        internal SequenceGate(bool requireContiguous) { contiguous = requireContiguous; }
        internal SequenceResult Preview(ulong sequence)
        {
            // Reserve the maximum value: the session must rekey before wrap.
            if (sequence == 0 || sequence == ulong.MaxValue) return SequenceResult.Invalid;
            if (sequence <= last) return SequenceResult.DuplicateOrOld;
            if (contiguous && sequence - last != 1) return SequenceResult.Gap;
            return SequenceResult.Accepted;
        }
        internal SequenceResult Accept(ulong sequence)
        {
            SequenceResult result = Preview(sequence);
            if (result == SequenceResult.Accepted) last = sequence;
            return result;
        }
    }
}
