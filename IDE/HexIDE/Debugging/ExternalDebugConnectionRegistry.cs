using System;
using System.Collections.Generic;
using HexIDE.Lsp;

namespace HexIDE.Debugging;

public sealed class ExternalDebugConnectionRegistry : ILanguageConnectionRegistry
{
    private readonly ILanguageConnectionRegistry languages;
    private readonly ExternalDebugService debugger;
    public ExternalDebugConnectionRegistry(ILspClient client, ExternalDebugService debugger)
    {
        languages = (ILanguageConnectionRegistry)client;
        this.debugger = debugger;
        languages.ConnectionsChanged += (_, _) => ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        debugger.Changed += () => ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }
    public IReadOnlyList<OwnedDiagnostic> Diagnostics => languages.Diagnostics;
    public IReadOnlyList<LanguageServerConnection> Connections => debugger.Connection is { } connection
        ? [.. languages.Connections, connection] : languages.Connections;
    public IReadOnlyList<LanguageServerConfigProblem> ConfigurationProblems => languages.ConfigurationProblems;
    public event EventHandler? ConnectionsChanged;
}
