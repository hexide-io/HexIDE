using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class CompletionDescriptionLayoutTests : IDisposable
{
    private readonly ILspClient client = Substitute.For<ILspClient>();
    private readonly ISettingsService settings = Substitute.For<ISettingsService>();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-completion-" + Guid.NewGuid().ToString("N"));
    private readonly List<Window> windows = [];
    private readonly List<RelatedDocumentEditorViewModel> models = [];

    public CompletionDescriptionLayoutTests()
    {
        Directory.CreateDirectory(directory);
        client.IsRunning.Returns(true);
        client.SupportsCapability(Arg.Any<string>(), "completionProvider").Returns(true);
        settings.AutoListMembers.Returns(true);
        settings.ListMembersAsYouType.Returns(true);
    }

    public void Dispose()
    {
        foreach (var window in windows) window.Close();
        foreach (var model in models) model.Dispose();
        Directory.Delete(directory, true);
        GC.SuppressFinalize(this);
    }

    private (Window Window, TextEditor Editor) Show()
    {
        var path = Path.Combine(directory, "carried.cpp");
        File.WriteAllText(path, "");
        var model = new RelatedDocumentEditorViewModel(client, Substitute.For<IWindowManager>(),
            Substitute.For<ILocalizationService>(), settings).Initialize(new RelatedDocumentDefinition(
                new ProjectDefinition(VBProjectType.EXE, "P"), "carried.cpp", path));
        models.Add(model);
        var view = new RelatedDocumentEditorView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.Focus();
        return (window, editor);
    }

    private static CompletionWindow? List(TextEditor editor) =>
        CodeEditorView.OpenPopupsOf(editor).OfType<CompletionWindow>().FirstOrDefault(w => w.IsOpen);

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static void Pump()
    {
        for (var i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private TextBlock Description(string documentation)
    {
        client.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new CompletionItem("chosen", CompletionItemKind.Text,
                Documentation: new MarkupContent("plaintext", documentation)) });
        var (window, editor) = Show();
        window.KeyTextInput("c");
        Pump();
        var completion = List(editor);
        completion.Should().NotBeNull();
        Press(window, Key.Down);
        Pump();
        completion!.CompletionList.SelectedItem.Should().NotBeNull();
        var popup = completion.GetLogicalDescendants().OfType<Popup>().Single(p => p.IsOpen);
        var tip = popup.Child.Should().BeOfType<CompletionTipContentControl>().Subject;
        var text = tip.GetVisualDescendants().OfType<TextBlock>().Single();
        text.Text.Should().Be(documentation);
        text.Bounds.Width.Should().BeGreaterThan(0);
        return text;
    }

    [AvaloniaFact]
    public void LongDescriptionWrapsWithinMaximumWidth()
    {
        var text = Description(string.Join(" ", Enumerable.Repeat(
            "A completion description with enough documentation to wrap across several lines.", 8)));
        text.Bounds.Width.Should().BeLessThanOrEqualTo(400);
        text.TextLayout.TextLines.Count.Should().BeGreaterThan(1);
        text.Bounds.Height.Should().BeGreaterThan(text.TextLayout.TextLines[0].Height);
    }

    [AvaloniaFact]
    public void ShortDescriptionKeepsItsNaturalSingleLineWidth()
    {
        var text = Description("Short description.");
        text.TextWrapping.Should().Be(TextWrapping.Wrap);
        text.MaxWidth.Should().Be(400);
        text.TextLayout.TextLines.Should().ContainSingle();
        text.Bounds.Width.Should().BeApproximately(text.TextLayout.WidthIncludingTrailingWhitespace, 1);
        text.Bounds.Width.Should().BeLessThan(400);
    }

    [AvaloniaFact]
    public void DescriptionPreservesExplicitLineBreaks()
    {
        var text = Description("First line.\n\nLast line.");
        text.TextWrapping.Should().Be(TextWrapping.Wrap);
        text.TextLayout.TextLines.Should().HaveCount(3);
    }
}
