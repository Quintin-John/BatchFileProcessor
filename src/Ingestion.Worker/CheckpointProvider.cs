namespace Ingestion.Worker;

/// <summary>
/// Which <c>ICheckpointStore</c> backend the host wires. <see cref="File"/> resumes only on the same
/// pod/volume; <see cref="Redis"/> enables cross-instance resume. Selected by configuration, fail-closed
/// on an unknown value.
/// </summary>
internal enum CheckpointProvider
{
    /// <summary>File-on-disk watermark store (default).</summary>
    File,

    /// <summary>Redis-backed watermark store for cross-instance resume.</summary>
    Redis,
}
