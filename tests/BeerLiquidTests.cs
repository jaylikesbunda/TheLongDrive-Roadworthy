using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class BeerLiquidTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            string path = name == "Assembly-CSharp" ? args[1] : Path.Combine(args[0], name + ".dll");
            return File.Exists(path) ? Assembly.Load(File.ReadAllBytes(path)) : null;
        };
        Assembly.Load(File.ReadAllBytes(args[1]));
        return Run();
    }

    static int Run()
    {
        Identity();
        Word();
        Bottles();
        Effects();
        FixedContents();
        for(int mask=0;mask<32;mask++)
        {
            bool fresh=(mask&1)!=0,host=(mask&2)!=0,fixedSpawned=(mask&4)!=0,house=(mask&8)!=0,done=(mask&16)!=0;
            Check(BeerLiquid.BarrelAllowed(fresh,host,fixedSpawned,house,done)==(mask==11), "barrel only on first native fixed spawn in a new host world: "+mask);
        }
        Console.WriteLine("PASS: " + checks + " beer liquid assertions; id 8 follows the seven native fluids and urine, the word slot is appended once, beer joins only alcohol bottle lists once, and drinking effects scale with liters.");
        return 0;
    }

    static void Identity()
    {
        int natives = Enum.GetNames(typeof(mainscript.fluidenum)).Length;
        Check(natives == 7, "the game still has seven native fluids");
        Check(BeerLiquid.FluidId == 8 && (int)BeerLiquid.Fluid == 8, "beer is fluid id 8");
        Check(UrineLiquid.FluidId == 7 && BeerLiquid.FluidId != UrineLiquid.FluidId, "urine keeps id 7");
        Check(!Enum.IsDefined(typeof(mainscript.fluidenum), BeerLiquid.Fluid), "beer does not reuse a native ordinal");
    }

    static void Word()
    {
        int index = BeerLiquid.WordIndex;
        Check(index == Enum.GetNames(typeof(ls.w)).Length, "beer word follows the native word table");
        var table = new string[index];
        var grown = BeerLiquid.WithWord(table, index);
        Check(grown.Length == index + 1 && grown[index] == "Beer", "missing slot is appended with the name");
        Check(ReferenceEquals(BeerLiquid.WithWord(grown, index), grown), "an existing slot is left alone");
        Check(BeerLiquid.WithWord(null, index) == null, "a missing table stays missing");
        var odd = new string[index + 5];
        Check(ReferenceEquals(BeerLiquid.WithWord(odd, index), odd), "an unexpected table size is not touched");
    }

    static void Bottles()
    {
        var bottle = new[] {
            new tankscript.randomfluidclass { type = mainscript.fluidenum.alcohol, chance = 10 },
            new tankscript.randomfluidclass { type = mainscript.fluidenum.water, chance = 5 } };
        var grown = BeerLiquid.WithBeer(bottle);
        Check(grown.Length == 3 && grown[2].type == BeerLiquid.Fluid && grown[2].chance == BeerLiquid.BottleChance, "beer joins a bottle that offers alcohol");
        Check(ReferenceEquals(grown[0], bottle[0]) && ReferenceEquals(grown[1], bottle[1]), "native entries are kept as they were");
        Check(ReferenceEquals(BeerLiquid.WithBeer(grown), grown), "beer is added only once");
        var fuel = new[] { new tankscript.randomfluidclass { type = mainscript.fluidenum.gas, chance = 2 } };
        Check(ReferenceEquals(BeerLiquid.WithBeer(fuel), fuel), "tanks without alcohol get no beer");
        Check(BeerLiquid.WithBeer(null) == null, "a missing list stays missing");
    }

    static void FixedContents()
    {
        var contents=new mainscript.fluidcontainer{maxC=.5f};
        contents.ChangeOne(.5f,mainscript.fluidenum.alcohol);
        Check(BeerLiquid.ReplaceBottledAlcohol(contents), "fixed alcohol bottle becomes beer");
        Check(Math.Abs(BeerLiquid.Amount(contents)-.5f)<1e-6f && contents.GetPercent(mainscript.fluidenum.alcohol)==0f, "fixed bottle keeps its volume and loses spirit alcohol");
        Check(!BeerLiquid.ReplaceBottledAlcohol(contents), "fixed bottle conversion is idempotent");
        Check(!BeerLiquid.ReplaceBottledAlcohol(new mainscript.fluidcontainer()), "empty bottles stay empty");
        Check(!BeerLiquid.ReplaceBottledAlcohol(null), "missing contents stay missing");
        foreach(var other in new[]{mainscript.fluidenum.water,mainscript.fluidenum.gas,mainscript.fluidenum.oil,mainscript.fluidenum.diesel,mainscript.fluidenum.blood})
        {
            contents=new mainscript.fluidcontainer{maxC=.5f};
            contents.ChangeOne(.25f,mainscript.fluidenum.alcohol);contents.ChangeOne(.25f,other);
            Check(!BeerLiquid.ReplaceBottledAlcohol(contents) && BeerLiquid.Amount(contents)==0f, "mixed and fuel contents stay intact: "+other);
        }
    }

    static void Effects()
    {
        float hydration, alcohol;
        BeerLiquid.Effects(0.5f, out hydration, out alcohol);
        Check(Math.Abs(hydration - 0.45f) < 1e-6f && Math.Abs(alcohol - 0.5f / 3f) < 1e-6f, "half a liter: 0.45 water and a third as much alcohol equivalent (1/6)");
        BeerLiquid.Effects(0f, out hydration, out alcohol);
        Check(hydration == 0f && alcohol == 0f, "nothing drunk, no effect");
        BeerLiquid.Effects(-1f, out hydration, out alcohol);
        Check(hydration == 0f && alcohol == 0f, "negative amounts do nothing");
        BeerLiquid.Effects(float.NaN, out hydration, out alcohol);
        Check(hydration == 0f && alcohol == 0f, "NaN does nothing");
        BeerLiquid.Effects(float.PositiveInfinity, out hydration, out alcohol);
        Check(hydration == 0f && alcohol == 0f, "infinity does nothing");
    }
}
