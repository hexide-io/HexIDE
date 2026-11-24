using System.Threading;
using System.Threading.Tasks;

namespace HexIDE.Lsp;

/// <summary>
/// Asks the person whether to launch a language server's command that the IDE hasn't run before, or to dial an
/// address it hasn't connected to before, or either one changed since it last did (#1209, #5465).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a launch the person starts still asks.</b> <c>lsp-servers.json</c> is a file any process running as
/// the person may write, which is why <see cref="LanguageServerCommandStore"/> exists. At startup an unseen
/// command is announced. A launch from the Language Servers window happens later, from a file read again since,
/// so it has to show the command before running it rather than after.
/// </para>
/// <para>
/// <b>What an implementation must do.</b> Show <paramref name="command"/> in full, arguments and environment
/// included, exactly as given, and answer <c>true</c> only when the person explicitly agrees. Closing the prompt
/// any other way, or failing to show it, answers <c>false</c>.
/// </para>
/// </remarks>
public interface ILanguageServerLaunchConfirmation
{
    /// <param name="serverId">The entry's id.</param>
    /// <param name="serverName">The entry's display name, or its id when it has none.</param>
    /// <param name="command">The identity, from <c>LanguageServerConfigLoader.SeenIdentityOf</c>: the command line it starts,
    /// with <c>; connect &lt;endpoint&gt;</c> after it when it also dials, or <c>connect &lt;endpoint&gt;</c> alone for an
    /// entry that only connects.</param>
    /// <param name="cancellationToken">Cancels the prompt, which then answers <c>false</c>.</param>
    Task<bool> ConfirmLaunchAsync(string serverId, string serverName, string command, CancellationToken cancellationToken);
}
