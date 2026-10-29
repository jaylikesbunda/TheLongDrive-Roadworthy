using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TldMp
{
    // Low-frequency snapshot of the native terrain streaming workload. This is
    // observational only; it does not change terrain generation or placement.
    internal static class TerrainWorkDiagnostics
    {
        const float ReportInterval = 30f;
        static readonly Dictionary<int, float> nextReport = new Dictionary<int, float>();
        static readonly FieldInfo lastDrawDistance = typeof(terrainGenScript).GetField(
            "lastDrawDistance", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo running = typeof(terrainGenScript).GetField(
            "running", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo control = typeof(terrainGenScript).GetField(
            "control", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo control2 = typeof(terrainGenScript).GetField(
            "control2", BindingFlags.Instance | BindingFlags.NonPublic);

        internal sealed class Snapshot
        {
            internal string Name;
            internal string Map;
            internal bool Active;
            internal bool Enabled;
            internal bool Distant;
            internal bool Collider;
            internal bool UseAsync;
            internal bool UseAsync2;
            internal bool Running;
            internal bool Control2;
            internal int Control;
            internal int Resolution;
            internal int Tiles;
            internal int Needed;
            internal int Removing;
            internal int Queued;
            internal float TileSize;
            internal float DrawDistance;
            internal float LastDrawDistance;

            internal string Format()
            {
                return "terrain-work name=" + Name + " map=" + Map +
                    " active=" + Active + " enabled=" + Enabled +
                    " distant=" + Distant + " collider=" + Collider +
                    " async=" + UseAsync + "/" + UseAsync2 +
                    " running=" + Running + " ctrl=" + Control + "/" + Control2 +
                    " tiles=" + Tiles + " needed=" + Needed + " removing=" + Removing +
                    " queued=" + Queued + " resolution=" + Resolution +
                    " tileSize=" + TileSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " draw=" + DrawDistance.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " lastDraw=" + LastDrawDistance.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        internal static bool ShouldReport(bool diagnosticsEnabled, int instanceId, float now,
            float interval, IDictionary<int, float> deadlines)
        {
            if (!diagnosticsEnabled || deadlines == null || interval < 0f) return false;
            float due;
            if (deadlines.TryGetValue(instanceId, out due) && now < due) return false;
            deadlines[instanceId] = now + interval;
            return true;
        }

        internal static Snapshot Capture(terrainGenScript terrain)
        {
            if (terrain == null) return null;
            mapSettingScript map = terrain.mapSetting;
            float last = ReadFloat(lastDrawDistance, terrain, Single.NaN);
            return new Snapshot
            {
                Name = terrain.terrainName ?? String.Empty,
                Map = map == null ? String.Empty : map.name,
                Active = terrain.gameObject.activeInHierarchy,
                Enabled = terrain.isActiveAndEnabled,
                Distant = terrain.isDistantTerrain,
                Collider = terrain.hasCollider,
                UseAsync = map != null && map.useAsync,
                UseAsync2 = map != null && map.useAsync2,
                Running = ReadBool(running, terrain),
                Control = ReadInt(control, terrain),
                Control2 = ReadBool(control2, terrain),
                Tiles = terrain.terrains == null ? 0 : terrain.terrains.Count,
                Needed = terrain.coordsNeeded == null ? 0 : terrain.coordsNeeded.Count,
                Removing = terrain.coordsToRemove == null ? 0 : terrain.coordsToRemove.Count,
                Queued = terrain.toGen == null ? 0 : terrain.toGen.Count,
                Resolution = terrain.resolution,
                TileSize = terrain.tileSize,
                DrawDistance = terrain.drawDistance,
                LastDrawDistance = last
            };
        }

        internal static string TryReport(terrainGenScript terrain)
        {
            if (!ModDiagnostics.On || terrain == null)
                return null;
            int id = terrain.GetInstanceID();
            if (!ShouldReport(true, id, Time.realtimeSinceStartup, ReportInterval, nextReport))
                return null;
            Snapshot snapshot = Capture(terrain);
            return snapshot == null ? null : snapshot.Format();
        }

        internal static void Reset()
        {
            nextReport.Clear();
        }

        static float ReadFloat(FieldInfo field, object target, float fallback)
        {
            try { return field == null ? fallback : (float)field.GetValue(target); }
            catch { return fallback; }
        }

        static int ReadInt(FieldInfo field, object target)
        {
            try { return field == null ? 0 : (int)field.GetValue(target); }
            catch { return 0; }
        }

        static bool ReadBool(FieldInfo field, object target)
        {
            try { return field != null && (bool)field.GetValue(target); }
            catch { return false; }
        }
    }
}
