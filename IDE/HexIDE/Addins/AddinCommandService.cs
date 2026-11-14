using HexIDE.Localization;
namespace HexIDE.Addins;

public sealed record AddinCommand(string Id, string Label, Action OnInvoke, string? Gesture, Func<bool>? CanExecute) : INotifyPropertyChanged
{
    private string _label = Label;
    public string Label
    {
        get => _label;
        internal set
        {
            if (_label == value) return;
            _label = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class AddinCommandService(ILocalizationService? localization = null)
{
    private readonly ILocalizationService _localization = localization ?? new LocalizationService();
    private readonly List<AddinCommand> _commands = [];
    public IReadOnlyList<AddinCommand> Commands => _commands;

    /// <summary>Raised whenever the command set changes (register or dispose), so the
    /// shell can rebuild key bindings.</summary>
    public event Action? CommandsChanged;

    public IDisposable Register(string id, string label, Action onInvoke, string? gesture, Func<bool>? canExecute)
        => Register(id, new AddinTitle(label), onInvoke, gesture, canExecute);

    public IDisposable Register(string id, AddinTitle label, Action onInvoke, string? gesture, Func<bool>? canExecute) =>
        RegisterOwned(default, id, label, onInvoke, gesture, canExecute);

    internal IDisposable RegisterOwned(AddinOrigin origin, string id, AddinTitle label, Action onInvoke, string? gesture, Func<bool>? canExecute)
    {
        var text = new RegisteredAddinTitle(label, origin);
        var cmd = new AddinCommand(id, text.Resolve(_localization), onInvoke, gesture, canExecute);
        void Refresh() => cmd.Label = text.Resolve(_localization);
        _localization.LanguageChanged += Refresh;
        _commands.Add(cmd);
        var handle = new ActionDisposable(() =>
        {
            _localization.LanguageChanged -= Refresh;
            _commands.Remove(cmd);
            CommandsChanged?.Invoke();
        });
        AddinInitializationScope.Record(handle.Dispose);
        CommandsChanged?.Invoke();
        return handle;
    }
}
