using TelnetNegotiationCore.Protocols;

namespace MUI.Crawl;

/// <summary>
/// Protocol plugins that report the moment the server agrees to them.
/// </summary>
/// <remarks>
/// <c>OnEnabledAsync</c> fires when an option is actually negotiated, which is the layer-1 fact we
/// want. The per-protocol message callbacks are not a substitute — <c>OnGMCPMessage</c> only fires
/// when a message arrives, so a server that supports GMCP but says nothing during the probe would go
/// unrecorded. Each subclass only observes; every one calls its base so negotiation is unaffected.
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
