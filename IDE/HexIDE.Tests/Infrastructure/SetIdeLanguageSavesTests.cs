using Avalonia.Threading;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Keep on the gate set_ide_language opens saves the language, as OK in Options does: the gate alone kept it for the
/// session, and the next start came back in the language saved before (#2268).
/// </summary>
public class SetIdeLanguageSavesTests
{
    private sealed class Scene : IDisposable
    {
        public ILanguageSwitchService LanguageSwitch { get; } = Substitute.For<ILanguageSwitchService>();
        public ISettingsService Settings { get; } = Substitute.For<ISettingsService>();
        public TaskCompletionSource<bool> Gate { get; } = new();
        private readonly IdeContext context;
        public HexIdeTools Tools { get; }

        public Scene(string active = "en")
        {
            LanguageSwitch.AvailableLanguages.Returns([new LanguageInfo("en", "English", "ltr"),
                new LanguageInfo("fr", "Français", "ltr")]);
            LanguageSwitch.RegionsFor(Arg.Any<string>()).Returns([]);
            LanguageSwitch.ActiveLanguage.Returns(active);
            // NSubstitute answers a string with "", which reads as an open gate.
            LanguageSwitch.PendingLanguage.Returns((string?)null);
            LanguageSwitch.SwitchWithGateAsync(Arg.Any<string>())
                .Returns(call => call.Arg<string>() == active ? Task.FromResult(true) : Gate.Task);
            context = new IdeContext(Substitute.For<IProjectManager>(), Substitute.For<IDocumentDockService>(),
                Substitute.For<ILspClient>(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
                LanguageSwitch, null!, null!, null!, Settings);
            Tools = new HexIdeTools(context);
        }

        public void Dispose() => context.Dispose();
    }

    [AvaloniaFact]
    public async Task Keep_saves_the_language_for_later_starts()
    {
        using var scene = new Scene();

        var result = await scene.Tools.SetIdeLanguageAsync("FR", CancellationToken.None);
        Dispatcher.UIThread.RunJobs();

        result.Success.Should().BeTrue();
        result.Note.Should().Contain("Keep saves Français for later starts").And.Contain("goes back to 'en' and saves nothing");
        scene.Settings.DidNotReceive().Save();

        scene.Gate.SetResult(true);
        Dispatcher.UIThread.RunJobs();

        scene.Settings.Received(1).ActiveLanguage = "fr";
        scene.Settings.Received(1).Save();
    }

    [AvaloniaFact]
    public async Task Revert_or_the_countdown_saves_nothing()
    {
        using var scene = new Scene();

        var result = await scene.Tools.SetIdeLanguageAsync("fr", CancellationToken.None);
        result.Success.Should().BeTrue();
        scene.Gate.SetResult(false);
        Dispatcher.UIThread.RunJobs();

        scene.Settings.DidNotReceive().ActiveLanguage = Arg.Any<string>();
        scene.Settings.DidNotReceive().Save();
    }

    // SwitchWithGateAsync opens no gate for the language already in use, and the reply used to say one was open anyway.
    [AvaloniaFact]
    public async Task The_language_already_in_use_opens_no_gate_and_is_saved()
    {
        using var scene = new Scene(active: "fr");

        var result = await scene.Tools.SetIdeLanguageAsync("fr", CancellationToken.None);
        Dispatcher.UIThread.RunJobs();

        result.Success.Should().BeTrue();
        result.Note.Should().Contain("already the IDE's language, so no confirmation gate was opened")
            .And.Contain("saved for later starts").And.NotContain("Keep");
        scene.Settings.Received(1).ActiveLanguage = "fr";
        scene.Settings.Received(1).Save();
    }
}
