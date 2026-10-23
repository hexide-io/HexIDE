using System.Runtime.CompilerServices;

namespace HexIDE.Runtime.Tests;

public sealed class WindowsOnlyTheoryAttribute : TheoryAttribute
{
    public WindowsOnlyTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows filesystem attributes required.";
    }
}

public sealed class UnixOnlyTheoryAttribute : TheoryAttribute
{
    public UnixOnlyTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (OperatingSystem.IsWindows()) Skip = "Unix filesystem attributes required.";
    }
}

public sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (OperatingSystem.IsWindows()) Skip = "Unix filesystem attributes required.";
    }
}
