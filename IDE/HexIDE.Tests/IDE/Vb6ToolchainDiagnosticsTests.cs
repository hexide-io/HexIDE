using HexIDE.Localization;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Tests.IDE;

/// <summary>
/// What a build does to the diagnostics channel.
///
/// <para>
/// The concrete <see cref="Vb6ToolchainService"/> had never been exercised by anything — it appears in the
/// test tree only as a substitute — so nothing had ever asserted what a build puts on that channel, and a
/// build erasing every form's language-server diagnostics went unnoticed (#358). These drive the real
/// method over a real router, with only the compiler invocation itself stubbed: VB6 is Windows-only and
/// installed on developer machines rather than on CI, so a test that starts the real compiler is a test
/// that never runs.
/// </para>
///
/// <para>
/// <b>What that leaves unproven, stated plainly:</b> that <c>VB6.EXE /make</c> exits as expected, that its
/// <c>/out</c> log has the shape <c>ParseVb6Errors</c> reads, and that a real build's marks land in the
/// editor. Everything from the exit code onward is real here; producing the exit code is not.
/// </para>
/// </summary>
public class Vb6ToolchainDiagnosticsTests
{
    // The name a saved form carries since #273 task 2.1, and the one production injects under. It was
    // `vb6://form/Form1`, then briefly an `untitled:` spelling -- neither of which this form answers to,
    // because the fixture gives it a file. AFailedBuildsErrorsSitBesideTheServersOnTheSameForm never
    // proved its own name while these disagreed: the server published under one URI and the compiler
    // injected under another, two ledger documents that AddinDiagnosticsService flattens into one list.
    // Paths, because #273 task 2.9 resolves a compiler diagnostic by the absolute path vb6.exe printed
    // rather than by matching a file stem against a form's name. The fixture gave its forms none, which
    // was invisible while the regex matched nothing the compiler writes.
    private static readonly string ProjectDir =
        Path.Combine(Path.GetTempPath(), "hexide-toolchain-test");

    private static readonly string Form1Path = Path.Combine(ProjectDir, "Form1.frm");

    private static readonly string Form1Uri = LspDocumentUri.ForFile(Form1Path);

    private readonly ILspClient _lsp;
    private readonly ILspClient _server = Substitute.For<ILspClient>();
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();

    public Vb6ToolchainDiagnosticsTests()
    {
        _server.IsRunning.Returns(true);
        _server.AdvertisedCapabilities.Returns(
            System.Text.Json.JsonDocument
                .Parse("""{"textDocumentSync":{"openClose":true,"change":1}}""").RootElement.Clone());

        _lsp = new LspClientRegistry(
            [new LanguageServerRegistration(
                "vb6", "vb6", DocumentLanguage.Vb6Extensions, DocumentLanguage.Vb6, () => _server, 0)],
            Substitute.For<ILogger<LspClientRegistry>>());

        _windows.MessageBox(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(),
            Arg.Any<MessageBoxIcon>()).Returns(MessageBoxResult.Ok);
    }

    /// <summary>
    /// A build whose compiler invocation is replaced by a canned answer. The exe path only has to exist —
    /// <see cref="Vb6ToolchainService.IsAvailable"/> tests for a file, and nothing here starts it.
    /// </summary>
    private sealed class StubbedBuild(
        ILspClient lsp, IWindowManager windows, ILocalizationService localization,
        Vb6ToolchainService.Vb6BuildOutcome? outcome)
        : Vb6ToolchainService(lsp, Substitute.For<IEventBus>(), windows, localization)
    {
        internal override Task<Vb6BuildOutcome?> RunVb6Async(string projectPath) =>
            Task.FromResult(outcome);
    }

    private Vb6ToolchainService Build(int exitCode, string output = "") =>
        WithVb6ExePath(() => new StubbedBuild(
            _lsp, _windows, Localization(), new Vb6ToolchainService.Vb6BuildOutcome(exitCode, output)));

    private Vb6ToolchainService BuildThatTimesOut() =>
        WithVb6ExePath(() => new StubbedBuild(_lsp, _windows, Localization(), outcome: null));

    private static ILocalizationService Localization()
    {
        var l = Substitute.For<ILocalizationService>();
        // The key itself, except where the caller formats the result — a returned key with no placeholder
        // in it silently swallows the compiler output the message box exists to show.
        l.GetString(Arg.Any<string>()).Returns(c =>
            (string)c[0] == "Str.Vb6Toolchain.CompilationFailed" ? "Compilation failed: {0}" : (string)c[0]);
        return l;
    }

    // The constructor reads VB6_EXE, so the path is supplied there and put back straight away. The test
    // assembly's own executable is a file that exists on every platform this runs on.
    private static Vb6ToolchainService WithVb6ExePath(Func<Vb6ToolchainService> create)
    {
        var previous = Environment.GetEnvironmentVariable("VB6_EXE");
        Environment.SetEnvironmentVariable("VB6_EXE", Environment.ProcessPath);
        try { return create(); }
        finally { Environment.SetEnvironmentVariable("VB6_EXE", previous); }
    }

    private static ProjectDefinition ProjectWithTwoForms()
    {
        var project = TestHelpers.CreateProjectWithForm(formName: "Form1");
        project.Forms[0].AbsolutePath = Form1Path;
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form2")
        {
            AbsolutePath = Path.Combine(ProjectDir, "Form2.frm"),
        });
        project.AbsolutePath = Path.Combine(ProjectDir, "Test.vbp");
        return project;
    }

    /// <summary>
    /// A failing build's log, in the shape vb6.exe actually writes -- measured, and quoted in
    /// docs/vb6-fidelity-oracle.md. The leading blank line is real, and so are the spaces either side of
    /// the colon.
    /// </summary>
    private static string CompileErrorIn(string path, int line = 7, string message = "Type mismatch") =>
        $"\r\nCompile Error in File '{path}', Line {line} : {message}\r\nBuild of 'Test.exe' failed.\r\n";

    private async Task GivenTheServerReportedAsyntaxErrorInForm1()
    {
        await _lsp.OpenDocumentAsync(Form1Uri, "Sub Foo()", TestContext.Current.CancellationToken);
        _server.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            _server,
            new PublishDiagnosticsParams(Form1Uri, [
                new Diagnostic(new LspRange(new Position(2, 0), new Position(2, 7)),
                    "Syntax error: unexpected 'End Sub'", DiagnosticSeverity.Error, "vb6")
            ]));
    }

    /// <summary>
    /// A project manager holding nothing. These tests are about which diagnostics survive which event, not
    /// about which document owns them, so the add-in service falls back to naming a diagnostic by the last
    /// segment of the URI it arrived under.
    /// </summary>
    private static IProjectManager NoProjects()
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        return projects;
    }

    [Fact]
    public async Task ASuccessfulBuildLeavesTheServersDiagnosticsWhereTheyWere()
    {
        var project = ProjectWithTwoForms();
        using var seen = new AddinDiagnosticsService(_lsp, NoProjects());
        await GivenTheServerReportedAsyntaxErrorInForm1();

        (await Build(exitCode: 0).MakeWithVb6Async(project)).Should().BeTrue();

        seen.GetAll().Should().ContainSingle(
            "the compiler having nothing to say is not the server having nothing to say")
            .Which.Message.Should().Be("Syntax error: unexpected 'End Sub'");
    }

    [Fact]
    public async Task AFailedBuildWhoseErrorsReachNoFormStillLeavesTheServersDiagnostics()
    {
        // The worst shape in #358: a compile error in a .bas, or output the regex does not match. Nothing
        // is injected, a message box carries the raw text, and the editor used to be left blank.
        var project = ProjectWithTwoForms();
        using var seen = new AddinDiagnosticsService(_lsp, NoProjects());
        await GivenTheServerReportedAsyntaxErrorInForm1();

        (await Build(exitCode: 1, output: "Compile error in Module1").MakeWithVb6Async(project))
            .Should().BeFalse();

        seen.GetAll().Should().ContainSingle()
            .Which.Message.Should().Be("Syntax error: unexpected 'End Sub'");
        await _windows.Received(1).MessageBox(
            Arg.Is<string>(s => s.Contains("Compile error in Module1")), Arg.Any<string?>(),
            Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>());
    }

    [Fact]
    public async Task AFailedBuildsErrorsSitBesideTheServersOnTheSameForm()
    {
        var project = ProjectWithTwoForms();
        using var seen = new AddinDiagnosticsService(_lsp, NoProjects());
        await GivenTheServerReportedAsyntaxErrorInForm1();

        var output = CompileErrorIn(Form1Path);
        (await Build(exitCode: 1, output).MakeWithVb6Async(project)).Should().BeFalse();

        seen.GetAll().Select(d => d.Message).Should().BeEquivalentTo([
            "Syntax error: unexpected 'End Sub'",
            "Type mismatch",
        ]);

        // On ONE document, which is what this test's name claims and what it could not previously check.
        // The server published under Form1Uri while production injected under DocumentWireName.For(form),
        // and while those disagreed they were two ledger entries that GetAll() flattens into one list --
        // so "both are present" passed whether or not they shared a document. Now that a saved form's wire
        // name IS its file URI, the two agree and the claim is testable.
        seen.GetAll().Select(d => d.FileName).Distinct().Should().ContainSingle(
            "a build's errors and a server's belong to the same document, not to two that look alike");
    }

    [Fact]
    public async Task TheNextBuildExpiresTheLastBuildsErrors()
    {
        // The behaviour the erasing clear was there for, and which must survive scoping it to one owner.
        var project = ProjectWithTwoForms();
        using var seen = new AddinDiagnosticsService(_lsp, NoProjects());

        var output = CompileErrorIn(Form1Path);
        await Build(exitCode: 1, output).MakeWithVb6Async(project);
        seen.GetAll().Should().ContainSingle("the failing build put its error on the form");

        await Build(exitCode: 0).MakeWithVb6Async(project);

        seen.GetAll().Should().BeEmpty("the error the previous build reported has been fixed");
    }

    [Fact]
    public async Task ABuildThatTimesOutStillExpiresTheLastBuildsErrors()
    {
        // The timeout used to return before the clear ran, so it was the one path that left a previous
        // build's markers on screen with nothing to remove them.
        var project = ProjectWithTwoForms();
        using var seen = new AddinDiagnosticsService(_lsp, NoProjects());

        var output = CompileErrorIn(Form1Path);
        await Build(exitCode: 1, output).MakeWithVb6Async(project);
        seen.GetAll().Should().ContainSingle();

        (await BuildThatTimesOut().MakeWithVb6Async(project)).Should().BeFalse();

        seen.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task AFormRenamedSinceTheLastBuildIsStillCleared()
    {
        // The other half of #269. The clear follows what the compiler published, so it reaches the URI the
        // editor was opened under — which a walk over the project's CURRENT forms does not.
        var project = ProjectWithTwoForms();
        using var seen = new AddinDiagnosticsService(_lsp, NoProjects());

        var output = CompileErrorIn(Form1Path);
        await Build(exitCode: 1, output).MakeWithVb6Async(project);
        seen.GetAll().Should().ContainSingle();

        // A rename is a write to the root component's Name property — what the Properties window does,
        // and the reason FormDefinition.Name is a computed getter.
        project.Forms[0].Components[0].SetProperty(VBProperties.NameProperty, "Renamed");
        project.Forms[0].Name.Should().Be("Renamed");
        await Build(exitCode: 0).MakeWithVb6Async(project);

        seen.GetAll().Should().BeEmpty("the marks belong to the compiler wherever it put them");
    }
}
