namespace SchulerPark.Infrastructure.Jobs;

using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;

// Bug #1: block overlapping runs — a run that overruns its schedule must not be
// re-entered on the next tick. 30-minute lock timeout.
[DisableConcurrentExecution(30 * 60)]
// WP1 3.5: a failed slot makes the job fail (visible in Hangfire + admin mail) and is
// retried after 1, 5 and 15 minutes. The LotteryRun idempotency guard means a retry only
// touches the slots that actually failed.
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
public class LotteryJob
{
    private readonly ILotteryService _lotteryService;
    private readonly IAdminNotifier _adminNotifier;
    private readonly TimeProvider _time;
    private readonly ILogger<LotteryJob> _logger;

    public LotteryJob(ILotteryService lotteryService, IAdminNotifier adminNotifier,
        TimeProvider time, ILogger<LotteryJob> logger)
    {
        _lotteryService = lotteryService;
        _adminNotifier = adminNotifier;
        _time = time;
        _logger = logger;
    }

    /// <summary>Hangfire entry point; the context is injected by Hangfire (null when invoked directly).</summary>
    public Task ExecuteAsync(PerformContext? context = null) =>
        ExecuteAsync(context is null ? 1 : context.GetJobParameter<int?>("RetryCount") is { } retries ? retries + 1 : 1);

    public async Task ExecuteAsync(int attempt)
    {
        var tomorrow = DeadlineHelper.BerlinToday(_time.GetUtcNow().UtcDateTime).AddDays(1);

        var summary = await _lotteryService.RunAllLotteriesAsync(tomorrow);
        if (!summary.HasFailures)
            return;

        _logger.LogError("Lottery for {Date}: {Failed} slot(s) failed, {Succeeded} succeeded (attempt {Attempt}).",
            tomorrow, summary.Failures.Count, summary.Succeeded, attempt);

        try
        {
            await _adminNotifier.LotteryFailedAsync(summary, attempt);
        }
        catch (Exception ex)
        {
            // The alert must never mask the original failure.
            _logger.LogError(ex, "Could not send the lottery-failure alert to admins.");
        }

        throw new InvalidOperationException(
            $"Lottery for {tomorrow:yyyy-MM-dd} failed for {summary.Failures.Count} location/time-slot pair(s): "
            + string.Join("; ", summary.Failures.Select(f => $"{f.LocationName}/{f.TimeSlot}: {f.Error}")));
    }
}
