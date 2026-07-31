namespace MUI.Discovery.Tests.Support;

/// <summary>
/// A clock the test moves by hand.
/// </summary>
/// <remarks>
/// Nothing in this suite sleeps. A rate limiter tested by waiting for its own interval proves only that
/// the machine was not too busy that second, and a schedule tested against
/// <c>DateTimeOffset.UtcNow</c> acquires flakiness nobody can reproduce. Every production type here
/// takes a <see cref="TimeProvider"/> for exactly this reason.
/// </remarks>
public sealed class ManualTimeProvider(DateTimeOffset? start = null) : TimeProvider
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<ManualTimer> _timers = [];
    private readonly Lock _gate = new();

    private DateTimeOffset _now = start ?? Epoch;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <summary>Moves the clock forward and fires anything that became due.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = _timers.Where(timer => timer.IsDueAt(_now)).ToList();
            foreach (var timer in due)
            {
                _timers.Remove(timer);
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new ManualTimer(callback, state, Deregister);

        lock (_gate)
        {
            timer.Schedule(_now, dueTime);
            if (timer.IsPending)
            {
                _timers.Add(timer);
            }
        }

        // A zero due time is due immediately, and firing it inside the lock would re-enter.
        if (!timer.IsPending)
        {
            timer.Fire();
        }

        return timer;
    }

    private void Deregister(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    /// <summary>
    /// A one-shot timer, which is all <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>
    /// asks for. A repeating one would need the clock to know how far each advance overshot, and no
    /// test here needs it.
    /// </summary>
    private sealed class ManualTimer(TimerCallback callback, object? state, Action<ManualTimer> deregister) : ITimer
    {
        private DateTimeOffset? _dueAt;

        public bool IsPending => _dueAt is not null;

        public void Schedule(DateTimeOffset now, TimeSpan dueTime) =>
            _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;

        public bool IsDueAt(DateTimeOffset now) => _dueAt is { } due && due <= now;

        public void Fire()
        {
            _dueAt = null;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : DateTimeOffset.MaxValue;
            return true;
        }

        public void Dispose() => deregister(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
