using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// What HexIDE carries without modelling goes through the designer's clipboard with the control that holds it (#1453):
/// a control it does not model, kept as text on its container, and a modelled control's own unmodelled properties,
/// each with the companion records it cites.
/// </summary>
/// <remarks>
/// <para>
/// Paste rebuilds every control from a property snapshot, and neither of these is a property, so both used to be left
/// behind. A cut removed the original, so the next save lost them, with nothing said.
/// </para>
/// <para>
/// A carried control goes only where its name is free, which a cut leaves it. A copy's would repeat the original's,
/// and vb6.exe builds a form holding a name twice without either control (#854), so a copy is still pasted without
/// them until how VB6 renames one is measured. It also goes only into a form that declares the libraries the form it
/// came from did, so an OCX's control never lands where nothing says which OCX it is.
/// </para>
/// </remarks>
public class CarriedPasteTests
{
    private static readonly byte[] ListImageRecord = [4, 0, 0, 0, 3, 3, 3, 3];
    private static readonly byte[] BadgeRecord = [4, 0, 0, 0, 2, 2, 2, 2];
    private static readonly byte[] Companion = [.. ListImageRecord, .. BadgeRecord];

    private const string ComctlDeclaration = "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"";

    // A Frame holding an ImageList, which HexIDE does not model and keeps as text on the Frame, citing a picture; and a
    // CommandButton with a Badge, a property HexIDE does not model, citing a record of its own.
    private const string Form1 =
        "VERSION 5.00\r\n" +
        ComctlDeclaration + "\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   4500\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   345\r\n" +
        "   ClientWidth     =   6000\r\n" +
        "   Begin VB.Frame Frame1 \r\n" +
        "      Caption         =   \"Frame1\"\r\n" +
        "      Height          =   1800\r\n" +
        "      Left            =   600\r\n" +
        "      TabIndex        =   0\r\n" +
        "      Top             =   1500\r\n" +
        "      Width           =   3000\r\n" +
        "      Begin MSComctlLib.ImageList imlIcons \r\n" +
        "         Left            =   150\r\n" +
        "         Top             =   450\r\n" +
        "         BeginProperty Images {2C247F25-8591-11D1-B16A-00C0F0283628} \r\n" +
        "            NumListImages   =   1\r\n" +
        "            BeginProperty ListImage1 {2C247F27-8591-11D1-B16A-00C0F0283628} \r\n" +
        "               Picture         =   \"Form1.frx\":0000\r\n" +
        "               Key             =   \"\"\r\n" +
        "            EndProperty\r\n" +
        "         EndProperty\r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Badge           =   \"Form1.frx\":0008\r\n" +
        "      Caption         =   \"Command1\"\r\n" +
        "      Height          =   495\r\n" +
        "      Left            =   3900\r\n" +
        "      TabIndex        =   1\r\n" +
        "      Top             =   300\r\n" +
        "      Width           =   1500\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private static string Form2(bool declaresComctl) =>
        "VERSION 5.00\r\n" +
        (declaresComctl ? ComctlDeclaration + "\r\n" : "") +
        "Begin VB.Form Form2 \r\n" +
        "   Caption         =   \"Form2\"\r\n" +
        "   ClientHeight    =   4500\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   345\r\n" +
        "   ClientWidth     =   6000\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form2\"\r\n";

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static (FormEditViewModel Vm, FormDefinition Form) Open(string source, byte[]? companion = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink(),
            companion is null ? null : FrxDeserializer.Read(companion, source))!;
        project.AddForm(form);
        var settings = Substitute.For<ISettingsService>();
        settings.GridWidth.Returns(8);
        settings.GridHeight.Returns(8);
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            settings, Substitute.For<ILocalizationService>()).Initialize(form);
        return (vm, form);
    }

    private static void Select(FormEditViewModel vm, string name)
    {
        var target = vm.AllComponents.Single(c => c.Name == name);
        vm.SelectedComponent = target;
        vm.SetSelectedComponents([target]);
    }

    private static (string Text, byte[] Companion) Save(FormEditViewModel vm, FormDefinition form, string fileName)
    {
        vm.CommitLayout();
        var (text, companion) = new FormSerializer().Serialize(form, fileName);
        return (text, companion ?? []);
    }

    private static int Count(string text, string what) => text.Split(what).Length - 1;

    // The record each citation of the saved companion names, by the property citing it.
    private static List<(string Property, byte[] Record)> Cited(string text, byte[] companion)
    {
        var records = FrxDeserializer.Read(companion, text);
        var cited = new List<(string, byte[])>();
        foreach (var line in text.Split("\r\n"))
        {
            if (!FrxDeserializer.IsFrxReference(line)) continue;
            var offset = FrxDeserializer.CitedOffsets(line).Single();
            cited.Add((line[..line.IndexOf('=')].Trim(), records.TryGetValue(offset, out var r) ? r : []));
        }
        return cited;
    }

    [AvaloniaFact]
    public void A_frame_cut_and_pasted_keeps_the_control_inside_it_and_its_picture()
    {
        var (vm, form) = Open(Form1, Companion);
        Select(vm, "Frame1");
        vm.CutSelectedControls();
        vm.PasteControls();

        var (text, companion) = Save(vm, form, "Form1.frm");

        Count(text, "Begin MSComctlLib.ImageList imlIcons").Should().Be(1);
        var frame = text.IndexOf("Begin VB.Frame", System.StringComparison.Ordinal);
        text.IndexOf("Begin MSComctlLib.ImageList", System.StringComparison.Ordinal).Should().BeGreaterThan(frame,
            "it is pasted inside the frame, where it was");
        Cited(text, companion).Should().ContainSingle(c => c.Property == "Picture")
            .Which.Record.Should().Equal(ListImageRecord);
    }

    [AvaloniaFact]
    public void A_frame_copied_and_pasted_leaves_the_original_its_control_and_gives_the_copy_none()
    {
        var (vm, form) = Open(Form1, Companion);
        Select(vm, "Frame1");
        vm.CopySelectedControls();
        vm.PasteControls();

        var (text, _) = Save(vm, form, "Form1.frm");

        Count(text, "Begin VB.Frame").Should().Be(2);
        Count(text, "Begin MSComctlLib.ImageList imlIcons").Should().Be(1,
            "a second imlIcons would be a name twice on the form, which vb6.exe builds without either (#854)");
    }

    [AvaloniaFact]
    public void A_cut_frame_pasted_twice_carries_its_control_into_the_first_paste_only()
    {
        var (vm, form) = Open(Form1, Companion);
        Select(vm, "Frame1");
        vm.CutSelectedControls();
        vm.PasteControls();
        vm.PasteControls();

        var (text, _) = Save(vm, form, "Form1.frm");

        Count(text, "Begin VB.Frame").Should().Be(2);
        Count(text, "Begin MSComctlLib.ImageList imlIcons").Should().Be(1);
    }

    [AvaloniaFact]
    public void A_control_keeps_its_own_unmodelled_property_through_cut_and_paste_and_copy()
    {
        var (vm, form) = Open(Form1, Companion);
        Select(vm, "Command1");
        vm.CutSelectedControls();
        vm.PasteControls();
        Select(vm, vm.AllComponents.Single(c => c.Instance.BaseClass.VBTypeName == "VB.CommandButton").Name);
        vm.CopySelectedControls();
        vm.PasteControls();

        var (text, companion) = Save(vm, form, "Form1.frm");

        Count(text, "Begin VB.CommandButton").Should().Be(2);
        Cited(text, companion).Where(c => c.Property == "Badge").Select(c => c.Record)
            .Should().HaveCount(2).And.AllSatisfy(r => r.Should().Equal(BadgeRecord),
                "a property has no name to repeat, so a copy carries it as VB6 copies every property");
    }

    [AvaloniaFact]
    public void Undoing_the_paste_takes_the_carried_control_away_again()
    {
        var (vm, form) = Open(Form1, Companion);
        Select(vm, "Frame1");
        vm.CutSelectedControls();
        vm.PasteControls();
        vm.UndoStack.Undo();
        vm.UndoStack.Undo();

        var (text, _) = Save(vm, form, "Form1.frm");

        Count(text, "Begin VB.Frame").Should().Be(1, "undoing the paste and then the cut leaves the form as it was read");
        Count(text, "Begin MSComctlLib.ImageList imlIcons").Should().Be(1);
    }

    [AvaloniaTheory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Pasted_into_another_form_it_goes_only_where_that_form_declares_its_library(bool declaresComctl, int expected)
    {
        var (from, _) = Open(Form1, Companion);
        Select(from, "Frame1");
        from.CutSelectedControls();
        var (to, form2) = Open(Form2(declaresComctl));
        to.PasteControls();

        var (text, _) = Save(to, form2, "Form2.frm");

        Count(text, "Begin VB.Frame").Should().Be(1);
        Count(text, "Begin MSComctlLib.ImageList imlIcons").Should().Be(expected,
            "without its Object line nothing says which OCX the ImageList comes from, and VB6 cannot load it");
    }
}
