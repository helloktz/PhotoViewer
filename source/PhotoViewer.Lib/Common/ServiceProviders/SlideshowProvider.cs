using Avalonia.Threading;
using PhotoViewer.Common.Types;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders;

public sealed class SlideshowProvider : PhDisposable
{

    public const double MIN_INTERVAL_MS = 50;

    private const int MAX_ADVANCE_WAIT_MS = 20_000;

    private const string SLEEP_GUARD_OWNER = "slideshow";

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _intervalCts;
    private SemaphoreSlim? _pauseGate;
    private readonly Lock _lock = new();

    private long _countdownSecondsBits;
    private volatile bool _isRunning;
    private volatile bool _isPaused;

    private double _remainingMsOnPause;

    private int _beepImageCount;

    public event Func<Task>? NextPhotoRequested;

    public bool IsRunning => _isRunning;

    public bool IsPaused => _isPaused;

    public double CountdownSeconds => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _countdownSecondsBits));

    public void Start()
    {
        lock (_lock)
        {
            Stop_Locked();

            _cts = new CancellationTokenSource();
            _pauseGate = new SemaphoreSlim(0, 1);
            _isRunning = true;
            _isPaused = false;
            _beepImageCount = 1;
            SetCountdown(0);

            SleepGuard.Acquire(SLEEP_GUARD_OWNER, $"{BHelper.AppDisplayName} slideshow");

            var token = _cts.Token;
            _ = RunLoopAsync(token);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            Stop_Locked();
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (!_isRunning || _isPaused) return;

            _remainingMsOnPause = CountdownSeconds * 1000.0;
            _isPaused = true;
            _intervalCts?.Cancel();
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (!_isRunning || !_isPaused) return;

            _isPaused = false;

            if (_pauseGate is { CurrentCount: 0 })
            {
                _pauseGate.Release();
            }
        }
    }

    public void ResetInterval()
    {
        lock (_lock)
        {
            _beepImageCount = 1;
            _remainingMsOnPause = 0;
            _intervalCts?.Cancel();
        }
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        lock (_lock)
        {
            Stop_Locked();
        }
    }

    private void Stop_Locked()
    {
        SleepGuard.Release(SLEEP_GUARD_OWNER);

        _isRunning = false;
        _isPaused = false;
        _beepImageCount = 0;
        _remainingMsOnPause = 0;
        SetCountdown(0);

        _intervalCts?.Cancel();
        _intervalCts?.Dispose();
        _intervalCts = null;

        if (_cts is not null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        _pauseGate?.Dispose();
        _pauseGate = null;
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        const int tickMs = 50;

        var nextIntervalMs = 0d;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_isPaused)
                {
                    if (_pauseGate is not null)
                    {
                        await _pauseGate.WaitAsync(token).ConfigureAwait(false);
                    }
                    continue;
                }

                double intervalMs;
                lock (_lock)
                {
                    if (_remainingMsOnPause > 0)
                    {
                        intervalMs = _remainingMsOnPause;
                        _remainingMsOnPause = 0;
                    }
                    else if (nextIntervalMs > 0)
                    {
                        intervalMs = nextIntervalMs;
                    }
                    else
                    {
                        intervalMs = GetNextIntervalMs();
                    }
                    nextIntervalMs = 0;
                }
                SetCountdown(intervalMs / 1000.0);

                CancellationTokenSource intervalCts;
                lock (_lock)
                {
                    _intervalCts?.Dispose();
                    _intervalCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    intervalCts = _intervalCts;
                }
                var intervalToken = intervalCts.Token;

                try
                {
                    var remaining = intervalMs;
                    do
                    {
                        var delay = Math.Max(1, (int)Math.Ceiling(Math.Min(tickMs, remaining)));
                        await Task.Delay(delay, intervalToken).ConfigureAwait(false);

                        remaining -= delay;
                        SetCountdown(Math.Max(0, remaining / 1000.0));
                    }
                    while (remaining > 0 && !intervalToken.IsCancellationRequested);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    continue;
                }

                if (token.IsCancellationRequested) break;

                var isLastPhoto = Core.Photos.CurrentIndex >= (int)Core.Photos.Count - 1;
                if (isLastPhoto && !Core.Config.EnableLoopSlideshow)
                {
                    _isPaused = true;
                    SetCountdown(0);
                    continue;
                }

                _beepImageCount++;
                var advanceTask = await Dispatcher.UIThread.InvokeAsync<Task>(
                    () => NextPhotoRequested?.Invoke() ?? Task.CompletedTask);

                var notifyCount = (int)Core.Config.SlideshowImagesToNotifySound;
                if (notifyCount > 0 && _beepImageCount >= notifyCount)
                {
                    _beepImageCount = 0;
                    _ = Task.Run(BHelper.PlayNotificationSoundAsync, token);
                }

                nextIntervalMs = GetNextIntervalMs();
                SetCountdown(nextIntervalMs / 1000.0);
                await WaitForAdvanceAsync(advanceTask, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isRunning = false;
            _isPaused = false;
            SetCountdown(0);
        }
    }

    private static async Task WaitForAdvanceAsync(Task advanceTask, CancellationToken token)
    {
        try
        {
            await advanceTask.WaitAsync(TimeSpan.FromMilliseconds(MAX_ADVANCE_WAIT_MS), token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
    }

    private void SetCountdown(double value)
    {
        Interlocked.Exchange(ref _countdownSecondsBits, BitConverter.DoubleToInt64Bits(value));
    }

    private static double GetNextIntervalMs()
    {
        var intervalFrom = Core.Config.SlideshowInterval;
        var intervalTo = Core.Config.SlideshowIntervalTo;
        var intervalMs = intervalFrom * 1000.0;

        if (Core.Config.EnableSlideshowRandomInterval && intervalTo > intervalFrom)
        {
            var range = intervalTo - intervalFrom;
            var randomOffset = Random.Shared.NextDouble() * range;

            intervalMs = (intervalFrom + randomOffset) * 1000.0;
        }

        if (double.IsNaN(intervalMs)) return MIN_INTERVAL_MS;

        return Math.Max(MIN_INTERVAL_MS, intervalMs);
    }

}
