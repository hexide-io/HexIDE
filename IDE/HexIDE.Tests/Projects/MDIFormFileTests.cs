using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tests.Infrastructure;
using HexIDE.Runtime.Running;

namespace HexIDE.Tests.Projects;

// VB6 SP6's saved bytes and this slice's refusals: https://github.com/hexide-io/HexIDE/issues/5313.
public sealed class MDIFormFileTests : IDisposable
{
    private readonly string directory = Path.Join(Path.GetTempPath(), "hexide-mdi-" + Guid.NewGuid().ToString("N"));
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private ProjectDefinition project = null!;

    public MDIFormFileTests()
    {
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(Path.Join(RepoTree.Root(), "corpus", "designer", "mdi")))
            File.Copy(file, Path.Join(directory, Path.GetFileName(file)));
    }

    public void Dispose()
    {
        Directory.Delete(directory, true);
        GC.SuppressFinalize(this);
    }

    private async Task<ProjectService> Open()
    {
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(Array.Empty<ProjectDefinition>());
        manager.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(call => project = call.Arg<ProjectDefinition>());
        var service = new ProjectService(() => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), manager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create());
        await service.OpenProject(Path.Join(directory, "MDIProbe.vbp"));
        return service;
    }

    [Fact]
    public async Task The_project_keeps_the_MDI_root_and_its_child()
    {
        await Open();
        project.Forms.Select(form => form.Name).Should().Equal("MDIForm1", "Form1");
        var mdi = project.Forms.First();
        mdi.RootVBTypeName.Should().Be("VB.MDIForm");
        mdi.Components.First().BaseClass.Name.Should().Be("MDIForm");
        mdi.Components.First().BaseClass.Events.Should().BeEmpty("the event list is unmeasured in this slice");
        project.StartupForm.Should().BeSameAs(mdi);
    }

    [Fact]
    public async Task An_unchanged_MDI_form_saves_the_measured_bytes()
    {
        var service = await Open();
        var path = Path.Join(directory, "MDIForm1.frm");
        var bytes = File.ReadAllBytes(path);
        Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be("9189f3e4bae9649cedf1f617b537fb34263e61ca0edd3753b97d4a423fcdee58");
        project.Forms.Should().Contain(form => form.Name == "MDIForm1");
        service.SerializeFormToFile(project.Forms.First(), path).Should().BeTrue();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public async Task A_code_edit_keeps_the_header_as_read()
    {
        var service = await Open();
        var mdi = project.Forms.Single(form => form.Name == "MDIForm1");
        var before = File.ReadAllText(mdi.AbsolutePath!);
        var addition = "\r\nPublic Sub Hello()\r\nEnd Sub\r\n";
        mdi.UpdateCode(mdi.Code + addition);
        service.SerializeFormToFile(mdi, mdi.AbsolutePath!).Should().BeTrue();
        File.ReadAllBytes(mdi.AbsolutePath!).Should().Equal(System.Text.Encoding.ASCII.GetBytes(before + addition));

        // A project-tree rename still reaches the root line while other saved properties remain untouched.
        mdi.Components.First().SetProperty(HexIDE.Runtime.Components.VBProperties.NameProperty, "Renamed");
        mdi.UpdateCode(FormCodeText.RetargetVbName(mdi.Code, "Renamed"));
        service.SerializeFormToFile(mdi, mdi.AbsolutePath!).Should().BeTrue();
        File.ReadAllBytes(mdi.AbsolutePath!).Should().Equal(System.Text.Encoding.ASCII.GetBytes(
            before.Replace("Begin VB.MDIForm MDIForm1 ", "Begin VB.MDIForm Renamed ")
                .Replace("Attribute VB_Name = \"MDIForm1\"", "Attribute VB_Name = \"Renamed\"") + addition));
    }

    [Fact]
    public async Task A_carried_companion_record_survives_a_code_edit_and_Save_As()
    {
        // Storage test, not MDI Icon/Picture value measurements: these records are opaque to this slice.
        var path = Path.Join(directory, "MDIForm1.frm");
        var original = File.ReadAllText(path).Replace("   LinkTopic",
            "   Icon            =   \"MDIForm1.frx\":0008\r\n   Picture         =   \"MDIForm1.frx\":0000\r\n   LinkTopic");
        File.WriteAllText(path, original);
        byte[] firstRecord = [4, 0, 0, 0, 1, 2, 3, 4];
        byte[] secondRecord = [4, 0, 0, 0, 5, 6, 7, 8];
        File.WriteAllBytes(Path.Join(directory, "MDIForm1.frx"), firstRecord.Concat(secondRecord).ToArray());
        var service = await Open();
        var mdi = project.Forms.Single(form => form.Name == "MDIForm1");
        mdi.UpdateCode(mdi.Code + "'code edit\r\n");
        var destination = Path.Join(directory, "Copy.frm");
        service.SerializeFormToFile(mdi, destination).Should().BeTrue();
        var firstSave = File.ReadAllText(destination);
        firstSave.ReplaceLineEndings("\n").Should().Contain("Copy.frx").And.NotContain("MDIForm1.frx").And.EndWith("'code edit\n");
        var companion = File.ReadAllBytes(Path.Join(directory, "Copy.frx"));
        companion.Should().Equal(secondRecord.Concat(firstRecord));

        // HeaderRefresher adopts the written prefix after a save; it must stay paired with the right records.
        mdi.RecordDesignerText(FormCodeText.DesignerHalfOf(firstSave, mdi.Code));
        service.SerializeFormToFile(mdi, destination).Should().BeTrue();
        File.ReadAllText(destination).ReplaceLineEndings("\n").Should().Be(firstSave.ReplaceLineEndings("\n"));
        File.ReadAllBytes(Path.Join(directory, "Copy.frx")).Should().Equal(companion);
    }

    [AvaloniaFact]
    public async Task A_startup_MDI_form_is_refused_before_its_code_runs()
    {
        await Open();
        foreach (var mdi in project.Forms.Where(form => form.Name == "MDIForm1"))
            mdi.UpdateCode("Private Sub Form_Initialize()\r\nError 5\r\nEnd Sub\r\n");
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.ProjectRunner.NotImplementedYet").Returns("Not implemented: {0}");
        localization.GetString("Str.ProjectRunner.StartupFormFailedToLoad").Returns("{0}: {1}");
        localization.GetString("Str.MDIForm.RunNotImplemented").Returns("Localized MDI refusal (#5313)");
        var host = new ProgramHost(windows, localization);
        var engine = new InterpreterRunEngine(TestRunners.Services(host), new DebugController());
        engine.RefusesToStart(project).Should().BeNull("the startup object exists");
        var start = engine.Prepare(project);
        try
        {
            start.Run.Should().BeNull();
            start.Failure!.Text.Should().Contain("Localized MDI refusal (#5313)");
        }
        finally { start.Run?.End(); }
    }

    [Theory]
    [InlineData("Load MDIForm1")]
    [InlineData("MDIForm1.Show")]
    public async Task A_run_refuses_the_MDI_statement_after_prior_code(string statement)
    {
        await Open();
        project.StartsAtSubMain = true;
        foreach (var mdi in project.Forms.Where(form => form.Name == "MDIForm1"))
            mdi.UpdateCode("Private Sub Form_Initialize()\r\nError 5\r\nEnd Sub\r\n");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode($"Public marker As Integer\r\nSub Main()\r\nmarker = 1\r\n{statement}\r\nmarker = 2\r\nEnd Sub\r\n");
        project.AddModule(module);
        var program = new VBProgram(project, new VBWindowContext(Substitute.For<IBasicStandardLibrary>()), null,
            _ => throw new InvalidOperationException("No MDI window may be created"));
        try
        {
            var run = () => program.RunMainAsync();
            await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*#5313*");
            program.ExecutionContext.TryGetVariable(program.Interpreter.PrimaryModule.ModuleEnv, "marker", out var value).Should().BeTrue();
            value.Value.Should().Be((short)1);
        }
        finally { program.Abandon(); }
    }

    [Fact]
    public async Task A_project_that_never_loads_its_MDI_form_can_run()
    {
        await Open();
        project.StartsAtSubMain = true;
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\r\nDebug.Print 7\r\nEnd Sub\r\n");
        project.AddModule(module);
        var library = Substitute.For<IBasicStandardLibrary>();
        var program = new VBProgram(project, new VBWindowContext(library), null,
            _ => throw new InvalidOperationException("The unused form should not open"));
        try
        {
            await program.RunMainAsync();
            library.Received(1).DebugPrint(Arg.Is<Vb6Value>(value => Convert.ToInt32(value.Value) == 7));
        }
        finally { program.Abandon(); }
    }

    [AvaloniaFact]
    public async Task View_designer_reports_the_refusal_instead_of_claiming_a_tab_opened()
    {
        await Open();
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns([]);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.MDIForm.DesignerNotImplemented").Returns("Localized MDI designer refusal (#5313)");
        var editors = new EditorService(windows, dock, null!, null!, null!, null!, () => manager, localization);
        using var context = new HexIDE.Desktop.Server.IdeContext(manager, dock, Substitute.For<HexIDE.Lsp.ILspClient>(), editors,
            Substitute.For<IProjectRunnerService>(), null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!);
        var result = await new HexIDE.Desktop.Server.HexIdeTools(context).ViewDesignerAsync("MDIForm1");
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("#5313");
        dock.DidNotReceiveWithAnyArgs().OpenDocument(null!);
        await windows.Received().MessageBox("Localized MDI designer refusal (#5313)");
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public string Message = "";
        public void LogError(string message) => Message = message;
    }

    [Fact]
    public void An_unknown_root_still_is_not_a_form()
    {
        var sink = new Sink();
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            "VERSION 5.00\r\nBegin VB.Unknown X \r\nEnd\r\n", sink).Should().BeNull();
        sink.Message.Should().Contain("This is not a valid form file");
    }
}
