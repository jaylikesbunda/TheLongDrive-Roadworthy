using System;
using TldMp;

public static class JoinCompatibilityTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static bool Has(string text,string part){return text!=null && text.IndexOf(part,StringComparison.Ordinal)>=0;}

    public static int Main()
    {
        try
        {
            string host=JoinCompatibility.Compose("0.2.0-pre2","BUILDAAAAAAAAAAAAAAAAAA","GAMEAAAAAAAAAAAAAAAAAA","sdr");
            Check(host.StartsWith("RW2|ver=0.2.0-pre2|build=BUILDAAAAAAAAAAA|game=GAMEAAAAAAAAAAAA|net=sdr",StringComparison.Ordinal),"compose format changed: "+host);
            Check(host==JoinCompatibility.Compose("0.2.0-pre2","BUILDAAAAAAAAAAAAAAAAAA","GAMEAAAAAAAAAAAAAAAAAA","sdr"),"compose is not deterministic");

            string old="0123456789ABCDEF0123456789ABCDEF";
            Check(Has(JoinCompatibility.Describe(host,old),"older version") && Has(JoinCompatibility.Describe(host,old),"v0.2.0-pre2"),"pre-RW2 guest not described as older");
            string otherVersion=JoinCompatibility.Compose("0.2.0-pre1","BUILDB","GAMEAAAAAAAAAAAAAAAAAA","sdr");
            string text=JoinCompatibility.Describe(host,otherVersion);
            Check(Has(text,"host v0.2.0-pre2") && Has(text,"you v0.2.0-pre1"),"version mismatch does not name both versions: "+text);
            Check(Has(JoinCompatibility.Describe(host,JoinCompatibility.Compose("0.2.0-pre2","BUILDB","GAMEAAAAAAAAAAAAAAAAAA","sdr")),"builds differ"),"build mismatch not described");
            Check(Has(JoinCompatibility.Describe(host,JoinCompatibility.Compose("0.2.0-pre2","BUILDAAAAAAAAAAAAAAAAAA","GAMEB","sdr")),"game files differ"),"game mismatch not described");
            Check(Has(JoinCompatibility.Describe(host,JoinCompatibility.Compose("0.2.0-pre2","BUILDAAAAAAAAAAAAAAAAAA","GAMEAAAAAAAAAAAAAAAAAA","p2p")),"transport"),"transport mismatch not described");
            Check(Has(JoinCompatibility.Describe(host+"|exp-mp-1",host),"host has New Buggy multiplayer"),"host-only experimental flag not described");
            Check(Has(JoinCompatibility.Describe(host,host+"|exp-mp-1"),"You have New Buggy multiplayer"),"guest-only experimental flag not described");
            // Version is reported before build/game so the most actionable fix comes first.
            Check(Has(JoinCompatibility.Describe(host,JoinCompatibility.Compose("0.1.9","X","Y","p2p")),"versions differ"),"version is not the first reported difference");

            Check(Has(JoinCompatibility.Friendly("Patch/game content mismatch. Install the same build and map content on every peer."),"different Roadworthy release"),"old host rejection text not translated");
            Check(Has(JoinCompatibility.Friendly("SDR peer disconnected (5003): timed out"),"over Steam"),"transport loss not translated");
            Check(JoinCompatibility.Friendly(text)==text,"an already friendly reason was changed");
            Check(JoinCompatibility.Friendly(null)!=null && JoinCompatibility.Friendly(new string('x',500)).Length<=203,"empty or long errors not handled");

            Console.WriteLine("PASS: "+checks+" join compatibility checks (signature fields, named version/build/game/transport/experimental mismatches, old-host and transport-loss wording).");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
