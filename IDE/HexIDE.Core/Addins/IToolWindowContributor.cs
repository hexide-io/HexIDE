namespace HexIDE.Addins;

public interface IToolWindowHandle : IDisposable
{
    void Show();
}

public interface IToolWindowContributor
{
    IToolWindowHandle Register(string id, string title, object? icon, Func<object> factory);
}
