namespace Common.FileIngestion.Pipeline;

/// <summary>
/// One file to ingest. <see cref="OpenStream"/> yields a fresh read stream each call — the pipeline
/// opens it twice (a hash pre-pass to establish the FileId, then the parse/publish pass), so it must
/// return equivalent content both times; the file source guarantees this by claiming the file.
/// </summary>
public sealed record IngestRequest
{
    /// <summary>Stable resume key (the claimed file identity), known before reading.</summary>
    public string SourceKey { get; }

    /// <summary>Original source file name (message provenance).</summary>
    public string FileName { get; }

    /// <summary>Run correlation id (message provenance).</summary>
    public string CorrelationId { get; }

    /// <summary>Id of the profile that matched the file (message provenance).</summary>
    public string ProfileId { get; }

    /// <summary>Layout version used to map records (message provenance).</summary>
    public string LayoutVersion { get; }

    /// <summary>Opens a fresh readable stream over the source; invoked once per pass.</summary>
    public Func<Stream> OpenStream { get; }

    /// <summary>Creates a validated request.</summary>
    /// <param name="sourceKey">Stable resume key; required, non-blank.</param>
    /// <param name="fileName">Source file name; required, non-blank.</param>
    /// <param name="correlationId">Run correlation id; required, non-blank.</param>
    /// <param name="profileId">Matched profile id; required, non-blank.</param>
    /// <param name="layoutVersion">Layout version; required, non-blank.</param>
    /// <param name="openStream">Stream factory; required.</param>
    /// <exception cref="ArgumentException">A string argument is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="openStream"/> is null.</exception>
    public IngestRequest(
        string sourceKey,
        string fileName,
        string correlationId,
        string profileId,
        string layoutVersion,
        Func<Stream> openStream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutVersion);
        ArgumentNullException.ThrowIfNull(openStream);

        SourceKey = sourceKey;
        FileName = fileName;
        CorrelationId = correlationId;
        ProfileId = profileId;
        LayoutVersion = layoutVersion;
        OpenStream = openStream;
    }
}
