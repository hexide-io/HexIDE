using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Dialogs;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class PrintDialogTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accept_writes_measured_values_directly_and_through_Action(bool action)
    {
        var host = Substitute.For<IBasicStandardLibrary>();
        host.ShowPrintDialog().Returns(Task.FromResult(true));
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty("Copies", new Vb6Value(3));
        dialog.TrySetProperty("PrinterDefault", new Vb6Value(false));
        if (action)
            (await dialog.TrySetPropertyAsync("Action", new Vb6Value(5))).Should().BeTrue();
        else
            await dialog.CallAsync("ShowPrinter", []);
        await host.Received(1).ShowPrintDialog();
        foreach (var (name, expected) in new[] { ("Copies", 1), ("Orientation", 1), ("FromPage", 0), ("ToPage", 0), ("Min", 0), ("Max", 0) })
        {
            dialog.TryGetProperty(name, out var value).Should().BeTrue();
            value.Value.Should().Be(expected);
        }
        dialog.TryGetProperty("hDC", out var dc).Should().BeTrue();
        dc.Value.Should().Be(0L);
        dialog.TryGetProperty("Flags", out var flags).Should().BeTrue();
        flags.Value.Should().Be(0L);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Cancel_preserves_values_and_honours_CancelError(bool action, bool cancelError)
    {
        var host = Substitute.For<IBasicStandardLibrary>();
        host.ShowPrintDialog().Returns(Task.FromResult(false));
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty("Copies", new Vb6Value(3));
        dialog.TrySetProperty("CancelError", new Vb6Value(cancelError));
        Func<Task> show = async () =>
        {
            if (action)
                await dialog.TrySetPropertyAsync("Action", new Vb6Value(5));
            else
                await dialog.CallAsync("ShowPrinter", []);
        };
        if (cancelError)
            (await show.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(32755);
        else
            await show.Should().NotThrowAsync();
        dialog.TryGetProperty("Copies", out var copies).Should().BeTrue();
        copies.Value.Should().Be(3);
        Action read = () => dialog.TryGetProperty("hDC", out _);
        read.Should().Throw<NotImplementedException>();
    }

    [AvaloniaTheory]
    [InlineData("Flags", 256)]
    [InlineData("Flags", 1)]
    [InlineData("Orientation", 2)]
    [InlineData("FromPage", 1)]
    [InlineData("ToPage", 1)]
    [InlineData("Min", 1)]
    [InlineData("Max", 1)]
    [InlineData("Copies", 2)]
    public async Task Unmeasured_configuration_and_native_DC_are_refused(string name, int value)
    {
        var host = Substitute.For<IBasicStandardLibrary>();
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty(name, new Vb6Value(value));
        Func<Task> show = async () => await dialog.CallAsync("ShowPrinter", []);
        await show.Should().ThrowAsync<NotImplementedException>();
        await host.DidNotReceive().ShowPrintDialog();
    }

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
