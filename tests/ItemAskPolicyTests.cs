using System;
using TldMp;

internal static class ItemAskPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        Check(!ItemAskSafety.MayRemove(true), "an existing item must never be removed on an ask");
        Check(ItemAskSafety.MayRemove(false), "a genuinely missing item must still be removed");
        Console.WriteLine("PASS: " + checks + " item-ask assertions; a held or stored item is retained, a missing item is removed.");
        return 0;
    }
}
