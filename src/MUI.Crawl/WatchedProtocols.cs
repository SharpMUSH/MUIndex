using TelnetNegotiationCore.Protocols;

namespace MUI.Crawl;

/// <summary>
/// Protocol plugins that report the moment the server agrees to them.
/// </summary>
/// <remarks>
/// <para>
/// <c>OnEnabledAsync</c> fires when an option is actually negotiated, which is the fact layer 1
/// wants. The per-protocol callbacks are not a substitute: <c>OnGMCPMessage</c> fires when a
/// <em>message</em> arrives, so a server that supports GMCP and happens to say nothing during the
/// probe would go unrecorded — reported as not supporting a protocol it supports.
/// </para>
/// <para>
/// Each subclass exists only to observe. None changes behaviour, and every one calls its base, so
/// the negotiation the library performs is exactly the negotiation it would have performed anyway.
/// The name is taken from the plugin's own <c>ProtocolName</c> rather than a string here, so the
/// vocabulary cannot drift from the library's.
/// </para>
/// </remarks>
internal static class Watched
{
    internal sealed class Mssp(Action<string> note) : MSSPProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    internal sealed class Gmcp(Action<string> note) : GMCPProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    internal sealed class Msdp(Action<string> note) : MSDPProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    internal sealed class Charset(Action<string> note) : CharsetProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    /// <summary>MNES rides on NEW-ENVIRON, so agreeing to this option is the MNES handshake.</summary>
    internal sealed class NewEnviron(Action<string> note) : NewEnvironProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    internal sealed class Mccp(Action<string> note) : MCCPProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    internal sealed class Mxp(Action<string> note) : MXPProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    internal sealed class Eor(Action<string> note) : EORProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    internal sealed class SuppressGoAhead(Action<string> note) : SuppressGoAheadProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    internal sealed class Naws(Action<string> note) : NAWSProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    internal sealed class TerminalType(Action<string> note) : TerminalTypeProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }

    internal sealed class Echo(Action<string> note) : EchoProtocol
    {
        public override ValueTask OnEnabledAsync()
        {
            note(ProtocolName);
            return base.OnEnabledAsync();
        }
    }
}
