namespace Common.FileIngestion.Layouts;

/// <summary>
/// What a delimited row type is, and therefore how rows are assigned to it. A delimited file has no
/// guaranteed discriminator column, so identification is positional: the first rows belong to the header
/// type, the last rows to the trailer type, and everything between is data. This is the delimited
/// counterpart of the fixed-width discriminator match.
/// </summary>
public enum RowRole
{
    /// <summary>Leading rows, identified by position from the start of the file.</summary>
    Header,

    /// <summary>The body of the file — every row not claimed by a header or trailer type.</summary>
    Data,

    /// <summary>Trailing rows, identified by position from the end of the file.</summary>
    Trailer,
}
