using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

public class RefusedPropertyRowsTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new System.InvalidOperationException(message);
    }

    // Width and refusal examples recorded in https://github.com/hexide-io/HexIDE/issues/4180.
    [AvaloniaTheory]
    [InlineData("Width", "abc", 1215d, 81d)]
    [InlineData("TabIndex", "abc", 0, 0)]
    public void Enter_refusal_restores_the_rows_own_units(string property, string input, object shown, object stored)
    {
        const string source = """
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.CommandButton Command1
                  Width = 1215
                  Height = 495
                  TabIndex = 0
               End
            End
            Attribute VB_Name = "Form1"
            """;
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var messages = Substitute.For<IWindowManager>();
        using var designer = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            null!, messages, Substitute.For<ISettingsService>(), loc, new Vb6ProjectFileFormat()).Initialize(form);
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(designer);
        var grid = new PropertiesToolViewModel(dock, Substitute.For<IEventBus>(), messages, loc, new Vb6ProjectFileFormat());
        designer.SelectedComponent = designer.AllComponents.Single(c => c.Name == "Command1");
        grid.FilterText = property;
        var row = grid.Properties.OfType<PropertyViewModel>().Single(p => p.Name == property);
        row.Value.Should().Be(shown);
        var view = new PropertiesToolView { DataContext = grid };
        var window = new Window { Content = view, Width = 400, Height = 400 };
        try
        {
            window.Show();
            view.FindControl<TabItem>("AlphabeticPropertiesTab")!.IsSelected = true;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var box = view.GetVisualDescendants().OfType<PropertyBox>()
                .Single(b => b.IsEffectivelyVisible && b.PropertyClass == row.PropertyClass);
            // PropertyBox.axaml is compiled internal to the IDE. Reproduce its generic editor,
            // as PropertyRowCommitTests does, keeping the real row binding and Enter handler.
            box.GenericTemplate = new FuncDataTemplate<object?>((_, _) =>
            {
                var text = new TextBox();
                text.Bind(TextBox.TextProperty, new Binding(nameof(PropertyBox.Object))
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(PropertyBox) },
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                });
                return text;
            });
            box.Template = new FuncControlTemplate<PropertyBox>((_, scope) =>
                new Panel { Name = "PART_Panel" }.RegisterInNameScope(scope));
            window.UpdateLayout();
            var editor = box.GetVisualDescendants().OfType<TextBox>().Single();
            editor.Text.Should().Be(shown.ToString());
            editor.Text = input;
            UiAutomationDriver.PressKey(editor, "Enter", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            row.Value.Should().Be(shown);
            editor.Text.Should().Be(shown.ToString());
            designer.SelectedComponent.Instance.GetBoxedPropertyOrDefault(row.PropertyClass).Should().Be(stored);
            messages.Received().MessageBox("Str.Tool.Properties.InvalidValue", icon: MessageBoxIcon.Error);
        }
        finally { window.Close(); }
    }
}
