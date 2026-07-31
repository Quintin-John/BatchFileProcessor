namespace Common.FileIngestion.Sources;

/// <summary>
/// The filesystem facts a completion guard needs, behind a seam so the guard is unit-testable without a
/// real filesystem.
/// </summary>
internal interface IFileProbe
{
    /// <summary>Whether the file currently exists.</summary>
    bool Exists(string path);

    /// <summary>Current file length in bytes.</summary>
    long Length(string path);

    /// <summary>Last-write timestamp (UTC).</summary>
    DateTimeOffset LastWriteTimeUtc(string path);

    /// <summary>Whether the file opens with no sharing (best-effort: false if a writer still holds it).</summary>
    bool CanOpenExclusive(string path);
}
