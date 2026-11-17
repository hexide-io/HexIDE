"""Reversible Linux mutation checks for #3426; run after the focused tests pass.

Local reproducer; the required external mutation-check.ps1 runs are recorded in
3426-validation.txt.
All source is restored even when a test/build fails. Logs go to artifacts/3426/.
"""
import os
import re
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[2]
member = root / 'IDE/HexIDE.Runtime/AvaloniaInterop/SSTabMembers.cs'
strip = root / 'IDE/HexIDE.Runtime/BuiltinControls/SSTabStrip.cs'
original = member.read_text()
original_strip = strip.read_text()
logs = root / 'artifacts/3426'
logs.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, UseSharedCompilation='false')
env['PATH'] = ':'.join(p for p in env['PATH'].split(':') if not p.startswith('/mnt/'))


def run(name, text, drawing=False):
    member.write_text(text)
    with (logs / (name + '.log')).open('w') as log:
        result = subprocess.run(['dotnet', 'test', 'HexIDE.Runtime.Integration.Tests/', '--',
                                 '--filter-class', '*SSTabDrawingTests' if drawing else '*SSTabPictureTests'],
                                cwd=root / 'IDE', env=env, stdout=log, stderr=subprocess.STDOUT)
    summary = (logs / (name + '.log')).read_text()
    if not re.search(r'Failed! - Failed: [1-9]', summary):
        raise RuntimeError(name + ' did not produce failing tests; inspect its log')
    print(name, 'exit', result.returncode, flush=True)
    for line in (logs / (name + '.log')).read_text().splitlines():
        if 'Failed! -' in line or 'Passed! -' in line or 'error CS' in line:
            print(line, flush=True)


try:
    # The two original picture refusals, preserving helpers needed by other changed callers.
    baseline = original.replace('_ => new Vb6Value(tab.TabPicture(index)),',
                                '_ => throw new NotImplementedException("SSTab TabPicture is not implemented"),')
    baseline = baseline.replace('default: tab.TabPicture(index, Picture(value)); break;',
                                'default: throw new NotImplementedException("SSTab TabPicture is not implemented");')
    run('before-picture-support', baseline)
    start = original_strip.index('            if (owner.TabPicture(i).Bitmap')
    end = original_strip.index('            var header = new Border', start)
    strip.write_text(original_strip[:start] + original_strip[end:])
    run('before-strip-support', original, drawing=True)
    strip.write_text(original_strip)
    run('stored-object-return', original.replace('new Vb6Value(tab.TabPicture(index))',
                                                 'new Vb6Value(new PanelPicture(tab.TabPicture(index).FileBytes))'))
    run('control-picture-path', original.replace('if (value.Value is VBPictureBox box) return ControlPicture(box.PictureData);', '')
        .replace('if (value.Value is VBImage image) return ControlPicture(image.Picture);', ''))
    run('type-13-check', original.replace('throw new VBRunTimeException(VBStandardError.TypeMismatch);',
                                        'return new PanelPicture([]);'))
finally:
    member.write_text(original)
    strip.write_text(original_strip)
