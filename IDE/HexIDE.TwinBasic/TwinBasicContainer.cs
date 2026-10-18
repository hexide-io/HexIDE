using System.Collections.Immutable;

namespace HexIDE.TwinBasic;

/// <summary>A detached, immutable snapshot. Names are archive names, never host filesystem paths.</summary>
public sealed record TwinBasicContainer(short Version, TwinBasicDirectory Root);

public abstract record TwinBasicEntry(string Name, ulong Revision, uint Flags, byte Category);

public sealed record TwinBasicDirectory(
    string Name, ulong Revision, uint Flags, byte Category, ImmutableArray<TwinBasicEntry> Children)
    : TwinBasicEntry(Name, Revision, Flags, Category);

/// <summary>Content is opaque: only a consumer that knows the file type should decode it as text.</summary>
public sealed record TwinBasicFile(
    string Name, ulong Revision, uint Flags, byte Category,
    ImmutableArray<byte> Content, ImmutableArray<uint> Revisions)
    : TwinBasicEntry(Name, Revision, Flags, Category);

/// <summary>Policy limits, not restrictions claimed by the file format. Depth zero is the root.</summary>
public sealed record TwinBasicReadLimits(
    int MaxContainerBytes = 64 * 1024 * 1024,
    int MaxEntries = 100_000,
    int MaxDepth = 128,
    int MaxNameBytes = 16 * 1024);

public sealed class TwinBasicFormatException(long offset, string entryPath, string reason)
    : IOException($"{reason} (offset 0x{offset:X}, entry '{entryPath}').")
{
    public long Offset { get; } = offset;
    public string EntryPath { get; } = entryPath;
}
