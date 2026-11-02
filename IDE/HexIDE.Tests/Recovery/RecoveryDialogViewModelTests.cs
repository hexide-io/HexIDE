using HexIDE.Forms.ViewModels;
using HexIDE.Localization;

namespace HexIDE.Tests.Recovery;

/// <summary>The Recover Unsaved Work dialog's list (hexide-io/HexIDE#499 task 2.2).</summary>
public sealed class RecoveryDialogViewModelTests
{
    private static RecoveryItemViewModel Item(string name) => new(name, "Never saved", "Backed up today", null, name);

    private static RecoveryDialogViewModel Dialog(Func<RecoveryItemViewModel, Task<bool>> restore, params string[] names) =>
        new(Substitute.For<ILocalizationService>(), names.Select(Item), restore, _ => Task.FromResult(true));

    [Fact]
    public void The_first_project_is_selected_so_Restore_is_ready()
    {
        var dialog = Dialog(_ => Task.FromResult(true), "Alpha", "Beta");

        dialog.SelectedItem!.Name.Should().Be("Alpha");
        dialog.RestoreCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task A_project_dealt_with_leaves_the_list_and_the_next_is_selected()
    {
        var dialog = Dialog(_ => Task.FromResult(true), "Alpha", "Beta");

        await dialog.RestoreSelectedAsync();

        dialog.Items.Select(i => i.Name).Should().Equal("Beta");
        dialog.SelectedItem!.Name.Should().Be("Beta");
    }

    [Fact]
    public async Task The_dialog_closes_once_the_list_is_empty()
    {
        var dialog = Dialog(_ => Task.FromResult(true), "Alpha");
        bool? closed = null;
        dialog.CloseRequested += result => closed = result;

        await dialog.RestoreSelectedAsync();

        closed.Should().BeTrue();
    }

    [Fact]
    public async Task A_project_that_could_not_be_dealt_with_stays_on_the_list()
    {
        var dialog = Dialog(_ => Task.FromResult(false), "Alpha");
        var closed = false;
        dialog.CloseRequested += _ => closed = true;

        await dialog.RestoreSelectedAsync();

        dialog.Items.Should().ContainSingle();
        closed.Should().BeFalse();
    }

    [Fact]
    public async Task Nothing_else_can_be_pressed_while_a_restore_is_under_way()
    {
        var release = new TaskCompletionSource<bool>();
        var dialog = Dialog(_ => release.Task, "Alpha", "Beta");

        var restoring = dialog.RestoreSelectedAsync();

        dialog.RestoreCommand.CanExecute(null).Should().BeFalse();
        dialog.DiscardCommand.CanExecute(null).Should().BeFalse();
        dialog.LaterCommand.CanExecute(null).Should().BeFalse();
        release.SetResult(true);
        await restoring;
        dialog.RestoreCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Decide_Later_closes_without_dealing_with_anything()
    {
        var dialog = Dialog(_ => throw new InvalidOperationException("Later must not restore"), "Alpha");
        bool? closed = null;
        dialog.CloseRequested += result => closed = result;

        dialog.LaterCommand.Execute(null);

        closed.Should().BeFalse();
        dialog.Items.Should().ContainSingle();
    }

    [Fact]
    public void A_row_is_announced_with_everything_it_shows()
    {
        new RecoveryItemViewModel("Alpha", "From C:\\work", "Backed up 14:25", "Changed on disk: Form1", "key")
            .AccessibleName.Should().Be("Alpha, From C:\\work, Backed up 14:25, Changed on disk: Form1");
    }
}
