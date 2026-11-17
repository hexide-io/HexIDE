using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tools;

public sealed class WatchDialogContextSource(IDocumentDockService dock, IProjectManager projects, ILspClient client)
{
    public async Task<(WatchContextModule[] Modules, string Context)> ReadAsync(string pausedContext)
    {
        var editor = dock.ActiveDocument as CodeEditorViewModel;
        var active = editor?.OpenDocument;
        var pausedModule = pausedContext.Split('.')[0];
        var project = active?.Project ?? projects.LoadedProjects.FirstOrDefault(p =>
            p.Forms.Any(f => f.Name == pausedModule) || p.Modules.Any(m => m.Name == pausedModule))
            ?? dock.ActiveDocument?.OpenDocument?.Project ?? projects.StartupProject;
        if (project is null) return ([], pausedContext);
        var documents = project.Forms.Select(DocumentIdentity.For)
            .Concat(project.Modules.Select(DocumentIdentity.For)).ToArray();
        var modules = new WatchContextModule[documents.Length];
        DocumentSymbol[] activeSymbols = [];
        for (var i = 0; i < documents.Length; i++)
        {
            var document = documents[i];
            var open = dock.OpenDocuments.OfType<CodeEditorViewModel>()
                .FirstOrDefault(e => document.Equals(e.OpenDocument));
            var symbols = open is not null ? open.OutlineSymbols.ToArray()
                : client.IsRunning ? await client.RequestDocumentSymbolsAsync(DocumentWireName.For(document), CancellationToken.None) : [];
            var procedures = symbols.SelectMany(s => s.Flatten())
                .Where(s => s.Kind is SymbolKind.Method or SymbolKind.Function or SymbolKind.Property).ToArray();
            modules[i] = new WatchContextModule(document.Name, procedures.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            if (document.Equals(active)) activeSymbols = procedures;
        }
        var inProcedure = editor is not null && activeSymbols.Any(s =>
            editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber - 1 >= s.Range.Start.Line &&
            editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber - 1 <= s.Range.End.Line);
        return (modules, DefaultContext(active?.Name, inProcedure, pausedContext, modules));
    }

    /// <summary>
    /// The context a new watch starts with (#4354). Only the declarations case is measured: the active module, with
    /// (All Procedures). With the caret inside a procedure, or no code window active, main's choice stays: the paused
    /// frame's <c>Module.Procedure</c> whole, never its procedure under another module.
    /// </summary>
    internal static string DefaultContext(string? activeModule, bool caretInProcedure, string pausedContext,
        System.Collections.Generic.IReadOnlyList<WatchContextModule> modules)
    {
        if ((activeModule is null || caretInProcedure) && pausedContext.Contains('.'))
            return pausedContext;
        var pausedModule = pausedContext.Split('.')[0];
        // No-active-window fallbacks are unmeasured: prefer the paused module, then the project's first code module.
        return activeModule ?? modules.FirstOrDefault(m => m.Name == pausedModule)?.Name ?? modules.FirstOrDefault()?.Name
            ?? pausedContext;
    }
}
