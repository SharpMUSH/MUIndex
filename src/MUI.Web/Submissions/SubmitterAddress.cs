using System.Net;

using Microsoft.AspNetCore.HttpOverrides;

namespace MUI.Web.Submissions;

/// <summary>
/// Where a request came from, and over what — when the answer decides a rate limit (spec §11) or
/// an absolute URL.
/// </summary>
/// <remarks>
/// <b>Behind a proxy, <c>RemoteIpAddress</c> is the proxy</b>, collapsing the submission rate limit
/// into one shared bucket for the whole internet — unless forwarded headers are unwound first.
/// <b>Trusting <c>X-Forwarded-For</c> unconditionally is worse than not trusting it</b>: a
/// client-settable header is an unlimited supply of buckets. So it's off unless a deployment states
/// its proxy hop count, and the framework's <c>ForwardedHeaders</c> middleware walks exactly that
/// many hops and no more.
/// <b>The scheme travels the same way, under the same gate</b> — behind a TLS-terminating proxy,
/// <c>Request.Scheme</c> is <c>http</c>, so every generated absolute URL would name the wrong scheme.
/// <c>X-Forwarded-Host</c> is deliberately not taken: nothing here needs it, and it is the header
/// host-spoofing attacks are written against.
/// </remarks>
public static class SubmitterAddress
{
    /// <summary>
    /// How many trusted reverse proxies sit in front of this deployment. Zero means none, and
    /// forwarded headers are then ignored entirely.
    /// </summary>
    public const string ProxyCountKey = "Submissions:TrustedProxyHops";

    /// <summary>Where a request came from, after any configured proxy hops have been unwound.</summary>
    public static IPAddress? Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Connection.RemoteIpAddress;
    }

    /// <summary>
    /// Wires forwarded-header handling, or deliberately does not.
    /// </summary>
    /// <remarks>
    /// <see cref="ForwardedHeadersOptions.ForwardLimit"/> is the whole security property: with a limit
    /// of one, the middleware takes the last-appended hop, overwriting any <c>X-Forwarded-For</c> value
    /// a client tried to fabricate. <c>KnownIPNetworks</c>/<c>KnownProxies</c> are cleared because a
    /// container's gateway address isn't knowable from here; the hop count is the bound that doesn't
    /// depend on knowing it.
    /// </remarks>
    public static void UseSubmitterAddress(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var hops = app.Configuration.GetValue(ProxyCountKey, 0);

        if (hops <= 0)
        {
            // Nothing said, so nothing trusted — conservative when somebody forgot to say there was one.
            return;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = hops,
        };

        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        app.UseForwardedHeaders(options);
    }
}
