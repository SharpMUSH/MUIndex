using Microsoft.Extensions.Logging;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// A logger that keeps what it was told, so "this is said once" is assertable.
/// </summary>
/// <remarks>
/// Used where a log line is the behaviour rather than decoration: an operator's only notification
/// that a game asked us to stop is the line the gate writes the first time it hears it, so a change
/// that made it fire on every cycle — or never — would be a real regression with nothing else to
/// catch it.
/// </remarks>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        Lines.Add(formatter(state, exception));
    }
}
