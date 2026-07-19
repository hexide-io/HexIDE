using System.Collections.ObjectModel;
using System.ComponentModel;

namespace HexIDE.AIChat.Views;

public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private string _rawContent = string.Empty;
    private bool _isStreaming;
    private readonly TaskCompletionSource<bool>? _approvalTcs;
    private bool _isDecided;

    public bool IsUser { get; }
    public bool IsAssistant => !IsUser && !IsStatusLine && !IsApproval;
    public bool IsStatusLine { get; }
    public bool IsError { get; }

    /// <summary>True for a tool-approval prompt (a write tool awaiting the user's approve/reject).</summary>
    public bool IsApproval { get; }

    /// <summary>True for a normal user/assistant message bubble (not a status, error, or approval line).</summary>
    public bool IsBubble => !IsStatusLine && !IsError && !IsApproval;

    public string RawContent
    {
        get => _rawContent;
        set
        {
            if (_rawContent == value) return;
            _rawContent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RawContent)));
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (_isStreaming == value) return;
            _isStreaming = value;
            if (!value) FinaliseContent();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStreaming)));
        }
    }

    public ObservableCollection<MessagePart> Parts { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public ChatMessageViewModel(string role, string content = "", bool isStatusLine = false, bool isError = false, bool isApproval = false)
    {
        IsUser       = role == "user";
        IsStatusLine = isStatusLine;
        IsError      = isError;
        IsApproval   = isApproval;
        _rawContent  = content;
        if (isApproval)
            _approvalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsBubble && !string.IsNullOrEmpty(content))
            FinaliseContent();
    }

    // ── tool approval ──────────────────────────────────────────────────────────

    /// <summary>Pending only while an approval prompt is still awaiting the user's click.</summary>
    public bool IsAwaitingDecision => IsApproval && !_isDecided;

    /// <summary>Shown once the user has decided ("✔ Approved" / "✗ Rejected").</summary>
    public string ApprovalOutcome { get; private set; } = string.Empty;

    /// <summary>Completes when the user approves (true) or rejects (false). Non-approval messages
    /// resolve to true immediately, so callers can await unconditionally.</summary>
    public Task<bool> Decision => _approvalTcs?.Task ?? Task.FromResult(true);

    public void Approve() => Decide(true);
    public void Reject()  => Decide(false);

    private void Decide(bool approved)
    {
        if (_isDecided || _approvalTcs is null) return;
        _isDecided = true;
        ApprovalOutcome = approved ? "✔ Approved" : "✗ Rejected";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAwaitingDecision)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApprovalOutcome)));
        _approvalTcs.TrySetResult(approved);
    }

    public void AppendContent(string chunk)
    {
        _rawContent += chunk;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RawContent)));
    }

    private void FinaliseContent()
    {
        Parts.Clear();
        foreach (var part in MessagePart.Parse(_rawContent))
            Parts.Add(part);
    }
}
