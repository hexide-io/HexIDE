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
    public string Title => "HexIDE";
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    public ObservableCollection<ConflictedFileViewModel> Files { get; } = new();

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
}

public class ConflictedFileViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;

    public override string ToString() => Name;
}
