namespace HexIDE.Addins;

public interface IAddinRegistry
{
    IReadOnlyList<AddinInfo> GetAll();
    AddinStatus GetStatus(string assemblyPath);

    /// <summary>
    /// Persists the enabled/disabled state. Disabling immediately takes out everything the add-in registered, whenever it
    /// registered it: language servers, menu items, commands, tool windows, Options pages, project templates and the rest
    /// (#5661). Loading or unloading in-process add-in code takes effect on next IDE start.
    /// </summary>
    void SetEnabled(string assemblyPath, bool enabled);

    /// <summary>
    /// Drops the first-load consent decision for a package (keyed by manifest hash), so the add-in is
    /// prompted again on next launch. Everything the add-in registered is taken out immediately, as on disabling;
    /// in-process add-in code remains loaded until restart.
    /// </summary>
    void RevokeConsent(string manifestHash);
}
