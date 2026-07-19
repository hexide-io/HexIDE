using System.Collections.Generic;

namespace HexIDE.Addins;

public sealed class AddinToolWindowService : IToolWindowContributor
{
    private readonly List<AddinToolWindowViewModel> _tools = [];

    public event Action<AddinToolWindowViewModel>? ToolRegistered;
    public event Action<AddinToolWindowViewModel>? ToolUnregistered;
    public event Action<AddinToolWindowViewModel>? ShowRequested;

    public IReadOnlyList<AddinToolWindowViewModel> Tools => _tools;

    public IToolWindowHandle Register(string id, string title, object? icon, Func<object> factory)
    {
        var tool = new AddinToolWindowViewModel(id, title, factory);
        _tools.Add(tool);
        ToolRegistered?.Invoke(tool);
        return new AddinToolWindowHandle(
            onShow:    () => ShowRequested?.Invoke(tool),
            onDispose: () => { _tools.Remove(tool); ToolUnregistered?.Invoke(tool); });
    }

    private sealed class AddinToolWindowHandle(Action onShow, Action onDispose) : IToolWindowHandle
    {
        private bool _disposed;

        public void Show()
        {
            if (!_disposed) onShow();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            onDispose();
        }
    }
}
