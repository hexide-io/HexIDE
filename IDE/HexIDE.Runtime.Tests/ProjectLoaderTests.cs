using System.IO;
using System.Text;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The project loader the IDE's open and the Standalone host share (#1062). A made program used to run from the forms
/// alone, read into a list the run never saw, so every module was missing and a second form had no definition. These read
/// a project from disk as both hosts do and check that every member joins the project.
/// </summary>
public sealed class ProjectLoaderTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-loader-" + Guid.NewGuid().ToString("N"));

    public ProjectLoaderTests() => Directory.CreateDirectory(Path.Combine(dir, "Forms"));

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = [];
        public void LogError(string error) => Errors.Add(error);
    }

    private void Write(string relative, string text) =>
        File.WriteAllText(Path.Combine(dir, relative), text.ReplaceLineEndings("\r\n"), Encoding.ASCII);

    private static string Form(string name) => $"""
        VERSION 5.00
        Begin VB.Form {name}
           Caption         =   "{name}"
           ClientHeight    =   3000
           ClientWidth     =   4000
        End
        Attribute VB_Name = "{name}"
        Private Sub Form_Load()
            Debug.Print "{name} load"
        End Sub

        """;

    private string Project(string startup)
    {
        Write("Form1.frm", Form("Form1"));
        Write(@"Forms\Form2.frm".Replace('\\', Path.DirectorySeparatorChar), Form("Form2"));
        Write("Module1.bas", """
            Attribute VB_Name = "Module1"
            Public Function Twice(ByVal n As Integer) As Integer
                Twice = n * 2
            End Function

            """);
        Write("Class1.cls", """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "Class1"
            Attribute VB_GlobalNameSpace = False
            Attribute VB_Creatable = True
            Attribute VB_PredeclaredId = False
            Attribute VB_Exposed = False
            Public Function Hello() As String
                Hello = "hello"
            End Function

            """);
        // Form2 sits in a folder, written with VB6's backslash on every host.
        Write("Made.vbp", $"""
            Type=Exe
            Form=Form1.frm
            Form=Forms\Form2.frm
            Module=Module1; Module1.bas
            Class=Class1; Class1.cls
            Startup="{startup}"
            Name="Made"

            """);
        return Path.Combine(dir, "Made.vbp");
    }

    [Fact]
    public async Task Every_form_and_module_joins_the_project_with_its_startup_form()
    {
        var sink = new Sink();
        var result = await new ProjectLoader().LoadAsync(Project("Form1"), sink);
        var project = result.Project;

        sink.Errors.Should().BeEmpty();
        project.Forms.Select(f => f.Name).Should().Equal("Form1", "Form2");
        project.StartupForm.Should().BeSameAs(project.Forms[0]);
        project.StartsAtSubMain.Should().BeFalse();
        project.Modules.Select(m => (m.Name, m.Kind)).Should().Equal(
            ("Module1", ModuleKind.StandardModule), ("Class1", ModuleKind.ClassModule));
        // The header is the file's, not the code's: the interpreter is given the body.
        project.Modules[0].Code.Should().StartWith("Public Function Twice").And.NotContain("Attribute VB_Name");
        project.Modules[1].Code.Should().StartWith("Public Function Hello").And.NotContain("MultiUse");
        project.Forms[1].Code.Should().Contain("Form2 load");
    }

    [Fact]
    public async Task A_project_that_starts_at_Sub_Main_says_so_and_has_no_startup_form()
    {
        var result = await new ProjectLoader().LoadAsync(Project("Sub Main"), new Sink());

        result.Project.StartsAtSubMain.Should().BeTrue();
        result.Project.StartupForm.Should().BeNull();
        result.Project.Forms.Should().HaveCount(2);
    }

    [Fact]
    public async Task Each_member_file_read_is_reported_and_the_project_file_is_not()
    {
        var read = new List<string>();
        await new ProjectLoader { FileRead = (path, _) => read.Add(Path.GetFileName(path)) }.LoadAsync(Project("Form1"), new Sink());

        read.Should().BeEquivalentTo(["Form1.frm", "Form2.frm", "Module1.bas", "Class1.cls"]);
    }

    [Fact]
    public async Task A_form_whose_file_is_missing_costs_only_itself_and_its_line_is_kept()
    {
        var path = Project("Form1");
        File.Delete(Path.Combine(dir, "Forms", "Form2.frm"));
        var sink = new Sink();

        var project = (await new ProjectLoader().LoadAsync(path, sink)).Project;

        project.Forms.Select(f => f.Name).Should().Equal("Form1");
        project.Modules.Should().HaveCount(2);
        sink.Errors.Should().ContainSingle().Which.Should().Contain(@"Forms\Form2.frm");
        project.PreservedItemLines.Should().Contain(@"Form=Forms\Form2.frm");
    }
}
