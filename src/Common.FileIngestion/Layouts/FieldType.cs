namespace Common.FileIngestion.Layouts;

/// <summary>How a field's raw bytes are interpreted. Generic — not tied to any specific file format.</summary>
public enum FieldType
{
    /// <summary>Raw text.</summary>
    Text,

    /// <summary>Numeric (decimal) value.</summary>
    Number,

    /// <summary>Calendar date.</summary>
    Date,

    /// <summary>Time of day.</summary>
    Time,

    /// <summary>Unused padding; parsed but not emitted.</summary>
    Filler,
}
