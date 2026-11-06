namespace HexIDE.Runtime.Serialization;

public interface IDeserializeErrorSink
{
    void LogError(string error);

    // Hosts without localization retain the English diagnostic; the IDE resolves the key instead.
    void LogError(string key, string fallback, params object[] arguments) =>
        LogError(string.Format(fallback, arguments));
}