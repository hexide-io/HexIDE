namespace HexIDE.Runtime.Serialization;

public interface IDeserializeErrorSink
{
    void LogError(string error);
}