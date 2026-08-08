using Common.FileIngestion.Abstractions;
using Common.Messaging.Contracts;

namespace Ingestion.Worker.Tests;

/// <summary>
/// Records what the pipeline published instead of sending it. Shared rather than repeated per test class:
/// every copy was identical and existed for the same reason, so a change to the publisher contract would
/// otherwise have to be made in each one.
/// </summary>
internal sealed class CapturingPublisher : IMessagePublisher
{
    public List<IngestBatchMessage> Batches { get; } = [];

    public List<RejectMessage> Rejects { get; } = [];

    public Task PublishBatchAsync(IngestBatchMessage batch, string destination, CancellationToken cancellationToken)
    {
        Batches.Add(batch);
        return Task.CompletedTask;
    }

    public Task PublishRejectAsync(RejectMessage reject, string destination, CancellationToken cancellationToken)
    {
        Rejects.Add(reject);
        return Task.CompletedTask;
    }
}

/// <summary>A checkpoint store held in memory, so a test can resume a run without touching disk.</summary>
internal sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly Dictionary<string, Watermark> _watermarks = new(StringComparer.Ordinal);

    public Task<Watermark?> LoadAsync(string sourceKey, CancellationToken cancellationToken) =>
        Task.FromResult(_watermarks.GetValueOrDefault(sourceKey));

    public Task SaveAsync(Watermark watermark, CancellationToken cancellationToken)
    {
        _watermarks[watermark.SourceKey] = watermark;
        return Task.CompletedTask;
    }

    public Task ClearAsync(string sourceKey, CancellationToken cancellationToken)
    {
        _watermarks.Remove(sourceKey);
        return Task.CompletedTask;
    }
}
