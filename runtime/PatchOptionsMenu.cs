using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TldMp
{
    // Appends Shadow quality and Performance preset dropdowns to the end of the
    // game's Graphics tab. The native UI is prefab-driven, so an existing
    // dropdownsetting row is cloned (same font, style and dropdown template);
    // existing row indices are untouched. Everything is defensive: an unexpected
    // hierarchy logs one warning and leaves the native menu alone.
    [DefaultExecutionOrder(20001)]
    public sealed class PatchOptionsMenu : MonoBehaviour
    {
        static readonly string[] PresetNames = { "Vanilla", "Balanced", "Performance", "Custom" };
        static readonly string[] ShadowNames = { "Off", "Low", "Medium", "High" };
        const string PresetRowName = "TldPerformancePreset";
        const string ShadowRowName = "TldShadowQuality";

        bool installed, failed;
        settingsscript installedSettings;
        int activeFrames;
        dropdownsetting presetRow, shadowRow;
        togglesetting smoothRow;
        const string SmoothRowName = "TldCarSmoothingToggle";

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
                presetRow = shadowRow = null;
                smoothRow = null;
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
            settingsscript.tab tab = settings.tabGraphics;
            if (tab == null || tab.dropdrops == null || tab.dropdrops.Length == 0 || tab.dropdrops[0] == null)
                throw new InvalidOperationException("Graphics tab or its first dropdown is missing.");
            dropdownsetting template = tab.dropdrops[0];
            GameObject templateObject = template.gameObject;
            Transform root = templateObject.transform.parent;
            if (root == null) throw new InvalidOperationException("Dropdown row has no parent.");

            GameObject[] existing = tab.obj ?? new GameObject[0];
            RectTransform templateRect = templateObject.transform as RectTransform;
            if (templateRect == null) throw new InvalidOperationException("Dropdown row is not a RectTransform.");
            RectTransform content = root as RectTransform;
            if (content == null) throw new InvalidOperationException("Dropdown rows are not inside a RectTransform content list.");

            RowBounds bounds = Bounds(root);
            Rect templateSpace = RowRect(templateRect);
            Debug.Log("[TLD MP options] tabGraphics rows=" + existing.Length + " dropdrops=" + tab.dropdrops.Length +
                " row=" + F(templateSpace.width) + "x" + F(templateSpace.height) +
                " usedBottom=" + F(bounds.Bottom) + " contentHeight=" + F(content.rect.height) +
                " columnLeft=" + F(templateSpace.xMin));

            // Rows live in a scrollable content list. Append under every existing
            // row and grow the content so the scroll view can reach them.
            float gap = 8f;
            float top = bounds.Bottom - gap;
            float left = templateSpace.xMin;
            float width = templateSpace.width;
            float height = templateSpace.height;
            float firstTop = top;

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
                Place((RectTransform)shadowRow.transform, root, left, top, width, height);
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
                Place((RectTransform)presetRow.transform, root, left, top, width, height);
            }
            else presetRow = Find<dropdownsetting>(root, PresetRowName);
            if (shadowRow == null || presetRow == null) throw new InvalidOperationException("New rows were not created.");
            float requiredHeight = -(top - height) + 24f;
            if (content.rect.height < requiredHeight)
                content.sizeDelta = new Vector2(content.sizeDelta.x, requiredHeight);
            var scroll = root.parent == null || root.parent.parent == null ? null : root.parent.parent.GetComponent<ScrollRect>();
            bool scrollable = scroll != null && scroll.content == content;
            if (scrollable && !scroll.vertical) scroll.vertical = true;
            bool autoScrolled = scrollable && templateObject.activeInHierarchy;
            if (autoScrolled) scroll.verticalNormalizedPosition = 0f;
            Debug.Log("[TLD MP options] rows placed shadowBottom=" + F(-(firstTop - height)) + " presetBottom=" + F(-(top - height)) +
                " contentHeight=" + F(content.rect.height) +
                " scroll=" + (scroll == null ? "none" : (scroll.enabled ? "on" : "off") + "/vertical=" + scroll.vertical) +
                " contentLinked=" + scrollable + " autoScrolled=" + autoScrolled);

            shadowRow.dropdown.onValueChanged.AddListener(delegate(int index) { OnShadow(index); });
            presetRow.dropdown.onValueChanged.AddListener(delegate(int index) { OnPreset(index); });

            tab.dropdrops = Append(tab.dropdrops, shadowRow, presetRow);
            tab.obj = Append(existing, shadowRow.gameObject, presetRow.gameObject);
            PatchOptions.Load();
            Sync();
            Debug.Log("[TLD MP options] functions added preset= " + PatchOptions.PresetName(PatchOptions.Preset) +
                " shadow=" + PatchOptions.ShadowName(PatchOptions.Shadow) + " options=" + PatchOptions.DefaultPath());
            try { InstallMisc(settings); }
            catch (Exception error) { Debug.LogWarning("[TLD MP options] smoothing toggle not installed: " + error.GetType().Name + " " + error.Message); }
        }

        void InstallMisc(settingsscript settings)
        {
            settingsscript.tab tab = settings.tabMisc;
            if (tab == null || tab.toggles == null || tab.toggles.Length == 0 || tab.toggles[0] == null)
                throw new InvalidOperationException("Misc tab or its first toggle is missing.");
            togglesetting template = tab.toggles[0];
            Transform root = template.transform.parent;
            RectTransform content = root as RectTransform;
            if (root == null || content == null) throw new InvalidOperationException("Misc toggle rows have no content parent.");
            if (Find(root, SmoothRowName) != null) { smoothRow = Find<togglesetting>(root, SmoothRowName); return; }

            smoothRow = CloneToggle(template, root, SmoothRowName);
            smoothRow.settings = null;
            smoothRow.childToggles = new togglesetting[0];
            smoothRow.dropdown = null;
            smoothRow.button = null;
            smoothRow.ev = new UnityEvent();
            smoothRow.def = true;
            LabelToggle(smoothRow, "Car smoothing");
            RowBounds bounds = Bounds(root);
            Rect templateSpace = RowRect((RectTransform)template.transform);
            float gap = 8f;
            float top = bounds.Bottom - gap;
            float left = templateSpace.xMin;
            float width = templateSpace.width;
            float height = templateSpace.height;
            Place((RectTransform)smoothRow.transform, root, left, top, width, height);
            float requiredHeight = -(top - height) + 24f;
            if (content.rect.height < requiredHeight)
                content.sizeDelta = new Vector2(content.sizeDelta.x, requiredHeight);
            smoothRow.toggle.SetIsOnWithoutNotify(PatchOptions.SmoothCars);
            smoothRow.toggle.onValueChanged.AddListener(delegate(bool on)
            {
                PatchOptions.Load();
                PatchOptions.SmoothCars = on;
                PatchOptions.Save();
            });
            tab.toggles = Append(tab.toggles, smoothRow);
            tab.obj = Append(tab.obj, smoothRow.gameObject);
            Debug.Log("[TLD MP options] misc toggle placed bottom=" + F(-(top - height)) +
                " contentHeight=" + F(content.rect.height) + " smoothing=" + PatchOptions.SmoothCars);
            AddMiscToggle(tab, template, root, content, RabbitsRowName, "Rabbits (single-player)", PatchOptions.Rabbits,
                delegate(bool on) { PatchOptions.Rabbits = on; });
            AddMiscToggle(tab, template, root, content, SandstormsRowName, "Sandstorms (single-player)", PatchOptions.Sandstorms,
                delegate(bool on) { PatchOptions.Sandstorms = on; });
            AddMiscToggle(tab, template, root, content, HighRateRowName, "High multiplayer update rate", PatchOptions.HighRate,
                delegate(bool on) { PatchOptions.HighRate = on; });
        }

        const string RabbitsRowName = "TldRabbitsToggle";
        const string SandstormsRowName = "TldSandstormsToggle";
        const string HighRateRowName = "TldHighRateToggle";

        // One more cloned Misc toggle under the current last row, saved to the
        // options file. Same cloning/placement as the car smoothing row.
        void AddMiscToggle(settingsscript.tab tab, togglesetting template, Transform root, RectTransform content,
            string name, string label, bool value, Action<bool> apply)
        {
            if (Find(root, name) != null) return;
            togglesetting row = CloneToggle(template, root, name);
            row.settings = null;
            row.childToggles = new togglesetting[0];
            row.dropdown = null;
            row.button = null;
            row.ev = new UnityEvent();
            row.def = true;
            LabelToggle(row, label);
            RowBounds bounds = Bounds(root);
            Rect templateSpace = RowRect((RectTransform)template.transform);
            float top = bounds.Bottom - 8f;
            float height = templateSpace.height;
            Place((RectTransform)row.transform, root, templateSpace.xMin, top, templateSpace.width, height);
            float requiredHeight = -(top - height) + 24f;
            if (content.rect.height < requiredHeight)
                content.sizeDelta = new Vector2(content.sizeDelta.x, requiredHeight);
            row.toggle.SetIsOnWithoutNotify(value);
            row.toggle.onValueChanged.AddListener(delegate(bool on)
            {
                PatchOptions.Load();
                apply(on);
                PatchOptions.Save();
            });
            tab.toggles = Append(tab.toggles, row);
            tab.obj = Append(tab.obj, row.gameObject);
            Debug.Log("[TLD MP options] misc toggle " + label + " placed bottom=" + F(-(top - height)) + " on=" + value);
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
            if (smoothRow != null && smoothRow.toggle != null)
                smoothRow.toggle.SetIsOnWithoutNotify(PatchOptions.SmoothCars);
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

        static bool HasAncestor(Transform transform, string name)
        {
            for (Transform t = transform; t != null; t = t.parent)
                if (t.name == name) return true;
            return false;
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
        struct RowBounds { public float Bottom; }

        // Rows in the scroll content use a top-left pivot/anchor, so their placed
        // rect needs no world-space round trip (and works while the tab is closed).
        static Rect RowRect(RectTransform rt)
        {
            if (rt.pivot.y > 0.99f && rt.anchorMin.y > 0.99f && rt.anchorMax.y > 0.99f)
                return new Rect(rt.anchoredPosition.x, rt.anchoredPosition.y - rt.rect.height, rt.rect.width, rt.rect.height);
            return RectIn(rt, rt.parent);
        }

        // Bottom of every existing row (direct child of the scroll content).
        static RowBounds Bounds(Transform content)
        {
            float bottom = 0f;
            bool any = false;
            for (int i = 0; i < content.childCount; i++)
            {
                RectTransform rt = content.GetChild(i) as RectTransform;
                if (rt == null || !rt.gameObject.activeSelf) continue;
                Rect r = RowRect(rt);
                if (!any || r.yMin < bottom) { bottom = r.yMin; any = true; }
            }
            return new RowBounds { Bottom = bottom };
        }

        static Rect RectIn(RectTransform rt, Transform space)
        {
            rt.GetWorldCorners(corners);
            Vector3 a = space.InverseTransformPoint(corners[0]), b = space.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        }

        static void Place(RectTransform rt, Transform space, float left, float top, float width, float height)
        {
            if (rt.pivot.y > 0.99f && rt.anchorMin.y > 0.99f && rt.anchorMax.y > 0.99f)
            {
                rt.sizeDelta = new Vector2(width, height);
                rt.anchoredPosition = new Vector2(left, top);
                return;
            }
            rt.GetWorldCorners(corners);
            Vector3 a = space.InverseTransformPoint(corners[0]), b = space.InverseTransformPoint(corners[2]);
            float w = Math.Max(1f, b.x - a.x), h = Math.Max(1f, b.y - a.y);
            float scaleX = rt.lossyScale.x / space.lossyScale.x, scaleY = rt.lossyScale.y / space.lossyScale.y;
            if (scaleX > 0f && scaleY > 0f)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x + (width / scaleX - w), rt.sizeDelta.y + (height / scaleY - h));
            rt.GetWorldCorners(corners);
            a = space.InverseTransformPoint(corners[0]); b = space.InverseTransformPoint(corners[2]);
            float dx = left - Math.Min(a.x, b.x), dy = top - Math.Max(a.y, b.y);
            if (Math.Abs(dx) > 0.01f || Math.Abs(dy) > 0.01f) rt.position += space.TransformVector(new Vector3(dx, dy, 0f));
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
