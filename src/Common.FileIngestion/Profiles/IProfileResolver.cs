namespace Common.FileIngestion.Profiles;

/// <summary>Resolves the ingestion profile for a dropped file by matching its path against ordered rules.</summary>
public interface IProfileResolver
{
    /// <summary>Returns the first profile whose glob matches <paramref name="filePath"/>, or null if none do.</summary>
    /// <param name="filePath">The dropped file's path.</param>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is null, empty, or whitespace.</exception>
    Profile? Resolve(string filePath);
}
