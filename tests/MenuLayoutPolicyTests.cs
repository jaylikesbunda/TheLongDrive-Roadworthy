using System;
using System.Collections.Generic;
using TldMp;

internal static class MenuLayoutPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    static readonly float[][] Screens =
    {
        new float[] { 1280, 720 }, new float[] { 1920, 1080 }, new float[] { 1600, 900 }, new float[] { 1024, 768 }, new float[] { 1280, 1024 },
        new float[] { 2560, 1080 }, new float[] { 3440, 1440 }, new float[] { 3840, 1080 }, new float[] { 800, 600 }, new float[] { 1920, 1200 },
    };

    // Footprint sizes as measured in the game (token sizes where the layout imposes them).
    static MenuSizes Sizes(LRect f, params object[] idsAndSizes)
    {
        var s = new MenuSizes();
        for (int i = 0; i < idsAndSizes.Length; i++)
        {
            string id = (string)idsAndSizes[i];
            float w, h;
            if (i + 2 < idsAndSizes.Length && idsAndSizes[i + 1] is float) { w = (float)idsAndSizes[i + 1]; h = (float)idsAndSizes[i + 2]; i += 2; }
            else if (!MenuLayoutPolicy.TokenSize(Kind(id), f, out w, out h)) throw new Exception("no size for " + id);
            s.Add(id, w, h);
        }
        return s;
    }
    static string Kind(string id)
    {
        if (id.StartsWith("tab")) return "setTab";
        if (id.StartsWith("arrow")) return "arrow";
        if (id == "slotSave" || id == "slotLoad") return "slot";
        if (id.StartsWith("slot") && id.Contains(".")) return "slot" + id.Substring(id.IndexOf('.'));
        if (id.StartsWith("mpPassword") || id.StartsWith("mpPlayer") || id.StartsWith("mpWrong")) return "mpDialog";
        return id;
    }
    static object[] Top(bool clock, bool distance)
    {
        var l = new List<object>();
        if (clock) l.AddRange(new object[] { "clock", 200f, 70f });
        l.AddRange(new object[] { "language", 420f, 70f });
        if (distance) l.AddRange(new object[] { "distance", 470f, 63f, "distanceReset", 160f, 40f });
        l.AddRange(new object[] { "version", 340f, 54f, "news" });
        return l.ToArray();
    }
    static object[] Join(params object[][] parts) { var l = new List<object>(); foreach (var p in parts) l.AddRange(p); return l.ToArray(); }

    static void Clean(MenuPlan p, LRect f, string what, params string[] exempt)
    {
        string o = MenuLayoutPolicy.FirstOverlap(p, exempt);
        Check(o == null, what + ": " + o);
        foreach (string id in p.Order) Check(p[id].Inside(f), what + ": " + id + " " + p[id] + " outside frame " + f);
    }
    static void Column(MenuPlan p, LRect f, string what, params string[] ids)
    {
        LRect? prev = null;
        foreach (string id in ids)
        {
            if (!p.Has(id)) continue;
            Check(Math.Abs(p[id].X0 - f.X0) < 0.01f, what + ": " + id + " not on the column edge");
            if (prev.HasValue) Check(Math.Abs(prev.Value.Y0 - p[id].Y1 - MenuTokens.Gap) < 0.01f, what + ": gap before " + id + " is not " + MenuTokens.Gap);
            prev = p[id];
        }
    }
    // Feeding a plan's own rects back as sizes must reproduce it (a second pass is a no-op).
    static void Stable(Func<MenuSizes, MenuPlan> run, MenuSizes s, string what)
    {
        MenuPlan a = run(s);
        var again = new MenuSizes();
        foreach (string id in a.Order) { float sc = a.ScaleOf(id); again.Add(id, a[id].W / sc, a[id].H / sc); }
        MenuPlan b = run(again);
        foreach (string id in a.Order) Check(b.Has(id) && Math.Abs(b[id].X0 - a[id].X0) < 0.01f && Math.Abs(b[id].Y1 - a[id].Y1) < 0.01f, what + ": " + id + " moved on a second pass");
    }

    static void Main()
    {
        float cw, ch;
        MenuLayoutPolicy.CanvasSize(1920, 1080, out cw, out ch); Check(cw == 1920 && ch == 1080, "16:9 canvas");
        MenuLayoutPolicy.CanvasSize(1024, 768, out cw, out ch); Check(Math.Abs(cw - 1920) < 0.01f && Math.Abs(ch - 1440) < 0.01f, "4:3 canvas grows taller");
        MenuLayoutPolicy.CanvasSize(2560, 1080, out cw, out ch); Check(Math.Abs(cw - 2560) < 0.01f && Math.Abs(ch - 1080) < 0.01f, "ultrawide canvas grows wider");
        LRect f0 = MenuLayoutPolicy.Frame(1920, 1080);
        Check(f0.X0 == -928 && f0.X1 == 928 && f0.Y0 == -508 && f0.Y1 == 508, "frame insets by the margin");
        Check(MenuLayoutPolicy.Frame(3840, 1080).W == MenuTokens.MaxFrameW - 2 * MenuTokens.Margin, "frame caps at 21:9");
        LRect cover = MenuLayoutPolicy.Cover(2560, 1080);
        Check(cover.X0 < -1280 && cover.X1 > 1280 && cover.Y0 < -540 && cover.Y1 > 540, "cover bleeds past the canvas");
        Check(MenuLayoutPolicy.FitScale(1160, 772, 1408, 920) == 1f && MenuLayoutPolicy.FitScale(2000, 500, 1000, 1000) == 0.5f, "fit scale shrinks only");

        foreach (float[] scr in Screens)
        {
            MenuLayoutPolicy.CanvasSize(scr[0], scr[1], out cw, out ch);
            LRect f = MenuLayoutPolicy.Frame(cw, ch);
            string at = scr[0] + "x" + scr[1];

            // Main menu: every window, with and without the clock.
            object[] nav = { "navContinue", "navMultiplayer", "navStart", "navGear", "navLoad", "navSettings", "navExit", "discord" };
            foreach (bool clock in new[] { true, false })
                foreach (object[] window in new[] { new object[0], new object[] { "mode", 1160f, 772f }, new object[] { "newsObj", 638f, 680f }, new object[] { "discordText", 1100f, 80f, "discordCopy", 200f, 70f } })
                {
                    MenuSizes s = Sizes(f, Join(Top(clock, true), nav, window));
                    string what = at + " main clock=" + clock + " window=" + (window.Length > 0 ? window[0] : "none");
                    MenuPlan p = MenuLayoutPolicy.MainMenu(f, s, false, false);
                    Clean(p, f, what);
                    Column(p, f, what, MenuLayoutPolicy.MainNav);
                    Check(p["navGear"].X1 <= MenuLayoutPolicy.ColumnRight(f) + 0.01f, what + ": gear outside the column");
                    LRect content = MenuLayoutPolicy.Content(f);
                    foreach (string w in new[] { "mode", "newsObj", "discordText" }) if (p.Has(w)) Check(Math.Abs(p[w].X0 - content.X0) < 0.01f, what + ": " + w + " not on the content edge");
                    Stable(delegate(MenuSizes x) { return MenuLayoutPolicy.MainMenu(f, x, false, false); }, s, what);
                }

            // Pause menu and its exit confirmation.
            object[] seed = { "pauseSeedMap", 200f, 54f, "pauseSeedName", 200f, 54f, "pauseSeedInput" };
            foreach (bool sure in new[] { false, true })
            {
                object[] items = sure ? new object[] { "exitNewStart", "exitRestart", "exitPrompt", "exitYes", "exitMainMenu", "exitNo" }
                                      : new object[] { "navMultiplayer", "pauseContinue", "pauseSave", "navSettings", "navExit" };
                MenuSizes s = Sizes(f, Join(Top(true, false), seed, items, new object[] { "discord" }));
                string what = at + " pause sureExit=" + sure;
                MenuPlan p = MenuLayoutPolicy.MainMenu(f, s, true, sure);
                Clean(p, f, what);
                Column(p, f, what, sure ? MenuLayoutPolicy.ExitNav : MenuLayoutPolicy.PauseNav);
                Check(p["pauseSeedInput"].Y0 - MenuTokens.Group >= p[sure ? "exitNewStart" : "navMultiplayer"].Y1 - 0.01f, what + ": seed block crowds the column");
                Stable(delegate(MenuSizes x) { return MenuLayoutPolicy.MainMenu(f, x, true, sure); }, s, what);
            }

            // Settings: every tab, with and without a preview and reset buttons.
            object[] tabs = { "tab0", 400f, 88f, "tab1", 400f, 88f, "tab2", 400f, 88f, "tab3", 400f, 88f, "tab4", 400f, 88f, "setBack", "setReset", "setPanel" };
            foreach (object[] extra in new[] { new object[0], new object[] { "setResetKeys" }, new object[] { "setPreview", 460f, 881f }, new object[] { "setPreview", 420f, 748f } })
            {
                MenuSizes s = Sizes(f, Join(tabs, extra));
                string what = at + " settings " + (extra.Length > 0 ? extra[0] : "plain");
                MenuPlan p = MenuLayoutPolicy.Settings(f, s);
                Clean(p, f, what);
                Column(p, f, what, MenuLayoutPolicy.Tabs);
                Check(Math.Abs(p["setPanel"].Y1 - MenuLayoutPolicy.ContentTop(f)) < 0.01f && Math.Abs(p["setPanel"].Y0 - f.Y0) < 0.01f, what + ": panel not between content top and frame bottom");
                Check(Math.Abs(p["setPanel"].W - MenuTokens.PanelW) < 0.01f, what + ": panel width");
                Check(Math.Abs(p["setBack"].Y0 - f.Y0) < 0.01f, what + ": back not at the bottom");
                Stable(delegate(MenuSizes x) { return MenuLayoutPolicy.Settings(f, x); }, s, what);
            }

            // Wheel setup.
            {
                MenuSizes s = Sizes(f, "wheelWheel", 640f, 356f, "wheelGas", 640f, 316f, "wheelBrake", 640f, 316f, "wheelClutch", 640f, 316f, "wheelHandBrake", 640f, 316f,
                                    "wheelAxes", 727f, 70f, "shiftHelp", 480f, 510f, "shiftToggle", 469f, 70f, "wheelBack");
                MenuPlan p = MenuLayoutPolicy.Wheel(f, s);
                Clean(p, f, at + " wheel");
                Stable(delegate(MenuSizes x) { return MenuLayoutPolicy.Wheel(f, x); }, s, at + " wheel");
            }

            // Save/Load: load mode, save mode and every confirmation.
            object[] grid = { "fileAuto1", 400f, 225f, "fileAuto2", 400f, 225f, "file01", 400f, 225f, "file02", 400f, 225f, "file03", 400f, 225f, "file04", 400f, 225f, "file05", 400f, 225f, "file06", 400f, 225f };
            object[] bar = { "pageText", 190f, 70f, "arrowUp", "arrowFastUp", "arrowJumpUp", "arrowDown", "arrowFastDown", "arrowJumpDown", "countText", 170f, 65f, "saveBack", "info", "setActive" };
            object[][] states =
            {
                new object[] { "loadLast", "slotLoad", 416f, 88f, "delete", 416f, 88f },
                new object[] { "loadLast.prompt", "loadLast.yes", "loadLast.no", "slotLoad", 416f, 88f, "delete", 416f, 88f },
                new object[] { "loadLast", "slotLoad", 416f, 88f, "delete.prompt", 336f, 56f, "delete.yes", 160f, 88f, "delete.no", 160f, 88f },
                new object[] { "saveName", "slotSave", 416f, 88f, "slotLoad.prompt", "slotLoad.yes", "slotLoad.no", "delete", 416f, 88f },
                new object[] { "saveName", "slotSave.prompt", "slotSave.yes", "slotSave.no", "slotLoad", 416f, 88f, "delete", 416f, 88f },
                new object[] { "saveName", "slotSave", 340f, 88f, "delete", 416f, 88f },
            };
            for (int k = 0; k < states.Length; k++)
                foreach (bool clock in new[] { true, false })
                {
                    var top = new List<object>(); if (clock) top.AddRange(new object[] { "clock", 200f, 70f }); top.AddRange(new object[] { "version", 340f, 54f });
                    MenuSizes s = Sizes(f, Join(top.ToArray(), bar, grid, states[k]));
                    string what = at + " save state " + k + " clock=" + clock;
                    MenuPlan p = MenuLayoutPolicy.SaveLoad(f, s);
                    Clean(p, f, what);
                    Check(Math.Abs(p["info"].Y1 - p["file01"].Y1) < 0.01f, what + ": info not aligned with the grid top");
                    Check(Math.Abs(p["fileAuto1"].Y1 - p["file01"].Y1) < 0.01f, what + ": autosaves not stacked from the top");
                    Check(Math.Abs(p["saveBack"].Y0 - f.Y0) < 0.01f && Math.Abs(p["saveBack"].X0 - f.X0) < 0.01f, what + ": back not bottom-left");
                    Stable(delegate(MenuSizes x) { return MenuLayoutPolicy.SaveLoad(f, x); }, s, what);
                }

            // Multiplayer: slider sizes, dialogs, in a menu and in game.
            foreach (float[] win in new[] { new float[] { 520, 380 }, new float[] { 894, 604 }, new float[] { 1520, ch - 100 } })
                foreach (bool menu in new[] { true, false })
                    foreach (string dialog in new[] { null, "mpPassword", "mpPlayer", "mpWrong" })
                    {
                        var items = new List<object> { "mpWindow", win[0], win[1], "mpTime", 348f, 38f, "mpDegu", 300f, 110f, "mpDataIn", 204f, 56f, "mpDataOut", 204f, 56f, "mpMic", 120f, 85f };
                        if (dialog != null) items.Add(dialog);
                        MenuSizes s = Sizes(f, items.ToArray());
                        string what = at + " mp " + win[0] + " menu=" + menu + " dialog=" + dialog;
                        float w, h;
                        MenuPlan p = MenuLayoutPolicy.Multiplayer(f, s, menu, out w, out h);
                        Clean(p, f, what, "mpWindow", "mpPassword", "mpWindow", "mpPlayer", "mpWindow", "mpWrong");
                        Check(w >= Math.Min(MenuTokens.MpMinW, p["mpWindow"].W) && h >= MenuTokens.MpMinH - 0.01f, what + ": window below its minimum");
                        if (menu) Check(p["mpWindow"].X0 >= MenuLayoutPolicy.ColumnRight(f) + MenuTokens.Group - 0.01f, what + ": window covers the menu column");
                        if (menu) foreach (string id in p.Order) Check(p[id].Y0 >= f.Y0 + MenuTokens.TopRowH - 0.01f , what + ": " + id + " runs into the version row");
                        if (dialog != null) Check(Math.Abs(p[dialog].CX - p["mpWindow"].CX) < 0.01f && Math.Abs(p[dialog].CY - p["mpWindow"].CY) < 0.01f, what + ": dialog not centred on the window");
                    }
        }
        Console.WriteLine("PASS: " + checks + " menu layout policy assertions");
    }
}
