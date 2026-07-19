namespace HexIDE.Addins;

public sealed class AddinProjectService(IProjectManager projectManager) : IProjectAccess
{
    public AddinProjectInfo? GetActiveProject()
    {
        var p = projectManager.StartupProject;
        return p is null ? null : new AddinProjectInfo(p.Name, p.AbsolutePath ?? string.Empty, GetFiles());
    }

    public IReadOnlyList<AddinFileInfo> GetFiles()
    {
        var p = projectManager.StartupProject;
        if (p is null) return [];

        var files = new List<AddinFileInfo>();
        files.AddRange(p.Forms.Select(f =>
            new AddinFileInfo(f.Name, f.AbsolutePath ?? string.Empty, AddinDocumentKind.Form)));
        files.AddRange(p.Modules.Select(m =>
            new AddinFileInfo(m.Name, m.AbsolutePath ?? string.Empty, AddinDocumentKind.Module)));
        return files;
    }
}
