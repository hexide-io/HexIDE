using System.Net.WebSockets;

namespace HexIDE.TwinBasic;

// Language data is codec-owned so sessions cannot depend on its encoding (#1217).
public abstract record CompilerLanguageData;
public sealed record CompilerRootInitialization(IReadOnlyList<string> PackagePaths, CompilerLanguageData LanguageData);
public abstract record CompilerInitializationRequest;
public sealed record CompilerProcessInfo(int ProcessId);
public sealed record CompilerChannelMetadata(int FileSystemPort, int DebuggerPort, int LanguagePort,
    int FormImagePort, int LiveBackupPort, string ServerId);
public sealed record CompilerSettingsRequest;
public sealed record CompilerPackageRequest(string Identifier);
public abstract record CompilerChannelData;
public sealed record CompilerEncodedMessage(WebSocketMessageType Type, ReadOnlyMemory<byte> Bytes);
public abstract record CompilerProjectRequest;
public sealed record CompilerLoadProject(string Path) : CompilerProjectRequest;
public sealed record CompilerProjectsLoaded : CompilerProjectRequest;
public sealed record CompilerKeepSettings : CompilerProjectRequest;
public sealed record CompilerProvidePackage(CompilerPackageRequest Package, ReadOnlyMemory<byte> Content) : CompilerProjectRequest;

/// <summary>Encoding boundary for root initialization, channels and project preparation (#1217).</summary>
public interface ICompilerSessionCodec
{
    CompilerChannelData DecodeChannel(WebSocketMessageType type, ReadOnlyMemory<byte> bytes);
    CompilerEncodedMessage EncodeChannel(CompilerChannelData message);
    CompilerInitializationRequest PrepareInitialization(CompilerRootInitialization initialization, int maximumMessageBytes);
    byte[] EncodePreinitialize();
    byte[] EncodeInitialize(CompilerInitializationRequest initialization);
    CompilerControlMessage DecodeControl(ReadOnlySpan<byte> message, int maximumMessageBytes);
    // Read payloads after envelope checks to preserve budget and error precedence (#1217).
    CompilerProcessInfo ReadProcessInfo(CompilerControlMessage message);
    CompilerChannelMetadata ReadChannelMetadata(CompilerControlMessage message);
    CompilerSettingsRequest ReadSettingsRequest(CompilerControlMessage message);
    CompilerPackageRequest ReadPackageRequest(CompilerControlMessage message);
    long MaximumPackageContentBytes(CompilerPackageRequest package, int maximumMessageBytes);
    byte[] EncodeProjectRequest(int requestId, CompilerProjectRequest request, int maximumMessageBytes);
}
