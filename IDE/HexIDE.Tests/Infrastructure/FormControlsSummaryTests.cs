using HexIDE.Runtime.Serialization;
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
    [InlineData(null, false, false)]
    [InlineData(null, false, true)]
    [InlineData("imagecombo1", false, false)]
    [InlineData("imagecombo1", false, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    [InlineData("imagecombo1", true, false)]
    [InlineData("imagecombo1", true, true)]
    public async Task Carried_controls_are_listed_and_found_by_name(string? name, bool nested, bool open)
    {
        var root = new ComponentInstance(FormComponentClass.Instance, "Form1");
        var frame = new ComponentInstance(FrameComponentClass.Instance, "Frame1");
        var parent = nested ? frame : root;
        parent.AddPreservedChildSubtree(0, 0, """
            Begin MSComctlLib.ImageCombo ImageCombo1
               Left = 300
               Top = 450
               Width = 1200
               Height = 600
            End
            """);
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var form = new FormDefinition(project, [root, frame], "");
        project.AddForm(form);
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        var designer = open ? new HexIDE.VisualDesigner.FormEditViewModel(
            null!, Substitute.For<IEventBus>(), null!, null!, null!, Substitute.For<ISettingsService>(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(), new Vb6ProjectFileFormat()).Initialize(form) : null;
        dock.OpenDocuments.Returns(designer is null ? [] : [designer]);
        using var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
                new Vb6ProjectFileFormat(),
                new Vb6ReadOnlyRegionReader());

        var tools = new HexIdeTools(context);
        var missing = await tools.GetFormControlsAsync("Form1", controlName: "Absent");
        missing.Error.Should().Contain("ImageCombo1");
        var result = await tools.GetFormControlsAsync("Form1", controlName: name);
        result.Error.Should().BeNull();
        var row = result.Controls.Should().ContainSingle(c => c.Name == "ImageCombo1").Subject;
        row.Type.Should().Be("MSComctlLib.ImageCombo");
        (row.Left, row.Top, row.Width, row.Height).Should().Be((20d, 30d, 80d, 40d));
        row.Container.Should().Be(nested ? "Frame1" : null);
        var json = JsonSerializer.SerializeToElement(row, ReplyJson.Options);
        json.GetProperty("carried").GetBoolean().Should().BeTrue();
        json.GetProperty("note").GetString().Should().Contain("cannot be moved, sized or set");
        if (name is not null)
        {
            result.Controls.Should().HaveCount(1);
            result.Properties.Should().BeNull();
            result.Note.Should().Contain("cannot be moved, sized or set");
        }
    }

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
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
                new Vb6ProjectFileFormat(),
                new Vb6ReadOnlyRegionReader());
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
