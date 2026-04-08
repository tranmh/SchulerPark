namespace SchulerPark.Infrastructure.Jobs;

using SchulerPark.Core.Interfaces;

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
