using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HexIDE.Utils;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// Converts an IsDirectory boolean to the appropriate icon (folder or project file).
/// Used as a MultiBinding converter in the Existing tab file browser.
/// </summary>
public class FileEntryIconConverter : IMultiValueConverter
{
    public static readonly FileEntryIconConverter Instance = new();

    private static IImage? s_folderIcon;
    private static IImage? s_fileIcon;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 1 || values[0] is not bool isDirectory)
            return null;

        if (isDirectory)
        {
            s_folderIcon ??= IconFactory.Themed("Geo.Folder");
            return s_folderIcon;
        }

        s_fileIcon ??= IconFactory.Themed("Geo.Project");
        return s_fileIcon;
    }
}
