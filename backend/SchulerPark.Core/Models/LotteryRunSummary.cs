namespace SchulerPark.Core.Models;

using SchulerPark.Core.Enums;

/// <summary>One location × time slot whose lottery run threw (WP1 3.5).</summary>
public record LotteryFailure(Guid LocationId, string LocationName, TimeSlot TimeSlot, string Error);

/// <summary>
/// Outcome of <c>RunAllLotteriesAsync</c>. The run keeps going past a failing slot, so a
/// summary can contain both successes and failures; the job turns a non-empty
/// <see cref="Failures"/> list into an admin alert and a failed Hangfire job.
/// </summary>
public record LotteryRunSummary(DateOnly Date, int Succeeded, IReadOnlyList<LotteryFailure> Failures)
{
    public bool HasFailures => Failures.Count > 0;
}
