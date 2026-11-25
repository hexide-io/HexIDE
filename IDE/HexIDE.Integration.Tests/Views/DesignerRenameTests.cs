using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using NSubstitute;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The Properties window asks before creating an array, joins an existing array without asking, and
/// records the name and indexes as one undoable change (#1397).
/// </summary>
public class DesignerRenameTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string TwoButtons = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.CommandButton Command1
           End
           Begin VB.CommandButton Command2
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // The refusal is a localization key now (#629); answer it with the English pack's text.
    private static ILocalizationService Naming()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString("Str.Naming.Msg.NameNotUniqueInForm").Returns("Name must be unique in form");
        return loc;
    }

    private static FormEditViewModel Designer(out FormDefinition form, bool array = false)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        form = new FormDeserializer().Deserialize(project, TwoButtons, new NullSink())!;
        project.AddForm(form);
        if (array)
        {
            form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1")
                .SetUntypedProperty(VBProperties.DesignIndexProperty, "0");
            var extra = new ComponentInstance(CommandButtonComponentClass.Instance, "Command1");
            extra.SetUntypedProperty(VBProperties.DesignIndexProperty, "1");
            form.UpdateComponents([.. form.Components, extra]);
        }

        var vm = new FormEditViewModel(
            null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!, null!,
            Naming());
        return vm.Initialize(form);
    }

    // Measured: docs/vb6-fidelity-oracle/the-tabindex-a-new-or-pasted-control-takes.md, #1397 / PR #5234.
    [AvaloniaTheory]
    [InlineData(MessageBoxResult.Yes)]
    [InlineData(MessageBoxResult.No)]
    public void Properties_window_offers_an_array_before_renaming(MessageBoxResult answer)
    {
        using var designer = Designer(out var form);
        var first = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1");
        var second = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command2");
        second.SetProperty(VBProperties.CaptionProperty, "Command2");
        second.SetProperty(VBProperties.LeftProperty, 24);
        second.SetProperty(VBProperties.TopProperty, 32);
        second.SetProperty(VBProperties.TabIndexProperty, 1);
        var before = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        var windows = Substitute.For<IWindowManager>();
        var pending = new TaskCompletionSource<MessageBoxResult>();
        windows.MessageBox(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(),
            Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<MessageBoxLayout>()).Returns(pending.Task);
        var loc = new LocalizationService();
        var dock = Substitute.For<IDocumentDockService>();
        var properties = new PropertiesToolViewModel(dock, new EventBus(), windows, loc);
        dock.ActiveDocument.Returns(designer);
        dock.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(dock,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        designer.SelectedComponent = designer.AllComponents.Single(c => c.Instance == second);
        var row = properties.Properties.OfType<PropertyViewModel>().Single(p => p.PropertyClass == VBProperties.NameProperty);

        row.Value = "Command1";
        new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(before, "the question precedes all changes");
        windows.Received(1).MessageBox(
            "You already have a control named 'Command1'.  Do you want to create a control array?",
            null, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, null, MessageBoxLayout.None);
        pending.SetResult(answer);
        var context = System.Threading.ExecutionContext.Capture();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        if (context is not null) System.Threading.ExecutionContext.Restore(context);

        if (answer == MessageBoxResult.Yes)
        {
            PropertyText.ArrayIndexOf(first).Should().Be(0);
            PropertyText.ArrayIndexOf(second).Should().Be(1);
            second.GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("Command1");
            second.GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("Command2");
            second.GetPropertyOrDefault(VBProperties.LeftProperty).Should().Be(24);
            second.GetPropertyOrDefault(VBProperties.TopProperty).Should().Be(32);
            second.GetPropertyOrDefault(VBProperties.TabIndexProperty).Should().Be(1);
            var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
            var loaded = new FormDeserializer().Deserialize(form.Owner, saved, new NullSink())!;
            loaded.Components.Where(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1")
                .Select(PropertyText.ArrayIndexOf).Should().BeEquivalentTo(new int?[] { 0, 1 });
            designer.UndoStack.Undo();
            new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(before);
            designer.CanUndo.Should().BeFalse("the three properties form one undo step");
            designer.UndoStack.Redo();
            new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(saved);
        }
        else
        {
            new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(before);
            row.Value.Should().Be("Command2");
            designer.CanUndo.Should().BeFalse();
        }
        windows.ReceivedCalls().Should().ContainSingle();
    }

    [AvaloniaFact]
    public void An_unindexed_control_joins_without_a_question_and_undo_restores_it()
    {
        using var designer = Designer(out var form, array: true);
        var second = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command2");
        var before = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        var windows = Substitute.For<IWindowManager>();
        var dock = Substitute.For<IDocumentDockService>();
        var properties = new PropertiesToolViewModel(dock, new EventBus(), windows, Naming());
        dock.ActiveDocument.Returns(designer);
        dock.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(dock,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        designer.SelectedComponent = designer.AllComponents.Single(c => c.Instance == second);
        properties.Properties.OfType<PropertyViewModel>().Single(p => p.PropertyClass == VBProperties.NameProperty).Value = "Command1";
        windows.ReceivedCalls().Should().BeEmpty();
        PropertyText.ArrayIndexOf(second).Should().Be(2);
        designer.UndoStack.Undo();
        new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(before);
        designer.UndoStack.Redo();
        PropertyText.ArrayIndexOf(second).Should().Be(2);
    }

    [AvaloniaFact]
    public void A_free_name_is_taken()
    {
        Designer(out var form);
        var command1 = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1");

        command1.SetUntypedProperty(VBProperties.NameProperty, "cmdOk");

        command1.GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("cmdOk");
    }
}
