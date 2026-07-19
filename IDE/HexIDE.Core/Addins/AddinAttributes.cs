namespace HexIDE.Addins;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AddinTitleAttribute(string title) : Attribute
{
    public string Title { get; } = title;
}

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AddinDescriptionAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AddinVersionAttribute(string version) : Attribute
{
    public string Version { get; } = version;
}

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AddinAuthorAttribute(string author) : Attribute
{
    public string Author { get; } = author;
}
