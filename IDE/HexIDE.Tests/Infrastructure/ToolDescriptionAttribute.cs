using Xunit.v3;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Includes a class in the tool and parameter description checks.
/// Run from IDE with: dotnet test HexIDE.Tests/ -- --filter-trait "Category=ToolDescription"
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ToolDescriptionAttribute : Attribute, ITraitAttribute
{
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new("Category", "ToolDescription")];
}
