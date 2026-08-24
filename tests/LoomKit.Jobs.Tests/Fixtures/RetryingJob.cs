using System.Collections.Concurrent;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Tests.Fixtures;

public sealed class RetryingJob : IJob<JobStatus>, ITraceable
{
    public List<string> Trace { get; } = [];

    public required int FailUntilAttempt { get; init; }
}

// Fails until the FailUntilAttempt-th attempt, then succeeds - lets tests exercise
// JobRetryMiddleware's re-enqueue-and-eventually-succeed path deterministically. Attempts are
// tracked by JobScheduleId, which JobRetryMiddleware's re-enqueue preserves across retries.
public sealed class RetryingJobHandler : IJobHandler<RetryingJob, JobStatus>
{
    private static readonly ConcurrentDictionary<string, int> _attempts = new();

    public Task HandleAsync(RetryingJob job, JobSchedule jobSchedule, JobStatus jobStatus, CancellationToken cancellationToken = default)
    {
        var attempt = _attempts.AddOrUpdate(jobSchedule.JobScheduleId, 1, static (_, count) => count + 1);

        job.Trace.Add($"attempt:{attempt}");

        if (attempt < job.FailUntilAttempt)
            throw new InvalidOperationException($"Simulated failure on attempt {attempt}");

        return Task.CompletedTask;
    }
}
