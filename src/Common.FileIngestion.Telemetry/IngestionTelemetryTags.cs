namespace Common.FileIngestion.Telemetry;

/// <summary>
/// Shared tag-key vocabulary for ingestion spans and metrics, so lineage traces and counters use
/// identical dimensions. One reason to change: the ingestion telemetry contract.
/// </summary>
public static class IngestionTelemetryTags
{
    /// <summary>Content-hash identity of the source file.</summary>
    public const string FileId = "file.id";

    /// <summary>Original file name as delivered.</summary>
    public const string FileName = "file.name";

    /// <summary>Id of the profile that matched the file.</summary>
    public const string ProfileId = "profile.id";

    /// <summary>Record type (layout discriminator match), e.g. TRAN/AUTH.</summary>
    public const string RecordType = "record.type";

    /// <summary>Batch sequence within the file.</summary>
    public const string BatchSeq = "batch.seq";

    /// <summary>Batch message id.</summary>
    public const string MessageId = "messaging.message_id";
}
