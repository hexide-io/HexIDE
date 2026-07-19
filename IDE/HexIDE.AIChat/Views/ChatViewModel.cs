using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using HexIDE.Addins;

namespace HexIDE.AIChat.Views;

public sealed class ChatViewModel : INotifyPropertyChanged
{
    private readonly IHexIdeHost _host;
    private readonly ChatSession _session;
    private readonly ChatSettings _settings;
    private readonly OpenAiClient _client;
    private readonly IdeToolExecutor _tools;
    private readonly string _settingsDir;
    private readonly string _historyPath;

    private string _inputText = string.Empty;
    private bool _isResponding;
    private bool _egressNoticeShown;
    private ChatMessageViewModel? _activeStream;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public string InputText
    {
        get => _inputText;
        set { _inputText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputText))); }
    }

    public bool IsResponding
    {
        get => _isResponding;
        private set { _isResponding = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsResponding))); }
    }

    /// <summary>
    /// Append a scripted assistant message to the transcript with NO LLM/API call — for automation and
    /// for narration that "teleports" into the panel (e.g. the demo-hexide-mcp skill). Write-only-ish: the
    /// getter is a no-op so the property is reachable via the MCP reflection <c>set_property</c> action
    /// (which can pass a string but cannot call a method). Empty input is ignored.
    /// </summary>
    public string Say
    {
        get => string.Empty;
        set
        {
            if (!string.IsNullOrEmpty(value))
                Messages.Add(new ChatMessageViewModel("assistant", value));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ChatViewModel(IHexIdeHost host, string settingsDir)
    {
        _host        = host;
        _settingsDir = settingsDir;
        _settings    = ChatSettings.Load(settingsDir);
        _historyPath = Path.Combine(settingsDir, "chat-history.json");
        _session     = ChatSession.LoadFrom(_historyPath);
        _client      = new OpenAiClient();
        _tools       = new IdeToolExecutor(host);

        foreach (var msg in _session.Log.Where(m => m.Role is "user" or "assistant"))
            Messages.Add(new ChatMessageViewModel(msg.Role, msg.Content));
    }

    public async Task SendAsync()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text) || IsResponding) return;

        InputText   = string.Empty;
        IsResponding = true;

        MaybeShowEgressNotice();

        var userMsg = new ConversationMessage("user", text);
        _session.Add(userMsg);
        Messages.Add(new ChatMessageViewModel("user", text));

        try
        {
            await RunAgenticLoopAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            _session.SaveTo(_historyPath);
            IsResponding = false;
        }
    }

    /// <summary>
    /// Finalises any in-flight streaming bubble and shows a visible error line so a
    /// failed request (bad API key, rate limit, network, invalid payload) never leaves
    /// the panel silently frozen.
    /// </summary>
    private void ShowError(Exception ex)
    {
        if (_activeStream is { } stream)
        {
            stream.IsStreaming = false;
            if (string.IsNullOrEmpty(stream.RawContent))
                Messages.Remove(stream);
            _activeStream = null;
        }

        Messages.Add(new ChatMessageViewModel("assistant", $"Error: {ex.Message}", isError: true));
    }

    public void NewChat()
    {
        _session.SaveTo(_historyPath);
        Messages.Clear();
    }

    public async Task ApplyToActiveFileAsync(string code)
    {
        var doc = _host.Editor.GetActiveDocument();
        if (doc is not null)
            await _host.Editor.SetContent(doc.FileName, code);
    }

    // ── agentic loop ─────────────────────────────────────────────────────────

    private async Task RunAgenticLoopAsync()
    {
        while (true)
        {
            await MaybeCompactAsync();

            var systemPrompt = BuildSystemPrompt();
            var apiMessages  = _session.BuildApiMessages(systemPrompt);

            var aiMsgVm = new ChatMessageViewModel("assistant") { IsStreaming = true };
            _activeStream = aiMsgVm;
            Messages.Add(aiMsgVm);

            var contentBuilder = new StringBuilder();
            List<ToolCall>? toolCalls = null;

            await foreach (var chunk in _client.StreamAsync(
                _settings.BaseUrl, _settings.ResolveApiKey(), _settings.ModelId,
                apiMessages, IdeToolExecutor.ToolDefinitions))
            {
                if (chunk.IsDone) break;

                if (chunk.ContentDelta is not null)
                {
                    contentBuilder.Append(chunk.ContentDelta);
                    Dispatcher.UIThread.Post(() => aiMsgVm.AppendContent(chunk.ContentDelta));
                }

                if (chunk.FinishedToolCalls is not null)
                    toolCalls = [..chunk.FinishedToolCalls];
            }

            var finalContent = contentBuilder.ToString();
            var assistantMsg = new ConversationMessage("assistant", finalContent, toolCalls);
            _session.Add(assistantMsg);
            Dispatcher.UIThread.Post(() => aiMsgVm.IsStreaming = false);
            _activeStream = null;

            if (toolCalls is not { Count: > 0 }) break;

            foreach (var tc in toolCalls)
            {
                // Write tools (edit / run / stop) pause for an in-panel approve/reject; read tools run
                // freely. A rejection is fed back to the model as a tool result so it can adjust.
                if (IdeToolExecutor.WriteTools.Contains(tc.Name))
                {
                    var approval = new ChatMessageViewModel("assistant", DescribeWriteTool(tc), isApproval: true);
                    Dispatcher.UIThread.Post(() => Messages.Add(approval));

                    if (!await approval.Decision)
                    {
                        var declined = JsonSerializer.Serialize(new { success = false, error = "The user declined this action." });
                        _session.Add(new ConversationMessage("tool", declined, toolCallId: tc.Id, toolName: tc.Name));
                        continue;
                    }
                }

                var statusVm = new ChatMessageViewModel("assistant",
                    $"Using tool: `{tc.Name}`", isStatusLine: true);
                Dispatcher.UIThread.Post(() => Messages.Add(statusVm));

                var result = await _tools.ExecuteAsync(tc);
                _session.Add(new ConversationMessage("tool", result,
                    toolCallId: tc.Id, toolName: tc.Name));
            }
        }
    }

    /// <summary>A human-readable description of a pending write-tool call for the approval prompt.</summary>
    private static string DescribeWriteTool(ToolCall tc)
    {
        string? file = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(tc.ArgumentsJson) && tc.ArgumentsJson != "{}"
                && JsonNode.Parse(tc.ArgumentsJson) is JsonObject o)
                file = o["file_name"]?.GetValue<string>();
        }
        catch { /* fall through to a generic description */ }

        return tc.Name switch
        {
            "apply_edit"   => $"Allow the AI to replace the entire contents of {(string.IsNullOrEmpty(file) ? "a file" : $"“{file}”")}?",
            "run_project"  => "Allow the AI to run the project in the IDE runtime?",
            "stop_project" => "Allow the AI to stop the running project?",
            _              => $"Allow the AI to run “{tc.Name}”?",
        };
    }

    /// <summary>Shows a one-time privacy notice (persisted per user) disclosing that chat content — the
    /// active file's code and diagnostics included — is sent to the configured third-party endpoint.</summary>
    private void MaybeShowEgressNotice()
    {
        if (_egressNoticeShown) return;
        _egressNoticeShown = true;

        var ackPath = Path.Combine(_settingsDir, ".ai-egress-ack");
        if (File.Exists(ackPath)) return;

        var host = Uri.TryCreate(_settings.BaseUrl, UriKind.Absolute, out var u) ? u.Host : _settings.BaseUrl;
        var tip = _settings.ApiKeyIsFromPlaintextFile()
            ? " Tip: set the HEXIDE_AI_API_KEY environment variable to avoid storing your API key in a file."
            : "";

        Messages.Add(new ChatMessageViewModel("assistant",
            $"ℹ Privacy: your messages, the active file's code, and its diagnostics are sent to {host} — " +
            "a third-party service you configured in AI Chat settings." + tip,
            isStatusLine: true));

        try { File.WriteAllText(ackPath, "shown"); } catch { /* best-effort */ }
    }

    // ── compaction ───────────────────────────────────────────────────────────

    private async Task MaybeCompactAsync()
    {
        if (!_session.NeedsCompaction(_settings.MaxTurnsBeforeCompaction)) return;

        var toSummarise = _session.DrainForCompaction(_settings.MaxTurnsBeforeCompaction / 2);
        if (toSummarise.Count == 0) return;

        // Flatten the drained messages into a plain-text transcript rather than
        // replaying them as structured tool_calls/tool messages: the summariser
        // request declares no tools, so a structured assistant.tool_calls payload
        // (or a tool message split from its parent) would be rejected by the API.
        var transcript = new StringBuilder();
        foreach (var m in toSummarise)
        {
            if (m.Role == "tool")
                transcript.AppendLine($"[tool result for {m.ToolName}]: {m.Content}");
            else if (m.ToolCalls is { Count: > 0 })
                transcript.AppendLine(
                    $"{m.Role}: {m.Content} [called tools: {string.Join(", ", m.ToolCalls.Select(t => t.Name))}]");
            else
                transcript.AppendLine($"{m.Role}: {m.Content}");
        }

        var summaryPrompt = new List<JsonObject>
        {
            new()
            {
                ["role"] = "system",
                ["content"] = "You are a helpful summariser. Summarise the following conversation history in 2-3 short paragraphs, focusing on what code was discussed, what changes were made, and any important context to carry forward. Be concise."
            },
            new()
            {
                ["role"] = "user",
                ["content"] = transcript.ToString()
            }
        };

        var summary = new StringBuilder();
        await foreach (var chunk in _client.StreamAsync(
            _settings.BaseUrl, _settings.ResolveApiKey(), _settings.ModelId,
            summaryPrompt, new System.Text.Json.Nodes.JsonArray()))
        {
            if (chunk.ContentDelta is not null) summary.Append(chunk.ContentDelta);
            if (chunk.IsDone) break;
        }

        _session.SetCompactionSummary(summary.ToString());
    }

    // ── context assembly ─────────────────────────────────────────────────────

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert Visual Basic 6 and VBA assistant integrated into the HexIDE development environment.");
        sb.AppendLine("You have access to IDE tools that let you read and modify files, inspect diagnostics, and navigate the project.");
        sb.AppendLine("When asked to fix code, always read the relevant file first, then apply the fix using apply_edit.");
        sb.AppendLine("Be concise and practical.");
        sb.AppendLine();

        var doc = _host.Editor.GetActiveDocument();
        if (doc is not null)
        {
            sb.AppendLine($"Active file: {doc.FileName} ({doc.Kind})");
            sb.AppendLine("Content:");
            sb.AppendLine("```vb");
            sb.AppendLine(doc.Content);
            sb.AppendLine("```");

            var diags = _host.Diagnostics.GetFor(doc.FileName);
            if (diags.Count > 0)
            {
                sb.AppendLine($"Diagnostics for {doc.FileName}:");
                foreach (var d in diags)
                    sb.AppendLine($"  [{d.Severity}] Line {d.Line}, Col {d.Column}: {d.Message}");
            }
        }
        else
        {
            sb.AppendLine("No active editor.");
        }

        return sb.ToString();
    }
}
