using System;
using System.Collections.Generic;
using System.Reflection;
using Vector3d = UnityEngine.Vector3d;

namespace TldMp
{
    // The game's Vector3d overrides Equals(object) but does not implement
    // IEquatable<Vector3d>, so the default comparer boxes a key on every
    // Dictionary<Vector3d,...> lookup and List<Vector3d>.Contains. World streaming
    // (roads, POIs, props, terrain) makes thousands of those calls per frame.
    //
    // This comparer returns exactly what the game's own methods return:
    //   Equals      -> double.Equals per component, like Vector3d.Equals(object)
    //                  (NaN equals NaN, 0.0 equals -0.0)
    //   GetHashCode -> the same formula as Vector3d.GetHashCode()
    // so every collection behaves the same, without the allocation. It becomes
    // EqualityComparer<Vector3d>.Default, which new dictionaries capture when
    // they are created and List.Contains reads on every call. A self-test against
    // the game's methods must pass first; otherwise nothing is installed.
    // The idea follows TLD-RevaMP's Vector3dComparer (LGPL), written independently.
    public sealed class Vector3dEquality : EqualityComparer<Vector3d>
    {
        public override bool Equals(Vector3d a, Vector3d b)
        {
            return a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z);
        }

        public override int GetHashCode(Vector3d v)
        {
            return v.x.GetHashCode() ^ (v.y.GetHashCode() << 2) ^ (v.z.GetHashCode() >> 2);
        }

        static bool attempted;
        public static bool Installed { get; private set; }

        // Returns a one-line status for the log. Safe to call more than once.
        public static string Install()
        {
            if (attempted) return Installed ? "installed" : "not installed";
            attempted = true;
            var candidate = new Vector3dEquality();
            string failure = SelfTest(candidate);
            if (failure != null) return "self-test failed, not installed: " + failure;
            FieldInfo field = typeof(EqualityComparer<Vector3d>).GetField("defaultComparer", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null || !field.FieldType.IsAssignableFrom(typeof(Vector3dEquality))) return "runtime has no replaceable default comparer, not installed";
            field.SetValue(null, candidate);
            Installed = EqualityComparer<Vector3d>.Default is Vector3dEquality;
            return Installed ? "installed" : "default comparer did not take the replacement";
        }

        // Compares against the game's own Vector3d.Equals(object) and GetHashCode()
        // on edge values and grid-like and random coordinates. Null means equal.
        public static string SelfTest(Vector3dEquality comparer)
        {
            double[] edge = { 0.0, -0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon,
                double.MaxValue, double.MinValue, 1.0, -1.0, 1e-9, 0.1, 250.0, -250.0, 123456.789, 4e6 };
            var values = new List<Vector3d>();
            for (int i = 0; i < edge.Length; i++)
                for (int j = 0; j < edge.Length; j++)
                    values.Add(new Vector3d(edge[i], edge[j], edge[(i * 7 + j) % edge.Length]));
            var random = new System.Random(20261114);
            for (int i = 0; i < 400; i++)
            {
                double gx = random.Next(2) == 0 ? Math.Round(random.NextDouble() * 200 - 100) * 100.0 : random.NextDouble() * 1e6 - 5e5;
                double gz = random.Next(2) == 0 ? Math.Round(random.NextDouble() * 200 - 100) * 100.0 : random.NextDouble() * 1e6 - 5e5;
                values.Add(new Vector3d(gx, random.Next(3) == 0 ? 0.0 : random.NextDouble() * 2000, gz));
            }
            for (int i = 0; i < values.Count; i++)
            {
                Vector3d a = values[i];
                if (comparer.GetHashCode(a) != a.GetHashCode()) return "hash differs for " + Describe(a);
                Vector3d copy = new Vector3d(a.x, a.y, a.z);
                if (comparer.Equals(a, copy) != a.Equals((object)copy)) return "self equality differs for " + Describe(a);
                for (int j = 0; j < values.Count; j += 5)
                {
                    Vector3d b = values[j];
                    if (comparer.Equals(a, b) != a.Equals((object)b)) return "equality differs for " + Describe(a) + " vs " + Describe(b);
                }
            }
            return null;
        }

        static string Describe(Vector3d v)
        {
            return "(" + v.x.ToString("R") + "," + v.y.ToString("R") + "," + v.z.ToString("R") + ")";
        }
    }
}
