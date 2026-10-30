"""Build and check a standalone GUI updater for the current patch sources."""
import base64
import hashlib
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

import build_release as release


def updater_source():
    entries = release.source_entries()
    gate = entries['tests/scripts/Test-Stability.ps1'].decode('utf-8-sig')
    line = next(x for x in gate.splitlines() if x.startswith('$signatureInputs='))
    names = re.findall("'([^']+)'", line)
    text = '\n'.join(name + ':' + hashlib.sha256(entries[name.replace('\\', '/')]).hexdigest().upper() for name in names)
    signature = hashlib.sha256(text.encode()).hexdigest().upper()
    payload = release.make_zip(entries)
    template = (release.ROOT / 'packaging/installer_template.py').read_text(encoding='utf-8')
    template = template[:template.rindex('\nif __name__ == "__main__":')]
    template = template.replace('__PAYLOAD_SHA256__', hashlib.sha256(payload).hexdigest().upper()).replace('__PAYLOAD_B64__', base64.b64encode(payload).decode('ascii'))
    footer = (release.ROOT / 'packaging/updater_gui.py').read_text(encoding='utf-8').replace('__SOURCE_SIGNATURE__', signature)
    return template + '\n' + footer


def main():
    output = release.ROOT / 'release/steam-update-exe'
    output.mkdir(parents=True, exist_ok=True)
    source = output / 'Roadworthy-Steam-Update.py'
    source.write_text(updater_source(), encoding='utf-8', newline='\n')
    compile(source.read_text(encoding='utf-8'), str(source), 'exec')
    subprocess.run([sys.executable, '-m', 'PyInstaller', '--noconfirm', '--onefile', '--windowed', '--noupx',
                    '--icon', str(release.APP_ICON), '--name', 'Roadworthy-Steam-Update',
                    '--distpath', str(output), '--workpath', str(release.ROOT / '.tools/steam-gui-work'),
                    '--specpath', str(release.ROOT / '.tools/steam-gui-spec'), str(source)], check=True)
    executable = output / 'Roadworthy-Steam-Update.exe'
    with tempfile.TemporaryDirectory(prefix='roadworthy-gui-check-') as directory:
        report = Path(directory) / 'report.txt'
        checked_exe = Path(directory) / executable.name
        shutil.copyfile(executable, checked_exe)
        subprocess.run([str(checked_exe), '--gui-self-test', str(report)], check=True, timeout=30)
        print(report.read_text())
    print('GUI updater: ' + str(executable))


if __name__ == '__main__':
    main()
