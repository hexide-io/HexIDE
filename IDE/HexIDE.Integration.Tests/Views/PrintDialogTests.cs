using Avalonia.Controls;
using Avalonia.Interactivity;
using HexIDE.Dialogs;

namespace HexIDE.Integration.Tests.Views;

public class PrintDialogTests
{
    [AvaloniaTheory]
    [InlineData("PART_Ok", true)]
    [InlineData("PART_Cancel", false)]
    public void Managed_dialog_answers_without_printing(string button, bool expected)
    {
        var dialog = new PrintDialog();
        bool? answer = null;
        dialog.PrintRequest += value => answer = value;
        dialog.FindControl<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        answer.Should().Be(expected);
    }
}
