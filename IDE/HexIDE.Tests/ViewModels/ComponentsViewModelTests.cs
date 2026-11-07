using System.IO;
using System.Linq;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The Components dialog (#1103). Every expectation is vb6.exe's, measured through its own Components dialog on the oracle
/// VM: the descriptions it lists, where Save Project writes an added <c>Object=</c> line, and the refusal to remove a
/// library a form uses.
/// </summary>
public class ComponentsViewModelTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static readonly string Ocx6 = ControlLibraries.MSComctlLib.ProjectLine;

    // Form1 holds a ProgressBar, so its header names MSCOMCTL; Form2 holds nothing.
    private static ProjectDefinition Project(bool form1UsesMsComctl, params string[] objectLines)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P1103");
        project.KnownLineKeys.Clear();
        foreach (var line in objectLines)
            project.UnknownPreSectionLines.Add((1, line));
        var header = form1UsesMsComctl ? "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" : "";
        foreach (var (name, head) in new[] { ("Form1", header), ("Form2", "") })
        {
            var form = new FormDeserializer().Deserialize(project,
                $"VERSION 5.00\r\n{head}Begin VB.Form {name}\r\n   Caption = \"{name}\"\r\nEnd\r\nAttribute VB_Name = \"{name}\"\r\n", new NullSink())!;
            form.AbsolutePath = Path.Join(Path.GetTempPath(), "p1103", name + ".frm");
            project.AddForm(form);
        }
        return project;
    }

    [Fact]
    public void Each_library_is_listed_under_its_registry_description_and_checked_where_referenced()
    {
        var vm = new ComponentsViewModel(Project(form1UsesMsComctl: true, Ocx6));

        // Keep these measured registry descriptions independent of later library additions.
        vm.Controls.Where(c => c.Library == ControlLibraries.ComctlLib || c.Library == ControlLibraries.MSComctlLib
                || c.Library == ControlLibraries.MSComDlg || c.Library == ControlLibraries.MSComCtl2
                || c.Library == ControlLibraries.RichTextLib)
            .Select(c => (c.Name, c.Location, c.IsSelected)).Should().Equal(
            ("Microsoft Common Dialog Control 6.0", "COMDLG32.OCX", false),
            ("Microsoft Windows Common Controls 5.0 (SP2)", "COMCTL32.OCX", false),
            ("Microsoft Windows Common Controls 6.0 (SP6)", "MSCOMCTL.OCX", true),
            ("Microsoft Windows Common Controls-2 6.0 (SP6)", "MSCOMCT2.OCX", false),
            ("RichTextLib", "RICHTX32.OCX", false));
        vm.Controls.Should().OnlyContain(c => c.CanChange);
    }

    [Fact]
    public void MsComCtl2_can_be_selected_and_saved_as_a_known_library()
    {
        const string line = "Object={86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCT2.OCX";
        var project = Project(form1UsesMsComctl: false, line);
        var vm = new ComponentsViewModel(project);
        var row = vm.Controls.Single(c => c.Location == "MSCOMCT2.OCX");

        row.Library.Should().NotBeNull();
        row.Name.Should().Be("Microsoft Windows Common Controls-2 6.0 (SP6)");
        row.CanChange.Should().BeTrue();
        row.IsSelected.Should().BeTrue();
        row.Library!.PersistedVersion.Should().Be(393216);
        row.Library.TryBare("NoSuchConstant", out _).Should().BeFalse();
        row.Library.ProjectLine.Should().Be(line);
        row.Library.FormLine.Should().Be("Object = \"{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCT2.OCX\"");
        row.Library.Controls.Keys.Should().BeEquivalentTo("UpDown", "DTPicker", "MonthView", "Animation", "FlatScrollBar");
        InBoxComponentClass.IssueFor("MSComCtl2.UpDown").Should().Be(1093);

        row.IsSelected = false;
        vm.ApplyCommand.Execute(null);
        project.ObjectReferences.Should().BeEmpty();
        row.IsSelected = true;
        vm.ApplyCommand.Execute(null);
        project.ObjectReferences.Select(r => r.File).Should().Equal("MSCOMCT2.OCX");
        new ProjectSerializer().Serialize(project, Path.Join(Path.GetTempPath(), "p1103", "probe.vbp"))
            .Should().Contain(line);
    }

    // Measured: checking 5.0's library and saving the project wrote its line after the Form= lines, and left the line
    // already there after Type=.
    [Fact]
    public void Checking_a_library_writes_its_Object_line_after_the_Form_lines()
    {
        var project = Project(form1UsesMsComctl: true, Ocx6);
        var vm = new ComponentsViewModel(project);

        vm.Controls.Single(c => c.Library == ControlLibraries.ComctlLib).IsSelected = true;
        vm.ApplyCommand.Execute(null);

        var vbp = new ProjectSerializer().Serialize(project, Path.Join(Path.GetTempPath(), "p1103", "probe.vbp"));
        vbp.Should().StartWith("Type=Exe\r\n"
            + "Object={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCTL.OCX\r\n"
            + "Form=Form1.frm\r\n"
            + "Form=Form2.frm\r\n"
            + "Object={6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0; COMCTL32.OCX\r\n"
            + "Startup=");
    }

    // Measured: unchecking a library a form uses is refused at once, with "Can't remove control or reference; in use", and
    // the box stays checked.
    [Fact]
    public void Unchecking_a_library_a_form_uses_is_refused_and_the_box_stays_checked()
    {
        var project = Project(form1UsesMsComctl: true, Ocx6);
        var vm = new ComponentsViewModel(project);
        RuntimeLibrary? refused = null;
        vm.Refused += library => refused = library;

        var row = vm.Controls.Single(c => c.Library == ControlLibraries.MSComctlLib);
        row.IsSelected = false;
        vm.OkCommand.Execute(null);

        refused.Should().BeSameAs(ControlLibraries.MSComctlLib);
        row.IsSelected.Should().BeTrue();
        project.ObjectReferences.Should().ContainSingle();
    }

    [Fact]
    public void Unchecking_a_library_no_form_uses_drops_its_line()
    {
        var project = Project(form1UsesMsComctl: false, Ocx6);
        var vm = new ComponentsViewModel(project);
        var refused = false;
        vm.Refused += _ => refused = true;

        vm.Controls.Single(c => c.Library == ControlLibraries.MSComctlLib).IsSelected = false;
        vm.ApplyCommand.Execute(null);

        refused.Should().BeFalse();
        project.ObjectReferences.Should().BeEmpty();
    }

    // A control library HexIDE doesn't implement is listed, checked, and can't be changed here; Apply leaves its line.
    [Fact]
    public void A_library_HexIDE_does_not_implement_is_listed_and_left_alone()
    {
        const string flexGrid = "Object={5E9E78A0-531B-11CF-91F6-C2863C385E30}#1.0#0; MSFLXGRD.OCX";
        var project = Project(form1UsesMsComctl: false, flexGrid);
        var vm = new ComponentsViewModel(project);

        var row = vm.Controls.Single(c => c.Library is null);
        row.Name.Should().Be("MSFLXGRD.OCX (not implemented in HexIDE)");
        row.IsSelected.Should().BeTrue();
        row.CanChange.Should().BeFalse();

        vm.ApplyCommand.Execute(null);
        project.UnknownPreSectionLines.Select(l => l.RawLine).Should().Contain(flexGrid);
    }

    [Fact]
    public void Selected_Items_Only_lists_the_checked_libraries()
    {
        var vm = new ComponentsViewModel(Project(form1UsesMsComctl: true, Ocx6)) { ShowOnlySelectedControls = true };

        vm.Controls.Select(c => c.Name).Should().Equal("Microsoft Windows Common Controls 6.0 (SP6)");
    }

    [Fact]
    public void Cancel_changes_nothing()
    {
        var project = Project(form1UsesMsComctl: false, Ocx6);
        var vm = new ComponentsViewModel(project);

        vm.Controls.Single(c => c.Library == ControlLibraries.ComctlLib).IsSelected = true;
        vm.CancelCommand.Execute(null);

        project.ObjectReferences.Select(r => r.File).Should().Equal("MSCOMCTL.OCX");
    }
}
