using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public partial class TypeHierarchyNode : ObservableObject
{
    public string Label { get; }
    public Location? Location { get; }
    public ObservableCollection<TypeHierarchyNode> Children { get; } = [];
    [Notify] private bool isExpanded;
    private bool requested;

    public TypeHierarchyNode(string label, Location? location = null, Func<Task>? expand = null)
    {
        Label = label;
        Location = location;
        if (expand is not null) Children.Add(new(""));
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsExpanded) && IsExpanded && !requested && expand is not null)
            {
                requested = true;
                expand().ListenErrors();
            }
        };
    }
}

public partial class TypeHierarchyToolViewModel : ResultsToolViewModel
{
    private readonly ILspClient client;
    private readonly IEditorService editors;
    private readonly ILocalizationService localization;
    private TypeHierarchyResult result = new([], []);
    private int generation;
    private int searchGeneration;
    public ObservableCollection<TypeHierarchyNode> Roots { get; } = [];
    [Notify] private TypeHierarchyNode? selectedNode;

    public TypeHierarchyToolViewModel(ILspClient client, IEditorService editors, ILocalizationService localization)
    {
        this.client = client;
        this.editors = editors;
        this.localization = localization;
        localization.BindTitle(this, "Str.Tool.TypeHierarchy.Title");
        CanClose = true;
        CanPin = false;
        localization.LanguageChanged += Rebuild;
    }

    public async Task SearchAsync(string uri, Position position, CancellationToken ct = default)
    {
        var current = ++searchGeneration;
        ++generation;
        result = new([], []);
        Roots.Clear();
        SelectedNode = null;
        RequestShow();
        TypeHierarchyResult answer;
        try { answer = await client.FindTypeHierarchyAsync(uri, position, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex) { answer = new([], [new ReferenceSearchError("", ex.Message)]); }
        if (current != searchGeneration) return;
        result = answer;
        Rebuild();
    }

    private TypeHierarchyNode Message(string key) => new(localization.GetString(key));
    private TypeHierarchyNode Error(string server, string message) =>
        new(string.Format(localization.GetString("Str.Tool.FindReferences.Error"), server, message));

    private void Rebuild()
    {
        ++generation;
        SelectedNode = null;
        Roots.Clear();
        foreach (var root in result.Roots) Roots.Add(Node(root.Client, root.Item, root.Server));
        foreach (var error in result.Errors) Roots.Add(Error(error.Server, error.Message));
        if (Roots.Count == 0) Roots.Add(Message("Str.Tool.TypeHierarchy.Empty"));
    }

    private TypeHierarchyNode Node(ILspClient source, TypeHierarchyItem item, string server)
    {
        var node = new TypeHierarchyNode(item.Detail is { Length: > 0 } ? $"{item.Name} — {item.Detail}" : item.Name,
            new Location(item.Uri, item.SelectionRange));
        node.Children.Add(Group(source, item, server, subtypes: true));
        node.Children.Add(Group(source, item, server, subtypes: false));
        return node;
    }

    private TypeHierarchyNode Group(ILspClient source, TypeHierarchyItem item, string server, bool subtypes)
    {
        var current = generation;
        TypeHierarchyNode group = null!;
        group = new(localization.GetString(subtypes ? "Str.Tool.TypeHierarchy.Subtypes" : "Str.Tool.TypeHierarchy.Supertypes"),
            expand: async () =>
            {
                try
                {
                    var items = subtypes ? await source.RequestSubtypesAsync(item) : await source.RequestSupertypesAsync(item);
                    if (current != generation) return;
                    group.Children.Clear();
                    foreach (var child in items) group.Children.Add(Node(source, child, server));
                    if (items.Length == 0) group.Children.Add(Message("Str.Tool.TypeHierarchy.Empty"));
                }
                catch (Exception ex)
                {
                    if (current != generation) return;
                    group.Children.Clear();
                    group.Children.Add(Error(server, ex.Message));
                }
            });
        return group;
    }

    public bool NavigateSelected() => SelectedNode?.Location is { } location
        && editors.NavigateTo(location.Uri, location.Range.Start.Line + 1, location.Range.Start.Character + 1);
}
