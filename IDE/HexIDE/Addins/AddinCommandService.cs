namespace HexIDE.Addins;

public sealed record AddinCommand(string Id, string Label, Action OnInvoke, string? Gesture, Func<bool>? CanExecute);

public sealed class AddinCommandService
{
    private readonly List<AddinCommand> _commands = [];
    public IReadOnlyList<AddinCommand> Commands => _commands;

    /// <summary>Raised whenever the command set changes (register or dispose), so the
    /// shell can rebuild key bindings.</summary>
    public event Action? CommandsChanged;

    public IDisposable Register(string id, string label, Action onInvoke, string? gesture, Func<bool>? canExecute)
    {
        var cmd = new AddinCommand(id, label, onInvoke, gesture, canExecute);
        _commands.Add(cmd);
        CommandsChanged?.Invoke();
        return new ActionDisposable(() =>
        {
            _commands.Remove(cmd);
            CommandsChanged?.Invoke();
        });
    }
}
