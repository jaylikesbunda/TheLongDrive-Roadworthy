namespace TldMp.Experimental
{
    internal enum MultiplayerMode { Native = 0, Experimental = 1 }

    // A mode is selected before joining. This object alone cannot enable a mode:
    // the game integration must also provide a working backend and admission gate.
    internal sealed class ModeLatch
    {
        internal MultiplayerMode Selected { get; private set; }
        internal MultiplayerMode Active { get; private set; }
        internal bool InSession { get; private set; }
        internal ulong Attempt { get; private set; }

        internal bool Select(MultiplayerMode mode)
        {
            if (InSession || (mode != MultiplayerMode.Native && mode != MultiplayerMode.Experimental)) return false;
            Selected = mode;
            return true;
        }
        internal bool Begin(bool experimentalBackendReady)
        {
            if (InSession || Attempt == ulong.MaxValue || (Selected == MultiplayerMode.Experimental && !experimentalBackendReady)) return false;
            Attempt++;
            Active = Selected;
            InSession = true;
            return true;
        }
        // Ignore a delayed callback from an older create/join attempt.
        internal bool End(ulong attempt)
        {
            if (!InSession || attempt == 0 || attempt != Attempt) return false;
            InSession = false;
            Active = MultiplayerMode.Native;
            return true;
        }
    }
}
