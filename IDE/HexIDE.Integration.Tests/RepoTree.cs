using System;
using System.IO;

namespace HexIDE.Integration.Tests;

internal static class RepoTree
{
    public static string Root(string? startDirectory = null)
    {
        var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "IDE")) &&
                Directory.Exists(Path.Combine(dir.FullName, "LspServer")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("no ancestor holds both IDE/ and LspServer/");
    }
}
