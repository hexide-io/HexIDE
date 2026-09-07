using System.Collections.ObjectModel;
using Avalonia.Threading;
using Dock.Model.Mvvm.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools.LanguageServers;

/// <summary>
/// One language and everything attached to it.
/// </summary>
/// <remarks>
/// Grouped by language rather than by protocol because that is the question people arrive with — "what have
/// I got for VB6, and is any of it working" — not "which of these speak LSP". It also makes one of the
/// failure modes structural instead of incidental: two servers claiming one language land adjacent, in
/// priority order, so "I cannot tell which one answered" is visible rather than deducible.
/// </remarks>
public sealed class LanguageServerGroupViewModel(string language, IReadOnlyList<LanguageServerRowViewModel> rows)
{
    /// <summary>The language id as configured. Not translated — it is an identifier the user wrote.</summary>
    public string Language { get; } = string.IsNullOrWhiteSpace(language) ? "(none)" : language;

    public IReadOnlyList<LanguageServerRowViewModel> Rows { get; } = rows;
}

/// <summary>
/// What is attached, what it claims, and whether it is working.
///
/// <para>
/// The registry has known all of this since it was written and nothing ever read it — before this,
/// <c>ILanguageConnectionRegistry</c> had exactly two references in the whole tree: the DI binding and the
/// class implementing it. Every failure mode on this seam presented identically, as nothing happening and
/// nothing saying why (hexide-io/HexIDE#259).
/// </para>
///
/// <para>
/// A <see cref="Document"/> rather than a docked <see cref="Tool"/>, and deliberately not an Options page:
/// Options is modal, and a surface you must close in order to open the file that provokes the failure you
/// are diagnosing is the wrong surface.
/// </para>
/// </summary>
public partial class LanguageServersToolViewModel : Document
{
    private readonly ILanguageConnectionRegistry _registry;
    private readonly ILocalizationService _localization;

    public ObservableCollection<LanguageServerGroupViewModel> Groups { get; } = [];
    public ObservableCollection<LanguageServerConfigProblem> Problems { get; } = [];

    [Notify] private bool hasProblems;
    [Notify] private bool hasNoServers;

    public LanguageServersToolViewModel(ILanguageConnectionRegistry registry, ILocalizationService localization)
    {
        _registry = registry;
        _localization = localization;

        localization.BindTitle(this, "Str.Tool.LanguageServers.Title");
        CanClose = true;
        CanFloat = false;

        _registry.ConnectionsChanged += OnConnectionsChanged;
        Refresh();
    }

    private void OnConnectionsChanged(object? sender, EventArgs e)
    {
        // The registry raises this from transport and RPC callbacks, which are not the UI thread.
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>
    /// Rebuilds every row. Wholesale rather than differentially, because a connection is a value: patching
    /// one in place is how a view ends up showing a state and a capability set that never coexisted, which
    /// is the exact defect the projection fix in #318 removed one layer down.
    /// </summary>
    private void Refresh()
    {
        Groups.Clear();

        var rows = _registry.Connections
            .Select(c => (Connection: c, Row: new LanguageServerRowViewModel(c, _localization)))
            .ToList();

        // Highest priority first within a language: that is the order the registry itself picks in, for the
        // features where exactly one server can answer, so the list reads as the ranking it actually is.
        foreach (var group in rows
            .GroupBy(r => r.Connection.LanguageId ?? "")
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(new LanguageServerGroupViewModel(
                group.Key,
                group.OrderByDescending(r => r.Connection.Priority)
                     .ThenBy(r => r.Connection.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(r => r.Row)
                     .ToList()));
        }

        Problems.Clear();
        foreach (var p in _registry.ConfigurationProblems) Problems.Add(p);

        HasProblems = Problems.Count > 0;
        HasNoServers = Groups.Count == 0;
    }

    /// <summary>
    /// The whole window as plain text — the deliverable for the case this exists to serve, which is telling
    /// whoever wrote a server what HexIDE observed, without them having to install HexIDE.
    /// </summary>
    public string ToReportText()
    {
        var lines = new List<string>();
        foreach (var group in Groups)
        {
            lines.Add(group.Language);
            foreach (var row in group.Rows)
            {
                lines.Add(row.ToReportText());
                lines.Add("");
            }
        }

        if (Problems.Count > 0)
        {
            lines.Add(_localization.GetString("Str.Tool.LanguageServers.Problems"));
            foreach (var p in Problems)
                lines.Add($"  [{p.Kind}] {(p.EntryId is { Length: > 0 } id ? id + ": " : "")}{p.Message}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
