namespace TldMp
{
    // Version text shown in the game's top-right label. The game's own
    // Application.version is untouched: saves and the multiplayer version check
    // still use it.
    internal static class PatchInfo
    {
        internal const string Version = "0.1.0";
        internal const string Name = "Roadworthy";

        internal static string Display
        {
            get { return "v" + Version + " " + Name + " "; }
        }
    }
}
