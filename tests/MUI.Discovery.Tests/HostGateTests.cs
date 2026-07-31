namespace MUI.Discovery.Tests;

/// <summary>
/// Per-host serialisation (spec §7.7): "prevents a multi-port game from being hit concurrently". Keyed
/// on the host alone, because six advertised ports are one machine.
/// </summary>
public class HostGateTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task TwoPortsOnOneMachineAreSerialised()
    {
        var gate = new HostGate();

        var first = await gate.EnterAsync("mud.example.org", None);
        var second = gate.EnterAsync("mud.example.org", None);

        await Assert.That(second.IsCompleted).IsFalse();

        first.Dispose();
        var taken = await second.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(taken).IsNotNull();
        taken.Dispose();
    }

    [Test]
    public async Task DifferentHostsDoNotWaitForEachOther()
    {
        var gate = new HostGate();

        var first = await gate.EnterAsync("a.example.org", None);
        var second = await gate.EnterAsync("b.example.org", None).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(second).IsNotNull();
        first.Dispose();
        second.Dispose();
    }

    [Test]
    public async Task TheHostNameIsMatchedInItsCanonicalForm()
    {
        // Otherwise a game advertising MUD.Example.ORG on one port and mud.example.org on another would
        // be two gates and one machine, which is the thing this type exists to prevent.
        var gate = new HostGate();

        var first = await gate.EnterAsync("MUD.Example.ORG.", None);
        var second = gate.EnterAsync("mud.example.org", None);

        await Assert.That(second.IsCompleted).IsFalse();

        first.Dispose();
        (await second).Dispose();
    }

    [Test]
    public async Task ReleasingTwiceIsHarmless()
    {
        // The loop disposes through a `using`, and a retry path could dispose again. Double-releasing a
        // semaphore would silently let two probes into one host.
        var gate = new HostGate();
        var held = await gate.EnterAsync("a.example.org", None);

        held.Dispose();
        held.Dispose();

        var again = await gate.EnterAsync("a.example.org", None).WaitAsync(TimeSpan.FromSeconds(5));
        var blocked = gate.EnterAsync("a.example.org", None);

        await Assert.That(blocked.IsCompleted).IsFalse();
        again.Dispose();
        (await blocked).Dispose();
    }
}
