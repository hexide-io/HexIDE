# Parse depth probes (#1223)

This is a measurement harness, not a test that certifies stack safety. It deliberately
runs inputs that may kill their process. No production limit or VB6 acceptance rule
is changed. On Linux, Python 3 and the repository's .NET SDK are the only harness requirements.

From the repository root:

```sh
dotnet build tools/ParseDepthProbe/
python3 tools/ParseDepthProbe/run.py
```

Each host/shape/depth gets a fresh process. The runner disables core dumps, bounds
provider/runtime calls to 30 seconds, and bounds each wire reply to 10 seconds.
Results, phase markers, stack traces and generated `.bas` modules are written under
`artifacts/parse-depth/`. An exit of `-6` on Linux means SIGABRT; read its log to
establish whether it was a stack overflow. A timeout alone establishes no stack failure.
The wire probe sends initialize, initialized, didOpen, documentSymbol, shutdown and
exit over stdio. EOF is sent after exit to release the framework's input reader.
LSP logs use an isolated XDG data directory under the output directory.

Depth means the number of `+` operators or continuation sequences, not ANTLR rule
depth. Every module declares `a As Long`, assigns `1&`, then assigns an expression to
`x` inside `Sub Main`. Shapes are:

- `chain`: `a+a+…+a` (small enough at 100,000 to pass the LSP source-size gate).
- `continuations`: repeated ` _` + LF before a single `a`, separating lexical
  continuation handling from expression-tree depth.
- `continued-chain`: repeated `a+ _` + LF followed by `a`, combining both paths.

The hosts are interpreter construction/pre-pass plus `RunStartupSubMain`, direct LSP
diagnostics/folding, symbols, declared types, Option Explicit analysis (normally off),
and the real stdio server. Each visitor is isolated so a failing earlier visitor does
not conceal later ones. These sample three CST visitors, not every possible tree
walk or source shape. See [the measurement record](../../docs/parse-depth-probes.md).

For a live IDE check, use the generated modules in a disposable project whose startup
object is `Sub Main`. Open each module before running it: observe editor loading,
diagnostics and the procedure list first, then F5 separately. Use a separate IDE
process for each case, retain crash logs, and do not interpret successful headless
parsing as proof that the editor or debugger walks are safe. The live check belongs
to the supervising session.
