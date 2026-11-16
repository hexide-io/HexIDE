using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace HexIDE.Office;

public enum VbaProjectStatus
{
    Found,
    NoProject,
    Unreadable
}

/// <summary>Owns the located storage's backing file. Dispose after reading its streams.</summary>
public sealed class VbaProjectResult : IDisposable
{
    private readonly CompoundFile? file;
    internal VbaProjectResult(VbaProjectStatus status, CompoundFile? file = null,
        CompoundEntry? project = null, OfficeFileException? error = null)
    {
        Status = status;
        this.file = file;
        Project = project;
        Error = error;
    }

    public VbaProjectStatus Status { get; }
    public CompoundEntry? Project { get; }
    public OfficeFileException? Error { get; }
    public void Dispose() => file?.Dispose();
}

public static class VbaProjectLocator
{
    public const int MaximumZipPartBytes = 64 * 1024 * 1024;
    private const string VbaContentType = "application/vnd.ms-office.vbaProject";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static VbaProjectResult Locate(string path)
    {
        try
        {
            return OfficeFileException.Read(() => LocateFile(path));
        }
        catch (OfficeFileException exception)
        {
            return new VbaProjectResult(VbaProjectStatus.Unreadable, error: exception);
        }
    }

    private static VbaProjectResult LocateFile(string path)
    {
        var source = File.OpenRead(path);
        CompoundFile? compound = null;
        var transferred = false;
        var sourceOwned = false;
        try
        {
            Span<byte> signature = stackalloc byte[8];
            var count = source.Read(signature);
            source.Position = 0;
            if (count == 8 && signature.SequenceEqual(CompoundFile.Signature))
            {
                compound = CompoundFile.Open(source, leaveOpen: false);
                if (compound.Root.FindChild("PowerPoint Document") != null)
                    throw new OfficeFileException("Legacy PowerPoint VBA projects are inside the PowerPoint Document stream (MS-PPT), which is not implemented.");
                var project = compound.Root.FindChild("_VBA_PROJECT_CUR") ?? compound.Root.FindChild("Macros");
                if (project != null && !project.IsStorage)
                    throw new OfficeFileException("The VBA project entry is not a storage.");
                if (project == null)
                    return new VbaProjectResult(VbaProjectStatus.NoProject);
                transferred = true;
                sourceOwned = true;
                return new VbaProjectResult(VbaProjectStatus.Found, compound, project);
            }
            if (count >= 4 && signature[0] == 'P' && signature[1] == 'K' &&
                ((signature[2] == 3 && signature[3] == 4) || (signature[2] == 5 && signature[3] == 6)))
            {
                using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
                var contentTypes = UniquePart(zip, "[Content_Types].xml") ??
                    throw new OfficeFileException("Zip container has no [Content_Types].xml part.");
                using var xmlBytes = new MemoryStream(ReadPart(contentTypes));
                using var xml = XmlReader.Create(xmlBytes, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumZipPartBytes
                });
                var document = XDocument.Load(xml);
                if (document.Root?.Name != ContentTypes + "Types")
                    throw new OfficeFileException("Invalid Open XML content-types root.");
                var projects = document.Root.Elements(ContentTypes + "Override")
                    .Where(e => (string?)e.Attribute("ContentType") == VbaContentType).ToArray();
                if (projects.Length == 0)
                    return new VbaProjectResult(VbaProjectStatus.NoProject);
                if (projects.Length != 1)
                    throw new OfficeFileException("Open XML container declares more than one VBA project part.");
                var partName = (string?)projects[0].Attribute("PartName");
                if (string.IsNullOrEmpty(partName) || !partName.StartsWith('/') || partName.Contains('\\') ||
                    partName.Contains('#') || partName.Contains('?') || partName.Split('/').Skip(1).Any(s => s is "" or "." or ".."))
                    throw new OfficeFileException("Invalid VBA project part name in [Content_Types].xml.");
                var part = UniquePart(zip, partName[1..]) ??
                    throw new OfficeFileException("The declared VBA project zip part is missing.");
                var projectSource = new MemoryStream(ReadPart(part), writable: false);
                try
                {
                    compound = CompoundFile.Open(projectSource, leaveOpen: false);
                }
                catch
                {
                    projectSource.Dispose();
                    throw;
                }
                transferred = true;
                return new VbaProjectResult(VbaProjectStatus.Found, compound, compound.Root);
            }
            throw new OfficeFileException("File is neither a compound file nor an Open XML zip container.");
        }
        finally
        {
            if (!transferred)
                compound?.Dispose();
            // Zip projects own a separate memory stream; compound projects own the source file.
            if (!sourceOwned)
                source.Dispose();
        }
    }

    private static ZipArchiveEntry? UniquePart(ZipArchive zip, string name)
    {
        var matches = zip.Entries.Where(e => PartNamesEqual(e.FullName, name)).Take(2).ToArray();
        if (matches.Length > 1)
            throw new OfficeFileException($"Zip container has duplicate part '{name}'.");
        return matches.FirstOrDefault();
    }

    // OPC part-name equivalence folds ASCII letters only.
    private static bool PartNamesEqual(string left, string right)
    {
        if (left.Length != right.Length)
            return false;
        for (var i = 0; i < left.Length; ++i)
        {
            var a = left[i] is >= 'a' and <= 'z' ? left[i] - ('a' - 'A') : left[i];
            var b = right[i] is >= 'a' and <= 'z' ? right[i] - ('a' - 'A') : right[i];
            if (a != b)
                return false;
        }
        return true;
    }

    private static byte[] ReadPart(ZipArchiveEntry part)
    {
        if (part.Length < 0 || part.Length > MaximumZipPartBytes)
            throw new OfficeFileException($"Zip part '{part.FullName}' exceeds the 64 MiB ({MaximumZipPartBytes} bytes) limit.");
        using var input = part.Open();
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            if (output.Length + count > MaximumZipPartBytes || output.Length + count > part.Length)
                throw new OfficeFileException($"Zip part '{part.FullName}' exceeds its declared length or the 64 MiB ({MaximumZipPartBytes} bytes) limit.");
            output.Write(buffer, 0, count);
        }
        if (output.Length != part.Length)
            throw new OfficeFileException($"Zip part '{part.FullName}' is truncated.");
        return output.ToArray();
    }
}
