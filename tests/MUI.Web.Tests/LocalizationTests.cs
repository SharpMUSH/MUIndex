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
    /// Read off the pattern the locale actually uses, which is the fallback's English where it has
    /// none of its own — so a locale that has translated nothing reports nothing missing here. That
    /// is correct and deliberate: an untranslated string is a fallback, and a half-translated plural
    /// is a bug. They are different failures and only the second one is this test's.
    /// </remarks>
    private static IReadOnlyList<string> Missing(string tag, string id)
    {
        if (!Messages.HasOwn(tag, id))
        {
            return [];
        }

        var pattern = Messages.Pattern(tag, id)!;
        var needed = PluralRules.CategoriesOf(tag).Select(PluralRules.Keyword).ToList();

        return
        [
            // `other` is a legal catch-all in ICU and it is exactly the wrong thing to accept
            // here: a Russian message that routes 2 through `other` renders a grammatical form no
            // native speaker would write, and it does so silently. A form the language distinguishes
            // has to be declared, not fallen into.
            .. IcuMessage.PluralBranches(pattern)
                .SelectMany(argument => needed
                    .Where(form => form != "other" && !argument.Value.Contains(form))
                    .Select(form => $"{argument.Key}:{form}")),
        ];
    }
}
