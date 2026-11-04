import os, subprocess, tempfile, unittest
from pathlib import Path
class ReverseFluid(unittest.TestCase):
 def test_production_transfer(self):
  root=Path(__file__).resolve().parents[1]
  source=(root/'runtime/StableGameBridge.cs').read_text()
  a=source.index('        sealed class FluidPromptState');b=source.index('        public static void TankPourTick(',a)
  fixture=(root/'tests/ReverseFluidFixture.cs').read_text().replace('HELPER',source[a:b])
  with tempfile.TemporaryDirectory(prefix='roadworthy-fluid-') as d:
   p=Path(d);cs=p/'test.cs';exe=p/'test.exe';cs.write_text(fixture)
   compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
   subprocess.run([str(compiler),'/nologo','/out:'+str(exe),str(cs)],check=True)
   subprocess.run([str(exe)],check=True)
