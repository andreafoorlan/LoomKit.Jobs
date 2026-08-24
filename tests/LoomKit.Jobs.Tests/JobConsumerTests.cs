using System.Diagnostics;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Events;
using LoomKit.Jobs.Extensions;
using LoomKit.Jobs.Middlewares;
using LoomKit.Jobs.Models;
using LoomKit.Jobs.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace LoomKit.Jobs.Tests;

public class JobConsumerTests
{
    [Fact]
    public async Task ScheduleNowAsync_PingJob_ExecutesHandler()
    {
        using var provider = TestServiceProviderFactory.Build();
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        await SchedulerTestHarness.RunAsync(scheduler, async () =>
        {
            var job = new PingJob();
            await scheduler.ScheduleNowAsync(TestServiceProviderFactory.QueueName, job);

            Assert.True(await Wait.UntilAsync(() => job.Trace.Count > 0, TimeSpan.FromSeconds(2)));
            Assert.Equal(["handler"], job.Trace);
        });
    }

    [Fact]
    public async Task ScheduleNowAsync_ExecutesMiddlewarePipeline_InRegistrationOrder()
    {
        using var provider = TestServiceProviderFactory.Build(c => c
            .UseJobMiddleware(typeof(FirstMiddleware<,>))
            .UseJobMiddleware(typeof(SecondMiddleware<,>)));
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        await SchedulerTestHarness.RunAsync(scheduler, async () =>
        {
            var job = new PingJob();
            await scheduler.ScheduleNowAsync(TestServiceProviderFactory.QueueName, job);

            Assert.True(await Wait.UntilAsync(() => job.Trace.Count >= 5, TimeSpan.FromSeconds(2)));
            Assert.Equal(
                ["first:before", "second:before", "handler", "second:after", "first:after"],
                job.Trace);
        });
    }

    [Fact]
    public async Task ScheduleNowAsync_TraceJob_WritesTypedResponse_OntoJobStatus()
    {
        // regression test for the discarded-response bug the IJob<TJobStatus> redesign fixes:
        // the response used to be computed by the pipeline and then silently thrown away
        using var provider = TestServiceProviderFactory.Build();
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        await SchedulerTestHarness.RunAsync(scheduler, async () =>
        {
            var job = new TraceJob();
            JobEndedEventArgs? ended = null;
            scheduler.JobEnded += (_, e) => { if (ReferenceEquals(e.JobSchedule.Job, job)) ended = e; };

            await scheduler.ScheduleNowAsync(TestServiceProviderFactory.QueueName, job);

            Assert.True(await Wait.UntilAsync(() => ended is not null, TimeSpan.FromSeconds(2)));

            var typedStatus = Assert.IsType<JobStatus<List<string>>>(ended!.JobSchedule.JobStatus);
            Assert.Same(job.Trace, typedStatus.JobResponse);
            Assert.Equal(["handler"], typedStatus.JobResponse);
        });
    }

    [Fact]
    public async Task ScheduleNowAsync_ReusesCachedDispatchPlan_AcrossMultipleIndependentCalls()
    {
        using var provider = TestServiceProviderFactory.Build(c => c.UseJobMiddleware(typeof(FirstMiddleware<,>)));
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        await SchedulerTestHarness.RunAsync(scheduler, async () =>
        {
            var first = new PingJob();
            var second = new PingJob();

            await scheduler.ScheduleNowAsync(TestServiceProviderFactory.QueueName, first);
            await scheduler.ScheduleNowAsync(TestServiceProviderFactory.QueueName, second);

            Assert.True(await Wait.UntilAsync(() => first.Trace.Count > 0 && second.Trace.Count > 0, TimeSpan.FromSeconds(2)));

            Assert.Equal(["first:before", "handler", "first:after"], first.Trace);
            Assert.Equal(["first:before", "handler", "first:after"], second.Trace);
        });
    }

    [Fact]
    public async Task RetryingJob_WithJobRetryMiddleware_ReEnqueuesAndEventuallySucceeds()
    {
        using var provider = TestServiceProviderFactory.Build(c => c.UseJobMiddleware(typeof(JobRetryMiddleware<,>)));
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        await SchedulerTestHarness.RunAsync(scheduler, async () =>
        {
            var job = new RetryingJob { FailUntilAttempt = 3 };
            await scheduler.ScheduleNowAsync(TestServiceProviderFactory.QueueName, job);

            Assert.True(await Wait.UntilAsync(() => job.Trace.Count >= 3, TimeSpan.FromSeconds(3)));
            Assert.Equal(["attempt:1", "attempt:2", "attempt:3"], job.Trace);
        });
    }

    [Fact]
    public async Task StopAsync_ReturnsPromptly_EvenWithLongJobAwaitCheckInterval()
    {
        // regression test: InProcessJobQueue.AwaitForJobScheduleAsync used to not pass the
        // cancellation token into its polling Task.Delay, so shutdown had to wait out a full
        // JobAwaitCheckInterval before observing cancellation. Doesn't use SchedulerTestHarness
        // since StopAsync's timing is exactly what's under test here.
        using var provider = TestServiceProviderFactory.Build(configureQueue: q => q.JobAwaitCheckInterval = 5000);
        var scheduler = provider.GetRequiredService<IJobScheduler>();
        await scheduler.StartAsync(CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await scheduler.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"StopAsync took {stopwatch.Elapsed}, expected well under the 5s JobAwaitCheckInterval");
    }
}
