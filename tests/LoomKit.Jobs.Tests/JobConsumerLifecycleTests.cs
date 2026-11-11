using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Extensions;
using LoomKit.Jobs.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoomKit.Jobs.Tests;

public class JobConsumerLifecycleTests
{
    [Fact]
    public async Task ScopedConsumer_DisposesScopeAsynchronously()
    {
        var disposalLog = new DisposalLog();

        using var provider = TestServiceProviderFactory.Build(
            configureConsumer: c => c.UseScopedServiceProvider = true,
            configureServices: services =>
            {
                services.AddSingleton(disposalLog);
                services.AddScoped<AsyncOnlyDisposable>();
            });
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        await SchedulerTestHarness.RunAsync(scheduler, async () =>
        {
            var job = new ScopedDependencyJob();

            await scheduler.ScheduleNowAsync(TestServiceProviderFactory.QueueName, job);

            var handled = await Wait.UntilAsync(() => job.Handled, TimeSpan.FromSeconds(2));
            var disposed = await Wait.UntilAsync(() => disposalLog.AsyncDisposals == 1, TimeSpan.FromSeconds(2));

            Assert.True(handled);
            Assert.True(disposed, "The scoped service that only implements IAsyncDisposable was never disposed.");
        });
    }

    [Fact]
    public async Task StopAsync_WhileWaitingForJobs_DoesNotLogErrors()
    {
        var logs = new CapturingLoggerProvider();

        using var provider = TestServiceProviderFactory.BuildWithQueue<CancellationThrowingJobQueue>(
            services => services.AddLogging(builder => builder.AddProvider(logs)));
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        await scheduler.StartAsync(CancellationToken.None);

        // give the consumer's background loop time to reach the wait
        await Task.Delay(200);

        await scheduler.StopAsync(CancellationToken.None);

        var errors = logs.Entries.Where(e => e.Level >= LogLevel.Error).ToList();

        Assert.True(errors.Count == 0, "Stopping the consumer logged errors: " + string.Join(" | ", errors.Select(e => e.Message)));
    }
}
