using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Conversations;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Redaction;
using HexIDE.Tools.LanguageServers;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Integration.Tests.Views;

public sealed class LanguageServerEditorViewTests
{
    [AvaloniaTheory]
    [InlineData("add", true)]
    [InlineData("add", false)]
    [InlineData("edit", true)]
    [InlineData("edit", false)]
    [InlineData("customise", true)]
    [InlineData("customise", false)]
    public void WindowOpensAFieldFormAndSavesOnlyItsEntry(string action, bool checkFocus)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-editor-view-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "lsp-servers.json");
        const string original = """
            { // keep me
              "servers":[{"id":"user","transport":"stdio","extensions":[".x"],"languageId":"x","command":"old","arguments":"old-args","future": 42}]
            }
            """;
        File.WriteAllText(path, original);
        var defaults = new[] { new LanguageServerEntry
        {
            Id = "bundled", Extensions = [".bas"], LanguageId = "vb6", Transport = "stdio", Command = "server", Priority = -100,
        }, new LanguageServerEntry { Id = "second", Enabled = false },
            new LanguageServerEntry { Id = "third", Enabled = false } };
        var store = new LanguageServerEditorStore(path, defaults, NullLogger<LanguageServerConfigLoader>.Instance,
            new LanguageServerCommandStore(Path.Combine(directory, "seen.json")));
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([]);
        registry.ConfigurationProblems.Returns([]);
        var capture = new ConversationLog();
        var vm = new LanguageServersToolViewModel(registry, new LocalizationService(), capture,
            Substitute.For<IEventBus>(), new Pseudonymiser(), editorStore: store);
        var view = new LanguageServersToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 280 };
        window.DataTemplates.Add(new ViewLocator());
        var localization = new LocalizationService();
        foreach (var key in new[] { "Help", "Command", "Save" })
            window.Resources["Str.Tool.LanguageServers.Editor." + key] = localization.GetString("Str.Tool.LanguageServers.Editor." + key);
        window.SetRenderScaling(1.5);
        window.Show();
        try
        {
            window.UpdateLayout();
            var expected = action == "add" ? vm.AddServerCommand
                : vm.Definitions.Single(d => d.Id == (action == "edit" ? "user" : "bundled")).EditCommand;
            var button = view.GetVisualDescendants().OfType<Button>().SingleOrDefault(b =>
                ReferenceEquals(b.Command, expected) && b.IsVisible);
            button.Should().NotBeNull("the window must offer " + action);
            button!.Command!.Execute(null);
            window.UpdateLayout();
            var editor = view.GetVisualDescendants().OfType<LanguageServerEditorView>().Single();
            editor.DataContext.Should().BeSameAs(vm.Editor);
            var context = System.Threading.ExecutionContext.Capture();
            Dispatcher.UIThread.RunJobs();
            if (context is not null) System.Threading.ExecutionContext.Restore(context);
            window.UpdateLayout();
            var first = editor.GetVisualDescendants().OfType<TextBox>().First();
            if (checkFocus)
            {
                first.IsFocused.Should().BeTrue("opening the form should focus its first field");
                first.TranslatePoint(default, view)!.Value.Y.Should().BeGreaterThanOrEqualTo(0);
                (first.TranslatePoint(default, view)!.Value.Y + first.Bounds.Height).Should().BeLessThanOrEqualTo(view.Bounds.Height);
            }
            var fields = editor.GetVisualDescendants().OfType<TextBox>().ToArray();
            // One box per field the form offers, counted from the form rather than written down, so a new field (#1209's
            // tcpRole) doesn't fail this.
            fields.Should().HaveCount(((LanguageServerEditorViewModel)editor.DataContext!).Fields.Count)
                .And.HaveCountGreaterThanOrEqualTo(28);
            void Set(string name, string value) => fields.Single(t =>
                t.DataContext is LanguageServerFieldViewModel f && f.Name == name).Text = value;
            if (action == "add")
            {
                Set("id", "added");
                Set("extensions", "[\".y\"]");
                Set("languageId", "y");
                Set("transport", "stdio");
            }
            Set("command", "new-server");
            Set("arguments", "\"--flag two\"");
            window.UpdateLayout();
            vm.Editor!.CanSave.Should().BeTrue();
            vm.Editor.Preview.Should().Contain("new-server --flag two");
            editor.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Should().Contain(t => t.Text == vm.Editor.Preview && t.Text.Contains("new-server", StringComparison.Ordinal));
            var save = editor.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.Editor.SaveCommand));
            save.IsEnabled.Should().BeTrue();
            if (!checkFocus)
            {
                var saveTop = save.TranslatePoint(default, view)!.Value.Y;
                saveTop.Should().BeGreaterThanOrEqualTo(0);
                (saveTop + save.Bounds.Height).Should().BeLessThanOrEqualTo(view.Bounds.Height,
                    "Save must stay visible when the fields are taller than the tool pane");
                var fieldScroll = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
                fieldScroll.Extent.Height.Should().BeGreaterThan(fieldScroll.Viewport.Height);
                fieldScroll.ScrollToEnd();
                window.UpdateLayout();
                save.TranslatePoint(default, view)!.Value.Y.Should().Be(saveTop);
                // A long command line scrolls in its own capped area rather than squeezing the fields away.
                Set("arguments", "[" + string.Join(", ", Enumerable.Range(0, 40).Select(i => $"\"--option-{i}={{workspaceDir}}/a/long/path/segment-{i}\"")) + "]");
                window.UpdateLayout();
                var footer = save.GetVisualAncestors().OfType<StackPanel>().First(p => p.GetVisualParent() is Grid);
                footer.Bounds.Height.Should().BeLessThanOrEqualTo(72 + save.Bounds.Height + 16,
                    "the problems and preview may take no more than their capped area from the fields");
                var summary = editor.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "Summary");
                summary.Extent.Height.Should().BeGreaterThan(summary.Viewport.Height, "the preview scrolls in its own area");
                Set("arguments", "\"--flag two\"");
                window.UpdateLayout();
            }
            save.Command!.Execute(null);
            vm.Editor.Should().BeNull();
            var text = File.ReadAllText(path);
            text.Should().Contain("// keep me").And.Contain("\"future\": 42");
            if (action == "edit") text.Should().Be(original.Replace("\"old\"", "\"new-server\"", StringComparison.Ordinal)
                .Replace("old-args", "--flag two", StringComparison.Ordinal));
            else text.Should().Contain("\"command\":\"old\"");
            var id = action == "add" ? "added" : action == "edit" ? "user" : "bundled";
            var saved = store.Preview(text).Entries.Single(e => e.Id == id);
            saved.Command.Should().Be("new-server");
            saved.Arguments!.Text.Should().Be("--flag two");
            new LanguageServerCommandStore(Path.Combine(directory, "seen.json"))
                .IsNewOrChanged(id, LanguageServerConfigLoader.LaunchedCommandOf(saved)!).Should().BeFalse();
            if (action == "customise")
            {
                using var document = LanguageServerEditorStore.Parse(text);
                document.RootElement.GetProperty("servers")[0].EnumerateObject().Select(p => p.Name)
                    .Should().BeEquivalentTo("id", "command", "arguments");
            }
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }
}
