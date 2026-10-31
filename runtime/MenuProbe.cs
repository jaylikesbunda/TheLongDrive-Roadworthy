using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GeneszSteamP2PBase;
using UnityEngine;
using UnityEngine.UI;

namespace TldMp
{
    // Developer-only menu layout probe. It never runs unless TLD_MP_MENU_PROBE
    // names an output folder. It walks every menu view and state through the
    // game's own open/close toggles (never Load, Save, Delete or settings save),
    // records screenshots plus the canvas-space rect of every active UI element,
    // then restores each state. Resolutions come from TLD_MP_MENU_PROBE_RES.
    public sealed class MenuProbe : MonoBehaviour
    {
        string output;
        readonly List<string> log = new List<string>();

        public static void StartIfRequested(GameObject host)
        {
            if(Environment.GetEnvironmentVariable("TLD_MP_WHEEL_PROBE")=="1" && host.GetComponent<WheelAssemblyProbe>()==null)host.AddComponent<WheelAssemblyProbe>();
            string dir = Environment.GetEnvironmentVariable("TLD_MP_MENU_PROBE");
            if (string.IsNullOrEmpty(dir) || host == null || host.GetComponent<MenuProbe>() != null) return;
            host.AddComponent<MenuProbe>().output = dir;
        }

        void Start() { StartCoroutine(Run()); }

        struct State
        {
            public string Name;
            public Action Enter;
            public Action Exit;
            public State(string name, Action enter, Action exit) { Name = name; Enter = enter; Exit = exit; }
        }

        IEnumerator Run()
        {
            Directory.CreateDirectory(output);
            float deadline = Time.realtimeSinceStartup + 120f;
            while ((menuhandler.s == null || settingsscript.s == null || DataFromMenuScript.s == null || !DataFromMenuScript.s.mainmenu) && Time.realtimeSinceStartup < deadline) yield return null;
            for (int i = 0; i < 300; i++) yield return null;
            if (menuhandler.s == null) { Note("menuhandler never appeared"); Flush(); yield break; }
            // Warm-up: the first settings open fills its tabs a frame late.
            try { settingsscript.s.Open(); settingsscript.s.PressedTab(0); } catch { }
            for (int i = 0; i < 10; i++) yield return null;
            CloseSettings();
            int originalW = Screen.width, originalH = Screen.height; FullScreenMode originalMode = Screen.fullScreenMode;
            string resList = Environment.GetEnvironmentVariable("TLD_MP_MENU_PROBE_RES");
            if (string.IsNullOrEmpty(resList)) resList = "1920x1080,1280x720,1024x768,1280x1024,2560x1080,1600x900";
            string only = Environment.GetEnvironmentVariable("TLD_MP_MENU_PROBE_ONLY");
            if (only == "loading-composition")
            {
                yield return StartCoroutine(LoadingComposition());
                Note("done"); Flush();
                if (Environment.GetEnvironmentVariable("TLD_MP_MENU_PROBE_QUIT") == "1") { yield return null; Application.Quit(); }
                yield break;
            }
            foreach (string res in resList.Split(','))
            {
                string[] p = res.Trim().Split('x');
                int w, h;
                if (p.Length != 2 || !int.TryParse(p[0], out w) || !int.TryParse(p[1], out h)) continue;
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                for (int i = 0; i < 90 && (Screen.width != w || Screen.height != h); i++) yield return null;
                for (int i = 0; i < 10; i++) yield return null;
                string tag = Screen.width + "x" + Screen.height;
                if (Screen.width != w || Screen.height != h) Note("requested " + res + " got " + tag);
                foreach (State s in States())
                {
                    if (!string.IsNullOrEmpty(only) && s.Name.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool entered = false;
                    try { s.Enter(); entered = true; } catch (Exception e) { Note(s.Name + " enter failed: " + e.GetType().Name + " " + e.Message); }
                    if (entered)
                    {
                        for (int i = 0; i < 4; i++) yield return null;
                        yield return new WaitForEndOfFrame();
                        string stem = Path.Combine(output, tag + "_" + s.Name);
                        try { ScreenCapture.CaptureScreenshot(stem + ".png"); } catch (Exception e) { Note(s.Name + " screenshot failed: " + e.Message); }
                        try { File.WriteAllText(stem + ".json", Dump(s.Name, tag)); } catch (Exception e) { Note(s.Name + " dump failed: " + e.Message); }
                        yield return null; yield return null;
                    }
                    try { s.Exit(); } catch (Exception e) { Note(s.Name + " exit failed: " + e.GetType().Name + " " + e.Message); }
                    for (int i = 0; i < 2; i++) yield return null;
                }
            }
            Screen.SetResolution(originalW, originalH, originalMode);
            Note("done");
            Flush();
            if (Environment.GetEnvironmentVariable("TLD_MP_MENU_PROBE_QUIT") == "1") { yield return null; Application.Quit(); }
        }

        void Note(string text) { log.Add(DateTime.Now.ToString("HH:mm:ss") + " " + text); Debug.Log("[TLD MP menu probe] " + text); }
        void Flush() { try { File.WriteAllLines(Path.Combine(output, "probe-log.txt"), log.ToArray()); } catch { } }

        static Transform Find(Transform root, string path) { return root == null ? null : root.Find(path); }
        static void Set(Transform root, string path, bool on) { Transform t = Find(root, path); if (t != null) t.gameObject.SetActive(on); }
        static Transform Overlay { get { return menuhandler.s == null ? null : menuhandler.s.transform; } }
        static Transform Column { get { return Find(Overlay, "2"); } }

        static void CloseSettings()
        {
            if (settingsscript.s == null) return;
            if (menuhandler.s != null) menuhandler.s.Menu.SetActive(true);
            if (mainscript.s != null) mainscript.s.settingsOpen = false;
            settingsscript.s.gameObject.SetActive(false);
        }
        static void OpenWindow(int index)
        {
            for (int i = 0; i < menuhandler.s.menuWindows.Count; i++) foreach (GameObject g in menuhandler.s.menuWindows[i].objs) if (g != null) g.SetActive(false);
            if (index == 2 && gamemodesettingsscript.s != null) gamemodesettingsscript.s.Open();
            foreach (GameObject g in menuhandler.s.menuWindows[index].objs) if (g != null) g.SetActive(true);
        }
        static void CloseWindows()
        {
            for (int i = 0; i < menuhandler.s.menuWindows.Count; i++) foreach (GameObject g in menuhandler.s.menuWindows[i].objs) if (g != null) g.SetActive(false);
        }
        static void SureExit(bool on)
        {
            if (on) { foreach (GameObject g in menuhandler.s.DisappearWhenExit) if (g != null) g.SetActive(false); menuhandler.s.SureExit.SetActive(true); }
            else menuhandler.s.ResetPressed();
        }
        static newSaveScreenScript Save { get { return menuhandler.s.saveScreen; } }
        static void SaveConfirm(string button, string confirm)
        {
            Transform root = Save.transform;
            Set(root, button, false); Set(root, confirm, true);
        }
        static SteamP2PMenuHandlerScript Mp { get { return networkingScript.s == null ? null : networkingScript.s.menu; } }
        static Transform MpLobby { get { return Mp == null ? null : Mp.transform.Find("GameObject/Background/GameObject/Lobby"); } }
        static void MpLobbyPart(string part)
        {
            Transform lobby = MpLobby;
            foreach (string n in new string[] { "Both", "Server", "Client" }) Set(lobby, n, n == "Both" || n == part);
        }
        static void MpWindow(string name, bool on) { if (Mp != null) Set(Mp.transform, "GameObject/" + name, on); }
        static float mpSize = -1f;
        static void MpSize(float size)
        {
            if (mpSize < 0f) mpSize = Mp.settings.FSize;
            Mp.settings.FSize = size; Mp.RefreshMainSize();
        }
        static void MpSizeRestore() { if (mpSize >= 0f && Mp != null) { Mp.settings.FSize = mpSize; Mp.RefreshMainSize(); } mpSize = -1f; }
        static void Pause(bool on) { DataFromMenuScript.s.mainmenu = !on; }

        static List<State> States()
        {
            var s = new List<State>();
            Action none = delegate { };
            s.Add(new State("main", none, none));
            if (menuhandler.s.menuWindows != null)
                for (int i = 0; i < menuhandler.s.menuWindows.Count; i++) { int k = i; s.Add(new State("main-window" + k, delegate { OpenWindow(k); }, CloseWindows)); }
            s.Add(new State("main-news", delegate { menuhandler.s.newsObj.SetActive(true); }, delegate { menuhandler.s.newsObj.SetActive(false); }));
            s.Add(new State("main-loading", delegate { Set(Column, "TextLoad", true); }, delegate { Set(Column, "TextLoad", false); }));
            for (int i = 0; i < 5; i++) { int k = i; s.Add(new State("settings-tab" + k, delegate { settingsscript.s.Open(); settingsscript.s.PressedTab(k); }, CloseSettings)); }
            s.Add(new State("settings-wheelsetup", delegate { settingsscript.s.Open(); Set(settingsscript.s.transform, "WheelSetup", true); }, delegate { Set(settingsscript.s.transform, "WheelSetup", false); CloseSettings(); }));
            s.Add(new State("load", delegate { Save.Open(); }, delegate { Save.PressedBack(); }));
            s.Add(new State("load-selected", delegate { Save.Open(); Save.SelectSlot(0); }, delegate { Save.PressedBack(); }));
            s.Add(new State("load-confirmdelete", delegate { Save.Open(); Save.SelectSlot(0); Save.PressedDelete(); }, delegate { Save.PressedDeleteNo(); Save.PressedBack(); }));
            s.Add(new State("load-confirmloadlast", delegate { Save.Open(); SaveConfirm("ButtonLoadLast", "TextLoadLast"); }, delegate { Save.PressedLoadLastNo(); Save.PressedBack(); }));
            s.Add(new State("mp-lobby", delegate { Mp.APressedMulti(); Mp.PressedLobby(); MpLobbyPart("Server"); }, delegate { Mp.Close(); }));
            s.Add(new State("mp-lobby-client", delegate { Mp.APressedMulti(); Mp.PressedLobby(); MpLobbyPart("Client"); }, delegate { MpLobbyPart("Server"); Mp.Close(); }));
            s.Add(new State("mp-chat", delegate { Mp.APressedMulti(); Mp.PressedChat(); }, delegate { Mp.Close(); }));
            s.Add(new State("mp-settings", delegate { Mp.APressedMulti(); Mp.PressedSettings(); }, delegate { Mp.Close(); }));
            s.Add(new State("mp-lobby-small", delegate { Mp.APressedMulti(); Mp.PressedLobby(); MpSize(0f); }, delegate { MpSizeRestore(); Mp.Close(); }));
            s.Add(new State("mp-lobby-large", delegate { Mp.APressedMulti(); Mp.PressedLobby(); MpSize(1f); }, delegate { MpSizeRestore(); Mp.Close(); }));
            s.Add(new State("mp-password", delegate { Mp.APressedMulti(); MpWindow("PasswordWindow", true); }, delegate { MpWindow("PasswordWindow", false); Mp.Close(); }));
            s.Add(new State("mp-player", delegate { Mp.APressedMulti(); MpWindow("PlayerWindow", true); }, delegate { MpWindow("PlayerWindow", false); Mp.Close(); }));
            s.Add(new State("mp-wrongpassword", delegate { Mp.APressedMulti(); MpWindow("WrongPasswordWindow", true); }, delegate { MpWindow("WrongPasswordWindow", false); Mp.Close(); }));
            // Pause-menu views are shown by flipping the main-menu flag the game's
            // own menu update reads; it is restored on exit.
            s.Add(new State("pause", delegate { Pause(true); }, delegate { Pause(false); }));
            s.Add(new State("pause-sureexit", delegate { Pause(true); SureExit(true); }, delegate { SureExit(false); Pause(false); }));
            s.Add(new State("save", delegate { Pause(true); Save.Open(); }, delegate { Save.PressedBack(); Pause(false); }));
            s.Add(new State("save-confirmoverwrite", delegate { Pause(true); Save.Open(); Save.SelectSlot(0); SaveConfirm("ButtonSave", "TextSaveOverwrite"); }, delegate { Save.PressedSaveNo(); Save.PressedBack(); Pause(false); }));
            s.Add(new State("save-confirmload", delegate { Pause(true); Save.Open(); Save.SelectSlot(0); SaveConfirm("ButtonLoad", "TextLoad"); }, delegate { Save.PressedLoadNo(); Save.PressedBack(); Pause(false); }));
            s.Add(new State("save-cantsave", delegate { Pause(true); Save.Open(); Set(Save.transform, "TextCantSave", true); Set(Save.transform, "ButtonSave", false); }, delegate { Save.PressedBack(); Pause(false); }));
            return s;
        }

        static readonly Vector3[] corners = new Vector3[4];
        static string Dump(string state, string tag)
        {
            var sb = new StringBuilder();
            sb.Append("{\"state\":\"").Append(state).Append("\",\"screen\":\"").Append(tag).Append("\",\"canvases\":[");
            bool firstCanvas = true;
            foreach (Transform root in new Transform[] { Overlay == null ? null : Overlay.parent, Mp == null ? null : Mp.transform })
            {
                RectTransform canvas = root as RectTransform;
                if (canvas == null) continue;
                if (!firstCanvas) sb.Append(','); firstCanvas = false;
                Rect cr = canvas.rect;
                sb.Append("{\"name\":\"").Append(root.name).Append("\",\"w\":").Append(F(cr.width)).Append(",\"h\":").Append(F(cr.height)).Append(",\"items\":[");
                bool first = true;
                Walk(canvas, canvas, "", ref first, sb);
                sb.Append("]}");
            }
            sb.Append("]}");
            return sb.ToString();
        }
        static void Walk(RectTransform canvas, Transform t, string path, ref bool first, StringBuilder sb)
        {
            if (!t.gameObject.activeInHierarchy) return;
            RectTransform rt = t as RectTransform;
            if (rt != null && t != canvas)
            {
                rt.GetWorldCorners(corners);
                Vector3 a = canvas.InverseTransformPoint(corners[0]), b = canvas.InverseTransformPoint(corners[2]);
                var kinds = new StringBuilder();
                foreach (Component c in t.GetComponents<Component>())
                {
                    if (c == null || c is Transform) continue;
                    var be = c as Behaviour;
                    if (be != null && !be.enabled) continue;
                    if (kinds.Length > 0) kinds.Append('|');
                    kinds.Append(c.GetType().Name);
                }
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"p\":\"").Append(Esc(path)).Append("\",\"r\":[").Append(F(a.x)).Append(',').Append(F(a.y)).Append(',').Append(F(b.x)).Append(',').Append(F(b.y)).Append("],\"k\":\"").Append(kinds.ToString()).Append("\"}");
            }
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                Walk(canvas, c, path.Length == 0 ? c.name : path + "/" + c.name, ref first, sb);
            }
        }
        static string F(float v) { return v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture); }
        static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }

        // Opt-in render diagnostic for the held loading overlay. Captures the
        // game's own menu composition, then the held stack built by
        // GroundDiagnostics.TickLoadingOverlay, then isolations of each piece.
        // Restores every flag, object and the Driver it touched.
        IEnumerator LoadingComposition()
        {
            var menu = menuhandler.s; var data = DataFromMenuScript.s;
            var menuBg = Find(Column, "MenuBackGround");
            var menuBgObj = menuBg == null ? null : menuBg.gameObject;
            var driver = UnityEngine.Object.FindObjectOfType<StableGameBridge.Driver>();
            Transform dimmerT = null;
            if (menu.GLoadText != null && menu.GLoadText.transform != null) dimmerT = menu.GLoadText.transform.Find("LoadTextBackGround");
            if (dimmerT == null && menu.transform != null) dimmerT = menu.transform.Find("2/TextLoad/LoadTextBackGround");
            var dimImg = dimmerT == null ? null : dimmerT.gameObject.GetComponent<Image>();
            bool dataWas = data.mainmenu;
            bool menuWas = menu.Menu != null && menu.Menu.activeSelf;
            bool bgObjWas = menuBgObj != null && menuBgObj.activeSelf;
            bool loadWas = menu.GLoadText != null && menu.GLoadText.activeSelf;
            bool driverWas = driver != null && driver.enabled;
            bool menuUpdateWas = menu.enabled;
            bool mainWas = mainscript.s != null && mainscript.s.enabled;
            bool pauseWas = mainscript.s != null && mainscript.s.pauseMenuOpen;
            bool dimWas = dimImg == null || dimImg.enabled;
            Note("snapshot mainmenu=" + dataWas + " Menu=" + menuWas + " MenuBackGround=" + bgObjWas + " GLoadText=" + loadWas + " driver=" + driverWas + " menuUpdate=" + menuUpdateWas + " mainscript=" + mainWas + " pauseMenuOpen=" + pauseWas + " dimImg=" + dimWas);
            try
            {
                if (driver != null) driver.enabled = false;
                menu.enabled = false;
                if (mainscript.s != null) { mainscript.s.enabled = false; mainscript.s.pauseMenuOpen = false; }
                Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
                for (int i = 0; i < 60 && (Screen.width != 1920 || Screen.height != 1080); i++) yield return null;
                for (int i = 0; i < 10; i++) yield return null;
                // Native reference: the game's own stack while its loading text is up.
                if (menu.GLoadText != null) menu.GLoadText.SetActive(true);
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();
                Capture("native-reference", menu, dimmerT);
                // Let the hold code learn the menu's composition, then hide the
                // menu the way the game's own load handoff does.
                GroundDiagnostics.TickLoadingOverlay(false);
                data.mainmenu = false;
                if (menu.Menu != null) menu.Menu.SetActive(false);
                if (menuBgObj != null) menuBgObj.SetActive(false);
                GroundDiagnostics.TickLoadingOverlay(false);
                if (menu.Menu != null) menu.Menu.SetActive(false);
                if (menuBgObj != null) menuBgObj.SetActive(false);
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();
                Capture("held-composition", menu, dimmerT);
                // Baseline pair: same captured sprite drawn at the menu's own
                // .25 opacity, to compare against the corrected full alpha.
                var paperOnly = dimmerT == null || dimmerT.parent == null ? null : Find(dimmerT.parent, "TldMpLoadingPaper");
                var paperImg = paperOnly == null ? null : paperOnly.gameObject.GetComponent<Image>();
                if (paperImg != null) paperImg.color = new Color(paperImg.color.r, paperImg.color.g, paperImg.color.b, .2509804f);
                for (int i = 0; i < 2; i++) yield return null;
                yield return new WaitForEndOfFrame();
                Capture("held-paper-native-opacity", menu, dimmerT);
                if (paperImg != null) paperImg.color = new Color(paperImg.color.r, paperImg.color.g, paperImg.color.b, 1f);
                // Same stack without the native dimmer image.
                if (dimImg != null) dimImg.enabled = false;
                for (int i = 0; i < 2; i++) yield return null;
                yield return new WaitForEndOfFrame();
                Capture("held-no-dimmer", menu, dimmerT);
                if (dimImg != null) dimImg.enabled = true;
                // Paper alone: generated base and frame off, dimmer image off.
                var baseT = dimmerT == null || dimmerT.parent == null ? null : Find(dimmerT.parent, "TldMpLoadingBase");
                var frameT = dimmerT == null || dimmerT.parent == null ? null : Find(dimmerT.parent, "TldMpLoadingFrame");
                if (menu.Menu != null) menu.Menu.SetActive(false);
                if (menuBgObj != null) menuBgObj.SetActive(false);
                if (baseT != null) baseT.gameObject.SetActive(false);
                if (frameT != null) frameT.gameObject.SetActive(false);
                if (dimImg != null) dimImg.enabled = false;
                for (int i = 0; i < 2; i++) yield return null;
                yield return new WaitForEndOfFrame();
                Capture("held-paper-alone", menu, dimmerT);
                if (dimImg != null) dimImg.enabled = true;
            }
            finally
            {
                data.mainmenu = true;
                try { GroundDiagnostics.TickLoadingOverlay(false); } catch (Exception e) { Note("cleanup tick failed: " + e.GetType().Name + " " + e.Message); }
                data.mainmenu = dataWas;
                if (menu.Menu != null) menu.Menu.SetActive(menuWas);
                if (menuBgObj != null) menuBgObj.SetActive(bgObjWas);
                if (menu.GLoadText != null) menu.GLoadText.SetActive(loadWas);
                if (dimImg != null) dimImg.enabled = dimWas;
                menu.enabled = menuUpdateWas;
                if (mainscript.s != null) { mainscript.s.enabled = mainWas; mainscript.s.pauseMenuOpen = pauseWas; }
                if (driver != null) driver.enabled = driverWas;
                Note("restored mainmenu=" + data.mainmenu + " Menu=" + (menu.Menu != null && menu.Menu.activeSelf) + " MenuBackGround=" + (menuBgObj != null && menuBgObj.activeSelf) + " menuUpdate=" + menu.enabled + " mainscript=" + (mainscript.s != null && mainscript.s.enabled) + " pauseMenuOpen=" + (mainscript.s != null && mainscript.s.pauseMenuOpen) + " driver=" + (driver != null && driver.enabled));
            }
        }

        void Capture(string stem, menuhandler menu, Transform dimmerT)
        {
            try { ScreenCapture.CaptureScreenshot(Path.Combine(output, stem + ".png")); } catch (Exception e) { Note(stem + " screenshot failed: " + e.Message); }
            var sb = new StringBuilder();
            sb.AppendLine(stem + " screen=" + Screen.width + "x" + Screen.height);
            DumpImage(sb, "menuBackGroundImage", menu.menuBackGroundImage);
            DumpImage(sb, "menuOverlayBG", menu.menuOverlayBG);
            DumpImage(sb, "menuOverlayBGKeret", menu.menuOverlayBGKeret);
            DumpImage(sb, "dimmer", dimmerT == null ? null : dimmerT.gameObject.GetComponent<Image>());
            if (dimmerT != null && dimmerT.parent != null)
                for (int i = 0; i < dimmerT.parent.childCount; i++)
                {
                    var c = dimmerT.parent.GetChild(i);
                    if (c.name.StartsWith("TldMpLoading")) DumpImage(sb, c.name, c.gameObject.GetComponent<Image>());
                }
            try { File.WriteAllText(Path.Combine(output, stem + ".txt"), sb.ToString()); } catch (Exception e) { Note(stem + " dump failed: " + e.Message); }
        }

        static void DumpImage(StringBuilder sb, string label, Image img)
        {
            if (img == null) { sb.AppendLine(label + ": <missing>"); return; }
            var go = img.gameObject; var t = go.transform;
            sb.Append(label + ": " + PathOf(t));
            sb.Append(" active=" + go.activeSelf + "/" + go.activeInHierarchy + " enabled=" + img.enabled + " layer=" + go.layer + " sib=" + t.GetSiblingIndex());
            var sp = img.sprite;
            sb.Append(sp == null ? " sprite=null" : " sprite=" + sp.name + "#" + sp.GetInstanceID() + " tex=" + (sp.texture == null ? "null" : sp.texture.width + "x" + sp.texture.height));
            var c = img.color; sb.Append(" color=" + c.r + "," + c.g + "," + c.b + "," + c.a);
            sb.Append(" type=" + img.type + " preserveAspect=" + img.preserveAspect + " raycast=" + img.raycastTarget);
            var m = img.material; sb.Append(m == null ? " mat=null" : " mat=" + m.name + " shader=" + (m.shader == null ? "null" : m.shader.name));
            var cr = img.canvasRenderer; sb.Append(cr == null ? " canvasAlpha=n/a" : " canvasAlpha=" + cr.GetAlpha());
            var rt = t as RectTransform;
            if (rt != null) { var r = rt.rect; sb.Append(" rect=" + r.width + "x" + r.height + " anchors=" + rt.anchorMin + ">" + rt.anchorMax + " size=" + rt.sizeDelta); }
            float ga = 1f; bool found = false;
            foreach (var g in img.GetComponentsInParent<CanvasGroup>()) if (g != null) { found = true; ga *= g.alpha; }
            sb.AppendLine(found ? " groupAlpha=" + ga : " groupAlpha=none");
        }

        static string PathOf(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
    // Opt-in scene probe: temporary prefab clones in the main menu only.
    public sealed class WheelAssemblyProbe:MonoBehaviour
    {
        IEnumerator Start()
        {
            while(itemdatabase.s==null || mainscript.s==null || menuhandler.s==null)yield return null;
            for(int i=0;i<120;i++)yield return null;
            if(!DataFromMenuScript.s.mainmenu){Debug.LogError("[wheel-probe] refused outside main menu");yield break;}
            foreach(int id in new[]{409,411})
            {
                GameObject rg=null,tg=null;
                try
                {
                    GameObject source=itemdatabase.s.Item(43);
                    foreach(var candidate in Resources.FindObjectsOfTypeAll<tosaveitemscript>())if(candidate.name=="Car07felnitire"){source=candidate.gameObject;break;}
                    rg=UnityEngine.Object.Instantiate(source,new Vector3(0,10000,0),Quaternion.identity);
                    tg=UnityEngine.Object.Instantiate(itemdatabase.s.Item(id),new Vector3(0,10000,0),Quaternion.identity);
                    var rim=rg.GetComponent<tosaveitemscript>();var tyre=tg.GetComponent<tosaveitemscript>();rim.FStart();tyre.FStart();
                    var slot=rim.P.wheel.gumi;
                    while(slot.hasPart()){var previous=slot.part();slot.UnCraft();previous.gameObject.SetActive(false);UnityEngine.Object.Destroy(previous.gameObject);}
                    slot.Craft(tyre.attachable);
                    var controllerObject=new GameObject("WheelPickupProbeController");
                    var player=controllerObject.AddComponent<fpscontroller>();player.enabled=false;player.input=inputscript.i;player.BcanMount=true;
                    var collider=tyre.P.cols[0].GetComponent<Collider>();
                    var camera=controllerObject.AddComponent<Camera>();camera.enabled=false;player.Cam=camera;player.FrayRange=4;player.pickupLayer=~0;
                    camera.transform.position=new Vector3(2,10000,0);camera.transform.rotation=Quaternion.LookRotation(Vector3.left);Physics.SyncTransforms();
                    var selected=StableGameBridge.ResolveWheelPickup(collider,player);
                    Debug.Log("[wheel-probe] tyre="+id+" selected="+(selected==null?"null":selected.name)+" expected="+rim.P.name+
                        " slotOwner="+(slot.tosaveitem==null?"null":slot.tosaveitem.name)+" slotPick="+(slot.pick==null?"null":slot.pick.name)+
                        " tyreAttached="+tyre.attachable.attached+" sameSlot="+(tyre.attachable.slot==slot)+" hasPart="+slot.hasPart(tyre.attachable)+
                        " rimAttached="+(rim.attachable!=null && rim.attachable.attached)+" rimSlot="+(rim.attachable!=null && rim.attachable.slot!=null)+
                        " pickup="+rim.P.CanPickup()+" rimHeld="+rim.P.pickedUp+" rimStored="+(rim.P.physlock!=null)+" tyreGumi="+(tyre.P.gumi!=null)+" slotPart="+(slot.part()==tyre.attachable)+" slotPartSave="+(slot.part().tosave!=null)+" slotPartP="+(slot.part().tosave!=null && slot.part().tosave.P!=null)+" slotPartPIsTyre="+(slot.part().tosave!=null && slot.part().tosave.P==tyre.P)+" ownerIsRim="+(slot.tosaveitem!=null && slot.tosaveitem.P==rim.P)+" wheelSlotMatches="+(rim.P.wheel.gumi==slot));
                    bool blocked=StableGameBridge.WheelPickupMountBlocked(player,selected);
                    bool pickupEligible=selected!=null && !blocked && selected.CanPickup() && !selected.cantPickupWhileMount && player.CanPickupSit(selected);
                    bool pickupIcon=pickupEligible && !player.BcanMount;
                    bool detachIcon=player.BcanDetach || player.BcanMount;
                    Debug.Log("[wheel-probe] UI tyre="+id+" pickup="+pickupIcon+" detach="+detachIcon+" blocked="+blocked);
                    camera.transform.position=new Vector3(2,10000,.27f);player.BcanMount=true;player.BcanDetach=false;
                    var side=StableGameBridge.ResolveWheelPickup(collider,player);
                    bool sideBlocked=StableGameBridge.WheelPickupMountBlocked(player,side);
                    Debug.Log("[wheel-probe] visible-side tyre="+id+" selected="+(side==null?"null":side.name)+" expected="+tyre.P.name+" dismount="+sideBlocked);
                    UnityEngine.Object.Destroy(controllerObject);
                    if(selected!=rim.P || !pickupIcon || detachIcon || side!=tyre.P || !sideBlocked)Debug.LogError("[wheel-probe] FAIL tyre="+id);else Debug.Log("[wheel-probe] PASS tyre="+id);
                }
                catch(Exception e){Debug.LogError("[wheel-probe] "+e);}
                finally{if(tg!=null)UnityEngine.Object.Destroy(tg);if(rg!=null)UnityEngine.Object.Destroy(rg);}
                yield return null;
            }
            Application.Quit();
        }
    }

}
