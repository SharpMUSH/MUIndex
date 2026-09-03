namespace MUI.Crawler;

/// <summary>
/// Something that wants to be told what each crawl cycle did.
/// </summary>
/// <remarks>
/// <para>
/// An interface here rather than a reference to whoever implements it, because the arrow only goes
/// one way: the crawl loop must not know a web tier exists, for the same reason
/// <c>MUI.Catalog</c> must not know a socket does. Today the implementation is <c>MUI.Web</c>'s
/// metrics counters; the loop cannot see that and must not.
/// </para>
/// <para>
/// <b>Told, not asked.</b> An implementation is handed the report the cycle already produced rather
/// than reaching into the loop for figures of its own — the report is the cycle's own account of
/// itself, and a second count taken elsewhere could disagree with it and then have to be reconciled
/// against a run that is already over.
/// </para>
/// <para>
/// An implementation must not throw and must not block. This is telemetry about a crawl that has
/// already been stored, and the loop calls it on the path that decides when the next cycle starts.
/// </para>
/// </remarks>
public interface ICycleObserver
{
    void Observe(CycleReport report);
}
