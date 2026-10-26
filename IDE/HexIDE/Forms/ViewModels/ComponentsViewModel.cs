using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// VB6's Components dialog (#1103): the in-box control libraries HexIDE implements, checked where the project references
/// them, and any other control library the project references, listed but not something HexIDE runs. Measured on vb6.exe:
/// <list type="bullet">
/// <item>Each library is listed under its type library's registry description, which the dialog shows as it is.</item>
/// <item>Checking one and applying adds its <c>Object=</c> line to the project, after the <c>Form=</c> lines.</item>
/// <item>Unchecking one a form uses is refused at once, with "Can't remove control or reference; in use", and the box
/// stays checked.</item>
/// </list>
/// </summary>
public partial class ComponentsViewModel : ObservableObject, IDialog
{
    private readonly ProjectDefinition project;
    private readonly IWindowManager? windowManager;
    private readonly ILocalizationService? localization;

    public string Title { get; }
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    public DelegateCommand OkCommand { get; }
    public DelegateCommand ApplyCommand { get; }
    public DelegateCommand CancelCommand { get; }

    [Notify] [AlsoNotify(nameof(Controls))] private bool showOnlySelectedControls;
    private List<ComponentsComponentViewModel> allControls { get; } = new();
    public List<ComponentsComponentViewModel> Controls => showOnlySelectedControls ? allControls.Where(c => c.IsSelected).ToList() : allControls;
    [Notify] private ComponentsComponentViewModel? selectedControl;

    [Notify] [AlsoNotify(nameof(Designers))] private bool showOnlySelectedDesigners;
    private List<ComponentsComponentViewModel> allDesigners { get; } = new();
    private List<ComponentsComponentViewModel> selectedDesigners { get; } = new();
    public List<ComponentsComponentViewModel> Designers => showOnlySelectedDesigners ? selectedDesigners : allDesigners;
    [Notify] private ComponentsComponentViewModel? selectedDesigner;

    [Notify] [AlsoNotify(nameof(InsertableObjects))] private bool showOnlySelectedInsertableObjects;
    private List<ComponentsComponentViewModel> allInsertableObjects { get; } = new();
    private List<ComponentsComponentViewModel> selectedInsertableObjects { get; } = new();
    public List<ComponentsComponentViewModel> InsertableObjects => showOnlySelectedInsertableObjects ? selectedInsertableObjects : allInsertableObjects;
    [Notify] private ComponentsComponentViewModel? selectedInsertableObject;

    public ComponentsViewModel(ProjectDefinition projectDefinition, IWindowManager? windowManager = null,
        ILocalizationService? localization = null)
    {
        project = projectDefinition;
        this.windowManager = windowManager;
        this.localization = localization;
        Title = "Components";

        var referenced = project.ObjectReferences.ToList();
        foreach (var library in ControlLibraries.All.OrderBy(l => l.Description, StringComparer.OrdinalIgnoreCase))
        {
            var row = new ComponentsComponentViewModel(library.Description, library.File, library)
            {
                IsSelected = referenced.Any(r => library.IsNamedBy(r)),
            };
            row.PropertyChanged += OnRowChanged;
            allControls.Add(row);
        }
        // A control library the project references that HexIDE doesn't implement: listed, checked, and not to be changed here.
        foreach (var other in referenced.Where(r => ControlLibraries.For(r) is null))
            allControls.Add(new ComponentsComponentViewModel(
                string.Format(localization?.GetString("Str.Dialog.Components.NotImplemented") ?? "{0} (not implemented in HexIDE)", other.File),
                other.File, library: null) { IsSelected = true });

        selectedControl = allControls.FirstOrDefault();
        selectedDesigner = allDesigners.FirstOrDefault();
        selectedInsertableObject = allInsertableObjects.FirstOrDefault();

        OkCommand = new DelegateCommand(() =>
        {
            Apply();
            CloseRequested?.Invoke(true);
        });
        ApplyCommand = new DelegateCommand(Apply);
        CancelCommand = new DelegateCommand(() => CloseRequested?.Invoke(false));
    }

    /// <summary>Whether a form of the project holds a control of <paramref name="library"/>: its header names the library.</summary>
    public bool IsInUse(ControlLibrary library) =>
        project.Forms.Any(f => f.ObjectReferences.Any(library.IsNamedBy));

    private bool restoring;

    // Unchecking a library a form uses is refused at once, as VB6 refuses it, and the box goes back. The check box's binding
    // ignores a change raised while it is itself writing the value, and then takes the same value again as no change, so
    // once the write is over the row passes through false back to true. Otherwise the box stays clear while OK keeps the
    // reference (found by the live check).
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (restoring
            || e.PropertyName != nameof(ComponentsComponentViewModel.IsSelected)
            || sender is not ComponentsComponentViewModel { IsSelected: false, Library: { } library } row
            || !IsInUse(library))
            return;
        row.IsSelected = true;
        Dispatcher.UIThread.Post(() =>
        {
            restoring = true;
            row.IsSelected = false;
            row.IsSelected = true;
            restoring = false;
        });
        Refused?.Invoke(library);
        _ = windowManager?.MessageBox(localization?.GetString("Str.Dialog.Components.InUse") ?? "Can't remove control or reference; in use",
            "HexIDE", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
    }

    /// <summary>Raised when unchecking a library is refused because a form uses it.</summary>
    public event Action<ControlLibrary>? Refused;

    /// <summary>Reference each checked library the project doesn't, and drop each unchecked one it does.</summary>
    public void Apply()
    {
        var referenced = project.ObjectReferences.ToList();
        foreach (var row in allControls)
        {
            if (row.Library is not { } library)
                continue;
            var isReferenced = referenced.Any(library.IsNamedBy);
            if (row.IsSelected && !isReferenced)
                project.AddObjectLine(library.ProjectLine);
            else if (!row.IsSelected && isReferenced && !IsInUse(library))
                project.RemoveObjectReference(library.Guid);
        }
        OnPropertyChanged(nameof(Controls));
    }
}

public partial class ComponentsComponentViewModel : ObservableObject
{
    public ComponentsComponentViewModel(string name, string location, ControlLibrary? library = null)
    {
        Name = name;
        Location = location;
        Library = library;
    }

    public string Name { get; }
    public string Location { get; }

    /// <summary>The in-box library the row is, or null for one HexIDE doesn't implement, which can't be changed here.</summary>
    public ControlLibrary? Library { get; }

    public bool CanChange => Library is not null;
    [Notify] private bool isSelected;
}
