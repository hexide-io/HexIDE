"""Run #3074 tests with the pre-feature behavior, retaining API declarations to compile.

Run from the repository root. Restores every changed source in finally.
"""
from pathlib import Path
import os
import re
import subprocess

paths = [Path(p) for p in (
    'IDE/HexIDE.Lsp/VBLspClient.cs',
    'IDE/HexIDE.Lsp/LspClientRegistry.cs',
    'IDE/HexIDE/MainView.axaml',
    'IDE/HexIDE/Forms/Views/CodeEditorView.axaml.cs',
)]
originals = {p: p.read_bytes() for p in paths}
env = dict(os.environ, HEXIDE_FOREIGN_LSP_DOWNLOAD='0')
runtime = Path('artifacts/3074-runtime').resolve()
runtime.mkdir(parents=True, exist_ok=True)
env['XDG_RUNTIME_DIR'] = str(runtime)
env['PATH'] = ':'.join(p for p in env['PATH'].split(':') if not p.startswith('/mnt/'))
try:
    for p in paths:
        text = p.read_text()
        if p.name == 'VBLspClient.cs':
            text = text.replace('                    Implementation: new NavigationClientCapabilities(),\n', '')
            text = text.replace('                    TypeDefinition: new NavigationClientCapabilities(),\n', '')
        elif p.name == 'LspClientRegistry.cs':
            for name in ('Implementation', 'TypeDefinition'):
                text = text.replace(f'FirstAnswerAsync(uri, c => c.Request{name}Async(uri, position, ct), ct);',
                                    'Task.FromResult<Location[]?>(null);')
        elif p.name == 'MainView.axaml':
            text = '\n'.join(line for line in text.split('\n') if 'ApplicationCommands.GoToImplementationCommand' not in line
                             and 'ApplicationCommands.GoToTypeDefinitionCommand' not in line)
        else:
            for name in ('Implementation', 'TypeDefinition'):
                text = text.replace(f'DataContext is CodeEditorViewModel vm && vm.CanGoTo{name}()', 'false')
        p.write_text(text)
    for project in ('HexIDE.Tests', 'HexIDE.Integration.Tests'):
        subprocess.run(['dotnet', 'build', project + '/'], cwd='IDE', env=env, check=True, timeout=1200)
        result = subprocess.run(['dotnet', project + '/bin/Debug/net10.0/' + project + '.dll',
                                 '-class', '*ImplementationNavigation*'],
                                cwd='IDE', env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=120)
        print(result.stdout, flush=True)
        expected = 12 if project == 'HexIDE.Tests' else 6
        if not re.search(r'failed:\s*' + str(expected) + r'\b', result.stdout, re.IGNORECASE):
            raise RuntimeError(project + ': expected failure count missing; inspect the output')
        if result.returncode == 0:
            raise RuntimeError(project + ': tests unexpectedly passed without the feature')
finally:
    for p, content in originals.items():
        p.write_bytes(content)
