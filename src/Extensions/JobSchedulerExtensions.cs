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
        // get job queue options
        var jobQueueOptions = await jobScheduler.GetJobQueueOptionsAsync(queueName, cancellationToken);

        // build job schedule - a fresh, correctly-typed JobStatus for this job's TJobStatus,
        // resolved reflectively since this method only knows TJob at compile time (see
        // JobStatusTypeResolver for why: keeping ergonomics at ScheduleNowAsync<TJob>(...) instead
        // of requiring callers to also spell out TJobStatus)
        var jobSchedule = new JobSchedule()
        {
            JobScheduleId = jobScheduleId ?? Guid.NewGuid().ToString(),
            JobGroupId = jobGroupId ?? Guid.NewGuid().ToString(),
            Job = job,
            JobStatus = JobStatusTypeResolver.CreateJobStatus(typeof(TJob)),
            NextAt = DateTime.UtcNow,
            RetriesLeft = jobQueueOptions?.MaxJobRetries ?? throw new InvalidDataException(nameof(jobQueueOptions))
        };

        // enqueue
        await jobScheduler.EnqueueJobScheduleAsync(queueName, jobSchedule, cancellationToken);
    }

    public static async Task ScheduleAtAsync<TJob>(this IJobScheduler jobScheduler, string queueName, DateTime at, TJob job, string? jobScheduleId = null, string? jobGroupId = null, CancellationToken cancellationToken = default)
        where TJob : IJob
    {
        // get job queue options
        var jobQueueOptions = await jobScheduler.GetJobQueueOptionsAsync(queueName, cancellationToken);

        // build job schedule
        var jobSchedule = new JobSchedule()
        {
            JobScheduleId = jobScheduleId ?? Guid.NewGuid().ToString(),
            JobGroupId = jobGroupId ?? Guid.NewGuid().ToString(),
            Job = job,
            JobStatus = JobStatusTypeResolver.CreateJobStatus(typeof(TJob)),
            NextAt = at,
            RetriesLeft = jobQueueOptions?.MaxJobRetries ?? throw new InvalidDataException(nameof(jobQueueOptions))
        };

        // enqueue
        await jobScheduler.EnqueueJobScheduleAsync(queueName, jobSchedule, cancellationToken);
    }

    public static async Task ScheduleCronAsync<TJob>(this IJobScheduler jobScheduler, string queueName, string cronExpression, DateTime? cronStartAt, DateTime? cronEndAt, TJob job, string? jobScheduleId = null, string? jobGroupId = null, CancellationToken cancellationToken = default)
        where TJob : IJob
    {
        // get job queue options
        var jobQueueOptions = await jobScheduler.GetJobQueueOptionsAsync(queueName, cancellationToken);

        // calculate next at
        var calculatedNextAt = CalculateCronNextAt(cronExpression, cronStartAt, cronEndAt);

        if (calculatedNextAt is null)
            return;

        // build job schedule
        var cronJobSchedule = new CronJobSchedule()
        {
            JobScheduleId = jobScheduleId ?? Guid.NewGuid().ToString(),
            JobGroupId = jobGroupId ?? Guid.NewGuid().ToString(),
            Job = job,
            JobStatus = JobStatusTypeResolver.CreateJobStatus(typeof(TJob)),
            CronExpression = cronExpression,
            CronStartAt = cronStartAt,
            CronEndAt = cronEndAt,
            NextAt = calculatedNextAt.Value!,
            RetriesLeft = jobQueueOptions?.MaxJobRetries ?? throw new InvalidDataException(nameof(jobQueueOptions))
        };

        // enqueue
        await jobScheduler.EnqueueJobScheduleAsync(queueName, cronJobSchedule, cancellationToken);
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
