namespace Common.FileIngestion.Abstractions;

/// <summary>
/// Decides whether a dropped file is fully written and therefore safe to claim. Consulted once per file
/// per poll. Size-stability strategies are stateful across calls (they compare successive observations),
/// so one instance serves one single-threaded poll loop and is not thread-safe.
/// </summary>
public interface ICompletionGuard
{
    /// <summary>True when the file is complete (safe to claim); false while it may still be written.</summary>
    /// <param name="path">Path to the candidate file; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    bool IsComplete(string path);
}
