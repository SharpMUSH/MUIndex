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
/// Nothing here asserts that a translation is <em>good</em> — no test can, and the handoff is
/// explicit that Han glyph correctness and register both need review by somebody who reads the
/// language. What these hold is the part a machine can: that a message reaches every form its locale
/// needs, that a locked string cannot quietly become a paraphrase, and that a locale nobody has
/// translated is never offered to a reader.
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
        // 11 and 12 end in 1 and 2 and take neither `one` nor `few`. A rule written from the first
        // three examples anybody tries is wrong for both, which is why the canary exists.
        await Assert.That(PluralRules.Keyword(PluralRules.Of("ru", count))).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(5)]
    public async Task ChineseHasOneFormAndSoCannotFailAnAgreementBug(int count)
    {
        // The reason Chinese ships first and Russian goes in CI. Chinese agrees with any string
        // architecture, including one that is wrong for every inflected language.
        await Assert.That(PluralRules.Of("zh-Hans", count)).IsEqualTo(PluralCategory.Other);
        await Assert.That(PluralRules.CategoriesOf("zh-Hans").Count).IsEqualTo(1);
    }

    [Test]
    public async Task AScriptOrPrivateSubtagDoesNotChangeHowANumberAgrees()
    {
        // zh-Hans and zh-Hant draw different glyphs; ru and the canary's ru-x-canary read different
        // bundles. Neither changes the arithmetic.
        await Assert.That(PluralRules.Of("zh-Hant", 5)).IsEqualTo(PluralRules.Of("zh", 5));
        await Assert.That(PluralRules.Of("ru-x-canary", 2)).IsEqualTo(PluralRules.Of("ru", 2));
    }

    // ── plural rules, checked against the CLDR 46 chart rather than against memory ────────────

    [Test]
    public async Task TurkishSaysOneForOneAndForNothingElse()
    {
        // CLDR 46 tr — one: n = 1. The table said `n = 0..1`, which is a real rule in CLDR and
        // belongs to Akan and Punjabi, not to Turkish. It put zero in the form Turkish reserves
        // for exactly one thing.
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
        // 1.0 is the only value that tells the two shapes apart, and transcribing every language
        // into `i = 1 and v = 0` because English is written that way is wrong for six of the
        // thirty-one here. It is also the exact error the operands in this file exist to catch.
        var oneDotZero = PluralOperands.Of(1.0m, visibleFractionDigits: 1);

        await Assert.That(PluralRules.Keyword(PluralRules.Of(tag, oneDotZero))).IsEqualTo(expected);
    }

    [Test]
    public async Task DanishSaysOneForAFractionBelowTwo()
    {
        // CLDR 46 da — one: n = 1 or t != 0 and i = 0,1. Danish is the only language in this table
        // whose `one` reaches a value that is not 1, and a rule copied from Swedish loses it.
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
        // many: e = 0 and i != 0 and i % 1000000 = 0 and v = 0 — "un millón de juegos" takes a
        // preposition the other forms do not. fr and pt had it; es and it were given English's
        // rule and so had no `many` at all.
        await Assert.That(PluralRules.Keyword(PluralRules.Of(tag, 1_000_000))).IsEqualTo("many");
        await Assert.That(PluralRules.Keyword(PluralRules.Of(tag, 2_000_000))).IsEqualTo("many");
        await Assert.That(PluralRules.Keyword(PluralRules.Of(tag, 1_000_001))).IsEqualTo("other");
    }

    [Test]
    public async Task HebrewLostItsManyFormAndAllOfItsOrdinalsBeforeCldr46()
    {
        // Both were removed upstream, and a table still carrying them selects a branch no
        // translator was asked to write — the failure this file's `other` fallback cannot catch,
        // because the branch exists and is simply wrong.
        await Assert.That(PluralRules.Keyword(PluralRules.Of("he", 20))).IsEqualTo("other");
        await Assert.That(PluralRules.Keyword(PluralRules.Of("he", 100))).IsEqualTo("other");

        await Assert.That(PluralRules.CategoriesOf("he", PluralKind.Ordinal))
            .IsEquivalentTo(new[] { PluralCategory.Other });

        // one: i = 1 and v = 0 or i = 0 and v != 0 — the second clause was missing entirely.
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
        // An ordinal rule missing from the table is not a missing translation — it is every rank
        // rendered in the `other` form, silently, in a language that inflects them.
        await Assert.That(PluralRules.Keyword(PluralRules.Of(tag, n, PluralKind.Ordinal))).IsEqualTo(expected);
    }

    [Test]
    public async Task ACldrRangeMatchesAWholeNumberAndNothingElse()
    {
        // `n % 100 = 3..10` is a range over integers: 3.5 is not in it. Reading it as `i % 100`
        // makes every fraction take the category of its integer part.
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
        // The panel shipped "1 games" and "0 games" on every accessible name until this existed.
        await Assert.That(Messages.Count("en", count)).IsEqualTo(expected);
    }

    [Test]
    public async Task TheSameMessageReachesADifferentFormInADifferentLocale()
    {
        // One message, one argument, three answers — which is the whole point of the message being
        // a message rather than a number glued to a word.
        await Assert.That(Messages.Count("ru-x-canary", 1)).IsEqualTo("1 игра");
        await Assert.That(Messages.Count("ru-x-canary", 2)).IsEqualTo("2 игры");
        await Assert.That(Messages.Count("ru-x-canary", 5)).IsEqualTo("5 игр");
    }

    [Test]
    public async Task AnExactMatchWinsOverACategory()
    {
        // `=0` is how a message says "no games" rather than "0 games" without inventing a plural
        // category that CLDR does not give English.
        var listing = Messages.For("en", "listing.total", new Dictionary<string, object?> { ["count"] = 0 });

        await Assert.That(listing).StartsWith("No games");
    }

    [Test]
    public async Task ABranchMayHoldAnotherArgument()
    {
        // "1 of 6 disagree" nests {total} inside a plural branch. A parser that stopped at the first
        // closing brace would cut the branch in half and read the rest as a key.
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
        // A formatter that accepts what it cannot do fails as a wrong string rather than as a build
        // error, and a wrong string on this site is a wrong claim.
        await Assert.That(() => IcuMessage.Format("{n, date, short}", "en", new Dictionary<string, object?> { ["n"] = 1 }))
            .Throws<FormatException>();

        await Assert.That(() => IcuMessage.Format("{missing}", "en"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task AHashOutsideAPluralBranchIsALiteral()
    {
        // ICU says so, and a game called "#1 MUSH" would otherwise render its own count.
        await Assert.That(IcuMessage.Format("channel #general", "en")).IsEqualTo("channel #general");
    }

    // ── the locked glossary ──────────────────────────────────────────────────────────────────

    [Test]
    public async Task EveryLockedIdIsAMessageTheSiteActuallySays()
    {
        // A glossary entry with no message behind it is a promise to translators about a string
        // that does not exist, and it would go on being translated for ever.
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
        // The finding this whole file exists for. "not measured", "uncounted", "unreachable" and
        // "not counted" are four claims, and a translation engine treats them as stylistic variants
        // of *unavailable* — after which a reader cannot tell a game that answered from one that did
        // not, which is the one thing this site is for.
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
        // "measured" is one string in English and four in Russian, chosen by what it describes. The
        // ids have to be granular enough for a translator to reach each case at all — collapsing
        // them because the source language cannot tell them apart is how three cases out of four end
        // up ungrammatical.
        var measured = Glossary.Locked.Where(l => l.English == "measured").ToList();

        await Assert.That(measured.Count).IsGreaterThanOrEqualTo(4);
        await Assert.That(measured.Select(m => m.Id).Distinct().Count()).IsEqualTo(measured.Count);
        await Assert.That(measured.Select(m => m.Subject).Distinct().Count()).IsGreaterThanOrEqualTo(4);

        // And the bare column header is one of them, because it modifies nothing and so takes a form
        // none of the others do.
        await Assert.That(Glossary.Of("kicker.measured")!.Subject).IsEqualTo(Subject.Standalone);
    }

    [Test]
    public async Task EveryLockedStringShipsWithTheReasonItIsLocked()
    {
        // The lock reads as distrust without it, and a well-meaning "improvement" arrives as a pull
        // request nobody knows how to refuse.
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
        // A reader meeting one English phrase inside another language learns something true. A
        // smoothed-over approximation of a locked string teaches them something false and gives them
        // no way to tell.
        await Assert.That(Messages.HasOwn("ru-x-canary", "term.connected")).IsFalse();
        await Assert.That(Messages.For("ru-x-canary", "term.connected")).IsEqualTo("connected");

        // And where it has been translated, the translation wins.
        await Assert.That(Messages.For("ru-x-canary", "kicker.measured")).IsEqualTo("ИЗМЕРЕНО");
    }

    [Test]
    public async Task TheCanaryFailsOnAMissingPluralFormRatherThanRenderingOne()
    {
        // The whole reason Russian is wired into CI while Chinese ships first. `listing.total` in
        // the canary bundle declares `one` and `other` — complete in English, and silently missing
        // the `few` form that a count of two takes in Russian.
        var incomplete = Missing("ru-x-canary", "listing.total");

        // Reported per argument, because a message with two counts in it can be complete for one
        // and not the other.
        await Assert.That(incomplete).Contains("count:few");
        await Assert.That(incomplete).Contains("count:many");

        // Where the message is complete, nothing is reported.
        await Assert.That(Missing("ru-x-canary", "facet.count")).IsEmpty();
    }

    [Test]
    public async Task EveryOfferedLocaleIsCompleteInEveryPluralFormItNeeds()
    {
        // The gate. A locale reaches LocaleStatus.Shipped only when this holds for it — which is why
        // the canary above is TestOnly and is not walked here.
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
        // Nothing ships to a reader before the glossary exists. Today that means English alone, and
        // this test is what stops somebody moving a status enum and shipping a mostly-English page
        // under a Chinese flag.
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
        // Accented and expanded, so a string that never went through the pipeline is visible at a
        // glance and every string is reviewed at the 1.4x width German and Russian actually need.
        var pseudo = Messages.Count("qps-ploc", 3);

        await Assert.That(pseudo).Contains("⟦");
        await Assert.That(pseudo).Contains("3");
        await Assert.That(pseudo.Length).IsGreaterThan(Messages.Count("en", 3).Length);

        // And the ICU syntax survived: a pseudolocale that mangled a branch keyword would prove
        // nothing, because the message would not parse.
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
    public async Task AcceptLanguageIsReadForTheFirstVisitOnly(string header, string? expected)
    {
        // A header is a standing preference about content in general, not a choice about this site —
        // worth one redirect and no more. The parse honours q-values because a browser sends them
        // meaning something, and matches on the language subtag so zh-CN reaches zh-Hans.
        //
        // Note every case answers null today, because English is the only offered locale and English
        // is where the reader already is. The rule is asserted against the offered set rather than
        // against a hard-coded answer, so this starts biting the day a locale ships.
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
        // It arrives in a form field and is written into a Location header. A protocol-relative URL
        // is a different host wearing a path's clothes and walks straight through a StartsWith('/')
        // check; several browsers read /\host the same way; a CR here is response splitting.
        // Any locale prefix comes off, because the endpoint decides which language the page is in.
        await Assert.That(LocaleRouting.Back(posted)).IsEqualTo(expected);
    }

    /// <summary>
    /// The document states the language it is written in, and it is the one the reader asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>lang</c> was the constant <c>"en"</c> while the whole page below it came back translated,
    /// so a reader on <c>/ja/games</c> was handed a fully Japanese document that told the browser it
    /// was English. That is not a tidiness point on the two locales this site shipped first: Unicode
    /// unified thousands of Han characters across Chinese and Japanese, the correct drawn form
    /// differs, and <c>lang</c> is the only thing that selects between the two font families. The
    /// wrong one renders at exactly the same width, so nothing in the overflow audit could see it.
    /// </para>
    /// <para>
    /// It is also what a screen reader switches voice on, and what the <c>hreflang</c> links in the
    /// head are claiming about this document — three separate promises that were all being made in
    /// the wrong language.
    /// </para>
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
    /// <para>
    /// <b>The locale was a one-way door, and the switcher out of it was the thing it shut.</b> The
    /// middleware answered any unprefixed request from a reader with <c>mui_locale=de</c> with a
    /// redirect to <c>/de/...</c> — including a POST. A browser follows a 302 as a GET and drops the
    /// body, and <c>/de/theme</c> is served by nothing, so a German reader could not change the
    /// theme; <c>POST /locale</c> went the same way, so they could not change the language back
    /// either. Every control on this site is a form, which is what made one redirect reach all of
    /// them.
    /// </para>
    /// <para>
    /// The API and the crawler's own files are excluded for a different reason: they are not
    /// documents in a language. <c>/api/…</c> answers the same JSON to everybody and
    /// <c>robots.txt</c> has one canonical address, which is where a crawler looks for it.
    /// </para>
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

        // And the way out of German is still open from inside German.
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
    /// <c>Request.Path.Value</c> is decoded, so writing it back into a <c>Location</c> header
    /// changes what the URL means: <c>%2F</c> comes back as a separator and makes one segment into
    /// two, <c>%23</c> comes back as a <c>#</c> and truncates everything after it, and a non-ASCII
    /// character comes back raw into a header field that cannot carry one. A game whose slug
    /// contains any of them is a game a reader cannot reach in any language but English.
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

    [Test]
    public async Task TheAlternatesNameEveryOfferedLocaleAndAnXDefault()
    {
        var alternates = LocaleRouting.Alternates("/games").ToList();

        await Assert.That(alternates.Select(a => a.HrefLang)).Contains("x-default");

        // x-default is the unprefixed address: the one to show a reader whose language nothing here
        // matches, rather than leaving a crawler to pick among them.
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
        // An Arabic game name inside an English page is correct permanently — that is <bdi> and a
        // per-element lang, and it needs nothing further. An entire Arabic interface still flowing
        // left-to-right is not, so it is not offered. The boundary is data rather than a comment, so
        // it is publishable and so this can hold it.
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
    /// <para>
    /// Read off the parsed message rather than off the string, so a branch inside a nested selector
    /// counts exactly as one at the top level does.
    /// </para>
    /// <para>
    /// An <c>other</c> branch does not excuse a missing form, and that is the point: ICU will
    /// happily route a Russian 2 through <c>other</c> and render a word no native speaker would
    /// write, silently. A form the language distinguishes has to be declared rather than fallen
    /// into.
    /// </para>
    /// <para>
    /// Only what the locale itself carries is checked. A locale that has translated nothing reports
    /// nothing missing here, which is correct: an untranslated string is a fallback and a
    /// half-translated plural is a bug, and only the second is this test's business.
    /// </para>
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
        // The whole reason the pattern is parsed rather than interpreted: a message with a missing
        // `other`, an unbalanced brace or a branch keyword no category uses is broken for exactly
        // one reader — whichever one's count reaches it. Parsing every bundle here turns all three
        // into a build failure.
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
        // A translator who drops {total} from a sentence leaves a fact off the page, and one who
        // invents {name} writes a message the site will refuse to render at all. Both are caught
        // here rather than by whoever opens that page in that language.
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
        // An unlisted language answers `other` for every count — right for Chinese, wrong for
        // German, and silent either way. This is the gate on adding one.
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
        // Two copies of the English exist on purpose — resx is where a translation lives and where
        // every translation tool looks, and the compiled-in bundle is the fallback that must not
        // depend on a satellite assembly having loaded. Two copies with no test between them is how
        // a message gets fixed in one and not the other, and the reader who finds out is whichever
        // one is served by the copy nobody updated.
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
    /// Deliberately not through <see cref="Microsoft.Extensions.Localization.IStringLocalizer"/>:
    /// that would read whatever the build embedded, which is the thing being checked. Reading the
    /// file compares what a translator would edit against what the site compiles.
    /// <para>
    /// The file is copied beside the test binary by the project — see the comment on that item for
    /// why, which is that counting <c>..</c> segments up from <see cref="AppContext.BaseDirectory"/>
    /// only reaches the repository root at one exact output depth, and failed as a missing file in
    /// a test whose subject is message content.
    /// </para>
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
    public async Task NothingButAShippedLocaleIsEverOfferedToAReader()
    {
        // The gate, stated twice on purpose. `Offered` is what a default may reach — Accept-Language,
        // a remembered cookie, an hreflang alternate — and it is English alone until a glossary is
        // translated. A review build lists more in the switcher; that is a control somebody clicks,
        // not a place a reader is sent.
        await Assert.That(Locales.Offered.Select(l => l.Tag)).IsEquivalentTo(new[] { Locales.SourceTag });

        foreach (var locale in Locales.All.Where(l => l.Status is not LocaleStatus.Shipped))
        {
            await Assert.That(locale.IsOffered)
                .IsFalse()
                .Because($"{locale.Tag} is {locale.Status} and must not be offered");
        }

        // And no default reaches a review locale, whatever a browser asks for.
        await Assert.That(LocaleRouting.Preferred("qps-ploc,ru;q=0.9")).IsNull();

        // The alternates name only what is offered: an hreflang pointing at a pseudolocale would
        // invite a search engine to index accented English as a language.
        var alternates = LocaleRouting.Alternates("/games").Select(a => a.HrefLang).ToList();

        await Assert.That(alternates).DoesNotContain("qps-ploc");
        await Assert.That(alternates).DoesNotContain("ru-x-canary");
    }

    [Test]
    public async Task AReviewBuildListsTheLocalesThatAreNotLanguages()
    {
        // Without this the switcher has one option, draws nothing, and cannot be reviewed at all —
        // which is how it shipped invisible.
        var production = Locales.Switchable(preview: false);
        var review = Locales.Switchable(preview: true);

        // A machine-translated locale is in both — it has real words and it says on every page that
        // a machine wrote them. The pseudolocale and the canary are in neither, because they are not
        // languages and nothing but a review build should list them.
        await Assert.That(production.Select(l => l.Tag)).Contains("de");
        await Assert.That(production.Select(l => l.Tag)).DoesNotContain("qps-ploc");
        await Assert.That(review.Select(l => l.Tag)).Contains("qps-ploc");
        await Assert.That(review.Count).IsGreaterThan(production.Count);

        // A planned locale is in neither: nothing is translated, so listing it would offer a page
        // that is still English under a name saying it is not.
        await Assert.That(review.Select(l => l.Tag)).DoesNotContain("ru");
    }

    [Test]
    public async Task WhetherTheReviewLocalesAreListedIsAskedOfTheRequestAndNotOfTheProcess()
    {
        // It was a static flag that every host start wrote, and this suite starts hosts in both
        // Development and Production inside one process — so the switcher listed whatever the last
        // host to start had decided, on every request served by any of them. Two requests, two
        // answers, and no dependency on which host began first.
        var review = Request(Environments.Development);
        var production = Request(Environments.Production);

        await Assert.That(review.IsReviewBuild()).IsTrue();
        await Assert.That(production.IsReviewBuild()).IsFalse();

        await Assert.That(Locales.Switchable(review.IsReviewBuild()).Select(l => l.Tag)).Contains("qps-ploc");
        await Assert.That(Locales.Switchable(production.IsReviewBuild()).Select(l => l.Tag)).DoesNotContain("qps-ploc");

        // And a component rendered with no request behind it at all — which is every headless
        // component test in this suite — is not a review build. That is the case that made taking
        // IWebHostEnvironment as a dependency impossible, and it is why this is asked of the
        // request rather than injected.
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
        // The difference between offering a locale and honouring a choice. Nothing sends a reader to
        // the pseudolocale; having picked it, they stay in it across an unprefixed URL, or the
        // switcher would appear to do nothing on the next page.
        await Assert.That(Locales.Find("qps-ploc")!.IsOffered).IsFalse();
        await Assert.That(Locales.Find("qps-ploc")!.IsChoosable).IsTrue();

        // Same for a machine-translated one, and for the same reason: nothing sends a reader there.
        await Assert.That(Locales.Find("de")!.IsOffered).IsFalse();
        await Assert.That(Locales.Find("de")!.IsChoosable).IsTrue();

        // A planned locale is neither. There is nothing to choose.
        await Assert.That(Locales.Find("ru")!.IsChoosable).IsFalse();
    }

    [Test]
    public async Task EveryMachineTranslatedLocaleTranslatedTheWholeGlossary()
    {
        // Not the review gate — that is a person reading them. This is the weaker thing worth
        // asserting: a bundle that stopped halfway would leave a page in two languages, with the
        // English half being exactly the provenance words a reader most needs to trust.
        foreach (var locale in Locales.All.Where(l => l.Status is LocaleStatus.MachineTranslated))
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
        // The finding the glossary exists for, checked against what the translators actually wrote.
        // A machine translating "not measured", "uncounted", "unreachable" and "not counted" will
        // reach for the same target phrase for at least two of them if nothing stops it — and a
        // reader then cannot tell a game that answered from one that did not.
        string[] ids = ["state.notMeasured", "state.uncounted", "state.unreachable", "state.notCounted"];

        foreach (var locale in Locales.All.Where(l => l.Status is LocaleStatus.MachineTranslated))
        {
            var rendered = ids.Select(id => Messages.For(locale.Tag, id)).ToList();

            await Assert.That(rendered.Distinct().Count())
                .IsEqualTo(ids.Length)
                .Because($"{locale.Tag} collapsed two of the four kinds of absence: {string.Join(" / ", rendered)}");
        }
    }
}
