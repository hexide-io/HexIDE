using System.Collections.Generic;
using HexIDE.Localization;

namespace HexIDE.Addins;

public sealed class AddinToolWindowService(ILocalizationService? localization = null) : IToolWindowContributor
{
    private readonly ILocalizationService _localization = localization ?? new LocalizationService();
    private readonly List<AddinToolWindowViewModel> _tools = [];
    private readonly HashSet<(string AddinId, string WindowId)> _ids = [];

    public event Action<AddinToolWindowViewModel>? ToolRegistered;
    public event Action<AddinToolWindowViewModel>? ToolUnregistered;
    public event Action<AddinToolWindowViewModel>? ShowRequested;

    public IReadOnlyList<AddinToolWindowViewModel> Tools => _tools;

    public IToolWindowHandle Register(string id, string title, object? icon, Func<object> factory)
    {
        return Register(id, new AddinTitle(title), icon, factory);
    }

    public IToolWindowHandle Register(string id, AddinTitle title, object? icon, Func<object> factory) =>
        RegisterLocalized(default, id, title, icon, factory);

    internal IToolWindowContributor ForOwner(string addinId) => ForOwner(new AddinOrigin(addinId, addinId));
    internal IToolWindowContributor ForOwner(AddinOrigin origin) => new OwnedContributor(this, origin);

    private IToolWindowHandle RegisterLocalized(AddinOrigin origin, string id, AddinTitle title, object? icon, Func<object> factory) =>
        RegisterOwned(origin.Id ?? "", id, new RegisteredAddinTitle(title, origin), icon, factory);

    private IToolWindowHandle RegisterOwned(string addinId, string id, RegisteredAddinTitle title, object? icon, Func<object> factory)
    {
        if (!_ids.Add((addinId, id)))
            throw new InvalidOperationException("A tool window with this id is already registered by this add-in.");
        AddinToolWindowViewModel tool;
        try { tool = new AddinToolWindowViewModel(id, title.Resolve(_localization), factory, addinId); }
        catch { _ids.Remove((addinId, id)); throw; }
        _tools.Add(tool);
        void Refresh() => tool.Title = title.Resolve(_localization);
        _localization.LanguageChanged += Refresh;
        var handle = new AddinToolWindowHandle(
            onShow:    () => ShowRequested?.Invoke(tool),
            onDispose: () =>
            {
                _localization.LanguageChanged -= Refresh;
                _tools.Remove(tool);
                _ids.Remove((addinId, id));
                ToolUnregistered?.Invoke(tool);
            });
        AddinInitializationScope.Record(handle.Dispose);
        ToolRegistered?.Invoke(tool);
        return handle;
    }

    private sealed class OwnedContributor(AddinToolWindowService service, AddinOrigin origin) : IToolWindowContributor
    {
        public IToolWindowHandle Register(string id, string title, object? icon, Func<object> factory) =>
            service.RegisterLocalized(origin, id, new AddinTitle(title), icon, factory);
        public IToolWindowHandle Register(string id, AddinTitle title, object? icon, Func<object> factory) =>
            service.RegisterLocalized(origin, id, title, icon, factory);
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
