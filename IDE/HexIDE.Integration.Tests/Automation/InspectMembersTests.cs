using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using HexIDE.Automation;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// What <c>inspect_element</c> reports about a control's DataContext, and what it leaves out (#562). Inspecting a
/// scroll bar inside a code window returned the editor's 73 members, among them the whole source file and about
/// fifty Dock layout members, ahead of the range the caller asked about.
/// </summary>
public class InspectMembersTests(ITestOutputHelper output)
{
    private static Window Show(Control content, object? dataContext = null)
    {
        var window = new Window { Content = content, Width = 400, Height = 300, DataContext = dataContext };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private sealed class PlainVm
    {
        public string Status { get; set; } = "ready";
    }

    [AvaloniaFact]
    public void An_inherited_DataContext_names_its_owner_instead_of_listing_its_members()
    {
        var button = new Button { Content = "Go" };
        var window = Show(new StackPanel { Children = { button } }, new PlainVm());
        try
        {
            var detail = UiAutomationDriver.Inspect(button, "Window/Button", window);

            detail.DataContextMembers.Should().BeEmpty("the members belong to the control that owns the DataContext");
            detail.DataContextOwner.Should().Be("Window");
            detail.DataContextNote.Should().Contain("inherits its DataContext (PlainVm) from Window");
            detail.DataContextType.Should().Be("PlainVm", "the type is still reported");

            var owner = UiAutomationDriver.Inspect(window, "Window", window);
            owner.DataContextOwner.Should().BeNull();
            owner.DataContextMembers.Should().ContainSingle(m => m.Name == "Status").Which.Value.Should().Be("ready");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_reflection_action_on_the_inheriting_control_still_reaches_the_owners_object()
    {
        var vm = new PlainVm();
        var button = new Button { Content = "Go" };
        var window = Show(new StackPanel { Children = { button } }, vm);
        try
        {
            UiAutomationDriver.Interact(button, "set_property", "Status=done").Success.Should().BeTrue();
            vm.Status.Should().Be("done");
        }
        finally { window.Close(); }
    }

    private sealed class DockedVm : Dock.Model.Mvvm.Controls.Document
    {
        public string Own { get; set; } = "mine";
    }

    [AvaloniaFact]
    public void Members_the_Dock_base_types_declare_are_left_out_and_counted()
    {
        var (members, omitted) = UiAutomationDriver.DescribeDataContext(new DockedVm());

        members.Should().Contain(m => m.Name == "Own");
        members.Should().NotContain(m => m.Name == "Proportion" || m.Name == "CanFloat" || m.Name == "Id");
        omitted.Should().BeGreaterThan(10);
    }

    private sealed class EditorVm : ISearchableDocument
    {
        public TextDocument Document { get; } = new("line one" + (char)10 + "line two" + (char)10 + "line three");
        public int CaretOffset { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }
    }

    [AvaloniaFact]
    public void An_editors_document_reads_as_a_line_count_and_a_pointer_to_get_file_content()
    {
        var document = UiAutomationDriver.ReflectDataContextMembers(new EditorVm()).Single(m => m.Name == "Document");

        document.Value.Should().Be(
            "<3 lines as the editor holds them, unsaved edits included; read the text with get_file_content>");
    }

    private sealed class ImmediateLikeVm
    {
        public TextDocument Document { get; } = new("printed" + (char)10);
    }

    [AvaloniaFact]
    public void A_document_that_is_not_an_editors_still_reads_as_its_text()
    {
        // The Immediate window also exposes this buffer as its editor's value (#2513).
        UiAutomationDriver.ReflectDataContextMembers(new ImmediateLikeVm())
            .Single(m => m.Name == "Document").Value.Should().Be("printed" + (char)10);
    }

    private sealed class LongVm
    {
        public string Text { get; } = new('x', 9000);
    }

    [AvaloniaFact]
    public void A_cut_value_states_its_full_length()
    {
        UiAutomationDriver.ReflectDataContextMembers(new LongVm()).Single().Value
            .Should().EndWith("[truncated at 200 of 9000 chars]");
    }

    private sealed class CardVm
    {
        public string Member00 { get; } = new('x', 5000);
        public string Member01 { get; } = new('x', 5000);
        public string Member02 { get; } = "ready";
        public string Member03 { get; } = "ready";
        public string Member04 { get; } = "ready";
        public string Member05 { get; } = "ready";
        public string Member06 { get; } = "ready";
        public string Member07 { get; } = "ready";
        public string Member08 { get; } = "ready";
        public string Member09 { get; } = "ready";
        public string Member10 { get; } = "ready";
        public string Member11 { get; } = "ready";
        public string Member12 { get; } = "ready";
        public string Member13 { get; } = "ready";
        public string Member14 { get; } = "ready";
        public string Member15 { get; } = "ready";
        public string Member16 { get; } = "ready";
        public string Member17 { get; } = "ready";
        public string Member18 { get; } = "ready";
        public string Member19 { get; } = "ready";
        public string Member20 { get; } = "ready";
        public string Member21 { get; } = "ready";
        public string Member22 { get; } = "ready";
        public string Member23 { get; } = "ready";
        public string Member24 { get; } = "ready";
        public string Member25 { get; } = "ready";
        public string Member26 { get; } = "ready";
        public string Member27 { get; } = "ready";
        public string Member28 { get; } = "ready";
        public string Member29 { get; } = "ready";
    }

    [AvaloniaFact]
    public void A_templated_card_bounds_every_member_and_explains_the_cuts()
    {
        var host = new ContentControl
        {
            Content = new CardVm(),
            ContentTemplate = new FuncDataTemplate<CardVm>((_, _) => new TextBlock { Text = "Id" }),
        };
        var window = Show(host);
        try
        {
            var text = host.GetVisualDescendants().OfType<TextBlock>().Single();
            var detail = UiAutomationDriver.Inspect(text, "Window/Text[Id]", window);
            var json = JsonSerializer.Serialize(detail);
            output.WriteLine($"Templated card reply: {json.Length} characters");

            using var assertions = new AwesomeAssertions.Execution.AssertionScope();
            detail.DataContextOwner.Should().BeNull();
            detail.DataContextMembers.Should().HaveCount(30);
            detail.DataContextMembers.Should().OnlyContain(m => m.Value != null && m.Value.Length <= 234);
            json.Length.Should().BeLessThan(5000);
            detail.DataContextNote.Should().Contain("2 member values over 200 characters were cut")
                .And.Contain("4,000 characters as its value");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_cut_member_note_is_joined_to_the_existing_Dock_note()
    {
        var window = Show(new TextBlock(), new DockedVm { Own = new string('x', 5000) });
        try
        {
            UiAutomationDriver.Inspect(window, "Window", window).DataContextNote
                .Should().Contain("Dock framework's base types")
                .And.Contain("1 member value over 200 characters was cut")
                .And.Contain("4,000 characters as its value");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Uncut_members_need_no_truncation_note()
    {
        var text = new TextBlock { Text = "ready", DataContext = new PlainVm() };
        var window = Show(text);
        try
        {
            UiAutomationDriver.Inspect(text, "Window/Text", window).DataContextNote.Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_text_box_caps_its_own_value_at_4000()
    {
        var text = new TextBox { Text = new string('x', 9000) };
        var window = Show(text);
        try
        {
            UiAutomationDriver.Inspect(text, "Window/Edit", window).Value
                .Should().EndWith("[truncated at 4000 of 9000 chars]");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_non_project_editor_keeps_its_4000_character_value_cap()
    {
        var editor = new TextEditor { Text = new string('x', 9000) };
        var window = Show(editor);
        try
        {
            UiAutomationDriver.Inspect(editor, "Window/Editor", window).Value
                .Should().EndWith("[truncated at 4000 of 9000 chars]");
        }
        finally { window.Close(); }
    }
}
