using System.Globalization;

namespace MUI.Web.Localization;

/// <summary>
/// The six values a CLDR plural rule is written in terms of.
/// </summary>
/// <param name="N">The absolute value of the number.</param>
/// <param name="I">Its integer digits.</param>
/// <param name="V">How many fraction digits are <em>visible</em>, trailing zeros included.</param>
/// <param name="W">How many are visible with trailing zeros removed.</param>
/// <param name="F">The visible fraction digits as an integer, trailing zeros included.</param>
/// <param name="T">The same with trailing zeros removed.</param>
/// <param name="E">The compact-decimal exponent, which is zero for everything this site formats.</param>
/// <remarks>
/// <para>
/// <b>Visible is the load-bearing word, and it is why this type exists rather than an <c>int</c>.</b>
/// In English <c>1</c> is <c>one</c> and <c>1.0</c> is <c>other</c> — "1.0 stars" is correct and
/// "1.0 star" is not — and the two are the same quantity. A rule cannot tell them apart from the
/// value alone; it needs to know how the number was <em>written</em>. That is what <c>v</c> and
/// <c>f</c> carry, and it is the single most commonly missed thing in a hand-rolled plural
/// implementation.
/// </para>
/// <para>
/// Every count this site pluralises is an integer, so <c>v</c> is zero throughout and none of this
/// changes an answer today. It is here because a plural implementation that only works for integers
/// is one that silently gives the wrong form the first time somebody formats a rate or an average,
/// and because the rules below are transcribed from CLDR in the operands CLDR states them in — a
/// transcription into a different vocabulary is a transcription that cannot be checked.
/// </para>
/// <para>
/// <b>These are absolute values and this type cannot print a number.</b> CLDR takes the absolute
/// value to choose a category, never to display one, so a <c>Format</c> here would drop the sign of
/// every number it was handed — and it did, along with the group separator, which put "1234 games"
/// in a sentence beside "1,234" in the column. Rendering belongs to <see cref="IcuMessage"/>, which
/// still holds the signed value the caller passed.
/// </para>
/// </remarks>
public readonly record struct PluralOperands(
    decimal N,
    long I,
    int V,
    int W,
    long F,
    long T,
    int E)
{
    /// <summary>The operands of an integer, where every fractional one is zero.</summary>
    public static PluralOperands Of(long value)
    {
        var n = Math.Abs(value);

        return new PluralOperands(n, n, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// The operands of a number as it will actually be written.
    /// </summary>
    /// <param name="value">The quantity.</param>
    /// <param name="visibleFractionDigits">
    /// How many fraction digits the rendered string shows, or null to read them off the value.
    /// </param>
    /// <remarks>
    /// The parameter is what makes <c>1</c> and <c>1.0</c> different: a caller formatting to two
    /// decimal places has to say so, because by the time the number reaches here as a
    /// <see cref="decimal"/> the trailing zeros it will be printed with are a fact about the format
    /// string and not about the value.
    /// </remarks>
    public static PluralOperands Of(decimal value, int? visibleFractionDigits = null)
    {
        var n = Math.Abs(value);
        var i = (long)decimal.Truncate(n);

        // decimal keeps its own scale, so 1.50m already knows it has two fraction digits. That is
        // the right default: a caller who did not say otherwise gets the digits the value carries.
        var scale = (byte)((decimal.GetBits(n)[3] >> 16) & 0x7f);
        var v = visibleFractionDigits ?? scale;

        var fractional = n - i;
        var f = v == 0 ? 0L : (long)decimal.Round(fractional * Pow10(v), 0, MidpointRounding.AwayFromZero);

        // w and t are v and f with trailing zeros taken off: 1.50 has v=2, f=50, w=1, t=5.
        var w = v;
        var t = f;

        while (w > 0 && t % 10 == 0)
        {
            t /= 10;
            w--;
        }

        return new PluralOperands(n, i, v, w, f, t, E: 0);
    }

    private static decimal Pow10(int power)
    {
        var result = 1m;

        for (var at = 0; at < power; at++)
        {
            result *= 10m;
        }

        return result;
    }
}
