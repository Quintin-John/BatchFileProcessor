namespace Ingestion.Worker.Profiles;

/// <summary>
/// The framing format of a profile's files, selecting the <c>IRecordParser</c> strategy. Only formats
/// with a built parser are declared — a value that validates but has no parser would be a fail-closed
/// gap, so <c>delimited</c> is not declared here until its parser exists.
/// </summary>
internal enum RecordFormat
{
    /// <summary>Fixed-length records framed by the layout's record length and terminator.</summary>
    FixedLength,
}
