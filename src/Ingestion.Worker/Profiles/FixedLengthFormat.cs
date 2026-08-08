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
    public bool CanFrame(ILayout layout, Stream file)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(file);

        // Another format's layout never fits; the profile pairs them, but the test must be total.
        if (layout is not Layout fixedWidth)
        {
            return false;
        }

        // Every record occupies the same stride, so a file this layout describes is a whole number of them.
        // A file written to a different version of the format has a different stride and will not divide —
        // unless the two strides share a multiple, which is exactly why the caller demands a unique fit
        // rather than taking the first that says yes.
        var stride = (long)fixedWidth.RecordLength + fixedWidth.TerminatorLength;

        // An empty file divides by every stride, so it would fit all candidates and decide nothing.
        return file.Length > 0 && file.Length % stride == 0;
    }

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
