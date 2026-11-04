import os,subprocess,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
class GrassTransitionTests(unittest.TestCase):
 def test_grass(self):
  with tempfile.TemporaryDirectory() as d:
   exe=Path(d)/'test.exe';csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe';subprocess.run([str(csc),'/nologo','/out:'+str(exe),str(ROOT/'tests/GrassTransitionTests.cs'),str(ROOT/'runtime/GrassTransition.cs')],check=True);subprocess.run([str(exe)],check=True)
