using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

// #2489, the MaskedEdBox oracle's Paste and Copy and cut tables.
public class MaskedEdBoxClipboardTests
{
    [AvaloniaTheory]
    [InlineData("##-##", "__-__", 0, 0, 0, "1234", "12-34", 5, null)]
    [InlineData("##-##", "__-__", 0, 0, 0, "12-34", "12-34", 5, null)]
    [InlineData("##-##", "__-__", 0, 0, 0, "12", "12-__", 3, null)]
    [InlineData("##-##", "__-__", 0, 0, 0, "1a34", "13-4_", 4, null)]
    [InlineData("##-##", "__-__", 0, 0, 0, "ab", "__-__", 0, "b_-__")]
    [InlineData("##-##", "__-__", 0, 0, 0, "123456", "12-34", 5, "12-34")]
    [InlineData("##-##", "12-34", 3, 0, 0, "9", "12-93", 4, "12-93")]
    [InlineData("##-##", "12-34", 0, 5, 0, "98", "98-__", 3, null)]
    [InlineData("##-##", "__-__", 0, 0, 1, "12-34", "12-34", 5, null)]
    [InlineData(null, "abc", 1, 0, 0, "XY", "aXYbc", 3, null)]
    public async Task Paste_rows(string? mask, string before, int start, int length, int mode,
        string clipboard, string after, int caret, string? invalid)
    {
        var box = Create(mask, before, start, length, mode);
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = box };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            await window.Clipboard!.SetTextAsync(clipboard);
            root.Calls.Clear();
            Press(box, TextBox.PasteGesture!);
            await box.ClipboardOperation;
            box.Text.Should().Be(after);
            box.CaretIndex.Should().Be(caret);
            box.SelectionStart.Should().Be(caret);
            box.SelectionEnd.Should().Be(caret);
            root.Calls.Count(c => c.Name == "Mask1_Change").Should().Be(1);
            var errors = root.Calls.Where(c => c.Name == "Mask1_ValidationError").ToArray();
            errors.Should().HaveCount(invalid is null ? 0 : 1);
            if (invalid is not null)
                errors[0].Args!.Select(v => v.Value).Should().Equal(invalid, clipboard == "ab" ? 0L : 5L);
        }
        finally { await window.Clipboard!.ClearAsync(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("12-34", 0, false, 0, 5, "12-34", "12-34")]
    [InlineData("12-34", 1, false, 0, 5, "1234", "12-34")]
    [InlineData("1_-34", 0, false, 0, 5, "1_-34", "1_-34")]
    [InlineData("1_-34", 1, false, 0, 5, "134", "1_-34")]
    [InlineData("12-34", 0, true, 1, 3, "2-3", "14-__")]
    [InlineData("12-34", 1, true, 1, 3, "23", "14-__")]
    public async Task Copy_and_cut_rows(string before, int mode, bool cut, int start, int length,
        string expectedClipboard, string after)
    {
        var box = Create("##-##", before, start, length, mode);
        var window = new Window { Content = box };
        window.Show();
        try
        {
            await window.Clipboard!.ClearAsync();
            Press(box, (cut ? TextBox.CutGesture : TextBox.CopyGesture)!);
            await box.ClipboardOperation;
            (await window.Clipboard.TryGetTextAsync()).Should().Be(expectedClipboard);
            box.Text.Should().Be(after);
        }
        finally { await window.Clipboard!.ClearAsync(); window.Close(); }
    }

    private static VBMaskEdBox Create(string? mask, string before, int start, int length, int mode)
    {
        var box = new VBMaskEdBox();
        VBProps.SetName(box, "Mask1");
        var bag = (ICSharpPropertyBag)box;
        if (mask is not null) bag.TrySetProperty("Mask", new(mask));
        bag.TrySetProperty("Text", new(before));
        bag.TrySetProperty("ClipMode", new(mode));
        bag.TrySetProperty("SelStart", new(start));
        bag.TrySetProperty("SelLength", new(length));
        return box;
    }

    private static void Press(VBMaskEdBox box, KeyGesture gesture) => box.RaiseEvent(new KeyEventArgs
        { RoutedEvent = InputElement.KeyDownEvent, Key = gesture.Key, KeyModifiers = gesture.KeyModifiers });
}
