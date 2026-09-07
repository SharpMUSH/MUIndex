"""Generate experimental components from current source; never maintain a forked row template."""
from pathlib import Path

def replace_once(text, old, new):
    if text.count(old) != 1:
        raise ValueError(f"Source shape changed: expected one {old!r}")
    return text.replace(old, new)


here = Path(__file__).resolve().parent
root = here.parent.parent
out = here / "Generated"
out.mkdir(exist_ok=True)
source = (root / "src/MUI.Web/Components/Pages/Games.razor").read_text()
start = source.index('        <ul class="games">') + len('        <ul class="games">')
end = source.index('        </ul>', start)
rows = replace_once(source[start:end], 'Listing.Games)', 'Listing.Games.Skip(Offset).Take(BatchSize))')
body = source.index('@if (Plain)')
code = source.index('@code {')
# A shell retains all catalogue-dependent controls; only its row loop is replaced.
shell = source[body:start] + '@((MarkupString)"<!--MUI-STREAM-ROWS-->")' + source[end:code]
generated = replace_once(source[:body], '@page "/games"\n', '')
generated += '@namespace MUI.Streaming.Prototype\n'
generated += '@if (RowsOnly)\n{\n' + rows + '\n}\nelse\n{\n' + shell + '\n}\n'
generated += replace_once(source[code:], '@code {', '''@code {
    [Parameter] public GameSummary? UnrankedStart { get; set; }
    [Parameter] public bool RowsOnly { get; set; }
    [Parameter] public int Offset { get; set; }
    [Parameter] public int BatchSize { get; set; } = 50;
''')
generated = replace_once(generated, 'FirstUnranked = Listing.Games.FirstOrDefault(g => GameSorting.IsUnranked(g, Filter.Sort));', 'FirstUnranked = UnrankedStart;')
(out / 'StreamedGames.razor').write_text(generated)
(out / '_Imports.razor').write_text((root / 'src/MUI.Web/Components/_Imports.razor').read_text())
renderer = (root / 'tests/MUI.Web.Tests/Render.cs').read_text()
renderer = replace_once(renderer, 'public static class Render', 'public static class StreamingRender')
renderer = replace_once(renderer, 'IGameQueries? queries = null)', 'IGameQueries? queries = null, TimeProvider? clock = null)')
renderer = replace_once(renderer, 'services.AddSingleton(TimeProvider.System);', 'services.AddSingleton(clock ?? TimeProvider.System);')
needle = '            return output.ToHtmlString();'
assert renderer.count(needle) == 1
renderer = replace_once(renderer, needle, '''            var html = output.ToHtmlString();
            MUI.Streaming.Prototype.RenderProbe.Current.Value?.Observe();
            return html;''')
(out / 'StreamingRender.cs').write_text(renderer)
