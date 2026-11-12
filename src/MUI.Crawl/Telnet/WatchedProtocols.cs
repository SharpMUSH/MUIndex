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

        /// <summary>
        /// The real signal for MSSP specifically, unlike every other protocol here. TNC's own
        /// <c>ConfigureStateMachine</c> for <c>MSSPProtocol</c> calls <c>OnNegotiatedAsync(true)</c>
        /// from <c>OnWillMSSPAsync</c> — the moment the peer's actual <c>WILL MSSP</c> arrives, before
        /// we have even sent <c>DO MSSP</c> back — so this fires on the real exchange, not on plugin
        /// construction the way <see cref="OnEnabledAsync"/> above does (verified against the pinned
        /// package's decompiled IL, not assumed: <c>OnEnabledAsync</c> is reachable in this library
        /// only through <c>ProtocolPluginManager.EnablePluginAsync</c>, which nothing here calls, so it
        /// never fires at all in practice). Noted under the literal capability name, not
        /// <see cref="MSSPProtocol.ProtocolName"/> — <see cref="OnEnabledAsync"/>'s <c>note(ProtocolName)</c>
        /// above would write the long descriptive string instead and mint a second, wrong field if it
        /// ever ran, which is one more reason it staying dead is not a loss.
        /// </summary>
        protected override ValueTask OnNegotiationChangedAsync(bool isNegotiated)
        {
            if (isNegotiated)
            {
                note("MSSP");
            }

            return base.OnNegotiationChangedAsync(isNegotiated);
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

    /// <remarks>
    /// The one plugin here that also answers. A server's <c>&lt;SUPPORT&gt;</c> is a question asked of
    /// this client, and CoffeeMud holds its connect screen for three seconds waiting for the reply
    /// (<c>DefaultSession.HandshakeCallback</c>, <c>HANDSHAKE_MXPPAUSE</c>). Unanswered, the probe
    /// settled on the questions themselves, stored them as the game's connect screen, and read the
    /// real screen into the WHO phase. The answer is the truth: this client renders nothing, so every
    /// entry asked about is refused, and a bare <c>&lt;SUPPORT&gt;</c> gets a reply that claims nothing.
    /// It goes out on a secure line, which a server reads as protocol rather than as typing.
    /// <para>
    /// Entities are decoded too. A server in MXP mode must escape <c>"</c>, <c>&amp;</c>, <c>&lt;</c>
    /// and <c>&gt;</c> in its text, and CoffeeMud does from the moment option 91 is agreed, so a client
    /// that renders nothing stored <c>&amp;quot;</c> wherever the game had printed a quote
    /// (CoffeeMud Hardcore's connect screen, 2026-11-03). Only after MXP mode starts; before that, an
    /// entity is text the game meant.
    /// </para>
    /// </remarks>
    internal sealed class Mxp : MXPProtocol
    {
        private readonly Action<string> _note;

        public Mxp(Action<string> note)
        {
            _note = note;
            OnMxpSupportRequested(asked => SendSupportsAsync([], asked));
            DecodeEntities();
        }

        public override ValueTask OnEnabledAsync()
        {
            _note(ProtocolName);
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

        /// <summary>
        /// Reports <see cref="TerminalWindow"/> the moment the game enables NAWS with <c>DO NAWS</c>.
        /// </summary>
        /// <remarks>
        /// Only then: an <c>SB NAWS</c> the game never asked for desyncs a strict parser (RFC 1073),
        /// and <see cref="NAWSProtocol.SendWindowSizeAsync"/> itself sends nothing before the game has
        /// agreed. A game that answers <c>DONT NAWS</c> is told nothing. Transition-only in the base,
        /// so a repeated <c>DO</c> does not repeat the report.
        /// </remarks>
        protected override async ValueTask OnNegotiationChangedAsync(bool isNegotiated)
        {
            await base.OnNegotiationChangedAsync(isNegotiated);

            if (isNegotiated && WindowSizeReportingEnabled)
            {
                await SendWindowSizeAsync(TerminalWindow.Columns, TerminalWindow.Rows);
            }
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
