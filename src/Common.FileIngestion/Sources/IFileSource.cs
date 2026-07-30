namespace Common.FileIngestion.Sources;

/// <summary>
/// Supplies files to ingest and records their terminal outcome. A claim is exclusive and makes the
/// file immutable for the duration of processing (so the pipeline's hash/read passes agree and a
/// resume is safe). <see cref="RecoverOrphans"/> is called once at startup to re-offer files a prior
/// run claimed but never completed; <see cref="Claim"/> is called each poll for newly arrived files.
/// </summary>
public interface IFileSource
{
    /// <summary>Returns files claimed by a prior run that never completed (interrupted mid-flight).</summary>
    IReadOnlyList<ClaimedFile> RecoverOrphans();

    /// <summary>Exclusively claims newly arrived files, returning those this call won.</summary>
    IReadOnlyList<ClaimedFile> Claim();

    /// <summary>Marks a claimed file successfully processed.</summary>
    /// <param name="file">The claimed file; required.</param>
    void Complete(ClaimedFile file);

    /// <summary>Marks a claimed file failed (moved aside for inspection).</summary>
    /// <param name="file">The claimed file; required.</param>
    void Fail(ClaimedFile file);
}
