using System.Globalization;
using System.Text;

namespace MUI.Web.Localization;

/// <summary>
/// One ICU MessageFormat message, rendered.
/// </summary>
/// <remarks>
/// <para>
/// <b>The subset, and why it is a subset.</b> Three constructs carry every string on this site:
/// argument substitution (<c>{value}</c>), <c>plural</c> with <c>#</c>, and <c>select</c>. Dates go
/// through <see cref="Components.Dates"/> and counts stay in Western digits by policy, so
/// <c>date</c>, <c>time</c> and <c>number</c> skeletons would be machinery nothing calls — and a
/// formatter that accepts syntax it does not implement is worse than one that refuses it, because
/// the failure arrives as a wrong string rather than as a build error. Unknown constructs throw.
/// </para>
/// <para>
/// <b>Written here rather than taken from a package.</b> The rules are small, the site commits to
/// nine locales, and this file is checkable against the CLDR chart in an afternoon. The same
/// argument the stylesheet makes about utility frameworks: a dependency would bury the discipline
/// that makes it work.
/// </para>
/// <para>
/// <b>What it is for.</b> Not tidiness — <c>545 on</c>, <c>23 games</c> and
/// <c>7 days measured · 168 probes</c> each glue a number to an English fragment in English word
/// order, and there is nowhere in that for a translator to intervene without editing markup. One
/// message per fact, the count as a named argument, and the plural clause written out in full is
/// what gives them somewhere to stand.
/// </para>
/// </remarks>
public static class IcuMessage
{
    /// <summary>Renders <paramref name="pattern"/> for <paramref name="tag"/>.</summary>
    /// <exception cref="FormatException">The pattern is malformed or uses unimplemented syntax.</exception>
    public static string Format(
        string pattern,
        string tag,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(tag);

        var b = new StringBuilder(pattern.Length);
        var at = 0;

        Render(pattern, ref at, tag, arguments ?? EmptyArguments, b, plural: null, stop: '\0');

        return b.ToString();
    }

    private static readonly Dictionary<string, object?> EmptyArguments = [];

    /// <summary>
    /// Renders until <paramref name="stop"/> or the end, appending into <paramref name="b"/>.
    /// </summary>
    /// <param name="plural">
    /// The number a bare <c>#</c> stands for, or null outside a plural branch — where <c>#</c> is a
    /// literal, exactly as ICU says it is.
    /// </param>
    private static void Render(
        string s,
        ref int at,
        string tag,
        IReadOnlyDictionary<string, object?> args,
        StringBuilder b,
        int? plural,
        char stop)
    {
        while (at < s.Length)
        {
            var c = s[at];

            if (c == stop)
            {
                return;
            }

            switch (c)
            {
                case '{':
                    at++;
                    Argument(s, ref at, tag, args, b, plural);
                    continue;

                case '#' when plural is { } n:
                    b.Append(n.ToString(CultureInfo.InvariantCulture));
                    at++;
                    continue;

                default:
                    b.Append(c);
                    at++;
                    continue;
            }
        }

        if (stop != '\0')
        {
            throw new FormatException($"Unclosed '{stop}' in message: {s}");
        }
    }

    /// <summary>One <c>{...}</c>, with <c>at</c> just past the opening brace.</summary>
    private static void Argument(
        string s,
        ref int at,
        string tag,
        IReadOnlyDictionary<string, object?> args,
        StringBuilder b,
        int? plural)
    {
        var name = Word(s, ref at);

        Skip(s, ref at);

        // {value} — the whole of it.
        if (Take(s, ref at, '}'))
        {
            b.Append(Lookup(args, name, s));
            return;
        }

        if (!Take(s, ref at, ','))
        {
            throw new FormatException($"Expected ',' or '}}' after '{name}' in: {s}");
        }

        Skip(s, ref at);

        var kind = Word(s, ref at);

        Skip(s, ref at);

        if (!Take(s, ref at, ','))
        {
            throw new FormatException($"Expected ',' after '{kind}' in: {s}");
        }

        switch (kind)
        {
            case "plural":
                Plural(s, ref at, tag, args, b, name);
                return;

            case "select":
                Select(s, ref at, tag, args, b, name, plural);
                return;

            default:
                // Better a build that stops than a string that is quietly wrong. See the class
                // remarks: the subset is the point.
                throw new FormatException(
                    $"'{kind}' is not implemented — this formatter carries plural and select only: {s}");
        }
    }

    private static void Plural(
        string s,
        ref int at,
        string tag,
        IReadOnlyDictionary<string, object?> args,
        StringBuilder b,
        string name)
    {
        var value = Lookup(args, name, s);

        if (value is not int count)
        {
            throw new FormatException($"'{name}' is a plural argument and must be an int: {s}");
        }

        var wanted = PluralRules.Keyword(PluralRules.Of(tag, count));
        var branches = Branches(s, ref at);

        // `=0` and friends win over a category, which is what lets a message say "no games" for
        // nothing and "1 game" for one without inventing a category CLDR does not have.
        var body = branches.GetValueOrDefault($"={count}")
            ?? branches.GetValueOrDefault(wanted)
            ?? branches.GetValueOrDefault("other")
            ?? throw new FormatException($"'{name}' has no '{wanted}' branch and no 'other': {s}");

        var inner = 0;
        Render(body, ref inner, tag, args, b, count, '\0');
    }

    private static void Select(
        string s,
        ref int at,
        string tag,
        IReadOnlyDictionary<string, object?> args,
        StringBuilder b,
        string name,
        int? plural)
    {
        var chosen = Convert.ToString(Lookup(args, name, s), CultureInfo.InvariantCulture) ?? string.Empty;
        var branches = Branches(s, ref at);

        var body = branches.GetValueOrDefault(chosen)
            ?? branches.GetValueOrDefault("other")
            ?? throw new FormatException($"'{name}' has no '{chosen}' branch and no 'other': {s}");

        var inner = 0;
        Render(body, ref inner, tag, args, b, plural, '\0');
    }

    /// <summary>Reads <c>key {body}</c> pairs up to the closing brace of the argument.</summary>
    private static Dictionary<string, string> Branches(string s, ref int at)
    {
        var branches = new Dictionary<string, string>(StringComparer.Ordinal);

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

            var key = Word(s, ref at);

            Skip(s, ref at);

            if (!Take(s, ref at, '{'))
            {
                throw new FormatException($"Expected '{{' after branch '{key}' in: {s}");
            }

            branches[key] = Balanced(s, ref at);
        }
    }

    /// <summary>
    /// The text of one branch, with <c>at</c> just past its opening brace.
    /// </summary>
    /// <remarks>
    /// Counted rather than scanned to the first <c>}</c>, because a branch legitimately contains
    /// nested arguments — <c>other {# games, {state}}</c> — and stopping at the first closing brace
    /// would cut the branch in half and leave the parser reading the remainder as a key.
    /// </remarks>
    private static string Balanced(string s, ref int at)
    {
        var start = at;
        var depth = 1;

        while (at < s.Length)
        {
            if (s[at] == '{') { depth++; }
            else if (s[at] == '}' && --depth == 0) { return s[start..at++]; }

            at++;
        }

        throw new FormatException($"Unclosed branch in: {s}");
    }

    private static object? Lookup(IReadOnlyDictionary<string, object?> args, string name, string s) =>
        args.TryGetValue(name, out var value)
            ? value
            : throw new FormatException($"No argument named '{name}' for: {s}");

    private static string Word(string s, ref int at)
    {
        Skip(s, ref at);

        var start = at;

        while (at < s.Length && (char.IsLetterOrDigit(s[at]) || s[at] is '_' or '.' or '=' or '-'))
        {
            at++;
        }

        if (at == start)
        {
            throw new FormatException($"Expected a name at {start} in: {s}");
        }

        return s[start..at];
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

    /// <summary>
    /// Every plural argument in a pattern, and the branch keywords it declares.
    /// </summary>
    /// <remarks>
    /// Read by the completeness check rather than by rendering. It is a scan and not a parse on
    /// purpose: it has to answer for a message in <em>any</em> locale's bundle, including one whose
    /// branches are wrong, and a parser that threw on the malformed case could not report it.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PluralBranches(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var found = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var at = 0;

        while ((at = pattern.IndexOf(", plural,", at, StringComparison.Ordinal)) >= 0)
        {
            var open = pattern.LastIndexOf('{', at);

            if (open < 0)
            {
                break;
            }

            var nameAt = open + 1;
            var name = Word(pattern, ref nameAt);

            at += ", plural,".Length;

            var keys = new List<string>();
            var scan = at;
            var depth = 1;

            while (scan < pattern.Length && depth > 0)
            {
                var c = pattern[scan];

                if (c == '{')
                {
                    depth++;

                    // The key is the word immediately before this brace.
                    var back = scan - 1;
                    while (back >= 0 && char.IsWhiteSpace(pattern[back])) { back--; }

                    var end = back + 1;
                    while (back >= 0 && (char.IsLetterOrDigit(pattern[back]) || pattern[back] is '=' or '_')) { back--; }

                    if (depth == 2 && end > back + 1)
                    {
                        keys.Add(pattern[(back + 1)..end]);
                    }
                }
                else if (c == '}')
                {
                    depth--;
                }

                scan++;
            }

            found[name] = keys;
        }

        return found;
    }
}
