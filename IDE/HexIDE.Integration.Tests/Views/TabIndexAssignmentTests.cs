using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control added or pasted in the designer takes the TabIndex VB6's designer gives it (#925): the next after the
/// highest on the form, counting only the classes that have one, a Label and a Frame among them.
/// </summary>
/// <remarks>
/// <para>
/// It used to take the highest of every component's plus one, the Form's 0 and a Timer's own included, so the first
/// control got 1, a Timer used a number up, and a Label's or a Frame's, kept only as text, went unseen and could be
/// handed out again. A paste kept its source's, so the file held two controls at one TabIndex.
/// </para>
/// <para>
/// Measured in VB6's IDE (docs/vb6-fidelity-oracle.md, "The TabIndex a new or pasted control takes"), on a form whose
/// Label, CommandButton, Frame and the Frame's CheckBox hold 0 to 3: double-clicking the CommandButton tool gave 4, a
/// Timer then took none, the next button 5, a pasted copy of the first button 6, and a pasted copy of the Frame 7 with
/// its CheckBox 8.
/// </para>
/// </remarks>
public class TabIndexAssignmentTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    // The Frame holds the highest number, and on main its TabIndex was kept as text, unseen by the next-number rule.
    private const string Form =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   4500\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   345\r\n" +
        "   ClientWidth     =   6000\r\n" +
        "   Begin VB.Frame Frame1 \r\n" +
        "      Caption         =   \"Frame1\"\r\n" +
        "      Height          =   1335\r\n" +
        "      Left            =   3240\r\n" +
        "      TabIndex        =   3\r\n" +
        "      Top             =   240\r\n" +
        "      Width           =   2415\r\n" +
        "      Begin VB.CheckBox Check1 \r\n" +
        "         Caption         =   \"Check1\"\r\n" +
        "         Height          =   375\r\n" +
        "         Left            =   240\r\n" +
        "         TabIndex        =   2\r\n" +
        "         Top             =   480\r\n" +
        "         Width           =   1335\r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"Command1\"\r\n" +
        "      Height          =   495\r\n" +
        "      Left            =   240\r\n" +
        "      TabIndex        =   1\r\n" +
        "      Top             =   960\r\n" +
        "      Width           =   1215\r\n" +
        "   End\r\n" +
        "   Begin VB.Timer Timer1 \r\n" +
        "      Left            =   1800\r\n" +
        "      Top             =   240\r\n" +
        "   End\r\n" +
        "   Begin VB.Label Label1 \r\n" +
        "      Caption         =   \"Label1\"\r\n" +
        "      Height          =   375\r\n" +
        "      Left            =   240\r\n" +
        "      TabIndex        =   0\r\n" +
        "      Top             =   240\r\n" +
        "      Width           =   1215\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private const string EmptyForm =
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";

    private static (FormEditViewModel Vm, FormDefinition Form) Open(string source)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink())!;
        project.AddForm(form);
        var settings = Substitute.For<ISettingsService>();
        settings.GridWidth.Returns(8);
        settings.GridHeight.Returns(8);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            settings, Substitute.For<ILocalizationService>()).Initialize(form);
        return (vm, form);
    }

    private static string Add(FormEditViewModel vm, ComponentBaseClass type)
    {
        vm.SpawnControlAt(type, new Rect(20, 20, 50, 30));
        return vm.SelectedComponent!.Name;
    }

    private static void CopyAndPaste(FormEditViewModel vm, string name)
    {
        var source = vm.AllComponents.Single(c => c.Name == name);
        vm.SelectedComponent = source;
        vm.SetSelectedComponents([source]);
        vm.CopySelectedControls();
        vm.PasteControls();
    }

    // The control's saved TabIndex line, or null when the file has none.
    private static string? TabIndex(FormEditViewModel vm, FormDefinition form, string control)
    {
        vm.CommitLayout();
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        var start = text.IndexOf($" {control} \r\n", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"{control} is on the form");
        var end = text.IndexOf("Begin ", start + 1, System.StringComparison.Ordinal) is var next and > 0 ? next : text.Length;
        var match = Regex.Match(text[start..end], @"^\s*TabIndex\s*=\s*(\d+)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    [AvaloniaFact]
    public void The_first_control_on_a_form_takes_0()
    {
        var (vm, form) = Open(EmptyForm);

        var name = Add(vm, CommandButtonComponentClass.Instance);

        TabIndex(vm, form, name).Should().Be("0", "the form has no TabIndex of its own to count");
    }

    [AvaloniaFact]
    public void A_control_added_takes_the_next_after_the_highest_a_Frame_and_a_Label_included()
    {
        var (vm, form) = Open(Form);

        var name = Add(vm, CommandButtonComponentClass.Instance);

        TabIndex(vm, form, name).Should().Be("4", "Frame1 holds 3, and a new control taking 3 would share it");
    }

    [AvaloniaFact]
    public void A_Timer_takes_no_TabIndex_and_uses_none_up()
    {
        var (vm, form) = Open(Form);

        var first = Add(vm, CommandButtonComponentClass.Instance);
        var timer = Add(vm, TimerComponentClass.Instance);
        var second = Add(vm, CommandButtonComponentClass.Instance);

        (TabIndex(vm, form, first), TabIndex(vm, form, timer), TabIndex(vm, form, second)).Should().Be(("4", null, "5"));
        // Nor is one held where the file cannot show it: a value on an instance whose class has no such property is
        // carried by a copy into places that read it (#801).
        vm.AllComponents.Single(c => c.Name == timer).Instance
            .TryGetProperty(VBProperties.TabIndexProperty, out _).Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_Label_or_a_Frame_added_is_saved_with_its_TabIndex()
    {
        var (vm, form) = Open(Form);

        var label = Add(vm, LabelComponentClass.Instance);
        var frame = Add(vm, FrameComponentClass.Instance);

        (TabIndex(vm, form, label), TabIndex(vm, form, frame)).Should().Be(("4", "5"));
        (TabIndex(vm, form, "Label1"), TabIndex(vm, form, "Frame1")).Should().Be(("0", "3"), "the loaded ones keep theirs");
    }

    [AvaloniaFact]
    public void A_pasted_control_takes_the_next_TabIndex_not_its_sources()
    {
        var (vm, form) = Open(Form);

        CopyAndPaste(vm, "Command1");

        var pasted = vm.SelectedComponent!.Name;
        TabIndex(vm, form, pasted).Should().Be("4");
        TabIndex(vm, form, "Command1").Should().Be("1");
    }

    [AvaloniaFact]
    public void A_pasted_container_takes_the_next_TabIndex_and_its_contents_the_ones_after()
    {
        var (vm, form) = Open(Form);
        var before = vm.AllComponents.Select(c => c.Name).ToHashSet();

        CopyAndPaste(vm, "Frame1");

        var pasted = vm.AllComponents.Where(c => !before.Contains(c.Name)).ToList();
        var frame = pasted.Single(c => c.Instance.BaseClass is FrameComponentClass).Name;
        var check = pasted.Single(c => c.Instance.BaseClass is CheckBoxComponentClass).Name;
        (TabIndex(vm, form, frame), TabIndex(vm, form, check)).Should().Be(("4", "5"));
    }
}
