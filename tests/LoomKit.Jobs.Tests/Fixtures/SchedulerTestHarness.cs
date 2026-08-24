using LoomKit.Jobs.Contracts;

namespace LoomKit.Jobs.Tests.Fixtures;

// Every test that exercises the scheduler needs the same start/run/stop scaffolding - this
// centralizes it so a change to teardown (e.g. a timeout) doesn't need to be repeated per test.
internal static class SchedulerTestHarness
{
    public static async Task RunAsync(IJobScheduler scheduler, Func<Task> body)
    {
        await scheduler.StartAsync(CancellationToken.None);

        try
        {
            await body();
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }
}
