using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// Whether a line is a login prompt that took what we typed for a character name.
/// </summary>
/// <remarks>
/// <para>
/// Read in two places, and one list so the two cannot drift: <see cref="WhoParser"/> asks it of the
/// reply to <c>WHO</c>, where a match means the server never had a <c>WHO</c> to answer
/// (<see cref="WhoReading.LoginPrompt"/>); and <c>TelnetProbe</c> asks it of the reply to
/// <c>INFO</c>, where a match means <c>VERSION</c> would be read the same way and is not sent.
/// </para>
/// <para>
/// The vocabulary is wider than one codebase: a game may ask for an <em>account</em> name rather than
/// a character's, print nothing but <c>Name:</c>, refuse a disliked name outright (<c>Illegal name,
/// try again.</c>, <c>That name is reserved for a senior member of the mud.</c>), or ask us to
/// confirm the spelling (<c>Did I get that right, Who (Y/N)?</c>).
/// </para>
/// <para>
/// The Chinese forms are the ES2/MudOS family's, measured at <c>fs.twkang.net:5555</c> (issue #182):
/// <c>INFO</c> came back <em>好吧﹐那麼請重新輸入您的英文名字﹕</em> ("re-enter your English name") and
/// <c>VERSION</c> <em>使用 version 這個名字將會創造一個新的人物﹐您確定嗎(y/n)﹖</em> ("the name version
/// will create a new character — are you sure?"). Both scripts, since the family runs in Big5 and
/// GBK alike. Readable only once the screen has been decoded as what it is, which for these games
/// means an operator's <c>CHARSET</c>; in the Latin-1 fallback they match nothing, and the probe
/// asks <c>VERSION</c> as it did before.
/// </para>
/// </remarks>
public static partial class NamePrompt
{
    /// <summary>Whether this one line is a login prompt reading our input as a name.</summary>
    public static bool IsIn(string line) => Pattern().IsMatch(line);

    [GeneratedRegex(
        @"no\s+character\s+by\s+that\s+name|enter\s+(?:the\s+|your\s+)?(?:\w+\s+)?name"
        + @"|create\s+a\s+new\s+(?:character|account|player)"
        + @"|what\s+is\s+your\s+name|password\s*:|type\s+'?new'?|\bname\s*:\s*$"
        + @"|\billegal\s+name\b|\bno\s+record\s+found\s+for\b|\bname\s+is\s+reserved\b"
        + @"|\bdid\s+i\s+get\s+that\s+right\b"
        + @"|[輸输]入[^\n]{0,6}名字|[創创]造一[個个]新的?人物",
        RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
