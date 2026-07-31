namespace Common.FileIngestion.Abstractions;

/// <summary>
/// A file a source has exclusively claimed for processing. <see cref="Name"/> is the stable identity
/// used as both the resume key and message file name; <see cref="ProcessingPath"/> is where the
/// claimed (immutable) copy now lives.
/// </summary>
/// <param name="Name">Claimed file name (resume key and provenance).</param>
/// <param name="ProcessingPath">Full path to the claimed file.</param>
public sealed record ClaimedFile(string Name, string ProcessingPath);
