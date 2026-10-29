using System;
using System.Collections.Generic;

namespace TldMp
{
    // Design tokens for the menu relayout. All values are canvas units on the
    // game's 1920x1080 reference canvas (CanvasScaler Expand), on an 8-unit grid.
    public static class MenuTokens
    {
        public const float Unit = 8f;
        public const float Margin = 32f;        // safe-frame inset from the screen edge
        public const float Gap = 16f;           // between siblings in a stack or row
        public const float Group = 48f;         // between separate groups/columns
        public const float Pad = 24f;           // between the top row and the content, inside panels
        public const float ButtonH = 88f;       // every text button
        public const float ColumnW = 400f;      // the left navigation column
        public const float SecondaryW = 336f;   // save/load actions
        public const float IconButton = 72f;    // square icon/arrow buttons
        public const float PromptH = 56f;       // confirmation prompt text above a yes/no pair
        public const float TopRowH = 72f;       // clock, language, distance, version, news
        public const float PanelW = 1000f;      // every settings panel
        public const float InfoW = 506f;        // save info column
        public const float InfoH = 305f;
        public const float CellW = 400f, CellH = 225f, CellGap = 24f;   // save slot grid
        public const float DialogW = 500f, DialogH = 225f;              // multiplayer dialogs
        public const float MpMinW = 880f, MpMinH = 560f;                // smallest multiplayer window
        public const float DesignW = 1920f;
        public const float DesignH = 1080f;
        public const float MaxFrameW = 2560f;   // content never spreads wider than 21:9 at 1080
    }

    public struct LRect
    {
        public float X0, Y0, X1, Y1;
        public LRect(float x0, float y0, float x1, float y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        public static LRect FromTopLeft(float left, float top, float w, float h) { return new LRect(left, top - h, left + w, top); }
        public float W { get { return X1 - X0; } }
        public float H { get { return Y1 - Y0; } }
        public float CX { get { return (X0 + X1) * 0.5f; } }
        public float CY { get { return (Y0 + Y1) * 0.5f; } }
        public LRect Offset(float dx, float dy) { return new LRect(X0 + dx, Y0 + dy, X1 + dx, Y1 + dy); }
        public LRect Union(LRect o) { return new LRect(Math.Min(X0, o.X0), Math.Min(Y0, o.Y0), Math.Max(X1, o.X1), Math.Max(Y1, o.Y1)); }
        public bool Overlaps(LRect o) { const float e = 0.5f; return X0 < o.X1 - e && o.X0 < X1 - e && Y0 < o.Y1 - e && o.Y0 < Y1 - e; }
        public bool Inside(LRect o) { const float e = 0.5f; return X0 >= o.X0 - e && Y0 >= o.Y0 - e && X1 <= o.X1 + e && Y1 <= o.Y1 + e; }
        public override string ToString() { return "[" + X0 + "," + Y0 + " .. " + X1 + "," + Y1 + "]"; }
    }

    // Measured footprint sizes of the elements that are visible in the current state.
    // An id that is absent is not visible.
    public sealed class MenuSizes
    {
        readonly Dictionary<string, float[]> d = new Dictionary<string, float[]>();
        public void Add(string id, float w, float h) { d[id] = new float[] { w, h }; }
        public void Clear() { d.Clear(); }
        public bool On(string id) { return d.ContainsKey(id); }
        public float W(string id) { float[] v; return d.TryGetValue(id, out v) ? v[0] : 0f; }
        public float H(string id) { float[] v; return d.TryGetValue(id, out v) ? v[1] : 0f; }
    }

    // Placement result: footprint rect per element, plus optional uniform scale
    // (scale-to-fit windows) and the order elements must be applied in.
    public sealed class MenuPlan
    {
        public readonly List<string> Order = new List<string>();
        public readonly Dictionary<string, LRect> Rects = new Dictionary<string, LRect>();
        public readonly Dictionary<string, float> Scale = new Dictionary<string, float>();
        public void Set(string id, LRect r) { if (!Rects.ContainsKey(id)) Order.Add(id); Rects[id] = r; }
        public bool Has(string id) { return Rects.ContainsKey(id); }
        public LRect this[string id] { get { return Rects[id]; } }
        public float ScaleOf(string id) { float s; return Scale.TryGetValue(id, out s) ? s : 1f; }
    }

    // Pure layout maths: no Unity types, so every rule is covered by offline tests.
    // Coordinates are canvas units with the origin at the canvas centre, y up.
    //
    // Every menu shares one structure: a top row (T), a 400-wide left column (L)
    // and a content area that starts at one common line (ContentTop) to the right
    // of the column. Back/Discord sit at the bottom of the column.
    public static class MenuLayoutPolicy
    {
        // Canvas size produced by CanvasScaler ScaleWithScreenSize/Expand at 1920x1080.
        public static void CanvasSize(float screenW, float screenH, out float w, out float h)
        {
            if (screenW <= 0f || screenH <= 0f) { w = MenuTokens.DesignW; h = MenuTokens.DesignH; return; }
            float scale = Math.Min(screenW / MenuTokens.DesignW, screenH / MenuTokens.DesignH);
            w = screenW / scale; h = screenH / scale;
        }

        // Safe frame: the area all menu content lives in. Wider than 21:9 it stays
        // centred at MaxFrameW so content does not drift to far screen corners.
        public static LRect Frame(float canvasW, float canvasH)
        {
            float w = Math.Min(Math.Max(canvasW, MenuTokens.DesignW), MenuTokens.MaxFrameW);
            float h = Math.Max(canvasH, MenuTokens.DesignH);
            return new LRect(-w * 0.5f + MenuTokens.Margin, -h * 0.5f + MenuTokens.Margin, w * 0.5f - MenuTokens.Margin, h * 0.5f - MenuTokens.Margin);
        }

        // Full-canvas cover rect for backgrounds (a little bleed hides seams).
        public static LRect Cover(float canvasW, float canvasH)
        {
            return new LRect(-canvasW * 0.5f - 2f, -canvasH * 0.5f - 2f, canvasW * 0.5f + 2f, canvasH * 0.5f + 2f);
        }

        public static float ContentTop(LRect f) { return f.Y1 - MenuTokens.TopRowH - MenuTokens.Pad; }
        public static float ColumnRight(LRect f) { return f.X0 + MenuTokens.ColumnW; }
        public static LRect Content(LRect f) { return new LRect(ColumnRight(f) + MenuTokens.Group, f.Y0, f.X1, ContentTop(f)); }

        // Element sizes the layout imposes (standard buttons, fixed panels). Elements
        // not listed keep their own size. Panel heights depend on the frame.
        public static bool TokenSize(string id, LRect f, out float w, out float h)
        {
            h = MenuTokens.ButtonH; w = MenuTokens.ColumnW;
            switch (id)
            {
                case "navContinue": case "navMultiplayer": case "navLoad": case "navSettings": case "navExit":
                case "pauseContinue": case "pauseSave": case "exitNewStart": case "exitRestart": case "exitYes":
                case "exitMainMenu": case "exitNo": case "discord": case "setBack": case "setReset": case "setResetKeys":
                case "saveBack": case "loadLast": case "wheelBack":
                    return true;
                case "navStart": w = MenuTokens.ColumnW - MenuTokens.ButtonH - MenuTokens.Gap; return true;
                case "navGear": w = MenuTokens.ButtonH; return true;
                case "setTab": w = MenuTokens.ColumnW - 75f; return true;   // the tab's LED icon fills the other 75
                case "exitPrompt": h = MenuTokens.PromptH; return true;
                case "pauseSeedInput": h = MenuTokens.IconButton; return true;
                case "news": w = h = MenuTokens.IconButton; return true;
                case "loadLast.yes": case "loadLast.no": w = (MenuTokens.ColumnW - MenuTokens.Gap) * 0.5f; return true;
                case "slot": case "delete": case "setActive": w = MenuTokens.SecondaryW; return true;
                case "slot.yes": case "slot.no": case "delete.yes": case "delete.no":
                    w = (MenuTokens.SecondaryW - MenuTokens.Gap) * 0.5f; return true;
                case "slot.prompt": case "delete.prompt": case "loadLast.prompt": w = MenuTokens.SecondaryW; h = MenuTokens.PromptH; return true;
                case "info": w = MenuTokens.InfoW; h = MenuTokens.InfoH; return true;
                case "saveName": w = MenuTokens.InfoW; h = 80f; return true;
                case "arrow": w = h = MenuTokens.IconButton; return true;
                case "setPanel": w = MenuTokens.PanelW; h = ContentTop(f) - f.Y0; return true;
                case "mpDialog": w = MenuTokens.DialogW; h = MenuTokens.DialogH; return true;
            }
            return false;
        }

        // ---- shared pieces ----

        // Top row: clock, language, distance (+its reset button inline) and, on the
        // save screen, the paging toolbar, left to right; news and version at the right.
        public static readonly string[] TopLeft = { "clock", "language", "distance", "distanceReset", "pageText", "arrowUp", "arrowFastUp", "arrowJumpUp", "arrowDown", "arrowFastDown", "arrowJumpDown", "countText" };
        public static void TopRow(MenuPlan p, LRect f, MenuSizes s) { TopRow(p, f, s, true); }
        public static void TopRow(MenuPlan p, LRect f, MenuSizes s, bool versionInRow)
        {
            float x = f.X0; string prev = null;
            foreach (string id in TopLeft)
            {
                if (!s.On(id)) continue;
                if (prev != null) x += GapBefore(id, prev);
                p.Set(id, RowItem(f, x, s.W(id), s.H(id)));
                x += s.W(id); prev = id;
            }
            float r = f.X1;
            if (s.On("news")) { p.Set("news", RowItem(f, r - s.W("news"), s.W("news"), s.H("news"))); r -= s.W("news") + MenuTokens.Gap; }
            if (s.On("version") && versionInRow) p.Set("version", RowItem(f, r - s.W("version"), s.W("version"), s.H("version")));
        }
        static float GapBefore(string id, string prev)
        {
            if (id == "distance" || id == "pageText") return MenuTokens.Group;
            if (id.StartsWith("arrow") && !prev.StartsWith("arrow")) return MenuTokens.Group;
            if (id == "arrowDown") return MenuTokens.Group;
            if (id == "countText") return MenuTokens.Group;
            return MenuTokens.Gap;
        }
        static LRect RowItem(LRect f, float x, float w, float h)
        {
            float top = f.Y1 - Math.Max(0f, (MenuTokens.TopRowH - h) * 0.5f);
            return LRect.FromTopLeft(x, top, w, h);
        }

        // Left column stack from `top`, visible ids only, Gap apart.
        public static float Column(MenuPlan p, LRect f, MenuSizes s, string[] ids, float top)
        {
            float y = top;
            foreach (string id in ids)
            {
                if (!s.On(id)) continue;
                p.Set(id, LRect.FromTopLeft(f.X0, y, s.W(id), s.H(id)));
                y -= s.H(id) + MenuTokens.Gap;
            }
            return y + MenuTokens.Gap;   // bottom of the last item
        }

        // Fits a w x h window into the area's top-left corner, shrinking (never growing) to fit.
        public static void Window(MenuPlan p, string id, LRect area, float w, float h)
        {
            float sc = FitScale(w, h, area.W, area.H);
            p.Scale[id] = sc;
            p.Set(id, LRect.FromTopLeft(area.X0, area.Y1, w * sc, h * sc));
        }
        public static float FitScale(float w, float h, float availW, float availH)
        {
            if (w <= 0f || h <= 0f) return 1f;
            return Math.Max(0.1f, Math.Min(1f, Math.Min(availW / w, availH / h)));
        }

        // ---- menus ----

        public static readonly string[] MainNav = { "navContinue", "navMultiplayer", "navStart", "navLoad", "navSettings", "navExit" };
        public static readonly string[] PauseNav = { "navMultiplayer", "pauseContinue", "pauseSave", "navSettings", "navExit" };
        public static readonly string[] ExitNav = { "exitNewStart", "exitRestart", "exitPrompt", "exitYes", "exitMainMenu", "exitNo" };

        // Main menu and pause menu share the top row, the column and the content area.
        public static MenuPlan MainMenu(LRect f, MenuSizes s, bool pause, bool sureExit)
        {
            var p = new MenuPlan();
            // The main menu's top row is full (clock, language, distance), so the version
            // moves to the bottom-right corner here.
            TopRow(p, f, s, false);
            if (s.On("version")) p.Set("version", new LRect(f.X1 - s.W("version"), f.Y0, f.X1, f.Y0 + s.H("version")));
            float top = ContentTop(f);
            if (pause)
            {
                top = Column(p, f, s, new[] { "pauseSeedMap", "pauseSeedName" }, top);
                if (s.On("pauseSeedInput")) { p.Set("pauseSeedInput", LRect.FromTopLeft(f.X0, top - MenuTokens.Gap * 0.5f, s.W("pauseSeedInput"), s.H("pauseSeedInput"))); top = p["pauseSeedInput"].Y0; }
                top -= MenuTokens.Group;
            }
            string[] nav = sureExit ? ExitNav : pause ? PauseNav : MainNav;
            Column(p, f, s, nav, top);
            if (p.Has("navStart") && s.On("navGear"))
            {
                LRect st = p["navStart"];
                p.Set("navGear", LRect.FromTopLeft(st.X1 + MenuTokens.Gap, st.Y1, s.W("navGear"), s.H("navGear")));
            }
            if (s.On("discord")) p.Set("discord", LRect.FromTopLeft(f.X0, f.Y0 + s.H("discord"), s.W("discord"), s.H("discord")));
            LRect content = Content(f);
            if (s.On("mode")) Window(p, "mode", content, s.W("mode"), s.H("mode"));
            if (s.On("newsObj")) Window(p, "newsObj", content, s.W("newsObj"), s.H("newsObj"));
            if (s.On("discordText"))
            {
                float copyW = s.On("discordCopy") ? s.W("discordCopy") + MenuTokens.Gap : 0f;
                float right = p.Has("version") ? p["version"].X0 - MenuTokens.Group : content.X1;
                float w = Math.Min(s.W("discordText"), right - content.X0 - copyW);
                p.Set("discordText", LRect.FromTopLeft(content.X0, f.Y0 + s.H("discordText"), w, s.H("discordText")));
                if (s.On("discordCopy")) p.Set("discordCopy", LRect.FromTopLeft(content.X0 + w + MenuTokens.Gap, f.Y0 + s.H("discordCopy"), s.W("discordCopy"), s.H("discordCopy")));
            }
            return p;
        }

        // Settings: tab column, Reset/Back at the column bottom, one panel size for
        // every tab, and the Player/Steering preview to the right of the panel.
        public static readonly string[] Tabs = { "tab0", "tab1", "tab2", "tab3", "tab4" };
        public static MenuPlan Settings(LRect f, MenuSizes s)
        {
            var p = new MenuPlan();
            TopRow(p, f, s);
            float top = ContentTop(f);
            Column(p, f, s, Tabs, top);
            float y = f.Y0;
            foreach (string id in new[] { "setBack", "setReset", "setResetKeys" })
            {
                if (!s.On(id)) continue;
                p.Set(id, new LRect(f.X0, y, f.X0 + s.W(id), y + s.H(id)));
                y += s.H(id) + MenuTokens.Gap;
            }
            LRect content = Content(f);
            if (s.On("setPanel"))
            {
                p.Set("setPanel", LRect.FromTopLeft(content.X0, top, s.W("setPanel"), s.H("setPanel")));
                if (s.On("setPreview"))
                {
                    float x0 = p["setPanel"].X1 + MenuTokens.Group;
                    Window(p, "setPreview", new LRect(x0, content.Y0, content.X1, top), s.W("setPreview"), s.H("setPreview"));
                }
            }
            return p;
        }

        // Wheel setup: axis blocks in two columns (it needs the full height), the
        // shifter help at the right, Back at the bottom of the column.
        public static readonly string[] WheelCol1 = { "wheelWheel", "wheelGas", "wheelAxes" };
        public static readonly string[] WheelCol2 = { "wheelBrake", "wheelClutch", "wheelHandBrake" };
        public static MenuPlan Wheel(LRect f, MenuSizes s)
        {
            var p = new MenuPlan();
            float w1 = 0f, w2 = 0f, wS = Math.Max(s.W("shiftHelp"), s.W("shiftToggle")), h1 = 0f, h2 = 0f;
            const float rowGap = MenuTokens.Pad;
            foreach (string id in WheelCol1) if (s.On(id)) { w1 = Math.Max(w1, s.W(id)); h1 += s.H(id) + rowGap; }
            foreach (string id in WheelCol2) if (s.On(id)) { w2 = Math.Max(w2, s.W(id)); h2 += s.H(id) + rowGap; }
            // The blocks plus the shifter help are a little wider than the frame at 16:9:
            // shrink the whole group uniformly rather than let columns collide.
            float back = s.On("wheelBack") ? s.H("wheelBack") + MenuTokens.Group : 0f;
            float needW = w1 + MenuTokens.Group + w2 + MenuTokens.Group + wS;
            float sc = Math.Min(1f, Math.Min(f.W / needW, Math.Min(f.H / Math.Max(1f, h2 - rowGap), (f.H - back) / Math.Max(1f, h1 - rowGap))));
            float x = f.X0, y = f.Y1;
            foreach (string id in WheelCol1) if (s.On(id)) { p.Scale[id] = sc; p.Set(id, LRect.FromTopLeft(x, y, s.W(id) * sc, s.H(id) * sc)); y -= (s.H(id) + rowGap) * sc; }
            x += (w1 + MenuTokens.Group) * sc; y = f.Y1;
            foreach (string id in WheelCol2) if (s.On(id)) { p.Scale[id] = sc; p.Set(id, LRect.FromTopLeft(x, y, s.W(id) * sc, s.H(id) * sc)); y -= (s.H(id) + rowGap) * sc; }
            float sx = Math.Max(x + (w2 + MenuTokens.Group) * sc, f.X1 - wS * sc); y = f.Y1;
            foreach (string id in new[] { "shiftHelp", "shiftToggle" })
                if (s.On(id)) { p.Scale[id] = sc; p.Set(id, LRect.FromTopLeft(sx, y, s.W(id) * sc, s.H(id) * sc)); y -= (s.H(id) + MenuTokens.Gap) * sc; }
            if (s.On("wheelBack")) p.Set("wheelBack", LRect.FromTopLeft(f.X0, f.Y0 + s.H("wheelBack"), s.W("wheelBack"), s.H("wheelBack")));
            return p;
        }

        // Save/Load: paging toolbar in the top row, slot grid from the content top,
        // info + actions in a fixed right column, Back/Load Last/Set Active along the bottom.
        public static readonly string[] Grid = { "fileAuto0", "file01", "file02", "fileAuto1", "file03", "file04", "fileAuto2", "file05", "file06" };
        public static MenuPlan SaveLoad(LRect f, MenuSizes s)
        {
            var p = new MenuPlan();
            TopRow(p, f, s);
            float top = ContentTop(f);
            float cw = MenuTokens.CellW, ch = MenuTokens.CellH, g = MenuTokens.CellGap;
            // Save slots keep their grid cell; visible autosaves stack from the top of column 0.
            int auto = 0;
            for (int i = 0; i < Grid.Length; i++)
            {
                if (!s.On(Grid[i])) continue;
                int col = i % 3, row = col == 0 ? auto++ : i / 3;
                p.Set(Grid[i], LRect.FromTopLeft(f.X0 + col * (cw + g), top - row * (ch + g), cw, ch));
            }
            // Right column.
            float rx = f.X1 - MenuTokens.InfoW, y = top;
            if (s.On("info")) { p.Set("info", LRect.FromTopLeft(rx, y, s.W("info"), s.H("info"))); y -= s.H("info") + MenuTokens.Gap; }
            if (s.On("saveName")) { p.Set("saveName", LRect.FromTopLeft(rx, y, s.W("saveName"), s.H("saveName"))); y -= s.H("saveName") + MenuTokens.Gap; }
            foreach (string slot in new[] { "slotSave", "slotLoad" })
            {
                if (!s.On(slot) && !s.On(slot + ".prompt")) continue;
                Slot(p, s, slot, f.X1, y);
                y -= MenuTokens.PromptH + MenuTokens.ButtonH + MenuTokens.Gap;
            }
            if (s.On("delete") || s.On("delete.prompt")) Slot(p, s, "delete", f.X1, f.Y0 + MenuTokens.PromptH + MenuTokens.ButtonH);
            // Bottom bar.
            float x = f.X0;
            if (s.On("saveBack")) { p.Set("saveBack", new LRect(x, f.Y0, x + s.W("saveBack"), f.Y0 + s.H("saveBack"))); x += s.W("saveBack") + MenuTokens.Gap; }
            float lastW = Math.Max(s.W("loadLast"), s.W("loadLast.no") + MenuTokens.Gap + s.W("loadLast.yes"));
            if (s.On("loadLast")) p.Set("loadLast", new LRect(x, f.Y0, x + s.W("loadLast"), f.Y0 + s.H("loadLast")));
            if (s.On("loadLast.prompt"))
            {
                p.Set("loadLast.prompt", new LRect(x, f.Y0 + MenuTokens.ButtonH, x + s.W("loadLast.prompt"), f.Y0 + MenuTokens.ButtonH + s.H("loadLast.prompt")));
                if (s.On("loadLast.no")) p.Set("loadLast.no", new LRect(x, f.Y0, x + s.W("loadLast.no"), f.Y0 + s.H("loadLast.no")));
                if (s.On("loadLast.yes")) p.Set("loadLast.yes", new LRect(x + lastW - s.W("loadLast.yes"), f.Y0, x + lastW, f.Y0 + s.H("loadLast.yes")));
            }
            if (s.On("loadLast") || s.On("loadLast.prompt")) x += lastW + MenuTokens.Gap;
            if (s.On("setActive")) p.Set("setActive", new LRect(x, f.Y0, x + s.W("setActive"), f.Y0 + s.H("setActive")));
            return p;
        }
        // An action slot: prompt row above, button row below, right-aligned at `right`.
        // The button footprint includes its icon, which hangs to the left.
        static void Slot(MenuPlan p, MenuSizes s, string id, float right, float top)
        {
            float by = top - MenuTokens.PromptH;
            if (s.On(id)) p.Set(id, new LRect(right - s.W(id), by - s.H(id), right, by));
            if (s.On(id + ".prompt"))
            {
                p.Set(id + ".prompt", new LRect(right - s.W(id + ".prompt"), top - s.H(id + ".prompt"), right, top));
                float pw = s.W(id + ".prompt");
                if (s.On(id + ".no")) p.Set(id + ".no", new LRect(right - pw, by - s.H(id + ".no"), right - pw + s.W(id + ".no"), by));
                if (s.On(id + ".yes")) p.Set(id + ".yes", new LRect(right - s.W(id + ".yes"), by - s.H(id + ".yes"), right, by));
            }
        }

        // Multiplayer window: in the content area while a menu is open (so the menu
        // column stays visible), at the column position in game. Its size comes from
        // the game's size slider, clamped between a minimum that fits its controls and
        // the space available above its footer.
        public static MenuPlan Multiplayer(LRect f, MenuSizes s, bool menuOpen, out float winW, out float winH)
        {
            var p = new MenuPlan();
            float top = ContentTop(f);
            float x0 = menuOpen ? Content(f).X0 : f.X0;
            float footer = Math.Max(s.H("mpTime") + s.H("mpDegu"), s.H("mpDataIn") + s.H("mpDataOut"));
            float micW = s.On("mpMic") ? s.W("mpMic") + MenuTokens.Gap : 0f;
            // Over a menu the bottom row holds the version text; keep the footer above it.
            float bottom = menuOpen ? f.Y0 + MenuTokens.TopRowH : f.Y0;
            float maxW = f.X1 - x0 - micW, maxH = top - bottom - footer - MenuTokens.Gap;
            winW = Clamp(s.W("mpWindow"), Math.Min(MenuTokens.MpMinW, maxW), maxW);
            winH = Clamp(s.H("mpWindow"), Math.Min(MenuTokens.MpMinH, maxH), maxH);
            LRect win = LRect.FromTopLeft(x0, top, winW, winH);
            if (s.On("mpWindow")) p.Set("mpWindow", win);
            float y = win.Y0 - MenuTokens.Gap * 0.5f;
            if (s.On("mpTime")) { p.Set("mpTime", LRect.FromTopLeft(win.X0, y, s.W("mpTime"), s.H("mpTime"))); }
            if (s.On("mpDegu")) p.Set("mpDegu", LRect.FromTopLeft(win.X0, y - s.H("mpTime"), s.W("mpDegu"), s.H("mpDegu")));
            if (s.On("mpDataIn")) p.Set("mpDataIn", LRect.FromTopLeft(win.X1 - s.W("mpDataIn"), y, s.W("mpDataIn"), s.H("mpDataIn")));
            if (s.On("mpDataOut")) p.Set("mpDataOut", LRect.FromTopLeft(win.X1 - s.W("mpDataOut"), y - s.H("mpDataIn"), s.W("mpDataOut"), s.H("mpDataOut")));
            foreach (string d in new[] { "mpPassword", "mpPlayer", "mpWrong" })
                if (s.On(d)) p.Set(d, new LRect(win.CX - s.W(d) * 0.5f, win.CY - s.H(d) * 0.5f, win.CX + s.W(d) * 0.5f, win.CY + s.H(d) * 0.5f));
            if (s.On("mpMessage")) p.Set("mpMessage", LRect.FromTopLeft(x0, top, s.W("mpMessage"), s.H("mpMessage")));
            if (s.On("mpMic")) p.Set("mpMic", LRect.FromTopLeft(f.X1 - s.W("mpMic"), top, s.W("mpMic"), s.H("mpMic")));
            return p;
        }
        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }

        // First overlapping pair among a plan's rects (ids in `skip` pairs are exempt), or null.
        public static string FirstOverlap(MenuPlan p, params string[] exempt)
        {
            for (int i = 0; i < p.Order.Count; i++)
                for (int j = i + 1; j < p.Order.Count; j++)
                {
                    string a = p.Order[i], b = p.Order[j];
                    if (Exempt(a, b, exempt)) continue;
                    if (p.Rects[a].Overlaps(p.Rects[b])) return a + " overlaps " + b;
                }
            return null;
        }
        static bool Exempt(string a, string b, string[] exempt)
        {
            for (int k = 0; k + 1 < exempt.Length; k += 2)
                if ((exempt[k] == a && exempt[k + 1] == b) || (exempt[k] == b && exempt[k + 1] == a)) return true;
            return false;
        }
    }
}
