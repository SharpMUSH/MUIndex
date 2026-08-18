using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace MUI.Web.Localization;

/// <summary>
/// ICU MessageFormat, rendered.
/// </summary>
/// <remarks>
/// <para>
/// Implements the whole of MessageFormat 1.0: simple arguments, <c>number</c>, <c>date</c> and
/// <c>time</c> with styles and skeletons, <c>plural</c> and <c>selectordinal</c> with <c>offset:</c>
/// and <c>=value</c> matches, <c>select</c>, arbitrary nesting, and ICU's apostrophe quoting. See
/// <see cref="MessagePattern"/> for the parse and for why <c>choice</c> is refused.
/// </para>
/// <para>
/// Patterns are parsed once and cached, keyed on pattern text; locale is applied at format time.
/// </para>
/// <para>
/// <b>The cache is unbounded, and that is safe only because of what may be passed to it</b> — a
/// pattern comes from a resource bundle or a literal here, so the set of distinct keys is fixed at
/// startup. <b>A pattern built from request data must never reach <see cref="Format"/> or
/// <see cref="Compile"/></b>: a querystring or a game's own name interpolated into a pattern would
/// make this dictionary grow without limit. Interpolate into an <em>argument</em> instead, which is
/// not cached or parsed.
/// </para>
/// </remarks>
public static class IcuMessage
{
    private static readonly ConcurrentDictionary<string, MessagePattern> Parsed = new(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, object?> NoArguments =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Renders <paramref name="pattern"/> for <paramref name="tag"/>.</summary>
    /// <exception cref="FormatException">The pattern is malformed, or an argument is missing.</exception>
    public static string Format(
        string pattern,
        string tag,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(tag);

        var b = new StringBuilder(pattern.Length);

        Render(Compile(pattern), tag, arguments ?? NoArguments, b, hash: null);

        return b.ToString();
    }

    /// <summary>The parsed form of a pattern, from the cache or freshly parsed.</summary>
    public static MessagePattern Compile(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return Parsed.GetOrAdd(pattern, MessagePattern.Parse);
    }

    private static void Render(
        MessagePattern message,
        string tag,
        IReadOnlyDictionary<string, object?> args,
        StringBuilder b,
        string? hash)
    {
        foreach (var part in message.Parts)
        {
            switch (part)
            {
                case LiteralPart literal:
                    b.Append(literal.Text);
                    break;

                case HashPart:
                    // Outside a plural branch the parser never produces one of these, so a `#` that
                    // reaches here always has a number behind it.
                    b.Append(hash);
                    break;

                case ArgumentPart argument:
                    Argument(argument, tag, args, b, hash);
                    break;
            }
        }
    }

    private static void Argument(
        ArgumentPart argument,
        string tag,
        IReadOnlyDictionary<string, object?> args,
        StringBuilder b,
        string? hash)
    {
        var culture = Culture(tag);

        if (!args.TryGetValue(argument.Name, out var value))
        {
            throw new FormatException($"No argument named '{argument.Name}' was supplied.");
        }

        switch (argument.Kind)
        {
            case ArgumentKind.None:
                b.Append(Convert.ToString(value, culture));
                return;

            case ArgumentKind.Number:
                b.Append(Number(argument.Name, value, argument.Style, culture));
                return;

            case ArgumentKind.Date:
            case ArgumentKind.Time:
                b.Append(Temporal(value, argument.Kind, argument.Style, culture));
                return;

            case ArgumentKind.Plural:
            case ArgumentKind.SelectOrdinal:
                Plural(argument, tag, args, b, value, culture);
                return;

            case ArgumentKind.Select:
                var chosen = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

                Render(
                    argument.Branches.GetValueOrDefault(chosen) ?? argument.Branches["other"],
                    tag, args, b, hash);
                return;
        }
    }

    private static void Plural(
        ArgumentPart argument,
        string tag,
        IReadOnlyDictionary<string, object?> args,
        StringBuilder b,
        object? value,
        CultureInfo culture)
    {
        var number = Quantity(argument.Name, value);
        var operands = value is PluralOperands given ? given : PluralOperands.Of(number);
        var kind = argument.Kind is ArgumentKind.SelectOrdinal
            ? PluralKind.Ordinal
            : PluralKind.Cardinal;

        // An `=value` match is tested against the number as written, before the offset — ICU says
        // so, and it is what lets "=0 {nobody}" work in a message that also subtracts one.
        var exact = "=" + Plain(number, operands.V);

        if (argument.Branches.TryGetValue(exact, out var matched))
        {
            Render(matched, tag, args, b, Hash(number, operands.V, culture));
            return;
        }

        // The category, and `#`, are both taken from the offset-adjusted number. "{n, plural,
        // offset:1 other {and # others}}" over three people is "and 2 others".
        var adjusted = number - argument.Offset;
        var keyword = PluralRules.Keyword(
            PluralRules.Of(tag, PluralOperands.Of(adjusted, operands.V), kind));

        var branch = argument.Branches.GetValueOrDefault(keyword) ?? argument.Branches["other"];

        Render(branch, tag, args, b, Hash(adjusted, operands.V, culture));
    }

    /// <summary>The signed number a caller passed, or a refusal naming the argument.</summary>
    private static decimal Quantity(string name, object? value) => value switch
    {
        int i => i,
        long l => l,
        short s => s,
        byte by => by,
        decimal d => d,
        double db => (decimal)db,
        float f => (decimal)f,

        // Absolute by construction — CLDR operands have already discarded any sign the quantity had.
        PluralOperands o => o.N,

        null => throw new FormatException($"'{name}' is a plural argument and was null."),
        _ => throw new FormatException(
            $"'{name}' is a plural argument and must be a number, not {value.GetType().Name}."),
    };

    /// <summary>
    /// The number a <c>#</c> stands for, written exactly as <c>{n, number}</c> would write it.
    /// </summary>
    /// <remarks>
    /// <c>#</c> is replaced with the argument's own formatted number, not raw integer digits — one
    /// bundle may not print one quantity two ways ("1234 games" beside "1,234" in a column). The
    /// sign travels with it: CLDR takes the absolute value only to choose a category, never to
    /// display one, and "3 games" for minus three is a measurement stated backwards.
    /// </remarks>
    private static string Hash(decimal number, int visibleFractionDigits, CultureInfo culture) =>
        number.ToString(
            visibleFractionDigits == 0 ? "#,##0" : "#,##0." + new string('0', visibleFractionDigits),
            culture);

    /// <summary>The same number as an <c>=</c> key, which is matched and never shown to anybody.</summary>
    /// <remarks>
    /// Plain and invariant — compared against what an author typed between the braces, not something
    /// a locale's separator should affect.
    /// </remarks>
    private static string Plain(decimal number, int visibleFractionDigits) =>
        number.ToString(
            visibleFractionDigits == 0 ? "0" : "0." + new string('0', visibleFractionDigits),
            CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>{n, number, style}</c>, with ICU's named styles and its <c>::</c> skeletons.
    /// </summary>
    /// <remarks>
    /// Nothing on this site calls it: counts, versions and ages are machine output and stay in
    /// Western digits in every locale, since a localized count column loses the alignment that is
    /// the only reason it is a column. This covers prose instead — a percentage inside a sentence is
    /// read rather than scanned, and that one localizes.
    /// </remarks>
    private static string Number(string name, object? value, string? style, CultureInfo culture)
    {
        decimal number;

        try
        {
            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is InvalidCastException or OverflowException or FormatException)
        {
            // Format's documented contract is FormatException; Convert's exceptions don't name the
            // argument, which a page rendering forty messages needs to know.
            throw new FormatException(
                $"'{name}' is a number argument and must be a number, "
                + $"not {value?.GetType().Name ?? "null"}.", e);
        }

        if (style is { Length: > 0 } && style.StartsWith("::", StringComparison.Ordinal))
        {
            return Skeleton(number, style[2..].Trim(), culture);
        }

        return style switch
        {
            null or "" => number.ToString("#,##0.###", culture),
            "integer" => Math.Round(number, MidpointRounding.ToEven).ToString("#,##0", culture),
            // Built rather than "P0": .NET's percent pattern inserts a space before the sign in
            // several cultures and ICU's does not.
            "percent" => (number * 100m).ToString("#,##0.###", culture) + culture.NumberFormat.PercentSymbol,
            "currency" => number.ToString("C", culture),

            // Anything else is a .NET format string, which is what ICU does with an unrecognised
            // style too: it hands it to the underlying number formatter.
            _ => number.ToString(style, culture),
        };
    }

    /// <summary>The handful of number skeletons that mean anything without a full ICU behind them.</summary>
    private static string Skeleton(decimal number, string skeleton, CultureInfo culture)
    {
        var parts = skeleton.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var format = "#,##0.###";
        var percent = false;

        foreach (var token in parts)
        {
            if (token is "percent")
            {
                percent = true;
                continue;
            }

            // .00 / .0 / .### — the fraction-precision stem, which is the one anybody writes.
            if (token.StartsWith('.'))
            {
                format = "#,##0" + token;
                continue;
            }

            if (token is "group-off")
            {
                format = format.Replace("#,##0", "0", StringComparison.Ordinal);
            }
        }

        var rendered = (percent ? number * 100m : number).ToString(format, culture);

        return percent ? rendered + culture.NumberFormat.PercentSymbol : rendered;
    }

    /// <summary>
    /// <c>{d, date, style}</c> and <c>{d, time, style}</c>.
    /// </summary>
    /// <remarks>
    /// Also uncalled, and also deliberate: every date this site prints goes through
    /// <see cref="Components.Dates"/> instead, which states UTC explicitly. This exists so the
    /// formatter doesn't refuse a pattern that carries a date.
    /// </remarks>
    private static string Temporal(object? value, ArgumentKind kind, string? style, CultureInfo culture)
    {
        var when = value switch
        {
            DateTimeOffset offset => offset,

            // Unspecified is treated as UTC, not the machine's own offset — this site is UTC
            // everywhere, and a date arriving without a zone did not arrive from somewhere else.
            DateTime { Kind: DateTimeKind.Unspecified } bare => new DateTimeOffset(bare, TimeSpan.Zero),
            DateTime dt => new DateTimeOffset(dt),

            // A day-grain fact carries a DateOnly (the trend chart's columns, the reachability
            // strip's bars), so those surfaces don't each format their own date at the call site.
            DateOnly day => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            _ => throw new FormatException($"A {kind} argument must be a date, not {value?.GetType().Name ?? "null"}."),
        };

        var date = kind is ArgumentKind.Date;

        if (style is { Length: > 0 } && style.StartsWith("::", StringComparison.Ordinal))
        {
            // .NET has no skeleton engine, so the closest honest thing is the culture's long or
            // short pattern.
            style = style.Contains('y', StringComparison.Ordinal) ? "long" : "short";
        }

        return style switch
        {
            null or "" or "medium" => when.ToString(date ? "d MMM yyyy" : "HH:mm:ss", culture),
            "short" => when.ToString(date ? "d" : "t", culture),
            "long" => when.ToString(date ? "D" : "T", culture),
            "full" => when.ToString(date ? "D" : "T", culture),
            _ => when.ToString(style, culture),
        };
    }

    /// <summary>
    /// The culture a tag names, or the invariant one where .NET has never heard of it.
    /// </summary>
    /// <remarks>
    /// <see cref="Locales.CultureOf"/>'s cache rather than one of its own, so the same tag can't be
    /// answered differently in two places.
    /// </remarks>
    private static CultureInfo Culture(string tag) => Locales.CultureOf(tag);
}
