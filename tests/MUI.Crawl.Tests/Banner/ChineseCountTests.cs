using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Reading a count out of a Chinese-language connect screen.
/// </summary>
/// <remarks>
/// <para>
/// Every fixture here is a live connect screen, taken from a sweep of all 904 stored banners on
/// 2026-09-17 (see docs/codebase-survey-2026-07-30.md). Thirty-three carry Chinese player
/// vocabulary and twenty-one state a count; those twenty-one are the whole of this file, positives
/// and refusals alike.
/// </para>
/// <para>
/// The family matters more than any one game: Taiwanese and mainland MudOS/ES2 games negotiate
/// nothing, publish no MSSP, and read a pre-login <c>WHO</c> as a character name — so the screen is
/// the only place a count was ever going to come from. Six of them had no count at all before this
/// reader.
/// </para>
/// </remarks>
public class ChineseCountTests
{
    /// <summary>
    /// The shape almost every one of them shares: staff, players and people still connecting, all
    /// three counted separately in one sentence.
    /// </summary>
    /// <remarks>
    /// The number is bound to 玩家 (players) rather than the line being disqualified for naming
    /// staff, which is how the English reader keeps "4 wizards" out of a count. It has to be: here
    /// all three figures share a line, so there is no line to disqualify. 巫師/巫师 (wizards),
    /// 管理者 (admins) and 使用者/用戶 (users still at the login screen) are simply never matched.
    /// </remarks>
    [Test]
    // jy.mud.com.tw:6666 — the full three-figure sentence, traditional.
    [Arguments("目前共有 1 位巫師、83 位玩家在線上，以及 1 位使用者嘗試連線中。", 83)]
    // 202.103.21.247:8888 — simplified, and with no spaces around the numbers at all.
    [Arguments("目前共有0位巫师、34位玩家，以及3位在尝试连线。", 34)]
    // 210.59.236.38:3000 — no 、 between the two roles, and a ﹐ for the comma.
    [Arguments("目前共有 0 位巫師 5 位玩家在線上﹐以及 1 位使用者嘗試連線中。", 5)]
    // mud.pkuxkx.net:8080 — the shortest form, players only.
    [Arguments("目前共有 559 位玩家在线上。", 559)]
    // mud.revivalworld.org:4000 — 管理者 for the staff role instead of 巫師.
    [Arguments("目前共有 1 位管理者 , 381 位玩家在線上，以及 1 位使用者嘗試連線中。", 381)]
    // windcloud.twmuds.com:8000 — the game's own name leads the sentence.
    [Arguments("風雲再起目前共有 109 位玩家在線上，並且尚有 3 位使用者正嘗試連線中。", 109)]
    // es.clovers.tw:8000 — 個 rather than 位 for the measure word, and no verb at all.
    [Arguments("共有: 93 個玩家.", 93)]
    public async Task TheFigureBoundToThePlayerNounIsTheCount(string line, int expected)
    {
        await Assert.That(BannerCount.Find(line)).IsEqualTo(expected);
    }

    /// <summary>
    /// fs.twkang.net:5555 (狂想空間), the game this reader was written for: an ES2 screen that names
    /// no player noun anywhere.
    /// </summary>
    /// <remarks>
    /// 線上 2 — "online 2" — is the bare connectivity form, the direct analogue of lusternia's
    /// "Currently On-Line: 12", which the English reader admits with no people-noun for the same
    /// reason. The two 人次 figures beside it are cumulative visit counts for today and this week,
    /// and the 1 位使用者 is somebody sitting at the login screen.
    /// </remarks>
    [Test]
    public async Task ABareOnlineLabelIsReadWhenNoPlayerNounIsStated()
    {
        var line = "今日上線人次: 2, 本週上線人次: 11, 線上 2, 以及 1 位使用者連線中。";

        await Assert.That(BannerCount.Find(line)).IsEqualTo(2);
    }

    /// <summary>
    /// The separator after 線上 is spaces and a colon and nothing else, which is what keeps the bare
    /// form from swallowing the next clause's number.
    /// </summary>
    /// <remarks>
    /// mud.revivalworld.org:4000 states "共計 4 人正在線上，1 人正在登入" — four online, and one
    /// person still logging in. A comma admitted as a separator would read that as one.
    /// </remarks>
    [Test]
    public async Task ACommaAfterTheOnlineWordIsNotASeparator()
    {
        var line = "目前有 0 位巫師、4 位玩家，共計 4 人正在線上，1 人正在登入。";

        await Assert.That(BannerCount.Find(line)).IsEqualTo(4);
    }

    /// <summary>
    /// A clause stating something other than who is playing right now offers no candidate at all.
    /// </summary>
    /// <remarks>
    /// Judged per clause rather than per line because these screens put the live figure and the
    /// record beside each other with a comma between them. Each fixture is the disqualifier earning
    /// its place: a cap, a record, a cumulative visit count, a figure about our own connection.
    /// </remarks>
    [Test]
    // doom.twmuds.com:4000 — 上限 500 名玩家, a licence for five hundred beside fifty-five people.
    [Arguments("現在共有 55 位玩家正在失落的國度中奮鬥. (上限 500 名玩家).", 55)]
    // 210.59.236.38:7788 — the cap leads, on its own sentence.
    [Arguments("系統上限: 300 人。目前有 67 位玩家在線上，1 位使用者嘗試連線中。", 67)]
    // windcloud.twmuds.com:8000 — a record and a today-total, neither of them a population.
    [Arguments(
        "風雲再起目前共有 109 位玩家在線上，並且尚有 3 位使用者正嘗試連線中。\n"
        + "風雲再起目前為止最高的線上人數為 185 位玩家，今日共有 330 位玩家上線遊戲。",
        109)]
    // 221.226.96.186:5555 — "the address you are at already has 0 players online" is about us.
    [Arguments(
        "您所在的地址已有 0 位玩家在线上，当前允许总数 780 人。\n"
        + "目前共有 0 位巫师、651 位玩家在线上，以及 2 位使用者尝试连线中。",
        651)]
    public async Task AFigureThatIsNotThePopulationRightNowIsNotACandidate(string banner, int expected)
    {
        await Assert.That(BannerCount.Find(banner)).IsEqualTo(expected);
    }

    /// <summary>
    /// Nouns that are not players, and are not read as players.
    /// </summary>
    [Test]
    // mud.revivalworld.org:4000 — 角色 is every character ever created, not who is on.
    [Arguments("重生的世界目前共有 13 座城市、593 個角色，已計連線人次共 5,137,643 次。")]
    // kk.muds.idv.tw:4000 — 英雄豪傑 ("heroes") is this game's own word for its players. Game-
    // specific vocabulary stays unread, the same way lostsouls' "atmai" does.
    [Arguments("目前線上共有 435 位英雄豪傑。")]
    // jy.mud.com.tw:6666 — every figure on the line is historical.
    [Arguments("自開站以來最高在線IP數 9487 個，今日上線 37 人次，本周上線 121 人次。")]
    public async Task AChineseLineWithNoPlayerFigureYieldsNothing(string line)
    {
        await Assert.That(BannerCount.Find(line)).IsNull();
    }

    /// <summary>
    /// Counts spelled in Chinese numerals stay unread, exactly as the 2026-08-20 survey left them.
    /// </summary>
    /// <remarks>
    /// Two games do this and nothing else. Reading 二百八十八 would mean a numeral parser for a
    /// second language on the weakest source in the project; the count is simply unknown, which is
    /// the honest answer.
    /// </remarks>
    [Test]
    // mudbest — "共有 二百八十八 位玩家连线中".
    [Arguments("「夕阳再现」共有 1 个站点联线中，共有 二百八十八 位玩家连线中。")]
    // mud.csie.org:3838.
    [Arguments("負載量為 11.515% 總上站人數有七百六十六次﹐上線人數最高記錄六十二人")]
    public async Task ACountInChineseNumeralsIsStillNotRead(string line)
    {
        await Assert.That(BannerCount.Find(line)).IsNull();
    }

    /// <summary>
    /// it.muds.net:7000, whose counter has overflowed in both directions.
    /// </summary>
    /// <remarks>
    /// A ten-digit run cannot be read as its last five — that is what the lookbehind on the pattern
    /// is for — and a negative figure is refused by the same rule that refuses one above
    /// <see cref="BannerCount.Implausible"/>.
    /// </remarks>
    [Test]
    public async Task AnOverflowedCounterIsNotACount()
    {
        var banner = "虛幻時空已有 2147483647 位玩家在此闖蕩，最高紀錄 2147483647 人同時連線。\n"
            + "目前線上共有 -2147483648 位英雄豪傑。";

        await Assert.That(BannerCount.Find(banner)).IsNull();
    }

    /// <summary>
    /// Two competing player figures are refused here exactly as they are in English.
    /// </summary>
    /// <remarks>
    /// c-e-c-a-a states a network-wide 86 on one line and its own 85 on the next. Both are real
    /// counts of something; nothing in the screen says which one is this game, so neither is
    /// published.
    /// </remarks>
    [Test]
    public async Task TwoDifferentChineseFiguresAreRefused()
    {
        var banner = "本游戏共有 86 位玩家在线（提示：mudlist all 查看遊戲列表）\n"
            + "目前共有 1 位巫师、85 位玩家在线上，以及 5 位使用者尝试连线中。";

        await Assert.That(BannerCount.Find(banner)).IsNull();
    }

    /// <summary>
    /// A Chinese count is read the same whether it arrived with colour around it or not.
    /// </summary>
    [Test]
    public async Task ColourDoesNotHideAChineseCount()
    {
        var coloured = "目前共有 0 位巫師、\u001b[1;32m18\u001b[0m 位玩家在線上﹐以及 1 位使用者嘗試連線中。";

        await Assert.That(BannerCount.Find(coloured)).IsEqualTo(18);
    }
}
