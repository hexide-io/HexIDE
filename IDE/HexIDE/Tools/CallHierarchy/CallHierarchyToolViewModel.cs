using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public partial class CallHierarchyNode : ObservableObject
{
    public string Label { get; }
    public Location? Location { get; }
    internal List<CallHierarchyNode> CallSites { get; } = [];
    public ObservableCollection<CallHierarchyNode> Children { get; } = [];
    [Notify] private bool isExpanded;
    private bool requested;

    public CallHierarchyNode(string label, Location? location = null, Func<Task>? expand = null)
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

public partial class CallHierarchyToolViewModel : Tool
{
    private readonly ILspClient client;
    private readonly IEditorService editors;
    private readonly ILocalizationService localization;
    private CallHierarchyResult result = new([], []);
    private int generation;
    private int searchGeneration;
    public event Action? ShowRequested;
    public ObservableCollection<CallHierarchyNode> Roots { get; } = [];
    [Notify] private CallHierarchyNode? selectedNode;
    [Notify] private bool callsFrom;

    public CallHierarchyToolViewModel(ILspClient client, IEditorService editors, ILocalizationService localization)
    {
        this.client = client;
        this.editors = editors;
        this.localization = localization;
        localization.BindTitle(this, "Str.Tool.CallHierarchy.Title");
        CanClose = true;
        CanPin = false;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CallsFrom)) Rebuild();
        };
        localization.LanguageChanged += Rebuild;
    }

    public async Task SearchAsync(string uri, Position position, CancellationToken ct = default)
    {
        var current = ++searchGeneration;
        ++generation;
        result = new([], []);
        Roots.Clear();
        SelectedNode = null;
        ShowRequested?.Invoke();
        CallHierarchyResult answer;
        try { answer = await client.FindCallHierarchyAsync(uri, position, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex) { answer = new([], [new ReferenceSearchError("", ex.Message)]); }
        if (current != searchGeneration) return;
        result = answer;
        Rebuild();
    }

    private CallHierarchyNode Message(string key) => new(localization.GetString(key));
    private CallHierarchyNode Error(string server, string message) =>
        new(string.Format(localization.GetString("Str.Tool.FindReferences.Error"), server, message));

    private void Rebuild()
    {
        ++generation;
        SelectedNode = null;
        Roots.Clear();
        foreach (var root in result.Roots) Roots.Add(Node(root.Client, root.Item, root.Server));
        foreach (var error in result.Errors) Roots.Add(Error(error.Server, error.Message));
        if (Roots.Count == 0) Roots.Add(Message("Str.Tool.CallHierarchy.Empty"));
    }

    private CallHierarchyNode Node(ILspClient source, CallHierarchyItem item, string server)
    {
        var current = generation;
        var outgoing = CallsFrom;
        CallHierarchyNode node = null!;
        node = new(item.Detail is { Length: > 0 } ? $"{item.Name} — {item.Detail}" : item.Name,
            new Location(item.Uri, item.SelectionRange), async () =>
            {
                try
                {
                    bool found;
                    if (outgoing)
                    {
                        var calls = await source.RequestOutgoingCallsAsync(item);
                        found = calls.Length > 0;
                        if (current != generation) return;
                        node.Children.Clear();
                        foreach (var site in node.CallSites) node.Children.Add(site);
                        foreach (var call in calls)
                        {
                            var child = Node(source, call.To, server);
                            AddSites(child, item.Uri, call.FromRanges);
                            node.Children.Add(child);
                        }
                    }
                    else
                    {
                        var calls = await source.RequestIncomingCallsAsync(item);
                        found = calls.Length > 0;
                        if (current != generation) return;
                        node.Children.Clear();
                        foreach (var site in node.CallSites) node.Children.Add(site);
                        foreach (var call in calls)
                        {
                            var child = Node(source, call.From, server);
                            AddSites(child, call.From.Uri, call.FromRanges);
                            node.Children.Add(child);
                        }
                    }
                    if (!found) node.Children.Add(Message("Str.Tool.CallHierarchy.Empty"));
                }
                catch (Exception ex)
                {
                    if (current != generation) return;
                    node.Children.Clear();
                    foreach (var site in node.CallSites) node.Children.Add(site);
                    node.Children.Add(Error(server, ex.Message));
                }
            });
        return node;
    }

    private void AddSites(CallHierarchyNode node, string uri, HexIDE.Lsp.Messages.Range[] ranges)
    {
        // Call sites belong beside the lazy placeholder; opening this node must retain them.
        foreach (var range in ranges)
            node.CallSites.Add(new(string.Format(localization.GetString("Str.Tool.CallHierarchy.CallSite"),
                range.Start.Line + 1, range.Start.Character + 1), new Location(uri, range)));
        foreach (var site in node.CallSites) node.Children.Add(site);
    }

    public bool NavigateSelected() => SelectedNode?.Location is { } location
        && editors.NavigateTo(location.Uri, location.Range.Start.Line + 1, location.Range.Start.Character + 1);
}
