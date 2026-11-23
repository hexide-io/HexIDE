using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Lsp;

namespace HexIDE.IDE;

internal sealed class LanguageServerLaunchConfirmation(
    IWindowManager windows, ILocalizationService localization) : ILanguageServerLaunchConfirmation
{
    public async Task<bool> ConfirmLaunchAsync(string serverId, string serverName, string command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync<bool>(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dialog = new LanguageServerLaunchViewModel(serverId, serverName, command, localization);
                using var registration = cancellationToken.Register(() =>
                    Dispatcher.UIThread.Post(() => dialog.CancelCommand.Execute(null)));
                await windows.ShowDialog(dialog);
                return !cancellationToken.IsCancellationRequested && dialog.Agreed;
            });
        }
        catch (Exception)
        {
            // A prompt that wasn't shown cannot authorize a launch.
            return false;
        }
    }
}
