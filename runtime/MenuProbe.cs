using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GeneszSteamP2PBase;
using UnityEngine;

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
    }
}
