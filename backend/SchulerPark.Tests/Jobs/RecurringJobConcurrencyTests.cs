namespace SchulerPark.Tests.Jobs;

using System.Reflection;
using Hangfire;
using SchulerPark.Infrastructure.Jobs;
using Xunit;

// Bug #1: recurring Hangfire jobs must not overlap. Each job class must carry
// [DisableConcurrentExecution] so a slow run can't be re-entered on the next tick.
public class RecurringJobConcurrencyTests
{
    [Theory]
    [InlineData(typeof(LotteryJob))]
    [InlineData(typeof(ConfirmationExpiryJob))]
    [InlineData(typeof(DataRetentionJob))]
    public void RecurringJob_HasDisableConcurrentExecution(Type jobType)
    {
        var attr = jobType.GetCustomAttribute<DisableConcurrentExecutionAttribute>(inherit: false);

        Assert.NotNull(attr);
    }
}
