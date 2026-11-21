using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace TldMp
{
    // Stable mods stored an item category on the item and looked it up as
    // itemdatabase.ReturnItem(category, id). The beta has neither, so reserve one
    // modded prefab id per (category, id) and register the mod's prefab in itemdatabase.
    internal static class TldLegacyCategories
    {
        static readonly Dictionary<long, GameObject> prefabs = new Dictionary<long, GameObject>();
        static readonly Dictionary<long, int> ids = new Dictionary<long, int>();

        static long Key(int category, int itemId) { return ((long)category << 32) ^ (uint)itemId; }

        internal static void Register(int category, int itemId, GameObject prefab)
        {
            if (category == 0 || prefab == null) return;
            long key = Key(category, itemId);
            prefabs[key] = prefab;
            if (ids.ContainsKey(key)) return;
            TldModItems.Register("legacy-category-" + category, itemId, prefab);
            var save = prefab.GetComponent<tosaveitemscript>();
            if (save != null) ids[key] = save.id;
        }

        internal static GameObject Find(int category, int itemId)
        {
            GameObject prefab;
            return prefabs.TryGetValue(Key(category, itemId), out prefab) ? prefab : null;
        }

        internal static bool Known(int category, int itemId) { return prefabs.ContainsKey(Key(category, itemId)); }

        internal static int SavedId(int category, int itemId)
        {
            int id;
            if (ids.TryGetValue(Key(category, itemId), out id)) return id;
            if (!Known(category, itemId)) return 0;
            var prefab = Find(category, itemId);
            if (prefab == null) return 0;
            Register(category, itemId, prefab);
            return ids.TryGetValue(Key(category, itemId), out id) ? id : 0;
        }

        // Called before the native save walks live items. Changing the item's id is safe:
        // the reserved id resolves back to the same prefab on load.
        internal static void BeforeSave()
        {
            var save = savedatascript.s;
            if (save == null || save.items == null) return;
            foreach (var pair in save.items)
            {
                var item = pair.Value;
                if (item == null) continue;
                int category = TldLoaderBridge.ItemCategory(item);
                if (category == 0 || !Known(category, item.id)) continue;
                int id = SavedId(category, item.id);
                if (id != 0 && item.id != id) item.id = id;
            }
        }

        // Older saves may still contain prefabID 0 for a modded category item. The legacy
        // record names the category, so rewrite the native record before it is spawned.
        internal static void RewriteLoaded(savedata data, Dictionary<int, StableShells.save_prefab> records)
        {
            if (data == null || data.itemData == null || data.itemData.items == null || records == null) return;
            foreach (var item in data.itemData.items)
            {
                if (item == null) continue;
                StableShells.save_prefab record;
                if (!records.TryGetValue(unchecked((int)item.id), out record) || record == null || record.categoryID == 0) continue;
                if (!Known(record.categoryID, record.prefabID)) continue;
                int id = SavedId(record.categoryID, record.prefabID);
                if (id != 0) item.prefabID = id;
            }
        }
    }

    // Stable mods used savedata.nearStuff/farStuff as live sidecar dictionaries. The beta
    // only has itemData, so keep the dictionaries per savedata and persist them beside the
    // native save. Records are deliberately opaque: every public serializable field of the
    // stable save_prefab shell (including meter/rendszam/transform) round-trips.
    internal static class TldLegacySave
    {
        const string Header = "ROADWORTHY-LEGACY 1";
        internal const string Suffix = ".roadworthy-legacy";

        internal sealed class Store
        {
            internal readonly Dictionary<int, StableShells.save_prefab> Near =
                new Dictionary<int, StableShells.save_prefab>();
            internal readonly Dictionary<int, StableShells.save_prefab> Far =
                new Dictionary<int, StableShells.save_prefab>();
            internal savedata Source;
            internal int OverlayFrame = -1;
            internal string File;
        }

        static readonly ConditionalWeakTable<savedata, Store> stores =
            new ConditionalWeakTable<savedata, Store>();
        static string pendingLoad;

        internal static Store For(savedata data)
        {
            Store store;
            if (data != null && stores.TryGetValue(data, out store)) return store;
            store = new Store();
            store.Source = data;
            if (data != null) stores.Add(data, store);
            return store;
        }

        internal static Dictionary<int, StableShells.save_prefab> Near(savedata data)
        {
            var store = For(data);
            Overlay(store, data);
            return store.Near;
        }

        internal static Dictionary<int, StableShells.save_prefab> Far(savedata data)
        {
            var store = For(data);
            Overlay(store, data);
            return store.Far;
        }

        internal static void SetNear(savedata data, Dictionary<int, StableShells.save_prefab> value)
        {
            var store = For(data);
            store.Near.Clear();
            if (value != null) foreach (var pair in value) store.Near[pair.Key] = pair.Value;
        }

        internal static void SetFar(savedata data, Dictionary<int, StableShells.save_prefab> value)
        {
            var store = For(data);
            store.Far.Clear();
            if (value != null) foreach (var pair in value) store.Far[pair.Key] = pair.Value;
        }

        static void Overlay(Store store, savedata data)
        {
            if (store == null || store.OverlayFrame == Time.frameCount) return;
            store.OverlayFrame = Time.frameCount;
            var live = savedatascript.s;
            var main = mainscript.s;
            if (data == null || live == null || main == null || !ReferenceEquals(live.data, data) || live.items == null) return;

            foreach (var pair in live.items)
            {
                var item = pair.Value;
                if (item == null) continue;
                int id = unchecked((int)pair.Key);
                StableShells.save_prefab record;
                if (!store.Near.TryGetValue(id, out record))
                    store.Near[id] = record = new StableShells.save_prefab();
                var anchor = item.useThisForDistance != null ? item.useThisForDistance : item.transform;
                record.prefabID = item.id;
                record.categoryID = TldLoaderBridge.ItemCategory(item);
                record.transform = new posRotd(anchor.position - main.visszarakva, item.transform.eulerAngles);
                record.buried = item.buried;
                record.inIninventory = TldLoaderBridge.PickupInventorySlot(item.P);
                record.rendszam = item.rendszam == null ? null : new save_rendszam(item.rendszam, pair.Key);
            }
        }

        // Called from newSaveScreenScript.Save before the native file is written.
        internal static void BeforeSave(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            var data = savedatascript.s == null ? null : savedatascript.s.data;
            if (data == null) return;
            TldLegacyCategories.BeforeSave();
            var store = For(data);
            Overlay(store, data);
            string path = Sidecar(name);
            if (path == null) return;
            try
            {
                var text = new StringBuilder();
                text.AppendLine(Header);
                WriteSection(text, "NEAR", store.Near);
                WriteSection(text, "FAR", store.Far);
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(path, text.ToString());
                store.File = path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TLD MP mods] could not write the legacy save sidecar: " + e);
            }
        }

        // Called from newSaveScreenScript.ReadSave before the world loads.
        internal static void BeforeLoad(string name) { pendingLoad = name; }

        // Called at the start of savedatascript.FixLoadStuff, after data is assigned.
        internal static void AfterLoadPending()
        {
            string name = pendingLoad;
            pendingLoad = null;
            if (string.IsNullOrEmpty(name)) return;
            var data = savedatascript.s == null ? null : savedatascript.s.data;
            if (data == null) return;
            var store = For(data);
            store.Near.Clear();
            store.Far.Clear();
            string path = Sidecar(name);
            if (path == null || !File.Exists(path)) return;
            try
            {
                Read(store, File.ReadAllLines(path));
                store.File = path;
                TldLegacyCategories.RewriteLoaded(data, store.Near);
                TldLegacyCategories.RewriteLoaded(data, store.Far);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TLD MP mods] could not read the legacy save sidecar: " + e.Message);
            }
        }

        static string Sidecar(string name)
        {
            try { return pathscript.SaveDataName(name) + Suffix; }
            catch (Exception) { return null; }
        }

        static void WriteSection(StringBuilder text, string label, Dictionary<int, StableShells.save_prefab> records)
        {
            text.AppendLine(label);
            foreach (var pair in records.OrderBy(p => p.Key))
            {
                text.Append("R\t").Append(pair.Key.ToString(CultureInfo.InvariantCulture)).AppendLine();
                WriteValue(text, "", pair.Value);
            }
        }

        static void WriteValue(StringBuilder text, string path, object value)
        {
            if (value == null) { Line(text, "N", path, ""); return; }
            var type = value.GetType();
            if (type == typeof(string))
            {
                Line(text, "S", path, Convert.ToBase64String(Encoding.UTF8.GetBytes((string)value)));
                return;
            }
            if (type.IsEnum)
            {
                Line(text, "I", path, Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: Line(text, "I", path, ((bool)value) ? "1" : "0"); return;
                case TypeCode.Char: Line(text, "I", path, ((int)(char)value).ToString(CultureInfo.InvariantCulture)); return;
                case TypeCode.SByte: case TypeCode.Byte: case TypeCode.Int16: case TypeCode.UInt16:
                case TypeCode.Int32: case TypeCode.UInt32: case TypeCode.Int64: case TypeCode.UInt64:
                    Line(text, "I", path, Convert.ToString(value, CultureInfo.InvariantCulture)); return;
                case TypeCode.Single: Line(text, "F", path, ((float)value).ToString("R", CultureInfo.InvariantCulture)); return;
                case TypeCode.Double: Line(text, "F", path, ((double)value).ToString("R", CultureInfo.InvariantCulture)); return;
                case TypeCode.Decimal: Line(text, "F", path, Convert.ToString(value, CultureInfo.InvariantCulture)); return;
            }
            var list = value as IList;
            if (list != null)
            {
                Line(text, "L", path, list.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < list.Count; i++) WriteValue(text, path + "[" + i + "]", list[i]);
                return;
            }
            if (type.IsArray)
            {
                var array = (Array)value;
                Line(text, "A", path, array.Length.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < array.Length; i++) WriteValue(text, path + "[" + i + "]", array.GetValue(i));
                return;
            }
            if (type.IsValueType || type.IsClass)
            {
                Line(text, "O", path, "");
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!Serializable(field.FieldType)) continue;
                    WriteValue(text, Join(path, field.Name), field.GetValue(value));
                }
            }
        }

        static bool Serializable(Type type)
        {
            if (type == null) return false;
            if (type == typeof(string)) return true;
            if (type.IsPrimitive || type.IsEnum) return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type)) return false;
            if (type.IsArray) return Serializable(type.GetElementType());
            if (typeof(IList).IsAssignableFrom(type))
                return type.IsGenericType && Serializable(type.GetGenericArguments()[0]);
            return type.IsValueType || (type.IsClass && !(type.Namespace ?? "").StartsWith("UnityEngine", StringComparison.Ordinal));
        }

        static string Join(string path, string name) { return path.Length == 0 ? name : path + "." + name; }

        static void Line(StringBuilder text, string code, string path, string value)
        {
            text.Append(code).Append('\t').Append(path).Append('\t').Append(value).AppendLine();
        }

        static void Read(Store store, string[] lines)
        {
            Dictionary<int, StableShells.save_prefab> section = null;
            object root = null;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (line == "NEAR") { section = store.Near; root = null; continue; }
                if (line == "FAR") { section = store.Far; root = null; continue; }
                if (line.StartsWith("R\t", StringComparison.Ordinal))
                {
                    if (section == null) continue;
                    int key;
                    if (!int.TryParse(line.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out key)) continue;
                    root = new StableShells.save_prefab();
                    section[key] = (StableShells.save_prefab)root;
                    continue;
                }
                if (root == null) continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 3) continue;
                string code = parts[0], path = parts[1], value = parts[2];
                switch (code)
                {
                    case "N": SetNull(root, path); break;
                    case "S": SetScalar(root, path, Encoding.UTF8.GetString(Convert.FromBase64String(value))); break;
                    case "I": SetScalar(root, path, value); break;
                    case "F": SetScalar(root, path, value); break;
                    case "O": CreateAt(root, path, null); break;
                    case "A":
                    {
                        int length;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out length))
                            CreateAt(root, path, length);
                        break;
                    }
                    case "L":
                    {
                        int length;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out length))
                            CreateAt(root, path, length);
                        break;
                    }
                }
            }
        }

        sealed class Token { internal string Name; internal int Index = -1; internal bool IsIndex; }

        static List<Token> Tokens(string path)
        {
            var result = new List<Token>();
            int i = 0;
            while (i < path.Length)
            {
                int start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                if (i > start) result.Add(new Token { Name = path.Substring(start, i - start) });
                if (i < path.Length && path[i] == '[')
                {
                    int close = path.IndexOf(']', i);
                    if (close < 0) break;
                    int index;
                    if (int.TryParse(path.Substring(i + 1, close - i - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                        result.Add(new Token { IsIndex = true, Index = index });
                    i = close + 1;
                }
                if (i < path.Length && path[i] == '.') i++;
            }
            return result;
        }

        static object Resolve(object root, string path, bool create)
        {
            var tokens = Tokens(path);
            object current = root;
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.IsIndex)
                {
                    var array = current as Array;
                    if (array != null && token.Index >= 0 && token.Index < array.Length) current = array.GetValue(token.Index);
                    else current = null;
                    continue;
                }
                if (current == null) return null;
                var field = current.GetType().GetField(token.Name, BindingFlags.Instance | BindingFlags.Public);
                if (field == null) return null;
                object child = field.GetValue(current);
                if (child == null && create)
                {
                    child = New(field.FieldType);
                    if (child != null) field.SetValue(current, child);
                }
                current = child;
            }
            return current;
        }

        static object New(Type type)
        {
            try
            {
                if (type.IsValueType) return Activator.CreateInstance(type);
                if (type == typeof(string)) return "";
                return Activator.CreateInstance(type, true);
            }
            catch (Exception) { return null; }
        }

        static void CreateAt(object root, string path, int? length)
        {
            var tokens = Tokens(path);
            if (tokens.Count == 0) return;
            object parent = tokens.Count == 1 ? root : Resolve(root, PathOf(tokens, tokens.Count - 1), true);
            if (parent == null) return;
            var leaf = tokens[tokens.Count - 1];
            if (leaf.IsIndex)
            {
                var array = parent as Array;
                if (array == null || leaf.Index < 0 || leaf.Index >= array.Length) return;
                Type element = array.GetType().GetElementType();
                object value = length.HasValue ? Array.CreateInstance(element, length.Value) : New(element);
                array.SetValue(value, leaf.Index);
                return;
            }
            var field = parent.GetType().GetField(leaf.Name, BindingFlags.Instance | BindingFlags.Public);
            if (field == null) return;
            object created = length.HasValue ? Array.CreateInstance(field.FieldType.GetElementType(), length.Value) : New(field.FieldType);
            if (created != null) field.SetValue(parent, created);
        }

        static string PathOf(List<Token> tokens, int count)
        {
            var text = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                var token = tokens[i];
                if (token.IsIndex) text.Append('[').Append(token.Index).Append(']');
                else
                {
                    if (text.Length > 0) text.Append('.');
                    text.Append(token.Name);
                }
            }
            return text.ToString();
        }

        static void SetNull(object root, string path)
        {
            var tokens = Tokens(path);
            if (tokens.Count == 0) return;
            object parent = tokens.Count == 1 ? root : Resolve(root, PathOf(tokens, tokens.Count - 1), true);
            if (parent == null) return;
            var leaf = tokens[tokens.Count - 1];
            if (leaf.IsIndex)
            {
                var array = parent as Array;
                if (array != null && leaf.Index >= 0 && leaf.Index < array.Length) array.SetValue(null, leaf.Index);
                return;
            }
            var field = parent.GetType().GetField(leaf.Name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null && (!field.FieldType.IsValueType || Nullable.GetUnderlyingType(field.FieldType) != null))
                field.SetValue(parent, null);
        }

        static void SetScalar(object root, string path, string value)
        {
            var tokens = Tokens(path);
            if (tokens.Count == 0) return;
            object parent = tokens.Count == 1 ? root : Resolve(root, PathOf(tokens, tokens.Count - 1), true);
            if (parent == null) return;
            var leaf = tokens[tokens.Count - 1];
            if (leaf.IsIndex)
            {
                var array = parent as Array;
                if (array == null || leaf.Index < 0 || leaf.Index >= array.Length) return;
                array.SetValue(ParseScalar(value, array.GetType().GetElementType()), leaf.Index);
                return;
            }
            var field = parent.GetType().GetField(leaf.Name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null) field.SetValue(parent, ParseScalar(value, field.FieldType));
        }

        static object ParseScalar(string value, Type type)
        {
            if (type == typeof(string)) return value;
            if (type.IsEnum) return Enum.ToObject(type, long.Parse(value, CultureInfo.InvariantCulture));
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                case TypeCode.Char: return (char)int.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.SByte: return sbyte.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Byte: return byte.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Int16: return short.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.UInt16: return ushort.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Int32: return int.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.UInt32: return uint.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Int64: return long.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.UInt64: return ulong.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Single: return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                case TypeCode.Double: return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                case TypeCode.Decimal: return decimal.Parse(value, CultureInfo.InvariantCulture);
            }
            return null;
        }
    }
}

