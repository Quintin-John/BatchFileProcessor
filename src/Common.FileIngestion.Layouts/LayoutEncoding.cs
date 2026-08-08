using System.Text;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// Resolves the character encoding a layout declares.
/// <para>
/// .NET ships only a handful of encodings in the box — ASCII, UTF-8, Latin-1 and the Unicode variants. The
/// code-page provider must be registered before anything else is reachable, and without it a layout naming
/// a mainframe EBCDIC page or a regional Windows page fails at startup even though the declaration is
/// perfectly valid. Registering it here means the set of legal encodings is decided by the layout, not by
/// what the framework happens to load by default.
/// </para>
/// Fail-closed: an encoding the platform cannot supply is rejected with the name that was declared, so the
/// diagnostic points at the layout rather than at a framework exception.
/// </summary>
public static class LayoutEncoding
{
    // Static initialisation runs once per process and is thread-safe by the CLR's type-initialiser
    // guarantee, which is what this needs: registration must happen before the first resolve, exactly once.
    static LayoutEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Resolves a declared encoding name to its <see cref="Encoding"/>.</summary>
    /// <param name="name">Encoding name as declared by the layout; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank, or names an encoding this platform cannot supply.</exception>
    public static Encoding Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"Layout declares encoding '{name}', which this platform cannot supply.", nameof(name), ex);
        }
    }
}
