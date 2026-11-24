using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;

namespace HexIDE.Tools.LanguageServers;

public sealed partial class LanguageServerDefinitionViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly Func<Task<LanguageServerTrialResult?>> _test;
    private LanguageServerTrialResult? _result;
    private bool _testing;
    private int _generation;

    public LanguageServerDefinitionViewModel(string id, bool isUser, ICommand editCommand)
        : this(id, isUser, editCommand, new LocalizationService(), () => Task.FromResult<LanguageServerTrialResult?>(null),
            new EventBus())
    {
    }

    internal LanguageServerDefinitionViewModel(string id, bool isUser, ICommand editCommand,
        ILocalizationService localization, Func<Task<LanguageServerTrialResult?>> test, IEventBus events)
    {
        Id = id;
        IsUser = isUser;
        EditCommand = editCommand;
        _localization = localization;
        _test = test;
        ShowMessagesCommand = new HexIDE.Utils.DelegateCommand(() =>
            events.Publish(new OpenProtocolInspectorEvent(LanguageServerTrial.CaptureIdPrefix + Id)));
    }

    public string Id { get; }
    public bool IsUser { get; private set; }
    public bool IsBundled => !IsUser;
    public ICommand EditCommand { get; }
    public ICommand ShowMessagesCommand { get; }
    public string TestLabel => Text(_testing ? "Testing" : "Test");
    public bool HasResult => _result is not null;
    /// <summary>Whether the test reached a connection whose messages the inspector holds: not one declined or not launchable.</summary>
    public bool HasMessages => _result is { Outcome: not (LanguageServerTrialOutcome.Declined or LanguageServerTrialOutcome.NotLaunchable) };
    internal LanguageServerTrialResult? Result => _result;
    public IReadOnlyList<LanguageServerStepViewModel> Steps =>
        _result?.Attempt is { } attempt
            ? [.. attempt.Steps.Select(step => new LanguageServerStepViewModel(step, _localization))]
            : [];

    public string ResultText
    {
        get
        {
            if (_result is not { } result) return "";
            var lines = new List<string>();
            switch (result.Outcome)
            {
                case LanguageServerTrialOutcome.Initialized:
                    lines.Add(Text("Initialized"));
                    lines.Add(result.Identity is { } identity &&
                        (!string.IsNullOrEmpty(identity.Name) || !string.IsNullOrEmpty(identity.Version))
                        ? Format("Identity", identity.Name ?? "", identity.Version ?? "") : Text("NoIdentity"));
                    lines.Add(Format("Capabilities", string.Join(", ", result.AdvertisedCapabilities.Order(StringComparer.Ordinal))));
                    if (result.DeclinedCapabilities.Count > 0)
                        lines.Add(Format("DeclinedCapabilities", string.Join(", ", result.DeclinedCapabilities)));
                    break;
                case LanguageServerTrialOutcome.Failed:
                {
                    var stopped = result.Attempt?.Steps.LastOrDefault(step => step.Outcome == LanguageConnectionStepOutcome.StoppedHere);
                    string? stopLine = null;
                    if (stopped is not null)
                    {
                        var step = new LanguageServerStepViewModel(stopped, _localization);
                        stopLine = $"{step.Stage}: {step.Detail}";
                    }
                    // With no start error, the stage it stopped at is the reason, so it completes the first line
                    // rather than leaving "did not initialize:" with nothing after it.
                    if (string.IsNullOrEmpty(result.StartError))
                    {
                        lines.Add(Format("Failed", stopLine ?? ""));
                    }
                    else
                    {
                        lines.Add(Format("Failed", result.StartError));
                        if (stopLine is not null) lines.Add(stopLine);
                    }
                    if (stopped?.Stage == LanguageConnectionStage.HandshakeSent
                        && stopped.Detail?.Contains("did not answer initialize", StringComparison.Ordinal) == true)
                        lines.Add(Text("Silent"));
                    break;
                }
                default:
                    lines.Add(Text(result.Outcome.ToString()));
                    break;
            }
            if (result.MayNeedWorkspaceArtifact) lines.Add(Text("Artifact"));
            if (result.Disabled) lines.Add(Text("Disabled"));
            // No client exists for these outcomes, so there was no stop to report.
            if (result.Outcome is not (LanguageServerTrialOutcome.Declined or LanguageServerTrialOutcome.NotLaunchable))
                lines.Add(result.StopError is null ? Text("Stopped") : Format("StopFailed", result.StopError));
            return string.Join(Environment.NewLine, lines);
        }
    }

    private string Text(string suffix) => _localization.GetString("Str.Tool.LanguageServers.Trial." + suffix);
    private string Format(string suffix, params object[] values) => string.Format(Text(suffix), values);

    [RelayCommand]
    private async Task TestAsync()
    {
        var generation = _generation;
        _testing = true;
        _result = null;
        LanguageChanged();
        try
        {
            var result = await _test();
            if (generation == _generation) _result = result;
        }
        finally
        {
            _testing = false;
            LanguageChanged();
        }
    }

    internal void ClearResult()
    {
        _generation++;
        _result = null;
        LanguageChanged();
    }

    internal void UpdateSource(bool isUser)
    {
        IsUser = isUser;
        OnPropertyChanged(nameof(IsUser));
        OnPropertyChanged(nameof(IsBundled));
    }

    internal void LanguageChanged()
    {
        OnPropertyChanged(nameof(TestLabel));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(Steps));
    }
}
