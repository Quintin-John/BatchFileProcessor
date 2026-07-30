namespace Common.FileIngestion.Profiles;

/// <summary>The record framing a profile uses.</summary>
public enum IngestionFormat
{
    /// <summary>Fixed-width records.</summary>
    FixedLength,

    /// <summary>Delimited records.</summary>
    Delimited,
}
