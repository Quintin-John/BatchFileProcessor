using System.Text;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Parsing;
using Common.FileIngestion.Reading;

namespace Ingestion.Worker.Profiles;

/// <summary>
/// Fixed-length records, framed by the layout's record length and terminator and identified by a
/// discriminator at a byte position.
/// </summary>
internal sealed class FixedLengthFormat : IRecordFormat
{
    /// <inheritdoc />
    public string Token => "fixed-length";

    /// <inheritdoc />
    public ILayout LoadLayout(string path) => LayoutLoader.LoadFromFile(path);

    /// <inheritdoc />
    public RecordFraming CreateFraming(ILayout layout, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(encoding);

        // Unreachable through a profile, which loads its layout from this same object, but guarded so a
        // mismatched pairing fails at composition rather than misreading every record.
        if (layout is not Layout fixedWidth)
        {
            throw new InvalidOperationException(
                $"Format '{Token}' requires a fixed-length layout but was given '{layout.GetType().Name}'.");
        }

        return new RecordFraming(
            new StreamRecordReader(fixedWidth.RecordLength, fixedWidth.TerminatorLength, encoding),
            new FixedLengthRecordParser(fixedWidth));
    }
}
