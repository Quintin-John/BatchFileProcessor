using Common.FileIngestion.Layouts;
using Common.FileIngestion.Pipeline;
using Ingestion.Worker.Messages;
using Ingestion.Worker.Profiles;

namespace Ingestion.Worker;

/// <summary>
/// <see cref="IIngestFileDispatcher"/> that runs a claimed file through the pipeline of whichever of its
/// profile's layouts the file belongs to — no mediator. Record-level parse failures are not surfaced here:
/// the pipeline routes each bad record to the reject queue and the file still completes. Only a file-level
/// fault (no layout fits, integrity/publish/checkpoint failure) propagates, which the worker quarantines.
/// <para>
/// One folder can receive more than one version of a format, so a profile may name several layouts. Which
/// one a file belongs to is asked of the format — the only thing that knows what makes a file fit — and
/// exactly one must say yes. Zero or several is an unattributable file, which fails closed rather than
/// being run through whichever layout happened to be declared first.
/// </para>
/// </summary>
internal sealed class PipelineIngestFileDispatcher : IIngestFileDispatcher
{
    private readonly IRecordFormat _format;
    private readonly IReadOnlyList<LayoutPipeline> _candidates;

    /// <summary>Creates the dispatcher for a profile's layouts and their pipelines.</summary>
    /// <param name="format">The profile's record format, which decides which layout a file fits; required.</param>
    /// <param name="candidates">One entry per layout the profile declares; required, non-empty.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="candidates"/> is empty.</exception>
    public PipelineIngestFileDispatcher(IRecordFormat format, IReadOnlyList<LayoutPipeline> candidates)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("A profile must declare at least one layout.", nameof(candidates));
        }

        _format = format;
        _candidates = candidates;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">No declared layout fits the file, or more than one does.</exception>
    public Task DispatchAsync(IngestFile command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var selected = Select(command);

        // Provenance carries the version of the layout that actually read the file, which is only known
        // once one has been chosen — it is a property of the match, not of the profile.
        var request = new IngestRequest(
            command.SourceKey,
            command.FileName,
            command.CorrelationId,
            command.ProfileId,
            selected.Layout.Version,
            () => File.OpenRead(command.ProcessingPath));

        return selected.Pipeline.IngestAsync(request, cancellationToken);
    }

    private LayoutPipeline Select(IngestFile command)
    {
        // With one layout there is no choice to make. Asking anyway would duplicate the framing check the
        // pipeline's first pass already performs, and would replace its specific diagnosis with a vaguer one.
        if (_candidates.Count == 1)
        {
            return _candidates[0];
        }

        using var file = File.OpenRead(command.ProcessingPath);

        LayoutPipeline? fit = null;
        var matches = 0;
        foreach (var candidate in _candidates)
        {
            if (!_format.CanFrame(candidate.Layout, file))
            {
                continue;
            }

            fit ??= candidate;
            matches++;
        }

        return matches == 1
            ? fit!.Value
            : throw new InvalidDataException(
                $"File '{command.FileName}' ({file.Length} bytes) matches {matches} of the " +
                $"{_candidates.Count} layouts profile '{command.ProfileId}' declares " +
                $"({string.Join(", ", _candidates.Select(c => c.Layout.Version))}); it cannot be attributed.");
    }
}

/// <summary>
/// One of a profile's layouts together with the pipeline built for it. Paired because a pipeline's reader,
/// parser and field protection all come from one layout, so selecting a layout selects a pipeline.
/// </summary>
/// <param name="Layout">The candidate layout.</param>
/// <param name="Pipeline">The pipeline built for that layout.</param>
internal readonly record struct LayoutPipeline(ILayout Layout, FileIngestionPipeline Pipeline);
