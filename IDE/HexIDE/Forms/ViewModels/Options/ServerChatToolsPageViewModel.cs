using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// Tools → Options → AI Chat Tools: for each language server that can offer AI Chat tools, whether AI Chat may use them,
/// over what the server's own entry says (#4257). It is the person's choice for an add-in's server too, whose entry the
/// add-in writes. Committed on OK and forgotten on Cancel, as every page is (#2073).
/// </summary>
public partial class ServerChatToolsPageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;
    private readonly ILocalizationService? _localization;
    private readonly Func<IReadOnlyList<ServerChatToolsCandidate>> _servers;

    public ServerChatToolsPageViewModel(ISettingsService settings, Func<IReadOnlyList<ServerChatToolsCandidate>> servers,
        ILocalizationService? localization = null)
    {
        _settings = settings;
        _servers = servers;
        _localization = localization;
        LoadFromSettings();
    }

    public string Title => "AI Chat tools";

    /// <summary>One row per server that can offer tools, by name.</summary>
    public ObservableCollection<ServerChatToolsRow> Servers { get; } = [];

    /// <summary>True when no server can offer tools, so the page says so instead of showing an empty list.</summary>
    public bool HasNoServers => Servers.Count == 0;

    public void LoadFromSettings()
    {
        Servers.Clear();
        var chosen = _settings.ServerChatTools ?? [];
        foreach (var server in _servers().OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var choices = new List<ServerChatToolsChoice>
            {
                new(null, Format("Str.Options.ServerChatTools.AsConfigured", "As configured ({0})", Label(server.Entry))),
                new(ServerChatToolsPolicy.Hints, Label(ServerChatToolsPolicy.Hints)),
                new(ServerChatToolsPolicy.AskEveryCall, Label(ServerChatToolsPolicy.AskEveryCall)),
                new(ServerChatToolsPolicy.Off, Label(ServerChatToolsPolicy.Off)),
            };
            var user = chosen.TryGetValue(server.Id, out var value) ? ServerChatToolsSetting.ParseUser(value) : null;
            Servers.Add(new ServerChatToolsRow(server.Id, server.DisplayName,
                server.Origin is { Length: > 0 } origin ? Format("Str.Options.ServerChatTools.FromAddin", "From {0}", origin) : null,
                Format("Str.Options.ServerChatTools.ChoiceName", "AI Chat tools from {0}", server.DisplayName),
                choices, choices.First(c => c.Policy == user)));
        }
        OnPropertyChanged(nameof(HasNoServers));
    }

    public void SaveToSettings()
    {
        // Only the listed servers change: a choice made for a server that isn't registered now, an add-in's that is turned
        // off, stays for when it is back.
        var map = _settings.ServerChatTools ?? [];
        foreach (var row in Servers)
        {
            if (row.Selected.Policy is { } policy) map[row.Id] = ServerChatToolsSetting.UserValue(policy);
            else map.Remove(row.Id);
        }
        _settings.ServerChatTools = map;
    }

    /// <summary>Every listed server back to what its entry says.</summary>
    public void RestoreDefaults()
    {
        foreach (var row in Servers) row.Selected = row.Choices[0];
    }

    private string Label(ServerChatToolsPolicy policy) => policy switch
    {
        ServerChatToolsPolicy.Hints => Text("Str.Options.ServerChatTools.On", "On: ask unless the tool only reads"),
        ServerChatToolsPolicy.AskEveryCall => Text("Str.Options.ServerChatTools.Ask", "Ask before every call"),
        _ => Text("Str.Options.ServerChatTools.Off", "Off"),
    };

    private string Text(string key, string fallback) => _localization?.GetString(key) ?? fallback;

    private string Format(string key, string fallback, string argument) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Text(key, fallback), argument);
}

/// <summary>A server on the page and the person's choice for it.</summary>
/// <param name="Id">The server's id, which the setting is keyed by.</param>
/// <param name="Name">Its display name.</param>
/// <param name="FromAddin">"From" and the add-in that contributed it; null for the configuration file's and the bundled one.</param>
/// <param name="ChoiceName">The choice's accessible name, which names the server.</param>
public sealed partial class ServerChatToolsRow(string Id, string Name, string? FromAddin, string ChoiceName,
    IReadOnlyList<ServerChatToolsChoice> Choices, ServerChatToolsChoice selected) : ObservableObject
{
    public string Id { get; } = Id;
    public string Name { get; } = Name;
    public string? FromAddin { get; } = FromAddin;
    public string ChoiceName { get; } = ChoiceName;
    public IReadOnlyList<ServerChatToolsChoice> Choices { get; } = Choices;

    [ObservableProperty]
    private ServerChatToolsChoice _selected = selected;
}

/// <summary>One choice: a policy, or null for "as the entry says".</summary>
public sealed record ServerChatToolsChoice(ServerChatToolsPolicy? Policy, string Label)
{
    public override string ToString() => Label;
}
