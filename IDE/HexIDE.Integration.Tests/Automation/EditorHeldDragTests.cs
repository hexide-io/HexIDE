using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Automation;
using HexIDE.Forms.Views;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Automation;

public class EditorHeldDragTests
{
    [AvaloniaTheory]
    [InlineData(false, 32)]
    [InlineData(false, 34)]
    [InlineData(false, 36)]
    [InlineData(false, 38)]
    [InlineData(false, 40)]
    [InlineData(true, 36)]
    [InlineData(true, 38)]
    [InlineData(true, 40)]
    public async Task Held_drag_selects_text_without_recursive_scrolling(bool code, int height)
    {
        var immediate = new ImmediateToolView
        {
            DataContext = new ImmediateToolViewModel(Substitute.For<ILocalizationService>(), Substitute.For<IDebugController>())
        };
        Control view = code ? new CodeEditorView() : immediate;
        var editor = view.FindControl<TextEditor>(code ? "TextEditor" : "Editor")!;
        editor.Text = "loaded\nclicked\n";
        editor.CaretOffset = editor.Document.TextLength;
        editor.Height = height;
        var window = new Window { Content = view, Width = 500, Height = 350 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        // Observe before AvaloniaEdit's scroll handler: a regression fails with offsets rather than killing the host.
        var handler = editor.TextArea.ActiveInputHandler;
        editor.TextArea.ActiveInputHandler = null;
        var offsets = new List<string>();
        void ObserveScroll(object? sender, EventArgs args)
        {
            offsets.Add(editor.TextArea.TextView.ScrollOffset.ToString());
            var depth = new StackTrace().GetFrames().Count(frame =>
                frame.GetMethod()?.DeclaringType == typeof(AvaloniaEdit.Rendering.TextView)
                && frame.GetMethod()?.Name == "MakeVisible");
            if (depth > 12)
                throw new InvalidOperationException("Recursive scrolling: " + string.Join("; ", offsets));
        }
        editor.TextArea.TextView.ScrollOffsetChanged += ObserveScroll;
        editor.TextArea.ActiveInputHandler = handler;
        try
        {
            var start = editor.TextArea.TextView.TranslatePoint(new Point(1, 1), editor)!.Value;
            var drag = FormattableString.Invariant($"{start.X},{start.Y} to {start.X + 40},{start.Y + 40} hold");
            TestContext.Current.TestOutputHelper!.WriteLine($"bounds={editor.TextArea.TextView.Bounds}; line={editor.TextArea.TextView.DefaultLineHeight}");
            var result = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(
                editor, "click", drag));
            result.Success.Should().BeTrue(result.Error + " viewport=" + editor.TextArea.TextView.Bounds);
            TestContext.Current.TestOutputHelper!.WriteLine("offsets=" + string.Join("; ", offsets));
            editor.SelectionLength.Should().BeGreaterThan(0);
            var selected = editor.SelectedText;
            var release = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(editor, "release", null));
            release.Success.Should().BeTrue(release.Error);
            editor.SelectedText.Should().Be(selected, "release preserves the held selection");
        }
        finally
        {
            editor.TextArea.ActiveInputHandler = null;
            editor.TextArea.TextView.ScrollOffsetChanged -= ObserveScroll;
            HeldButton.Current?.End();
            HeldButton.TakeNote();
            window.Close();
        }
    }
}
