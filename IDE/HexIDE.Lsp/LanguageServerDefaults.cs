using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

/// <summary>
/// The entries HexIDE contributes itself, before any user configuration is layered over them.
///
/// <para>
/// <b>The bundled VB6 server is an ordinary entry.</b> Not a special case beside the configuration, and
/// that is the requirement rather than a tidiness: this capability's purpose says the backend can be
/// replaced without touching the editor, and a backend special-cased in code is abstracted but not
/// replaceable. It is expressed here, in code, only because a default must exist when no file does —
/// deleting the user's file has to restore a working IDE.
/// </para>
/// </summary>
public static class LanguageServerDefaults
{
    public const string BundledVb6Id = "hexide.vb6";

    /// <summary>
    /// The default entries for this installation.
    ///
    /// <para>
    /// The bundled server's command comes from the locator rather than being written down, because its path
    /// differs between a dev build and a publish. An installation where it cannot be found contributes
    /// <b>no</b> entry: a server that is not there should be absent rather than listed as
    /// attached-and-permanently-failing, and a registration that can never connect is a row in a list that
    /// lies.
    /// </para>
    /// </summary>
    public static IReadOnlyList<LanguageServerEntry> For(ILspServerLocator locator, ILogger logger)
    {
        if (locator.FindLspServer() is not { } server)
        {
            logger.LogWarning(
                "The bundled VB6 language server was not found; it contributes no entry and VB6 language "
              + "features are unavailable.");
            return [];
        }

        return
        [
            new LanguageServerEntry
            {
                Id = BundledVb6Id,
                DisplayName = "HexIDE VB6 Language Server",
                Extensions = DocumentLanguage.Vb6Extensions,
                LanguageId = DocumentLanguage.Vb6,
                Transport = "stdio",
                Command = server.FileName,
                Arguments = server.Arguments,

                // Deliberately not the locator's working directory. Left empty so the transport asks the
                // workspace at start, and the server runs in whichever project is open by then.
                WorkingDirectory = "",

                // Below the value an entry takes when it states none, so a user attaching their own VB6
                // server wins the pick-one features without discovering that this field exists.
                Priority = LanguageServerRegistration.BundledPriority,
            },
        ];
    }
}
