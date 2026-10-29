using System;
using Steamworks;
using UnityEngine;

namespace TldMp
{
    // Guards used when a remote player disappears. The native timeout path
    // destroys the proxy but leaves its entry in syncScript.players; later item
    // or player events then dereference the destroyed mpplayerscript. These
    // helpers preserve the native call and only skip it once the script is gone.
    internal static class PlayerLeaveSafety
    {
        internal static bool ShouldPrune(bool hasProxy, bool hasScript)
        {
            return !hasProxy || !hasScript;
        }

        internal static readonly CollisionRestoreCache RestoreCache = new CollisionRestoreCache();
        static readonly System.Collections.Generic.List<Collider> itemColliders = new System.Collections.Generic.List<Collider>(256);
        static int[] itemIds = new int[256];
        static int[] playerIds = new int[16];
        static int cacheState;

        // TLD_MP_IGNORE_CACHE=0 restores the native call every time.
        static bool CacheEnabled
        {
            get
            {
                if (cacheState == 0) cacheState = System.Environment.GetEnvironmentVariable("TLD_MP_IGNORE_CACHE") == "0" ? 2 : 1;
                return cacheState == 1;
            }
        }

        internal static void IgnoreCollision(mpplayerscript script, Transform target, bool ignore)
        {
            if (script == null) return;
            if (ignore)
            {
                RestoreCache.PlayerIgnored(script.GetInstanceID());
                script.IgnoreCollision(target, true);
                return;
            }
            // A null target keeps the native behaviour (it throws inside native).
            if (target == null || !CacheEnabled)
            {
                script.IgnoreCollision(target, false);
                return;
            }
            // Same collider query as native: GetComponentsInChildren<Collider>() (active objects only).
            target.GetComponentsInChildren<Collider>(itemColliders);
            int itemCount = itemColliders.Count;
            if (itemIds.Length < itemCount) itemIds = new int[Math.Max(itemCount, itemIds.Length * 2)];
            for (int i = 0; i < itemCount; i++) itemIds[i] = itemColliders[i] == null ? 0 : itemColliders[i].GetInstanceID();
            itemColliders.Clear();
            Collider[] cols = script.cols;
            int playerCount = cols == null ? 0 : cols.Length;
            if (playerIds.Length < playerCount) playerIds = new int[playerCount];
            for (int i = 0; i < playerCount; i++) playerIds[i] = cols[i] == null ? 0 : cols[i].GetInstanceID();
            int itemId = target.GetInstanceID(), playerId = script.GetInstanceID();
            if (RestoreCache.CanSkip(itemId, playerId, itemIds, itemCount, playerIds, playerCount)) return;
            script.IgnoreCollision(target, false);
            RestoreCache.RecordRestore(itemId, playerId, itemIds, itemCount, playerIds, playerCount);
        }

        internal static syncScript.playerSyncClass PlayerWithScript(syncScript owner, CSteamID id)
        {
            if (owner == null) return null;
            syncScript.playerSyncClass player = owner.GetPlayerBySteamID(id);
            if (player == null || player.mpscript == null) return null;
            return player;
        }
    }
}
