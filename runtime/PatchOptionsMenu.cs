using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TldMp
{
    // Adds a Roadworthy tab to the settings menu. The tab is a clone of the native
    // Misc tab (same panel, scroll view and reset button) with its native rows
    // removed, and its tab button is a clone of the native Misc button. The
    // Shadow quality and Performance preset dropdowns and every Roadworthy toggle
    // live in that tab. The native UI is prefab-driven, so the rows are cloned from
    // existing native rows and keep their look. If the hierarchy is not what is
    // expected, the rows go back into Graphics and Misc (the previous placement) so
    // settings are never lost; that path logs one warning. Everything is defensive.
    [DefaultExecutionOrder(20001)]
    public sealed class PatchOptionsMenu : MonoBehaviour
    {
        static readonly string[] PresetNames = { "Vanilla", "Balanced", "Performance", "Custom" };
        static readonly string[] ShadowNames = { "Off", "Low", "Medium", "High" };
        const string PresetRowName = "TldPerformancePreset";
        const string ShadowRowName = "TldShadowQuality";
        const string RoadTabName = "TabRoadworthy";
        const string RoadButtonName = "ButtonRoadworthy";
        const string LedOn = "ImageLedOn";
        const string LedOff = "ImageLedOff";
        // Native settings tabs: "Tab" + name is the panel, "Button" + name its tab button.
        static readonly string[] NativeTabs = { "Graphics", "Controls", "SteeringWheel", "Misc", "Player" };

        bool installed, failed;
        settingsscript installedSettings;
        int activeFrames;
        bool wasSettingsOpen;
        dropdownsetting presetRow, shadowRow;
        togglesetting smoothRow;
        const string SmoothRowName = "TldCarSmoothingToggle";
        togglesetting experimentalRow;
        const string ExperimentalRowName = "TldExperimentalMultiplayerToggle";
        // Every Roadworthy toggle and how to read its current value, for re-syncing.
        readonly List<togglesetting> toggleRows = new List<togglesetting>();
        readonly List<Func<bool>> toggleReads = new List<Func<bool>>();

        // Roadworthy tab state; all null when the rows live in the native tabs.
        Transform roadTab, roadButton, roadContainer;
        // Roadworthy scroll content and its scroll view, to return to the top on open.
        RectTransform roadContent;
        ScrollRect roadScroll;
        // Gap above the first Roadworthy row.
        const float TopPad = 8f;
        Transform[] nativeButtons;
        // LED child states [on, off] for a selected and an idle tab button. Sampled
        // from the native buttons at runtime so the Roadworthy button matches them.
        readonly bool[] ledSelected = { true, false };
        readonly bool[] ledIdle = { false, true };

        // The Roadworthy tab's rows, kept apart from the native tab arrays.
        sealed class RoadTab
        {
            public readonly settingsscript.tab Tab = new settingsscript.tab();
            public RectTransform Content;
            public float FirstTop;
        }

        void Update()
        {
            var settings = settingsscript.s;
            // This driver survives scene loads; the native settings prefab does
            // not. Install once per prefab, including returns to the main menu.
            if (!ReferenceEquals(installedSettings, settings))
            {
                installedSettings = settings;
                installed = failed = false;
                activeFrames = 0;
                wasSettingsOpen = false;
                ResetRowReferences();
                roadTab = roadButton = roadContainer = null;
                roadContent = null;
                roadScroll = null;
                nativeButtons = null;
            }
            // The backend is re-selected before a peer joins, so the row stays
            // interactive; it only reflects the effective choice.
            if (installed && experimentalRow != null && experimentalRow.toggle != null)
                experimentalRow.toggle.SetIsOnWithoutNotify(ExperimentalEffective());
            if (installed)
            {
                bool open = settings != null && settings.gameObject != null && settings.gameObject.activeInHierarchy;
                if (open != wasSettingsOpen)
                {
                    wasSettingsOpen = open;
                    // Reopening the settings shows the native default tab, never Roadworthy.
                    if (open) SetRoadworthyOpen(false);
                }
                if (open && roadContainer != null && nativeButtons != null) SampleLeds();
            }
            if (installed || failed) return;
            if (settings == null || settings.gameObject == null || !settings.gameObject.activeInHierarchy)
            {
                activeFrames = 0;
                return;
            }
            // MenuLayout settles the panel and its rows ten frames after a change.
            if (activeFrames++ < 10) return;
            try
            {
                Install(settings);
                installed = true;
            }
            catch (Exception error)
            {
                failed = true;
                Debug.LogWarning("[TLD MP options] menu rows not installed: " + error.GetType().Name + " " + error.Message);
            }
        }

        void Install(settingsscript settings)
        {
            settingsscript.tab graphics = settings.tabGraphics;
            if (graphics == null || graphics.dropdrops == null || graphics.dropdrops.Length == 0 || graphics.dropdrops[0] == null)
                throw new InvalidOperationException("Graphics tab or its first dropdown is missing.");
            settingsscript.tab misc = settings.tabMisc;
            if (misc == null || misc.toggles == null || misc.toggles.Length == 0 || misc.toggles[0] == null)
                throw new InvalidOperationException("Misc tab or its first toggle is missing.");
            dropdownsetting template = graphics.dropdrops[0];
            togglesetting miscTemplate = misc.toggles[0];
            RectTransform graphicsContent = template.transform.parent as RectTransform;
            RectTransform miscContent = miscTemplate.transform.parent as RectTransform;
            if (graphicsContent == null || miscContent == null)
                throw new InvalidOperationException("Settings rows are not inside a RectTransform content list.");

            bool inRoadTab = false;
            try
            {
                RoadTab road = CreateRoadTab(settings, miscTemplate);
                InstallDropdowns(road.Tab, road.Content, template, miscTemplate, road.FirstTop);
                InstallToggles(road.Tab, road.Content, miscTemplate, road.FirstTop);
                LogRoadRows(road.Content);
                inRoadTab = true;
            }
            catch (Exception error)
            {
                Debug.LogWarning("[TLD MP options] Roadworthy tab not used, rows stay in Graphics and Misc: " +
                    error.GetType().Name + " " + error.Message);
                DropRoadTab();
                ResetRowReferences();
            }

            if (inRoadTab)
            {
                HookNativeTabs();
            }
            else
            {
                InstallDropdowns(graphics, graphicsContent, template, null, 0f);
                try { InstallToggles(misc, miscContent, miscTemplate, 0f); }
                catch (Exception error) { Debug.LogWarning("[TLD MP options] toggles not installed: " + error.GetType().Name + " " + error.Message); }
            }
            PatchOptions.Load();
            Sync();
            Debug.Log("[TLD MP options] functions added preset= " + PatchOptions.PresetName(PatchOptions.Preset) +
                " shadow=" + PatchOptions.ShadowName(PatchOptions.Shadow) + " tab=" + (inRoadTab ? RoadTabName : "Graphics/Misc") +
                " options=" + PatchOptions.DefaultPath());
        }

        // Clones the native Misc panel and its tab button as Roadworthy, strips the
        // native rows from the panel copy and repoints its reset button. Nothing in the
        // native tab arrays changes. Any failure removes the copies and throws.
        RoadTab CreateRoadTab(settingsscript settings, togglesetting miscTemplate)
        {
            Transform miscRoot = AncestorNamed(miscTemplate.transform, "TabMisc");
            if (miscRoot == null) throw new InvalidOperationException("Misc rows are not inside TabMisc.");
            Transform container = miscRoot.parent;
            if (container == null || container.Find(RoadTabName) != null || container.Find(RoadButtonName) != null)
                throw new InvalidOperationException("Settings container is missing or already has a Roadworthy tab.");
            Transform miscButton = container.Find("ButtonMisc");
            if (miscButton == null) throw new InvalidOperationException("Misc tab button is missing.");
            Transform playerButton = container.Find("ButtonPlayer");
            RectTransform miscContent = miscTemplate.transform.parent as RectTransform;
            if (miscContent == null) throw new InvalidOperationException("Misc rows have no content list.");

            var road = new RoadTab();
            // Roadworthy rows start just below the top of the copied content. The native
            // Misc first row sits far down that list, which used to leave a large gap.
            road.FirstTop = -TopPad;
            GameObject tabCopy = null, buttonCopy = null;
            try
            {
                tabCopy = Object.Instantiate(miscRoot.gameObject);
                tabCopy.name = RoadTabName;
                tabCopy.transform.SetParent(container, false);
                tabCopy.transform.SetSiblingIndex(miscRoot.GetSiblingIndex() + 1);
                tabCopy.SetActive(false);
                DisableLocalizers(tabCopy);

                road.Content = SameSpot(miscContent, miscRoot, tabCopy.transform) as RectTransform;
                if (road.Content == null) throw new InvalidOperationException("Misc content list has no copy.");
                // Every native child of the content goes (setting rows, labels, input
                // fields, buttons); Roadworthy rows take their place. Each is hidden
                // before it is destroyed, so the row lookups that follow never see it.
                for (int i = road.Content.childCount - 1; i >= 0; i--)
                {
                    GameObject child = road.Content.GetChild(i).gameObject;
                    child.SetActive(false);
                    Object.Destroy(child);
                }

                Transform bg = tabCopy.transform.Find("BG");
                Transform resetObject = bg == null ? null : bg.Find("ButtonReset");
                Button reset = resetObject == null ? null : resetObject.GetComponent<Button>();
                if (reset == null) throw new InvalidOperationException("Misc panel has no reset button.");
                reset.onClick = new Button.ButtonClickedEvent();
                reset.onClick.AddListener(ResetRoadworthy);
                Transform keys = bg.Find("ButtonResetKeys");
                if (keys != null) keys.gameObject.SetActive(false);
                TMP_Text title = SetText(bg, "Roadworthy", true);
                if (title == null)
                    Debug.LogWarning("[TLD MP options] Roadworthy panel has no title label; it keeps the Misc title.");
                // Other native widgets on the panel (for example the custom radio path)
                // are hidden; the scroll view, title and reset button stay.
                HideNativeChrome(tabCopy.transform, road.Content, title == null ? null : title.transform, resetObject);

                buttonCopy = Object.Instantiate(miscButton.gameObject);
                buttonCopy.name = RoadButtonName;
                buttonCopy.transform.SetParent(container, false);
                buttonCopy.transform.SetSiblingIndex((playerButton != null ? playerButton : miscButton).GetSiblingIndex() + 1);
                PlaceBelow(buttonCopy.transform as RectTransform, miscButton as RectTransform, playerButton as RectTransform);
                DisableLocalizers(buttonCopy);
                if (SetText(buttonCopy.transform, "Roadworthy", false) == null)
                    throw new InvalidOperationException("Misc tab button has no label.");
                Button button = buttonCopy.GetComponent<Button>();
                if (button == null) throw new InvalidOperationException("Misc tab button has no Button.");
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(OpenRoadworthy);

                roadTab = tabCopy.transform;
                roadButton = buttonCopy.transform;
                roadContainer = container;
                roadContent = road.Content;
                roadScroll = ScrollAbove(road.Content, tabCopy.transform);
                nativeButtons = new Transform[NativeTabs.Length];
                for (int i = 0; i < NativeTabs.Length; i++) nativeButtons[i] = container.Find("Button" + NativeTabs[i]);
                return road;
            }
            catch
            {
                if (buttonCopy != null) Object.Destroy(buttonCopy);
                if (tabCopy != null) Object.Destroy(tabCopy);
                throw;
            }
        }

        // The copied button starts exactly on top of Misc, which hid and stole Misc's
        // clicks until MenuLayout ran again. Put it one native step below the last
        // native button (Player), then ask MenuLayout to lay the column out again.
        static void PlaceBelow(RectTransform copy, RectTransform misc, RectTransform player)
        {
            if (copy == null || misc == null) return;
            RectTransform last = player != null ? player : misc;
            Vector2 step = player != null ? player.anchoredPosition - misc.anchoredPosition : Vector2.zero;
            if (step.sqrMagnitude < 1f) step = new Vector2(0f, -misc.rect.height);
            copy.anchoredPosition = last.anchoredPosition + step;
            MenuLayout.Rescan();
        }

        // Shows the Roadworthy panel, hides the native ones and lights its own button.
        void OpenRoadworthy()
        {
            settingsscript settings = settingsscript.s;
            if (settings == null || roadTab == null) return;
            settings.PressedTab(99);   // closes all five native tabs
            for (int i = 0; i < nativeButtons.Length; i++) ApplyLed(nativeButtons[i], false);
            SetRoadworthyOpen(true);
        }

        // A native tab was clicked: its own handler opens the native panel; Roadworthy closes.
        void SelectNative(int index)
        {
            SetRoadworthyOpen(false);
            for (int i = 0; nativeButtons != null && i < nativeButtons.Length; i++) ApplyLed(nativeButtons[i], i == index);
        }

        void HookNativeTabs()
        {
            for (int i = 0; i < nativeButtons.Length; i++)
            {
                Button button = nativeButtons[i] == null ? null : nativeButtons[i].GetComponent<Button>();
                if (button == null) continue;
                int index = i;
                button.onClick.AddListener(delegate { SelectNative(index); });
            }
        }

        void SetRoadworthyOpen(bool open)
        {
            if (roadTab != null) roadTab.gameObject.SetActive(open);
            if (open) ScrollRoadToTop();
            ApplyLed(roadButton, open);
        }

        // Opening Roadworthy shows its first rows, not wherever the list was left.
        void ScrollRoadToTop()
        {
            if (roadContent != null) roadContent.anchoredPosition = new Vector2(roadContent.anchoredPosition.x, 0f);
            if (roadScroll != null) roadScroll.verticalNormalizedPosition = 1f;
        }

        // Reads the LED children of the native buttons: the selected one from the
        // open native panel, the idle ones from the rest.
        void SampleLeds()
        {
            for (int i = 0; i < NativeTabs.Length; i++)
            {
                Transform button = nativeButtons[i];
                Transform panel = roadContainer.Find("Tab" + NativeTabs[i]);
                if (button == null || panel == null) continue;
                Transform on = button.Find(LedOn), off = button.Find(LedOff);
                if (on == null || off == null) continue;
                bool[] pattern = panel.gameObject.activeInHierarchy ? ledSelected : ledIdle;
                pattern[0] = on.gameObject.activeSelf;
                pattern[1] = off.gameObject.activeSelf;
            }
        }

        void ApplyLed(Transform button, bool selected)
        {
            if (button == null) return;
            Transform on = button.Find(LedOn), off = button.Find(LedOff);
            if (on == null || off == null) return;
            bool[] pattern = selected ? ledSelected : ledIdle;
            on.gameObject.SetActive(pattern[0]);
            off.gameObject.SetActive(pattern[1]);
        }

        // Reset on the Roadworthy panel: defaults for every Roadworthy option.
        void ResetRoadworthy()
        {
            PatchOptions.Load();
            PatchOptions.ResetToDefaults();
            PatchOptions.Save();
            StableGameBridge.ApplyGraphicsPreset();
            Sync();
        }

        // Removes the Roadworthy copies (used when the rows cannot be installed there).
        void DropRoadTab()
        {
            if (roadTab != null) Object.Destroy(roadTab.gameObject);
            if (roadButton != null) Object.Destroy(roadButton.gameObject);
            roadTab = roadButton = roadContainer = null;
            roadContent = null;
            roadScroll = null;
            nativeButtons = null;
        }

        void ResetRowReferences()
        {
            presetRow = shadowRow = null;
            smoothRow = null;
            experimentalRow = null;
            toggleRows.Clear();
            toggleReads.Clear();
        }

        // Shadow quality and Performance preset rows, appended under the last row of
        // `content`. When `columnFrom` (a toggle row in another tab) is given, the column's
        // left edge comes from that toggle row, while its width and row heights come from
        // the dropdown template.
        static bool loggedColumnWidthFallback;
        void InstallDropdowns(settingsscript.tab tab, RectTransform content, dropdownsetting template, togglesetting columnFrom, float startTop)
        {
            Transform root = content;
            RectTransform templateRect = template.transform as RectTransform;
            if (templateRect == null) throw new InvalidOperationException("Dropdown row is not a RectTransform.");
            Rect templateSpace = RowRect(templateRect);
            Rect column = templateSpace;
            if (columnFrom != null)
            {
                Rect other = RowRect((RectTransform)columnFrom.transform);
                float columnWidth = templateSpace.width;
                if (columnWidth <= 1f)
                {
                    columnWidth = Math.Max(200f, content.rect.width - 2f * other.xMin);
                    if (!loggedColumnWidthFallback)
                    {
                        loggedColumnWidthFallback = true;
                        Debug.Log("[TLD MP options] dropdown template width " + F(templateSpace.width) +
                            " is not usable; using content width fallback " + F(columnWidth));
                    }
                }
                column = new Rect(other.xMin, templateSpace.yMin, columnWidth, templateSpace.height);
            }

            // Rows live in a scrollable content list. Append under every existing
            // row and grow the content so the scroll view can reach them.
            float gap = 8f;
            float bottom;
            float top = RowsBottom(root, out bottom) ? bottom - gap : startTop;
            float left = column.xMin;
            float width = column.width;
            float height = column.height;
            float labelMin = 0f, labelMax = 0f;
            if (columnFrom != null)
            {
                // The template label sits left of the dropdown box, outside the row rect.
                // At x=0 the scroll mask clips it, so move the column right by the overhang.
                Rect label = TemplateLabelRect(template);
                labelMin = label.xMin;
                labelMax = label.xMax;
                left = labelMin < 0f ? column.xMin + (-labelMin) + 8f : column.xMin;
                if (content.rect.width > 1f && left + width > content.rect.width)
                    width = Math.Max(200f, content.rect.width - left - 8f);
            }
            Debug.Log("[TLD MP options] dropdown rows in " + content.name + " row=" + F(column.width) + "x" + F(column.height) +
                " top=" + F(top) + " columnLeft=" + F(column.xMin) +
                " labelMin=" + F(labelMin) + " labelMax=" + F(labelMax) + " left=" + F(left) + " width=" + F(width));

            if (Find(root, ShadowRowName) == null)
            {
                shadowRow = Clone(template, root, ShadowRowName);
                shadowRow.stringWords = ShadowNames;
                shadowRow.words = new int[0];
                shadowRow.dtype = dropdownsetting.dropdowntype.strings;
                shadowRow.refreshWhenChange = new dropdownsetting[0];
                shadowRow.setting = null;
                shadowRow.settingPlayer = null;
                shadowRow.gsetting = null;
                shadowRow.useDefInt = true;
                shadowRow.defInt = (int)ShadowLevel.Medium;
                Label(shadowRow, "Shadow quality");
                shadowRow.FStart();
                Place((RectTransform)shadowRow.transform, left, top, width, height);
                top -= height + gap;
            }
            else shadowRow = Find<dropdownsetting>(root, ShadowRowName);
            if (Find(root, PresetRowName) == null)
            {
                presetRow = Clone(template, root, PresetRowName);
                presetRow.stringWords = PresetNames;
                presetRow.words = new int[0];
                presetRow.dtype = dropdownsetting.dropdowntype.strings;
                presetRow.refreshWhenChange = new dropdownsetting[0];
                presetRow.setting = null;
                presetRow.settingPlayer = null;
                presetRow.gsetting = null;
                presetRow.useDefInt = true;
                presetRow.defInt = (int)GraphicsPreset.Balanced;
                Label(presetRow, "Performance preset");
                presetRow.FStart();
                Place((RectTransform)presetRow.transform, left, top, width, height);
            }
            else presetRow = Find<dropdownsetting>(root, PresetRowName);
            if (shadowRow == null || presetRow == null) throw new InvalidOperationException("New rows were not created.");
            SizeContent(content, -(top - height) + 24f);
            var scroll = root.parent == null || root.parent.parent == null ? null : root.parent.parent.GetComponent<ScrollRect>();
            bool scrollable = scroll != null && scroll.content == content;
            if (scrollable && !scroll.vertical) scroll.vertical = true;
            bool autoScrolled = scrollable && content != roadContent && template.gameObject.activeInHierarchy;
            if (autoScrolled) scroll.verticalNormalizedPosition = 0f;
            Debug.Log("[TLD MP options] rows placed in " + content.name + " presetBottom=" + F(-(top - height)) +
                " contentHeight=" + F(content.rect.height) +
                " scroll=" + (scroll == null ? "none" : (scroll.enabled ? "on" : "off") + "/vertical=" + scroll.vertical) +
                " contentLinked=" + scrollable + " autoScrolled=" + autoScrolled);

            shadowRow.dropdown.onValueChanged.AddListener(delegate(int index) { OnShadow(index); });
            presetRow.dropdown.onValueChanged.AddListener(delegate(int index) { OnPreset(index); });

            tab.dropdrops = Append(tab.dropdrops, shadowRow, presetRow);
            tab.obj = Append(tab.obj, shadowRow.gameObject, presetRow.gameObject);
        }

        // Car smoothing, the Misc toggles and the experimental row, in order.
        void InstallToggles(settingsscript.tab tab, RectTransform content, togglesetting template, float startTop)
        {
            Transform root = content;
            smoothRow = AddMiscToggle(tab, template, root, content, startTop, SmoothRowName, "Car smoothing",
                delegate { return PatchOptions.SmoothCars; },
                delegate(bool on) { PatchOptions.SmoothCars = on; });
            AddMiscToggle(tab, template, root, content, startTop, RabbitsRowName, "Rabbits (single-player)",
                delegate { return PatchOptions.Rabbits; },
                delegate(bool on) { PatchOptions.Rabbits = on; });
            AddMiscToggle(tab, template, root, content, startTop, SandstormsRowName, "Sandstorms (single-player)",
                delegate { return PatchOptions.Sandstorms; },
                delegate(bool on) { PatchOptions.Sandstorms = on; });
            AddMiscToggle(tab, template, root, content, startTop, HighRateRowName, "Faster multiplayer",
                delegate { return PatchOptions.HighRate; },
                delegate(bool on) { PatchOptions.HighRate = on; });
            AddMiscToggle(tab, template, root, content, startTop, RadiatorRepairRowName, "Repairable radiators",
                delegate { return PatchOptions.RadiatorRepair; },
                delegate(bool on) { PatchOptions.RadiatorRepair = on; });
            AddMiscToggle(tab, template, root, content, startTop, DrunkEffectsRowName, "Drunk effects",
                delegate { return PatchOptions.DrunkEffects; },
                delegate(bool on) { PatchOptions.DrunkEffects = on; });
            experimentalRow = AddMiscToggle(tab, template, root, content, startTop, ExperimentalRowName,
                "New Buggy multiplayer", ExperimentalEffective,
                delegate(bool on)
                {
                    PatchOptions.ExperimentalMultiplayer = on;
                    if (Environment.GetEnvironmentVariable("TLD_MP_EXPERIMENTAL") == "1")
                        Debug.LogWarning("[TLD MP experimental] TLD_MP_EXPERIMENTAL=1 overrides the menu choice for this process.");
                    else if (StableGameBridge.InSession)
                        Debug.Log("[TLD MP experimental] Backend preference set to " + (on ? "experimental" : "native") +
                            "; it switches automatically before a peer joins. Experimental enables the external join, item owner gate, player identity gate and breakable authority.");
                });
        }

        // The environment override wins for a development launch. Show that
        // effective value so the row never claims native while experimental runs.
        static bool ExperimentalEffective()
        {
            return PatchOptions.ExperimentalMultiplayer ||
                Environment.GetEnvironmentVariable("TLD_MP_EXPERIMENTAL") == "1";
        }

        const string RabbitsRowName = "TldRabbitsToggle";
        const string SandstormsRowName = "TldSandstormsToggle";
        const string HighRateRowName = "TldHighRateToggle";
        const string RadiatorRepairRowName = "TldRadiatorRepairToggle";
        const string DrunkEffectsRowName = "TldDrunkEffectsToggle";

        // One cloned toggle under the current last row, saved to the options file.
        // `read` gives its value at install and on every re-sync; `apply` stores a change.
        togglesetting AddMiscToggle(settingsscript.tab tab, togglesetting template, Transform root, RectTransform content, float startTop,
            string name, string label, Func<bool> read, Action<bool> apply)
        {
            if (Find(root, name) != null) return Find<togglesetting>(root, name);
            Rect templateSpace = RowRect((RectTransform)template.transform);
            float height = templateSpace.height;
            float bottom;
            float top = RowsBottom(root, out bottom) ? bottom - 8f : startTop;
            togglesetting row = CloneToggle(template, root, name);
            row.settings = null;
            row.childToggles = new togglesetting[0];
            row.dropdown = null;
            row.button = null;
            row.ev = new UnityEvent();
            row.def = true;
            LabelToggle(row, label);
            Place((RectTransform)row.transform, templateSpace.xMin, top, templateSpace.width, height);
            SizeContent(content, -(top - height) + 24f);
            row.toggle.SetIsOnWithoutNotify(read());
            row.toggle.interactable = true;
            row.toggle.onValueChanged.AddListener(delegate(bool on)
            {
                PatchOptions.Load();
                apply(on);
                PatchOptions.Save();
            });
            toggleRows.Add(row);
            toggleReads.Add(read);
            tab.toggles = Append(tab.toggles, row);
            tab.obj = Append(tab.obj, row.gameObject);
            Debug.Log("[TLD MP options] toggle " + label + " placed bottom=" + F(-(top - height)) + " on=" + read());
            return row;
        }

        static togglesetting CloneToggle(togglesetting template, Transform root, string name)
        {
            GameObject copy = Object.Instantiate(template.gameObject);
            copy.name = name;
            copy.transform.SetParent(root, false);
            DisableLocalizers(copy);
            togglesetting row = copy.GetComponent<togglesetting>();
            if (row == null) throw new InvalidOperationException("Cloned row has no togglesetting.");
            return row;
        }

        static void LabelToggle(togglesetting row, string text)
        {
            Transform label = row.transform.Find("ToggleLabel");
            TMP_Text target = label == null ? null : label.GetComponentInChildren<TMP_Text>(true);
            if (target == null)
            {
                TMP_Text[] labels = row.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < labels.Length; i++)
                {
                    if (labels[i] == null || HasAncestor(labels[i].transform, "Template") || HasAncestor(labels[i].transform, "Item")) continue;
                    target = labels[i];
                    break;
                }
            }
            if (target == null) { Debug.LogWarning("[TLD MP options] no toggle label found for " + row.name); return; }
            target.text = text;
        }

        void OnPreset(int index)
        {
            PatchOptions.Load();
            PatchOptions.SelectPreset((GraphicsPreset)index);
            PatchOptions.Save();
            StableGameBridge.ApplyGraphicsPreset();
            Sync();
        }

        void OnShadow(int index)
        {
            PatchOptions.Load();
            PatchOptions.SelectShadow((ShadowLevel)index);
            PatchOptions.Save();
            StableGameBridge.ApplyGraphicsPreset();
            Sync();
        }

        void Sync()
        {
            if (shadowRow != null && shadowRow.dropdown != null && shadowRow.dropdown.options.Count == ShadowNames.Length)
                shadowRow.dropdown.SetValueWithoutNotify((int)PatchOptions.Shadow);
            if (presetRow != null && presetRow.dropdown != null && presetRow.dropdown.options.Count == PresetNames.Length)
                presetRow.dropdown.SetValueWithoutNotify((int)PatchOptions.Preset);
            for (int i = 0; i < toggleRows.Count; i++)
            {
                if (toggleRows[i] == null || toggleRows[i].toggle == null) continue;
                toggleRows[i].toggle.SetIsOnWithoutNotify(toggleReads[i]());
            }
            if (experimentalRow != null && experimentalRow.toggle != null)
                experimentalRow.toggle.interactable = true;
        }

        static dropdownsetting Clone(dropdownsetting template, Transform root, string name)
        {
            GameObject copy = Object.Instantiate(template.gameObject);
            copy.name = name;
            copy.transform.SetParent(root, false);
            DisableLocalizers(copy);
            dropdownsetting row = copy.GetComponent<dropdownsetting>();
            if (row == null) throw new InvalidOperationException("Cloned row has no dropdownsetting.");
            return row;
        }

        // The prefab labels are driven by languagetext.OnEnable / ls.UpdLanguage.
        // Our rows use fixed player-facing names, so the cloned localizers are
        // disabled and unregistered before the row is ever activated; otherwise
        // they would reset both titles to the template's "Resolution".
        static void DisableLocalizers(GameObject copy)
        {
            languagetext[] localizers = copy.GetComponentsInChildren<languagetext>(true);
            for (int i = 0; i < localizers.Length; i++)
            {
                if (localizers[i] == null) continue;
                localizers[i].enabled = false;
                if (ls.l != null)
                {
                    ls.l.tmps.Remove(localizers[i]);
                    ls.l.texts.Remove(localizers[i]);
                }
            }
        }

        static void Label(dropdownsetting row, string text)
        {
            Transform label = row.transform.Find("Label");
            TMP_Text target = label == null ? null : label.GetComponentInChildren<TMP_Text>(true);
            if (target == null)
            {
                TMP_Text[] labels = row.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < labels.Length; i++)
                {
                    if (labels[i] == null || HasAncestor(labels[i].transform, "Template") || HasAncestor(labels[i].transform, "Item")) continue;
                    target = labels[i];
                    break;
                }
            }
            if (target == null)
            {
                Debug.LogWarning("[TLD MP options] no label found for " + row.name);
                return;
            }
            target.text = text;
        }

        // Label rect of a dropdown template in the row's left-edge space (x=0 is the row's
        // left edge, where Place() puts it). Empty rect when the template has no label.
        static Rect TemplateLabelRect(dropdownsetting template)
        {
            RectTransform row = (RectTransform)template.transform;
            Transform label = row.Find("Label");
            TMP_Text target = label == null ? null : label.GetComponentInChildren<TMP_Text>(true);
            if (target == null)
            {
                TMP_Text[] labels = template.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < labels.Length; i++)
                {
                    if (labels[i] == null || HasAncestor(labels[i].transform, "Template") || HasAncestor(labels[i].transform, "Item")) continue;
                    target = labels[i];
                    break;
                }
            }
            if (target == null) return new Rect(0f, 0f, 0f, 0f);
            Rect inRow = RectIn((RectTransform)target.transform, row);
            float origin = row.rect.xMin;
            return Rect.MinMaxRect(inRow.xMin - origin, inRow.yMin, inRow.xMax - origin, inRow.yMax);
        }

        // Sets the first text under `root` that is not a template or item. With
        // `panelOnly`, texts inside a button or scroll view are skipped (panel titles).
        // Returns the label that was set, or null when there is none.
        static TMP_Text SetText(Transform root, string text, bool panelOnly)
        {
            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == null || HasAncestor(labels[i].transform, "Template") || HasAncestor(labels[i].transform, "Item")) continue;
                if (panelOnly && (labels[i].text.Length == 0 || UnderButtonOrScroll(labels[i].transform, root))) continue;
                labels[i].text = text;
                return labels[i];
            }
            return null;
        }

        static bool UnderButtonOrScroll(Transform t, Transform top)
        {
            for (Transform a = t; a != null && a != top; a = a.parent)
                if (a.GetComponent<Button>() != null || a.GetComponent<ScrollRect>() != null) return true;
            return false;
        }

        static bool HasAncestor(Transform transform, string name)
        {
            for (Transform t = transform; t != null; t = t.parent)
                if (t.name == name) return true;
            return false;
        }

        static Transform AncestorNamed(Transform transform, string name)
        {
            for (Transform t = transform; t != null; t = t.parent)
                if (t.name == name) return t;
            return null;
        }

        // The same child path from `from` (below `fromRoot`) under `toRoot`, or null.
        static Transform SameSpot(Transform from, Transform fromRoot, Transform toRoot)
        {
            var indices = new List<int>();
            for (Transform t = from; t != fromRoot; t = t.parent)
            {
                if (t == null) return null;
                indices.Add(t.GetSiblingIndex());
            }
            Transform result = toRoot;
            for (int i = indices.Count - 1; i >= 0; i--)
            {
                if (result == null || indices[i] >= result.childCount) return null;
                result = result.GetChild(indices[i]);
            }
            return result;
        }

        // The scroll view above `content`, searched up to (not including) `stop`.
        static ScrollRect ScrollAbove(Transform content, Transform stop)
        {
            for (Transform t = content.parent; t != null && t != stop; t = t.parent)
            {
                ScrollRect scroll = t.GetComponent<ScrollRect>();
                if (scroll != null) return scroll;
            }
            return null;
        }

        // Keeps only what Roadworthy uses on the copied panel: the chain of transforms
        // down to the scroll content (scroll view, viewport), its scrollbars, the title
        // and the reset button. Any other native widget on the panel with a Selectable
        // or a text label is hidden. Panel decoration without those is left alone.
        static void HideNativeChrome(Transform panel, Transform content, Transform title, Transform reset)
        {
            var towardContent = new HashSet<Transform>();
            for (Transform t = content.parent; t != null && t != panel; t = t.parent) towardContent.Add(t);
            var keep = new HashSet<Transform>();
            for (Transform t = title; t != null && t != panel; t = t.parent) keep.Add(t);
            for (Transform t = reset; t != null && t != panel; t = t.parent) keep.Add(t);
            PruneNative(panel, content, towardContent, keep);
        }

        static void PruneNative(Transform node, Transform content, HashSet<Transform> towardContent, HashSet<Transform> keep)
        {
            var children = new List<Transform>();
            for (int i = 0; i < node.childCount; i++) children.Add(node.GetChild(i));
            for (int i = 0; i < children.Count; i++)
            {
                Transform child = children[i];
                if (child == content) continue;
                if (towardContent.Contains(child))
                {
                    PruneNative(child, content, towardContent, keep);
                    continue;
                }
                if (keep.Contains(child)) continue;
                if (child.GetComponentInChildren<Scrollbar>(true) != null) continue;
                if (child.GetComponentInChildren<Selectable>(true) == null &&
                    child.GetComponentInChildren<TMP_Text>(true) == null) continue;
                child.gameObject.SetActive(false);
            }
        }

        // Road content is sized to exactly its rows; the native content only grows.
        void SizeContent(RectTransform content, float requiredHeight)
        {
            if (content == roadContent || content.rect.height < requiredHeight)
                content.sizeDelta = new Vector2(content.sizeDelta.x, requiredHeight);
        }

        // One line with each Roadworthy row's placement, to check the layout in the game log.
        static void LogRoadRows(RectTransform content)
        {
            var text = new System.Text.StringBuilder("[TLD MP options] Roadworthy rows in " + content.name + ":");
            for (int i = 0; i < content.childCount; i++)
            {
                RectTransform rt = content.GetChild(i) as RectTransform;
                if (rt == null || !rt.gameObject.activeSelf) continue;
                text.Append(" | " + rt.name + " anchorMin=" + Vec(rt.anchorMin) + " anchorMax=" + Vec(rt.anchorMax) +
                    " anchoredPosition=" + Vec(rt.anchoredPosition) + " sizeDelta=" + Vec(rt.sizeDelta));
            }
            Debug.Log(text.ToString());
        }

        static string Vec(Vector2 v)
        {
            return "(" + F(v.x) + "," + F(v.y) + ")";
        }

        static T Find<T>(Transform root, string name) where T : Component
        {
            if (root == null) return null;
            Transform child = root.Find(name);
            return child == null ? null : child.GetComponent<T>();
        }

        static Transform Find(Transform root, string name)
        {
            return root == null ? null : root.Find(name);
        }

        static readonly Vector3[] corners = new Vector3[4];

        // Rows in the scroll content use a top-left pivot/anchor, so their placed
        // rect needs no world-space round trip (and works while the tab is closed).
        static Rect RowRect(RectTransform rt)
        {
            if (rt.pivot.y > 0.99f && rt.anchorMin.y > 0.99f && rt.anchorMax.y > 0.99f)
                return new Rect(rt.anchoredPosition.x, rt.anchoredPosition.y - rt.rect.height, rt.rect.width, rt.rect.height);
            return RectIn(rt, rt.parent);
        }

        // Bottom of the active rows directly under the scroll content; false when there are none.
        static bool RowsBottom(Transform content, out float bottom)
        {
            bottom = 0f;
            bool any = false;
            for (int i = 0; i < content.childCount; i++)
            {
                RectTransform rt = content.GetChild(i) as RectTransform;
                if (rt == null || !rt.gameObject.activeSelf) continue;
                float y = RowRect(rt).yMin;
                if (!any || y < bottom) { bottom = y; any = true; }
            }
            return any;
        }

        static Rect RectIn(RectTransform rt, Transform space)
        {
            rt.GetWorldCorners(corners);
            Vector3 a = space.InverseTransformPoint(corners[0]), b = space.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        }

        // Puts a row at a fixed top-left position and size in its content list. The
        // anchors and pivot are set explicitly, so a row cloned from a stretch-anchored
        // template (the Graphics dropdown) cannot keep its stretch behaviour.
        static void Place(RectTransform rt, float left, float top, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(left, top);
        }

        static T[] Append<T>(T[] source, params T[] extra)
        {
            if (source == null) source = new T[0];
            T[] result = new T[source.Length + extra.Length];
            Array.Copy(source, result, source.Length);
            Array.Copy(extra, 0, result, source.Length, extra.Length);
            return result;
        }

        static string F(float value)
        {
            return value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
