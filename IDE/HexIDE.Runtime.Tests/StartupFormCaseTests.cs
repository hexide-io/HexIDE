using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class StartupFormCaseTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-startup-case-" + Guid.NewGuid().ToString("N"));

    public StartupFormCaseTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        Directory.Delete(dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    // VB6 SP6 measurements: https://github.com/hexide-io/HexIDE/issues/823 (oracle PR #4517).
    [Theory]
    [InlineData("form1")]
    [InlineData("FORM1")]
    [InlineData("Form1")]
    public async Task Startup_form_loads_and_saves_with_the_forms_own_spelling(string startup)
    {
        var scratch = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(scratch, FormComponentClass.Instance, "Form1");
        var (text, _) = new FormSerializer().Serialize(form, "", "Form1.frm");
        File.WriteAllText(Path.Join(dir, "Form1.frm"), text);
        var path = Path.Join(dir, "P.vbp");
        File.WriteAllText(path, $"Type=Exe\r\nForm=Form1.frm\r\nStartup=\"{startup}\"\r\nName=\"P\"\r\n");

        var result = await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>());
        var saved = new ProjectSerializer().Serialize(result.Project, path);

        result.Project.StartupForm.Should().BeSameAs(result.Project.Forms.Single());
        result.Project.StartsAtSubMain.Should().BeFalse();
        saved.ReplaceLineEndings("\n").Split('\n').Should().Contain("Startup=\"Form1\"");
    }
}
