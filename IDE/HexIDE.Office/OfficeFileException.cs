namespace HexIDE.Office;

/// <summary>A container cannot be read safely. Reasons are program-facing English.</summary>
public sealed class OfficeFileException(string reason, Exception? innerException = null)
    : Exception(reason, innerException)
{
    public string Reason => Message;

    internal static T Read<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                         System.Xml.XmlException or NotSupportedException or
                                         UnauthorizedAccessException or ObjectDisposedException)
        {
            throw new OfficeFileException($"Cannot read Office container: {exception.Message}", exception);
        }
    }
}
