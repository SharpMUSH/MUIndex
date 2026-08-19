using System.Globalization;

namespace MUI.Web.Localization;

/// <summary>What an argument does with its value.</summary>
public enum ArgumentKind
{
    /// <summary><c>{name}</c> — substituted as it stands.</summary>
    None,

    Number,
    Date,
    Time,

    /// <summary><c>{n, plural, …}</c> — cardinal agreement.</summary>
    Plural,

    /// <summary><c>{n, selectordinal, …}</c> — ordinal agreement: 1st, 2nd, 3rd, 4th.</summary>
    SelectOrdinal,

    /// <summary><c>{gender, select, …}</c> — keyword match on a string.</summary>
    Select,
}

/// <summary>One piece of a parsed message.</summary>
public abstract record MessagePart;

/// <summary>Text, with every quote already resolved.</summary>
public sealed record LiteralPart(string Text) : MessagePart;

/// <summary>A bare <c>#</c> inside a plural branch: the count, less the offset.</summary>
public sealed record HashPart : MessagePart;

/// <summary>An argument, and whatever it selects between.</summary>
public sealed record ArgumentPart(
    string Name,
    ArgumentKind Kind,
    string? Style,
    long Offset,
    IReadOnlyDictionary<string, MessagePattern> Branches) : MessagePart;

/// <summary>
/// One ICU MessageFormat 1.0 pattern, parsed once and formattable many times.
/// </summary>
/// <remarks>
/// Parsed rather than interpreted so a broken pattern — an unused plural branch, a <c>select</c>
/// with no <c>other</c> — fails at startup (and in a test that walks every bundle) rather than only
/// for the one reader whose count happens to reach it. <c>choice</c> is refused: deprecated in ICU,
/// ambiguous with <c>plural</c>. Not MessageFormat 2.0 — no .NET implementation exists yet and the
/// translation tooling here speaks MF1.
/// </remarks>
public sealed record MessagePattern(IReadOnlyList<MessagePart> Parts)
{
    /// <summary>Parses a pattern, or throws saying where it stopped making sense.</summary>
    public static MessagePattern Parse(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var at = 0;
        var parsed = ParseMessage(pattern, ref at, inPlural: false, nested: false);

        if (at < pattern.Length)
        {
            throw new FormatException($"Unexpected '{pattern[at]}' at {at} in: {pattern}");
        }

        return parsed;
    }

    /// <summary>Every argument this pattern reads, and what it does with each — walks nested branches too.</summary>
    public IEnumerable<ArgumentPart> Arguments()
    {
        foreach (var part in Parts)
        {
            if (part is not ArgumentPart argument)
            {
                continue;
            }

            yield return argument;

            foreach (var branch in argument.Branches.Values)
            {
                foreach (var nested in branch.Arguments())
                {
                    yield return nested;
                }
            }
        }
    }

    private static MessagePattern ParseMessage(string s, ref int at, bool inPlural, bool nested)
    {
        var parts = new List<MessagePart>();
        var text = new System.Text.StringBuilder();

        void Flush()
        {
            if (text.Length > 0)
            {
                parts.Add(new LiteralPart(text.ToString()));
                text.Clear();
            }
        }

        while (at < s.Length)
        {
            var c = s[at];

            if (c == '}')
            {
                if (!nested)
                {
                    throw new FormatException($"Unmatched '}}' at {at} in: {s}");
                }

                break;
            }

            if (c == '\'')
            {
                Quote(s, ref at, inPlural, text);
                continue;
            }

            if (c == '{')
            {
                Flush();
                at++;
                parts.Add(ParseArgument(s, ref at));
                continue;
            }

            if (c == '#' && inPlural)
            {
                Flush();
                parts.Add(new HashPart());
                at++;
                continue;
            }

            text.Append(c);
            at++;
        }

        Flush();

        return new MessagePattern(parts);
    }

    /// <summary>
    /// ICU's apostrophe rules, in the DOUBLE_OPTIONAL mode ICU uses by default.
    /// </summary>
    /// <remarks>
    /// An apostrophe only starts quoted text when it immediately precedes <c>{</c>, <c>}</c>,
    /// <c>|</c>, or <c>#</c> inside a plural — elsewhere it's literal, so <c>doesn't</c> needs no
    /// escaping. A doubled apostrophe is always one literal apostrophe.
    /// </remarks>
    private static void Quote(string s, ref int at, bool inPlural, System.Text.StringBuilder text)
    {
        // '' — one literal apostrophe, quoting or not.
        if (at + 1 < s.Length && s[at + 1] == '\'')
        {
            text.Append('\'');
            at += 2;
            return;
        }

        var starts = at + 1 < s.Length
            && (s[at + 1] is '{' or '}' or '|' || (inPlural && s[at + 1] == '#'));

        if (!starts)
        {
            text.Append('\'');
            at++;
            return;
        }

        at++;

        while (at < s.Length)
        {
            if (s[at] == '\'')
            {
                // Inside a quote, '' is still one apostrophe; a lone one closes.
                if (at + 1 < s.Length && s[at + 1] == '\'')
                {
                    text.Append('\'');
                    at += 2;
                    continue;
                }

                at++;
                return;
            }

            text.Append(s[at]);
            at++;
        }

        // ICU closes an unterminated quote at the end of the pattern rather than erroring.
    }

    private static ArgumentPart ParseArgument(string s, ref int at)
    {
        Skip(s, ref at);

        var name = Name(s, ref at);

        Skip(s, ref at);

        if (Take(s, ref at, '}'))
        {
            return new ArgumentPart(name, ArgumentKind.None, null, 0, EmptyBranches);
        }

        Expect(s, ref at, ',', name);
        Skip(s, ref at);

        var type = Word(s, ref at, "an argument type");

        Skip(s, ref at);

        var kind = type switch
        {
            "number" => ArgumentKind.Number,
            "date" => ArgumentKind.Date,
            "time" => ArgumentKind.Time,
            "plural" => ArgumentKind.Plural,
            "selectordinal" => ArgumentKind.SelectOrdinal,
            "select" => ArgumentKind.Select,

            // Deprecated in ICU and ambiguous with plural; every use is better written as one.
            "choice" => throw new FormatException(
                $"'choice' is deprecated in ICU and is not supported — use plural or select: {s}"),

            _ => throw new FormatException($"Unknown argument type '{type}' in: {s}"),
        };

        if (Take(s, ref at, '}'))
        {
            // {n, number} and {d, date} are legal with no style.
            return kind is ArgumentKind.Number or ArgumentKind.Date or ArgumentKind.Time
                ? new ArgumentPart(name, kind, null, 0, EmptyBranches)
                : throw new FormatException($"'{type}' needs branches in: {s}");
        }

        Expect(s, ref at, ',', type);

        if (kind is ArgumentKind.Number or ArgumentKind.Date or ArgumentKind.Time)
        {
            return new ArgumentPart(name, kind, Style(s, ref at), 0, EmptyBranches);
        }

        var offset = 0L;

        if (kind is not ArgumentKind.Select)
        {
            Skip(s, ref at);

            if (s.AsSpan(at).StartsWith("offset:", StringComparison.Ordinal))
            {
                at += "offset:".Length;
                Skip(s, ref at);

                var digits = Word(s, ref at, "an offset");

                if (!long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out offset))
                {
                    throw new FormatException($"'{digits}' is not an offset in: {s}");
                }
            }
        }

        var branches = ParseBranches(s, ref at, kind);

        // Every selector needs a fallback, and ICU says so: without one a value nobody anticipated
        // has no rendering at all. Refused here rather than at render time, so the message that is
        // missing it fails a build rather than one reader's page.
        if (!branches.ContainsKey("other"))
        {
            throw new FormatException($"'{name}' has no 'other' branch in: {s}");
        }

        return new ArgumentPart(name, kind, null, offset, branches);
    }

    private static Dictionary<string, MessagePattern> ParseBranches(string s, ref int at, ArgumentKind kind)
    {
        var branches = new Dictionary<string, MessagePattern>(StringComparer.Ordinal);
        var plural = kind is ArgumentKind.Plural or ArgumentKind.SelectOrdinal;

        while (true)
        {
            Skip(s, ref at);

            if (at >= s.Length)
            {
                throw new FormatException($"Unclosed branch list in: {s}");
            }

            if (Take(s, ref at, '}'))
            {
                return branches;
            }

            var selector = Selector(s, ref at);

            if (plural && !selector.StartsWith('=') && !PluralRules.IsCategory(selector))
            {
                // A keyword no category uses is dead: nothing ever selects it, in any locale, and
                // it is almost always a typo for one that would have.
                throw new FormatException(
                    $"'{selector}' is not a plural category and not an '=' match in: {s}");
            }

            Skip(s, ref at);
            Expect(s, ref at, '{', selector);

            branches[selector] = ParseMessage(s, ref at, plural, nested: true);

            Expect(s, ref at, '}', selector);
        }
    }

    /// <summary>A style, which runs to the argument's closing brace and may contain quotes.</summary>
    private static string Style(string s, ref int at)
    {
        var text = new System.Text.StringBuilder();

        while (at < s.Length && s[at] != '}')
        {
            if (s[at] == '\'')
            {
                Quote(s, ref at, inPlural: false, text);
                continue;
            }

            text.Append(s[at]);
            at++;
        }

        Expect(s, ref at, '}', "a style");

        return text.ToString().Trim();
    }

    private static readonly IReadOnlyDictionary<string, MessagePattern> EmptyBranches =
        new Dictionary<string, MessagePattern>(StringComparer.Ordinal);

    private static string Name(string s, ref int at) => Word(s, ref at, "an argument name");

    /// <summary>
    /// A branch selector: a category keyword, or <c>=</c> and the number it matches exactly.
    /// </summary>
    /// <remarks>
    /// A bare <c>=</c> must be a parse error, not a selector: without this check it was accepted
    /// and stored a branch under a key no number could ever match — a dead branch nothing selects.
    /// A leading sign and a fraction are legal after <c>=</c>; a word is not.
    /// </remarks>
    private static string Selector(string s, ref int at)
    {
        var start = at;

        if (at < s.Length && s[at] == '=')
        {
            at++;

            if (at < s.Length && s[at] == '-')
            {
                at++;
            }

            var digits = at;

            while (at < s.Length && char.IsAsciiDigit(s[at]))
            {
                at++;
            }

            if (at == digits)
            {
                throw new FormatException($"Expected a number after '=' at {digits} in: {s}");
            }

            if (at < s.Length && s[at] == '.')
            {
                at++;

                var fraction = at;

                while (at < s.Length && char.IsAsciiDigit(s[at]))
                {
                    at++;
                }

                if (at == fraction)
                {
                    throw new FormatException($"Expected a fraction after '.' at {fraction} in: {s}");
                }
            }

            return s[start..at];
        }

        while (at < s.Length && (char.IsLetterOrDigit(s[at]) || s[at] is '_' or '-' or '.'))
        {
            at++;
        }

        return at > start
            ? s[start..at]
            : throw new FormatException($"Expected a selector at {start} in: {s}");
    }

    private static string Word(string s, ref int at, string what)
    {
        Skip(s, ref at);

        var start = at;

        while (at < s.Length && (char.IsLetterOrDigit(s[at]) || s[at] is '_' or '-' or '.' or '+'))
        {
            at++;
        }

        return at > start
            ? s[start..at]
            : throw new FormatException($"Expected {what} at {start} in: {s}");
    }

    private static void Skip(string s, ref int at)
    {
        while (at < s.Length && char.IsWhiteSpace(s[at]))
        {
            at++;
        }
    }

    private static bool Take(string s, ref int at, char c)
    {
        Skip(s, ref at);

        if (at < s.Length && s[at] == c)
        {
            at++;
            return true;
        }

        return false;
    }

    private static void Expect(string s, ref int at, char c, string after)
    {
        if (!Take(s, ref at, c))
        {
            throw new FormatException($"Expected '{c}' after '{after}' at {at} in: {s}");
        }
    }
}
