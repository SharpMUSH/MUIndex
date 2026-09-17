using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// The player counts a Chinese-language connect screen states about itself.
/// </summary>
/// <remarks>
/// <para>
/// A branch of <see cref="BannerCount"/> rather than more alternatives inside it, because the shape
/// of the problem is different. The English reader works a line at a time and disqualifies a whole
/// line that counts only staff; a Chinese screen states staff, players and people still at the
/// login screen as three figures in <em>one</em> sentence — <c>目前共有 1 位巫師、83 位玩家在線上，
/// 以及 1 位使用者嘗試連線中。</c> — so there is no line to disqualify and no way to tell the three
/// apart except by the noun each number is bound to.
/// </para>
/// <para>
/// So this binds rather than excludes: a digit run is a candidate only when the noun immediately
/// after it is 玩家 (players). Staff nouns need no list, since 巫師 and 管理者 are simply never
/// matched — the same outcome <c>BannerCount.CountsOnlyStaff</c> reaches in English by the opposite
/// route, and for the same reason (a count of wizards is a count of somebody this project does not
/// count).
/// </para>
/// <para>
/// Grown only from real captures, the way <c>docs/codebase-survey-2026-07-30.md</c> requires: a
/// sweep of all 904 stored connect screens on 2026-09-17 found 33 carrying Chinese player
/// vocabulary and 21 stating a count. Twenty resolve, one is refused for stating two different
/// figures, and nothing in the remaining 883 screens matches at all. <c>ChineseCountTests</c>
/// carries every one of the 21 as a fixture.
/// </para>
/// <para>
/// Counts spelled in Chinese numerals (<c>二百八十八 位玩家</c>) stay unread, exactly as the
/// 2026-08-20 survey left them. Two games write one and nothing else does; an unknown count is
/// honest and a numeral parser for a second language on the weakest source in the project is not
/// worth the ways it could be wrong.
/// </para>
/// </remarks>
internal static partial class ChineseCount
{
    /// <summary>
    /// Every count this text states about itself in Chinese, in either of the two ways one is
    /// stated.
    /// </summary>
    /// <remarks>
    /// Clause by clause rather than line by line, because these screens put the live figure and a
    /// record or a cap beside each other with one comma between them —
    /// <c>目前為止最高的線上人數為 185 位玩家，今日共有 330 位玩家上線遊戲。</c> is two figures,
    /// neither of them a population, on the line below one that is.
    /// </remarks>
    public static IEnumerable<int> In(string text)
    {
        foreach (var clause in ClauseBreakPattern().Split(text))
        {
            if (NotThePopulationNowPattern().IsMatch(clause))
            {
                continue;
            }

            foreach (Match match in BoundToPlayersPattern().Matches(clause))
            {
                if (int.TryParse(match.Groups["n"].Value, out var players))
                {
                    yield return players;
                }
            }

            foreach (Match match in OnlinePattern().Matches(clause))
            {
                if (int.TryParse(match.Groups["n"].Value, out var online))
                {
                    yield return online;
                }
            }
        }
    }

    // Where one statement ends and the next begins. A colon is deliberately absent: "共有: 93 個玩家"
    // (es.clovers.tw:8000) and "今日上線人次: 2" (fs.twkang.net:5555) both put one in the middle of a
    // single statement, and splitting there would separate each number from the words that say what
    // it counts — in the second case from the very word that disqualifies it.
    [GeneratedRegex(@"[\n，,。．\.、；;（）\(\)【】\[\]「」！!？?﹐﹑﹔｜|]")]
    private static partial Regex ClauseBreakPattern();

    // A clause about something other than who is playing right now. Every term was measured:
    //   上限 / 允許總數      a licence, not a population — "(上限 500 名玩家)" (doom.twmuds.com:4000),
    //                       "系統上限: 300 人" (210.59.236.38:7788), "当前允许总数 780 人"
    //   最高 / 紀錄          a record — "目前為止最高的線上人數為 185 位玩家" (windcloud.twmuds.com)
    //   人次                 person-times: cumulative logins, not people — "今日上線人次: 2"
    //   今日 / 本週          a figure for today or this week, however it is worded — "今日共有 330
    //                       位玩家上線遊戲" is three hundred and thirty logins, not three hundred
    //                       and thirty players on
    //   您所在 / 你所在      a fact about our own connection rather than about the game:
    //                       "您所在的地址已有 0 位玩家在线上" is how many of us are on from this
    //                       address. Publishing it as the game's population is rule 5 exactly.
    [GeneratedRegex("上限|允許總數|允许总数|最高|紀錄|記錄|纪录|记录|人次|今日|今天|本日|本週|本周|您所在|你所在")]
    private static partial Regex NotThePopulationNowPattern();

    // "83 位玩家", "34位玩家", "93 個玩家" — a digit run, a measure word, and the player noun, with
    // nothing else admitted between them.
    //
    // 名 is deliberately not a measure word here. It is a perfectly ordinary one in the language, but
    // the only place the sweep found it is a cap — doom.twmuds.com:4000's "(上限 500 名玩家)" — so
    // admitting it would be growing the pattern from a guess rather than from a capture, which is
    // what the survey says not to do. The clause guard above refuses that line anyway; this is the
    // second lock on the same door.
    //
    // The lookbehind is what makes the digit ceiling a real ceiling: without it,
    // "2147483647 位玩家" (it.muds.net:7000, whose counter has overflowed) matches its own last five
    // digits and publishes 83647 as a population.
    [GeneratedRegex(@"(?<![0-9])(?<n>\d{1,5})\s*[位個个]\s*玩家")]
    private static partial Regex BoundToPlayersPattern();

    // The bare connectivity form, for a screen that names no player noun at all: fs.twkang.net:5555
    // states "線上 2" and leaves it at that. The direct analogue of lusternia.com:5000's
    // "Currently On-Line: 12", which BannerCount admits in English with no people-noun either.
    //
    // Spaces and a colon are the only separators, and that is the whole guard. mud.revivalworld.org
    // writes "共計 4 人正在線上，1 人正在登入" — four on, one still logging in — and a comma admitted
    // here would read that as one.
    [GeneratedRegex(@"(?:線上|线上|在線|在线)[ \t]*[:：]?[ \t]*(?<n>\d{1,5})(?![0-9])")]
    private static partial Regex OnlinePattern();
}
