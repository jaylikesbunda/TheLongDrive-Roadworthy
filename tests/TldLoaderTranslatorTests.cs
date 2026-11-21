using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TldMp;

// Translates the installed TLDLoader 3.x against the pristine beta assembly and
// checks the result: hooks placed, old names gone, broken Harmony classes out
// of PatchAll, working ones kept, every bridge call present in the runtime.
// args: <Managed> <pristine Assembly-CSharp> <Tld.Multiplayer.dll>
internal static class TldLoaderTranslatorTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        string managed = args[0], game = args[1], runtime = args[2];
        string source = Path.Combine(managed, "TLDLoader.roadworthy-original.bin");
        if (!File.Exists(source)) source = Path.Combine(managed, "TLDLoader.dll");
        if (!File.Exists(source)) { Console.WriteLine("SKIP: TLDLoader is not installed in " + managed + "."); return 0; }
        byte[] original = File.ReadAllBytes(source);
        var runtimeName = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(runtime))).Name;
        var translator = new TldLoaderTranslator(game, new[] { managed }, runtimeName.Name, runtimeName.Version);
        var report = translator.TranslateLoader(original);
        Check(report.LoadDllHooked, "LoadDLL is hooked");
        Check(report.SceneHookRemoved, "the activeSceneChanged subscription is removed");
        Check(report.Rewrites > 5, "game names were rewritten (" + report.Rewrites + ")");

        string[] dropped = { "Patch_itemdatabase_ReturnItem", "Patch_itemPlaceRemoveScript_PlaceOneItem", "Patch_itemPlaceRemoveScript_RemoveItems",
            "Patch_MainMenu_Discord", "Patch_MainMenu_CopyDiscord", "Patch_SaveScreen_Delete" };
        string[] kept = { "Patch_Seat_GetIn", "Patch_Seat_GetOut", "Patch_SaveScreen_Save", "Patch_SaveScreen_Load", "Patch_Mainscript_SetCursorVisible", "Patch_spawnInBoxScript_FStart" };
        Check(report.RegisterHooked, "ItemDatabase.RegisterItem hands prefabs to the beta");
        Check(report.ReplacedBodies.Contains("ItemDatabase.MakeSaveable") && report.ReplacedBodies.Contains("ItemRegistrationBuilder.SpawnAt"), "MakeSaveable and SpawnAt run beta-native: " + string.Join(", ", report.ReplacedBodies.ToArray()));

        var output = AssemblyDefinition.ReadAssembly(new MemoryStream(report.Bytes));
        var module = output.MainModule;
        var profilePaths = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "LoaderDataRoot")
            .Select(i => i.Next.Operand as string).ToArray();
        foreach (var path in new[] { "\\Mods\\", "\\Mods\\temp\\mp.dat", "\\Modpack\\", "\\Mods\\Config\\Mod Settings\\TLDLoader_Core\\" })
            Check(profilePaths.Contains(path), "loader uses the active profile for " + path);
        var byName = module.GetTypes().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());
        foreach (string name in dropped)
            Check(byName.ContainsKey(name) && !HasHarmony(byName[name]), name + " is out of PatchAll");
        foreach (string name in kept)
            Check(byName.ContainsKey(name) && HasHarmony(byName[name]), name + " keeps its Harmony attributes");

        // No public-game names left.
        string[] gone = { "mainscript::M", "mainscript::DFMS", "itemdatabase::d", "savedatascript::d", "menuhandler::Settings", "mainmenuscript::" };
        foreach (var type in module.GetTypes())
            foreach (var method in type.Methods.Where(m => m.HasBody))
                foreach (var instruction in method.Body.Instructions)
                {
                    var field = instruction.Operand as FieldReference;
                    if (field == null) continue;
                    string key = field.DeclaringType.FullName + "::" + field.Name;
                    foreach (string old in gone)
                        Check(!(key == old || (old.EndsWith("::") && key.StartsWith(old))), "old name " + key + " is still used in " + type.Name + "." + method.Name);
                }
        Check(!module.GetTypeReferences().Any(t => t.Name == "mainmenuscript"), "the mainmenuscript type is gone");

        // The hook is the first thing LoadDLL does.
        var load = module.GetType("TLDLoader.ModLoader").Methods.First(m => m.Name == "LoadDLL");
        var first = load.Body.Instructions.Take(3).ToArray();
        Check(first[0].OpCode == OpCodes.Ldarg_0 && first[1].OpCode == OpCodes.Call && ((MethodReference)first[1].Operand).Name == "TranslateMod" && first[2].OpCode.Code == Code.Starg, "LoadDLL starts with the translate hook");

        // Every TldLoaderBridge call matches a public static method in the runtime.
        var runtimeModule = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(runtime))).MainModule;
        CheckWaveDecoder(runtime);
        bridge = runtimeModule.GetType("TldMp.TldLoaderBridge");
        Check(bridge != null && bridge.IsPublic, "the runtime has a public TldMp.TldLoaderBridge");
        // In game, a type scan of the runtime before Cecil is loaded made Mono reject the
        // translator for the whole session. So the runtime never references Cecil itself.
        Check(!runtimeModule.AssemblyReferences.Any(r => r.Name == "Mono.Cecil"), "Tld.Multiplayer.dll does not reference Mono.Cecil");
        Check(runtimeModule.GetType("TldMp.TldLoaderTranslator") == null, "the Cecil translator is not compiled into Tld.Multiplayer.dll");
        var embedded = runtimeModule.Resources.OfType<EmbeddedResource>().ToDictionary(r => r.Name, r => r);
        Check(embedded.ContainsKey("TldMp.Mono.Cecil.dll"), "Mono.Cecil is embedded in the runtime");
        Check(embedded.ContainsKey("TldMp.TldLoaderTranslator.dll"), "the translator assembly is embedded in the runtime");
        var translatorModule = AssemblyDefinition.ReadAssembly(new MemoryStream(embedded["TldMp.TldLoaderTranslator.dll"].GetResourceData())).MainModule;
        Check(translatorModule.Assembly.Name.Name == "TldMp.TldLoaderTranslator" && translatorModule.GetType("TldMp.TldLoaderTranslator") != null, "the embedded translator is TldMp.TldLoaderTranslator");
        var used = BridgeCalls(module, runtimeName.Name);
        foreach (string name in new[] { "TranslateMod", "MainScript", "DataFromMenu", "MenuSettings", "SceneName", "AnimatorSetBool" })
            Check(used.Contains(name), "TLDLoader uses the bridge's " + name);

        // What is still missing is only in Harmony classes that were left out of PatchAll.
        var allowed = dropped.Select(d => "TLDLoader." + d + ".").Concat(new[] { "TLDLoader.ModLoader/Patch_MainMenu_" }).ToArray();
        foreach (var pair in report.Missing)
            foreach (string user in pair.Value)
                Check(allowed.Any(a => user.StartsWith(a)), pair.Key + " is missing on the beta but used outside the item framework by " + user);
        Check(!report.Missing.Values.Any(users => users.Any(u => u.StartsWith("TLDLoader.ItemRegistrationBuilder") || u.StartsWith("TLDLoader.ItemDatabase"))), "the item framework uses no missing game names");

        // Stable: a second translation of the same file gives the same bytes.
        var again = new TldLoaderTranslator(game, new[] { managed }, runtimeName.Name, runtimeName.Version).TranslateLoader(original);
        Check(again.Bytes.SequenceEqual(report.Bytes), "translation is deterministic");

        // Installed mods (if any): every bridge call they get must exist in the runtime.
        string mods = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TheLongDrive"), "Mods");
        int modCount = 0;
        var files = new List<string>(args.Skip(3));
        if (Directory.Exists(mods)) files.AddRange(Directory.GetFiles(mods, "*.dll").Where(f => !args.Skip(3).Any(a => Path.GetFileName(a) == Path.GetFileName(f))));
        foreach (string file in files)
            {
                var modReport = translator.TranslateMod(File.ReadAllBytes(file));
                BridgeCalls(AssemblyDefinition.ReadAssembly(new MemoryStream(modReport.Bytes)).MainModule, runtimeName.Name);
                modCount++;
                if (args.Skip(3).Contains(file))
                    Check(modReport.Missing.Count == 0, Path.GetFileName(file) + " (built for this test) uses no missing game names: " + string.Join(", ", modReport.Missing.Keys.ToArray()));
                if (Path.GetFileName(file) == "Roadworthy_StableApi.dll")
                {
                    // Built against the stable branch: each beta difference takes its rule.
                    var stableMod = AssemblyDefinition.ReadAssembly(new MemoryStream(modReport.Bytes)).MainModule;
                    var calls = stableMod.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                        .Select(i => i.Operand as MethodReference).Where(m => m != null).Select(m => m.DeclaringType.Name + "::" + m.Name).ToList();
                    Check(modReport.Degraded.Contains("mainscript::chatWasOpen"), "a removed field gets stand-in storage: " + string.Join(", ", modReport.Degraded.ToArray()));
                    Check(modReport.Degraded.Any(d => d.StartsWith("mainscript::TankInc(")) && calls.Any(c => c.StartsWith("RoadworthyMissingGameMembers::")), "a removed method becomes a stub");
                    Check(calls.Contains("TldLoaderBridge::MainGodMode") && calls.Contains("TldLoaderBridge::MainChGodMode"), "god mode goes to the developer settings");
                    Check(calls.Contains("TldLoaderBridge::MainHpCh"), "HpCh goes to its helper");
                    Check(calls.Contains("TldLoaderBridge::WorldLoading"), "mainscript.load reads \"still loading\"");
                    Check(calls.Contains("TldLoaderBridge::MainWorld"), "mainscript.mainWorld goes to its helper");
                    Check(!stableMod.GetTypeReferences().Any(t => t.Name == "doublecoord"), "doublecoord became the beta's Vector3d");
                    var runtimeTypes = new HashSet<string>(bridge.Module.GetTypes().Select(t => t.FullName));
                    var standIns = stableMod.GetTypeReferences().Where(t => t.Scope != null && t.Scope.Name == runtimeName.Name && t.Namespace == "TldMp.StableShells").Select(t => t.FullName).ToList();
                    Check(standIns.Contains("TldMp.StableShells.TerrainGenerator") && standIns.Contains("TldMp.StableShells.roadclass"), "deleted stable types go to stand-ins: " + string.Join(", ", standIns.ToArray()));
                    Check(standIns.Contains("TldMp.StableShells.roadBuildingScript_buildingGroup") &&
                        !stableMod.GetTypeReferences().Any(t => t.FullName.EndsWith("roadBuildingScript/buildingGroup")),
                        "nested shell types are rewritten independently of their declaring type");
                    Check(standIns.Contains("TldMp.StableShells.save_prefab") &&
                        calls.Contains("TldLoaderBridge::SpawnStableSavedItem"),
                        "stable save records keep their fields and construction, and spawning calls the adapter");
                    Check(calls.Contains("TldLoaderBridge::LegacyNear") && calls.Contains("TldLoaderBridge::LegacyFar") &&
                        calls.Contains("TldLoaderBridge::CaptureUsable") &&
                        calls.Contains("TldLoaderBridge::CapturePainting") &&
                        calls.Contains("TldLoaderBridge::CaptureAi"), "stable save views and removed component constructors use their beta adapters");
                    Check(stableMod.GetMemberReferences().OfType<FieldReference>().Any(f => f.DeclaringType.Name == "save_carscript" && f.Name == "BhandBrake"),
                        "case-only stable field renames bind to the real beta field");
                    Check(calls.Contains("TldLoaderBridge::PickupInventorySlot") && calls.Contains("TldLoaderBridge::PickupSlots") &&
                        calls.Contains("TldLoaderBridge::SetPickupSlots"), "stable inventory and slot accesses go to beta state");
                    Check(calls.Contains("TldLoaderBridge::RadioVolume"), "stable radio volume reads the beta's volume control");
                    foreach (string standIn in standIns) Check(runtimeTypes.Contains(standIn), "the runtime has the stand-in " + standIn);
                    Check(calls.Contains("TldLoaderBridge::StableRoads") && calls.Contains("TldLoaderBridge::MainRoadScript") && calls.Contains("TldLoaderBridge::RoadSegmentLength"), "stable road data comes from the bridge");
                    Check(calls.Contains("mainscript::Spawn") && calls.Contains("TldLoaderBridge::PlayerRightHandRelease"), "changed signatures are called through the beta's overloads");
                    var probe = stableMod.GetTypes().SelectMany(t => t.Methods).First(m => m.Name == "OnLoad").Body.Instructions;
                    var spawn = probe.First(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Spawn");
                    Check(((MethodReference)spawn.Operand).ReturnType.FullName == "UnityEngine.GameObject" && spawn.Next.OpCode == OpCodes.Pop, "the beta's Spawn result is dropped where the stable call returned nothing");
                }
                Console.WriteLine("  mod " + Path.GetFileName(file) + ": " + modReport.Rewrites + " rewrites, " + modReport.Missing.Count + " names still missing" +
                    (modReport.Missing.Count == 0 ? "" : " (" + string.Join(", ", modReport.Missing.Keys.ToArray()) + ")"));
            }

        Console.WriteLine("PASS: TLDLoader translation (" + modCount + " installed mods checked; " + checks + " checks; " + report.Rewrites + " rewrites; " + report.StrippedPatches.Count +
            " patch classes left out; " + report.Missing.Count + " names left only in those classes).");
        return 0;
    }

    static TypeDefinition bridge;

    static void CheckWaveDecoder(string runtime)
    {
        var wave = System.Reflection.Assembly.Load(File.ReadAllBytes(runtime)).GetType("TldMp.StableShells.WAV", true);
        byte[] data;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            var ascii = System.Text.Encoding.ASCII;
            writer.Write(ascii.GetBytes("RIFF")); writer.Write(46);
            writer.Write(ascii.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(8000); writer.Write(32000);
            writer.Write((short)4); writer.Write((short)16);
            writer.Write(ascii.GetBytes("JUNK")); writer.Write(1); writer.Write((byte)1); writer.Write((byte)0);
            writer.Write(ascii.GetBytes("data")); writer.Write(8);
            writer.Write((short)-32768); writer.Write((short)16384);
            writer.Write((short)8192); writer.Write((short)0);
            data = stream.ToArray();
            // RIFF's length excludes the first eight bytes.
            int length = data.Length - 8;
            data[4] = (byte)length;
        }
        var decoded = Activator.CreateInstance(wave, new object[] { data });
        Func<string, object> get = name => wave.GetProperty(name).GetValue(decoded, null);
        Check((int)get("SampleCount") == 2 && (int)get("Frequency") == 8000 && (int)get("ChannelCount") == 2,
            "stable WAV metadata survives the PCM decoder");
        Check(((float[])get("LeftChannel")).SequenceEqual(new[] { -1f, .25f }) &&
            ((float[])get("RightChannel")).SequenceEqual(new[] { .5f, 0f }) &&
            ((float[])get("StereoChannel")).SequenceEqual(new[] { -1f, .5f, .25f, 0f }),
            "stable WAV sample order and signedness are preserved");
        bool rejected = false;
        try { Activator.CreateInstance(wave, new object[] { data.Take(data.Length - 1).ToArray() }); }
        catch (System.Reflection.TargetInvocationException e) { rejected = e.InnerException is InvalidDataException; }
        Check(rejected, "truncated PCM input is rejected");
    }

    static HashSet<string> BridgeCalls(ModuleDefinition module, string runtimeName)
    {
        var used = new HashSet<string>();
        foreach (var type in module.GetTypes())
            foreach (var method in type.Methods.Where(m => m.HasBody))
                foreach (var instruction in method.Body.Instructions)
                {
                    var called = instruction.Operand as MethodReference;
                    if (called == null || called.DeclaringType.Name != "TldLoaderBridge") continue;
                    Check(called.DeclaringType.Scope.Name == runtimeName, "bridge calls reference " + runtimeName);
                    var element = called is GenericInstanceMethod ? ((GenericInstanceMethod)called).ElementMethod : called;
                    int arity = called is GenericInstanceMethod ? ((GenericInstanceMethod)called).GenericArguments.Count : 0;
                    var target = bridge.Methods.FirstOrDefault(m => m.Name == element.Name && m.IsStatic && m.IsPublic && m.Parameters.Count == element.Parameters.Count &&
                        m.GenericParameters.Count == arity && Sig(m.ReturnType) == Sig(element.ReturnType) &&
                        m.Parameters.Select(p => Sig(p.ParameterType)).SequenceEqual(element.Parameters.Select(p => Sig(p.ParameterType))));
                    Check(target != null, "the runtime has " + called.FullName + " (used by " + type.FullName + "." + method.Name + ")");
                    // Mono matches signatures exactly: object must be the built-in type, not a class reference.
                    foreach (var part in new[] { element.ReturnType }.Concat(element.Parameters.Select(p => p.ParameterType)))
                        Check(!ClassObject(part), called.FullName + " (in " + type.FullName + "." + method.Name + ") names System.Object as a class reference; Mono would not find it");
                    used.Add(called.Name);
                }
        return used;
    }

    static bool ClassObject(TypeReference type)
    {
        var instance = type as GenericInstanceType;
        if (instance != null) return ClassObject(instance.ElementType) || instance.GenericArguments.Any(ClassObject);
        var spec = type as TypeSpecification;
        if (spec != null) return ClassObject(spec.ElementType);
        return type.FullName == "System.Object" && type.MetadataType != MetadataType.Object;
    }

    // Type signature with method generic parameters written as !!index on both sides.
    static string Sig(TypeReference type)
    {
        var generic = type as GenericParameter;
        if (generic != null) return "!!" + generic.Position;
        var array = type as ArrayType;
        if (array != null) return Sig(array.ElementType) + "[]";
        var byRef = type as ByReferenceType;
        if (byRef != null) return Sig(byRef.ElementType) + "&";
        var instance = type as GenericInstanceType;
        if (instance != null) return instance.ElementType.FullName + "<" + string.Join(",", instance.GenericArguments.Select(Sig).ToArray()) + ">";
        return type.FullName;
    }

    static bool HasHarmony(TypeDefinition type)
    {
        return type.CustomAttributes.Any(a => a.AttributeType.Namespace == "HarmonyLib") ||
            type.Methods.Any(m => m.CustomAttributes.Any(a => a.AttributeType.Namespace == "HarmonyLib"));
    }
}
