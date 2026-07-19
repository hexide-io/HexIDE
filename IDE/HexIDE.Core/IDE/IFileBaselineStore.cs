using System.Collections.Generic;

namespace HexIDE.IDE;

/// <summary>
/// Tracks, per file, the IDE's belief about its on-disk content (a <see cref="FileBaseline"/>).
/// Updated on every IDE read (load) and write (save). The file watcher consults it to distinguish
/// real external changes from the IDE's own atomic writes and from mtime-only touches, and to detect
/// dirtiness of files not open in any editor.
/// <para>
/// Paths are normalized (full path, case-insensitive) so the same file keyed via different spellings
/// resolves to one entry. Implementations must be thread-safe — the watcher calls in from background
/// threads while the IDE records baselines on the UI / save threads.
/// </para>
/// </summary>
public interface IFileBaselineStore
{
    /// <summary>The recorded baseline for <paramref name="path"/>, or <c>null</c> if none.</summary>
    FileBaseline? TryGet(string path);

    /// <summary>Records a baseline from the exact bytes written/read for <paramref name="path"/>.</summary>
    void Record(string path, byte[] content);

    /// <summary>
    /// Records a baseline from text content (hashed as UTF-8 without BOM — see
    /// <see cref="FileHasher.Hash(string)"/>).
    /// </summary>
    void Record(string path, string content);

    /// <summary>Records a pre-built baseline (used when restoring from a persisted snapshot).</summary>
    void Record(string path, FileBaseline baseline);

    /// <summary>
    /// True when <paramref name="diskContent"/> matches the recorded baseline for
    /// <paramref name="path"/> (same length and hash) — i.e. the file on disk is what the IDE expects.
    /// Returns false when there is no baseline.
    /// </summary>
    bool Matches(string path, byte[] diskContent);

    /// <summary>Drops the baseline for <paramref name="path"/> (e.g. on external delete/rename).</summary>
    void Remove(string path);

    /// <summary>An immutable copy of all baselines — for the future crash-recovery sidecar phase.</summary>
    IReadOnlyDictionary<string, FileBaseline> Snapshot();
}
