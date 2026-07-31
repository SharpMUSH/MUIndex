namespace MUI.Import;

/// <summary>
/// One directory we read.
/// </summary>
/// <remarks>
/// <para>
/// The abstraction the whole assembly is shaped around, so that adding a seventh directory is a class
/// rather than a rewrite: everything downstream — identity, the writers, the tier's history sink, the
/// report and the attribution list — consumes this and nothing narrower.
/// </para>
/// <para>
/// The tier is a property of the site, never of a call (spec §7.6). A source that pings is
/// <see cref="ImportTier.Measured"/> for everything it yields and one that does not is
/// <see cref="ImportTier.Asserted"/> for everything it yields.
/// </para>
/// </remarks>
public interface IDirectorySource
{
    string SourceName { get; }

    ImportTier Tier { get; }

    ImportEtiquette Etiquette { get; }

    IAsyncEnumerable<ImportedGame> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The shared plumbing every concrete source uses.
/// </summary>
/// <remarks>
/// It fetches through an <see cref="IDirectoryFetcher"/> and takes its etiquette <em>from</em> that
/// fetcher, so a source cannot be configured one way and fetch another — the etiquette a test asserts
/// on is provably the etiquette the request was made under.
/// </remarks>
public abstract class DirectorySource(IDirectoryFetcher fetcher) : IDirectorySource
{
    protected IDirectoryFetcher Fetcher { get; } = fetcher ?? throw new ArgumentNullException(nameof(fetcher));

    public abstract string SourceName { get; }

    public abstract ImportTier Tier { get; }

    public ImportEtiquette Etiquette => Fetcher.Etiquette;

    public abstract IAsyncEnumerable<ImportedGame> ReadAsync(CancellationToken cancellationToken);
}
