using System.Text;
using System.Text.RegularExpressions;

namespace Common.FileIngestion.Profiles;

/// <summary>
/// Resolves a file path to a profile by testing ordered path globs; the first match wins.
/// Globs support <c>*</c> (any run of non-separator characters), <c>**</c> (any characters), and
/// <c>?</c> (one character), matched case-insensitively against a separator-normalised path.
/// </summary>
public sealed class ProfileResolver : IProfileResolver
{
    private const char Separator = '/';
    private readonly List<(Profile Profile, Regex Matcher)> _rules;

    /// <summary>Creates a resolver from ordered profiles.</summary>
    /// <param name="profiles">Ordered profiles; required, non-empty, no null elements.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="profiles"/> is empty or contains a null.</exception>
    public ProfileResolver(IReadOnlyList<Profile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (profiles.Count == 0)
        {
            throw new ArgumentException("At least one profile is required.", nameof(profiles));
        }

        _rules = new List<(Profile, Regex)>(profiles.Count);
        foreach (var profile in profiles)
        {
            if (profile is null)
            {
                throw new ArgumentException("Profiles must not contain null elements.", nameof(profiles));
            }

            _rules.Add((profile, GlobToRegex(profile.Match)));
        }
    }

    /// <inheritdoc />
    public Profile? Resolve(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalized = filePath.Replace('\\', Separator);
        foreach (var (profile, matcher) in _rules)
        {
            if (matcher.IsMatch(normalized))
            {
                return profile;
            }
        }

        return null;
    }

    private static Regex GlobToRegex(string glob)
    {
        var builder = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            switch (c)
            {
                case '*' when i + 1 < glob.Length && glob[i + 1] == '*':
                    builder.Append(".*");
                    i++;
                    break;
                case '*':
                    builder.Append("[^/]*");
                    break;
                case '?':
                    builder.Append('.');
                    break;
                default:
                    builder.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
}
