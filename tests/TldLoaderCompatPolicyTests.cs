using System;
using System.Collections.Generic;
using TldMp;

internal static class TldLoaderCompatPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        // Switch: the environment's 0 always wins.
        Check(TldLoaderCompatPolicy.Enabled(null, true), "on by default");
        Check(!TldLoaderCompatPolicy.Enabled("0", true), "TLD_MP_TLDLOADER=0 turns it off");
        Check(!TldLoaderCompatPolicy.Enabled(" 0 ", true), "whitespace around 0 still turns it off");
        Check(TldLoaderCompatPolicy.Enabled("1", true) && !TldLoaderCompatPolicy.Enabled("1", false), "otherwise the settings toggle decides");

        // Name mapping.
        var m = TldLoaderCompatPolicy.ForField("mainscript", "M");
        Check(m.Kind == TldLoaderCompatPolicy.RewriteKind.CallHelper && m.Name == "MainScript" && m.Static, "mainscript.M becomes the menu-aware helper");
        Check(TldLoaderCompatPolicy.ForField("mainscript", "DFMS").Name == "DataFromMenu" && !TldLoaderCompatPolicy.ForField("mainscript", "DFMS").Static, "DFMS is an instance helper");
        Check(TldLoaderCompatPolicy.ForField("menuhandler", "Settings").Name == "MenuSettings", "menuhandler.Settings maps to settingsscript.s");
        Check(TldLoaderCompatPolicy.ForField("itemdatabase", "d").Name == "s" && TldLoaderCompatPolicy.ForField("savedatascript", "d").Name == "s", "d singletons become s");
        Check(TldLoaderCompatPolicy.ForField("mainmenuscript", "mainmenu").Name == "s", "mainmenuscript.mainmenu becomes menuhandler.s");
        Check(TldLoaderCompatPolicy.ForField("mainmenuscript", "Canvas").Name == "canv2" && TldLoaderCompatPolicy.ForField("mainmenuscript", "saveScreen").Name == "saveScreen", "menu fields keep their meaning");
        Check(TldLoaderCompatPolicy.ForField("mainscript", "loaded").Name == "WorldLoaded" && TldLoaderCompatPolicy.ForField("mainscript", "world").Name == "WorldRoot", "world-loaded flag and world root use helpers");
        Check(TldLoaderCompatPolicy.MethodHelper("headlightscript", "SwitchState", new[] { "System.Int32" }) == "HeadlightSwitchState", "the old headlight switch maps to the helper");
        Check(TldLoaderCompatPolicy.AddComponentHelper("partscript") == "ModAddPart" && TldLoaderCompatPolicy.AddComponentHelper("attachablescript") == "ModAddPart", "a part added at run time gets the beta set-up");
        Check(TldLoaderCompatPolicy.AddComponentHelper("partslotscript") == "ModAddSlot", "a slot added at run time gets the beta set-up");
        Check(TldLoaderCompatPolicy.AddComponentHelper("tosaveitemscript") == null, "other components are added as written");
        Func<string, bool> fields = f => f == "speed";
        Check(TldLoaderCompatPolicy.PatchArgumentFits("__instance", false, true, new string[0], fields) && !TldLoaderCompatPolicy.PatchArgumentFits("__instance", true, true, new string[0], fields), "__instance needs an instance method");
        Check(TldLoaderCompatPolicy.PatchArgumentFits("__result", false, false, new string[0], fields) && !TldLoaderCompatPolicy.PatchArgumentFits("__result", false, true, new string[0], fields), "__result needs a return value");
        Check(TldLoaderCompatPolicy.PatchArgumentFits("___speed", false, true, new string[0], fields) && !TldLoaderCompatPolicy.PatchArgumentFits("___gone", false, true, new string[0], fields), "___field needs that field");
        Check(TldLoaderCompatPolicy.PatchArgumentFits("led", false, true, new[] { "led" }, fields) && !TldLoaderCompatPolicy.PatchArgumentFits("state", false, true, new[] { "led" }, fields), "named arguments must match a parameter");
        Check(TldLoaderCompatPolicy.PatchArgumentFits("__1", false, true, new[] { "a", "b" }, fields) && !TldLoaderCompatPolicy.PatchArgumentFits("__2", false, true, new[] { "a", "b" }, fields), "__N must be a parameter index");
        Check(TldLoaderCompatPolicy.MethodHelper("partslotscript", "FStart", new string[0]) == "SlotFStart", "a mod's slot FStart keeps the public game's meaning");
        Check(TldLoaderCompatPolicy.ReturnAdaptable("System.Void", "UnityEngine.GameObject"), "a void call to a method that now returns a value drops the value");
        Check(TldLoaderCompatPolicy.ReturnAdaptable("System.Int32", "System.UInt32") && !TldLoaderCompatPolicy.ReturnAdaptable("UnityEngine.GameObject", "System.Void"), "a value the beta no longer returns cannot be made up");
        Check(TldLoaderCompatPolicy.ForField("mainscript", "load").Name == "WorldLoading" && TldLoaderCompatPolicy.ForField("mainscript", "loaded").Name == "WorldLoaded", "load means still loading, loaded means ready");
        Check(TldLoaderCompatPolicy.MethodHelper("mainscript", "HpCh", new[] { "System.Single" }) == "MainHpCh" && TldLoaderCompatPolicy.MethodHelper("mainscript", "PressedExit", new string[0]) == "MainPressedExit", "removed mainscript shortcuts map to helpers");
        Check(TldLoaderCompatPolicy.ForField("mainscript", "mainWorld").Name == "MainWorld", "the stable world object maps to its helper");
        Check(TldLoaderCompatPolicy.RenamedType("roadclass") == "[runtime]TldMp.StableShells.roadclass" && TldLoaderCompatPolicy.RenamedType("settingsscript/DrawDistanceSetting") == "[runtime]TldMp.StableShells.settingsscript_DrawDistanceSetting",
            "deleted stable types go to their stand-ins (nested ones flattened)");
        Check(TldLoaderCompatPolicy.ForField("attachablescript", "onlyToPoint").Name == "onlyToSlot" && TldLoaderCompatPolicy.ForField("attachablescript", "lockRotation").Name == "AttachLockedRotation", "stable point options and the rotation lock have distinct mappings");
        Check(TldLoaderCompatPolicy.ForField("tosaveitemscript", "category").Name == "ItemCategory" && TldLoaderCompatPolicy.ForField("roadGenScript", "roadList").Name == "RoadList", "item category and the road list map to helpers");
        Check(TldLoaderCompatPolicy.ForField("tankcapscript", "TCap").SetName == "SetCapMoving" && TldLoaderCompatPolicy.ForField("attachablescript", "colliderOrTargetId").Name == "AttachColliderId", "fuel caps and stable attachment fields map both ways");
        Check(TldLoaderCompatPolicy.MethodHelper("attachablescript", "Load", new[] { "UnityEngine.Vector3" }) == "AttachLoad" && TldLoaderCompatPolicy.ForField("mainscript", "godmode").Name == "MainGodMode", "stable attachment load and god mode map to helpers");
        Check(TldLoaderCompatPolicy.MethodHelper("mountStuff", "Lock", new[] { "pickupable", "System.Boolean" }) == "MountLock" && TldLoaderCompatPolicy.ForField("pickupable", "isPhyslocked").Name == "PickupIsPhyslocked", "physics locks map to the beta's");
        Check(TldLoaderCompatPolicy.ForField("roadGenScript", "roadSegmentLength").Name == "RoadSegmentLength" && TldLoaderCompatPolicy.ForField("menuhandler", "DFMS").Name == "MenuDataFromMenu", "road dimensions and the menu data map to helpers");
        Check(TldLoaderCompatPolicy.ForField("roadGenScript", "roads").Name == "StableRoads" && TldLoaderCompatPolicy.ForField("savedatascript", "mainRoadScript").Name == "MainRoadScript", "stable road data maps to helpers");
        Check(TldLoaderCompatPolicy.ForField("roadGenScript", "length").SetName == "SetRoadLength" &&
            TldLoaderCompatPolicy.ForField("roadGenScript", "doublePos").SetName == "SetRoadDoublePos", "stable road controls keep their writes");
        Check(TldLoaderCompatPolicy.RenamedType("menuhandler") == null && TldLoaderCompatPolicy.RenamedType("doublecoord") == "UnityEngine.Vector3d", "types the beta still has, and mapped ones, are not stood in");
        var aligned = TldLoaderCompatPolicy.AlignParameters(new[] { "System.Single", "System.Boolean" }, new[] { "System.Single", "UnityEngine.Vector3", "System.Boolean" });
        Check(aligned != null && aligned[0] == 0 && aligned[1] == -1 && aligned[2] == 1, "a parameter added between the call's arguments gets the default");
        var trailing = TldLoaderCompatPolicy.AlignParameters(new[] { "System.Boolean" }, new[] { "System.Boolean", "System.Boolean" });
        Check(trailing != null && trailing[0] == 0 && trailing[1] == -1, "extra parameters at the end are filled in order");
        Check(TldLoaderCompatPolicy.AlignParameters(new[] { "System.Boolean" }, new[] { "System.Int32", "System.Boolean", "System.Single", "System.Boolean" }) == null, "an argument that could go to two places is not guessed");
        Check(TldLoaderCompatPolicy.RenamedType("doublecoord") == "UnityEngine.Vector3d" && !TldLoaderCompatPolicy.RenamedToCoreLibrary("UnityEngine.Vector3d") && TldLoaderCompatPolicy.RenamedToCoreLibrary("System.Object"), "coordinate structs map to the beta's game types");
        Check(TldLoaderCompatPolicy.ReturnItemIndex(0, 71) == 71, "TLDLoader's box patch ReturnItem(0, id) returns that item, not item 0");
        Check(TldLoaderCompatPolicy.ReturnItemIndex(71, 0) == 71 && TldLoaderCompatPolicy.ReturnItemIndex(3, 71) == 71, "the id is the second number unless it is 0");
        Check(TldLoaderCompatPolicy.SlotLayer(0, 1 | (1 << 9), 1 << 9 | 1) == 0, "a slot both searches already see keeps its layer");
        Check(TldLoaderCompatPolicy.SlotLayer(0, (1 << 9) | (1 << 12), (1 << 12) | (1 << 3)) == 12, "a slot moves to the lowest layer both searches see");
        Check(TldLoaderCompatPolicy.SlotLayer(0, 1 << 9, 1 << 12) == 12, "with no shared layer the slot goes where the player's attach ray looks");
        Check(TldLoaderCompatPolicy.SlotLayer(5, 0, 0) == 5, "unknown masks leave the layer alone");
        Check(TldLoaderCompatPolicy.MethodHelper("headlightscript", "SwitchState", new[] { "System.Int32", "usablescript" }) == null, "the beta's own overload is untouched");
        Check(TldLoaderCompatPolicy.MethodHelper("newSaveScreenScript", "Open", new[] { "System.Boolean" }) == "OpenSaveScreen", "stable's Open(bool) reaches the beta save screen");
        // Item mods.
        Check(TldLoaderCompatPolicy.RenamedType("save_prefab") == TldLoaderCompatPolicy.ShellPrefix + "save_prefab" &&
            TldLoaderCompatPolicy.RenamedType("partscript") == "attachablescript", "removed save and part types are retargeted");
        Check(TldLoaderCompatPolicy.SameStackType("System.Int32", "System.UInt32") && TldLoaderCompatPolicy.SameStackType("System.UInt64", "System.Int64"), "signedness changes are stack-compatible");
        Check(!TldLoaderCompatPolicy.SameStackType("System.Int32", "System.Int64") && !TldLoaderCompatPolicy.SameStackType("System.Int32", "System.Single"), "size or kind changes are not");
        var attach = TldLoaderCompatPolicy.ForField("pickupable", "attach");
        Check(TldLoaderCompatPolicy.CanApply(attach, true, false) && TldLoaderCompatPolicy.CanApply(attach, false, false, true), "pickupable.attach has a load and a store helper");
        var target = TldLoaderCompatPolicy.ForField("mainscript", "world");
        Check(TldLoaderCompatPolicy.CanApply(target, true, false) && !TldLoaderCompatPolicy.CanApply(target, false, false, true), "a load-only helper leaves stores alone");
        Check(!TldLoaderCompatPolicy.CanApply(attach, false, false, false), "an address of a helper field is left alone");
        Check(TldLoaderCompatPolicy.ForField("savedata", "nearStuff").Name == "LegacyNear" && TldLoaderCompatPolicy.ForField("savedata", "farStuff").Name == "LegacyFar", "near and far are live legacy record stores");
        Check(TldLoaderCompatPolicy.ForField("savedata", "farStuff").SetName == "SetLegacyFar", "legacy stores keep dictionary replacement");
        Check(TldLoaderCompatPolicy.ForField("partscript", "tosaveitem").Kind == TldLoaderCompatPolicy.RewriteKind.RenameField, "partscript.tosaveitem is attachablescript.tosave");
        Check(TldLoaderCompatPolicy.MethodHelper("partslotscript", "Craft", new[] { "partscript", "System.Boolean" }) == "SlotCraft", "the two-argument Craft maps to the helper");
        Check(TldLoaderCompatPolicy.ForField("tosaveitemscript", "idInSave").Kind == TldLoaderCompatPolicy.RewriteKind.None, "idInSave is only retyped, not rewritten");
        // Item framework: stable modded prefab ids.
        Func<int, string> empty = delegate(int id) { return null; };
        string keyA = TldLoaderCompatPolicy.ModItemKey("Nitrous", 3);
        int idA = TldLoaderCompatPolicy.ModItemId(keyA, -1, empty);
        Check(TldLoaderCompatPolicy.IsModItemId(idA), "a modded id is inside the modded range: " + idA);
        Check(idA == TldLoaderCompatPolicy.ModItemId(keyA, -1, empty), "the same key always hashes to the same id (players agree)");
        Check(TldLoaderCompatPolicy.ModItemId(TldLoaderCompatPolicy.ModItemKey(" Nitrous ", 3), -1, empty) == idA, "mod ID whitespace does not change the id");
        Check(TldLoaderCompatPolicy.ModItemId(keyA, -1, delegate(int id) { return id == idA ? keyA : null; }) == idA, "an id held by the same key is kept");
        int moved = TldLoaderCompatPolicy.ModItemId(keyA, -1, delegate(int id) { return id == idA ? "Other#1" : null; });
        Check(moved != idA && TldLoaderCompatPolicy.IsModItemId(moved), "a taken id moves to a free one");
        int rememberedId = TldLoaderCompatPolicy.ModItemBase + 7;
        Check(TldLoaderCompatPolicy.ModItemId(keyA, rememberedId, empty) == rememberedId, "a remembered id wins over the hash");
        Check(TldLoaderCompatPolicy.ModItemId(keyA, rememberedId, delegate(int id) { return id == rememberedId ? "Other#1" : null; }) != rememberedId, "a remembered id taken by another key is not reused");
        Check(TldLoaderCompatPolicy.ModItemId(keyA, -1, delegate(int id) { return "Full#" + id; }) == -1, "a full range gives no id");
        Check(!TldLoaderCompatPolicy.IsModItemId(TldLoaderCompatPolicy.ModItemBase - 1) && !TldLoaderCompatPolicy.IsModItemId(TldLoaderCompatPolicy.ModItemBase + TldLoaderCompatPolicy.ModItemRange), "vanilla ids are never modded ids");
        Check(TldLoaderCompatPolicy.ModItemKey("A", 12) != TldLoaderCompatPolicy.ModItemKey("A1", 2), "keys keep mod and item apart");
        // Array/List fields.
        Check(TldLoaderCompatPolicy.Shape("tankscript", null, null, "tankscript") == TldLoaderCompatPolicy.CollectionShape.ArrayInModListInGame, "T[] in the mod, List<T> on the beta");
        Check(TldLoaderCompatPolicy.Shape(null, "Collider", "Collider", null) == TldLoaderCompatPolicy.CollectionShape.ListInModArrayInGame, "List<T> in the mod, T[] on the beta");
        Check(TldLoaderCompatPolicy.Shape("tankscript", null, null, "usablescript") == TldLoaderCompatPolicy.CollectionShape.None, "different element types never convert");
        // Body replacements and the box spawner patch.
        Check(TldLoaderCompatPolicy.ReplacedLoaderMethod("TLDLoader.ItemDatabase", "MakeSaveable") == "ModMakeSaveable" && TldLoaderCompatPolicy.ReplacedLoaderMethod("TLDLoader.ItemRegistrationBuilder", "SpawnAt") == "ModSpawnAt", "the framework's beta-native bodies");
        Check(TldLoaderCompatPolicy.ReplacedLoaderMethod("TLDLoader.ItemDatabase", "RegisterItem") == null, "RegisterItem keeps its body (it is hooked)");
        Check(!TldLoaderCompatPolicy.IsDroppedLoaderPatch("Patch_spawnInBoxScript_FStart"), "the box spawner patch is kept on the beta");
        Check(TldLoaderCompatPolicy.MethodHelper("itemdatabase", "ReturnItem", new[] { "System.Int32", "System.Int32" }) == "ReturnItem", "ReturnItem maps to Item(index)");
        Check(TldLoaderCompatPolicy.MethodHelper("tosaveitemscript", "FStart", new[] { "System.Int32" }) == "ModRegisterItem" && TldLoaderCompatPolicy.MethodHelper("mainscript", "PostSpawn", new[] { "UnityEngine.GameObject" }) == "ModPostSpawn", "mod item registration goes through the guest check");
        Check(TldLoaderCompatPolicy.ForField("mainscript", "s").Kind == TldLoaderCompatPolicy.RewriteKind.None, "beta names are left alone");
        Check(TldLoaderCompatPolicy.RenamedType("mainmenuscript") == "menuhandler" && TldLoaderCompatPolicy.RenamedType("mainscript") == null, "only the removed type is renamed");
        // Opcode fit.
        Check(TldLoaderCompatPolicy.CanApply(m, true, true), "ldsfld mainscript.M is rewritten");
        Check(!TldLoaderCompatPolicy.CanApply(m, false, true), "a store to mainscript.M is not turned into a call");
        Check(!TldLoaderCompatPolicy.CanApply(m, true, false), "a static rule never applies to an instance opcode");
        Check(TldLoaderCompatPolicy.CanApply(TldLoaderCompatPolicy.ForField("itemdatabase", "d"), false, true), "renames apply to stores too");

        // Harmony classes.
        Check(TldLoaderCompatPolicy.IsDroppedLoaderPatch("Patch_SaveScreen_Delete") && TldLoaderCompatPolicy.IsDroppedLoaderPatch("Patch_MainMenu_Discord"), "listed loader patches are dropped");
        Check(!TldLoaderCompatPolicy.IsDroppedLoaderPatch("Patch_Seat_GetIn") && !TldLoaderCompatPolicy.IsDroppedLoaderPatch("Patch_SaveScreen_Save"), "working loader patches are kept");
        Check(TldLoaderCompatPolicy.StripPatch(TldLoaderCompatPolicy.TargetStatus.Missing, false, false), "a missing target strips the patch");
        Check(TldLoaderCompatPolicy.StripPatch(TldLoaderCompatPolicy.TargetStatus.Exists, true, false), "a body using missing game code strips the patch");
        Check(!TldLoaderCompatPolicy.StripPatch(TldLoaderCompatPolicy.TargetStatus.Unknown, false, false), "an unknown target is kept");
        Check(!TldLoaderCompatPolicy.StripPatch(TldLoaderCompatPolicy.TargetStatus.Exists, false, false), "a valid patch is kept");
        Check(TldLoaderCompatPolicy.PatchedMethodName("speed", 1) == "get_speed" && TldLoaderCompatPolicy.PatchedMethodName("x", 3) == ".ctor", "method types map to metadata names");
        Check(TldLoaderCompatPolicy.PatchedMethodName(null, 1) == null && TldLoaderCompatPolicy.PatchedMethodName("x", 7) == null, "unknown shapes cannot be checked");

        // In-place TLDLoader.dll.
        string key = TldLoaderCompatPolicy.CacheKey("SRC", "GAME");
        Check(key != TldLoaderCompatPolicy.CacheKey("SRC", "GAME2") && key != TldLoaderCompatPolicy.CacheKey("SRC2", "GAME"), "the key covers both the source and the game build");
        Check(TldLoaderCompatPolicy.PlanLoader(null, null, null, null, "GAME") == TldLoaderCompatPolicy.LoaderPlan.NotInstalled, "no file, nothing to do");
        Check(TldLoaderCompatPolicy.PlanLoader("ORIG", null, null, null, "GAME") == TldLoaderCompatPolicy.LoaderPlan.FromCurrent, "a first run backs up and translates the file");
        Check(TldLoaderCompatPolicy.PlanLoader("OUT", "OUT", key, "SRC", "GAME") == TldLoaderCompatPolicy.LoaderPlan.UpToDate, "our own output is left alone");
        Check(TldLoaderCompatPolicy.PlanLoader("out", "OUT", key, "SRC", "GAME") == TldLoaderCompatPolicy.LoaderPlan.UpToDate, "hash case does not matter");
        Check(TldLoaderCompatPolicy.PlanLoader("OUT", "OUT", key, "SRC", "GAME2") == TldLoaderCompatPolicy.LoaderPlan.FromBackup, "a new game build retranslates from the backup");
        Check(TldLoaderCompatPolicy.PlanLoader("NEW", "OUT", key, "SRC", "GAME") == TldLoaderCompatPolicy.LoaderPlan.FromCurrent, "an updated TLDLoader becomes the new original");
        Check(TldLoaderCompatPolicy.PlanLoader("OUT", "OUT", key, null, "GAME") == TldLoaderCompatPolicy.LoaderPlan.UpToDate, "without its backup our output is never translated twice");
        Check(TldLoaderCompatPolicy.ShouldRestore("OUT", "OUT", true), "turned off, our output is replaced by the backup");
        Check(!TldLoaderCompatPolicy.ShouldRestore("NEW", "OUT", true), "a file we did not write is never replaced");
        Check(!TldLoaderCompatPolicy.ShouldRestore("OUT", "OUT", false), "nothing to restore without a backup");

        // One scene: menu and world.
        Check(TldLoaderCompatPolicy.SceneName(false) == "SceneMenu" && TldLoaderCompatPolicy.SceneName(true) == "SceneNewRandom", "the public game's scene names");
        Check(TldLoaderCompatPolicy.Step(false, true) == TldLoaderCompatPolicy.Transition.None, "staying on the menu");
        Check(TldLoaderCompatPolicy.Step(false, false) == TldLoaderCompatPolicy.Transition.EnterWorld, "leaving the menu enters the world");
        Check(TldLoaderCompatPolicy.Step(true, false) == TldLoaderCompatPolicy.Transition.None, "staying in the world");
        Check(TldLoaderCompatPolicy.Step(true, true) == TldLoaderCompatPolicy.Transition.LeaveWorld, "the menu again leaves the world");

        // Lobby list.
        var mods = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("zeta", "1.0"),
            new KeyValuePair<string, string>("alpha", "2.0"),
            new KeyValuePair<string, string>("we;ird@id", "1;2"),
            new KeyValuePair<string, string>("", "9"),
        };
        string encoded = TldLoaderCompatPolicy.EncodeModList(mods);
        Check(encoded == "alpha@2.0;we,ird_id@1,2;zeta@1.0", "sorted, separators replaced, empty ids dropped: " + encoded);
        var decoded = TldLoaderCompatPolicy.DecodeModList(encoded);
        Check(decoded.Count == 3 && decoded["alpha"] == "2.0" && decoded["we,ird_id"] == "1,2", "the list round-trips");
        var many = new List<KeyValuePair<string, string>>();
        for (int i = 0; i < 1000; i++) many.Add(new KeyValuePair<string, string>("mod" + i.ToString("D4"), "1.0.0"));
        Check(TldLoaderCompatPolicy.EncodeModList(many).Length <= TldLoaderCompatPolicy.LobbyListLimit, "the list fits lobby metadata");
        Check(TldLoaderCompatPolicy.DescribeDifference(encoded, encoded) == null, "same mods: no warning");
        Check(TldLoaderCompatPolicy.DescribeDifference("", null) == null && TldLoaderCompatPolicy.DescribeDifference(null, "") == null, "no mods on either side: no warning");
        string note = TldLoaderCompatPolicy.DescribeDifference("alpha@2.0;mine@1", "alpha@2.1;theirs@3");
        Check(note != null && note.Contains("Only the host has: theirs") && note.Contains("Only you have: mine") && note.Contains("alpha (host 2.1, you 2.0)"), "the warning names each difference: " + note);
        Check(note.Contains("You can still play"), "the warning never blocks");
        Check(TldLoaderCompatPolicy.DescribeDifference("mine@1", null).Contains("Only you have: mine"), "a host without the list counts as no mods");

        // Settings rows.
        Check(TldLoaderCompatPolicy.ModRowLabel("Odometer", "1.0", false, 0) == "Odometer 1.0", "a running mod");
        Check(TldLoaderCompatPolicy.ModRowLabel("Odometer", "1.0", true, 3).EndsWith("(disabled)"), "a disabled mod says so");
        Check(TldLoaderCompatPolicy.ModRowLabel("Items", "", false, 2) == "Items (may fail on this game version)", "a mod using missing game code is flagged");

        Console.WriteLine("PASS: TLDLoader compat policy (" + checks + " checks).");
        return 0;
    }
}
