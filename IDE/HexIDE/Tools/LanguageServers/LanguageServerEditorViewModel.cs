using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Localization;
using HexIDE.Lsp;

namespace HexIDE.Tools.LanguageServers;

public sealed class LanguageServerFieldViewModel : ObservableObject
{
    private string _text;
    public string Name { get; }
    public bool IsJson { get; }
    public bool IsReadOnly { get; }
    public string Original { get; }
    public string Text { get => _text; set => SetProperty(ref _text, value); }

    public LanguageServerFieldViewModel(string name, string text, bool json, bool readOnly)
    {
        Name = name;
        Original = _text = text;
        IsJson = json;
        IsReadOnly = readOnly;
    }
}

/// <summary>A field form over a snapshot of the file. Only changed values are written back.</summary>
public sealed partial class LanguageServerEditorViewModel : ObservableObject
{
    private readonly LanguageServerEditorStore _store;
    private readonly ILocalizationService _localization;
    private readonly ILspWorkspace? _workspace;
    private readonly string _original;
    private readonly string? _id;
    private readonly bool _customise;
    private readonly Action _close;
    private string? _updated;
    private LanguageServerEntry? _entry;
    private string _problems = "";
    private string _preview = "";
    private bool _canSave;

    public ObservableCollection<LanguageServerFieldViewModel> Fields { get; } = [];
    public string Problems { get => _problems; private set => SetProperty(ref _problems, value); }
    public string Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public bool CanSave { get => _canSave; private set { SetProperty(ref _canSave, value); SaveCommand.NotifyCanExecuteChanged(); } }

    private static readonly HashSet<string> JsonFields = new(StringComparer.Ordinal)
    {
        "extensions", "arguments", "environment", "initializationOptions", "priority", "enabled",
        "capture.envelopeEntries", "capture.prologueEntries", "capture.payloadBytesPerConnection", "capture.frameBytes",
        "workspaceArtifact.settings", "workspaceArtifact.restartOnChange", "projectImport.arguments",
    };

    public LanguageServerEditorViewModel(LanguageServerEditorStore store, ILocalizationService localization,
        ILspWorkspace? workspace, string? id, bool customise, Action close)
    {
        _store = store;
        _localization = localization;
        _workspace = workspace;
        _original = store.Read();
        _id = id;
        _customise = customise;
        _close = close;
        var source = id is null ? "{}" : customise
            ? JsonSerializer.Serialize(store.Defaults.Single(e => e.Id == id), LanguageServerConfigJsonContext.Default.LanguageServerEntry)
            : store.UserEntry(_original, id) ?? "{}";
        using var document = LanguageServerEditorStore.Parse(source);
        foreach (var name in new[]
        {
            "id", "extensions", "languageId", "transport", "command", "arguments", "environment",
            "workingDirectory", "pipeName", "pipeRole", "endpoint", "tcpRole", "websocketRole", "displayName", "initializationOptions", "trace",
            "priority", "enabled", "capture.envelopeEntries", "capture.prologueEntries", "capture.payloadBytesPerConnection",
            "capture.frameBytes", "workspaceArtifact.path", "workspaceArtifact.provider", "workspaceArtifact.settings",
            "workspaceArtifact.restartOnChange", "projectImport.command", "projectImport.arguments",
        })
        {
            var value = document.RootElement;
            foreach (var part in name.Split('.'))
                value = value.ValueKind == JsonValueKind.Object && value.TryGetProperty(part, out var nested) ? nested : default;
            var json = JsonFields.Contains(name);
            var text = value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? ""
                : json || value.ValueKind != JsonValueKind.String ? value.GetRawText() : value.GetString() ?? "";
            var field = new LanguageServerFieldViewModel(name, text, json, name == "id" && id is not null);
            field.PropertyChanged += (_, _) => Validate();
            Fields.Add(field);
        }
        Validate();
    }

    internal void LanguageChanged() => Validate();

    private void Validate()
    {
        CanSave = false;
        _updated = null;
        _entry = null;
        Preview = "";
        try
        {
            var changes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in Fields)
            {
                if (field.Text == field.Original) continue;
                var literal = string.IsNullOrWhiteSpace(field.Text) ? "null"
                    : field.IsJson ? field.Text : JsonSerializer.Serialize(field.Text, LanguageServerEditorStore.AsTyped);
                try
                {
                    using var parsed = LanguageServerEditorStore.Parse(literal);
                }
                catch (JsonException ex)
                {
                    Problems = OriginalFileProblem() ?? string.Format(
                        _localization.GetString("Str.Tool.LanguageServers.Editor.InvalidJson"), field.Name, ex.Message);
                    return;
                }
                changes[field.Name] = literal;
            }
            var id = Fields.Single(f => f.Name == "id").Text;
            if (_id is null && (_store.UserIds(_original).Contains(id) || _store.Defaults.Any(e => e.Id == id)))
            {
                Problems = _localization.GetString("Str.Tool.LanguageServers.Editor.DuplicateId");
                return;
            }
            if (_customise) changes["id"] = JsonSerializer.Serialize(_id, LanguageServerEditorStore.AsTyped);
            _updated = _store.Change(_original, _customise ? null : _id, changes);
            var result = _store.Preview(_updated);
            Problems = FormatProblems(result.Problems.Where(p => p.EntryId is null || p.EntryId == id));
            _entry = result.Entries.LastOrDefault(e => e.Id == id);
            if (_entry is null || result.Problems.Any(p => p.EntryRejected && (p.EntryId is null || p.EntryId == id))) return;
            Preview = CommandPreview(_entry);
            CanSave = changes.Count > (_customise ? 1 : 0);
        }
        catch (JsonException ex)
        {
            Problems = OriginalFileProblem() ?? ex.Message;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Problems = OriginalFileProblem() ?? ex.Message;
        }
    }

    private string? OriginalFileProblem()
    {
        var problems = _store.Preview(_original).Problems.Where(p => p.EntryId is null && p.EntryRejected).ToArray();
        return problems.Length == 0 ? null : FormatProblems(problems);
    }

    private string FormatProblems(IEnumerable<LanguageServerConfigProblem> problems) =>
        string.Join(Environment.NewLine, problems.Select(p => p.Code is null ? p.Message : string.Format(
            CultureInfo.InvariantCulture, _localization.GetString($"Str.Tool.LanguageServers.Problem.{p.Code}"), p.Arguments)));

    private string CommandPreview(LanguageServerEntry entry)
    {
        var args = entry.Arguments;
        var unresolved = false;
        string Resolve(string value)
        {
            if (!string.Equals(entry.Transport?.Trim(), "pipe", StringComparison.OrdinalIgnoreCase)) return value;
            value = value.Replace("{pipe}", entry.PipeName, StringComparison.Ordinal)
                .Replace("{pid}", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var directory = _workspace?.Directory;
            if (value.Contains("{workspaceDir}", StringComparison.Ordinal) || value.Contains("{workspaceUri}", StringComparison.Ordinal))
            {
                var uri = LspWorkspaceUri.For(directory);
                if (string.IsNullOrWhiteSpace(directory) || uri is null) unresolved = true;
                else value = value.Replace("{workspaceDir}", directory, StringComparison.Ordinal)
                    .Replace("{workspaceUri}", uri, StringComparison.Ordinal);
            }
            return value;
        }
        var resolved = args?.List is { } list ? new ServerArguments(null, list.Select(Resolve).ToArray())
            : args is null ? null : new ServerArguments(Resolve(args.Text ?? ""), null);
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(entry, LanguageServerConfigJsonContext.Default.LanguageServerEntry),
            LanguageServerConfigJsonContext.Default.LanguageServerEntry)!;
        copy.Arguments = resolved;
        var command = LanguageServerConfigLoader.LaunchedCommandOf(copy) ?? entry.Endpoint ?? entry.PipeName ?? "";
        return command + (unresolved ? Environment.NewLine + _localization.GetString("Str.Tool.LanguageServers.Editor.NoWorkspace") : "");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CanSave || _updated is null || _entry is null) return;
        try
        {
            if (!_store.Save(_original, _updated, _entry))
            {
                Problems = _localization.GetString("Str.Tool.LanguageServers.Editor.FileChanged");
                CanSave = false;
                return;
            }
            _close();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Problems = _localization.GetString("Str.Tool.LanguageServers.Editor.Save") + ": " + ex.Message;
        }
    }

    [RelayCommand] private void Cancel() => _close();
}
