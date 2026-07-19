using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class FileBrowserViewModel : ObservableObject
{
    private readonly Stack<string> _backStack = new();

    [Notify] private string currentDirectory;
    [Notify] private string fileName = "";
    [Notify] private int selectedFilterIndex;
    [Notify] private FileSystemEntryViewModel? selectedEntry;

    public ObservableCollection<FileSystemEntryViewModel> Entries { get; } = new();

    public string[] FileTypeFilters { get; } =
    [
        "VB Project Files (*.vbp)",
        "All Files (*.*)"
    ];

    private static readonly string[] s_vbpPatterns = ["*.vbp"];
    private static readonly string[] s_allPatterns = ["*.*"];

    public DelegateCommand NavigateUpCommand { get; }
    public DelegateCommand NavigateBackCommand { get; }

    public FileBrowserViewModel()
    {
        // Start in Documents or user profile
        var startDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(startDir))
            startDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        currentDirectory = startDir;

        NavigateUpCommand = new DelegateCommand(NavigateUp, () =>
        {
            var parent = Directory.GetParent(CurrentDirectory);
            return parent != null;
        });
        NavigateBackCommand = new DelegateCommand(NavigateBack, () => _backStack.Count > 0);

        RefreshEntries();
    }

    public void NavigateToDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        _backStack.Push(CurrentDirectory);
        CurrentDirectory = Path.GetFullPath(path);
        RefreshEntries();
        NavigateUpCommand.RaiseCanExecutedChanged();
        NavigateBackCommand.RaiseCanExecutedChanged();
    }

    private void NavigateUp()
    {
        var parent = Directory.GetParent(CurrentDirectory);
        if (parent == null)
            return;

        _backStack.Push(CurrentDirectory);
        CurrentDirectory = parent.FullName;
        RefreshEntries();
        NavigateUpCommand.RaiseCanExecutedChanged();
        NavigateBackCommand.RaiseCanExecutedChanged();
    }

    private void NavigateBack()
    {
        if (_backStack.Count == 0)
            return;

        CurrentDirectory = _backStack.Pop();
        RefreshEntries();
        NavigateUpCommand.RaiseCanExecutedChanged();
        NavigateBackCommand.RaiseCanExecutedChanged();
    }

    private void OnSelectedFilterIndexChanged()
    {
        RefreshEntries();
    }

    private void OnSelectedEntryChanged()
    {
        if (SelectedEntry != null && !SelectedEntry.IsDirectory)
            FileName = SelectedEntry.Name;
    }

    private string[] GetActivePatterns()
    {
        return SelectedFilterIndex == 0 ? s_vbpPatterns : s_allPatterns;
    }

    private void RefreshEntries()
    {
        Entries.Clear();

        if (!Directory.Exists(CurrentDirectory))
            return;

        try
        {
            // Directories first, sorted alphabetically
            var dirs = new DirectoryInfo(CurrentDirectory).GetDirectories()
                .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var dir in dirs)
                Entries.Add(new FileSystemEntryViewModel(dir.Name, dir.FullName, true));

            // Files matching the current filter
            var patterns = GetActivePatterns();
            var files = new List<FileInfo>();
            foreach (var pattern in patterns)
            {
                files.AddRange(new DirectoryInfo(CurrentDirectory).GetFiles(pattern)
                    .Where(f => (f.Attributes & FileAttributes.Hidden) == 0));
            }

            foreach (var file in files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                Entries.Add(new FileSystemEntryViewModel(file.Name, file.FullName, false));
        }
        catch (UnauthorizedAccessException)
        {
            // Silently skip directories we can't read
        }
        catch (IOException)
        {
            // Silently skip IO errors
        }
    }

    /// <summary>
    /// Resolves the full path of the file the user has selected/typed.
    /// Returns null if the file doesn't exist.
    /// </summary>
    public string? ResolveSelectedFile()
    {
        if (string.IsNullOrWhiteSpace(FileName))
            return null;

        // If it's an absolute path, use it directly
        var path = Path.IsPathRooted(FileName)
            ? FileName
            : Path.Combine(CurrentDirectory, FileName);

        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }
}

public class FileSystemEntryViewModel : ObservableObject
{
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }

    public FileSystemEntryViewModel(string name, string fullPath, bool isDirectory)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
    }
}
