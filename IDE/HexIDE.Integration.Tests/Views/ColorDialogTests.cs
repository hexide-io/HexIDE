using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Dialogs;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class ColorDialogTests
{
    // Oracle: "In-box controls: the CommonDialog", ShowColor (native and p-code).
    [AvaloniaFact]
    public async Task Action_three_uses_the_color_host()
    {
        var host = Substitute.For<IBasicStandardLibrary>();
        host.ShowColorDialog(16711680).Returns(Task.FromResult<int?>(16711680));
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty("Flags", new Vb6Value(1L));
        dialog.TrySetProperty("Color", new Vb6Value(16711680L));
        (await dialog.TrySetPropertyAsync("Action", new Vb6Value(3))).Should().BeTrue();
        await host.Received(1).ShowColorDialog(16711680);
        dialog.TryGetProperty("Color", out var color).Should().BeTrue();
        color.Value.Should().Be(16711680U);
    }

    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(1, 255)]
    public async Task ShowColor_initializes_and_writes_back_the_measured_color(int flags, int expected)
    {
        var host = Substitute.For<IBasicStandardLibrary>();
        host.ShowColorDialog(expected).Returns(Task.FromResult<int?>(expected));
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty("Color", new Vb6Value(255L));
        dialog.TrySetProperty("Flags", new Vb6Value((long)flags));

        await dialog.CallAsync("ShowColor", []);

        await host.Received(1).ShowColorDialog(expected);
        dialog.TryGetProperty("Color", out var color).Should().BeTrue();
        color.Value.Should().Be((uint)expected);
        dialog.TryGetProperty("Flags", out var after).Should().BeTrue();
        after.Value.Should().Be((long)flags);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_preserves_color_and_honours_CancelError(bool cancelError)
    {
        var host = Substitute.For<IBasicStandardLibrary>();
        host.ShowColorDialog(16711680).Returns(Task.FromResult<int?>(null));
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty("Color", new Vb6Value(16711680L));
        dialog.TrySetProperty("Flags", new Vb6Value(1L));
        dialog.TrySetProperty("CancelError", new Vb6Value(cancelError));
        Func<Task> show = async () => await dialog.CallAsync("ShowColor", []);
        if (cancelError)
            (await show.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(32755);
        else
            await show.Should().NotThrowAsync();
        dialog.TryGetProperty("Color", out var color).Should().BeTrue();
        color.Value.Should().Be(16711680U);
    }

    [AvaloniaTheory]
    [InlineData(2, 255)]
    [InlineData(4, 255)]
    [InlineData(8, 255)]
    [InlineData(1, -2147483643)]
    public async Task Unmeasured_flags_and_system_color_initialization_are_refused(int flags, int color)
    {
        var host = Substitute.For<IBasicStandardLibrary>();
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty("Color", new Vb6Value((long)color));
        dialog.TrySetProperty("Flags", new Vb6Value((long)flags));
        Func<Task> show = async () => await dialog.CallAsync("ShowColor", []);
        await show.Should().ThrowAsync<NotImplementedException>();
        await host.DidNotReceiveWithAnyArgs().ShowColorDialog(default);
    }

    [AvaloniaFact]
    public void Managed_chooser_palette_selects_and_Cancel_answers_null()
    {
        var dialog = new ColorDialog(0);
        var palette = dialog.FindControl<WrapPanel>("PART_Palette")!;
        ((Button)palette.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        dialog.FindControl<TextBox>("PART_Rgb")!.Text.Should().Be("FF0000");
        int? answer = 255;
        dialog.ColorRequest += c => answer = c;
        dialog.FindControl<Button>("PART_Cancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        answer.Should().BeNull();
    }

    [AvaloniaFact]
    public void Managed_chooser_edits_RGB_previews_it_and_returns_OLE_RGB()
    {
        var dialog = new ColorDialog(255);
        var rgb = dialog.FindControl<TextBox>("PART_Rgb")!;
        var ok = dialog.FindControl<Button>("PART_Ok")!;
        rgb.Text.Should().Be("FF0000");
        rgb.Text = "123456";
        var preview = (SolidColorBrush)dialog.FindControl<Border>("PART_Preview")!.Background!;
        preview.Color.Should().Be(Color.FromRgb(0x12, 0x34, 0x56));
        int? chosen = null;
        dialog.ColorRequest += c => chosen = c;
        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        chosen.Should().Be(0x563412);
        rgb.Text = "invalid";
        ok.IsEnabled.Should().BeFalse();
    }
}
