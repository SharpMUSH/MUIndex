using System.Net;

using Microsoft.AspNetCore.HttpOverrides;

namespace MUI.Web.Submissions;

/// <summary>
/// Where a request came from, and over what — when the answer decides a rate limit (spec §11) or
/// an absolute URL.
/// </summary>
/// <remarks>
/// <para>
/// <b>Behind a proxy, <c>RemoteIpAddress</c> is the proxy, and the bound becomes one bucket for the
/// whole internet.</b> That is the deployment this project actually has — one ASP.NET Core
/// deployable behind whatever terminates TLS — so the form's limit was, in that shape, five
/// submissions per hour for everybody at once. It is the failure that looks like the limit working.
/// </para>
/// <para>
/// <b>And trusting <c>X-Forwarded-For</c> unconditionally is strictly worse than not trusting it.</b>
/// A header anybody may set is an unlimited supply of buckets, handed to the one caller the limit
/// exists for. So this is off unless a deployment says how many proxies are in front of it, and the
/// framework's own <c>ForwardedHeaders</c> middleware — which walks exactly that many hops and no
/// more — is what does the walking. Nothing is inferred from the presence of a header.
/// </para>
/// <para>
/// <b>The scheme travels the same way, under the same gate.</b> That same proxy is what terminates
/// TLS, so <c>Request.Scheme</c> behind it is <c>http</c> — and every absolute URL the deployment
/// generates then names a scheme no reader used. <see cref="SiteUrls"/> is the reason this became
/// visible, but it was already wrong: the RSS feed's own self-link is built from the request, so a
/// deployed feed has been advertising <c>http://</c> URLs for itself. <c>X-Forwarded-Host</c> is
/// deliberately <em>not</em> taken — a forwarded host rewrites which site we claim to be, nothing
/// here needs it, and it is the header host-spoofing attacks are written against.
/// </para>
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
    /// <para>
    /// <see cref="ForwardedHeadersOptions.ForwardLimit"/> is the whole security property: with a
    /// limit of one, a client that sends its own <c>X-Forwarded-For</c> has that value overwritten
    /// by the address the proxy actually saw, because the proxy appends and the middleware takes the
    /// last hop. Without a limit it would walk the client's fabrication all the way back.
    /// </para>
    /// <para>
    /// <c>KnownIPNetworks</c> and <c>KnownProxies</c> are cleared because a container's gateway address
    /// is not knowable from here, and the hop count is the bound that does not depend on knowing it.
    /// A deployment that can name its proxies is welcome to a tighter answer; this one refuses to
    /// pretend it has one.
    /// </para>
    /// </remarks>
    public static void UseSubmitterAddress(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var hops = app.Configuration.GetValue(ProxyCountKey, 0);

        if (hops <= 0)
        {
            // Nothing said, so nothing trusted. The connection address is the truth, which is right
            // when there is no proxy and conservative when somebody forgot to say there was.
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
