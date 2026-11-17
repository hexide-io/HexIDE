using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Infrastructure;

public class DesignerSizeTests
{
    // SP6 native/p-code measurements are recorded in #589's final comment (PR #4069).
    [AvaloniaTheory]
    [InlineData("add", "Width", -10)]
    [InlineData("add", "Height", -10)]
    [InlineData("move", "Width", -10)]
    [InlineData("move", "Height", -10)]
    [InlineData("property", "Width", -10)]
    [InlineData("property", "Height", -10)]
    [InlineData("grid", "Width", -10)]
    [InlineData("grid", "Height", -10)]
    [InlineData("add", "Height", 0)]
    [InlineData("move", "Height", 0)]
    [InlineData("property", "Height", 0)]
    [InlineData("grid", "Height", 0)]
    [InlineData("add", "Width", 1)]
    [InlineData("move", "Width", 1)]
    [InlineData("property", "Width", 15)]
    [InlineData("grid", "Width", 15)]
    public async Task Size_edits_refuse_negatives_before_any_mutation(string route, string dimension, double size)
    {
        var project = TestHelpers.CreateProjectWithForm("Project1");
        project.WorkingDirectory = Path.Combine(Path.GetTempPath(), "hexide-589-" + Guid.NewGuid().ToString("N"));
        var form = project.Forms[0];
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        var events = new EventBus();
        var windows = Substitute.For<IWindowManager>();
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns("Invalid property value");
        var service = new ProjectService(() => throw new InvalidOperationException(), windows, events,
            projects, Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<HexIDE.Sidecar.IUserSidecarService>(), new FileBaselineStore(), localization,
            Substitute.For<IHeaderRefresher>(), new ComponentRegistry());
        await service.SaveProjectToDirectory(project, project.WorkingDirectory);
        try
        {
            var toolbox = new ToolBoxToolViewModel(dock, new ComponentRegistry(), localization);
            using var designer = new FormEditViewModel(toolbox, events, service, Substitute.For<IEditorService>(), windows,
                Substitute.For<ISettingsService>(), localization).Initialize(form);
            dock.OpenDocuments.Returns([designer]);
            using var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(), null!,
                Substitute.For<IProjectRunnerService>(), service, events, null!, null!, null!, null!, null!, null!,
                null!, null!, null!, null!, localization, null!);
            var tools = new HexIdeTools(context);
            var added = await tools.AddControlAsync("Form1", "CommandButton", 10, 10, 81, 33);
            added.Success.Should().BeTrue();
            var control = designer.AllComponents.Single(c => c.Name == added.ControlName);
            var property = dimension == "Width" ? VBProperties.WidthProperty : VBProperties.HeightProperty;
            var before = control.Instance.GetPropertyOrDefault(property);
            var count = designer.AllComponents.Count;
            var bytes = File.ReadAllBytes(form.AbsolutePath!);
            var success = true;
            string? error = null;
            if (route == "add")
            {
                var result = await tools.AddControlAsync("Form1", "CommandButton", -50, 5000,
                    dimension == "Width" ? size : 81, dimension == "Height" ? size : 33);
                success = result.Success;
                error = result.Error;
            }
            else if (route == "grid")
            {
                designer.SelectedComponent = control;
                var grid = new PropertiesToolViewModel(dock, events, windows, localization);
                dock.ActiveDocument.Returns(designer);
                dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock, new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
                grid.UpdateValue(property, size);
                success = control.Instance.GetPropertyOrDefault(property) != before;
            }
            else
            {
                var result = route == "move"
                    ? await tools.MoveControlAsync("Form1", control.Name, left: 200,
                        width: dimension == "Width" ? size : null, height: dimension == "Height" ? size : null)
                    : await tools.SetControlPropertyAsync("Form1", control.Name, dimension,
                        size.ToString(System.Globalization.CultureInfo.InvariantCulture));
                success = result.Success;
                error = result.Error;
            }
            success.Should().Be(size >= 0);
            if (size < 0)
            {
                if (route != "grid")
                {
                    error.Should().Contain("Invalid property value").And.Contain("589")
                        .And.Contain("refuses to load a .frm");
                    error.Should().Contain(route == "add" ? "No control was created" : "remains at");
                }
                control.Instance.GetPropertyOrDefault(property).Should().Be(before);
                control.RelativeLeft.Should().Be(10);
                designer.AllComponents.Count.Should().Be(count);
                File.ReadAllBytes(form.AbsolutePath!).Should().Equal(bytes);
            }
        }
        finally { Directory.Delete(project.WorkingDirectory, true); }
    }
}
