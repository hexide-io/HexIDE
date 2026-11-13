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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Z_order_follows_live_reordering_within_each_container(bool nested)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var source = """
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.CommandButton Command1
                  Left = 300
                  Top = 300
                  Width = 1200
                  Height = 600
               End
               Begin VB.CommandButton Command2
                  Left = 300
                  Top = 300
                  Width = 1200
                  Height = 600
               End
            End
            Attribute VB_Name = "Form1"
            """;
        var form = new FormDeserializer().Deserialize(project, source, Substitute.For<IDeserializeErrorSink>())!;
        if (nested)
        {
            var frame = new ComponentInstance(FrameComponentClass.Instance, "Frame1");
            foreach (var child in form.Components.Where(c => c.BaseClass is CommandButtonComponentClass))
                child.SetContainer(frame);
            form.UpdateComponents(form.Components.Append(frame).ToList());
        }
        project.AddForm(form);
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns([]);
        using var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        var tools = new HexIdeTools(context);
        async Task Check(string[] names)
        {
            var reply = await tools.GetFormControlsAsync("Form1");
            reply.Error.Should().BeNull();
            var rows = reply.Controls.Where(c => c.Name.StartsWith("Command")).ToArray();
            rows.Select(c => c.Name).Should().Equal(names);
            rows.Select(c => c.Container).Should().OnlyContain(c => c == (nested ? "Frame1" : null));
            rows.Select(c => JsonSerializer.SerializeToElement(c, ReplyJson.Options).GetProperty("zOrder").GetInt32())
                .Should().Equal(0, 1);
            var one = await tools.GetFormControlsAsync("Form1", controlName: names[1]);
            JsonSerializer.SerializeToElement(one.Controls.Single(), ReplyJson.Options).GetProperty("zOrder").GetInt32()
                .Should().Be(1);
        }
        var closed = await tools.GetFormControlsAsync("Form1");
        using var designer = new HexIDE.VisualDesigner.FormEditViewModel(
            null!, Substitute.For<IEventBus>(), null!, null!, null!, Substitute.For<ISettingsService>(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(), new Vb6ProjectFileFormat()).Initialize(form);
        dock.OpenDocuments.Returns([designer]);
        var first = designer.AllComponents.Single(c => c.Name == "Command1");
        designer.SendToBack(first);
        designer.BringToFront(first);
        await Check(["Command2", "Command1"]);
        var opened = await tools.GetFormControlsAsync("Form1");
        opened.Controls.Should().Equal(closed.Controls);
        designer.SendToBack(first);
        await Check(["Command1", "Command2"]);
    }

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
        parent.AddPreservedChildSubtree(nested ? 0 : 1, 0, """
            Begin MSComctlLib.ImageCombo ImageCombo1
               Left = 300
               Top = 450
               Width = 1200
               Height = 600
            End
            """);
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var menu = new ComponentInstance(MenuComponentClass.Instance, "Menu1");
        var form = new FormDefinition(project, [root, menu, frame], "");
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
        json.GetProperty("zOrder").GetInt32().Should().Be(nested ? 0 : 1);
        json.GetProperty("note").GetString().Should().Contain("cannot be moved, sized or set");
        if (open && !nested)
        {
            designer!.BringToFront(designer.AllComponents.Single(c => c.Name == "Frame1"));
            var moved = await tools.GetFormControlsAsync("Form1");
            moved.Controls.Where(c => c.Name is not ("Form1" or "Menu1")).Select(c => c.Name).Should().Equal("ImageCombo1", "Frame1");
            moved.Controls.Where(c => c.Name is not ("Form1" or "Menu1"))
                .Select(c => JsonSerializer.SerializeToElement(c, ReplyJson.Options).GetProperty("zOrder").GetInt32())
                .Should().Equal(0, 1);
        }
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
