using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;

namespace HexIDE.Tests.ViewModels;

public class NewProjectViewModelTests
{
    private readonly IWindowManager _windowManager = Substitute.For<IWindowManager>();
    private readonly IRecentProjectsService _recentProjects = Substitute.For<IRecentProjectsService>();
    private readonly ISettingsService _settingsService = Substitute.For<ISettingsService>();
    private readonly IPersonalityService _personality = Substitute.For<IPersonalityService>();
    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();

    public NewProjectViewModelTests()
    {
        AvaloniaTestSetup.EnsureInitialized();
        _recentProjects.GetRecent().Returns(new List<string>());
        _settingsService.PromptForProjectOnStartup.Returns(true);
        _personality.AvailableProjectTypes.Returns(new List<IProjectTemplate>(IProjectTemplate.Templates));
        _localization.GetString("Str.NewProject.Msg.Title").Returns("New Project");
        _localization.GetString("Str.NewProject.Msg.VersionDoesNotSupport").Returns("Your HexIDE version doesn't support {0}");
        _localization.GetString("Str.NewProject.Msg.ProjectNoLongerExists").Returns("The project file no longer exists:\n{0}");
    }

    private NewProjectViewModel CreateSut() => new(_windowManager, _recentProjects, _settingsService, _personality, _localization);

    [Fact]
    public void Title_ShouldBeNewProject()
    {
        var sut = CreateSut();

        sut.Title.Should().Be("New Project");
    }

    [Fact]
    public void CanResize_ShouldBeFalse()
    {
        var sut = CreateSut();

        sut.CanResize.Should().BeFalse();
    }

    [Fact]
    public void Templates_ShouldBePopulatedFromProjectTemplates()
    {
        var sut = CreateSut();

        sut.Templates.Should().HaveCount(IProjectTemplate.Templates.Length);
        sut.Templates[0].Name.Should().Be(IProjectTemplate.Templates[0].Name);
    }

    [Fact]
    public void SelectedTemplate_ShouldDefaultToFirstTemplate()
    {
        var sut = CreateSut();

        sut.SelectedTemplate.Should().NotBeNull();
        sut.SelectedTemplate!.Name.Should().Be(sut.Templates[0].Name);
    }

    [Fact]
    public void OkNewCanExecute_ShouldBeTrue_WhenSelectedTemplateIsNotNull()
    {
        var sut = CreateSut();

        sut.OkNew.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void OkNewCanExecute_ShouldBeFalse_WhenSelectedTemplateIsNull()
    {
        var sut = CreateSut();

        sut.SelectedTemplate = null;

        sut.OkNew.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void OkNewExecute_ShouldFireCloseRequestedTrue_ForSupportedTemplate()
    {
        var sut = CreateSut();
        sut.SelectedTemplate = sut.Templates.First(t => t.Supported);
        bool? received = null;
        sut.CloseRequested += value => received = value;

        sut.OkNew.Execute(null);

        received.Should().BeTrue();
        sut.ResultFilePath.Should().BeNull();
    }

    [Fact]
    public void OkNewExecute_ShouldCallMessageBox_ForUnsupportedTemplate()
    {
        var sut = CreateSut();
        var unsupported = sut.Templates.First(t => !t.Supported);
        sut.SelectedTemplate = unsupported;
        bool? received = null;
        sut.CloseRequested += value => received = value;

        sut.OkNew.Execute(null);

        received.Should().BeNull();
        _windowManager.Received(1).MessageBox(
            Arg.Is<string>(s => s.Contains(unsupported.Name)),
            "HexIDE",
            MessageBoxButtons.Ok,
            MessageBoxIcon.Information);
    }

    [Fact]
    public void CancelExecute_ShouldFireCloseRequestedFalse()
    {
        var sut = CreateSut();
        bool? received = null;
        sut.CloseRequested += value => received = value;

        sut.Cancel.Execute(null);

        received.Should().BeFalse();
    }

    [Fact]
    public void OkNewCanExecuteChanged_ShouldFire_WhenSelectedTemplateChanges()
    {
        var sut = CreateSut();
        var fired = false;
        sut.OkNew.CanExecuteChanged += (_, _) => fired = true;

        sut.SelectedTemplate = null;

        fired.Should().BeTrue();
    }

    [Fact]
    public void RecentProjects_ShouldBePopulatedFromService()
    {
        // Build with Path.Combine, not a literal "C:\...". On Linux CI a backslash is an ordinary
        // filename character, so Path.GetFileName returns the whole string and FileName never
        // narrows to "Project1.vbp" — the test passes on Windows and fails on CI.
        var dir = Path.Combine(Path.GetTempPath(), "Projects");
        _recentProjects.GetRecent().Returns(new List<string>
        {
            Path.Combine(dir, "Project1.vbp"),
            Path.Combine(dir, "Project2.vbp")
        });

        var sut = CreateSut();

        sut.RecentProjects.Should().HaveCount(2);
        sut.RecentProjects[0].FileName.Should().Be("Project1.vbp");
        sut.RecentProjects[1].FileName.Should().Be("Project2.vbp");
    }

    [Fact]
    public void OkRecentCanExecute_ShouldBeFalse_WhenNoRecentSelected()
    {
        var sut = CreateSut();

        sut.OkRecent.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void FileBrowser_ShouldExist()
    {
        var sut = CreateSut();

        sut.FileBrowser.Should().NotBeNull();
        sut.FileBrowser.Entries.Should().NotBeNull();
    }

    [Fact]
    public void DontShowOnStartup_InitializesFromService()
    {
        _settingsService.PromptForProjectOnStartup.Returns(false);

        var sut = CreateSut();

        sut.DontShowOnStartup.Should().BeTrue();
    }

    [Fact]
    public void DontShowOnStartup_PersistsToService_WhenChanged()
    {
        var sut = CreateSut();

        sut.DontShowOnStartup = true;

        _settingsService.Received().PromptForProjectOnStartup = false;
        _settingsService.Received().Save();
    }
}
