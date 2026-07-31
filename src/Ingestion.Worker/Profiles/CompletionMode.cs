namespace Ingestion.Worker.Profiles;

/// <summary>
/// How a profile decides a dropped file is fully written before it is claimed. Only modes with an
/// implemented guard are declared; producer-signal modes (atomic-rename / sentinel) are added when
/// their guard exists.
/// </summary>
internal enum CompletionMode
{
    /// <summary>File is complete once its size is unchanged for a quiet period and it opens un-shared.</summary>
    StableSize,
}
