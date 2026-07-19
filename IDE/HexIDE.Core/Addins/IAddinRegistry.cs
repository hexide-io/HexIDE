namespace HexIDE.Addins;

public interface IAddinRegistry
{
    IReadOnlyList<AddinInfo> GetAll();
    AddinStatus GetStatus(string assemblyPath);

    /// <summary>
    /// Persists the enabled/disabled state. Takes effect on next IDE start.
    /// </summary>
    void SetEnabled(string assemblyPath, bool enabled);

    /// <summary>
    /// Drops the first-load consent decision for a package (keyed by manifest hash), so the add-in is
    /// prompted again on next launch. No effect on the current session.
    /// </summary>
    void RevokeConsent(string manifestHash);
}
