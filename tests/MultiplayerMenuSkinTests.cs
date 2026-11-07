using System;
using TldMp;

internal static class MultiplayerMenuSkinTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }

    public static int Main(string[] args)
    {
        Debug();
        Colours();
        Sizes();
        Console.WriteLine("PASS: " + checks + " multiplayer menu skin assertions; only the debug readouts (time, degu and data in/out texts, the debug send and receive buttons) are hidden, saturated text keeps its colour, dark translucent panels are the only backgrounds lightened, placeholders are half as opaque, and auto-size minimums stay readable.");
        return 0;
    }

    static void Debug()
    {
        Check(MultiplayerMenuSkinPolicy.ShouldHideDebug("Time"), "the clock readout is hidden");
        Check(MultiplayerMenuSkinPolicy.ShouldHideDebug("TextDegu"), "the debug text is hidden");
        Check(MultiplayerMenuSkinPolicy.ShouldHideDebug("TextDataIn"), "the incoming data readout is hidden");
        Check(MultiplayerMenuSkinPolicy.ShouldHideDebug("TextDataOut"), "the outgoing data readout is hidden");
        Check(MultiplayerMenuSkinPolicy.ShouldHideDebug("ButtonDebugSend"), "the debug send button is hidden");
        Check(MultiplayerMenuSkinPolicy.ShouldHideDebug("ButtonDebugRec"), "the debug receive button is hidden");
        Check(!MultiplayerMenuSkinPolicy.ShouldHideDebug("Lobby"), "a lobby control is never hidden");
        Check(!MultiplayerMenuSkinPolicy.ShouldHideDebug("ButtonLobby"), "a header button is never hidden");
        Check(!MultiplayerMenuSkinPolicy.ShouldHideDebug(""), "an empty name is never hidden");
    }

    static void Colours()
    {
        Check(MultiplayerMenuSkinPolicy.KeepsOwnColor(1f, 0.1f, 0.1f), "red error text keeps its colour");
        Check(!MultiplayerMenuSkinPolicy.KeepsOwnColor(1f, 1f, 1f), "white text is restyled to the native dark");
        Check(!MultiplayerMenuSkinPolicy.KeepsOwnColor(0.2f, 0.2f, 0.2f), "grey text is restyled");
        Check(MultiplayerMenuSkinPolicy.IsDarkPanel(0f, 0f, 0f, 0.8f), "a translucent black panel is lightened");
        Check(!MultiplayerMenuSkinPolicy.IsDarkPanel(0f, 0f, 0f, 0.1f), "a nearly invisible black image is left alone");
        Check(!MultiplayerMenuSkinPolicy.IsDarkPanel(0.9f, 0.9f, 0.9f, 1f), "a light image is not treated as dark");
        Check(Near(MultiplayerMenuSkinPolicy.PlaceholderAlpha(1f), 0.5f), "placeholder text is half opaque");
        Check(Near(MultiplayerMenuSkinPolicy.PlaceholderAlpha(0f), 0f), "an invisible placeholder stays invisible");
        Check(Near(MultiplayerMenuSkinPolicy.PanelTintAlpha, 0.12f), "the panel tint is 12 percent black");
        Check(Near(MultiplayerMenuSkinPolicy.LobbyPanelAlpha, 0.06f), "lobby backings are almost clear");
        Check(MultiplayerMenuSkinPolicy.IsLight(0.95f, 0.95f, 0.95f), "white text counts as light");
        Check(!MultiplayerMenuSkinPolicy.IsLight(0.2f, 0.2f, 0.2f), "dark grey text is already dark ink");
    }

    static void Sizes()
    {
        Check(Near(MultiplayerMenuSkinPolicy.AutoSizeMin(20f), 10f), "auto-size minimum is half the size");
        Check(Near(MultiplayerMenuSkinPolicy.AutoSizeMin(2f), 4f), "auto-size minimum never drops below four");
    }
}
