namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// A single settings page in the Tools&#8594;Options dialog (the right-hand content pane).
/// The shell (<see cref="OptionsViewModel"/>) aggregates pages: each page holds its own
/// copy of the settings it owns, so the dialog only commits on OK and never mutates
/// <see cref="HexIDE.IDE.ISettingsService"/> mid-edit.
/// </summary>
public interface IOptionsPage
{
    /// <summary>Label shown for this page's node in the category tree.</summary>
    string Title { get; }

    /// <summary>Pull the owned settings from the service into this page's editable copy.</summary>
    void LoadFromSettings();

    /// <summary>Write this page's editable copy back to the service. Called on OK only.</summary>
    void SaveToSettings();

    /// <summary>
    /// Reset this page's editable copy to the factory defaults (<see cref="HexIDE.IDE.SettingsDefaults"/>).
    /// Preview-only — like any other edit it is committed on OK and discarded on Cancel.
    /// </summary>
    void RestoreDefaults();
}
