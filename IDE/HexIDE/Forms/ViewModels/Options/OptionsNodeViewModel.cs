using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// A node in the Options dialog category tree. A node can hold children and an optional page.
/// <see cref="Page"/> drives the right-hand content pane when the node is selected;
/// categories without an overview have no page.
/// A leaf's page is an <see cref="IOptionsPage"/> for first-party settings pages, or an
/// add-in page ViewModel for the Add-Ins section; the View is resolved by the ViewLocator.
/// </summary>
public partial class OptionsNodeViewModel : ObservableObject
{
    public OptionsNodeViewModel(string title, object? page = null, string? key = null)
    {
        _title = title;
        Page = page;
        Key = key;
    }

    /// <summary>Display label. Settable so the shell can re-localize it live on a language change.</summary>
    [ObservableProperty] private string _title;

    /// <summary>Localization key for <see cref="Title"/>, or null for nodes whose label is not localized
    /// (e.g. add-in nodes, whose title is the add-in's own name).</summary>
    public string? Key { get; }

    /// <summary>The page rendered on the right when this node is selected; null for categories without an overview.</summary>
    public object? Page { get; }

    public ObservableCollection<OptionsNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Adds a child node and returns this node, for fluent tree construction.</summary>
    public OptionsNodeViewModel Add(OptionsNodeViewModel child)
    {
        Children.Add(child);
        return this;
    }
}
