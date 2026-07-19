namespace HexIDE.Addins;

public interface IMenuContributor
{
    IDisposable Add(string parentPath, string label, object? icon, Action onInvoke,
                    Func<bool>? canExecute = null);
}
