using Common.FileIngestion.Pipeline;
using Ingestion.Worker.Messages;
using MassTransit;

namespace Ingestion.Worker.Consumers;

/// <summary>
/// Mediator consumer that turns an <see cref="IngestFile"/> command into a pipeline run: it opens the
/// claimed file and delegates to <see cref="FileIngestionPipeline"/>. Any failure propagates so the
/// mediator send faults and the worker moves the file to failed (fail-closed).
/// </summary>
public sealed class IngestFileConsumer : IConsumer<IngestFile>
{
    private readonly FileIngestionPipeline _pipeline;

    /// <summary>Creates the consumer.</summary>
    /// <param name="pipeline">The ingestion pipeline; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeline"/> is null.</exception>
    public IngestFileConsumer(FileIngestionPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public Task Consume(ConsumeContext<IngestFile> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var command = context.Message;
        var request = new IngestRequest(
            command.SourceKey,
            command.FileName,
            command.CorrelationId,
            command.ProfileId,
            command.LayoutVersion,
            () => File.OpenRead(command.ProcessingPath));

        return _pipeline.IngestAsync(request, context.CancellationToken);
    }
}
