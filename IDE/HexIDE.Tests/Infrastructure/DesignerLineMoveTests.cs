using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Infrastructure;

public class DesignerLineMoveTests
{
    [AvaloniaTheory]
    [InlineData(50d, 40d, null)]
    [InlineData(null, null, 80d)]
    [InlineData(null, null, null)]
    public async Task Move_control_refuses_a_Line_without_changing_it(double? left, double? top, double? width)
    {
        using var fixture = new LineDesigner();
        var line = fixture.Line;
        var designer = fixture.Designer;
        var properties = line.GetAllSetProperties();
        var undo = designer.UndoStack;
        var modified = designer.IsModified;
        var undoChanged = false;
        undo.Changed += () => undoChanged = true;

        var reply = await fixture.Tools.MoveControlAsync("Form1", "Line1", left: left, top: top, width: width);

        reply.Success.Should().BeFalse();
        reply.Error.Should().StartWith("Nothing was moved.")
            .And.Contain("A Line has no Left, Top, Width or Height and is placed by its endpoints.")
            .And.Contain("X1 = 600, Y1 = 600, X2 = 2400, Y2 = 1200")
            .And.Contain("in its container's scale")
            .And.Contain("get_form_controls with controlName")
            .And.EndWith("Use set_control_property to set the endpoints.");
        line.GetAllSetProperties().Should().BeEquivalentTo(properties);
        designer.IsDragging.Should().BeFalse();
        undoChanged.Should().BeFalse();
        undo.CanUndo.Should().BeFalse();
        undo.CanRedo.Should().BeFalse();
        designer.IsModified.Should().Be(modified);
    }

    [AvaloniaFact]
    public async Task Set_control_property_can_set_a_Line_endpoint()
    {
        using var fixture = new LineDesigner();
        var directory = Directory.CreateTempSubdirectory("hexide-5784-");
        try
        {
            fixture.Form.Owner.WorkingDirectory = directory.FullName;
            fixture.Form.AbsolutePath = Path.Combine(directory.FullName, "Form1.frm");
            var reply = await fixture.Tools.SetControlPropertyAsync("Form1", "Line1", "X1", "750");

            reply.Success.Should().BeTrue(reply.Error);
            reply.Note.Should().Contain("Line1.X1 is now 750");
            fixture.Line.GetPropertyOrDefault(VBProperties.X1Property).Should().Be(750);
        }
        finally { directory.Delete(true); }
    }

    private sealed class LineDesigner : IDisposable
    {
        private readonly IdeContext context;
        public ComponentInstance Line { get; } = new(LineComponentClass.Instance, "Line1");
        public FormDefinition Form { get; }
        public FormEditViewModel Designer { get; }
        public HexIdeTools Tools { get; }

        public LineDesigner()
        {
            // The twips form from #5784's reproduction.
            Line.SetProperty(VBProperties.X1Property, 600d);
            Line.SetProperty(VBProperties.Y1Property, 600d);
            Line.SetProperty(VBProperties.X2Property, 2400d);
            Line.SetProperty(VBProperties.Y2Property, 1200d);
            var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
            Form = new FormDefinition(project, [new ComponentInstance(FormComponentClass.Instance, "Form1"), Line], "");
            project.AddForm(Form);
            var projects = Substitute.For<IProjectManager>();
            projects.LoadedProjects.Returns([project]);
            var dock = Substitute.For<IDocumentDockService>();
            var events = new EventBus();
            var writer = Substitute.For<IProjectService>();
            writer.SaveForm(Form, false, interactive: false, rewrites: Arg.Any<SaveRewriteReport>()).Returns(true);
            var localization = Substitute.For<ILocalizationService>();
            var settings = Substitute.For<ISettingsService>();
            Designer = new FormEditViewModel(null!, events, writer, null!, null!, settings, localization).Initialize(Form);
            dock.OpenDocuments.Returns([Designer]);
            context = new IdeContext(projects, dock, Substitute.For<ILspClient>(), null!, null!, writer, events,
                null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, localization, settings);
            Tools = new HexIdeTools(context);
        }

        public void Dispose()
        {
            context.Dispose();
            Designer.Dispose();
        }
    }
}
