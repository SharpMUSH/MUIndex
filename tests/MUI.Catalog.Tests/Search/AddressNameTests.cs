namespace MUI.Catalog.Tests;

/// <summary>
/// Which names are only the address a game was found at — the fallback <c>CatalogueBinder.NameOf</c>
/// writes when a game published no name.
/// </summary>
public class AddressNameTests
{
    [Test]
    [Arguments("mud.example.org:4000")]
    [Arguments("203.0.113.7:23")]
    [Arguments("2001:db8::1:4000")]
    [Arguments("[2001:db8::1]:4000")]
    [Arguments("  fs.twkang.net:5555 ")]
    public async Task AnAddressIsAnAddress(string name) =>
        await Assert.That(AddressName.Is(name)).IsTrue();

    [Test]
    [Arguments("Convergence MUSH")]
    [Arguments("Babylon:5")]
    [Arguments("Star Wars: 1977")]
    [Arguments("mud.example.org")]
    [Arguments("mud.example.org:0")]
    [Arguments("mud.example.org:70000")]
    [Arguments("")]
    [Arguments(null)]
    public async Task ANameIsNot(string? name) =>
        await Assert.That(AddressName.Is(name)).IsFalse();
}
