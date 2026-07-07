internal struct FixedRateLoop
{
    private DateTime _lastToggleAt;
    private bool _high;

    public FixedRateLoop(DateTime startAt)
    {
        _lastToggleAt = startAt;
        _high = false;
    }

    public bool IsHigh => _high;

    public void Tick(DateTime now, TimeSpan halfPeriod)
    {
        var elapsed = now - _lastToggleAt;
        if (elapsed < halfPeriod)
        {
            return;
        }

        var steps = Math.Max(1, (int)(elapsed.Ticks / halfPeriod.Ticks));
        if ((steps & 1) == 1)
        {
            _high = !_high;
        }

        _lastToggleAt = _lastToggleAt.AddTicks(halfPeriod.Ticks * steps);
    }

    public void Reset(DateTime now)
    {
        _high = false;
        _lastToggleAt = now;
    }
}
