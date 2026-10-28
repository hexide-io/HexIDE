using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Projects;

public class ProjectLoadWarningTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-load-warnings-" + Guid.NewGuid().ToString("N"));
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();

    public ProjectLoadWarningTests()
    {
        Directory.CreateDirectory(dir);
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>() switch
        {
            "Str.ProjectLoad.FormNotFound" => "Formular fehlt: {0}",
            "Str.ProjectLoad.Heading" => "Warnungen:",
            "Str.ProjectLoad.MoreWarnings" => "Weitere: {0}",
            "Str.ProjectLoad.UnknownControl" => "Klasse {0}, Steuerelement {1}",
            "Str.ProjectLoad.ActiveXDocuments" => "Übersprungen: {0}",
            _ => ci.Arg<string>()
        });
    }

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private async Task Open(string form, string extra = "")
    {
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Join(dir, "Form1.frm"), form, token);
        var path = Path.Join(dir, "P.vbp");
        await File.WriteAllTextAsync(path, "Type=Exe\r\nName=\"P\"\r\nForm=Form1.frm\r\n" + extra, token);
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var service = new ProjectService(() => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), manager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(),
            new FileBaselineStore(), localization, Substitute.For<IHeaderRefresher>());
        await service.OpenProject(path);
    }

    private string[] Messages => windows.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox))
        .Select(c => (string)c.GetArguments()[0]!).ToArray();

    [Fact]
    public async Task Warning_heading_and_arguments_use_the_current_language()
    {
        await Open("VERSION 5.00\r\nBegin VB.Form Form1\r\nBegin Vendor.Widget Widget1\r\nEnd\r\nEnd\r\n");
        Messages.Should().ContainSingle().Which.ReplaceLineEndings("\n")
            .Should().Be("Warnungen:\nKlasse Vendor.Widget, Steuerelement Widget1");
    }

    [Fact]
    public async Task Warning_limit_formats_the_remaining_count_in_the_current_language()
    {
        var children = string.Concat(Enumerable.Range(1, 12).Select(i => $"Begin Vendor.Widget Widget{i}\r\nEnd\r\n"));
        await Open("VERSION 5.00\r\nBegin VB.Form Form1\r\n" + children + "End\r\n");
        var message = Messages.Should().ContainSingle().Which;
        message.Should().EndWith("Weitere: 2");
        message.Should().NotContain("Steuerelement Widget11");
    }

    [Fact]
    public async Task Skipped_ActiveX_documents_use_the_current_language_and_keep_the_paths()
    {
        await Open("VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\n", "UserDocument=Report.dob\r\n");
        Messages.Should().ContainSingle().Which.Should().Be("Übersprungen:   • Report.dob");
    }

    [Theory]
    [InlineData("UserControl=Control1.ctl", "Control1.ctl", "ModuleFailed", true)]
    [InlineData("Module=Module1; Module1.bas", "Module1.bas", "ModuleFailed", true)]
    [InlineData("Form=Missing.frm", "Missing.frm", "FormNotFound", false)]
    [InlineData("Form=Broken.frm", "Broken.frm", "FormFailed", false)]
    [InlineData("Form=Broken.frm", "Broken.frm", "FormFailedWithReason", true)]
    public async Task Loader_supplies_a_key_fallback_and_separate_arguments(
        string item, string member, string suffix, bool throwOnRead)
    {
        var token = TestContext.Current.CancellationToken;
        var path = Path.Join(dir, "P.vbp");
        await File.WriteAllTextAsync(path, "Type=Exe\r\nName=\"P\"\r\n" + item + "\r\n", token);
        if (suffix != "FormNotFound")
            await File.WriteAllTextAsync(Path.Join(dir, member), "Begin VB.Label Label1\r\nEnd", token);
        var sink = new Sink();
        var loader = new ProjectLoader
        {
            FileRead = (_, _) =>
            {
                if (throwOnRead) throw new IOException("read probe");
            }
        };

        await loader.LoadAsync(path, sink);

        sink.Key.Should().Be("Str.ProjectLoad." + suffix);
        sink.Arguments.Should().Equal(throwOnRead ? [member, "read probe"] : [member]);
        var expected = suffix switch
        {
            "ModuleFailed" => $"Failed to load module {member}: read probe",
            "FormNotFound" => $"Form file not found: {member}",
            "FormFailed" => $"Failed to load form {member}",
            _ => $"Failed to load form {member}: read probe"
        };
        string.Format(sink.Fallback!, sink.Arguments).Should().Be(expected);
    }

    [Fact]
    public async Task Loader_warning_uses_the_current_language_and_keeps_the_path()
    {
        await Open("VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\n", "Form=Missing.frm\r\n");
        Messages.Should().ContainSingle().Which.ReplaceLineEndings("\n")
            .Should().Be("Warnungen:\nFormular fehlt: Missing.frm");
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public string? Key;
        public string? Fallback;
        public object[] Arguments = [];
        public readonly List<string> Keys = [];
        public void LogError(string error) { }
        public void LogError(string key, string fallback, params object[] arguments)
        {
            Keys.Add(key);
            Key = key;
            Fallback = fallback;
            Arguments = arguments;
        }
    }

    [Theory]
    [InlineData("garbage", "ParseFailed", null, null)]
    [InlineData("Begin VB.Label Label1\r\nEnd", "InvalidForm", null, null)]
    [InlineData("Begin VB.Form Form1\r\nBegin Vendor.Widget Widget1\r\nEnd\r\nEnd", "UnknownControl", "Vendor.Widget", "Widget1")]
    [InlineData("Begin VB.Form Form1\r\nCaption = 123\r\nEnd", "InvalidValue", "Caption", "Form1")]
    [InlineData("Begin VB.Form Form1\r\nIcon = \"Form1.frx\":0000\r\nEnd", "MissingOffset", "Icon", "Form1")]
    [InlineData("Begin VB.Form Form1\r\nMystery = \"Form1.frx\":0000\r\nEnd", "MissingRecord", "Mystery", "Form1")]
    public void Deserializer_supplies_a_key_and_separate_arguments(string source, string suffix, string? first, string? second)
    {
        var sink = new Sink();
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, sink,
            new Dictionary<int, byte[]>());
        sink.Key.Should().Be("Str.ProjectLoad." + suffix);
        if (first != null) sink.Arguments.Should().Equal(first, second!);
        else if (suffix == "ParseFailed") sink.Arguments.Should().ContainSingle();
        else sink.Arguments.Should().BeEmpty();
    }

    // #2327: a control HexIDE runs is carried without a warning, as the load would otherwise tell the user that a control
    // which works did not. A Toolbar or an UpDown holding controls, or in a control array, is still refused at run time,
    // so it warns.
    [Theory]
    [InlineData("Begin VB.Form Form1\r\nBegin MSComctlLib.Toolbar Toolbar1\r\nEnd\r\nEnd", null)]
    [InlineData("Begin VB.Form Form1\r\nBegin MSComDlg.CommonDialog CommonDialog1\r\nEnd\r\nEnd", null)]
    [InlineData("Begin VB.Form Form1\r\nBegin MSComCtl2.UpDown UpDown1\r\nEnd\r\nEnd", null)]
    [InlineData("Begin VB.Form Form1\r\nBegin MSComctlLib.Toolbar Bars\r\nIndex = 0\r\nEnd\r\nEnd", "Bars")]
    [InlineData("Begin VB.Form Form1\r\nBegin MSComctlLib.Toolbar Bar\r\nBegin VB.CommandButton Command1\r\nEnd\r\nEnd\r\nEnd", "Bar")]
    [InlineData("Begin VB.Form Form1\r\nBegin MSComCtl2.UpDown Spins\r\nIndex = 0\r\nEnd\r\nEnd", "Spins", "MSComCtl2.UpDown")]
    public void A_carried_control_warns_only_when_HexIDE_does_not_run_it(string source, string? warned,
        string warnedType = "MSComctlLib.Toolbar")
    {
        var sink = new Sink();
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, sink,
            new Dictionary<int, byte[]>());
        if (warned is null)
            sink.Key.Should().BeNull();
        else
        {
            sink.Key.Should().Be("Str.ProjectLoad.UnknownControl");
            sink.Arguments.Should().Equal(warnedType, warned);
        }
    }

    // #1093: VB6's own save of four UpDowns, one bound to a TextBox and one double-clicked from the toolbox, loads without
    // a word, as each of them runs.
    [Fact]
    public async Task VB6s_saved_UpDowns_load_without_a_warning()
    {
        var path = Path.Combine(RepoTree.Root(), "corpus", "inbox-controls", "saved", "updown", "Form1.frm");
        var sink = new Sink();
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), sink, new Dictionary<int, byte[]>());
        form.Should().NotBeNull();
        form!.UnknownChildSubtreeTexts.Should().HaveCount(4, "each UpDown is carried as its block");
        sink.Keys.Should().BeEmpty();
    }
}
