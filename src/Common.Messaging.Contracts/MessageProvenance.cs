namespace Common.Messaging.Contracts;

/// <summary>
/// Shared source/run provenance carried by every message: which run produced it, which file
/// it came from, and how that file was processed. Extracted so the batch and reject messages
/// share one cohesive concept instead of repeating the same fields.
/// </summary>
public sealed record MessageProvenance
{
    /// <summary>Correlation identity for the run that produced the message (the RunId).</summary>
    public string CorrelationId { get; }

    /// <summary>Content hash / identity of the source file.</summary>
    public string FileId { get; }

    /// <summary>Original source file name.</summary>
    public string FileName { get; }

    /// <summary>Profile that produced the message (selects layout, destination, etc.).</summary>
    public string Profile { get; }

    /// <summary>Layout version used to map the records, so consumers resolve field types.</summary>
    public string LayoutVersion { get; }

    /// <summary>Creates validated provenance. Every member is required and non-blank.</summary>
    /// <param name="correlationId">Run correlation id.</param>
    /// <param name="fileId">Source file identity.</param>
    /// <param name="fileName">Source file name.</param>
    /// <param name="profile">Producing profile.</param>
    /// <param name="layoutVersion">Layout version.</param>
    /// <exception cref="ArgumentException">Any argument is null, empty, or whitespace.</exception>
    public MessageProvenance(
        string correlationId,
        string fileId,
        string fileName,
        string profile,
        string layoutVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutVersion);

        CorrelationId = correlationId;
        FileId = fileId;
        FileName = fileName;
        Profile = profile;
        LayoutVersion = layoutVersion;
    }
}
