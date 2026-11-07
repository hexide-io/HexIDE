using System.Text.Json;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Infrastructure;

public class FormControlsSummaryTests
{
    [AvaloniaTheory]
    [InlineData("Line", true)]
    [InlineData("Line", false)]
    [InlineData("Shape", true)]
    [InlineData("Shape", false)]
    [InlineData("CommandButton", true)]
    [InlineData("CommandButton", false)]
    public async Task Summary_reports_only_properties_the_class_has(string type, bool value)
    {
        ComponentBaseClass componentClass = type switch
        {
            "Line" => LineComponentClass.Instance,
            "Shape" => ShapeComponentClass.Instance,
            _ => CommandButtonComponentClass.Instance
        };
        var control = new ComponentInstance(componentClass, "Control1");
        control.SetProperty(VBProperties.VisibleProperty, value);
        if (type == "CommandButton")
            control.SetProperty(VBProperties.EnabledProperty, value);
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var form = new FormDefinition(project,
            [new ComponentInstance(FormComponentClass.Instance, "Form1"), control], "");
        project.AddForm(form);
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns([]);
        // Only these services are used by this read-only tool; the context also subscribes to diagnostics.
        using var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var tools = new HexIdeTools(context);

        foreach (var name in new string?[] { null, "Control1" })
        {
            var result = await tools.GetFormControlsAsync("Form1", controlName: name);
            result.Error.Should().BeNull();
            var summary = result.Controls.Single(c => c.Name == "Control1");
            summary.Visible.Should().Be(value);
            ((bool?)summary.Enabled).Should().Be(type == "CommandButton" ? value : (bool?)null);
            var json = JsonSerializer.SerializeToElement(summary, ReplyJson.Options);
            json.GetProperty("visible").GetBoolean().Should().Be(value);
            if (type == "CommandButton")
                json.GetProperty("enabled").GetBoolean().Should().Be(value);
            else
                // The SDK omits nulls, as it already does for absent Caption/Text.
                json.TryGetProperty("enabled", out _).Should().BeFalse();
        }
    }
}
