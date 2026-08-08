using System.Text;
using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Layouts;

namespace Ingestion.Worker.Profiles;

/// <summary>
/// Everything that differs between one file format and another, in one place: the token a profile selects
/// it by, how its layout is loaded, and which reader and parser it frames and maps with.
/// <para>
/// Held together deliberately. Loading a layout and building the framing for it are two halves of one
/// decision, so a format that could load a layout its own reader cannot frame is not expressible. Adding a
/// format is a new implementation plus a registry entry — no existing file grows another branch.
/// </para>
/// </summary>
internal interface IRecordFormat
{
    /// <summary>The value written as a profile's <c>format</c>; matched case-insensitively.</summary>
    string Token { get; }

    /// <summary>Loads this format's layout definition.</summary>
    /// <param name="path">Path to the layout definition; required, non-blank.</param>
    /// <exception cref="FormatException">The layout is malformed or violates an invariant.</exception>
    ILayout LoadLayout(string path);

    /// <summary>
    /// Whether this layout could frame the given file.
    /// <para>
    /// This is how a profile that declares several layouts decides which one a file belongs to, and the
    /// test is the format's own — nothing outside knows what makes a file fit, so a new format brings its
    /// own answer and the selection never grows a branch per format.
    /// </para>
    /// <para>
    /// A format that cannot yet tell its own layouts apart says yes to all of them. That is not a guess:
    /// the caller requires exactly one candidate to fit, so an undecidable set reads as an ambiguous file
    /// and fails closed, rather than being attributed to whichever layout happened to be declared first.
    /// </para>
    /// </summary>
    /// <param name="layout">A layout produced by <see cref="LoadLayout"/>; required.</param>
    /// <param name="file">The file to test; required, readable and seekable. Left positioned as found.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    bool CanFrame(ILayout layout, Stream file);

    /// <summary>Builds the reader and parser that frame and map this format's records.</summary>
    /// <param name="layout">A layout produced by <see cref="LoadLayout"/>; required.</param>
    /// <param name="encoding">The encoding the layout declares.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="layout"/> is not this format's layout type.</exception>
    RecordFraming CreateFraming(ILayout layout, Encoding encoding);
}

/// <summary>
/// The framing pair for one profile: the reader that turns bytes into records and the parser that turns a
/// record into fields. Always built together, because a reader and a parser only agree about a file if they
/// came from the same format.
/// </summary>
/// <param name="Reader">Frames the stream into records.</param>
/// <param name="Parser">Maps one framed record to fields.</param>
internal readonly record struct RecordFraming(IRecordReader Reader, IRecordParser Parser);
