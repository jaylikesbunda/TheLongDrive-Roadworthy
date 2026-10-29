using System;
using System.Collections.Generic;
using GeneszSteamP2PBase;
using UnityEngine;
using UnityEngine.UI;

namespace TldMp
{
    // Runtime relayout of the main, pause, save/load, settings, wheel setup and
    // multiplayer menus onto the MenuLayoutPolicy grid. It only resizes and moves
    // existing rect transforms; it never changes what a control does or shows.
    // Off with TLD_MP_MENU_LAYOUT=0, in VR, or for non-overlay canvases.
    [DefaultExecutionOrder(20000)]
    public sealed class MenuLayout : MonoBehaviour
    {
        const string O = "UIOVERLAY/2";
        const string MENU = O + "/Menu";
        const string MAIN = MENU + "/MainMenu/MainStuff";
        const string PAUSE = MENU + "/IngameMenu";
        const string SL = O + "/SaveLoadScreen";
        const string SET = O + "/SettingsPrefab";
        const string NS = SET + "/NewSettings";
        const string WS = SET + "/WheelSetup";
        const string MPW = "GameObject/Background";
        const string MPIN = MPW + "/GameObject";

        // Backgrounds that cover the whole canvas while active. Menu first: other Menu
        // children are anchored to its edges.
        static readonly string[] Covers =
        {
            MENU, O + "/MenuBackGround/ScreenShotBackGroundMenu", SL + "/BackGround", SL + "/TextLoad (1)/LoadTextBackGround",
            NS + "/BG", WS + "/WheelSetupBackground", O + "/TextLoad/LoadTextBackGround", MENU + "/MainMenu/Poszter", MAIN + "/CarSelectScreen",
        };
        static readonly string[] TabRoots = { "TabGraphics", "TabControls", "TabSteeringWheel", "TabMisc", "TabPlayer" };
        static readonly string[] TabButtons = { "ButtonGraphics", "ButtonControls", "ButtonSteeringWheel", "ButtonMisc", "ButtonPlayer" };

        // A placeable element: one or more roots (moved together), extra rects that are
        // part of its footprint (icons hanging outside it), and the token size kind.
        sealed class El
        {
            public string Id, Kind;
            public RectTransform[] Roots;
            public RectTransform[] Extras;
            public bool FitText;
        }

        readonly Dictionary<string, El> els = new Dictionary<string, El>();
        readonly MenuSizes sizes = new MenuSizes();
        readonly Dictionary<Transform, Vector3> baseScale = new Dictionary<Transform, Vector3>();
        readonly List<Transform> watched = new List<Transform>();
        static readonly Vector3[] corners = new Vector3[4];
        bool off, logged;
        int lastSig, pending = -1;
        RectTransform canvas;

        void Awake() { if (Environment.GetEnvironmentVariable("TLD_MP_MENU_LAYOUT") == "0") { off = true; enabled = false; } }

        void LateUpdate()
        {
            if (off) return;
            try
            {
                if (menuhandler.s == null) return;
                canvas = menuhandler.s.transform.parent as RectTransform;
                if (canvas == null || mainscript.IsVR()) return;
                Canvas c = canvas.GetComponent<Canvas>();
                if (c == null || c.renderMode != RenderMode.ScreenSpaceOverlay) return;
                int sig = Signature();
                if (sig != lastSig) { lastSig = sig; pending = Time.frameCount + 10; Apply(); }
                else if (pending >= 0 && Time.frameCount >= pending) { pending = -1; Apply(); }
            }
            catch (Exception e)
            {
                off = true; enabled = false;
                if (!logged) { logged = true; Debug.Log("[TLD MP menus] layout disabled: " + e.GetType().Name + " " + e.Message + "\n" + e.StackTrace); }
            }
        }

        static RectTransform Mp { get { return networkingScript.s == null || networkingScript.s.menu == null ? null : networkingScript.s.menu.transform as RectTransform; } }

        // Cheap per-frame change detection: canvas size, multiplayer size setting and
        // the active state of every menu root and toggled element.
        int Signature()
        {
            if (watched.Count == 0) Collect();
            int sig = (int)canvas.rect.width * 7919 + (int)canvas.rect.height;
            RectTransform mp = Mp;
            if (mp != null) sig = sig * 31 + (int)(networkingScript.s.menu.settings.FSize * 1000f);
            for (int i = 0; i < watched.Count; i++) sig = sig * 31 + (watched[i] != null && watched[i].gameObject.activeInHierarchy ? 1 : 0);
            return sig;
        }
        void Collect()
        {
            watched.Clear();
            var stack = new List<Transform>();
            foreach (string p in new[] { MENU, SL, SET, O + "/Clock", O + "/Version", O + "/TextLoad" }) { Transform t = canvas.Find(p); if (t != null) stack.Add(t); }
            if (Mp != null) stack.Add(Mp.Find("GameObject"));
            // Everything down to depth 4 under each root: enough to see every toggled element.
            for (int i = 0; i < stack.Count; i++)
            {
                Transform t = stack[i];
                if (t == null) continue;
                watched.Add(t);
                if (Depth(t) - Depth(canvas) < 7) for (int k = 0; k < t.childCount; k++) stack.Add(t.GetChild(k));
            }
        }
        static int Depth(Transform t) { int d = 0; while (t != null) { d++; t = t.parent; } return d; }

        // ---- canvas-space measurement and movement ----

        LRect Own(RectTransform rt, RectTransform space)
        {
            rt.GetWorldCorners(corners);
            Vector3 a = space.InverseTransformPoint(corners[0]), b = space.InverseTransformPoint(corners[2]);
            return new LRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        }
        LRect Foot(El e, RectTransform space)
        {
            LRect r = Own(e.Roots[0], space);
            for (int i = 1; i < e.Roots.Length; i++) r = r.Union(Own(e.Roots[i], space));
            if (e.Extras != null) foreach (RectTransform x in e.Extras) if (x != null && x.gameObject.activeInHierarchy) r = r.Union(Own(x, space));
            return r;
        }
        static void Move(RectTransform rt, RectTransform space, float dx, float dy)
        {
            if (Math.Abs(dx) < 0.01f && Math.Abs(dy) < 0.01f) return;
            rt.position += space.TransformVector(new Vector3(dx, dy, 0f));
        }
        // Resizes from the signed local rect, so stretch-anchored elements that start
        // inverted (negative height) are corrected too.
        void SetSize(RectTransform rt, RectTransform space, float w, float h)
        {
            float rx = rt.lossyScale.x / space.lossyScale.x, ry = rt.lossyScale.y / space.lossyScale.y;
            if (rx <= 0f || ry <= 0f) return;
            float dw = w / rx - rt.rect.width, dh = h / ry - rt.rect.height;
            if (Math.Abs(dw) < 0.05f && Math.Abs(dh) < 0.05f) return;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x + dw, rt.sizeDelta.y + dh);
        }
        void SetRect(RectTransform rt, RectTransform space, LRect r)
        {
            SetSize(rt, space, r.W, r.H);
            LRect cur = Own(rt, space);
            Move(rt, space, r.X0 - cur.X0, r.Y1 - cur.Y1);
        }

        // ---- element registry ----

        RectTransform Rt(Transform root, string path) { if (root == null) return null; Transform t = root.Find(path); return t as RectTransform; }
        static bool On(RectTransform rt) { return rt != null && rt.gameObject.activeInHierarchy; }

        void Add(Transform space, string id, string path, string kind, params string[] extras)
        {
            RectTransform rt = Rt(space, path);
            if (!On(rt)) return;
            var e = new El { Id = id, Kind = kind ?? id, Roots = new[] { rt } };
            if (extras.Length > 0) { e.Extras = new RectTransform[extras.Length]; for (int i = 0; i < extras.Length; i++) e.Extras[i] = Rt(rt, extras[i]); }
            els[id] = e;
        }
        // First active of several candidates fills one slot (a button or its status text).
        void AddFirst(Transform space, string id, string kind, string extra, params string[] paths)
        {
            foreach (string p in paths) { if (On(Rt(space, p))) { if (extra != null) Add(space, id, p, kind, extra); else Add(space, id, p, kind); return; } }
        }
        // Footprint covers every active descendant (labels that sit outside the root).
        void AddDeep(Transform space, string id, string path)
        {
            Add(space, id, path, id);
            El e; if (!els.TryGetValue(id, out e)) return;
            var kids = new List<RectTransform>();
            foreach (RectTransform k in e.Roots[0].GetComponentsInChildren<RectTransform>(false)) if (k != e.Roots[0]) kids.Add(k);
            e.Extras = kids.ToArray();
        }
        void AddText(Transform space, string id, string path)
        {
            Add(space, id, path, id);
            El e; if (els.TryGetValue(id, out e)) e.FitText = true;
        }

        void Register()
        {
            els.Clear();
            Transform c = canvas;
            Add(c, "clock", O + "/Clock", null);
            Add(c, "language", MENU + "/Language", null);
            AddText(c, "distance", MAIN + "/Distance");
            Add(c, "distanceReset", MAIN + "/Distance/ButtonResetDist", null);
            AddText(c, "version", O + "/Version");
            Add(c, "news", MENU + "/ButtonNews", null);
            Add(c, "discord", MENU + "/ButtonDiscord", null);
            Add(c, "discordText", MENU + "/ButtonDiscord/DiscordText/TMP_IF_Discord", null);
            Add(c, "discordCopy", MENU + "/ButtonDiscord/DiscordText/ButtonCopyDiscord", null);
            Add(c, "navContinue", MAIN + "/DisappearWhenExit/ButtonContinue", null);
            Add(c, "navMultiplayer", MENU + "/DisappearWhenExit/ButtonMultiplayer", null);
            Add(c, "navStart", MAIN + "/DisappearWhenExit/ButtonStart", null);
            Add(c, "navGear", MAIN + "/DisappearWhenExit/ButtonStart/ButtonGameModeSettings", null);
            Add(c, "navLoad", MAIN + "/DisappearWhenExit/ButtonLoad", null);
            Add(c, "navSettings", MENU + "/DisappearWhenExit/ButtonSettings", null);
            Add(c, "navExit", MENU + "/DisappearWhenExit/ButtonExit", null);
            Add(c, "mode", MAIN + "/GameModeSettingWindow", null, "Preview");
            Add(c, "newsObj", MENU + "/NewsObj", null);
            AddText(c, "pauseSeedMap", PAUSE + "/TMP_IF_Seed/TextMapName");
            AddText(c, "pauseSeedName", PAUSE + "/TMP_IF_Seed/TextSeedName");
            Add(c, "pauseSeedInput", PAUSE + "/TMP_IF_Seed", null);
            Add(c, "pauseContinue", PAUSE + "/DisappearWhenExit/ButtonContinue", null);
            Add(c, "pauseSave", PAUSE + "/DisappearWhenExit/ButtonSave", null);
            foreach (string n in new[] { "NewStart", "Restart", "ExitYes", "MainMenu", "ExitNo" })
                Add(c, "exit" + (n == "ExitYes" ? "Yes" : n == "ExitNo" ? "No" : n), MENU + "/SureExit/Button" + n, null);
            Add(c, "exitPrompt", MENU + "/SureExit/TextExitGame", null);

            Add(c, "info", SL + "/InfoBackGround", null);
            Add(c, "saveName", SL + "/TMP_IF_SaveName", null);
            AddText(c, "pageText", SL + "/PageText");
            AddText(c, "countText", SL + "/SaveCountText");
            foreach (string a in new[] { "Up", "FastUp", "JumpUp", "Down", "FastDown", "JumpDown" }) Add(c, "arrow" + a, SL + "/Button" + a, "arrow");
            Add(c, "fileAuto0", SL + "/ButtonFileAutoSave", "cell");
            Add(c, "fileAuto1", SL + "/ButtonFileAutoSave (1)", "cell");
            Add(c, "fileAuto2", SL + "/ButtonFileAutoSave (2)", "cell");
            for (int i = 1; i <= 6; i++) Add(c, "file0" + i, SL + "/ButtonFile0" + i, "cell");
            AddFirst(c, "slotSave", "slot", "Image", SL + "/ButtonSave", SL + "/TextSaved", SL + "/TextCantSave");
            Add(c, "slotSave.prompt", SL + "/TextSaveOverwrite", "slot.prompt");
            Add(c, "slotSave.yes", SL + "/TextSaveOverwrite/ButtonSaveYes", "slot.yes");
            Add(c, "slotSave.no", SL + "/TextSaveOverwrite/ButtonSaveNo", "slot.no");
            Add(c, "slotLoad", SL + "/ButtonLoad", "slot", "Image");
            Add(c, "slotLoad.prompt", SL + "/TextLoad", "slot.prompt");
            Add(c, "slotLoad.yes", SL + "/TextLoad/ButtonLoadYes", "slot.yes");
            Add(c, "slotLoad.no", SL + "/TextLoad/ButtonLoadNo", "slot.no");
            Add(c, "delete", SL + "/ButtonDelete", null, "Image");
            Add(c, "delete.prompt", SL + "/TextDelete", null);
            Add(c, "delete.yes", SL + "/TextDelete/ButtonDeleteYes", null);
            Add(c, "delete.no", SL + "/TextDelete/ButtonDeleteNo", null);
            Add(c, "loadLast", SL + "/ButtonLoadLast", null);
            Add(c, "loadLast.prompt", SL + "/TextLoadLast", null);
            Add(c, "loadLast.yes", SL + "/TextLoadLast/ButtonSaveYes", null);
            Add(c, "loadLast.no", SL + "/TextLoadLast/ButtonSaveNo", null);
            Add(c, "saveBack", SL + "/ButtonBack", null);
            Add(c, "setActive", SL + "/ButtonSetActive", null);

            for (int i = 0; i < 5; i++) Add(c, "tab" + i, NS + "/" + TabButtons[i], "setTab", "ImageLedOff");
            Add(c, "setBack", NS + "/ButtonBack", null);
            for (int i = 0; i < 5; i++)
            {
                if (!On(Rt(c, NS + "/" + TabRoots[i]))) continue;
                Add(c, "setPanel", NS + "/" + TabRoots[i] + "/BG", null);
                Add(c, "setReset", NS + "/" + TabRoots[i] + "/BG/ButtonReset", null);
                Add(c, "setResetKeys", NS + "/" + TabRoots[i] + "/BG/ButtonResetKeys", null);
                if (i == 4)
                {
                    RectTransform a = Rt(c, NS + "/TabPlayer/BG/PlayerDisplayBorder"), b = Rt(c, NS + "/TabPlayer/BG/PlayerDisplayDirt"), m = Rt(c, NS + "/TabPlayer/BG/PlayerDisplayMask");
                    if (On(a) && On(b) && On(m)) els["setPreview"] = new El { Id = "setPreview", Kind = "setPreview", Roots = new[] { a, b, m } };
                }
                if (i == 2) Add(c, "setPreview", NS + "/TabSteeringWheel/Image", null);
            }
            if (On(Rt(c, WS)))
            {
                // Each axis block's footprint is the slider plus every label/toggle it owns.
                foreach (string n in new[] { "Wheel", "Gas", "Brake", "Clutch", "HandBrake" }) AddDeep(c, "wheel" + n, WS + "/Slider" + n);
                AddDeep(c, "wheelAxes", WS + "/ToggleAxesIngame");
                // The help caption's box is far wider than its right-aligned text: trim it first.
                RectTransform cap = Rt(c, WS + "/ShiftHelpImage/ShiftHelpText");
                if (On(cap)) TrimTextRight(cap, canvas);
                Add(c, "shiftHelp", WS + "/ShiftHelpImage", null, "ShiftHelpText");
                AddDeep(c, "shiftToggle", WS + "/ToggleHShifter");
                Add(c, "wheelBack", WS + "/ButtonBack", null);
            }

            RectTransform mp = Mp;
            if (mp != null)
            {
                Add(mp, "mpWindow", MPW, null);
                Add(mp, "mpTime", MPIN + "/Time", null);
                Add(mp, "mpDegu", MPIN + "/TextDegu", null);
                Add(mp, "mpDataIn", MPIN + "/TextDataIn", null);
                Add(mp, "mpDataOut", MPIN + "/TextDataOut", null);
                Add(mp, "mpPassword", "GameObject/PasswordWindow", "mpDialog");
                Add(mp, "mpPlayer", "GameObject/PlayerWindow", "mpDialog");
                Add(mp, "mpWrong", "GameObject/WrongPasswordWindow", "mpDialog");
                Add(mp, "mpMessage", "GameObject/MessageIcon", null);
                Add(mp, "mpMic", "GameObject/MicrophoneIcon", null);
            }
        }

        // Token sizes and text-fit first, then natural (unscaled) footprints.
        void Measure(LRect frame, RectTransform space, IEnumerable<string> ids)
        {
            sizes.Clear();
            foreach (string id in ids)
            {
                El e; if (!els.TryGetValue(id, out e)) continue;
                float w, h;
                if (MenuLayoutPolicy.TokenSize(e.Kind, frame, out w, out h)) SetSize(e.Roots[0], space, w, h);
                if (e.FitText) FitText(e.Roots[0], space);
                foreach (RectTransform r in e.Roots) { Vector3 b; if (!baseScale.TryGetValue(r, out b)) { b = r.localScale; baseScale[r] = b; } r.localScale = b; }
                LRect f = Foot(e, space);
                sizes.Add(id, f.W, f.H);
            }
        }
        // Text boxes grow to their rendered text so footprints match what is drawn.
        void FitText(RectTransform rt, RectTransform space)
        {
            ILayoutElement le = rt.GetComponent<ILayoutElement>();
            if (le == null) return;
            LRect cur = Own(rt, space);
            float rx = rt.lossyScale.x / space.lossyScale.x;
            float w = Math.Max(cur.W, (le.preferredWidth + 8f) * rx), h = Math.Max(cur.H, le.preferredHeight * rx);
            SetSize(rt, space, (float)Math.Ceiling(w), (float)Math.Ceiling(h));
        }

        // Shrinks a text box to its rendered width, keeping its right edge.
        void TrimTextRight(RectTransform rt, RectTransform space)
        {
            ILayoutElement le = rt.GetComponent<ILayoutElement>();
            if (le == null) return;
            LRect cur = Own(rt, space);
            float rx = rt.lossyScale.x / space.lossyScale.x;
            float w = (float)Math.Ceiling((le.preferredWidth + 8f) * rx);
            if (w >= cur.W - 0.5f) return;
            SetSize(rt, space, w, cur.H);
            LRect now = Own(rt, space);
            Move(rt, space, cur.X1 - now.X1, cur.Y1 - now.Y1);
        }

        // Applies a plan: parents before children, scale-to-fit groups, footprint top-left to target.
        void Place(MenuPlan plan, RectTransform space)
        {
            var order = new List<string>(plan.Order);
            order.Sort(delegate(string a, string b)
            {
                int da = Depth(els[a].Roots[0]), db = Depth(els[b].Roots[0]);
                return da != db ? da.CompareTo(db) : plan.Order.IndexOf(a).CompareTo(plan.Order.IndexOf(b));
            });
            foreach (string id in order)
            {
                El e = els[id];
                LRect target = plan[id];
                float s = plan.ScaleOf(id);
                if (e.Roots.Length == 1)
                {
                    e.Roots[0].localScale = baseScale[e.Roots[0]] * s;
                    LRect f = Foot(e, space);
                    if (e.Extras == null && s == 1f && (Math.Abs(f.W - target.W) > 0.5f || Math.Abs(f.H - target.H) > 0.5f)) { SetSize(e.Roots[0], space, target.W, target.H); f = Foot(e, space); }
                    Move(e.Roots[0], space, target.X0 - f.X0, target.Y1 - f.Y1);
                    continue;
                }
                // Sibling roots: keep their relative layout while scaling.
                LRect u = Foot(e, space);
                var off = new Vector2[e.Roots.Length];
                for (int i = 0; i < e.Roots.Length; i++) { LRect o = Own(e.Roots[i], space); off[i] = new Vector2(o.X0 - u.X0, o.Y1 - u.Y1); }
                for (int i = 0; i < e.Roots.Length; i++)
                {
                    e.Roots[i].localScale = baseScale[e.Roots[i]] * s;
                    LRect o = Own(e.Roots[i], space);
                    Move(e.Roots[i], space, target.X0 + off[i].x * s - o.X0, target.Y1 + off[i].y * s - o.Y1);
                }
            }
        }

        List<string> CanvasIds() { var l = new List<string>(); foreach (string k in els.Keys) if (!k.StartsWith("mp")) l.Add(k); return l; }

        static bool Active(Transform root, string path) { if (root == null) return false; Transform t = root.Find(path); return t != null && t.gameObject.activeInHierarchy; }

        void Apply()
        {
            float cw = canvas.rect.width, ch = canvas.rect.height;
            LRect frame = MenuLayoutPolicy.Frame(cw, ch);
            LRect cover = MenuLayoutPolicy.Cover(cw, ch);
            foreach (string p in Covers)
            {
                RectTransform rt = Rt(canvas, p);
                if (!On(rt)) continue;
                SetRect(rt, canvas, cover);
                if (p == WS + "/WheelSetupBackground")
                {
                    // Opaque, so the settings tab underneath no longer shows through.
                    Image img = rt.GetComponent<Image>();
                    if (img != null && img.color.a < 1f) { Color k = img.color; k.a = 1f; img.color = k; }
                }
            }
            Register();
            if (Active(canvas, MENU))
            {
                bool pause = Active(canvas, PAUSE), sure = Active(canvas, MENU + "/SureExit");
                Measure(frame, canvas, CanvasIds());
                Place(MenuLayoutPolicy.MainMenu(frame, sizes, pause, sure), canvas);
            }
            if (Active(canvas, SL)) { Measure(frame, canvas, CanvasIds()); Place(MenuLayoutPolicy.SaveLoad(frame, sizes), canvas); }
            if (Active(canvas, SET))
            {
                Measure(frame, canvas, CanvasIds());
                Place(MenuLayoutPolicy.Settings(frame, sizes), canvas);
                if (Active(canvas, WS)) { Measure(frame, canvas, CanvasIds()); Place(MenuLayoutPolicy.Wheel(frame, sizes), canvas); }
            }
            RectTransform mp = Mp;
            if (mp != null && Active(mp, MPW))
            {
                LRect mf = MenuLayoutPolicy.Frame(mp.rect.width, mp.rect.height);
                bool menuOpen = (DataFromMenuScript.s != null && DataFromMenuScript.s.mainmenu) || (mainscript.s != null && mainscript.s.pauseMenuOpen);
                var ids = new List<string>(); foreach (string k in els.Keys) if (k.StartsWith("mp")) ids.Add(k);
                Measure(mf, mp, ids);
                float w, h;
                MenuPlan plan = MenuLayoutPolicy.Multiplayer(mf, sizes, menuOpen, out w, out h);
                SetSize(els["mpWindow"].Roots[0], mp, w, h);
                Place(plan, mp);
                // Dialogs get an opaque backdrop so the window text no longer shows through.
                foreach (string d in new[] { "mpPassword", "mpPlayer", "mpWrong" })
                {
                    El e; if (!els.TryGetValue(d, out e)) continue;
                    Image img = e.Roots[0].GetComponent<Image>();
                    if (img != null && img.color.a < 1f) { Color k = img.color; k.a = 1f; img.color = k; }
                }
                // The lobby list was 350 units wider than its half of the window.
                RectTransform list = Rt(mp, MPIN + "/Lobby/Both/List");
                if (list != null) { list.anchorMin = new Vector2(0.55f, 0f); list.anchorMax = new Vector2(0.99f, 1f); list.sizeDelta = Vector2.zero; list.anchoredPosition = Vector2.zero; }
            }
        }
    }

}
