using System;
using System.Globalization;
using UnityEngine;

namespace TldMp
{
    // Unity-free text helpers for the native store/unstore diagnostics. A name is
    // reduced to a single log token; a position is formatted with the invariant
    // culture so a log reads the same on every machine.
    internal static class NativeStoreText
    {
        internal const int MaxNameLength = 40;
        internal const string Unknown = "?";

        internal static string Name(string raw)
        {
            if (raw == null) return Unknown;
            string text = raw.Replace("(Clone)", "").Trim();
            if (text.Length == 0) return Unknown;
            var chars = new char[text.Length];
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) c = '_';
                chars[count++] = c;
            }
            if (count == 0) return Unknown;
            if (count > MaxNameLength) count = MaxNameLength;
            return new string(chars, 0, count);
        }

        internal static string Position(Vector3 value)
        {
            return "(" + value.x.ToString("F1", CultureInfo.InvariantCulture) + "," +
                value.y.ToString("F1", CultureInfo.InvariantCulture) + "," +
                value.z.ToString("F1", CultureInfo.InvariantCulture) + ")";
        }

        // Position in the parent's local space. A missing parent is reported as
        // unknown instead of throwing during a store on a torn-down object.
        internal static string Local(Transform parent, Vector3 world)
        {
            if (parent == null) return Unknown;
            return Position(parent.InverseTransformPoint(world));
        }
    }
}
