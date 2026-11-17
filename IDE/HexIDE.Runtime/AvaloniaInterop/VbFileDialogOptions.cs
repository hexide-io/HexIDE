using HexIDE.IDE;
using System.Linq;
using Avalonia.Platform.Storage;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A CommonDialog's request as the options Avalonia's pickers take (#1084), so every host shows the same dialog.
/// </summary>
public static class VbFileDialogOptions
{
    public static List<FilePickerFileType> Types(VbFileDialogRequest request) =>
        request.Filters.Select((f, i) => new FilePickerFileType(f.Description)
        {
            Patterns = request.Patterns(i),
        }).ToList();

    /// <summary>The folder a dialog opens in: InitDir where it is one, and the platform's choice otherwise.</summary>
    public static async Task<IStorageFolder?> StartFolder(IStorageProvider provider, VbFileDialogRequest request) =>
        request.InitialDirectory.Length == 0 ? null : await provider.TryGetFolderFromPathAsync(request.InitialDirectory);

    public static FilePickerOpenOptions Open(VbFileDialogRequest request, IStorageFolder? start)
    {
        var types = Types(request);
        return new FilePickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = request.AllowMultiple,
            FileTypeFilter = types.Count == 0 ? null : types,
            SuggestedFileType = request.FirstFilterPosition is { } position ? types[position] : null,
            SuggestedFileName = request.SuggestedName,
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
            SuggestedFileType = request.FirstFilterPosition is { } position ? types[position] : null,
            SuggestedFileName = request.SuggestedName,
            SuggestedStartLocation = start,
            DefaultExtension = request.DefaultExtension.Length == 0 ? null : request.DefaultExtension,
            ShowOverwritePrompt = request.OverwritePrompt,
        };
    }

    public static int? IndexOf(VbFileDialogRequest request, FilePickerFileType? chosen) =>
        chosen is null ? null : request.IndexOf(chosen.Name, chosen.Patterns ?? []);
}
