using System.Globalization;

using MUI.Catalog;
using MUI.Web.Components;
using WebProvenance = MUI.Web.Components.Provenance;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// One date format, one age ladder, and an age a reader can resolve to a time — in any language.
/// </summary>
/// <remarks>
/// <para>
/// The site printed three absolute formats — <c>2026-08-17</c> on a game page, <c>31 July 2026</c>
/// in the rankings, <c>Aug 2026</c> beside an address — and its relative ages carried no absolute
/// value at all, so a reader arriving from a cached page or a search result could not tell what
/// "19m ago" was 19 minutes before.
/// </para>
/// <para>
/// Then it printed the one format under <see cref="CultureInfo.InvariantCulture"/>, which is the
/// same defect wearing a different hat: a German page said <c>30 Jul 2026</c>, and the month name
/// was not in any file a translator is ever sent. These now assert the fact through the bundle
/// rather than the English literal, because a literal passes on the day the German is wrong.
/// </para>
/// </remarks>
public class TimeSurfaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 14, 21, 0, TimeSpan.Zero);

    private const string English = Locales.SourceTag;
    private const string German = "de";

    /// <summary>
    /// The locales that claim to be languages.
    /// </summary>
    /// <remarks>
    /// The pseudolocale accents every letter it is handed, so it renders UTC as ÚTÇ and MSSP inside
    /// brackets. That is the pseudolocale doing its one job — proving a string came through the
    /// pipeline — and it is never offered to a reader, so it is not the thing these two assert on.
    /// </remarks>
    private static IEnumerable<Locale> Languages =>
        Locales.All.Where(l => l.Status is not LocaleStatus.TestOnly);

    [Test]
    public async Task ThereIsOneAbsoluteFormatAndItNamesItsZone()
    {
        var at = new DateTimeOffset(2026, 8, 17, 14, 2, 0, TimeSpan.Zero);

        await Assert.That(Dates.Absolute(English, at)).IsEqualTo("17 Aug 2026");
        await Assert.That(Dates.Stamp(English, at)).IsEqualTo("17 Aug 2026 14:02 UTC");
        await Assert.That(Dates.Machine(at)).IsEqualTo("2026-08-17T14:02:00Z");
    }

    /// <summary>
    /// The month is CLDR's, not an array here and not the invariant culture's.
    /// </summary>
    /// <remarks>
    /// The same finding as the heatmap's seven English day names, one file over: no translator could
    /// have fixed <c>Jul</c> on a German page, because the word was never in a file they are sent.
    /// July is the case worth pinning — German writes <c>Juli</c> in a date and .NET's *standalone*
    /// abbreviation is the legacy <c>Jul</c>, so a plausible-looking call to
    /// <c>GetAbbreviatedMonthName</c> would leave this passing in English and wrong in German.
    /// </remarks>
    [Test]
    public async Task AGermanPageNamesTheMonthInGerman()
    {
        var july = new DateTimeOffset(2026, 7, 30, 18, 0, 0, TimeSpan.Zero);

        // The word, not the whole string: the order of the three parts is `date.absolute`'s to
        // decide, and German's translation of it supplies the ordinal point that English has no use
        // for — "30. Juli 2026". Pinning the assembled sentence here would have made a correct
        // translation fail, which is the test asserting its own English word order as if it were a
        // fact about dates.
        await Assert.That(Dates.Absolute(German, july)).Contains("Juli");
        await Assert.That(Dates.Absolute(German, july)).Contains("30");
        await Assert.That(Dates.Absolute(German, july)).Contains("2026");
        await Assert.That(Dates.Absolute(German, july)).DoesNotContain("Jul ");

        // The stamp is the date with the clock on it, in whatever order that locale puts them.
        await Assert.That(Dates.Stamp(German, july)).Contains(Dates.Absolute(German, july));
        await Assert.That(Dates.Stamp(German, july)).Contains("18:00");
        await Assert.That(Dates.Stamp(German, july)).Contains("UTC");

        await Assert.That(Dates.Absolute(German, july)).IsNotEqualTo(Dates.Absolute(English, july));
    }

    /// <summary>Every month of the year, and not one of them still in English.</summary>
    /// <remarks>
    /// One month passing is a coincidence — <c>Feb</c>, <c>Aug</c> and <c>Sep</c> are nearly the
    /// same word in both languages, and the sweep that found this was run against a fixture whose
    /// dates are all in July. Walking twelve is what proves the name came out of CLDR rather than
    /// out of a lucky abbreviation.
    /// </remarks>
    [Test]
    public async Task NoMonthOfTheGermanYearComesBackInEnglish()
    {
        for (var month = 1; month <= 12; month++)
        {
            var at = new DateTimeOffset(2026, month, 15, 9, 0, 0, TimeSpan.Zero);

            await Assert.That(Dates.Absolute(German, at))
                .IsNotEqualTo(Dates.Absolute(English, at))
                .Because($"month {month} reads the same in German as in English");
        }
    }

    /// <summary>
    /// UTC is a zone and not a word, and the clock is the site's rather than the locale's.
    /// </summary>
    /// <remarks>
    /// Every time here is UTC because a crawler's clock is the only one it has. A locale that
    /// rendered <c>2:02 PM</c>, or one that translated the suffix, would be describing a different
    /// instant to a reader who has no way to tell.
    /// </remarks>
    [Test]
    public async Task TheZoneIsNamedInEveryLanguageAndTheClockDoesNotMove()
    {
        var at = new DateTimeOffset(2026, 8, 17, 14, 2, 0, TimeSpan.Zero);

        foreach (var locale in Languages)
        {
            var stamp = Dates.Stamp(locale.Tag, at);

            await Assert.That(stamp).Contains("14:02").Because($"{locale.Tag} moved the clock");
            await Assert.That(stamp).Contains("UTC").Because($"{locale.Tag} lost the zone");
        }
    }

    [Test]
    public async Task TheAgeLadderRunsMinutesToNinetyThenHoursToFortyEight()
    {
        await Assert.That(Relative.Format(English, TimeSpan.FromSeconds(30))).IsEqualTo("now");
        await Assert.That(Relative.Format(English, TimeSpan.FromMinutes(84))).IsEqualTo("84m");
        await Assert.That(Relative.Format(English, TimeSpan.FromMinutes(95))).IsEqualTo("1h");
        await Assert.That(Relative.Format(English, TimeSpan.FromHours(47))).IsEqualTo("47h");
        await Assert.That(Relative.Format(English, TimeSpan.FromHours(49))).IsEqualTo("2d");
    }

    /// <summary>
    /// Every rung of every family is a real ICU plural, with the branches its language has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>n + "w ago"</c> is a sentence assembled in English word order with nowhere for a
    /// translator to stand. The rungs are patterns instead, so a language selects its own category
    /// — and this walks every locale the site names against the CLDR rules it transcribes, because
    /// a pattern that parses in English can still be missing the branch Russian needs.
    /// </para>
    /// <para>
    /// <c>other</c> is mandatory everywhere, including in Japanese and Chinese, which have nothing
    /// else. A branch keyword a language does not have is rejected rather than ignored.
    /// </para>
    /// </remarks>
    [Test]
    public async Task EveryRungIsAPluralWithTheCategoriesItsLanguageActuallyHas()
    {
        string[] families = ["age.short", "age.ago", "age.dark"];
        string[] rungs = ["now", "minutes", "hours", "days", "weeks", "months", "years"];

        foreach (var family in families)
        {
            foreach (var rung in rungs)
            {
                var id = family + "." + rung;

                await Assert.That(Messages.Source(id))
                    .IsNotNull()
                    .Because($"{id} is not in the English bundle");

                foreach (var locale in Locales.All)
                {
                    foreach (var count in new[] { 0, 1, 2, 5, 11, 21, 101 })
                    {
                        var said = Messages.For(
                            locale.Tag,
                            id,
                            new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = count });

                        await Assert.That(string.IsNullOrWhiteSpace(said))
                            .IsFalse()
                            .Because($"{locale.Tag} said nothing for {id} at {count}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The number reaches the pattern, so a language can put it where it belongs.
    /// </summary>
    [Test]
    public async Task ThePluralPrintsTheNumberItWasGiven()
    {
        await Assert.That(Relative.Format(English, TimeSpan.FromDays(21))).IsEqualTo("3w");
        await Assert.That(Relative.Ago(English, TimeSpan.FromDays(21))).IsEqualTo("3w ago");
        await Assert.That(Relative.Format(English, TimeSpan.FromDays(400))).IsEqualTo("13mo");
        await Assert.That(Relative.Ago(English, TimeSpan.FromDays(1200))).IsEqualTo("3y ago");
    }

    /// <summary>
    /// "How fresh is this measurement" and "how long has this game been dark" are two questions.
    /// </summary>
    /// <remarks>
    /// English answers both with "2w ago" and several languages will not. The ids are separate so a
    /// translator can reach each one at all — collapsing them because the source language cannot
    /// tell them apart is how three cases in four end up ungrammatical.
    /// </remarks>
    [Test]
    public async Task AFreshMeasurementAndADarkGameAreTwoIdsEvenWhereTheEnglishIsOne()
    {
        var age = TimeSpan.FromDays(14);

        await Assert.That(Relative.Ago(English, age, AgeSense.Confirmed))
            .IsEqualTo(Relative.Ago(English, age, AgeSense.Reached));

        string[] rungs = ["now", "minutes", "hours", "days", "weeks", "months", "years"];

        foreach (var rung in rungs)
        {
            await Assert.That(Messages.Source("age.ago." + rung)).IsNotNull();
            await Assert.That(Messages.Source("age.dark." + rung)).IsNotNull();
        }
    }

    /// <summary>
    /// The dark register never names a cause, in any language.
    /// </summary>
    /// <remarks>
    /// We measured a socket from one vantage point at intervals. A game with a routing problem to
    /// our host is unreachable and perfectly alive, and "offline" would file our vantage point as a
    /// fact about their game (rule 5).
    /// </remarks>
    [Test]
    public async Task ADarkGameIsNeverCalledOfflineOrDown()
    {
        foreach (var locale in Locales.All)
        {
            foreach (var rung in new[] { "now", "hours", "weeks", "years" })
            {
                var said = Messages.For(
                    locale.Tag,
                    "age.dark." + rung,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = 3 })
                    .ToLowerInvariant();

                foreach (var cause in new[] { "offline", "down", "uptime", "crashed", "dead" })
                {
                    await Assert.That(said).DoesNotContain(cause);
                }
            }
        }
    }

    [Test]
    public async Task AnAgeCarriesTheInstantItIsRelativeTo()
    {
        var html = await Render.ComponentAsync<Moment>(new()
        {
            ["At"] = new DateTimeOffset(2026, 8, 17, 14, 2, 0, TimeSpan.Zero),
            ["Now"] = Now,
        });

        await Assert.That(html).Contains("<time datetime=\"2026-08-17T14:02:00Z\"");
        await Assert.That(html).Contains("title=\"19m ago, 17 Aug 2026 14:02 UTC\"");
        await Assert.That(Render.Words(html)).Contains(">19m</time>");
    }

    [Test]
    public async Task TheAbsoluteTimeIsSpokenWhereAReaderIsWeighingOneFactAndNotWhereTheyAreScanning()
    {
        // A listing row that announced the absolute time of every age would be the wall of
        // repetition this whole pass exists to remove; a game page's field, where the age is the
        // fact being weighed, is exactly where it belongs.
        var parameters = new Dictionary<string, object?>
        {
            ["At"] = new DateTimeOffset(2026, 8, 17, 14, 2, 0, TimeSpan.Zero),
            ["Now"] = Now,
        };

        var scanning = await Render.ComponentAsync<Moment>(parameters);
        var weighing = await Render.ComponentAsync<Moment>(
            new(parameters) { ["Spoken"] = true });

        await Assert.That(scanning).DoesNotContain("sr-only");
        await Assert.That(Render.Words(weighing)).Contains("17 Aug 2026 14:02 UTC");
    }

    /// <summary>
    /// Which of the age and the instant comes first is a language's decision.
    /// </summary>
    /// <remarks>
    /// It was a comma in an interpolated string, which is a word order chosen for English and
    /// offered to nobody else.
    /// </remarks>
    [Test]
    public async Task TheTitleIsAMessageAndNotAComma()
    {
        await Assert.That(Messages.Source("time.title")).IsEqualTo("{age}, {stamp}");

        var said = Messages.For(
            English,
            "time.title",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["age"] = Relative.Ago(English, TimeSpan.FromMinutes(19)),
                ["stamp"] = Dates.Stamp(English, new DateTimeOffset(2026, 8, 17, 14, 2, 0, TimeSpan.Zero)),
            });

        await Assert.That(said).IsEqualTo("19m ago, 17 Aug 2026 14:02 UTC");
    }

    /// <summary>
    /// <b>MSSP, not <c>Mssp</c>.</b>
    /// </summary>
    /// <remarks>
    /// The chip's tooltip interpolated <see cref="FieldSource"/> straight into a sentence and told
    /// readers a value arrived "via Mssp" — an acronym mis-cased by C#'s naming convention, in a
    /// string no translator could reach. Upper-casing at the call site would have fixed four letters
    /// and left the next member to leak the same way, which is what the exhaustive map prevents.
    /// </remarks>
    [Test]
    public async Task AProtocolAcronymIsSpeltAsTheProtocolSpellsIt()
    {
        await Assert.That(WebProvenance.Via(English, FieldSource.Mssp)).IsEqualTo("MSSP");
        await Assert.That(WebProvenance.Via(English, FieldSource.Who)).IsEqualTo("WHO");
        await Assert.That(WebProvenance.Via(English, FieldSource.Info)).IsEqualTo("INFO");
        await Assert.That(WebProvenance.Via(English, FieldSource.I3)).IsEqualTo("I3");

        // And an acronym is the same acronym in every language: it is evidence, not a word.
        foreach (var locale in Languages)
        {
            await Assert.That(WebProvenance.Via(locale.Tag, FieldSource.Mssp))
                .IsEqualTo("MSSP")
                .Because($"{locale.Tag} translated a protocol name");
        }
    }

    /// <summary>Every source has a display name, and none of them is the enum member.</summary>
    [Test]
    public async Task NoFieldSourceReachesAReaderAsItsEnumMember()
    {
        foreach (var source in Enum.GetValues<FieldSource>())
        {
            var shown = WebProvenance.Via(English, source);

            await Assert.That(string.IsNullOrWhiteSpace(shown))
                .IsFalse()
                .Because($"{source} has no display name");

            // I3 is the one member whose C# spelling is already the reader's — an initial and a
            // digit, with no casing for PascalCase to get wrong. Every other member's ToString
            // would be a defect on the page: Mssp for an acronym, Who for a command, I3Mudlist for
            // two words and a missing space.
            if (source is FieldSource.I3)
            {
                continue;
            }

            await Assert.That(shown)
                .IsNotEqualTo(source.ToString())
                .Because($"{source} is reaching the page as its own C# spelling");
        }
    }

    /// <summary>
    /// The chip's tooltip, whole: machine voice through, our words from the bundle.
    /// </summary>
    /// <remarks>
    /// "last confirmed" is the load-bearing phrase and it is a fact about our crawl. It is the day
    /// <em>we</em> last saw the value, and it must never be read as a day the game did anything.
    /// </remarks>
    [Test]
    public async Task TheChipTooltipSaysTheSourceTheDateAndWhoseFactTheDateIs()
    {
        var confirmed = new DateTimeOffset(2026, 7, 30, 18, 0, 0, TimeSpan.Zero);

        var english = Messages.For(
            English,
            "chip.title",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = "PennMUSH 1.8.8p0",
                ["how"] = Messages.For(English, "provenance.game.declared"),
                ["source"] = WebProvenance.Via(English, FieldSource.Mssp),
                ["date"] = Dates.Stamp(English, confirmed),
            });

        await Assert.That(english)
            .IsEqualTo("PennMUSH 1.8.8p0 — declared via MSSP, last confirmed 30 Jul 2026 18:00 UTC");

        // The same tooltip asked for in German is not the same string. It was, and that is the
        // defect this pass exists to close.
        var german = Messages.For(
            German,
            "chip.title",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = "PennMUSH 1.8.8p0",
                ["how"] = Messages.For(German, "provenance.game.declared"),
                ["source"] = WebProvenance.Via(German, FieldSource.Mssp),
                ["date"] = Dates.Stamp(German, confirmed),
            });

        await Assert.That(german).IsNotEqualTo(english);
        await Assert.That(german).Contains("MSSP");
        await Assert.That(german).Contains("PennMUSH 1.8.8p0");
    }

    /// <summary>
    /// Measured, declared and owner-declared stay three words in every locale.
    /// </summary>
    /// <remarks>
    /// The provenance tooltip is the string that says which of the two a reader is looking at, so
    /// collapsing them into one word is the one failure that makes this site say something false.
    /// </remarks>
    [Test]
    public async Task TheChipNeverCollapsesMeasuredAndDeclaredIntoOneWord()
    {
        string[] ids =
        [
            "provenance.game.measured",
            "provenance.game.declared",
            "provenance.game.ownerDeclared",
        ];

        foreach (var locale in Locales.All)
        {
            var said = ids.Select(id => Messages.For(locale.Tag, id)).ToList();

            await Assert.That(said.Distinct(StringComparer.Ordinal).Count())
                .IsEqualTo(ids.Length)
                .Because($"{locale.Tag} said the same word for two different claims");
        }
    }
}
