using System.Text;

namespace MUI.Crawler;

/// <summary>
/// The URL segment a game is minted with (spec §5.7).
/// </summary>
/// <remarks>
/// <para>
/// Two identifiers, deliberately: the id is an immutable GUID every foreign key points at, and the
/// slug is the mutable URL segment, because games rename themselves. <b>Every slug a game has ever
/// had redirects to it, for ever</b> — a URL that once worked keeps working, which is the same
/// promise §7.5 makes about pages.
/// </para>
/// <para>
/// <b>Minting is not re-minting.</b> This is only ever called when a game is first listed. A rename
/// does not re-mint automatically — a game that flips its name daily would otherwise churn its URL —
/// and the re-mint-after-stability half of §5.7, together with the redirect table it needs, belongs
/// with whatever owns renaming. Nothing here does it by accident.
/// </para>
/// </remarks>
public static class GameSlug
{
    /// <summary>
    /// Longer than any name worth putting in a path, and short enough that a slug is readable. A
    /// bound rather than a preference: the name is a stranger's text.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// The slug for a name, before collisions are considered: lower-cased, with non-alphanumerics
    /// collapsed to single hyphens.
    /// </summary>
    /// <remarks>
    /// A name that leaves nothing behind — punctuation only, or a script this fold does not keep —
    /// yields the empty string rather than something invented. The caller decides what to do about
    /// that, because only the caller knows what else it could use.
    /// </remarks>
    public static string Mint(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var builder = new StringBuilder(name.Length);
        var pendingHyphen = false;

        foreach (var rune in name)
        {
            var folded = char.IsAsciiLetterOrDigit(rune) ? char.ToLowerInvariant(rune).ToString() : Fold(rune);

            if (folded is null)
            {
                pendingHyphen = true;
                continue;
            }

            if (pendingHyphen && builder.Length > 0)
            {
                builder.Append('-');
            }

            pendingHyphen = false;
            builder.Append(folded);

            if (builder.Length >= MaxLength)
            {
                break;
            }
        }

        return builder.Length > MaxLength ? builder.ToString(0, MaxLength) : builder.ToString();
    }

    /// <summary>
    /// The ASCII a Latin-1 letter folds to, or null for anything that is not one.
    /// </summary>
    /// <remarks>
    /// <b>A table rather than <c>Normalize(FormD)</c>, because this solution sets
    /// <c>InvariantGlobalization</c>, under which <c>string.Normalize</c> is a silent no-op</b> — it
    /// returns the string unchanged rather than throwing, so decomposing and dropping combining marks
    /// looks correct, compiles, and turns "Café Noir" into <c>caf-noir</c>. Measured, not assumed.
    /// The range is the Latin-1 Supplement and nothing beyond it: it covers essentially every accented
    /// Western European name, and a game named in a script this cannot fold gets the address-derived
    /// fallback rather than a slug pretending to be its name.
    /// </remarks>
    private static string? Fold(char rune) => rune switch
    {
        'À' or 'Á' or 'Â' or 'Ã' or 'Ä' or 'Å' or 'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' => "a",
        'Æ' or 'æ' => "ae",
        'Ç' or 'ç' => "c",
        'È' or 'É' or 'Ê' or 'Ë' or 'è' or 'é' or 'ê' or 'ë' => "e",
        'Ì' or 'Í' or 'Î' or 'Ï' or 'ì' or 'í' or 'î' or 'ï' => "i",
        'Ð' or 'ð' => "d",
        'Ñ' or 'ñ' => "n",
        'Ò' or 'Ó' or 'Ô' or 'Õ' or 'Ö' or 'Ø' or 'ò' or 'ó' or 'ô' or 'õ' or 'ö' or 'ø' => "o",
        'Ù' or 'Ú' or 'Û' or 'Ü' or 'ù' or 'ú' or 'û' or 'ü' => "u",
        'Ý' or 'ý' or 'ÿ' => "y",
        'Þ' or 'þ' => "th",
        'ß' => "ss",
        _ => null,
    };

    /// <summary>
    /// A slug nothing else has taken, asking <paramref name="isTaken"/> until it says no.
    /// </summary>
    /// <remarks>
    /// The numeric suffix is §5.7's own answer to a collision. Two unedited PennMUSHes really do want
    /// the same slug, which is the same observation that makes a codebase default the absence of an
    /// identity signal rather than a weak one — except that here they are two games and both are
    /// entitled to a URL.
    /// </remarks>
    public static async Task<string> UniqueAsync(
        string name,
        Func<string, CancellationToken, Task<bool>> isTaken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(isTaken);

        var stem = Mint(name);
        if (stem.Length == 0)
        {
            // Never an invented word: a slug that reads like a name nobody chose is worse than one
            // that plainly says it was generated.
            stem = "game";
        }

        if (!await isTaken(stem, cancellationToken))
        {
            return stem;
        }

        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            var candidate = $"{stem}-{suffix}";
            if (!await isTaken(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        // Ten thousand games sharing one name is not a collision, it is a bug somewhere upstream —
        // but a listing refused outright is worse than an ugly URL, so this always terminates.
        return $"{stem}-{Guid.CreateVersion7():N}"[..MaxLength];
    }
}
