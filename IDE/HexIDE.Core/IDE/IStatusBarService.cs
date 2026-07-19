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

    /// <summary>Current cursor line (1-based), or null when no code editor is active.</summary>
    int? Line { get; set; }

    /// <summary>Current cursor column (1-based), or null when no code editor is active.</summary>
    int? Column { get; set; }

    /// <summary>True when the editor is in insert mode; false for overwrite mode.</summary>
    bool InsertMode { get; set; }

    /// <summary>Sets the status message. Use for persistent state like "Ready" or "Running…".</summary>
    void SetMessage(string message);

    /// <summary>
    /// Shows a temporary message that auto-reverts to "Ready" after <paramref name="duration"/>.
    /// Useful for transient feedback like "Replace All: 5 replacements made".
    /// </summary>
    void SetTemporaryMessage(string message, TimeSpan duration);

    /// <summary>Clears the message back to "Ready".</summary>
    void ClearMessage();
}
