#!/usr/bin/env python3
"""Replay the pre-feature receiver and inert UI entry points, then restore all files.

The new signatures/contracts remain so the regression tests compile. No handler or
capability remains; the new UI entry points do nothing. Run from this clone only.
"""
from pathlib import Path
import os
import subprocess

root = Path(__file__).resolve().parents[2]
logs = root / 'artifacts/2908'
logs.mkdir(parents=True, exist_ok=True)
for folder in ('runtime', 'nuget-http'):
    (logs / folder).mkdir(mode=0o700, parents=True, exist_ok=True)
paths = [root / name for name in (
    'IDE/HexIDE.Lsp/VBLspClient.cs',
    'IDE/HexIDE/IDE/ServerMessagePrompt.cs',
    'IDE/HexIDE/IDE/WindowManager.cs')]
saved = {path: path.read_bytes() for path in paths}
env = os.environ.copy()
env['PATH'] = ':'.join(part for part in env['PATH'].split(':') if not part.startswith('/mnt/'))
cached_java = Path.home() / '.jre/jdk-11.0.26+4-jre/bin'
if cached_java.is_dir():
    env['PATH'] = str(cached_java) + ':' + env['PATH']
env['XDG_RUNTIME_DIR'] = str(logs / 'runtime')
env['NUGET_HTTP_CACHE_PATH'] = str(logs / 'nuget-http')
env['DOTNET_CLI_USE_MSBUILD_SERVER'] = '0'
env['UseSharedCompilation'] = 'false'
env['MSBUILDDISABLENODEREUSE'] = '1'
env['HEXIDE_FOREIGN_LSP_DOWNLOAD'] = '0'
try:
    path = paths[0]
    text = saved[path].decode()
    start = text.index('        [JsonRpcMethod("window/showMessageRequest",')
    end = text.index('        [JsonRpcMethod("window/showDocument",', start)
    text = text[:start] + text[end:]
    text = text.replace('new ShowDocumentClientCapabilities(),\n                    ShowMessage: new ShowMessageClientCapabilities(new MessageActionItemClientCapabilities())',
                        'new ShowDocumentClientCapabilities()')
    path.write_text(text)
    path = paths[1]
    text = saved[path].decode()
    start = text.index('        var buttons = new StackPanel')
    text = text[:start] + '        await Task.CompletedTask;\n        return null;\n    }\n}\n'
    path.write_text(text)
    path = paths[2]
    text = saved[path].decode().replace('        _nativeDialogs++;\n        try { return await show(); }\n        finally { _nativeDialogs--; }',
                                      '        _nativeDialogs = 0;\n        return await show();')
    path.write_text(text)
    for project, test_class, expected in (
        ('HexIDE.Tests', 'ShowMessageRequestWireTests', 8),
        ('HexIDE.Integration.Tests', 'ServerMessagePromptTests', 5)):
        command = ['dotnet', 'test', f'IDE/{project}/', '--no-restore', '-m:1',
                   '-nodeReuse:false', '-p:UseSharedCompilation=false', '--',
                   '--filter-class', f'*{test_class}']
        with (logs / f'before-{project}.log').open('w') as output:
            result = subprocess.run(command, cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        report = (logs / f'before-{project}.log').read_text()
        summary = f'Failed! - Failed: {expected}, Passed: 0, Skipped: 0, Total: {expected}'
        if result.returncode == 0 or summary not in report:
            raise RuntimeError(f'{test_class}: expected {expected} failures; inspect the log')
        print(f'{test_class}: {expected} failed, 0 passed, 0 skipped', flush=True)
finally:
    for path, contents in saved.items():
        path.write_bytes(contents)
