namespace Common.FileIngestion.Checkpointing.Redis;

/// <summary>
/// The three Redis string operations a watermark store needs, behind a seam so the store's key-building
/// and serialization are unit-testable without a real Redis connection.
/// </summary>
internal interface IRedisWatermarkBackend
{
    /// <summary>Gets the string value at <paramref name="key"/>, or null if absent.</summary>
    Task<string?> GetAsync(string key);

    /// <summary>Sets the string value at <paramref name="key"/>.</summary>
    Task SetAsync(string key, string value);

    /// <summary>Deletes <paramref name="key"/> if present.</summary>
    Task DeleteAsync(string key);
}
