using System;
using System.Security.Cryptography;
using System.Text;

namespace HexIDE.IDE;

/// <summary>
/// A snapshot of what the IDE believes is currently on disk for a single project file — the
/// "baseline" used by the file watcher to answer two questions:
/// <list type="bullet">
///   <item>Did the file <i>actually</i> change? (vs. an mtime touch or the IDE's own save.)</item>
///   <item>Is the in-memory copy dirty relative to disk? (for files not open in any editor.)</item>
/// </list>
/// Recorded whenever the IDE reads (load) or writes (save) the file. Deliberately a plain,
/// dependency-free record so a future crash-recovery/autosave phase can persist a
/// <see cref="System.Collections.Generic.IReadOnlyDictionary{TKey,TValue}"/> of these to the user
/// sidecar and restore from it. See <c>openspec/specs/file-watcher/spec.md</c>.
/// </summary>
/// <param name="Hash">Hex-encoded SHA-256 of the raw file bytes (binary-safe — covers <c>.frx</c>).</param>
/// <param name="Length">Byte length — a cheap pre-filter before comparing hashes.</param>
/// <param name="LastWriteUtc">Last-write timestamp seen when the baseline was recorded.</param>
/// <param name="Epoch">Monotonic counter bumped on each update; lets a queued conflict batch detect
/// that a newer baseline raced ahead of it.</param>
public sealed record FileBaseline(string Hash, long Length, DateTime LastWriteUtc, long Epoch);

/// <summary>
/// Pure content hashing for <see cref="IFileBaselineStore"/>. SHA-256, hex-encoded, no I/O — safe to
/// live in <c>HexIDE.Core</c> and reuse anywhere a stable content fingerprint is needed.
/// </summary>
public static class FileHasher
{
    /// <summary>Hex-encoded SHA-256 of the given bytes.</summary>
    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    /// <summary>
    /// Hex-encoded SHA-256 of the UTF-8 (no BOM) encoding of <paramref name="content"/>. Matches the
    /// bytes <see cref="System.IO.File.WriteAllText(string, string?)"/> produces, so a baseline
    /// recorded from a string the IDE just wrote matches a later byte-level read of that file.
    /// </summary>
    public static string Hash(string content) => Hash(Encoding.UTF8.GetBytes(content));
}
