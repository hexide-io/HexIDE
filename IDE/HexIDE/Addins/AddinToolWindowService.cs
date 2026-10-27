using System.Collections.Generic;

namespace HexIDE.Addins;

public sealed class AddinToolWindowService : IToolWindowContributor
{
    private readonly List<AddinToolWindowViewModel> _tools = [];
    private readonly HashSet<(string AddinId, string WindowId)> _ids = [];

    public event Action<AddinToolWindowViewModel>? ToolRegistered;
    public event Action<AddinToolWindowViewModel>? ToolUnregistered;
    public event Action<AddinToolWindowViewModel>? ShowRequested;

    public IReadOnlyList<AddinToolWindowViewModel> Tools => _tools;

    public IToolWindowHandle Register(string id, string title, object? icon, Func<object> factory)
    {
        return RegisterOwned("", id, title, icon, factory);
    }

    internal IToolWindowContributor ForOwner(string addinId) => new OwnedContributor(this, addinId);

    private IToolWindowHandle RegisterOwned(string addinId, string id, string title, object? icon, Func<object> factory)
    {
        if (!_ids.Add((addinId, id)))
            throw new InvalidOperationException("A tool window with this id is already registered by this add-in.");
        AddinToolWindowViewModel tool;
        try { tool = new AddinToolWindowViewModel(id, title, factory, addinId); }
        catch { _ids.Remove((addinId, id)); throw; }
        _tools.Add(tool);
        var handle = new AddinToolWindowHandle(
            onShow:    () => ShowRequested?.Invoke(tool),
            onDispose: () => { _tools.Remove(tool); _ids.Remove((addinId, id)); ToolUnregistered?.Invoke(tool); });
        AddinInitializationScope.Record(handle.Dispose);
        ToolRegistered?.Invoke(tool);
        return handle;
    }

    private sealed class OwnedContributor(AddinToolWindowService service, string addinId) : IToolWindowContributor
    {
        public IToolWindowHandle Register(string id, string title, object? icon, Func<object> factory) =>
            service.RegisterOwned(addinId, id, title, icon, factory);
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
