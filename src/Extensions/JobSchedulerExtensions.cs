using Cronos;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Internal;
using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Extensions;

public static class JobSchedulerExtensions
{
    public static async Task ScheduleNowAsync<TJob>(this IJobScheduler jobScheduler, string queueName, TJob job, string? jobScheduleId = null, string? jobGroupId = null, CancellationToken cancellationToken = default)
        where TJob : IJob
    {
        var (id, groupId, jobStatus, retriesLeft) = await PrepareJobScheduleAsync<TJob>(jobScheduler, queueName, jobScheduleId, jobGroupId, cancellationToken);

        // build job schedule
        var jobSchedule = new JobSchedule()
        {
            JobScheduleId = id,
            JobGroupId = groupId,
            Job = job,
            JobStatus = jobStatus,
            NextAt = DateTime.UtcNow,
            RetriesLeft = retriesLeft
        };

        // enqueue
        await jobScheduler.EnqueueJobScheduleAsync(queueName, jobSchedule, cancellationToken);
    }

    public static async Task ScheduleAtAsync<TJob>(this IJobScheduler jobScheduler, string queueName, DateTime at, TJob job, string? jobScheduleId = null, string? jobGroupId = null, CancellationToken cancellationToken = default)
        where TJob : IJob
    {
        var (id, groupId, jobStatus, retriesLeft) = await PrepareJobScheduleAsync<TJob>(jobScheduler, queueName, jobScheduleId, jobGroupId, cancellationToken);

        // build job schedule
        var jobSchedule = new JobSchedule()
        {
            JobScheduleId = id,
            JobGroupId = groupId,
            Job = job,
            JobStatus = jobStatus,
            NextAt = at,
            RetriesLeft = retriesLeft
        };

        // enqueue
        await jobScheduler.EnqueueJobScheduleAsync(queueName, jobSchedule, cancellationToken);
    }

    public static async Task ScheduleCronAsync<TJob>(this IJobScheduler jobScheduler, string queueName, string cronExpression, DateTime? cronStartAt, DateTime? cronEndAt, TJob job, string? jobScheduleId = null, string? jobGroupId = null, CancellationToken cancellationToken = default)
        where TJob : IJob
    {
        // calculate next at first - pure/sync, no reason to prepare a schedule (which does I/O)
        // for an expression/window that yields no next occurrence
        var calculatedNextAt = CalculateCronNextAt(cronExpression, cronStartAt, cronEndAt);

        if (calculatedNextAt is null)
            return;

        var (id, groupId, jobStatus, retriesLeft) = await PrepareJobScheduleAsync<TJob>(jobScheduler, queueName, jobScheduleId, jobGroupId, cancellationToken);

        // build job schedule
        var cronJobSchedule = new CronJobSchedule()
        {
            JobScheduleId = id,
            JobGroupId = groupId,
            Job = job,
            JobStatus = jobStatus,
            CronExpression = cronExpression,
            CronStartAt = cronStartAt,
            CronEndAt = cronEndAt,
            NextAt = calculatedNextAt.Value,
            RetriesLeft = retriesLeft
        };

        // enqueue
        await jobScheduler.EnqueueJobScheduleAsync(queueName, cronJobSchedule, cancellationToken);
    }

    // Shared plumbing for every Schedule*Async overload above: resolve the queue's retry budget,
    // fall back to a fresh id/group id when the caller didn't supply one, and build the job's
    // JobStatus instance with its correct concrete type (see JobStatusTypeResolver).
    private static async Task<(string JobScheduleId, string JobGroupId, JobStatus JobStatus, int RetriesLeft)> PrepareJobScheduleAsync<TJob>(IJobScheduler jobScheduler, string queueName, string? jobScheduleId, string? jobGroupId, CancellationToken cancellationToken)
        where TJob : IJob
    {
        // GetJobQueueOptionsAsync never returns without a value - it throws if the queue doesn't exist
        var jobQueueOptions = await jobScheduler.GetJobQueueOptionsAsync(queueName, cancellationToken);

        return (
            jobScheduleId ?? Guid.NewGuid().ToString(),
            jobGroupId ?? Guid.NewGuid().ToString(),
            JobStatusTypeResolver.CreateJobStatus(typeof(TJob)),
            jobQueueOptions.MaxJobRetries);
    }

    private static DateTime? CalculateCronNextAt(string cronExpression, DateTime? cronStartAt, DateTime? cronEndAt)
    {
        // init
        DateTime? cronNextAt = default;

        // try parse cron expression to get nextAt
        if (!Cronos.CronExpression.TryParse(cronExpression, CronFormat.IncludeSeconds, out var cronExpressionInstance))
        {
            //
            throw new InvalidDataException($"Cron expression '{cronExpression}' is not valid");
        }

        // if startAt is in the future, use it
        // if startAt is in the past, use now
        // if startAt is null, use now
        var startAt = cronStartAt switch
        {
            DateTime dt when dt > DateTime.UtcNow => dt,
            _ => DateTime.UtcNow.ToUniversalTime(),
        };

        //
        cronNextAt = cronExpressionInstance.GetNextOccurrence(startAt.ToUniversalTime(), true);

        //
        if (cronNextAt is null)
        {
            //
            return null;
        }

        // check if next occurrent is before end date (if any)
        if (cronEndAt is not null && cronNextAt > cronEndAt)
        {
            return null;
        }

        //
        return cronNextAt;
    }
}
