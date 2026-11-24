using System.Collections.Immutable;
using System.Text;
using HexIDE.IDE;

namespace HexIDE.TwinBasic;

internal enum ArchiveSaveFailure { ChangedOnDisk, CannotRoundTrip }

internal sealed class ArchiveSaveException(ArchiveSaveFailure failure, string path, Exception? inner = null)
    : IOException($"Cannot save '{path}': {failure}.", inner)
{
    internal ArchiveSaveFailure Failure { get; } = failure;
}

internal static class ArchiveProjectSave
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static ArchiveProjectSnapshot Save(ArchiveProjectSnapshot snapshot,
        IReadOnlyDictionary<ArchiveEntryIdentity, string> edits)
    {
        ArchiveProjectSnapshot replacement;
        try
        {
            foreach (var edit in edits)
                if (!snapshot.TryGetEntry(edit.Key, out var entry) || entry is not ArchiveProjectDocument document
                    || !document.Name.EndsWith(".twin", StringComparison.OrdinalIgnoreCase)
                    || document.ReadText().Status != ArchiveTextStatus.Available)
                    throw new InvalidOperationException("Only available .twin sources can be saved.");

            TwinBasicEntry Edit(ArchiveProjectEntry entry)
            {
                if (entry is ArchiveProjectFolder folder)
                    return ((TwinBasicDirectory)folder.Source) with
                    { Children = folder.Children.Select(Edit).ToImmutableArray() };
                var document = (ArchiveProjectDocument)entry;
                if (!edits.TryGetValue(entry.Identity, out var text) || text == document.ReadText().Text)
                    return document.File;
                var content = Utf8.GetBytes(text);
                if (document.File.Content.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
                    content = [0xEF, 0xBB, 0xBF, .. content];
                // Measured edit in docs/twinbasic-oracle/a-container-hexide-wrote.md (#5143).
                return document.File with { Content = [.. content], Revision = checked(document.File.Revision + 2) };
            }

            var proposed = snapshot.Container with { Root = (TwinBasicDirectory)Edit(snapshot.Root) };
            replacement = Verify(snapshot, proposed);
            foreach (var edit in edits)
                if (!replacement.TryGetEntry(edit.Key, out var entry) || entry is not ArchiveProjectDocument document
                    || document.ReadText(int.MaxValue).Text != edit.Value)
                    throw new InvalidOperationException("Edited text did not round-trip.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or OverflowException)
        {
            throw new ArchiveSaveException(ArchiveSaveFailure.CannotRoundTrip, snapshot.ContainerPath, ex);
        }

        return Write(snapshot, replacement);
    }

    internal static ArchiveProjectSnapshot SaveStructure(ArchiveProjectSnapshot snapshot, TwinBasicContainer proposed) =>
        Write(snapshot, Verify(snapshot, proposed));

    private static ArchiveProjectSnapshot Verify(ArchiveProjectSnapshot snapshot, TwinBasicContainer proposed)
    {
        using var output = new MemoryStream();
        TwinBasicContainerWriter.Write(proposed, output);
        var replacement = ArchiveProjectSnapshot.Read(new MemoryStream(output.ToArray()), snapshot.ContainerPath);
        if (!Equal(proposed.Root, replacement.Container.Root))
            throw new ArchiveSaveException(ArchiveSaveFailure.CannotRoundTrip, snapshot.ContainerPath);
        return replacement;
    }

    private static ArchiveProjectSnapshot Write(ArchiveProjectSnapshot snapshot, ArchiveProjectSnapshot replacement)
    {
        if (!MatchesDisk(snapshot))
            throw new ArchiveSaveException(ArchiveSaveFailure.ChangedOnDisk, snapshot.ContainerPath);
        AtomicFileWriter.WriteRecoverableBytes(snapshot.ContainerPath, replacement.OriginalBytes);
        return replacement;
    }

    private static bool MatchesDisk(ArchiveProjectSnapshot snapshot)
    {
        using var input = File.OpenRead(snapshot.ContainerPath);
        if (input.Length != snapshot.OriginalBytes.Length) return false;
        var bytes = new byte[snapshot.OriginalBytes.Length];
        try { input.ReadExactly(bytes); }
        catch (EndOfStreamException) { return false; }
        return input.ReadByte() == -1 && bytes.AsSpan().SequenceEqual(snapshot.OriginalBytes);
    }

    private static bool Equal(TwinBasicEntry left, TwinBasicEntry right)
    {
        if (left.Name != right.Name || left.Revision != right.Revision || left.Flags != right.Flags || left.Category != right.Category)
            return false;
        if (left is TwinBasicFile a && right is TwinBasicFile b)
            return a.Content.AsSpan().SequenceEqual(b.Content.AsSpan()) && a.Revisions.AsSpan().SequenceEqual(b.Revisions.AsSpan());
        if (left is TwinBasicDirectory x && right is TwinBasicDirectory y)
            return x.Children.Length == y.Children.Length && x.Children.Zip(y.Children).All(pair => Equal(pair.First, pair.Second));
        return false;
    }
}
