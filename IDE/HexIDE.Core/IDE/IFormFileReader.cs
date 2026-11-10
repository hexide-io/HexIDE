using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.IDE;

/// <summary>
/// How a form's file becomes the IDE's model of it. Opening a project, reloading a changed file and restoring crash-recovery
/// data all come here, and recovery has only a saved text and companion, so what is read needn't be a file on disk.
/// </summary>
public interface IFormFileReader
{
    /// <summary>A form, from its file's text and companion, which are read together: the text says how the companion divides.</summary>
    /// <returns>The form, or null when the text can't be read as one, which goes to <paramref name="problems"/>.</returns>
    FormDefinition? ReadForm(ProjectDefinition project, string text, byte[]? companion, IDeserializeErrorSink problems);

    /// <summary>The companion file that goes with this form's file, or null when a form of this kind has none.</summary>
    string? CompanionPath(string path);
}
