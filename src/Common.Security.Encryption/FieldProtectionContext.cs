namespace Common.Security.Encryption;

/// <summary>
/// Identifies the field being protected. Its parts form the authenticated associated data that
/// binds a ciphertext to (file, record, field), so a value cannot be replayed elsewhere.
/// </summary>
public sealed record FieldProtectionContext
{
    /// <summary>Source file identity.</summary>
    public string FileId { get; }

    /// <summary>1-based record sequence.</summary>
    public long RecordSeq { get; }

    /// <summary>Field name.</summary>
    public string Field { get; }

    /// <summary>Creates a validated context.</summary>
    /// <param name="fileId">Source file identity; required, non-blank.</param>
    /// <param name="recordSeq">1-based record sequence; must be at least 1.</param>
    /// <param name="field">Field name; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="fileId"/> or <paramref name="field"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordSeq"/> is less than 1.</exception>
    public FieldProtectionContext(string fileId, long recordSeq, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordSeq, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        FileId = fileId;
        RecordSeq = recordSeq;
        Field = field;
    }
}
