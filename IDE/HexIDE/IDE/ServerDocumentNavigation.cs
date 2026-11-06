using System;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Localization;
using HexIDE.Lsp.Messages;
using Serilog;

namespace HexIDE.IDE;

internal sealed class ServerDocumentNavigation(ServerMessagePrompt prompts, ILocalizationService localization,
    Func<Uri, CancellationToken, Task<bool>> launch)
{
    // A server may hand the system launcher a web or mail link only. Anything else, such as file: naming a program,
    // is refused without asking: the launcher would run it, and a Yes to a long URI is not informed consent to that.
    private static readonly string[] LaunchableSchemes = ["http", "https", "mailto"];

    internal async Task<bool> ShowExternalAsync(string title, string uri, CancellationToken token)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var target)
            || Array.IndexOf(LaunchableSchemes, target.Scheme.ToLowerInvariant()) < 0)
        {
            Log.Warning("Refused a server's request to launch {Uri}: only http, https and mailto links are launched", uri);
            return false;
        }
        var yes = new MessageActionItem(localization.GetString("Str.Dialog.MessageBox.Button.Yes"));
        var no = new MessageActionItem(localization.GetString("Str.Dialog.MessageBox.Button.No"));
        var answer = await prompts.ShowAsync(title, new(3,
            string.Format(localization.GetString("Str.Lsp.OpenExternalUri"), uri), [yes, no]), token, defaultAction: 1);
        if (answer != yes) return false;
        token.ThrowIfCancellationRequested();
        try { return await launch(target, token); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Log.Warning(error, "Could not launch a server-requested URI");
            return false;
        }
    }
}
