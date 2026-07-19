namespace HexIDE.Addins;

public interface IAddin : IDisposable
{
    void Initialize(IHexIdeHost host);
}
