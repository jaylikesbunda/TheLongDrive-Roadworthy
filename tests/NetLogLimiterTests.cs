using System;
using TldMp;

internal static class NetLogLimiterTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        DigitStripping();
        PerKeyLimit();
        WindowRollOver();
        ConstructorRefusesBadValues();
        Console.WriteLine("PASS: " + checks + " net-log limiter assertions; digits share a key, 5 per key per 10s, one summary on roll-over, bounded key table.");
        return 0;
    }

    static void DigitStripping()
    {
        Check(NetLogLimiter.Key("Length: 12") == NetLogLimiter.Key("Length: 480"), "numbered variants share one key");
        Check(NetLogLimiter.Key("Unknown Message Type: 7 Length: 3") == "Unknown Message Type:  Length: ", "digits are removed and other text kept");
        Check(NetLogLimiter.Key("Received: itemPosLocal") != NetLogLimiter.Key("Received: itemPosRemote"), "different text stays different");
        Check(NetLogLimiter.Key(null) == "" && NetLogLimiter.Key("") == "", "null and empty messages have an empty key");
    }

    static void PerKeyLimit()
    {
        var limiter = new NetLogLimiter(5, 10.0);
        string summary;
        int allowed = 0;
        for (int i = 0; i < 20; i++)
        {
            if (limiter.Offer("Unknown Message Type: " + i, 0.0 + i * 0.01, out summary)) allowed++;
            Check(summary == null, "no summary before a window rolls over");
        }
        Check(allowed == 5, "exactly 5 messages per key in the first window");

        // A different key has its own budget.
        int other = 0;
        for (int i = 0; i < 5; i++) if (limiter.Offer("Received: itemPosLocal " + i, 0.5, out summary)) other++;
        Check(other == 5, "a second key gets its own 5 messages");
        Check(limiter.TrackedKeys == 2, "two keys tracked");
    }

    static void WindowRollOver()
    {
        var limiter = new NetLogLimiter(5, 10.0);
        string summary;
        for (int i = 0; i < 8; i++) limiter.Offer("flood 1", 0.0, out summary);
        // 3 were suppressed. Still inside the window: no summary yet.
        Check(!limiter.Offer("flood 2", 9.9, out summary) && summary == null, "still suppressed inside the window, no summary");

        // Roll-over: the first same-key message after 10 s reports the suppressed count and is itself allowed.
        bool allowed = limiter.Offer("flood 3", 10.0, out summary);
        Check(allowed, "first message after the window is allowed again");
        Check(summary == "[TLD MP net-log] suppressed 4 similar messages: flood ", "summary counts the suppressed messages of the closed window");

        // The new window starts fresh: no stale summary on the next message.
        string next;
        Check(limiter.Offer("flood 4", 10.5, out next) && next == null, "new window has no stale summary");
        for (int i = 0; i < 3; i++) limiter.Offer("flood 5", 10.6, out next);
        Check(!limiter.Offer("flood 6", 10.7, out next), "new window also caps at 5");
        Check(limiter.Offer("other", 10.7, out next) && next == null, "an unrelated key is unaffected by another key's window");

        // Digits in a flood do not reset the budget.
        var numbered = new NetLogLimiter(2, 10.0);
        Check(numbered.Offer("x 1", 0, out next) && numbered.Offer("x 2", 0, out next) && !numbered.Offer("x 3", 0, out next), "numbered floods share the budget");
    }

    static void ConstructorRefusesBadValues()
    {
        bool threw = false;
        try { new NetLogLimiter(0, 10.0); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "zero limit refused");
        threw = false;
        try { new NetLogLimiter(5, 0.0); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "zero window refused");
        threw = false;
        try { new NetLogLimiter(5, double.NaN); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "NaN window refused");

        var many = new NetLogLimiter(NetLogLimiter.DefaultLimit, NetLogLimiter.DefaultWindow);
        string summary;
        for (int i = 0; i < NetLogLimiter.MaxKeys + 10; i++) many.Offer("distinct " + new string((char)('a' + i % 26), 1) + i + Convert.ToString(i, 2).Replace('0', 'z').Replace('1', 'y'), 0.0, out summary);
        Check(many.TrackedKeys <= NetLogLimiter.MaxKeys, "key table stays bounded");
    }
}
