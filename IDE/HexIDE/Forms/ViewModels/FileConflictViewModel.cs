using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// The consolidated "files changed outside the IDE" conflict dialog. Lists every file that changed on
/// disk while the IDE held unsaved edits, with two batch actions: reload all (discarding edits) or keep
/// all (ignoring the disk changes). Modelled on <see cref="SaveChangesViewModel"/>.
/// </summary>
public class FileConflictViewModel : ObservableObject, IDialog
{
    public string Title => HexIDE.IDE.Distribution.DisplayName;
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    public ObservableCollection<ConflictedFileViewModel> Files { get; } = new();

    /// <summary>
    /// Whether the dialog also offers Cancel, which closes it with neither choice made: for a save that hasn't written
    /// anything yet, as an add-in's project file is asked about (#3698).
    /// </summary>
    public bool CanCancel { get; init; }

    /// <summary>True if the user chose Reload-all; false if Keep-all. Valid after the dialog closes.</summary>
    public bool ReloadChosen { get; private set; }

    public void Add(string name) => Files.Add(new ConflictedFileViewModel(name));

    public void ReloadAll()
    {
        ReloadChosen = true;
        CloseRequested?.Invoke(true);
    }

    public void KeepAll()
    {
        ReloadChosen = false;
        CloseRequested?.Invoke(true);
    }

    public void Cancel() => CloseRequested?.Invoke(false);
}

public class ConflictedFileViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;

    public override string ToString() => Name;
}
