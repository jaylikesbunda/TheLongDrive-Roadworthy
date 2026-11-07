using System;

namespace TldMp
{
    // Unity-free rules for the multiplayer menu reskin (MultiplayerMenuSkin).
    public static class MultiplayerMenuSkinPolicy
    {
        // Alpha of the black tint over list and scroll backgrounds.
        public const float PanelTintAlpha = 0.12f;
        // Lobby backings (the list and the right column) under the paper: nearly clear.
        public const float LobbyPanelAlpha = 0.06f;
        // Placeholder text keeps half the alpha of the panel text.
        public const float PlaceholderScale = 0.5f;
        // Lowest size a native-font label may shrink to when its longer glyphs need room.
        public const float AutoSizeMinScale = 0.5f;
        // A colour with channels this far apart is an intentional colour (the red join
        // error, coloured status text) and keeps its own hue on the paper.
        const float SaturationLimit = 0.3f;

        // Debug readouts of the multiplayer window; they are hidden, not restyled.
        public static bool ShouldHideDebug(string name)
        {
            switch (name)
            {
                case "Time":
                case "TextDegu":
                case "TextDataIn":
                case "TextDataOut":
                case "ButtonDebugSend":
                case "ButtonDebugRec":
                    return true;
                default:
                    return false;
            }
        }

        // Luma above a quarter: the text would not read as dark ink on the paper.
        public static bool IsLight(float r, float g, float b)
        {
            return 0.299f * r + 0.587f * g + 0.114f * b > 0.25f;
        }

        public static bool KeepsOwnColor(float r, float g, float b)
        {
            float hi = Math.Max(r, Math.Max(g, b));
            float lo = Math.Min(r, Math.Min(g, b));
            return hi - lo > SaturationLimit;
        }

        // A translucent, near-black backing: the native menus have none, so it is lightened.
        public static bool IsDarkPanel(float r, float g, float b, float a)
        {
            return a > 0.2f && Math.Max(r, Math.Max(g, b)) < 0.3f;
        }

        public static float PlaceholderAlpha(float panelAlpha)
        {
            return panelAlpha * PlaceholderScale;
        }

        public static float AutoSizeMin(float fontSize)
        {
            return Math.Max(4f, fontSize * AutoSizeMinScale);
        }
    }
}
