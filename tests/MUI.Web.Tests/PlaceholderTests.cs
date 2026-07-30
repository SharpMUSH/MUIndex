namespace MUI.Web.Tests;

/// <summary>
/// The web tier is a placeholder until the first implementation plan lands. This keeps the project
/// wired into CI so the first real test has somewhere to go.
/// </summary>
public class PlaceholderTests
{
    [Test]
    public async Task TheWebProjectIsWiredIntoTheTestRun()
    {
        await Assert.That(typeof(Program).Assembly.GetName().Name).IsEqualTo("MUI.Web");
    }
}
