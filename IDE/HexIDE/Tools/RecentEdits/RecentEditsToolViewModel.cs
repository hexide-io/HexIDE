using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public partial class RecentEditsToolViewModel : Tool
{
    private readonly Func<IEditorService> editorService;
    [Notify] private IReadOnlyList<RecentEditLocation> entries = Array.Empty<RecentEditLocation>();
    [Notify] private RecentEditLocation? selectedEntry;

    public RecentEditsToolViewModel(RecentEditHistory history, Func<IEditorService> editorService, ILocalizationService localization)
    {
        this.editorService = editorService;
        localization.BindTitle(this, "Str.Tool.RecentEdits.Title");
        CanPin = false;
        CanClose = true;
        history.Changed += () => Entries = history.Entries.ToArray();
    }

    private void OnSelectedEntryChanged()
    {
        if (SelectedEntry is { } entry)
            editorService().NavigateTo(DocumentWireName.For(entry.Document), entry.Line, 1);
    }
}
