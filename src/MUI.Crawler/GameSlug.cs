using System.Text;

namespace MUI.Crawler;

/// <summary>
/// The URL segment a game is minted with (spec §5.7).
/// </summary>
/// <remarks>
/// Two identifiers, deliberately: the id is an immutable GUID every foreign key points at, and the
/// slug is the mutable URL segment, because games rename themselves. Every slug a game has ever had
/// redirects to it, for ever. This is only the arithmetic, never the decision to (re)mint: a rename
/// does not re-mint automatically, or a game that flips its name daily would churn its URL —
/// <see cref="CatalogueBinder"/> calls it on first listing, <see cref="SlugMinter"/> calls it again
/// only once a new name has held for a grace period.
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
    /// A table rather than <c>Normalize(FormD)</c>: this solution sets <c>InvariantGlobalization</c>,
    /// under which <c>string.Normalize</c> is a silent no-op that returns the string unchanged rather
    /// than throwing, so it looks correct and turns "Café Noir" into <c>caf-noir</c>. The range is the
    /// Latin-1 Supplement only; a name in a script this can't fold gets the address-derived fallback.
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
    /// The numeric suffix is §5.7's own answer to a collision — two games both entitled to a URL can
    /// legitimately want the same slug. Taken means taken by anybody, ever: every caller checks the
    /// former-slug table as well as <c>game.slug</c>, since a URL a game gave up is still one somebody
    /// might follow, and pointing it at a different game is worse than the 404 it replaces.
    /// </remarks>
    /// <param name="name">What the game calls itself.</param>
    /// <param name="isTaken">Whether a candidate belongs to anybody, ever.</param>
    /// <param name="fallback">
    /// What to mint from when <paramref name="name"/> folds to nothing — the address the game answers
    /// at, or the URL it already has. Null where the caller genuinely knows nothing else.
    /// </param>
    /// <param name="cancellationToken">The caller's budget.</param>
    public static async Task<string> UniqueAsync(
        string name,
        Func<string, CancellationToken, Task<bool>> isTaken,
        string? fallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(isTaken);

        var stem = Mint(name);

        // The fold above keeps Latin-1 and nothing beyond it, so a game named in Hangul, Cyrillic or
        // Kanji arrives here with nothing to make a URL out of; the fallback is its address, which
        // every caller knows.
        if (stem.Length == 0 && fallback is { Length: > 0 })
        {
            stem = Mint(fallback);
        }

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

        // A listing refused outright is worse than an ugly URL, so this always terminates. Truncated
        // only when there is something to truncate: the range operator throws for a string already
        // shorter than MaxLength.
        var last = $"{stem}-{Guid.CreateVersion7():N}";

        return last.Length > MaxLength ? last[..MaxLength] : last;
    }
}
