using System;
using System.IO;
using System.Reflection;
using TldMp;
using GeneszSteamP2PBase;
using UnityEngine;

internal static class CarCargoTests
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
        Check(CarCargoPolicy.Moving(5f, 0f), "a car at 5 m/s is moving");
        Check(CarCargoPolicy.Moving(0f, 1f), "a spinning car is moving");
        Check(!CarCargoPolicy.Moving(0.1f, 0.05f), "a parked car keeps world poses (no change from the game)");
        Vector3 min = new Vector3(-1f, 0f, -2.5f), max = new Vector3(1f, 1.6f, 2.5f);
        Check(CarCargoPolicy.Inside(new Vector3(0.4f, 0.8f, 1.2f), min, max), "the glove box is inside the car");
        Check(CarCargoPolicy.Inside(new Vector3(1.2f, 0.8f, 0f), min, max), "the box is padded for items on the edge");
        Check(!CarCargoPolicy.Inside(new Vector3(0f, 0.5f, 4f), min, max), "an item behind the car is not cargo");
        Check(CarCargoPolicy.Loose(true, false, false, false, false), "a free item with a body is loose");
        Check(!CarCargoPolicy.Loose(true, true, false, false, false), "a stored item keeps its mount pose");
        Check(!CarCargoPolicy.Loose(true, false, true, false, false), "a fitted part keeps its attachment");
        Check(!CarCargoPolicy.Loose(true, false, false, true, false), "a held item keeps its hand pose");
        Check(!CarCargoPolicy.Loose(true, false, false, false, true), "a car is never cargo");
        Check(!CarCargoPolicy.Loose(false, false, false, false, false), "an item without a body is left alone");
        Check(CarCargoPolicy.ColliderAnchor == -200000, "the anchor is the game's collider-0 local-pose index");

        // The game's own encoder must produce the layout the guest reads.
        byte[] local = DataByteConverterScript.GetBytes(msgType.itemPosLocal, 7u, 9u, CarCargoPolicy.ColliderAnchor, new Vector3(1f, 2f, 3f), new Vector3(10f, 20f, 30f));
        Check(local.Length == 37 && local[0] == 17, "itemPosLocal is 37 bytes");
        Check(BitConverter.ToUInt32(local, 1) == 7 && BitConverter.ToUInt32(local, 5) == 9 && BitConverter.ToInt32(local, 9) == -200000, "id, car and anchor sit at bytes 1, 5 and 9");
        Check(BitConverter.ToSingle(local, 13) == 1f && BitConverter.ToSingle(local, 21) == 3f && BitConverter.ToSingle(local, 25) == 10f && BitConverter.ToSingle(local, 33) == 30f, "position at 13, rotation at 25");
        Check(StablePackets.Valid(local), "the rewritten pose passes the packet validator");

        // Ordering: world and car-relative poses of one item share one order.
        byte[] world = DataByteConverterScript.GetBytes(msgType.itemPosGlobal, 7u, 3, new Vector3d(1, 2, 3), new Vector3(0, 0, 0));
        Check(PoseSequence.Key(world) == PoseSequence.Key(local), "a late world pose cannot override a newer car-relative pose");
        byte[] other = DataByteConverterScript.GetBytes(msgType.itemPosLocal, 8u, 9u, CarCargoPolicy.ColliderAnchor, Vector3.zero, Vector3.zero);
        Check(PoseSequence.Key(other) != PoseSequence.Key(local), "each item keeps its own order");

        Console.WriteLine("PASS: " + checks + " car cargo assertions; loose items in a moving car are sent relative to the car in the game's own format, parked cars and stored/fitted/held items are untouched.");
        return 0;
    }
}
