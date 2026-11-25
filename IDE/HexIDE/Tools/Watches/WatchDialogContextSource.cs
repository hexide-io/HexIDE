using System;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.Language;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tools;

// A module no open code window has a server's outline for lists the declarations the helpers registered for vb6 find, the
// language of every VB-family project's members (#5403); with none registered, it lists none.
public sealed class WatchDialogContextSource(IDocumentDockService dock, IProjectManager projects, ILanguageEditing languageEditing)
{
    public Task<(WatchContextModule[] Modules, string Context)> ReadAsync(string pausedContext)
    {
        var editor = dock.ActiveDocument as CodeEditorViewModel;
        var active = editor?.OpenDocument;
        var pausedModule = pausedContext.Split('.')[0];
        var project = active?.Project ?? projects.LoadedProjects.FirstOrDefault(p =>
            p.Forms.Any(f => f.Name == pausedModule) || p.Modules.Any(m => m.Name == pausedModule))
            ?? dock.ActiveDocument?.OpenDocument?.Project ?? projects.StartupProject;
        if (project is null) return Task.FromResult<(WatchContextModule[] Modules, string Context)>(([], pausedContext));
        var documents = project.Forms.Select(DocumentIdentity.For)
            .Concat(project.Modules.Select(DocumentIdentity.For)).ToArray();
        var modules = new WatchContextModule[documents.Length];
        DocumentSymbol[] activeSymbols = [];
        for (var i = 0; i < documents.Length; i++)
        {
            var document = documents[i];
            var open = dock.OpenDocuments.OfType<CodeEditorViewModel>()
                .FirstOrDefault(e => document.Equals(e.OpenDocument));
            var symbols = open is not null && open.OutlineSymbols.Count > 0 ? open.OutlineSymbols.ToArray()
                : languageEditing.DeclarationsFor(DocumentLanguage.Vb6)
                    .Declarations(open?.Document.Text ?? document.Module?.Code ?? document.Form!.Code).ToArray();
            var procedures = symbols.SelectMany(s => s.Flatten())
                .Where(s => s.Kind is SymbolKind.Method or SymbolKind.Function or SymbolKind.Property).ToArray();
            // VB6 lists them sorted, not in source order: a Sub declared last came before `X(Get)` (measured, #4401). How
            // names that differ only in case sort is unmeasured.
            modules[i] = new WatchContextModule(document.Name, procedures.Select(ProcedureName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
            if (document.Equals(active)) activeSymbols = procedures;
        }
        var inProcedure = editor is not null && activeSymbols.Any(s =>
            editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber - 1 >= s.Range.Start.Line &&
            editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber - 1 <= s.Range.End.Line);
        return Task.FromResult((modules, DefaultContext(active?.Name, inProcedure, pausedContext, modules)));
    }

    // Each Property accessor is its own entry and context, written as VB6 writes it: `X(Get)`, with no space (measured,
    // #4401). The bundled server and the collector name it `X (Get)`. The interpreter matches the same text, so a watch
    // scoped to `X(Get)` reads `<Out of context>` while `X(Let)` runs, as VB6's does.
    private static string ProcedureName(DocumentSymbol symbol)
    {
        if (symbol.Kind == SymbolKind.Property)
            foreach (var suffix in new[] { " (Get)", " (Let)", " (Set)" })
                if (symbol.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return symbol.Name[..^suffix.Length] + suffix[1..];
        return symbol.Name;
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
        // With no code window active and no program paused, VB6's dialog starts on (All Modules) (measured, #4452).
        if (activeModule is null && pausedContext == WatchExpression.AllProceduresContext)
            return WatchExpression.AllModulesContext;
        var pausedModule = pausedContext.Split('.')[0];
        // No-active-window fallbacks are unmeasured: prefer the paused module, then the project's first code module.
        return activeModule ?? modules.FirstOrDefault(m => m.Name == pausedModule)?.Name ?? modules.FirstOrDefault()?.Name
            ?? pausedContext;
    }
}
