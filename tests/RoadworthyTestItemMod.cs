using System.Collections.Generic;
using TLDLoader;
using UnityEngine;

// A minimal TLDLoader item mod used to check Roadworthy's beta item framework in game.
// Build it against the player's original TLDLoader.dll; Roadworthy translates it like any
// other mod. It registers one crate through ItemRegistrationBuilder (rigidbody, pickup);
// it never spawns on its own (a test item has no place in boxes or buildings). F9 in a
// world spawns three in front of the player. Its log lines start with "[RW test item]".
public class RoadworthyTestItem : Mod
{
    public override string ID { get { return "RoadworthyTestItem"; } }
    public override string Name { get { return "Roadworthy test item"; } }
    public override string Author { get { return "Roadworthy"; } }
    public override string Version { get { return "1.0.0"; } }
    public override bool LoadInDB { get { return true; } }

    const int CrateId = 1;
    GameObject prefab;

    public override void DbLoad()
    {
        prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prefab.name = "Roadworthy test crate";
        prefab.transform.localScale = new Vector3(0.35f, 0.25f, 0.25f);
        var renderer = prefab.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = new Color(0.9f, 0.45f, 0.1f);
        var db = itemdatabase.s;
        RegisterItem(prefab, CrateId, "Roadworthy test crate")
            .WithRigidbody(4f, 0.05f, 0.05f)
            .AsPickupable(new PickupableOptions { CanInventory = true })
            .Register();
        // The builder registered a copy (held inactive by TLDLoader); drop the source cube.
        var source = prefab;
        prefab = GetItem(CrateId);
        Object.Destroy(source);
        var save = prefab == null ? null : prefab.GetComponent<tosaveitemscript>();
        Debug.Log("[RW test item] registered: save id " + (save == null ? -1 : save.id) + ", in database " + (db != null && save != null && db.Item(save.id) == prefab));
    }

    public override void OnLoad()
    {
        var save = prefab == null ? null : prefab.GetComponent<tosaveitemscript>();
        int id = save == null ? -1 : save.id, found = 0;
        if (savedatascript.s != null && savedatascript.s.items != null)
            foreach (var item in savedatascript.s.items.Values)
                if (item != null && item.id == id) found++;
        Debug.Log("[RW test item] world loaded: " + found + " test crate(s) in the world (save id " + id + ").");
    }

    public override void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F9) || mainscript.s == null || mainscript.s.player == null || prefab == null) return;
        var head = mainscript.s.player.Cam != null ? mainscript.s.player.Cam.transform : mainscript.s.player.transform;
        for (int i = 0; i < 3; i++)
        {
            var go = Object.Instantiate(prefab, head.position + head.forward * 1.5f + Vector3.up * (0.4f * i), Quaternion.identity);
            go.SetActive(true);
            mainscript.s.PostSpawn(go);
        }
        Debug.Log("[RW test item] spawned 3 test crates.");
    }
}
