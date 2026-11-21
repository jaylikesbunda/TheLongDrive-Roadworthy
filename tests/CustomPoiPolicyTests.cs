using System;
using System.Collections.Generic;
using System.IO;
using TldMp;

// Selection rules for Roadworthy's roadside buildings, and a structural check of the
// exported building data (read without Unity, so it runs in the offline gate).
internal static class CustomPoiPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        // The gate passes (managed, original); find the repository data from the test's own folder.
        // The gate may run the test from a temp copy, so also look up from the working folder.
        string data = null;
        foreach (string start in new[] { AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir != null && data == null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, Path.Combine("runtime", Path.Combine("assets", "RoadworthyPois.bin")));
                if (File.Exists(candidate)) data = candidate;
            }
        Check(data != null, "runtime/assets/RoadworthyPois.bin found");

        // Only version-6 worlds, roadside, eligible types.
        Check(CustomPoiPolicy.Pick(5, 1, 0, 0, "oldhouse", true, 4) < 0, "older worlds never change");
        Check(CustomPoiPolicy.IsReplaceable("oldhouse") && CustomPoiPolicy.IsReplaceable("budi"), "minor buildings are eligible");
        foreach (var major in new[] { "benzinkut01", "nagybenzinkut", "muhely", "kut", "mansion", "yellowfoodkut" })
            Check(CustomPoiPolicy.IsReplaceable(major), "major buildings are eligible too: " + major);
        foreach (var keep in new[] { "kezdohaz", "error", null })
            Check(!CustomPoiPolicy.IsReplaceable(keep), "never replaced: " + keep);

        int swapped = 0, total = 0; var perBuilding = new int[8];
        for (int x = -200; x < 200; x++)
            for (int z = -5; z < 5; z++)
            {
                double px = x * 173.0, pz = z * 911.0;
                int a = CustomPoiPolicy.Pick(6, 1234, px, pz, "oldhouse", true, 8);
                Check(a == CustomPoiPolicy.Pick(6, 1234, px, pz, "oldhouse", true, 8), "the same place always picks the same building");
                Check(CustomPoiPolicy.Pick(6, 1234, px, pz, "oldhouse", false, 4) < 0, "POIs away from roads are kept");
                Check(CustomPoiPolicy.Pick(6, 1234, px, pz, "kezdohaz", true, 8) < 0, "the start house is kept");
                total++;
                if (a >= 0) { swapped++; perBuilding[a]++; Check(a < 8, "index in range"); }
            }
        double share = swapped / (double)total;
        Check(share > 0.30 && share < 0.40, "about a third of roadside POIs are swapped, got " + share);
        foreach (int n in perBuilding) Check(n > swapped / 16, "every building appears");
        Check(CustomPoiPolicy.Pick(6, 1, 0, 0, "oldhouse", true, 0) < 0 && CustomPoiPolicy.Pick(6, 1, double.NaN, 0, "oldhouse", true, 4) < 0, "no buildings or bad positions keep vanilla");
        int differ = 0;
        for (int x = 0; x < 200; x++) if (CustomPoiPolicy.Pick(6, 1, x * 97.0, 0, "oldhouse", true, 4) != CustomPoiPolicy.Pick(6, 2, x * 97.0, 0, "oldhouse", true, 4)) differ++;
        Check(differ > 40, "different worlds get different layouts");
        Check(CustomPoiPolicy.Hash(5, 10, -3) == 0x0UL + CustomPoiPolicy.Hash(5, 10, -3), "hash is a pure function");

        // Layers: never a collision-only or special layer.
        Check(CustomPoiPolicy.VisibleLayer(0) == 0 && CustomPoiPolicy.VisibleLayer(15) == 15, "visible world layers are kept");
        foreach (int hidden in new[] { 2, 9, 10, 11, 12, 13, 14, 20, 22, 27, -1, 40 })
            Check(CustomPoiPolicy.VisibleLayer(hidden) == 0, "layer " + hidden + " falls back to Default");

        // Pushing back from the road.
        Check(CustomPoiPolicy.PushOut(20, 6) == 0, "far enough needs no move");
        Check(Math.Abs(CustomPoiPolicy.PushOut(8, 6) - (6 + CustomPoiPolicy.RoadMargin - 8)) < 1e-9, "too close moves back to the margin");
        Check(CustomPoiPolicy.PushOut(-1, 6) == 6 + CustomPoiPolicy.RoadMargin && CustomPoiPolicy.PushOut(double.NaN, 6) == 0, "odd distances are safe");

        // Exported data: header, names, materials, sane meshes, collision and loot.
        var names = new List<string>();
        using (var r = new BinaryReader(File.OpenRead(data)))
        {
            Check(new string(r.ReadChars(4)) == "RWP5", "data header");
            int count = r.ReadInt32();
            Check(count == 8, "eight buildings, got " + count);
            for (int b = 0; b < count; b++)
            {
                string name = Str(r); names.Add(name);
                float front = r.ReadSingle(), align = r.ReadSingle();
                Check(front > 2 && front < 15 && align >= 4 && align < 20, name + " footprint numbers are sane");
                int parts = r.ReadInt32();
                Check(parts > 2 && parts < 20, name + " uses a few game materials");
                for (int p = 0; p < parts; p++)
                {
                    string mat = Str(r);
                    Check(mat.Length > 0, "material name");
                    int nv = r.ReadInt32();
                    Check(nv > 0 && nv < 65000, name + "/" + mat + " vertex count");
                    for (int i = 0; i < nv; i++)
                    {
                        float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle();
                        float nx = r.ReadSingle(), ny = r.ReadSingle(), nz = r.ReadSingle();
                        r.ReadSingle(); r.ReadSingle();
                        Check(Math.Abs(x) < 40 && y > -1 && y < 30 && Math.Abs(z) < 40, name + " vertex inside the footprint");
                        Check(Math.Abs(Math.Sqrt(nx * nx + ny * ny + nz * nz) - 1) < 0.02, name + " normals are unit length");
                    }
                    Indices(r, nv, name);
                }
                int cv = r.ReadInt32();
                Check(cv > 8, name + " has collision");
                for (int i = 0; i < cv * 3; i++) r.ReadSingle();
                Indices(r, cv, name);
                int loot = r.ReadInt32();
                Check(loot >= 3 && loot <= 12, name + " has loot spots");
                for (int i = 0; i < loot; i++) { r.ReadSingle(); float y = r.ReadSingle(); r.ReadSingle(); Check(y >= 0 && y < 15, name + " loot above ground"); }
                int wrecks = r.ReadInt32();
                Check(wrecks >= 0 && wrecks <= 64, name + " wreck count");
                if (name == "rw_scrapyard") Check(wrecks >= 10, "the scrapyard is full of car bodies");
                if (name == "rw_hangar") Check(wrecks == 1, "the hangar has its project car");
                for (int i = 0; i < wrecks; i++)
                {
                    float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle();
                    r.ReadSingle(); float roll = r.ReadSingle(); int column = r.ReadInt32(), level = r.ReadInt32();
                    Check(Math.Abs(x) < 40 && y >= 0 && y < 2 && Math.Abs(z) < 40 && Math.Abs(roll) < 30 && column >= 0 && level >= 0 && level < 4, name + " wreck spot is sane");
                }
                int props = r.ReadInt32();
                Check(props >= 0 && props <= 64, name + " prop count");
                for (int i = 0; i < props; i++)
                {
                    string item = Str(r);
                    Check(!item.StartsWith("crated"), name + " uses no crate fragments (crated01..12 are broken pieces), got " + item);
                    float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle(); r.ReadSingle();
                    Check(Math.Abs(x) < 40 && y >= 0 && y < 15 && Math.Abs(z) < 40, name + " item spot is sane");
                }
                int spawns = r.ReadInt32(), boxes = 0;
                Check(spawns >= 0 && spawns <= 128, name + " native spawn count");
                for (int i = 0; i < spawns; i++)
                {
                    string kind = Str(r);
                    if (kind == "BoxSpawn") boxes++;
                    Check(kind.Length > 3 && kind.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0, name + " spot names a vanilla spawner, got " + kind);
                    float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle(); r.ReadSingle();
                    Check(Math.Abs(x) < 40 && y >= 0 && y < 15 && Math.Abs(z) < 40, name + " spawn spot is sane");
                }
                if (name == "rw_motel") Check(spawns >= 8, "the motel has real beds and TVs");
                if (name == "rw_scrapyard" || name == "rw_barn" || name == "rw_railway") Check(boxes >= 4, name + " has breakable crates from the vanilla box spawner");
                int doors = r.ReadInt32();
                Check(doors >= 0 && doors <= 32, name + " door count");
                if (name == "rw_motel") Check(doors == 4, "the motel has four working doors");
                for (int i = 0; i < doors; i++)
                {
                    float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle(), w = r.ReadSingle(), h = r.ReadSingle(), yaw = r.ReadSingle();
                    Check(w > 0.6f && w < 1.6f && h > 1.8f && h < 2.4f && Math.Abs(yaw) <= 180.01f && y >= 0 && y < 6, name + " doorway is door-sized");
                }
                int areas = r.ReadInt32();
                Check(areas >= 1 && areas <= 32, name + " has surfaces for vanilla item spawns");
                for (int i = 0; i < areas; i++)
                {
                    float x0 = r.ReadSingle(), z0 = r.ReadSingle(), x1 = r.ReadSingle(), z1 = r.ReadSingle(), y = r.ReadSingle();
                    Check(x1 > x0 && z1 > z0 && y >= 0 && y < 6 && Math.Abs(x0) < 40 && Math.Abs(z1) < 40, name + " surface is sane");
                }
                int fixtures = r.ReadInt32();
                Check(fixtures >= 0 && fixtures <= 64, name + " fixture count");
                if (name == "rw_motel") Check(fixtures == 9, "four TV stands and the fallen door");
                if (name == "rw_drivein") Check(fixtures == 2, "two wall shelves in the projection booth");
                if (name == "rw_hangar") Check(fixtures == 3, "two workbenches and the starting garage rack");
                if (name == "rw_railway") Check(fixtures == 5, "four ramp boards and the signal-box table");
                for (int i = 0; i < fixtures; i++)
                {
                    string source = Str(r), meshName = Str(r);
                    float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle();
                    float w = r.ReadSingle(), h = r.ReadSingle(), d = r.ReadSingle();
                    float pitch = r.ReadSingle(), yaw = r.ReadSingle(), roll = r.ReadSingle();
                    bool collision = r.ReadBoolean();
                    Check(source.Length > 0 && (source == "door" || meshName.Length > 0), name + " fixture has a source");
                    Check(Math.Abs(x) < 40 && y >= 0 && y < 15 && Math.Abs(z) < 40, name + " fixture position is sane");
                    Check(w > 0 && w <= 6 && h > 0 && h <= 6 && d > 0 && d <= 6, name + " fixture size is sane");
                    Check(Math.Abs(pitch) <= 180 && Math.Abs(yaw) <= 180 && Math.Abs(roll) <= 180, name + " fixture rotation is sane");
                    if (source == "door" || meshName == "deszka02") Check(!collision, name + " decorative fixture adds no collision");
                    if (meshName == "konyvespolc/PolcWood") Check(!collision, name + " shelf collision comes from the authored mesh");
                    if (meshName == "fa anyagok")
                        Check(name == "rw_hangar" && source == "kezdohaz" && h > 2 && collision, "hangar uses the full-height garage rack");
                }
            }
            Check(r.BaseStream.Position == r.BaseStream.Length, "no trailing data");
        }
        foreach (var n in new[] { "rw_chapel", "rw_motel", "rw_drivein", "rw_silos", "rw_hangar", "rw_barn", "rw_scrapyard", "rw_railway" }) Check(names.Contains(n), "building " + n);

        Console.WriteLine("PASS: " + checks + " custom POI assertions; roadside POIs (not the start house) in version-6 worlds swap (about a third, stable per place and world), buildings clear the road, and the exported meshes, collision and loot spots are well formed.");
        return 0;
    }

    static void Indices(BinaryReader r, int vertexCount, string name)
    {
        int n = r.ReadInt32();
        Check(n > 0 && n % 3 == 0, name + " triangle list");
        for (int i = 0; i < n; i++) { int k = r.ReadInt32(); if (k < 0 || k >= vertexCount) Check(false, name + " index in range"); }
    }

    static string Str(BinaryReader r) { int n = r.ReadInt32(); return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n)); }
}
