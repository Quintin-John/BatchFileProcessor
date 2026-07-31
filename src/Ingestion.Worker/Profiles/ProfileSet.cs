using System.Collections.ObjectModel;

namespace Ingestion.Worker.Profiles;

/// <summary>
/// The validated set of ingestion profiles. Non-empty, with unique names (the checkpoint namespace and
/// provenance identity) and unique incoming directories (two profiles watching one folder would race to
/// claim the same file). Defensively copied; read-only.
/// </summary>
internal sealed class ProfileSet
{
    /// <summary>The profiles. Read-only.</summary>
    public IReadOnlyList<Profile> Profiles { get; }

    /// <summary>Creates a validated profile set.</summary>
    /// <param name="profiles">Profiles; required, non-empty, unique names, unique incoming directories.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> or a contained profile is null.</exception>
    /// <exception cref="ArgumentException">Empty, or a name / incoming directory is duplicated.</exception>
    public ProfileSet(IReadOnlyList<Profile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0)
        {
            throw new ArgumentException("At least one profile must be defined.", nameof(profiles));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var incoming = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            ArgumentNullException.ThrowIfNull(profile);

            if (!names.Add(profile.Name))
            {
                throw new ArgumentException($"Duplicate profile name '{profile.Name}'.", nameof(profiles));
            }

            if (!incoming.Add(profile.Folders.Incoming))
            {
                throw new ArgumentException(
                    $"Duplicate incoming directory '{profile.Folders.Incoming}' (profile '{profile.Name}').",
                    nameof(profiles));
            }
        }

        Profiles = new ReadOnlyCollection<Profile>(new List<Profile>(profiles));
    }
}
