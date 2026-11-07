#!/usr/bin/env python3
"""Build-only guard bypasses in an isolated copy of HEAD; never patch the working tree."""
import pathlib, subprocess, tarfile, io, sys
round3 = "--round3" in sys.argv
here = pathlib.Path(__file__).resolve().parent
root = here.parents[2]
scratch = here / '.scratch'
if scratch.exists():
    raise SystemExit('Remove this probe\'s .scratch directory before preparing again')
scratch.mkdir()
with tarfile.open(fileobj=io.BytesIO(subprocess.check_output(['git', 'archive', 'HEAD'], cwd=root))) as archive:
    archive.extractall(scratch, filter='data')
patches = {
 'IDE/HexIDE.Runtime/Interpreter/ParseDepthGuard.cs': [('DefaultMaxDepth = 300', 'DefaultMaxDepth = 1000000')],
 'LspServer/HexIDE.VbLspServer/VbScopeAnalyzer.cs': [('MaxDepth = 500', 'MaxDepth = 1000000')],
 'LspServer/HexIDE.VbLspServer/VbSymbolProvider.cs': [('MaxDepth = 500', 'MaxDepth = 1000000')],
 'IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs': [('if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())', 'if (false)')],
}
if round3:
    del patches['IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs']
    # Keep both original conditional-expression checks at 300 while lifting ordinary module parsing.
    patches['IDE/HexIDE.Runtime/Interpreter/ConditionalCompilation/ConstantExpression.cs'] = [
        ('new ParseDepthGuard()', 'new ParseDepthGuard(300)'),
        ('item.Depth > ParseDepthGuard.DefaultMaxDepth', 'item.Depth > 300')]
for name, replacements in patches.items():
    path = scratch / name
    text = path.read_text(encoding='utf-8-sig')
    for old, new in replacements:
        if old not in text:
            raise SystemExit(f'Patch no longer applies: {name}: {old}')
        text = text.replace(old, new)
    # The scratch-only false condition intentionally leaves the original throw unreachable.
    if name.endswith('ExpressionExecutor.cs'):
        text = '#pragma warning disable CS0162\n' + text
    path.write_text(text)
print(subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip())
