namespace Common.FileIngestion.Sources;

/// <summary>Real filesystem <see cref="IFileProbe"/> over <see cref="System.IO"/>.</summary>
internal sealed class FileSystemProbe : IFileProbe
{
    public bool Exists(string path) => File.Exists(path);

    public long Length(string path) => new FileInfo(path).Length;

    public DateTimeOffset LastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public bool CanOpenExclusive(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            // Another process (typically the producer still writing) holds the file open.
            return false;
        }
    }
}
