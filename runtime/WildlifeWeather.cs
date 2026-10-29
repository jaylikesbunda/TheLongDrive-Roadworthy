using System;
using UnityEngine;

namespace TldMp
{
    // Vanilla ships rabbits and sandstorms built but switched off:
    //   * The local Player carries a fully configured aispawn for the Nyul (rabbit)
    //     prefab (up to 10, 1-2 every 50-100 s, 30-100 m away, outside the view
    //     cone), but nothing ever calls aispawn.START().
    //   * The SandStorm prefab, itemdatabase.storms, save/load and the day-cycle
    //     fog/sky blending exist, but no sandStormSpawnScript is placed in the
    //     scene, so mainscript.sandStormSpawn is always null.
    // Unity-free decisions; WildlifeWeather applies them.
    internal static class WildlifeWeatherPolicy
    {
        // Spawn timing for the re-created sandstorm spawner. The developer's
        // values are not in the game files (the component was never placed).
        internal const float SandstormMinSeconds = 600f;
        internal const float SandstormMaxSeconds = 1800f;
        internal const float SandstormRadius = 1500f;

        // Rabbits run in single-player only. In multiplayer the guest receives each
        // rabbit's ragdoll several times a second: every update teleports its frozen
        // bones (which can fling a car the guest is riding in) and logs a warning
        // per bone, costing 30-45 ms per message. Vanilla never spawned them online.
        internal static bool RabbitsShouldRun(bool option, bool connected, bool worldStarted, bool hasPlayer, bool hasSpawner)
        {
            return option && !connected && worldStarted && hasPlayer && hasSpawner;
        }

        // A host removes rabbits left in the world (from single-player play) so
        // guests never receive them; the native destroy broadcasts the removal.
        internal static bool RemoveRabbits(bool connected, bool isHost, bool rabbitPrefabKnown)
        {
            return connected && isHost && rabbitPrefabKnown;
        }

        // Sandstorms are not synchronised (each peer would get its own storms and
        // its own wind forces on shared items), so they run in single-player only.
        internal static bool SandstormsShouldRun(bool option, bool connected, bool worldStarted, bool hasPlayer, int stormPrefabs)
        {
            return option && !connected && worldStarted && hasPlayer && stormPrefabs > 0;
        }

        // movecreaturescript writes velocity every frame; on a kinematic body Unity
        // ignores the write and logs a warning. Skipping it changes nothing else.
        internal static bool SkipCreatureVelocity(bool hasBody, bool kinematic)
        {
            return hasBody && kinematic;
        }
    }

    internal static class WildlifeWeather
    {
        internal const string SpawnerName = "TldSandstormSpawner";
        static sandStormSpawnScript ownSpawner;
        static aispawn startedSpawner;
        internal static long CreatureWritesSkipped;
        static float nextCheck;

        // Called once per frame by the bridge driver; checks twice a second.
        internal static void Tick(bool connected)
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            try
            {
                PatchOptions.Load();
                var main = mainscript.s;
                fpscontroller player = main == null ? null : main.player;
                var map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
                bool worldStarted = map != null && map.firstRun;
                TickRabbits(player, connected, worldStarted);
                TickSandstorms(main, player, connected, worldStarted);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[TLD MP wildlife] " + error.GetType().Name + ": " + error.Message);
            }
        }

        static aispawn FindSpawner(fpscontroller player)
        {
            if (player == null) return null;
            aispawn spawner = player.GetComponent<aispawn>();
            if (spawner == null) spawner = player.GetComponentInParent<aispawn>();
            if (spawner == null) spawner = player.GetComponentInChildren<aispawn>();
            return spawner;
        }

        static void TickRabbits(fpscontroller player, bool connected, bool worldStarted)
        {
            aispawn spawner = FindSpawner(player);
            bool run = WildlifeWeatherPolicy.RabbitsShouldRun(PatchOptions.Rabbits, connected, worldStarted, player != null, spawner != null);
            if (run && !spawner.started)
            {
                spawner.START();
                startedSpawner = spawner;
                Debug.Log("[TLD MP wildlife] rabbit spawner started (max " + spawner.maxAINum + ", every " + spawner.minTime + "-" + spawner.maxTime + " s)");
            }
            else if (!run && startedSpawner != null && startedSpawner.started)
            {
                // The native routine ends after its current wait once started is false.
                startedSpawner.started = false;
                Debug.Log("[TLD MP wildlife] rabbit spawner stopped" + (connected ? " (multiplayer)" : ""));
            }
            tosaveitemscript prefab = spawner == null || spawner.AIPrefab == null ? null : spawner.AIPrefab.GetComponent<tosaveitemscript>();
            if (WildlifeWeatherPolicy.RemoveRabbits(connected, syncScript.IsHost(), prefab != null)) RemoveRabbits(prefab.id);
        }

        static readonly System.Collections.Generic.List<GameObject> doomed = new System.Collections.Generic.List<GameObject>();

        static void RemoveRabbits(int prefabId)
        {
            var save = savedatascript.s;
            if (save == null || save.items == null) return;
            doomed.Clear();
            foreach (var pair in save.items)
                if (pair.Value != null && pair.Value.id == prefabId && pair.Value.GetComponent<movecreaturescript>() != null) doomed.Add(pair.Value.gameObject);
            for (int i = 0; i < doomed.Count; i++) UnityEngine.Object.Destroy(doomed[i]);
            if (doomed.Count > 0) Debug.Log("[TLD MP wildlife] removed " + doomed.Count + " rabbit(s) for multiplayer");
            doomed.Clear();
        }

        static void TickSandstorms(mainscript main, fpscontroller player, bool connected, bool worldStarted)
        {
            itemdatabase db = itemdatabase.s;
            int prefabs = db == null || db.storms == null ? 0 : db.storms.Length;
            bool run = main != null && WildlifeWeatherPolicy.SandstormsShouldRun(PatchOptions.Sandstorms, connected, worldStarted, player != null, prefabs);
            if (run)
            {
                if (ownSpawner != null || main.sandStormSpawn != null) return;
                var go = new GameObject(SpawnerName);
                go.SetActive(false);
                var spawner = go.AddComponent<sandStormSpawnScript>();
                spawner.minTime = WildlifeWeatherPolicy.SandstormMinSeconds;
                spawner.maxTime = WildlifeWeatherPolicy.SandstormMaxSeconds;
                spawner.radius = WildlifeWeatherPolicy.SandstormRadius;
                spawner.prefabid = 0;
                main.sandStormSpawn = spawner;   // save/load keeps its timer from now on
                go.SetActive(true);
                ownSpawner = spawner;
                Debug.Log("[TLD MP wildlife] sandstorm spawner created (every " + spawner.minTime + "-" + spawner.maxTime + " s, " + spawner.radius + " m away)");
            }
            else if (ownSpawner != null)
            {
                if (main != null && main.sandStormSpawn == ownSpawner) main.sandStormSpawn = null;
                UnityEngine.Object.Destroy(ownSpawner.gameObject);
                ownSpawner = null;
                Debug.Log("[TLD MP wildlife] sandstorm spawner removed (" + (connected ? "multiplayer" : "turned off") + ")");
            }
        }

        // IL guards on movecreaturescript.Move/Jump (true = run the native body).
        internal static bool CreatureMove(movecreaturescript creature)
        {
            if (creature == null) return true;
            Rigidbody body = creature.RB;
            if (!WildlifeWeatherPolicy.SkipCreatureVelocity(body != null, body != null && body.isKinematic)) return true;
            // Native Move ends by clearing moveDir; keep that effect.
            creature.moveDir = Vector3.zero;
            CreatureWritesSkipped++;
            return false;
        }

        internal static bool CreatureJump(movecreaturescript creature)
        {
            if (creature == null) return true;
            Rigidbody body = creature.RB;
            if (!WildlifeWeatherPolicy.SkipCreatureVelocity(body != null, body != null && body.isKinematic)) return true;
            CreatureWritesSkipped++;
            return false;
        }
    }
}
