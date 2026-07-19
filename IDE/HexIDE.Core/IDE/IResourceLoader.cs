namespace HexIDE.IDE;

public interface IResourceLoader
{
    Stream? LoadResource(string path);
}
