namespace MUI.Web.Components;

/// <summary>
/// Which of the three liveness feeds an entry belongs to (spec §9).
/// </summary>
/// <remarks>
/// The register is a property of the feed, not of the entry: <see cref="FeedEntry"/> is the same
/// record in all three, and a card cannot infer from one which of the three it is in. Naming the
/// kind at the call site is what stops "came back" being guessed from a date.
/// </remarks>
public enum FeedKind
{
    NewlyDiscovered,
    WentDark,
    CameBack,
}
