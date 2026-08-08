using System.Text;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Parsing;
using Common.FileIngestion.Reading;

namespace Ingestion.Worker.Profiles;

/// <summary>
/// Delimited records — CSV, TSV, or any other separator the layout declares — framed by the line terminator
/// and identified by row position rather than by a discriminator.
/// </summary>
internal sealed class DelimitedFormat : IRecordFormat
{
    /// <inheritdoc />
    public string Token => "delimited";

    /// <inheritdoc />
    public ILayout LoadLayout(string path) => DelimitedLayoutLoader.LoadFromFile(path);

    /// <inheritdoc />
    public bool CanFrame(ILayout layout, Stream file)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(file);

        // Rows vary in length, so nothing structural separates one delimited layout from another: there is
        // no stride to divide by, and two layouts over the same feed can accept the same bytes. Every
        // delimited layout is therefore a possible fit, which leaves a profile declaring one working
        // exactly as before and a profile declaring several failing closed on the caller's unique-fit rule
        // — the honest answer until this format has a test that can actually tell them apart.
        return layout is DelimitedLayout;
    }

    /// <inheritdoc />
    public RecordFraming CreateFraming(ILayout layout, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(encoding);

        // Unreachable through a profile, which loads its layout from this same object, but guarded so a
        // mismatched pairing fails at composition rather than misreading every record.
        if (layout is not DelimitedLayout delimited)
        {
            throw new InvalidOperationException(
                $"Format '{Token}' requires a delimited layout but was given '{layout.GetType().Name}'.");
        }

        return new RecordFraming(
            new DelimitedLineReader(delimited, encoding),
            new DelimitedRecordParser(delimited));
    }
}
