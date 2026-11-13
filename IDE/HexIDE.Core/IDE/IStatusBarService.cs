using System.ComponentModel;

namespace HexIDE.IDE;

/// <summary>
/// Provides status bar state for the main IDE window.
/// Any component can post messages; the active code editor pushes cursor position.
/// Raises <see cref="PropertyChanged"/> so the status bar UI reacts to changes.
/// </summary>
public interface IStatusBarService : INotifyPropertyChanged
{
    /// <summary>Current status message (default "Ready").</summary>
    string Message { get; }

    /// <summary>Counter incremented on every displayed message assignment, including repeated text; deferred updates do not advance it.</summary>
    long MessageVersion { get; }

    /// <summary>Current cursor line (1-based), or null when no code editor is active.</summary>
    int? Line { get; set; }

    /// <summary>Current cursor column (1-based), or null when no code editor is active.</summary>
    int? Column { get; set; }

    /// <summary>True when the editor is in insert mode; false for overwrite mode.</summary>
    bool InsertMode { get; set; }

    /// <summary>Records persistent text, deferring its display until any temporary message expires.</summary>
    void SetMessage(string message);

    /// <summary>Immediately replaces the displayed message, cancelling temporary feedback. For run/debug transitions.</summary>
    void SetImmediateMessage(string message);

    /// <summary>Replaces matching persistent text, even when hidden; null restores localized Ready. Leaves temporary feedback up.</summary>
    bool TryReplaceMessage(string expected, string? replacement);

    /// <summary>
    /// Shows a temporary message, restoring the latest persistent text after <paramref name="duration"/>.
    /// Useful for transient feedback like "Replace All: 5 replacements made".
    /// </summary>
    void SetTemporaryMessage(string message, TimeSpan duration);

    /// <summary>Clears the message back to "Ready".</summary>
    void ClearMessage();
}
