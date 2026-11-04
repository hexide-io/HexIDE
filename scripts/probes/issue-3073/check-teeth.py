"""Revert preparation behavior temporarily, retaining API stubs so new tests can compile."""
from pathlib import Path
import os
import subprocess

root = Path(__file__).resolve().parents[3]
os.chdir(root)
paths = [Path(p) for p in ('IDE/HexIDE.Lsp/VBLspClient.cs', 'IDE/HexIDE.Lsp/LspClientRegistry.cs',
                          'IDE/HexIDE/Forms/Views/CodeEditorView.axaml.cs')]
original = {p: p.read_bytes() for p in paths}
(root / 'artifacts').mkdir(exist_ok=True)
try:
    for p in paths[:2]:
        text = p.read_text()
        start = text.index('    public async Task<PrepareRenameResult?> RequestPrepareRenameAsync')
        end = text.index('    public async Task<RenameResult?> RequestRenameAsync', start)
        text = text[:start] + '''    public Task<PrepareRenameResult?> RequestPrepareRenameAsync(string uri, Position position, CancellationToken ct = default)
        => Task.FromResult<PrepareRenameResult?>(null);

''' + text[end:]
        text = text.replace('                    Rename: new RenameClientCapabilities(),\n', '')
        p.write_text(text)
    p = paths[2]
    text = p.read_text()
    start = text.index('            await vm.FlushDocumentAsync();', text.index('    internal async Task RenameSymbolAsync'))
    end = text.index('            if (newName is null', start)
    p.write_text(text[:start] + '            var newName = await vm.ShowRenameInputBoxAsync(oldName);\n' + text[end:])
    for project in ('HexIDE.Tests', 'HexIDE.Integration.Tests'):
        with open(root / 'artifacts' / f'3073-before-{project}.log', 'w') as log:
            result = subprocess.run(['dotnet', 'test', f'IDE/{project}/', '-m:1',
                                     '-p:UseSharedCompilation=false', '--', '--filter-method', '*PrepareRename*',
                                     '--filter-method', '*Preparation*', '--results-directory',
                                     str(root / 'artifacts'), '--report-xunit', '--report-xunit-filename',
                                     f'3073-before-{project}.xml'], stdout=log, stderr=subprocess.STDOUT)
        print(f'{project}: exit {result.returncode}', flush=True)
finally:
    for p, content in original.items():
        p.write_bytes(content)
