namespace Common.FileIngestion.Layouts;

/// <summary>
/// The part of a layout that is the same whatever the framing: its identity, its encoding, and the
/// classification of every field it declares.
/// <para>
/// Deliberately narrow — exactly the surface shared by the consumers that must not care how records are
/// framed: data-protection policy, sensitive-log-key redaction, and encoding selection. Everything
/// framing-specific (record length and discriminator for fixed-width; delimiter and row roles for
/// delimited) stays off this interface, so no consumer can reach for it by accident.
/// </para>
/// </summary>
public interface ILayout
{
    /// <summary>Layout version identifier; travels upstream as message provenance.</summary>
    string Version { get; }

    /// <summary>Character encoding name (single-byte).</summary>
    string Encoding { get; }

    /// <summary>
    /// Every field the layout declares, across every record or row type, in declaration order. A field name
    /// may repeat across types; consumers that key by name resolve the collision themselves rather than this
    /// interface silently picking a winner.
    /// </summary>
    IEnumerable<LayoutField> DeclaredFields { get; }
}
