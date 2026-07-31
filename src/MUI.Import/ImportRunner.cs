namespace MUI.Import;

/// <summary>How a backfill run was asked for.</summary>
public sealed record ImportRunOptions
{
    /// <summary>Read and report; write nothing. The default, deliberately.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Restrict the run to these source names. Empty means every registered source.</summary>
    public IReadOnlyList<string> Only { get; init; } = [];
}

/// <summary>What a whole backfill did, source by source.</summary>
public sealed record ImportRun(
    bool DryRun,
    IReadOnlyList<ImportReport> Reports,
    IReadOnlyList<string> Skipped)
{
    public IEnumerable<string> Lines()
    {
        yield return DryRun ? "Dry run — nothing was written." : "Committing run.";

        foreach (var report in Reports)
        {
            yield return report.ToString();

            foreach (var note in report.Notes)
            {
                yield return $"  · {note}";
            }
        }

        foreach (var skipped in Skipped)
        {
            yield return $"skipped: {skipped}";
        }
    }
}

/// <summary>
/// Runs the registered sources through the pipeline, one at a time.
/// </summary>
/// <remarks>
/// One at a time on purpose. Two directories fetched concurrently is twice the traffic arriving at
/// two hobbyists' servers for the sake of a one-off job that has all the time in the world, and each
/// source's rate limit only governs its own fetches.
/// </remarks>
public sealed class ImportRunner(SourceRegistry registry, ImportPipeline pipeline)
{
    private readonly SourceRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly ImportPipeline _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

    public async Task<ImportRun> RunAsync(ImportRunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var reports = new List<ImportReport>();
        var skipped = new List<string>();

        foreach (var (source, decision) in _registry.Plan())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.Only.Count > 0
                && !options.Only.Contains(source.SourceName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            // A source with no permitted route is skipped and said out loud, never attempted. This is
            // where the contacted-maintainer gate is felt: a scrape-only directory nobody has emailed
            // reports why it did not run instead of running.
            if (decision.Route is FetchRoute.None)
            {
                skipped.Add($"{source.SourceName} — {decision.RefusedReason}");
                continue;
            }

            var report = options.DryRun
                ? await _pipeline.DryRunAsync(source, cancellationToken).ConfigureAwait(false)
                : await _pipeline.RunAsync(source, cancellationToken).ConfigureAwait(false);

            reports.Add(report);
        }

        return new ImportRun(options.DryRun, reports, skipped);
    }
}
