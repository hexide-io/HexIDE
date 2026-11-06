namespace HexIDE.Addins;

public interface IAddinRegistry
{
    IReadOnlyList<AddinInfo> GetAll();
    AddinStatus GetStatus(string assemblyPath);

    /// <summary>
    /// Persists the enabled/disabled state. Disabling immediately unregisters contributed language servers;
    /// loading or unloading in-process add-in code takes effect on next IDE start.
    /// </summary>
    void SetEnabled(string assemblyPath, bool enabled);

    /// <summary>
    /// Drops the first-load consent decision for a package (keyed by manifest hash), so the add-in is
    /// prompted again on next launch. Contributed language servers are unregistered immediately;
    /// in-process add-in code remains loaded until restart.
    /// </summary>
    void RevokeConsent(string manifestHash);
}
