namespace Common.Security.DataProtection;

/// <summary>
/// Masks a value PCI-style, revealing the first six and last four characters and masking the
/// middle. Values of ten characters or fewer are fully masked (too short to safely reveal a
/// six/four split).
/// </summary>
public sealed class PanMasker : IMasker
{
    private const int LeadVisible = 6;
    private const int TrailVisible = 4;
    private const int MinLengthToReveal = LeadVisible + TrailVisible; // 10
    private const char MaskChar = '*';

    /// <inheritdoc />
    public string Name => "first6last4";

    /// <inheritdoc />
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= MinLengthToReveal)
        {
            return new string(MaskChar, value.Length);
        }

        var maskedCount = value.Length - MinLengthToReveal;
        return string.Concat(
            value.AsSpan(0, LeadVisible),
            new string(MaskChar, maskedCount),
            value.AsSpan(value.Length - TrailVisible));
    }
}
