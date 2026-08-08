namespace Common.FileIngestion.Tests.Reading;

/// <summary>
/// A stream that yields at most <paramref name="drip"/> bytes per read, forcing records to span many pipe
/// segments. Shared by both reader test classes: framing across segment boundaries is the failure mode both
/// readers must survive, so it is asserted the same way for each.
/// </summary>
internal sealed class DripStream(byte[] data, int drip) : Stream
{
    private int _position;

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= data.Length)
        {
            return 0;
        }

        var n = Math.Min(Math.Min(count, drip), data.Length - _position);
        Array.Copy(data, _position, buffer, offset, n);
        _position += n;
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => data.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
