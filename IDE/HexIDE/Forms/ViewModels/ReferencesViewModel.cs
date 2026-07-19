using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class ReferencesViewModel : ObservableObject, IDialog
{
    private static readonly VbReference VbaBuiltIn =
        new("{00020430-0000-0000-C000-000000000046}", "2.0", 0, null, "Visual Basic For Applications");

    public string Title { get; }
    public bool CanResize => true;
    public event Action<bool>? CloseRequested;

    public DelegateCommand OkCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public DelegateCommand BrowseCommand { get; }
    public DelegateCommand MoveUpCommand { get; }
    public DelegateCommand MoveDownCommand { get; }

    public ObservableCollection<ReferencesReferenceViewModel> References { get; } = new();
    [Notify] private ReferencesReferenceViewModel? selectedReference;

    private readonly IWindowManager windowManager;

    public ReferencesViewModel(ProjectDefinition definition,
        IReferenceLibraryService libraryService,
        IWindowManager windowManager)
    {
        Title = $"References - {definition.Name}";
        this.windowManager = windowManager;

        // VBA built-in is always checked and cannot be removed
        References.Add(new ReferencesReferenceViewModel(VbaBuiltIn, isChecked: true, isBuiltIn: true));

        // References already in the project (excluding VBA, which is implicit)
        var projectRefs = definition.References
            .Where(r => !string.Equals(r.Guid, VbaBuiltIn.Guid, StringComparison.OrdinalIgnoreCase));

        var projectRefGuids = new HashSet<string>(
            projectRefs.Select(r => r.Guid),
            StringComparer.OrdinalIgnoreCase);

        foreach (var r in projectRefs)
            References.Add(new ReferencesReferenceViewModel(r, isChecked: true, isBuiltIn: false));

        // Available type libraries from the registry that are not already added
        foreach (var avail in libraryService.GetAvailableReferences())
        {
            if (projectRefGuids.Contains(avail.Guid)) continue;
            var r = new VbReference(avail.Guid, avail.Version, 0, avail.LibPath, avail.Name);
            References.Add(new ReferencesReferenceViewModel(r, isChecked: false, isBuiltIn: false));
        }

        selectedReference = References.FirstOrDefault();

        OkCommand = new DelegateCommand(() => CloseRequested?.Invoke(true));
        CancelCommand = new DelegateCommand(() => CloseRequested?.Invoke(false));
        BrowseCommand = new DelegateCommand(async () => await Browse());
        MoveUpCommand = new DelegateCommand(MoveUp, CanMoveUp);
        MoveDownCommand = new DelegateCommand(MoveDown, CanMoveDown);
    }

    private void OnSelectedReferenceChanged(ReferencesReferenceViewModel? oldValue, ReferencesReferenceViewModel? newValue)
    {
        MoveUpCommand.RaiseCanExecutedChanged();
        MoveDownCommand.RaiseCanExecutedChanged();
    }

    private bool CanMoveUp() =>
        selectedReference is { IsBuiltIn: false } && References.IndexOf(selectedReference) > 1;

    private bool CanMoveDown() =>
        selectedReference is { IsBuiltIn: false } &&
        References.IndexOf(selectedReference) is var idx && idx >= 0 && idx < References.Count - 1;

    private void MoveUp()
    {
        if (selectedReference == null) return;
        var idx = References.IndexOf(selectedReference);
        if (idx > 1)
            References.Move(idx, idx - 1);
        MoveUpCommand.RaiseCanExecutedChanged();
        MoveDownCommand.RaiseCanExecutedChanged();
    }

    private void MoveDown()
    {
        if (selectedReference == null) return;
        var idx = References.IndexOf(selectedReference);
        if (idx >= 0 && idx < References.Count - 1)
            References.Move(idx, idx + 1);
        MoveUpCommand.RaiseCanExecutedChanged();
        MoveDownCommand.RaiseCanExecutedChanged();
    }

    private async Task Browse()
    {
        var files = await windowManager.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Type Library",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Type Libraries") { Patterns = new[] { "*.tlb", "*.olb", "*.dll", "*.exe", "*.ocx" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files == null || files.Count == 0) return;
        var path = files[0];

        // If this path is already in the list, just select and check it
        var existing = References.FirstOrDefault(r =>
            string.Equals(r.LibPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.IsChecked = true;
            SelectedReference = existing;
            return;
        }

        var filename = System.IO.Path.GetFileNameWithoutExtension(path);
        var browsed = new VbReference("{00000000-0000-0000-0000-000000000000}", "1.0", 0, path, filename);
        var vm = new ReferencesReferenceViewModel(browsed, isChecked: true, isBuiltIn: false);
        References.Add(vm);
        SelectedReference = vm;
    }

    public void Apply(ProjectDefinition project)
    {
        project.SetReferences(References
            .Where(r => r.IsChecked && !r.IsBuiltIn)
            .Select(r => new VbReference(r.Guid, r.Version, r.Lcid, r.LibPath, r.Name)));
    }
}

public partial class ReferencesReferenceViewModel
{
    public ReferencesReferenceViewModel(VbReference reference, bool isChecked, bool isBuiltIn)
    {
        Guid = reference.Guid;
        Version = reference.Version;
        Lcid = reference.Lcid;
        LibPath = reference.LibPath;
        Name = reference.Name ?? reference.Guid;
        IsChecked = isChecked;
        IsBuiltIn = isBuiltIn;
    }

    public string Guid { get; }
    public string Version { get; }
    public int Lcid { get; }
    public string? LibPath { get; }
    public string Name { get; }
    public bool IsBuiltIn { get; }

    [Notify] private bool isChecked;

    public string Location => LibPath ?? (IsBuiltIn ? "built-in" : "");
    public string Language => "English/Standard";
}
