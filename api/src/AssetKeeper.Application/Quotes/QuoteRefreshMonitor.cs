namespace AssetKeeper.Application.Quotes;

public sealed class QuoteRefreshMonitor
{
    private readonly object _lock = new();
    private bool _isRunning;
    private string? _runningTrigger;
    private QuoteRefreshRunDto? _lastRun;
    private bool _schedulerEnabled;
    private int _intervalSeconds;
    private DateTimeOffset? _nextRunAt;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _isRunning;
            }
        }
    }

    public bool TryBegin(string trigger)
    {
        lock (_lock)
        {
            if (_isRunning)
            {
                return false;
            }

            _isRunning = true;
            _runningTrigger = trigger;

            return true;
        }
    }

    public void Complete(QuoteRefreshRunDto run)
    {
        lock (_lock)
        {
            _lastRun = run;
            _isRunning = false;
            _runningTrigger = null;
        }
    }

    public void SetSchedule(bool enabled, int intervalSeconds, DateTimeOffset? nextRunAt)
    {
        lock (_lock)
        {
            _schedulerEnabled = enabled;
            _intervalSeconds = intervalSeconds;
            _nextRunAt = nextRunAt;
        }
    }

    public QuoteRefreshMonitorSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new QuoteRefreshMonitorSnapshot(
                _isRunning,
                _runningTrigger,
                _lastRun,
                _schedulerEnabled,
                _intervalSeconds,
                _nextRunAt);
        }
    }
}

public sealed record QuoteRefreshMonitorSnapshot(
    bool IsRunning,
    string? RunningTrigger,
    QuoteRefreshRunDto? LastRun,
    bool SchedulerEnabled,
    int IntervalSeconds,
    DateTimeOffset? NextRunAt);
