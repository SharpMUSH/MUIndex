namespace MUI.Catalog.Persistence;

/// <summary>
/// The one place a C# enum becomes a schema string and back.
/// </summary>
/// <remarks>
/// Every one of these spellings is also written into a <c>CHECK</c> constraint in
/// <c>migrations/</c>, so a member added on one side and forgotten on the other fails at the
/// database rather than becoming a value every reader downstream has to cope with. The vocabulary
/// tests walk both directions to prove the two halves still agree.
/// </remarks>
public static class SqlEnums
{
    public static string ToDb(FieldSource source) => source switch
    {
        FieldSource.Staff => "staff",
        FieldSource.Handshake => "handshake",
        FieldSource.Owner => "owner",
        FieldSource.Who => "who",
        FieldSource.Mssp => "mssp",
        FieldSource.Banner => "banner",
        _ => throw Unmapped(source),
    };

    public static FieldSource ToFieldSource(string value) => value switch
    {
        "staff" => FieldSource.Staff,
        "handshake" => FieldSource.Handshake,
        "owner" => FieldSource.Owner,
        "who" => FieldSource.Who,
        "mssp" => FieldSource.Mssp,
        "banner" => FieldSource.Banner,
        _ => throw Unread(value, nameof(FieldSource)),
    };

    public static string ToDb(AvailabilityState state) => state switch
    {
        // Reachable, never up (spec §5.8). We measured a socket from one vantage point; we did not
        // measure whether the game was up, and "up" claims we did.
        AvailabilityState.Reachable => "reachable",
        AvailabilityState.Degraded => "degraded",
        AvailabilityState.Unreachable => "unreachable",
        _ => throw Unmapped(state),
    };

    public static AvailabilityState ToAvailabilityState(string value) => value switch
    {
        "reachable" => AvailabilityState.Reachable,
        "degraded" => AvailabilityState.Degraded,
        "unreachable" => AvailabilityState.Unreachable,
        _ => throw Unread(value, nameof(AvailabilityState)),
    };

    public static string ToDb(FailureCause cause) => cause switch
    {
        // 'none' is the cause a reachable interval carries. It is never a probe's answer, and it is
        // emphatically not "we failed and do not know why".
        FailureCause.None => "none",
        FailureCause.Dns => "dns",
        FailureCause.Refused => "refused",
        FailureCause.Tls => "tls",
        FailureCause.Timeout => "timeout",
        FailureCause.HandshakeStalled => "handshake_stalled",
        _ => throw Unmapped(cause),
    };

    public static FailureCause ToFailureCause(string value) => value switch
    {
        "none" => FailureCause.None,
        "dns" => FailureCause.Dns,
        "refused" => FailureCause.Refused,
        "tls" => FailureCause.Tls,
        "timeout" => FailureCause.Timeout,
        "handshake_stalled" => FailureCause.HandshakeStalled,
        _ => throw Unread(value, nameof(FailureCause)),
    };

    public static string ToDb(LifecycleState state) => state switch
    {
        LifecycleState.Active => "active",
        LifecycleState.Quiet => "quiet",
        LifecycleState.Dark => "dark",
        LifecycleState.Archived => "archived",
        _ => throw Unmapped(state),
    };

    public static LifecycleState ToLifecycleState(string value) => value switch
    {
        "active" => LifecycleState.Active,
        "quiet" => LifecycleState.Quiet,
        "dark" => LifecycleState.Dark,
        "archived" => LifecycleState.Archived,
        _ => throw Unread(value, nameof(LifecycleState)),
    };

    public static string ToDb(UnmeasurableReason reason) => reason switch
    {
        UnmeasurableReason.WhoUnparseable => "who_unparseable",
        UnmeasurableReason.WhoNotOffered => "who_not_offered",
        UnmeasurableReason.PlayersNotNumeric => "players_not_numeric",
        _ => throw Unmapped(reason),
    };

    public static UnmeasurableReason ToUnmeasurableReason(string value) => value switch
    {
        "who_unparseable" => UnmeasurableReason.WhoUnparseable,
        "who_not_offered" => UnmeasurableReason.WhoNotOffered,
        "players_not_numeric" => UnmeasurableReason.PlayersNotNumeric,
        _ => throw Unread(value, nameof(UnmeasurableReason)),
    };

    public static string ToDb(EndpointKind kind) => kind switch
    {
        EndpointKind.Telnet => "telnet",
        EndpointKind.Tls => "tls",
        EndpointKind.WebSocket => "websocket",
        EndpointKind.Http => "http",
        _ => throw Unmapped(kind),
    };

    public static EndpointKind ToEndpointKind(string value) => value switch
    {
        "telnet" => EndpointKind.Telnet,
        "tls" => EndpointKind.Tls,
        "websocket" => EndpointKind.WebSocket,
        "http" => EndpointKind.Http,
        _ => throw Unread(value, nameof(EndpointKind)),
    };

    public static string ToDb(EndpointState state) => state switch
    {
        EndpointState.Active => "active",
        EndpointState.Stale => "stale",
        EndpointState.Gone => "gone",
        _ => throw Unmapped(state),
    };

    public static EndpointState ToEndpointState(string value) => value switch
    {
        "active" => EndpointState.Active,
        "stale" => EndpointState.Stale,
        "gone" => EndpointState.Gone,
        _ => throw Unread(value, nameof(EndpointState)),
    };

    public static string ToDb(IntervalOrigin origin) => origin switch
    {
        IntervalOrigin.FirstParty => "first_party",
        _ => throw Unmapped(origin),
    };

    public static IntervalOrigin ToIntervalOrigin(string value) => value switch
    {
        "first_party" => IntervalOrigin.FirstParty,
        _ => throw Unread(value, nameof(IntervalOrigin)),
    };

    private static ArgumentOutOfRangeException Unmapped<T>(T value) where T : struct, Enum =>
        new(nameof(value), value, $"{typeof(T).Name}.{value} has no schema spelling.");

    private static InvalidOperationException Unread(string value, string type) =>
        new($"'{value}' is not a {type} this schema declares.");
}
