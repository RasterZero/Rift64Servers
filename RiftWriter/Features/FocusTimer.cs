namespace RiftWriter.Features;

/// <summary>
/// 25-minute Pomodoro focus timer displayed in the title bar.
/// </summary>
internal sealed class FocusTimer
{
    private const int DurationSeconds = 1500; // 25 minutes
    private DateTime _startTime;
    private int _lastDisplayedSec = -1;

    public bool IsActive { get; private set; }
    public bool IsFinished { get; private set; }

    public void Start()
    {
        IsActive = true;
        IsFinished = false;
        _startTime = DateTime.UtcNow;
        _lastDisplayedSec = -1;
    }

    public void Stop()
    {
        IsActive = false;
        _lastDisplayedSec = -1;
    }

    /// <summary>
    /// Returns the formatted timer string "[MM:SS]" if display should update,
    /// null if no update needed, or empty string if timer just finished.
    /// </summary>
    public string? Tick()
    {
        if (!IsActive) return null;

        var elapsed = (int)(DateTime.UtcNow - _startTime).TotalSeconds;
        var remaining = DurationSeconds - elapsed;

        if (remaining <= 0)
        {
            IsActive = false;
            IsFinished = true;
            return "";
        }

        if (remaining == _lastDisplayedSec) return null;

        _lastDisplayedSec = remaining;
        var mins = remaining / 60;
        var secs = remaining % 60;
        return $"[{mins:D2}:{secs:D2}]";
    }
}
