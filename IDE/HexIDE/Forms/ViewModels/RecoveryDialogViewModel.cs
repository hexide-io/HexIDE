using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Utils;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// Unsaved work left by an IDE that ended without a clean exit, offered back one project at a time (hexide-io/HexIDE#499).
/// </summary>
/// <remarks>
/// <para>
/// One dialog for each place the work is offered: at start, as a project is opened, and from File › Recover Unsaved
/// Work. <b>Restore</b> and <b>Discard</b> act on the selected project and take it off the list, and the dialog closes
/// once the list is empty. <b>Decide Later</b> leaves whatever is left waiting, under the File menu.
/// </para>
/// <para>
/// The work is restored and discarded by the caller, through the two functions it passes. Each says whether it went
/// through, and a project stays on the list when it didn't.
/// </para>
/// </remarks>
public sealed class RecoveryDialogViewModel : ObservableObject, IDialog
{
    private readonly Func<RecoveryItemViewModel, Task<bool>> restore;
    private readonly Func<RecoveryItemViewModel, Task<bool>> discard;
    private RecoveryItemViewModel? selectedItem;
    private bool busy;

    public RecoveryDialogViewModel(ILocalizationService localization, IEnumerable<RecoveryItemViewModel> items,
        Func<RecoveryItemViewModel, Task<bool>> restore, Func<RecoveryItemViewModel, Task<bool>> discard)
    {
        Title = localization.GetString("Str.Dialog.Recovery.Title");
        this.restore = restore;
        this.discard = discard;
        Items = new ObservableCollection<RecoveryItemViewModel>(items);
        selectedItem = Items.Count > 0 ? Items[0] : null;
        RestoreCommand = new DelegateCommand(() => RestoreSelectedAsync().ListenErrors(), () => selectedItem is not null && !busy);
        DiscardCommand = new DelegateCommand(() => DiscardSelectedAsync().ListenErrors(), () => selectedItem is not null && !busy);
        LaterCommand = new DelegateCommand(() => CloseRequested?.Invoke(false), () => !busy);
    }

    public string Title { get; }

    public bool CanResize => false;

    public event Action<bool>? CloseRequested;

    /// <summary>The projects whose work is waiting, newest backup first.</summary>
    public ObservableCollection<RecoveryItemViewModel> Items { get; }

    public RecoveryItemViewModel? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (SetProperty(ref selectedItem, value))
                RaiseCanExecute();
        }
    }

    public DelegateCommand RestoreCommand { get; }

    public DelegateCommand DiscardCommand { get; }

    public DelegateCommand LaterCommand { get; }

    /// <summary>What <see cref="RestoreCommand"/> does, for a caller that needs to know when it has finished.</summary>
    internal Task RestoreSelectedAsync() => Act(restore);

    /// <summary>What <see cref="DiscardCommand"/> does, for a caller that needs to know when it has finished.</summary>
    internal Task DiscardSelectedAsync() => Act(discard);

    private async Task Act(Func<RecoveryItemViewModel, Task<bool>> action)
    {
        if (selectedItem is not { } item || busy)
            return;
        busy = true;
        RaiseCanExecute();
        try
        {
            if (!await action(item))
                return;
            var index = Items.IndexOf(item);
            Items.Remove(item);
            if (Items.Count == 0)
            {
                CloseRequested?.Invoke(true);
                return;
            }
            SelectedItem = Items[Math.Min(index, Items.Count - 1)];
        }
        finally
        {
            busy = false;
            RaiseCanExecute();
        }
    }

    private void RaiseCanExecute()
    {
        RestoreCommand.RaiseCanExecutedChanged();
        DiscardCommand.RaiseCanExecutedChanged();
        LaterCommand.RaiseCanExecutedChanged();
    }
}

/// <summary>A project's waiting work, as <see cref="RecoveryDialogViewModel"/> lists it.</summary>
/// <param name="Name">The project's name.</param>
/// <param name="Origin">Where it came from: never saved, its folder, or the folder it has since moved from.</param>
/// <param name="BackedUp">When its latest backup was taken.</param>
/// <param name="ChangedOnDisk">The files that have changed on disk since then, or null when none has.</param>
/// <param name="Key">The caller's own handle on the work.</param>
public sealed record RecoveryItemViewModel(string Name, string Origin, string BackedUp, string? ChangedOnDisk, object Key)
{
    /// <summary>What a screen reader announces for the row: everything it shows, in order.</summary>
    public string AccessibleName => ChangedOnDisk is null
        ? $"{Name}, {Origin}, {BackedUp}"
        : $"{Name}, {Origin}, {BackedUp}, {ChangedOnDisk}";

    public override string ToString() => AccessibleName;
}
