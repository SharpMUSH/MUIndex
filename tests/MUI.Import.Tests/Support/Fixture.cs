namespace MUI.Import.Tests.Support;

/// <summary>
/// Reads a recorded payload out of <c>Fixtures/</c>.
/// </summary>
/// <remarks>
/// Nothing in this suite fetches anything. Every byte an importer sees comes from a file committed
/// beside its test, recorded once by hand from the live page and trimmed to a handful of entries.
/// </remarks>
public static class Fixture
{
    public static string Read(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    }
}
