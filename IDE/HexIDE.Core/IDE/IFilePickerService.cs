namespace HexIDE.IDE;

public record FilePickerFilter(string Name, params string[] Extensions);

public record FilePickerOptions(
    string? Title = null,
    bool AllowMultiple = false,
    IReadOnlyList<FilePickerFilter>? Filters = null);

public record SaveFilePickerOptions(
    string? Title = null,
    string? SuggestedFileName = null,
    IReadOnlyList<FilePickerFilter>? Filters = null);

public interface IFilePickerService
{
    Task<IReadOnlyList<string>?> OpenFileAsync(FilePickerOptions options);
    Task<string?> SaveFileAsync(SaveFilePickerOptions options);
}
