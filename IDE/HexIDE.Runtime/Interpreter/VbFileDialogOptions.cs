using System.Linq;
using Avalonia.Platform.Storage;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// A CommonDialog's request as the options Avalonia's pickers take (#1084), so every host shows the same dialog.
/// </summary>
public static class VbFileDialogOptions
{
    /// <summary>The <c>Filter</c> pairs as file types: <c>*.txt;*.log</c> is two patterns.</summary>
    public static List<FilePickerFileType> Types(VbFileDialogRequest request) =>
        request.Filters.Select(f => new FilePickerFileType(f.Description)
        {
            Patterns = f.Patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        }).ToList();

    // The type shown first: FilterIndex counts from 1, and 0, or one past the end, shows the first.
    private static FilePickerFileType? First(VbFileDialogRequest request, List<FilePickerFileType> types) =>
        request.FilterIndex >= 1 && request.FilterIndex <= types.Count ? types[request.FilterIndex - 1] : types.FirstOrDefault();

    // The name part of FileName: a program often leaves a whole path there from the last dialog.
    private static string? SuggestedName(VbFileDialogRequest request)
    {
        var name = request.FileName[(request.FileName.LastIndexOfAny(['\\', '/']) + 1)..];
        return name.Length == 0 ? null : name;
    }

    /// <summary>The folder a dialog opens in: InitDir where it is one, and the platform's choice otherwise.</summary>
    public static async Task<IStorageFolder?> StartFolder(IStorageProvider provider, VbFileDialogRequest request) =>
        request.InitialDirectory.Length == 0 ? null : await provider.TryGetFolderFromPathAsync(request.InitialDirectory);

    public static FilePickerOpenOptions Open(VbFileDialogRequest request, IStorageFolder? start)
    {
        var types = Types(request);
        return new FilePickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = false,
            FileTypeFilter = types.Count == 0 ? null : types,
            SuggestedFileType = First(request, types),
            SuggestedFileName = SuggestedName(request),
            SuggestedStartLocation = start,
        };
    }

    public static FilePickerSaveOptions Save(VbFileDialogRequest request, IStorageFolder? start)
    {
        var types = Types(request);
        return new FilePickerSaveOptions
        {
            Title = request.Title,
            FileTypeChoices = types.Count == 0 ? null : types,
            SuggestedFileType = First(request, types),
            SuggestedFileName = SuggestedName(request),
            SuggestedStartLocation = start,
            DefaultExtension = request.DefaultExtension.Length == 0 ? null : request.DefaultExtension,
            ShowOverwritePrompt = request.OverwritePrompt,
        };
    }

    /// <summary>The index, from 1, of the type a Save dialog was answered under, or null where it can't be told.</summary>
    public static int? IndexOf(VbFileDialogRequest request, FilePickerFileType? chosen)
    {
        if (chosen is null)
            return null;
        var types = Types(request);
        var index = types.FindIndex(t => t.Name == chosen.Name
                                         && (t.Patterns ?? []).SequenceEqual(chosen.Patterns ?? []));
        return index < 0 ? null : index + 1;
    }
}
