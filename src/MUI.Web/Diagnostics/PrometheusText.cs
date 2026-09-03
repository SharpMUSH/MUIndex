using System.Globalization;
using System.Text;

namespace MUI.Web.Diagnostics;

/// <summary>
/// The Prometheus text exposition format, written by hand.
/// </summary>
/// <remarks>
/// <para>
/// No package, for the same reason <c>ImageHeader</c> parses headers rather than taking an image
/// library: the format is a dozen lines of text and the alternatives are a prerelease
/// (OpenTelemetry's Prometheus exporter) or a third-party dependency on the public web project, both
/// carried for ever to serve one endpoint.
/// </para>
/// <para>
/// <b>One <c>HELP</c> and one <c>TYPE</c> per metric name, however many label sets follow.</b>
/// Prometheus treats a repeated declaration as a duplicate-metric error and rejects <em>the whole
/// scrape</em>, not the offending line — so a metric that grew a second label set would take every
/// other metric down with it, and the graph would look like a quiet day rather than like a broken
/// exporter. That failure mode is why this type tracks what it has already declared instead of
/// leaving it to each caller.
/// </para>
/// </remarks>
public sealed class PrometheusText
{
    private readonly StringBuilder _body = new();
    private readonly HashSet<string> _declared = new(StringComparer.Ordinal);

    /// <summary>A value read at scrape time: a heap size, a queue depth, a thread count.</summary>
    public void Gauge(string name, string help, double value, params (string Key, string Value)[] labels) =>
        Write(name, help, "gauge", value, labels);

    /// <summary>
    /// A total that only ever rises.
    /// </summary>
    /// <remarks>
    /// Declared as a counter rather than a gauge because Prometheus reads the type: <c>rate()</c>
    /// over something declared a gauge gives a wrong answer quietly rather than refusing.
    /// </remarks>
    public void Counter(string name, string help, double value, params (string Key, string Value)[] labels) =>
        Write(name, help, "counter", value, labels);

    private void Write(
        string name,
        string help,
        string type,
        double value,
        (string Key, string Value)[] labels)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(help);
        ArgumentNullException.ThrowIfNull(labels);

        if (_declared.Add(name))
        {
            _body.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            _body.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
        }

        _body.Append(name);

        if (labels.Length > 0)
        {
            _body.Append('{');

            for (var i = 0; i < labels.Length; i++)
            {
                if (i > 0)
                {
                    _body.Append(',');
                }

                _body.Append(labels[i].Key).Append("=\"");
                Escape(_body, labels[i].Value);
                _body.Append('"');
            }

            _body.Append('}');
        }

        // Invariant, always. A de-DE process writing `1,5` produces a scrape Prometheus rejects, and
        // it would only ever have failed in one deployment — see InvariantGlobalization in
        // Directory.Build.props for why this codebase cannot assume the invariant culture is current.
        _body.Append(' ')
            .Append(value.ToString("R", CultureInfo.InvariantCulture))
            .Append('\n');
    }

    /// <summary>
    /// The three escapes the format defines for a label value, and no others.
    /// </summary>
    /// <remarks>
    /// Backslash first: escaping it after the others would go back over the backslashes they just
    /// introduced and double them.
    /// </remarks>
    private static void Escape(StringBuilder into, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': into.Append(@"\\"); break;
                case '"': into.Append("\\\""); break;
                case '\n': into.Append("\\n"); break;
                default: into.Append(c); break;
            }
        }
    }

    /// <summary>The body, ending in the newline the format requires of its last line.</summary>
    public override string ToString() => _body.ToString();
}
