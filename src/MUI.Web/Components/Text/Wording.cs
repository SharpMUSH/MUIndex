using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>Machine vocabulary said the way a person would say it.</summary>
public static class Wording
{
    /// <summary>
    /// Why a dial did not complete, in the reader's language.
    /// </summary>
    /// <remarks>
    /// What our socket saw, never a judgement of the game: "connection refused" is an event on a
    /// wire, not "the game was down".
    /// </remarks>
    public static string Cause(string tag, FailureCause cause) => Messages.For(tag, cause switch
    {
        FailureCause.Dns => "cause.dns",
        FailureCause.Refused => "cause.refused",
        FailureCause.Tls => "cause.tls",
        FailureCause.Timeout => "cause.timeout",
        FailureCause.HandshakeStalled => "cause.handshakeStalled",
        FailureCause.NoRoute => "cause.noRoute",
        _ => "cause.none",
    });

    /// <summary>
    /// A fraction as a percentage. Hand-formatted because <c>P1</c> under an invariant culture puts
    /// a space before the sign, and "96.7 %" reads as a typo in a sentence.
    /// </summary>
    public static string Percent(double fraction) => $"{fraction * 100:0.0}%";

    public static string Duration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            var hours = span.Hours;
            return hours == 0 ? $"{(int)span.TotalDays}d" : $"{(int)span.TotalDays}d {hours}h";
        }

        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h" : $"{(int)span.TotalMinutes}m";
    }
}
