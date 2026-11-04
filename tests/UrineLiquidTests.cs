using System;
using System.Collections.Generic;
using TldMp;
public class mainscript {
 public enum fluidenum {gas,oil,alcohol,water,blood,diesel,milk}
 public class fluid {public fluidenum type;public float amount;public fluid(fluidenum t,float a){type=t;amount=a;}}
 public class fluidcontainer {public List<fluid> fluids=new List<fluid>(); public float changed;public void ChangeSync(float f){changed+=Math.Abs(f);}}
}
public static class UrineLiquidTests {
 static int n;static void Check(bool b){n++;if(!b)throw new Exception("check "+n);}
 public static void Main(){
  var bladder=new mainscript.fluidcontainer();bladder.fluids.Add(new mainscript.fluid(mainscript.fluidenum.alcohol,.25f));bladder.fluids.Add(new mainscript.fluid(mainscript.fluidenum.water,.5f));
  Check(UrineLiquid.NormalizeBladder(bladder));Check(bladder.fluids.Count==1);Check((int)bladder.fluids[0].type==7);Check(bladder.fluids[0].amount==.75f);Check(bladder.changed==.75f);Check(!UrineLiquid.NormalizeBladder(bladder));
  bladder.fluids.Add(new mainscript.fluid((mainscript.fluidenum)7,.25f));Check(UrineLiquid.NormalizeBladder(bladder));Check(bladder.fluids.Count==1 && bladder.fluids[0].amount==1f);
  var invalid=new mainscript.fluidcontainer();invalid.fluids.Add(new mainscript.fluid(mainscript.fluidenum.water,float.NaN));Check(!UrineLiquid.NormalizeBladder(invalid));Check(float.IsNaN(invalid.fluids[0].amount));
  var empty=new mainscript.fluidcontainer();Check(!UrineLiquid.NormalizeBladder(empty));Check(!UrineLiquid.NormalizeBladder(null));
  Console.WriteLine("PASS "+n+" bladder normalization checks");
 }
}
