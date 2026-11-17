using System.Runtime.CompilerServices;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Test fixtures build projects by hand, with no project type to set their format, so they get VB6's (#3411 slice 10b-3).
/// The product sets every project's format where it opens or makes it, and never sets this.
/// </summary>
internal static class TestFixturesOnlyFormat
{
    [ModuleInitializer]
    internal static void Set() => HexIDE.IDE.ProjectFormats.TestFixturesOnlyFallback = new HexIDE.Projects.Vb6ProjectFileFormat();
}
