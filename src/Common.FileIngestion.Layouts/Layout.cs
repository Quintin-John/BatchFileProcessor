using System.Collections.ObjectModel;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// A validated fixed-width layout: the record length, the discriminator position, and the record
/// types. Construction is fail-closed — a <see cref="Layout"/> cannot exist in an invalid state.
/// Generic: the format-specific detail lives entirely in the source data, not here.
/// </summary>
public sealed class Layout : ILayout
{
    private readonly Dictionary<string, RecordDefinition> _byMatch;

    /// <summary>Layout version identifier.</summary>
    public string Version { get; }

    /// <summary>Fixed record length in bytes.</summary>
    public int RecordLength { get; }

    /// <summary>Character encoding name (single-byte).</summary>
    public string Encoding { get; }

    /// <summary>Record terminator length in bytes (0 = fixed-width with no terminator, 1 = LF, 2 = CRLF).</summary>
    public int TerminatorLength { get; }

    /// <summary>1-based start of the record-type discriminator.</summary>
    public int DiscriminatorStart { get; }

    /// <summary>Length of the discriminator.</summary>
    public int DiscriminatorLength { get; }

    /// <summary>0-based offset of the discriminator (derived from <see cref="DiscriminatorStart"/>).</summary>
    public int DiscriminatorOffset => DiscriminatorStart - 1;

    /// <summary>The record types. Defensively copied; read-only.</summary>
    public IReadOnlyList<RecordDefinition> RecordTypes { get; }

    /// <inheritdoc />
    public IEnumerable<LayoutField> DeclaredFields =>
        RecordTypes.SelectMany(record => record.Fields)
                   .Select(definition => new LayoutField(definition.Name, definition.Encrypt));

    /// <summary>Creates a validated layout. All structural invariants are enforced here.</summary>
    /// <param name="version">Version identifier; required, non-blank.</param>
    /// <param name="recordLength">Fixed record length; must be at least 1.</param>
    /// <param name="encoding">Encoding name; required, non-blank.</param>
    /// <param name="terminatorLength">Record terminator length in bytes; must be non-negative (0 = none).</param>
    /// <param name="discriminatorStart">1-based discriminator start; must fit within the record.</param>
    /// <param name="discriminatorLength">Discriminator length; must fit within the record.</param>
    /// <param name="recordTypes">Record types; required, non-empty, unique matches, each field set tiling the record with no gaps.</param>
    /// <exception cref="ArgumentException">Any string is blank, record types are empty, matches collide, or a record's fields do not tile the record.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A numeric value is out of range or the discriminator exceeds the record.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="recordTypes"/> is null.</exception>
    public Layout(
        string version,
        int recordLength,
        string encoding,
        int terminatorLength,
        int discriminatorStart,
        int discriminatorLength,
        IReadOnlyList<RecordDefinition> recordTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordLength, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(encoding);
        ArgumentOutOfRangeException.ThrowIfNegative(terminatorLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(discriminatorStart, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(discriminatorLength, 1);
        ArgumentNullException.ThrowIfNull(recordTypes);

        if (discriminatorStart + discriminatorLength - 1 > recordLength)
        {
            throw new ArgumentOutOfRangeException(nameof(discriminatorLength), "Discriminator exceeds the record length.");
        }

        if (recordTypes.Count == 0)
        {
            throw new ArgumentException("A layout must define at least one record type.", nameof(recordTypes));
        }

        _byMatch = new Dictionary<string, RecordDefinition>(recordTypes.Count, StringComparer.Ordinal);
        foreach (var record in recordTypes)
        {
            ArgumentNullException.ThrowIfNull(record);

            if (!_byMatch.TryAdd(record.Match, record))
            {
                throw new ArgumentException($"Duplicate discriminator value '{record.Match}'.", nameof(recordTypes));
            }

            // A skipped record (header/trailer) is consumed for framing but never sliced, so it emits no
            // fields and is exempt from the byte-coverage invariant that applies to emitted records.
            if (!record.Skip)
            {
                ValidateContiguousCoverage(record, recordLength);
            }
        }

        Version = version;
        RecordLength = recordLength;
        Encoding = encoding;
        TerminatorLength = terminatorLength;
        DiscriminatorStart = discriminatorStart;
        DiscriminatorLength = discriminatorLength;
        RecordTypes = new ReadOnlyCollection<RecordDefinition>(new List<RecordDefinition>(recordTypes));
    }

    /// <summary>
    /// Resolves the record type for a discriminator value, or null if unknown. A blank/whitespace
    /// value is data (an empty record-type field), not a programming error, so it resolves to null
    /// (unknown) rather than throwing — the caller quarantines that one record instead of faulting
    /// the whole file. Only a null reference is a caller bug.
    /// </summary>
    /// <param name="discriminatorValue">The discriminator value read from a record.</param>
    /// <exception cref="ArgumentNullException"><paramref name="discriminatorValue"/> is null.</exception>
    public RecordDefinition? ResolveByDiscriminator(string discriminatorValue)
    {
        ArgumentNullException.ThrowIfNull(discriminatorValue);
        return _byMatch.GetValueOrDefault(discriminatorValue);
    }

    private static void ValidateContiguousCoverage(RecordDefinition record, int recordLength)
    {
        // Fields must tile the record exactly: start at 1, chain with no gap/overlap, end at recordLength.
        // This is a completeness guarantee on the layout (every byte is accounted for), not value policing.
        var expected = 1;
        foreach (var field in record.Fields)
        {
            if (field.Start != expected)
            {
                throw new ArgumentException(
                    $"Record '{record.Name}': field '{field.Name}' starts at {field.Start}, expected {expected} (gap or overlap).",
                    nameof(record));
            }

            expected = field.EndInclusive + 1;
        }

        if (expected - 1 != recordLength)
        {
            throw new ArgumentException(
                $"Record '{record.Name}': fields cover {expected - 1} bytes, expected {recordLength}.",
                nameof(record));
        }
    }
}
