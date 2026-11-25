# Product references into the provider

The remaining references across #3411's product/provider boundary, guarded by `ProviderBoundaryTests`
(#5388). This list can only shrink: remove a row when the reference goes. Move new dependencies into
the provider or behind a `HexIDE.Core` contract; do not add a row without the move's lead agreeing.

The test reads built metadata (including generated code and compiled AXAML), project references and
cross-boundary `InternalsVisibleTo` grants. It never writes this file. Debug checks both additions and
removals; Release checks additions only because Desktop's automation server is Debug-only.
`HexIDE.Browser` is classified but not built by this test project, so its DLL is omitted.

| kind | from | to |
|---|---|---|
| assembly | HexIDE | HexIDE.Runtime |
| assembly | HexIDE.Desktop | HexIDE.Runtime |
| internals | HexIDE.Core | HexIDE.Runtime |
| internals | HexIDE.Core | HexIDE.Runtime.Integration.Tests |
| internals | HexIDE.Core | HexIDE.Runtime.Tests |
| internals | HexIDE.Runtime | HexIDE.Integration.Tests |
| internals | HexIDE.Runtime | HexIDE.Tests |
| project | HexIDE | HexIDE.Runtime |
| project | HexIDE.Desktop | HexIDE.Runtime |
| project | HexIDE.Desktop | HexIDE.VbLspServer |
| project | HexIDE.Integration.Tests | HexIDE.Runtime |
| project | HexIDE.Integration.Tests | HexIDE.Standalone |
| project | HexIDE.Tests | HexIDE.Runtime |
| project | HexIDE.Tests | HexIDE.Standalone |
| type | HexIDE | HexIDE.Runtime.AvaloniaInterop.DesignPositions |
| type | HexIDE | HexIDE.Runtime.BuiltinControls.VBLine |
| type | HexIDE | HexIDE.Runtime.Components.ComponentBaseClass |
| type | HexIDE | HexIDE.Runtime.Components.FormComponentClass |
| type | HexIDE | HexIDE.Runtime.Components.InBox.FlatScrollBarComponentClass |
| type | HexIDE | HexIDE.Runtime.Components.InBox.ImageListComponentClass |
| type | HexIDE | HexIDE.Runtime.Components.InBox.InBoxComponentClass |
| type | HexIDE | HexIDE.Runtime.Components.InBox.RuntimeLibrary |
| type | HexIDE | HexIDE.Runtime.Components.InBox.SSTabComponentClass |
| type | HexIDE | HexIDE.Runtime.Components.InBox.StatusBarComponentClass |
| type | HexIDE | HexIDE.Runtime.Components.LineComponentClass |
| type | HexIDE | HexIDE.Runtime.Components.MenuComponentClass |
| type | HexIDE | HexIDE.Runtime.Interpreter.ExecutionEnvironment |
| type | HexIDE | HexIDE.Runtime.Interpreter.IBasicStandardLibrary |
| type | HexIDE | HexIDE.Runtime.Interpreter.ModuleExecutionContext |
| type | HexIDE | HexIDE.Runtime.Interpreter.VbObject |
| type | HexIDE | HexIDE.Runtime.Serialization.UnmodelledControl |
| type | HexIDE | HexIDE.Runtime.StandaloneStandardLib |
| type | HexIDE | HexIDE.Runtime.VBFormRuntime |
| type | HexIDE | HexIDE.Runtime.VBLoader |
| type | HexIDE | HexIDE.Runtime.VBProgram |
| type | HexIDE | HexIDE.Runtime.VBWindowContext |
| type | HexIDE.Desktop | HexIDE.Runtime.AvaloniaInterop.DesignPositions |
| type | HexIDE.Desktop | HexIDE.Runtime.BasicProvider |
| type | HexIDE.Desktop | HexIDE.Runtime.Components.FormComponentClass |
| type | HexIDE.Desktop | HexIDE.Runtime.Components.InBox.SSTabComponentClass |
| type | HexIDE.Desktop | HexIDE.Runtime.Components.MenuComponentClass |
| type | HexIDE.Desktop | HexIDE.Runtime.Serialization.UnmodelledControl |
