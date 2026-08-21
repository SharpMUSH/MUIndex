namespace MUI.Catalog.Tests;

/// <summary>
/// Spec §15.4 — the retention policy is an <b>open question</b>, so it is configuration, and the
/// defaults are what a project ships while a question is open.
/// </summary>
/// <remarks>
/// Same shape as the dataset licence (§15.2), for the same reason: a constant in the source settles
/// an intentionally open question by accident, and is wrong wherever a deployment answered it
/// differently. The difference is that this one deletes things.
/// </remarks>
public class PresenceRetentionTests
{
    [Test]
    public async Task TheDefaultDeletesNothingAtAnyGrain()
    {
        var options = new PresenceRetentionOptions();

        await Assert.That(options.RawSamples).IsNull();
        await Assert.That(options.HourlyRollups).IsNull();
        await Assert.That(options.DailyRollups).IsNull();
    }

    [Test]
    public async Task TheDesignedPolicyIsAvailableButNotAssumed()
    {
        // §5.2 states raw ninety days, hourly two years, daily for ever. Shipping that as the default
        // would answer §15.4 by writing it down twice; shipping it as a preset lets a deployment that
        // has measured its own storage turn it on in one line.
        var designed = PresenceRetentionOptions.AsDesigned;

        await Assert.That(designed.RawSamples).IsEqualTo(TimeSpan.FromDays(90));
        await Assert.That(designed.HourlyRollups).IsEqualTo(TimeSpan.FromDays(730));
        await Assert.That(designed.DailyRollups).IsNull();
    }

    [Test]
    public async Task PartitionsAreAlwaysMadeAheadOfNeed()
    {
        // Not a retention question and not optional: the raw table has no DEFAULT partition, so a
        // month without one loses every measurement taken in it.
        await Assert.That(new PresenceRetentionOptions().MonthsOfPartitionsAhead).IsGreaterThanOrEqualTo(1);

        await Assert.That(() => new PresenceRetentionOptions { MonthsOfPartitionsAhead = 0 }.Validate())
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ARetentionShorterThanAGraphTheSiteDrawsIsRefused()
    {
        // The heatmap reads an eight-week window (§5.2), so a deployment that asked to keep raw
        // samples for a fortnight would be asking for six weeks of every heatmap on the site to go
        // blank — and an empty cell may not be given a cause, so the page could not even explain it.
        await Assert.That(() =>
                new PresenceRetentionOptions { RawSamples = TimeSpan.FromDays(14) }.Validate())
            .Throws<ArgumentException>();

        await Assert.That(() =>
                new PresenceRetentionOptions { HourlyRollups = TimeSpan.FromDays(30) }.Validate())
            .Throws<ArgumentException>();

        // The daily grain is the one §5.2 keeps for ever, so its floor is a year rather than a window.
        await Assert.That(() =>
                new PresenceRetentionOptions { DailyRollups = TimeSpan.FromDays(90) }.Validate())
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task TheDesignedPolicyPassesItsOwnValidation()
    {
        PresenceRetentionOptions.AsDesigned.Validate();
        new PresenceRetentionOptions().Validate();

        await Assert.That(PresenceRetentionOptions.AsDesigned.RollupOverlap).IsGreaterThan(TimeSpan.Zero);
    }
}
