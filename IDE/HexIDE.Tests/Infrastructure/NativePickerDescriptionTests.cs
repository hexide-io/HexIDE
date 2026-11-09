using System.ComponentModel;
using System.Reflection;
using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Infrastructure;

[ToolDescription]
public class NativePickerDescriptionTests
{
    [Fact]
    public void Arming_guidance_counts_scratch_project_members_and_explains_open_pickers()
    {
        var text = typeof(HexIdeTools).GetMethod(nameof(HexIdeTools.AnswerNextFileDialogAsync))!
            .GetCustomAttribute<DescriptionAttribute>()!.Description;
        text.Should().Contain("one answer per picker").And.Contain("forms, modules, project")
            .And.Contain("scratch project").And.Contain("--newproject")
            .And.Contain("every member").And.Contain("close or cancel")
            .And.NotContain("stops this server answering");
    }
}
