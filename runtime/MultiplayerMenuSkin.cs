using System;
using System.Collections.Generic;
using GeneszSteamP2PBase;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TldMp
{
    // Restyles the Steam multiplayer menu to the native menus at runtime: plank
    // buttons with native lettering, the paper panel, dark text, native checkboxes,
    // slider and input boxes, and no debug readouts. Only looks change. Handlers,
    // text content, sizes and positions are never touched (MenuLayout still lays
    // the window out). Native looks are sampled from the main menu, the settings
    // Misc tab and its first slider and toggle. Off with TLD_MP_MENU_SKIN=0.
    public sealed class MultiplayerMenuSkin : MonoBehaviour
    {
        const string O = "UIOVERLAY/2";
        const string MENU = O + "/Menu";
        const string SET = O + "/SettingsPrefab";
        // Paths below are relative to the multiplayer menu root (networkingScript.s.menu).
        const string MPW = "GameObject/Background";
        const string MPIN = MPW + "/GameObject";
        const string HEADER = MPW + "/Buttons";
        const string MAIN = MENU + "/MainMenu/MainStuff";
        static readonly string[] Popups =
        {
            "GameObject/PasswordWindow", "GameObject/WrongPasswordWindow",
            "GameObject/PlayerWindow", "GameObject/Roadworthy join failure",
        };
        const float RetryFrames = 30f;

        bool off;
        int retry;
        SteamP2PMenuHandlerScript installed;
        Natives natives;
        int lastSig;
        // Components already restyled, by instance id. Rows and messages are new
        // instances, so each one is styled once when it appears.
        readonly HashSet<int> done = new HashSet<int>();
        readonly List<Transform> watch = new List<Transform>();
        readonly List<GameObject> debugObjects = new List<GameObject>();
        bool loggedDebug;

        void Awake()
        {
            if (Environment.GetEnvironmentVariable("TLD_MP_MENU_SKIN") == "0") { off = true; enabled = false; }
        }

        void Update()
        {
            if (off) return;
            try { Tick(); }
            catch (Exception e)
            {
                off = true; enabled = false;
                Debug.Log("[TLD MP menu skin] stopped, native look kept: " + e.GetType().Name + " " + e.Message + "\n" + e.StackTrace);
            }
        }

        void Tick()
        {
            var s = networkingScript.s;
            SteamP2PMenuHandlerScript menu = s == null ? null : s.menu;
            if (menu == null) return;
            if (!ReferenceEquals(installed, menu))
            {
                if (retry-- > 0) return;
                retry = (int)RetryFrames;
                Natives found = Natives.Sample(MenuCanvas());
                if (found == null) return;
                natives = found;
                Install(menu);
                installed = menu;
                return;
            }
            // Debug readouts come back when the game re-enables them; hide them again.
            for (int i = 0; i < debugObjects.Count; i++)
            {
                GameObject d = debugObjects[i];
                if (d != null && d.activeSelf) d.SetActive(false);
            }
            // New rows, messages and popups: restyle the menu once their parent changes.
            int sig = Signature();
            if (sig != lastSig)
            {
                Restyle(menu);
                lastSig = Signature();
            }
        }

        static Transform MenuCanvas()
        {
            if (menuhandler.s == null) return null;
            return menuhandler.s.transform.parent;
        }

        void Install(SteamP2PMenuHandlerScript menu)
        {
            if (done.Count > 4000) done.Clear();
            debugObjects.Clear();
            Transform root = menu.transform;
            Transform mpin = root.Find(MPIN);
            if (mpin != null)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (MultiplayerMenuSkinPolicy.ShouldHideDebug(t.name) && t.IsChildOf(mpin)) debugObjects.Add(t.gameObject);
            }
            LogNotice(menu, "before");
            Restyle(menu);
            LogNotice(menu, "after");
            lastSig = Signature();
            if (!loggedDebug)
            {
                loggedDebug = true;
                Debug.Log("[TLD MP menu skin] skinned: " + lastCounts.ToString() + " debug hidden=" + debugObjects.Count +
                    " native checkbox=" + natives.HasCheck + " slider=" + natives.HasSlider + " input=" + natives.HasInput);
            }
        }

        Counts lastCounts = new Counts();

        // Restyles the whole menu and its prefab rows. Idempotent: components already
        // styled are skipped by instance id.
        void Restyle(SteamP2PMenuHandlerScript menu)
        {
            Transform root = menu.transform;
            watch.Clear();
            watch.Add(root);
            Counts c = new Counts();
            Transform mpw = root.Find(MPW);
            if (mpw != null) watch.Add(mpw);
            Transform mpin = root.Find(MPIN);
            if (mpin != null) watch.Add(mpin);
            if (menu.lobbyListContent != null) watch.Add(menu.lobbyListContent);
            if (menu.playerListContent != null) watch.Add(menu.playerListContent);

            // The main window, its header bar and the popups sit on the paper panel.
            if (mpw != null) Paper(mpw.GetComponent<Image>(), c);
            if (mpw != null)
            {
                Transform header = mpw.Find("Buttons");
                Image headerImage = header == null ? null : header.GetComponent<Image>();
                if (headerImage != null)
                {
                    Color hc = headerImage.color;
                    hc.a = 0f;
                    headerImage.color = hc;
                }
            }
            // The game repaints the header bar every frame from these two colours.
            if (menu.IFocus != null) { Color fc = menu.IFocus.color; fc.a = 0f; menu.IFocus.color = fc; }
            menu.CFocused = Transparent(menu.CFocused);
            menu.CDeFocused = Transparent(menu.CDeFocused);
            foreach (string p in Popups)
            {
                Transform popup = root.Find(p);
                if (popup != null) { Paper(popup.GetComponent<Image>(), c); c.popups++; }
            }
            // The join failure text is wrapped and centred inside its paper box.
            Transform failure = root.Find("GameObject/Roadworthy join failure");
            if (failure != null) FitJoinFailure(failure);
            // The header gear uses the dark gear of the main menu.
            Transform gearIcon = mpw == null ? null : mpw.Find("Buttons/ButtonSettings/Image");
            Image gearImage = gearIcon == null ? null : gearIcon.GetComponent<Image>();
            if (gearImage != null && natives.Gear != null) Apply(gearImage, natives.Gear);

            // Only the main window and the popups are restyled. The new-message, unread and
            // push-to-talk indicators are drawn over the game world and keep their native look.
            if (mpw != null) SkinTree(mpw, c);
            foreach (string p in Popups)
            {
                Transform popup = root.Find(p);
                if (popup != null) SkinTree(popup, c);
            }
            if (menu.prefabLobbyButton != null) SkinTree(menu.prefabLobbyButton.transform, c);
            if (menu.prefabPlayerButton != null) SkinTree(menu.prefabPlayerButton.transform, c);
            lastCounts = c;
        }

        // Styles every control and text under `top` that has not been styled yet.
        void SkinTree(Transform top, Counts c)
        {
            foreach (Button b in top.GetComponentsInChildren<Button>(true))
            {
                if (!done.Add(b.GetInstanceID())) continue;
                Image img = b.targetGraphic as Image;
                if (img == null) img = b.GetComponent<Image>();
                if (img != null) Apply(img, natives.Plank);
                natives.PlankState.Apply(b);
                c.buttons++;
            }
            foreach (Toggle t in top.GetComponentsInChildren<Toggle>(true))
            {
                if (!done.Add(t.GetInstanceID())) continue;
                if (natives.HasCheck)
                {
                    Image box = t.targetGraphic as Image;
                    if (box != null) Apply(box, natives.Checkbox);
                    Image mark = t.graphic as Image;
                    if (mark != null) Apply(mark, natives.CheckMark);
                    natives.CheckState.Apply(t);
                }
                c.toggles++;
            }
            foreach (Slider s in top.GetComponentsInChildren<Slider>(true))
            {
                if (!done.Add(s.GetInstanceID())) continue;
                if (natives.HasSlider)
                {
                    Transform groove = s.transform.Find("Background");
                    Image grooveImage = groove == null ? null : groove.GetComponent<Image>();
                    if (grooveImage != null) Apply(grooveImage, natives.SliderGroove);
                    Image fill = s.fillRect == null ? null : s.fillRect.GetComponent<Image>();
                    if (fill != null) Apply(fill, natives.SliderFill);
                    Image handle = s.handleRect == null ? null : s.handleRect.GetComponent<Image>();
                    if (handle != null) Apply(handle, natives.SliderHandle);
                }
                c.sliders++;
            }
            foreach (TMP_InputField f in top.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (!done.Add(f.GetInstanceID())) continue;
                if (natives.HasInput)
                {
                    Image box = f.targetGraphic as Image;
                    if (box == null) box = f.GetComponent<Image>();
                    if (box != null) Apply(box, natives.InputBox);
                    natives.InputState.Apply(f);
                }
                c.inputs++;
            }
            foreach (ScrollRect sr in top.GetComponentsInChildren<ScrollRect>(true))
            {
                if (sr.content != null) watch.Add(sr.content);
                if (!done.Add(sr.GetInstanceID())) continue;
                Image backing = sr.GetComponent<Image>();
                if (backing != null && backing.color.a > 0.05f)
                {
                    Color tint = new Color(0f, 0f, 0f, MultiplayerMenuSkinPolicy.PanelTintAlpha);
                    backing.color = tint;
                }
                c.scrolls++;
            }
            // A mask shows its viewport's content; the viewport image itself is hidden.
            foreach (Mask m in top.GetComponentsInChildren<Mask>(true))
            {
                if (!done.Add(m.GetInstanceID())) continue;
                m.showMaskGraphic = false;
            }
            foreach (Image img in top.GetComponentsInChildren<Image>(true))
            {
                if (!done.Add(img.GetInstanceID())) continue;
                // Controls were styled above; icons keep their art.
                if (Ancestor<Selectable>(img.transform) != null) continue;
                // Lobby backings sit under the paper; keep them almost clear.
                if (HasAncestorNamed(img.transform, "Lobby"))
                {
                    img.color = new Color(0f, 0f, 0f, MultiplayerMenuSkinPolicy.LobbyPanelAlpha);
                    c.panels++;
                    continue;
                }
                if (img.sprite != null) continue;
                Color ic = img.color;
                if (MultiplayerMenuSkinPolicy.IsDarkPanel(ic.r, ic.g, ic.b, ic.a))
                {
                    img.color = new Color(0f, 0f, 0f, MultiplayerMenuSkinPolicy.PanelTintAlpha);
                    c.panels++;
                }
            }
            foreach (TMP_Text t in top.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!done.Add(t.GetInstanceID())) continue;
                SkinText(t, c);
            }
        }

        void SkinText(TMP_Text t, Counts c)
        {
            if (Ancestor<Button>(t.transform) != null)
            {
                // Button captions take the native plank lettering, with its own colour.
                ApplyFont(t, natives.ButtonText);
                t.color = natives.ButtonText.Color;
                if (!t.enableAutoSizing)
                {
                    float size = t.fontSize;
                    t.enableAutoSizing = true;
                    t.fontSizeMax = size;
                    t.fontSizeMin = MultiplayerMenuSkinPolicy.AutoSizeMin(size);
                }
                c.labels++;
                return;
            }
            TMP_InputField field = Ancestor<TMP_InputField>(t.transform);
            if (field != null && field.placeholder != null && (Object)field.placeholder == (Object)t)
            {
                ApplyFont(t, natives.PanelText);
                Color pc = natives.PanelText.Color;
                t.color = new Color(pc.r, pc.g, pc.b, MultiplayerMenuSkinPolicy.PlaceholderAlpha(pc.a));
                c.texts++;
                return;
            }
            // Panel and input text: native font, dark unless the text has its own colour.
            ApplyFont(t, natives.PanelText);
            Color own = t.color;
            if (!MultiplayerMenuSkinPolicy.KeepsOwnColor(own.r, own.g, own.b)) t.color = natives.PanelText.Color;
            c.texts++;
        }

        // Logs the colour of the notification texts, which must keep their native look.
        static void LogNotice(SteamP2PMenuHandlerScript menu, string when)
        {
            GameObject[] notices = { menu.GMessage, menu.GChatRed, menu.GMicrophone };
            string[] names = { "GMessage", "GChatRed", "GMicrophone" };
            var sb = new System.Text.StringBuilder("[TLD MP menu skin] notice text " + when + ":");
            for (int i = 0; i < notices.Length; i++)
            {
                if (notices[i] == null) { sb.Append(" " + names[i] + "=none"); continue; }
                TMP_Text[] texts = notices[i].GetComponentsInChildren<TMP_Text>(true);
                for (int k = 0; k < texts.Length; k++)
                {
                    Color c = texts[k].color;
                    sb.Append(" " + names[i] + "/" + texts[k].name + "=(" + c.r.ToString("F2") + "," + c.g.ToString("F2") + "," + c.b.ToString("F2") + "," + c.a.ToString("F2") + ")");
                }
            }
            Debug.Log(sb.ToString());
        }

        static bool HasAncestorNamed(Transform t, string name)
        {
            for (; t != null; t = t.parent)
                if (t.name == name) return true;
            return false;
        }

        // Wraps the join failure text and keeps it inside the paper box, centred.
        static void FitJoinFailure(Transform popup)
        {
            Transform labelT = popup.Find("TextWrong");
            TMP_Text label = labelT == null ? null : labelT.GetComponent<TMP_Text>();
            if (label == null) return;
            label.enableWordWrapping = true;
            RectTransform rt = label.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(24f, 8f);
            rt.offsetMax = new Vector2(-24f, -8f);
            label.alignment = TextAlignmentOptions.Center;
            if (!label.enableAutoSizing)
            {
                float size = label.fontSize;
                label.enableAutoSizing = true;
                label.fontSizeMax = size;
                label.fontSizeMin = size * 0.6f;
            }
        }

        static Color Transparent(Color c)
        {
            c.a = 0f;
            return c;
        }

        // The nearest component of type T on `t` or a parent. Unlike GetComponentInParent this
        // also works while the multiplayer window is hidden, when the first styling pass runs.
        static T Ancestor<T>(Transform t) where T : Component
        {
            for (; t != null; t = t.parent)
            {
                T found = t.GetComponent<T>();
                if (found != null) return found;
            }
            return null;
        }

        static void ApplyFont(TMP_Text t, TextLook look)
        {
            if (look.Font != null && t.font != look.Font) t.font = look.Font;
            if (look.Material != null && t.fontSharedMaterial != look.Material) t.fontSharedMaterial = look.Material;
            t.fontStyle = look.Style;
            t.enableVertexGradient = look.Gradient;
            t.colorGradient = look.GradientColors;
        }

        void Paper(Image img, Counts c)
        {
            if (img == null) return;
            Apply(img, natives.Paper);
            c.panels++;
        }

        static void Apply(Image img, ImageLook look)
        {
            img.sprite = look.Sprite;
            img.type = look.Type;
            img.color = look.Color;
            img.pixelsPerUnitMultiplier = look.PixelsPerUnit;
            img.fillCenter = look.FillCenter;
        }

        // Cheap change check: child counts of the menu, its window, the list contents.
        int Signature()
        {
            int sig = 0;
            for (int i = 0; i < watch.Count; i++)
                sig = unchecked(sig * 31 + (watch[i] == null ? -1 : watch[i].childCount));
            return sig;
        }

        sealed class Counts
        {
            public int buttons, labels, texts, toggles, sliders, inputs, scrolls, popups, panels;
            public override string ToString()
            {
                return "buttons=" + buttons + " labels=" + labels + " texts=" + texts + " toggles=" + toggles +
                    " sliders=" + sliders + " inputs=" + inputs + " scrollviews=" + scrolls + " popups=" + popups +
                    " panels=" + panels;
            }
        }

        // Native looks, copied out of the game's menus so they stay valid when a
        // prefab is rebuilt. Plank, paper and panel text are required; the rest are
        // optional and keep the multiplayer look when missing.
        sealed class Natives
        {
            public ImageLook Paper, Plank, Checkbox, CheckMark, SliderGroove, SliderFill, SliderHandle, InputBox, Gear;
            public SelectableLook PlankState, CheckState, InputState;
            public TextLook ButtonText, PanelText;
            public bool HasCheck, HasSlider, HasInput;

            public static Natives Sample(Transform canvas)
            {
                if (canvas == null) return null;
                var settings = settingsscript.s;
                if (settings == null || settings.tabMisc == null) return null;

                Transform paperT = canvas.Find(SET + "/NewSettings/TabMisc/BG");
                Image paperImage = paperT == null ? null : paperT.GetComponent<Image>();
                Transform plankT = canvas.Find(MENU + "/DisappearWhenExit/ButtonMultiplayer");
                Button plank = plankT == null ? null : plankT.GetComponent<Button>();
                Image plankImage = plank == null ? null : plank.GetComponent<Image>();
                TMP_Text plankLabel = plankT == null ? null : plankT.GetComponentInChildren<TMP_Text>(true);
                TMP_Text panelLabel = FirstPanelText(paperT == null ? null : paperT.parent);
                if (paperImage == null || plankImage == null || plankLabel == null || panelLabel == null) return null;

                var n = new Natives();
                n.Paper = ImageLook.Of(paperImage);
                n.Plank = ImageLook.Of(plankImage);
                n.PlankState = SelectableLook.Of(plank);
                n.ButtonText = TextLook.Of(plankLabel);
                n.PanelText = TextLook.Of(panelLabel);
                // Panel and chat text must read as dark ink on the paper, whatever the native label colour.
                Color ink = n.PanelText.Color;
                if (MultiplayerMenuSkinPolicy.IsLight(ink.r, ink.g, ink.b)) n.PanelText.Color = new Color(0.16f, 0.14f, 0.12f, 1f);

                // The dark gear on the main menu's START row: its icon child, not the plank.
                Transform gearButton = canvas.Find(MAIN + "/DisappearWhenExit/ButtonStart/ButtonGameModeSettings");
                Transform gearIcon = gearButton == null ? null : gearButton.Find("Image");
                Image gearImage = gearIcon == null ? null : gearIcon.GetComponent<Image>();
                if (gearImage != null) n.Gear = ImageLook.Of(gearImage);

                var toggles = settings.tabMisc.toggles;
                if (toggles != null && toggles.Length > 0 && toggles[0] != null && toggles[0].toggle != null)
                {
                    Toggle t = toggles[0].toggle;
                    Image box = t.targetGraphic as Image;
                    Image mark = t.graphic as Image;
                    if (box != null && mark != null)
                    {
                        n.Checkbox = ImageLook.Of(box);
                        n.CheckMark = ImageLook.Of(mark);
                        n.CheckState = SelectableLook.Of(t);
                        n.HasCheck = true;
                    }
                }

                var sliders = settings.tabMisc.sliders;
                if (sliders != null && sliders.Length > 0 && sliders[0] != null)
                {
                    Slider s = sliders[0].slider;
                    if (s != null)
                    {
                        Transform groove = s.transform.Find("Background");
                        Image grooveImage = groove == null ? null : groove.GetComponent<Image>();
                        Image fill = s.fillRect == null ? null : s.fillRect.GetComponent<Image>();
                        Image handle = s.handleRect == null ? null : s.handleRect.GetComponent<Image>();
                        if (grooveImage != null && fill != null && handle != null)
                        {
                            n.SliderGroove = ImageLook.Of(grooveImage);
                            n.SliderFill = ImageLook.Of(fill);
                            n.SliderHandle = ImageLook.Of(handle);
                            n.HasSlider = true;
                        }
                    }
                    TMP_InputField field = sliders[0].IF;
                    Image box = field == null ? null : field.targetGraphic as Image;
                    if (field != null && box != null)
                    {
                        n.InputBox = ImageLook.Of(box);
                        n.InputState = SelectableLook.Of(field);
                        n.HasInput = true;
                    }
                }
                return n;
            }

            static TMP_Text FirstPanelText(Transform panel)
            {
                if (panel == null) return null;
                foreach (TMP_Text t in panel.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.text.Length == 0) continue;
                    if (Ancestor<Button>(t.transform) != null) continue;
                    if (HasAncestor(t.transform, "Template") || HasAncestor(t.transform, "Item")) continue;
                    return t;
                }
                return null;
            }

            static bool HasAncestor(Transform transform, string name)
            {
                for (Transform t = transform; t != null; t = t.parent)
                    if (t.name == name) return true;
                return false;
            }
        }

        sealed class ImageLook
        {
            public Sprite Sprite;
            public Image.Type Type;
            public Color Color;
            public float PixelsPerUnit;
            public bool FillCenter;

            public static ImageLook Of(Image image)
            {
                return new ImageLook
                {
                    Sprite = image.sprite,
                    Type = image.type,
                    Color = image.color,
                    PixelsPerUnit = image.pixelsPerUnitMultiplier,
                    FillCenter = image.fillCenter,
                };
            }
        }

        sealed class SelectableLook
        {
            public Selectable.Transition Transition;
            public ColorBlock Colors;
            public SpriteState Sprites;

            public static SelectableLook Of(Selectable s)
            {
                return new SelectableLook { Transition = s.transition, Colors = s.colors, Sprites = s.spriteState };
            }

            public void Apply(Selectable s)
            {
                s.transition = Transition;
                s.colors = Colors;
                s.spriteState = Sprites;
            }
        }

        sealed class TextLook
        {
            public TMP_FontAsset Font;
            public Material Material;
            public Color Color;
            public FontStyles Style;
            public bool Gradient;
            public VertexGradient GradientColors;

            public static TextLook Of(TMP_Text t)
            {
                return new TextLook
                {
                    Font = t.font,
                    Material = t.fontSharedMaterial,
                    Color = t.color,
                    Style = t.fontStyle,
                    Gradient = t.enableVertexGradient,
                    GradientColors = t.colorGradient,
                };
            }
        }
    }
}
