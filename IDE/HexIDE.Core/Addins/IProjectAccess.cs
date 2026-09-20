namespace HexIDE.Addins;

public interface IProjectAccess
{
    /// <summary>The startup project, which is the one a bare document name is most likely to mean.</summary>
    AddinProjectInfo? GetActiveProject();

    /// <summary>The startup project's documents.</summary>
    IReadOnlyList<AddinFileInfo> GetFiles();

    /// <summary>
    /// Every loaded project, in load order.
    /// </summary>
    /// <remarks>
    /// Without this the project argument the other interfaces now take is undiscoverable: an add-in could be
    /// told a name was ambiguous and have no way to learn what to pass instead.
    /// </remarks>
    IReadOnlyList<AddinProjectInfo> GetProjects();
}
