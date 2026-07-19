namespace HexIDE.Addins;

public interface IProjectAccess
{
    AddinProjectInfo? GetActiveProject();
    IReadOnlyList<AddinFileInfo> GetFiles();
}
