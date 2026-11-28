using HexIDE.Localization;

namespace HexIDE.VisualDesigner;

public sealed class DesignerUndoName
{
    private readonly string _text;
    private readonly object[]? _arguments;

    private DesignerUndoName(string text, object[]? arguments)
    {
        _text = text;
        _arguments = arguments;
    }

    public static DesignerUndoName Localized(string key, params object[] arguments) => new(key, arguments);

    public static DesignerUndoName Verbatim(string text) => new(text, null);

    internal string Resolve(ILocalizationService localization) =>
        _arguments is null ? _text : string.Format(localization.GetString(_text), _arguments);
}
