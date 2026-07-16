namespace SchulerPark.Infrastructure.Jobs;

using Hangfire;
using SchulerPark.Core.Interfaces;

// Bug #1: block overlapping runs — a run that overruns its schedule must not be
// re-entered on the next tick. 30-minute lock timeout.
[DisableConcurrentExecution(30 * 60)]
public class LotteryJob
{
    private static readonly TimeZoneInfo BerlinTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private readonly ILotteryService _lotteryService;

    public LotteryJob(ILotteryService lotteryService)
    {
        _lotteryService = lotteryService;
    }

    public async Task ExecuteAsync()
    {
        var berlinNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BerlinTz);
        var tomorrow = DateOnly.FromDateTime(berlinNow).AddDays(1);

        await _lotteryService.RunAllLotteriesAsync(tomorrow);
    }
}
