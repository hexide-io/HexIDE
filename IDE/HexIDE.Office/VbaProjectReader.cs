namespace HexIDE.Office;

public static class VbaProjectReader
{
    public static VbaProject Read(CompoundEntry projectStorage)
    {
        var vba = projectStorage.FindChild("VBA");
        if (!projectStorage.IsStorage || vba is not { IsStorage: true })
            throw new OfficeFileException("VBA project storage is missing.");
        var (directory, codePage) = VbaDirectory.ReadWithMeasuredCodePage(
            VbaCompression.Decompress(Stream(vba, "dir").ReadBytes()));
        var encoding = VbaDirectory.EncodingFor(codePage);
        var properties = VbaProjectProperties.Read(encoding.GetString(Stream(projectStorage, "PROJECT").ReadBytes()));
        var modules = new List<VbaModule>();
        foreach (var record in directory.Modules)
        {
            var name = record.NameUnicode ?? encoding.GetString(record.NameBytes);
            var streamName = record.StreamNameUnicode;
            if (!properties.ModuleKinds.TryGetValue(name, out var kind))
                throw new OfficeFileException($"VBA module '{name}' has no PROJECT declaration.");
            CompoundEntry? designer = null;
            if (kind == VbaModuleKind.UserForm)
            {
                designer = projectStorage.FindChild(name);
                if (designer is not { IsStorage: true })
                    throw new OfficeFileException($"VBA UserForm '{name}' has no designer storage.");
            }
            // Respect the editor's viewing lock: do not even read the module stream when locked.
            var source = properties.IsLocked ? null : encoding.GetString(
                ReadModuleSource(Stream(vba, streamName).ReadBytes(), record.SourceOffset));
            modules.Add(new VbaModule(name, streamName, kind, record.ModuleType, record.SourceOffset,
                record.IsReadOnly, record.IsPrivate, record.DocString, record.HelpContext, source, designer));
        }
        return new VbaProject(directory.Information, codePage, properties.IsLocked, properties,
            directory.References, modules.AsReadOnly());
    }

    public static byte[] ReadModuleSource(byte[] stream, uint sourceOffset)
    {
        if (sourceOffset >= stream.Length)
            throw new OfficeFileException("VBA module source offset is past its stream.");
        return VbaCompression.Decompress(stream.AsSpan((int)sourceOffset));
    }

    private static CompoundEntry Stream(CompoundEntry storage, string name)
    {
        var entry = storage.FindChild(name);
        if (entry == null || entry.IsStorage)
            throw new OfficeFileException($"VBA stream '{name}' is missing.");
        return entry;
    }
}
