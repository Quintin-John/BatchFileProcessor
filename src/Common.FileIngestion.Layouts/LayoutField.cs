namespace Common.FileIngestion.Layouts;

/// <summary>
/// One declared field reduced to what a framing-agnostic consumer needs: its name and whether the layout
/// classifies it as encrypted. Position is deliberately absent — it is a byte range in one framing and an
/// index in another, and no consumer of <see cref="ILayout"/> needs either.
/// </summary>
/// <param name="Name">Field name as declared by the layout.</param>
/// <param name="Encrypt">Whether the layout marks this field for encryption before publish.</param>
public readonly record struct LayoutField(string Name, bool Encrypt);
