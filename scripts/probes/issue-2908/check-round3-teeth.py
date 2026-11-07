#!/usr/bin/env python3
"""Run #2908's new tests with the pre-round-3 receiver behaviour, retaining the new test seams.

Build the fixed tests first. This disables applyEdit dispatch/advertising and restores the external
showDocument refusal, builds only HexIDE.Lsp, and temporarily puts it in both test output directories.
Sources and binaries are restored byte-for-byte even if a check fails. Run with no other build active.
"""
import os
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[3]
source = root / 'IDE/HexIDE.Lsp/VBLspClient.cs'
outputs = [root / f'IDE/{project}/bin/Debug/net10.0' for project in
           ('HexIDE.Lsp', 'HexIDE.Tests', 'HexIDE.Integration.Tests')]
original = {source: source.read_bytes()}
for output in outputs:
    for name in ('HexIDE.Lsp.dll', 'HexIDE.Lsp.pdb'):
        path = output / name
        original[path] = path.read_bytes()
env = dict(os.environ, HEXIDE_FOREIGN_LSP_DOWNLOAD='0')
env['PATH'] = ':'.join(p for p in env['PATH'].split(':') if not p.startswith('/mnt/'))
logs = root / 'artifacts/2908-teeth'
logs.mkdir(parents=True, exist_ok=True)
try:
    text = original[source].decode('utf-8')
    text = text.replace('                    ApplyEdit: true,\n', '')
    text = text.replace('[JsonRpcMethod("workspace/applyEdit",', '[JsonRpcMethod("baseline/applyEdit",')
    text = text.replace('new(_client._showDocument is { } show', 'new(!p.External && _client._showDocument is { } show')
    source.write_text(text, encoding='utf-8')
    with (logs / 'build.log').open('w') as log:
        subprocess.run(['dotnet', 'build', str(root / 'IDE/HexIDE.Lsp/HexIDE.Lsp.csproj'),
                        '--no-restore', '-p:BuildProjectReferences=false'], cwd=root, env=env,
                       stdout=log, stderr=subprocess.STDOUT, check=True)
    for output in outputs[1:]:
        for name in ('HexIDE.Lsp.dll', 'HexIDE.Lsp.pdb'):
            (output / name).write_bytes((outputs[0] / name).read_bytes())
    for project, classes in (
        ('HexIDE.Tests', ['ServerApplyEditTests', 'ShowDocumentWireTests']),
        ('HexIDE.Integration.Tests', ['ExternalServerDocumentTests'])):
        arguments = ['dotnet', 'test', f'IDE/{project}/', '--no-build', '--']
        for cls in classes:
            arguments += ['--filter-class', '*' + cls]
        arguments += ['--report-xunit', '--report-xunit-filename', '2908-baseline.xml']
        with (logs / (project + '.log')).open('w') as log:
            result = subprocess.run(arguments, cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
        print(f'{project}: exit {result.returncode}; {logs / (project + ".log")}', flush=True)
        if result.returncode == 0:
            raise RuntimeError('Expected pre-fix test failures')
finally:
    for path, content in original.items():
        path.write_bytes(content)
