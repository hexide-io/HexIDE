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
    /// <summary>
    /// Which privacy notice the acknowledgement file records. The first wrote "shown"; change this whenever what
    /// the notice discloses grows, so that everyone is told once more.
    /// </summary>
    internal const string EgressNoticeVersion = "whole-file";

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

    /// <summary>Notes for the assistant written while it was responding, held until a user message may come next.</summary>
    private readonly List<ConversationMessage> _heldNotes = [];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    /// <summary>What the assistant is sent: the conversation as the session holds it.</summary>
    internal IReadOnlyList<ConversationMessage> SessionLog => _session.Log;

    public string InputText
    {
        get => _inputText;
        set { _inputText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputText))); }
    }

    public bool IsResponding
    {
        get => _isResponding;
        internal set { _isResponding = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsResponding))); }
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
            Messages.Add(msg.FromIde
                ? new ChatMessageViewModel("assistant", msg.Content, isStatusLine: true)
                : new ChatMessageViewModel(msg.Role, msg.Content));
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
            FlushHeldNotes();
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

    /// <summary>
    /// The Apply button on a code block: replaces the active document's content with the block, says in the
    /// transcript what came of it, and tells the assistant.
    /// </summary>
    /// <remarks>
    /// The block may be the code alone or the whole file, and the IDE keeps the document's header either way. It
    /// refuses a block whose header differs, which is the common case for a whole form a model has re-typed,
    /// since the header is compared line for line. That refusal used to vanish, and so did a block applied with
    /// no code window active; the button looked as though it had done something. The click handler does not
    /// await this, so nothing may escape it either.
    /// <para>
    /// The assistant is told the outcome too, accepted or refused, as a note in the conversation. Until the 3.19
    /// review only the transcript said, so after a refusal the assistant's next turn went on as though its code
    /// were in the file (the ai-chat delta's requirement has both halves). No note is written when no code window
    /// was active, since no change was put to the IDE.
    /// </para>
    /// </remarks>
    public async Task ApplyToActiveFileAsync(string code)
    {
        AddinDocument? doc = null;
        try
        {
            doc = _host.Editor.GetActiveDocument();
            if (doc is null)
            {
                Messages.Add(new ChatMessageViewModel("assistant",
                    "Not applied: no code window is active. Open the file to apply the code to, then click Apply again.",
                    isError: true));
                return;
            }

            var result = await _host.Editor.TrySetContent(doc.FileName, code, doc.Project);
            Messages.Add(result.Succeeded
                ? new ChatMessageViewModel("assistant", $"Applied to {doc.FileName}.", isStatusLine: true)
                : new ChatMessageViewModel("assistant", $"Not applied to {doc.FileName}: {result.Reason}", isError: true));
            TellAssistant(result.Succeeded
                ? $"The developer applied a code block from this chat to {Describe(doc)}, and the IDE accepted it"
                  + (result.HeaderKept ? ", keeping the file's own header in front of it." : ".")
                : $"The developer applied a code block from this chat to {Describe(doc)}, and the IDE refused it, so "
                  + $"nothing was written: {result.Reason}");
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessageViewModel("assistant", $"Not applied: {ex.Message}", isError: true));
            if (doc is not null)
                TellAssistant($"The developer applied a code block from this chat to {Describe(doc)}, and it failed, "
                              + $"so nothing was written: {ex.Message}");
        }

        static string Describe(AddinDocument doc) =>
            doc.Project is { } project ? $"{doc.FileName} (project {project})" : doc.FileName;
    }

    /// <summary>
    /// Puts a note from the IDE into the conversation, for the assistant to read on its next request.
    /// </summary>
    /// <remarks>
    /// A note can only go where a user message may: after a <c>tool_calls</c> message the endpoint expects that
    /// call's results and nothing else, and refuses the whole request otherwise, which would leave the saved
    /// history unusable. The Apply button stays live while the assistant is responding, including while it waits
    /// for approval between a call and its result, so a note written then is held, and the loop puts it in at its
    /// next safe point.
    /// </remarks>
    private void TellAssistant(string note)
    {
        lock (_heldNotes)
            _heldNotes.Add(new ConversationMessage("user", "[Note from the IDE] " + note, fromIde: true));
        if (IsResponding) return;

        FlushHeldNotes();
        _session.SaveTo(_historyPath);
    }

    /// <summary>Moves held notes into the conversation. Call only where a user message may come next.</summary>
    internal void FlushHeldNotes()
    {
        lock (_heldNotes)
        {
            foreach (var note in _heldNotes) _session.Add(note);
            _heldNotes.Clear();
        }
    }

    // ── agentic loop ─────────────────────────────────────────────────────────

    private async Task RunAgenticLoopAsync()
    {
        while (true)
        {
            // Here the last message is the developer's or a tool result, after which a note may come.
            FlushHeldNotes();
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

                await RunToolCallAsync(tc);
            }
        }
    }

    /// <summary>
    /// Runs one tool call the assistant asked for, once approved where it needs to be: the assistant is given the
    /// result, and the transcript says what a write came to.
    /// </summary>
    /// <remarks>
    /// The transcript showed only "Using tool" until the 3.19 review. A write the IDE refused, an
    /// <c>apply_edit</c> whose header differs above all, reached the assistant and not the developer, who had
    /// just approved it and was left believing it had landed (the ai-chat delta's requirement has both halves).
    /// </remarks>
    internal async Task RunToolCallAsync(ToolCall tc)
    {
        var statusVm = new ChatMessageViewModel("assistant",
            $"Using tool: `{tc.Name}`", isStatusLine: true);
        Dispatcher.UIThread.Post(() => Messages.Add(statusVm));

        var result = await _tools.ExecuteAsync(tc);
        _session.Add(new ConversationMessage("tool", result,
            toolCallId: tc.Id, toolName: tc.Name));

        if (DescribeWriteOutcome(tc, result) is { } outcome)
            Dispatcher.UIThread.Post(() => Messages.Add(outcome));
    }

    /// <summary>
    /// The transcript line for what a write tool came to: an <c>apply_edit</c> applied or refused, or another
    /// write refused. Null for a read tool, and for a write that did what it was asked and has nothing to add.
    /// </summary>
    private static ChatMessageViewModel? DescribeWriteOutcome(ToolCall tc, string result)
    {
        if (!IdeToolExecutor.WriteTools.Contains(tc.Name)) return null;

        bool succeeded;
        string? reason;
        try
        {
            var reply = JsonNode.Parse(result) as JsonObject;
            succeeded = reply?["success"]?.GetValue<bool>() == true;
            reason = reply?["error"]?.GetValue<string>();
        }
        catch
        {
            succeeded = false;
            reason = result;
        }

        var file = FileNameOf(tc) ?? "the file";
        if (tc.Name == "apply_edit")
            return succeeded
                ? new ChatMessageViewModel("assistant", $"Applied to {file}.", isStatusLine: true)
                : new ChatMessageViewModel("assistant", $"Not applied to {file}: {reason ?? "no reason was given."}", isError: true);
        return succeeded
            ? null
            : new ChatMessageViewModel("assistant", $"`{tc.Name}` failed: {reason ?? "no reason was given."}", isError: true);
    }

    /// <summary>The <c>file_name</c> a tool call names, or null.</summary>
    private static string? FileNameOf(ToolCall tc)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(tc.ArgumentsJson) && tc.ArgumentsJson != "{}"
                && JsonNode.Parse(tc.ArgumentsJson) is JsonObject o
                && o["file_name"]?.GetValue<string>() is { Length: > 0 } file)
                return file;
        }
        catch { /* no name to give */ }
        return null;
    }

    /// <summary>A human-readable description of a pending write-tool call for the approval prompt.</summary>
    private static string DescribeWriteTool(ToolCall tc)
    {
        var file = FileNameOf(tc);

        return tc.Name switch
        {
            "apply_edit"   => $"Allow the AI to replace the entire contents of {(string.IsNullOrEmpty(file) ? "a file" : $"“{file}”")}?",
            "run_project"  => "Allow the AI to run the project in the IDE runtime?",
            "stop_project" => "Allow the AI to stop the running project?",
            _              => $"Allow the AI to run “{tc.Name}”?",
        };
    }

    /// <summary>Shows a one-time privacy notice (persisted per user) disclosing that chat content — the
    /// active file's contents and diagnostics included — is sent to the configured third-party endpoint.</summary>
    /// <remarks>
    /// It said "the active file's code" until the content became the whole file (hexide-io/HexIDE#273 task
    /// 3.19). A form's designer block goes too, and with it its captions, menu text and the names of the binary
    /// files beside it, which "code" would not lead anyone to expect. So the acknowledgement records which notice
    /// was shown, and a developer who saw only the old one is shown the new one once: more now leaves the machine
    /// than they were told.
    /// </remarks>
    internal void MaybeShowEgressNotice()
    {
        if (_egressNoticeShown) return;
        _egressNoticeShown = true;

        var ackPath = Path.Combine(_settingsDir, ".ai-egress-ack");
        try
        {
            if (File.Exists(ackPath) && File.ReadAllText(ackPath).Trim() == EgressNoticeVersion) return;
        }
        catch { /* unreadable: show the notice, which errs on the side of telling */ }

        var host = Uri.TryCreate(_settings.BaseUrl, UriKind.Absolute, out var u) ? u.Host : _settings.BaseUrl;
        var tip = _settings.ApiKeyIsFromPlaintextFile()
            ? " Tip: set the HEXIDE_AI_API_KEY environment variable to avoid storing your API key in a file."
            : "";

        Messages.Add(new ChatMessageViewModel("assistant",
            $"ℹ Privacy: your messages, the active file's contents (for a form, its layout and captions too), and its diagnostics are sent to {host} — " +
            "a third-party service you configured in AI Chat settings." + tip,
            isStatusLine: true));

        try { File.WriteAllText(ackPath, EgressNoticeVersion); } catch { /* best-effort */ }
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

    /// <summary>
    /// The system prompt: who the assistant is, the rules the IDE holds it to, and the active file.
    /// </summary>
    /// <remarks>
    /// The file is its whole content as the code window shows it, so the line numbers in its diagnostics, which
    /// count from the top of the file, are lines of the text the model is shown. Until hexide-io/HexIDE#273 task
    /// 3.19 the content was the code alone while the diagnostics already counted the header, so every line the
    /// model was given was out by the header's length.
    /// </remarks>
    internal string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert Visual Basic 6 and VBA assistant integrated into the HexIDE development environment.");
        sb.AppendLine("You have access to IDE tools that let you read and modify files, inspect diagnostics, and navigate the project.");
        sb.AppendLine("When asked to fix code, always read the relevant file first, then apply the fix using apply_edit.");
        sb.AppendLine("A file's content is the whole file as the code window shows it. Before the code comes its header: a form's or "
                      + "UserControl's VERSION line and designer block, which lists its controls and their properties, a class's "
                      + "header, and the Attribute lines. The header is read-only: only the IDE changes it, from the form "
                      + "designer and the Properties window. So apply_edit takes either the whole file with the header exactly "
                      + "as shown, or the code alone, and keeps the header either way; content whose header differs is refused. "
                      + "To change a control or a property, tell the developer to do it in the designer.");
        sb.AppendLine("Line numbers, in diagnostics and for navigate_to, count from 1 at the top of the file, header included.");
        sb.AppendLine("Be concise and practical.");
        sb.AppendLine();

        var doc = _host.Editor.GetActiveDocument();
        if (doc is not null)
        {
            sb.AppendLine(doc.Project is { } project
                ? $"Active file: {doc.FileName} ({doc.Kind}), in project {project}"
                : $"Active file: {doc.FileName} ({doc.Kind})");
            sb.AppendLine("Content:");
            sb.AppendLine("```vb");
            sb.AppendLine(doc.Content);
            sb.AppendLine("```");

            var diags = _host.Diagnostics.GetFor(doc.FileName, doc.Project);
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
