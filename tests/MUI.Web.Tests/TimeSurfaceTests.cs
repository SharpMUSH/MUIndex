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
/// The site once printed three different absolute formats with relative ages carrying no absolute
/// value, and then the one unified format under <see cref="CultureInfo.InvariantCulture"/> — a
/// German page saying <c>30 Jul 2026</c>, the month name in no file any translator is sent. These
/// assert through the bundle rather than the English literal, which passes even when German is wrong.
/// </remarks>
public class TimeSurfaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 14, 21, 0, TimeSpan.Zero);

    private const string English = Locales.SourceTag;
    private const string German = "de";

    /// <summary>
    /// The locales that claim to be languages.
    /// </summary>
    /// <remarks>The pseudolocale accents everything it's handed (UTC becomes ÚTÇ) to prove a string came through the pipeline; it's never offered to a reader, so it's excluded here.</remarks>
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
    /// <remarks>July is the case worth pinning: German writes <c>Juli</c>, but .NET's *standalone* abbreviation is the legacy <c>Jul</c> — a plausible <c>GetAbbreviatedMonthName</c> call would pass in English and be wrong in German.</remarks>
    [Test]
    public async Task AGermanPageNamesTheMonthInGerman()
    {
        var july = new DateTimeOffset(2026, 7, 30, 18, 0, 0, TimeSpan.Zero);

        // The word, not the whole string — part order is `date.absolute`'s to decide (German adds an
        // ordinal point English has no use for). Pinning the assembled sentence would fail a correct
        // translation.
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
    /// <remarks>One month passing is a coincidence — <c>Feb</c>/<c>Aug</c>/<c>Sep</c> are nearly the same word in both languages. Walking all twelve proves the name came from CLDR, not a lucky abbreviation.</remarks>
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
    /// <remarks>Every time is UTC because the crawler's clock is the only one it has; a locale rendering <c>2:02 PM</c> would describe a different instant with no way for the reader to tell.</remarks>
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
    /// Rungs are ICU patterns, not <c>n + "w ago"</c> assembled in English word order, so each
    /// language selects its own plural category — a pattern valid in English can still be missing
    /// the branch Russian needs. <c>other</c> is mandatory everywhere, including Japanese and Chinese.
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
    /// <remarks>English answers both with "2w ago"; several languages won't. The ids are kept separate so a translator can reach each — collapsing them on the source language's inability to distinguish is how translations end up ungrammatical.</remarks>
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
    /// <remarks>We measured a socket from one vantage point; a game with a routing problem to our host is unreachable and perfectly alive. "Offline" would file our vantage point as a fact about their game (rule 5).</remarks>
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
        // A listing row announcing the absolute time of every age is repetition; a game page's field,
        // where the age is the fact being weighed, is where it belongs.
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
    /// <remarks>Was a comma in an interpolated string — a word order chosen for English and offered to nobody else.</remarks>
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
    /// <remarks>The chip's tooltip once interpolated <see cref="FieldSource"/> straight into a sentence, showing "via Mssp" — mis-cased by C#'s convention. An exhaustive map, not a call-site fix, stops the next member leaking the same way.</remarks>
    [Test]
    public async Task AProtocolAcronymIsSpeltAsTheProtocolSpellsIt()
    {
        await Assert.That(WebProvenance.Via(English, FieldSource.Mssp)).IsEqualTo("MSSP");
        await Assert.That(WebProvenance.Via(English, FieldSource.Who)).IsEqualTo("WHO");
        await Assert.That(WebProvenance.Via(English, FieldSource.Info)).IsEqualTo("INFO");
        await Assert.That(WebProvenance.Via(English, FieldSource.I3)).IsEqualTo("I3");

        // An acronym is the same acronym in every language: it's evidence, not a word.
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

            // Two members' C# spelling is already the reader's: I3 is a protocol name and
            // AresCentral is a proper noun, and both are written that way in every locale. For
            // every other member a ToString on the page would be a defect (Mssp, Who, I3Mudlist),
            // which is what this guard is for — the exemption is a list of two, not a loophole.
            if (source is FieldSource.I3 or FieldSource.AresCentral)
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
    /// <remarks>"last confirmed" is the day <em>we</em> last saw the value — a fact about our crawl, never to be read as a day the game did anything.</remarks>
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

        // The same tooltip asked for in German is not the same string.
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
    /// <remarks>Collapsing measured and declared into one word is the failure that makes this site say something false.</remarks>
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
