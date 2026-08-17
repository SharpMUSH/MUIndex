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
    /// </remarks>
    private static IReadOnlyDictionary<string, string> ResxMessages()
    {
        var path = System.IO.Path.Combine(
            System.AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "MUI.Web", "Resources", "Messages.resx");

        var document = System.Xml.Linq.XDocument.Load(System.IO.Path.GetFullPath(path));

        return document.Root!
            .Elements("data")
            .Where(e => e.Attribute("name") is not null)
            .ToDictionary(
                e => e.Attribute("name")!.Value,
                e => e.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }
}
