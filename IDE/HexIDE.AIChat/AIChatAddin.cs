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
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HexIDE");
        Directory.CreateDirectory(settingsDir);

        _toolWindow = host.ToolWindows.Register(
            "aiChat",
            "AI Chat",
            null,
            () => new ChatView(host, settingsDir));

        _menuItem = host.Menus.Add(
            "Tools",
            "AI Chat",
            null,
            () => _toolWindow?.Show());
    }

    public void Dispose()
    {
        _menuItem?.Dispose();
        _toolWindow?.Dispose();
    }
}
