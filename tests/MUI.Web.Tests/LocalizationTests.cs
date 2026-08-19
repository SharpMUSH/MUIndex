using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The localization pipeline: plural rules, ICU messages, the locked glossary, and the routing.
/// </summary>
/// <remarks>
/// Nothing here asserts a translation is <em>good</em> — only what a machine can check: a message
/// reaches every plural form its locale needs, a locked string can't quietly become a paraphrase, and
/// an untranslated locale is never offered to a reader.
/// </remarks>
public class LocalizationTests
{
    // ── plural rules ─────────────────────────────────────────────────────────────────────────

    [Test]
    [Arguments(0, "other")]
    [Arguments(1, "one")]
    [Arguments(2, "other")]
    [Arguments(21, "other")]
    public async Task EnglishHasTwoForms(int count, string expected)
    {
        await Assert.That(PluralRules.Keyword(PluralRules.Of("en", count))).IsEqualTo(expected);
    }

    [Test]
    [Arguments(1, "one")]
    [Arguments(21, "one")]
    [Arguments(2, "few")]
    [Arguments(23, "few")]
    [Arguments(5, "many")]
    [Arguments(11, "many")]      // the exception that catches a naive n % 10 rule
    [Arguments(12, "many")]
    [Arguments(0, "many")]
    public async Task RussianHasThreeAndTheyAreNotObvious(int count, string expected)
    {
        // 11 and 12 end in 1 and 2 but take neither `one` nor `few`.
        await Assert.That(PluralRules.Keyword(PluralRules.Of("ru", count))).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(5)]
    public async Task ChineseHasOneFormAndSoCannotFailAnAgreementBug(int count)
    {
        // Chinese agrees with any string architecture, including one wrong for every inflected language.
        await Assert.That(PluralRules.Of("zh-Hans", count)).IsEqualTo(PluralCategory.Other);
        await Assert.That(PluralRules.CategoriesOf("zh-Hans").Count).IsEqualTo(1);
    }

    [Test]
    public async Task AScriptOrPrivateSubtagDoesNotChangeHowANumberAgrees()
    {
        await Assert.That(PluralRules.Of("zh-Hant", 5)).IsEqualTo(PluralRules.Of("zh", 5));
        await Assert.That(PluralRules.Of("ru-x-canary", 2)).IsEqualTo(PluralRules.Of("ru", 2));
    }

    // ── plural rules, checked against the CLDR 46 chart rather than against memory ────────────

    [Test]
    public async Task TurkishSaysOneForOneAndForNothingElse()
    {
        // CLDR 46 tr — one: n = 1. `n = 0..1` is a real CLDR rule, but belongs to Akan and Punjabi.
        await Assert.That(PluralRules.Keyword(PluralRules.Of("tr", 0))).IsEqualTo("other");
        await Assert.That(PluralRules.Keyword(PluralRules.Of("tr", 1))).IsEqualTo("one");
        await Assert.That(PluralRules.Keyword(PluralRules.Of("tr", 2))).IsEqualTo("other");
    }

    [Test]
    [Arguments("el", "one")]        // one: n = 1
    [Arguments("no", "one")]        // one: n = 1
    [Arguments("es", "one")]        // one: n = 1
    [Arguments("tr", "one")]        // one: n = 1
    [Arguments("da", "one")]        // one: n = 1 or t != 0 and i = 0,1
    [Arguments("be", "one")]        // one: n % 10 = 1 and n % 100 != 11 — stated on n, no v guard
    [Arguments("en", "other")]      // one: i = 1 and v = 0
    [Arguments("de", "other")]      // one: i = 1 and v = 0
    [Arguments("it", "other")]      // one: i = 1 and v = 0
    [Arguments("ru", "other")]      // one: v = 0 and i % 10 = 1 and i % 100 != 11
    public async Task OnePointZeroIsWhatSeparatesARuleStatedOnNFromOneStatedOnI(string tag, string expected)
    {
        // 1.0 is the only value telling the two rule shapes apart; transcribing every language as
        // `i = 1 and v = 0` because English is written that way is wrong for six of the thirty-one here.
        var oneDotZero = PluralOperands.Of(1.0m, visibleFractionDigits: 1);

        await Assert.That(PluralRules.Keyword(PluralRules.Of(tag, oneDotZero))).IsEqualTo(expected);
    }

    [Test]
    public async Task DanishSaysOneForAFractionBelowTwo()
    {
        // CLDR 46 da — one: n = 1 or t != 0 and i = 0,1. Danish is the only language here whose `one`
        // reaches a value that isn't 1.
        await Assert.That(PluralRules.Keyword(PluralRules.Of("da", PluralOperands.Of(0.5m)))).IsEqualTo("one");
        await Assert.That(PluralRules.Keyword(PluralRules.Of("da", PluralOperands.Of(1.5m)))).IsEqualTo("one");
        await Assert.That(PluralRules.Keyword(PluralRules.Of("da", PluralOperands.Of(2.5m)))).IsEqualTo("other");
        await Assert.That(PluralRules.Keyword(PluralRules.Of("da", 0))).IsEqualTo("other");
    }

    [Test]
    [Arguments("fr")]
    [Arguments("pt")]
    [Arguments("es")]
    [Arguments("it")]
    public async Task AMillionTakesItsOwnFormInEveryRomanceLanguageCldrStatesOneFor(string tag)
    {
        // many: e = 0 and i != 0 and i % 1000000 = 0 and v = 0 — es and it had been given English's
        // rule and so had no `many` at all.
        await Assert.That(PluralRules.Keyword(PluralRules.Of(tag, 1_000_000))).IsEqualTo("many");
        await Assert.That(PluralRules.Keyword(PluralRules.Of(tag, 2_000_000))).IsEqualTo("many");
        await Assert.That(PluralRules.Keyword(PluralRules.Of(tag, 1_000_001))).IsEqualTo("other");
    }

    [Test]
    public async Task HebrewLostItsManyFormAndAllOfItsOrdinalsBeforeCldr46()
    {
        // Both removed upstream; a table still carrying them selects a branch no translator wrote.
        await Assert.That(PluralRules.Keyword(PluralRules.Of("he", 20))).IsEqualTo("other");
        await Assert.That(PluralRules.Keyword(PluralRules.Of("he", 100))).IsEqualTo("other");

        await Assert.That(PluralRules.CategoriesOf("he", PluralKind.Ordinal))
            .IsEquivalentTo(new[] { PluralCategory.Other });

        await Assert.That(PluralRules.Keyword(PluralRules.Of("he", PluralOperands.Of(0.5m)))).IsEqualTo("one");
    }

    [Test]
    [Arguments("sv", 1, "one")]     // one: n % 10 = 1,2 and n % 100 != 11,12
    [Arguments("sv", 2, "one")]
    [Arguments("sv", 3, "other")]
    [Arguments("sv", 11, "other")]
    [Arguments("sv", 12, "other")]
    [Arguments("sv", 21, "one")]
    [Arguments("be", 2, "few")]     // few: n % 10 = 2,3 and n % 100 != 12,13
    [Arguments("be", 3, "few")]
    [Arguments("be", 12, "other")]
    [Arguments("be", 13, "other")]
    [Arguments("vi", 1, "one")]     // one: n = 1
    [Arguments("vi", 2, "other")]
    [Arguments("ms", 1, "one")]     // one: n = 1
    public async Task TheOrdinalTableStatesARuleForEveryLanguageCldrStatesOneFor(string tag, int n, string expected)
    {
        // A missing ordinal rule is not a missing translation — it silently renders every rank in the
        // `other` form in a language that inflects them.
        await Assert.That(PluralRules.Keyword(PluralRules.Of(tag, n, PluralKind.Ordinal))).IsEqualTo(expected);
    }

    [Test]
    public async Task ACldrRangeMatchesAWholeNumberAndNothingElse()
    {
        // `n % 100 = 3..10` is a range over integers: 3.5 is not in it.
        await Assert.That(PluralRules.Keyword(PluralRules.Of("ar", PluralOperands.Of(3.5m)))).IsEqualTo("other");
        await Assert.That(PluralRules.Keyword(PluralRules.Of("ar", 3))).IsEqualTo("few");
        await Assert.That(PluralRules.Keyword(PluralRules.Of("ar", 11))).IsEqualTo("many");
    }

    // ── ICU messages ─────────────────────────────────────────────────────────────────────────

    [Test]
    [Arguments(0, "0 games")]
    [Arguments(1, "1 game")]
    [Arguments(2, "2 games")]
    public async Task ACountAndItsNounAgree(int count, string expected)
    {
        await Assert.That(Messages.Count("en", count)).IsEqualTo(expected);
    }

    [Test]
    public async Task TheSameMessageReachesADifferentFormInADifferentLocale()
    {
        await Assert.That(Messages.Count("ru-x-canary", 1)).IsEqualTo("1 игра");
        await Assert.That(Messages.Count("ru-x-canary", 2)).IsEqualTo("2 игры");
        await Assert.That(Messages.Count("ru-x-canary", 5)).IsEqualTo("5 игр");
    }

    [Test]
    public async Task AnExactMatchWinsOverACategory()
    {
        // `=0` says "no games" without inventing a plural category CLDR doesn't give English.
        var listing = Messages.For("en", "listing.total", new Dictionary<string, object?> { ["count"] = 0 });

        await Assert.That(listing).StartsWith("No games");
    }

    [Test]
    public async Task ABranchMayHoldAnotherArgument()
    {
        // Nests {total} inside a plural branch; a parser stopping at the first closing brace would
        // cut it in half.
        var args = new Dictionary<string, object?> { ["disagreeing"] = 1, ["total"] = 6 };

        await Assert.That(Messages.For("en", "capabilities.agree", args))
            .IsEqualTo("1 of 6 disagrees with what the game declares.");

        args["disagreeing"] = 0;

        await Assert.That(Messages.For("en", "capabilities.agree", args))
            .IsEqualTo("None of the 6 disagree.");
    }

    [Test]
    public async Task SyntaxThisFormatterDoesNotImplementIsRefusedRatherThanGuessed()
    {
        await Assert.That(() => IcuMessage.Format("{n, date, short}", "en", new Dictionary<string, object?> { ["n"] = 1 }))
            .Throws<FormatException>();

        await Assert.That(() => IcuMessage.Format("{missing}", "en"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task AHashOutsideAPluralBranchIsALiteral()
    {
        await Assert.That(IcuMessage.Format("channel #general", "en")).IsEqualTo("channel #general");
    }

    // ── the locked glossary ──────────────────────────────────────────────────────────────────

    [Test]
    public async Task EveryLockedIdIsAMessageTheSiteActuallySays()
    {
        // A glossary entry with no message behind it is a promise to translators about a string that
        // doesn't exist.
        foreach (var locked in Glossary.Locked)
        {
            await Assert.That(Messages.Pattern(Locales.SourceTag, locked.Id))
                .IsEqualTo(locked.English)
                .Because($"{locked.Id} is locked but the English bundle disagrees");
        }
    }

    [Test]
    public async Task TheFourKindsOfAbsenceAreFourDifferentStrings()
    {
        // The finding this file exists for: four distinct claims a translation engine will collapse
        // into stylistic variants of "unavailable" if nothing stops it.
        string[] ids = ["state.notMeasured", "state.uncounted", "state.unreachable", "state.notCounted"];

        var english = ids.Select(id => Messages.For("en", id)).ToList();

        await Assert.That(english.Distinct().Count()).IsEqualTo(ids.Length);

        foreach (var id in ids)
        {
            await Assert.That(Glossary.IsLocked(id)).IsTrue().Because($"{id} must not be paraphrasable");
        }
    }

    [Test]
    public async Task OneEnglishWordWithSeveralSubjectsIsSeveralIds()
    {
        // "measured" is one string in English and four in Russian, chosen by what it describes; the
        // ids must be granular enough for a translator to reach each case.
        var measured = Glossary.Locked.Where(l => l.English == "measured").ToList();

        await Assert.That(measured.Count).IsGreaterThanOrEqualTo(4);
        await Assert.That(measured.Select(m => m.Id).Distinct().Count()).IsEqualTo(measured.Count);
        await Assert.That(measured.Select(m => m.Subject).Distinct().Count()).IsGreaterThanOrEqualTo(4);

        await Assert.That(Glossary.Of("kicker.measured")!.Subject).IsEqualTo(Subject.Standalone);
    }

    [Test]
    public async Task EveryLockedStringShipsWithTheReasonItIsLocked()
    {
        foreach (var locked in Glossary.Locked)
        {
            await Assert.That(string.IsNullOrWhiteSpace(locked.Rationale)).IsFalse();
            await Assert.That(locked.Rationale.Length).IsGreaterThan(40);
        }

        await Assert.That(Glossary.Brief()).Contains("provenance.count.measured");
        await Assert.That(Glossary.Brief()).Contains("the English will show");
    }

    // ── completeness, and the canary ─────────────────────────────────────────────────────────

    [Test]
    public async Task AnUntranslatedStringFallsBackToTheEnglishRatherThanToNothing()
    {
        // A visible English phrase teaches a reader something true; a smoothed-over paraphrase of a
        // locked string teaches something false with no way to tell.
        await Assert.That(Messages.HasOwn("ru-x-canary", "term.connected")).IsFalse();
        await Assert.That(Messages.For("ru-x-canary", "term.connected")).IsEqualTo("connected");

        await Assert.That(Messages.For("ru-x-canary", "kicker.measured")).IsEqualTo("ИЗМЕРЕНО");
    }

    [Test]
    public async Task TheCanaryFailsOnAMissingPluralFormRatherThanRenderingOne()
    {
        // `listing.total` in the canary bundle is complete for English but silently missing the
        // `few` form a count of two takes in Russian.
        var incomplete = Missing("ru-x-canary", "listing.total");

        // Reported per argument: a message with two counts can be complete for one and not the other.
        await Assert.That(incomplete).Contains("count:few");
        await Assert.That(incomplete).Contains("count:many");

        await Assert.That(Missing("ru-x-canary", "facet.count")).IsEmpty();
    }

    [Test]
    public async Task EveryOfferedLocaleIsCompleteInEveryPluralFormItNeeds()
    {
        // A locale reaches LocaleStatus.Shipped only when this holds — the canary is TestOnly and
        // isn't walked here.
        foreach (var locale in Locales.Offered)
        {
            foreach (var id in Messages.Ids)
            {
                await Assert.That(Missing(locale.Tag, id))
                    .IsEmpty()
                    .Because($"{locale.Tag} / {id} is missing a plural form it needs");
            }
        }
    }

    [Test]
    public async Task NoLocaleIsOfferedBeforeItsLockedStringsAreTranslated()
    {
        // Stops somebody moving a status enum and shipping a mostly-English page under a Chinese flag.
        foreach (var locale in Locales.Offered.Where(l => l.Tag != Locales.SourceTag))
        {
            foreach (var locked in Glossary.Locked)
            {
                await Assert.That(Messages.HasOwn(locale.Tag, locked.Id))
                    .IsTrue()
                    .Because($"{locale.Tag} is offered without a translation for {locked.Id}");
            }
        }
    }

    [Test]
    public async Task ThePseudolocaleExercisesTheMachineryAndTheWidthBudget()
    {
        // Accented and expanded so a string that skipped the pipeline is visible at a glance, at
        // roughly the width German and Russian actually need.
        var pseudo = Messages.Count("qps-ploc", 3);

        await Assert.That(pseudo).Contains("⟦");
        await Assert.That(pseudo).Contains("3");
        await Assert.That(pseudo.Length).IsGreaterThan(Messages.Count("en", 3).Length);

        // ICU syntax survived: a pseudolocale that mangled a branch keyword would fail to parse.
        await Assert.That(Messages.Count("qps-ploc", 1)).Contains("1");
    }

    // ── routing ──────────────────────────────────────────────────────────────────────────────

    [Test]
    [Arguments("de", "de")]
    [Arguments("de-AT,de;q=0.9,en;q=0.8", "de")]
    [Arguments("zh-CN,zh;q=0.9", "zh-Hans")]
    [Arguments("en-GB,en;q=0.9", null)]              // already where they are
    [Arguments("de;q=0", null)]                       // explicitly refused
    [Arguments("*", null)]
    [Arguments("", null)]
    [Arguments("kl,fo", null)]                        // nothing offered answers
    [Arguments("en;q=0.9,ja;q=0.2", null)]            // English was the actual top preference
    [Arguments("en-US,en;q=0.9,ja;q=0.2", null)]      // same, with a region on the English tag
    public async Task AcceptLanguageIsReadForTheFirstVisitOnly(string header, string? expected)
    {
        // A header is a standing preference, worth one redirect and no more. Matches on the language
        // subtag so zh-CN reaches zh-Hans. Every case answers null today (English is the only offered
        // locale); asserted against the offered set rather than a hard-coded answer, so this starts
        // biting the day a locale ships.
        //
        // English must be scored against every other candidate, not excluded from scoring: a browser
        // that lists a second language at a low q is not asking to be moved there ahead of English.
        var chosen = LocaleRouting.Preferred(header);

        if (expected is null || Locales.Find(expected) is not { IsOffered: true })
        {
            await Assert.That(chosen).IsNull();
            return;
        }

        await Assert.That(chosen!.Tag).IsEqualTo(expected);
    }

    [Test]
    [Arguments("/games", "/games")]
    [Arguments("/de/games", "/games")]
    [Arguments("//elsewhere.example", "/")]
    [Arguments("/\\elsewhere.example", "/")]
    [Arguments("/games\nLocation: /evil", "/")]
    [Arguments("", "/")]
    public async Task TheReturnPathIsAPathOnThisSiteAndNothingElse(string posted, string expected)
    {
        // Written into a Location header. A protocol-relative URL walks through a naive
        // StartsWith('/') check; several browsers read /\host the same way; a CR here is response
        // splitting.
        await Assert.That(LocaleRouting.Back(posted)).IsEqualTo(expected);
    }

    /// <summary>
    /// The document states the language it is written in, and it is the one the reader asked for.
    /// </summary>
    /// <remarks>
    /// If <c>lang</c> stayed the constant <c>"en"</c> while the page rendered Japanese, three things
    /// would be wrong at once: Han unification means <c>lang</c> is what selects the correct glyph
    /// form for Chinese vs. Japanese (rendering at the same width, invisible to an overflow audit); a
    /// screen reader switches voice on it; and it's what the <c>hreflang</c> links in the head claim
    /// about the document.
    /// </remarks>
    [Test]
    [Arguments("/games", "en")]
    [Arguments("/de/games", "de")]
    [Arguments("/nl/games", "nl")]
    [Arguments("/ja/games", "ja")]
    [Arguments("/zh-Hans/games", "zh-Hans")]
    public async Task TheDocumentDeclaresTheLanguageItIsAnsweredIn(string path, string expected)
    {
        await using var site = await SiteHost.StartAsync();

        var markup = await site.Client.GetStringAsync(path);

        await Assert.That(markup).Contains($"<html lang=\"{expected}\"");
    }

    /// <summary>
    /// A locale is a property of a document, so nothing else is ever moved to reach one.
    /// </summary>
    /// <remarks>
    /// <b>The locale was a one-way door, and the switcher out of it was the thing it shut.</b> A
    /// blanket redirect on every unprefixed request (including POST) meant a browser followed a 302
    /// as a GET, dropped the body, and hit a route nothing served — so a German reader could change
    /// neither the theme nor the language back. The API and crawler files are excluded separately:
    /// they aren't documents in a language.
    /// </remarks>
    [Test]
    public async Task AChoiceOfLocaleNeverMovesARequestThatIsNotADocument()
    {
        await using var site = await SiteHost.StartAsync();

        // The theme is written, rather than the request being bounced to a URL nothing serves.
        var theme = new HttpRequestMessage(HttpMethod.Post, "/theme")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("theme", "light"),
                new KeyValuePair<string, string>("return", "/games"),
            ]),
        };
        theme.Headers.Add("Cookie", $"{Locales.CookieName}=de");

        var themed = await site.Client.SendAsync(theme);

        await Assert.That(themed.Headers.TryGetValues("Set-Cookie", out var themeCookies)).IsTrue();
        await Assert.That(string.Join(' ', themeCookies!)).Contains("mui_theme=light");

        var switched = new HttpRequestMessage(HttpMethod.Post, Locales.Path)
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>(Locales.Field, "nl"),
                new KeyValuePair<string, string>(Locales.ReturnField, "/games"),
            ]),
        };
        switched.Headers.Add("Cookie", $"{Locales.CookieName}=de");

        var moved = await site.Client.SendAsync(switched);

        await Assert.That(moved.Headers.TryGetValues("Set-Cookie", out var localeCookies)).IsTrue();
        await Assert.That(string.Join(' ', localeCookies!)).Contains($"{Locales.CookieName}=nl");
        await Assert.That(moved.Headers.Location?.OriginalString).IsEqualTo("/nl/games");
    }

    /// <summary>
    /// Picking English pins it exactly like every other locale, rather than clearing the cookie and
    /// leaving a later <c>Accept-Language</c> guess free to move the reader again.
    /// </summary>
    [Test]
    public async Task AChoiceOfEnglishSticksInsteadOfClearingTheCookie()
    {
        await using var site = await SiteHost.StartAsync();

        var switched = new HttpRequestMessage(HttpMethod.Post, Locales.Path)
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>(Locales.Field, "en"),
                new KeyValuePair<string, string>(Locales.ReturnField, "/games"),
            ]),
        };
        switched.Headers.Add("Cookie", $"{Locales.CookieName}=de");

        var moved = await site.Client.SendAsync(switched);

        await Assert.That(moved.Headers.TryGetValues("Set-Cookie", out var localeCookies)).IsTrue();
        var setCookie = string.Join(' ', localeCookies!);
        await Assert.That(setCookie).Contains($"{Locales.CookieName}=en");
        await Assert.That(setCookie).DoesNotContain("1970");

        // A header whose own top preference is a different, offered locale must not move a reader off
        // the English they just pinned — the whole point of pinning it. Ranked above English (not
        // just present), so this is a genuine conflict Preferred() would resolve away from English.
        var competing = Locales.Offered.First(locale => locale.Tag != Locales.SourceTag);

        var revisit = new HttpRequestMessage(HttpMethod.Get, "/games");
        revisit.Headers.Add("Cookie", $"{Locales.CookieName}=en");
        revisit.Headers.Add("Accept-Language", $"{competing.Tag};q=1,en;q=0.9");

        var answered = await site.Client.SendAsync(revisit);

        await Assert.That((int)answered.StatusCode).IsEqualTo(200);
    }

    /// <summary>A document still follows the reader's choice, which is the rule the guard protects.</summary>
    [Test]
    public async Task AChoiceOfLocaleStillMovesADocument()
    {
        await using var site = await SiteHost.StartAsync();

        var asked = new HttpRequestMessage(HttpMethod.Get, "/games");
        asked.Headers.Add("Cookie", $"{Locales.CookieName}=de");

        var answered = await site.Client.SendAsync(asked);

        await Assert.That((int)answered.StatusCode).IsEqualTo(302);
        await Assert.That(answered.Headers.Location?.OriginalString).IsEqualTo("/de/games");
    }

    /// <summary>
    /// A redirect re-emits the address that was asked for, and not a decoded reading of it.
    /// </summary>
    /// <remarks>
    /// <c>Request.Path.Value</c> is decoded, so writing it back into a <c>Location</c> header changes
    /// what the URL means: <c>%2F</c> becomes a separator, <c>%23</c> truncates the rest as a
    /// fragment, and non-ASCII comes back raw into a header field that can't carry it.
    /// </remarks>
    [Test]
    [Arguments("/g/a%2Fb")]
    [Arguments("/g/caf%C3%A9")]
    [Arguments("/g/one%23two")]
    [Arguments("/g/what%3Fnow")]
    public async Task AnEscapedPathSurvivesTheRedirectItIsSentThrough(string path)
    {
        await using var site = await SiteHost.StartAsync();

        var asked = new HttpRequestMessage(HttpMethod.Get, path);
        asked.Headers.Add("Cookie", $"{Locales.CookieName}=de");

        var answered = await site.Client.SendAsync(asked);

        await Assert.That((int)answered.StatusCode).IsEqualTo(302);
        await Assert.That(answered.Headers.Location?.OriginalString).IsEqualTo("/de" + path);
    }

    /// <summary>And the same going the other way, off the /en prefix that redirects to no prefix.</summary>
    [Test]
    [Arguments("/en/g/a%2Fb", "/g/a%2Fb")]
    [Arguments("/en/g/caf%C3%A9", "/g/caf%C3%A9")]
    public async Task AnEscapedPathSurvivesTheRedirectOffTheEnglishPrefix(string asked, string expected)
    {
        await using var site = await SiteHost.StartAsync();

        var answered = await site.Client.GetAsync(asked);

        await Assert.That((int)answered.StatusCode).IsEqualTo(301);
        await Assert.That(answered.Headers.Location?.OriginalString).IsEqualTo(expected);
    }

    /// <summary>
    /// A file is the same file in every language, including the ones whose names we cannot know.
    /// </summary>
    /// <remarks>
    /// The stylesheet carries a content fingerprint, so no fixed path list can name it. Recognised by
    /// extension rather than "the last segment has a dot", since <c>{Slug}</c> is a route parameter
    /// and a game's slug is not this rule's to constrain.
    /// </remarks>
    [Test]
    [Arguments("/app.gt0hup1p9v.css")]
    [Arguments("/passkey.js")]
    [Arguments("/apple-touch-icon.png")]
    [Arguments("/favicon.svg")]
    [Arguments("/site.webmanifest")]
    [Arguments("/robots.txt")]
    [Arguments("/sitemap.xml")]
    public async Task AFingerprintedFileIsNeverGivenALocalePrefix(string path)
    {
        await Assert.That(LocaleRouting.IsUnlocalized(path)).IsTrue();
    }

    /// <summary>And a document is still a document, whatever is in its slug.</summary>
    [Test]
    [Arguments("/games")]
    [Arguments("/g/m-u-s-h")]
    [Arguments("/reference/protocols/mssp")]
    [Arguments("/g/a-game-with.no-extension")]
    public async Task ADocumentIsNeverMistakenForAFile(string path)
    {
        await Assert.That(LocaleRouting.IsUnlocalized(path)).IsFalse();
    }

    /// <summary>The read API and the crawler's files have one address each, in every language.</summary>
    [Test]
    [Arguments("/api/games")]
    [Arguments("/robots.txt")]
    [Arguments("/sitemap.xml")]
    public async Task AFileThatIsTheSameInEveryLanguageIsNotGivenALocalePrefix(string path)
    {
        await using var site = await SiteHost.StartAsync();

        var asked = new HttpRequestMessage(HttpMethod.Get, path);
        asked.Headers.Add("Cookie", $"{Locales.CookieName}=de");

        var answered = await site.Client.SendAsync(asked);

        await Assert.That((int)answered.StatusCode).IsNotEqualTo(302);
    }

    // ── links out of a locale ────────────────────────────────────────────────────────────────

    /// <summary>
    /// One address, in the locale the page carrying it is read in.
    /// </summary>
    /// <remarks>
    /// An app path takes the prefix, the source locale takes none, and four shapes are left exactly
    /// as they arrived: a query-only address (<em>this page, asked differently</em>), a file with one
    /// canonical address, an absolute URL, and a protocol-relative one.
    /// </remarks>
    [Test]
    [Arguments("en", "/games", "/games")]
    [Arguments("de", "/games", "/de/games")]
    [Arguments("de", "/", "/de/")]
    [Arguments("de", "/g/ashen-court", "/de/g/ashen-court")]
    [Arguments("de", "/games?sort=busiest", "/de/games?sort=busiest")]
    [Arguments("zh-Hans", "/rankings", "/zh-Hans/rankings")]
    [Arguments("de", "?plain=1", "?plain=1")]
    [Arguments("de", "?window=30d&plain=1", "?window=30d&plain=1")]
    [Arguments("de", "#content", "#content")]
    [Arguments("de", "/api/games", "/api/games")]
    [Arguments("de", "/robots.txt", "/robots.txt")]
    [Arguments("de", "/sitemap.xml", "/sitemap.xml")]
    [Arguments("de", "/favicon.svg", "/favicon.svg")]
    [Arguments("de", "/site.webmanifest", "/site.webmanifest")]
    [Arguments("de", "/api/games?limit=5", "/api/games?limit=5")]
    [Arguments("de", "https://example.com/games", "https://example.com/games")]
    [Arguments("de", "mailto:someone@example.com", "mailto:someone@example.com")]
    [Arguments("de", "//elsewhere.example/games", "//elsewhere.example/games")]
    [Arguments("de", "", "")]
    public async Task AnAddressIsWrittenInTheLocaleThePageIsReadIn(string tag, string given, string expected)
    {
        await Assert.That(LocaleRouting.Link(tag, given)).IsEqualTo(expected);
    }

    /// <summary>
    /// Every link on a localized page stays inside that locale.
    /// </summary>
    /// <remarks>
    /// <b>Every internal link was an absolute path emitted verbatim, so a German page's links all
    /// pointed out of German.</b> A reader who followed a shared <c>/de/…</c> link carries no cookie,
    /// so nothing could send them back. Swept over every anchor and form on the page (the bug was
    /// fifty separate link sites) rather than spot-checked, and asked with no cookie — this is the
    /// reader who was sent a link.
    /// </remarks>
    [Test]
    [Arguments("/de/")]
    [Arguments("/de/games")]
    [Arguments("/de/games?sort=busiest")]
    [Arguments("/de/archive")]
    [Arguments("/de/find")]
    [Arguments("/de/rankings")]
    [Arguments("/de/ecosystem")]
    [Arguments("/de/about")]
    [Arguments("/de/reference")]
    [Arguments("/de/reference/codebases/pennmush")]
    [Arguments("/de/submit")]
    [Arguments("/de/g/ashen-court")]
    public async Task EveryLinkOnALocalizedPageStaysInThatLocale(string path)
    {
        await using var site = await SiteHost.StartAsync();

        var markup = await site.Client.GetStringAsync(path);
        var addresses = Addresses(markup);

        await Assert.That(addresses.Count).IsGreaterThan(3);

        foreach (var address in addresses)
        {
            if (LeavesThisSite(address) || LocaleRouting.IsUnlocalized(PathOf(address)))
            {
                continue;
            }

            await Assert.That(address)
                .StartsWith("/de/")
                .Because($"{path} offers {address}, which leads out of German");
        }
    }

    /// <summary>And the source locale is the absence of a prefix, on the same pages.</summary>
    /// <remarks><c>/games</c> is the canonical English address and <c>/en/games</c> redirects to it, so a prefix here would be a second URL for a document that already has one.</remarks>
    [Test]
    [Arguments("/")]
    [Arguments("/games")]
    [Arguments("/archive")]
    [Arguments("/find")]
    [Arguments("/rankings")]
    [Arguments("/g/ashen-court")]
    public async Task TheSourceLocaleIsTheAbsenceOfAPrefix(string path)
    {
        await using var site = await SiteHost.StartAsync();

        var markup = await site.Client.GetStringAsync(path);

        foreach (var address in Addresses(markup))
        {
            foreach (var locale in Locales.All)
            {
                await Assert.That(address.StartsWith($"/{locale.Tag}/", StringComparison.Ordinal))
                    .IsFalse()
                    .Because($"{path} offers {address}, a second address for a page that has one");
            }
        }
    }

    /// <summary>
    /// A query-only address means <em>this page, asked differently</em>, in every language.
    /// </summary>
    /// <remarks>Written as a bare querystring so it resolves against the page it's on; prefixing it would make <c>/de/?plain=1</c> the home page, not the listing the reader was reading.</remarks>
    [Test]
    [Arguments("/de/games")]
    [Arguments("/de/g/ashen-court")]
    [Arguments("/de/rankings")]
    public async Task AQueryOnlyAddressIsLeftExactlyAsItWasWritten(string path)
    {
        await using var site = await SiteHost.StartAsync();

        var markup = await site.Client.GetStringAsync(path);
        var queries = Addresses(markup).Where(a => a.StartsWith('?')).ToList();

        await Assert.That(queries.Count).IsGreaterThan(0);

        foreach (var query in queries)
        {
            await Assert.That(query).DoesNotContain("/de");
        }
    }

    /// <summary>
    /// A file with one canonical address never gets a prefix, whatever language the page is in.
    /// </summary>
    /// <remarks>The head's own links aren't swept above (a stylesheet isn't a document in a language), so this asserts the list directly against the markup.</remarks>
    [Test]
    public async Task AFileWithOneAddressIsNeverPrefixedInTheMarkupEither()
    {
        await using var site = await SiteHost.StartAsync();

        var markup = await site.Client.GetStringAsync("/de/games");

        foreach (var file in new[] { "/favicon.svg", "/favicon.ico", "/site.webmanifest" })
        {
            await Assert.That(markup).Contains($"href=\"{file}\"");
            await Assert.That(markup).DoesNotContain($"href=\"/de{file}\"");
        }
    }

    /// <summary>
    /// A control that answers with a <c>Location</c> header keeps the reader's language too.
    /// </summary>
    /// <remarks>
    /// <b>These are the links a sweep of the markup cannot see</b>: "surprise me" from
    /// <c>/de/games</c> answered with <c>Location: /g/eldertale</c>, putting a reader two clicks into
    /// German back into English via a header rather than an attribute. No cookie on any of these,
    /// deliberately — a reader who followed a shared link has none, so the path is the only thing
    /// that knows the language.
    /// </remarks>
    [Test]
    public async Task AControlThatAnswersWithARedirectKeepsTheReadersLanguage()
    {
        await using var site = await SiteHost.StartAsync();

        var random = await site.Client.GetAsync("/de/games/random");

        await Assert.That((int)random.StatusCode).IsEqualTo(302);
        await Assert.That(Where(random)).StartsWith("/de/g/");

        var theme = new HttpRequestMessage(HttpMethod.Post, "/de/theme")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("theme", "light"),
                new KeyValuePair<string, string>("return", "/games"),
            ]),
        };

        var themed = await site.Client.SendAsync(theme);

        await Assert.That((int)themed.StatusCode).IsEqualTo(303);
        await Assert.That(themed.Headers.Location?.OriginalString).IsEqualTo("/de/games");

        var english = await site.Client.GetAsync("/games/random");

        await Assert.That(Where(english)).StartsWith("/g/");
        await Assert.That(Where(english)).DoesNotContain("/de/");
    }

    /// <summary>
    /// The plain surface prints its addresses in the locale it is printing.
    /// </summary>
    /// <remarks>
    /// <b>They are links, whatever they look like.</b> No anchors on this surface — a path is printed
    /// to type, follow in a text browser, or paste — so a German reader copying an English address
    /// off the plain surface is the same defect as the fifty in the markup.
    /// </remarks>
    [Test]
    public async Task ThePlainSurfacePrintsItsAddressesInTheLocaleItIsPrintingIn()
    {
        await using var site = await SiteHost.StartAsync();

        var listing = await site.Client.GetStringAsync("/de/games?plain=1");
        var game = await site.Client.GetStringAsync("/de/g/ashen-court?plain=1");
        var english = await site.Client.GetStringAsync("/games?plain=1");

        await Assert.That(listing).Contains("/de/g/ashen-court");
        await Assert.That(english).Contains("/g/ashen-court");
        await Assert.That(english).DoesNotContain("/de/g/ashen-court");

        await Assert.That(game).Contains("?from=");
        await Assert.That(game).DoesNotContain("/de?from=");
    }

    /// <summary>
    /// A reference article is the article in every locale, and not the section's empty state.
    /// </summary>
    /// <remarks>
    /// <c>NavigationManager.Uri</c>'s local path still carries the prefix the middleware moved into
    /// <c>PathBase</c>, so looking the article up by it asked for a document filed under the
    /// unprefixed path, found none, and answered every non-English reader with "no reference page
    /// here" for all thirty-odd articles.
    /// </remarks>
    [Test]
    [Arguments("/reference/codebases/pennmush")]
    [Arguments("/de/reference/codebases/pennmush")]
    [Arguments("/ja/reference/codebases/pennmush")]
    public async Task AReferenceArticleIsFoundInEveryLocale(string path)
    {
        await using var site = await SiteHost.StartAsync();

        var markup = await site.Client.GetStringAsync(path);

        await Assert.That(markup).Contains("PennMUSH");
        await Assert.That(Render.Words(markup)).DoesNotContain("No reference page here");
    }

    /// <summary>Every anchor and every form on a rendered page, as the browser receives them.</summary>
    /// <remarks>Read off the frame rather than the source — the components held the right paths, the markup is what pointed out of the locale.</remarks>
    private static IReadOnlyList<string> Addresses(string markup) =>
    [
        .. System.Text.RegularExpressions.Regex
            .Matches(markup, "<a\\s[^>]*?href=\"([^\"]*)\"|<form\\s[^>]*?action=\"([^\"]*)\"")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Select(a => System.Net.WebUtility.HtmlDecode(a) ?? string.Empty)
            .Where(a => a.Length > 0),
    ];

    /// <summary>
    /// Where a redirect sends the reader, as a path.
    /// </summary>
    /// <remarks>An endpoint writes a relative <c>Location</c>; a page redirecting through <c>NavigationManager</c> gets an absolute one. Both are correct — this normalizes to where the reader lands.</remarks>
    private static string Where(HttpResponseMessage response) =>
        response.Headers.Location is not { } location ? string.Empty
        : location.IsAbsoluteUri ? location.AbsolutePath
        : location.OriginalString;

    /// <summary>Whether an address names somewhere that is not a page of this site.</summary>
    private static bool LeavesThisSite(string address) =>
        address[0] != '/' || (address.Length > 1 && address[1] is '/' or '\\');

    private static string PathOf(string address)
    {
        var cut = address.AsSpan().IndexOfAny('?', '#');

        return cut < 0 ? address : address[..cut];
    }

    /// <summary>
    /// A sentence with links in it is one message, and what a translator writes is never markup.
    /// </summary>
    /// <remarks>
    /// Gluing English word order around the anchors would give a language wanting different order or
    /// prepositions nowhere to say so; formatting anchors into the string and trusting it through
    /// <c>MarkupString</c> would make every bundle a place a tag could be put. Instead the message
    /// places two private-use markers and the page walks them.
    /// </remarks>
    [Test]
    public async Task ASentenceWithLinksInItPlacesThemWithoutCarryingMarkup()
    {
        foreach (var id in new[] { "random.empty.title", "random.empty.body", "random.empty.listing", "random.empty.archive" })
        {
            await Assert.That(Messages.Ids).Contains(id);
        }

        foreach (var locale in Locales.All.Where(l => l.IsChoosable))
        {
            var body = Messages.Pattern(locale.Tag, "random.empty.body");

            await Assert.That(body).Contains("{listing}").Because($"{locale.Tag} must place the listing link");
            await Assert.That(body).Contains("{archive}").Because($"{locale.Tag} must place the archive link");
            await Assert.That(body).DoesNotContain("<").Because($"{locale.Tag} must carry no markup");
        }
    }

    [Test]
    public async Task TheAlternatesNameEveryOfferedLocaleAndAnXDefault()
    {
        var alternates = LocaleRouting.Alternates("/games").ToList();

        await Assert.That(alternates.Select(a => a.HrefLang)).Contains("x-default");

        // x-default is the unprefixed address, shown to a reader whose language matches nothing here.
        await Assert.That(alternates.Single(a => a.HrefLang == "x-default").Path).IsEqualTo("/games");

        foreach (var locale in Locales.Offered)
        {
            var mine = alternates.Single(a => a.HrefLang == locale.Tag);

            await Assert.That(mine.Path).IsEqualTo(
                locale.Tag == Locales.SourceTag ? "/games" : $"/{locale.Tag}/games");
        }
    }

    [Test]
    public async Task ARenderOnlyScriptIsNeverAnInterfaceLanguage()
    {
        // An Arabic game name inside an English page (<bdi> + per-element lang) is correct
        // permanently; a whole Arabic interface still flowing left-to-right is not, so it's not offered.
        foreach (var script in Locales.RenderOnlyScripts)
        {
            await Assert.That(Locales.Offered.Any(l => l.Tag.StartsWith(script, StringComparison.Ordinal)))
                .IsFalse()
                .Because($"{script} is render-only and must not be an interface language");
        }
    }

    /// <summary>
    /// The plural forms a locale needs for a message and does not declare.
    /// </summary>
    /// <remarks>
    /// An <c>other</c> branch does not excuse a missing form — ICU will happily route a Russian 2
    /// through <c>other</c> silently. Only what the locale itself carries is checked: an untranslated
    /// string is a fallback, a half-translated plural is a bug, and only the second is this test's
    /// business.
    /// </remarks>
    private static IReadOnlyList<string> Missing(string tag, string id)
    {
        if (!Messages.HasOwn(tag, id))
        {
            return [];
        }

        var pattern = IcuMessage.Compile(Messages.Pattern(tag, id)!);

        return
        [
            .. pattern.Arguments()
                .Where(a => a.Kind is ArgumentKind.Plural or ArgumentKind.SelectOrdinal)
                .SelectMany(a => PluralRules
                    .CategoriesOf(tag, a.Kind is ArgumentKind.SelectOrdinal
                        ? PluralKind.Ordinal
                        : PluralKind.Cardinal)
                    .Select(PluralRules.Keyword)
                    .Where(form => form != "other" && !a.Branches.ContainsKey(form))
                    .Select(form => $"{a.Name}:{form}")),
        ];
    }

    [Test]
    public async Task EveryPatternInEveryBundleParses()
    {
        // A message with a missing `other`, unbalanced brace, or unused branch keyword is broken for
        // exactly the one reader whose count reaches it; parsing every bundle here turns that into a
        // build failure instead.
        foreach (var locale in Locales.All)
        {
            foreach (var id in Messages.Ids)
            {
                if (!Messages.HasOwn(locale.Tag, id))
                {
                    continue;
                }

                var pattern = Messages.Pattern(locale.Tag, id)!;

                await Assert.That(() => IcuMessage.Compile(pattern))
                    .ThrowsNothing()
                    .Because($"{locale.Tag} / {id} does not parse: {pattern}");
            }
        }
    }

    [Test]
    public async Task EveryTranslationReadsTheSameArgumentsTheEnglishDoes()
    {
        // A translator who drops {total} leaves a fact off the page; one who invents {name} writes a
        // message that refuses to render at all. Both caught here.
        foreach (var locale in Locales.All.Where(l => l.Tag != Locales.SourceTag))
        {
            foreach (var id in Messages.Ids.Where(i => Messages.HasOwn(locale.Tag, i)))
            {
                var source = Names(Messages.Pattern(Locales.SourceTag, id)!);
                var mine = Names(Messages.Pattern(locale.Tag, id)!);

                await Assert.That(mine.Except(source))
                    .IsEmpty()
                    .Because($"{locale.Tag} / {id} names an argument the English does not supply");
            }
        }

        static IReadOnlyList<string> Names(string pattern) =>
            [.. IcuMessage.Compile(pattern).Arguments().Select(a => a.Name).Distinct()];
    }

    [Test]
    public async Task EveryLocaleTheSiteNamesHasAPluralRuleWrittenForIt()
    {
        // An unlisted language answers `other` for every count, silently — right for Chinese, wrong for German.
        foreach (var locale in Locales.All)
        {
            await Assert.That(PluralRules.Covers(locale.Tag))
                .IsTrue()
                .Because($"{locale.Tag} has no plural rule transcribed from CLDR {PluralRules.CldrVersion}");
        }
    }

    [Test]
    public async Task TheResxAndTheCompiledInSourceSayTheSameThing()
    {
        // Two copies of English exist on purpose: resx is where a translation lives, the compiled-in
        // bundle is the fallback that must not depend on a satellite assembly loading. With no test
        // between them, a message gets fixed in one and not the other.
        var resx = ResxMessages();

        await Assert.That(resx).IsNotEmpty();

        foreach (var id in Messages.Ids)
        {
            await Assert.That(resx.ContainsKey(id))
                .IsTrue()
                .Because($"{id} is in the source bundle and not in Messages.resx");

            await Assert.That(resx[id])
                .IsEqualTo(Messages.Source(id))
                .Because($"{id} differs between Messages.resx and the compiled-in source");
        }

        foreach (var id in resx.Keys)
        {
            await Assert.That(Messages.Ids)
                .Contains(id)
                .Because($"{id} is in Messages.resx and nothing on the site says it");
        }
    }

    /// <summary>
    /// The English resx, read as XML rather than through the resource manager.
    /// </summary>
    /// <remarks>
    /// Deliberately not through <see cref="Microsoft.Extensions.Localization.IStringLocalizer"/>,
    /// which would read whatever the build embedded — the thing being checked. Reading the file
    /// compares what a translator would edit against what the site compiles.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> ResxMessages()
    {
        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Messages.resx");

        var document = System.Xml.Linq.XDocument.Load(path);

        return document.Root!
            .Elements("data")
            .Where(e => e.Attribute("name") is not null)
            .ToDictionary(
                e => e.Attribute("name")!.Value,
                e => e.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    [Test]
    public async Task ALocaleIsOfferedWhenItHasTheWordsAndNotBefore()
    {
        // `Offered` is what a default may reach (Accept-Language, a cookie, an hreflang alternate);
        // what opens it is the bundle carrying the locked glossary, not a claim of having read it.
        foreach (var locale in Locales.Offered.Where(l => l.Tag != Locales.SourceTag))
        {
            foreach (var locked in Glossary.Locked)
            {
                await Assert.That(Messages.HasOwn(locale.Tag, locked.Id))
                    .IsTrue()
                    .Because($"{locale.Tag} is offered without a translation for {locked.Id}");
            }
        }

        foreach (var locale in Locales.All.Where(l => l.Status is LocaleStatus.Planned))
        {
            await Assert.That(locale.IsOffered)
                .IsFalse()
                .Because($"{locale.Tag} is planned and has no words yet");
        }

        // A review locale is reachable by choice and never by default — a reader must not be sent to
        // a pseudolocale by their browser's settings.
        foreach (var locale in Locales.All.Where(l => l.Status is LocaleStatus.TestOnly))
        {
            await Assert.That(locale.IsOffered)
                .IsFalse()
                .Because($"{locale.Tag} is a review locale and must not be offered");
        }

        await Assert.That(LocaleRouting.Preferred("qps-ploc,ru;q=0.9")).IsNull();

        // Alternates name only what's offered — an hreflang pointing at a pseudolocale would invite a
        // search engine to index accented English as a language.
        var alternates = LocaleRouting.Alternates("/games").Select(a => a.HrefLang).ToList();

        await Assert.That(alternates).DoesNotContain("qps-ploc");
        await Assert.That(alternates).DoesNotContain("ru-x-canary");
    }

    [Test]
    public async Task AReviewBuildListsTheLocalesThatAreNotLanguages()
    {
        var production = Locales.Switchable(preview: false);
        var review = Locales.Switchable(preview: true);

        // A machine-translated locale is in both. The pseudolocale and canary are in neither — not languages.
        await Assert.That(production.Select(l => l.Tag)).Contains("de");
        await Assert.That(production.Select(l => l.Tag)).DoesNotContain("qps-ploc");
        await Assert.That(review.Select(l => l.Tag)).Contains("qps-ploc");
        await Assert.That(review.Count).IsGreaterThan(production.Count);

        // A planned locale is in neither: listing it would offer a page still English under a name
        // saying it isn't.
        await Assert.That(review.Select(l => l.Tag)).DoesNotContain("ru");
    }

    [Test]
    public async Task WhetherTheReviewLocalesAreListedIsAskedOfTheRequestAndNotOfTheProcess()
    {
        // Previously a static flag every host start wrote — this suite starts hosts in both
        // Development and Production in one process, so the switcher listed whatever the last host
        // decided, for every request.
        var review = Request(Environments.Development);
        var production = Request(Environments.Production);

        await Assert.That(review.IsReviewBuild()).IsTrue();
        await Assert.That(production.IsReviewBuild()).IsFalse();

        await Assert.That(Locales.Switchable(review.IsReviewBuild()).Select(l => l.Tag)).Contains("qps-ploc");
        await Assert.That(Locales.Switchable(production.IsReviewBuild()).Select(l => l.Tag)).DoesNotContain("qps-ploc");

        // A component rendered with no request at all (every headless component test) is not a
        // review build — why this is asked of the request rather than injected as a dependency.
        await Assert.That(((HttpContext?)null).IsReviewBuild()).IsFalse();
    }

    /// <summary>A request served by a host running in one named environment.</summary>
    private static HttpContext Request(string environment)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new StubEnvironment { EnvironmentName = environment });

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;

        public string ApplicationName { get; set; } = "MUI.Web.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Test]
    public async Task AChosenReviewLocaleIsHonouredEvenThoughItIsNeverOffered()
    {
        // The difference between offering a locale and honouring a choice: nothing sends a reader to
        // the pseudolocale, but having picked it, they stay in it.
        await Assert.That(Locales.Find("qps-ploc")!.IsOffered).IsFalse();
        await Assert.That(Locales.Find("qps-ploc")!.IsChoosable).IsTrue();

        await Assert.That(Locales.Find("de")!.IsOffered).IsTrue();
        await Assert.That(Locales.Find("de")!.IsChoosable).IsTrue();

        await Assert.That(Locales.Find("ru")!.IsChoosable).IsFalse();
        await Assert.That(Locales.Find("ru")!.IsOffered).IsFalse();
    }

    [Test]
    public async Task EveryOfferedLocaleTranslatedTheWholeGlossary()
    {
        // A bundle stopped halfway would leave a page in two languages, the English half being
        // exactly the provenance words a reader most needs to trust.
        foreach (var locale in Locales.All.Where(l => l.IsOffered && l.Tag != Locales.SourceTag))
        {
            foreach (var locked in Glossary.Locked)
            {
                await Assert.That(Messages.HasOwn(locale.Tag, locked.Id))
                    .IsTrue()
                    .Because($"{locale.Tag} has no {locked.Id}");
            }
        }
    }

    [Test]
    public async Task TheFourKindsOfAbsenceStayFourInEveryTranslation()
    {
        // Checked against what translators actually wrote, not just the source bundle.
        string[] ids = ["state.notMeasured", "state.uncounted", "state.unreachable", "state.notCounted"];

        foreach (var locale in Locales.All.Where(l => l.IsOffered && l.Tag != Locales.SourceTag))
        {
            var rendered = ids.Select(id => Messages.For(locale.Tag, id)).ToList();

            await Assert.That(rendered.Distinct().Count())
                .IsEqualTo(ids.Length)
                .Because($"{locale.Tag} collapsed two of the four kinds of absence: {string.Join(" / ", rendered)}");
        }
    }
}
