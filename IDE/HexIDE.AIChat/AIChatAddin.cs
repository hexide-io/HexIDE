using HexIDE.IDE;
using HexIDE.Addins;
using HexIDE.AIChat.Views;

[assembly: AddinTitle("AI Chat")]
[assembly: AddinDescription("AI-powered VB6 coding assistant with IDE tool access")]
[assembly: AddinVersion("1.0.0")]
[assembly: AddinAuthor("HexIDE Team")]

namespace HexIDE.AIChat;

public sealed class AIChatAddin : IAddin
{
    private IDisposable? _menuItem;
    private IToolWindowHandle? _toolWindow;

    public void Initialize(IHexIdeHost host)
    {
        var settingsDir = UserDataPath.Directory;
        Directory.CreateDirectory(settingsDir);

        _toolWindow = host.ToolWindows.Register(
            "aiChat",
            AddinTitle.FromKey("Str.AIChat.Title"),
            null,
            () => new ChatView(host, settingsDir));

        _menuItem = host.Menus.Add(
            "Tools",
            AddinTitle.FromKey("Str.AIChat.Menu"),
            null,
            () => _toolWindow?.Show());
    }

    public void Dispose()
    {
        _menuItem?.Dispose();
        _toolWindow?.Dispose();
    }
}
