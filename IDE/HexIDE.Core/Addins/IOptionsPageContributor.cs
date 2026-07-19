namespace HexIDE.Addins;

/// <summary>A handle to a registered Options-page contribution; dispose it to remove the contribution.</summary>
public interface IOptionsPageHandle : IDisposable
{
}

/// <summary>
/// Lets an add-in contribute a settings control to its own page in the Tools&#8594;Options dialog,
/// rendered below the fixed add-in details header. Obtained from <see cref="IHexIdeHost.Options"/>.
/// </summary>
public interface IOptionsPageContributor
{
    /// <summary>
    /// Registers a control factory for the calling add-in's Options page. The factory returns a UI
    /// control (typed as <see cref="object"/> so this contract stays UI-framework-agnostic) and is
    /// invoked when the Options dialog is opened. Call this from your add-in's <c>Initialize</c>
    /// method; dispose the returned handle to remove the contribution.
    /// </summary>
    IOptionsPageHandle RegisterOptionsPage(Func<object> factory);
}
